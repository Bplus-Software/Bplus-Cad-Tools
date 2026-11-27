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
        static string M3(double v) { return v.ToString("N3", CultureInfo.InvariantCulture); }

        // Pick points one after another with a rubber band. Enter finishes. Returns null if cancelled.
        static List<double[]> PickChain(Editor ed, string first, string next, int min, string doneWord)
        {
            var pts = new List<double[]>();
            while (true)
            {
                var o = new PromptPointOptions("\n" + (pts.Count == 0 ? first : next + Ar.T(" [Undo] (Enter = ") + doneWord + "): ")) { AllowNone = true };
                o.Keywords.Add(Ar.T("Undo")); o.AppendKeywordsToMessage = false;
                if (pts.Count > 0) { o.UseBasePoint = true; o.BasePoint = new Point3d(pts[pts.Count - 1][0], pts[pts.Count - 1][1], pts[pts.Count - 1][2]); o.UseDashedLine = true; }
                var r = ed.GetPoint(o);
                if (r.Status == PromptStatus.Keyword) { if (pts.Count > 0) pts.RemoveAt(pts.Count - 1); continue; }
                if (r.Status == PromptStatus.None) { if (pts.Count >= min) return pts; ed.WriteMessage(Ar.T("\n[B+] Need at least ") + min + Ar.T(" points.\n")); continue; }
                if (r.Status != PromptStatus.OK) return null;
                pts.Add(new[] { r.Value.X, r.Value.Y, r.Value.Z });
            }
        }

        static void WriteMeasure(Document doc, Action<Transaction, BlockTableRecord, ObjectId> draw)
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                var lay = SurveyCad.EnsureLayer(tr, doc.Database, "BP-MEASURE", 2); draw(tr, ms, lay); tr.Commit();
            }
        }
        static void AddText(Transaction tr, BlockTableRecord ms, ObjectId lay, Database db, string text, Point3d at)
        {
            var mt = new MText { Contents = text, Location = at, TextHeight = Config.TextHeight, LayerId = lay }; mt.SetDatabaseDefaults(db); mt.Contents = text; mt.Location = at; mt.TextHeight = Config.TextHeight; mt.LayerId = lay;
            ms.AppendEntity(mt); tr.AddNewlyCreatedDBObject(mt, true);
        }
        static AcPoly MakePoly(Database db, IList<double[]> p, bool closed, ObjectId lay)
        {
            var pl = new AcPoly(); pl.SetDatabaseDefaults(db); for (int i = 0; i < p.Count; i++) pl.AddVertexAt(i, new Point2d(p[i][0], p[i][1]), 0, 0, 0); pl.Closed = closed; pl.LayerId = lay; return pl;
        }

        [CommandMethod("BPCHAIN")]
        public static void ChainLength()
        {
            Guard("BPCHAIN", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var p = PickChain(ed, "Start point of the line to measure: ", "Next point", 2, "finish"); if (p == null) return;
                double h = MeasureMath.Length2D(p), sl = MeasureMath.Length3D(p);
                var rows = new List<string[]> { new[] { "Points", p.Count.ToString() }, new[] { "Total length (horizontal)", M3(h) + " m" } };
                if (Math.Abs(sl - h) > 1e-6) rows.Add(new[] { "Total length (slope, with Z)", M3(sl) + " m" });
                rows.Add(new[] { "Segments (m)", string.Join("   ", Enumerable.Range(0, p.Count - 1).Select(i => M3(Geo2D.Distance(p[i][0], p[i][1], p[i + 1][0], p[i + 1][1]))).Take(12)) + (p.Count - 1 > 12 ? "   ..." : "") });
                var a = p[p.Count - 2]; var b = p[p.Count - 1]; double az = Geo2D.Azimuth(a[0], a[1], b[0], b[1]);
                rows.Add(new[] { "Last segment", "azimuth " + Geo2D.Dms(az) + "   bearing " + Geo2D.QuadrantBearing(az) });
                rows.Add(new[] { "Straight line first to last", M3(Geo2D.Distance(p[0][0], p[0][1], b[0], b[1])) + " m" });
                ed.WriteMessage(Ar.T("\n[B+] Total length ") + M3(h) + " m.\n");
                if (MeasureUi.Show("Chain length", rows, "Writing in the drawing adds a polyline and the total on layer BP-MEASURE."))
                    WriteMeasure(doc, (tr, ms, lay) => { var pl = MakePoly(doc.Database, p, false, lay); ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true); AddText(tr, ms, lay, doc.Database, M3(h) + " m", new Point3d(b[0], b[1], b[2])); });
            });
        }

        [CommandMethod("BPAREAPTS")]
        public static void AreaByPoints()
        {
            Guard("BPAREAPTS", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var p = PickChain(ed, "First corner of the area: ", "Next corner", 3, "close the area"); if (p == null) return;
                var e = p.Select(q => q[0]).ToList(); var n = p.Select(q => q[1]).ToList(); double a = Geo2D.PolygonArea(e, n), per = Geo2D.Perimeter(e, n, true);
                bool cross = false; // simple check: any two non-adjacent edges cross
                for (int i = 0; i < p.Count && !cross; i++) for (int j = i + 2; j < p.Count && !cross; j++) { if (i == 0 && j == p.Count - 1) continue; cross = Cross(p[i], p[(i + 1) % p.Count], p[j], p[(j + 1) % p.Count]); }
                var rows = new List<string[]> { new[] { "Corners", p.Count.ToString() }, new[] { "Area", M3(a) + " m\u00B2" }, new[] { "Perimeter", M3(per) + " m" } };
                ed.WriteMessage(Ar.T("\n[B+] Area ") + M3(a) + Ar.T(" m2, perimeter ") + M3(per) + " m.\n");
                if (MeasureUi.Show("Area by points", rows, cross ? "Warning: the outline crosses itself, so the area is not what you expect." : "Writing in the drawing adds a closed polyline and the area on layer BP-MEASURE."))
                    WriteMeasure(doc, (tr, ms, lay) => { var pl = MakePoly(doc.Database, p, true, lay); ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true); var c = MeasureMath.Centroid(p); AddText(tr, ms, lay, doc.Database, M3(a) + " m\u00B2", new Point3d(c[0], c[1], 0)); });
            });
        }
        static bool Cross(double[] a, double[] b, double[] c, double[] d)
        {
            Func<double[], double[], double[], double> o = (x, y, z) => (y[0] - x[0]) * (z[1] - x[1]) - (y[1] - x[1]) * (z[0] - x[0]);
            double d1 = o(a, b, c), d2 = o(a, b, d), d3 = o(c, d, a), d4 = o(c, d, b); return d1 * d2 < 0 && d3 * d4 < 0;
        }

        [CommandMethod("BPANGLE")]
        public static void AngleCmd()
        {
            Guard("BPANGLE", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var a = ed.GetPoint(Ar.T("\nFirst point (end of the first line): ")); if (a.Status != PromptStatus.OK) return;
                var bo = new PromptPointOptions(Ar.T("\nCorner point (where the angle is): ")) { UseBasePoint = true, BasePoint = a.Value, UseDashedLine = true }; var b = ed.GetPoint(bo); if (b.Status != PromptStatus.OK) return;
                var co = new PromptPointOptions(Ar.T("\nLast point (end of the second line): ")) { UseBasePoint = true, BasePoint = b.Value, UseDashedLine = true }; var c = ed.GetPoint(co); if (c.Status != PromptStatus.OK) return;
                double ang = MeasureMath.AngleAt(a.Value.X, a.Value.Y, b.Value.X, b.Value.Y, c.Value.X, c.Value.Y);
                double azBA = Geo2D.Azimuth(b.Value.X, b.Value.Y, a.Value.X, a.Value.Y), azBC = Geo2D.Azimuth(b.Value.X, b.Value.Y, c.Value.X, c.Value.Y);
                var rows = new List<string[]> { new[] { "Angle at the corner", Geo2D.Dms(ang) + "   (" + ang.ToString("F4", CultureInfo.InvariantCulture) + "\u00B0)" }, new[] { "Other side", Geo2D.Dms(360 - ang) },
                    new[] { "Corner to first point", "azimuth " + Geo2D.Dms(azBA) + "   " + Geo2D.QuadrantBearing(azBA) + "   " + M3(Geo2D.Distance(b.Value.X, b.Value.Y, a.Value.X, a.Value.Y)) + " m" },
                    new[] { "Corner to last point", "azimuth " + Geo2D.Dms(azBC) + "   " + Geo2D.QuadrantBearing(azBC) + "   " + M3(Geo2D.Distance(b.Value.X, b.Value.Y, c.Value.X, c.Value.Y)) + " m" } };
                ed.WriteMessage(Ar.T("\n[B+] Angle ") + Geo2D.Dms(ang) + ".\n");
                if (MeasureUi.Show("Angle", rows, "Writing in the drawing adds the angle as text at the corner (layer BP-MEASURE)."))
                    WriteMeasure(doc, (tr, ms, lay) => AddText(tr, ms, lay, doc.Database, Geo2D.Dms(ang), b.Value));
            });
        }

        [CommandMethod("BPPICK")]
        public static void PickCoords()
        {
            Guard("BPPICK", () =>
            {
                var doc = Doc; var ed = doc.Editor; var pts = new List<double[]>();
                var cs = CoordSys.FromEpsg(DrawingCs.LoadEpsg(doc.Database)); if (cs != null) cs.UnitToMetre = DrawingCs.UnitToMetre(doc.Database);
                var p = PickChain(ed, "Pick a point: ", "Pick the next point", 1, "finish"); if (p == null) return;
                var rows = new List<string[]>(); int k = 0; var inv = CultureInfo.InvariantCulture;
                foreach (var q in p)
                {
                    k++; string ll = ""; if (cs != null) { try { double lo, la; cs.ToLonLat(q[0], q[1], out lo, out la); ll = "    " + la.ToString("F6", inv) + " N  " + lo.ToString("F6", inv) + " E"; } catch { } }
                    rows.Add(new[] { "Point " + k, "E " + M3(q[0]) + "   N " + M3(q[1]) + "   Z " + M3(q[2]) + ll });
                    if (k >= 15) { rows.Add(new[] { "...", (p.Count - 15) + " more (Copy has the first 15 only)" }); break; }
                }
                string note = cs == null ? "No coordinate system is set for this drawing, so no latitude and longitude. Set one with BPGMAPCS." : "Latitude and longitude from EPSG:" + cs.Epsg + " " + cs.Name + ".";
                if (MeasureUi.Show("Pick coordinates", rows, note))
                    WriteMeasure(doc, (tr, ms, lay) => { foreach (var q in p) AddText(tr, ms, lay, doc.Database, "E " + M3(q[0]) + "\\PN " + M3(q[1]), new Point3d(q[0], q[1], q[2])); });
            });
        }
    }
}
