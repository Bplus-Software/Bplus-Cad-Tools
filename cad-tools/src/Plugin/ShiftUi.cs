using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace BPlus.LiveMap
{
    static class ShiftUi
    {
        static string S3(double v) { return v.ToString("F3", CultureInfo.InvariantCulture); }
        static string Sg(double v) { return (v >= 0 ? "+" : "-") + Math.Abs(v).ToString("F3", CultureInfo.InvariantCulture) + " m"; }

        // Returns null on cancel. PickMode true: pick A and B. Otherwise D holds the typed offsets.
        public static bool Ask(int count, out bool pickMode, out double[] d)
        {
            pickMode = false; d = new double[3];
            using (var f = Theme.Base("Shift selection", 520, 360))
            {
                f.Controls.Add(Theme.Lbl("Mode", 20, 14, 200, true));
                var r1 = new RadioButton { Text = "Type the offsets", Left = 24, Top = 36, Width = 460, Height = 22, ForeColor = Theme.Text, Checked = true };
                var r2 = new RadioButton { Text = "From point A to point B  (take distance and level difference)", Left = 24, Top = 64, Width = 470, Height = 22, ForeColor = Theme.Text };
                f.Controls.Add(r1); f.Controls.Add(r2);
                f.Controls.Add(Theme.Lbl("You pick A, then B in the drawing. The selection moves by B minus A.", 46, 90, 450, true));
                f.Controls.Add(Theme.Lbl("Typed offsets (used only in the first mode)", 20, 128, 400, true));
                var tb = new TextBox[3]; string[] nm = { "dE (m)", "dN (m)", "dZ (m)" };
                for (int i = 0; i < 3; i++)
                {
                    f.Controls.Add(Theme.Lbl(nm[i], 20 + i * 160, 154, 140, true));
                    tb[i] = new TextBox { Left = 20 + i * 160, Top = 176, Width = 140, Text = "0.000", BorderStyle = BorderStyle.FixedSingle }; Theme.Style(tb[i]); f.Controls.Add(tb[i]);
                }
                r2.CheckedChanged += (s, e) => { foreach (var t in tb) t.Enabled = !r2.Checked; };
                f.Controls.Add(Theme.Lbl("Selected: " + count + " object" + (count == 1 ? "" : "s"), 20, 232, 300, true));
                var info = new Label { Left = 20, Top = 258, Width = 480, Height = 20, ForeColor = Color.FromArgb(255, 150, 120), BackColor = Color.Transparent };
                f.Controls.Add(info);
                var ok = Theme.Btn("Next", 300, 306, 100, true); var cancel = Theme.Btn("Cancel", 410, 306, 90);
                bool good = false; bool pm = false; var dd = new double[3];
                ok.Click += (s, e) =>
                {
                    pm = r2.Checked;
                    if (!pm)
                        for (int i = 0; i < 3; i++)
                            if (!double.TryParse(tb[i].Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out dd[i])) { info.Text = "Type numbers only in " + nm[i] + "."; return; }
                    good = true; f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.Add(ok); f.Controls.Add(cancel); f.AcceptButton = ok; f.CancelButton = cancel;
                f.ShowDialog();
                pickMode = pm; d = dd; return good;
            }
        }

        public static bool Confirm(double[] a, double[] b, int count)
        {
            double dx = b[0] - a[0], dy = b[1] - a[1], dz = b[2] - a[2], h = Math.Sqrt(dx * dx + dy * dy);
            using (var f = Theme.Base("Shift: check before applying", 520, 380))
            {
                f.Controls.Add(Theme.Lbl("Measured from point A to point B", 20, 14, 400, true));
                f.Controls.Add(Theme.Lbl("A   E " + S3(a[0]) + "   N " + S3(a[1]) + "   Z " + S3(a[2]), 20, 40, 480));
                f.Controls.Add(Theme.Lbl("B   E " + S3(b[0]) + "   N " + S3(b[1]) + "   Z " + S3(b[2]), 20, 62, 480));
                string[] k = { "dX (east)", "dY (north)", "dZ (level)", "Horizontal distance" }; string[] v = { Sg(dx), Sg(dy), Sg(dz), S3(h) + " m" };
                for (int i = 0; i < 4; i++)
                {
                    f.Controls.Add(Theme.Lbl(k[i], 20, 104 + i * 26, 190, true));
                    f.Controls.Add(new Label { Text = v[i], Left = 220, Top = 104 + i * 26, Width = 240, Height = 20, ForeColor = Theme.Text, BackColor = Color.Transparent, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
                }
                f.Controls.Add(Theme.Lbl("The " + count + " selected object" + (count == 1 ? "" : "s") + " will move by dX, dY, dZ.", 20, 222, 480));
                f.Controls.Add(Theme.Lbl("One UNDO reverses the whole shift.", 20, 246, 480, true));
                var ok = Theme.Btn("Apply", 300, 328, 100, true); var cancel = Theme.Btn("Cancel", 410, 328, 90);
                ok.Click += (s, e) => f.DialogResult = DialogResult.OK; cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.Add(ok); f.Controls.Add(cancel); f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK;
            }
        }
    }
}
