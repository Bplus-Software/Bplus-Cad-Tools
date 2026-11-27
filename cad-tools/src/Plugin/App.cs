using System;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using BPlus.LiveMap.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

[assembly: ExtensionApplication(typeof(BPlus.LiveMap.App))]
[assembly: CommandClass(typeof(BPlus.LiveMap.Commands))]

namespace BPlus.LiveMap
{
    public class App : IExtensionApplication
    {
        public static string Version = "0.4.28-experimental";
        public void Initialize()
        {
            try
            {
                Log.Write("Startup. Plugin " + Version + ", runtime " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + ", AutoCAD " + AcApp.Version + ", DLL " + Assembly.GetExecutingAssembly().Location);
                Engine.Init();
                Ribbon.Install();
                CuiSetup.StartupCheck();
                UpdateCheck.StartupCheck();
                var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                if (ed != null) ed.WriteMessage(Ar.T("\nB+ CAD Tools ") + Version + Ar.T(" loaded. Use the B+ CAD Tools tab.\n"));
            }
            catch (Exception e) { Log.Write("Startup failed: " + e); Commands.LastError = e.Message; }
        }
        public void Terminate() { Engine.Shutdown(); Log.Write("Shutdown."); }
    }

    public partial class Commands
    {
        public static string LastError = "";
        static void Guard(string name, Action a)
        {
            try { a(); }
            catch (Exception e) { LastError = name + ": " + e.Message; Log.Write(name + " failed: " + e); try { AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n[BPlus map] " + name + " failed: " + e.Message + " (details in " + Paths.LogFile + ")\n"); } catch { } }
        }
        static Document Doc { get { return AcApp.DocumentManager.MdiActiveDocument; } }

        [CommandMethod("BPGMAP")]
        public static void Toggle() { Guard("BPGMAP", () => { if (Engine.On) Off(); else On(); }); }

        public static void On()
        {
            Guard("BPGMAP", () =>
            {
                var doc = Doc; if (doc == null) return;
                if (!Config.TermsAccepted || (Config.UseOwnKey && !KeyStore.HasKey)) { doc.Editor.WriteMessage(Ar.T("\n[BPlus map] Please read and accept the terms") + (Config.UseOwnKey ? Ar.T(" and enter your Google key") : "") + ".\n"); SettingsDialog.Show(); if (!Config.TermsAccepted || (Config.UseOwnKey && !KeyStore.HasKey)) { System.Windows.Forms.MessageBox.Show(Ar.T("The live map did not start: ") + (!Config.TermsAccepted ? Ar.T("the terms were not accepted. ") : "") + (Config.UseOwnKey && !KeyStore.HasKey ? Ar.T("No Google key is saved. Click Live map > Options > Settings and Google key, paste your key, tick the terms and press Save.") : ""), Ar.T("B+ CAD Tools - Live Map"), System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information); return; } }
                var p = Engine.EnsureProvider(doc, true);
                if (p == null) { doc.Editor.WriteMessage(Ar.T("\n[BPlus map] No coordinate system chosen. Live map not started.\n")); return; }
                doc.Editor.WriteMessage(Ar.T("\n[BPlus map] Live map on. Coordinate system: ") + p.Description + Ar.T(" (from ") + p.Source + Ar.T(").\nGoogle imagery is for viewing only. Do not trace or digitize from it.\n"));
                Engine.SetOn(true);
                System.Windows.Forms.MessageBox.Show(Ar.T("Live map is on.\n\nIt shows Google imagery behind the whole drawing view, in the drawing's coordinate system (") + p.Description + Ar.T("). Use PLAN > World first, then pan and zoom. Click Live map again to turn it off.\n\nIf you see nothing: run BPGMAPTEST, or BPCHECK and send the result."), Ar.T("B+ CAD Tools - Live Map"), System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
            });
        }
        public static void Off() { Guard("BPGMAP", () => { Engine.SetOn(false); Doc?.Editor.WriteMessage(Ar.T("\n[BPlus map] Live map off.\n")); }); }

        [CommandMethod("BPGMAPSET")] public static void Set() { Guard("BPGMAPSET", SettingsDialog.Show); }

        [CommandMethod("BPGMAPTYPE")]
        public static void Type()
        {
            Guard("BPGMAPTYPE", () =>
            {
                var o = new PromptKeywordOptions(Ar.T("\nMap type [Satellite/Roadmap/Hybrid]"), Ar.T("Satellite Roadmap Hybrid")) { AllowNone = false };
                var r = Doc.Editor.GetKeywords(o); if (r.Status != PromptStatus.OK) return;
                Config.Kind = r.StringResult == "Roadmap" ? MapKind.Roadmap : r.StringResult == "Hybrid" ? MapKind.Hybrid : MapKind.Satellite;
                Engine.RefreshNow();
            });
        }

        [CommandMethod("BPGMAPOPACITY")]
        public static void Opacity()
        {
            Guard("BPGMAPOPACITY", () =>
            {
                var o = new PromptIntegerOptions(Ar.T("\nMap opacity percent (10-100)")) { DefaultValue = Config.OpacityPercent, UseDefaultValue = true, LowerLimit = 10, UpperLimit = 100 };
                var r = Doc.Editor.GetInteger(o); if (r.Status != PromptStatus.OK) return;
                Config.OpacityPercent = r.Value; Engine.RefreshNow();
            });
        }

        [CommandMethod("BPGMAPREFRESH")] public static void Refresh() { Guard("BPGMAPREFRESH", Engine.RefreshNow); }

        [CommandMethod("BPGMAPCS")]
        public static void ChooseCs()
        {
            Guard("BPGMAPCS", () =>
            {
                var doc = Doc; int prevPick = DrawingCs.LoadEpsg(doc.Database); int epsg = CsPicker.Ask(); if (epsg <= 0) return;
                using (doc.LockDocument()) DrawingCs.SaveEpsg(doc.Database, epsg);
                try { var cs = CoordSys.FromEpsg(epsg); if (cs != null) { int o = AssignCs.Offer(doc, cs, prevPick); doc.Editor.WriteMessage(o == 3 ? "\n[BPlus map] Set as the drawing's coordinate system and the objects were moved. UNDO should reverse it.\n" : o == 2 ? "\n[BPlus map] Also set as the drawing's coordinate system. No object was moved.\n" : "\n[BPlus map] Used in B+ tools only. The drawing's own setting was not changed.\n"); } } catch (System.Exception ex) { Log.Write("AssignCs offer: " + ex.Message); }
                Engine.ForgetProvider(doc);
                var p = Engine.EnsureProvider(doc, false);
                doc.Editor.WriteMessage(Ar.T("\n[BPlus map] Coordinate system: ") + (p != null ? p.Description + Ar.T(" (from ") + p.Source + ")" : Ar.T("not set")) + "\n");
            });
        }

        [CommandMethod("BPLIBDUMP")]
        public static void LibDump()
        {
            Guard("BPLIBDUMP", () =>
            {
                var ed = Doc.Editor; var r = ed.GetString(new PromptStringOptions(Ar.T("\nWord to look for in the coordinate library (e.g. GRF17, Saudi, UTM; Enter = everything): ")) { AllowSpaces = true });
                if (r.Status != PromptStatus.OK && r.Status != PromptStatus.None) return;
                string path = AssignCs.DumpLibrary(r.StringResult);
                ed.WriteMessage(Ar.T("\n[BPlus map] Library list written to: ") + path + "\n");
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
            });
        }

        [CommandMethod("BPGMAPFLIP")]
        public static void Flip() { Guard("BPGMAPFLIP", () => { Config.FlipRows = !Config.FlipRows; Doc.Editor.WriteMessage(Ar.T("\n[BPlus map] Image rows flipped: ") + (Config.FlipRows ? Ar.T("yes") : "no") + "\n"); Engine.RefreshNow(); }); }

        [CommandMethod("BPGMAPTEST")]
        public static void Test()
        {
            Guard("BPGMAPTEST", () =>
            {
                string prob; var vi = Engine.ReadView(Doc, out prob);
                if (vi == null) { Doc.Editor.WriteMessage(Ar.T("\n[BPlus map] ") + prob + "\n"); return; }
                Engine.ShowTestCard(Doc, vi);
            });
        }

        [CommandMethod("BPCHECK")]
        public static void Check()
        {
            Guard("BPCHECK", () =>
            {
                var doc = Doc; var ed = doc.Editor;
                var p = Engine.ProviderFor(doc) ?? DrawingCs.Get(doc.Database);
                string prob; Engine.ReadView(doc, out prob);
                ed.WriteMessage(Ar.T("\n--- B+ CAD Tools check ---"));
                ed.WriteMessage(Ar.T("\nPlugin version : ") + App.Version);
                ed.WriteMessage(Ar.T("\nAutoCAD        : ") + AcApp.Version);
                ed.WriteMessage(Ar.T("\nRuntime        : ") + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
                ed.WriteMessage(Ar.T("\nPlugin DLL     : ") + Assembly.GetExecutingAssembly().Location);
                ed.WriteMessage(Ar.T("\nLog file       : ") + Paths.LogFile);
                ed.WriteMessage(Ar.T("\nMap source     : ") + (Config.UseOwnKey ? Ar.T("your own Google key (") + (KeyStore.HasKey ? Ar.T("saved") : Ar.T("NOT saved")) + ")" : Ar.T("BPlus map server ") + Config.ServerUrl + " (" + (Config.InstallToken.Length > 0 ? Ar.T("registered") : Ar.T("not registered yet")) + ")"));
                ed.WriteMessage(Ar.T("\nTerms accepted : ") + (Config.TermsAccepted ? Ar.T("yes") : "no"));
                ed.WriteMessage(Ar.T("\nCoord system   : ") + (p != null ? p.Description + Ar.T(" (from ") + p.Source + ")" : Ar.T("none yet")));
                ed.WriteMessage(Ar.T("\nPlan view      : ") + (prob == null ? Ar.T("yes") : "no - " + prob));
                ed.WriteMessage(Ar.T("\nLive map       : ") + (Engine.On ? "on" : Ar.T("off")));
                ed.WriteMessage(Ar.T("\nRequests/month : ") + Engine.Usage.ThisMonth);
                ed.WriteMessage(Ar.T("\nLast error     : ") + (string.IsNullOrEmpty(Engine.LastError) ? (string.IsNullOrEmpty(LastError) ? Ar.T("none") : LastError) : Engine.LastError) + "\n");
            });
        }
    }
}
