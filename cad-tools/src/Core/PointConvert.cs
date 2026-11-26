using System;
using System.Collections.Generic;
using System.Globalization;

namespace BPlus.LiveMap.Core
{
    public sealed class ConvItem { public SurveyPoint Src; public int Number; public string Status; /* ok, numbered, used, nonumber, duplicate */ public bool Go { get { return Status == "ok" || Status == "numbered"; } } }

    /// <summary>Decides which points can be converted and which number each gets. Never renumbers: a number already used is skipped.</summary>
    public static class PointConvert
    {
        public static List<ConvItem> Plan(IList<SurveyPoint> src, ICollection<int> used, bool giveFree, int start)
        {
            var taken = new HashSet<int>(used); var res = new List<ConvItem>(); var next = start < 1 ? 1 : start; var reserved = new HashSet<int>();
            foreach (var p in src) { int q; if (!string.IsNullOrWhiteSpace(p.No) && int.TryParse(p.No.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out q) && q > 0) reserved.Add(q); }
            foreach (var p in src)
            {
                var it = new ConvItem { Src = p }; int n;
                if (!string.IsNullOrWhiteSpace(p.No) && int.TryParse(p.No.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out n) && n > 0)
                {
                    if (taken.Contains(n)) it.Status = "used"; else { taken.Add(n); it.Number = n; it.Status = "ok"; }
                }
                else if (giveFree)
                {
                    while (taken.Contains(next) || reserved.Contains(next)) next++;
                    taken.Add(next); it.Number = next; it.Status = "numbered"; next++;
                }
                else it.Status = "nonumber";
                res.Add(it);
            }
            return res;
        }
    }
}
