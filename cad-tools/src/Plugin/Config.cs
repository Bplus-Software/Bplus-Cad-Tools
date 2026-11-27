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
        // "shared" = BPlus map server (default). "own" = the user's own Google key.
        public static bool SharedAvailable { get { return !Defaults.ServerUrl.Contains("@@"); } }
        public static bool UseOwnKey { get { if (!SharedAvailable) return true; return F.Get("mode", "shared") == "own"; } set { F.Set("mode", value ? "own" : "shared"); F.Save(); } }
        public static string InstallToken { get { return F.Get("installToken", ""); } set { F.Set("installToken", value ?? ""); F.Save(); } }
        public static string ServerUrl { get { string o = F.Get("serverUrl", ""); return o.Length > 0 ? o : Defaults.ServerUrl; } }
        public static bool AutoUpdate { get { return F.GetInt("autoUpdate", 1) == 1; } set { F.Set("autoUpdate", value ? 1 : 0); F.Save(); } }
        public static string LastUpdateCheck { get { return F.Get("lastUpdateCheck", ""); } set { F.Set("lastUpdateCheck", value ?? ""); F.Save(); } }
        public static string PointOrder { get { string o = F.Get("pointOrder", "PENZD"); return PointText.ValidOrder(o) ? o.ToUpperInvariant() : "PENZD"; } set { F.Set("pointOrder", value); F.Save(); } }
        public static int Decimals { get { int v = F.GetInt("decimals", 3); return v < 0 ? 0 : v > 8 ? 8 : v; } set { F.Set("decimals", value); F.Save(); } }
        public static double TextHeight { get { double v; return double.TryParse(F.Get("textHeight", "1"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && v > 0 && v <= 50 ? v : 1; } set { F.Set("textHeight", value.ToString(System.Globalization.CultureInfo.InvariantCulture)); F.Save(); } }
        public static bool Enabled { get { return F.GetInt("enabled", 0) == 1; } set { F.Set("enabled", value ? 1 : 0); F.Save(); } }
    }
}
