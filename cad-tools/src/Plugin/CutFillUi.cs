using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using BPlus.LiveMap.Core;

namespace BPlus.LiveMap
{
    sealed class CutFillChoice { public int Existing, Proposed, Area; public double Level, Grade, Cell = 1, Swell = 1; }

    static class CutFillUi
    {
        static string N(double v, int d) { return v.ToString("N" + d, CultureInfo.InvariantCulture); }
        static string P(double v, int d) { return v.ToString("F" + d, CultureInfo.InvariantCulture); }
        static RadioButton Rb(string t, int top, bool on, int w) { return new RadioButton { Text = t, Left = 20, Top = top, Width = w, Height = 22, Checked = on, ForeColor = Theme.Text, BackColor = Color.Transparent }; }
        static TextBox Tb(string t, int l, int top, int w) { var x = new TextBox { Left = l, Top = top, Width = w, Text = t, BorderStyle = BorderStyle.FixedSingle }; Theme.Style(x); return x; }

        /// <summary>select(key) hides the dialog, lets the user pick in the drawing and returns a status text, or null if nothing was picked.
        /// Keys: ex_surf, ex_pts, cmp_surf, cmp_pts, poly. status holds what is already selected.</summary>
        public static CutFillChoice Ask(Func<string, string> select, Dictionary<string, string> status, int startExisting)
        {
            using (var f = Theme.Base("B+ CAD Tools - Cut and fill volumes", 620, 640))
            {
                var labels = new Dictionary<string, Label>(); var green = Color.FromArgb(120, 210, 140);
                Func<string, int, int, Control[]> pick = (key, x, y) =>
                {
                    var b = Theme.Btn("Select <", x, y, 100); var l = Theme.Lbl("", x + 112, y + 5, 190, true); labels[key] = l;
                    Action refresh = () => { string st; bool has = status.TryGetValue(key, out st) && !string.IsNullOrEmpty(st); l.Text = has ? st : "nothing selected yet"; l.ForeColor = has ? green : Theme.Dim; };
                    refresh();
                    b.Click += (s, e) =>
                    {
                        f.Hide(); string r = null; try { r = select(key); } catch (System.Exception ex) { r = null; MessageBox.Show("Selection failed: " + ex.Message, "B+ CAD Tools"); }
                        if (r != null) status[key] = r; refresh(); f.Show(); f.Activate();
                    };
                    return new Control[] { b, l };
                };
                f.Controls.Add(Theme.Lbl("1   Existing ground", 16, 12, 400));
                var e0 = Rb("Civil 3D surface", 38, startExisting == 0, 260); var e1 = Rb("Points, 3D faces, 3D polylines", 66, startExisting == 1, 270);
                f.Controls.Add(Theme.Lbl("2   Compare with", 16, 104, 400));
                var c0 = Rb("Civil 3D surface", 130, false, 260); var c1 = Rb("Points, 3D faces, 3D polylines", 158, false, 270); var c2 = Rb("A flat level", 186, true, 270); var c3 = Rb("A sloping plane", 214, false, 270);
                var lv = Tb("0.000", 300, 184, 90); var gr = Tb("1.0", 300, 212, 60);
                var lm = Theme.Lbl("m", 398, 190, 30, true); var gl = Theme.Lbl("%   then you pick a point, a direction and the level", 368, 218, 240, true);
                f.Controls.Add(Theme.Lbl("3   Area to calculate", 16, 256, 400));
                var a0 = Rb("Where the two overlap  (the whole surface)", 282, false, 440); var a1 = Rb("Inside a closed polyline", 308, true, 270);
                // Each group of radio buttons sits in its own panel, otherwise WinForms makes all of them exclusive together.
                Action<int, int, Control[]> grp = (top, h, items) => { var pn = new Panel { Left = 0, Top = top, Width = 620, Height = h, BackColor = Color.Transparent }; foreach (var it in items) { it.Top -= top; pn.Controls.Add(it); } f.Controls.Add(pn); };
                var l1 = new List<Control> { e0, e1 }; l1.AddRange(pick("ex_surf", 300, 36)); l1.AddRange(pick("ex_pts", 300, 64)); grp(34, 60, l1.ToArray());
                var l2 = new List<Control> { c0, c1, c2, c3, lv, gr, lm, gl }; l2.AddRange(pick("cmp_surf", 300, 128)); l2.AddRange(pick("cmp_pts", 300, 156)); grp(126, 120, l2.ToArray());
                var l3 = new List<Control> { a0, a1 }; l3.AddRange(pick("poly", 300, 306)); grp(278, 56, l3.ToArray());
                f.Controls.Add(Theme.Lbl("Grid cell (m)", 16, 352, 200, true)); var c = Tb("1.000", 16, 374, 80); f.Controls.Add(Theme.Lbl("smaller = slower", 104, 378, 110, true));
                f.Controls.Add(Theme.Lbl("Swell / shrink factor (optional)", 250, 352, 270, true)); var sw = Tb("1.00", 250, 374, 80);
                f.Controls.AddRange(new Control[] { c, sw });
                f.Controls.Add(Theme.Lbl("Select < hides this window, you pick in the drawing, then you come back here.", 16, 424, 580, true));
                f.Controls.Add(Theme.Lbl("Cut = existing ground above the comparison. Fill = below it. Net uses the factor on the cut only.", 16, 448, 580, true));
                f.Controls.Add(Theme.Lbl("The report is shown first. Nothing is drawn unless you ask for the table.", 16, 472, 580, true));
                var ok = Theme.Btn("Report", 382, 596, 120, true); var cancel = Theme.Btn("Cancel", 512, 596, 92); CutFillChoice res = null;
                Func<string, bool> has = k => { string st; return status.TryGetValue(k, out st) && !string.IsNullOrEmpty(st); };
                ok.Click += (s, e) =>
                {
                    double v; Func<TextBox, double> g = t => double.TryParse(t.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
                    double cell = g(c), sf = g(sw), lev = g(lv), grd = g(gr);
                    if (double.IsNaN(cell) || cell <= 0) { MessageBox.Show(f, "The grid cell must be a number above 0.", "B+ CAD Tools"); return; }
                    if (double.IsNaN(sf) || sf <= 0) { MessageBox.Show(f, "The factor must be a number above 0 (1.00 = none).", "B+ CAD Tools"); return; }
                    if (c2.Checked && double.IsNaN(lev)) { MessageBox.Show(f, "Type the flat level as a number.", "B+ CAD Tools"); return; }
                    if (c3.Checked && double.IsNaN(grd)) { MessageBox.Show(f, "Type the grade as a number (%).", "B+ CAD Tools"); return; }
                    if (!has(e0.Checked ? "ex_surf" : "ex_pts")) { MessageBox.Show(f, "Existing ground: press Select < next to the ticked row first.", "B+ CAD Tools"); return; }
                    if ((c0.Checked && !has("cmp_surf")) || (c1.Checked && !has("cmp_pts"))) { MessageBox.Show(f, "Compare with: press Select < next to the ticked row first.", "B+ CAD Tools"); return; }
                    if (a1.Checked && !has("poly")) { MessageBox.Show(f, "Area: press Select < to pick the closed polyline first.", "B+ CAD Tools"); return; }
                    res = new CutFillChoice { Existing = e0.Checked ? 0 : 1, Proposed = c0.Checked ? 0 : c1.Checked ? 1 : c2.Checked ? 2 : 3, Area = a0.Checked ? 0 : 1, Level = double.IsNaN(lev) ? 0 : lev, Grade = double.IsNaN(grd) ? 0 : grd, Cell = cell, Swell = sf }; f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                return Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(f) == DialogResult.OK ? res : null;
            }
        }

        public static string TableText(CutFillResult r, string info)
        {
            var sb = new StringBuilder("\tArea (m2)\tVolume (m3)\tMean depth (m)\r\n");
            sb.Append("Cut (excavation)\t" + P(r.CutArea, 1) + "\t" + P(r.CutVolume, 1) + "\t" + P(r.MeanCut, 2) + "\r\n");
            sb.Append("Fill (embankment)\t" + P(r.FillArea, 1) + "\t" + P(r.FillVolume, 1) + "\t" + P(r.MeanFill, 2) + "\r\n");
            sb.Append("Net (" + (r.Net >= 0 ? "cut" : "fill") + ")\t\t" + P(Math.Abs(r.Net), 1) + "\t\r\n");
            if (Math.Abs(r.Swell - 1) > 1e-9) sb.Append("Swell factor on cut\t\t" + P(r.Swell, 2) + "\t\r\n");
            sb.Append(info.Replace("\n", " ") + "\r\n");
            return sb.ToString();
        }

        // true = put the table in the drawing
        public static bool Result(CutFillResult r, string info, string note)
        {
            using (var f = Theme.Base("B+ CAD Tools - Cut and fill report", 600, 400))
            {
                var lv = new ListView { Left = 16, Top = 16, Width = 568, Height = 120, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, HideSelection = false }; Theme.Style(lv); Theme.DarkList(lv);
                string[] h = { "", "Area (m\u00B2)", "Volume (m\u00B3)", "Mean depth (m)" }; int[] w = { 190, 120, 130, 120 };
                for (int i = 0; i < h.Length; i++) lv.Columns.Add(h[i], w[i]);
                lv.Items.Add(new ListViewItem(new[] { "Cut (excavation)", N(r.CutArea, 1), N(r.CutVolume, 1), P(r.MeanCut, 2) }));
                lv.Items.Add(new ListViewItem(new[] { "Fill (embankment)", N(r.FillArea, 1), N(r.FillVolume, 1), P(r.MeanFill, 2) }));
                lv.Items.Add(new ListViewItem(new[] { "Net: " + (r.Net >= 0 ? "cut (spoil)" : "fill needed"), "", N(Math.Abs(r.Net), 1), "" }));
                f.Controls.Add(lv);
                f.Controls.Add(Theme.Lbl(info, 16, 150, 568, true)); f.Controls.Add(Theme.Lbl(note, 16, 214, 568, true));
                var tb = Theme.Btn("Table in drawing", 16, 346, 150, true); var copy = Theme.Btn("Copy", 176, 346, 110); var close = Theme.Btn("Close", 494, 346, 90);
                copy.Click += (s, e) => { try { Clipboard.SetText(TableText(r, info)); copy.Text = "Copied"; } catch { copy.Text = "Copy failed"; } };
                tb.Click += (s, e) => f.DialogResult = DialogResult.OK; close.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { tb, copy, close }); f.CancelButton = close;
                return f.ShowDialog() == DialogResult.OK;
            }
        }
    }
}
