using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BPlus.LiveMap.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BPlus.LiveMap
{
    sealed class LayoutRow { public string Name, Setup, Paper, Scale, Canon; public bool Ticked = true; }
    sealed class PlotChoice
    {
        public List<string> Layouts = new List<string>(); public bool OwnSetup, Pdf = true, Combined = true, Lineweights = true;
        public string Device, MediaCanonical, Scale = "Fit", Style = "", Folder = "", Pattern = "{drawing}-{layout}"; public int Rotation;
    }

    static class PlotUi
    {
        static RadioButton Rb(string t, int x, int y, int w, bool on) { return new RadioButton { Text = t, Left = x, Top = y, Width = w, Height = 24, Checked = on, ForeColor = Theme.Text, BackColor = Color.Transparent, AutoSize = false }; }
        static ComboBox Cb(int x, int y, int w) { var c = new ComboBox { Left = x, Top = y, Width = w, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat }; Theme.Style(c); return c; }
        static Panel Group(Panel body, int y, int h) { var p = new Panel { Left = 0, Top = y, Width = 680, Height = h, BackColor = Color.Transparent }; body.Controls.Add(p); return p; }
        static int Pick(ComboBox c, params string[] likes) { foreach (var l in likes) for (int i = 0; i < c.Items.Count; i++) if (c.Items[i].ToString().IndexOf(l, StringComparison.OrdinalIgnoreCase) >= 0) return i; return c.Items.Count > 0 ? 0 : -1; }

        public static PlotChoice Ask(List<LayoutRow> rows, string drawing, string defFolder, PlotLists lists)
        {
            using (var f = Theme.Base("B+ CAD Tools - Plot layouts", 700, 700))
            {
                // Scales with the screen's DPI, resizable, never taller than the screen. Buttons sit in a bar that is always visible.
                f.AutoScaleDimensions = new SizeF(96f, 96f); f.AutoScaleMode = AutoScaleMode.Dpi;
                f.FormBorderStyle = FormBorderStyle.Sizable; f.MaximizeBox = true; f.MinimumSize = new Size(560, 420);
                var wa = Screen.FromControl(f).WorkingArea; f.StartPosition = FormStartPosition.Manual;
                var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Bar };
                var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bar }; body.AutoScrollMinSize = new Size(660, 730);
                f.Controls.Add(body); f.Controls.Add(bar); bar.BringToFront(); body.BringToFront();
                bar.Dock = DockStyle.Bottom;

                body.Controls.Add(Theme.Lbl("Layouts in this drawing", 16, 10, 300));
                var all = new Label { Text = "All", Left = 500, Top = 10, Width = 40, Height = 22, ForeColor = Theme.Accent, BackColor = Color.Transparent, Cursor = Cursors.Hand };
                var none = new Label { Text = "None", Left = 550, Top = 10, Width = 50, Height = 22, ForeColor = Theme.Accent, BackColor = Color.Transparent, Cursor = Cursors.Hand };
                var grid = new DataGridView { Left = 16, Top = 36, Width = 628, Height = 150, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BorderStyle = BorderStyle.None, BackgroundColor = Theme.Field, GridColor = Theme.Bar, EnableHeadersVisualStyles = false, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 28, RowTemplate = { Height = 26 }, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, MultiSelect = false };
                grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Panel; grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text; grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Panel;
                grid.DefaultCellStyle.BackColor = Theme.Field; grid.DefaultCellStyle.ForeColor = Theme.Text; grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(48, 60, 78); grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
                grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "", Width = 34 });
                foreach (var c in new[] { new[] { "Layout", "190" }, new[] { "Page setup", "140" }, new[] { "Paper", "150" }, new[] { "Scale", "90" } }) grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = c[0], Width = int.Parse(c[1]), ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
                grid.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
                foreach (var r in rows) grid.Rows.Add(true, r.Name, r.Setup, r.Paper, r.Scale);
                var cnt = Theme.Lbl("", 16, 192, 628, true);
                Action upd = () => { int n = 0; foreach (DataGridViewRow r in grid.Rows) if (Convert.ToBoolean(r.Cells[0].Value)) n++; cnt.Text = n + " of " + rows.Count + " layouts ticked. The Model tab is not plotted by this tool."; };
                grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
                grid.CellValueChanged += (s, e) => upd(); upd();
                all.Click += (s, e) => { foreach (DataGridViewRow r in grid.Rows) r.Cells[0].Value = true; }; none.Click += (s, e) => { foreach (DataGridViewRow r in grid.Rows) r.Cells[0].Value = false; };
                body.Controls.AddRange(new Control[] { all, none, grid, cnt });

                body.Controls.Add(Theme.Lbl("Plot with", 16, 226, 300));
                var pw = Group(body, 250, 54); var own = Rb("Each layout's own page setup", 20, 0, 600, false); var my = Rb("My settings (the same for all ticked layouts)", 20, 26, 600, true); pw.Controls.AddRange(new Control[] { own, my });
                body.Controls.Add(Theme.Lbl("A layout with no printer or PDF set in its page setup uses my settings below.", 44, 308, 600, true));
                body.Controls.Add(Theme.Lbl("Printer / PDF", 36, 340, 280, true)); var dev = Cb(36, 362, 290);
                body.Controls.Add(Theme.Lbl("Paper size", 346, 340, 250, true)); var med = Cb(346, 362, 290);
                body.Controls.Add(Theme.Lbl("Scale (Fit or 1:500)", 36, 404, 150, true)); var sc = new ComboBox { Left = 36, Top = 426, Width = 140, DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.Flat }; Theme.Style(sc); sc.Items.AddRange(new object[] { "Fit", "1:50", "1:100", "1:200", "1:250", "1:500", "1:1000", "1:2000", "1:5000" }); sc.Text = "Fit";
                body.Controls.Add(Theme.Lbl("Plot style", 196, 404, 190, true)); var sty = Cb(196, 426, 210);
                body.Controls.Add(Theme.Lbl("Drawing on paper", 426, 404, 210, true)); var rot = Cb(426, 426, 210); rot.Items.AddRange(new object[] { "As in the layout", "Turned 90\u00B0" }); rot.SelectedIndex = 0;
                var lw = new CheckBox { Text = "Plot line weights", Left = 36, Top = 466, Width = 300, Height = 24, Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                body.Controls.AddRange(new Control[] { dev, med, sc, sty, rot, lw });
                body.Controls.Add(Theme.Lbl("A scale like 1:500 means 1 mm on paper = 500 mm of drawing.", 36, 494, 600, true));

                body.Controls.Add(Theme.Lbl("Output", 16, 530, 300));
                var po = Group(body, 554, 28); var pdf = Rb("PDF files", 20, 0, 140, true); var prn = Rb("Printer (as chosen above)", 170, 0, 300, false); po.Controls.AddRange(new Control[] { pdf, prn });
                var pc = Group(body, 584, 28); var one = Rb("One PDF with all pages", 36, 0, 230, true); var sep = Rb("One PDF per layout", 280, 0, 230, false); pc.Controls.AddRange(new Control[] { one, sep });
                body.Controls.Add(Theme.Lbl("Folder", 36, 620, 200, true)); var fold = new TextBox { Left = 36, Top = 642, Width = 480, Text = defFolder, BorderStyle = BorderStyle.FixedSingle }; Theme.Style(fold); var br = Theme.Btn("Browse...", 528, 640, 108);
                body.Controls.Add(Theme.Lbl("File name", 36, 676, 90, true)); var pat = new TextBox { Left = 130, Top = 674, Width = 260, Text = "{drawing}-{layout}", BorderStyle = BorderStyle.FixedSingle }; Theme.Style(pat);
                var ex = Theme.Lbl("", 36, 704, 600, true);
                Action exUpd = () => ex.Text = pdf.Checked ? (one.Checked ? "File: " + PlotNaming.FileName("{drawing}", drawing, "") + ".pdf" : "e.g. " + PlotNaming.FileName(pat.Text, drawing, rows.Count > 0 ? rows[0].Name : "Layout1") + ".pdf") : "";
                pat.TextChanged += (s, e) => exUpd(); one.CheckedChanged += (s, e) => exUpd();
                body.Controls.AddRange(new Control[] { fold, br, pat, ex });
                br.Click += (s, e) => { using (var d = new FolderBrowserDialog { SelectedPath = fold.Text }) if (d.ShowDialog(f) == DialogResult.OK) fold.Text = d.SelectedPath; };
                body.AutoScrollMinSize = new Size(660, 736);

                List<string[]> media = new List<string[]>();
                Action fillMed = () =>
                {
                    med.Items.Clear(); media = dev.Text.Length == 0 ? new List<string[]>() : lists.Media(dev.Text); foreach (var m in media) med.Items.Add(m[1]);
                    // Start on the paper of the first ticked layout (A4 stays A4), otherwise any A4, otherwise the first.
                    int idx = -1; string want = rows.Count > 0 ? rows[0].Canon : null;
                    if (!string.IsNullOrEmpty(want)) idx = media.FindIndex(m => string.Equals(m[0], want, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0) { idx = media.FindIndex(m => m[0].IndexOf("A4", StringComparison.OrdinalIgnoreCase) >= 0 && m[0].IndexOf("Extended", StringComparison.OrdinalIgnoreCase) < 0 && m[0].IndexOf("Tab", StringComparison.OrdinalIgnoreCase) < 0); }
                    if (med.Items.Count > 0) med.SelectedIndex = idx >= 0 ? idx : 0;
                };
                Action fillDev = () =>
                {
                    var keep = dev.Text; dev.Items.Clear();
                    foreach (var d in lists.Devices.Where(d => pdf.Checked ? d.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) >= 0 : d.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) < 0 && !d.Equals("None", StringComparison.OrdinalIgnoreCase))) dev.Items.Add(d);
                    int i = dev.Items.IndexOf(keep); if (i < 0) i = pdf.Checked ? Pick(dev, "DWG To PDF", "PDF") : (dev.Items.Count > 0 ? 0 : -1);
                    dev.SelectedIndex = i; fillMed(); exUpd();
                };
                dev.SelectedIndexChanged += (s, e) => fillMed();
                pdf.CheckedChanged += (s, e) => { fillDev(); one.Enabled = sep.Enabled = br.Enabled = fold.Enabled = pat.Enabled = pdf.Checked; };
                sty.Items.Add("(as in the layout)"); foreach (var st in lists.Styles) sty.Items.Add(st); sty.SelectedIndex = 0;
                fillDev();
                Action custom = () => { bool on = my.Checked; dev.Enabled = med.Enabled = sc.Enabled = sty.Enabled = rot.Enabled = lw.Enabled = on; };
                my.CheckedChanged += (s, e) => custom(); custom();

                var ok = Theme.Btn("Plot", 0, 14, 120, true); var cancel = Theme.Btn("Cancel", 0, 14, 100); ok.Anchor = cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                bar.Controls.AddRange(new Control[] { ok, cancel }); PlotChoice res = null;
                Action place = () => { cancel.Left = bar.ClientSize.Width - cancel.Width - 20; ok.Left = cancel.Left - ok.Width - 10; };
                bar.Resize += (s, e) => place(); f.Load += (s, e) => place();
                ok.Click += (s, e) =>
                {
                    var ticked = new List<string>(); foreach (DataGridViewRow r in grid.Rows) if (Convert.ToBoolean(r.Cells[0].Value)) ticked.Add(Convert.ToString(r.Cells[1].Value));
                    if (ticked.Count == 0) { MessageBox.Show(f, "Tick at least one layout.", "B+ CAD Tools"); return; }
                    if (string.IsNullOrWhiteSpace(fold.Text) && pdf.Checked) { MessageBox.Show(f, "Choose a folder for the PDF files.", "B+ CAD Tools"); return; }
                    if (my.Checked) { if (dev.Text.Length == 0) { MessageBox.Show(f, "No printer or PDF device is available for this choice.", "B+ CAD Tools"); return; } if (PlotNaming.ParseScale(sc.Text) < 0) { MessageBox.Show(f, "Scale: type Fit or something like 1:500.", "B+ CAD Tools"); return; } }
                    res = new PlotChoice { OwnSetup = own.Checked, Pdf = pdf.Checked, Combined = one.Checked, Lineweights = lw.Checked, Device = dev.Text, MediaCanonical = med.SelectedIndex >= 0 && med.SelectedIndex < media.Count ? media[med.SelectedIndex][0] : null, Scale = sc.Text, Style = sty.SelectedIndex > 0 ? sty.Text : "", Folder = fold.Text.Trim(), Pattern = pat.Text, Rotation = rot.SelectedIndex, Layouts = ticked };
                    f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel; f.AcceptButton = ok; f.CancelButton = cancel;
                // Fit the screen: at most the working area, centred.
                int w = Math.Min(f.Width, wa.Width - 20), h = Math.Min(f.Height, wa.Height - 20); f.Size = new Size(w, h);
                f.Location = new Point(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2);
                return AcApp.ShowModalDialog(f) == DialogResult.OK ? res : null;
            }
        }
    }
}
