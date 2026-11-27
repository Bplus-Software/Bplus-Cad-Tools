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
        /// <summary>Per layout overrides from the list: name -> { canonical paper or null, scale or null, rotation (0/1) or null }. Used only with "My settings".</summary>
        public Dictionary<string, string[]> Over = new Dictionary<string, string[]>();
        public PlotChoice For(string layout)
        {
            var c = (PlotChoice)MemberwiseClone(); string[] o; if (OwnSetup || !Over.TryGetValue(layout, out o)) return c;
            if (!string.IsNullOrEmpty(o[0])) c.MediaCanonical = o[0]; if (!string.IsNullOrEmpty(o[1])) c.Scale = o[1]; if (!string.IsNullOrEmpty(o[2])) c.Rotation = int.Parse(o[2]); return c;
        }
    }

    static class PlotUi
    {
        static RadioButton Rb(string t, int x, int y, int w, bool on) { return new RadioButton { Text = t, Left = x, Top = y, Width = w, Height = 24, Checked = on, ForeColor = Theme.Text, BackColor = Color.Transparent, AutoSize = false }; }
        static ComboBox Cb(int x, int y, int w) { var c = new ComboBox { Left = x, Top = y, Width = w, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat }; Theme.Style(c); return c; }
        static Panel Group(Panel body, int y, int h) { var p = new Panel { Left = 0, Top = y, Width = 680, Height = h, BackColor = Color.Transparent }; body.Controls.Add(p); return p; }
        static int Pick(ComboBox c, params string[] likes) { foreach (var l in likes) for (int i = 0; i < c.Items.Count; i++) if (c.Items[i].ToString().IndexOf(l, StringComparison.OrdinalIgnoreCase) >= 0) return i; return c.Items.Count > 0 ? 0 : -1; }

        static Panel Card(Panel body, int y, int h, string title)
        {
            var p = new Panel { Left = 16, Top = y, Width = 668, Height = h, BackColor = Theme.Panel };
            p.Controls.Add(new Label { Text = title, Left = 16, Top = 10, Width = 300, Height = 18, ForeColor = Theme.Dim, BackColor = Color.Transparent, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) });
            body.Controls.Add(p); return p;
        }
        static Label Small(Panel p, string t, int x, int y, int w) { var l = Theme.Lbl(t, x, y, w, true); l.Height = 18; p.Controls.Add(l); return l; }

        public static PlotChoice Ask(List<LayoutRow> rows, string drawing, string defFolder, PlotLists lists, bool arabic = false, bool oldAcad = false)
        {
            using (var f = Theme.Base("B+ CAD Tools - Plot layouts", 700, 700))
            {
                // Three calm cards: Layouts, Page, Save as. Scales with the screen's DPI, sizes itself to its content (capped to the screen), buttons always visible.
                f.AutoScaleMode = AutoScaleMode.None; // no WinForms autoscale (it mixed scaled and unscaled cards on his screen); all geometry is scaled once by Theme.Scale below
                f.FormBorderStyle = FormBorderStyle.Sizable; f.MaximizeBox = true; f.MinimumSize = new Size(560, 420);
                var wa = Screen.FromControl(f).WorkingArea; f.StartPosition = FormStartPosition.Manual;
                var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Bar };
                var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bar }; // scrolls only when the screen is too small; Plot/Cancel bar is outside it
                Theme.DarkScroll(body); f.Controls.Add(body); f.Controls.Add(bar); bar.BringToFront(); body.BringToFront();

                // Card 1: layouts
                f.Font = new System.Drawing.Font("Segoe UI", 10f);
                // Only the layout list scrolls. Its height is what is left on the screen after the other cards, the button bar and the title bar.
                float sc0 = 1f; try { using (var g0 = f.CreateGraphics()) sc0 = g0.DpiY / 96f; } catch { }
                int availDesign = (int)((wa.Height - 20 - 45 * sc0) / sc0) - bar.Height - 16, full = Math.Max(rows.Count, 1) * 34 + 6;
                int listH = Math.Max(3 * 34 + 6, Math.Min(full, availDesign - 562));
                var c1 = Card(body, 14, 30 + listH + 30, "LAYOUTS");
                var all = new Label { Text = "All", Left = 540, Top = 10, Width = 40, Height = 18, ForeColor = Theme.Accent, BackColor = Color.Transparent, Cursor = Cursors.Hand };
                var none = new Label { Text = "None", Left = 590, Top = 10, Width = 60, Height = 18, ForeColor = Theme.Accent, BackColor = Color.Transparent, Cursor = Cursors.Hand };
                var list = new Panel { Left = 16, Top = 32, Width = 636, Height = listH, BackColor = Theme.Field, AutoScroll = true }; Theme.DarkScroll(list);
                var checks = new List<CheckBox>(); var rowMed = new List<ComboBox>(); var rowSc = new List<ComboBox>(); var rowRot = new List<ComboBox>(); var rowOv = new List<bool[]>(); List<string[]> media = null;
                // Name column is as wide as the longest name (30 to 330 px design), paper and scale follow right after it.
                int nameW = 150; try { nameW = rows.Select(q => TextRenderer.MeasureText(q.Name, f.Font).Width).DefaultIfEmpty(100).Max() + 40; } catch { }
                { float sn = 1f; try { using (var g1 = f.CreateGraphics()) sn = g1.DpiY / 96f; } catch { } nameW = (int)(nameW / sn); } nameW = Math.Max(110, Math.Min(190, nameW));
                for (int k = 0; k < rows.Count; k++)
                {
                    var r = rows[k];
                    var cb = new CheckBox { Text = r.Name, Left = 10, Top = 4 + k * 34, Width = nameW, Height = 28, Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                    int rx = 10 + nameW + 8;
                    var rm = new ComboBox { Left = rx, Top = 3 + k * 34, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat }; Theme.Style(rm);
                    var rs = new ComboBox { Left = rx + 158, Top = 3 + k * 34, Width = 84, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat }; Theme.Style(rs); rs.Items.AddRange(new object[] { "Fit", "1:50", "1:100", "1:200", "1:250", "1:500", "1:1000" }); rs.SelectedIndex = 0;
                    var ro = new ComboBox { Left = rx + 250, Top = 3 + k * 34, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat }; Theme.Style(ro); ro.Items.AddRange(new object[] { "As in the layout", "Turned 90\u00B0" }); ro.SelectedIndex = 0;
                    var flag = new bool[1]; rowOv.Add(flag); Action<ComboBox> mark = c0 => { flag[0] = true; c0.ForeColor = Theme.Text; };
                    rm.SelectionChangeCommitted += (s0, e0) => mark(rm); rs.SelectionChangeCommitted += (s0, e0) => mark(rs); ro.SelectionChangeCommitted += (s0, e0) => mark(ro);
                    rowMed.Add(rm); rowSc.Add(rs); rowRot.Add(ro);
                    list.Controls.Add(rm); list.Controls.Add(rs); list.Controls.Add(ro);
                    list.Controls.Add(cb); checks.Add(cb);
                }
                var cnt = Small(c1, "", 16, 32 + listH + 6, 636);
                Action upd = () => { int t = checks.Count(c => c.Checked); cnt.Text = t + " of " + rows.Count + " ticked. The Model tab is not plotted by this tool."; };
                foreach (var cb in checks) cb.CheckedChanged += (s, e) => upd(); upd();
                all.Click += (s, e) => { foreach (var c in checks) c.Checked = true; }; none.Click += (s, e) => { foreach (var c in checks) c.Checked = false; };
                c1.Controls.AddRange(new Control[] { all, none, list });

                // Card 2: page
                int y2 = 14 + c1.Height + 12;
                var c2 = Card(body, y2, 266, "PAGE");
                var pw = new Panel { Left = 16, Top = 30, Width = 636, Height = 26, BackColor = Color.Transparent }; c2.Controls.Add(pw);
                var own = Rb("Each layout's own page setup", 0, 0, 300, false); var my = Rb("My settings (same for all ticked)", 310, 0, 320, true); pw.Controls.AddRange(new Control[] { own, my });
                Small(c2, "Printer / PDF", 16, 64, 300); var dev = Cb(16, 84, 308); c2.Controls.Add(dev);
                Small(c2, "Paper size", 340, 64, 300); var med = Cb(340, 84, 312); c2.Controls.Add(med);
                Small(c2, "Scale (Fit or 1:500)", 16, 122, 140); var sc = new ComboBox { Left = 16, Top = 142, Width = 140, DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.Flat }; Theme.Style(sc); sc.Items.AddRange(new object[] { "Fit", "1:50", "1:100", "1:200", "1:250", "1:500", "1:1000" }); sc.Text = "Fit"; c2.Controls.Add(sc);
                Small(c2, "Plot style", 172, 122, 220); var sty = Cb(172, 142, 230); c2.Controls.Add(sty);
                Small(c2, "Drawing on paper", 418, 122, 230); var rot = Cb(418, 142, 234); rot.Items.AddRange(new object[] { "As in the layout", "Turned 90\u00B0" }); rot.SelectedIndex = 0; c2.Controls.Add(rot);
                var lw = new CheckBox { Text = "Plot line weights", Left = 16, Top = 182, Width = 300, Height = 24, Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent }; c2.Controls.Add(lw);
                Small(c2, "Fit plots the sheet at 1:1. A scale like 1:500 means 1 mm on paper = 500 mm of drawing.", 16, 212, 636);

                var warn = Small(c2, "", 16, 236, 636); warn.ForeColor = Color.FromArgb(255, 190, 80); warn.Height = 18; warn.Visible = false;
                Action warnUpd = () =>
                {
                    bool dwg = dev.Text.IndexOf("DWG To PDF", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (arabic && oldAcad && dwg) { warn.Text = "Arabic text found. On this AutoCAD version DWG To PDF can print Arabic letters separated. Try Microsoft Print to PDF."; warn.Visible = true; }
                    else if (arabic && dev.Text.IndexOf("microsoft", StringComparison.OrdinalIgnoreCase) >= 0) { warn.Text = "Arabic text found: Microsoft Print to PDF chosen so the letters stay joined."; warn.ForeColor = Theme.Dim; warn.Visible = true; }
                    else warn.Visible = false;
                };
                dev.TextChanged += (s, e) => warnUpd();

                // Card 3: save as
                int y3 = y2 + c2.Height + 12;
                var c3 = Card(body, y3, 198, "SAVE AS");
                var po = new Panel { Left = 16, Top = 30, Width = 636, Height = 26, BackColor = Color.Transparent }; c3.Controls.Add(po);
                var pdf = Rb("PDF files", 0, 0, 140, true); var prn = Rb("Printer (as chosen above)", 150, 0, 300, false); po.Controls.AddRange(new Control[] { pdf, prn });
                var pc = new Panel { Left = 16, Top = 58, Width = 636, Height = 26, BackColor = Color.Transparent }; c3.Controls.Add(pc);
                var one = Rb("One PDF with all pages", 0, 0, 240, true); var sep = Rb("One PDF per layout", 250, 0, 240, false); pc.Controls.AddRange(new Control[] { one, sep });
                Small(c3, "Folder", 16, 94, 80); var fold = new TextBox { Left = 100, Top = 90, Width = 440, Text = defFolder, BorderStyle = BorderStyle.FixedSingle }; Theme.Style(fold);
                var br = Theme.Btn("Browse...", 552, 88, 100); c3.Controls.AddRange(new Control[] { fold, br });
                Small(c3, "File name", 16, 126, 80); var pat = new TextBox { Left = 100, Top = 122, Width = 300, Text = "{drawing}-{layout}", BorderStyle = BorderStyle.FixedSingle }; Theme.Style(pat); c3.Controls.Add(pat);
                var ex = Small(c3, "", 100, 150, 552);
                var msNote = Small(c3, "Microsoft Print to PDF: Windows may ask where to save each file. Choose the folder and click Save. One PDF per layout. It stops after 2 minutes.", 16, 172, 636); msNote.Visible = false;
                dev.TextChanged += (s, e) => { bool m = dev.Text.IndexOf("microsoft", StringComparison.OrdinalIgnoreCase) >= 0; msNote.Visible = m; if (m) { sep.Checked = true; one.Enabled = false; } else one.Enabled = pdf.Checked; };
                Action exUpd = () => ex.Text = pdf.Checked ? (one.Checked ? "File: " + PlotNaming.FileName("{drawing}", drawing, "") + ".pdf" : "e.g. " + PlotNaming.FileName(pat.Text, drawing, rows.Count > 0 ? rows[0].Name : "Layout1") + ".pdf") : "";
                pat.TextChanged += (s, e) => exUpd(); one.CheckedChanged += (s, e) => exUpd();
                br.Click += (s, e) => { using (var d = new FolderBrowserDialog { SelectedPath = fold.Text }) if (d.ShowDialog(f) == DialogResult.OK) fold.Text = d.SelectedPath; };
                

                media = new List<string[]>();
                // Rows not changed by hand follow the Page card (dim text); a row changed by hand keeps its own value (normal text).
                Action syncRows = () =>
                {
                    for (int k = 0; k < rowMed.Count; k++)
                    {
                        var ovf = rowOv[k][0];
                        if (ovf) { if (rowMed[k].SelectedIndex < 0 && rowMed[k].Items.Count > 0) { rowOv[k][0] = false; ovf = false; } else continue; }
                        rowMed[k].ForeColor = rowSc[k].ForeColor = rowRot[k].ForeColor = Theme.Dim;
                        if (med.SelectedIndex >= 0 && med.SelectedIndex < rowMed[k].Items.Count) rowMed[k].SelectedIndex = med.SelectedIndex;
                        string st = sc.Text.Trim(); int si = rowSc[k].Items.IndexOf(st); if (si < 0 && st.Length > 0) si = rowSc[k].Items.Add(st); rowSc[k].SelectedIndex = si;
                        rowRot[k].SelectedIndex = Math.Max(0, rot.SelectedIndex);
                    }
                };
                med.SelectedIndexChanged += (s, e) => syncRows(); sc.TextChanged += (s, e) => syncRows(); rot.SelectedIndexChanged += (s, e) => syncRows();
                Action fillMed = () =>
                {
                    med.Items.Clear(); media = dev.Text.Length == 0 ? new List<string[]>() : lists.Media(dev.Text); foreach (var m in media) med.Items.Add(m[1]);
                    // Start on the paper of the first ticked layout (A4 stays A4), otherwise any A4, otherwise the first.
                    int idx = -1; string want = rows.Count > 0 ? rows[0].Canon : null;
                    if (!string.IsNullOrEmpty(want)) idx = media.FindIndex(m => string.Equals(m[0], want, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0) { idx = media.FindIndex(m => m[0].IndexOf("A4", StringComparison.OrdinalIgnoreCase) >= 0 && m[0].IndexOf("Extended", StringComparison.OrdinalIgnoreCase) < 0 && m[0].IndexOf("Tab", StringComparison.OrdinalIgnoreCase) < 0); }
                    if (med.Items.Count > 0) med.SelectedIndex = idx >= 0 ? idx : 0;
                    foreach (var rm in rowMed) { rm.Items.Clear(); foreach (var m in media) rm.Items.Add(m[1]); }
                    syncRows();
                };
                Action fillDev = () =>
                {
                    var keep = dev.Text; dev.Items.Clear();
                    Func<string, bool> sys = d => { var u = d.ToLowerInvariant(); return u.Contains("microsoft") || u.Contains("xps") || u.Contains("onenote") || u.Contains("fax") || u.Contains("send to"); };
                    // PDF files: AutoCAD PDF drivers first, plus the Windows Microsoft Print to PDF (not the default). Printer: real printers only.
                    foreach (var d in lists.Devices.Where(d => pdf.Checked ? d.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) >= 0 && (d.EndsWith(".pc3", StringComparison.OrdinalIgnoreCase) && !sys(d) || d.IndexOf("microsoft print to pdf", StringComparison.OrdinalIgnoreCase) >= 0) : d.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) < 0 && !sys(d) && !d.Equals("None", StringComparison.OrdinalIgnoreCase) && !d.EndsWith(".pc3", StringComparison.OrdinalIgnoreCase))) dev.Items.Add(d);
                    int i = dev.Items.IndexOf(keep); if (i < 0) i = pdf.Checked ? (arabic ? Pick(dev, "Microsoft Print To PDF", "DWG To PDF", "PDF") : Pick(dev, "DWG To PDF", "PDF")) : (dev.Items.Count > 0 ? 0 : -1);
                    dev.SelectedIndex = i; fillMed(); exUpd();
                };
                dev.SelectedIndexChanged += (s, e) => fillMed();
                pdf.CheckedChanged += (s, e) => { fillDev(); one.Enabled = pdf.Checked && dev.Text.IndexOf("microsoft", StringComparison.OrdinalIgnoreCase) < 0; sep.Enabled = br.Enabled = fold.Enabled = pat.Enabled = pdf.Checked; };
                sty.Items.Add("(as in the layout)"); foreach (var st in lists.Styles) sty.Items.Add(st); sty.SelectedIndex = 0;
                fillDev();
                Action custom = () => { bool on = my.Checked; foreach (var c in rowMed.Concat<ComboBox>(rowSc).Concat(rowRot)) c.Enabled = on; dev.Enabled = med.Enabled = sc.Enabled = sty.Enabled = rot.Enabled = lw.Enabled = on; };
                my.CheckedChanged += (s, e) => custom(); custom();

                var ok = Theme.Btn("Plot", 0, 14, 120, true); var cancel = Theme.Btn("Cancel", 0, 14, 100); ok.Anchor = cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                bar.Controls.AddRange(new Control[] { ok, cancel }); PlotChoice res = null;
                Action place = () => { cancel.Left = bar.ClientSize.Width - cancel.Width - 20; ok.Left = cancel.Left - ok.Width - 10; };
                bar.Resize += (s, e) => place(); f.Load += (s, e) => place();
                ok.Click += (s, e) =>
                {
                    var ticked = new List<string>(); for (int k = 0; k < checks.Count; k++) if (checks[k].Checked) ticked.Add(rows[k].Name);
                    if (ticked.Count == 0) { MessageBox.Show(f, Ar.T("Tick at least one layout."), Ar.T("B+ CAD Tools")); return; }
                    if (string.IsNullOrWhiteSpace(fold.Text) && pdf.Checked) { MessageBox.Show(f, Ar.T("Choose a folder for the PDF files."), Ar.T("B+ CAD Tools")); return; }
                    if (my.Checked) { if (med.SelectedIndex < 0 || med.SelectedIndex >= media.Count) { MessageBox.Show(f, Ar.T("Choose a paper size for this printer first."), Ar.T("B+ CAD Tools")); return; } if (dev.Text.Length == 0) { MessageBox.Show(f, Ar.T("No printer or PDF device is available for this choice."), Ar.T("B+ CAD Tools")); return; } if (PlotNaming.ParseScale(sc.Text) < 0) { MessageBox.Show(f, Ar.T("Scale: type Fit or something like 1:500."), Ar.T("B+ CAD Tools")); return; } }
                    res = new PlotChoice { OwnSetup = own.Checked, Pdf = pdf.Checked, Combined = one.Checked, Lineweights = lw.Checked, Device = dev.Text, MediaCanonical = med.SelectedIndex >= 0 && med.SelectedIndex < media.Count ? media[med.SelectedIndex][0] : null, Scale = sc.Text, Style = sty.SelectedIndex > 0 ? sty.Text : "", Folder = fold.Text.Trim(), Pattern = pat.Text, Rotation = rot.SelectedIndex, Layouts = ticked };
                    for (int k = 0; k < checks.Count; k++) { if (!checks[k].Checked || !rowOv[k][0]) continue; string cn = rowMed[k].SelectedIndex >= 0 && rowMed[k].SelectedIndex < media.Count ? media[rowMed[k].SelectedIndex][0] : null; string sc1 = rowSc[k].Text; string ro1 = rowRot[k].SelectedIndex.ToString(); res.Over[rows[k].Name] = new[] { cn, sc1, ro1 }; }
                    f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel; f.AcceptButton = ok; f.CancelButton = cancel;
                { float sx = 1f; try { using (var g = f.CreateGraphics()) sx = g.DpiY / 96f; } catch { } int designBottom = c3.Bottom; Theme.Scale(body, sx); bar.Height = (int)Math.Ceiling(bar.Height * sx); Theme.Scale(bar, sx); Theme.Fit(f, body, bar, wa, 716, designBottom); } f.Load += (s, e) => place();
                return AcApp.ShowModalDialog(f) == DialogResult.OK ? res : null;
            }
        }
    }
}
