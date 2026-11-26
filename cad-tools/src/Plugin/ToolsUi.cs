using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace BPlus.LiveMap
{
    sealed class LenRow { public object Tag; public string Layer = "", Kind = ""; public double Len, Area; public bool Closed; }

    static class ToolsUi
    {
        static string N3(double v) { return v.ToString("F3", CultureInfo.InvariantCulture); }
        static string Q(string s) { return "\"" + (s ?? "").Replace("\"", "\"\"") + "\""; }

        public static string LengthsCsv(List<LenRow> rows)
        {
            var sb = new StringBuilder("No,Layer,Type,Length (m),Area (m2),Closed\r\n"); int i = 0; double tl = 0, ta = 0;
            foreach (var r in rows) { i++; tl += r.Len; if (r.Closed) ta += r.Area; sb.Append(i).Append(',').Append(Q(r.Layer)).Append(',').Append(Q(r.Kind)).Append(',').Append(N3(r.Len)).Append(',').Append(r.Closed ? N3(r.Area) : "").Append(',').Append(r.Closed ? "Yes" : "No").Append("\r\n"); }
            sb.Append(",,Total,").Append(N3(tl)).Append(',').Append(N3(ta)).Append(",\r\n"); return sb.ToString();
        }

        // action: 0 close, 1 zoom to selected, 2 label each (all rows), 3 insert table
        public static void Lengths(List<LenRow> rows, int selected, out int action, out int row)
        {
            int act = 0, sel = -1; double tl = rows.Sum(r => r.Len), ta = rows.Where(r => r.Closed).Sum(r => r.Area);
            using (var f = Theme.Base("B+ CAD Tools - Polyline lengths", 760, 500))
            {
                f.Controls.Add(Theme.Lbl(rows.Count + " objects.  Total length " + N3(tl) + " m.  Total area of closed ones " + N3(ta) + " m\u00B2.", 16, 12, 720));
                var lv = new ListView { Left = 16, Top = 38, Width = 728, Height = 360, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, MultiSelect = false, HideSelection = false }; Theme.Style(lv); Theme.DarkList(lv);
                lv.Columns.Add("No", 40); lv.Columns.Add("Layer", 200); lv.Columns.Add("Type", 110); lv.Columns.Add("Length (m)", 130); lv.Columns.Add("Area (m\u00B2)", 130); lv.Columns.Add("Closed", 90);
                int i = 0; foreach (var r in rows) { i++; lv.Items.Add(new ListViewItem(new[] { i.ToString(), r.Layer, r.Kind, N3(r.Len), r.Closed ? N3(r.Area) : "", r.Closed ? "Yes" : "No" })); }
                lv.Items.Add(new ListViewItem(new[] { "", "Total", "", N3(tl), N3(ta), "" }));
                if (selected >= 0 && selected < rows.Count) { lv.Items[selected].Selected = true; lv.EnsureVisible(selected); }
                f.Controls.Add(Theme.Lbl("Lengths are along the curve in drawing units shown as metres. Areas are for closed objects only.", 16, 406, 728, true));
                var zoom = Theme.Btn("Zoom to", 16, 456, 90, true); var lab = Theme.Btn("Label each", 114, 456, 100); var tab = Theme.Btn("Insert table", 222, 456, 100); var copy = Theme.Btn("Copy", 330, 456, 70); var save = Theme.Btn("Save CSV...", 408, 456, 100); var close = Theme.Btn("Close", 654, 456, 90);
                zoom.Click += (s, e) => { if (lv.SelectedIndices.Count == 0 || lv.SelectedIndices[0] >= rows.Count) return; sel = lv.SelectedIndices[0]; act = 1; f.DialogResult = DialogResult.OK; };
                lab.Click += (s, e) => { act = 2; f.DialogResult = DialogResult.OK; };
                tab.Click += (s, e) => { act = 3; f.DialogResult = DialogResult.OK; };
                copy.Click += (s, e) => { try { Clipboard.SetText(LengthsCsv(rows).Replace(",", "\t").Replace("\"", "")); } catch { } };
                save.Click += (s, e) => { using (var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "lengths.csv" }) if (d.ShowDialog(f) == DialogResult.OK) File.WriteAllText(d.FileName, LengthsCsv(rows), new UTF8Encoding(true)); };
                close.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                lv.DoubleClick += (s, e) => zoom.PerformClick();
                f.Controls.AddRange(new Control[] { lv, zoom, lab, tab, copy, save, close }); f.CancelButton = close;
                f.ShowDialog();
            }
            action = act; row = sel;
        }

        public sealed class RenumChoice { public int Start = 1, Step = 1; public string Sort = "keep", Prefix = "", Suffix = ""; public bool Labels = true; }
        public static RenumChoice Renumber(int count)
        {
            using (var f = Theme.Base("B+ CAD Tools - Renumber points", 440, 330))
            {
                var c = new RenumChoice();
                f.Controls.Add(Theme.Lbl(count + " points selected.", 16, 12, 400));
                f.Controls.Add(Theme.Lbl("Start at", 16, 46, 100)); var st = new NumericUpDown { Left = 130, Top = 43, Width = 90, Minimum = 0, Maximum = 99999999, Value = 1 }; Theme.Style(st);
                f.Controls.Add(Theme.Lbl("Step", 240, 46, 50)); var sp = new NumericUpDown { Left = 290, Top = 43, Width = 70, Minimum = 1, Maximum = 100000, Value = 1 }; Theme.Style(sp);
                f.Controls.Add(Theme.Lbl("Order by", 16, 82, 100)); var so = new ComboBox { Left = 130, Top = 79, Width = 230, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat }; Theme.Style(so); so.Items.AddRange(new object[] { "Current order in the drawing", "Current point number", "Easting (west to east)", "Northing (south to north)" }); so.SelectedIndex = 0;
                f.Controls.Add(Theme.Lbl("Prefix", 16, 118, 100)); var pre = new TextBox { Left = 130, Top = 115, Width = 90, BorderStyle = BorderStyle.FixedSingle }; Theme.Style(pre);
                f.Controls.Add(Theme.Lbl("Suffix", 240, 118, 50)); var suf = new TextBox { Left = 290, Top = 115, Width = 70, BorderStyle = BorderStyle.FixedSingle }; Theme.Style(suf);
                var lab = new CheckBox { Left = 16, Top = 156, Width = 400, Text = "Also change the text labels made by Import", Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                f.Controls.Add(Theme.Lbl("Only plain points are changed. Civil 3D COGO points are skipped.", 16, 190, 410, true));
                f.Controls.Add(Theme.Lbl("One UNDO reverses the whole renumbering.", 16, 212, 410, true));
                var ok = Theme.Btn("Renumber", 232, 286, 98, true); var cancel = Theme.Btn("Cancel", 338, 286, 90);
                ok.Click += (s, e) => { c.Start = (int)st.Value; c.Step = (int)sp.Value; c.Sort = new[] { "keep", "no", "east", "north" }[so.SelectedIndex]; c.Prefix = pre.Text.Trim(); c.Suffix = suf.Text.Trim(); c.Labels = lab.Checked; f.DialogResult = DialogResult.OK; };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { st, sp, so, pre, suf, lab, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? c : null;
            }
        }
    }
}
