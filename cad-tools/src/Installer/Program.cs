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
                    InstallLogic.Uninstall(target, Log); InstallLogic.Uninstall(InstallLogic.LegacyTarget(), Log);
                    try { Registry.CurrentUser.DeleteSubKeyTree(UninstKey, false); Registry.CurrentUser.DeleteSubKeyTree(LegacyUninstKey, false); } catch { }
                    if (!silent) MessageBox.Show("B+ CAD Tools was removed. Your Google key and settings were kept in " + Path.GetDirectoryName(LogPath) + ".\nRestart AutoCAD.", "B+ CAD Tools", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                if (!silent)
                {
                    bool exists = Directory.Exists(target) || Directory.Exists(InstallLogic.LegacyTarget());
                    var r = MessageBox.Show((exists ? "B+ CAD Tools is already installed. Update it?" : "Install B+ CAD Tools (experimental) for AutoCAD and Civil 3D 2021 to 2026?") + "\n\nIt installs for your Windows user only. Close AutoCAD first.\nThe map includes Google Maps content. Its use is subject to the Google Maps/Google Earth Additional Terms (maps.google.com/help/terms_maps) and the Google Privacy Policy (policies.google.com/privacy). Imagery is for viewing only: do not trace or digitize from it. Do not distribute this plugin in China, Crimea, Cuba, Donetsk, Iran, Luhansk, North Korea, Syria or Vietnam.", "B+ CAD Tools setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                    if (r != DialogResult.OK) return 1;
                }
                Log("Install started. Installer " + Assembly.GetExecutingAssembly().GetName().Version + ", OS " + Environment.OSVersion);
                InstallLogic.RemoveLegacy(InstallLogic.LegacyTarget(), Log);
                try { Registry.CurrentUser.DeleteSubKeyTree(LegacyUninstKey, false); } catch { }
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("bundle.zip"))
                {
                    if (s == null) throw new InvalidOperationException("The installer is damaged (no package inside).");
                    InstallLogic.Install(s, target, Log);
                }
                using (var k = Registry.CurrentUser.CreateSubKey(UninstKey))
                {
                    string exe = Assembly.GetExecutingAssembly().Location;
                    k.SetValue("DisplayName", "B+ CAD Tools (experimental)");
                    k.SetValue("DisplayVersion", "0.3.2");
                    k.SetValue("Publisher", "BPlus Software");
                    k.SetValue("InstallLocation", target);
                    k.SetValue("UninstallString", "\"" + exe + "\" /U");
                    k.SetValue("NoModify", 1); k.SetValue("NoRepair", 1);
                }
                Log("Install finished OK.");
                if (!silent) MessageBox.Show("Installed.\n\nRestart AutoCAD or Civil 3D. You will see a B+ CAD Tools tab. Type BPCHECK to check the install.\nInstall log: " + LogPath, "B+ CAD Tools", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
