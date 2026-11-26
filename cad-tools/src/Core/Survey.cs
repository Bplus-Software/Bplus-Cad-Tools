using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace BPlus.LiveMap.Core
{
    public sealed class SurveyPoint
    {
        public string No = ""; public double E, N, Z; public bool HasZ = true; public string Desc = ""; public string Category = "";
    }

    public sealed class ParseResult
    {
        public List<SurveyPoint> Points = new List<SurveyPoint>();
        public List<string> Problems = new List<string>();
        public bool HeaderSkipped; public int Blank;
    }

    public static class PointText
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Removes hidden characters (BOM, zero width, direction marks, control codes), turns no-break spaces into spaces and Arabic digits into 0-9.
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '\u00A0' || c == '\u2007' || c == '\u202F') { sb.Append(' '); continue; }
                if (c == '\uFEFF' || (c >= '\u200B' && c <= '\u200F') || (c >= '\u202A' && c <= '\u202E') || c == '\u2060' || c == '\u061C') continue;
                if (c == '\t') { sb.Append(c); continue; }
                if (char.IsControl(c)) continue;
                if (c >= '\u0660' && c <= '\u0669') { sb.Append((char)('0' + (c - '\u0660'))); continue; }
                if (c >= '\u06F0' && c <= '\u06F9') { sb.Append((char)('0' + (c - '\u06F0'))); continue; }
                if (c == '\u066B') { sb.Append('.'); continue; }
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        public static bool ValidOrder(string order)
        {
            if (string.IsNullOrEmpty(order)) return false;
            order = order.ToUpperInvariant();
            foreach (char c in order) if ("PENZD".IndexOf(c) < 0) return false;
            if (order.Distinct().Count() != order.Length) return false;
            return order.Contains('E') && order.Contains('N');
        }

        public static char DetectDelimiter(IEnumerable<string> lines)
        {
            var sample = lines.Select(Clean).Where(l => l.Length > 0).Take(20).ToList();
            if (sample.Count == 0) return ',';
            foreach (char d in new[] { '\t', ';', ',' })
            {
                var counts = sample.Select(l => l.Count(ch => ch == d)).ToList();
                if (counts.All(n => n > 0) && counts.Distinct().Count() <= 2) return d;
            }
            return ' ';
        }

        // Splits one line. Quotes are honoured for , ; and tab. The last wanted field keeps the rest of the line.
        public static string[] SplitFields(string line, char delim, int max)
        {
            var res = new List<string>();
            if (delim == ' ')
            {
                int i = 0; int n = line.Length;
                while (i < n)
                {
                    while (i < n && line[i] == ' ') i++;
                    if (i >= n) break;
                    if (res.Count == max - 1) { res.Add(line.Substring(i).Trim()); return res.ToArray(); }
                    int j = i; while (j < n && line[j] != ' ') j++;
                    res.Add(line.Substring(i, j - i)); i = j;
                }
                return res.ToArray();
            }
            var cur = new StringBuilder(); bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else q = false; } else cur.Append(c); continue; }
                if (c == '"' && cur.Length == 0) { q = true; continue; }
                if (c == delim && res.Count < max - 1) { res.Add(cur.ToString().Trim()); cur.Clear(); continue; }
                cur.Append(c);
            }
            res.Add(cur.ToString().Trim());
            return res.ToArray();
        }

        public static bool TryNum(string s, char delim, out double v)
        {
            s = Clean(s);
            if (delim != ',' && s.IndexOf(',') >= 0 && s.IndexOf('.') < 0) s = s.Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, Inv, out v) && !double.IsNaN(v) && !double.IsInfinity(v);
        }

        public static ParseResult Parse(IEnumerable<string> lines, string order, char? delimiter = null)
        {
            var all = lines.ToList();
            char d = delimiter ?? DetectDelimiter(all);
            var rows = new List<KeyValuePair<int, string[]>>();
            int ln = 0; var r = new ParseResult();
            order = (order ?? "PENZD").ToUpperInvariant();
            foreach (var raw in all)
            {
                ln++; string l = Clean(raw);
                if (l.Length == 0 || l.StartsWith("#") || l.StartsWith("//")) { r.Blank++; continue; }
                rows.Add(new KeyValuePair<int, string[]>(ln, SplitFields(l, d, order.Length)));
            }
            return ParseRows(rows, order, d, r);
        }

        public static ParseResult ParseRows(List<KeyValuePair<int, string[]>> rows, string order, char delim, ParseResult r = null)
        {
            r = r ?? new ParseResult();
            order = (order ?? "PENZD").ToUpperInvariant();
            if (!ValidOrder(order)) { r.Problems.Add("Column order must use P E N Z D (each once) and include E and N."); return r; }
            int iE = order.IndexOf('E'), iN = order.IndexOf('N'), iP = order.IndexOf('P'), iZ = order.IndexOf('Z'), iD = order.IndexOf('D');
            bool first = true;
            foreach (var row in rows)
            {
                var f = row.Value.Select(Clean).ToArray();
                if (f.All(x => x.Length == 0)) { r.Blank++; continue; }
                double e = 0, n = 0, z = 0;
                bool okE = f.Length > iE && TryNum(f[iE], delim, out e), okN = f.Length > iN && TryNum(f[iN], delim, out n);
                if (first)
                {
                    first = false;
                    if (!okE && !okN) { r.HeaderSkipped = true; continue; }
                }
                if (!okE || !okN) { r.Problems.Add("Line " + row.Key + ": Easting/Northing is missing or not a number."); continue; }
                var p = new SurveyPoint { E = e, N = n };
                if (iZ >= 0)
                {
                    if (f.Length > iZ && f[iZ].Length > 0)
                    {
                        if (TryNum(f[iZ], delim, out z)) p.Z = z; else { r.Problems.Add("Line " + row.Key + ": Elevation is not a number."); continue; }
                    }
                    else p.HasZ = false;
                }
                else p.HasZ = false;
                if (iP >= 0 && f.Length > iP) p.No = f[iP];
                if (iD >= 0 && f.Length > iD) p.Desc = f[iD];
                r.Points.Add(p);
            }
            return r;
        }

        public static string Num(double v, int decimals) { return v.ToString("F" + Math.Max(0, Math.Min(10, decimals)), Inv); }

        static string Field(string s, char delim)
        {
            s = Clean(s).Replace("\r", " ").Replace("\n", " ");
            if (delim == ' ' ) return s.Replace(' ', '_');
            if (s.IndexOf(delim) >= 0 || s.IndexOf('"') >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        // Clean export text: one line per point, CRLF, no hidden characters, no BOM added here.
        public static string Format(IEnumerable<SurveyPoint> pts, string order, char delim, int decimals, bool header = false)
        {
            order = order.ToUpperInvariant();
            var sb = new StringBuilder(); int seq = 0;
            if (header)
            {
                var names = new Dictionary<char, string> { { 'P', "Point" }, { 'E', "Easting" }, { 'N', "Northing" }, { 'Z', "Elevation" }, { 'D', "Description" } };
                sb.Append(string.Join(delim.ToString(), order.Select(c => names[c]))).Append("\r\n");
            }
            foreach (var p in pts)
            {
                seq++;
                var cols = new List<string>();
                foreach (char c in order)
                {
                    switch (c)
                    {
                        case 'P': cols.Add(Field(string.IsNullOrEmpty(p.No) ? seq.ToString(Inv) : p.No, delim)); break;
                        case 'E': cols.Add(Num(p.E, decimals)); break;
                        case 'N': cols.Add(Num(p.N, decimals)); break;
                        case 'Z': cols.Add(Num(p.Z, decimals)); break;
                        case 'D': cols.Add(Field(p.Desc, delim)); break;
                    }
                }
                sb.Append(string.Join(delim.ToString(), cols)).Append("\r\n");
            }
            return sb.ToString();
        }
    }

    public sealed class XlsxReader
    {
        static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        public static List<string[]> ReadFirstSheet(Stream s)
        {
            using (var za = new ZipArchive(s, ZipArchiveMode.Read))
            {
                var shared = new List<string>();
                var ss = za.GetEntry("xl/sharedStrings.xml");
                if (ss != null)
                    using (var st = ss.Open())
                        foreach (var si in XDocument.Load(st).Root.Elements(Ns + "si")) shared.Add(string.Concat(si.Descendants(Ns + "t").Select(t => t.Value)));
                var sheet = za.GetEntry("xl/worksheets/sheet1.xml") ?? za.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/") && e.FullName.EndsWith(".xml")).OrderBy(e => e.FullName, StringComparer.Ordinal).FirstOrDefault();
                if (sheet == null) throw new InvalidDataException("No worksheet found in this Excel file.");
                var rows = new List<string[]>();
                using (var st = sheet.Open())
                {
                    var data = XDocument.Load(st).Root.Element(Ns + "sheetData");
                    if (data == null) return rows;
                    foreach (var row in data.Elements(Ns + "row"))
                    {
                        var cells = new Dictionary<int, string>(); int max = -1; int auto = 0;
                        foreach (var c in row.Elements(Ns + "c"))
                        {
                            int col = ColIndex((string)c.Attribute("r"), auto); auto = col + 1;
                            string t = (string)c.Attribute("t"); string v = (string)c.Element(Ns + "v") ?? "";
                            if (t == "s") { int k; v = int.TryParse(v, out k) && k >= 0 && k < shared.Count ? shared[k] : ""; }
                            else if (t == "inlineStr") v = string.Concat(c.Descendants(Ns + "t").Select(x => x.Value));
                            cells[col] = v; if (col > max) max = col;
                        }
                        var arr = new string[max + 1];
                        for (int i = 0; i <= max; i++) arr[i] = cells.ContainsKey(i) ? cells[i] : "";
                        rows.Add(arr);
                    }
                }
                return rows;
            }
        }
        static int ColIndex(string r, int fallback)
        {
            if (string.IsNullOrEmpty(r)) return fallback;
            int n = 0, i = 0;
            while (i < r.Length && char.IsLetter(r[i])) { n = n * 26 + (char.ToUpperInvariant(r[i]) - 'A' + 1); i++; }
            return n == 0 ? fallback : n - 1;
        }
        public static ParseResult ParsePoints(Stream s, string order)
        {
            var rows = ReadFirstSheet(s);
            var list = new List<KeyValuePair<int, string[]>>(); int i = 0;
            foreach (var r in rows) { i++; list.Add(new KeyValuePair<int, string[]>(i, r)); }
            return PointText.ParseRows(list, order, '.');
        }
    }

    public sealed class CodeCategory { public string Code, Name; public List<string> Prefixes = new List<string>(); }

    public sealed class CodeRules
    {
        public List<CodeCategory> Cats = new List<CodeCategory>();
        public const string Uncoded = "UNCODED", Other = "OTHER";
        public const string DefaultText = "# One line per category:  PREFIX, PREFIX2 = Name\r\n# A point belongs to a category when the letters at the start of its description match a prefix.\r\n# Examples: MNH1, MNH 3, MNH-7 all match MNH.\r\nMNH = Manholes\r\nLP = Light poles\r\nTREE = Trees\r\nEG = Natural ground\r\nRD = Roads\r\n";

        public static CodeRules Parse(string text, List<string> problems = null)
        {
            var r = new CodeRules(); int ln = 0;
            foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                ln++; string l = PointText.Clean(raw);
                if (l.Length == 0 || l.StartsWith("#")) continue;
                int eq = l.IndexOf('=');
                if (eq <= 0 || eq == l.Length - 1) { if (problems != null) problems.Add("Line " + ln + ": use  PREFIX = Name"); continue; }
                var pre = l.Substring(0, eq).Split(',').Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).ToList();
                string name = l.Substring(eq + 1).Trim();
                if (pre.Count == 0 || name.Length == 0) { if (problems != null) problems.Add("Line " + ln + ": use  PREFIX = Name"); continue; }
                if (pre.Any(p => !p.All(ch => char.IsLetter(ch) || char.IsDigit(ch) || ch == '_'))) { if (problems != null) problems.Add("Line " + ln + ": prefixes may only have letters, digits and _"); continue; }
                var cat = r.Cats.FirstOrDefault(c => c.Code == pre[0]);
                if (cat == null) { cat = new CodeCategory { Code = pre[0], Name = name }; r.Cats.Add(cat); }
                foreach (var p in pre) if (!cat.Prefixes.Contains(p)) cat.Prefixes.Add(p);
            }
            return r;
        }
        public static CodeRules Default() { return Parse(DefaultText); }

        // Letters at the start of the description, upper case. "MNH-12" -> "MNH", "tree palm" -> "TREE".
        public static string LeadingWord(string desc)
        {
            desc = PointText.Clean(desc); int i = 0;
            while (i < desc.Length && (char.IsLetter(desc[i]) || desc[i] == '_')) i++;
            return desc.Substring(0, i).ToUpperInvariant();
        }
        public string Classify(string desc)
        {
            string w = LeadingWord(desc);
            if (w.Length == 0) return PointText.Clean(desc).Length == 0 ? Uncoded : Other;
            foreach (var c in Cats) if (c.Prefixes.Contains(w)) return c.Code;
            return Other;
        }
        public string NameOf(string code)
        {
            if (code == Uncoded) return "No code"; if (code == Other) return "Other codes";
            var c = Cats.FirstOrDefault(x => x.Code == code); return c == null ? code : c.Name;
        }
        public void Apply(IEnumerable<SurveyPoint> pts) { foreach (var p in pts) p.Category = Classify(p.Desc); }
    }

    public sealed class QaIssue { public string Kind = "", PointNo = "", Text = ""; public double E, N; }
    public sealed class QaOptions { public double CoincidentTol = 0.005, SpikeThreshold = 1.0, FarDist = 200; }

    public static class SurveyQa
    {
        public static List<QaIssue> Run(IList<SurveyPoint> pts, QaOptions o)
        {
            var res = new List<QaIssue>();
            string Id(SurveyPoint p, int i) { return string.IsNullOrEmpty(p.No) ? "#" + (i + 1) : p.No; }
            // 1 repeated point numbers
            foreach (var g in pts.Select((p, i) => new { p, i }).Where(x => !string.IsNullOrEmpty(x.p.No)).GroupBy(x => x.p.No).Where(g => g.Count() > 1))
                res.Add(new QaIssue { Kind = "Repeated number", PointNo = g.Key, Text = "Point number used " + g.Count() + " times.", E = g.First().p.E, N = g.First().p.N });
            // 2 coincident points
            var order = Enumerable.Range(0, pts.Count).OrderBy(i => pts[i].E).ToList();
            for (int a = 0; a < order.Count; a++)
                for (int b = a + 1; b < order.Count && pts[order[b]].E - pts[order[a]].E <= o.CoincidentTol; b++)
                {
                    var p = pts[order[a]]; var q = pts[order[b]];
                    double dx = p.E - q.E, dy = p.N - q.N; if (Math.Sqrt(dx * dx + dy * dy) > o.CoincidentTol) continue;
                    double dz = p.Z - q.Z;
                    res.Add(new QaIssue { Kind = Math.Abs(dz) < 0.001 ? "Duplicate point" : "Same XY, different Z", PointNo = Id(p, order[a]) + " / " + Id(q, order[b]), Text = Math.Abs(dz) < 0.001 ? "Two points at the same place." : "Same place, elevation differs by " + Math.Abs(dz).ToString("F3", CultureInfo.InvariantCulture) + ".", E = p.E, N = p.N });
                }
            // 3 missing code, 4 zero elevation
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                if (PointText.Clean(p.Desc).Length == 0) res.Add(new QaIssue { Kind = "No code", PointNo = Id(p, i), Text = "Point has no description.", E = p.E, N = p.N });
                if (!p.HasZ || p.Z == 0) res.Add(new QaIssue { Kind = "No elevation", PointNo = Id(p, i), Text = "Elevation is missing or exactly zero.", E = p.E, N = p.N });
            }
            // 5 spikes and 6 isolated points, inside each category (needs enough points)
            foreach (var grp in pts.Select((p, i) => new { p, i }).Where(x => x.p.HasZ && x.p.Z != 0).GroupBy(x => x.p.Category))
            {
                var list = grp.ToList(); if (list.Count < 5 || list.Count > 20000) continue;
                double minE = list.Min(x => x.p.E), maxE = list.Max(x => x.p.E), minN = list.Min(x => x.p.N), maxN = list.Max(x => x.p.N);
                double cell = Math.Max(1.0, Math.Max(maxE - minE, maxN - minN) * 2 / Math.Sqrt(list.Count));
                var grid = new Dictionary<long, List<int>>();
                Func<double, int> cx = e => (int)Math.Floor((e - minE) / cell); Func<double, int> cy = n => (int)Math.Floor((n - minN) / cell);
                Func<int, int, long> key = (x, y) => ((long)x << 32) ^ (uint)y;
                for (int k = 0; k < list.Count; k++) { long kk = key(cx(list[k].p.E), cy(list[k].p.N)); if (!grid.ContainsKey(kk)) grid[kk] = new List<int>(); grid[kk].Add(k); }
                for (int k = 0; k < list.Count; k++)
                {
                    var p = list[k].p; int gx = cx(p.E), gy = cy(p.N); var cand = new List<int>();
                    for (int ring = 1; ring <= 4 && cand.Count < 5; ring++)
                    {
                        cand.Clear();
                        for (int x = gx - ring; x <= gx + ring; x++) for (int y = gy - ring; y <= gy + ring; y++)
                            { List<int> l; if (grid.TryGetValue(key(x, y), out l)) cand.AddRange(l.Where(j => j != k)); }
                    }
                    var near = cand.Select(j => new { j, d = Math.Sqrt(Math.Pow(list[j].p.E - p.E, 2) + Math.Pow(list[j].p.N - p.N, 2)) }).OrderBy(t => t.d).Take(4).ToList();
                    if (near.Count == 0 || near[0].d > o.FarDist)
                        res.Add(new QaIssue { Kind = "Far from others", PointNo = Id(p, list[k].i), Text = near.Count == 0 ? "No other point of the same code nearby." : "Nearest point of the same code is " + near[0].d.ToString("F1", CultureInfo.InvariantCulture) + " away.", E = p.E, N = p.N });
                    if (near.Count >= 3)
                    {
                        var zs = near.Select(t => list[t.j].p.Z).OrderBy(z => z).ToList();
                        double med = zs.Count % 2 == 1 ? zs[zs.Count / 2] : (zs[zs.Count / 2 - 1] + zs[zs.Count / 2]) / 2;
                        if (Math.Abs(p.Z - med) > o.SpikeThreshold)
                            res.Add(new QaIssue { Kind = "Elevation spike", PointNo = Id(p, list[k].i), Text = "Elevation " + p.Z.ToString("F3", CultureInfo.InvariantCulture) + " differs by " + Math.Abs(p.Z - med).ToString("F3", CultureInfo.InvariantCulture) + " from its neighbours.", E = p.E, N = p.N });
                    }
                }
            }
            return res;
        }
    }

    public static class Geo2D
    {
        public const double FeddanM2 = 4200.833; // Egyptian feddan; 24 qirat, 1 qirat = 24 sahm
        public static double Distance(double e1, double n1, double e2, double n2) { double dx = e2 - e1, dy = n2 - n1; return Math.Sqrt(dx * dx + dy * dy); }
        public static double Azimuth(double e1, double n1, double e2, double n2)
        {
            double a = Math.Atan2(e2 - e1, n2 - n1) * 180 / Math.PI; if (a < 0) a += 360; return a >= 360 ? 0 : a;
        }
        public static string Dms(double deg)
        {
            double t = Math.Round(deg * 3600); int d = (int)(t / 3600); t -= d * 3600; int m = (int)(t / 60); int s = (int)(t - m * 60);
            return d + "\u00B0" + m.ToString("00") + "'" + s.ToString("00") + "\"";
        }
        public static string QuadrantBearing(double az)
        {
            string ns, ew; double a;
            if (az <= 90) { ns = "N"; ew = "E"; a = az; } else if (az <= 180) { ns = "S"; ew = "E"; a = 180 - az; } else if (az <= 270) { ns = "S"; ew = "W"; a = az - 180; } else { ns = "N"; ew = "W"; a = 360 - az; }
            return ns + " " + Dms(a) + " " + ew;
        }
        public static double PolygonArea(IList<double> e, IList<double> n)
        {
            double s = 0; int c = e.Count; if (c < 3) return 0;
            for (int i = 0; i < c; i++) { int j = (i + 1) % c; s += e[i] * n[j] - e[j] * n[i]; }
            return Math.Abs(s) / 2;
        }
        public static double Perimeter(IList<double> e, IList<double> n, bool closed)
        {
            double s = 0; int c = e.Count;
            for (int i = 0; i < c - 1; i++) s += Distance(e[i], n[i], e[i + 1], n[i + 1]);
            if (closed && c > 2) s += Distance(e[c - 1], n[c - 1], e[0], n[0]);
            return s;
        }
        public static string Feddan(double m2)
        {
            double f = m2 / FeddanM2; int fed = (int)Math.Floor(f + 1e-9); double rem = (f - fed) * 24; int q = (int)Math.Floor(rem + 1e-9); int s = (int)Math.Round((rem - q) * 24);
            if (s >= 24) { s = 0; q++; } if (q >= 24) { q = 0; fed++; }
            return fed + " feddan " + q + " qirat " + s + " sahm";
        }
    }
}

namespace BPlus.LiveMap.Core
{
    public sealed class ConnectLine { public string Key = ""; public string Code = ""; public bool Closed; public List<SurveyPoint> Pts = new List<SurveyPoint>(); }

    public static class Connect
    {
        // Groups points into lines. By code: "RD1" and "RD2" are different lines when splitByNumber is true.
        // Order inside a line: point number when numeric, otherwise the order given.
        public static List<ConnectLine> Build(IList<SurveyPoint> pts, bool byCode, bool splitByNumber, IEnumerable<string> onlyCodes = null)
        {
            var only = onlyCodes == null ? null : new HashSet<string>(onlyCodes);
            var map = new Dictionary<string, ConnectLine>(); var order = new List<string>();
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i]; string d = PointText.Clean(p.Desc); string w = CodeRules.LeadingWord(d);
                string key = "ALL";
                if (byCode)
                {
                    if (w.Length == 0) continue;
                    if (only != null && !only.Contains(w)) continue;
                    key = w;
                    if (splitByNumber) { int k = w.Length; string num = ""; while (k < d.Length && d[k] == ' ' && num.Length == 0) k++; while (k < d.Length && char.IsDigit(d[k])) num += d[k++]; key = w + num; }
                }
                ConnectLine l; if (!map.TryGetValue(key, out l)) { l = new ConnectLine { Key = key, Code = byCode ? w : "ALL" }; map[key] = l; order.Add(key); }
                l.Pts.Add(p);
                var up = (" " + d.ToUpperInvariant() + " ");
                if (up.Contains(" CLOSE ") || up.Contains(" CLS ")) l.Closed = true;
            }
            var res = new List<ConnectLine>();
            foreach (var k in order)
            {
                var l = map[k];
                var idx = l.Pts.Select((p, i) => new { p, i }).ToList();
                double dummy;
                bool allNum = idx.All(x => double.TryParse(x.p.No, NumberStyles.Float, CultureInfo.InvariantCulture, out dummy));
                if (allNum) l.Pts = idx.OrderBy(x => double.Parse(x.p.No, CultureInfo.InvariantCulture)).ThenBy(x => x.i).Select(x => x.p).ToList();
                if (l.Pts.Count >= 2) res.Add(l);
            }
            return res;
        }
    }
}
