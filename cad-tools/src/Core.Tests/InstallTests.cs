using System;
using System.IO;
using System.IO.Compression;
using BPlus.LiveMap.Setup;
using Xunit;

public class InstallTests
{
    static MemoryStream Zip(bool withDll, bool evil = false)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            Put(z, "PackageContents.xml", "<ApplicationPackage><Components><ComponentEntry ModuleName=\"./Contents/net48/a.dll\"/></Components></ApplicationPackage>");
            if (withDll) Put(z, "Contents/net48/a.dll", "x");
            if (evil) Put(z, "../evil.txt", "x");
        }
        ms.Position = 0; return ms;
    }
    static void Put(ZipArchive z, string n, string c) { var e = z.CreateEntry(n); using (var w = new StreamWriter(e.Open())) w.Write(c); }
    static string Tmp() { return Path.Combine(Path.GetTempPath(), "bpt" + Guid.NewGuid().ToString("N"), "BPlusCadTools.bundle"); }

    [Fact] public void FreshInstallAndUninstall()
    {
        var t = Tmp(); Install(Zip(true), t);
        Assert.True(File.Exists(Path.Combine(t, "Contents", "net48", "a.dll")));
        Assert.True(InstallLogic.Uninstall(t, s => { })); Assert.False(Directory.Exists(t));
        Assert.False(InstallLogic.Uninstall(t, s => { }));
    }
    [Fact] public void UpgradeReplacesOld()
    {
        var t = Tmp(); Install(Zip(true), t); File.WriteAllText(Path.Combine(t, "stale.txt"), "s");
        Install(Zip(true), t);
        Assert.False(File.Exists(Path.Combine(t, "stale.txt"))); Assert.False(Directory.Exists(t + ".old")); Assert.False(Directory.Exists(t + ".new"));
    }
    [Fact] public void BadPackageKeepsExistingInstall()
    {
        var t = Tmp(); Install(Zip(true), t); File.WriteAllText(Path.Combine(t, "keep.txt"), "k");
        Assert.Throws<InvalidOperationException>(() => Install(Zip(false), t));
        Assert.True(File.Exists(Path.Combine(t, "keep.txt"))); Assert.False(Directory.Exists(t + ".new"));
    }
    [Fact] public void ZipSlipRejected()
    {
        var t = Tmp(); Assert.Throws<InvalidOperationException>(() => Install(Zip(true, true), t));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(t), "evil.txt")));
    }
    [Fact] public void LegacyBundleIsRemovedBeforeInstall()
    {
        var t = Tmp(); var legacy = Path.Combine(Path.GetDirectoryName(t), "BPlusLiveMap.bundle"); Directory.CreateDirectory(legacy); File.WriteAllText(Path.Combine(legacy, "x.txt"), "old");
        Assert.True(InstallLogic.RemoveLegacy(legacy, x => { })); Assert.False(Directory.Exists(legacy)); Assert.False(Directory.Exists(legacy + ".removed"));
        Install(Zip(true), t); Assert.True(Directory.Exists(t));
        Assert.False(InstallLogic.RemoveLegacy(legacy, x => { }));
    }
    [Fact] public void NewBundleNameDiffersFromLegacy() { Assert.Equal("BPlusCadTools.bundle", InstallLogic.BundleName); Assert.EndsWith("BPlusLiveMap.bundle", InstallLogic.LegacyTarget()); }
    static void Install(Stream s, string t) { InstallLogic.Install(s, t, x => { }); }
    [Fact] public void LockedFolderFallsBackToFileByFile()
    {
        var t = Tmp(); Install(Zip(true), t);
        File.WriteAllText(Path.Combine(t, "Contents", "net48", "a.dll"), "OLD"); File.WriteAllText(Path.Combine(t, "stale.old-bp"), "z");
        InstallLogic.ForceDirSwapFail = true;
        try { Install(Zip(true), t); } finally { InstallLogic.ForceDirSwapFail = false; }
        Assert.Equal("x", File.ReadAllText(Path.Combine(t, "Contents", "net48", "a.dll")));
        Assert.False(Directory.Exists(t + ".new")); Assert.False(Directory.Exists(t + ".old"));
    }
    [Fact] public void LeftoverNewFolderIsCleared()
    {
        var t = Tmp(); Directory.CreateDirectory(t + ".new"); File.WriteAllText(Path.Combine(t + ".new", "junk"), "j");
        Install(Zip(true), t); Assert.True(File.Exists(Path.Combine(t, "Contents", "net48", "a.dll"))); Assert.False(Directory.Exists(t + ".new"));
    }
}

public class ProxyClientTests
{
    sealed class Fake : System.Net.Http.HttpMessageHandler
    {
        public System.Collections.Generic.List<string> Urls = new System.Collections.Generic.List<string>();
        public int TileStatus = 200;
        protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage r, System.Threading.CancellationToken ct)
        {
            Urls.Add(r.Method + " " + r.RequestUri);
            var res = new System.Net.Http.HttpResponseMessage((System.Net.HttpStatusCode)(r.RequestUri.AbsolutePath.Contains("createSession") ? 200 : TileStatus));
            if (r.RequestUri.AbsolutePath.Contains("createSession")) res.Content = new System.Net.Http.StringContent("{\"session\":\"S1\",\"expiry\":\"4102444800\"}");
            else if (TileStatus == 200) res.Content = new System.Net.Http.ByteArrayContent(new byte[] { 1, 2, 3 });
            else res.Content = new System.Net.Http.StringContent("{\"error\":{\"code\":\"daily_cap\",\"message\":\"Today's shared map limit has been reached.\"}}");
            return System.Threading.Tasks.Task.FromResult(res);
        }
    }
    static BPlus.LiveMap.Core.GoogleTiles Make(Fake f, string host)
    {
        return new BPlus.LiveMap.Core.GoogleTiles(ct => System.Threading.Tasks.Task.FromResult("TOKEN"), () => host,
            new BPlus.LiveMap.Core.TileCache(10), new BPlus.LiveMap.Core.UsageCounter(System.IO.Path.GetTempFileName()), f);
    }
    [Fact] public void ProxyModeSendsTokenToProxyHostOnly()
    {
        var f = new Fake(); var g = Make(f, "https://map.example.workers.dev/");
        var r = g.GetTileAsync(BPlus.LiveMap.Core.MapKind.Satellite, new BPlus.LiveMap.Core.TileId(10, 1, 2), System.Threading.CancellationToken.None).Result;
        Assert.NotNull(r.Data);
        Assert.All(f.Urls, u => Assert.Contains("https://map.example.workers.dev/", u));
        Assert.All(f.Urls, u => Assert.Contains("key=TOKEN", u));
        Assert.DoesNotContain(f.Urls, u => u.Contains("googleapis.com"));
    }
    [Fact] public void DirectModeUsesGoogleHost()
    {
        var f = new Fake(); var g = Make(f, "");
        g.GetTileAsync(BPlus.LiveMap.Core.MapKind.Satellite, new BPlus.LiveMap.Core.TileId(10, 1, 2), System.Threading.CancellationToken.None).Wait();
        Assert.All(f.Urls, u => Assert.Contains("https://tile.googleapis.com/", u));
    }
    [Fact] public void ServerLimitMessageIsShownAsIs()
    {
        var f = new Fake { TileStatus = 429 }; var g = Make(f, "https://x.example/");
        var r = g.GetTileAsync(BPlus.LiveMap.Core.MapKind.Satellite, new BPlus.LiveMap.Core.TileId(10, 1, 2), System.Threading.CancellationToken.None).Result;
        Assert.Null(r.Data); Assert.Equal("Today's shared map limit has been reached.", r.Error);
    }
}
