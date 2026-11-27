using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BPlus.LiveMap.Core
{
    public sealed class ContourLine { public double Level; public List<double[]> Pts = new List<double[]>(); public bool Closed; public double Length; }

    /// <summary>Contour lines from triangles or from a height grid. Units: m.</summary>
    public static class Contour
    {
        public static double SuggestInterval(double range) { return ProfileMath.NiceStep(Math.Max(range, 0.01) / 15.0); }

        /// <summary>Levels that are whole multiples of the interval strictly inside (zmin, zmax).</summary>
        public static List<double> Levels(double zmin, double zmax, double interval)
        {
            var r = new List<double>(); if (interval <= 0 || zmax <= zmin) return r;
            long k0 = (long)Math.Floor(zmin / interval) + 1, k1 = (long)Math.Floor(zmax / interval);
            for (long k = k0; k <= k1 && r.Count < 100000; k++) { double l = Math.Round(k * interval, 9); if (l > zmin && l <= zmax) r.Add(l); }
            return r;
        }

        public static bool IsIndex(double level, double interval, int every) { if (every < 1) return false; return Math.Abs(Math.Round(level / interval)) % every == 0; }

        /// <summary>Blue (low) to red (high).</summary>
        public static int[] ElevColor(double t)
        {
            t = Math.Max(0, Math.Min(1, t)); double h = 240.0 * (1 - t) / 360.0, s = 0.85, v = 1.0;
            double r, g, b; int i = (int)Math.Floor(h * 6); double f = h * 6 - i, p = v * (1 - s), q = v * (1 - f * s), u = v * (1 - (1 - f) * s);
            switch (i % 6) { case 0: r = v; g = u; b = p; break; case 1: r = q; g = v; b = p; break; case 2: r = p; g = v; b = u; break; case 3: r = p; g = q; b = v; break; case 4: r = u; g = p; b = v; break; default: r = v; g = p; b = q; break; }
            return new[] { (int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255) };
        }

        // Crossing point on an edge; the end points are put in a fixed order so two triangles sharing the edge get identical numbers.
        static double[] Cross(double xa, double ya, double za, double xb, double yb, double zb, double level)
        {
            if (xa > xb || (xa == xb && ya > yb)) { double tx = xa, ty = ya, tz = za; xa = xb; ya = yb; za = zb; xb = tx; yb = ty; zb = tz; }
            double t = (level - za) / (zb - za); return new[] { xa + t * (xb - xa), ya + t * (yb - ya) };
        }

        static void TriSeg(double x1, double y1, double z1, double x2, double y2, double z2, double x3, double y3, double z3, double level, List<double[][]> segs)
        {
            bool a1 = z1 >= level, a2 = z2 >= level, a3 = z3 >= level; if (a1 == a2 && a2 == a3) return;
            var pts = new List<double[]>(2);
            if (a1 != a2) pts.Add(Cross(x1, y1, z1, x2, y2, z2, level));
            if (a2 != a3) pts.Add(Cross(x2, y2, z2, x3, y3, z3, level));
            if (a3 != a1) pts.Add(Cross(x3, y3, z3, x1, y1, z1, level));
            if (pts.Count == 2 && (pts[0][0] != pts[1][0] || pts[0][1] != pts[1][1])) segs.Add(new[] { pts[0], pts[1] });
        }

        static double MaxEdge(Tri t)
        {
            double a = Math.Sqrt((t.X1 - t.X2) * (t.X1 - t.X2) + (t.Y1 - t.Y2) * (t.Y1 - t.Y2)), b = Math.Sqrt((t.X2 - t.X3) * (t.X2 - t.X3) + (t.Y2 - t.Y3) * (t.Y2 - t.Y3)), c = Math.Sqrt((t.X3 - t.X1) * (t.X3 - t.X1) + (t.Y3 - t.Y1) * (t.Y3 - t.Y1));
            return Math.Max(a, Math.Max(b, c));
        }

        public static List<ContourLine> FromTris(IList<Tri> tris, double level, double maxEdge)
        {
            var segs = new List<double[][]>();
            foreach (var t in tris) { if (maxEdge > 0 && MaxEdge(t) > maxEdge) continue; TriSeg(t.X1, t.Y1, t.Z1, t.X2, t.Y2, t.Z2, t.X3, t.Y3, t.Z3, level, segs); }
            return Chain(segs, level);
        }

        public static List<ContourLine> FromGrid(Grid g, double level)
        {
            var segs = new List<double[][]>();
            for (int j = 0; j + 1 < g.NY; j++) for (int i = 0; i + 1 < g.NX; i++)
                {
                    double za = g.Z[j * g.NX + i], zb = g.Z[j * g.NX + i + 1], zc = g.Z[(j + 1) * g.NX + i + 1], zd = g.Z[(j + 1) * g.NX + i];
                    if (double.IsNaN(za) || double.IsNaN(zb) || double.IsNaN(zc) || double.IsNaN(zd)) continue;
                    double x0 = g.CX(i), x1 = g.CX(i + 1), y0 = g.CY(j), y1 = g.CY(j + 1);
                    TriSeg(x0, y0, za, x1, y0, zb, x1, y1, zc, level, segs); TriSeg(x0, y0, za, x1, y1, zc, x0, y1, zd, level, segs);
                }
            return Chain(segs, level);
        }

        public static List<ContourLine> Chain(List<double[][]> segs, double level)
        {
            var res = new List<ContourLine>(); int n = segs.Count; if (n == 0) return res;
            var map = new Dictionary<Tuple<double, double>, List<int>>();
            Func<double[], Tuple<double, double>> key = p => Tuple.Create(p[0], p[1]);
            for (int i = 0; i < n; i++) foreach (var p in segs[i]) { var k = key(p); List<int> l; if (!map.TryGetValue(k, out l)) { l = new List<int>(2); map[k] = l; } l.Add(i); }
            var used = new bool[n];
            Func<double[], int, int> next = (p, from) => { List<int> l; if (!map.TryGetValue(key(p), out l)) return -1; foreach (var s in l) if (!used[s] && s != from) return s; return -1; };
            for (int s0 = 0; s0 < n; s0++)
            {
                if (used[s0]) continue; used[s0] = true;
                var line = new LinkedList<double[]>(); line.AddLast(segs[s0][0]); line.AddLast(segs[s0][1]);
                for (int dir = 0; dir < 2; dir++)
                {
                    while (true)
                    {
                        var end = dir == 0 ? line.Last.Value : line.First.Value; int s = next(end, -1); if (s < 0) break; used[s] = true;
                        var o = segs[s][0][0] == end[0] && segs[s][0][1] == end[1] ? segs[s][1] : segs[s][0];
                        if (dir == 0) line.AddLast(o); else line.AddFirst(o);
                    }
                }
                var cl = new ContourLine { Level = level, Pts = line.ToList() };
                var f = cl.Pts[0]; var e = cl.Pts[cl.Pts.Count - 1];
                if (cl.Pts.Count > 3 && f[0] == e[0] && f[1] == e[1]) { cl.Closed = true; cl.Pts.RemoveAt(cl.Pts.Count - 1); }
                double len = 0; for (int i = 1; i < cl.Pts.Count; i++) len += Dist(cl.Pts[i - 1], cl.Pts[i]); if (cl.Closed) len += Dist(cl.Pts[cl.Pts.Count - 1], cl.Pts[0]);
                cl.Length = len; res.Add(cl);
            }
            return res;
        }

        static double Dist(double[] a, double[] b) { double dx = a[0] - b[0], dy = a[1] - b[1]; return Math.Sqrt(dx * dx + dy * dy); }

        /// <summary>Where to put the label: a point 40% along the line, the text angle in degrees along the line and kept readable (never upside down). Null if the line is too short.</summary>
        public static double[] LabelSpot(ContourLine c, double minLength)
        {
            if (c.Length < minLength || c.Pts.Count < 2) return null;
            double target = c.Length * 0.4, acc = 0; int m = c.Pts.Count; int segs = c.Closed ? m : m - 1;
            for (int i = 0; i < segs; i++)
            {
                var a = c.Pts[i]; var b = c.Pts[(i + 1) % m]; double l = Dist(a, b);
                if (acc + l >= target && l > 0)
                {
                    double t = (target - acc) / l; double ang = Math.Atan2(b[1] - a[1], b[0] - a[0]) * 180.0 / Math.PI;
                    if (ang > 90 || ang < -90) ang += 180; while (ang > 180) ang -= 360; while (ang <= -180) ang += 360;
                    return new[] { a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1]), ang };
                }
                acc += l;
            }
            return null;
        }

        public static string F(double level, int decimals) { return level.ToString("F" + decimals, CultureInfo.InvariantCulture); }
        public static int Decimals(double interval) { int d = 0; double v = interval; while (d < 4 && Math.Abs(v - Math.Round(v)) > 1e-6) { v *= 10; d++; } return d; }
    }
}
