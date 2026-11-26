using System;
using System.IO;
using System.IO.Compression;
using System.Xml;

namespace BPlus.LiveMap.Setup
{
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

        // Extracts the zip to a temporary folder, checks it, then swaps it in. The old copy is restored if anything fails.
        public static void Install(Stream zip, string target, Action<string> log)
        {
            string parent = Path.GetDirectoryName(target);
            Directory.CreateDirectory(parent);
            string tmp = target + ".new", old = target + ".old";
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            if (Directory.Exists(old)) Directory.Delete(old, true);
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
            catch { Directory.Delete(tmp, true); throw; }
            bool hadOld = Directory.Exists(target);
            try
            {
                if (hadOld) { log("Replacing existing install"); Directory.Move(target, old); }
                Directory.Move(tmp, target);
                if (hadOld) { try { Directory.Delete(old, true); } catch (Exception e) { log("Could not delete old copy: " + e.Message); } }
                log("Installed to " + target);
            }
            catch
            {
                if (hadOld && !Directory.Exists(target) && Directory.Exists(old)) Directory.Move(old, target);
                throw;
            }
        }

        public static bool Uninstall(string target, Action<string> log)
        {
            if (!Directory.Exists(target)) { log("Nothing to remove at " + target); return false; }
            Directory.Delete(target, true); log("Removed " + target); return true;
        }
    }
}
