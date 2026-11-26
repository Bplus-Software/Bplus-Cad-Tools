using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace BPlus.LiveMap
{
    static class MeasureUi
    {
        /// <summary>Shows measured rows (name, value). true = write the result in the drawing.</summary>
        public static bool Show(string title, List<string[]> rows, string note, bool canWrite = true)
        {
            int h = 110 + rows.Count * 24 + (string.IsNullOrEmpty(note) ? 0 : 28);
            using (var f = Theme.Base("B+ CAD Tools - " + title, 600, h + 44))
            {
                int y = 14;
                foreach (var r in rows) { f.Controls.Add(Theme.Lbl(r[0], 16, y, 200, true)); f.Controls.Add(Theme.Lbl(r[1], 220, y, 360)); y += 24; }
                if (!string.IsNullOrEmpty(note)) { y += 6; f.Controls.Add(Theme.Lbl(note, 16, y, 560, true)); y += 22; }
                y += 14;
                var copy = Theme.Btn("Copy", 16, y, 100); var wr = Theme.Btn("Write in drawing", 126, y, 150); var close = Theme.Btn("Close", 486, y, 90, true);
                copy.Click += (s, e) => { var sb = new StringBuilder(); foreach (var r in rows) sb.Append(r[0] + "\t" + r[1] + "\r\n"); try { Clipboard.SetText(sb.ToString()); copy.Text = "Copied"; } catch { copy.Text = "Copy failed"; } };
                wr.Visible = canWrite; wr.Click += (s, e) => f.DialogResult = DialogResult.OK; close.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { copy, wr, close }); f.CancelButton = close;
                return Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(f) == DialogResult.OK;
            }
        }
    }
}
