using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using BPlus.LiveMap.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BPlus.LiveMap
{
    public partial class Commands
    {
        static string F2(double v) { return v.ToString("F3", CultureInfo.InvariantCulture); }

        [CommandMethod("BPIMPORT")]
        public static void ImportPts()
        {
            Guard("BPIMPORT", () =>
            {
                var doc = Doc; var c = SurveyUi.Import(); if (c == null || c.Result == null) return;
                int n = SurveyCad.Import(doc, c.Result.Points, SurveyCad.LoadRules(), c.Labels, c.TextH, true);
                doc.Editor.WriteMessage("\n[BPlus] Imported " + n + " points" + (c.Result.Problems.Count > 0 ? ", skipped " + c.Result.Problems.Count + " bad lines" : "") + ". Use ZOOM EXTENTS to see them.\n");
            });
        }

        [CommandMethod("BPEXPORT")]
        public static void ExportPts()
        {
            Guard("BPEXPORT", () =>
            {
                var doc = Doc; var pts = SurveyCad.Collect(doc);
                if (pts.Count == 0) { doc.Editor.WriteMessage("\n[BPlus] No points found in model space.\n"); return; }
                var cats = pts.GroupBy(p => p.P.Category).OrderBy(g => g.Key).Select(g => new KeyValuePair<string, int>(g.Key, g.Count())).ToList();
                var c = SurveyUi.Export(cats); if (c == null) return;
                var sel = pts.Where(p => c.Cats.Contains(p.P.Category)).Select(p => p.P).ToList();
                File.WriteAllText(c.Path, PointText.Format(sel, c.Order, c.Delim, c.Decimals, c.Header), new UTF8Encoding(false));
                doc.Editor.WriteMessage("\n[BPlus] Exported " + sel.Count + " points to " + c.Path + "\n");
            });
        }

        [CommandMethod("BPCODES")]
        public static void EditCodes()
        {
            Guard("BPCODES", () => { var t = SurveyUi.Codes(SurveyCad.RulesText()); if (t != null) { SurveyCad.SaveRulesText(t); Doc.Editor.WriteMessage("\n[BPlus] Codes saved to " + SurveyCad.CodesFile + "\n"); } });
        }

        [CommandMethod("BPISOLATE")]
        public static void Isolate()
        {
            Guard("BPISOLATE", () =>
            {
                var doc = Doc; var layers = SurveyCad.SurveyLayers(doc);
                if (layers.Count == 0) { doc.Editor.WriteMessage("\n[BPlus] No BP-PT layers yet. Import points first.\n"); return; }
                var r = SurveyUi.Isolate(layers, SurveyCad.LayerOnState(doc)); if (r != null) SurveyCad.SetLayers(doc, r);
            });
        }

        [CommandMethod("BPQAQC")]
        public static void Qa()
        {
            Guard("BPQAQC", () =>
            {
                var doc = Doc; var pts = SurveyCad.Collect(doc);
                if (pts.Count == 0) { doc.Editor.WriteMessage("\n[BPlus] No points found in model space.\n"); return; }
                var issues = SurveyQa.Run(pts.Select(p => p.P).ToList(), new QaOptions());
                int sel = -1;
                while (true)
                {
                    int i = SurveyUi.Qa(issues, pts.Count, sel); if (i < 0) break;
                    sel = i; SurveyCad.ZoomTo(doc, issues[i].E, issues[i].N);
                }
            });
        }

        [CommandMethod("BPCONNECT")]
        public static void ConnectPts()
        {
            Guard("BPCONNECT", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var opt = new PromptSelectionOptions { MessageForAdding = "\nSelect points (Enter for all points)" };
                var psr = ed.GetSelection(opt, new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "POINT,AECC_COGO_POINT") }));
                ICollection<ObjectId> only = null;
                if (psr.Status == PromptStatus.OK) only = psr.Value.GetObjectIds();
                else if (psr.Status != PromptStatus.Error) return;
                var pts = SurveyCad.Collect(doc, only);
                if (pts.Count < 2) { ed.WriteMessage("\n[BPlus] Need at least two points.\n"); return; }
                var c = SurveyUi.Connect(); if (c == null) return;
                var codes = c.Only.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToUpperInvariant()).ToList();
                var lines = Connect.Build(pts.Select(p => p.P).ToList(), c.ByCode, c.SplitByNumber, codes.Count > 0 ? codes : null);
                if (lines.Count == 0) { ed.WriteMessage("\n[BPlus] No lines to draw. Points need a code with at least two points.\n"); return; }
                int n = SurveyCad.DrawLines(doc, lines, SurveyCad.LoadRules(), c.ByCode);
                ed.WriteMessage("\n[BPlus] Drew " + n + " line(s) from " + lines.Sum(l => l.Pts.Count) + " points.\n");
            });
        }

        [CommandMethod("BPDIST")]
        public static void Dist()
        {
            Guard("BPDIST", () =>
            {
                var ed = Doc.Editor;
                while (true)
                {
                    var a = ed.GetPoint(new PromptPointOptions("\nFirst point (Enter to finish): ") { AllowNone = true }); if (a.Status != PromptStatus.OK) return;
                    var o = new PromptPointOptions("\nSecond point: ") { UseBasePoint = true, BasePoint = a.Value, UseDashedLine = true };
                    var b = ed.GetPoint(o); if (b.Status != PromptStatus.OK) return;
                    double h = Geo2D.Distance(a.Value.X, a.Value.Y, b.Value.X, b.Value.Y), dz = b.Value.Z - a.Value.Z, az = Geo2D.Azimuth(a.Value.X, a.Value.Y, b.Value.X, b.Value.Y);
                    ed.WriteMessage("\nHorizontal " + F2(h) + "   Slope " + F2(Math.Sqrt(h * h + dz * dz)) + "   dZ " + F2(dz) + "\nAzimuth " + Geo2D.Dms(az) + "   Bearing " + Geo2D.QuadrantBearing(az) + "\n");
                }
            });
        }

        [CommandMethod("BPAREA")]
        public static void Area()
        {
            Guard("BPAREA", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect closed polylines" }, new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE,POLYLINE") }));
                if (psr.Status != PromptStatus.OK) return;
                var kw = new PromptKeywordOptions("\nPlace an area label for each? [Yes/No] <No>: ", "Yes No") { AllowNone = true }; var kr = ed.GetKeywords(kw);
                bool label = kr.Status == PromptStatus.OK && kr.StringResult == "Yes";
                double total = 0; int n = 0, skipped = 0;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                    foreach (var id in psr.Value.GetObjectIds())
                    {
                        var pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                        if (pl == null || !pl.Closed || pl.NumberOfVertices < 3) { skipped++; continue; }
                        bool bulge = false; var e = new List<double>(); var nn = new List<double>();
                        for (int i = 0; i < pl.NumberOfVertices; i++) { var p = pl.GetPoint2dAt(i); e.Add(p.X); nn.Add(p.Y); if (Math.Abs(pl.GetBulgeAt(i)) > 1e-9) bulge = true; }
                        double a = bulge ? pl.Area : Geo2D.PolygonArea(e, nn); double per = bulge ? pl.Length : Geo2D.Perimeter(e, nn, true);
                        total += a; n++;
                        ed.WriteMessage("\n" + F2(a) + " m2   " + Geo2D.Feddan(a) + "   perimeter " + F2(per) + " m");
                        if (label)
                        {
                            Point3d c; try { var ext = pl.GeometricExtents; c = new Point3d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2, 0); } catch { c = Point3d.Origin; }
                            double h = Math.Max(0.1, Math.Min(pl.GeometricExtents.MaxPoint.X - pl.GeometricExtents.MinPoint.X, pl.GeometricExtents.MaxPoint.Y - pl.GeometricExtents.MinPoint.Y) / 20);
                            var mt = new MText { Contents = F2(a) + " m2\\P" + Geo2D.Feddan(a), Location = c, TextHeight = h, Attachment = AttachmentPoint.MiddleCenter, LayerId = pl.LayerId };
                            ms.AppendEntity(mt); tr.AddNewlyCreatedDBObject(mt, true);
                        }
                    }
                    tr.Commit();
                }
                ed.WriteMessage("\n" + n + " polyline(s), total " + F2(total) + " m2 = " + Geo2D.Feddan(total) + (skipped > 0 ? "   (" + skipped + " skipped: open or not a polyline)" : "") + "\n");
            });
        }

        [CommandMethod("BPSHIFT")]
        public static void Shift()
        {
            Guard("BPSHIFT", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect objects to shift" }); if (psr.Status != PromptStatus.OK) return;
                double[] d = new double[3]; string[] names = { "dE (east)", "dN (north)", "dZ (elevation)" };
                for (int i = 0; i < 3; i++)
                {
                    var o = new PromptDoubleOptions("\nShift " + names[i] + " <0>: ") { AllowNone = true, DefaultValue = 0, UseDefaultValue = true }; var r = ed.GetDouble(o);
                    if (r.Status != PromptStatus.OK) return; d[i] = r.Value;
                }
                if (d[0] == 0 && d[1] == 0 && d[2] == 0) return;
                var m = Matrix3d.Displacement(new Vector3d(d[0], d[1], d[2])); int n = 0;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (var id in psr.Value.GetObjectIds()) { var en = tr.GetObject(id, OpenMode.ForWrite) as Entity; if (en != null) { en.TransformBy(m); n++; } }
                    tr.Commit();
                }
                ed.WriteMessage("\n[BPlus] Shifted " + n + " object(s) by " + F2(d[0]) + ", " + F2(d[1]) + ", " + F2(d[2]) + ". UNDO reverses it.\n");
            });
        }
    }
}
