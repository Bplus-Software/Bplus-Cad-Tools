using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using BPlus.LiveMap.Core;
using AcPoly = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace BPlus.LiveMap
{
    public partial class Commands
    {
        // ---------- BPJOIN ----------
        sealed class JSrc { public ObjectId Id; public double Elev; }

        static JPath ToPath(Transaction tr, ObjectId id, int idx, List<JSrc> src)
        {
            var e = tr.GetObject(id, OpenMode.ForRead); var p = new JPath(); p.Src.Add(idx); double elev = 0;
            var ln = e as Line; var ar = e as Arc; var pl = e as AcPoly;
            if (ln != null) { p.V.Add(new JV(ln.StartPoint.X, ln.StartPoint.Y)); p.V.Add(new JV(ln.EndPoint.X, ln.EndPoint.Y)); elev = ln.StartPoint.Z; }
            else if (ar != null)
            {
                if (Math.Abs(ar.Normal.Z - 1) > 1e-9) return null;
                double sw = ar.EndAngle - ar.StartAngle; if (sw <= 0) sw += 2 * Math.PI;
                p.V.Add(new JV(ar.StartPoint.X, ar.StartPoint.Y, Math.Tan(sw / 4))); p.V.Add(new JV(ar.EndPoint.X, ar.EndPoint.Y)); elev = ar.StartPoint.Z;
            }
            else if (pl != null)
            {
                if (pl.Closed || pl.NumberOfVertices < 2 || Math.Abs(pl.Normal.Z - 1) > 1e-9) return null;
                for (int i = 0; i < pl.NumberOfVertices; i++) { var pt = pl.GetPoint2dAt(i); p.V.Add(new JV(pt.X, pt.Y, i < pl.NumberOfVertices - 1 ? pl.GetBulgeAt(i) : 0)); }
                elev = pl.Elevation;
            }
            else return null;
            src.Add(new JSrc { Id = id, Elev = elev }); p.Src[0] = src.Count - 1; return p;
        }

        [CommandMethod("BPJOIN", CommandFlags.UsePickSet)]
        public static void JoinShapes()
        {
            Guard("BPJOIN", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var psr = Sel.Get(ed, new PromptSelectionOptions { MessageForAdding = "\nSelect lines, polylines and arcs to join" }, "LWPOLYLINE,LINE,ARC");
                if (psr.Status != PromptStatus.OK) return;
                var src = new List<JSrc>(); var paths = new List<JPath>(); int skipped = 0;
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (var id in psr.Value.GetObjectIds()) { var p = ToPath(tr, id, 0, src); if (p == null) skipped++; else paths.Add(p); }
                    tr.Commit();
                }
                if (paths.Count < 2) { ed.WriteMessage("\n[B+] Need at least two open shapes to join" + (skipped > 0 ? " (" + skipped + " skipped: closed, 3D or not in the XY plane)" : "") + ".\n"); return; }
                Func<JoinChoice, string[]> prev = c =>
                {
                    var r = JoinLogic.Run(paths, c.Tol, c.Mode); int ch = r.Paths.Count(x => x.Changed);
                    return new[] { paths.Count + " shapes  ->  " + r.Paths.Count + " after joining" + (r.Closed > 0 ? " (" + r.Closed + " closed)" : ""), r.Extended + r.Straight + " gaps will be closed (" + r.Extended + " extended, " + r.Straight + " straight across)", r.LeftOpen > 0 ? r.LeftOpen + " gap(s) just beyond the distance stay open" : "" };
                };
                var ch2 = JoinTrimUi.Join(paths.Count, prev); if (ch2 == null) return;
                var res = JoinLogic.Run(paths, ch2.Tol, ch2.Mode); int made = 0, erased = 0;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                    foreach (var p in res.Paths)
                    {
                        if (!p.Changed) continue;
                        var first = (Entity)tr.GetObject(src[p.Src[0]].Id, OpenMode.ForRead);
                        var np = new AcPoly(); np.SetDatabaseDefaults(doc.Database);
                        for (int i = 0; i < p.V.Count; i++) np.AddVertexAt(i, new Point2d(p.V[i].X, p.V[i].Y), p.V[i].B, 0, 0);
                        np.Closed = p.Closed; np.Elevation = src[p.Src[0]].Elev; np.SetPropertiesFrom(first);
                        ms.AppendEntity(np); tr.AddNewlyCreatedDBObject(np, true); made++;
                        if (!ch2.Keep) foreach (var si in p.Src) { var o = (Entity)tr.GetObject(src[si].Id, OpenMode.ForWrite); o.Erase(); erased++; }
                    }
                    tr.Commit();
                }
                ed.WriteMessage("\n[B+] Join: " + made + " polyline(s) made, " + res.Extended + " gaps extended, " + res.Straight + " straight across, " + res.Closed + " closed, " + erased + " original(s) replaced" + (skipped > 0 ? ", " + skipped + " skipped (closed, 3D or tilted)" : "") + ". UNDO reverses it. Not tested in real AutoCAD yet: check the result before relying on it.\n");
            });
        }

        // ---------- BPTRIM ----------
        sealed class TrimCount { public int Cut, Deleted, Whole, Skipped; }

        static bool Inside(List<double[]> poly, Point3d p) { return Poly2D.Contains(poly, p.X, p.Y); }
        static Point3d MidOf(Curve c) { return c.GetPointAtDist(c.GetDistanceAtParameter(c.EndParam) / 2); }

        static string TrimOne(Transaction tr, ObjectId id, ObjectId bndId, AcPoly bnd, List<double[]> poly, TrimChoice o, bool apply, BlockTableRecord ms, ObjectId trimLay)
        {
            if (id == bndId) return "skip";
            var e = tr.GetObject(id, OpenMode.ForRead) as Entity; if (e == null) return "skip";
            try
            {
                var c = e as Curve;
                if (c != null && (e is Line || e is AcPoly || e is Arc || e is Circle))
                {
                    if (!o.Curves) return "skip";
                    var pts = new Point3dCollection();
#pragma warning disable CS0618
                    c.IntersectWith(bnd, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
#pragma warning restore CS0618
                    var prm = new List<double>();
                    foreach (Point3d p in pts) { try { prm.Add(c.GetParameterAtPoint(c.GetClosestPointTo(p, false))); } catch { } }
                    prm.Sort(); var u = new List<double>();
                    foreach (var v in prm) { if (u.Count == 0 || v - u[u.Count - 1] > 1e-9) u.Add(v); }
                    if (!c.Closed) u.RemoveAll(v => v <= c.StartParam + 1e-9 || v >= c.EndParam - 1e-9);
                    if (c.Closed && u.Count < 2) u.Clear();
                    if (u.Count == 0)
                    {
                        bool inn = Inside(poly, MidOf(c));
                        if (inn == o.KeepInside) return "whole";
                        if (o.KeepInside) return "whole"; // wholly outside the polygon: never touched
                        if (apply) { var w = (Entity)tr.GetObject(id, OpenMode.ForWrite); if (o.KeepRemoved) w.LayerId = trimLay; else w.Erase(); }
                        return "deleted";
                    }
                    var pieces = c.GetSplitCurves(new DoubleCollection(u.ToArray()));
                    var keep = new List<Curve>(); var drop = new List<Curve>();
                    foreach (DBObject d in pieces) { var pc = (Curve)d; try { (Inside(poly, MidOf(pc)) == o.KeepInside ? keep : drop).Add(pc); } catch { drop.Add(pc); } }
                    string outcome = drop.Count == 0 ? "whole" : keep.Count == 0 ? (o.KeepInside ? "whole" : "deleted") : "cut";
                    if (!apply || outcome == "whole") { foreach (DBObject d in pieces) d.Dispose(); return outcome; }
                    var orig = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                    foreach (var k in keep) { k.SetPropertiesFrom(orig); ms.AppendEntity(k); tr.AddNewlyCreatedDBObject(k, true); }
                    foreach (var dr in drop)
                    {
                        if (o.KeepRemoved) { dr.SetPropertiesFrom(orig); dr.LayerId = trimLay; ms.AppendEntity(dr); tr.AddNewlyCreatedDBObject(dr, true); } else dr.Dispose();
                    }
                    orig.Erase(); return outcome;
                }
                Point3d? at = null; bool isPt = e is DBPoint || e is DBText || e is MText, isBlk = e is BlockReference;
                if (e is DBPoint) at = ((DBPoint)e).Position; else if (e is DBText) at = ((DBText)e).Position; else if (e is MText) at = ((MText)e).Location; else if (isBlk) at = ((BlockReference)e).Position;
                if (at == null || (isPt && !o.Points) || (isBlk && !o.Blocks)) return "skip";
                if (Inside(poly, at.Value) == o.KeepInside) return "whole";
                if (o.KeepInside) return "whole"; // outside the polygon: never touched
                if (apply) { var w = (Entity)tr.GetObject(id, OpenMode.ForWrite); if (o.KeepRemoved) w.LayerId = trimLay; else w.Erase(); }
                return "deleted";
            }
            catch { return "skip"; }
        }

        static TrimCount TrimAll(Document doc, ObjectId[] ids, ObjectId bndId, TrimChoice o, bool apply)
        {
            var n = new TrimCount();
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var bnd = (AcPoly)tr.GetObject(bndId, OpenMode.ForRead);
                var jv = new List<JV>(); for (int i = 0; i < bnd.NumberOfVertices; i++) { var p = bnd.GetPoint2dAt(i); jv.Add(new JV(p.X, p.Y, bnd.GetBulgeAt(i))); }
                var poly = Poly2D.Flatten(jv, true);
                BlockTableRecord ms = null; ObjectId lay = ObjectId.Null;
                if (apply)
                {
                    ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                    if (o.KeepRemoved) { lay = SurveyCad.EnsureLayer(tr, doc.Database, "BP-TRIMMED", 1); var l = (LayerTableRecord)tr.GetObject(lay, OpenMode.ForWrite); l.IsOff = true; }
                }
                foreach (var id in ids)
                {
                    string r = TrimOne(tr, id, bndId, bnd, poly, o, apply, ms, lay);
                    if (r == "cut") n.Cut++; else if (r == "deleted") n.Deleted++; else if (r == "whole") n.Whole++; else n.Skipped++;
                }
                if (apply) tr.Commit(); else tr.Abort();
            }
            return n;
        }

        [CommandMethod("BPTRIM", CommandFlags.UsePickSet)]
        public static void TrimToBoundary()
        {
            Guard("BPTRIM", () =>
            {
                var doc = Doc; var ed = doc.Editor; var pre = Sel.Peek(ed);
                var peo = new PromptEntityOptions("\nPick the closed polyline to use as the boundary: "); peo.SetRejectMessage("\nOnly a polyline can be the boundary."); peo.AddAllowedClass(typeof(AcPoly), true);
                var per = ed.GetEntity(peo); if (per.Status != PromptStatus.OK) return;
                double area; Extents3d bx; using (var tr = doc.Database.TransactionManager.StartTransaction()) { var b = (AcPoly)tr.GetObject(per.ObjectId, OpenMode.ForRead); if (!b.Closed) { ed.WriteMessage("\n[B+] The boundary must be a closed polyline.\n"); return; } area = b.Area; bx = b.GeometricExtents; tr.Commit(); }
                var psr = Sel.Get(ed, new PromptSelectionOptions { MessageForAdding = "\nSelect objects to trim (Enter = only the objects crossing or inside the polygon's area)" }, null, pre);
                ObjectId[] ids;
                if (psr.Status == PromptStatus.OK) ids = psr.Value.GetObjectIds();
                else if (psr.Status == PromptStatus.Error) { var all = ed.SelectCrossingWindow(bx.MinPoint, bx.MaxPoint); if (all.Status != PromptStatus.OK) { ed.WriteMessage("\n[B+] No objects in the polygon's area.\n"); return; } ids = all.Value.GetObjectIds(); }
                else return;
                ids = ids.Where(i => i != per.ObjectId).ToArray();
                Func<TrimChoice, string> prev = o => { var c = TrimAll(doc, ids, per.ObjectId, o, false); return c.Cut + " cut, " + c.Deleted + " removed, " + c.Whole + " untouched, " + c.Skipped + " skipped (" + ids.Length + " objects)"; };
                var ch = JoinTrimUi.Trim("Boundary: closed polyline picked (area " + area.ToString("N1", System.Globalization.CultureInfo.InvariantCulture) + " m\u00B2).", ids.Length, prev); if (ch == null) return;
                TrimCount n;
                using (doc.LockDocument()) n = TrimAll(doc, ids, per.ObjectId, ch, true);
                ed.WriteMessage("\n[B+] Trim: " + n.Cut + " cut, " + n.Deleted + " removed" + (ch.KeepRemoved ? " (moved to BP-TRIMMED)" : "") + ", " + n.Whole + " untouched, " + n.Skipped + " skipped (hatches and other types are not trimmed). UNDO reverses it. Not tested in real AutoCAD yet: check the result before relying on it.\n");
            });
        }
    }
}
