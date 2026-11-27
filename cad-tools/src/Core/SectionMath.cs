using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BPlus.LiveMap.Core
{
    /// <summary>A survey point seen from the line: station along it, signed offset (left of the direction is negative, right positive) and level.</summary>
    public sealed class SecPt { public string Id = ""; public double Sta, Off, Z; }

    /// <summary>A typical road type. Sizes in metres; crossfall in percent. Values are editable defaults, not a standard.</summary>
    public sealed class RoadPreset
    {
        public string Name = ""; public int Lanes = 2; public double LaneWidth = 3.5, Sidewalk = 1.8, Planting = 0, Kerb = 0.15, Crossfall = 2.0;
        public RoadPreset Copy() { return new RoadPreset { Name = Name, Lanes = Lanes, LaneWidth = LaneWidth, Sidewalk = Sidewalk, Planting = Planting, Kerb = Kerb, Crossfall = Crossfall }; }
    }

    /// <summary>Road to draw on a section. LeftEdge and RightEdge are offsets from the line of the two carriageway edges, Centre the offset of the crown.</summary>
    public sealed class RoadSpec
    {
        public double LeftEdge, RightEdge, Centre; public double[] Lanes = new double[0]; public double SwL = 1.8, SwR = 1.8, PlL, PlR, Kerb = 0.15, Crossfall = 2.0; public double CentreLevel = double.NaN;
    }

    public static class SectionMath
    {
        public static List<RoadPreset> RoadDefaults()
        {   // Lane 3.50 to 3.65 m and sidewalk 1.8 m minimum are from the Saudi ministry guides; lane counts, kerb height and crossfall are starting values to confirm.
            return new List<RoadPreset> {
                new RoadPreset { Name = "Local", Lanes = 2, LaneWidth = 3.5, Sidewalk = 1.8 },
                new RoadPreset { Name = "Collector", Lanes = 4, LaneWidth = 3.5, Sidewalk = 1.8 },
                new RoadPreset { Name = "Arterial", Lanes = 6, LaneWidth = 3.65, Sidewalk = 1.8 } };
        }
        static string R(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        public static string Pack(IList<RoadPreset> l) { return string.Join(";", l.Select(p => p.Name + "|" + p.Lanes + "|" + R(p.LaneWidth) + "|" + R(p.Sidewalk) + "|" + R(p.Planting) + "|" + R(p.Kerb) + "|" + R(p.Crossfall))); }
        public static List<RoadPreset> Unpack(string s)
        {
            var res = RoadDefaults(); if (string.IsNullOrWhiteSpace(s)) return res; var parts = s.Split(';');
            for (int i = 0; i < res.Count && i < parts.Length; i++)
            {
                var f = parts[i].Split('|'); if (f.Length < 7) continue; int n; double lw, sw, pl, k, cf;
                if (int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out n) && n >= 1 && n <= 12 && Num(f[2], out lw) && lw > 0 && lw <= 10 && Num(f[3], out sw) && sw >= 0 && sw <= 20 && Num(f[4], out pl) && pl >= 0 && pl <= 20 && Num(f[5], out k) && k >= 0 && k <= 1 && Num(f[6], out cf) && cf >= 0 && cf <= 15)
                { res[i].Lanes = n; res[i].LaneWidth = lw; res[i].Sidewalk = sw; res[i].Planting = pl; res[i].Kerb = k; res[i].Crossfall = cf; }
            }
            return res;
        }
        static bool Num(string t, out double v) { return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v); }

        /// <summary>Stations to draw: 0, step, 2 step ... and the end of the line.</summary>
        public static List<double> Stations(double length, double step)
        {
            var r = new List<double>(); if (step <= 0 || length < 0) return r;
            for (double s = 0; s < length - 1e-6; s += step) r.Add(s);
            r.Add(length); return r;
        }

        /// <summary>Points that belong to one station: within the strip along the line and within the half-width across it.</summary>
        public static List<SecPt> Select(IEnumerable<SecPt> all, double sta, double strip, double halfWidth)
        {
            return all.Where(p => Math.Abs(p.Sta - sta) <= strip + 1e-9 && Math.Abs(p.Off) <= halfWidth + 1e-9).OrderBy(p => p.Off).ThenBy(p => p.Z).ToList();
        }

        /// <summary>Ground level at the given offset by straight interpolation between the two nearest points on each side; NaN when the offset is outside the points.</summary>
        public static double GroundAt(IList<SecPt> sel, double off)
        {
            var s = sel.OrderBy(p => p.Off).ToList(); if (s.Count == 0) return double.NaN;
            for (int i = 0; i < s.Count; i++) if (Math.Abs(s[i].Off - off) < 1e-9) return s[i].Z;
            for (int i = 0; i + 1 < s.Count; i++) if (s[i].Off < off && s[i + 1].Off > off) { double t = (off - s[i].Off) / (s[i + 1].Off - s[i].Off); return s[i].Z + t * (s[i + 1].Z - s[i].Z); }
            return double.NaN;
        }

        /// <summary>Lane widths: the typed list, or the road width divided equally.</summary>
        public static double[] EqualLanes(double roadWidth, int n) { if (n < 1) n = 1; return Enumerable.Repeat(roadWidth / n, n).ToArray(); }

        /// <summary>Straight interpolation of a width between the first and last station.</summary>
        public static double Lerp(double a, double b, double sta, double length) { return length <= 0 ? a : a + (b - a) * Math.Max(0, Math.Min(1, sta / length)); }

        /// <summary>One section in section coordinates: x = offset + half-width (0 at the left edge of the frame), y = (level - datum) x exaggeration.</summary>
        public static ProfModel Build(IList<SecPt> ground, double halfWidth, double ve, double datum, double textH, bool label, RoadSpec road, string title, out double halfUsed, out string warning)
        { return Build(ground, halfWidth, ve, datum, textH, label, road, null, title, out halfUsed, out warning); }

        public static ProfModel Build(IList<SecPt> ground, double halfWidth, double ve, double datum, double textH, bool label, RoadSpec road, SecAssembly asm, string title, out double halfUsed, out string warning)
        {
            warning = ""; var g = (ground ?? new List<SecPt>()).OrderBy(p => p.Off).ToList();
            if (g.Count == 0 && road == null && asm == null) throw new ArgumentException("nothing to draw");
            if (ve <= 0) throw new ArgumentException("exaggeration must be above zero");
            double hw = halfWidth; double cl = double.NaN;
            if (road != null)
            {
                cl = road.CentreLevel; if (double.IsNaN(cl)) { cl = GroundAt(g, road.Centre); if (double.IsNaN(cl)) { cl = g.Count > 0 ? g.Average(p => p.Z) : 0; warning = "no ground at the centre: used " + ProfileMath.F2(cl) + " m"; } }
                hw = Math.Max(hw, Math.Max(Math.Abs(road.LeftEdge) + road.SwL + road.PlL, Math.Abs(road.RightEdge) + road.SwR + road.PlR) + 1);
            }
            AsmGeo ag = null;
            if (asm != null)
            {
                cl = asm.CentreLevel; if (double.IsNaN(cl)) { cl = GroundAt(g, 0); if (double.IsNaN(cl)) { cl = g.Count > 0 ? g.Average(p => p.Z) : 0; warning = "no ground at the centre: used " + ProfileMath.F2(cl) + " m"; } }
                ag = AssemblyMath.Layout(asm, cl, g); hw = Math.Max(hw, Math.Max(-ag.Left, ag.Right) + 1);
                if (ag.Notes.Count > 0) warning = (warning == "" ? "" : warning + "; ") + string.Join("; ", ag.Notes);
            }
            halfUsed = hw;
            Func<double, double> X = o => o + hw;
            var zs = new List<double>(); zs.AddRange(g.Select(p => p.Z));
            Func<double, double> surf = null;
            if (road != null)
            {
                double cf = road.Crossfall / 100.0; surf = o => cl - cf * Math.Abs(o - road.Centre);
                zs.Add(cl); zs.Add(surf(road.LeftEdge) + road.Kerb); zs.Add(surf(road.RightEdge) + road.Kerb); zs.Add(surf(road.LeftEdge)); zs.Add(surf(road.RightEdge));
            }
            if (ag != null && ag.Segs.Count > 0) { zs.Add(ag.MinZ); zs.Add(ag.MaxZ); }
            double minZ = zs.Min(), maxZ = zs.Max(); if (double.IsNaN(datum)) datum = ProfileMath.AutoDatum(minZ, maxZ);
            var m = new ProfModel { MinZ = minZ, MaxZ = maxZ, Datum = datum, Length = 2 * hw };
            double top = Math.Max(maxZ - datum, 0.1), es = Math.Max(ProfileMath.NiceStep(top / 4.0), ProfileMath.NiceStep(3.0 * textH / ve)); m.ElevStep = es; /* level lines at least 3 text heights apart on the drawing */
            double dimBand = road != null || ag != null ? 7 * textH : 0;
            int nz = (int)Math.Ceiling((top + dimBand / ve) / es - 1e-9); if (nz < 1) nz = 1; double yTop = nz * es * ve;
            double offStep = hw > 30 ? 10 : 5; m.StaStep = offStep;
            m.Segs.Add(new ProfSeg { X1 = 0, Y1 = 0, X2 = 2 * hw, Y2 = 0, Kind = "frame" }); m.Segs.Add(new ProfSeg { X1 = 0, Y1 = 0, X2 = 0, Y2 = yTop, Kind = "frame" });
            m.Segs.Add(new ProfSeg { X1 = 2 * hw, Y1 = 0, X2 = 2 * hw, Y2 = yTop, Kind = "frame" }); m.Segs.Add(new ProfSeg { X1 = 0, Y1 = yTop, X2 = 2 * hw, Y2 = yTop, Kind = "frame" });
            for (double o = -Math.Floor(hw / offStep) * offStep; o <= hw + 1e-9; o += offStep)
            {
                m.Segs.Add(new ProfSeg { X1 = X(o), Y1 = 0, X2 = X(o), Y2 = yTop, Kind = o == 0 ? "centre" : "grid" });
                m.Texts.Add(new ProfText { X = X(o), Y = -textH * 1.2, S = o == 0 ? "0" : (o < 0 ? "L " : "R ") + Math.Abs(o).ToString("0.##", CultureInfo.InvariantCulture), Align = 1, Kind = "sta" });
            }
            for (int k = 0; k <= nz; k++)
            {
                double y = k * es * ve; if (k > 0) m.Segs.Add(new ProfSeg { X1 = 0, Y1 = y, X2 = 2 * hw, Y2 = y, Kind = "grid" });
                m.Texts.Add(new ProfText { X = -textH * 0.5, Y = y, S = ProfileMath.F2(datum + k * es), Align = 2, Kind = "elev" });
            }
            m.Texts.Add(new ProfText { X = 0, Y = -textH * 3.2, S = title + "   datum " + ProfileMath.F2(datum) + " m   x" + ve.ToString("0.##", CultureInfo.InvariantCulture), Align = 0, Kind = "title" });
            double lastLab = double.NegativeInfinity; int hidden = 0;
            foreach (var p in g)
            {
                m.Ground.Add(new[] { X(p.Off), (p.Z - datum) * ve });
                if (label && X(p.Off) - lastLab < textH * 1.3) { hidden++; continue; }
                if (label) lastLab = X(p.Off);
                if (label) m.Texts.Add(new ProfText { X = X(p.Off), Y = -textH * 4.4, S = (p.Id != "" ? p.Id + "  " : "") + ProfileMath.F2(p.Z), Align = 2, Kind = "pt", Rot = 90 });
            }
            if (road != null)
            {
                Func<double, double> Y = z => (z - datum) * ve;
                double l = road.LeftEdge, r = road.RightEdge, kb = road.Kerb;
                m.Segs.Add(new ProfSeg { X1 = X(l), Y1 = Y(surf(l)), X2 = X(road.Centre), Y2 = Y(cl), Kind = "road" }); m.Segs.Add(new ProfSeg { X1 = X(road.Centre), Y1 = Y(cl), X2 = X(r), Y2 = Y(surf(r)), Kind = "road" });
                // kerbs and sidewalks
                m.Segs.Add(new ProfSeg { X1 = X(l), Y1 = Y(surf(l)), X2 = X(l), Y2 = Y(surf(l) + kb), Kind = "walk" }); m.Segs.Add(new ProfSeg { X1 = X(l), Y1 = Y(surf(l) + kb), X2 = X(l - road.SwL), Y2 = Y(surf(l) + kb), Kind = "walk" });
                m.Segs.Add(new ProfSeg { X1 = X(r), Y1 = Y(surf(r)), X2 = X(r), Y2 = Y(surf(r) + kb), Kind = "walk" }); m.Segs.Add(new ProfSeg { X1 = X(r), Y1 = Y(surf(r) + kb), X2 = X(r + road.SwR), Y2 = Y(surf(r) + kb), Kind = "walk" });
                if (road.PlL > 0) m.Segs.Add(new ProfSeg { X1 = X(l - road.SwL), Y1 = Y(surf(l) + kb), X2 = X(l - road.SwL - road.PlL), Y2 = Y(surf(l) + kb), Kind = "plant" });
                if (road.PlR > 0) m.Segs.Add(new ProfSeg { X1 = X(r + road.SwR), Y1 = Y(surf(r) + kb), X2 = X(r + road.SwR + road.PlR), Y2 = Y(surf(r) + kb), Kind = "plant" });
                double yDim = Y(maxZ) + textH * 2.0, acc = l;
                foreach (double w in road.Lanes)
                {
                    double nx = acc + w; double zb = surf(nx);
                    if (Math.Abs(nx - r) > 1e-6) m.Segs.Add(new ProfSeg { X1 = X(nx), Y1 = Y(zb), X2 = X(nx), Y2 = Y(zb) + textH, Kind = "lane" });
                    m.Texts.Add(new ProfText { X = X((acc + nx) / 2), Y = yDim, S = w.ToString("0.###", CultureInfo.InvariantCulture), Align = 1, Kind = "dim" }); acc = nx;
                }
                if (road.SwL > 0) m.Texts.Add(new ProfText { X = X(l - road.SwL / 2), Y = yDim, S = road.SwL.ToString("0.###", CultureInfo.InvariantCulture), Align = 1, Kind = "dim" });
                if (road.SwR > 0) m.Texts.Add(new ProfText { X = X(r + road.SwR / 2), Y = yDim, S = road.SwR.ToString("0.###", CultureInfo.InvariantCulture), Align = 1, Kind = "dim" });
                m.Texts.Add(new ProfText { X = X((l + r) / 2), Y = yDim + textH * 2.2, S = "road " + (r - l).ToString("0.###", CultureInfo.InvariantCulture) + " m,  centre level " + ProfileMath.F2(cl), Align = 1, Kind = "dim" });
            }
            if (hidden > 0) warning = (warning == "" ? "" : warning + "; ") + hidden + " point label(s) left out because the points are closer than the text (use a smaller text height or a wider frame to see all)";
            if (ag != null)
            {
                foreach (var sg in ag.Segs) m.Segs.Add(new ProfSeg { X1 = X(sg.X1), Y1 = (sg.Y1 - datum) * ve, X2 = X(sg.X2), Y2 = (sg.Y2 - datum) * ve, Kind = sg.Kind });
                double yD = (maxZ - datum) * ve + textH * 2.0;
                for (int i = 0; i < ag.Dims.Count; i++) m.Texts.Add(new ProfText { X = X(ag.Dims[i][0]), Y = yD, S = ag.DimText[i], Align = 1, Kind = "dim" });
                m.Texts.Add(new ProfText { X = X((ag.Left + ag.Right) / 2), Y = yD + textH * 2.2, S = asm.Name + ",  centre level " + ProfileMath.F2(cl), Align = 1, Kind = "dim" });
            }
            return m;
        }
    }
}
