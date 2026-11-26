using System;
using System.Collections.Generic;

namespace BPlus.LiveMap.Core
{
    public static class MeasureMath
    {
        /// <summary>Angle at b between the lines b-a and b-c, 0 to 180 degrees (the other side is 360 minus this).</summary>
        public static double AngleAt(double ae, double an, double be, double bn, double ce, double cn)
        {
            double a1 = Math.Atan2(ae - be, an - bn), a2 = Math.Atan2(ce - be, cn - bn);
            double d = Math.Abs(a1 - a2) * 180 / Math.PI; if (d > 180) d = 360 - d; return d;
        }
        /// <summary>Slope length of a chain of 3D points (e, n, z).</summary>
        public static double Length3D(IList<double[]> p) { double s = 0; for (int i = 0; i + 1 < p.Count; i++) { double dx = p[i + 1][0] - p[i][0], dy = p[i + 1][1] - p[i][1], dz = p[i + 1][2] - p[i][2]; s += Math.Sqrt(dx * dx + dy * dy + dz * dz); } return s; }
        public static double Length2D(IList<double[]> p) { double s = 0; for (int i = 0; i + 1 < p.Count; i++) s += Geo2D.Distance(p[i][0], p[i][1], p[i + 1][0], p[i + 1][1]); return s; }
        /// <summary>Area-weighted centre of a polygon (falls back to the mean of the points for a zero area).</summary>
        public static double[] Centroid(IList<double[]> p)
        {
            double a = 0, cx = 0, cy = 0; int n = p.Count;
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; double c = p[i][0] * p[j][1] - p[j][0] * p[i][1]; a += c; cx += (p[i][0] + p[j][0]) * c; cy += (p[i][1] + p[j][1]) * c; }
            if (Math.Abs(a) < 1e-12) { double sx = 0, sy = 0; foreach (var q in p) { sx += q[0]; sy += q[1]; } return new[] { sx / n, sy / n }; }
            return new[] { cx / (3 * a), cy / (3 * a) };
        }
    }
}
