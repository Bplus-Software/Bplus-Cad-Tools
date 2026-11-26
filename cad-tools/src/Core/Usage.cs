using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace BPlus.LiveMap.Core
{
    /// <summary>Tiny key=value text file (no JSON library, so nothing can clash with AutoCAD's own assemblies).</summary>
    public sealed class KvFile
    {
        readonly string _path; readonly Dictionary<string, string> _d = new Dictionary<string, string>();
        public KvFile(string path)
        {
            _path = path;
            try { if (File.Exists(path)) foreach (var line in File.ReadAllLines(path)) { int i = line.IndexOf('='); if (i > 0) _d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim(); } } catch { }
        }
        public string Get(string k, string def = "") { string v; return _d.TryGetValue(k, out v) ? v : def; }
        public int GetInt(string k, int def) { int v; return int.TryParse(Get(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def; }
        public void Set(string k, string v) { _d[k] = v; }
        public void Set(string k, int v) { _d[k] = v.ToString(CultureInfo.InvariantCulture); }
        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var lines = new List<string>(); foreach (var kv in _d) lines.Add(kv.Key + "=" + kv.Value);
            File.WriteAllLines(_path, lines);
        }
    }

    /// <summary>Counts billable tile requests per calendar month (counts only, no map content).</summary>
    public sealed class UsageCounter
    {
        readonly KvFile _f; readonly Func<DateTime> _now;
        public UsageCounter(string path, Func<DateTime> now = null) { _f = new KvFile(path); _now = now ?? (() => DateTime.UtcNow); }
        string Month() { return _now().ToString("yyyy-MM", CultureInfo.InvariantCulture); }
        public int ThisMonth { get { return _f.Get("month") == Month() ? _f.GetInt("count", 0) : 0; } }
        public void Add(int n)
        {
            if (_f.Get("month") != Month()) { _f.Set("month", Month()); _f.Set("count", 0); }
            _f.Set("count", _f.GetInt("count", 0) + n);
            try { _f.Save(); } catch { }
        }
    }
}
