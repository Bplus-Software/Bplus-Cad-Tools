using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BPlus.LiveMap
{
    sealed class LayoutRow { public string Name, Setup, Paper, Scale; public bool Ticked = true; }
    sealed class PlotChoice
    {
        public List<string> Layouts = new List<string>(); public bool OwnSetup, Pdf = true, Combined = true, Lineweights = true;
        public string Device, MediaCanonical, Scale = "Fit", Style = "", Folder = "", Pattern = "{drawing}-{layout}"; public int Rotation;
    }

    static class PlotUi
    {
        static RadioButton Rb(string t, int x, int y, int w, bool on) { return new RadioButton { Text = t, Left = x, Top = y, Width = w, Height = 22, Checked = on, ForeColor = Theme.Text, BackColor = Color.Transparent }; }
        static ComboBox Cb(int x, int y, int w) { var c = new ComboBox { Left = x, Top = y, Width = w, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat }; Theme.Style(c); return c; }

        public static PlotChoice Ask(List<LayoutRow> rows, string drawing, string defFolder, PlotLists lists)
        {
            using (var f = Theme.Base("B+ CAD Tools - Plot layouts", 660, 760))
            {
                f.Controls.Add(Theme.Lbl("Layouts in this drawing", 16, 10, 300));
                var all = new Label { Text = "All", Left = 470, Top = 12, Width = 30, Height = 20, ForeColor = Theme.Accent, BackColor = Color.Transparent, Cursor = Cursors.Hand };
                var none = new Label { Text = "None", Left = 510, Top = 12, Width = 40, Height = 20, ForeColor = Theme.Accent, BackColor = Color.Transparent, Cursor = Cursors.Hand };
                var lv = new ListView { Left = 16, Top = 36, Width = 628, Height = 170, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, CheckBoxes = true, HideSelection = false, OwnerDraw = true }; Theme.Style(lv);
                lv.Columns.Add("Layout", 190); lv.Columns.Add("Page setup", 150); lv.Columns.Add("Paper", 170); lv.Columns.Add("Scale", 100);
                lv.DrawColumnHeader += (s, e) => { using (var b = new SolidBrush(Theme.Panel)) e.Graphics.FillRectangle(b, e.Bounds); TextRenderer.DrawText(e.Graphics, e.Header.Text, lv.Font, new Rectangle(e.Bounds.X + (e.ColumnIndex == 0 ? 26 : 6), e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis); };
                lv.DrawItem += (s, e) => { };
                lv.DrawSubItem += (s, e) =>
                {
                    using (var b = new SolidBrush(e.Item.Selected ? Theme.Accent : Theme.Field)) e.Graphics.FillRectangle(b, e.Bounds);
                    int off = 6;
                    if (e.ColumnIndex == 0)
                    {
                        var box = new Rectangle(e.Bounds.X + 5, e.Bounds.Y + (e.Bounds.Height - 14) / 2, 14, 14);
                        using (var b = new SolidBrush(e.Item.Checked ? Theme.Accent : Theme.Field)) e.Graphics.FillRectangle(b, box);
                        using (var p = new Pen(Theme.Text)) e.Graphics.DrawRectangle(p, box);
                        if (e.Item.Checked) using (var p = new Pen(Color.White, 2f)) { e.Graphics.DrawLine(p, box.X + 3, box.Y + 7, box.X + 6, box.Y + 10); e.Graphics.DrawLine(p, box.X + 6, box.Y + 10, box.X + 11, box.Y + 4); }
                        off = 26;
                    }
                    TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lv.Font, new Rectangle(e.Bounds.X + off, e.Bounds.Y, e.Bounds.Width - off - 2, e.Bounds.Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                };
                foreach (var r in rows) lv.Items.Add(new ListViewItem(new[] { r.Name, r.Setup, r.Paper, r.Scale }) { Checked = true });
                var cnt = Theme.Lbl("", 16, 210, 628, true); Action upd = () => cnt.Text = lv.CheckedItems.Count + " of " + rows.Count + " layouts ticked. The Model tab is not plotted by this tool.";
                lv.ItemChecked += (s, e) => upd(); upd();
                all.Click += (s, e) => { foreach (ListViewItem i in lv.Items) i.Checked = true; }; none.Click += (s, e) => { foreach (ListViewItem i in lv.Items) i.Checked = false; };
                f.Controls.AddRange(new Control[] { all, none, lv, cnt });

                f.Controls.Add(Theme.Lbl("Plot with", 16, 240, 300));
                var own = Rb("Each layout's own page setup", 20, 264, 420, false); var my = Rb("My settings (the same for all ticked layouts)", 20, 290, 420, true);
                var pw = new Panel { Left = 0, Top = 262, Width = 660, Height = 54, BackColor = Color.Transparent }; own.Top -= 262; my.Top -= 262; pw.Controls.AddRange(new Control[] { own, my }); f.Controls.Add(pw);
                f.Controls.Add(Theme.Lbl("A layout with no printer or PDF set in its page setup uses my settings below.", 44, 318, 590, true));
                f.Controls.Add(Theme.Lbl("Printer / PDF", 36, 344, 250, true)); var dev = Cb(36, 362, 290);
                f.Controls.Add(Theme.Lbl("Paper size", 340, 344, 250, true)); var med = Cb(340, 362, 304);
                f.Controls.Add(Theme.Lbl("Scale  (Fit, or 1:500 ...)", 36, 398, 180, true)); var sc = new ComboBox { Left = 36, Top = 416, Width = 130, DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.Flat }; Theme.Style(sc); sc.Items.AddRange(new object[] { "Fit", "1:50", "1:100", "1:200", "1:250", "1:500", "1:1000", "1:2000", "1:5000" }); sc.Text = "Fit";
                f.Controls.Add(Theme.Lbl("Plot style", 180, 398, 160, true)); var sty = Cb(180, 416, 190);
                f.Controls.Add(Theme.Lbl("Drawing on paper", 384, 398, 200, true)); var rot = Cb(384, 416, 150); rot.Items.AddRange(new object[] { "As in the layout", "Turned 90\u00B0" }); rot.SelectedIndex = 0;
                var lw = new CheckBox { Text = "Plot line weights", Left = 36, Top = 452, Width = 300, Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                f.Controls.AddRange(new Control[] { dev, med, sc, sty, rot, lw });
                f.Controls.Add(Theme.Lbl("Scale is paper millimetres to the drawing's units (set from the drawing's INSUNITS; metres if not set).", 36, 480, 600, true));

                f.Controls.Add(Theme.Lbl("Output", 16, 516, 300));
                var pdf = Rb("PDF files", 20, 542, 120, true); var prn = Rb("Printer (as chosen above)", 150, 542, 250, false);
                var po = new Panel { Left = 0, Top = 540, Width = 660, Height = 26, BackColor = Color.Transparent }; pdf.Top -= 540; prn.Top -= 540; po.Controls.AddRange(new Control[] { pdf, prn }); f.Controls.Add(po);
                var one = Rb("One PDF with all pages", 36, 570, 210, true); var sep = Rb("One PDF per layout", 260, 570, 200, false);
                var pc = new Panel { Left = 0, Top = 568, Width = 660, Height = 26, BackColor = Color.Transparent }; one.Top -= 568; sep.Top -= 568; pc.Controls.AddRange(new Control[] { one, sep }); f.Controls.Add(pc);
                f.Controls.Add(Theme.Lbl("Folder", 36, 600, 200, true)); var fold = new TextBox { Left = 36, Top = 620, Width = 460, Text = defFolder, BorderStyle = BorderStyle.FixedSingle }; Theme.Style(fold); var br = Theme.Btn("Browse...", 506, 618, 100);
                f.Controls.Add(Theme.Lbl("File name", 36, 650, 100, true)); var pat = new TextBox { Left = 120, Top = 648, Width = 220, Text = "{drawing}-{layout}", BorderStyle = BorderStyle.FixedSingle }; Theme.Style(pat);
                var ex = Theme.Lbl("", 350, 652, 290, true);
                Action exUpd = () => ex.Text = "e.g. " + BPlus.LiveMap.Core.PlotNaming.FileName(pat.Text, drawing, rows.Count > 0 ? rows[0].Name : "Layout1") + ".pdf" + (one.Checked && pdf.Checked ? "   (combined: " + BPlus.LiveMap.Core.PlotNaming.FileName("{drawing}", drawing, "") + ".pdf)" : "");
                pat.TextChanged += (s, e) => exUpd(); one.CheckedChanged += (s, e) => exUpd();
                f.Controls.AddRange(new Control[] { fold, br, pat, ex });
                br.Click += (s, e) => { using (var d = new FolderBrowserDialog { SelectedPath = fold.Text }) if (d.ShowDialog(f) == DialogResult.OK) fold.Text = d.SelectedPath; };

                Action fillDev = () =>
                {
                    var keep = dev.Text; dev.Items.Clear();
                    foreach (var d in lists.Devices.Where(d => pdf.Checked ? d.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) >= 0 : d.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) < 0 && !d.Equals("None", StringComparison.OrdinalIgnoreCase))) dev.Items.Add(d);
                    int i = dev.Items.IndexOf(keep); if (i < 0 && pdf.Checked) i = dev.Items.IndexOf("DWG To PDF.pc3"); dev.SelectedIndex = dev.Items.Count == 0 ? -1 : Math.Max(0, i); exUpd();
                };
                List<string[]> media = new List<string[]>();
                Action fillMed = () => { med.Items.Clear(); media = dev.Text.Length == 0 ? new List<string[]>() : lists.Media(dev.Text); foreach (var m in media) med.Items.Add(m[1]); if (med.Items.Count > 0) med.SelectedIndex = 0; };
                dev.SelectedIndexChanged += (s, e) => fillMed(); pdf.CheckedChanged += (s, e) => { fillDev(); one.Enabled = sep.Enabled = pdf.Checked; };
                foreach (var st in lists.Styles) sty.Items.Add(st); sty.Items.Insert(0, "(as in the layout)"); sty.SelectedIndex = 0;
                fillDev();
                Action custom = () => { bool on = my.Checked; dev.Enabled = med.Enabled = sc.Enabled = sty.Enabled = rot.Enabled = lw.Enabled = on; };
                my.CheckedChanged += (s, e) => custom(); custom();

                var ok = Theme.Btn("Plot", 440, 710, 110, true); var cancel = Theme.Btn("Cancel", 560, 710, 84); PlotChoice res = null;
                ok.Click += (s, e) =>
                {
                    if (lv.CheckedItems.Count == 0) { MessageBox.Show(f, "Tick at least one layout.", "B+ CAD Tools"); return; }
                    if (string.IsNullOrWhiteSpace(fold.Text) && pdf.Checked) { MessageBox.Show(f, "Choose a folder for the PDF files.", "B+ CAD Tools"); return; }
                    if (my.Checked) { if (dev.Text.Length == 0) { MessageBox.Show(f, "No printer or PDF device is available for this choice.", "B+ CAD Tools"); return; } if (BPlus.LiveMap.Core.PlotNaming.ParseScale(sc.Text) < 0) { MessageBox.Show(f, "Scale: type Fit or something like 1:500.", "B+ CAD Tools"); return; } }
                    res = new PlotChoice { OwnSetup = own.Checked, Pdf = pdf.Checked, Combined = one.Checked, Lineweights = lw.Checked, Device = dev.Text, MediaCanonical = med.SelectedIndex >= 0 && med.SelectedIndex < media.Count ? media[med.SelectedIndex][0] : null, Scale = sc.Text, Style = sty.SelectedIndex > 0 ? sty.Text : "", Folder = fold.Text.Trim(), Pattern = pat.Text, Rotation = rot.SelectedIndex };
                    foreach (ListViewItem i in lv.CheckedItems) res.Layouts.Add(i.SubItems[0].Text);
                    f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel; f.Controls.AddRange(new Control[] { ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                return AcApp.ShowModalDialog(f) == DialogResult.OK ? res : null;
            }
        }
    }
}
