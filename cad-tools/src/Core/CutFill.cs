using System;
using System.Collections.Generic;
using System.Linq;

namespace BPlus.LiveMap.Core
{
    /// <summary>Height of a triangulated surface at any X,Y (NaN outside).</summary>
    public sealed class TriSurface
    {
        readonly Tri[] t; readonly double minx, miny, bw; readonly int nx, ny; readonly List<int>[] bins;
        public double MinX, MinY, MaxX, MaxY;
        public TriSurface(IList<Tri> tris)
        {
            t = tris.ToArray(); MinX = MinY = double.MaxValue; MaxX = MaxY = double.MinValue;
            foreach (var a in t) { MinX = Math.Min(MinX, Math.Min(a.X1, Math.Min(a.X2, a.X3))); MaxX = Math.Max(MaxX, Math.Max(a.X1, Math.Max(a.X2, a.X3))); MinY = Math.Min(MinY, Math.Min(a.Y1, Math.Min(a.Y2, a.Y3))); MaxY = Math.Max(MaxY, Math.Max(a.Y1, Math.Max(a.Y2, a.Y3))); }
            minx = MinX; miny = MinY; double side = Math.Sqrt(Math.Max(1e-9, (MaxX - MinX) * (MaxY - MinY) / Math.Max(1, t.Length / 4.0)));
            bw = Math.Max(side, Math.Max(MaxX - MinX, MaxY - MinY) / 400.0); if (bw <= 0) bw = 1;
            nx = (int)((MaxX - MinX) / bw) + 1; ny = (int)((MaxY - MinY) / bw) + 1; bins = new List<int>[nx * ny];
            for (int k = 0; k < t.Length; k++)
            {
                var a = t[k]; int i0 = (int)((Math.Min(a.X1, Math.Min(a.X2, a.X3)) - minx) / bw), i1 = (int)((Math.Max(a.X1, Math.Max(a.X2, a.X3)) - minx) / bw), j0 = (int)((Math.Min(a.Y1, Math.Min(a.Y2, a.Y3)) - miny) / bw), j1 = (int)((Math.Max(a.Y1, Math.Max(a.Y2, a.Y3)) - miny) / bw);
                for (int j = j0; j <= j1 && j < ny; j++) for (int i = i0; i <= i1 && i < nx; i++) { int b = j * nx + i; if (bins[b] == null) bins[b] = new List<int>(); bins[b].Add(k); }
            }
        }
        public double Z(double x, double y)
        {
            int i = (int)Math.Floor((x - minx) / bw), j = (int)Math.Floor((y - miny) / bw); if (i < 0 || j < 0 || i >= nx || j >= ny) return double.NaN;
            var l = bins[j * nx + i]; if (l == null) return double.NaN;
            foreach (int k in l)
            {
                var a = t[k]; double den = (a.Y2 - a.Y3) * (a.X1 - a.X3) + (a.X3 - a.X2) * (a.Y1 - a.Y3); if (Math.Abs(den) < 1e-12) continue;
                double u = ((a.Y2 - a.Y3) * (x - a.X3) + (a.X3 - a.X2) * (y - a.Y3)) / den, v = ((a.Y3 - a.Y1) * (x - a.X3) + (a.X1 - a.X3) * (y - a.Y3)) / den, w = 1 - u - v;
                if (u < -1e-9 || v < -1e-9 || w < -1e-9) continue;
                return u * a.Z1 + v * a.Z2 + w * a.Z3;
            }
            return double.NaN;
        }
    }

    public sealed class CutFillResult
    {
        public double Cell; public long Cells, UsedCells;
        public double CutVolume, FillVolume, CutArea, FillArea, Swell = 1;
        public double MeanCut { get { return CutArea > 0 ? CutVolume / CutArea : 0; } }
        public double MeanFill { get { return FillArea > 0 ? FillVolume / FillArea : 0; } }
        /// <summary>Positive = surplus of cut (spoil), negative = fill needed. Cut is multiplied by the swell factor.</summary>
        public double Net { get { return CutVolume * Swell - FillVolume; } }
    }

    public static class CutFill
    {
        public const long MaxCells = 600000;
        public static bool InPolygon(IList<double[]> poly, double x, double y)
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i][1] > y) != (poly[j][1] > y) && x < (poly[j][0] - poly[i][0]) * (y - poly[i][1]) / (poly[j][1] - poly[i][1]) + poly[i][0]) c = !c;
            return c;
        }
        /// <summary>Samples the centre of each cell; existing and proposed give Z (NaN = no data). Cells with no data in either, or outside 'inside', are skipped.</summary>
        public static CutFillResult Compute(Func<double, double, double> existing, Func<double, double, double> proposed, double minx, double miny, double maxx, double maxy, double cell, Func<double, double, bool> inside, double swell)
        {
            if (cell <= 0) throw new ArgumentException("cell");
            while ((long)((maxx - minx) / cell + 1) * (long)((maxy - miny) / cell + 1) > MaxCells) cell *= 1.5;
            var r = new CutFillResult { Cell = cell, Swell = swell <= 0 ? 1 : swell }; double ca = cell * cell;
            int nx = (int)Math.Ceiling((maxx - minx) / cell - 1e-9), ny = (int)Math.Ceiling((maxy - miny) / cell - 1e-9); if (nx < 1) nx = 1; if (ny < 1) ny = 1;
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
                {
                    r.Cells++; double x = minx + (i + 0.5) * cell, y = miny + (j + 0.5) * cell;
                    if (inside != null && !inside(x, y)) continue;
                    double e = existing(x, y), p = proposed(x, y); if (double.IsNaN(e) || double.IsNaN(p)) continue;
                    r.UsedCells++; double dz = e - p;
                    if (dz > 0) { r.CutVolume += dz * ca; r.CutArea += ca; } else if (dz < 0) { r.FillVolume += -dz * ca; r.FillArea += ca; }
                }
            return r;
        }
    }
}
