using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.Runtime;
using BPlus.LiveMap.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BPlus.LiveMap
{
    static class AsmUi
    {
        public static List<SecAssembly> All() { return AssemblyMath.Unpack(Config.Assemblies); }
        static string N(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
        public static Color KindColor(string k) { switch (k) { case "road": return Color.White; case "walk": return Color.FromArgb(200, 120, 230); case "plant": return Color.FromArgb(70, 220, 120); case "tree": return Color.FromArgb(70, 220, 120); case "ped": return Color.FromArgb(255, 150, 90); case "slope": return Color.FromArgb(120, 190, 255); default: return Color.Gray; } }

        public static string Hint(string type)
        {
            switch (type)
            {
                case "Kerb": return "Kerb: A = height (m). The surface after it is that much higher.";
                case "Median": return "Median: width in the centre. A = kerb height (m) on both sides.";
                case "Tree": return "Tree: width of the planting strip. A = trunk height (m), B = canopy width (m).";
                case "Walkway": return "Walkway for pedestrians: width and slope. A = 1 also draws a person symbol.";
                case "Slope to ground": return "Slope to ground: put it last on a side. A = 1 : n (2 means 1:2), B = longest length (m). Goes down (fill) or up (cut) to meet the ground.";
                case "Ditch": return "Ditch: width. Slope % is how deep the middle goes.";
                default: return "Slope %: positive falls away from the centre, negative rises. Sidewalk -2 means it rises 2 % outward.";
            }
        }

        public static void Edit(string select)
        {
            var all = All(); int cur = 0; if (select != null) { int i = all.FindIndex(a => a.Name == select); if (i >= 0) cur = i; }
            using (var f = Theme.Base("B+ CAD Tools - Section assemblies", 940, 640))
            {
                f.Controls.Add(Theme.Lbl("Build the road section by hand, one element per row. Left of the line is negative. Sizes in metres. The assemblies saved here appear in Quick section. Defaults are examples to confirm, not a standard.", 16, 10, 900, true)); f.Controls[0].Height = 36;
                f.Controls.Add(Theme.Lbl("SecAssembly", 16, 54, 70));
                var name = new ComboBox { Left = 90, Top = 51, Width = 300, DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.Flat }; Theme.Style(name); f.Controls.Add(name);
                var nw = Theme.Btn("New", 400, 49, 60); var dup = Theme.Btn("Copy", 466, 49, 60); var del = Theme.Btn("Delete", 532, 49, 70); f.Controls.AddRange(new Control[] { nw, dup, del });
                var grid = new DataGridView { Left = 16, Top = 86, Width = 560, Height = 360, AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, EnableHeadersVisualStyles = false, BackgroundColor = Theme.Field, BorderStyle = BorderStyle.FixedSingle, GridColor = Theme.Panel, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 28 };
                grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Panel, ForeColor = Theme.Text, SelectionBackColor = Theme.Panel, SelectionForeColor = Theme.Text };
                grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Field, ForeColor = Theme.Text, SelectionBackColor = Theme.Accent, SelectionForeColor = Color.White };
                var ct = new DataGridViewComboBoxColumn { HeaderText = "Element", Width = 150, FlatStyle = FlatStyle.Flat }; ct.Items.AddRange(AssemblyMath.Types);
                var cs = new DataGridViewComboBoxColumn { HeaderText = "Side", Width = 70, FlatStyle = FlatStyle.Flat }; cs.Items.AddRange(new object[] { "L", "C", "R" });
                grid.Columns.Add(ct); grid.Columns.Add(cs);
                foreach (var h in new[] { "Width m", "Slope %", "A", "B" }) grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = h, Width = 70, SortMode = DataGridViewColumnSortMode.NotSortable });
                var hint = new Label { Left = 16, Top = 452, Width = 560, Height = 34, ForeColor = Theme.Dim, BackColor = Color.Transparent };
                var info = new Label { Left = 16, Top = 520, Width = 900, Height = 36, ForeColor = Color.FromArgb(255, 150, 120), BackColor = Color.Transparent };
                var add = Theme.Btn("Add element", 590, 86, 150); var ins = Theme.Btn("Insert above", 746, 86, 130); var dp = Theme.Btn("Duplicate row", 590, 122, 150); var up = Theme.Btn("Move up", 746, 122, 130); var dn = Theme.Btn("Move down", 590, 158, 150); var rm = Theme.Btn("Delete row", 746, 158, 130); var mir = Theme.Btn("Mirror left to right", 590, 194, 286);
                var typeAdd = new ComboBox { Left = 590, Top = 232, Width = 286, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat }; Theme.Style(typeAdd); typeAdd.Items.AddRange(AssemblyMath.Types); typeAdd.SelectedIndex = 0;
                var sideAdd = new ComboBox { Left = 590, Top = 262, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat }; Theme.Style(sideAdd); sideAdd.Items.AddRange(new object[] { "Left", "Right" }); sideAdd.SelectedIndex = 1;
                var pv = new Panel { Left = 590, Top = 300, Width = 330, Height = 190, BackColor = Theme.Field, BorderStyle = BorderStyle.FixedSingle };
                var total = Theme.Lbl("", 16, 490, 560, true);
                var save = Theme.Btn("Save", 760, 590, 80, true); var close = Theme.Btn("Close", 848, 590, 72);
                f.Controls.AddRange(new Control[] { grid, hint, info, add, ins, dp, up, dn, rm, mir, typeAdd, sideAdd, pv, total, save, close });

                Action redraw = () => pv.Invalidate();
                Action<AsmElem, int> addRow = (e, at) =>
                {
                    int r = at < 0 ? grid.Rows.Add() : at; if (at >= 0) grid.Rows.Insert(at, 1);
                    var row = grid.Rows[r]; row.Cells[0].Value = e.Type; row.Cells[1].Value = e.Side; row.Cells[2].Value = N(e.W); row.Cells[3].Value = N(e.Slope); row.Cells[4].Value = N(e.A); row.Cells[5].Value = N(e.B);
                };
                Func<bool, SecAssembly> readA = quiet =>
                {
                    var a = new SecAssembly { Name = name.Text.Trim() };
                    foreach (DataGridViewRow r in grid.Rows)
                    {
                        double w, s, x, y; Func<int, string> c = i => Convert.ToString(r.Cells[i].Value ?? "");
                        if (!Theme.TryNum(c(2), out w) || !Theme.TryNum(c(3), out s) || !Theme.TryNum(c(4), out x) || !Theme.TryNum(c(5), out y)) { if (!quiet) info.Text = "Row " + (r.Index + 1) + ": check the numbers."; if (quiet) continue; return null; }
                        var e = new AsmElem { Type = c(0), Side = c(1), W = w, Slope = s, A = x, B = y }; var m = AssemblyMath.Check(e); if (m != "") { if (!quiet) info.Text = "Row " + (r.Index + 1) + ": " + m; if (quiet) continue; return null; }
                        a.Items.Add(e);
                    }
                    return a;
                };
                Action refreshNames = () => { name.Items.Clear(); foreach (var a in all) name.Items.Add(a.Name); };
                Action load = () =>
                {
                    grid.Rows.Clear(); if (cur < 0 || cur >= all.Count) { name.Text = ""; return; }
                    name.Text = all[cur].Name; foreach (var e in all[cur].Items) addRow(e, -1); redraw();
                };
                Func<bool> stash = () => { var a = readA(false); if (a == null) return false; if (a.Name.Length == 0) { info.Text = "Give the assembly a name."; return false; } if (cur >= 0 && cur < all.Count) { if (all.Where((x, i) => i != cur).Any(x => x.Name == a.Name)) { info.Text = "Another assembly has this name."; return false; } all[cur] = a; } info.Text = ""; return true; };
                refreshNames(); name.Text = all[cur].Name; load();
                bool busy = false;
                name.SelectionChangeCommitted += (s, e) => { if (busy) return; int ni = name.SelectedIndex; if (ni < 0 || ni == cur) return; busy = true; if (stash()) { cur = ni; refreshNames(); load(); } else name.Text = all[cur].Name; busy = false; };
                nw.Click += (s, e) => { if (!stash()) return; var a = new SecAssembly { Name = "New assembly " + (all.Count + 1) }; a.Items.Add(AssemblyMath.Make("Lane", "L")); a.Items.Add(AssemblyMath.Make("Lane", "R")); all.Add(a); cur = all.Count - 1; refreshNames(); load(); };
                dup.Click += (s, e) => { if (!stash()) return; var a = all[cur].Copy(); a.Name = a.Name + " copy"; all.Add(a); cur = all.Count - 1; refreshNames(); load(); };
                del.Click += (s, e) => { if (all.Count <= 1) { info.Text = "Keep at least one assembly."; return; } all.RemoveAt(cur); cur = 0; refreshNames(); load(); };
                Func<AsmElem> mk = () => AssemblyMath.Make((string)typeAdd.SelectedItem, sideAdd.SelectedIndex == 0 ? "L" : "R");
                add.Click += (s, e) => { addRow(mk(), -1); redraw(); };
                ins.Click += (s, e) => { int i = grid.CurrentRow == null ? 0 : grid.CurrentRow.Index; addRow(mk(), i); redraw(); };
                dp.Click += (s, e) => { if (grid.CurrentRow == null) return; var a = readA(true); int i = grid.CurrentRow.Index; if (a == null || i >= a.Items.Count) return; addRow(a.Items[i].Copy(), i + 1); redraw(); };
                Action<int> move = d => { if (grid.CurrentRow == null) return; int i = grid.CurrentRow.Index, j = i + d; if (j < 0 || j >= grid.Rows.Count) return; var a = readA(true); if (a == null || a.Items.Count != grid.Rows.Count) { info.Text = "Fix the numbers first."; return; } var t = a.Items[i]; a.Items[i] = a.Items[j]; a.Items[j] = t; grid.Rows.Clear(); foreach (var x in a.Items) addRow(x, -1); grid.CurrentCell = grid.Rows[j].Cells[0]; redraw(); };
                up.Click += (s, e) => move(-1); dn.Click += (s, e) => move(1);
                rm.Click += (s, e) => { if (grid.CurrentRow != null) { grid.Rows.RemoveAt(grid.CurrentRow.Index); redraw(); } };
                mir.Click += (s, e) => { var a = readA(false); if (a == null) return; AssemblyMath.MirrorLeftToRight(a); grid.Rows.Clear(); foreach (var x in a.Items) addRow(x, -1); redraw(); info.Text = "Right side rebuilt from the left side."; };
                grid.CellValueChanged += (s, e) => { redraw(); if (e.ColumnIndex == 0) hint.Text = Hint(Convert.ToString(grid.Rows[e.RowIndex].Cells[0].Value)); };
                grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewComboBoxCell) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
                grid.SelectionChanged += (s, e) => { if (grid.CurrentRow != null) hint.Text = Hint(Convert.ToString(grid.CurrentRow.Cells[0].Value)); };
                grid.DataError += (s, e) => { e.ThrowException = false; };
                pv.Paint += (s, e) =>
                {
                    var a = readA(true); if (a == null || a.Items.Count == 0) return; AsmGeo g; try { g = AssemblyMath.Layout(a, 0, null); } catch { return; }
                    if (g.Segs.Count == 0) return; double w = Math.Max(0.5, g.Right - g.Left), h = Math.Max(0.2, g.MaxZ - g.MinZ); float sc = (float)Math.Min((pv.Width - 24) / w, (pv.Height - 24) / (h * 2));
                    float ox = 12 - (float)g.Left * sc, oy = pv.Height - 12 + (float)g.MinZ * sc * 2;
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    foreach (var sg in g.Segs) using (var pen = new Pen(KindColor(sg.Kind), 1.6f)) e.Graphics.DrawLine(pen, ox + (float)sg.X1 * sc, oy - (float)sg.Y1 * sc * 2, ox + (float)sg.X2 * sc, oy - (float)sg.Y2 * sc * 2);
                };
                grid.RowsAdded += (s, e) => redraw(); grid.RowsRemoved += (s, e) => { redraw(); total.Text = ""; };
                save.Click += (s, e) => { if (!stash()) return; Config.Assemblies = AssemblyMath.Pack(all); info.ForeColor = Color.FromArgb(120, 220, 140); info.Text = "Saved " + all.Count + " assembly(ies). They are in Quick section now."; };
                close.Click += (s, e) => f.DialogResult = DialogResult.Cancel; f.CancelButton = close;
                f.Controls.Add(Theme.Lbl("Preview (vertical x2, no ground)", 590, 284, 330, true));
                AcApp.ShowModalDialog(f);
            }
        }
    }

    public partial class Commands
    {
        [CommandMethod("BPASSEMBLY")]
        public static void SectionAssemblies() { Guard("BPASSEMBLY", () => AsmUi.Edit(null)); }
    }
}
