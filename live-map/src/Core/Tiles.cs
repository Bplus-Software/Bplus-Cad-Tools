using System;
using System.Collections.Generic;

namespace BPlus.LiveMap.Core
{
    public struct TileId : IEquatable<TileId>
    {
        public readonly int Z, X, Y;
        public TileId(int z, int x, int y) { Z = z; X = x; Y = y; }
        public bool Equals(TileId o) { return Z == o.Z && X == o.X && Y == o.Y; }
        public override bool Equals(object o) { return o is TileId && Equals((TileId)o); }
        public override int GetHashCode() { unchecked { return (Z * 397 ^ X) * 397 ^ Y; } }
        public override string ToString() { return Z + "/" + X + "/" + Y; }
    }

    /// <summary>Web Mercator tile scheme (256 px tiles, as used by Google 2D tiles).</summary>
    public static class Mercator
    {
        public const double MaxLat = 85.0511287798;
        public static double TileXf(double lon, int z) { return (lon + 180.0) / 360.0 * (1 << z); }
        public static double TileYf(double lat, int z)
        {
            lat = Math.Max(-MaxLat, Math.Min(MaxLat, lat));
            double s = Math.Sin(lat * Math.PI / 180);
            return (0.5 - Math.Log((1 + s) / (1 - s)) / (4 * Math.PI)) * (1 << z);
        }
        public static double TileLon(double x, int z) { return x / (1 << z) * 360.0 - 180.0; }
        public static double TileLat(double y, int z)
        {
            double n = Math.PI - 2 * Math.PI * y / (1 << z);
            return 180.0 / Math.PI * Math.Atan(0.5 * (Math.Exp(n) - Math.Exp(-n)));
        }
        /// <summary>Ground metres per image pixel for 256 px tiles at a latitude.</summary>
        public static double MetresPerPixel(double lat, int z) { return 156543.03392804097 * Math.Cos(lat * Math.PI / 180) / (1 << z); }

        /// <summary>Zoom whose pixel size best matches the screen pixel size (a little finer, never blurrier than needed).</summary>
        public static int ZoomFor(double metresPerScreenPixel, double lat, int maxZoom)
        {
            if (metresPerScreenPixel <= 0) return maxZoom;
            double z = Math.Log(156543.03392804097 * Math.Cos(lat * Math.PI / 180) / metresPerScreenPixel, 2);
            int zi = (int)Math.Round(z);
            return Math.Max(1, Math.Min(maxZoom, zi));
        }

        /// <summary>Tiles covering the lon/lat bounding box. Lowers the zoom until at most maxTiles are needed.</summary>
        public static List<TileId> Cover(double minLon, double minLat, double maxLon, double maxLat, ref int zoom, int maxTiles)
        {
            for (; ; )
            {
                int x0 = (int)Math.Floor(TileXf(minLon, zoom)), x1 = (int)Math.Floor(TileXf(maxLon, zoom));
                int y0 = (int)Math.Floor(TileYf(maxLat, zoom)), y1 = (int)Math.Floor(TileYf(minLat, zoom));
                long count = (long)(x1 - x0 + 1) * (y1 - y0 + 1);
                if (count <= maxTiles || zoom <= 1)
                {
                    var l = new List<TileId>();
                    int n = 1 << zoom;
                    for (int y = Math.Max(0, y0); y <= Math.Min(n - 1, y1); y++)
                        for (int x = x0; x <= x1; x++) l.Add(new TileId(zoom, ((x % n) + n) % n, y));
                    return l;
                }
                zoom--;
            }
        }
    }
}
