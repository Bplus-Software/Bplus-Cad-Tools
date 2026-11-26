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
    // ---------- KML / KMZ (longitude, latitude, altitude; the plugin converts to and from drawing coordinates) ----------
    public sealed class KmlFeature
    {
        public string Name = "", Desc = "", Kind = "Point"; // Point, Line, Polygon
        public List<double[]> Coords = new List<double[]>(); // lon, lat, alt
    }

    public static class Kml
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static List<KmlFeature> Read(Stream s, bool isKmz)
        {
            if (isKmz)
            {
                using (var z = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    var e = z.Entries.FirstOrDefault(x => x.FullName.Equals("doc.kml", StringComparison.OrdinalIgnoreCase)) ?? z.Entries.FirstOrDefault(x => x.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase));
                    if (e == null) throw new InvalidDataException("The KMZ has no .kml inside.");
                    using (var es = e.Open()) return ReadXml(es);
                }
            }
            return ReadXml(s);
        }

        static List<KmlFeature> ReadXml(Stream s)
        {
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
            XDocument d; using (var r = System.Xml.XmlReader.Create(s, settings)) d = XDocument.Load(r);
            var res = new List<KmlFeature>();
            foreach (var pm in d.Descendants().Where(x => x.Name.LocalName == "Placemark"))
            {
                string name = Child(pm, "name"), desc = Child(pm, "description");
                foreach (var g in pm.Descendants().Where(x => x.Name.LocalName == "Point" || x.Name.LocalName == "LineString" || x.Name.LocalName == "Polygon"))
                {
                    var f = new KmlFeature { Name = name, Desc = desc, Kind = g.Name.LocalName == "LineString" ? "Line" : g.Name.LocalName };
                    XElement coordsEl = g.Name.LocalName == "Polygon"
                        ? g.Descendants().Where(x => x.Name.LocalName == "outerBoundaryIs").SelectMany(x => x.Descendants()).FirstOrDefault(x => x.Name.LocalName == "coordinates")
                        : g.Descendants().FirstOrDefault(x => x.Name.LocalName == "coordinates");
                    if (coordsEl == null) continue;
                    foreach (var tok in coordsEl.Value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var p = tok.Split(','); double lo, la, al = 0;
                        if (p.Length < 2 || !double.TryParse(p[0], NumberStyles.Float, Inv, out lo) || !double.TryParse(p[1], NumberStyles.Float, Inv, out la)) continue;
                        if (p.Length > 2) double.TryParse(p[2], NumberStyles.Float, Inv, out al);
                        if (Math.Abs(lo) > 180 || Math.Abs(la) > 90) continue;
                        f.Coords.Add(new[] { lo, la, al });
                    }
                    if (f.Kind == "Polygon" && f.Coords.Count > 1) { var a = f.Coords[0]; var b = f.Coords[f.Coords.Count - 1]; if (a[0] == b[0] && a[1] == b[1]) f.Coords.RemoveAt(f.Coords.Count - 1); }
                    if (f.Coords.Count > 0) res.Add(f);
                }
            }
            return res;
        }
        static string Child(XElement e, string n) { var c = e.Elements().FirstOrDefault(x => x.Name.LocalName == n); return c == null ? "" : c.Value.Trim(); }

        public static string Write(IEnumerable<KmlFeature> feats, string docName)
        {
            XNamespace ns = "http://www.opengis.net/kml/2.2";
            var doc = new XElement(ns + "Document", new XElement(ns + "name", docName));
            foreach (var f in feats)
            {
                string coords = string.Join(" ", f.Coords.Select(c => c[0].ToString("0.0000000", Inv) + "," + c[1].ToString("0.0000000", Inv) + "," + c[2].ToString("0.###", Inv)));
                XElement geom;
                if (f.Kind == "Point") geom = new XElement(ns + "Point", new XElement(ns + "coordinates", coords));
                else if (f.Kind == "Line") geom = new XElement(ns + "LineString", new XElement(ns + "tessellate", 1), new XElement(ns + "coordinates", coords));
                else
                {
                    var c = f.Coords.Count > 0 ? coords + " " + f.Coords[0][0].ToString("0.0000000", Inv) + "," + f.Coords[0][1].ToString("0.0000000", Inv) + "," + f.Coords[0][2].ToString("0.###", Inv) : coords;
                    geom = new XElement(ns + "Polygon", new XElement(ns + "outerBoundaryIs", new XElement(ns + "LinearRing", new XElement(ns + "coordinates", c))));
                }
                var pm = new XElement(ns + "Placemark", new XElement(ns + "name", f.Name));
                if (!string.IsNullOrEmpty(f.Desc)) pm.Add(new XElement(ns + "description", f.Desc));
                pm.Add(geom); doc.Add(pm);
            }
            return new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement(ns + "kml", doc)).Declaration + "\n" + new XElement(ns + "kml", doc).ToString();
        }

        public static void WriteKmz(Stream s, string kml)
        {
            using (var z = new ZipArchive(s, ZipArchiveMode.Create, true))
            {
                var e = z.CreateEntry("doc.kml"); using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(kml);
            }
        }
    }

    // ---------- Leica GSI (8 or 16 digit words). Built from the public format description; UNVERIFIED against a real instrument file. ----------
    public static class Gsi
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static ParseResult Parse(IEnumerable<string> lines)
        {
            var r = new ParseResult(); int ln = 0;
            foreach (var raw in lines)
            {
                ln++; var line = (raw ?? "").Trim(); if (line.Length == 0) { r.Blank++; continue; }
                if (line.StartsWith("*")) line = line.Substring(1);
                var p = new SurveyPoint { HasZ = false }; bool any = false, ok = true;
                foreach (var w in line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (w.Length < 8) continue;
                    string wi = w.Substring(0, 2); string info = w.Substring(2, 4); string data = w.Substring(6);
                    char sign = data[0]; string body = data.Substring(1);
                    char unit = info[3];
                    if (wi == "11") { p.No = body.TrimStart('0'); if (p.No.Length == 0) p.No = "0"; any = true; }
                    else if (wi == "71" || wi == "41" || wi == "72") { if (p.Desc.Length == 0) p.Desc = body.TrimStart('0', '?').Trim(); }
                    else if (wi == "81" || wi == "82" || wi == "83")
                    {
                        long v; if (!long.TryParse(body, NumberStyles.None, Inv, out v)) { ok = false; break; }
                        double div; switch (unit) { case '0': case '1': div = 1000; break; case '6': div = 10000; break; case '8': div = 100000; break; default: ok = false; div = 1; break; }
                        if (!ok) break;
                        if (unit == '1') { ok = false; break; } // feet: refused rather than guessed
                        double d = v / div * (sign == '-' ? -1 : 1);
                        if (wi == "81") p.E = d; else if (wi == "82") p.N = d; else { p.Z = d; p.HasZ = true; }
                        any = true;
                    }
                }
                if (!any || !ok) { r.Problems.Add("Line " + ln + ": not a coordinate record (or unsupported unit)"); continue; }
                r.Points.Add(p);
            }
            return r;
        }

        public static string Format(IEnumerable<SurveyPoint> pts, bool sixteen)
        {
            var sb = new StringBuilder(); int idx = 0; int w = sixteen ? 16 : 8;
            Func<long, string> num = v => (v < 0 ? "-" : "+") + Math.Abs(v).ToString(Inv).PadLeft(w, '0');
            foreach (var p in pts)
            {
                idx++;
                string no = new string((p.No ?? "").Where(char.IsDigit).ToArray()); if (no.Length == 0) no = idx.ToString(Inv);
                if (no.Length > w) no = no.Substring(no.Length - w);
                sb.Append(sixteen ? "*" : "").Append("11").Append((idx % 10000).ToString("0000", Inv)).Append("+").Append(no.PadLeft(w, '0'));
                sb.Append(" 81..00").Append(num((long)Math.Round(p.E * 1000))).Append(" 82..00").Append(num((long)Math.Round(p.N * 1000)));
                if (p.HasZ) sb.Append(" 83..00").Append(num((long)Math.Round(p.Z * 1000)));
                string code = new string((p.Desc ?? "").Where(c => c < 128 && !char.IsControl(c)).ToArray()).Trim(); if (code.Length > w) code = code.Substring(0, w);
                if (code.Length > 0) sb.Append(" 71....+").Append(code.PadLeft(w, '0'));
                sb.Append("\r\n");
            }
            return sb.ToString();
        }
    }

    // ---------- Sokkia/Trimble SDR33 (type 08 coordinate records). Built from the public layout; UNVERIFIED against a real instrument file. ----------
    public static class Sdr
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static ParseResult Parse(IEnumerable<string> lines)
        {
            var r = new ParseResult(); int ln = 0;
            foreach (var raw in lines)
            {
                ln++; var line = raw ?? ""; if (line.Trim().Length == 0) { r.Blank++; continue; }
                if (!line.StartsWith("08")) continue; // other record types are not coordinates
                if (line.Length < 4 + 16 * 3) { r.Problems.Add("Line " + ln + ": coordinate record is too short"); continue; }
                string F(int i) { int s = 4 + 16 * i; return s >= line.Length ? "" : line.Substring(s, Math.Min(16, line.Length - s)).Trim(); }
                double n, e, z = 0; bool hz = false;
                if (!double.TryParse(F(1), NumberStyles.Float, Inv, out n) || !double.TryParse(F(2), NumberStyles.Float, Inv, out e)) { r.Problems.Add("Line " + ln + ": northing or easting is not a number"); continue; }
                if (F(3).Length > 0 && double.TryParse(F(3), NumberStyles.Float, Inv, out z)) hz = true;
                r.Points.Add(new SurveyPoint { No = F(0), N = n, E = e, Z = z, HasZ = hz, Desc = F(4) });
            }
            return r;
        }

        public static string Format(IEnumerable<SurveyPoint> pts)
        {
            var sb = new StringBuilder();
            Func<string, string> L = s => (s ?? "").Length > 16 ? s.Substring(0, 16) : (s ?? "").PadRight(16);
            Func<double, string> R = v => v.ToString("0.0000", Inv).PadLeft(16);
            sb.Append("00NMBPlus CadTools".PadRight(40)).Append("\r\n");
            foreach (var p in pts)
            {
                string code = new string((p.Desc ?? "").Where(c => c < 128 && !char.IsControl(c)).ToArray());
                sb.Append("08KI").Append(L(p.No)).Append(R(p.N)).Append(R(p.E)).Append(p.HasZ ? R(p.Z) : new string(' ', 16)).Append(L(code)).Append("\r\n");
            }
            return sb.ToString();
        }
    }

    public static class Renumber
    {
        /// <summary>Returns the new number for each input point, in input order. sortBy: "keep" (current order of the list), "no" (by current number), "east", "north".</summary>
        public static List<string> Plan(IList<SurveyPoint> pts, int start, int step, string sortBy, string prefix = "", string suffix = "")
        {
            var order = Enumerable.Range(0, pts.Count).ToList();
            double Num(string s) { double d; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : double.MaxValue; }
            if (sortBy == "no") order = order.OrderBy(i => Num(pts[i].No)).ThenBy(i => pts[i].No, StringComparer.Ordinal).ThenBy(i => i).ToList();
            else if (sortBy == "east") order = order.OrderBy(i => pts[i].E).ThenBy(i => pts[i].N).ThenBy(i => i).ToList();
            else if (sortBy == "north") order = order.OrderBy(i => pts[i].N).ThenBy(i => pts[i].E).ThenBy(i => i).ToList();
            var res = new string[pts.Count]; long n = start;
            foreach (var i in order) { res[i] = prefix + n.ToString(CultureInfo.InvariantCulture) + suffix; n += step; }
            return res.ToList();
        }
    }

    public static class LoopGroups
    {
        public static bool Inside(double x, double y, IList<double[]> poly)
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                double xi = poly[i][0], yi = poly[i][1], xj = poly[j][0], yj = poly[j][1];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) c = !c;
            }
            return c;
        }
        static double Area(IList<double[]> p) { double a = 0; for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) a += p[j][0] * p[i][1] - p[i][0] * p[j][1]; return Math.Abs(a) / 2; }

        /// <summary>For each loop returns the index of its group. A loop nested at an even depth (0, 2, ...) starts a group; a loop at an odd depth (an island) joins the group of the loop that directly contains it.</summary>
        public static int[] Group(IList<IList<double[]>> loops)
        {
            int n = loops.Count; var depth = new int[n]; var parent = new int[n];
            for (int i = 0; i < n; i++)
            {
                parent[i] = -1; double best = double.MaxValue; var probe = loops[i].Count > 0 ? loops[i][0] : null; if (probe == null) continue;
                for (int j = 0; j < n; j++)
                {
                    if (i == j || loops[j].Count < 3) continue;
                    double aj = Area(loops[j]); if (aj <= Area(loops[i])) continue;
                    // a loop is inside j when most of its vertices are inside j
                    int inside = loops[i].Count(p => Inside(p[0], p[1], loops[j]));
                    if (inside * 2 > loops[i].Count) { depth[i]++; if (aj < best) { best = aj; parent[i] = j; } }
                }
            }
            var group = new int[n];
            for (int i = 0; i < n; i++) { int k = i; while (depth[k] % 2 == 1 && parent[k] >= 0) k = parent[k]; group[i] = k; }
            return group;
        }
    }
}
