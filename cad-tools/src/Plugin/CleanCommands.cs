using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

namespace BPlus.LiveMap
{
    /// <summary>What the scan found. Nothing is changed by a scan.</summary>
    sealed class CleanScan
    {
        public int Layers, Blocks, Styles, Others, ZeroLen, Duplicates, ReviewLayers, Objects, LayerCount; public long FileBytes;
        public bool DoLayers = true, DoBlocks = true, DoStyles = true, DoOthers = true, DoZero = true, DoDup = false, DoAudit = true, Backup = true, SaveAfter = true;
    }

    static class CleanUi
    {
        /// <summary>1 = clean, 3 = scan again, 0 = close.</summary>
        public static int Ask(CleanScan s, string drawing)
        {
            using (var f = Theme.Base("B+ CAD Tools - Clean drawing", 700, 640))
            {
                f.AutoScaleDimensions = new SizeF(96f, 96f); f.AutoScaleMode = AutoScaleMode.Dpi; f.FormBorderStyle = FormBorderStyle.Sizable; f.MaximizeBox = false; f.MinimumSize = new Size(560, 420);
                var wa = Screen.FromControl(f).WorkingArea; f.StartPosition = FormStartPosition.Manual;
                var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Bar }; var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bar };
                f.Controls.Add(body); f.Controls.Add(bar); bar.BringToFront(); body.BringToFront(); int action = 0;
                var c1 = Card(body, 14, 70, "THIS DRAWING"); c1.Controls.Add(Theme.Lbl(drawing, 16, 30, 636));
                var info = Theme.Lbl("File size now: " + Mb(s.FileBytes) + "     Objects: " + s.Objects.ToString("N0", CultureInfo.InvariantCulture) + "     Layers: " + s.LayerCount, 16, 48, 636, true); info.Height = 18; c1.Controls.Add(info);
                var c2 = Card(body, 96, 332, "FOUND (nothing is changed yet)");
                var items = new[] { Tuple.Create("Empty layers (no objects)", s.Layers, 0), Tuple.Create("Unused blocks", s.Blocks, 1), Tuple.Create("Unused text styles, linetypes, dimension styles", s.Styles, 2), Tuple.Create("Unused registered apps and other leftovers", s.Others, 3), Tuple.Create("Zero-length lines and empty text", s.ZeroLen, 4), Tuple.Create("Exact duplicate objects (same type, layer and place)", s.Duplicates, 5) };
                var cbs = new List<CheckBox>(); int y = 32;
                bool[] on = { s.DoLayers, s.DoBlocks, s.DoStyles, s.DoOthers, s.DoZero, s.DoDup };
                foreach (var it in items)
                {
                    var cb = new CheckBox { Text = it.Item1, Left = 16, Top = y, Width = 540, Height = 24, Checked = on[it.Item3] && it.Item2 > 0, Enabled = it.Item2 > 0, ForeColor = Theme.Text, BackColor = Color.Transparent };
                    var n = new Label { Text = it.Item2.ToString("N0", CultureInfo.InvariantCulture), Left = 560, Top = y, Width = 90, Height = 24, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Text, BackColor = Color.Transparent };
                    c2.Controls.Add(cb); c2.Controls.Add(n); cbs.Add(cb); y += 30;
                }
                var au = new CheckBox { Text = "Audit: check the drawing and fix errors", Left = 16, Top = y, Width = 540, Height = 24, Checked = s.DoAudit, ForeColor = Theme.Text, BackColor = Color.Transparent }; c2.Controls.Add(au); y += 30;
                var rv = Theme.Lbl("Hidden or frozen layers that hold objects (shown only)", 38, y + 2, 520, true); var rvn = new Label { Text = s.ReviewLayers.ToString("N0", CultureInfo.InvariantCulture), Left = 560, Top = y, Width = 90, Height = 24, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Dim, BackColor = Color.Transparent }; c2.Controls.Add(rv); c2.Controls.Add(rvn); y += 30;
                var nt = Theme.Lbl("Layers that hold objects are never deleted. Layer 0, Defpoints, the current layer and xref layers are skipped. Duplicates are off until you tick them.", 16, y, 636, true); nt.Height = 34; nt.ForeColor = Color.FromArgb(255, 190, 80); c2.Controls.Add(nt);
                var c3 = Card(body, 440, 96, "SAFETY");
                var bk = new CheckBox { Text = "Save a backup first: " + Path.GetFileNameWithoutExtension(drawing) + "-before-cleanup.dwg (same folder)", Left = 16, Top = 30, Width = 636, Height = 24, Checked = s.Backup, ForeColor = Theme.Text, BackColor = Color.Transparent };
                var sv = new CheckBox { Text = "Save after cleaning and show the file size before and after", Left = 16, Top = 56, Width = 636, Height = 24, Checked = s.SaveAfter, ForeColor = Theme.Text, BackColor = Color.Transparent };
                c3.Controls.AddRange(new Control[] { bk, sv });
                var ok = Theme.Btn("Clean", 0, 14, 120, true); var again = Theme.Btn("Scan again", 0, 14, 110); var close = Theme.Btn("Close", 0, 14, 90); ok.Anchor = again.Anchor = close.Anchor = AnchorStyles.Top | AnchorStyles.Right; bar.Controls.AddRange(new Control[] { ok, again, close });
                Action place = () => { close.Left = bar.ClientSize.Width - close.Width - 20; ok.Left = close.Left - ok.Width - 10; again.Left = ok.Left - again.Width - 10; }; bar.Resize += (s2, e) => place();
                Action grab = () => { s.DoLayers = cbs[0].Checked; s.DoBlocks = cbs[1].Checked; s.DoStyles = cbs[2].Checked; s.DoOthers = cbs[3].Checked; s.DoZero = cbs[4].Checked; s.DoDup = cbs[5].Checked; s.DoAudit = au.Checked; s.Backup = bk.Checked; s.SaveAfter = sv.Checked; };
                ok.Click += (s2, e) => { grab(); action = 1; f.DialogResult = DialogResult.OK; }; again.Click += (s2, e) => { grab(); action = 3; f.DialogResult = DialogResult.Retry; }; close.Click += (s2, e) => f.DialogResult = DialogResult.Cancel; f.CancelButton = close;
                Theme.Fit(f, body, bar, wa, 716, c3.Bottom); f.Load += (s3, e3) => place();
                var r = AcApp.ShowModalDialog(f); return r == DialogResult.Cancel ? 0 : action;
            }
        }
        public static string Mb(long b) { return (b / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB"; }
        static Panel Card(Panel body, int y, int h, string title)
        {
            var p = new Panel { Left = 16, Top = y, Width = 668, Height = h, BackColor = Theme.Panel };
            p.Controls.Add(new Label { Text = title, Left = 16, Top = 10, Width = 400, Height = 18, ForeColor = Theme.Dim, BackColor = Color.Transparent, Font = new System.Drawing.Font("Segoe UI", 8.5f, FontStyle.Bold) });
            body.Controls.Add(p); return p;
        }
    }

    public partial class Commands
    {
        static IEnumerable<ObjectId> PurgeCandidates(Database db, Transaction tr, bool layers, bool blocks, bool styles, bool others)
        {
            Func<ObjectId, IEnumerable<ObjectId>> all = tid => ((SymbolTable)tr.GetObject(tid, OpenMode.ForRead)).Cast<ObjectId>();
            if (layers) foreach (var id in all(db.LayerTableId)) yield return id;
            if (blocks)
                foreach (var id in all(db.BlockTableId))
                {
                    var b = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (b.IsLayout || b.IsAnonymous || b.IsFromExternalReference || b.IsFromOverlayReference || b.IsDependent) continue; yield return id;
                }
            if (styles) { foreach (var id in all(db.TextStyleTableId)) yield return id; foreach (var id in all(db.LinetypeTableId)) yield return id; foreach (var id in all(db.DimStyleTableId)) yield return id; }
            if (others) { foreach (var id in all(db.RegAppTableId)) yield return id; foreach (var id in all(db.UcsTableId)) yield return id; foreach (var id in all(db.ViewportTableId)) yield return id; }
        }

        static int CountPurgeable(Database db, Transaction tr, bool layers, bool blocks, bool styles, bool others)
        {
            var ids = new ObjectIdCollection(); foreach (var id in PurgeCandidates(db, tr, layers, blocks, styles, others)) ids.Add(id);
            if (ids.Count == 0) return 0; db.Purge(ids); return ids.Count;
        }

        static string DupKey(Entity e, Transaction tr)
        {
            Func<double, string> r = v => Math.Round(v, 5).ToString("0.#####", CultureInfo.InvariantCulture);
            Func<Autodesk.AutoCAD.Geometry.Point3d, string> p = q => r(q.X) + "," + r(q.Y) + "," + r(q.Z);
            string k = null;
            if (e is Line) { var l = (Line)e; var a = p(l.StartPoint); var b = p(l.EndPoint); k = string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a; }
            else if (e is DBPoint) k = p(((DBPoint)e).Position);
            else if (e is Circle && !(e is Arc)) { var c = (Circle)e; k = p(c.Center) + "|" + r(c.Radius); }
            else if (e is DBText) { var t = (DBText)e; k = p(t.Position) + "|" + t.TextString + "|" + r(t.Height); }
            else if (e is MText) { var t = (MText)e; k = p(t.Location) + "|" + t.Contents; }
            else if (e is BlockReference) { var b = (BlockReference)e; k = b.Name + "|" + p(b.Position) + "|" + r(b.Rotation) + "|" + r(b.ScaleFactors.X) + "," + r(b.ScaleFactors.Y); }
            else if (e is Polyline) { var pl = (Polyline)e; var sb = new StringBuilder(pl.Closed ? "c" : "o"); for (int i = 0; i < pl.NumberOfVertices; i++) { var v = pl.GetPoint2dAt(i); sb.Append("|" + r(v.X) + "," + r(v.Y) + "," + r(pl.GetBulgeAt(i))); } k = sb.ToString(); }
            return k == null ? null : e.GetType().Name + "|" + e.Layer + "|" + k;
        }

        /// <summary>Walks model space and layouts (not block definitions). Counts, or erases when erase is true. Locked layers are left alone.</summary>
        static void WalkObjects(Database db, Transaction tr, bool zero, bool dup, bool erase, out int zeroN, out int dupN, out int objects)
        {
            zeroN = 0; dupN = 0; objects = 0; var seen = new HashSet<string>(); var locked = new HashSet<string>();
            foreach (ObjectId lid in (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead)) { var lr = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead); if (lr.IsLocked) locked.Add(lr.Name); }
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId bid in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(bid, OpenMode.ForRead); if (!btr.IsLayout) continue;
                foreach (ObjectId id in btr)
                {
                    objects++; var e = tr.GetObject(id, OpenMode.ForRead) as Entity; if (e == null || e.IsErased) continue; if (locked.Contains(e.Layer)) continue;
                    bool bad = false;
                    if (zero)
                    {
                        if (e is Line && ((Line)e).Length < 1e-9) bad = true;
                        else if (e is DBText && string.IsNullOrWhiteSpace(((DBText)e).TextString)) bad = true;
                        else if (e is MText && string.IsNullOrWhiteSpace(((MText)e).Text)) bad = true;
                    }
                    if (!bad && dup) { var k = DupKey(e, tr); if (k != null && !seen.Add(btr.Name + "|" + k)) { dupN++; if (erase) { e.UpgradeOpen(); e.Erase(); } continue; } }
                    if (bad) { zeroN++; if (erase) { e.UpgradeOpen(); e.Erase(); } }
                }
            }
        }

        [CommandMethod("BPCLEAN")]
        public static void CleanDrawing()
        {
            Guard("BPCLEAN", () =>
            {
                var doc = Doc; var ed = doc.Editor; var db = doc.Database; var st = new CleanScan();
                if (string.IsNullOrEmpty(doc.Name) || !File.Exists(doc.Name)) { MessageBox.Show("Save the drawing first, then run Clean drawing.", "B+ CAD Tools"); return; }
                while (true)
                {
                    try { AcApp.MainWindow.Focus(); } catch { }
                    using (doc.LockDocument())
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        int z, d, o; WalkObjects(db, tr, true, true, false, out z, out d, out o); st.ZeroLen = z; st.Duplicates = d; st.Objects = o;
                        st.Layers = CountPurgeable(db, tr, true, false, false, false); st.Blocks = CountPurgeable(db, tr, false, true, false, false);
                        st.Styles = CountPurgeable(db, tr, false, false, true, false); st.Others = CountPurgeable(db, tr, false, false, false, true);
                        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead); int n = 0, rv = 0; foreach (ObjectId id in lt) { n++; var lr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead); if (lr.IsOff || lr.IsFrozen) rv++; }
                        st.LayerCount = n; st.ReviewLayers = Math.Max(0, rv - st.Layers); st.FileBytes = new FileInfo(doc.Name).Length;
                    }
                    int act = CleanUi.Ask(st, doc.Name); if (act == 0) return; if (act == 3) continue; break;
                }
                if (Convert.ToInt32(AcApp.GetSystemVariable("DBMOD")) != 0)
                {
                    var r = MessageBox.Show("This drawing has unsaved changes. Save it first so the backup and the size comparison are correct?", "B+ CAD Tools - Clean drawing", MessageBoxButtons.YesNoCancel);
                    if (r == DialogResult.Cancel) return; if (r == DialogResult.Yes) db.SaveAs(doc.Name, true, DwgVersion.Current, db.SecurityParameters);
                }
                long before = new FileInfo(doc.Name).Length; string backup = null;
                if (st.Backup)
                {
                    string dir = Path.GetDirectoryName(doc.Name), nm = Path.GetFileNameWithoutExtension(doc.Name); backup = Path.Combine(dir, nm + "-before-cleanup.dwg"); int k = 2;
                    while (File.Exists(backup)) backup = Path.Combine(dir, nm + "-before-cleanup" + (k++) + ".dwg");
                    try { File.Copy(doc.Name, backup); } catch (Exception ex) { MessageBox.Show("Could not save the backup, nothing was cleaned.\r\n" + ex.Message, "B+ CAD Tools"); return; }
                }
                int rl = 0, rb = 0, rs = 0, ro = 0, rz = 0, rd = 0;
                using (doc.LockDocument())
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        int z, d, o; if (st.DoZero || st.DoDup) { WalkObjects(db, tr, st.DoZero, st.DoDup, true, out z, out d, out o); rz = z; rd = d; }
                        tr.Commit();
                    }
                    // Purge in rounds: removing one thing (a block) can free another (its layer or style).
                    for (int round = 0; round < 6; round++)
                    {
                        int got = 0;
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            Action<bool, bool, bool, bool, Action<int>> go = (l, b, s, ot, add) =>
                            {
                                var ids = new ObjectIdCollection(); foreach (var id in PurgeCandidates(db, tr, l, b, s, ot)) ids.Add(id); if (ids.Count == 0) return; db.Purge(ids);
                                foreach (ObjectId id in ids) { try { var ob = tr.GetObject(id, OpenMode.ForWrite); ob.Erase(); got++; add(1); } catch { } }
                            };
                            if (st.DoBlocks) go(false, true, false, false, v => rb += v);
                            if (st.DoLayers) go(true, false, false, false, v => rl += v);
                            if (st.DoStyles) go(false, false, true, false, v => rs += v);
                            if (st.DoOthers) go(false, false, false, true, v => ro += v);
                            tr.Commit();
                        }
                        if (got == 0) break;
                    }
                }
                if (st.DoAudit) { try { doc.SendStringToExecute("_.AUDIT _Y ", true, false, false); } catch { } }
                string msg = "Removed: " + rl + " layers, " + rb + " blocks, " + rs + " styles and linetypes, " + ro + " other leftovers, " + rz + " zero-length objects, " + rd + " duplicates." + (st.DoAudit ? "\r\nAudit was run." : "");
                if (st.SaveAfter)
                {
                    try { db.SaveAs(doc.Name, true, DwgVersion.Current, db.SecurityParameters); long after = new FileInfo(doc.Name).Length; msg += "\r\nFile size: " + CleanUi.Mb(before) + "  ->  " + CleanUi.Mb(after) + (before > 0 ? "  (" + Math.Round(100.0 * (before - after) / before) + "% smaller)" : ""); }
                    catch (Exception ex) { msg += "\r\nCould not save: " + ex.Message; }
                }
                else msg += "\r\nNot saved. UNDO reverses the cleaning.";
                if (backup != null) msg += "\r\nBackup: " + backup;
                ed.WriteMessage("\n[B+] " + msg.Replace("\r", "") + "\n"); MessageBox.Show(msg, "B+ CAD Tools - Clean drawing");
            });
        }
    }
}
