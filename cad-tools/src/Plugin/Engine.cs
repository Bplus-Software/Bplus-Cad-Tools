using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using BPlus.LiveMap.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BPlus.LiveMap
{
    sealed class ViewInfo
    {
        public Point3d[] Corners = new Point3d[4]; // DCS lower-left, lower-right, upper-right, upper-left, in WCS
        public Vector3d RightDir; public double Height, Width; public int ScreenH; public string Signature;
    }

    sealed class Plan
    {
        public MapKind Kind; public int Zoom; public int Opacity; public bool Flip;
        public List<TileId> Tiles = new List<TileId>();
        public Dictionary<TileId, Tuple<Point3d, Vector3d, Vector3d>> Place = new Dictionary<TileId, Tuple<Point3d, Vector3d, Vector3d>>();
        public double South, West, North, East; public ViewInfo View; public CancellationTokenSource Cts;
    }

    static class Engine
    {
        public static readonly TileCache Cache = new TileCache(400);
        public static readonly UsageCounter Usage = new UsageCounter(Paths.Usage);
        public static readonly GoogleTiles Client = new GoogleTiles(Credential, () => Config.UseOwnKey ? "" : Config.ServerUrl, Cache, Usage);
        static readonly System.Net.Http.HttpClient Reg = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        static readonly SemaphoreSlim RegLock = new SemaphoreSlim(1, 1);

        // Shared mode: the install token from the BPlus map server stands in for the Google key. Own mode: the user's own key.
        static async Task<string> Credential(CancellationToken ct)
        {
            if (Config.UseOwnKey) return KeyStore.Get();
            string t = Config.InstallToken; if (t.Length > 0) return t;
            await RegLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                t = Config.InstallToken; if (t.Length > 0) return t;
                string url = Config.ServerUrl; if (string.IsNullOrEmpty(url) || url.Contains("@@")) throw new InvalidOperationException("This build has no map server address. Use your own Google key in BPGMAPSET.");
                var resp = await Reg.PostAsync(url.TrimEnd('/') + "/v1/register", new System.Net.Http.StringContent("{}", System.Text.Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(GoogleTiles.Explain((int)resp.StatusCode, body));
                var m = System.Text.RegularExpressions.Regex.Match(body, "\"token\"\\s*:\\s*\"([0-9a-f.]+)\"");
                if (!m.Success) throw new InvalidOperationException("The map server gave an unexpected answer.");
                Config.InstallToken = m.Groups[1].Value; Log.Write("Registered this install with the map server.");
                return Config.InstallToken;
            }
            finally { RegLock.Release(); }
        }
        static SynchronizationContext _ui;
        static MapDrawable _drawable; static bool _transientAdded;
        static readonly Dictionary<Database, CsProvider> Providers = new Dictionary<Database, CsProvider>();
        static string _sig = "", _renderedSig = ""; static DateTime _changedAt = DateTime.MinValue;
        static Plan _current; static bool _busy; static DateTime _lastMsg = DateTime.MinValue; static bool _warned;
        public static bool On { get; private set; }
        public static string LastError = "";

        public static void Init()
        {
            _ui = SynchronizationContext.Current ?? new SynchronizationContext();
            AcApp.Idle += OnIdle;
            AcApp.DocumentManager.DocumentActivated += (s, e) => { _renderedSig = ""; _sig = ""; };
        }

        public static void Shutdown() { try { AcApp.Idle -= OnIdle; SetOn(false); } catch { } }

        public static CsProvider ProviderFor(Document doc) { CsProvider p; return Providers.TryGetValue(doc.Database, out p) ? p : null; }
        public static CsProvider EnsureProvider(Document doc, bool interactive)
        {
            var p = ProviderFor(doc);
            if (p == null) { p = DrawingCs.Get(doc.Database); if (p != null) Providers[doc.Database] = p; }
            if (p == null && interactive)
            {
                int epsg = CsPicker.Ask();
                if (epsg > 0)
                {
                    using (doc.LockDocument()) DrawingCs.SaveEpsg(doc.Database, epsg);
                    p = DrawingCs.Get(doc.Database); if (p != null) Providers[doc.Database] = p;
                }
            }
            return p;
        }
        public static void ForgetProvider(Document doc) { Providers.Remove(doc.Database); _renderedSig = ""; }
        public static void SetProvider(Document doc, CsProvider p) { Providers[doc.Database] = p; _renderedSig = ""; }

        public static void SetOn(bool on)
        {
            On = on; Config.Enabled = on;
            if (!on)
            {
                if (_current != null && _current.Cts != null) _current.Cts.Cancel();
                RemoveTransient();
            }
            else { _renderedSig = ""; _sig = ""; }
        }

        static void RemoveTransient()
        {
            try
            {
                if (_drawable != null && _transientAdded)
                    TransientManager.CurrentTransientManager.EraseTransient(_drawable, new IntegerCollection());
            }
            catch (Exception e) { Log.Write("Erase transient: " + e.Message); }
            _transientAdded = false;
            if (_drawable != null) { foreach (var t in _drawable.Tiles) t.Dispose(); _drawable.Tiles.Clear(); }
        }

        // ---------- view ----------
        public static ViewInfo ReadView(Document doc, out string problem)
        {
            problem = null;
            ViewTableRecord v;
            try { v = doc.Editor.GetCurrentView(); } catch (Exception e) { problem = "Could not read the view: " + e.Message; return null; }
            if (!v.ViewDirection.IsCodirectionalTo(Vector3d.ZAxis)) { problem = "The view is not a plan view. Run PLAN, then World, to see the live map."; return null; }
            var m = Matrix3d.Rotation(-v.ViewTwist, v.ViewDirection, v.Target) * Matrix3d.Displacement(v.Target - Point3d.Origin) * Matrix3d.PlaneToWorld(v.ViewDirection);
            double cx = v.CenterPoint.X, cy = v.CenterPoint.Y, hw = v.Width / 2, hh = v.Height / 2;
            var vi = new ViewInfo { Height = v.Height, Width = v.Width };
            vi.Corners[0] = new Point3d(cx - hw, cy - hh, 0).TransformBy(m);
            vi.Corners[1] = new Point3d(cx + hw, cy - hh, 0).TransformBy(m);
            vi.Corners[2] = new Point3d(cx + hw, cy + hh, 0).TransformBy(m);
            vi.Corners[3] = new Point3d(cx - hw, cy + hh, 0).TransformBy(m);
            vi.RightDir = (vi.Corners[1] - vi.Corners[0]).GetNormal();
            try { var s = (Point2d)AcApp.GetSystemVariable("SCREENSIZE"); vi.ScreenH = (int)Math.Max(200, s.Y); } catch { vi.ScreenH = 900; }
            vi.Signature = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}|{1:F3},{2:F3}|{3:F3}|{4:F3}|{5}|{6}|{7}|{8}", doc.Name, vi.Corners[0].X, vi.Corners[0].Y, vi.Width, vi.Height, vi.RightDir.Y.ToString("F4"), Config.Kind, Config.OpacityPercent, Config.FlipRows);
            return vi;
        }

        static void Say(string msg, bool force = false)
        {
            if (!force && (DateTime.Now - _lastMsg).TotalSeconds < 20) return;
            _lastMsg = DateTime.Now;
            try { var d = AcApp.DocumentManager.MdiActiveDocument; if (d != null) d.Editor.WriteMessage("\n[BPlus map] " + msg + "\n"); } catch { }
            Log.Write(msg);
        }

        static void OnIdle(object sender, EventArgs e)
        {
            if (!On || _busy) return;
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument; if (doc == null) return;
                var prov = ProviderFor(doc) ?? EnsureProvider(doc, false); if (prov == null) { Say("This drawing has no coordinate system yet. Run BPGMAPCS to choose one."); return; }
                string problem; var vi = ReadView(doc, out problem);
                if (vi == null) { if (problem != null) Say(problem); return; }
                if (vi.Signature != _sig) { _sig = vi.Signature; _changedAt = DateTime.Now; return; }
                if (vi.Signature == _renderedSig || (DateTime.Now - _changedAt).TotalMilliseconds < 350) return;
                _renderedSig = vi.Signature;
                Refresh(doc, prov, vi);
            }
            catch (Exception ex) { Log.Write("Idle: " + ex); Say("Problem while updating the map: " + ex.Message); }
        }

        public static void RefreshNow()
        {
            _renderedSig = ""; _sig = "";
        }

        // ---------- plan (UI thread) ----------
        static Plan MakePlan(CsProvider prov, ViewInfo vi)
        {
            var p = new Plan { Kind = Config.Kind, Opacity = Config.OpacityPercent, Flip = Config.FlipRows, View = vi };
            double minLon = 999, maxLon = -999, minLat = 999, maxLat = -999;
            var pts = new List<Point3d>(vi.Corners);
            pts.Add(Mid(vi.Corners[0], vi.Corners[1])); pts.Add(Mid(vi.Corners[1], vi.Corners[2])); pts.Add(Mid(vi.Corners[2], vi.Corners[3])); pts.Add(Mid(vi.Corners[3], vi.Corners[0]));
            foreach (var q in pts)
            {
                double lon, lat; prov.ToLonLat(q.X, q.Y, out lon, out lat);
                minLon = Math.Min(minLon, lon); maxLon = Math.Max(maxLon, lon); minLat = Math.Min(minLat, lat); maxLat = Math.Max(maxLat, lat);
            }
            double padX = (maxLon - minLon) * 0.03, padY = (maxLat - minLat) * 0.03;
            p.West = minLon - padX; p.East = maxLon + padX; p.South = minLat - padY; p.North = maxLat + padY;
            double cLat = (minLat + maxLat) / 2;
            double groundH = Haversine(Mid2(vi.Corners[0], vi.Corners[1]), Mid2(vi.Corners[3], vi.Corners[2]), prov);
            double mpp = groundH / vi.ScreenH;
            int zoom = Mercator.ZoomFor(mpp, cLat, Client.MaxZoom);
            p.Tiles = Mercator.Cover(p.West, p.South, p.East, p.North, ref zoom, 48);
            p.Zoom = zoom;
            foreach (var t in p.Tiles)
            {
                double w = Mercator.TileLon(t.X, t.Z), e2 = Mercator.TileLon(t.X + 1, t.Z), n = Mercator.TileLat(t.Y, t.Z), s = Mercator.TileLat(t.Y + 1, t.Z);
                double x0, y0, x1, y1, x2, y2;
                prov.FromLonLat(w, s, out x0, out y0); prov.FromLonLat(e2, s, out x1, out y1); prov.FromLonLat(w, n, out x2, out y2);
                p.Place[t] = Tuple.Create(new Point3d(x0, y0, 0), new Vector3d(x1 - x0, y1 - y0, 0), new Vector3d(x2 - x0, y2 - y0, 0));
            }
            return p;
        }

        static Point3d Mid(Point3d a, Point3d b) { return new Point3d((a.X + b.X) / 2, (a.Y + b.Y) / 2, 0); }
        static Point3d Mid2(Point3d a, Point3d b) { return Mid(a, b); }
        static double Haversine(Point3d a, Point3d b, CsProvider prov)
        {
            double lo1, la1, lo2, la2; prov.ToLonLat(a.X, a.Y, out lo1, out la1); prov.ToLonLat(b.X, b.Y, out lo2, out la2);
            double R = 6371008.8, dLa = (la2 - la1) * Math.PI / 180, dLo = (lo2 - lo1) * Math.PI / 180;
            double h = Math.Sin(dLa / 2) * Math.Sin(dLa / 2) + Math.Cos(la1 * Math.PI / 180) * Math.Cos(la2 * Math.PI / 180) * Math.Sin(dLo / 2) * Math.Sin(dLo / 2);
            return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(h)));
        }

        static void Refresh(Document doc, CsProvider prov, ViewInfo vi)
        {
            Plan plan;
            try { plan = MakePlan(prov, vi); } catch (Exception e) { Say("Could not work out the map area: " + e.Message, true); return; }
            if (Config.UseOwnKey && string.IsNullOrEmpty(KeyStore.Get())) { Say("No Google API key is saved yet. Run BPGMAPSET and paste your key.", true); On = false; Config.Enabled = false; RemoveTransient(); return; }
            if (_current != null && _current.Cts != null) _current.Cts.Cancel();
            plan.Cts = new CancellationTokenSource(); _current = plan; _busy = true;
            if (!_warned && Usage.ThisMonth >= Config.WarnAt) { _warned = true; Say("Google tile requests this month: " + Usage.ThisMonth + " (your warning level is " + Config.WarnAt + ").", true); }
            var ct = plan.Cts.Token; var kind = plan.Kind;
            Task.Run(async () =>
            {
                var got = new Dictionary<TileId, byte[]>(); string err = null;
                try
                {
                    await Client.UpdateViewportAsync(kind, plan.Zoom, plan.South, plan.West, plan.North, plan.East, ct).ConfigureAwait(false);
                    var sem = new SemaphoreSlim(6);
                    var tasks = plan.Tiles.Select(async t =>
                    {
                        await sem.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            var r = await Client.GetTileAsync(kind, t, ct).ConfigureAwait(false);
                            if (r.Data != null) lock (got) got[t] = r.Data; else if (err == null) err = r.Error;
                        }
                        finally { sem.Release(); }
                    }).ToArray();
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { _ui.Post(_ => { _busy = false; }, null); return; }
                catch (Exception e) { err = e.Message; }
                _ui.Post(_ => { _busy = false; if (!ct.IsCancellationRequested) Apply(plan, got, err); }, null);
            });
        }

        static void Apply(Plan plan, Dictionary<TileId, byte[]> got, string err)
        {
            if (!On) return;
            if (err != null && got.Count == 0) { LastError = err; Say(err, true); return; }
            try
            {
                if (_drawable == null) _drawable = new MapDrawable();
                var old = new List<PlacedTile>(_drawable.Tiles);
                var fresh = new List<PlacedTile>();
                foreach (var t in plan.Tiles)
                {
                    byte[] data; if (!got.TryGetValue(t, out data)) continue;
                    var pl = plan.Place[t];
                    try { fresh.Add(PlacedTile.From(data, pl.Item1, pl.Item2, pl.Item3, plan.Opacity, plan.Flip)); }
                    catch (Exception e) { Log.Write("Tile " + t + " could not be decoded: " + e.Message); }
                }
                _drawable.Tiles = fresh;
                var vi = plan.View;
                string cr = Client.Copyright;
                _drawable.Attribution = "Google Maps" + (string.IsNullOrEmpty(cr) ? "" : "  |  " + cr);
                _drawable.AttrHeight = vi.Height * 0.014;
                _drawable.AttrDir = vi.RightDir;
                var up = new Vector3d(-vi.RightDir.Y, vi.RightDir.X, 0);
                _drawable.AttrPos = vi.Corners[0] + vi.RightDir * (vi.Width * 0.01) + up * (vi.Height * 0.012);
                if (!_transientAdded)
                {
                    TransientManager.CurrentTransientManager.AddTransient(_drawable, TransientDrawingMode.Main, 128, new IntegerCollection());
                    _transientAdded = true;
                }
                else TransientManager.CurrentTransientManager.UpdateTransient(_drawable, new IntegerCollection());
                foreach (var t in old) t.Dispose();
                if (err != null) Say("Some tiles failed: " + err);
                var dd = AcApp.DocumentManager.MdiActiveDocument; if (dd != null) dd.Editor.UpdateScreen();
            }
            catch (Exception e) { Log.Write("Apply: " + e); Say("Could not draw the map: " + e.Message, true); }
        }

        // ---------- test card (orientation / draw order check) ----------
        public static void ShowTestCard(Document doc, ViewInfo vi)
        {
            using (var bmp = new System.Drawing.Bitmap(256, 256))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.FromArgb(255, 30, 120, 200));
                    using (var f = new System.Drawing.Font("Arial", 28, System.Drawing.FontStyle.Bold))
                    {
                        g.DrawString("TOP", f, System.Drawing.Brushes.White, 80, 8);
                        g.DrawString("BOTTOM", f, System.Drawing.Brushes.Yellow, 40, 210);
                        g.DrawString("L", f, System.Drawing.Brushes.White, 6, 110);
                        g.DrawString("R", f, System.Drawing.Brushes.White, 220, 110);
                    }
                }
                var ms = new System.IO.MemoryStream(); bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                var centre = new Point3d((vi.Corners[0].X + vi.Corners[2].X) / 2, (vi.Corners[0].Y + vi.Corners[2].Y) / 2, 0);
                double size = Math.Min(vi.Width, vi.Height) * 0.5;
                var up = new Vector3d(-vi.RightDir.Y, vi.RightDir.X, 0);
                var origin = centre - vi.RightDir * (size / 2) - up * (size / 2);
                var dr = new MapDrawable();
                dr.Tiles.Add(PlacedTile.From(ms.ToArray(), origin, vi.RightDir * size, up * size, 100, Config.FlipRows));
                var tm = TransientManager.CurrentTransientManager;
                tm.AddTransient(dr, TransientDrawingMode.Main, 128, new IntegerCollection());
                doc.Editor.UpdateScreen();
                var o = new PromptStringOptions("\nTest card shown. Is the word TOP at the top, and do your drawing lines show over the blue card? Press Enter to remove it") { AllowSpaces = true };
                doc.Editor.GetString(o);
                tm.EraseTransient(dr, new IntegerCollection());
                foreach (var t in dr.Tiles) t.Dispose();
            }
        }
    }
}
