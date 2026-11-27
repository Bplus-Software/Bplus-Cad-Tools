using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using BPlus.LiveMap.Core;
using ColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BPlus.LiveMap
{
    static class SectionUi
    {
        public static List<RoadPreset> Presets() { return SectionMath.Unpack(Config.RoadPresets); }
        static string N(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        public static void EditPresets()
        {
            var list = Presets();
            using (var f = Theme.Base("B+ CAD Tools - Road section presets", 560, 360))
            {
                f.Controls.Add(Theme.Lbl("Sizes in metres, crossfall in %. Lane 3.50 to 3.65 m and sidewalk 1.8 m minimum come from the Saudi ministry guides. Lane count, kerb and crossfall are starting values to confirm.", 16, 12, 528, true)); f.Controls[0].Height = 50;
                string[] h = { "Type", "Lanes", "Lane (m)", "Sidewalk", "Planting", "Kerb (m)", "Crossfall %" }; int[] xs = { 16, 110, 170, 250, 330, 410, 480 };
                for (int i = 0; i < h.Length; i++) f.Controls.Add(Theme.Lbl(h[i], xs[i], 74, 80, true));
                var tb = new TextBox[3, 6];
                for (int r = 0; r < 3; r++)
                {
                    f.Controls.Add(Theme.Lbl(list[r].Name, 16, 106 + r * 40, 90));
                    var v = new[] { list[r].Lanes.ToString(CultureInfo.InvariantCulture), N(list[r].LaneWidth), N(list[r].Sidewalk), N(list[r].Planting), N(list[r].Kerb), N(list[r].Crossfall) };
                    for (int c = 0; c < 6; c++) { tb[r, c] = new TextBox { Left = xs[c + 1], Top = 102 + r * 40, Width = 62, Text = v[c], BorderStyle = BorderStyle.FixedSingle }; Theme.Style(tb[r, c]); f.Controls.Add(tb[r, c]); }
                }
                var info = new Label { Left = 16, Top = 232, Width = 528, Height = 36, ForeColor = Color.FromArgb(255, 150, 120), BackColor = Color.Transparent }; f.Controls.Add(info);
                var reset = Theme.Btn("Back to defaults", 16, 296, 140); var ok = Theme.Btn("Save", 384, 296, 75, true); var cancel = Theme.Btn("Cancel", 467, 296, 75);
                reset.Click += (s, e) => { var d = SectionMath.RoadDefaults(); for (int r = 0; r < 3; r++) { tb[r, 0].Text = d[r].Lanes.ToString(CultureInfo.InvariantCulture); tb[r, 1].Text = N(d[r].LaneWidth); tb[r, 2].Text = N(d[r].Sidewalk); tb[r, 3].Text = N(d[r].Planting); tb[r, 4].Text = N(d[r].Kerb); tb[r, 5].Text = N(d[r].Crossfall); } };
                ok.Click += (s, e) =>
                {
                    var res = SectionMath.RoadDefaults();
                    for (int r = 0; r < 3; r++)
                    {
                        int n; double lw, sw, pl, k, cf;
                        if (!int.TryParse(tb[r, 0].Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out n) || n < 1 || n > 12 || !Theme.TryNum(tb[r, 1].Text, out lw) || lw <= 0 || lw > 10 || !Theme.TryNum(tb[r, 2].Text, out sw) || sw < 0 || sw > 20 || !Theme.TryNum(tb[r, 3].Text, out pl) || pl < 0 || pl > 20 || !Theme.TryNum(tb[r, 4].Text, out k) || k < 0 || k > 1 || !Theme.TryNum(tb[r, 5].Text, out cf) || cf < 0 || cf > 15)
                        { info.Text = res[r].Name + ": lanes 1 to 12, lane above 0, sidewalk and planting 0 or more, kerb 0 to 1 m, crossfall 0 to 15 %."; return; }
                        res[r].Lanes = n; res[r].LaneWidth = lw; res[r].Sidewalk = sw; res[r].Planting = pl; res[r].Kerb = k; res[r].Crossfall = cf;
                    }
                    Config.RoadPresets = SectionMath.Pack(res); f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { reset, ok, cancel }); f.CancelButton = cancel; AcApp.ShowModalDialog(f);
            }
        }

        public sealed class Choice
        {
            public bool Whole = true, Ground = true; public int Road; /* 0 none, 1 typical, 2 existing, 3 assembly */ public SecAssembly Asm;
            public double Interval = 20, Half = 15, Strip = 2.5, Ve = 1, Start, End, TextH, CentreLevel = double.NaN, SwL, SwR, PlL, PlR, Kerb, Cross; public double[] Lanes = new double[0]; public bool Divide, Labels = true; public int Cols = 3;
        }

        public static bool Ask(double length, bool havePts, double th, out Choice ch)
        {
            var res = new Choice { TextH = th }; bool good = false; var pre = Presets();
            using (var f = Theme.Base("B+ CAD Tools - Quick section", 600, 660))
            {
                Func<string, int, int, int, Label> L = (t, x, y, w) => Theme.Lbl(t, x, y, w);
                f.Controls.Add(Theme.Lbl("Line length " + length.ToString("F2", CultureInfo.InvariantCulture) + " m. Left of the line is negative, right is positive.", 16, 10, 560, true));
                var rw = new RadioButton { Left = 16, Top = 34, Width = 270, Text = "Whole road (all stations)", Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent }; var r1 = new RadioButton { Left = 290, Top = 34, Width = 280, Text = "One station (I pick it)", ForeColor = Theme.Text, BackColor = Color.Transparent };
                var gr = new CheckBox { Left = 16, Top = 62, Width = 560, Text = havePts ? "Existing ground from the selected points" : "Existing ground from points (no points selected)", Checked = havePts, Enabled = havePts, ForeColor = Theme.Text, BackColor = Color.Transparent };
                f.Controls.AddRange(new Control[] { rw, r1, gr });
                f.Controls.Add(Theme.Lbl("Road", 16, 94, 60, true));
                var ro0 = new RadioButton { Left = 70, Top = 92, Width = 70, Text = "None", ForeColor = Theme.Text, BackColor = Color.Transparent }; var ro1 = new RadioButton { Left = 142, Top = 92, Width = 150, Text = "Typical (presets)", Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent }; var ro3 = new RadioButton { Left = 294, Top = 92, Width = 120, Text = "My assembly", ForeColor = Theme.Text, BackColor = Color.Transparent }; var ro2 = new RadioButton { Left = 416, Top = 92, Width = 180, Text = "Existing (pick edges)", ForeColor = Theme.Text, BackColor = Color.Transparent };
                f.Controls.AddRange(new Control[] { ro0, ro1, ro3, ro2 });
                var asmList = AsmUi.All(); var asmBox = new ComboBox { Left = 410, Top = 124, Width = 170, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat }; Theme.Style(asmBox); foreach (var a in asmList) asmBox.Items.Add(a.Name); asmBox.SelectedIndex = 0; f.Controls.Add(Theme.Lbl("SecAssembly", 336, 127, 70)); f.Controls.Add(asmBox);
                var typ = new ComboBox { Left = 160, Top = 124, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat }; Theme.Style(typ); foreach (var p in pre) typ.Items.Add(p.Name); typ.SelectedIndex = 1;
                f.Controls.Add(L("Road type", 16, 127, 130)); f.Controls.Add(typ);
                string[] nm = { "Lane widths (m, left to right, comma)", "Sidewalk left (m)", "Sidewalk right (m)", "Planting left (m)", "Planting right (m)", "Kerb height (m)", "Crossfall (%)", "Centre level (m), empty = ground", "Station interval (m)", "Half-width each side (m)", "Strip along the line (m)", "Vertical exaggeration", "Road width at start (m)", "Road width at end (m)", "Sections per row", "Text height" };
                var tb = new TextBox[nm.Length];
                for (int i = 0; i < nm.Length; i++)
                {
                    int col = i == 0 ? 0 : (i - 1) % 2, row = i == 0 ? 0 : 1 + (i - 1) / 2; int x = i == 0 ? 16 : 16 + col * 290; int y = 160 + row * 32;
                    f.Controls.Add(Theme.Lbl(nm[i], x, y + 3, i == 0 ? 280 : 190)); tb[i] = new TextBox { Left = i == 0 ? 300 : x + 195, Top = y, Width = i == 0 ? 280 : 80, BorderStyle = BorderStyle.FixedSingle }; Theme.Style(tb[i]); f.Controls.Add(tb[i]);
                }
                Action<int> fillFrom = idx =>
                {
                    var p = pre[idx]; tb[0].Text = string.Join(", ", Enumerable.Repeat(N(p.LaneWidth), p.Lanes)); tb[1].Text = N(p.Sidewalk); tb[2].Text = N(p.Sidewalk); tb[3].Text = N(p.Planting); tb[4].Text = N(p.Planting); tb[5].Text = N(p.Kerb); tb[6].Text = N(p.Crossfall);
                };
                fillFrom(1); typ.SelectedIndexChanged += (s, e) => fillFrom(typ.SelectedIndex);
                tb[7].Text = ""; tb[8].Text = "20"; tb[9].Text = "15"; tb[10].Text = "2.5"; tb[11].Text = "1"; tb[12].Text = ""; tb[13].Text = ""; tb[14].Text = "3"; tb[15].Text = N(th);
                int by = 160 + 9 * 32;
                var div = new CheckBox { Left = 16, Top = by, Width = 560, Text = "Existing road: divide the picked (or start/end) width equally among the lanes typed above", Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                var lab = new CheckBox { Left = 16, Top = by + 28, Width = 560, Text = "Label every ground point (number and level)", Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                var note = Theme.Lbl("Whole road with an existing road: type the road width at the start and the end; it is interpolated along the line, centred on the line. One station: you pick the two edges and the centre.", 16, by + 56, 560, true); note.Height = 40;
                var info = new Label { Left = 16, Top = by + 98, Width = 560, Height = 36, ForeColor = Color.FromArgb(255, 150, 120), BackColor = Color.Transparent };
                f.Controls.AddRange(new Control[] { div, lab, note, info });
                var edit = Theme.Btn("Edit road presets...", 16, 590, 170); var editA = Theme.Btn("Edit assemblies...", 192, 590, 170); var ok = Theme.Btn("Next", 410, 590, 80, true); var cancel = Theme.Btn("Cancel", 498, 590, 80);
                edit.Click += (s, e) => { EditPresets(); var np = Presets(); for (int i = 0; i < 3; i++) pre[i] = np[i]; fillFrom(typ.SelectedIndex); };
                editA.Click += (s, e) => { string keep = asmBox.SelectedItem as string; AsmUi.Edit(keep); asmList = AsmUi.All(); asmBox.Items.Clear(); foreach (var a in asmList) asmBox.Items.Add(a.Name); int ix = asmList.FindIndex(a => a.Name == keep); asmBox.SelectedIndex = ix < 0 ? 0 : ix; };
                ok.Click += (s, e) =>
                {
                    double v; var d = new double[16]; var named = new[] { 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 15 };
                    foreach (int i in named) { if (!Theme.TryNum(tb[i].Text, out v) || v < 0) { info.Text = "Check the number in: " + nm[i]; return; } d[i] = v; }
                    if (d[8] <= 0 || d[9] <= 0 || d[10] <= 0 || d[11] <= 0 || d[15] <= 0 || d[15] > 50) { info.Text = "Interval, half-width, strip, exaggeration and text height must be above 0 (text height at most 50)."; return; }
                    if (length / d[8] > 300 && rw.Checked) { info.Text = "That is more than 300 sections. Use a bigger interval."; return; }
                    var lanes = new List<double>(); if (ro1.Checked || ro2.Checked) { foreach (var t in tb[0].Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)) { if (!Theme.TryNum(t, out v) || v <= 0 || v > 10) { info.Text = "Lane widths: numbers above 0 separated by commas."; return; } lanes.Add(v); } if (lanes.Count == 0) { info.Text = "Type at least one lane width."; return; } }
                    double cl = double.NaN; if (tb[7].Text.Trim().Length > 0) { if (!Theme.TryNum(tb[7].Text, out cl)) { info.Text = "Centre level must be a number or empty."; return; } }
                    double ws = 0, we = 0; if (rw.Checked && ro2.Checked) { if (!Theme.TryNum(tb[12].Text, out ws) || !Theme.TryNum(tb[13].Text, out we) || ws <= 0 || we <= 0) { info.Text = "Type the road width at the start and at the end (above 0)."; return; } }
                    if (!gr.Checked && ro0.Checked) { info.Text = "Choose ground from points, a road, or both."; return; }
                    int cols; if (!int.TryParse(tb[14].Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out cols) || cols < 1 || cols > 20) { info.Text = "Sections per row: 1 to 20."; return; }
                    res.Asm = ro3.Checked && asmBox.SelectedIndex >= 0 ? asmList[asmBox.SelectedIndex].Copy() : null; res.Whole = rw.Checked; res.Ground = gr.Checked; res.Road = ro0.Checked ? 0 : ro1.Checked ? 1 : ro3.Checked ? 3 : 2; res.Interval = d[8]; res.Half = d[9]; res.Strip = d[10]; res.Ve = d[11]; res.TextH = d[15]; res.SwL = d[1]; res.SwR = d[2]; res.PlL = d[3]; res.PlR = d[4]; res.Kerb = d[5]; res.Cross = d[6];
                    res.Lanes = lanes.ToArray(); res.CentreLevel = cl; res.Start = ws; res.End = we; res.Divide = div.Checked; res.Labels = lab.Checked; res.Cols = cols; good = true; f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { edit, editA, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel; AcApp.ShowModalDialog(f);
            }
            ch = res; return good;
        }
    }

    public partial class Commands
    {
        static short SecColor(string kind) { switch (kind) { case "frame": return 7; case "grid": return 8; case "centre": return 1; case "road": return 7; case "walk": return 6; case "plant": return 3; case "tree": return 3; case "ped": return 30; case "slope": return 4; case "lane": return 2; case "dim": return 2; case "pt": return 3; default: return 7; } }

        static int DrawSections(Document doc, List<ProfModel> models, List<double> halfs, Point3d at, double th, int cols)
        {
            var db = doc.Database; int n = 0;
            double dx = halfs.Max() * 2 + 12 * th, dy = models.Max(m => m.Segs.Max(s => Math.Max(s.Y1, s.Y2))) + 24 * th;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lay = SurveyCad.EnsureLayer(tr, db, "B+_SECTION", 7);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                Action<Entity, short> add = (en, col) => { en.LayerId = lay; en.Color = AcColor.FromColorIndex(ColorMethod.ByAci, col); ms.AppendEntity(en); tr.AddNewlyCreatedDBObject(en, true); n++; };
                for (int i = 0; i < models.Count; i++)
                {
                    var m = models[i]; double ox = at.X + (i % cols) * dx, oy = at.Y - (i / cols) * dy;
                    Func<double, double, Point3d> P = (x, y) => new Point3d(ox + x, oy + y, 0);
                    foreach (var s in m.Segs) add(new Line(P(s.X1, s.Y1), P(s.X2, s.Y2)), SecColor(s.Kind));
                    if (m.Ground.Count > 1) { var pl = new Autodesk.AutoCAD.DatabaseServices.Polyline(); int k = 0; foreach (var g in m.Ground) pl.AddVertexAt(k++, new Point2d(ox + g[0], oy + g[1]), 0, 0, 0); add(pl, 3); }
                    foreach (var t in m.Texts)
                    {
                        var d = new DBText { TextString = t.S, Height = t.Kind == "title" ? th * 1.2 : th, Position = P(t.X, t.Y), Rotation = t.Rot * Math.PI / 180.0 };
                        d.HorizontalMode = t.Align == 0 ? TextHorizontalMode.TextLeft : t.Align == 1 ? TextHorizontalMode.TextCenter : TextHorizontalMode.TextRight; d.VerticalMode = TextVerticalMode.TextVerticalMid; d.AlignmentPoint = P(t.X, t.Y);
                        add(d, SecColor(t.Kind));
                    }
                }
                tr.Commit();
            }
            return n;
        }

        [CommandMethod("BPSECTION", CommandFlags.UsePickSet)]
        public static void QuickSection()
        {
            Guard("BPSECTION", () =>
            {
                var doc = Doc; var ed = doc.Editor; var db = doc.Database;
                var eo = new PromptEntityOptions("\nSelect the road line (polyline, line or arc): "); eo.SetRejectMessage("\nOnly a polyline, line or arc."); eo.AddAllowedClass(typeof(Curve), false);
                var er = ed.GetEntity(eo); if (er.Status != PromptStatus.OK) return;
                var po = new PromptSelectionOptions { MessageForAdding = "\nSelect the ground points (AutoCAD points or COGO points), or press Enter to skip: " };
                SelResult psr = null; try { psr = Sel.Get(ed, po, "POINT,AECC_COGO_POINT"); } catch { psr = null; }
                bool havePts = psr != null && psr.Status == PromptStatus.OK && psr.Value.Count > 0;
                double length; using (var tr = db.TransactionManager.StartTransaction()) { var cv0 = (Curve)tr.GetObject(er.ObjectId, OpenMode.ForRead); length = cv0.GetDistanceAtParameter(cv0.EndParam); tr.Commit(); }
                double scale = DrawingScale(db), th = scale > 1.0001 ? 0.0025 * scale : ProfileMath.AutoTextHeight(30);
                SectionUi.Choice ch; if (!SectionUi.Ask(length, havePts, th, out ch)) return;
                var all = new List<SecPt>(); var notes = new List<string>(); var models = new List<ProfModel>(); var halfs = new List<double>(); int skipped = 0;
                // single-station picks first (before any transaction)
                double oneSta = 0; Point3d p1 = Point3d.Origin, p2 = Point3d.Origin, pc = Point3d.Origin;
                Func<Curve, Point3d, double, Vector3d> leftN = (cv, q, s) => { var dv = cv.GetFirstDerivative(q); dv = dv.GetNormal(); return new Vector3d(-dv.Y, dv.X, 0); };
                if (!ch.Whole)
                {
                    var pp = ed.GetPoint("\nPick the station: click a point near the line where the section goes: "); if (pp.Status != PromptStatus.OK) return;
                    using (var tr = db.TransactionManager.StartTransaction()) { var cv = (Curve)tr.GetObject(er.ObjectId, OpenMode.ForRead); var q = cv.GetClosestPointTo(new Point3d(pp.Value.X, pp.Value.Y, 0), Vector3d.ZAxis, false); oneSta = cv.GetDistAtPoint(q); tr.Commit(); }
                    if (ch.Road == 2)
                    {
                        var a = ed.GetPoint("\nPick one edge of the carriageway (kerb to kerb): "); if (a.Status != PromptStatus.OK) return;
                        var b = ed.GetPoint("\nPick the other edge: "); if (b.Status != PromptStatus.OK) return;
                        var c = ed.GetPoint("\nPick the centre of the road: "); if (c.Status != PromptStatus.OK) return; p1 = a.Value; p2 = b.Value; pc = c.Value;
                    }
                }
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var cv = (Curve)tr.GetObject(er.ObjectId, OpenMode.ForRead);
                    if (ch.Ground && havePts)
                    {
                        var ids = new HashSet<ObjectId>(psr.Value.GetObjectIds()); int off = 0;
                        foreach (var cp in SurveyCad.Collect(doc, ids))
                        {
                            if (!cp.P.HasZ) continue; var p = new Point3d(cp.P.E, cp.P.N, 0); Point3d q;
                            try { q = cv.GetClosestPointTo(p, Vector3d.ZAxis, false); } catch { off++; continue; }
                            double sta; try { sta = cv.GetDistAtPoint(q); } catch { off++; continue; }
                            var nl = leftN(cv, q, sta); double side = (p.X - q.X) * nl.X + (p.Y - q.Y) * nl.Y, dist = Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
                            all.Add(new SecPt { Id = cp.P.No ?? "", Sta = sta, Off = side > 0 ? -dist : dist, Z = cp.P.Z });
                        }
                        if (off > 0) notes.Add(off + " point(s) could not be placed on the line");
                    }
                    var stations = ch.Whole ? SectionMath.Stations(length, ch.Interval) : new List<double> { oneSta };
                    foreach (double s in stations)
                    {
                        var q = cv.GetPointAtDist(s); var nl = leftN(cv, q, s);
                        var sel = ch.Ground ? SectionMath.Select(all, s, ch.Strip, ch.Half) : new List<SecPt>();
                        RoadSpec road = null;
                        if (ch.Road == 1)
                        {
                            double w = ch.Lanes.Sum(); road = new RoadSpec { LeftEdge = -w / 2, RightEdge = w / 2, Centre = 0, Lanes = ch.Lanes };
                        }
                        else if (ch.Road == 2)
                        {
                            if (ch.Whole) { double w = SectionMath.Lerp(ch.Start, ch.End, s, length); road = new RoadSpec { LeftEdge = -w / 2, RightEdge = w / 2, Centre = 0, Lanes = ch.Divide ? SectionMath.EqualLanes(w, ch.Lanes.Length) : ch.Lanes }; }
                            else
                            {
                                Func<Point3d, double> offOf = pt => -((pt.X - q.X) * nl.X + (pt.Y - q.Y) * nl.Y);
                                double o1 = offOf(p1), o2 = offOf(p2), oc = offOf(pc), lo = Math.Min(o1, o2), hi = Math.Max(o1, o2), w = hi - lo;
                                if (w < 0.5) { ed.WriteMessage("\n[B+] The two edge points are less than 0.5 m apart across the road. Nothing drawn.\n"); return; }
                                road = new RoadSpec { LeftEdge = lo, RightEdge = hi, Centre = oc, Lanes = ch.Divide ? SectionMath.EqualLanes(w, ch.Lanes.Length) : ch.Lanes };
                                if (!ch.Divide && Math.Abs(ch.Lanes.Sum() - w) > 0.01) notes.Add("typed lanes add up to " + ch.Lanes.Sum().ToString("0.###", CultureInfo.InvariantCulture) + " m but the picked road is " + w.ToString("0.###", CultureInfo.InvariantCulture) + " m");
                            }
                        }
                        if (road != null) { road.SwL = ch.SwL; road.SwR = ch.SwR; road.PlL = ch.PlL; road.PlR = ch.PlR; road.Kerb = ch.Kerb; road.Crossfall = ch.Cross; road.CentreLevel = ch.CentreLevel; }
                        if (sel.Count < 2 && road == null && !(ch.Road == 3 && ch.Asm != null)) { skipped++; notes.Add("station " + ProfileMath.Sta(s) + ": " + sel.Count + " point(s), not drawn"); continue; }
                        if (ch.Road == 3 && ch.Asm != null) ch.Asm.CentreLevel = ch.CentreLevel;
                        double hu; string warn; var m = SectionMath.Build(sel, ch.Half, ch.Ve, double.NaN, ch.TextH, ch.Labels, road, ch.Road == 3 ? ch.Asm : null, "Station " + ProfileMath.Sta(s), out hu, out warn);
                        if (warn != "") notes.Add("station " + ProfileMath.Sta(s) + ": " + warn);
                        models.Add(m); halfs.Add(hu);
                    }
                    tr.Commit();
                }
                if (models.Count == 0) { ed.WriteMessage("\n[B+] Nothing to draw. " + string.Join("; ", notes) + "\n"); return; }
                var ip = ed.GetPoint("\nPick the insertion point (top left of the first section): "); if (ip.Status != PromptStatus.OK) return;
                int made = DrawSections(doc, models, halfs, ip.Value, ch.TextH, ch.Cols);
                ed.WriteMessage("\n[B+] " + models.Count + " section(s) drawn on layer B+_SECTION (" + made + " objects)" + (skipped > 0 ? ", " + skipped + " station(s) skipped for lack of points" : "") + ". Left of the line is negative. It is a drawing: it does not update if points move. It does not check the widths against any standard." + (notes.Count > 0 ? "\n[B+] Notes: " + string.Join("; ", notes.Take(12)) + (notes.Count > 12 ? "; and " + (notes.Count - 12) + " more" : "") : "") + "\n");
            });
        }
    }
}
