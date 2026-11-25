using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BPlus.LiveMap.Setup
{
    static class Program
    {
        const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BPlusLiveMap";
        static string LogPath { get { string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BPlus", "LiveMap"); Directory.CreateDirectory(d); return Path.Combine(d, "install-log.txt"); } }
        static void Log(string m) { try { File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + m + Environment.NewLine); } catch { } }

        [STAThread]
        static int Main(string[] args)
        {
            bool silent = Array.Exists(args, a => a.Equals("/S", StringComparison.OrdinalIgnoreCase));
            bool un = Array.Exists(args, a => a.Equals("/U", StringComparison.OrdinalIgnoreCase));
            Application.EnableVisualStyles();
            try
            {
                string target = InstallLogic.DefaultTarget();
                if (un)
                {
                    InstallLogic.Uninstall(target, Log);
                    try { Registry.CurrentUser.DeleteSubKeyTree(UninstKey, false); } catch { }
                    if (!silent) MessageBox.Show("BPlus Live Map was removed. Your Google key and settings were kept in " + Path.GetDirectoryName(LogPath) + ".\nRestart AutoCAD.", "BPlus Live Map", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                if (!silent)
                {
                    bool exists = Directory.Exists(target);
                    var r = MessageBox.Show((exists ? "BPlus Live Map is already installed. Update it?" : "Install BPlus Live Map (experimental) for AutoCAD and Civil 3D 2021 to 2026?") + "\n\nIt installs for your Windows user only. Close AutoCAD first.\nGoogle imagery is for viewing only. Do not trace or digitize from it.", "BPlus Live Map setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                    if (r != DialogResult.OK) return 1;
                }
                Log("Install started. Installer " + Assembly.GetExecutingAssembly().GetName().Version + ", OS " + Environment.OSVersion);
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("bundle.zip"))
                {
                    if (s == null) throw new InvalidOperationException("The installer is damaged (no package inside).");
                    InstallLogic.Install(s, target, Log);
                }
                using (var k = Registry.CurrentUser.CreateSubKey(UninstKey))
                {
                    string exe = Assembly.GetExecutingAssembly().Location;
                    k.SetValue("DisplayName", "BPlus Live Map (experimental)");
                    k.SetValue("DisplayVersion", "0.1.0");
                    k.SetValue("Publisher", "BPlus Software");
                    k.SetValue("InstallLocation", target);
                    k.SetValue("UninstallString", "\"" + exe + "\" /U");
                    k.SetValue("NoModify", 1); k.SetValue("NoRepair", 1);
                }
                Log("Install finished OK.");
                if (!silent) MessageBox.Show("Installed.\n\nRestart AutoCAD or Civil 3D. You will see a BPlus tab. Type BPCHECK to check the install.\nInstall log: " + LogPath, "BPlus Live Map", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception e)
            {
                Log("FAILED: " + e);
                if (!silent) MessageBox.Show("Install failed:\n" + e.Message + "\n\nLog: " + LogPath, "BPlus Live Map", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
        }
    }
}
