using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BPlus.LiveMap.Core
{
    /// <summary>Converts coordinates from one system to another through WGS 84 (source -> WGS 84 -> target).</summary>
    public static class CoordConvert
    {
        public static void Point(CoordSys from, CoordSys to, double x, double y, out double x2, out double y2)
        {
            double lon, lat; from.ToLonLat(x, y, out lon, out lat); to.FromLonLat(lon, lat, out x2, out y2);
        }

        /// <summary>True for systems whose datum shift to WGS 84 is only about 18 m accurate (Ain el Abd, Aramco Lambert).</summary>
        public static bool IsApprox(CoordSys c) { return c != null && (c.Epsg >= 20435 && c.Epsg <= 20440 || c.Epsg == 2318); }

        static char Delim(string line) { foreach (var d in new[] { '\t', ';', ',' }) if (line.IndexOf(d) >= 0) return d; return ' '; }
        static string[] Split(string line, char d) { return d == ' ' ? line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries) : line.Split(d); }
        static bool Num(string s, out double v) { return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v); }

        /// <summary>Guess the East and North columns (1-based) from the first data line: two neighbouring numbers with decimals, else columns 2 and 3 (number, E, N) or 1 and 2.</summary>
        public static void GuessColumns(string text, out int e, out int n)
        {
            e = 1; n = 2;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r'); if (line.Trim().Length == 0) continue; var f = Split(line, Delim(line)); double v;
                for (int i = 0; i + 1 < f.Length; i++) if (Num(f[i], out v) && f[i].Contains(".") && Num(f[i + 1], out v) && f[i + 1].Contains(".")) { e = i + 1; n = i + 2; return; }
                int nums = f.Count(s => Num(s, out v));
                if (nums >= 3) { e = 2; n = 3; return; }
                if (nums >= 2) { e = 1; n = 2; return; }
            }
        }

        /// <summary>Converts the E and N columns of every line that has numbers there; other lines (headers, blanks) are copied unchanged. Output precision: 3 decimals, 8 for lat/long.</summary>
        public static string ConvertText(string text, CoordSys from, CoordSys to, int eCol, int nCol, out int converted, out int skipped)
        {
            converted = 0; skipped = 0; var sb = new StringBuilder(); string fmt = to.IsGeographic ? "F8" : "F3";
            var lines = text.Replace("\r\n", "\n").Split('\n'); int last = lines.Length - (lines[lines.Length - 1].Length == 0 ? 1 : 0);
            for (int li = 0; li < last; li++)
            {
                var line = lines[li]; var d = Delim(line); var f = Split(line, d); double x, y;
                if (line.Trim().Length > 0 && f.Length >= Math.Max(eCol, nCol) && Num(f[eCol - 1], out x) && Num(f[nCol - 1], out y))
                {
                    double x2, y2; Point(from, to, x, y, out x2, out y2);
                    f[eCol - 1] = x2.ToString(fmt, CultureInfo.InvariantCulture); f[nCol - 1] = y2.ToString(fmt, CultureInfo.InvariantCulture); converted++;
                    sb.Append(string.Join(d == ' ' ? " " : d.ToString(), f));
                }
                else { if (line.Trim().Length > 0) skipped++; sb.Append(line); }
                sb.Append("\r\n");
            }
            return sb.ToString();
        }
    }
}
