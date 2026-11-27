using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.PlottingServices;
using Autodesk.AutoCAD.Runtime;
using BPlus.LiveMap.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BPlus.LiveMap
{
    /// <summary>Printers, paper sizes and plot styles the installed AutoCAD offers.</summary>
    sealed class PlotLists
    {
        public List<string> Devices = new List<string>(), Styles = new List<string>();
        public static PlotLists Read()
        {
            var l = new PlotLists(); var psv = PlotSettingsValidator.Current;
            try { foreach (string d in psv.GetPlotDeviceList()) l.Devices.Add(d); } catch { }
            try { foreach (string s in psv.GetPlotStyleSheetList()) l.Styles.Add(s); } catch { }
            return l;
        }
        /// <summary>(canonical name, local name) for each paper size of a device.</summary>
        public List<string[]> Media(string device)
        {
            var r = new List<string[]>();
            try
            {
                var psv = PlotSettingsValidator.Current; using (var ps = new PlotSettings(true))
                {
                    psv.SetPlotConfigurationName(ps, device, null); psv.RefreshLists(ps);
                    foreach (string c in psv.GetCanonicalMediaNameList(ps)) { string loc = c; try { loc = psv.GetLocaleMediaName(ps, c); } catch { } r.Add(new[] { c, loc }); }
                }
            }
            catch { }
            return r;
        }
    }

    /// <summary>Microsoft Print to PDF opens its own Save As window. This watcher brings it to the front and closes it after the time limit so AutoCAD cannot hang for ever.</summary>
    static class MsPdfWatch
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        static volatile bool _run; static System.Threading.Thread _t; static long _t0;
        public static void Reset() { System.Threading.Interlocked.Exchange(ref _t0, DateTime.UtcNow.Ticks); }
        public static void Start(int seconds)
        {
            Stop(); _run = true; uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id; Reset();
            _t = new System.Threading.Thread(() =>
            {
                while (_run)
                {
                    System.Threading.Thread.Sleep(700); double age = (DateTime.UtcNow - new DateTime(System.Threading.Interlocked.Read(ref _t0), DateTimeKind.Utc)).TotalSeconds; bool kill = age > seconds;
                    try
                    {
                        EnumWindows((h, l) =>
                        {
                            uint pid; GetWindowThreadProcessId(h, out pid); if (pid != me || !IsWindowVisible(h)) return true;
                            var cls = new StringBuilder(64); GetClassName(h, cls, 64); if (cls.ToString() != "#32770") return true;
                            if (kill) PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); else SetForegroundWindow(h);
                            return true;
                        }, IntPtr.Zero);
                    }
                    catch { }
                    if (kill) { Log.Write("Microsoft Print to PDF: stopped after " + seconds + " s"); _run = false; }
                }
            }) { IsBackground = true }; _t.Start();
        }
        public static void Stop() { _run = false; }
    }

    public partial class Commands
    {
        const string PdfDevice = "DWG To PDF.pc3";

        static string ScaleText(Layout lo, double unit)
        {
            try
            {
                if (lo.UseStandardScale) return lo.StdScaleType == StdScaleType.ScaleToFit ? "Fit" : lo.StdScaleType.ToString().Replace("Scale", "").Replace("To", ":").Replace("_", ":");
                var cs = lo.CustomPrintScale; if (cs.Numerator > 0 && cs.Denominator > 0 && lo.PlotPaperUnits == PlotPaperUnit.Millimeters) return "1:" + (cs.Denominator * 1000.0 * unit / cs.Numerator).ToString("0.##", CultureInfo.InvariantCulture);
            }
            catch { }
            return "";
        }

        static string SafeCanon(PlotSettings p) { try { return p.CanonicalMediaName; } catch { return null; } }

        static string MediaText(PlotSettings src)
        {
            string c = ""; try { c = src.CanonicalMediaName; } catch { }
            if (string.IsNullOrEmpty(c)) return "(none)";
            // The validator works on a scratch copy, never on the layout opened from the drawing.
            try { using (var ps = new PlotSettings(src.ModelType)) { ps.CopyFrom(src); return PlotSettingsValidator.Current.GetLocaleMediaName(ps, c); } } catch { return c; }
        }

        static PlotInfo BuildInfo(Layout lo, PlotChoice ch, double unit, out string how, out PlotSettings keep)
        {
            var psv = PlotSettingsValidator.Current; var pi = new PlotInfo { Layout = lo.ObjectId }; how = "";
            bool hasDevice = !string.IsNullOrEmpty(lo.PlotConfigurationName) && !lo.PlotConfigurationName.Equals("None", StringComparison.OrdinalIgnoreCase);
            bool custom = !ch.OwnSetup || !hasDevice || string.IsNullOrEmpty(lo.CanonicalMediaName);
            keep = null;
            {
                var ps = new PlotSettings(lo.ModelType); keep = ps;
                ps.CopyFrom(lo);
                if (custom)
                {
                    // Each step is separate: a step the printer refuses is skipped and logged, only device and paper are essential, and an error names the step.
                    Action<string, Action, bool> step = (nm, act, must) => { try { act(); } catch (System.Exception ex) { Log.Write("BPPLOT step '" + nm + "' failed for " + lo.LayoutName + ": " + ex.Message); if (must) throw new System.Exception("step '" + nm + "': " + ex.Message); } };
                    step("printer", () => { psv.SetPlotConfigurationName(ps, ch.Device, null); psv.RefreshLists(ps); }, true);
                    step("paper size", () =>
                    {
                        try { psv.SetCanonicalMediaName(ps, ch.MediaCanonical); }
                        catch
                        {
                            var names = psv.GetCanonicalMediaNameList(ps); string pick = null; foreach (string n in names) if (n.IndexOf("A4", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Extended", StringComparison.OrdinalIgnoreCase) < 0) { pick = n; break; }
                            if (pick == null && names.Count > 0) pick = names[0]; if (pick == null) throw; psv.SetCanonicalMediaName(ps, pick); Log.Write("BPPLOT: paper '" + ch.MediaCanonical + "' not accepted by " + ch.Device + ", used " + pick);
                        }
                    }, true);
                    step("units", () => psv.SetPlotPaperUnits(ps, PlotPaperUnit.Millimeters), false);
                    step("plot area", () => psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Layout), false);
                    // Orientation follows the layout (never forced to portrait). Compare the layout's sheet with the chosen paper.
                    double lw0 = 0, lh0 = 0, pw0 = 0, ph0 = 0; bool sameSheet = false;
                    step("rotation", () =>
                    {
                        bool landLayout = true;
                        try { var a = lo.PlotPaperSize; lw0 = a.X; lh0 = a.Y; bool r0 = lo.PlotRotation == PlotRotation.Degrees000 || lo.PlotRotation == PlotRotation.Degrees180; landLayout = r0 ? a.X >= a.Y : a.Y >= a.X; } catch { }
                        var b = ps.PlotPaperSize; pw0 = b.X; ph0 = b.Y;
                        bool wantLand = ch.Rotation == 0 ? landLayout : !landLayout;
                        psv.SetPlotRotation(ps, (pw0 >= ph0) == wantLand ? PlotRotation.Degrees000 : PlotRotation.Degrees090);
                        sameSheet = lw0 > 0 && Math.Abs(Math.Max(lw0, lh0) - Math.Max(pw0, ph0)) < 1.5 && Math.Abs(Math.Min(lw0, lh0) - Math.Min(pw0, ph0)) < 1.5;
                        Log.Write("BPPLOT " + lo.LayoutName + ": layout sheet " + lw0.ToString("0") + "x" + lh0.ToString("0") + ", chosen paper " + pw0.ToString("0") + "x" + ph0.ToString("0") + ", same sheet " + sameSheet);
                    }, false);
                    double ratio = PlotNaming.ParseScale(ch.Scale);
                    // A paper space layout is already laid out at paper size: "Fit" means 1:1 on the sheet.
                    step("centre", () => psv.SetPlotCentered(ps, false), false);
                    step("origin", () => psv.SetPlotOrigin(ps, new Autodesk.AutoCAD.Geometry.Point2d(0, 0)), false);
                    // Fit = 1:1 when the paper is the layout's own sheet. On a different paper size, 1:1 would crop the drawing, so shrink to fit instead.
                    if (ratio == 0) step("scale", () => { psv.SetUseStandardScale(ps, true); psv.SetStdScaleType(ps, sameSheet ? StdScaleType.StdScale1To1 : StdScaleType.ScaleToFit); if (!sameSheet) { psv.SetPlotCentered(ps, true); } }, false);
                    else step("scale", () => { psv.SetUseStandardScale(ps, false); psv.SetCustomPrintScale(ps, new CustomScale(1.0, PlotNaming.DrawingUnitsPerPaperMm(ratio, unit))); }, true);
                    if (!string.IsNullOrEmpty(ch.Style)) step("plot style", () => psv.SetCurrentStyleSheet(ps, ch.Style), false);
                    step("line weights", () => ps.PrintLineweights = ch.Lineweights, false);
                    how = hasDevice || !ch.OwnSetup ? "my settings" : "my settings (the layout has no printer set)";
                }
                else
                {
                    how = "layout's own page setup";
                    if (ch.Pdf && lo.PlotConfigurationName.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) < 0) { psv.SetPlotConfigurationName(ps, string.IsNullOrEmpty(ch.Device) ? PdfDevice : ch.Device, lo.CanonicalMediaName); psv.RefreshLists(ps); how += " on the PDF driver"; }
                }
                pi.OverrideSettings = ps;
                try { var piv = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled }; piv.Validate(pi); } catch (System.Exception ex) { Log.Write("BPPLOT validate failed for " + lo.LayoutName + ": " + ex.Message + ", retrying with MatchDisabled"); var piv2 = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchDisabled }; try { piv2.Validate(pi); } catch (System.Exception ex2) { throw new System.Exception("validate (" + ch.Device + ", paper " + ch.MediaCanonical + "): " + ex2.Message); } }
            }
            return pi;
        }

        /// <summary>True when the drawing has text with Arabic letters (text, mtext, attributes in any space). Stops at the first hit or after 300,000 objects.</summary>
        static bool HasArabic(Database db)
        {
            try
            {
                int seen = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    foreach (ObjectId id in bt)
                    {
                        var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        if (btr.IsFromExternalReference || btr.IsDependent) continue;
                        foreach (ObjectId eid in btr)
                        {
                            if (++seen > 100000 || ((seen & 1023) == 0 && sw.ElapsedMilliseconds > 2000)) { Log.Write("Arabic scan: stopped early after " + seen + " objects"); return false; }
                            string txt = null; var cls = eid.ObjectClass.DxfName;
                            if (cls == "TEXT" || cls == "MTEXT" || cls == "ATTDEF" || cls == "INSERT" || cls == "ATTRIB")
                            {
                                var o = tr.GetObject(eid, OpenMode.ForRead);
                                if (o is DBText) txt = ((DBText)o).TextString; else if (o is MText) txt = ((MText)o).Contents;
                                else if (o is BlockReference) { foreach (ObjectId a in ((BlockReference)o).AttributeCollection) { var at = tr.GetObject(a, OpenMode.ForRead) as AttributeReference; if (at != null && at.TextString != null && at.TextString.Any(ch => ch >= 0x0600 && ch <= 0x06FF)) return true; } }
                            }
                            if (txt != null && txt.Any(ch => ch >= 0x0600 && ch <= 0x06FF)) return true;
                        }
                    }
                }
            }
            catch (System.Exception ex) { Log.Write("Arabic scan: " + ex.Message); }
            return false;
        }

        static bool EscPressed() { try { return (GetAsyncKeyState(0x1B) & 0x8000) != 0; } catch { return false; } }

        [CommandMethod("BPPLOT")]
        public static void PlotLayouts()
        {
            Guard("BPPLOT", () =>
            {
                var doc = Doc; var ed = doc.Editor; var db = doc.Database; var inv = CultureInfo.InvariantCulture;
                if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting) { ed.WriteMessage(Ar.T("\n[B+] AutoCAD is plotting right now. Wait for it to finish.\n")); return; }
                Log.Write("BPPLOT: start"); double unit = DrawingCs.UnitToMetre(db); string drawing = Path.GetFileNameWithoutExtension(doc.Name);
                var rows = new List<LayoutRow>();
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ld = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead); var list = new List<Layout>();
                    foreach (DBDictionaryEntry en in ld) { var lo = (Layout)tr.GetObject(en.Value, OpenMode.ForRead); if (!lo.ModelType) list.Add(lo); }
                    Log.Write("BPPLOT: reading " + list.Count + " layouts"); foreach (var lo in list.OrderBy(l => l.TabOrder)) rows.Add(new LayoutRow { Name = lo.LayoutName, Setup = string.IsNullOrEmpty(lo.PlotSettingsName) ? "(none)" : lo.PlotSettingsName, Paper = MediaText(lo), Scale = ScaleText(lo, unit), Canon = SafeCanon(lo) });
                    tr.Commit();
                }
                if (rows.Count == 0) { ed.WriteMessage(Ar.T("\n[B+] This drawing has no layouts (only the Model tab).\n")); return; }
                string folder = Path.GetDirectoryName(doc.Name); if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                try
                {
                    if (Convert.ToInt32(AcApp.GetSystemVariable("DBMOD")) != 0)
                    {
                        var r = System.Windows.Forms.MessageBox.Show(Ar.T("This drawing has unsaved changes.\r\n\r\nPlotting talks to the printer drivers, and a driver problem can close AutoCAD without warning.\r\nSave the drawing first?"), Ar.T("B+ CAD Tools - Plot layouts"), System.Windows.Forms.MessageBoxButtons.YesNoCancel);
                        if (r == System.Windows.Forms.DialogResult.Cancel) return;
                        if (r == System.Windows.Forms.DialogResult.Yes) { doc.Database.SaveAs(doc.Name, true, DwgVersion.Current, doc.Database.SecurityParameters); }
                    }
                }
                catch (System.Exception ex) { Log.Write("Plot save check: " + ex.Message); }
                Log.Write("BPPLOT: layouts listed, reading printer lists"); var lists = PlotLists.Read(); Log.Write("BPPLOT: opening dialog"); bool arabic = HasArabic(db); bool oldAcad = true; try { oldAcad = AcApp.Version < new Version(24, 3); } catch { } Log.Write("BPPLOT: arabic text " + arabic + ", old AutoCAD " + oldAcad); var ch = PlotUi.Ask(rows, drawing, folder, lists, arabic, oldAcad); if (ch == null) return; Log.Write("BPPLOT: dialog OK, plotting " + ch.Layouts.Count + " layout(s)");
                if (ch.Pdf && string.IsNullOrEmpty(ch.Device)) ch.Device = PdfDevice;
                bool ms = ch.Pdf && ch.OwnSetup == false && ch.Device != null && ch.Device.IndexOf("microsoft", StringComparison.OrdinalIgnoreCase) >= 0; if (ms) ch.Combined = false;
                if (ms) MsPdfWatch.Start(120);
                if (ch.Pdf) { try { Directory.CreateDirectory(ch.Folder); } catch (System.Exception ex) { ed.WriteMessage(Ar.T("\n[B+] Cannot use that folder: ") + ex.Message + "\n"); return; } }

                string phase = ""; var done = new List<string>(); var failed = new List<string>(); object oldBg = null; string oldLayout = null;
                try { oldBg = AcApp.GetSystemVariable("BACKGROUNDPLOT"); AcApp.SetSystemVariable("BACKGROUNDPLOT", 0); } catch { }
                try { oldLayout = LayoutManager.Current.CurrentLayout; } catch { }
                try
                {
                    var keepers = new List<PlotSettings>();
                    using (doc.LockDocument())
                    {
                        try
                        {
                            var pis = new List<KeyValuePair<string, PlotInfo>>();
                            foreach (var name in ch.Layouts)
                            {
                                try
                                {
                                    LayoutManager.Current.CurrentLayout = name; // switch with nothing open, then read the layout
                                    using (var tr = db.TransactionManager.StartTransaction())
                                    {
                                        var ld = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                                        var lo = (Layout)tr.GetObject(ld.GetAt(name), OpenMode.ForRead); string how; PlotSettings keep;
                                        var pi = BuildInfo(lo, ch.For(name), unit, out how, out keep); keepers.Add(keep); pis.Add(new KeyValuePair<string, PlotInfo>(name, pi));
                                        tr.Commit();
                                    }
                                }
                                catch (System.Exception ex) { failed.Add(name + ": " + ex.Message); }
                            }
                            if (pis.Count > 0)
                            {
                                using (var pe = PlotFactory.CreatePublishEngine())
                                {
                                    pe.BeginPlot(null, null);
                                    if (ch.Pdf && ch.Combined)
                                    {
                                        string path = PlotNaming.Unique(ch.Folder, PlotNaming.FileName("{drawing}", drawing, ""), ".pdf", File.Exists);
                                        pe.BeginDocument(pis[0].Value, doc.Name, null, 1, true, path);
                                        for (int i = 0; i < pis.Count; i++)
                                        {
                                            if (EscPressed()) { failed.Add("Stopped with Esc before " + pis[i].Key); break; }
                                            ed.WriteMessage(Ar.T("\n[B+] Plotting ") + (i + 1) + " of " + pis.Count + ": " + pis[i].Key);
                                            try { LayoutManager.Current.CurrentLayout = pis[i].Key; } catch { }
                                            var pp = new PlotPageInfo(); pe.BeginPage(pp, pis[i].Value, i == pis.Count - 1, null); pe.BeginGenerateGraphics(null); pe.EndGenerateGraphics(null); pe.EndPage(null);
                                        }
                                        pe.EndDocument(null); done.Add(path + "  (" + pis.Count + " pages)");
                                    }
                                    else
                                    {
                                        foreach (var kv in pis)
                                        {
                                            try
                                            {
                                                if (EscPressed()) { failed.Add("Stopped with Esc before " + kv.Key); break; }
                                                ed.WriteMessage(Ar.T("\n[B+] Plotting ") + kv.Key);
                                                try { LayoutManager.Current.CurrentLayout = kv.Key; } catch { }
                                                if (ms) MsPdfWatch.Reset();
                                                string path = ch.Pdf ? PlotNaming.Unique(ch.Folder, PlotNaming.FileName(ch.Pattern, drawing, kv.Key), ".pdf", File.Exists) : "";
                                                phase = "BeginDocument"; pe.BeginDocument(kv.Value, doc.Name, null, 1, ch.Pdf, path);
                                                var pp = new PlotPageInfo(); phase = "BeginPage"; pe.BeginPage(pp, kv.Value, true, null); phase = "BeginGenerateGraphics"; pe.BeginGenerateGraphics(null); phase = "EndGenerateGraphics"; pe.EndGenerateGraphics(null); phase = "EndPage"; pe.EndPage(null); phase = "EndDocument"; pe.EndDocument(null);
                                                done.Add(ch.Pdf ? path : kv.Key + " (printer)");
                                            }
                                            catch (System.Exception ex) { Log.Write("BPPLOT plot step " + phase + " failed for " + kv.Key + ": " + ex.Message); failed.Add(kv.Key + ": at " + phase + " (" + ch.Device + ", " + ch.MediaCanonical + "): " + ex.Message); try { pe.EndDocument(null); } catch { } }
                                        }
                                    }
                                    pe.EndPlot(null);
                                }
                            }
                        }
                        finally { foreach (var k in keepers) { try { k.Dispose(); } catch { } } }
                    }
                }
                catch (System.Exception ex) { failed.Add("Plot stopped: " + ex.Message); }
                finally
                {
                    MsPdfWatch.Stop();
                    try { if (oldLayout != null) LayoutManager.Current.CurrentLayout = oldLayout; } catch { }
                    try { if (oldBg != null) AcApp.SetSystemVariable("BACKGROUNDPLOT", oldBg); } catch { }
                }
                // A file that is missing or 0 bytes is a failure, not a success.
                foreach (var d in done.ToList()) { if (!ch.Pdf) continue; string p = d.Contains("  (") ? d.Substring(0, d.IndexOf("  (")) : d; try { if (!File.Exists(p) || new FileInfo(p).Length == 0) { done.Remove(d); failed.Add(p + ": the file is missing or empty"); } } catch { } }
                var sb = new StringBuilder(); sb.AppendLine(done.Count + " done, " + failed.Count + " failed."); foreach (var d in done) sb.AppendLine("  " + d); foreach (var x in failed) sb.AppendLine("  FAILED " + x);
                ed.WriteMessage("\n[B+] " + sb.ToString().Replace("\r", ""));
                System.Windows.Forms.MessageBox.Show(sb.ToString(), Ar.T("B+ CAD Tools - Plot layouts"));
            });
        }
    }
}
