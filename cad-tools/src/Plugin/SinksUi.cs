using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using BPlus.LiveMap.Core;

namespace BPlus.LiveMap
{
    sealed class SinkChoice { public int Source; public double MinDepth = 0.05, MinArea = 10, Cell = 1; public bool Point = true, Leader = true, Outline = true, Low = true, Dir = true; public double Spacing; }

    static class SinksUi
    {
        static string N(double v, int d) { return v.ToString("F" + d, CultureInfo.InvariantCulture); }

        public static SinkChoice Ask()
        {
            using (var f = Theme.Base("B+ CAD Tools - Water collection points (sinks)", 520, 660))
            {
                f.Controls.Add(Theme.Lbl("Find sinks over the whole surface of", 16, 12, 480));
                var r = new[] { new RadioButton { Text = "A Civil 3D surface  (pick it)", Checked = true }, new RadioButton { Text = "Selected points, 3D faces and 3D polylines" }, new RadioButton { Text = "All points, 3D faces and 3D polylines in the drawing" } };
                for (int i = 0; i < 3; i++) { r[i].Left = 20; r[i].Top = 38 + i * 26; r[i].Width = 470; r[i].Height = 22; r[i].ForeColor = Theme.Text; r[i].BackColor = Color.Transparent; f.Controls.Add(r[i]); }
                f.Controls.Add(Theme.Lbl("Ignore sinks shallower than (m)", 16, 128, 220, true)); var d = new TextBox { Left = 16, Top = 150, Width = 80, Text = "0.050", BorderStyle = BorderStyle.FixedSingle }; Theme.Style(d);
                f.Controls.Add(Theme.Lbl("Ignore sinks smaller than (m\u00B2)", 250, 128, 240, true)); var a = new TextBox { Left = 250, Top = 150, Width = 80, Text = "10", BorderStyle = BorderStyle.FixedSingle }; Theme.Style(a);
                f.Controls.Add(Theme.Lbl("Grid cell used for the search (m)  smaller = more detail, slower", 16, 190, 480, true)); var c = new TextBox { Left = 16, Top = 212, Width = 80, Text = "1.000", BorderStyle = BorderStyle.FixedSingle }; Theme.Style(c);
                f.Controls.AddRange(new Control[] { d, a, c });
                f.Controls.Add(Theme.Lbl("Mark each sink with", 16, 256, 300));
                var k1 = new CheckBox { Text = "A point on layer BP-SINK", Left = 20, Top = 282, Width = 460, Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                var k2 = new CheckBox { Text = "A leader:  S1  E, N, Z  depth", Left = 20, Top = 308, Width = 460, Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                var k3 = new CheckBox { Text = "The outline of the pond (closed polyline at the spill level)", Left = 20, Top = 334, Width = 470, Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                f.Controls.AddRange(new Control[] { k1, k2, k3 });
                f.Controls.Add(Theme.Lbl("New", 16, 370, 300));
                var k4 = new CheckBox { Text = "Low point: a cross and the level (Z) at the lowest place of each sink", Left = 20, Top = 396, Width = 480, Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                var k5 = new CheckBox { Text = "Slope direction: arrows inside each sink pointing downhill", Left = 20, Top = 422, Width = 480, Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                f.Controls.AddRange(new Control[] { k4, k5 });
                f.Controls.Add(Theme.Lbl("Arrow spacing (m), auto = about 6 across", 44, 452, 250, true)); var sp = new TextBox { Left = 300, Top = 450, Width = 70, Text = "auto", BorderStyle = BorderStyle.FixedSingle }; Theme.Style(sp); f.Controls.Add(sp);
                f.Controls.Add(Theme.Lbl("Layers: BP-SINK-LOW and BP-SINK-DIR.", 44, 476, 440, true));
                f.Controls.Add(Theme.Lbl("Next you pick the surface (or select objects). You see the list of sinks before anything is drawn.", 16, 512, 490, true));
                f.Controls.Add(Theme.Lbl("A sink is a place where water cannot run out: the grid is filled from the edges and the filled parts are the sinks.", 16, 544, 490, true));
                var ok = Theme.Btn("Next", 312, 616, 100, true); var cancel = Theme.Btn("Cancel", 420, 616, 84);
                SinkChoice res = null;
                ok.Click += (s, e) =>
                {
                    double v1, v2, v3; Func<string, double> p = t => double.TryParse(t.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v3) ? v3 : -1;
                    v1 = p(d.Text); v2 = p(a.Text); v3 = p(c.Text);
                    double v4 = 0; if (sp.Text.Trim().ToLowerInvariant() != "auto" && sp.Text.Trim().Length > 0) { v4 = p(sp.Text); if (v4 <= 0) { MessageBox.Show(f, "Arrow spacing must be a number above 0, or auto.", "B+ CAD Tools"); return; } }
                    if (v1 < 0 || v2 < 0 || v3 <= 0) { MessageBox.Show(f, "Depth and area must be numbers (0 or more), the grid cell above 0.", "B+ CAD Tools"); return; }
                    res = new SinkChoice { Source = r[0].Checked ? 0 : r[1].Checked ? 1 : 2, MinDepth = v1, MinArea = v2, Cell = v3, Point = k1.Checked, Leader = k2.Checked, Outline = k3.Checked, Low = k4.Checked, Dir = k5.Checked, Spacing = v4 }; f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                var adv = new Panel { Left = 0, Top = 226, Width = 510, Height = 296, BackColor = Color.Transparent };
                var mv = new List<Control>(); foreach (Control c0 in f.Controls) if (c0.Top >= 190 && c0.Top <= 480) mv.Add(c0);
                var notes = new List<Control>(); foreach (Control c0 in f.Controls) if (c0.Top >= 500 && c0.Top <= 560) notes.Add(c0);
                foreach (var m0 in mv) { f.Controls.Remove(m0); m0.Top -= 190; adv.Controls.Add(m0); }
                f.Controls.Add(adv);
                var advBtn = Theme.Btn("Advanced options  (show)", 16, 190, 220); f.Controls.Add(advBtn);
                f.Controls.AddRange(new Control[] { ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                Action<bool> setAdv = open => { SectionUi.AdvOpen = open; adv.Visible = open; int d0 = open ? 296 : 0; notes[0].Top = 232 + d0; if (notes.Count > 1) notes[1].Top = 264 + d0; ok.Top = 306 + d0; cancel.Top = 306 + d0; f.Height = 350 + d0; advBtn.Text = open ? "Advanced options  (hide)" : "Advanced options  (show)"; };
                advBtn.Click += (s, e) => setAdv(!SectionUi.AdvOpen);
                setAdv(SectionUi.AdvOpen);
                return f.ShowDialog() == DialogResult.OK ? res : null;
            }
        }

        public static string TableText(List<Sink> s)
        {
            var sb = new StringBuilder("No\tE\tN\tLowest Z\tSpill Z\tDepth (m)\tArea (m2)\tVolume (m3)\r\n");
            foreach (var k in s) sb.Append("S" + k.No + "\t" + N(k.E, 3) + "\t" + N(k.N, 3) + "\t" + N(k.LowZ, 3) + "\t" + N(k.SpillZ, 3) + "\t" + N(k.Depth, 3) + "\t" + N(k.Area, 1) + "\t" + N(k.Volume, 1) + "\r\n");
            return sb.ToString();
        }

        // true = mark in the drawing
        public static bool Result(List<Sink> sinks, int ignored, string note)
        {
            using (var f = Theme.Base("B+ CAD Tools - Sinks found: " + sinks.Count, 780, 420))
            {
                double vol = 0, md = 0; foreach (var k in sinks) { vol += k.Volume; md = Math.Max(md, k.Depth); }
                f.Controls.Add(Theme.Lbl(sinks.Count + " sinks, largest depth " + N(md, 3) + " m, total volume " + N(vol, 1) + " m\u00B3" + (ignored > 0 ? ", " + ignored + " small one(s) ignored." : "."), 16, 12, 740));
                var lv = new ListView { Left = 16, Top = 40, Width = 748, Height = 280, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, MultiSelect = true, HideSelection = false }; Theme.Style(lv); Theme.DarkList(lv);
                string[] h = { "No", "E", "N", "Lowest Z", "Spill Z", "Depth (m)", "Area (m\u00B2)", "Volume (m\u00B3)" }; int[] w = { 50, 100, 110, 85, 85, 85, 90, 100 };
                for (int i = 0; i < h.Length; i++) lv.Columns.Add(h[i], w[i]);
                foreach (var k in sinks) lv.Items.Add(new ListViewItem(new[] { "S" + k.No, N(k.E, 3), N(k.N, 3), N(k.LowZ, 3), N(k.SpillZ, 3), N(k.Depth, 3), N(k.Area, 1), N(k.Volume, 1) }));
                f.Controls.Add(lv);
                f.Controls.Add(Theme.Lbl(note, 16, 326, 748, true));
                var mark = Theme.Btn("Mark in drawing", 16, 372, 150, true); var copy = Theme.Btn("Copy table", 176, 372, 120); var close = Theme.Btn("Close", 674, 372, 90);
                copy.Click += (s, e) => { try { Clipboard.SetText(TableText(sinks)); copy.Text = "Copied"; } catch { copy.Text = "Copy failed"; } };
                mark.Click += (s, e) => f.DialogResult = DialogResult.OK; close.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { mark, copy, close }); f.CancelButton = close;
                return f.ShowDialog() == DialogResult.OK;
            }
        }
    }
}
