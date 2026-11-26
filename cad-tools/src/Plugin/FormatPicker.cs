using System.Drawing;
using System.Windows.Forms;

namespace BPlus.LiveMap
{
    static class FormatPicker
    {
        static readonly string[] Names = { "TXT / CSV", "XLSX", "SDR", "GSI", "KML / KMZ" };
        static readonly string[] ImpSub = { "point no, E, N, Z, code", "Excel sheet, first sheet", "Sokkia / Trimble SDR", "Leica GSI-8 / GSI-16", "Google Earth" };
        static readonly string[] ExpSub = { "clean text, one line per point", "Excel file", "Sokkia / Trimble SDR", "Leica GSI-16", "points and polylines" };
        static readonly bool[] Unver = { false, false, true, true, false };

        // Returns 0 TXT/CSV, 1 XLSX, 2 SDR, 3 GSI, 4 KML, or -1 when cancelled.
        public static int Ask(bool import)
        {
            using (var f = Theme.Base(import ? "Import points" : "Export points", 560, 430))
            {
                f.Controls.Add(Theme.Lbl("Format", 20, 14, 200, true));
                var rbs = new RadioButton[5];
                for (int i = 0; i < 5; i++)
                {
                    int y = 38 + i * 40;
                    var row = new Panel { Left = 20, Top = y, Width = 520, Height = 34, BackColor = Theme.Panel };
                    var rb = new RadioButton { Left = 12, Top = 7, Width = 150, Text = Names[i], ForeColor = Theme.Text, BackColor = Color.Transparent, Checked = i == 0, AutoSize = false, Height = 20 };
                    var sub = Theme.Lbl(import ? ImpSub[i] : ExpSub[i], 175, 7, 220, true);
                    row.Controls.Add(rb); row.Controls.Add(sub);
                    if (Unver[i]) row.Controls.Add(new Label { Text = "unverified", Left = 430, Top = 7, Width = 80, Height = 20, ForeColor = Color.FromArgb(230, 160, 40), BackColor = Color.Transparent });
                    row.Click += (s, e) => rb.Checked = true; sub.Click += (s, e) => rb.Checked = true;
                    rbs[i] = rb; f.Controls.Add(row);
                }
                f.Controls.Add(Theme.Lbl(import ? "After you choose, the file opens in the import window (columns, codes, layers)." : "Next: the usual export options (order, categories, file name).", 20, 242, 520, true));
                f.Controls.Add(new Label { Text = "Unverified = written from the format spec, not yet tested with a real instrument file.", Left = 20, Top = 264, Width = 520, Height = 20, ForeColor = Color.FromArgb(230, 160, 40), BackColor = Color.Transparent });
                var ok = Theme.Btn(import ? "Choose file..." : "Export...", 350, 384, 100, true); var cancel = Theme.Btn("Cancel", 460, 384, 80);
                int result = -1;
                ok.Click += (s, e) => { for (int i = 0; i < 5; i++) if (rbs[i].Checked) result = i; f.DialogResult = DialogResult.OK; };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.Add(ok); f.Controls.Add(cancel); f.AcceptButton = ok; f.CancelButton = cancel;
                f.ShowDialog();
                return result;
            }
        }
    }
}
