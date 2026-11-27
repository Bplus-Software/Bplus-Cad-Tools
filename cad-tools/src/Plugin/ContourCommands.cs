using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using BPlus.LiveMap.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcPoly = Autodesk.AutoCAD.DatabaseServices.Polyline;
using ColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace BPlus.LiveMap
{
    sealed class ContourChoice { public double Interval = 1, MaxEdge; public int Every = 5, Mode; public bool Labels = true, LabelAll, RealZ = true; public double TextH; public short IndexCol = 2, MidCol = 4; }

    static class ContourUi
    {
        public static ContourChoice Ask(string found, double suggested, bool tri, double autoTh)
        {
            var res = new ContourChoice { Interval = suggested }; bool good = false;
            using (var f = Theme.Base("B+ CAD Tools - Quick contour", 520, 342))
            {
                f.Controls.Add(Theme.Lbl(found, 16, 10, 490, true));
                f.Controls.Add(Theme.Lbl("Contour interval (m)", 16, 52, 170)); var iv = new TextBox { Left = 16, Top = 72, Width = 90, Text = suggested.ToString("0.###", CultureInfo.InvariantCulture), BorderStyle = BorderStyle.FixedSingle }; Theme.Style(iv);
                f.Controls.Add(Theme.Lbl("Index line every N", 200, 52, 170)); var ev = new TextBox { Left = 200, Top = 72, Width = 90, Text = "5", BorderStyle = BorderStyle.FixedSingle }; Theme.Style(ev); f.Controls.AddRange(new Control[] { iv, ev });
                var lab = new CheckBox { Left = 16, Top = 108, Width = 480, Text = "Elevation labels on the index lines", Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                f.Controls.Add(lab); f.Controls.Add(Theme.Lbl("Colour", 16, 142, 100, true));
                var m0 = new RadioButton { Left = 16, Top = 162, Width = 150, Text = "Index / intermediate", Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent }; var m1 = new RadioButton { Left = 176, Top = 162, Width = 200, Text = "By elevation (blue to red)", ForeColor = Theme.Text, BackColor = Color.Transparent }; var m2 = new RadioButton { Left = 380, Top = 162, Width = 120, Text = "One colour", ForeColor = Theme.Text, BackColor = Color.Transparent };
                f.Controls.AddRange(new Control[] { m0, m1, m2 });
                var adv = new Panel { Left = 0, Top = 232, Width = 510, Height = 160, BackColor = Color.Transparent };
                var la = new CheckBox { Left = 16, Top = 4, Width = 480, Text = "Label every contour, not only the index lines", ForeColor = Theme.Text, BackColor = Color.Transparent };
                adv.Controls.Add(la);
                adv.Controls.Add(Theme.Lbl("Label text height (drawing units)", 16, 37, 280)); var th = new TextBox { Left = 300, Top = 34, Width = 90, Text = autoTh.ToString("0.###", CultureInfo.InvariantCulture), BorderStyle = BorderStyle.FixedSingle }; Theme.Style(th); adv.Controls.Add(th);
                adv.Controls.Add(Theme.Lbl("Index colour", 16, 69, 100)); var ci = Commands.GLCombo(120, 66, 100); ci.SelectedIndex = 1; adv.Controls.Add(ci);
                adv.Controls.Add(Theme.Lbl("Intermediate", 250, 69, 100)); var cm = Commands.GLCombo(350, 66, 100); cm.SelectedIndex = 2; adv.Controls.Add(cm);
                var rz = new CheckBox { Left = 16, Top = 96, Width = 480, Text = "Draw each line at its real height (untick = flat at 0)", Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent }; adv.Controls.Add(rz);
                var me = new TextBox { Left = 300, Top = 126, Width = 90, Text = "0", BorderStyle = BorderStyle.FixedSingle, Enabled = tri }; Theme.Style(me);
                adv.Controls.Add(Theme.Lbl("Longest triangle side to trust (m), 0 = off", 16, 129, 280)); adv.Controls.Add(me); f.Controls.Add(adv);
                var advBtn = Theme.Btn("Advanced options  (show)", 16, 196, 220); f.Controls.Add(advBtn);
                var info = new Label { Left = 16, Top = 236, Width = 490, Height = 36, ForeColor = Color.FromArgb(255, 150, 120), BackColor = Color.Transparent }; f.Controls.Add(info);
                var ok = Theme.Btn("Draw", 310, 272, 90, true); var cancel = Theme.Btn("Cancel", 410, 272, 90); f.Controls.AddRange(new Control[] { ok, cancel });
                Action<bool> setAdv = open => { SectionUi.AdvOpen = open; adv.Visible = open; int d0 = open ? 160 : 0; info.Top = 236 + d0; ok.Top = 272 + d0; cancel.Top = 272 + d0; f.Height = 342 + d0; advBtn.Text = open ? "Advanced options  (hide)" : "Advanced options  (show)"; };
                advBtn.Click += (s, e) => setAdv(!SectionUi.AdvOpen);
                info.TextChanged += (s, e) => { if (info.Text.Length > 0 && !SectionUi.AdvOpen) setAdv(true); };
                setAdv(SectionUi.AdvOpen);
                ok.Click += (s, e) =>
                {
                    double v, t, mx; int n;
                    if (!Theme.TryNum(iv.Text, out v) || v <= 0) { info.Text = "Interval must be a number above 0."; return; }
                    if (!int.TryParse(ev.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out n) || n < 1 || n > 100) { info.Text = "Index every N: a whole number from 1 to 100."; return; }
                    if (!Theme.TryNum(th.Text, out t) || t <= 0 || t > 50) { info.Text = "Label text height must be above 0 and at most 50."; return; }
                    if (!Theme.TryNum(me.Text, out mx) || mx < 0) { info.Text = "Longest triangle side: a number, 0 or more."; return; }
                    res.Interval = v; res.Every = n; res.Labels = lab.Checked; res.LabelAll = la.Checked; res.TextH = t; res.RealZ = rz.Checked; res.MaxEdge = mx; res.Mode = m1.Checked ? 1 : m2.Checked ? 2 : 0; res.IndexCol = Commands.GLAci[ci.SelectedIndex]; res.MidCol = Commands.GLAci[cm.SelectedIndex]; good = true; f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel; f.AcceptButton = ok; f.CancelButton = cancel; AcApp.ShowModalDialog(f);
            }
            return good ? res : null;
        }
    }

    public partial class Commands
    {
        [CommandMethod("BPCONTOUR", CommandFlags.UsePickSet)]
        public static void QuickContour()
        {
            Guard("BPCONTOUR", () =>
            {
                var doc = Doc; var ed = doc.Editor; var db = doc.Database; var pre = Sel.Peek(ed);
                var sr = Sel.Get(ed, new PromptSelectionOptions { MessageForAdding = "\nSelect points, 3D faces, 3D polylines, or a Civil 3D surface: " }, "POINT,3DFACE,POLYLINE,AECC_TIN_SURFACE,AECC_GRID_SURFACE", pre);
                if (sr.Status != PromptStatus.OK) return;
                var ids = sr.Value.GetObjectIds(); Grid grid = null; List<Tri> tris = null; double zmin = double.MaxValue, zmax = double.MinValue, ex0 = double.MaxValue, ey0 = double.MaxValue, ex1 = double.MinValue, ey1 = double.MinValue; string found;
                var sid = ids.FirstOrDefault(i => (i.ObjectClass.DxfName ?? "").IndexOf("SURFACE", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!sid.IsNull)
                {
                    string err; double used;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var ext = ((Entity)tr.GetObject(sid, OpenMode.ForRead)).GeometricExtents; double cell = ProfileMath.NiceStep(Math.Max(ext.MaxPoint.X - ext.MinPoint.X, ext.MaxPoint.Y - ext.MinPoint.Y) / 150.0);
                        grid = CivilGrid(tr, sid, cell, ed, out used, out err); tr.Commit();
                    }
                    if (grid == null) { ed.WriteMessage("\n[B+] " + err + "\n"); return; }
                    foreach (var z in grid.Z) if (!double.IsNaN(z)) { zmin = Math.Min(zmin, z); zmax = Math.Max(zmax, z); }
                    ex0 = grid.X0; ey0 = grid.Y0; ex1 = grid.CX(grid.NX - 1); ey1 = grid.CY(grid.NY - 1);
                    found = "Civil 3D surface, read on a " + used.ToString("0.###", CultureInfo.InvariantCulture) + " m grid (not tested on a real surface: check a few levels).  Heights " + zmin.ToString("F2", CultureInfo.InvariantCulture) + " to " + zmax.ToString("F2", CultureInfo.InvariantCulture) + " m.";
                }
                else
                {
                    var pts = new List<double[]>(); tris = new List<Tri>();
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (var id in ids)
                        {
                            var e = tr.GetObject(id, OpenMode.ForRead); var dp = e as DBPoint; var fc = e as Face; var p3 = e as Polyline3d;
                            if (dp != null) pts.Add(new[] { dp.Position.X, dp.Position.Y, dp.Position.Z });
                            else if (fc != null)
                            {
                                var v = new List<Point3d>(); for (short i = 0; i < 4; i++) v.Add(fc.GetVertexAt(i));
                                tris.Add(new Tri(v[0].X, v[0].Y, v[0].Z, v[1].X, v[1].Y, v[1].Z, v[2].X, v[2].Y, v[2].Z));
                                if (v[3].DistanceTo(v[2]) > 1e-9) tris.Add(new Tri(v[0].X, v[0].Y, v[0].Z, v[2].X, v[2].Y, v[2].Z, v[3].X, v[3].Y, v[3].Z));
                            }
                            else if (p3 != null) foreach (ObjectId vid in p3) { var pv = (PolylineVertex3d)tr.GetObject(vid, OpenMode.ForRead); pts.Add(new[] { pv.Position.X, pv.Position.Y, pv.Position.Z }); }
                        }
                        tr.Commit();
                    }
                    if (pts.Count > MaxDelaunayPoints) { ed.WriteMessage("\n[B+] " + pts.Count + " points is too many for the built-in triangulation (limit " + MaxDelaunayPoints + "). Use 3D faces or a smaller selection.\n"); return; }
                    if (pts.Count >= 3) tris.AddRange(Sinks.Delaunay(pts));
                    if (tris.Count == 0) { ed.WriteMessage("\n[B+] Need at least 3 points or some 3D faces.\n"); return; }
                    foreach (var t in tris) { foreach (var z in new[] { t.Z1, t.Z2, t.Z3 }) { zmin = Math.Min(zmin, z); zmax = Math.Max(zmax, z); } foreach (var x in new[] { t.X1, t.X2, t.X3 }) { ex0 = Math.Min(ex0, x); ex1 = Math.Max(ex1, x); } foreach (var y in new[] { t.Y1, t.Y2, t.Y3 }) { ey0 = Math.Min(ey0, y); ey1 = Math.Max(ey1, y); } }
                    found = pts.Count + " point(s) and " + tris.Count + " triangle(s).  Heights " + zmin.ToString("F2", CultureInfo.InvariantCulture) + " to " + zmax.ToString("F2", CultureInfo.InvariantCulture) + " m.";
                }
                if (zmax - zmin < 1e-6) { ed.WriteMessage("\n[B+] All heights are the same: no contours.\n"); return; }
                double diag = Math.Sqrt((ex1 - ex0) * (ex1 - ex0) + (ey1 - ey0) * (ey1 - ey0)), scale = DrawingScale(db);
                double autoTh = scale > 1.0001 ? 0.0025 * scale : ProfileMath.NiceStep(diag / 250.0);
                var ch = ContourUi.Ask(found, Contour.SuggestInterval(zmax - zmin), tris != null, autoTh); if (ch == null) return;
                var levels = Contour.Levels(zmin, zmax, ch.Interval);
                if (levels.Count == 0) { ed.WriteMessage("\n[B+] No contour level falls inside the heights with this interval.\n"); return; }
                var lines = new List<ContourLine>();
                foreach (var l in levels) lines.AddRange(grid != null ? Contour.FromGrid(grid, l) : Contour.FromTris(tris, l, ch.MaxEdge));
                if (lines.Count == 0) { ed.WriteMessage("\n[B+] No contour lines were found (a long triangle limit may have removed them).\n"); return; }
                double total = lines.Sum(x => x.Length);
                if (lines.Count > 2000 && MessageBox.Show("This will draw " + lines.Count + " lines (" + total.ToString("F0", CultureInfo.InvariantCulture) + " m in total). Continue? A bigger interval gives fewer lines.", "B+ CAD Tools", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                int made = 0, labels = 0; double th = ch.TextH;
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    var layI = SurveyCad.EnsureLayer(tr, db, "BP-CONTOUR-INDEX", ch.IndexCol); var layM = SurveyCad.EnsureLayer(tr, db, "BP-CONTOUR", ch.MidCol); var layT = SurveyCad.EnsureLayer(tr, db, "BP-CONTOUR-TEXT", 7);
                    int dec = Contour.Decimals(ch.Interval);
                    foreach (var c in lines)
                    {
                        bool idx = Contour.IsIndex(c.Level, ch.Interval, ch.Every);
                        var pl = new AcPoly(); int k = 0; foreach (var p in c.Pts) pl.AddVertexAt(k++, new Point2d(p[0], p[1]), 0, 0, 0);
                        pl.Closed = c.Closed; if (ch.RealZ) pl.Elevation = c.Level; pl.ConstantWidth = idx && ch.Mode == 0 ? th * 0.15 : 0;
                        if (ch.Mode == 1) { var rgb = Contour.ElevColor((c.Level - zmin) / (zmax - zmin)); pl.LayerId = layM; pl.Color = AcColor.FromRgb((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]); }
                        else if (ch.Mode == 2) { pl.LayerId = layI; pl.Color = AcColor.FromColorIndex(ColorMethod.ByAci, ch.IndexCol); }
                        else { pl.LayerId = idx ? layI : layM; pl.Color = AcColor.FromColorIndex(ColorMethod.ByAci, idx ? ch.IndexCol : ch.MidCol); }
                        ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true); made++;
                        if (ch.Labels && (idx || ch.LabelAll))
                        {
                            var sp = Contour.LabelSpot(c, th * 8); if (sp == null) continue; double a = sp[2] * Math.PI / 180.0;
                            var at = new Point3d(sp[0] - Math.Sin(a) * th * 0.7, sp[1] + Math.Cos(a) * th * 0.7, ch.RealZ ? c.Level : 0);
                            var d = new DBText { TextString = Contour.F(c.Level, dec), Height = th, Rotation = a, HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = TextVerticalMode.TextVerticalMid, LayerId = layT };
                            d.Position = at; d.AlignmentPoint = at; ms.AppendEntity(d); tr.AddNewlyCreatedDBObject(d, true); labels++;
                        }
                    }
                    tr.Commit();
                }
                ed.WriteMessage("\n[B+] Contours: " + made + " lines (" + total.ToString("F0", CultureInfo.InvariantCulture) + " m), " + labels + " labels, interval " + ch.Interval.ToString("0.###", CultureInfo.InvariantCulture) + " m, from " + levels[0].ToString("F2", CultureInfo.InvariantCulture) + " to " + levels[levels.Count - 1].ToString("F2", CultureInfo.InvariantCulture) + " m. Layers BP-CONTOUR-INDEX, BP-CONTOUR, BP-CONTOUR-TEXT.\n");
            });
        }
    }
}
