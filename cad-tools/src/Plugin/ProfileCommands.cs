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
    public partial class Commands
    {
        sealed class ProfOpts { public double Interval, Ve = 10, Offset = 5, Datum = double.NaN, TextH; public bool Label = true, Table = true, OwnZ, Grid = true, Drops = true, Mark = true, Avg = true, GLine = true; public short GCol = 3; public double Height = double.NaN; }

        /// <summary>Drawing scale: DIMSCALE of this drawing (the scale the surrounding drawing is annotated at). 0 or less counts as 1.</summary>
        static double DrawingScale(Database db) { double s = 1; try { s = Convert.ToDouble(Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("DIMSCALE"), CultureInfo.InvariantCulture); } catch { } return s > 0 ? s : 1; }

        /// <summary>Text height in drawing units: 2.5 mm on paper at the drawing scale. At scale 1 that would be unreadably small in a metre drawing, so the Settings text height is used instead.</summary>
        static double ProfileTextHeight(double scale) { return scale > 1.0001 ? 0.0025 * scale : Config.TextHeight; }
        /// <summary>For a profile: the larger of the scale-based or Settings height and a height that suits the line length (so a long line does not get tiny text).</summary>
        static double ProfileTextHeight(double scale, double length) { return Math.Max(ProfileTextHeight(scale), ProfileMath.AutoTextHeight(length)); }

        static bool ProfileAsk(double length, double scale, double th, bool anyPts, out ProfOpts o)
        {
            o = new ProfOpts { TextH = th, Interval = ProfileMath.DefaultInterval(length) }; var res = o; bool good = false;
            using (var f = Theme.Base("B+ CAD Tools - Quick profile", 520, 620))
            {
                f.Controls.Add(Theme.Lbl("Line length " + length.ToString("F2", CultureInfo.InvariantCulture) + " m.  Drawing scale 1:" + scale.ToString("0.##", CultureInfo.InvariantCulture) + " (DIMSCALE)" + ", text height suggested from the line length: change it below if you want.", 16, 14, 490, true));
                string[] nm = { "Vertical exaggeration", "Station interval (m)", "Offset: points within (m) of the line", "Datum level (m), empty = automatic", "Text height (drawing units)", "Frame height (drawing units), empty = use exaggeration" };
                string[] vl = { "10", ProfileMath.DefaultInterval(length).ToString(CultureInfo.InvariantCulture), "5", "", th.ToString("0.###", CultureInfo.InvariantCulture), "" };
                var tb = new TextBox[6];
                for (int i = 0; i < 6; i++) { f.Controls.Add(Theme.Lbl(nm[i], 16, 52 + i * 34, 300)); tb[i] = new TextBox { Left = 330, Top = 49 + i * 34, Width = 100, Text = vl[i], BorderStyle = BorderStyle.FixedSingle }; Theme.Style(tb[i]); f.Controls.Add(tb[i]); }
                var own = new CheckBox { Left = 16, Top = 262, Width = 480, Text = "Use the levels (Z) of the line's own vertices (ignore points)", Checked = !anyPts, Enabled = anyPts, ForeColor = Theme.Text, BackColor = Color.Transparent };
                var lab = new CheckBox { Left = 16, Top = 290, Width = 480, Text = "Label every point (number and level)", Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                var tab = new CheckBox { Left = 16, Top = 318, Width = 480, Text = "Add a table below with a summary (average, highest, lowest, height difference)", Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                Func<string, int, bool, CheckBox> cb = (t, y, c) => new CheckBox { Left = 16, Top = y, Width = 480, Text = t, Checked = c, ForeColor = Theme.Text, BackColor = Color.Transparent };
                var grid = cb("Show grid lines", 346, true); var drops = cb("Show a line from every point down to the datum", 374, true); var mark = cb("Mark the highest and lowest point on the profile", 402, true); var avgl = cb("Draw the average level as a line", 430, true); var glc = cb("Show the ground line, colour:", 458, true); glc.Width = 260; var glb = GLCombo(290, 456, 110); glc.CheckedChanged += (s2, e2) => glb.Enabled = glc.Checked;
                f.Controls.AddRange(new Control[] { own, lab, tab, grid, drops, mark, avgl, glc, glb });
                var info = new Label { Left = 16, Top = 492, Width = 490, Height = 36, ForeColor = Color.FromArgb(255, 150, 120), BackColor = Color.Transparent }; f.Controls.Add(info);
                var ok = Theme.Btn("Next", 300, 540, 100, true); var cancel = Theme.Btn("Cancel", 410, 540, 90);
                ok.Click += (s, e) =>
                {
                    double ve, iv, off, th2, dat = double.NaN;
                    if (!Theme.TryNum(tb[0].Text, out ve) || ve <= 0 || ve > 1000) { info.Text = "Exaggeration must be a number above 0 and at most 1000."; return; }
                    if (!Theme.TryNum(tb[1].Text, out iv) || iv <= 0) { info.Text = "Station interval must be a number above 0."; return; }
                    if (!Theme.TryNum(tb[2].Text, out off) || off < 0) { info.Text = "Offset must be a number, 0 or more."; return; }
                    if (tb[3].Text.Trim().Length > 0 && !Theme.TryNum(tb[3].Text, out dat)) { info.Text = "Datum must be a number or empty."; return; }
                    if (!Theme.TryNum(tb[4].Text, out th2) || th2 <= 0 || th2 > 50) { info.Text = "Text height must be above 0 and at most 50."; return; }
                    double hh = double.NaN; if (tb[5].Text.Trim().Length > 0 && (!Theme.TryNum(tb[5].Text, out hh) || hh <= 0)) { info.Text = "Frame height must be a number above 0, or empty."; return; }
                    if (length / iv > 400) { info.Text = "That gives more than 400 station marks. Use a bigger interval."; return; }
                    res.Ve = ve; res.Interval = iv; res.Offset = off; res.Datum = dat; res.TextH = th2; res.OwnZ = own.Checked; res.Label = lab.Checked; res.Table = tab.Checked; res.Height = hh; res.Grid = grid.Checked; res.Drops = drops.Checked; res.Mark = mark.Checked; res.Avg = avgl.Checked; res.GLine = glc.Checked; res.GCol = GLAci[glb.SelectedIndex]; good = true; f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.Add(ok); f.Controls.Add(cancel); f.AcceptButton = ok; f.CancelButton = cancel; AcApp.ShowModalDialog(f);
            }
            o = res; return good;
        }

        [CommandMethod("BPPROFILE", CommandFlags.UsePickSet)]
        public static void QuickProfile()
        {
            Guard("BPPROFILE", () =>
            {
                var doc = Doc; var ed = doc.Editor; var db = doc.Database;
                var eo = new PromptEntityOptions("\nSelect the line to cut the profile along (polyline, line or arc): "); eo.SetRejectMessage("\nOnly a polyline, line or arc."); eo.AddAllowedClass(typeof(Curve), false);
                var er = ed.GetEntity(eo); if (er.Status != PromptStatus.OK) return;
                var po = new PromptSelectionOptions { MessageForAdding = "\nSelect the points (AutoCAD points or Civil 3D COGO points). Press Enter to use the line's own vertex levels: " };
                var pre = Sel.Peek(ed); var psr = pre.Length > 0 ? Sel.Get(ed, po, "POINT,AECC_COGO_POINT", pre) : null;
                if (psr == null || psr.Status != PromptStatus.OK) { try { psr = Sel.Get(ed, po, "POINT,AECC_COGO_POINT"); } catch { psr = null; } }
                bool any = psr != null && psr.Status == PromptStatus.OK && psr.Value.Count > 0;
                var raw = new List<ProfPt>(); double length; ProfOpts o;
                using (doc.LockDocument())
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var cv = (Curve)tr.GetObject(er.ObjectId, OpenMode.ForRead); length = cv.GetDistanceAtParameter(cv.EndParam);
                        tr.Commit();
                    }
                    double scale = DrawingScale(db), th = ProfileTextHeight(scale, length);
                    if (!ProfileAsk(length, scale, th, any, out o)) return;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var cv = (Curve)tr.GetObject(er.ObjectId, OpenMode.ForRead); int skipped = 0, noz = 0;
                        if (any && !o.OwnZ)
                        {
                            var ids = new HashSet<ObjectId>(psr.Value.GetObjectIds());
                            foreach (var cp in SurveyCad.Collect(doc, ids))
                            {
                                if (!cp.P.HasZ) { noz++; continue; }
                                Point3d p = new Point3d(cp.P.E, cp.P.N, 0), q;
                                try { q = cv.GetClosestPointTo(p, Vector3d.ZAxis, false); } catch { skipped++; continue; }
                                double dx = p.X - q.X, dy = p.Y - q.Y, off = Math.Sqrt(dx * dx + dy * dy);
                                if (off > o.Offset) { skipped++; continue; }
                                double sta; try { sta = cv.GetDistAtPoint(q); } catch { skipped++; continue; }
                                raw.Add(new ProfPt { Id = cp.P.No ?? "", Sta = sta, Z = cp.P.Z, Off = off });
                            }
                            if (skipped > 0 || noz > 0) ed.WriteMessage("\n[B+] " + skipped + " point(s) are farther than " + o.Offset + " m from the line or off its ends and were left out" + (noz > 0 ? "; " + noz + " have no level" : "") + ".\n");
                        }
                        else
                        {
                            int n = 0;
                            foreach (var v in Verts(tr, cv))
                            {
                                n++; double sta; try { sta = cv.GetDistAtPoint(v); } catch { continue; }
                                raw.Add(new ProfPt { Id = n.ToString(CultureInfo.InvariantCulture), Sta = sta, Z = v.Z, Off = 0 });
                            }
                        }
                        tr.Commit();
                    }
                }
                if (raw.Count == 0) { ed.WriteMessage("\n[B+] No usable points: nothing within the offset of the line, or the line has no levels (all Z are 0 on a flat polyline)." + "\n"); return; }
                if (raw.Count == 1) { ed.WriteMessage("\n[B+] Only one point found; a profile needs at least two.\n"); return; }
                var ip = ed.GetPoint("\nPick the insertion point of the profile (its datum line starts here): "); if (ip.Status != PromptStatus.OK) return;
                var model0 = raw; var model = ProfileMath.Build(raw, o.Ve, o.Interval, o.TextH, o.Datum, o.Label, o.Table, new ProfStyle { Height = o.Height, Grid = o.Grid, Drops = o.Drops, MarkExtremes = o.Mark, AvgLine = o.Avg });
                int made = DrawProfile(doc, model, ip.Value, o.TextH, o.GLine, o.GCol);
                if (model.Summary.Count > 4) ed.WriteMessage("\n[B+] Average level " + model.Summary[1][2] + " m (of the points). Highest " + model.Summary[2][2] + " m at " + model.Summary[2][1] + ". Lowest " + model.Summary[3][2] + " m at " + model.Summary[3][1] + " (" + model.Summary[3][3] + ").\n");
                ed.WriteMessage("\n[B+] Profile drawn on layer B+_PROFILE: " + raw.Count + " points, line length " + length.ToString("F2", CultureInfo.InvariantCulture) + " m, datum " + model.Datum.ToString("F2", CultureInfo.InvariantCulture) + " m, exaggeration x" + o.Ve.ToString("0.##", CultureInfo.InvariantCulture) + ", " + made + " objects. It is a drawing: it does not update if the points move.\n");
            });
        }

        static int DrawProfile(Document doc, ProfModel m, Point3d at, double th, bool gline = true, short gcol = 3)
        {
            var db = doc.Database; int n = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lay = SurveyCad.EnsureLayer(tr, db, "B+_PROFILE", 7);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                Action<Entity, short> add = (en, col) => { en.LayerId = lay; en.Color = AcColor.FromColorIndex(ColorMethod.ByAci, col); ms.AppendEntity(en); tr.AddNewlyCreatedDBObject(en, true); n++; };
                Func<double, double, Point3d> P = (x, y) => new Point3d(at.X + x, at.Y + y, 0);
                foreach (var s in m.Segs) add(new Line(P(s.X1, s.Y1), P(s.X2, s.Y2)), s.Kind == "frame" ? (short)7 : s.Kind == "grid" ? (short)8 : s.Kind == "avg" ? (short)4 : s.Kind == "mark" ? (short)1 : (short)9);
                var pl = new Autodesk.AutoCAD.DatabaseServices.Polyline(); int i = 0;
                foreach (var g in m.Ground) pl.AddVertexAt(i++, new Point2d(at.X + g[0], at.Y + g[1]), 0, 0, 0);
                pl.ConstantWidth = 0; if (gline) add(pl, gcol);
                Action<ProfText, double> txt = (t, h) =>
                {
                    var d = new DBText { TextString = t.S, Height = h, Position = P(t.X, t.Y), Rotation = t.Rot * Math.PI / 180.0 };
                    d.HorizontalMode = t.Align == 0 ? TextHorizontalMode.TextLeft : t.Align == 1 ? TextHorizontalMode.TextCenter : TextHorizontalMode.TextRight; d.VerticalMode = TextVerticalMode.TextVerticalMid;
                    d.AlignmentPoint = P(t.X, t.Y); add(d, t.Kind == "pt" ? gcol : t.Kind == "avg" ? (short)4 : t.Kind == "mark" ? (short)1 : (short)7);
                };
                foreach (var t in m.Texts) txt(t, t.Kind == "title" ? th * 1.2 : th);
                if (m.Table.Count > 0)
                {
                    int rows = Math.Min(m.Table.Count, 201); double rh = th * 2, cw = th * 9, y0 = -th * 6, x0 = 0;
                    for (int r = 0; r <= rows; r++) add(new Line(P(x0, y0 - r * rh), P(x0 + cw * 4, y0 - r * rh)), 8);
                    for (int c = 0; c <= 4; c++) add(new Line(P(x0 + c * cw, y0), P(x0 + c * cw, y0 - rows * rh)), 8);
                    for (int r = 0; r < rows; r++) for (int c = 0; c < 4; c++) txt(new ProfText { X = x0 + c * cw + th * 0.4, Y = y0 - r * rh - rh / 2, S = m.Table[r][c], Align = 0, Kind = r == 0 ? "title" : "tab" }, th * 1.0);
                    double ySum = y0 - rows * rh - th * 3;
                    if (m.Table.Count > rows) ySum -= th * 2;
                    double[] cws = { cw * 1.6, cw, cw, cw * 1.4 }; double tw = cws.Sum();
                    for (int r = 0; r <= m.Summary.Count; r++) add(new Line(P(x0, ySum - r * rh), P(x0 + tw, ySum - r * rh)), 8);
                    double cx = x0; var xs2 = new List<double>(); xs2.Add(cx); foreach (var w in cws) { cx += w; xs2.Add(cx); }
                    foreach (double xx in xs2) add(new Line(P(xx, ySum), P(xx, ySum - m.Summary.Count * rh)), 8);
                    for (int r = 0; r < m.Summary.Count; r++) for (int c = 0; c < 4; c++) txt(new ProfText { X = xs2[c] + th * 0.4, Y = ySum - r * rh - rh / 2, S = m.Summary[r][c], Align = 0, Kind = r == 0 ? "title" : "tab" }, th * 1.1);
                    if (m.Table.Count > rows) txt(new ProfText { X = x0, Y = y0 - rows * rh - th * 1.5, S = "Table shows the first " + (rows - 1) + " of " + (m.Table.Count - 1) + " points.", Align = 0, Kind = "tab" }, th);
                }
                tr.Commit();
            }
            return n;
        }
    }
}
