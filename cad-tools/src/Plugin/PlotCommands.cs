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

        static string MediaText(PlotSettings ps)
        {
            string c = ""; try { c = ps.CanonicalMediaName; } catch { }
            if (string.IsNullOrEmpty(c)) return "(none)";
            try { return PlotSettingsValidator.Current.GetLocaleMediaName(ps, c); } catch { return c; }
        }

        static PlotInfo BuildInfo(Layout lo, PlotChoice ch, double unit, out string how)
        {
            var psv = PlotSettingsValidator.Current; var pi = new PlotInfo { Layout = lo.ObjectId }; how = "";
            bool hasDevice = !string.IsNullOrEmpty(lo.PlotConfigurationName) && !lo.PlotConfigurationName.Equals("None", StringComparison.OrdinalIgnoreCase);
            bool custom = !ch.OwnSetup || !hasDevice || string.IsNullOrEmpty(lo.CanonicalMediaName);
            using (var ps = new PlotSettings(lo.ModelType))
            {
                ps.CopyFrom(lo);
                if (custom)
                {
                    psv.SetPlotConfigurationName(ps, ch.Device, ch.MediaCanonical); psv.RefreshLists(ps);
                    psv.SetPlotPaperUnits(ps, PlotPaperUnit.Millimeters); psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Layout);
                    psv.SetPlotRotation(ps, ch.Rotation == 0 ? PlotRotation.Degrees000 : PlotRotation.Degrees090);
                    double ratio = PlotNaming.ParseScale(ch.Scale);
                    if (ratio == 0) psv.SetStdScaleType(ps, StdScaleType.ScaleToFit);
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
                double unit = DrawingCs.UnitToMetre(db); string drawing = Path.GetFileNameWithoutExtension(doc.Name);
                var rows = new List<LayoutRow>();
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ld = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead); var list = new List<Layout>();
                    foreach (DBDictionaryEntry en in ld) { var lo = (Layout)tr.GetObject(en.Value, OpenMode.ForRead); if (!lo.ModelType) list.Add(lo); }
                    foreach (var lo in list.OrderBy(l => l.TabOrder)) rows.Add(new LayoutRow { Name = lo.LayoutName, Setup = string.IsNullOrEmpty(lo.PlotSettingsName) ? "(none)" : lo.PlotSettingsName, Paper = MediaText(lo), Scale = ScaleText(lo, unit) });
                    tr.Commit();
                }
                if (rows.Count == 0) { ed.WriteMessage("\n[B+] This drawing has no layouts (only the Model tab).\n"); return; }
                string folder = Path.GetDirectoryName(doc.Name); if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var ch = PlotUi.Ask(rows, drawing, folder, PlotLists.Read()); if (ch == null) return;
                if (ch.Pdf) { try { Directory.CreateDirectory(ch.Folder); } catch (System.Exception ex) { ed.WriteMessage("\n[B+] Cannot use that folder: " + ex.Message + "\n"); return; } }

                var done = new List<string>(); var failed = new List<string>(); object oldBg = null; string oldLayout = null;
                try { oldBg = AcApp.GetSystemVariable("BACKGROUNDPLOT"); AcApp.SetSystemVariable("BACKGROUNDPLOT", 0); } catch { }
                try { oldLayout = LayoutManager.Current.CurrentLayout; } catch { }
                try
                {
                    using (doc.LockDocument())
                    using (var tr = db.TransactionManager.StartTransaction())
                    using (var pe = PlotFactory.CreatePublishEngine())
                    {
                        var ld = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                        var pis = new List<KeyValuePair<string, PlotInfo>>();
                        foreach (var name in ch.Layouts)
                        {
                            try
                            {
                                LayoutManager.Current.CurrentLayout = name;
                                var lo = (Layout)tr.GetObject(ld.GetAt(name), OpenMode.ForRead); string how;
                                var pi = BuildInfo(lo, ch, unit, out how); pis.Add(new KeyValuePair<string, PlotInfo>(name, pi));
                            }
                            catch (System.Exception ex) { failed.Add(name + ": " + ex.Message); }
                        }
                        if (pis.Count > 0)
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
                        tr.Commit();
                    }
                }
                catch (System.Exception ex) { failed.Add("Plot stopped: " + ex.Message); }
                finally
                {
                    try { if (oldLayout != null) LayoutManager.Current.CurrentLayout = oldLayout; } catch { }
                    try { if (oldBg != null) AcApp.SetSystemVariable("BACKGROUNDPLOT", oldBg); } catch { }
                }
                var sb = new StringBuilder(); sb.AppendLine(done.Count + " done, " + failed.Count + " failed."); foreach (var d in done) sb.AppendLine("  " + d); foreach (var x in failed) sb.AppendLine("  FAILED " + x);
                ed.WriteMessage("\n[B+] " + sb.ToString().Replace("\r", ""));
                System.Windows.Forms.MessageBox.Show(sb.ToString(), "B+ CAD Tools - Plot layouts");
            });
        }
    }
}
