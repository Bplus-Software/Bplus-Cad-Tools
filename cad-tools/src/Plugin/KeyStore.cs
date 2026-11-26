using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BPlus.LiveMap
{
    /// <summary>The Google API key lives only on this PC, encrypted for the current Windows user (DPAPI). It is never logged.</summary>
    static class KeyStore
    {
        static string _cached;
        public static bool HasKey { get { return File.Exists(Paths.KeyFile); } }

        public static string Get()
        {
            if (_cached != null) return _cached;
            try
            {
                if (!File.Exists(Paths.KeyFile)) return "";
                byte[] raw = ProtectedData.Unprotect(File.ReadAllBytes(Paths.KeyFile), Encoding.UTF8.GetBytes("BPlus.LiveMap"), DataProtectionScope.CurrentUser);
                _cached = Encoding.UTF8.GetString(raw);
                return _cached;
            }
            catch (Exception e) { Log.Write("Key could not be read: " + e.GetType().Name); return ""; }
        }

        public static void Set(string key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) { try { File.Delete(Paths.KeyFile); } catch { } _cached = null; return; }
            byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes("BPlus.LiveMap"), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Paths.KeyFile, enc);
            _cached = key;
        }
    }
}
