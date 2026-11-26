using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using BPlus.LiveMap.Core;

namespace BPlus.LiveMap
{
    sealed class JoinChoice { public double Tol = 0.5; public JoinMode Mode = JoinMode.Extend; public bool Keep; }
    sealed class TrimChoice { public bool KeepInside = true, Curves = true, Points = true, Blocks = true, KeepRemoved; }

    static class JoinTrimUi
    {
        static CheckBox Chk(string t, int x, int y, int w, bool on) { return new CheckBox { Text = t, Left = x, Top = y, Width = w, Height = 22, Checked = on, ForeColor = Theme.Text, BackColor = Color.Transparent }; }
        static RadioButton Rad(string t, int x, int y, int w, bool on) { return new RadioButton { Text = t, Left = x, Top = y, Width = w, Height = 22, Checked = on, ForeColor = Theme.Text, BackColor = Color.Transparent }; }

        // preview returns the one-line check result for the current choices.
        public static JoinChoice Join(int shapes, Func<JoinChoice, string[]> preview)
        {
            using (var f = Theme.Base("B+ CAD Tools - Join polylines", 560, 500))
            {
                f.Controls.Add(Theme.Lbl(shapes + " shapes selected (lines, polylines, arcs).", 16, 12, 520));
                f.Controls.Add(Theme.Lbl("Close gaps up to", 16, 44, 300));
                var tol = new TextBox { Left = 16, Top = 68, Width = 80, Text = "0.500", BorderStyle = BorderStyle.FixedSingle }; Theme.Style(tol);
                f.Controls.Add(tol); f.Controls.Add(Theme.Lbl("m  (distance between two ends)", 104, 71, 300, true));
                f.Controls.Add(Theme.Lbl("How to close a gap", 16, 108, 300));
                var r1 = Rad("Extend to meet: continue both ends until they cross, then join", 16, 134, 530, true);
                var r2 = Rad("Join straight across: add a short segment between the two ends", 16, 160, 530, false);
                var r3 = Rad("Only join ends that already touch (tolerance 0.001 m)", 16, 186, 530, false);
                f.Controls.AddRange(new Control[] { r1, r2, r3 });
                f.Controls.Add(Theme.Lbl("If the extended lines do not cross within the gap distance (or an end is an arc), that gap is joined straight across.", 40, 210, 510, true));
                var keep = Chk("Keep the original shapes (default: they are replaced)", 16, 244, 520, false); f.Controls.Add(keep);
                f.Controls.Add(Theme.Lbl("Result of the check (before anything is changed)", 16, 284, 520));
                var l1 = Theme.Lbl("", 24, 310, 520); var l2 = Theme.Lbl("", 24, 332, 520); var l3 = new Label { Left = 24, Top = 354, Width = 520, Height = 20, ForeColor = Color.FromArgb(255, 170, 60), BackColor = Color.Transparent };
                f.Controls.AddRange(new Control[] { l1, l2, l3 });
                f.Controls.Add(Theme.Lbl("One UNDO reverses everything. New polylines keep the layer and colour of the first shape joined.", 16, 392, 530, true));
                var ok = Theme.Btn("Join", 350, 448, 100, true); var cancel = Theme.Btn("Cancel", 460, 448, 80);
                Func<JoinChoice> read = () =>
                {
                    double t; if (!double.TryParse(tol.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out t) || t < 0) return null;
                    return new JoinChoice { Tol = t, Mode = r1.Checked ? JoinMode.Extend : r2.Checked ? JoinMode.Straight : JoinMode.Touch, Keep = keep.Checked };
                };
                Action refresh = () =>
                {
                    var c = read(); if (c == null) { l1.Text = "Type the gap as a number."; l2.Text = ""; l3.Text = ""; return; }
                    var t = preview(c); l1.Text = t[0]; l2.Text = t[1]; l3.Text = t[2];
                };
                tol.TextChanged += (s, e) => refresh(); r1.CheckedChanged += (s, e) => refresh(); r2.CheckedChanged += (s, e) => refresh(); r3.CheckedChanged += (s, e) => refresh();
                refresh();
                JoinChoice res = null;
                ok.Click += (s, e) => { res = read(); if (res == null) { MessageBox.Show(f, "The gap must be a number, zero or more.", "B+ CAD Tools"); return; } f.DialogResult = DialogResult.OK; };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.Add(ok); f.Controls.Add(cancel); f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? res : null;
            }
        }

        public static TrimChoice Trim(string boundaryInfo, int selected, Func<TrimChoice, string> preview)
        {
            using (var f = Theme.Base("B+ CAD Tools - Trim to boundary", 580, 500))
            {
                f.Controls.Add(Theme.Lbl(boundaryInfo, 16, 12, 540));
                f.Controls.Add(Theme.Lbl("Keep", 16, 40, 200));
                var k1 = Rad("What is inside the boundary", 16, 64, 400, true); var k2 = Rad("What is outside the boundary", 16, 90, 400, false);
                f.Controls.Add(k1); f.Controls.Add(k2);
                f.Controls.Add(Theme.Lbl("Trim these (" + selected + " objects selected)", 16, 126, 400));
                var c1 = Chk("Lines, polylines, arcs, circles (cut at the boundary)", 16, 150, 540, true);
                var c2 = Chk("Points and text (removed when on the wrong side)", 16, 176, 540, true);
                var c3 = Chk("Blocks (kept whole if the insertion point is on the kept side)", 16, 202, 540, true);
                f.Controls.AddRange(new Control[] { c1, c2, c3 });
                f.Controls.Add(Theme.Lbl("Hatches are not trimmed in this version: they stay as they are.", 16, 232, 540, true));
                var keepR = Chk("Keep the trimmed-away parts on layer BP-TRIMMED (switched off) instead of deleting", 16, 262, 550, true); f.Controls.Add(keepR);
                f.Controls.Add(Theme.Lbl("Keep inside: only objects that cross the boundary are cut. Nothing outside is touched.", 16, 296, 550, true));
                f.Controls.Add(Theme.Lbl("Keep outside: objects inside are removed, crossing ones are cut. The boundary itself is not changed. One UNDO.", 16, 318, 550, true));
                var res = new Label { Left = 16, Top = 350, Width = 548, Height = 30, ForeColor = Color.FromArgb(120, 210, 140), BackColor = Color.Transparent, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
                f.Controls.Add(res);
                Func<TrimChoice> read = () => new TrimChoice { KeepInside = k1.Checked, Curves = c1.Checked, Points = c2.Checked, Blocks = c3.Checked, KeepRemoved = keepR.Checked };
                Action refresh = () => { res.Text = "Check first: " + preview(read()); };
                k1.CheckedChanged += (s, e) => refresh(); c1.CheckedChanged += (s, e) => refresh(); c2.CheckedChanged += (s, e) => refresh(); c3.CheckedChanged += (s, e) => refresh();
                refresh();
                var ok = Theme.Btn("Trim", 350, 448, 100, true); var cancel = Theme.Btn("Cancel", 460, 448, 80);
                ok.Click += (s, e) => f.DialogResult = DialogResult.OK; cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.Add(ok); f.Controls.Add(cancel); f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? read() : null;
            }
        }
    }
}
