using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BPlus.LiveMap.Core
{
    // Very small JSON reader: objects become Dictionary<string, object>, arrays List<object>, numbers double, plus string, bool, null.
    public static class MiniJson
    {
        public static object Parse(string s)
        {
            int i = 0; var v = Val(s, ref i); Ws(s, ref i);
            if (i != s.Length) throw new FormatException("Unexpected text after JSON.");
            return v;
        }
        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        static object Val(string s, ref int i)
        {
            Ws(s, ref i); if (i >= s.Length) throw new FormatException("Unexpected end.");
            char c = s[i];
            if (c == '{')
            {
                i++; var d = new Dictionary<string, object>(); Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i); string k = Str(s, ref i); Ws(s, ref i);
                    if (s[i] != ':') throw new FormatException("Expected ':'."); i++;
                    d[k] = Val(s, ref i); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("Expected ',' or '}'.");
                }
            }
            if (c == '[')
            {
                i++; var l = new List<object>(); Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Val(s, ref i)); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("Expected ',' or ']'.");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i; while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double n; if (i == st || !double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out n)) throw new FormatException("Bad value.");
            return n;
        }
        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("Expected string."); i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("Unterminated string.");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break; case 'r': sb.Append('\r'); break; case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break; case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
        }
    }

    public sealed class UpdateInfo { public string Tag = "", Version = "", Page = "", InstallerUrl = "", SumsUrl = ""; public bool Prerelease; }

    public static class Updater
    {
        public const string Prefix = "cadtools-v", InstallerAsset = "BPlusCadTools-Setup.exe", SumsAsset = "SHA256SUMS.txt";
        public const string ReleasesUrl = "https://api.github.com/repos/Bplus-Software/Bplus-Cad-Tools/releases?per_page=30";

        // "0.3.2-experimental" -> 0.3.2. Returns null when the text has no version.
        public static Version ParseVersion(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            int dash = s.IndexOf('-'); if (dash > 0) s = s.Substring(0, dash);
            var parts = s.Trim().Split('.'); if (parts.Length < 2 || parts.Length > 4) return null;
            var n = new int[4];
            for (int i = 0; i < parts.Length; i++) if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return null;
            return new Version(n[0], n[1], n[2], n[3]);
        }

        // Newest cadtools-v* release that has both the installer and the checksum file. Drafts are ignored.
        public static UpdateInfo Newest(string releasesJson)
        {
            var list = MiniJson.Parse(releasesJson) as List<object>; if (list == null) throw new FormatException("Unexpected answer from GitHub.");
            UpdateInfo best = null; Version bv = null;
            foreach (var o in list)
            {
                var r = o as Dictionary<string, object>; if (r == null) continue;
                string tag = r.ContainsKey("tag_name") ? r["tag_name"] as string : null; if (tag == null || !tag.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                if (r.ContainsKey("draft") && r["draft"] is bool && (bool)r["draft"]) continue;
                var v = ParseVersion(tag.Substring(Prefix.Length)); if (v == null) continue;
                var info = new UpdateInfo { Tag = tag, Version = v.ToString(3), Page = r.ContainsKey("html_url") ? r["html_url"] as string ?? "" : "", Prerelease = r.ContainsKey("prerelease") && r["prerelease"] is bool && (bool)r["prerelease"] };
                var assets = r.ContainsKey("assets") ? r["assets"] as List<object> : null; if (assets == null) continue;
                foreach (var a in assets)
                {
                    var ad = a as Dictionary<string, object>; if (ad == null) continue;
                    string name = ad.ContainsKey("name") ? ad["name"] as string : null, url = ad.ContainsKey("browser_download_url") ? ad["browser_download_url"] as string : null;
                    if (name == InstallerAsset) info.InstallerUrl = url ?? ""; else if (name == SumsAsset) info.SumsUrl = url ?? "";
                }
                if (info.InstallerUrl.Length == 0 || info.SumsUrl.Length == 0) continue;
                if (!info.InstallerUrl.StartsWith("https://github.com/Bplus-Software/", StringComparison.Ordinal) || !info.SumsUrl.StartsWith("https://github.com/Bplus-Software/", StringComparison.Ordinal)) continue;
                if (bv == null || v > bv) { best = info; bv = v; }
            }
            return best;
        }

        public static bool IsNewer(UpdateInfo u, string currentVersion)
        {
            var cur = ParseVersion(currentVersion); var nv = u == null ? null : ParseVersion(u.Version);
            return cur != null && nv != null && nv > cur;
        }

        // SHA256SUMS.txt lines look like "<64 hex>  <file name>" (a leading * before the name is allowed).
        public static string HashFor(string sumsText, string fileName)
        {
            foreach (var raw in (sumsText ?? "").Replace("\r", "").Split('\n'))
            {
                var l = raw.Trim(); int sp = l.IndexOf(' '); if (sp != 64) continue;
                string h = l.Substring(0, 64).ToLowerInvariant(), n = l.Substring(65).Trim().TrimStart('*').Trim();
                if (string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase) && IsHex(h)) return h;
            }
            return null;
        }
        static bool IsHex(string s) { foreach (char c in s) if (!Uri.IsHexDigit(c)) return false; return true; }

        public static string Sha256Hex(System.IO.Stream s)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create()) { var b = sha.ComputeHash(s); var sb = new StringBuilder(); foreach (var x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }
        }
    }
}
