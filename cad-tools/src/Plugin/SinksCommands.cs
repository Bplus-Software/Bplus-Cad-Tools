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
        const int CivilMaxCells = 120000, MaxDelaunayPoints = 8000;

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

        static Grid CivilGrid(Transaction tr, ObjectId id, double cell, Editor ed, out double used, out string err)
        {
            err = null; used = cell;
            var o = tr.GetObject(id, OpenMode.ForRead); var m = o.GetType().GetMethod("FindElevationAtXY", new[] { typeof(double), typeof(double) });
            if (m == null) { err = "This object has no elevation method (" + o.GetType().Name + "). Civil 3D is needed for surfaces."; return null; }
            var ext = ((Entity)o).GeometricExtents; double minx = ext.MinPoint.X, miny = ext.MinPoint.Y, maxx = ext.MaxPoint.X, maxy = ext.MaxPoint.Y;
            while ((long)((maxx - minx) / cell + 2) * (long)((maxy - miny) / cell + 2) > CivilMaxCells) cell *= 1.5;
            used = cell;
            var g = new Grid { X0 = minx, Y0 = miny, Cell = cell, NX = (int)Math.Ceiling((maxx - minx) / cell) + 1, NY = (int)Math.Ceiling((maxy - miny) / cell) + 1 }; g.Z = new double[g.NX * g.NY];
            ed.WriteMessage("\n[B+] Sampling the surface: " + g.NX + " x " + g.NY + " cells of " + cell.ToString("0.###", CultureInfo.InvariantCulture) + " m. This can take a while...\n");
            int ok = 0, tried = 0, bad = 0; var args = new object[2]; var clock = System.Diagnostics.Stopwatch.StartNew(); long lastMsg = 0;
            for (int j = 0; j < g.NY; j++) for (int i = 0; i < g.NX; i++)
                {
                    // Stay responsive: Esc cancels, 90 seconds is the limit, progress is printed every 5 seconds, and a surface that fails nearly everywhere stops early.
                    if ((++tried & 255) == 0)
                    {
                        System.Windows.Forms.Application.DoEvents();
                        if ((GetAsyncKeyState(0x1B) & 0x8000) != 0) { err = "Cancelled (Esc)."; return null; }
                        if (clock.ElapsedMilliseconds > 90000) { err = "Stopped after 90 seconds: reading this surface is too slow. Use a larger grid cell."; return null; }
                        if (clock.ElapsedMilliseconds - lastMsg > 5000) { lastMsg = clock.ElapsedMilliseconds; ed.WriteMessage("\n[B+] Sampling... " + (100L * tried / ((long)g.NX * g.NY)) + "% (Esc cancels)\n"); }
                        if (tried >= 5000 && ok * 20 < tried) { err = "The surface gave an elevation at fewer than 5% of the sampled places. Check the surface or use a larger grid cell."; return null; }
                    }
                    double z = double.NaN;
                    try { args[0] = g.CX(i); args[1] = g.CY(j); z = Convert.ToDouble(m.Invoke(o, args)); ok++; } catch { }
                    g.Z[j * g.NX + i] = z;
                }
            if (ok == 0) { err = "No elevation could be read from the surface (empty, or outside its border)."; return null; }
            return g;
        }

        [CommandMethod("BPSINKS", CommandFlags.UsePickSet)]
        public static void FindSinks()
        {
            Guard("BPSINKS", () =>
            {
                var doc = Doc; var ed = doc.Editor; var pre = Sel.Peek(ed); var ch = SinksUi.Ask(); if (ch == null) return;
                Grid g = null; double used = ch.Cell; string err = null, note = "";
                if (ch.Source == 0)
                {
                    ObjectId sid = pre.FirstOrDefault(i => !i.IsNull && (i.ObjectClass.DxfName ?? "").IndexOf("SURFACE", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (sid.IsNull) { var peo = new PromptEntityOptions("\nPick the Civil 3D surface: "); var per = ed.GetEntity(peo); if (per.Status != PromptStatus.OK) return; sid = per.ObjectId; } else ed.WriteMessage("\n[B+] Using the selected surface.\n");
                    string dxf = sid.ObjectClass.DxfName ?? "";
                    if (dxf.IndexOf("SURFACE", StringComparison.OrdinalIgnoreCase) < 0) { ed.WriteMessage("\n[B+] That is not a Civil 3D surface (" + dxf + ").\n"); return; }
                    using (var tr = doc.Database.TransactionManager.StartTransaction()) { g = CivilGrid(tr, sid, ch.Cell, ed, out used, out err); tr.Commit(); }
                    note = "Civil 3D surface read through its elevation method (not tested on a real surface: check a few values).";
                }
                else
                {
                    var flt = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "POINT,3DFACE,POLYLINE") }); ObjectId[] ids;
                    if (ch.Source == 1) { var psr = Sel.Get(ed, new PromptSelectionOptions { MessageForAdding = "\nSelect points, 3D faces and 3D polylines" }, "POINT,3DFACE,POLYLINE", pre); if (psr.Status != PromptStatus.OK) return; ids = psr.Value.GetObjectIds(); }
                    else { var all = ed.SelectAll(flt); if (all.Status != PromptStatus.OK) { ed.WriteMessage("\n[B+] Nothing found.\n"); return; } ids = all.Value.GetObjectIds(); }
                    var pts = new List<double[]>(); var tris = new List<Tri>();
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (var id in ids)
                        {
                            var e = tr.GetObject(id, OpenMode.ForRead);
                            var dp = e as DBPoint; var fc = e as Face; var p3 = e as Polyline3d;
                            if (dp != null) pts.Add(new[] { dp.Position.X, dp.Position.Y, dp.Position.Z });
                            else if (fc != null)
                            {
                                var v = new List<Point3d>(); for (short i = 0; i < 4; i++) v.Add(fc.GetVertexAt(i));
                                tris.Add(new Tri(v[0].X, v[0].Y, v[0].Z, v[1].X, v[1].Y, v[1].Z, v[2].X, v[2].Y, v[2].Z));
                                if (v[3].DistanceTo(v[2]) > 1e-9) tris.Add(new Tri(v[0].X, v[0].Y, v[0].Z, v[2].X, v[2].Y, v[2].Z, v[3].X, v[3].Y, v[3].Z));
                            }
                            else if (p3 != null) foreach (ObjectId vid in p3) { var pv = (PolylineVertex3d)tr.GetObject(vid, OpenMode.ForRead); pts.Add(new[] { pv.Position.X, pv.Position.Y, pv.Position.Z }); }
                        }
                        tr.Commit();
                    }
                    if (pts.Count > MaxDelaunayPoints) { ed.WriteMessage("\n[B+] " + pts.Count + " points is too many for the built-in triangulation (limit " + MaxDelaunayPoints + "). Use 3D faces or a smaller selection.\n"); return; }
                    if (pts.Count >= 3) tris.AddRange(Sinks.Delaunay(pts));
                    if (tris.Count == 0) { ed.WriteMessage("\n[B+] Need at least 3 points or some 3D faces.\n"); return; }
                    g = Sinks.Rasterize(tris, ch.Cell, out used);
                    note = "Built from " + pts.Count + " point(s) and " + tris.Count + " triangle(s). Long thin triangles at the edge of a point cloud can create false slopes.";
                }
                if (g == null) { ed.WriteMessage("\n[B+] " + err + "\n"); return; }
                if (used > ch.Cell + 1e-9) note += " Grid cell was enlarged to " + used.ToString("0.###", CultureInfo.InvariantCulture) + " m to keep it fast.";
                int ignored; var sinks = Sinks.Analyze(g, ch.MinDepth, ch.MinArea, out ignored);
                ed.WriteMessage("\n[B+] Sinks: " + sinks.Count + " found, " + ignored + " ignored as too small or shallow.\n");
                if (sinks.Count == 0) { System.Windows.Forms.MessageBox.Show("No sinks found with these limits (" + ignored + " smaller ones ignored).", "B+ CAD Tools"); return; }
                if (!SinksUi.Result(sinks, ignored, note)) return;
                int pn = 0, ln = 0, on = 0, lown = 0, arn = 0; double th = Config.TextHeight;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                    var layLow = ch.Low ? SurveyCad.EnsureLayer(tr, doc.Database, "BP-SINK-LOW", 1) : ObjectId.Null; var layDir = ch.Dir ? SurveyCad.EnsureLayer(tr, doc.Database, "BP-SINK-DIR", 3) : ObjectId.Null; var lay = SurveyCad.EnsureLayer(tr, doc.Database, "BP-SINK", 5); var layO = SurveyCad.EnsureLayer(tr, doc.Database, "BP-SINK-OUT", 4);
                    foreach (var s in sinks)
                    {
                        var at = new Point3d(s.E, s.N, s.LowZ);
                        if (ch.Low)
                        {
                            double r = Math.Max(th, Math.Min(th * 2, Math.Sqrt(s.Area) / 6));
                            foreach (var seg in new[] { new[] { -r, 0, r, 0 }, new[] { 0, -r, 0, r } }) { var ln2 = new Line(new Point3d(at.X + seg[0], at.Y + seg[1], at.Z), new Point3d(at.X + seg[2], at.Y + seg[3], at.Z)); ln2.SetDatabaseDefaults(doc.Database); ln2.LayerId = layLow; ms.AppendEntity(ln2); tr.AddNewlyCreatedDBObject(ln2, true); }
                            var zt = new DBText(); zt.SetDatabaseDefaults(doc.Database); zt.TextString = "Z " + s.LowZ.ToString("F3", CultureInfo.InvariantCulture); zt.Height = th; zt.Position = new Point3d(at.X + r * 1.3, at.Y - th * 0.5, at.Z); zt.LayerId = layLow; ms.AppendEntity(zt); tr.AddNewlyCreatedDBObject(zt, true); lown++;
                        }
                        if (ch.Point) { var dp = new DBPoint(at) { LayerId = lay }; ms.AppendEntity(dp); tr.AddNewlyCreatedDBObject(dp, true); pn++; }
                        if (ch.Leader)
                        {
                            var inv = CultureInfo.InvariantCulture; string text = "S" + s.No + "\\PE " + s.E.ToString("F3", inv) + "  N " + s.N.ToString("F3", inv) + "\\PZ " + s.LowZ.ToString("F3", inv) + "  depth " + s.Depth.ToString("F3", inv) + " m";
                            var ml = new MLeader(); ml.SetDatabaseDefaults(doc.Database); ml.ContentType = ContentType.MTextContent;
                            var mt = new MText { Contents = text, TextHeight = th }; mt.SetDatabaseDefaults(doc.Database); ml.MText = mt;
                            int li = ml.AddLeader(); int lnn = ml.AddLeaderLine(li); ml.AddFirstVertex(lnn, at); ml.AddLastVertex(lnn, new Point3d(at.X + th * 3, at.Y + th * 3, at.Z));
                            ml.TextHeight = th; ml.ArrowSize = th * 0.8; ml.LayerId = lay; ms.AppendEntity(ml); tr.AddNewlyCreatedDBObject(ml, true); ln++;
                        }
                        if (ch.Outline)
                            foreach (var loop in s.Outline)
                            {
                                var pl = new AcPoly(); pl.SetDatabaseDefaults(doc.Database);
                                for (int i = 0; i < loop.Count; i++) pl.AddVertexAt(i, new Point2d(loop[i][0], loop[i][1]), 0, 0, 0);
                                pl.Closed = true; pl.Elevation = s.SpillZ; pl.LayerId = layO; ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true); on++;
                            }
                    }
                    if (ch.Dir)
                    {
                        var fa = FlowMap.Arrows(g, sinks, ch.Spacing); double sp2 = ch.Spacing > 0 ? ch.Spacing : Math.Max(g.Cell, Math.Max(g.NX, g.NY) / 40 * g.Cell);
                        foreach (var ar in fa)
                        {
                            double len = Math.Max(th * 1.5, sp2 * 0.6), hd = len * 0.25, a = ar[2];
                            double x0 = ar[0] - Math.Cos(a) * len / 2, y0 = ar[1] - Math.Sin(a) * len / 2, x1 = ar[0] + Math.Cos(a) * len / 2, y1 = ar[1] + Math.Sin(a) * len / 2;
                            var pl2 = new AcPoly(); pl2.SetDatabaseDefaults(doc.Database); int vi = 0;
                            pl2.AddVertexAt(vi++, new Point2d(x0, y0), 0, 0, 0); pl2.AddVertexAt(vi++, new Point2d(x1, y1), 0, 0, 0);
                            pl2.AddVertexAt(vi++, new Point2d(x1 - Math.Cos(a - 0.5) * hd, y1 - Math.Sin(a - 0.5) * hd), 0, 0, 0); pl2.AddVertexAt(vi++, new Point2d(x1, y1), 0, 0, 0); pl2.AddVertexAt(vi++, new Point2d(x1 - Math.Cos(a + 0.5) * hd, y1 - Math.Sin(a + 0.5) * hd), 0, 0, 0);
                            pl2.Elevation = ar[3]; pl2.LayerId = layDir; ms.AppendEntity(pl2); tr.AddNewlyCreatedDBObject(pl2, true); arn++;
                        }
                    }
                    tr.Commit();
                }
                ed.WriteMessage("\n[B+] Low points: " + lown + ", slope arrows: " + arn + ".\n[B+] Marked " + sinks.Count + " sinks: " + pn + " point(s) on BP-SINK, " + ln + " leader(s), " + on + " outline(s) on BP-SINK-OUT. One UNDO removes them. Outlines are cell-size staircases.\n");
            });
        }
    }
}
