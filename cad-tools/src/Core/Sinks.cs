using System;
using System.Collections.Generic;
using System.Linq;

namespace BPlus.LiveMap.Core
{
    public sealed class Tri { public double X1, Y1, Z1, X2, Y2, Z2, X3, Y3, Z3; public Tri(double x1, double y1, double z1, double x2, double y2, double z2, double x3, double y3, double z3) { X1 = x1; Y1 = y1; Z1 = z1; X2 = x2; Y2 = y2; Z2 = z2; X3 = x3; Y3 = y3; Z3 = z3; } }

    public sealed class Grid
    {
        public double X0, Y0, Cell; public int NX, NY; public double[] Z; // Z[j * NX + i], NaN = no data; (X0,Y0) is the centre of cell 0,0
        public double CX(int i) { return X0 + i * Cell; } public double CY(int j) { return Y0 + j * Cell; }
    }

    public sealed class Sink
    {
        public int No; public double E, N, LowZ, SpillZ, Depth, Area, Volume; public List<List<double[]>> Outline = new List<List<double[]>>();
    }

    public static class Sinks
    {
        const int MaxCells = 2000000;

        // Delaunay triangulation of 3D points (Bowyer-Watson). Duplicate XY keep the lowest Z. O(n^2): keep n below about 8000.
        public static List<Tri> Delaunay(IList<double[]> pts)
        {
            var d = new Dictionary<string, double[]>();
            foreach (var p in pts) { string k = Math.Round(p[0], 4).ToString("R") + "|" + Math.Round(p[1], 4).ToString("R"); double[] o; if (!d.TryGetValue(k, out o) || p[2] < o[2]) d[k] = p; }
            var P = d.Values.ToList(); int n = P.Count; var res = new List<Tri>(); if (n < 3) return res;
            double minx = P.Min(p => p[0]), maxx = P.Max(p => p[0]), miny = P.Min(p => p[1]), maxy = P.Max(p => p[1]);
            double dm = Math.Max(maxx - minx, maxy - miny); if (dm <= 0) return res; double mx = (minx + maxx) / 2, my = (miny + maxy) / 2;
            var V = new List<double[]>(P) { new[] { mx - 20 * dm, my - dm, 0 }, new[] { mx, my + 20 * dm, 0 }, new[] { mx + 20 * dm, my - dm, 0 } };
            var T = new List<int[]> { new[] { n, n + 1, n + 2 } };
            for (int i = 0; i < n; i++)
            {
                double px = V[i][0], py = V[i][1]; var bad = new List<int[]>(); var rest = new List<int[]>();
                foreach (var t in T) (InCircle(V[t[0]], V[t[1]], V[t[2]], px, py) ? bad : rest).Add(t);
                var edges = new Dictionary<long, int[]>(); var cnt = new Dictionary<long, int>();
                foreach (var t in bad) for (int e = 0; e < 3; e++)
                    {
                        int a = t[e], b = t[(e + 1) % 3]; long key = a < b ? (long)a * 1000003 + b : (long)b * 1000003 + a;
                        int c; cnt.TryGetValue(key, out c); cnt[key] = c + 1; edges[key] = new[] { a, b };
                    }
                T = rest;
                foreach (var kv in edges) if (cnt[kv.Key] == 1) T.Add(new[] { kv.Value[0], kv.Value[1], i });
            }
            foreach (var t in T) { if (t[0] >= n || t[1] >= n || t[2] >= n) continue; var a = V[t[0]]; var b = V[t[1]]; var c = V[t[2]]; res.Add(new Tri(a[0], a[1], a[2], b[0], b[1], b[2], c[0], c[1], c[2])); }
            return res;
        }

        static bool InCircle(double[] a, double[] b, double[] c, double px, double py)
        {
            double ax = a[0] - px, ay = a[1] - py, bx = b[0] - px, by = b[1] - py, cx = c[0] - px, cy = c[1] - py;
            double det = (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay) + (cx * cx + cy * cy) * (ax * by - bx * ay);
            double orient = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
            return orient > 0 ? det > 1e-12 : det < -1e-12;
        }

        public static Grid Rasterize(IList<Tri> tris, double cell, out double usedCell)
        {
            double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
            foreach (var t in tris) { minx = Math.Min(minx, Math.Min(t.X1, Math.Min(t.X2, t.X3))); maxx = Math.Max(maxx, Math.Max(t.X1, Math.Max(t.X2, t.X3))); miny = Math.Min(miny, Math.Min(t.Y1, Math.Min(t.Y2, t.Y3))); maxy = Math.Max(maxy, Math.Max(t.Y1, Math.Max(t.Y2, t.Y3))); }
            while ((long)((maxx - minx) / cell + 2) * (long)((maxy - miny) / cell + 2) > MaxCells) cell *= 1.5;
            usedCell = cell;
            var g = new Grid { X0 = minx, Y0 = miny, Cell = cell, NX = (int)Math.Ceiling((maxx - minx) / cell) + 1, NY = (int)Math.Ceiling((maxy - miny) / cell) + 1 };
            g.Z = new double[g.NX * g.NY]; for (int i = 0; i < g.Z.Length; i++) g.Z[i] = double.NaN;
            foreach (var t in tris)
            {
                double den = (t.Y2 - t.Y3) * (t.X1 - t.X3) + (t.X3 - t.X2) * (t.Y1 - t.Y3); if (Math.Abs(den) < 1e-12) continue;
                int i0 = Math.Max(0, (int)Math.Ceiling((Math.Min(t.X1, Math.Min(t.X2, t.X3)) - minx) / cell - 1e-9)), i1 = Math.Min(g.NX - 1, (int)Math.Floor((Math.Max(t.X1, Math.Max(t.X2, t.X3)) - minx) / cell + 1e-9));
                int j0 = Math.Max(0, (int)Math.Ceiling((Math.Min(t.Y1, Math.Min(t.Y2, t.Y3)) - miny) / cell - 1e-9)), j1 = Math.Min(g.NY - 1, (int)Math.Floor((Math.Max(t.Y1, Math.Max(t.Y2, t.Y3)) - miny) / cell + 1e-9));
                for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++)
                    {
                        double x = minx + i * cell, y = miny + j * cell;
                        double a = ((t.Y2 - t.Y3) * (x - t.X3) + (t.X3 - t.X2) * (y - t.Y3)) / den, b = ((t.Y3 - t.Y1) * (x - t.X3) + (t.X1 - t.X3) * (y - t.Y3)) / den, c = 1 - a - b;
                        if (a < -1e-9 || b < -1e-9 || c < -1e-9) continue;
                        g.Z[j * g.NX + i] = a * t.Z1 + b * t.Z2 + c * t.Z3;
                    }
            }
            return g;
        }

        // Priority-flood fill, then each connected area of equal fill level that holds water is one sink.
        public static List<Sink> Analyze(Grid g, double minDepth, double minArea, out int ignored)
        {
            int nx = g.NX, ny = g.NY, N = nx * ny; var fill = new double[N]; var seen = new bool[N]; var zs = g.Z;
            var heap = new SortedSet<KeyValuePair<double, int>>(new Cmp());
            Func<int, int, bool> nodata = (i, j) => i < 0 || j < 0 || i >= nx || j >= ny || double.IsNaN(zs[j * nx + i]);
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i; if (double.IsNaN(zs[k])) continue;
                    bool edge = nodata(i - 1, j) || nodata(i + 1, j) || nodata(i, j - 1) || nodata(i, j + 1);
                    if (edge) { seen[k] = true; fill[k] = zs[k]; heap.Add(new KeyValuePair<double, int>(zs[k], k)); }
                }
            int[] di = { 1, -1, 0, 0, 1, 1, -1, -1 }, dj = { 0, 0, 1, -1, 1, -1, 1, -1 };
            while (heap.Count > 0)
            {
                var top = heap.Min; heap.Remove(top); int k = top.Value, ci = k % nx, cj = k / nx;
                for (int d = 0; d < 8; d++)
                {
                    int i = ci + di[d], j = cj + dj[d]; if (nodata(i, j)) continue; int q = j * nx + i; if (seen[q]) continue;
                    seen[q] = true; fill[q] = Math.Max(zs[q], fill[k]); heap.Add(new KeyValuePair<double, int>(fill[q], q));
                }
            }
            var comp = new int[N]; var list = new List<Sink>(); ignored = 0; double cell2 = g.Cell * g.Cell; int id = 0;
            for (int s = 0; s < N; s++)
            {
                if (comp[s] != 0 || double.IsNaN(zs[s]) || fill[s] - zs[s] <= 1e-6) continue;
                id++; var cells = new List<int>(); var st = new Stack<int>(); st.Push(s); comp[s] = id; double lvl = fill[s];
                while (st.Count > 0)
                {
                    int k = st.Pop(); cells.Add(k); int ci = k % nx, cj = k / nx;
                    for (int d = 0; d < 4; d++) { int i = ci + di[d], j = cj + dj[d]; if (nodata(i, j)) continue; int q = j * nx + i; if (comp[q] != 0 || fill[q] - zs[q] <= 1e-6 || Math.Abs(fill[q] - lvl) > 1e-6) continue; comp[q] = id; st.Push(q); }
                }
                int low = cells[0]; double vol = 0; foreach (var k in cells) { if (zs[k] < zs[low]) low = k; vol += (fill[k] - zs[k]) * cell2; }
                double depth = lvl - zs[low], area = cells.Count * cell2;
                if (depth < minDepth || area < minArea) { ignored++; continue; }
                var sk = new Sink { E = g.CX(low % nx), N = g.CY(low / nx), LowZ = zs[low], SpillZ = lvl, Depth = depth, Area = area, Volume = vol };
                sk.Outline = Outline(cells, g); list.Add(sk);
            }
            list = list.OrderByDescending(x => x.Depth).ToList(); for (int i = 0; i < list.Count; i++) list[i].No = i + 1;
            return list;
        }

        sealed class Cmp : IComparer<KeyValuePair<double, int>> { public int Compare(KeyValuePair<double, int> a, KeyValuePair<double, int> b) { int c = a.Key.CompareTo(b.Key); return c != 0 ? c : a.Value.CompareTo(b.Value); } }

        // Boundary of a set of cells as closed loops (cell-edge staircase).
        public static List<List<double[]>> Outline(List<int> cells, Grid g)
        {
            var set = new HashSet<int>(cells); int nx = g.NX; double h = g.Cell / 2; var next = new Dictionary<long, List<long>>();
            Func<int, int, long> key = (a, b) => (long)a * 100000 + b; // vertex grid coords (2*i+-1)
            Action<int, int, int, int> add = (ax, ay, bx, by) => { long k = key(ax + 5, ay + 5); List<long> l; if (!next.TryGetValue(k, out l)) next[k] = l = new List<long>(); l.Add(key(bx + 5, by + 5)); };
            foreach (var k in cells)
            {
                int i = k % nx, j = k / nx; int x0 = 2 * i - 1, x1 = 2 * i + 1, y0 = 2 * j - 1, y1 = 2 * j + 1;
                if (!set.Contains(k - nx) || j == 0) add(x0, y0, x1, y0);       // bottom, moving right
                if (!set.Contains(k + 1) || i == nx - 1) add(x1, y0, x1, y1);   // right, moving up
                if (!set.Contains(k + nx)) add(x1, y1, x0, y1);                // top, moving left
                if (!set.Contains(k - 1) || i == 0) add(x0, y1, x0, y0);       // left, moving down
            }
            var loops = new List<List<double[]>>();
            while (next.Count > 0)
            {
                long start = next.Keys.First(); long cur = start; var loop = new List<double[]>();
                while (true)
                {
                    List<long> l; if (!next.TryGetValue(cur, out l) || l.Count == 0) break;
                    int a = (int)(cur / 100000) - 5, b = (int)(cur % 100000) - 5; loop.Add(new[] { g.X0 + a * h, g.Y0 + b * h });
                    long nxt = l[0]; l.RemoveAt(0); if (l.Count == 0) next.Remove(cur); cur = nxt; if (cur == start) break;
                }
                if (loop.Count >= 3) loops.Add(loop);
            }
            return loops;
        }
    }
}
