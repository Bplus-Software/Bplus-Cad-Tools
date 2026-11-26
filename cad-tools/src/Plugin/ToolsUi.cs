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

        static string CopyText(List<LenRow> rows, IEnumerable<int> idx, string mode)
        {
            var sb = new StringBuilder();
            if (mode == "all") sb.Append("No\tLayer\tType\tLength (m)\tArea (m2)\tClosed\r\n");
            foreach (int i in idx)
            {
                var r = rows[i];
                if (mode == "len") sb.Append(N3(r.Len)).Append("\r\n");
                else if (mode == "area") { if (r.Closed) sb.Append(N3(r.Area)).Append("\r\n"); }
                else sb.Append(i + 1).Append('\t').Append(r.Layer).Append('\t').Append(r.Kind).Append('\t').Append(N3(r.Len)).Append('\t').Append(r.Closed ? N3(r.Area) : "").Append('\t').Append(r.Closed ? "Yes" : "No").Append("\r\n");
            }
            return sb.ToString();
        }

        // action: 0 close, 1 zoom to the first selected row, 2 label each (all rows), 3 insert table
        public static void Lengths(List<LenRow> rows, int selected, out int action, out int row)
        {
            int act = 0, sel = -1; double tl = rows.Sum(r => r.Len), ta = rows.Where(r => r.Closed).Sum(r => r.Area);
            using (var f = Theme.Base("B+ CAD Tools - Polyline lengths", 760, 520))
            {
                f.Controls.Add(Theme.Lbl(rows.Count + " objects.  Total length " + N3(tl) + " m.  Total area of closed ones " + N3(ta) + " m\u00B2.", 16, 12, 720));
                var lv = new ListView { Left = 16, Top = 38, Width = 728, Height = 230, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, MultiSelect = true, HideSelection = false }; Theme.Style(lv); Theme.DarkList(lv);
                lv.Columns.Add("No", 40); lv.Columns.Add("Layer", 200); lv.Columns.Add("Type", 110); lv.Columns.Add("Length (m)", 130); lv.Columns.Add("Area (m\u00B2)", 130); lv.Columns.Add("Closed", 90);
                int i = 0; foreach (var r in rows) { i++; lv.Items.Add(new ListViewItem(new[] { i.ToString(), r.Layer, r.Kind, N3(r.Len), r.Closed ? N3(r.Area) : "", r.Closed ? "Yes" : "No" })); }
                lv.Items.Add(new ListViewItem(new[] { "", "Total", "", N3(tl), N3(ta), "" }));
                if (selected >= 0 && selected < rows.Count) { lv.Items[selected].Selected = true; lv.EnsureVisible(selected); }
                f.Controls.Add(Theme.Lbl("Click a row. Ctrl+click or Shift+click for more rows. Ctrl+A selects all.", 16, 272, 728, true));
                f.Controls.Add(Theme.Lbl("Copy", 16, 300, 100));
                var cAll = Theme.Btn("Copy all rows", 16, 322, 150, true); var cSel = Theme.Btn("Copy selected rows", 174, 322, 170); var cLen = Theme.Btn("Copy lengths only", 352, 322, 170); var cArea = Theme.Btn("Copy areas only", 530, 322, 170);
                f.Controls.Add(Theme.Lbl("Pasted as tab-separated text: it opens in Excel or Notepad as columns. Ctrl+C copies the selected rows.", 16, 356, 728, true));
                var note = new Label { Left = 16, Top = 384, Width = 728, Height = 26, AutoSize = false, ForeColor = Color.FromArgb(120, 210, 140), BackColor = Color.Transparent, Text = "", TextAlign = ContentAlignment.MiddleLeft };
                Func<string, IEnumerable<int>> pick = m => m == "all" ? Enumerable.Range(0, rows.Count) : lv.SelectedIndices.Cast<int>().Where(x => x < rows.Count).OrderBy(x => x);
                Action<string> copy = mode =>
                {
                    var idx = pick(mode == "sel" ? "sel" : mode == "all" ? "all" : (lv.SelectedIndices.Count > 0 ? "sel" : "all")).ToList();
                    if (idx.Count == 0) { note.ForeColor = Color.FromArgb(240, 150, 120); note.Text = "No row is selected. Click a row first, or use Copy all rows."; return; }
                    string text = CopyText(rows, idx, mode == "sel" ? "sel" : mode);
                    if (text.Length == 0) { note.ForeColor = Color.FromArgb(240, 150, 120); note.Text = "Nothing to copy: none of those rows is closed, so there is no area."; return; }
                    try { Clipboard.SetDataObject(text, true, 10, 150); note.ForeColor = Color.FromArgb(120, 210, 140); note.Text = "Copied " + idx.Count + " row" + (idx.Count == 1 ? "" : "s") + ". Paste with Ctrl+V."; }
                    catch (Exception ex) { note.ForeColor = Color.FromArgb(240, 150, 120); note.Text = "Windows would not give access to the clipboard (" + ex.Message + "). Press the button again, or use Save CSV."; }
                };
                cAll.Click += (s, e) => copy("all"); cSel.Click += (s, e) => copy("sel"); cLen.Click += (s, e) => copy("len"); cArea.Click += (s, e) => copy("area");
                lv.KeyDown += (s, e) =>
                {
                    if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem it in lv.Items) it.Selected = it.Index < rows.Count; e.Handled = true; }
                    else if (e.Control && e.KeyCode == Keys.C) { copy("sel"); e.Handled = true; }
                };
                var zoom = Theme.Btn("Zoom to", 16, 478, 90); var lab = Theme.Btn("Label each", 114, 478, 100); var tab = Theme.Btn("Insert table", 222, 478, 110); var save = Theme.Btn("Save CSV...", 340, 478, 110); var close = Theme.Btn("Close", 654, 478, 90);
                zoom.Click += (s, e) => { if (lv.SelectedIndices.Count == 0 || lv.SelectedIndices[0] >= rows.Count) { note.ForeColor = Color.FromArgb(240, 150, 120); note.Text = "Click a row first."; return; } sel = lv.SelectedIndices[0]; act = 1; f.DialogResult = DialogResult.OK; };
                lab.Click += (s, e) => { act = 2; f.DialogResult = DialogResult.OK; };
                tab.Click += (s, e) => { act = 3; f.DialogResult = DialogResult.OK; };
                save.Click += (s, e) => { using (var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "lengths.csv" }) if (d.ShowDialog(f) == DialogResult.OK) { try { File.WriteAllText(d.FileName, LengthsCsv(rows), new UTF8Encoding(true)); note.ForeColor = Color.FromArgb(120, 210, 140); note.Text = "Saved " + d.FileName; } catch (Exception ex) { note.ForeColor = Color.FromArgb(240, 150, 120); note.Text = "Could not save: " + ex.Message; } } };
                close.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                lv.DoubleClick += (s, e) => zoom.PerformClick();
                f.Controls.AddRange(new Control[] { lv, cAll, cSel, cLen, cArea, note, zoom, lab, tab, save, close }); f.CancelButton = close;
                f.ShowDialog();
            }
            action = act; row = sel;
        }

        public sealed class LeaderChoice { public string What = "auto"; public bool Centre; public double TextH = 1; public int Decimals = 2; public bool Arrow = true; }
        public static LeaderChoice Leader(int total, int closed)
        {
            using (var f = Theme.Base("B+ CAD Tools - Auto leaders", 460, 440))
            {
                var c = new LeaderChoice { TextH = Config.TextHeight };
                f.Controls.Add(Theme.Lbl(total + " shapes selected (" + closed + " closed, " + (total - closed) + " open).", 16, 12, 420));
                f.Controls.Add(Theme.Lbl("Put on the leader", 16, 44, 300));
                string[] names = { "Length (m)", "Area (m\u00B2), closed shapes only", "Length and area, two lines", "Automatic: area for closed, length for open" };
                var rb = new RadioButton[4];
                for (int i = 0; i < 4; i++) { rb[i] = new RadioButton { Left = 16, Top = 70 + i * 26, Width = 420, Text = names[i], ForeColor = Theme.Text, BackColor = Color.Transparent, Checked = i == 3 }; f.Controls.Add(rb[i]); }
                f.Controls.Add(Theme.Lbl("Leader starts at", 16, 184, 200));
                var st = new ComboBox { Left = 16, Top = 208, Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat }; Theme.Style(st); st.Items.AddRange(new object[] { "Middle of the shape's length", "Centre of the shape (closed ones)" }); st.SelectedIndex = 0;
                f.Controls.Add(Theme.Lbl("Text height", 16, 246, 100)); var th = new TextBox { Left = 16, Top = 268, Width = 80, BorderStyle = BorderStyle.FixedSingle, Text = c.TextH.ToString(CultureInfo.InvariantCulture) }; Theme.Style(th);
                f.Controls.Add(Theme.Lbl("Decimals", 120, 246, 80)); var dec = new NumericUpDown { Left = 120, Top = 268, Width = 60, Minimum = 0, Maximum = 6, Value = 2 }; Theme.Style(dec);
                var arrow = new CheckBox { Left = 210, Top = 268, Width = 220, Text = "Arrow head at the shape", Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                f.Controls.Add(Theme.Lbl("Each leader goes up and right by 3 x the text height, on layer BP-LEADER.", 16, 312, 430, true));
                f.Controls.Add(Theme.Lbl("Dense drawings: leaders may overlap, move them after. One UNDO removes all.", 16, 334, 430, true));
                var ok = Theme.Btn("Create", 262, 396, 90, true); var cancel = Theme.Btn("Cancel", 360, 396, 90);
                ok.Click += (s, e) =>
                {
                    double h; if (!double.TryParse(th.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out h) || h <= 0) { MessageBox.Show(f, "Text height must be a number above zero.", "B+ CAD Tools"); return; }
                    c.TextH = h; c.Decimals = (int)dec.Value; c.Arrow = arrow.Checked; c.Centre = st.SelectedIndex == 1; c.What = rb[0].Checked ? "len" : rb[1].Checked ? "area" : rb[2].Checked ? "both" : "auto"; f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { st, th, dec, arrow, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? c : null;
            }
        }

        // Returns the follow-up command to run ("" for none), or null when cancelled. Map type and opacity are applied by the caller.
        public static string MapOptions(int kind, int opacity, out int newKind, out int newOpacity)
        {
            newKind = kind; newOpacity = opacity; string follow = "";
            using (var f = Theme.Base("B+ CAD Tools - Map options", 440, 360))
            {
                f.Controls.Add(Theme.Lbl("Map type", 16, 14, 200));
                var sat = new RadioButton { Left = 16, Top = 38, Width = 400, Text = "Satellite", ForeColor = Theme.Text, BackColor = Color.Transparent };
                var road = new RadioButton { Left = 16, Top = 62, Width = 400, Text = "Roadmap", ForeColor = Theme.Text, BackColor = Color.Transparent };
                var hyb = new RadioButton { Left = 16, Top = 86, Width = 400, Text = "Hybrid (satellite with labels)", ForeColor = Theme.Text, BackColor = Color.Transparent };
                (kind == 1 ? road : kind == 2 ? hyb : sat).Checked = true;
                f.Controls.Add(Theme.Lbl("Opacity", 16, 124, 100));
                var tb = new TrackBar { Left = 16, Top = 146, Width = 330, Minimum = 10, Maximum = 100, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Value = Math.Max(10, Math.Min(100, opacity)) };
                var pct = Theme.Lbl(tb.Value + "%", 360, 150, 60); tb.ValueChanged += (s, e) => pct.Text = tb.Value + "%";
                var refresh = Theme.Btn("Refresh map", 16, 204, 120); var cs = Theme.Btn("Coordinate system...", 144, 204, 160); var key = Theme.Btn("Google key...", 312, 204, 112);
                var test = Theme.Btn("Test card", 16, 240, 120); var chk = Theme.Btn("Check installation", 144, 240, 160);
                f.Controls.Add(Theme.Lbl("Changes to map type and opacity apply when you press OK.", 16, 284, 408, true));
                var ok = Theme.Btn("OK", 238, 320, 90, true); var cancel = Theme.Btn("Cancel", 334, 320, 90);
                bool okd = false;
                Action<string> go = c => { follow = c; okd = true; f.DialogResult = DialogResult.OK; };
                refresh.Click += (s, e) => go("BPGMAPREFRESH "); cs.Click += (s, e) => go("BPGMAPCS "); key.Click += (s, e) => go("BPGMAPSET "); test.Click += (s, e) => go("BPGMAPTEST "); chk.Click += (s, e) => go("BPCHECK ");
                ok.Click += (s, e) => { okd = true; f.DialogResult = DialogResult.OK; }; cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { sat, road, hyb, tb, pct, refresh, cs, key, test, chk, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog() != DialogResult.OK || !okd) return null;
                newKind = road.Checked ? 1 : hyb.Checked ? 2 : 0; newOpacity = tb.Value; return follow;
            }
        }
    }
}
