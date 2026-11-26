using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
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
        static string N3(double v) { return v.ToString("F3", CultureInfo.InvariantCulture); }
        const string CrvFilter = "LWPOLYLINE,POLYLINE,LINE,ARC,CIRCLE";

        static List<Point3d> Verts(Transaction tr, Entity ent)
        {
            var res = new List<Point3d>();
            var lw = ent as Autodesk.AutoCAD.DatabaseServices.Polyline;
            if (lw != null) { for (int i = 0; i < lw.NumberOfVertices; i++) res.Add(lw.GetPoint3dAt(i)); return res; }
            var p3 = ent as Polyline3d;
            if (p3 != null) { foreach (ObjectId v in p3) { var pv = (PolylineVertex3d)tr.GetObject(v, OpenMode.ForRead); if (pv.VertexType != Vertex3dType.ControlVertex) res.Add(pv.Position); } return res; }
            var p2 = ent as Polyline2d;
            if (p2 != null) { foreach (ObjectId v in p2) { var pv = (Vertex2d)tr.GetObject(v, OpenMode.ForRead); if (pv.VertexType != Vertex2dType.SplineControlVertex) res.Add(new Point3d(pv.Position.X, pv.Position.Y, p2.Elevation)); } return res; }
            var ln = ent as Line; if (ln != null) { res.Add(ln.StartPoint); res.Add(ln.EndPoint); }
            return res;
        }

        static string KindName(Entity e) { if (e is Autodesk.AutoCAD.DatabaseServices.Polyline) return "Polyline"; if (e is Polyline2d) return "2D polyline"; if (e is Polyline3d) return "3D polyline"; if (e is Line) return "Line"; if (e is Arc) return "Arc"; if (e is Circle) return "Circle"; return e.GetType().Name; }

        static List<LenRow> LenRows(Document doc, ObjectId[] ids)
        {
            var rows = new List<LenRow>();
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    var c = tr.GetObject(id, OpenMode.ForRead) as Curve; if (c == null) continue;
                    double len = 0, area = 0; bool closed = c.Closed || c is Circle;
                    try { len = c.GetDistanceAtParameter(c.EndParam) - c.GetDistanceAtParameter(c.StartParam); } catch { }
                    if (closed) { try { area = Math.Abs(c.Area); } catch { closed = false; } }
                    rows.Add(new LenRow { Tag = id, Layer = c.Layer, Kind = KindName(c), Len = len, Area = area, Closed = closed });
                }
                tr.Commit();
            }
            return rows;
        }

        static void ZoomExtent(Document doc, Extents3d x)
        {
            var ed = doc.Editor; var v = ed.GetCurrentView();
            double dx = Math.Max(x.MaxPoint.X - x.MinPoint.X, 1e-6), dy = Math.Max(x.MaxPoint.Y - x.MinPoint.Y, 1e-6), ratio = v.Width / v.Height;
            double h = Math.Max(dy, dx / ratio) * 1.3;
            v.Height = h; v.Width = h * ratio;
            v.CenterPoint = SurveyCad.ToDcs(v, new Point3d((x.MinPoint.X + x.MaxPoint.X) / 2, (x.MinPoint.Y + x.MaxPoint.Y) / 2, (x.MinPoint.Z + x.MaxPoint.Z) / 2));
            ed.SetCurrentView(v);
        }

        [CommandMethod("BPLENGTHS", CommandFlags.UsePickSet)]
        public static void Lengths()
        {
            Guard("BPLENGTHS", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var psr = Sel.Get(ed, new PromptSelectionOptions { MessageForAdding = "\nSelect polylines, lines or arcs" }, CrvFilter);
                if (psr.Status != PromptStatus.OK) return;
                var ids = psr.Value.GetObjectIds(); var rows = LenRows(doc, ids);
                if (rows.Count == 0) { ed.WriteMessage("\n[B+] Nothing measurable was selected.\n"); return; }
                int sel = -1;
                try
                {
                    while (true)
                    {
                        int action, row; ToolsUi.Lengths(rows, sel, out action, out row);
                        if (action == 0) break;
                        if (action == 1 && row >= 0 && row < rows.Count)
                        {
                            sel = row;
                            using (var tr = doc.Database.TransactionManager.StartTransaction()) { var en = (Entity)tr.GetObject((ObjectId)rows[row].Tag, OpenMode.ForRead); ZoomExtent(doc, en.GeometricExtents); tr.Commit(); }
                            doc.Editor.SetImpliedSelection(new[] { (ObjectId)rows[row].Tag });
                            continue;
                        }
                        if (action == 2) { int n = LabelCurves(doc, rows.Select(r => (ObjectId)r.Tag).ToArray(), true, true); ed.WriteMessage("\n[B+] " + n + " label(s) placed on layer BP-LABEL. One UNDO removes them.\n"); }
                        if (action == 3) InsertTable(doc, rows);
                        break;
                    }
                }
                finally { }
            });
        }

        static void InsertTable(Document doc, List<LenRow> rows)
        {
            var ed = doc.Editor; var pr = ed.GetPoint(new PromptPointOptions("\nTable insertion point (top left): ")); if (pr.Status != PromptStatus.OK) return;
            double th = Config.TextHeight; int n = rows.Count;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                var t = new Table(); t.TableStyle = doc.Database.Tablestyle; t.Position = pr.Value;
                t.SetSize(n + 3, 5);
                for (int c = 0; c < 5; c++) t.Columns[c].Width = th * (c == 0 ? 5 : c == 1 ? 18 : 12);
                for (int r = 0; r < n + 3; r++) t.Rows[r].Height = th * 2;
                string[] head = { "No", "Layer", "Length (m)", "Area (m\u00B2)", "Closed" };
                try { t.MergeCells(CellRange.Create(t, 0, 0, 0, 4)); } catch { }
                t.Cells[0, 0].TextString = "Polyline lengths";
                for (int c = 0; c < 5; c++) t.Cells[1, c].TextString = head[c];
                double tl = 0, ta = 0;
                for (int i = 0; i < n; i++)
                {
                    var r = rows[i]; tl += r.Len; if (r.Closed) ta += r.Area;
                    t.Cells[i + 2, 0].TextString = (i + 1).ToString(); t.Cells[i + 2, 1].TextString = r.Layer; t.Cells[i + 2, 2].TextString = N3(r.Len); t.Cells[i + 2, 3].TextString = r.Closed ? N3(r.Area) : ""; t.Cells[i + 2, 4].TextString = r.Closed ? "Yes" : "No";
                }
                t.Cells[n + 2, 1].TextString = "Total"; t.Cells[n + 2, 2].TextString = N3(tl); t.Cells[n + 2, 3].TextString = N3(ta);
                for (int r = 0; r < n + 3; r++) for (int c = 0; c < 5; c++) { try { t.Cells[r, c].TextHeight = th; } catch { } }
                t.GenerateLayout();
                ms.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true); tr.Commit();
            }
            ed.WriteMessage("\n[B+] Table inserted with " + n + " rows. One UNDO removes it.\n");
        }

        static int LabelCurves(Document doc, ObjectId[] ids, bool len, bool area)
        {
            int n = 0; double th = Config.TextHeight;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                var lay = SurveyCad.EnsureLayer(tr, doc.Database, "BP-LABEL", 7);
                foreach (var id in ids)
                {
                    var c = tr.GetObject(id, OpenMode.ForRead) as Curve; if (c == null) continue;
                    double total; try { total = c.GetDistanceAtParameter(c.EndParam) - c.GetDistanceAtParameter(c.StartParam); } catch { continue; }
                    if (len)
                    {
                        Point3d mid; Vector3d tan;
                        try { double d = c.GetDistanceAtParameter(c.StartParam) + total / 2; mid = c.GetPointAtDist(d); tan = c.GetFirstDerivative(mid); } catch { continue; }
                        double ang = Math.Atan2(tan.Y, tan.X); if (Math.Cos(ang) < -1e-9) ang += Math.PI;
                        var off = new Vector3d(-Math.Sin(ang), Math.Cos(ang), 0) * th * 0.7;
                        var t = new DBText { TextString = N3(total) + " m", Height = th, Position = mid + off, Rotation = ang, LayerId = lay };
                        ms.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true); n++;
                    }
                    if (area && (c.Closed || c is Circle))
                    {
                        double a; try { a = Math.Abs(c.Area); } catch { continue; }
                        var ext = c.GeometricExtents;
                        var mt = new MText { Contents = N3(a) + " m\u00B2", Location = new Point3d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2, 0), TextHeight = th, Attachment = AttachmentPoint.MiddleCenter, LayerId = lay };
                        ms.AppendEntity(mt); tr.AddNewlyCreatedDBObject(mt, true); n++;
                    }
                }
                tr.Commit();
            }
            return n;
        }

        [CommandMethod("BPLABEL", CommandFlags.UsePickSet)]
        public static void LabelLen()
        {
            Guard("BPLABEL", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var psr = Sel.Get(ed, new PromptSelectionOptions { MessageForAdding = "\nSelect polylines, lines or arcs to label" }, CrvFilter);
                if (psr.Status != PromptStatus.OK) return;
                var kw = new PromptKeywordOptions("\nLabel [Length/Area/Both] <Both>: ", "Length Area Both") { AllowNone = true }; var kr = ed.GetKeywords(kw);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
                string m = kr.Status == PromptStatus.OK ? kr.StringResult : "Both";
                int n = LabelCurves(doc, psr.Value.GetObjectIds(), m != "Area", m != "Length");
                ed.WriteMessage("\n[B+] " + n + " label(s) placed on layer BP-LABEL (text height " + N3(Config.TextHeight) + ", change it in Settings). One UNDO removes them.\n");
            });
        }

        [CommandMethod("BPVERTS", CommandFlags.UsePickSet)]
        public static void VertsToPoints()
        {
            Guard("BPVERTS", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var psr = Sel.Get(ed, new PromptSelectionOptions { MessageForAdding = "\nSelect polylines or lines" }, "LWPOLYLINE,POLYLINE,LINE");
                if (psr.Status != PromptStatus.OK) return;
                var sn = ed.GetInteger(new PromptIntegerOptions("\nFirst point number <1>: ") { AllowNone = true, DefaultValue = 1, UseDefaultValue = true, LowerLimit = 0 }); if (sn.Status != PromptStatus.OK) return;
                int no = sn.Value; var pts = new List<SurveyPoint>(); var seen = new HashSet<string>();
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (var id in psr.Value.GetObjectIds())
                        foreach (var v in Verts(tr, (Entity)tr.GetObject(id, OpenMode.ForRead)))
                        {
                            string key = N3(v.X) + "|" + N3(v.Y) + "|" + N3(v.Z); if (!seen.Add(key)) continue;
                            pts.Add(new SurveyPoint { No = (no++).ToString(CultureInfo.InvariantCulture), E = v.X, N = v.Y, Z = v.Z, HasZ = true, Desc = "VTX" });
                        }
                    tr.Commit();
                }
                int n = SurveyCad.Import(doc, pts, SurveyCad.LoadRules(), true, Config.TextHeight, true);
                ed.WriteMessage("\n[B+] " + n + " point(s) made from vertices (repeated positions skipped), numbered from " + sn.Value + ". One UNDO removes them.\n");
            });
        }

        [CommandMethod("BPCOORD", CommandFlags.UsePickSet)]
        public static void CoordLabels()
        {
            Guard("BPCOORD", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var psr = Sel.Get(ed, new PromptSelectionOptions { MessageForAdding = "\nSelect points, polylines or lines to label with E, N" }, "POINT,LWPOLYLINE,POLYLINE,LINE");
                if (psr.Status != PromptStatus.OK) return;
                double th = Config.TextHeight; int n = 0; var seen = new HashSet<string>();
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                    var lay = SurveyCad.EnsureLayer(tr, doc.Database, "BP-COORD", 7);
                    foreach (var id in psr.Value.GetObjectIds())
                    {
                        var en = (Entity)tr.GetObject(id, OpenMode.ForRead); var dp = en as DBPoint;
                        var list = dp != null ? new List<Point3d> { dp.Position } : Verts(tr, en);
                        foreach (var v in list)
                        {
                            if (!seen.Add(N3(v.X) + "|" + N3(v.Y))) continue;
                            var mt = new MText { Contents = "E " + N3(v.X) + "\\PN " + N3(v.Y), Location = new Point3d(v.X + th * 0.5, v.Y + th * 0.5, 0), TextHeight = th, Attachment = AttachmentPoint.BottomLeft, LayerId = lay };
                            ms.AppendEntity(mt); tr.AddNewlyCreatedDBObject(mt, true); n++;
                        }
                    }
                    tr.Commit();
                }
                ed.WriteMessage("\n[B+] " + n + " coordinate label(s) on layer BP-COORD (text height " + N3(th) + "). One UNDO removes them.\n");
            });
        }

        // ---------- Convert ----------
        static CsProvider NeedCs(Document doc)
        {
            var p = Engine.EnsureProvider(doc, true);
            if (p == null) MessageBox.Show("This drawing has no coordinate system yet. Pick one (Live map > Options > Coordinate system) and run the command again.", "B+ CAD Tools", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return p;
        }

        [CommandMethod("BPKMLIMP")]
        public static void KmlImport()
        {
            Guard("BPKMLIMP", () =>
            {
                var doc = Doc; var ed = doc.Editor; string path;
                using (var d = new OpenFileDialog { Filter = "Google Earth (*.kmz;*.kml)|*.kmz;*.kml", Title = "Import KML or KMZ" }) { if (d.ShowDialog() != DialogResult.OK) return; path = d.FileName; }
                var cs = NeedCs(doc); if (cs == null) return;
                List<KmlFeature> feats;
                try { using (var fs = File.OpenRead(path)) feats = Kml.Read(fs, path.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase)); }
                catch (System.Exception e) { MessageBox.Show("Could not read the file: " + e.Message, "B+ CAD Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                var pts = new List<SurveyPoint>(); int lines = 0, no = 1;
                foreach (var f in feats.Where(x => x.Kind == "Point")) { double x, y; cs.FromLonLat(f.Coords[0][0], f.Coords[0][1], out x, out y); pts.Add(new SurveyPoint { No = (no++).ToString(), E = x, N = y, Z = f.Coords[0][2], HasZ = true, Desc = f.Name }); }
                if (pts.Count > 0) SurveyCad.Import(doc, pts, SurveyCad.LoadRules(), true, Config.TextHeight, true);
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                    var lay = SurveyCad.EnsureLayer(tr, doc.Database, "BP-KML", 4);
                    foreach (var f in feats.Where(x => x.Kind != "Point"))
                    {
                        if (f.Coords.Count < 2) continue;
                        var pl = new Autodesk.AutoCAD.DatabaseServices.Polyline { LayerId = lay, Closed = f.Kind == "Polygon" };
                        for (int i = 0; i < f.Coords.Count; i++) { double x, y; cs.FromLonLat(f.Coords[i][0], f.Coords[i][1], out x, out y); pl.AddVertexAt(i, new Point2d(x, y), 0, 0, 0); }
                        ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true); lines++;
                    }
                    tr.Commit();
                }
                ed.WriteMessage("\n[B+] KML/KMZ: " + pts.Count + " point(s), " + lines + " line/polygon(s) imported using " + cs.Description + ". Use ZOOM EXTENTS. Check them against known points.\n");
            });
        }

        [CommandMethod("BPKMLEXP", CommandFlags.UsePickSet)]
        public static void KmlExport()
        {
            Guard("BPKMLEXP", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var psr = Sel.Get(ed, new PromptSelectionOptions { MessageForAdding = "\nSelect points, polylines and lines to export" }, "POINT,AECC_COGO_POINT,LWPOLYLINE,POLYLINE,LINE");
                if (psr.Status != PromptStatus.OK) return;
                var cs = NeedCs(doc); if (cs == null) return;
                string path; using (var d = new SaveFileDialog { Filter = "KMZ|*.kmz|KML|*.kml", FileName = "export.kmz", Title = "Export KML or KMZ" }) { if (d.ShowDialog() != DialogResult.OK) return; path = d.FileName; }
                var feats = new List<KmlFeature>(); var ids = psr.Value.GetObjectIds();
                Func<double, double, double, double[]> ll = (x, y, z) => { double lo, la; cs.ToLonLat(x, y, out lo, out la); return new[] { lo, la, z }; };
                foreach (var c in SurveyCad.Collect(doc, ids)) feats.Add(new KmlFeature { Name = string.IsNullOrEmpty(c.P.No) ? c.P.Desc : c.P.No, Desc = c.P.Desc, Kind = "Point", Coords = { ll(c.P.E, c.P.N, c.P.HasZ ? c.P.Z : 0) } });
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (var id in ids)
                    {
                        var en = (Entity)tr.GetObject(id, OpenMode.ForRead); if (en is DBPoint || en.GetType().Name == "CogoPoint") continue;
                        var v = Verts(tr, en); if (v.Count < 2) continue;
                        bool closed = (en as Curve) != null && ((Curve)en).Closed && !(en is Line);
                        var f = new KmlFeature { Name = en.Layer, Kind = closed ? "Polygon" : "Line" }; foreach (var q in v) f.Coords.Add(ll(q.X, q.Y, q.Z)); feats.Add(f);
                    }
                    tr.Commit();
                }
                string kml = Kml.Write(feats, Path.GetFileNameWithoutExtension(path));
                if (path.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase)) using (var fs = File.Create(path)) Kml.WriteKmz(fs, kml); else File.WriteAllText(path, kml, new UTF8Encoding(false));
                ed.WriteMessage("\n[B+] Exported " + feats.Count + " feature(s) to " + path + " using " + cs.Description + ". Arc segments are written as straight lines between vertices.\n");
            });
        }

        static void ImportText(string title, string filter, Func<string[], ParseResult> parse, string label)
        {
            var doc = Doc; string path;
            using (var d = new OpenFileDialog { Filter = filter, Title = title }) { if (d.ShowDialog() != DialogResult.OK) return; path = d.FileName; }
            ParseResult r;
            try { r = parse(File.ReadAllLines(path, Encoding.UTF8)); } catch (System.Exception e) { MessageBox.Show("Could not read the file: " + e.Message, "B+ CAD Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (r.Points.Count == 0) { MessageBox.Show("No coordinate records were found in this file." + (r.Problems.Count > 0 ? "\n\n" + string.Join("\n", r.Problems.Take(5)) : ""), "B+ CAD Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            int n = SurveyCad.Import(doc, r.Points, SurveyCad.LoadRules(), true, Config.TextHeight, true);
            doc.Editor.WriteMessage("\n[B+] " + label + ": imported " + n + " point(s)" + (r.Problems.Count > 0 ? ", skipped " + r.Problems.Count + " line(s)" : "") + ". This reader is not verified against a real instrument file: compare a few points. Use ZOOM EXTENTS.\n");
        }
        static void ExportText(string title, string filter, string name, Func<List<SurveyPoint>, string> fmt, string label)
        {
            var doc = Doc; var pts = SurveyCad.Collect(doc).Select(c => c.P).ToList();
            if (pts.Count == 0) { doc.Editor.WriteMessage("\n[B+] No points found in model space.\n"); return; }
            string path; using (var d = new SaveFileDialog { Filter = filter, FileName = name, Title = title }) { if (d.ShowDialog() != DialogResult.OK) return; path = d.FileName; }
            File.WriteAllText(path, fmt(pts), new UTF8Encoding(false));
            doc.Editor.WriteMessage("\n[B+] " + label + ": exported " + pts.Count + " point(s) to " + path + ". This writer is not verified against a real instrument: test with one small file first.\n");
        }
        [CommandMethod("BPSDRIMP")] public static void SdrImp() { Guard("BPSDRIMP", () => ImportText("Import SDR", "SDR33 (*.sdr)|*.sdr|All files|*.*", Sdr.Parse, "SDR")); }
        [CommandMethod("BPSDREXP")] public static void SdrExp() { Guard("BPSDREXP", () => ExportText("Export SDR", "SDR33 (*.sdr)|*.sdr", "points.sdr", Sdr.Format, "SDR")); }
        [CommandMethod("BPGSIIMP")] public static void GsiImp() { Guard("BPGSIIMP", () => ImportText("Import GSI", "Leica GSI (*.gsi)|*.gsi|All files|*.*", Gsi.Parse, "GSI")); }
        [CommandMethod("BPGSIEXP")] public static void GsiExp() { Guard("BPGSIEXP", () => ExportText("Export GSI", "Leica GSI-16 (*.gsi)|*.gsi", "points.gsi", p => Gsi.Format(p, true), "GSI-16")); }

        // ---------- Hatch merge / split ----------
        static Hatch NewLike(Transaction tr, BlockTableRecord ms, Hatch b)
        {
            var h = new Hatch(); ms.AppendEntity(h); tr.AddNewlyCreatedDBObject(h, true);
            h.Normal = b.Normal; h.Elevation = b.Elevation; h.LayerId = b.LayerId; h.Color = b.Color;
            try { h.Linetype = b.Linetype; } catch { }
            if (b.HatchObjectType == HatchObjectType.GradientObject) { h.HatchObjectType = HatchObjectType.GradientObject; h.SetGradient(b.GradientType, b.GradientName); h.GradientAngle = b.GradientAngle; h.GradientShift = b.GradientShift; h.ShadeTintValue = b.ShadeTintValue; try { h.SetGradientColors(b.GetGradientColors()); } catch { } }
            else { h.SetHatchPattern(b.PatternType, b.PatternName); try { h.PatternScale = b.PatternScale; h.PatternAngle = b.PatternAngle; } catch { } }
            h.Associative = false; return h;
        }
        static void CopyLoop(Hatch src, int i, Hatch dst)
        {
            var lp = src.GetLoopAt(i);
            if (lp.IsPolyline) { var pc = new Point2dCollection(); var bc = new DoubleCollection(); foreach (BulgeVertex v in lp.Polyline) { pc.Add(v.Vertex); bc.Add(v.Bulge); } dst.AppendLoop(lp.LoopType, pc, bc); }
            else { var cc = new Curve2dCollection(); foreach (Curve2d c in lp.Curves) cc.Add((Curve2d)c.Clone()); var seg = new IntegerCollection(); foreach (Curve2d c in cc) seg.Add(c is LineSegment2d ? 1 : c is CircularArc2d ? 2 : c is EllipticalArc2d ? 3 : 4); dst.AppendLoop(lp.LoopType, cc, seg); }
        }
        static IList<double[]> LoopPoly(Hatch h, int i)
        {
            var lp = h.GetLoopAt(i); var res = new List<double[]>();
            if (lp.IsPolyline) { foreach (BulgeVertex v in lp.Polyline) res.Add(new[] { v.Vertex.X, v.Vertex.Y }); }
            else foreach (Curve2d c in lp.Curves) { var sp = c.GetSamplePoints(24); foreach (var p in sp) res.Add(new[] { p.X, p.Y }); }
            return res;
        }

        [CommandMethod("BPHATCHMERGE", CommandFlags.UsePickSet)]
        public static void HatchMerge()
        {
            Guard("BPHATCHMERGE", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var psr = Sel.Get(ed, new PromptSelectionOptions { MessageForAdding = "\nSelect two or more hatches to merge" }, "HATCH");
                if (psr.Status != PromptStatus.OK) return;
                var ids = psr.Value.GetObjectIds(); if (ids.Length < 2) { ed.WriteMessage("\n[B+] Select at least two hatches.\n"); return; }
                var r = MessageBox.Show("Merge " + ids.Length + " hatches into one hatch object?\n\nIt takes the pattern, scale, colour and layer of the first selected hatch. Boundaries that overlap each other may leave holes.\n\nYes = merge and delete the originals\nNo = merge and keep the originals\nCancel = do nothing", "B+ CAD Tools - Merge hatches", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return; bool keep = r == DialogResult.No; int used = 0, skipped = 0;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                    var first = (Hatch)tr.GetObject(ids[0], OpenMode.ForRead); var dst = NewLike(tr, ms, first);
                    foreach (var id in ids)
                    {
                        var h = (Hatch)tr.GetObject(id, OpenMode.ForRead);
                        if (!h.Normal.IsEqualTo(first.Normal) || Math.Abs(h.Elevation - first.Elevation) > 1e-9) { skipped++; continue; }
                        for (int i = 0; i < h.NumberOfLoops; i++) CopyLoop(h, i, dst);
                        used++;
                    }
                    dst.EvaluateHatch(true);
                    if (!keep) foreach (var id in ids) { var h = (Hatch)tr.GetObject(id, OpenMode.ForWrite); if (h.Normal.IsEqualTo(first.Normal) && Math.Abs(h.Elevation - first.Elevation) <= 1e-9) h.Erase(); }
                    tr.Commit();
                }
                ed.WriteMessage("\n[B+] Merged " + used + " hatch(es) into one" + (skipped > 0 ? " (" + skipped + " skipped: different plane)" : "") + (keep ? ", originals kept" : ", originals deleted") + ". The new hatch is not associative with its boundaries. One UNDO reverses it.\n");
            });
        }

        [CommandMethod("BPHATCHSPLIT", CommandFlags.UsePickSet)]
        public static void HatchSplit()
        {
            Guard("BPHATCHSPLIT", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var psr = Sel.Get(ed, new PromptSelectionOptions { MessageForAdding = "\nSelect hatches to split" }, "HATCH");
                if (psr.Status != PromptStatus.OK) return;
                var ids = psr.Value.GetObjectIds();
                var r = MessageBox.Show("Split each selected hatch into one hatch per separate area?\n\nIslands (holes) stay with the area that contains them.\n\nYes = split and delete the originals\nNo = split and keep the originals\nCancel = do nothing", "B+ CAD Tools - Split hatches", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return; bool keep = r == DialogResult.No; int made = 0, split = 0;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                    foreach (var id in ids)
                    {
                        var h = (Hatch)tr.GetObject(id, OpenMode.ForRead); int nl = h.NumberOfLoops; if (nl < 2) continue;
                        var polys = new List<IList<double[]>>(); for (int i = 0; i < nl; i++) polys.Add(LoopPoly(h, i));
                        var g = LoopGroups.Group(polys); var groups = g.Distinct().ToList(); if (groups.Count < 2) continue;
                        foreach (var gk in groups)
                        {
                            var dst = NewLike(tr, ms, h);
                            for (int i = 0; i < nl; i++) if (g[i] == gk) CopyLoop(h, i, dst);
                            dst.EvaluateHatch(true); made++;
                        }
                        split++;
                        if (!keep) { var w = (Hatch)tr.GetObject(id, OpenMode.ForWrite); w.Erase(); }
                    }
                    tr.Commit();
                }
                ed.WriteMessage(split == 0 ? "\n[B+] Nothing to split: each selected hatch is a single area.\n" : "\n[B+] Split " + split + " hatch(es) into " + made + " (" + (keep ? "originals kept" : "originals deleted") + "). New hatches are not associative. One UNDO reverses it.\n");
            });
        }

        [CommandMethod("BPMAPOPT")]
        public static void MapOptions()
        {
            Guard("BPMAPOPT", () =>
            {
                int k0 = Config.Kind == MapKind.Roadmap ? 1 : Config.Kind == MapKind.Hybrid ? 2 : 0, nk, no;
                string follow = ToolsUi.MapOptions(k0, Config.OpacityPercent, out nk, out no); if (follow == null) return;
                if (nk != k0 || no != Config.OpacityPercent)
                {
                    Config.Kind = nk == 1 ? MapKind.Roadmap : nk == 2 ? MapKind.Hybrid : MapKind.Satellite; Config.OpacityPercent = no;
                    Engine.RefreshNow();
                    Doc.Editor.WriteMessage("\n[B+] Map type and opacity saved" + (Engine.On ? "." : ". The map is off: click Live map to show it.") + "\n");
                }
                if (follow.Length > 0) Doc.SendStringToExecute(follow, true, false, false);
            });
        }

        [CommandMethod("BPLEADER", CommandFlags.UsePickSet)]
        public static void AutoLeader()
        {
            Guard("BPLEADER", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var psr = Sel.Get(ed, new PromptSelectionOptions { MessageForAdding = "\nSelect shapes to label with leaders" }, CrvFilter + ",POINT");
                if (psr.Status != PromptStatus.OK) return;
                var allIds = psr.Value.GetObjectIds();
                var ptIds = allIds.Where(i => i.ObjectClass.DxfName == "POINT").ToArray(); var ids = allIds.Where(i => i.ObjectClass.DxfName != "POINT").ToArray();
                var rows = LenRows(doc, ids);
                if (rows.Count == 0 && ptIds.Length == 0) { ed.WriteMessage("\n[B+] Nothing measurable was selected.\n"); return; }
                var ch = ToolsUi.Leader(rows.Count, rows.Count(r => r.Closed), ptIds.Length); if (ch == null) return;
                int made = 0, skipped = 0; double th = ch.TextH; string fmt = "F" + ch.Decimals; var inv = CultureInfo.InvariantCulture;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                    var lay = SurveyCad.EnsureLayer(tr, doc.Database, "BP-LEADER", 7);
                    foreach (var pid in ptIds)
                    {
                        var dp = (DBPoint)tr.GetObject(pid, OpenMode.ForRead); var pp = dp.Position;
                        if (ch.What == "len" || ch.What == "area" || ch.What == "both") { skipped++; continue; }
                        string ptext = "E " + pp.X.ToString(fmt, inv) + "\\PN " + pp.Y.ToString(fmt, inv) + (ch.Z ? "\\PZ " + pp.Z.ToString(fmt, inv) : "");
                        var pend = new Point3d(pp.X + th * 3, pp.Y + th * 3, pp.Z);
                        var pml = new MLeader(); pml.SetDatabaseDefaults(doc.Database); pml.ContentType = ContentType.MTextContent;
                        var pmt = new MText { Contents = ptext, TextHeight = th }; pmt.SetDatabaseDefaults(doc.Database); pml.MText = pmt;
                        int pli = pml.AddLeader(); int pln = pml.AddLeaderLine(pli); pml.AddFirstVertex(pln, pp); pml.AddLastVertex(pln, pend);
                        pml.TextHeight = th; pml.ArrowSize = ch.Arrow ? th * 0.8 : 0.0; pml.LayerId = lay;
                        ms.AppendEntity(pml); tr.AddNewlyCreatedDBObject(pml, true); made++;
                    }
                    foreach (var r in rows)
                    {
                        if (ch.What == "coord") { skipped++; continue; }
                        var c = (Curve)tr.GetObject((ObjectId)r.Tag, OpenMode.ForRead);
                        string text; string lt = r.Len.ToString(fmt, inv) + " m", at = r.Area.ToString(fmt, inv) + " m\u00B2";
                        if (ch.What == "len") text = lt;
                        else if (ch.What == "area") { if (!r.Closed) { skipped++; continue; } text = at; }
                        else if (ch.What == "both") text = r.Closed ? lt + "\\P" + at : lt;
                        else text = r.Closed ? at : lt;
                        Point3d start;
                        try
                        {
                            if (ch.Centre && r.Closed) { var ext = c.GeometricExtents; start = new Point3d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2, 0); }
                            else start = c.GetPointAtDist(c.GetDistanceAtParameter(c.StartParam) + r.Len / 2);
                        }
                        catch { skipped++; continue; }
                        var end = new Point3d(start.X + th * 3, start.Y + th * 3, start.Z);
                        var ml = new MLeader();
                        ml.SetDatabaseDefaults(doc.Database);
                        ml.ContentType = ContentType.MTextContent;
                        var mt = new MText { Contents = text, TextHeight = th }; mt.SetDatabaseDefaults(doc.Database);
                        ml.MText = mt;
                        int li = ml.AddLeader(); int ln = ml.AddLeaderLine(li);
                        ml.AddFirstVertex(ln, start); ml.AddLastVertex(ln, end);
                        ml.TextHeight = th; ml.ArrowSize = ch.Arrow ? th * 0.8 : 0.0; ml.LayerId = lay;
                        ms.AppendEntity(ml); tr.AddNewlyCreatedDBObject(ml, true); made++;
                    }
                    tr.Commit();
                }
                ed.WriteMessage("\n[B+] " + made + " leader(s) on layer BP-LEADER" + (skipped > 0 ? ", " + skipped + " shape(s) skipped (open shape for an area label, or not measurable)" : "") + ". One UNDO removes them.\n");
            });
        }
    }
}
