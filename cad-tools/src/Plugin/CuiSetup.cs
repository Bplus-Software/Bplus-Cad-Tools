using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Customization;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BPlus.LiveMap
{
    /// <summary>Puts the B+ tab in the menu (CUIx) system, so it is part of every workspace and survives a workspace change.
    /// 1. Writes our own partial menu file. 2. Adds the tab to every workspace of the main menu file, after a backup. The tab built in code stays as a safety net.</summary>
    static class CuiSetup
    {
        const string Group = "BPLUS_CADTOOLS", TabName = "B+ CAD Tools", TabId = "BPLUS_CADTOOLS_CUI_TAB";
        static string State { get { return Path.Combine(Paths.Dir, "cui-state.txt"); } }
        static string PartialPath { get { return Path.Combine(Paths.Dir, "BPlusCadTools.cuix"); } }
        static string BackupDir { get { string d = Path.Combine(Paths.Dir, "cui-backup"); Directory.CreateDirectory(d); return d; } }
        static string Read() { try { return File.Exists(State) ? File.ReadAllText(State).Trim() : ""; } catch { return ""; } }
        static void Write(string v) { try { File.WriteAllText(State, v); } catch { } }
        static void Say(string t) { try { var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor; if (ed != null) ed.WriteMessage("\n[B+] " + t + "\n"); } catch { } Log.Write("CUI: " + t); }

        /// <summary>Called once at startup, when AutoCAD is idle. Does nothing when this version has already been set up or the user said no.</summary>
        public static void StartupCheck()
        {
            string st = Read(); if (st == "off" || st == App.Version) return;
            AcApp.Idle += OnIdleOnce;
        }
        static void OnIdleOnce(object s, EventArgs e)
        {
            AcApp.Idle -= OnIdleOnce;
            try { if (AcApp.DocumentManager.MdiActiveDocument == null) { AcApp.Idle += OnIdleOnce; return; } Run(false); } catch (System.Exception ex) { Say("Menu setup failed and was skipped: " + ex.Message); }
        }

        /// <summary>The real work. manual = typed command (asks nothing extra, forces a rebuild).</summary>
        public static void Run(bool manual)
        {
            string menu = Convert.ToString(AcApp.GetSystemVariable("MENUNAME")); string main = Path.GetFullPath(menu + ".cuix");
            if (!File.Exists(main)) { Say("Main menu file not found (" + main + "). The tab built in code is used instead."); Write(App.Version); return; }
            var fi = new FileInfo(main); if (fi.IsReadOnly) { Say("The main menu file is read-only. The tab built in code is used instead."); Write(App.Version); return; }
            // tell the user, with a way to say no
            string backup = Path.Combine(BackupDir, Path.GetFileNameWithoutExtension(main) + "-before-BPlus-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".cuix");
            var r = MessageBox.Show("B+ CAD Tools will add its ribbon tab to every workspace in your main menu file:\n\n" + main + "\n\nA backup copy is saved first:\n" + backup + "\n\nThis keeps the tab when you switch workspace. The command BPRIBBONRESET undoes it.\n\nOK = do it now.   Cancel = skip (type BPRIBBONSETUP later).", "B+ CAD Tools - ribbon setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (r != DialogResult.OK) { Write("off"); Say("Ribbon setup skipped. Type BPRIBBONSETUP to do it later."); return; }
            File.Copy(main, backup, true); Say("Backup saved: " + backup);
            string err = BuildPartial(); if (err != null) { Say("Could not build the menu file: " + err + ". Nothing in your main menu file was changed."); Write(App.Version); return; }
            try
            {
                var cs = new CustomizationSection(main);
                if (!cs.PartialCuiFiles.Contains(PartialPath)) cs.PartialCuiFiles.Add(PartialPath);
                var part = new CustomizationSection(PartialPath); RibbonTabSource tab = null;
                foreach (RibbonTabSource t in part.MenuGroup.RibbonRoot.RibbonTabSources) if (t.Name == TabName) tab = t;
                if (tab == null) throw new InvalidOperationException("the tab is not in the new menu file");
                int added = 0, had = 0;
                for (int i = 0; i < cs.Workspaces.Count; i++)
                {
                    var wr = cs.Workspaces[i].WorkspaceRibbonRoot; if (wr == null) continue;
                    if (wr.FindTabReference(Group, tab.ElementID) != null) { had++; continue; }
                    var rf = WSRibbonTabSourceReference.Create(tab); rf.SetParent(wr); wr.WorkspaceTabs.Add(rf); added++;
                }
                if (cs.IsModified) cs.Save();
                var check = new CustomizationSection(main); if (check.Workspaces.Count != cs.Workspaces.Count) throw new InvalidOperationException("the saved menu file does not read back the same");
                Say("Tab added to " + added + " workspace(s)" + (had > 0 ? " (" + had + " already had it)" : "") + ". Reloading the menu once.");
                Write(App.Version);
                var doc = AcApp.DocumentManager.MdiActiveDocument; string ws = Convert.ToString(AcApp.GetSystemVariable("WSCURRENT")), bn = cs.CUIFileBaseName;
                doc.SendStringToExecute("FILEDIA 0 ", false, false, false); doc.SendStringToExecute("CUIUNLOAD " + bn + " ", false, false, false); doc.SendStringToExecute("CUILOAD " + bn + " ", false, false, false); doc.SendStringToExecute("FILEDIA 1 ", false, false, false); doc.SendStringToExecute("WSCURRENT " + ws + "\n", false, false, false);
            }
            catch (System.Exception ex)
            {
                try { File.Copy(backup, main, true); Say("Setup failed (" + ex.Message + "). Your main menu file was restored from the backup."); } catch (System.Exception e2) { Say("Setup failed (" + ex.Message + ") and the restore failed (" + e2.Message + "). Copy " + backup + " over " + main + "."); }
                Write(App.Version);
            }
        }

        /// <summary>Writes the partial menu file: macros, panels (stacked rows of small buttons) and the tab. Returns an error text or null.</summary>
        static string BuildPartial()
        {
            try
            {
                if (File.Exists(PartialPath)) File.Delete(PartialPath);
                string icons = Path.Combine(Paths.Dir, "icons"); Directory.CreateDirectory(icons);
                var cs = new CustomizationSection(PartialPath, Group); var mg = cs.MenuGroup; mg.DisplayName = TabName;
                var grp = new MacroGroup("B+ CAD Tools", mg); if (!mg.MacroGroups.Contains(grp.Name)) mg.MacroGroups.Add(grp);
                var root = mg.RibbonRoot; var tab = new RibbonTabSource(root) { Name = TabName, Text = TabName, Id = TabId }; root.RibbonTabSources.Add(tab);
                int pn = 0;
                foreach (var kv in Ribbon.Layout())
                {
                    var parts = kv.Key.Split('|'); string title = parts[0]; int rows = int.Parse(parts[1]); var btns = kv.Value; pn++;
                    var ps = new RibbonPanelSource(root) { Name = title, Text = title, Id = "BPLUS_PANEL_" + pn }; root.RibbonPanelSources.Add(ps);
                    var top = new RibbonRow(ps); ps.Items.Add(top);
                    for (int c = 0; c < (btns.Length + rows - 1) / rows; c++)
                    {
                        var rp = new RibbonRowPanel(top); top.Items.Add(rp);
                        for (int rr = 0; rr < rows; rr++)
                        {
                            int i = c * rows + rr; if (i >= btns.Length) break; var b = btns[i];
                            string s16 = Path.Combine(icons, b[2] + "16.png"), s32 = Path.Combine(icons, b[2] + "32.png"); Ribbon.SavePng(16, b[2], s16); Ribbon.SavePng(32, b[2], s32);
                            var mm = grp.CreateMenuMacro(b[0].Replace("\n", " "), "^C^C" + b[1].Trim(), "BPLUS_" + b[1].Trim(), b[3], MacroType.Any, s16, s32, null);
                            var row = new RibbonRow(rp); rp.Items.Add(row);
                            var btn = new RibbonCommandButton(row) { ButtonStyle = RibbonButtonStyle.SmallWithText, MacroID = mm.ElementID, Id = "BPLUS_BTN_" + pn + "_" + i, Text = b[0].Replace("\n", " ") }; row.Items.Add(btn);
                        }
                    }
                    var pr = new RibbonPanelSourceReference(tab) { PanelId = ps.ElementID }; tab.Items.Add(pr);
                }
                cs.Save();
                var back = new CustomizationSection(PartialPath); if (!back.MenuGroup.RibbonRoot.RibbonTabSources.Cast<RibbonTabSource>().Any(t => t.Name == TabName)) return "the file did not read back";
                return null;
            }
            catch (System.Exception ex) { return ex.Message; }
        }

        /// <summary>Removes our tab from every workspace and unloads our partial file, then restores nothing else (the backup stays in the folder).</summary>
        public static void Reset()
        {
            string menu = Convert.ToString(AcApp.GetSystemVariable("MENUNAME")); string main = Path.GetFullPath(menu + ".cuix");
            if (!File.Exists(main)) { Say("Main menu file not found."); return; }
            string backup = Path.Combine(BackupDir, Path.GetFileNameWithoutExtension(main) + "-before-reset-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".cuix"); File.Copy(main, backup, true);
            try
            {
                var cs = new CustomizationSection(main); int removed = 0;
                for (int i = 0; i < cs.Workspaces.Count; i++)
                {
                    var wr = cs.Workspaces[i].WorkspaceRibbonRoot; if (wr == null) continue;
                    var del = wr.WorkspaceTabs.Cast<WSRibbonTabSourceReference>().Where(t => t.MenuGroup == Group || t.MenuGroup == TabName).ToList();
                    foreach (var t in del) { wr.WorkspaceTabs.Remove(t); removed++; }
                }
                var pf = cs.PartialCuiFiles.Cast<string>().Where(p => p.EndsWith("BPlusCadTools.cuix", StringComparison.OrdinalIgnoreCase)).ToList(); foreach (var p in pf) cs.PartialCuiFiles.Remove(p);
                if (cs.IsModified) cs.Save();
                Write("off"); Say("Removed the B+ tab from " + removed + " workspace entry(ies). A backup of the file before this step: " + backup + ". Restart or type WSCURRENT to refresh. The tab built in code still works.");
            }
            catch (System.Exception ex) { try { File.Copy(backup, main, true); } catch { } Say("Reset failed: " + ex.Message + ". Main menu file restored from " + backup); }
        }
    }

    public partial class Commands
    {
        [CommandMethod("BPRIBBON")] public static void RibbonAgain() { Guard("BPRIBBON", () => { bool ok = Ribbon.AddNow(); AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage(ok ? "\n[B+] The B+ CAD Tools tab is in the ribbon.\n" : "\n[B+] Could not add the tab: the ribbon is not ready.\n"); }); }
        [CommandMethod("BPRIBBONSETUP")] public static void RibbonSetup() { Guard("BPRIBBONSETUP", () => CuiSetup.Run(true)); }
        [CommandMethod("BPRIBBONRESET")] public static void RibbonReset() { Guard("BPRIBBONRESET", () => CuiSetup.Reset()); }
    }
}
