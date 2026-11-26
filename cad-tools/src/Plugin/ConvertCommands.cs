using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Exception = System.Exception;
using BPlus.LiveMap.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BPlus.LiveMap
{
    /// <summary>Plain AutoCAD points and Civil 3D COGO points, both ways. The Civil 3D side is called by reflection.</summary>
    static class CivilBridge
    {
        public static Assembly Find(string name) { return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name); }
        public static bool Present { get { return Find("AeccDbMgd") != null; } }

        static object Doc(Database db)
        {
            var t = Find("AeccDbMgd").GetType("Autodesk.Civil.ApplicationServices.CivilDocument");
            return t.GetMethod("GetCivilDocument", new[] { typeof(Database) }).Invoke(null, new object[] { db });
        }
        static object Cogo(Database db) { var d = Doc(db); return d.GetType().GetProperty("CogoPoints").GetValue(d, null); }

        public static HashSet<int> UsedNumbers(Database db, Transaction tr)
        {
            var used = new HashSet<int>();
            foreach (var id in (IEnumerable)Cogo(db))
            {
                var o = tr.GetObject((ObjectId)id, OpenMode.ForRead); var p = o.GetType().GetProperty("PointNumber");
                if (p != null) { try { used.Add(Convert.ToInt32(p.GetValue(o, null), CultureInfo.InvariantCulture)); } catch { } }
            }
            return used;
        }

        /// <summary>Creates a COGO point with the given number. Returns an error text or null.</summary>
        public static string Add(Database db, Transaction tr, ConvItem it)
        {
            try
            {
                var col = Cogo(db); var loc = new Point3d(it.Src.E, it.Src.N, it.Src.Z); ObjectId id = ObjectId.Null;
                var add = col.GetType().GetMethod("Add", new[] { typeof(Point3d), typeof(bool) });
                if (add == null) return "This Civil 3D version has no CogoPoints.Add(point, bool).";
                id = (ObjectId)add.Invoke(col, new object[] { loc, false });
                var o = tr.GetObject(id, OpenMode.ForWrite); var pn = o.GetType().GetProperty("PointNumber");
                if (pn != null && pn.CanWrite) pn.SetValue(o, Convert.ChangeType(it.Number, pn.PropertyType, CultureInfo.InvariantCulture), null);
                else
                {
                    var sm = col.GetType().GetMethod("SetPointNumber", new[] { typeof(ObjectId), typeof(uint) });
                    if (sm == null) return "Cannot set the point number in this Civil 3D version.";
                    sm.Invoke(col, new object[] { id, (uint)it.Number });
                }
                var rd = o.GetType().GetProperty("RawDescription"); if (rd != null && rd.CanWrite && !string.IsNullOrEmpty(it.Src.Desc)) rd.SetValue(o, it.Src.Desc, null);
                return null;
            }
            catch (TargetInvocationException e) { return (e.InnerException ?? e).Message; }
            catch (Exception e) { return e.Message; }
        }
    }

    public partial class Commands
    {
        sealed class ConvChoice { public bool ToCogo = true, Keep = true, GiveFree; public int Start = 1001; }

        static RadioButton ConvRb(string t, int y, bool on) { return new RadioButton { Text = t, Left = 20, Top = y, Width = 500, Height = 22, Checked = on, ForeColor = Theme.Text, BackColor = Color.Transparent }; }

        static ConvChoice ConvAsk()
        {
            using (var f = Theme.Base("B+ CAD Tools - Convert points", 560, 400))
            {
                f.Controls.Add(Theme.Lbl("Convert", 16, 12, 300));
                var pd = new Panel { Left = 0, Top = 36, Width = 560, Height = 54, BackColor = Color.Transparent };
                var a = ConvRb("AutoCAD points  \u2192  Civil 3D COGO points", 0, true); var b = ConvRb("Civil 3D COGO points  \u2192  AutoCAD points", 26, false); pd.Controls.AddRange(new Control[] { a, b }); f.Controls.Add(pd);
                f.Controls.Add(Theme.Lbl("Points that have no number", 16, 106, 400));
                var pn = new Panel { Left = 0, Top = 130, Width = 560, Height = 54, BackColor = Color.Transparent };
                var skip = ConvRb("Skip them", 0, true); var give = ConvRb("Give them the next free number, starting at", 26, false); give.Width = 330;
                var st = new TextBox { Left = 356, Top = 26, Width = 70, Text = "1001", BorderStyle = BorderStyle.FixedSingle }; Theme.Style(st);
                pn.Controls.AddRange(new Control[] { skip, give, st }); f.Controls.Add(pn);
                f.Controls.Add(Theme.Lbl("A number that is already used is never changed: that point is skipped and listed.", 20, 190, 520, true));
                var keep = new CheckBox { Text = "Keep the original points", Left = 20, Top = 222, Width = 400, Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
                f.Controls.Add(keep);
                f.Controls.Add(Theme.Lbl("Next you select the points. You see a summary before anything changes, and one UNDO reverses it.", 20, 252, 520, true));
                f.Controls.Add(Theme.Lbl("Civil 3D points need Civil 3D to be running.", 20, 276, 520, true));
                var ok = Theme.Btn("Select points", 300, 320, 130, true); var cancel = Theme.Btn("Cancel", 440, 320, 90); ConvChoice res = null;
                ok.Click += (s, e) => { int n; if (give.Checked && (!int.TryParse(st.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out n) || n < 1)) { MessageBox.Show(f, "The start number must be a whole number, 1 or more.", "B+ CAD Tools"); return; } res = new ConvChoice { ToCogo = a.Checked, Keep = keep.Checked, GiveFree = give.Checked, Start = give.Checked ? int.Parse(st.Text.Trim(), CultureInfo.InvariantCulture) : 1001 }; f.DialogResult = DialogResult.OK; };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel; f.Controls.AddRange(new Control[] { ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                return AcApp.ShowModalDialog(f) == DialogResult.OK ? res : null;
            }
        }

        [CommandMethod("BPCONVPTS")]
        public static void ConvertPoints()
        {
            Guard("BPCONVPTS", () =>
            {
                var doc = Doc; var ed = doc.Editor; var db = doc.Database;
                if (!CivilBridge.Present)
                {
                    MessageBox.Show("Convert points needs Civil 3D.\r\n\r\nThis is plain AutoCAD, so nothing was changed.\r\nOpen the drawing in Civil 3D and run this again.", "B+ CAD Tools - Convert points");
                    ed.WriteMessage("\n[B+] Convert points needs Civil 3D. Nothing changed.\n"); return;
                }
                var ch = ConvAsk(); if (ch == null) return;
                var po = new PromptSelectionOptions { MessageForAdding = ch.ToCogo ? "\nSelect the AutoCAD points to convert: " : "\nSelect the COGO points to convert: " };
                var sr = Sel.Get(ed, po, ch.ToCogo ? "POINT" : "AECC_COGO_POINT"); if (sr.Status != PromptStatus.OK) return;
                var ids = new HashSet<ObjectId>(sr.Value.GetObjectIds()); var pts = SurveyCad.Collect(doc, ids).Where(p => p.IsCogo != ch.ToCogo).ToList();
                if (pts.Count == 0) { ed.WriteMessage("\n[B+] No " + (ch.ToCogo ? "AutoCAD" : "COGO") + " points in the selection.\n"); return; }
                int done = 0; var skipped = new List<string>(); var errors = new List<string>();
                using (doc.LockDocument())
                {
                    var mark = db.TransactionManager.StartTransaction();
                    using (mark)
                    {
                        var used = ch.ToCogo ? CivilBridge.UsedNumbers(db, mark) : new HashSet<int>();
                        List<ConvItem> plan;
                        if (ch.ToCogo) plan = PointConvert.Plan(pts.Select(p => p.P).ToList(), used, ch.GiveFree, ch.Start);
                        else plan = pts.Select(p => new ConvItem { Src = p.P, Status = "ok" }).ToList();
                        int ok = plan.Count(i => i.Go), used2 = plan.Count(i => i.Status == "used"), nonum = plan.Count(i => i.Status == "nonumber"), fresh = plan.Count(i => i.Status == "numbered");
                        var sb = new StringBuilder();
                        sb.AppendLine("Selected: " + plan.Count + " point(s)"); sb.AppendLine("Will convert: " + ok + (fresh > 0 ? "  (" + fresh + " get a new number)" : ""));
                        if (used2 > 0) sb.AppendLine("Skipped, number already used: " + used2); if (nonum > 0) sb.AppendLine("Skipped, no number: " + nonum);
                        sb.AppendLine(ch.Keep ? "Originals stay in the drawing." : "Originals are deleted after conversion (UNDO brings them back).");
                        foreach (var i in plan.Where(i => !i.Go).Take(8)) sb.AppendLine("   skipped: " + (string.IsNullOrEmpty(i.Src.No) ? "(no number)" : i.Src.No) + "  E " + i.Src.E.ToString("0.###", CultureInfo.InvariantCulture) + "  N " + i.Src.N.ToString("0.###", CultureInfo.InvariantCulture));
                        if (ok == 0) { MessageBox.Show(sb.ToString() + "\r\nNothing to convert.", "B+ CAD Tools - Convert points"); return; }
                        if (MessageBox.Show(sb.ToString().Replace("\r", "").Replace("\n", "\r\n") + "\r\nConvert now?", "B+ CAD Tools - Convert points", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                        var byId = new Dictionary<SurveyPoint, ObjectId>(); foreach (var p in pts) byId[p.P] = p.Id;
                        Action<string> doIt = null;
                        if (ch.ToCogo)
                        {
                            foreach (var it in plan)
                            {
                                if (!it.Go) { skipped.Add(it.Src.No); continue; }
                                var err = CivilBridge.Add(db, mark, it);
                                if (err != null) { errors.Add(it.Number + ": " + err); continue; }
                                done++;
                                if (!ch.Keep) { var o = mark.GetObject(byId[it.Src], OpenMode.ForWrite); o.Erase(); }
                            }
                        }
                        else
                        {
                            var rules = SurveyCad.LoadRules(); var list = plan.Where(i => i.Go).Select(i => i.Src).ToList();
                            done = SurveyCad.Import(doc, list, rules, false, 1, false);
                            if (!ch.Keep) foreach (var s in list) { var o = mark.GetObject(byId[s], OpenMode.ForWrite); o.Erase(); }
                        }
                        mark.Commit();
                    }
                }
                ed.WriteMessage("\n[B+] Converted " + done + " point(s)" + (skipped.Count > 0 ? ", skipped " + skipped.Count : "") + (errors.Count > 0 ? ", " + errors.Count + " failed" : "") + ".\n");
                foreach (var e in errors.Take(5)) ed.WriteMessage("[B+]   failed " + e + "\n");
                if (errors.Count > 0) MessageBox.Show("Some points did not convert:\r\n" + string.Join("\r\n", errors.Take(8)), "B+ CAD Tools - Convert points");
            });
        }
    }
}
