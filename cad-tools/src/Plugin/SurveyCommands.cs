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
        public static void ImportAny()
        {
            Guard("BPIMPORT", () =>
            {
                switch (FormatPicker.Ask(true))
                {
                    case 0: case 1: ImportPts(); break;
                    case 2: SdrImp(); break;
                    case 3: GsiImp(); break;
                    case 4: KmlImport(); break;
                }
            });
        }

        static void ImportPts()
        {
            Guard("BPIMPORT", () =>
            {
                var doc = Doc; var c = SurveyUi.Import(); if (c == null || c.Result == null) return;
                int n = SurveyCad.Import(doc, c.Result.Points, SurveyCad.LoadRules(), c.Labels, c.TextH, true);
                doc.Editor.WriteMessage(Ar.T("\n[BPlus] Imported ") + n + Ar.T(" points") + (c.Result.Problems.Count > 0 ? Ar.T(", skipped ") + c.Result.Problems.Count + Ar.T(" bad lines") : "") + Ar.T(". Use ZOOM EXTENTS to see them.\n"));
            });
        }

        [CommandMethod("BPEXPORT")]
        public static void ExportAny()
        {
            Guard("BPEXPORT", () =>
            {
                switch (FormatPicker.Ask(false))
                {
                    case 0: ExportPts(); break;
                    case 1: ExportXlsx(); break;
                    case 2: SdrExp(); break;
                    case 3: GsiExp(); break;
                    case 4: KmlExport(); break;
                }
            });
        }

        static void ExportXlsx()
        {
            var doc = Doc; var pts = SurveyCad.Collect(doc).Select(c => c.P).ToList();
            if (pts.Count == 0) { doc.Editor.WriteMessage(Ar.T("\n[B+] No points found in model space.\n")); return; }
            string path; using (var d = new System.Windows.Forms.SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "points.xlsx", Title = "Export points to Excel" }) { if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return; path = d.FileName; }
            using (var fs = File.Create(path)) XlsxWriter.WritePoints(fs, pts);
            doc.Editor.WriteMessage(Ar.T("\n[B+] Excel: exported ") + pts.Count + Ar.T(" point(s) to ") + path + Ar.T(". Not opened in Excel here: tell us if Excel complains.\n"));
        }

        static void ExportPts()
        {
            Guard("BPEXPORT", () =>
            {
                var doc = Doc; var pts = SurveyCad.Collect(doc);
                if (pts.Count == 0) { doc.Editor.WriteMessage(Ar.T("\n[BPlus] No points found in model space.\n")); return; }
                var cats = pts.GroupBy(p => p.P.Category).OrderBy(g => g.Key).Select(g => new KeyValuePair<string, int>(g.Key, g.Count())).ToList();
                var c = SurveyUi.Export(cats); if (c == null) return;
                var sel = pts.Where(p => c.Cats.Contains(p.P.Category)).Select(p => p.P).ToList();
                File.WriteAllText(c.Path, PointText.Format(sel, c.Order, c.Delim, c.Decimals, c.Header), new UTF8Encoding(false));
                doc.Editor.WriteMessage(Ar.T("\n[BPlus] Exported ") + sel.Count + Ar.T(" points to ") + c.Path + "\n");
            });
        }

        [CommandMethod("BPCODES")]
        public static void EditCodes()
        {
            Guard("BPCODES", () => { var t = SurveyUi.Codes(SurveyCad.RulesText()); if (t != null) { SurveyCad.SaveRulesText(t); Doc.Editor.WriteMessage(Ar.T("\n[BPlus] Codes saved to ") + SurveyCad.CodesFile + "\n"); } });
        }

        [CommandMethod("BPISOLATE")]
        public static void Isolate()
        {
            Guard("BPISOLATE", () =>
            {
                var doc = Doc; var layers = SurveyCad.SurveyLayers(doc);
                if (layers.Count == 0) { doc.Editor.WriteMessage(Ar.T("\n[BPlus] No BP-PT layers yet. Import points first.\n")); return; }
                var r = SurveyUi.Isolate(layers, SurveyCad.LayerOnState(doc)); if (r != null) SurveyCad.SetLayers(doc, r);
            });
        }

        [CommandMethod("BPQAQC")]
        public static void Qa()
        {
            Guard("BPQAQC", () =>
            {
                var doc = Doc; var pts = SurveyCad.Collect(doc);
                if (pts.Count == 0) { doc.Editor.WriteMessage(Ar.T("\n[BPlus] No points found in model space.\n")); return; }
                var issues = SurveyQa.Run(pts.Select(p => p.P).ToList(), new QaOptions());
                int sel = -1; var byHandle = pts.Where(p => !string.IsNullOrEmpty(p.P.Ref)).GroupBy(p => p.P.Ref).ToDictionary(g => g.Key, g => g.First().Id);
                try
                {
                    while (true)
                    {
                        int action, row; List<SurveyUi.QaRow> rows;
                        SurveyUi.Qa(issues, pts.Count, sel, out action, out row, out rows);
                        if (action == 0 || row < 0) break;
                        sel = row;
                        if (action == 1) { var q = rows[row].P; SurveyCad.ZoomTo(doc, q.E, q.N, q.HasZ ? q.Z : 0); continue; }
                        var ids = rows.Where(r => r.Issue == rows[row].Issue && r.P.Ref != null && byHandle.ContainsKey(r.P.Ref)).Select(r => byHandle[r.P.Ref]).Distinct().ToArray();
                        if (ids.Length > 0) { doc.Editor.SetImpliedSelection(ids); doc.Editor.WriteMessage("\n[B+] " + ids.Length + Ar.T(" point(s) selected. Press Delete, move them, or Esc.\n")); }
                        else doc.Editor.WriteMessage(Ar.T("\n[B+] These points are not in the drawing as objects, so they cannot be selected.\n"));
                        break;
                    }
                }
                finally { SurveyCad.ClearMark(); }
            });
        }

        [CommandMethod("BPCONNECT", CommandFlags.UsePickSet)]
        public static void ConnectPts()
        {
            Guard("BPCONNECT", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var opt = new PromptSelectionOptions { MessageForAdding = Ar.T("\nSelect points (Enter for all points)") };
                var psr = Sel.Get(ed, opt, "POINT,AECC_COGO_POINT");
                ICollection<ObjectId> only = null;
                if (psr.Status == PromptStatus.OK) only = psr.Value.GetObjectIds();
                else if (psr.Status != PromptStatus.Error) return;
                var pts = SurveyCad.Collect(doc, only);
                if (pts.Count < 2) { ed.WriteMessage(Ar.T("\n[BPlus] Need at least two points.\n")); return; }
                var c = SurveyUi.Connect(); if (c == null) return;
                var codes = c.Only.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToUpperInvariant()).ToList();
                var lines = Connect.Build(pts.Select(p => p.P).ToList(), c.ByCode, c.SplitByNumber, codes.Count > 0 ? codes : null);
                if (lines.Count == 0) { ed.WriteMessage(Ar.T("\n[BPlus] No lines to draw. Points need a code with at least two points.\n")); return; }
                int n = SurveyCad.DrawLines(doc, lines, SurveyCad.LoadRules(), c.ByCode);
                ed.WriteMessage(Ar.T("\n[BPlus] Drew ") + n + Ar.T(" line(s) from ") + lines.Sum(l => l.Pts.Count) + Ar.T(" points.\n"));
            });
        }

        /// <summary>When both points lie on the same line, polyline, arc or spline: the length along it between them. Null if none.</summary>
        static double? AlongCurve(Document doc, Point3d a, Point3d b)
        {
            try
            {
                double view = Convert.ToDouble(AcApp.GetSystemVariable("VIEWSIZE")), tol = Math.Max(view / 150, 1e-6);
                var sel = doc.Editor.SelectCrossingWindow(new Point3d(a.X - tol, a.Y - tol, 0), new Point3d(a.X + tol, a.Y + tol, 0));
                if (sel.Status != PromptStatus.OK) return null;
                using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
                    foreach (var id in sel.Value.GetObjectIds())
                    {
                        var c = tr.GetObject(id, OpenMode.ForRead) as Curve; if (c == null || c is Ray || c is Xline) continue;
                        var pa = c.GetClosestPointTo(a, true); var pb = c.GetClosestPointTo(b, true);
                        if (pa.DistanceTo(new Point3d(a.X, a.Y, pa.Z)) > tol * 1.5 || pb.DistanceTo(new Point3d(b.X, b.Y, pb.Z)) > tol * 1.5) continue;
                        return Math.Abs(c.GetDistAtPoint(pb) - c.GetDistAtPoint(pa));
                    }
            }
            catch (System.Exception ex) { Log.Write("BPDIST along: " + ex.Message); }
            return null;
        }

        [CommandMethod("BPDIST")]
        public static void Dist()
        {
            Guard("BPDIST", () =>
            {
                var ed = Doc.Editor;
                while (true)
                {
                    var a = ed.GetPoint(new PromptPointOptions(Ar.T("\nFirst point (Enter to finish): ")) { AllowNone = true }); if (a.Status != PromptStatus.OK) return;
                    var o = new PromptPointOptions(Ar.T("\nSecond point: ")) { UseBasePoint = true, BasePoint = a.Value, UseDashedLine = true };
                    var b = ed.GetPoint(o); if (b.Status != PromptStatus.OK) return;
                    double h = Geo2D.Distance(a.Value.X, a.Value.Y, b.Value.X, b.Value.Y), dz = b.Value.Z - a.Value.Z, az = Geo2D.Azimuth(a.Value.X, a.Value.Y, b.Value.X, b.Value.Y);
                    double sl = Math.Sqrt(h * h + dz * dz), de = b.Value.X - a.Value.X, dn = b.Value.Y - a.Value.Y, grade = h > 1e-9 ? dz / h * 100 : 0;
                    ed.WriteMessage(Ar.T("\n[B+] Horizontal ") + F2(h) + Ar.T(" m   Slope ") + F2(sl) + " m   dZ " + F2(dz) + Ar.T(" m   Azimuth ") + Geo2D.Dms(az) + Ar.T("   Bearing ") + Geo2D.QuadrantBearing(az) + "\n");
                    var rows = new List<string[]> {
                        new[] { "Horizontal distance", M3(h) + " m" }, new[] { "Slope distance (with Z)", M3(sl) + " m" }, new[] { "Height difference (dZ)", M3(dz) + " m" }, new[] { "Grade", dz == 0 ? "0 %" : grade.ToString("0.###", CultureInfo.InvariantCulture) + " %" },
                        new[] { "Azimuth", Geo2D.Dms(az) }, new[] { "Bearing", Geo2D.QuadrantBearing(az) }, new[] { "dE   dN", M3(de) + "   " + M3(dn) },
                        new[] { "From  E N Z", M3(a.Value.X) + "  " + M3(a.Value.Y) + "  " + M3(a.Value.Z) }, new[] { "To  E N Z", M3(b.Value.X) + "  " + M3(b.Value.Y) + "  " + M3(b.Value.Z) } };
                    var pa = a.Value; var pb = b.Value; var doc2 = Doc; var alo = AlongCurve(doc2, pa, pb);
                    if (alo.HasValue) rows.Insert(1, new[] { "Along the line", M3(alo.Value) + " m" });
                    if (MeasureUi.Show("Distance", rows, "Writing in the drawing adds a line and the horizontal distance on layer BP-MEASURE. Close, then pick the next pair (Enter ends)."))
                        WriteMeasure(doc2, (tr, ms, lay) => { var ln = new Line(pa, pb); ln.SetDatabaseDefaults(doc2.Database); ln.LayerId = lay; ms.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true); AddText(tr, ms, lay, doc2.Database, M3(h) + " m", new Point3d((pa.X + pb.X) / 2, (pa.Y + pb.Y) / 2, 0)); });
                }
            });
        }

        [CommandMethod("BPAREA", CommandFlags.UsePickSet)]
        public static void Area()
        {
            Guard("BPAREA", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var psr = Sel.Get(ed, new PromptSelectionOptions { MessageForAdding = Ar.T("\nSelect closed polylines") }, Ar.T("LWPOLYLINE,POLYLINE"));
                if (psr.Status != PromptStatus.OK) return;
                try { var iu = Convert.ToInt32(AcApp.GetSystemVariable(Ar.T("INSUNITS"))); if (iu != 6 && iu != 0) ed.WriteMessage(Ar.T("\n[B+] Note: this drawing's units (INSUNITS=") + iu + Ar.T(") are not metres. Results below are in drawing units, shown as m and m2.")); } catch { }
                var kw = new PromptKeywordOptions(Ar.T("\nPlace an area label for each? [Yes/No] <No>: "), Ar.T("Yes No")) { AllowNone = true }; var kr = ed.GetKeywords(kw);
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
                        ed.WriteMessage("\n" + F2(a) + Ar.T(" m2   perimeter ") + F2(per) + " m");
                        if (label)
                        {
                            Point3d c; try { var ext = pl.GeometricExtents; c = new Point3d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2, 0); } catch { c = Point3d.Origin; }
                            double h = Math.Max(0.1, Math.Min(pl.GeometricExtents.MaxPoint.X - pl.GeometricExtents.MinPoint.X, pl.GeometricExtents.MaxPoint.Y - pl.GeometricExtents.MinPoint.Y) / 20);
                            var mt = new MText { Contents = F2(a) + " m2", Location = c, TextHeight = h, Attachment = AttachmentPoint.MiddleCenter, LayerId = pl.LayerId };
                            ms.AppendEntity(mt); tr.AddNewlyCreatedDBObject(mt, true);
                        }
                    }
                    tr.Commit();
                }
                ed.WriteMessage("\n" + n + Ar.T(" polyline(s), total ") + F2(total) + " m2" + (skipped > 0 ? "   (" + skipped + Ar.T(" skipped: open or not a polyline)") : "") + "\n");
            });
        }

        [CommandMethod("BPSHIFT", CommandFlags.UsePickSet)]
        public static void Shift()
        {
            Guard("BPSHIFT", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var psr = Sel.Get(ed, new PromptSelectionOptions { MessageForAdding = Ar.T("\nSelect objects to shift") }, null); if (psr.Status != PromptStatus.OK) return;
                int cnt = psr.Value.Count; bool pick; double[] d;
                if (!ShiftUi.Ask(cnt, out pick, out d)) return;
                if (pick)
                {
                    var pa = ed.GetPoint(new PromptPointOptions(Ar.T("\nPick point A (from): "))); if (pa.Status != PromptStatus.OK) return;
                    var pbo = new PromptPointOptions(Ar.T("\nPick point B (to): ")) { UseBasePoint = true, BasePoint = pa.Value, UseDashedLine = true };
                    var pb = ed.GetPoint(pbo); if (pb.Status != PromptStatus.OK) return;
                    var A = new[] { pa.Value.X, pa.Value.Y, pa.Value.Z }; var B = new[] { pb.Value.X, pb.Value.Y, pb.Value.Z };
                    if (!ShiftUi.Confirm(A, B, cnt)) return;
                    d = new[] { B[0] - A[0], B[1] - A[1], B[2] - A[2] };
                }
                if (d[0] == 0 && d[1] == 0 && d[2] == 0) return;
                var m = Matrix3d.Displacement(new Vector3d(d[0], d[1], d[2])); int n = 0;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (var id in psr.Value.GetObjectIds()) { var en = tr.GetObject(id, OpenMode.ForWrite) as Entity; if (en != null) { en.TransformBy(m); n++; } }
                    tr.Commit();
                }
                ed.WriteMessage(Ar.T("\n[BPlus] Shifted ") + n + Ar.T(" object(s) by ") + F2(d[0]) + ", " + F2(d[1]) + ", " + F2(d[2]) + Ar.T(". UNDO reverses it.\n"));
            });
        }
    }
}

namespace BPlus.LiveMap
{
    public partial class Commands
    {
        [CommandMethod("BPSETTINGS")] public static void GeneralSet() { Guard("BPSETTINGS", GeneralSettings.Show); }
        [CommandMethod("BPUPDATE", CommandFlags.Session)] public static void Update() { Guard("BPUPDATE", UpdateCheck.Manual); }
        [CommandMethod("BPABOUT")]
        public static void About()
        {
            Guard("BPABOUT", () => System.Windows.Forms.MessageBox.Show(Ar.T("B+ CAD Tools ") + App.Version + Ar.T("\nExperimental pre-release for AutoCAD and Civil 3D 2021 to 2026.\nUpdates: github.com/Bplus-Software/Bplus-Cad-Tools\nSettings and log: ") + Paths.Dir, Ar.T("About B+ CAD Tools"), System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information));
        }
        static void SetKind(MapKind k, string name) { Guard("BPMAP", () => { Config.Kind = k; Engine.RefreshNow(); Doc.Editor.WriteMessage(Ar.T("\n[B+] Map type: ") + name + (Engine.On ? "" : Ar.T(". The map is off: click Live map to show it.")) + "\n"); }); }
        static void SetOpacity(int p) { Guard("BPMAP", () => { Config.OpacityPercent = p; Engine.RefreshNow(); Doc.Editor.WriteMessage(Ar.T("\n[B+] Map opacity: ") + p + "%\n"); }); }
        [CommandMethod("BPMAPSAT")] public static void MapSat() { SetKind(MapKind.Satellite, "Satellite"); }
        [CommandMethod("BPMAPROAD")] public static void MapRoad() { SetKind(MapKind.Roadmap, "Roadmap"); }
        [CommandMethod("BPMAPHYB")] public static void MapHyb() { SetKind(MapKind.Hybrid, "Hybrid"); }
        [CommandMethod("BPMAPOP100")] public static void Op100() { SetOpacity(100); }
        [CommandMethod("BPMAPOP75")] public static void Op75() { SetOpacity(75); }
        [CommandMethod("BPMAPOP50")] public static void Op50() { SetOpacity(50); }
        [CommandMethod("BPMAPOP25")] public static void Op25() { SetOpacity(25); }
    }
}
