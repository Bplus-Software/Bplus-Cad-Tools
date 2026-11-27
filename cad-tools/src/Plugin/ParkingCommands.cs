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
using ColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BPlus.LiveMap
{
    static class ParkingUi
    {
        public static List<ParkPreset> Presets() { return ParkingMath.Unpack(Config.ParkingPresets); }

        /// <summary>Editable sizes in metres: width, length and aisle for each of the three types.</summary>
        public static void EditPresets()
        {
            var list = Presets();
            using (var f = Theme.Base("B+ CAD Tools - Parking presets", 520, 330))
            {
                f.Controls.Add(Theme.Lbl("Sizes in metres. Defaults: Ministry of Municipalities and Housing (parallel 2.5 x 6.5, 90 and 45 degrees 2.8 x 5.5). Aisle values are not from a source: set what your project needs.", 16, 12, 488, true));
                f.Controls[0].Height = 50;
                string[] h = { "Type", "Width (m)", "Length (m)", "Aisle (m)" }; int[] xs = { 16, 170, 270, 370 };
                for (int i = 0; i < 4; i++) f.Controls.Add(Theme.Lbl(h[i], xs[i], 74, 100, true));
                var tb = new TextBox[3, 3];
                for (int r = 0; r < 3; r++)
                {
                    f.Controls.Add(Theme.Lbl(list[r].Name, 16, 104 + r * 40, 150));
                    var v = new[] { list[r].Width, list[r].Length, list[r].Aisle };
                    for (int c = 0; c < 3; c++) { tb[r, c] = new TextBox { Left = xs[c + 1], Top = 100 + r * 40, Width = 80, Text = v[c].ToString("0.###", CultureInfo.InvariantCulture), BorderStyle = BorderStyle.FixedSingle }; Theme.Style(tb[r, c]); f.Controls.Add(tb[r, c]); }
                }
                var info = new Label { Left = 16, Top = 230, Width = 488, Height = 20, ForeColor = Color.FromArgb(255, 150, 120), BackColor = Color.Transparent }; f.Controls.Add(info);
                var reset = Theme.Btn("Back to defaults", 16, 266, 140); var ok = Theme.Btn("Save", 324, 266, 85, true); var cancel = Theme.Btn("Cancel", 417, 266, 85);
                reset.Click += (s, e) => { var d = ParkingMath.Defaults(); for (int r = 0; r < 3; r++) { tb[r, 0].Text = d[r].Width.ToString("0.###", CultureInfo.InvariantCulture); tb[r, 1].Text = d[r].Length.ToString("0.###", CultureInfo.InvariantCulture); tb[r, 2].Text = d[r].Aisle.ToString("0.###", CultureInfo.InvariantCulture); } };
                ok.Click += (s, e) =>
                {
                    var res = ParkingMath.Defaults();
                    for (int r = 0; r < 3; r++)
                    {
                        double w, l, a;
                        if (!Theme.TryNum(tb[r, 0].Text, out w) || w <= 0 || w > 20 || !Theme.TryNum(tb[r, 1].Text, out l) || l <= 0 || l > 30 || !Theme.TryNum(tb[r, 2].Text, out a) || a < 0 || a > 30) { info.Text = "Row " + res[r].Name + ": width and length must be above 0, aisle 0 or more (numbers only)."; return; }
                        res[r].Width = w; res[r].Length = l; res[r].Aisle = a;
                    }
                    Config.ParkingPresets = ParkingMath.Pack(res); f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { reset, ok, cancel }); f.CancelButton = cancel; AcApp.ShowModalDialog(f);
            }
        }

        public sealed class Choice { public int Type; public bool Left, Fill = true, Numbers, AisleLine; public int Count = 1; }

        public static bool Ask(double length, List<ParkPreset> pr, out Choice ch)
        {
            var res = new Choice(); bool good = false;
            using (var f = Theme.Base("B+ CAD Tools - Parking", 520, 440))
            {
                f.Controls.Add(Theme.Lbl("Line length " + length.ToString("F2", CultureInfo.InvariantCulture) + " m", 16, 12, 480, true));
                var rb = new RadioButton[3];
                for (int i = 0; i < 3; i++)
                {
                    var p = pr[i]; rb[i] = new RadioButton { Left = 16, Top = 38 + i * 28, Width = 480, Height = 24, ForeColor = Theme.Text, BackColor = Color.Transparent, Checked = i == 1,
                        Text = p.Name + ":  " + p.Width.ToString("0.##", CultureInfo.InvariantCulture) + " x " + p.Length.ToString("0.##", CultureInfo.InvariantCulture) + " m,  " + p.Pitch.ToString("0.##", CultureInfo.InvariantCulture) + " m along the line each" }; f.Controls.Add(rb[i]);
                }
                f.Controls.Add(Theme.Lbl("Side (looking along the line)", 16, 132, 220, true));
                var sl = new RadioButton { Left = 16, Top = 156, Width = 100, Text = "Left", Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent }; var sr = new RadioButton { Left = 120, Top = 156, Width = 100, Text = "Right", ForeColor = Theme.Text, BackColor = Color.Transparent };
                f.Controls.Add(sl); f.Controls.Add(sr);
                var fill = new RadioButton { Left = 16, Top = 192, Width = 300, Text = "Fill the whole line", Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                var cnt = new RadioButton { Left = 16, Top = 218, Width = 140, Text = "Number of stalls", ForeColor = Theme.Text, BackColor = Color.Transparent };
                var n = new TextBox { Left = 160, Top = 216, Width = 60, Text = "5", BorderStyle = BorderStyle.FixedSingle, Enabled = false }; Theme.Style(n);
                cnt.CheckedChanged += (s, e) => n.Enabled = cnt.Checked;
                var num = new CheckBox { Left = 16, Top = 252, Width = 480, Text = "Number the stalls (1, 2, 3 ...)", ForeColor = Theme.Text, BackColor = Color.Transparent };
                var ais = new CheckBox { Left = 16, Top = 278, Width = 480, Text = "Draw the aisle edge line (using the aisle width from the presets)", ForeColor = Theme.Text, BackColor = Color.Transparent };
                f.Controls.AddRange(new Control[] { fill, cnt, n, num, ais });
                var info = new Label { Left = 16, Top = 312, Width = 488, Height = 36, ForeColor = Color.FromArgb(255, 150, 120), BackColor = Color.Transparent }; f.Controls.Add(info);
                var edit = Theme.Btn("Edit sizes...", 16, 376, 130); var ok = Theme.Btn("Draw", 316, 376, 90, true); var cancel = Theme.Btn("Cancel", 414, 376, 90);
                edit.Click += (s, e) => { EditPresets(); var np = Presets(); for (int i = 0; i < 3; i++) { pr[i] = np[i]; rb[i].Text = np[i].Name + ":  " + np[i].Width.ToString("0.##", CultureInfo.InvariantCulture) + " x " + np[i].Length.ToString("0.##", CultureInfo.InvariantCulture) + " m,  " + np[i].Pitch.ToString("0.##", CultureInfo.InvariantCulture) + " m along the line each"; } };
                ok.Click += (s, e) =>
                {
                    int c = 1; if (cnt.Checked && (!int.TryParse(n.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out c) || c < 1 || c > 500)) { info.Text = "Number of stalls: a whole number from 1 to 500."; return; }
                    res.Type = rb[0].Checked ? 0 : rb[1].Checked ? 1 : 2; res.Left = sl.Checked; res.Fill = fill.Checked; res.Count = c; res.Numbers = num.Checked; res.AisleLine = ais.Checked; good = true; f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { edit, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel; AcApp.ShowModalDialog(f);
            }
            ch = res; return good;
        }
    }

    public partial class Commands
    {
        [CommandMethod("BPPARKING")]
        public static void Parking()
        {
            Guard("BPPARKING", () =>
            {
                var doc = Doc; var ed = doc.Editor; var db = doc.Database;
                var eo = new PromptEntityOptions("\nSelect the line the stalls start from (curb or edge: polyline, line or arc). The stalls follow its direction: "); eo.SetRejectMessage("\nOnly a polyline, line or arc."); eo.AddAllowedClass(typeof(Curve), false);
                var er = ed.GetEntity(eo); if (er.Status != PromptStatus.OK) return;
                double length; using (var tr = db.TransactionManager.StartTransaction()) { var cv = (Curve)tr.GetObject(er.ObjectId, OpenMode.ForRead); length = cv.GetDistanceAtParameter(cv.EndParam); tr.Commit(); }
                var pr = ParkingUi.Presets(); ParkingUi.Choice ch; if (!ParkingUi.Ask(length, pr, out ch)) return;
                var p = pr[ch.Type]; int fit = ParkingMath.Fit(length, p), n = ch.Fill ? fit : Math.Min(ch.Count, fit);
                if (n < 1) { ed.WriteMessage("\n[B+] The line (" + length.ToString("F2", CultureInfo.InvariantCulture) + " m) is shorter than one stall (" + p.Pitch.ToString("F2", CultureInfo.InvariantCulture) + " m).\n"); return; }
                int made = 0; double th = Math.Min(Config.TextHeight, Math.Max(0.3, p.Width * 0.5));
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var cv = (Curve)tr.GetObject(er.ObjectId, OpenMode.ForRead);
                    var lay = SurveyCad.EnsureLayer(tr, db, "B+_PARKING", 3);
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    Action<Entity> add = en => { en.LayerId = lay; en.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256); ms.AppendEntity(en); tr.AddNewlyCreatedDBObject(en, true); made++; };
                    double s0 = cv.GetDistanceAtParameter(cv.StartParam);
                    for (int i = 0; i < n; i++)
                    {
                        Point3d a = cv.GetPointAtDist(s0 + i * p.Pitch), b = cv.GetPointAtDist(s0 + (i + 1) * p.Pitch);
                        double tx = b.X - a.X, ty = b.Y - a.Y, l = Math.Sqrt(tx * tx + ty * ty); if (l < 1e-9) continue; tx /= l; ty /= l;
                        double nx = ch.Left ? -ty : ty, ny = ch.Left ? tx : -tx;
                        var c = ParkingMath.Corners(a.X, a.Y, b.X, b.Y, nx, ny, p);
                        var pl = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                        for (int k = 0; k < 4; k++) pl.AddVertexAt(k, new Point2d(c[k * 2], c[k * 2 + 1]), 0, 0, 0);
                        pl.Closed = true; add(pl);
                        if (ch.Numbers)
                        {
                            double cx = (c[0] + c[2] + c[4] + c[6]) / 4, cy = (c[1] + c[3] + c[5] + c[7]) / 4;
                            var t = new DBText { TextString = (i + 1).ToString(CultureInfo.InvariantCulture), Height = th, HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = TextVerticalMode.TextVerticalMid, Position = new Point3d(cx, cy, 0), AlignmentPoint = new Point3d(cx, cy, 0) };
                            add(t);
                        }
                    }
                    if (ch.AisleLine)
                    {
                        try
                        {
                            double off = (ch.Left ? 1 : -1) * (p.Depth + p.Aisle);
                            foreach (DBObject o in cv.GetOffsetCurves(off)) { var en = o as Entity; if (en != null) add(en); }
                        }
                        catch (System.Exception ex) { ed.WriteMessage("\n[B+] The aisle edge line could not be drawn: " + ex.Message + "\n"); }
                    }
                    tr.Commit();
                }
                ed.WriteMessage("\n[B+] " + n + " " + p.Name + " stall(s) drawn on layer B+_PARKING, " + p.Width.ToString("0.##", CultureInfo.InvariantCulture) + " x " + p.Length.ToString("0.##", CultureInfo.InvariantCulture) + " m, " + (p.Pitch * n).ToString("F2", CultureInfo.InvariantCulture) + " m of the " + length.ToString("F2", CultureInfo.InvariantCulture) + " m line used" + (ch.Fill ? "; " + (length - p.Pitch * n).ToString("F2", CultureInfo.InvariantCulture) + " m left over (less than one stall)" : "") + (ch.Count > fit && !ch.Fill ? "; only " + fit + " fit, you asked for " + ch.Count : "") + ". On a curved line each stall follows the chord between its two curb points.\n");
            });
        }
    }
}
