using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BPlus.LiveMap.Setup
{
    static class Program
    {
        const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BPlusCadTools", LegacyUninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BPlusLiveMap";
        static string LogPath { get { string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BPlus", "LiveMap"); Directory.CreateDirectory(d); return Path.Combine(d, "install-log.txt"); } }
        static void Log(string m) { try { File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + m + Environment.NewLine); } catch { } }

        const string Version = "0.4.4";

        sealed class WaitForm : Form
        {
            public Label Msg = new Label { Left = 16, Top = 14, Width = 448, Height = 70, AutoSize = false };
            public Button Again = new Button { Text = "Ask AutoCAD to close again", Left = 16, Top = 96, Width = 190, Height = 28 };
            public Button Cancel2 = new Button { Text = "Cancel", Left = 374, Top = 96, Width = 90, Height = 28 };
            public bool Cancelled, AskAgain;
            protected override bool ShowWithoutActivation { get { return true; } }
            public WaitForm()
            {
                Text = "B+ CAD Tools update"; ClientSize = new System.Drawing.Size(480, 138); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = true; ShowInTaskbar = true; TopMost = false; StartPosition = FormStartPosition.Manual;
                var wa = Screen.PrimaryScreen.WorkingArea; Location = new System.Drawing.Point(wa.Right - Width - 20, wa.Bottom - Height - 20);
                Controls.AddRange(new Control[] { Msg, Again, Cancel2 });
                Again.Click += (s, e) => AskAgain = true; Cancel2.Click += (s, e) => Cancelled = true; FormClosing += (s, e) => { if (!Cancelled) { Cancelled = true; } };
            }
        }

        static System.Diagnostics.Process[] CadProcs() { try { return System.Diagnostics.Process.GetProcessesByName("acad"); } catch { return new System.Diagnostics.Process[0]; } }

        // Asks every AutoCAD / Civil 3D window to close the normal way (AutoCAD shows its own save prompts). Never kills a process. Returns false if the user cancels or 10 minutes pass.
        static bool CloseCad(System.Collections.Generic.List<string> reopen)
        {
            var procs = CadProcs(); if (procs.Length == 0) return true;
            foreach (var p in procs) { try { string f = p.MainModule.FileName; if (!reopen.Contains(f)) reopen.Add(f); } catch (Exception e) { Log("Path of " + p.Id + ": " + e.Message); } }
            Log("Asking " + procs.Length + " AutoCAD process(es) to close.");
            Action ask = () => { foreach (var p in CadProcs()) { try { if (!p.CloseMainWindow()) Log("CloseMainWindow refused for " + p.Id); } catch (Exception e) { Log("Close request failed: " + e.Message); } } };
            ask();
            using (var f = new WaitForm())
            {
                f.Msg.Text = "Waiting for AutoCAD to close...\n\nIf AutoCAD asks you to save a drawing, answer it there. This window does not block AutoCAD."; f.Show();
                var start = DateTime.UtcNow; bool slow = false;
                while (true)
                {
                    Application.DoEvents(); System.Threading.Thread.Sleep(250);
                    if (f.Cancelled) return false;
                    if (f.AskAgain) { f.AskAgain = false; ask(); }
                    var left = CadProcs(); if (left.Length == 0) break;
                    double sec = (DateTime.UtcNow - start).TotalSeconds;
                    if (sec > 600) return false;
                    if (!slow && sec > 20) { slow = true; f.Msg.Text = "AutoCAD is still open. If you kept a drawing, save it and close AutoCAD: the update then continues by itself. Nothing is closed by force.\n\n(If AutoCAD runs as administrator, Windows blocks the close request: close it yourself.)"; }
                }
                f.Msg.Text = "AutoCAD closed. Installing...";
                Application.DoEvents(); System.Threading.Thread.Sleep(800);
            }
            return true;
        }

        static string RunningCad()
        {
            try { var p = System.Diagnostics.Process.GetProcessesByName("acad"); int n = p.Length; foreach (var x in p) x.Dispose(); return n == 0 ? null : (n == 1 ? "AutoCAD or Civil 3D" : "AutoCAD or Civil 3D (" + n + " windows)"); } catch { return null; }
        }

        [STAThread]
        static int Main(string[] args)
        {
            bool silent = Array.Exists(args, a => a.Equals("/S", StringComparison.OrdinalIgnoreCase));
            bool un = Array.Exists(args, a => a.Equals("/U", StringComparison.OrdinalIgnoreCase));
            bool confirmed = Array.Exists(args, a => a.Equals("/CONFIRMED", StringComparison.OrdinalIgnoreCase));
            Application.EnableVisualStyles();
            try
            {
                string target = InstallLogic.DefaultTarget();
                if (un)
                {
                    InstallLogic.Uninstall(target, Log); InstallLogic.Uninstall(InstallLogic.LegacyTarget(), Log);
                    try { Registry.CurrentUser.DeleteSubKeyTree(UninstKey, false); Registry.CurrentUser.DeleteSubKeyTree(LegacyUninstKey, false); } catch { }
                    if (!silent) MessageBox.Show("B+ CAD Tools was removed. Your Google key and settings were kept in " + Path.GetDirectoryName(LogPath) + ".\nRestart AutoCAD.", "B+ CAD Tools", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                if (!silent && !confirmed)
                {
                    bool exists = Directory.Exists(target) || Directory.Exists(InstallLogic.LegacyTarget());
                    var r = MessageBox.Show((exists ? "B+ CAD Tools is already installed. Update it?" : "Install B+ CAD Tools (experimental) for AutoCAD and Civil 3D 2021 to 2026?") + "\n\nIt installs for your Windows user only." + (RunningCad() != null ? "\n\nAutoCAD or Civil 3D is open. It will be closed the normal way: AutoCAD asks you to save any unsaved drawing, nothing is forced. You can then reopen it from this installer." : "") + "\n\nThe map includes Google Maps content. Its use is subject to the Google Maps/Google Earth Additional Terms (maps.google.com/help/terms_maps) and the Google Privacy Policy (policies.google.com/privacy). Imagery is for viewing only: do not trace or digitize from it. Do not distribute this plugin in China, Crimea, Cuba, Donetsk, Iran, Luhansk, North Korea, Syria or Vietnam.", "B+ CAD Tools setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                    if (r != DialogResult.OK) return 1;
                }
                var reopen = new System.Collections.Generic.List<string>();
                var drawings = new System.Collections.Generic.List<string>(); string cadExe = null;
                try
                {
                    int di = Array.FindIndex(args, a => a.Equals("/DRAWINGS", StringComparison.OrdinalIgnoreCase));
                    if (di >= 0 && di + 1 < args.Length && File.Exists(args[di + 1]))
                    {
                        var ls = File.ReadAllLines(args[di + 1]); if (ls.Length > 0 && ls[0].Length > 0 && File.Exists(ls[0])) cadExe = ls[0];
                        for (int i = 1; i < ls.Length; i++) if (File.Exists(ls[i]) && ls[i].EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) drawings.Add(ls[i]);
                        Log("Drawings to reopen: " + drawings.Count);
                    }
                }
                catch (Exception e) { Log("Reopen list: " + e.Message); }
                if (!silent) { if (confirmed) System.Threading.Thread.Sleep(1500); if (!CloseCad(reopen)) { Log("Cancelled while waiting for AutoCAD to close."); return 1; } }
                Log("Install started. Installer " + Assembly.GetExecutingAssembly().GetName().Version + ", OS " + Environment.OSVersion);
                InstallLogic.RemoveLegacy(InstallLogic.LegacyTarget(), Log);
                try { Registry.CurrentUser.DeleteSubKeyTree(LegacyUninstKey, false); } catch { }
                while (true)
                {
                    try
                    {
                        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("bundle.zip"))
                        {
                            if (s == null) throw new InvalidOperationException("The installer is damaged (no package inside).");
                            InstallLogic.Install(s, target, Log);
                        }
                        break;
                    }
                    catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException || ex is LockedException) && !silent)
                    {
                        Log("Install problem: " + ex);
                        string run = RunningCad();
                        var rr = MessageBox.Show("Windows would not let the installer replace the files in\n" + target + "\n" + (run != null ? "\n" + run + " is open and is probably holding them. Close it completely (check Task Manager for acad.exe)." : "\nSomething else is holding them (antivirus, a file window or a command prompt open in that folder). Close those.") + "\n\nPress Retry after closing.\n\n(" + ex.Message + ")", "B+ CAD Tools setup", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning);
                        if (rr != DialogResult.Retry) throw;
                    }
                }
                using (var k = Registry.CurrentUser.CreateSubKey(UninstKey))
                {
                    string exe = Assembly.GetExecutingAssembly().Location;
                    k.SetValue("DisplayName", "B+ CAD Tools (experimental)");
                    k.SetValue("DisplayVersion", Version);
                    k.SetValue("Publisher", "BPlus Software");
                    k.SetValue("InstallLocation", target);
                    k.SetValue("UninstallString", "\"" + exe + "\" /U");
                    k.SetValue("NoModify", 1); k.SetValue("NoRepair", 1);
                }
                Log("Install finished OK.");
                if (!silent)
                {
                    if (reopen.Count > 0)
                    {
                        if (MessageBox.Show("Installed " + Version + ".\n\nOpen AutoCAD again now?" + (drawings.Count > 0 ? " Your " + drawings.Count + " saved drawing(s) will be opened again." : " (Your earlier drawings are not reopened.)") + "\n\nYou will see a B+ CAD Tools tab. Type BPCHECK to check the install.", "B+ CAD Tools", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                            foreach (var exe in reopen) { try { string a = (cadExe != null && string.Equals(exe, cadExe, StringComparison.OrdinalIgnoreCase)) ? string.Join(" ", drawings.ConvertAll(d => "\"" + d + "\"").ToArray()) : ""; System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, a) { UseShellExecute = true }); } catch (Exception ex) { Log("Reopen failed: " + ex.Message); } }
                    }
                    else MessageBox.Show("Installed " + Version + ".\n\nRestart AutoCAD or Civil 3D. You will see a B+ CAD Tools tab. Type BPCHECK to check the install.\nInstall log: " + LogPath, "B+ CAD Tools", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return 0;
            }
            catch (Exception e)
            {
                Log("FAILED: " + e);
                if (!silent) MessageBox.Show("Install failed:\n" + e.Message + "\n\nLog: " + LogPath, "B+ CAD Tools", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
        }
    }
}
