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
        public static string Version = "0.1.0-experimental";
        public void Initialize()
        {
            try
            {
                Log.Write("Startup. Plugin " + Version + ", runtime " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + ", AutoCAD " + AcApp.Version + ", DLL " + Assembly.GetExecutingAssembly().Location);
                Engine.Init();
                Ribbon.Install();
                var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                if (ed != null) ed.WriteMessage("\nBPlus Live Map " + Version + " loaded. Use the BPlus tab, or type BPGMAP.\n");
            }
            catch (Exception e) { Log.Write("Startup failed: " + e); Commands.LastError = e.Message; }
        }
        public void Terminate() { Engine.Shutdown(); Log.Write("Shutdown."); }
    }

    public class Commands
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
                if (!Config.TermsAccepted || !KeyStore.HasKey) { doc.Editor.WriteMessage("\n[BPlus map] First enter your Google key and accept the terms.\n"); SettingsDialog.Show(); if (!Config.TermsAccepted || !KeyStore.HasKey) return; }
                var p = Engine.EnsureProvider(doc, true);
                if (p == null) { doc.Editor.WriteMessage("\n[BPlus map] No coordinate system chosen. Live map not started.\n"); return; }
                doc.Editor.WriteMessage("\n[BPlus map] Live map on. Coordinate system: " + p.Description + " (from " + p.Source + ").\nGoogle imagery is for viewing only. Do not trace or digitize from it.\n");
                Engine.SetOn(true);
            });
        }
        public static void Off() { Guard("BPGMAP", () => { Engine.SetOn(false); Doc?.Editor.WriteMessage("\n[BPlus map] Live map off.\n"); }); }

        [CommandMethod("BPGMAPSET")] public static void Set() { Guard("BPGMAPSET", SettingsDialog.Show); }

        [CommandMethod("BPGMAPTYPE")]
        public static void Type()
        {
            Guard("BPGMAPTYPE", () =>
            {
                var o = new PromptKeywordOptions("\nMap type [Satellite/Roadmap/Hybrid]", "Satellite Roadmap Hybrid") { AllowNone = false };
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
                var o = new PromptIntegerOptions("\nMap opacity percent (10-100)") { DefaultValue = Config.OpacityPercent, UseDefaultValue = true, LowerLimit = 10, UpperLimit = 100 };
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
                var doc = Doc; int epsg = CsPicker.Ask(); if (epsg <= 0) return;
                using (doc.LockDocument()) DrawingCs.SaveEpsg(doc.Database, epsg);
                Engine.ForgetProvider(doc);
                var p = Engine.EnsureProvider(doc, false);
                doc.Editor.WriteMessage("\n[BPlus map] Coordinate system: " + (p != null ? p.Description + " (from " + p.Source + ")" : "not set") + "\n");
            });
        }

        [CommandMethod("BPGMAPFLIP")]
        public static void Flip() { Guard("BPGMAPFLIP", () => { Config.FlipRows = !Config.FlipRows; Doc.Editor.WriteMessage("\n[BPlus map] Image rows flipped: " + (Config.FlipRows ? "yes" : "no") + "\n"); Engine.RefreshNow(); }); }

        [CommandMethod("BPGMAPTEST")]
        public static void Test()
        {
            Guard("BPGMAPTEST", () =>
            {
                string prob; var vi = Engine.ReadView(Doc, out prob);
                if (vi == null) { Doc.Editor.WriteMessage("\n[BPlus map] " + prob + "\n"); return; }
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
                ed.WriteMessage("\n--- BPlus Live Map check ---");
                ed.WriteMessage("\nPlugin version : " + App.Version);
                ed.WriteMessage("\nAutoCAD        : " + AcApp.Version);
                ed.WriteMessage("\nRuntime        : " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
                ed.WriteMessage("\nPlugin DLL     : " + Assembly.GetExecutingAssembly().Location);
                ed.WriteMessage("\nLog file       : " + Paths.LogFile);
                ed.WriteMessage("\nGoogle key     : " + (KeyStore.HasKey ? "saved on this PC" : "not saved"));
                ed.WriteMessage("\nTerms accepted : " + (Config.TermsAccepted ? "yes" : "no"));
                ed.WriteMessage("\nCoord system   : " + (p != null ? p.Description + " (from " + p.Source + ")" : "none yet"));
                ed.WriteMessage("\nPlan view      : " + (prob == null ? "yes" : "no - " + prob));
                ed.WriteMessage("\nLive map       : " + (Engine.On ? "on" : "off"));
                ed.WriteMessage("\nRequests/month : " + Engine.Usage.ThisMonth);
                ed.WriteMessage("\nLast error     : " + (string.IsNullOrEmpty(Engine.LastError) ? (string.IsNullOrEmpty(LastError) ? "none" : LastError) : Engine.LastError) + "\n");
            });
        }
    }
}
