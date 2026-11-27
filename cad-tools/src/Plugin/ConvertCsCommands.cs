using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using BPlus.LiveMap.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

namespace BPlus.LiveMap
{
    sealed class ConvState
    {
        public bool FileMode; public int From, To; public bool Copy = true; public string InFile = ""; public int ECol = 2, NCol = 3; public ObjectId[] Ids = new ObjectId[0]; public string Summary = "";
        public string Example = "";
    }

    static class ConvertCsUi
    {
        const int ActConvert = 1, ActSelect = 2;
        static string Label(int epsg) { var c = CoordSys.FromEpsg(epsg); return c == null ? "(not set)" : epsg + "  " + c.Name; }

        /// <summary>Returns 1 = convert, 2 = select objects first, 0 = cancel.</summary>
        public static int Ask(ConvState st)
        {
            using (var f = Theme.Base("B+ CAD Tools - Convert coordinates", 700, 700))
            {
                f.AutoScaleDimensions = new SizeF(96f, 96f); f.AutoScaleMode = AutoScaleMode.Dpi; f.FormBorderStyle = FormBorderStyle.Sizable; f.MaximizeBox = false; f.MinimumSize = new Size(560, 420);
                var wa = Screen.FromControl(f).WorkingArea; f.StartPosition = FormStartPosition.Manual;
                var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Bar };
                var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bar };
                f.Controls.Add(body); f.Controls.Add(bar); bar.BringToFront(); body.BringToFront();
                int action = 0;

                // Source of the coordinates
                var c1 = Card(body, 14, 130, "WHAT TO CONVERT");
                var pm = new Panel { Left = 16, Top = 30, Width = 636, Height = 26, BackColor = Color.Transparent }; c1.Controls.Add(pm);
                var rObj = Rb("Drawing objects", 0, 0, 220, !st.FileMode); var rFile = Rb("A coordinate file (CSV / TXT)", 230, 0, 300, st.FileMode); pm.Controls.AddRange(new Control[] { rObj, rFile });
                var selBtn = Theme.Btn("Select objects", 16, 66, 150, true); var selTxt = Theme.Lbl(st.Ids.Length == 0 ? "No objects selected yet" : st.Ids.Length + " objects selected", 180, 70, 460); c1.Controls.AddRange(new Control[] { selBtn, selTxt });
                var sum = Theme.Lbl(st.Summary, 16, 100, 636, true); sum.Height = 18; c1.Controls.Add(sum);
                var fileBtn = Theme.Btn("Choose file...", 16, 66, 150); var fileTxt = Theme.Lbl(st.InFile, 180, 70, 460); c1.Controls.AddRange(new Control[] { fileBtn, fileTxt });
                var colE = new NumericUpDown { Left = 130, Top = 98, Width = 60, Minimum = 1, Maximum = 99, Value = Math.Max(1, Math.Min(99, st.ECol)), BorderStyle = BorderStyle.FixedSingle }; Theme.Style(colE);
                var colN = new NumericUpDown { Left = 330, Top = 98, Width = 60, Minimum = 1, Maximum = 99, Value = Math.Max(1, Math.Min(99, st.NCol)), BorderStyle = BorderStyle.FixedSingle }; Theme.Style(colN);
                var lE = Theme.Lbl("East / Lon column", 16, 100, 112, true); var lN = Theme.Lbl("North / Lat column", 210, 100, 118, true); lE.Height = lN.Height = 18;
                c1.Controls.AddRange(new Control[] { colE, colN, lE, lN });

                // From / To
                var c2 = Card(body, 14 + c1.Height + 12, 176, "CONVERT FROM / TO");
                Small(c2, "From", 16, 32, 200); var from = new Button { Left = 16, Top = 52, Width = 290, Height = 30, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Field, ForeColor = Theme.Text, UseVisualStyleBackColor = false }; from.FlatAppearance.BorderColor = Color.FromArgb(90, 100, 115);
                var swap = Theme.Btn("Swap", 318, 52, 64); swap.Height = 30;
                Small(c2, "To", 394, 32, 200); var to = new Button { Left = 394, Top = 52, Width = 258, Height = 30, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Field, ForeColor = Theme.Text, UseVisualStyleBackColor = false }; to.FlatAppearance.BorderColor = Color.FromArgb(90, 100, 115);
                var ex = Small(c2, "", 16, 94, 636); ex.ForeColor = Theme.Text; var warn = Small(c2, "", 16, 116, 636); warn.ForeColor = Color.FromArgb(255, 190, 80);
                var hint = Small(c2, "Click a box to pick the coordinate system (search by name, EPSG or country).", 16, 142, 636);
                c2.Controls.AddRange(new Control[] { from, swap, to });
                Action refresh = () =>
                {
                    from.Text = Label(st.From); to.Text = Label(st.To); var a = CoordSys.FromEpsg(st.From); var b = CoordSys.FromEpsg(st.To);
                    ex.Text = st.Example; bool ap = CoordConvert.IsApprox(a) || CoordConvert.IsApprox(b);
                    warn.Text = ap ? "Ain el Abd / Aramco shift to WGS 84 is only about 18 m accurate (EPSG). Do not use for survey-grade work." : "";
                };
                Action<bool> pick = isFrom => { int e = CsPicker.Ask(); if (e > 0) { if (isFrom) st.From = e; else st.To = e; st.Example = Example(st); refresh(); } };
                from.Click += (s, e) => pick(true); to.Click += (s, e) => pick(false); swap.Click += (s, e) => { int t = st.From; st.From = st.To; st.To = t; st.Example = Example(st); refresh(); };

                // Result
                var c3 = Card(body, c2.Top + c2.Height + 12, 100, "RESULT");
                var pr = new Panel { Left = 16, Top = 30, Width = 636, Height = 54, BackColor = Color.Transparent }; c3.Controls.Add(pr);
                var rCopy = Rb("Make converted copies (originals stay)", 0, 0, 400, st.Copy); var rMove = Rb("Move the objects (originals replaced)", 0, 26, 400, !st.Copy); pr.Controls.AddRange(new Control[] { rCopy, rMove });
                var fnote = Theme.Lbl("The file is saved next to the original as  name-converted.csv. The original file is not changed.", 16, 40, 636, true); fnote.Height = 18; c3.Controls.Add(fnote);

                Action modeUpd = () =>
                {
                    bool fm = rFile.Checked; st.FileMode = fm;
                    selBtn.Visible = selTxt.Visible = sum.Visible = !fm; fileBtn.Visible = fileTxt.Visible = lE.Visible = lN.Visible = colE.Visible = colN.Visible = fm; pr.Visible = !fm; fnote.Visible = fm;
                };
                rFile.CheckedChanged += (s, e) => modeUpd(); modeUpd(); refresh();
                selBtn.Click += (s, e) => { action = ActSelect; f.DialogResult = DialogResult.Retry; };
                fileBtn.Click += (s, e) =>
                {
                    using (var d = new OpenFileDialog { Filter = "Coordinate files (*.csv;*.txt;*.xyz;*.pts)|*.csv;*.txt;*.xyz;*.pts|All files|*.*" })
                        if (d.ShowDialog(f) == DialogResult.OK)
                        {
                            st.InFile = d.FileName; fileTxt.Text = d.FileName;
                            try { int ec, nc; CoordConvert.GuessColumns(File.ReadAllText(d.FileName), out ec, out nc); colE.Value = ec; colN.Value = nc; } catch { }
                        }
                };
                var ok = Theme.Btn("Convert", 0, 14, 120, true); var cancel = Theme.Btn("Cancel", 0, 14, 100); ok.Anchor = cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                bar.Controls.AddRange(new Control[] { ok, cancel });
                Action place = () => { cancel.Left = bar.ClientSize.Width - cancel.Width - 20; ok.Left = cancel.Left - ok.Width - 10; };
                bar.Resize += (s, e) => place();
                ok.Click += (s, e) =>
                {
                    st.ECol = (int)colE.Value; st.NCol = (int)colN.Value; st.Copy = rCopy.Checked;
                    if (st.From == st.To) { MessageBox.Show(f, "Choose two different coordinate systems.", "B+ CAD Tools"); return; }
                    if (st.FileMode && (st.InFile.Length == 0 || !File.Exists(st.InFile))) { MessageBox.Show(f, "Choose the coordinate file first.", "B+ CAD Tools"); return; }
                    if (st.FileMode && st.ECol == st.NCol) { MessageBox.Show(f, "East and North must be different columns.", "B+ CAD Tools"); return; }
                    if (!st.FileMode && st.Ids.Length == 0) { MessageBox.Show(f, "Select the objects first.", "B+ CAD Tools"); return; }
                    action = ActConvert; f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel; f.CancelButton = cancel;
                Theme.Fit(f, body, bar, wa, 716, c3.Bottom); f.Load += (s3, e3) => place();
                var r = AcApp.ShowModalDialog(f);
                st.FileMode = rFile.Checked; st.ECol = (int)colE.Value; st.NCol = (int)colN.Value; st.Copy = rCopy.Checked;
                return r == DialogResult.OK || r == DialogResult.Retry ? action : 0;
            }
        }

        static string Example(ConvState st)
        {
            var a = CoordSys.FromEpsg(st.From); var b = CoordSys.FromEpsg(st.To); if (a == null || b == null || double.IsNaN(SampleX) ) return "";
            try { double x2, y2; CoordConvert.Point(a, b, SampleX, SampleY, out x2, out y2); var c = CultureInfo.InvariantCulture; return "Example:  " + SampleX.ToString("F3", c) + ", " + SampleY.ToString("F3", c) + "   becomes   " + x2.ToString(b.IsGeographic ? "F8" : "F3", c) + ", " + y2.ToString(b.IsGeographic ? "F8" : "F3", c); } catch { return ""; }
        }
        public static double SampleX = double.NaN, SampleY = double.NaN;
        public static void SetExample(ConvState st) { st.Example = Example(st); }

        static Panel Card(Panel body, int y, int h, string title)
        {
            var p = new Panel { Left = 16, Top = y, Width = 668, Height = h, BackColor = Theme.Panel };
            p.Controls.Add(new Label { Text = title, Left = 16, Top = 10, Width = 300, Height = 18, ForeColor = Theme.Dim, BackColor = Color.Transparent, Font = new System.Drawing.Font("Segoe UI", 8.5f, FontStyle.Bold) });
            body.Controls.Add(p); return p;
        }
        static Label Small(Panel p, string t, int x, int y, int w) { var l = Theme.Lbl(t, x, y, w, true); l.Height = 18; p.Controls.Add(l); return l; }
        static RadioButton Rb(string t, int x, int y, int w, bool on) { return new RadioButton { Text = t, Left = x, Top = y, Width = w, Height = 24, Checked = on, ForeColor = Theme.Text, BackColor = Color.Transparent, AutoSize = false }; }
    }

    public partial class Commands
    {
        static int ConvertEntity(Entity en, CoordSys a, CoordSys b, Matrix3d dummy)
        {
            Func<Point3d, Point3d> cv = p => { double x, y; CoordConvert.Point(a, b, p.X, p.Y, out x, out y); return new Point3d(x, y, p.Z); };
            if (en is DBPoint) { var o = (DBPoint)en; o.Position = cv(o.Position); return 1; }
            if (en is Line) { var o = (Line)en; o.StartPoint = cv(o.StartPoint); o.EndPoint = cv(o.EndPoint); return 1; }
            if (en is Polyline)
            {
                var o = (Polyline)en; for (int i = 0; i < o.NumberOfVertices; i++) { var p = o.GetPoint2dAt(i); double x, y; CoordConvert.Point(a, b, p.X, p.Y, out x, out y); o.SetPointAt(i, new Point2d(x, y)); }
                return 1;
            }
            if (en is Polyline3d)
            {
                var o = (Polyline3d)en; var tr = o.Database.TransactionManager.TopTransaction;
                foreach (ObjectId vid in o) { var v = (PolylineVertex3d)tr.GetObject(vid, OpenMode.ForWrite); v.Position = cv(v.Position); }
                return 1;
            }
            Point3d? at = null;
            if (en is Circle) at = ((Circle)en).Center; else if (en is DBText) at = ((DBText)en).Position; else if (en is MText) at = ((MText)en).Location; else if (en is BlockReference) at = ((BlockReference)en).Position;
            if (at != null) { var q = cv(at.Value); en.TransformBy(Matrix3d.Displacement(q - at.Value)); return 1; }   // size and rotation are kept
            return 0;
        }

        static string ObjName(Entity en) { return en is DBPoint ? "points" : en is Line ? "lines" : en is Polyline || en is Polyline3d ? "polylines" : en is Circle ? "circles and arcs" : en is DBText || en is MText ? "text" : en is BlockReference ? "blocks" : en is Hatch ? "hatches (skipped)" : en.GetType().Name + " (skipped)"; }

        [CommandMethod("BPCONVCS", CommandFlags.UsePickSet)]
        public static void ConvertCs()
        {
            Guard("BPCONVCS", () =>
            {
                var doc = Doc; var ed = doc.Editor; var db = doc.Database; double unit = DrawingCs.UnitToMetre(db);
                var st = new ConvState(); int cur = DrawingCs.LoadEpsg(db); st.From = cur > 0 ? cur : 32638; st.To = st.From == 4326 ? 32638 : 4326;
                st.Ids = Sel.Peek(ed);
                while (true)
                {
                    if (st.Ids.Length > 0) Describe(doc, st);
                    int act = ConvertCsUi.Ask(st); if (act == 0) return;
                    if (act == 2)
                    {
                        var r = Sel.Get(ed, new PromptSelectionOptions { MessageForAdding = "\nSelect objects to convert" }, null, new ObjectId[0]); if (r.Status == PromptStatus.OK) st.Ids = r.Value.GetObjectIds(); continue;
                    }
                    break;
                }
                var a = CoordSys.FromEpsg(st.From); var b = CoordSys.FromEpsg(st.To); if (a == null || b == null) { ed.WriteMessage("\n[B+] Unknown coordinate system.\n"); return; }
                if (st.FileMode)
                {
                    a.UnitToMetre = 1; b.UnitToMetre = 1; int n, sk; string txt = File.ReadAllText(st.InFile);
                    string outTxt = CoordConvert.ConvertText(txt, a, b, st.ECol, st.NCol, out n, out sk);
                    string outPath = Path.Combine(Path.GetDirectoryName(st.InFile), Path.GetFileNameWithoutExtension(st.InFile) + "-converted" + Path.GetExtension(st.InFile)); int k = 2;
                    while (File.Exists(outPath)) outPath = Path.Combine(Path.GetDirectoryName(st.InFile), Path.GetFileNameWithoutExtension(st.InFile) + "-converted" + (k++) + Path.GetExtension(st.InFile));
                    File.WriteAllText(outPath, outTxt, new System.Text.UTF8Encoding(false));
                    string m = n + " line(s) converted, " + sk + " other line(s) copied unchanged.\r\nSaved: " + outPath + (CoordConvert.IsApprox(a) || CoordConvert.IsApprox(b) ? "\r\nAin el Abd / Aramco: about 18 m accuracy." : "");
                    ed.WriteMessage("\n[B+] " + m.Replace("\r", "") + "\n"); MessageBox.Show(m, "B+ CAD Tools - Convert coordinates"); return;
                }
                a.UnitToMetre = unit; b.UnitToMetre = unit; int done = 0, skipped = 0; var kinds = new Dictionary<string, int>();
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    string layer = "B+ Converted";
                    if (st.Copy) { var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead); if (!lt.Has(layer)) { lt.UpgradeOpen(); var lr = new LayerTableRecord { Name = layer }; lt.Add(lr); tr.AddNewlyCreatedDBObject(lr, true); } }
                    foreach (var id in st.Ids)
                    {
                        var en = tr.GetObject(id, OpenMode.ForRead) as Entity; if (en == null) { skipped++; continue; }
                        string name = ObjName(en); Entity target = en;
                        if (en is Hatch || !(en is DBPoint || en is Line || en is Polyline || en is Polyline3d || en is Circle || en is DBText || en is MText || en is BlockReference)) { skipped++; continue; }
                        if (st.Copy)
                        {
                            var cp = (Entity)en.Clone(); var btr = (BlockTableRecord)tr.GetObject(en.OwnerId, OpenMode.ForWrite); btr.AppendEntity(cp); tr.AddNewlyCreatedDBObject(cp, true); cp.Layer = layer; target = cp;
                        }
                        else en.UpgradeOpen();
                        int c = ConvertEntity(target, a, b, Matrix3d.Identity); if (c > 0) { done++; int v; kinds.TryGetValue(name, out v); kinds[name] = v + 1; } else skipped++;
                    }
                    tr.Commit();
                }
                string detail = string.Join(", ", kinds.Select(kv => kv.Value + " " + kv.Key));
                ed.WriteMessage("\n[B+] Converted " + done + " object(s) (" + detail + "), skipped " + skipped + ". " + (st.Copy ? "Copies are on layer B+ Converted. " : "") + "UNDO reverses it." + (CoordConvert.IsApprox(a) || CoordConvert.IsApprox(b) ? " Ain el Abd / Aramco: about 18 m accuracy." : "") + "\n");
            });
        }

        static bool Movable(Entity en) { return !(en is Hatch) && (en is DBPoint || en is Line || en is Polyline || en is Polyline3d || en is Circle || en is DBText || en is MText || en is BlockReference); }

        /// <summary>Counts model space objects this tool can move and the others by type name (they would stay where they are).</summary>
        public static void ScanModel(Document doc, out int movable, out Dictionary<string, int> others)
        {
            movable = 0; others = new Dictionary<string, int>(); var db = doc.Database;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms) { var en = tr.GetObject(id, OpenMode.ForRead) as Entity; if (en == null) continue; if (Movable(en)) movable++; else { string k = id.ObjectClass.DxfName; int v; others.TryGetValue(k, out v); others[k] = v + 1; } }
            }
        }

        /// <summary>Moves every movable model space object from system a to system b, in place (one transaction, one UNDO). Returns the number moved.</summary>
        public static int MoveAllModel(Document doc, CoordSys a, CoordSys b)
        {
            var db = doc.Database; double unit = DrawingCs.UnitToMetre(db); a.UnitToMetre = unit; b.UnitToMetre = unit; int done = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var en = tr.GetObject(id, OpenMode.ForRead) as Entity; if (en == null || !Movable(en)) continue;
                    en.UpgradeOpen(); if (ConvertEntity(en, a, b, Matrix3d.Identity) > 0) done++;
                }
                tr.Commit();
            }
            return done;
        }

        static void Describe(Document doc, ConvState st)
        {
            var kinds = new SortedDictionary<string, int>(); double sx = double.NaN, sy = double.NaN;
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
                foreach (var id in st.Ids)
                {
                    var en = tr.GetObject(id, OpenMode.ForRead) as Entity; if (en == null) continue; int v; string nm = ObjName(en); kinds.TryGetValue(nm, out v); kinds[nm] = v + 1;
                    if (double.IsNaN(sx)) { try { Point3d? p = null; if (en is DBPoint) p = ((DBPoint)en).Position; else if (en is Line) p = ((Line)en).StartPoint; else if (en is Polyline) p = ((Polyline)en).GetPoint3dAt(0); else if (en is Circle) p = ((Circle)en).Center; else if (en is DBText) p = ((DBText)en).Position; else if (en is BlockReference) p = ((BlockReference)en).Position; if (p != null) { sx = p.Value.X; sy = p.Value.Y; } } catch { } }
                }
            st.Summary = string.Join(", ", kinds.Select(kv => kv.Value + " " + kv.Key)) + ". Z values are kept.";
            ConvertCsUi.SampleX = sx; ConvertCsUi.SampleY = sy; ConvertCsUi.SetExample(st);
        }
    }
}
