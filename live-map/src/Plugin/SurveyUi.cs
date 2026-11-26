using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using BPlus.LiveMap.Core;

namespace BPlus.LiveMap
{
    static class SurveyUi
    {
        static TextBox Box(int x, int y, int w) { var t = new TextBox { Left = x, Top = y, Width = w, BorderStyle = BorderStyle.FixedSingle }; Theme.Style(t); return t; }
        static CheckBox Chk(string text, int x, int y, int w, bool v) { return new CheckBox { Text = text, Left = x, Top = y, Width = w, Checked = v, ForeColor = Theme.Text, BackColor = Color.Transparent }; }
        static ComboBox Combo(int x, int y, int w, params string[] items) { var c = new ComboBox { Left = x, Top = y, Width = w, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat }; Theme.Style(c); c.Items.AddRange(items); c.SelectedIndex = 0; return c; }
        static readonly string[] OrderNames = { "PENZD (Point, E, N, Z, Description)", "PNEZD (Point, N, E, Z, Description)", "PENZ", "PNEZ", "PEN", "Custom order..." };
        static readonly string[] OrderCodes = { "PENZD", "PNEZD", "PENZ", "PNEZ", "PEN", "" };
        static readonly string[] DelimNames = { "Automatic", "Comma", "Tab", "Space", "Semicolon" };
        static readonly char?[] DelimCodes = { null, ',', '\t', ' ', ';' };

        public sealed class ImportChoice { public string Path = "", Order = "PENZD"; public char? Delim; public bool Labels = true; public double TextH = 1; public ParseResult Result; }

        public static ImportChoice Import()
        {
            using (var f = Theme.Base("BPlus - Import points", 520, 470))
            {
                var c = new ImportChoice();
                f.Controls.Add(Theme.Lbl("Points file (txt, csv or xlsx):", 16, 14, 300));
                var path = Box(16, 38, 392); var br = Theme.Btn("Browse...", 416, 36, 88);
                f.Controls.Add(Theme.Lbl("Column order:", 16, 76, 150)); var ord = Combo(16, 98, 250, OrderNames); var custom = Box(276, 98, 100); custom.Visible = false; custom.MaxLength = 5;
                f.Controls.Add(Theme.Lbl("Separator:", 392, 76, 110)); var del = Combo(392, 98, 112, DelimNames);
                var lab = Chk("Add text labels (number and code)", 16, 132, 280, true); var th = Box(430, 130, 74); th.Text = "1"; var thl = Theme.Lbl("Text height:", 340, 133, 86, true);
                var pre = new ListView { Left = 16, Top = 168, Width = 488, Height = 210, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Nonclickable }; Theme.Style(pre);
                foreach (var h in new[] { "Point", "Easting", "Northing", "Elevation", "Description" }) pre.Columns.Add(h, h == "Description" ? 140 : 84);
                var info = Theme.Lbl("Choose a file to see a preview.", 16, 384, 488, true); info.Height = 36;
                var ok = Theme.Btn("Import", 316, 430, 90, true); var cancel = Theme.Btn("Cancel", 414, 430, 90); ok.Enabled = false;
                Action reload = () =>
                {
                    pre.Items.Clear(); c.Result = null; ok.Enabled = false;
                    string order = ord.SelectedIndex == OrderCodes.Length - 1 ? custom.Text.Trim().ToUpperInvariant() : OrderCodes[ord.SelectedIndex];
                    if (path.Text.Trim().Length == 0 || !File.Exists(path.Text.Trim())) return;
                    if (!PointText.ValidOrder(order)) { info.Text = "Column order must use P, E, N, Z, D once each (for example PENZD)."; return; }
                    try
                    {
                        ParseResult r; string p = path.Text.Trim();
                        if (p.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) using (var s = File.OpenRead(p)) r = XlsxReader.ParsePoints(s, order);
                        else { var lines = File.ReadAllLines(p, Encoding.UTF8); r = PointText.Parse(lines, order, DelimCodes[del.SelectedIndex]); }
                        c.Result = r; c.Order = order;
                        foreach (var q in r.Points.Take(200)) pre.Items.Add(new ListViewItem(new[] { q.No, q.E.ToString("F3"), q.N.ToString("F3"), q.HasZ ? q.Z.ToString("F3") : "", q.Desc }));
                        info.Text = r.Points.Count + " points read" + (r.HeaderSkipped ? " (header row skipped)" : "") + (r.Problems.Count > 0 ? ", " + r.Problems.Count + " lines skipped. First: " + r.Problems[0] : ".");
                        ok.Enabled = r.Points.Count > 0;
                    }
                    catch (Exception e) { info.Text = "Could not read the file: " + e.Message; }
                };
                br.Click += (s, e) => { using (var d = new OpenFileDialog { Filter = "Point files|*.txt;*.csv;*.xyz;*.pts;*.xlsx|All files|*.*" }) if (d.ShowDialog(f) == DialogResult.OK) { path.Text = d.FileName; reload(); } };
                ord.SelectedIndexChanged += (s, e) => { custom.Visible = ord.SelectedIndex == OrderCodes.Length - 1; reload(); };
                custom.TextChanged += (s, e) => reload(); del.SelectedIndexChanged += (s, e) => reload(); path.Leave += (s, e) => reload();
                ok.Click += (s, e) =>
                {
                    double h; if (!double.TryParse(th.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out h) || h <= 0) { MessageBox.Show(f, "Text height must be a number above zero.", "BPlus"); return; }
                    c.TextH = h; c.Labels = lab.Checked; c.Path = path.Text.Trim(); c.Delim = DelimCodes[del.SelectedIndex]; f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { path, br, ord, custom, del, lab, th, thl, pre, info, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? c : null;
            }
        }

        public sealed class ExportChoice { public string Path = "", Order = "PENZD"; public char Delim = ','; public int Decimals = 3; public bool Header; public HashSet<string> Cats = new HashSet<string>(); }

        public static ExportChoice Export(List<KeyValuePair<string, int>> cats)
        {
            using (var f = Theme.Base("BPlus - Export points", 480, 470))
            {
                var c = new ExportChoice();
                f.Controls.Add(Theme.Lbl("Format:", 16, 14, 100)); var ord = Combo(16, 36, 250, OrderNames.Take(5).ToArray());
                f.Controls.Add(Theme.Lbl("Separator:", 280, 14, 100)); var del = Combo(280, 36, 180, "Comma (csv)", "Tab", "Space", "Semicolon");
                f.Controls.Add(Theme.Lbl("Decimals:", 16, 72, 80)); var dec = new NumericUpDown { Left = 100, Top = 70, Width = 60, Minimum = 0, Maximum = 8, Value = 3 }; Theme.Style(dec);
                var hdr = Chk("Header row", 200, 70, 200, false);
                f.Controls.Add(Theme.Lbl("Categories to export:", 16, 108, 300));
                var list = new CheckedListBox { Left = 16, Top = 132, Width = 444, Height = 170, BorderStyle = BorderStyle.None, CheckOnClick = true }; Theme.Style(list);
                foreach (var k in cats) list.Items.Add(k.Key + "  (" + k.Value + ")", true);
                var path = Box(16, 318, 360); var br = Theme.Btn("Save as...", 384, 316, 76);
                f.Controls.Add(Theme.Lbl("The file is UTF-8, one point per line, no hidden characters.", 16, 354, 444, true));
                var ok = Theme.Btn("Export", 266, 420, 90, true); var cancel = Theme.Btn("Cancel", 370, 420, 90);
                br.Click += (s, e) => { using (var d = new SaveFileDialog { Filter = "Text|*.txt|CSV|*.csv", FileName = "points" + (del.SelectedIndex == 0 ? ".csv" : ".txt") }) if (d.ShowDialog(f) == DialogResult.OK) path.Text = d.FileName; };
                ok.Click += (s, e) =>
                {
                    if (path.Text.Trim().Length == 0) { MessageBox.Show(f, "Choose where to save the file.", "BPlus"); return; }
                    c.Path = path.Text.Trim(); c.Order = OrderCodes[ord.SelectedIndex]; c.Delim = new[] { ',', '\t', ' ', ';' }[del.SelectedIndex]; c.Decimals = (int)dec.Value; c.Header = hdr.Checked;
                    for (int i = 0; i < list.Items.Count; i++) if (list.GetItemChecked(i)) c.Cats.Add(cats[i].Key);
                    if (c.Cats.Count == 0) { MessageBox.Show(f, "Tick at least one category.", "BPlus"); return; }
                    f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { ord, del, dec, hdr, list, path, br, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? c : null;
            }
        }

        public static string Codes(string current)
        {
            using (var f = Theme.Base("BPlus - Point codes", 520, 440))
            {
                f.Controls.Add(Theme.Lbl("One line per category:  PREFIX, PREFIX2 = Name", 16, 12, 480));
                var t = new TextBox { Left = 16, Top = 38, Width = 488, Height = 300, Multiline = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10f), AcceptsReturn = true, Text = current.Replace("\r\n", "\n").Replace("\n", "\r\n") }; Theme.Style(t); t.SelectionStart = 0; t.SelectionLength = 0;
                f.Controls.Add(Theme.Lbl("A point matches when the letters at the start of its description match a prefix.", 16, 346, 488, true));
                var reset = Theme.Btn("Reset", 16, 392, 80); var ok = Theme.Btn("Save", 316, 392, 90, true); var cancel = Theme.Btn("Cancel", 414, 392, 90);
                string result = null;
                reset.Click += (s, e) => t.Text = CodeRules.DefaultText.Replace("\r\n", "\n").Replace("\n", "\r\n");
                ok.Click += (s, e) =>
                {
                    var pr = new List<string>(); var r = CodeRules.Parse(t.Text, pr);
                    if (pr.Count > 0) { MessageBox.Show(f, string.Join("\n", pr.Take(6)), "BPlus - fix these lines"); return; }
                    if (r.Cats.Count == 0) { MessageBox.Show(f, "Add at least one category.", "BPlus"); return; }
                    result = t.Text; f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { t, reset, ok, cancel }); f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? result : null;
            }
        }

        // Returns on/off per layer name, or null when cancelled. Categories drive both the BP-PT and BP-TXT layers.
        public static Dictionary<string, bool> Isolate(List<string> layers, Dictionary<string, bool> state)
        {
            var cats = layers.Where(l => l.StartsWith("BP-PT-") || l.StartsWith("BP-LINE-")).ToList();
            using (var f = Theme.Base("BPlus - Show or hide point categories", 420, 420))
            {
                f.Controls.Add(Theme.Lbl("Tick the categories to show:", 16, 12, 380));
                var list = new CheckedListBox { Left = 16, Top = 36, Width = 388, Height = 290, BorderStyle = BorderStyle.None, CheckOnClick = true }; Theme.Style(list);
                foreach (var c in cats) list.Items.Add(c, state.ContainsKey(c) ? state[c] : true);
                var all = Theme.Btn("Show all", 16, 340, 90); var none = Theme.Btn("Hide all", 112, 340, 90);
                var ok = Theme.Btn("Apply", 214, 380, 90, true); var cancel = Theme.Btn("Cancel", 314, 380, 90);
                all.Click += (s, e) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); };
                none.Click += (s, e) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, false); };
                ok.Click += (s, e) => f.DialogResult = DialogResult.OK; cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { list, all, none, ok, cancel }); f.CancelButton = cancel;
                if (f.ShowDialog() != DialogResult.OK) return null;
                var res = new Dictionary<string, bool>();
                for (int i = 0; i < cats.Count; i++)
                {
                    bool on = list.GetItemChecked(i); res[cats[i]] = on;
                    if (cats[i].StartsWith("BP-PT-")) res["BP-TXT-" + cats[i].Substring(6)] = on;
                }
                return res;
            }
        }

        public sealed class ConnectChoice { public bool ByCode = true, SplitByNumber = true; public string Only = ""; }
        public static ConnectChoice Connect()
        {
            using (var f = Theme.Base("BPlus - Connect points", 460, 300))
            {
                var c = new ConnectChoice();
                f.Controls.Add(Theme.Lbl("Mode:", 16, 14, 80)); var mode = Combo(16, 36, 428, "By code, in point number order", "All points, in point number order");
                var split = Chk("Separate lines when the code carries a number (RD1 and RD2 are two lines)", 16, 76, 428, true);
                f.Controls.Add(Theme.Lbl("Only these codes (empty = all coded points), e.g. RD, EG:", 16, 108, 428)); var only = Box(16, 132, 428);
                f.Controls.Add(Theme.Lbl("Add the word CLOSE to a point's description to close that line.", 16, 168, 428, true));
                f.Controls.Add(Theme.Lbl("Lines are 3D polylines on layers BP-LINE-<code>. One UNDO removes them.", 16, 190, 428, true));
                var ok = Theme.Btn("Connect", 256, 252, 90, true); var cancel = Theme.Btn("Cancel", 354, 252, 90);
                mode.SelectedIndexChanged += (s, e) => { split.Enabled = mode.SelectedIndex == 0; only.Enabled = mode.SelectedIndex == 0; };
                ok.Click += (s, e) => { c.ByCode = mode.SelectedIndex == 0; c.SplitByNumber = split.Checked; c.Only = only.Text; f.DialogResult = DialogResult.OK; };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { mode, split, only, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? c : null;
            }
        }

        // Returns the index to zoom to (and the dialog should reopen), -1 to close.
        public static int Qa(List<QaIssue> issues, int pointCount, int selected)
        {
            using (var f = Theme.Base("BPlus - Survey QA/QC", 640, 480))
            {
                f.Controls.Add(Theme.Lbl(pointCount + " points checked, " + issues.Count + " findings.", 16, 12, 600));
                var lv = new ListView { Left = 16, Top = 38, Width = 608, Height = 340, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, MultiSelect = false }; Theme.Style(lv);
                lv.Columns.Add("Check", 150); lv.Columns.Add("Point", 110); lv.Columns.Add("Details", 330);
                foreach (var q in issues) lv.Items.Add(new ListViewItem(new[] { q.Kind, q.PointNo, q.Text }));
                if (selected >= 0 && selected < lv.Items.Count) { lv.Items[selected].Selected = true; lv.EnsureVisible(selected); }
                f.Controls.Add(Theme.Lbl("Zoom centres the view on the finding (plan view, WCS).", 16, 386, 608, true));
                var zoom = Theme.Btn("Zoom to", 16, 430, 90, true); var copy = Theme.Btn("Copy", 114, 430, 80); var save = Theme.Btn("Save CSV...", 202, 430, 100); var close = Theme.Btn("Close", 534, 430, 90);
                int result = -1;
                zoom.Click += (s, e) => { if (lv.SelectedIndices.Count == 0) return; result = lv.SelectedIndices[0]; f.DialogResult = DialogResult.Retry; };
                copy.Click += (s, e) => { try { Clipboard.SetText(Csv(issues)); } catch { } };
                save.Click += (s, e) => { using (var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "qa-report.csv" }) if (d.ShowDialog(f) == DialogResult.OK) File.WriteAllText(d.FileName, Csv(issues), new UTF8Encoding(true)); };
                close.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                lv.DoubleClick += (s, e) => zoom.PerformClick();
                f.Controls.AddRange(new Control[] { lv, zoom, copy, save, close }); f.CancelButton = close;
                f.ShowDialog(); return result;
            }
        }
        static string Csv(List<QaIssue> issues)
        {
            Func<string, string> q = s => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            var sb = new StringBuilder("Check,Point,Easting,Northing,Details\r\n");
            foreach (var i in issues) sb.Append(q(i.Kind)).Append(',').Append(q(i.PointNo)).Append(',').Append(i.E.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(i.N.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(q(i.Text)).Append("\r\n");
            return sb.ToString();
        }
    }
}
