using System;
using System.IO;
using System.IO.Compression;
using System.Xml;

namespace BPlus.LiveMap.Setup
{
    public sealed class LockedException : Exception { public LockedException(string m) : base(m) { } }

    public static class InstallLogic
    {
        public const string BundleName = "BPlusCadTools.bundle", LegacyBundleName = "BPlusLiveMap.bundle";
        public static string DefaultTarget() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "ApplicationPlugins", BundleName); }

        public static string LegacyTarget() { return Path.Combine(Path.GetDirectoryName(DefaultTarget()), LegacyBundleName); }

        // Version 0.1.0 to 0.3.0 used the bundle name BPlusLiveMap.bundle. Two bundles would load the same commands twice, so the old one is removed.
        // Moving the folder first fails cleanly (nothing is lost) when AutoCAD still has the old DLL open.
        public static bool RemoveLegacy(string legacy, Action<string> log)
        {
            if (!Directory.Exists(legacy)) return false;
            string gone = legacy + ".removed";
            if (Directory.Exists(gone)) { try { Directory.Delete(gone, true); } catch { } }
            try { Directory.Move(legacy, gone); }
            catch (Exception e) { throw new InvalidOperationException("The old BPlus Live Map install is in use. Close AutoCAD and Civil 3D, then run the installer again. (" + e.Message + ")"); }
            log("Moved old bundle " + legacy);
            try { Directory.Delete(gone, true); } catch (Exception e) { log("Could not delete " + gone + ": " + e.Message); }
            return true;
        }

        public static void Validate(string dir)
        {
            string pc = Path.Combine(dir, "PackageContents.xml");
            if (!File.Exists(pc)) throw new InvalidOperationException("PackageContents.xml is missing from the package.");
            var doc = new XmlDocument(); doc.Load(pc);
            var nodes = doc.SelectNodes("//ComponentEntry");
            if (nodes == null || nodes.Count == 0) throw new InvalidOperationException("PackageContents.xml has no components.");
            foreach (XmlNode n in nodes)
            {
                string m = n.Attributes["ModuleName"].Value.Replace("./", "").Replace('/', Path.DirectorySeparatorChar);
                if (!File.Exists(Path.Combine(dir, m))) throw new InvalidOperationException("Missing file in package: " + m);
            }
        }

        public static bool ForceDirSwapFail; // tests only: pretend the folder is locked

        static void Try(Action a) { try { a(); } catch { } }
        static void DeleteTree(string d) { if (!Directory.Exists(d)) return; for (int i = 0; i < 3; i++) { try { Directory.Delete(d, true); return; } catch { System.Threading.Thread.Sleep(300); } } }

        // Leftovers of earlier runs: folders ending .new/.old, and renamed locked files (*.old-bp).
        public static void CleanLeftovers(string target)
        {
            DeleteTree(target + ".new"); DeleteTree(target + ".old");
            if (Directory.Exists(target)) foreach (var f in Directory.GetFiles(target, "*.old-bp", SearchOption.AllDirectories)) Try(() => File.Delete(f));
        }

        // Extracts the zip to a temporary folder, checks it, then swaps it in. If AutoCAD holds the old folder open the swap is refused;
        // then every file is replaced in place (a locked DLL is renamed aside, which Windows allows), so the new version loads at the next start.
        public static void Install(Stream zip, string target, Action<string> log)
        {
            string parent = Path.GetDirectoryName(target);
            Directory.CreateDirectory(parent);
            string tmp = target + ".new", old = target + ".old";
            CleanLeftovers(target);
            if (Directory.Exists(tmp)) throw new IOException("Cannot clear the leftover folder " + tmp + ". Delete it by hand and run the installer again.");
            log("Unpacking to " + tmp);
            using (var za = new ZipArchive(zip, ZipArchiveMode.Read))
            {
                string root = Path.GetFullPath(tmp) + Path.DirectorySeparatorChar;
                foreach (var e in za.Entries)
                {
                    string dest = Path.GetFullPath(Path.Combine(tmp, e.FullName));
                    if (!dest.StartsWith(root, StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe path in package: " + e.FullName);
                    if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    using (var s = e.Open()) using (var f = File.Create(dest)) s.CopyTo(f);
                }
            }
            try { Validate(tmp); }
            catch { DeleteTree(tmp); throw; }
            bool hadOld = Directory.Exists(target);
            if (!hadOld) { Directory.Move(tmp, target); log("Installed to " + target); return; }
            Exception swapErr = null;
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (ForceDirSwapFail) throw new UnauthorizedAccessException("simulated lock");
                    Directory.Move(target, old);
                    try { Directory.Move(tmp, target); }
                    catch { if (!Directory.Exists(target) && Directory.Exists(old)) Directory.Move(old, target); throw; }
                    DeleteTree(old); log("Installed to " + target + " (folder swap)"); return;
                }
                catch (Exception e) { swapErr = e; log("Folder swap attempt " + (i + 1) + " failed: " + e.Message); System.Threading.Thread.Sleep(700); }
            }
            log("Falling back to file-by-file update because: " + swapErr.Message);
            string stamp = ".old-bp";
            foreach (var src in Directory.GetFiles(tmp, "*", SearchOption.AllDirectories))
            {
                string rel = src.Substring(tmp.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                string dest = Path.Combine(target, rel); Directory.CreateDirectory(Path.GetDirectoryName(dest));
                try { File.Copy(src, dest, true); continue; } catch (Exception) { }
                try
                {
                    if (File.Exists(dest + stamp)) Try(() => File.Delete(dest + stamp));
                    string aside = File.Exists(dest + stamp) ? dest + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + stamp : dest + stamp;
                    File.Move(dest, aside); File.Copy(src, dest, true); log("Replaced locked file " + rel + " (old one renamed aside)");
                }
                catch (Exception e2) { throw new LockedException("A file of the installed version is locked: " + rel + " (" + e2.Message + ")"); }
            }
            DeleteTree(tmp);
            log("Installed to " + target + " (file-by-file). The new version loads at the next AutoCAD start.");
        }

        public static bool Uninstall(string target, Action<string> log)
        {
            if (!Directory.Exists(target)) { log("Nothing to remove at " + target); return false; }
            Directory.Delete(target, true); log("Removed " + target); return true;
        }
    }
}
