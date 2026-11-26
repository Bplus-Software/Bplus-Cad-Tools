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
        static volatile bool _run; static System.Threading.Thread _t;
        public static void Start(int seconds)
        {
            Stop(); _run = true; uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id; var t0 = DateTime.UtcNow;
            _t = new System.Threading.Thread(() =>
            {
                while (_run)
                {
                    System.Threading.Thread.Sleep(700); double age = (DateTime.UtcNow - t0).TotalSeconds; bool kill = age > seconds;
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
                    psv.SetPlotConfigurationName(ps, ch.Device, ch.MediaCanonical); psv.RefreshLists(ps);
                    psv.SetPlotPaperUnits(ps, PlotPaperUnit.Millimeters); psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Layout);
                    psv.SetPlotRotation(ps, ch.Rotation == 0 ? PlotRotation.Degrees000 : PlotRotation.Degrees090);
                    double ratio = PlotNaming.ParseScale(ch.Scale);
                    // A paper space layout is already laid out at paper size: "Fit" means 1:1 on the sheet. (Scale-to-fit on a Layout plot shrinks or stretches the sheet and cut the page edges.)
                    psv.SetPlotCentered(ps, false); psv.SetPlotOrigin(ps, new Autodesk.AutoCAD.Geometry.Point2d(0, 0));
                    if (ratio == 0) { psv.SetUseStandardScale(ps, true); psv.SetStdScaleType(ps, StdScaleType.StdScale1To1); }
                    else { psv.SetUseStandardScale(ps, false); psv.SetCustomPrintScale(ps, new CustomScale(1.0, PlotNaming.DrawingUnitsPerPaperMm(ratio, unit))); }
                    if (!string.IsNullOrEmpty(ch.Style)) psv.SetCurrentStyleSheet(ps, ch.Style);
                    ps.PrintLineweights = ch.Lineweights;
                    how = hasDevice || !ch.OwnSetup ? "my settings" : "my settings (the layout has no printer set)";
                }
                else
                {
                    how = "layout's own page setup";
                    if (ch.Pdf && lo.PlotConfigurationName.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) < 0) { psv.SetPlotConfigurationName(ps, string.IsNullOrEmpty(ch.Device) ? PdfDevice : ch.Device, lo.CanonicalMediaName); psv.RefreshLists(ps); how += " on the PDF driver"; }
                }
                pi.OverrideSettings = ps;
                var piv = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled }; piv.Validate(pi);
            }
            return pi;
        }

        [CommandMethod("BPPLOT")]
        public static void PlotLayouts()
        {
            Guard("BPPLOT", () =>
            {
                var doc = Doc; var ed = doc.Editor; var db = doc.Database; var inv = CultureInfo.InvariantCulture;
                if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting) { ed.WriteMessage("\n[B+] AutoCAD is plotting right now. Wait for it to finish.\n"); return; }
                Log.Write("BPPLOT: start"); double unit = DrawingCs.UnitToMetre(db); string drawing = Path.GetFileNameWithoutExtension(doc.Name);
                var rows = new List<LayoutRow>();
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ld = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead); var list = new List<Layout>();
                    foreach (DBDictionaryEntry en in ld) { var lo = (Layout)tr.GetObject(en.Value, OpenMode.ForRead); if (!lo.ModelType) list.Add(lo); }
                    Log.Write("BPPLOT: reading " + list.Count + " layouts"); foreach (var lo in list.OrderBy(l => l.TabOrder)) rows.Add(new LayoutRow { Name = lo.LayoutName, Setup = string.IsNullOrEmpty(lo.PlotSettingsName) ? "(none)" : lo.PlotSettingsName, Paper = MediaText(lo), Scale = ScaleText(lo, unit), Canon = SafeCanon(lo) });
                    tr.Commit();
                }
                if (rows.Count == 0) { ed.WriteMessage("\n[B+] This drawing has no layouts (only the Model tab).\n"); return; }
                string folder = Path.GetDirectoryName(doc.Name); if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                try
                {
                    if (Convert.ToInt32(AcApp.GetSystemVariable("DBMOD")) != 0)
                    {
                        var r = System.Windows.Forms.MessageBox.Show("This drawing has unsaved changes.\r\n\r\nPlotting talks to the printer drivers, and a driver problem can close AutoCAD without warning.\r\nSave the drawing first?", "B+ CAD Tools - Plot layouts", System.Windows.Forms.MessageBoxButtons.YesNoCancel);
                        if (r == System.Windows.Forms.DialogResult.Cancel) return;
                        if (r == System.Windows.Forms.DialogResult.Yes) { doc.Database.SaveAs(doc.Name, true, DwgVersion.Current, doc.Database.SecurityParameters); }
                    }
                }
                catch (System.Exception ex) { Log.Write("Plot save check: " + ex.Message); }
                Log.Write("BPPLOT: layouts listed, reading printer lists"); var lists = PlotLists.Read(); Log.Write("BPPLOT: opening dialog"); var ch = PlotUi.Ask(rows, drawing, folder, lists); if (ch == null) return; Log.Write("BPPLOT: dialog OK, plotting " + ch.Layouts.Count + " layout(s)");
                if (ch.Pdf && string.IsNullOrEmpty(ch.Device)) ch.Device = PdfDevice;
                bool ms = ch.Pdf && ch.OwnSetup == false && ch.Device != null && ch.Device.IndexOf("microsoft", StringComparison.OrdinalIgnoreCase) >= 0; if (ms) ch.Combined = false;
                if (ms) MsPdfWatch.Start(120);
                if (ch.Pdf) { try { Directory.CreateDirectory(ch.Folder); } catch (System.Exception ex) { ed.WriteMessage("\n[B+] Cannot use that folder: " + ex.Message + "\n"); return; } }

                var done = new List<string>(); var failed = new List<string>(); object oldBg = null; string oldLayout = null;
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
                                        var pi = BuildInfo(lo, ch, unit, out how, out keep); keepers.Add(keep); pis.Add(new KeyValuePair<string, PlotInfo>(name, pi));
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
                                                try { LayoutManager.Current.CurrentLayout = kv.Key; } catch { }
                                                string path = ch.Pdf ? PlotNaming.Unique(ch.Folder, PlotNaming.FileName(ch.Pattern, drawing, kv.Key), ".pdf", File.Exists) : "";
                                                pe.BeginDocument(kv.Value, doc.Name, null, 1, ch.Pdf, path);
                                                var pp = new PlotPageInfo(); pe.BeginPage(pp, kv.Value, true, null); pe.BeginGenerateGraphics(null); pe.EndGenerateGraphics(null); pe.EndPage(null); pe.EndDocument(null);
                                                done.Add(ch.Pdf ? path : kv.Key + " (printer)");
                                            }
                                            catch (System.Exception ex) { failed.Add(kv.Key + ": " + ex.Message); try { pe.EndDocument(null); } catch { } }
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
                System.Windows.Forms.MessageBox.Show(sb.ToString(), "B+ CAD Tools - Plot layouts");
            });
        }
    }
}
