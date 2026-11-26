using System;
using System.IO;
using System.Linq;
using System.Text;

namespace BPlus.LiveMap.Core
{
    public static class PlotNaming
    {
        static readonly char[] Bad = Path.GetInvalidFileNameChars().Concat(new[] { ':', '*', '?', '"', '<', '>', '|', '/', '\\' }).Distinct().ToArray();

        public static string Clean(string s)
        {
            var sb = new StringBuilder(); foreach (char c in s ?? "") sb.Append(Array.IndexOf(Bad, c) >= 0 ? '_' : c);
            return sb.ToString().Trim().TrimEnd('.');
        }

        /// <summary>Pattern with {drawing} and {layout}, e.g. "{drawing}-{layout}". An empty result becomes "plot". Adds no extension.</summary>
        public static string FileName(string pattern, string drawing, string layout)
        {
            if (string.IsNullOrWhiteSpace(pattern)) pattern = "{drawing}-{layout}";
            string r = pattern.Replace("{drawing}", Clean(drawing)).Replace("{layout}", Clean(layout));
            r = Clean(r); return r.Length == 0 ? "plot" : r;
        }

        /// <summary>First path that does not exist yet: name.pdf, name-2.pdf, name-3.pdf ... Never overwrites.</summary>
        public static string Unique(string folder, string name, string ext, Func<string, bool> exists)
        {
            string p = Path.Combine(folder, name + ext); int i = 2;
            while (exists(p)) p = Path.Combine(folder, name + "-" + (i++) + ext);
            return p;
        }

        /// <summary>"1:500", "1 : 500", "500" give 500. "fit" gives 0 (scale to fit). Returns -1 when not understood.</summary>
        public static double ParseScale(string s)
        {
            if (s == null) return -1; s = s.Trim().ToLowerInvariant().Replace(" ", "");
            if (s == "fit" || s == "tofit" || s == "scaletofit") return 0;
            if (s.StartsWith("1:")) s = s.Substring(2);
            double v; return double.TryParse(s.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && v > 0 ? v : -1;
        }

        /// <summary>Drawing units that equal 1 mm of paper at 1:ratio, for a drawing whose unit is unitToMetre metres.</summary>
        public static double DrawingUnitsPerPaperMm(double ratio, double unitToMetre) { return ratio / (1000.0 * (unitToMetre <= 0 ? 1 : unitToMetre)); }
    }
}
