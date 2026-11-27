using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.EditorInput;
using BPlus.LiveMap.Core;

namespace BPlus.LiveMap
{
    /// <summary>Searchable list of coordinate systems. Returns the EPSG code, or 0 when cancelled.</summary>
    static class CsPicker
    {
        public static int Ask()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument; int current = doc == null ? 0 : DrawingCs.LoadEpsg(doc.Database);
            double unit = doc == null ? 1.0 : DrawingCs.UnitToMetre(doc.Database);
            var all = CoordSys.Catalog(); string group = "Saudi Arabia"; HashSet<int> only = null; double[] picked = null; var inv = CultureInfo.InvariantCulture;
            using (var f = Theme.Base("B+ CAD Tools - Coordinate system", 660, 660))
            {
                f.Controls.Add(Theme.Lbl("Search  (name, EPSG number, country or zone)", 16, 10, 400, true));
                var q = new TextBox { Left = 16, Top = 32, Width = 628, BorderStyle = BorderStyle.FixedSingle }; Theme.Style(q);
                var tabs = new[] { "All", "Saudi Arabia", "Egypt", "Gulf", "UTM zones", "Lat / long" }; var tabBtns = new List<Label>(); int tx = 16;
                var lv = new ListView { Left = 16, Top = 98, Width = 628, Height = 220, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, MultiSelect = false, HideSelection = false }; Theme.Style(lv); Theme.DarkList(lv);
                lv.Columns.Add("EPSG", 70); lv.Columns.Add("Name", 270); lv.Columns.Add("Where", 270);
                var count = Theme.Lbl("", 16, 322, 400, true); var sel = Theme.Lbl("", 16, 350, 628, false); var det = Theme.Lbl("", 16, 374, 628, true);
                var pickLbl = Theme.Lbl("", 190, 432, 460, false); var pickBtn = Theme.Btn("Pick a point", 16, 426, 160); var sugBtn = Theme.Btn("Suggest", 16, 468, 160);
                var sugLbl = Theme.Lbl("Suggest tries every zone on the picked point and keeps those that land in Saudi Arabia or Egypt.", 190, 474, 460, true);
                Action refresh = () =>
                {
                    string t = q.Text.Trim().ToLowerInvariant(); lv.Items.Clear(); int n = 0;
                    foreach (var c in all)
                    {
                        if (only != null) { if (!only.Contains(c.Epsg)) continue; }
                        else if (group != "All" && !c.Groups.Contains(group)) continue;
                        string text = (c.Epsg + " " + c.Name + " " + c.Region + " " + string.Join(" ", c.Groups)).ToLowerInvariant();
                        if (t.Length > 0 && !t.Split(' ').All(w => text.Contains(w))) continue;
                        var it = new ListViewItem(new[] { c.Epsg.ToString(), c.Name + (c.Unverified ? "   (unverified)" : ""), c.Region }) { Tag = c }; lv.Items.Add(it); n++;
                        if (c.Epsg == current && lv.SelectedItems.Count == 0) it.Selected = true;
                    }
                    count.Text = n + " of " + all.Count + " systems shown";
                };
                Action<CoordSys> show = c =>
                {
                    if (c == null) { sel.Text = "Select a system in the list."; det.Text = ""; pickLbl.Text = ""; return; }
                    sel.Text = "Selected:  EPSG " + c.Epsg + "  " + c.Name;
                    det.Text = c.IsGeographic ? "Degrees (longitude, latitude)." : c.Unverified ? "Metres. UNVERIFIED: checked against PROJ on test points, not on a known point of yours. Datum shift is about 5 m from the best published one." : "Metres.";
                    if (picked != null)
                    {
                        c.UnitToMetre = unit; try { double lo, la; c.ToLonLat(picked[0], picked[1], out lo, out la); pickLbl.Text = "E " + picked[0].ToString("F3", inv) + "  N " + picked[1].ToString("F3", inv) + "   ->   " + la.ToString("F5", inv) + " N  " + lo.ToString("F5", inv) + " E"; } catch { pickLbl.Text = "This point does not fit this system."; }
                    }
                };
                foreach (var name in tabs)
                {
                    string nm = name; var lb = new Label { Text = nm, Left = tx, Top = 68, Width = nm.Length * 8 + 18, Height = 22, ForeColor = nm == group ? Theme.Accent : Theme.Dim, BackColor = Color.Transparent, Cursor = Cursors.Hand, Font = new Font(f.Font, nm == group ? FontStyle.Bold : FontStyle.Regular) };
                    tx += lb.Width + 6; tabBtns.Add(lb); f.Controls.Add(lb);
                    lb.Click += (s, e) => { group = nm; only = null; foreach (var t2 in tabBtns) { bool on = t2.Text == group; t2.ForeColor = on ? Theme.Accent : Theme.Dim; t2.Font = new Font(f.Font, on ? FontStyle.Bold : FontStyle.Regular); } refresh(); };
                }
                q.TextChanged += (s, e) => refresh();
                lv.SelectedIndexChanged += (s, e) => show(lv.SelectedItems.Count > 0 ? lv.SelectedItems[0].Tag as CoordSys : null);
                pickBtn.Click += (s, e) =>
                {
                    f.Hide(); try { var ed = doc.Editor; var r = ed.GetPoint(Ar.T("\nPick a point of your drawing: ")); if (r.Status == PromptStatus.OK) picked = new[] { r.Value.X, r.Value.Y }; } catch { }
                    f.Show(); f.Activate(); show(lv.SelectedItems.Count > 0 ? lv.SelectedItems[0].Tag as CoordSys : null);
                };
                sugBtn.Click += (s, e) =>
                {
                    if (picked == null) { MessageBox.Show(f, Ar.T("Pick a point first."), Ar.T("B+ CAD Tools")); return; }
                    var g = CsSuggest.Suggest(picked[0], picked[1], unit);
                    if (g.Count == 0) { MessageBox.Show(f, Ar.T("No system puts this point in Saudi Arabia or Egypt. Check the drawing units and the numbers."), Ar.T("B+ CAD Tools")); return; }
                    only = new HashSet<int>(g.Select(x => x.Cs.Epsg)); q.Text = ""; refresh();
                    sugLbl.Text = g.Count + " system(s) fit. Check one against the live map before you trust it.";
                };
                f.Controls.AddRange(new Control[] { q, lv, count, sel, det, pickLbl, pickBtn, sugBtn, sugLbl });
                f.Controls.Add(Theme.Lbl("The choice is saved inside this drawing. The live map, KML import / export and", 16, 520, 628, true));
                f.Controls.Add(Theme.Lbl("Pick coordinates all use it. Change it later with BPGMAPCS.", 16, 542, 628, true));
                var ok = Theme.Btn("Apply", 440, 596, 110, true); var cancel = Theme.Btn("Cancel", 560, 596, 84); int result = 0;
                ok.Click += (s, e) => { if (lv.SelectedItems.Count == 0) { MessageBox.Show(f, Ar.T("Select a system in the list first."), Ar.T("B+ CAD Tools")); return; } result = ((CoordSys)lv.SelectedItems[0].Tag).Epsg; f.DialogResult = DialogResult.OK; };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel; f.Controls.AddRange(new Control[] { ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                refresh(); show(lv.SelectedItems.Count > 0 ? lv.SelectedItems[0].Tag as CoordSys : null);
                return AcApp.ShowModalDialog(f) == DialogResult.OK ? result : 0;
            }
        }
    }
}
