using BPlus.LiveMap.Core;

namespace BPlus.LiveMap
{
    static class Config
    {
        static KvFile F = new KvFile(Paths.Settings);
        public static MapKind Kind
        {
            get { switch (F.Get("kind", "satellite")) { case "roadmap": return MapKind.Roadmap; case "hybrid": return MapKind.Hybrid; default: return MapKind.Satellite; } }
            set { F.Set("kind", value == MapKind.Roadmap ? "roadmap" : value == MapKind.Hybrid ? "hybrid" : "satellite"); F.Save(); }
        }
        public static int OpacityPercent { get { int v = F.GetInt("opacity", 100); return v < 10 ? 10 : v > 100 ? 100 : v; } set { F.Set("opacity", value); F.Save(); } }
        public static int WarnAt { get { return F.GetInt("warnAt", 50000); } set { F.Set("warnAt", value); F.Save(); } }
        /// <summary>Image rows are handed to AutoCAD bottom row first unless this is turned off (BPGMAPFLIP).</summary>
        public static bool FlipRows { get { return F.GetInt("flipRows", 1) == 1; } set { F.Set("flipRows", value ? 1 : 0); F.Save(); } }
        public static bool TermsAccepted { get { return F.GetInt("terms", 0) == 1; } set { F.Set("terms", value ? 1 : 0); F.Save(); } }
        public static bool Enabled { get { return F.GetInt("enabled", 0) == 1; } set { F.Set("enabled", value ? 1 : 0); F.Save(); } }
    }
}
