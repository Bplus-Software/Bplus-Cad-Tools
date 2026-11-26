using System;
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

        public static CutFillChoice Ask()
        {
            using (var f = Theme.Base("B+ CAD Tools - Cut and fill volumes", 540, 640))
            {
                f.Controls.Add(Theme.Lbl("Existing ground", 16, 12, 400));
                var e0 = Rb("A Civil 3D surface  (pick it)", 38, true, 300); var e1 = Rb("Points, 3D faces, 3D polylines  (select them)", 64, false, 400);
                f.Controls.Add(Theme.Lbl("Compare with", 16, 102, 400));
                var p0 = Rb("Another surface  (Civil 3D, or points / 3D faces)", 128, false, 440); var p1 = Rb("A flat level", 154, true, 160); var p2 = Rb("A sloping plane: grade", 180, false, 170);
                var lv = Tb("0.000", 200, 150, 90); var gr = Tb("1.0", 200, 176, 60);
                f.Controls.Add(Theme.Lbl("m", 298, 156, 30, true)); f.Controls.Add(Theme.Lbl("%  (you pick a point, a direction, and the level at the point)", 266, 182, 270, true));
                f.Controls.Add(Theme.Lbl("Area to calculate", 16, 220, 400));
                var a0 = Rb("Where the two overlap  (the whole surface)", 246, false, 440); var a1 = Rb("Inside a closed polyline  (pick it)", 272, true, 440);
                f.Controls.Add(Theme.Lbl("Grid cell used (m)", 16, 312, 200, true)); var c = Tb("1.000", 16, 334, 80); f.Controls.Add(Theme.Lbl("smaller = slower", 104, 338, 110, true));
                f.Controls.Add(Theme.Lbl("Swell / shrink factor (optional)", 250, 312, 270, true)); var sw = Tb("1.00", 250, 334, 80);
                f.Controls.Add(Theme.Lbl("Cut = existing ground above the comparison. Fill = below it.", 16, 384, 500, true));
                f.Controls.Add(Theme.Lbl("The factor multiplies the cut volume in the Net line only. 1.00 = no change.", 16, 410, 500, true));
                f.Controls.Add(Theme.Lbl("The report is shown first. Nothing is drawn unless you ask for the table.", 16, 436, 500, true));
                // Each group of radio buttons sits in its own panel, otherwise WinForms makes all seven exclusive together.
                Action<int, int, Control[]> grp = (top, h, items) => { var pn = new Panel { Left = 0, Top = top, Width = 540, Height = h, BackColor = Color.Transparent }; foreach (var it in items) { it.Top -= top; pn.Controls.Add(it); } f.Controls.Add(pn); };
                grp(34, 56, new Control[] { e0, e1 }); grp(124, 84, new Control[] { p0, p1, p2, lv, gr }); grp(242, 56, new Control[] { a0, a1 });
                f.Controls.AddRange(new Control[] { c, sw });
                var ok = Theme.Btn("Report", 322, 596, 120, true); var cancel = Theme.Btn("Cancel", 452, 596, 72); CutFillChoice res = null;
                ok.Click += (s, e) =>
                {
                    double v; Func<TextBox, double> g = t => double.TryParse(t.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
                    double cell = g(c), sf = g(sw), lev = g(lv), grd = g(gr);
                    if (double.IsNaN(cell) || cell <= 0) { MessageBox.Show(f, "The grid cell must be a number above 0.", "B+ CAD Tools"); return; }
                    if (double.IsNaN(sf) || sf <= 0) { MessageBox.Show(f, "The factor must be a number above 0 (1.00 = none).", "B+ CAD Tools"); return; }
                    if (p1.Checked && double.IsNaN(lev)) { MessageBox.Show(f, "Type the flat level as a number.", "B+ CAD Tools"); return; }
                    if (p2.Checked && double.IsNaN(grd)) { MessageBox.Show(f, "Type the grade as a number (%).", "B+ CAD Tools"); return; }
                    res = new CutFillChoice { Existing = e0.Checked ? 0 : 1, Proposed = p0.Checked ? 0 : p1.Checked ? 1 : 2, Area = a0.Checked ? 0 : 1, Level = double.IsNaN(lev) ? 0 : lev, Grade = double.IsNaN(grd) ? 0 : grd, Cell = cell, Swell = sf }; f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? res : null;
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
