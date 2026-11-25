using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BPlus.LiveMap.Core
{
    public enum MapKind { Satellite, Roadmap, Hybrid }

    public sealed class TileResult
    {
        public byte[] Data; public string CacheControl; public bool FromCache; public int StatusCode; public string Error;
    }

    /// <summary>Client for Google's Map Tiles API (2D tiles). Needs the user's own API key.</summary>
    public sealed class GoogleTiles
    {
        const string Host = "https://tile.googleapis.com";
        readonly HttpClient _http; readonly Func<string> _key; readonly TileCache _cache; readonly UsageCounter _usage;
        string _session; DateTime _sessionExpiry = DateTime.MinValue; MapKind _sessionKind;
        public string Copyright { get; private set; }
        public int MaxZoom { get; private set; }

        public GoogleTiles(Func<string> keyProvider, TileCache cache, UsageCounter usage, HttpMessageHandler handler = null)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            _http = handler == null ? new HttpClient() : new HttpClient(handler);
            _http.Timeout = TimeSpan.FromSeconds(20);
            _key = keyProvider; _cache = cache; _usage = usage; MaxZoom = 21; Copyright = "";
        }

        public static string SessionBody(MapKind k)
        {
            string type = k == MapKind.Roadmap ? "roadmap" : "satellite";
            var sb = new StringBuilder("{\"mapType\":\"" + type + "\",\"language\":\"en-US\",\"region\":\"SA\"");
            if (k == MapKind.Hybrid) sb.Append(",\"layerTypes\":[\"layerRoadmap\"],\"overlay\":false");
            sb.Append("}");
            return sb.ToString();
        }

        public static string ParseSession(string json) { var m = Regex.Match(json ?? "", "\"session\"\\s*:\\s*\"([^\"]+)\""); return m.Success ? m.Groups[1].Value : null; }
        public static DateTime ParseExpiry(string json)
        {
            var m = Regex.Match(json ?? "", "\"expiry\"\\s*:\\s*\"?(\\d+)\"?");
            long s; if (m.Success && long.TryParse(m.Groups[1].Value, out s)) return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(s);
            return DateTime.UtcNow.AddHours(12);
        }
        public static string ParseCopyright(string json) { var m = Regex.Match(json ?? "", "\"copyright\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""); return m.Success ? Regex.Unescape(m.Groups[1].Value) : ""; }
        public static int ParseMaxZoom(string json)
        {
            int best = 0;
            foreach (Match m in Regex.Matches(json ?? "", "\"maxZoom\"\\s*:\\s*(\\d+)")) { int v = int.Parse(m.Groups[1].Value); if (v > best) best = v; }
            return best;
        }

        public async Task<string> EnsureSessionAsync(MapKind kind, CancellationToken ct)
        {
            string key = _key();
            if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("No Google API key is saved. Run BPGMAPSET to add it.");
            if (_session != null && kind == _sessionKind && DateTime.UtcNow < _sessionExpiry.AddMinutes(-5)) return _session;
            var req = new HttpRequestMessage(HttpMethod.Post, Host + "/v1/createSession?key=" + Uri.EscapeDataString(key))
            { Content = new StringContent(SessionBody(kind), Encoding.UTF8, "application/json") };
            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(Explain((int)resp.StatusCode, body));
            _session = ParseSession(body); _sessionExpiry = ParseExpiry(body); _sessionKind = kind;
            if (_session == null) throw new InvalidOperationException("Google did not return a session.");
            return _session;
        }

        public static string Explain(int status, string body)
        {
            string msg = Regex.Match(body ?? "", "\"message\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            if (status == 403 || status == 400) return "Google refused the request (" + status + "). " + (msg.Length > 0 ? msg + " " : "") + "Check that the key is correct, Map Tiles API is enabled, and billing is on.";
            if (status == 429) return "Google says too many requests (429). Wait a minute and refresh.";
            return "Google returned HTTP " + status + ". " + msg;
        }

        public async Task UpdateViewportAsync(MapKind kind, int zoom, double south, double west, double north, double east, CancellationToken ct)
        {
            try
            {
                string s = await EnsureSessionAsync(kind, ct).ConfigureAwait(false);
                string url = Host + "/tile/v1/viewport?session=" + s + "&key=" + Uri.EscapeDataString(_key()) + "&zoom=" + zoom
                    + "&north=" + north.ToString("R", CultureInfo.InvariantCulture) + "&south=" + south.ToString("R", CultureInfo.InvariantCulture)
                    + "&east=" + east.ToString("R", CultureInfo.InvariantCulture) + "&west=" + west.ToString("R", CultureInfo.InvariantCulture);
                var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    Copyright = ParseCopyright(body);
                    int mz = ParseMaxZoom(body); if (mz > 0) MaxZoom = mz;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        public async Task<TileResult> GetTileAsync(MapKind kind, TileId id, CancellationToken ct)
        {
            byte[] cached;
            if (_cache.TryGetFresh(Key(kind, id), out cached)) return new TileResult { Data = cached, FromCache = true, StatusCode = 200 };
            string s = await EnsureSessionAsync(kind, ct).ConfigureAwait(false);
            string url = Host + "/v1/2dtiles/" + id.Z + "/" + id.X + "/" + id.Y + "?session=" + s + "&key=" + Uri.EscapeDataString(_key());
            var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            _usage.Add(1);
            if (!resp.IsSuccessStatusCode)
            {
                string b = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new TileResult { StatusCode = (int)resp.StatusCode, Error = Explain((int)resp.StatusCode, b) };
            }
            byte[] data = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            string cc = resp.Headers.CacheControl != null ? resp.Headers.CacheControl.ToString() : null;
            _cache.Put(Key(kind, id), data, cc);
            return new TileResult { Data = data, CacheControl = cc, StatusCode = 200 };
        }

        // The cache keys include the map kind so satellite and roadmap tiles never mix.
        static TileId Key(MapKind k, TileId id) { return new TileId(id.Z + 100 * (int)k, id.X, id.Y); }
    }
}
