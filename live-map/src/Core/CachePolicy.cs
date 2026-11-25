using System;
using System.Collections.Generic;
using System.Globalization;

namespace BPlus.LiveMap.Core
{
    /// <summary>
    /// Memory-only tile cache. A tile is kept only for the time Google's own Cache-Control header allows
    /// (max-age, plus stale-while-revalidate only as the header states it). No header, no-store or max-age=0
    /// means the tile is not kept. Nothing is ever written to disk.
    /// </summary>
    public sealed class TileCache
    {
        sealed class Entry { public byte[] Data; public DateTime FreshUntilUtc; public DateTime StaleUntilUtc; public long Seq; }
        readonly Dictionary<TileId, Entry> _map = new Dictionary<TileId, Entry>();
        readonly int _max; long _seq;
        readonly Func<DateTime> _now;
        public TileCache(int maxEntries = 400, Func<DateTime> now = null) { _max = maxEntries; _now = now ?? (() => DateTime.UtcNow); }

        public static bool TryParse(string cacheControl, out TimeSpan maxAge, out TimeSpan staleWhileRevalidate)
        {
            maxAge = TimeSpan.Zero; staleWhileRevalidate = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(cacheControl)) return false;
            bool any = false;
            foreach (var raw in cacheControl.Split(','))
            {
                var p = raw.Trim().ToLowerInvariant();
                if (p == "no-store" || p == "no-cache") { maxAge = TimeSpan.Zero; staleWhileRevalidate = TimeSpan.Zero; return false; }
                if (p.StartsWith("max-age=")) { long s; if (long.TryParse(p.Substring(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out s) && s > 0) { maxAge = TimeSpan.FromSeconds(s); any = true; } }
                else if (p.StartsWith("stale-while-revalidate=")) { long s; if (long.TryParse(p.Substring(23), NumberStyles.Integer, CultureInfo.InvariantCulture, out s) && s > 0) staleWhileRevalidate = TimeSpan.FromSeconds(s); }
                else if (p == "must-revalidate") { staleWhileRevalidate = TimeSpan.Zero; }
            }
            return any;
        }

        public void Put(TileId id, byte[] data, string cacheControl)
        {
            TimeSpan age, swr;
            if (!TryParse(cacheControl, out age, out swr)) { lock (_map) _map.Remove(id); return; }
            var now = _now();
            lock (_map)
            {
                _map[id] = new Entry { Data = data, FreshUntilUtc = now + age, StaleUntilUtc = now + age + swr, Seq = ++_seq };
                if (_map.Count > _max)
                {
                    TileId oldest = default(TileId); long best = long.MaxValue;
                    foreach (var kv in _map) if (kv.Value.Seq < best) { best = kv.Value.Seq; oldest = kv.Key; }
                    _map.Remove(oldest);
                }
            }
        }

        /// <summary>Returns a tile only while it is fresh. Stale tiles are dropped, never served.</summary>
        public bool TryGetFresh(TileId id, out byte[] data)
        {
            lock (_map)
            {
                Entry e;
                if (_map.TryGetValue(id, out e))
                {
                    if (_now() < e.FreshUntilUtc) { data = e.Data; return true; }
                    if (_now() >= e.StaleUntilUtc) _map.Remove(id);
                }
            }
            data = null; return false;
        }

        public void Clear() { lock (_map) _map.Clear(); }
        public int Count { get { lock (_map) return _map.Count; } }
    }
}
