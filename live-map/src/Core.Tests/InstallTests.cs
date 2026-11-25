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
    static string Tmp() { return Path.Combine(Path.GetTempPath(), "bpt" + Guid.NewGuid().ToString("N"), "BPlusLiveMap.bundle"); }

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
    static void Install(Stream s, string t) { InstallLogic.Install(s, t, x => { }); }
}
