using System;
using System.Collections.Generic;
using System.Linq;

namespace BPlus.LiveMap.Core
{
    public struct JV { public double X, Y, B; public JV(double x, double y, double b = 0) { X = x; Y = y; B = b; } }
    public enum JoinMode { Extend, Straight, Touch }

    public sealed class JPath
    {
        public List<JV> V = new List<JV>(); public bool Closed, Changed; public List<int> Src = new List<int>();
        public JV First { get { return V[0]; } } public JV Last { get { return V[V.Count - 1]; } }
        public JPath Copy() { return new JPath { V = new List<JV>(V), Closed = Closed, Changed = Changed, Src = new List<int>(Src) }; }
        public void Reverse()
        {
            int n = V.Count; var r = new List<JV>(n);
            for (int k = 0; k < n; k++) { var p = V[n - 1 - k]; double b = k < n - 1 ? -V[n - 2 - k].B : 0; r.Add(new JV(p.X, p.Y, b)); }
            V = r;
        }
    }

    public sealed class JoinResult { public List<JPath> Paths = new List<JPath>(); public int ShapesIn, Extended, Straight, Closed, LeftOpen; }

    public static class JoinLogic
    {
        const double Eps = 1e-9;
        static double D(JV a, JV b) { double dx = a.X - b.X, dy = a.Y - b.Y; return Math.Sqrt(dx * dx + dy * dy); }
        static JV End(JPath p, int e) { return e == 0 ? p.First : p.Last; }

        // a ends at 'aEnd', b starts at 'bStart': extend both lines until they cross, within tol.
        static bool TryExtend(JV aPrev, JV aEnd, double aBulge, JV bStart, JV bNext, double bBulge, double tol, out JV x)
        {
            x = aEnd; if (aBulge != 0 || bBulge != 0) return false;
            double ax = aEnd.X - aPrev.X, ay = aEnd.Y - aPrev.Y, bx = bStart.X - bNext.X, by = bStart.Y - bNext.Y;
            double al = Math.Sqrt(ax * ax + ay * ay), bl = Math.Sqrt(bx * bx + by * by); if (al < Eps || bl < Eps) return false;
            ax /= al; ay /= al; bx /= bl; by /= bl;
            double cr = ax * by - ay * bx; if (Math.Abs(cr) < 1e-9) return false;
            double wx = bStart.X - aEnd.X, wy = bStart.Y - aEnd.Y;
            double s = (wx * by - wy * bx) / cr, t = (wx * ay - wy * ax) / cr;
            if (s < -Eps || t < -Eps || s > tol || t > tol) return false;
            x = new JV(aEnd.X + s * ax, aEnd.Y + s * ay); return true;
        }

        public static JoinResult Run(IList<JPath> input, double tol, JoinMode mode)
        {
            var res = new JoinResult { ShapesIn = input.Count };
            var paths = input.Select(p => p.Copy()).ToList();
            double eff = mode == JoinMode.Touch ? 0.001 : tol;
            while (true)
            {
                int bi = -1, bj = -1, bei = 0, bej = 0; double bd = double.MaxValue;
                for (int i = 0; i < paths.Count; i++) for (int j = i + 1; j < paths.Count; j++)
                    {
                        if (paths[i].Closed || paths[j].Closed) continue;
                        for (int ei = 0; ei < 2; ei++) for (int ej = 0; ej < 2; ej++)
                            { double d = D(End(paths[i], ei), End(paths[j], ej)); if (d <= eff && d < bd) { bd = d; bi = i; bj = j; bei = ei; bej = ej; } }
                    }
                if (bi < 0) break;
                var a = paths[bi]; var b = paths[bj];
                if (bei == 0) a.Reverse();
                if (bej == 1) b.Reverse();
                var v = new List<JV>(); int na = a.V.Count;
                if (bd < Eps || mode == JoinMode.Touch) { v.AddRange(a.V.Take(na - 1)); v.AddRange(b.V); }
                else
                {
                    JV x;
                    if (mode == JoinMode.Extend && na >= 2 && b.V.Count >= 2 && TryExtend(a.V[na - 2], a.Last, a.V[na - 2].B, b.First, b.V[1], b.First.B, tol, out x))
                    { v.AddRange(a.V.Take(na - 1)); v.Add(new JV(x.X, x.Y, b.First.B)); v.AddRange(b.V.Skip(1)); res.Extended++; }
                    else { var al = a.V.ToList(); al[na - 1] = new JV(al[na - 1].X, al[na - 1].Y, 0); v.AddRange(al); v.AddRange(b.V); res.Straight++; }
                }
                a.V = v; a.Src.AddRange(b.Src); a.Changed = true; paths.RemoveAt(bj);
            }
            foreach (var p in paths)
            {
                int n = p.V.Count; if (p.Closed || n < 3) continue;
                double d = D(p.First, p.Last); if (d > eff) continue;
                if (d < Eps || mode == JoinMode.Touch) { p.V.RemoveAt(n - 1); }
                else
                {
                    JV x;
                    if (mode == JoinMode.Extend && TryExtend(p.V[n - 2], p.Last, p.V[n - 2].B, p.First, p.V[1], p.First.B, tol, out x)) { p.V[0] = new JV(x.X, x.Y, p.V[0].B); p.V.RemoveAt(n - 1); res.Extended++; }
                    else { p.V[n - 1] = new JV(p.Last.X, p.Last.Y, 0); res.Straight++; }
                }
                p.Closed = true; p.Changed = true; res.Closed++;
            }
            // gaps that stay open: end pairs just outside the tolerance
            var ends = new List<JV>(); foreach (var p in paths) if (!p.Closed) { ends.Add(p.First); ends.Add(p.Last); }
            for (int i = 0; i < ends.Count; i++) for (int j = i + 1; j < ends.Count; j++) { double d = D(ends[i], ends[j]); if (d > eff && d <= eff * 5 && d > Eps) res.LeftOpen++; }
            res.Paths = paths; return res;
        }
    }

    public static class Poly2D
    {
        public static List<double[]> Flatten(IList<JV> v, bool closed, int arcSteps = 24)
        {
            var o = new List<double[]>(); int n = v.Count; int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                var p = v[i]; var q = v[(i + 1) % n]; o.Add(new[] { p.X, p.Y });
                if (Math.Abs(p.B) > 1e-12)
                {
                    double th = 4 * Math.Atan(p.B), dx = q.X - p.X, dy = q.Y - p.Y, c = Math.Sqrt(dx * dx + dy * dy); if (c < 1e-12) continue;
                    double k = (c / 2) / Math.Tan(th / 2); double cx = (p.X + q.X) / 2 - dy / c * k, cy = (p.Y + q.Y) / 2 + dx / c * k;
                    double a0 = Math.Atan2(p.Y - cy, p.X - cx), r = Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
                    for (int s = 1; s < arcSteps; s++) { double a = a0 + th * s / arcSteps; o.Add(new[] { cx + r * Math.Cos(a), cy + r * Math.Sin(a) }); }
                }
            }
            if (!closed) o.Add(new[] { v[n - 1].X, v[n - 1].Y });
            return o;
        }

        // true when inside or on the edge
        public static bool Contains(IList<double[]> poly, double x, double y)
        {
            int n = poly.Count; bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = poly[i][0], yi = poly[i][1], xj = poly[j][0], yj = poly[j][1];
                double dx = xj - xi, dy = yj - yi, l2 = dx * dx + dy * dy;
                double t = l2 > 0 ? Math.Max(0, Math.Min(1, ((x - xi) * dx + (y - yi) * dy) / l2)) : 0;
                double px = xi + t * dx - x, py = yi + t * dy - y; if (px * px + py * py < 1e-12) return true;
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }
    }
}
