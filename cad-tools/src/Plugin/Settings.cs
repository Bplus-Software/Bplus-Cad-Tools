using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using BPlus.LiveMap.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BPlus.LiveMap
{
    static class UpdateCheck
    {
        public static string Pending = "";
        static readonly HttpClient Http = Make();
        static HttpClient Make()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("BPlusCadTools/" + App.Version);
            h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return h;
        }
        public static string Current { get { int d = App.Version.IndexOf('-'); return d > 0 ? App.Version.Substring(0, d) : App.Version; } }

        public static async Task<UpdateInfo> FetchAsync()
        {
            string json = await Http.GetStringAsync(Updater.ReleasesUrl).ConfigureAwait(false);
            return Updater.Newest(json);
        }

        // Quiet check at startup, at most once a day. Never downloads anything; it only leaves a note for the command line.
        public static void StartupCheck()
        {
            if (!Config.AutoUpdate) return;
            DateTime last; if (DateTime.TryParse(Config.LastUpdateCheck, null, System.Globalization.DateTimeStyles.RoundtripKind, out last) && (DateTime.UtcNow - last).TotalHours < 20) return;
            Task.Run(async () =>
            {
                try
                {
                    var u = await FetchAsync().ConfigureAwait(false);
                    Config.LastUpdateCheck = DateTime.UtcNow.ToString("o");
                    if (Updater.IsNewer(u, Current)) { Pending = "Update available: B+ CAD Tools " + u.Version + " (you have " + Current + "). Type BPUPDATE to install it."; AcApp.Idle += OnIdle; }
                }
                catch (Exception e) { Log.Write("Update check: " + e.Message); }
            });
        }
        static void OnIdle(object s, EventArgs e)
        {
            AcApp.Idle -= OnIdle;
            try { var d = AcApp.DocumentManager.MdiActiveDocument; if (d != null && Pending.Length > 0) d.Editor.WriteMessage("\n[B+] " + Pending + "\n"); } catch { }
        }

        // Unsaved drawings (DBMOD) and the saved drawings to open again. Needs the Session flag to switch documents.
        static bool CheckDrawings(out List<string> unsaved, out List<string> saved)
        {
            unsaved = new List<string>(); saved = new List<string>();
            var dm = AcApp.DocumentManager; var cur = dm.MdiActiveDocument;
            var docs = new List<Document>(); foreach (Document d in dm) docs.Add(d);
            foreach (var d in docs)
            {
                string name = d.Name; bool known = false; long mod = 0;
                try { if (dm.MdiActiveDocument != d) dm.MdiActiveDocument = d; if (dm.MdiActiveDocument == d) { mod = Convert.ToInt64(AcApp.GetSystemVariable("DBMOD")); known = true; } } catch (Exception e) { Log.Write("DBMOD check: " + e.Message); }
                bool onDisk = Path.IsPathRooted(name) && File.Exists(name);
                if (!known) unsaved.Add(Path.GetFileName(name) + " (could not check: save it to be safe)");
                else if (mod != 0) unsaved.Add(Path.GetFileName(name));
                if (onDisk && name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) saved.Add(name);
            }
            try { if (cur != null && dm.MdiActiveDocument != cur) dm.MdiActiveDocument = cur; } catch { }
            return unsaved.Count == 0;
        }

        public static void Manual()
        {
            UpdateInfo u;
            try { u = Task.Run(FetchAsync).GetAwaiter().GetResult(); Config.LastUpdateCheck = DateTime.UtcNow.ToString("o"); }
            catch (Exception e) { Log.Write("Update check: " + e); MessageBox.Show("Could not check for updates (" + Short(e) + "). Check the internet connection.", "B+ CAD Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (u == null) { MessageBox.Show("No release was found on GitHub.", "B+ CAD Tools", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!Updater.IsNewer(u, Current)) { MessageBox.Show("You have the newest version (" + Current + ").", "B+ CAD Tools", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var r = MessageBox.Show("Version " + u.Version + (u.Prerelease ? " (experimental pre-release)" : "") + " is available. You have " + Current + ".\n\nDownload it now? The installer's SHA-256 is checked against the release's SHA256SUMS.txt before it starts. You must close AutoCAD and Civil 3D to finish the update.\n\n" + u.Page, "B+ CAD Tools update", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            string dir = Path.Combine(Path.GetTempPath(), "BPlusCadTools"); Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, "BPlusCadTools-Setup-" + u.Version + ".exe");
            try
            {
                string sums = Task.Run(() => Http.GetStringAsync(u.SumsUrl)).GetAwaiter().GetResult();
                string want = Updater.HashFor(sums, Updater.InstallerAsset);
                if (want == null) throw new InvalidOperationException("The release has no checksum for the installer.");
                Task.Run(async () => { using (var resp = await Http.GetAsync(u.InstallerUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false)) { resp.EnsureSuccessStatusCode(); using (var fs = File.Create(exe)) await resp.Content.CopyToAsync(fs).ConfigureAwait(false); } }).GetAwaiter().GetResult();
                string got; using (var fs = File.OpenRead(exe)) got = Updater.Sha256Hex(fs);
                if (!string.Equals(got, want, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(exe); } catch { } throw new InvalidOperationException("The downloaded file does not match its SHA-256. It was deleted."); }
            }
            catch (Exception e) { Log.Write("Update download: " + e); MessageBox.Show("Update failed: " + Short(e), "B+ CAD Tools", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            Log.Write("Update " + u.Version + " downloaded and verified: " + exe);
            List<string> unsaved, saved;
            if (!CheckDrawings(out unsaved, out saved))
            {
                MessageBox.Show("The update was NOT started: these drawings have unsaved changes.\n\n  " + string.Join("\n  ", unsaved) + "\n\nSave them (QSAVE), then run BPUPDATE again. The update closes AutoCAD, and unsaved work would be lost.", "B+ CAD Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log.Write("Update refused, unsaved: " + string.Join("; ", unsaved)); return;
            }
            var go = MessageBox.Show("Version " + u.Version + " is downloaded and its SHA-256 matches.\n\nInstall now? AutoCAD will close the normal way: it asks you to save any unsaved drawing, nothing is forced. The installer then finishes by itself and offers to open AutoCAD again.\n\nWindows SmartScreen may warn because the installer is not code-signed.", "B+ CAD Tools update", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (go != DialogResult.Yes) { Log.Write("Update installer kept for later: " + exe); return; }
            if (!CheckDrawings(out unsaved, out saved)) { MessageBox.Show("The update was NOT started: a drawing was changed. Unsaved: " + string.Join(", ", unsaved) + ".\nSave it (QSAVE), then run BPUPDATE again.", "B+ CAD Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string args = "/CONFIRMED";
            try
            {
                var lines = new List<string>(); try { lines.Add(Process.GetCurrentProcess().MainModule.FileName); } catch { lines.Add(""); }
                lines.AddRange(saved); string lf = Path.Combine(dir, "reopen.txt"); File.WriteAllLines(lf, lines.ToArray()); args += " /DRAWINGS \"" + lf + "\"";
                Log.Write("Reopen list written with " + saved.Count + " drawing(s).");
            }
            catch (Exception e) { Log.Write("Reopen list failed: " + e.Message); }
            try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true }); } catch (Exception e) { MessageBox.Show("Could not start the installer: " + e.Message + "\nIt is saved at " + exe, "B+ CAD Tools", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        static string Short(Exception e) { while (e.InnerException != null) e = e.InnerException; return e.Message; }
    }

    static class GeneralSettings
    {
        static readonly string[] Orders = { "PENZD", "PNEZD", "PENZ", "PNEZ", "PEN" };
        public static void Show()
        {
            using (var f = Theme.Base("B+ CAD Tools - Settings", 520, 440))
            {
                f.Controls.Add(Theme.Lbl("Version " + App.Version + ". Updates come from GitHub: Bplus-Software/Bplus-Cad-Tools.", 16, 14, 490, true));
                var auto = new CheckBox { Left = 16, Top = 44, Width = 480, Text = "Check for updates when AutoCAD starts (at most once a day)", Checked = Config.AutoUpdate, ForeColor = Theme.Text, BackColor = Color.Transparent };
                f.Controls.Add(Theme.Lbl("Point file order (import and export default)", 16, 80, 300));
                var ord = new ComboBox { Left = 330, Top = 77, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat }; Theme.Style(ord); ord.Items.AddRange(Orders); ord.SelectedItem = Config.PointOrder;
                f.Controls.Add(Theme.Lbl("Decimals in exports", 16, 114, 300));
                var dec = new NumericUpDown { Left = 330, Top = 111, Width = 60, Minimum = 0, Maximum = 8, Value = Config.Decimals }; Theme.Style(dec);
                f.Controls.Add(Theme.Lbl("Default text height for point labels (drawing units)", 16, 148, 310));
                var th = new TextBox { Left = 330, Top = 145, Width = 80, BorderStyle = BorderStyle.FixedSingle, Text = Config.TextHeight.ToString(System.Globalization.CultureInfo.InvariantCulture) }; Theme.Style(th);
                f.Controls.Add(Theme.Lbl("Lengths are shown in m and areas in m2.", 16, 180, 480, true));
                var codes = Theme.Btn("Edit point codes...", 16, 214, 160); var map = Theme.Btn("Live Map settings and Google key...", 184, 214, 250);
                var upd = Theme.Btn("Check for updates now", 16, 258, 180, true); var rel = Theme.Btn("Open releases page", 204, 258, 160); var logb = Theme.Btn("Open log folder", 372, 258, 130);
                var info = Theme.Lbl("Update: the installer is downloaded, its SHA-256 is checked, and you confirm before it runs.", 16, 296, 490, true); info.Height = 36;
                var ok = Theme.Btn("Save", 324, 396, 85, true); var cancel = Theme.Btn("Cancel", 417, 396, 85);
                codes.Click += (s, e) => { var t = SurveyUi.Codes(SurveyCad.RulesText()); if (t != null) SurveyCad.SaveRulesText(t); };
                map.Click += (s, e) => SettingsDialog.Show();
                upd.Click += (s, e) => { try { var dd = AcApp.DocumentManager.MdiActiveDocument; f.DialogResult = DialogResult.Cancel; if (dd != null) dd.SendStringToExecute("BPUPDATE ", true, false, false); } catch { } };
                rel.Click += (s, e) => { try { Process.Start(new ProcessStartInfo("https://github.com/Bplus-Software/Bplus-Cad-Tools/releases") { UseShellExecute = true }); } catch { } };
                logb.Click += (s, e) => { try { Process.Start(new ProcessStartInfo(Paths.Dir) { UseShellExecute = true }); } catch { } };
                ok.Click += (s, e) =>
                {
                    double h; if (!double.TryParse(th.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out h) || h <= 0) { MessageBox.Show(f, "Text height must be a number above zero.", "B+ CAD Tools"); return; }
                    Config.AutoUpdate = auto.Checked; Config.PointOrder = (string)ord.SelectedItem; Config.Decimals = (int)dec.Value; Config.TextHeight = h; f.DialogResult = DialogResult.OK;
                };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { auto, ord, dec, th, codes, map, upd, rel, logb, info, ok, cancel }); f.CancelButton = cancel;
                f.ShowDialog();
            }
        }
    }
}
