using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BPlus.LiveMap.Core
{
    /// <summary>A point projected on the profile line: station along the line, level, offset from the line.</summary>
    public sealed class ProfPt { public string Id = ""; public double Sta, Z, Off; }
    public sealed class ProfSeg { public double X1, Y1, X2, Y2; public string Kind = ""; }
    /// <summary>Align: 0 left, 1 centre, 2 right (middle height).</summary>
    public sealed class ProfText { public double X, Y; public string S = ""; public int Align; public string Kind = ""; public double Rot; }

    public sealed class ProfModel
    {
        public List<ProfSeg> Segs = new List<ProfSeg>(); public List<ProfText> Texts = new List<ProfText>(); public List<double[]> Ground = new List<double[]>();
        public List<string[]> Table = new List<string[]>(); public List<string[]> Summary = new List<string[]>();
        public double Datum, MinZ, MaxZ, Length, ElevStep, StaStep;
    }

    /// <summary>Look of the profile. Height NaN or 0 = use the exaggeration; otherwise the frame is this tall (drawing units) and the exaggeration follows.</summary>
    public sealed class ProfStyle { public double Height = double.NaN; public bool Grid = true, Drops = true, MarkExtremes = true, AvgLine = true; }

    /// <summary>Quick longitudinal profile: pure geometry in profile coordinates (x = station in m, y = (level - datum) x exaggeration). The drawing code only places it.</summary>
    public static class ProfileMath
    {
        public static double NiceStep(double raw)
        {
            if (raw <= 0 || double.IsNaN(raw) || double.IsInfinity(raw)) return 1;
            double p = Math.Pow(10, Math.Floor(Math.Log10(raw))), f = raw / p;
            return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * p;
        }

        public static string Sta(double s)
        {
            s = Math.Round(s, 2); int k = (int)Math.Floor(s / 1000.0); double m = s - k * 1000.0;
            return k.ToString(CultureInfo.InvariantCulture) + "+" + m.ToString("000.00", CultureInfo.InvariantCulture);
        }

        static string PtName(ProfPt p, IList<ProfPt> pts) { return p.Id != "" ? "No " + p.Id : "No " + (pts.IndexOf(p) + 1).ToString(CultureInfo.InvariantCulture); }
        public static string F2(double v) { return v.ToString("F2", CultureInfo.InvariantCulture); }

        /// <summary>Default station interval for a line of this length (about ten divisions).</summary>
        public static double DefaultInterval(double length) { return NiceStep(length / 10.0); }

        /// <summary>Datum: a round level below the lowest point.</summary>
        public static double AutoDatum(double minZ, double maxZ)
        {
            double range = Math.Max(maxZ - minZ, 0.1), step = NiceStep(range / 4.0);
            return Math.Floor(minZ / step) * step - step;
        }

        public static ProfModel Build(IList<ProfPt> input, double ve, double interval, double textH, double datum, bool labelPoints, bool table)
        { return Build(input, ve, interval, textH, datum, labelPoints, table, new ProfStyle()); }

        public static ProfModel Build(IList<ProfPt> input, double ve, double interval, double textH, double datum, bool labelPoints, bool table, ProfStyle st)
        {
            if (st == null) st = new ProfStyle();
            if (input == null || input.Count == 0) throw new ArgumentException("no points");
            if (ve <= 0) throw new ArgumentException("exaggeration must be above zero");
            var pts = input.OrderBy(p => p.Sta).ThenBy(p => p.Z).ToList();
            var m = new ProfModel { MinZ = pts.Min(p => p.Z), MaxZ = pts.Max(p => p.Z) };
            double length = Math.Max(pts.Max(p => p.Sta), pts.Last().Sta); m.Length = length;
            if (double.IsNaN(datum)) datum = AutoDatum(m.MinZ, m.MaxZ);
            m.Datum = datum; if (interval <= 0) interval = DefaultInterval(length); m.StaStep = interval;
            double top = Math.Max(m.MaxZ - datum, 0.1); double es = NiceStep(top / 7.0); m.ElevStep = es;
            int nz = (int)Math.Ceiling(top / es - 1e-9); if (nz < 1) nz = 1; nz += 1; /* one step of headroom for the high-point label */
            if (st.Height > 0 && !double.IsNaN(st.Height)) ve = st.Height / (nz * es);
            double yTop = nz * es * ve;
            int ns = (int)Math.Ceiling(length / interval - 1e-9); if (ns < 1) ns = 1; double xEnd = ns * interval;
            // frame and grid
            m.Segs.Add(new ProfSeg { X1 = 0, Y1 = 0, X2 = xEnd, Y2 = 0, Kind = "frame" });
            m.Segs.Add(new ProfSeg { X1 = 0, Y1 = 0, X2 = 0, Y2 = yTop, Kind = "frame" });
            for (int i = 0; i <= ns; i++)
            {
                double x = i * interval;
                if (st.Grid) m.Segs.Add(new ProfSeg { X1 = x, Y1 = 0, X2 = x, Y2 = yTop, Kind = "grid" });
                m.Texts.Add(new ProfText { X = x, Y = -textH * 1.2, S = Sta(x), Align = 1, Kind = "sta", Rot = 0 });
            }
            for (int k = 0; k <= nz; k++)
            {
                double y = k * es * ve;
                if (k > 0 && st.Grid) m.Segs.Add(new ProfSeg { X1 = 0, Y1 = y, X2 = xEnd, Y2 = y, Kind = "grid" });
                m.Texts.Add(new ProfText { X = -textH * 0.5, Y = y, S = F2(datum + k * es), Align = 2, Kind = "elev" });
            }
            m.Texts.Add(new ProfText { X = 0, Y = -textH * 3.2, S = "Datum " + F2(datum) + " m   vertical exaggeration x" + ve.ToString("0.##", CultureInfo.InvariantCulture), Align = 0, Kind = "title" });
            foreach (var p in pts)
            {
                double x = p.Sta, y = (p.Z - datum) * ve;
                m.Ground.Add(new[] { x, y });
                if (st.Drops) m.Segs.Add(new ProfSeg { X1 = x, Y1 = y, X2 = x, Y2 = 0, Kind = "drop" });
                if (labelPoints) m.Texts.Add(new ProfText { X = x, Y = y + textH * 0.6, S = (p.Id != "" ? p.Id + "  " : "") + F2(p.Z), Align = 0, Kind = "pt", Rot = 90 });
            }
            double avg = pts.Average(p => p.Z); var hi = pts.First(p => p.Z == m.MaxZ); var lo = pts.First(p => p.Z == m.MinZ);
            m.Summary.Add(new[] { "Summary", "Station", "Level (m)", "Point" });
            m.Summary.Add(new[] { "Average", "-", F2(avg), "(" + pts.Count + " points)" });
            m.Summary.Add(new[] { "Highest", Sta(hi.Sta), F2(hi.Z), PtName(hi, pts) });
            m.Summary.Add(new[] { "Lowest (low point)", Sta(lo.Sta), F2(lo.Z), PtName(lo, pts) });
            m.Summary.Add(new[] { "Height difference", "-", F2(m.MaxZ - m.MinZ), "" });
            if (st.AvgLine)
            {
                double ya = (avg - datum) * ve; m.Segs.Add(new ProfSeg { X1 = 0, Y1 = ya, X2 = xEnd, Y2 = ya, Kind = "avg" });
                m.Texts.Add(new ProfText { X = xEnd + textH * 0.5, Y = ya, S = "Average " + F2(avg), Align = 0, Kind = "avg" });
            }
            if (st.MarkExtremes)
            {
                Action<ProfPt, string, bool> mark = (p, label, below) =>
                {
                    double x = p.Sta, y = (p.Z - datum) * ve, r = textH * 0.5; double px = x + r, py = y;
                    for (int i = 1; i <= 12; i++) { double t = i * Math.PI / 6, nx = x + r * Math.Cos(t), ny = y + r * Math.Sin(t); m.Segs.Add(new ProfSeg { X1 = px, Y1 = py, X2 = nx, Y2 = ny, Kind = "mark" }); px = nx; py = ny; }
                    double ty = below && y - textH * 2.6 > 0 ? y - textH * 2.0 : y + textH * 2.0;
                    m.Texts.Add(new ProfText { X = x, Y = ty, S = label + " " + F2(p.Z) + " at " + Sta(p.Sta), Align = 1, Kind = "mark" });
                };
                mark(hi, "High", false); if (lo != hi) mark(lo, "Low", true);
            }
            if (table)
            {
                m.Table.Add(new[] { "No", "Station", "Level (m)", "Offset (m)" });
                int n = 0; foreach (var p in pts) { n++; m.Table.Add(new[] { p.Id != "" ? p.Id : n.ToString(CultureInfo.InvariantCulture), Sta(p.Sta), F2(p.Z), F2(p.Off) }); }
            }
            return m;
        }
    }
}
