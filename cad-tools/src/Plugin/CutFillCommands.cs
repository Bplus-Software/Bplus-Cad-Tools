using System;
using System.Collections.Generic;
using System.Globalization;
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
        sealed class Elev { public Func<double, double, double> Z; public bool HasBounds; public double MinX, MinY, MaxX, MaxY; public string Note; }

        static string SurfaceName(Document doc, ObjectId id)
        {
            try { using (var t = doc.Database.TransactionManager.StartTransaction()) { var o = t.GetObject(id, OpenMode.ForRead); var p = o.GetType().GetProperty("Name"); var n = p == null ? null : p.GetValue(o, null) as string; t.Commit(); if (!string.IsNullOrEmpty(n)) return n; } } catch { }
            return "surface";
        }

        static bool IsSurface(ObjectId id) { return (id.ObjectClass.DxfName ?? "").IndexOf("SURFACE", StringComparison.OrdinalIgnoreCase) >= 0; }

        static Elev CivilElev(Transaction tr, ObjectId id, string name)
        {
            var o = tr.GetObject(id, OpenMode.ForRead); var m = o.GetType().GetMethod("FindElevationAtXY", new[] { typeof(double), typeof(double) });
            if (m == null) return null;
            var ext = ((Entity)o).GeometricExtents; var args = new object[2];
            return new Elev
            {
                HasBounds = true, MinX = ext.MinPoint.X, MinY = ext.MinPoint.Y, MaxX = ext.MaxPoint.X, MaxY = ext.MaxPoint.Y,
                Z = (x, y) => { try { args[0] = x; args[1] = y; return Convert.ToDouble(m.Invoke(o, args)); } catch { return double.NaN; } },
                Note = name + ": Civil 3D surface, read through its elevation method (not tested on a real surface)."
            };
        }

        static Elev TrisElev(Transaction tr, Editor ed, ObjectId[] ids, string name)
        {
            var pts = new List<double[]>(); var tris = new List<Tri>();
            foreach (var id in ids)
            {
                var e = tr.GetObject(id, OpenMode.ForRead); var dp = e as DBPoint; var fc = e as Face; var p3 = e as Polyline3d;
                if (dp != null) pts.Add(new[] { dp.Position.X, dp.Position.Y, dp.Position.Z });
                else if (fc != null)
                {
                    var v = new List<Point3d>(); for (short i = 0; i < 4; i++) v.Add(fc.GetVertexAt(i));
                    tris.Add(new Tri(v[0].X, v[0].Y, v[0].Z, v[1].X, v[1].Y, v[1].Z, v[2].X, v[2].Y, v[2].Z));
                    if (v[3].DistanceTo(v[2]) > 1e-9) tris.Add(new Tri(v[0].X, v[0].Y, v[0].Z, v[2].X, v[2].Y, v[2].Z, v[3].X, v[3].Y, v[3].Z));
                }
                else if (p3 != null) foreach (ObjectId vid in p3) { var pv = (PolylineVertex3d)tr.GetObject(vid, OpenMode.ForRead); pts.Add(new[] { pv.Position.X, pv.Position.Y, pv.Position.Z }); }
            }
            if (pts.Count > MaxDelaunayPoints) { ed.WriteMessage("\n[B+] " + pts.Count + Ar.T(" points is too many for the built-in triangulation (limit ") + MaxDelaunayPoints + Ar.T("). Use 3D faces or a smaller selection.\n")); return null; }
            if (pts.Count >= 3) tris.AddRange(Sinks.Delaunay(pts));
            if (tris.Count == 0) { ed.WriteMessage("\n[B+] " + name + Ar.T(": need at least 3 points or some 3D faces.\n")); return null; }
            var s = new TriSurface(tris);
            return new Elev { Z = s.Z, HasBounds = true, MinX = s.MinX, MinY = s.MinY, MaxX = s.MaxX, MaxY = s.MaxY, Note = name + ": " + pts.Count + " point(s), " + tris.Count + " triangle(s). Long thin triangles at the edge of a point cloud can create false slopes." };
        }

        [CommandMethod("BPCUTFILL", CommandFlags.UsePickSet)]
        public static void CutFillCmd()
        {
            Guard("BPCUTFILL", () =>
            {
                var doc = Doc; var ed = doc.Editor; var pre = Sel.Peek(ed);
                var inv = CultureInfo.InvariantCulture; var none = new ObjectId[0];
                ObjectId exSurf = ObjectId.Null, cmpSurf = ObjectId.Null, polyId = ObjectId.Null; ObjectId[] exPts = none, cmpPts = none;
                var status = new Dictionary<string, string>(); int startEx = 0; var ptTypes = new[] { "POINT", "3DFACE", "POLYLINE" };
                // objects selected before the command fill in the existing ground
                var preSurf = pre.FirstOrDefault(i => !i.IsNull && IsSurface(i));
                if (!preSurf.IsNull) { exSurf = preSurf; status["ex_surf"] = SurfaceName(doc, exSurf) + "  (selected)"; }
                else { var pp0 = pre.Where(i => !i.IsNull && ptTypes.Contains((i.ObjectClass.DxfName ?? "").ToUpperInvariant())).ToArray(); if (pp0.Length > 0) { exPts = pp0; status["ex_pts"] = pp0.Length + " object(s) selected"; startEx = 1; } }
                Func<string, string> select = key =>
                {
                    if (key == "ex_surf" || key == "cmp_surf")
                    {
                        var peo = new PromptEntityOptions(Ar.T("\nPick the Civil 3D surface: ")); var per = ed.GetEntity(peo); if (per.Status != PromptStatus.OK) return null;
                        if (!IsSurface(per.ObjectId)) { ed.WriteMessage(Ar.T("\n[B+] That is not a Civil 3D surface. Surfaces exist only in Civil 3D: start Civil 3D (not plain AutoCAD) for this.\n")); return null; }
                        if (key == "ex_surf") exSurf = per.ObjectId; else cmpSurf = per.ObjectId;
                        return SurfaceName(doc, per.ObjectId) + "  (selected)";
                    }
                    if (key == "poly")
                    {
                        var peo = new PromptEntityOptions(Ar.T("\nPick the closed polyline that limits the area: ")); peo.SetRejectMessage(Ar.T("\nOnly a polyline.")); peo.AddAllowedClass(typeof(AcPoly), true);
                        var per = ed.GetEntity(peo); if (per.Status != PromptStatus.OK) return null;
                        using (var tr0 = doc.Database.TransactionManager.StartTransaction()) { var pl0 = (AcPoly)tr0.GetObject(per.ObjectId, OpenMode.ForRead); if (!pl0.Closed) { ed.WriteMessage(Ar.T("\n[B+] The polyline must be closed.\n")); return null; } polyId = per.ObjectId; return Ar.T("1 polyline (") + pl0.Area.ToString("N1", inv) + " m\u00B2)"; }
                    }
                    var psr = Sel.Get(ed, new PromptSelectionOptions { MessageForAdding = Ar.T("\nSelect points, 3D faces and 3D polylines") }, Ar.T("POINT,3DFACE,POLYLINE"), none); if (psr.Status != PromptStatus.OK) return null;
                    var ids = psr.Value.GetObjectIds(); if (key == "ex_pts") exPts = ids; else cmpPts = ids; return ids.Length + " object(s) selected";
                };
                var ch = CutFillUi.Ask(select, status, startEx); if (ch == null) return;
                CutFillResult res; string info, note; Elev ex, pr = null;
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    // existing ground
                    if (ch.Existing == 0)
                    {
                        ex = CivilElev(tr, exSurf, Ar.T("Existing ground")); if (ex == null) { ed.WriteMessage(Ar.T("\n[B+] This object has no elevation method. Surfaces work only in Civil 3D, not in plain AutoCAD.\n")); return; }
                    }
                    else { ex = TrisElev(tr, ed, exPts, "Existing ground"); if (ex == null) return; }
                    // comparison
                    string what;
                    if (ch.Proposed == 0) { pr = CivilElev(tr, cmpSurf, Ar.T("Compared with")); if (pr == null) { ed.WriteMessage(Ar.T("\n[B+] No elevation method on that surface. Surfaces work only in Civil 3D.\n")); return; } what = Ar.T("another surface"); }
                    else if (ch.Proposed == 1) { pr = TrisElev(tr, ed, cmpPts, "Compared with"); if (pr == null) return; what = "selected points / faces"; }
                    else if (ch.Proposed == 2) { double lz = ch.Level; pr = new Elev { Z = (x, y) => lz, Note = "Compared with: flat level " + lz.ToString("F3", inv) + " m." }; what = "flat level " + lz.ToString("F3", inv) + " m"; }
                    else
                    {
                        var p1 = ed.GetPoint(Ar.T("\nPoint on the plane: ")); if (p1.Status != PromptStatus.OK) return;
                        var o2 = new PromptPointOptions(Ar.T("\nPoint in the uphill direction: ")) { UseBasePoint = true, BasePoint = p1.Value }; var p2 = ed.GetPoint(o2); if (p2.Status != PromptStatus.OK) return;
                        double dx = p2.Value.X - p1.Value.X, dy = p2.Value.Y - p1.Value.Y, L = Math.Sqrt(dx * dx + dy * dy); if (L < 1e-9) { ed.WriteMessage(Ar.T("\n[B+] The two points are the same.\n")); return; }
                        var po = new PromptDoubleOptions(Ar.T("\nLevel (Z) of the plane at the first point: ")) { AllowNone = false }; var pz = ed.GetDouble(po); if (pz.Status != PromptStatus.OK) return;
                        double x0 = p1.Value.X, y0 = p1.Value.Y, z0 = pz.Value, g = ch.Grade / 100.0, ux = dx / L, uy = dy / L;
                        pr = new Elev { Z = (x, y) => z0 + g * ((x - x0) * ux + (y - y0) * uy), Note = "Compared with: plane, level " + z0.ToString("F3", inv) + " m at the first point, grade " + ch.Grade.ToString("0.###", inv) + " % uphill." };
                        what = "plane at " + ch.Grade.ToString("0.###", inv) + " %";
                    }
                    // area
                    double minx = ex.MinX, miny = ex.MinY, maxx = ex.MaxX, maxy = ex.MaxY; Func<double, double, bool> inside = null; string areaText;
                    if (pr.HasBounds) { minx = Math.Max(minx, pr.MinX); miny = Math.Max(miny, pr.MinY); maxx = Math.Min(maxx, pr.MaxX); maxy = Math.Min(maxy, pr.MaxY); }
                    if (ch.Area == 1)
                    {
                        var pl = (AcPoly)tr.GetObject(polyId, OpenMode.ForRead); if (!pl.Closed) { ed.WriteMessage(Ar.T("\n[B+] The polyline must be closed.\n")); return; }
                        var poly = new List<double[]>(); int n = pl.NumberOfVertices;
                        for (int i = 0; i < n; i++)
                        {
                            var v = pl.GetPoint2dAt(i); poly.Add(new[] { v.X, v.Y });
                            if (Math.Abs(pl.GetBulgeAt(i)) > 1e-9) for (int k = 1; k < 16; k++) { var q = pl.GetPointAtParameter(i + k / 16.0); poly.Add(new[] { q.X, q.Y }); }
                        }
                        var be = pl.GeometricExtents; minx = Math.Max(minx, be.MinPoint.X); miny = Math.Max(miny, be.MinPoint.Y); maxx = Math.Min(maxx, be.MaxPoint.X); maxy = Math.Min(maxy, be.MaxPoint.Y);
                        inside = (x, y) => CutFill.InPolygon(poly, x, y); areaText = "inside the picked polyline (" + pl.Area.ToString("N1", inv) + " m\u00B2)";
                    }
                    else areaText = "where the two overlap";
                    tr.Commit();
                    if (maxx <= minx || maxy <= miny) { ed.WriteMessage(Ar.T("\n[B+] The two surfaces and the area do not overlap.\n")); return; }
                    ed.WriteMessage(Ar.T("\n[B+] Calculating cut and fill...\n"));
                    res = CutFill.Compute(ex.Z, pr.Z, minx, miny, maxx, maxy, ch.Cell, inside, ch.Swell);
                    info = "Compared with " + what + ". Area: " + areaText + ". Grid " + res.Cell.ToString("0.###", inv) + " m, " + res.UsedCells + " cells used.";
                    note = ex.Note + " " + pr.Note;
                    if (res.Cell > ch.Cell + 1e-9) note += " Grid cell was enlarged to " + res.Cell.ToString("0.###", inv) + " m to keep it fast.";
                    if (res.UsedCells == 0) { System.Windows.Forms.MessageBox.Show(Ar.T("No place was found where both have an elevation. Check the area and the surfaces."), Ar.T("B+ CAD Tools")); return; }
                    note += " Cell-centre method: the result changes a little with the cell size and may differ slightly from Civil 3D volume reports.";
                }
                ed.WriteMessage(Ar.T("\n[B+] Cut ") + res.CutVolume.ToString("N1", inv) + Ar.T(" m3, fill ") + res.FillVolume.ToString("N1", inv) + Ar.T(" m3, net ") + res.Net.ToString("N1", inv) + " m3 (" + (res.Net >= 0 ? Ar.T("cut") : Ar.T("fill")) + ").\n");
                if (!CutFillUi.Result(res, info, note)) return;
                var pp = ed.GetPoint(Ar.T("\nPick the corner of the table: ")); if (pp.Status != PromptStatus.OK) return;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                    var lay = SurveyCad.EnsureLayer(tr, doc.Database, "BP-CUTFILL", 3);
                    string F(double v, int d) { return v.ToString("N" + d, inv); }
                    string text = "CUT AND FILL\\P" +
                        "Cut (excavation): " + F(res.CutVolume, 1) + " m\u00B3   area " + F(res.CutArea, 1) + " m\u00B2\\P" +
                        "Fill (embankment): " + F(res.FillVolume, 1) + " m\u00B3   area " + F(res.FillArea, 1) + " m\u00B2\\P" +
                        "Net: " + F(Math.Abs(res.Net), 1) + " m\u00B3 " + (res.Net >= 0 ? "cut (spoil)" : "fill needed") + (Math.Abs(res.Swell - 1) > 1e-9 ? "   (cut x " + res.Swell.ToString("0.00", inv) + ")" : "") + "\\P" +
                        "Grid " + res.Cell.ToString("0.###", inv) + " m";
                    var mt = new MText { Contents = text, Location = pp.Value, TextHeight = Config.TextHeight, LayerId = lay }; mt.SetDatabaseDefaults(doc.Database); mt.Contents = text; mt.Location = pp.Value; mt.TextHeight = Config.TextHeight; mt.LayerId = lay;
                    ms.AppendEntity(mt); tr.AddNewlyCreatedDBObject(mt, true); tr.Commit();
                }
                ed.WriteMessage(Ar.T("\n[B+] Table placed on layer BP-CUTFILL. One UNDO removes it.\n"));
            });
        }
    }
}
