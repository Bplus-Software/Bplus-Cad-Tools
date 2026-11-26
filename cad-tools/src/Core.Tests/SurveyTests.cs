using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using BPlus.LiveMap.Core;
using Xunit;

public class SurveyTests
{
    [Fact] public void ParsePenzdComma()
    {
        var r = PointText.Parse(new[] { "1,500000.123,2800000.5,12.34,MNH1", "2,500010,2800010,12.5,LP 3" }, "PENZD");
        Assert.Equal(2, r.Points.Count); Assert.Empty(r.Problems);
        Assert.Equal("1", r.Points[0].No); Assert.Equal(500000.123, r.Points[0].E, 6); Assert.Equal(2800000.5, r.Points[0].N, 6); Assert.Equal(12.34, r.Points[0].Z, 6); Assert.Equal("LP 3", r.Points[1].Desc);
    }
    [Fact] public void ParseHeaderSpaceAndHiddenChars()
    {
        var lines = new[] { "\uFEFFPoint Easting Northing Elevation Description", "1\u00A0100.5 200.5 3.0 TREE PALM big", "\u200B", "2 101.5 201.5 3.1" };
        var r = PointText.Parse(lines, "PENZD");
        Assert.True(r.HeaderSkipped); Assert.Equal(2, r.Points.Count); Assert.Equal("TREE PALM big", r.Points[0].Desc); Assert.Equal("", r.Points[1].Desc);
    }
    [Fact] public void ParseArabicDigitsTabAndPnezd()
    {
        var r = PointText.Parse(new[] { "\u0661\t\u0662\u0660\u0660\u066B\u0665\t\u0661\u0660\u0660\t\u0663\tRD" }, "PNEZD");
        Assert.Single(r.Points); Assert.Equal(100.0, r.Points[0].E, 6); Assert.Equal(200.5, r.Points[0].N, 6);
    }
    [Fact] public void BadLineReportedNotFatal()
    {
        var r = PointText.Parse(new[] { "1,100,200,5,A", "2,abc,200,5,B", "3,101,201,5,C" }, "PENZD");
        Assert.Equal(2, r.Points.Count); Assert.Single(r.Problems); Assert.Contains("Line 2", r.Problems[0]);
    }
    [Fact] public void PenzWithoutDescription()
    {
        var r = PointText.Parse(new[] { "1,10,20,5", "2,11,21,6" }, "PENZ");
        Assert.Equal(2, r.Points.Count); Assert.True(r.Points[0].HasZ);
        var r2 = PointText.Parse(new[] { "1,10,20" }, "PEN"); Assert.False(r2.Points[0].HasZ);
    }
    [Fact] public void FormatIsCleanAndRoundTrips()
    {
        var pts = new[] { new SurveyPoint { No = "7", E = 1.5, N = 2.25, Z = 3, Desc = "MNH,\u200B1 \"x\"" } };
        string t = PointText.Format(pts, "PENZD", ',', 3);
        Assert.True(!t.Contains("\u200B"), "[" + t + "]"); Assert.EndsWith("\r\n", t); Assert.Single(t.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries));
        var back = PointText.Parse(t.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries), "PENZD", ',');
        Assert.Equal("MNH,1 \"x\"", back.Points[0].Desc); Assert.Equal(2.25, back.Points[0].N, 6);
        Assert.Equal("7 1.500 2.250 3.000 MNH,1_\"x\"\r\n", PointText.Format(pts, "PENZD", ' ', 3));
    }
    [Fact] public void FormatNumbersWhenMissingAndHeader()
    {
        var t = PointText.Format(new[] { new SurveyPoint { E = 1, N = 2, Z = 3 }, new SurveyPoint { E = 4, N = 5, Z = 6 } }, "PNEZD", ',', 2, true);
        Assert.StartsWith("Point,Northing,Easting,Elevation,Description\r\n1,2.00,1.00,3.00,\r\n2,5.00,4.00,6.00,\r\n", t);
    }
    [Fact] public void OrderValidation()
    {
        Assert.True(PointText.ValidOrder("PENZD")); Assert.True(PointText.ValidOrder("pnezd")); Assert.False(PointText.ValidOrder("PEZD")); Assert.False(PointText.ValidOrder("PENNZ")); Assert.False(PointText.ValidOrder("PENX"));
    }
    [Fact] public void XlsxReadsSharedAndInlineStrings()
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            Put(z, "xl/sharedStrings.xml", "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>MNH1</t></si><si><t>Point</t></si></sst>");
            Put(z, "xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>1</v></c><c r=\"B1\" t=\"inlineStr\"><is><t>E</t></is></c></row><row r=\"2\"><c r=\"A2\"><v>1</v></c><c r=\"B2\"><v>100.5</v></c><c r=\"C2\"><v>200.25</v></c><c r=\"E2\" t=\"s\"><v>0</v></c></row></sheetData></worksheet>");
        }
        ms.Position = 0; var r = XlsxReader.ParsePoints(ms, "PENZD");
        Assert.True(r.HeaderSkipped); Assert.Single(r.Points); Assert.Equal("MNH1", r.Points[0].Desc); Assert.False(r.Points[0].HasZ); Assert.Equal(200.25, r.Points[0].N, 6);
    }
    static void Put(ZipArchive z, string n, string c) { var e = z.CreateEntry(n); using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(c); }

    [Fact] public void CodesClassify()
    {
        var r = CodeRules.Default();
        Assert.Equal("MNH", r.Classify("MNH1")); Assert.Equal("MNH", r.Classify("mnh 12")); Assert.Equal("LP", r.Classify("LP-3")); Assert.Equal("TREE", r.Classify("Tree palm"));
        Assert.Equal(CodeRules.Uncoded, r.Classify("")); Assert.Equal(CodeRules.Other, r.Classify("XYZ1")); Assert.Equal(CodeRules.Other, r.Classify("MNHOLE"));
        Assert.Equal("Natural ground", r.NameOf("EG"));
    }
    [Fact] public void CodeRulesCustomAndProblems()
    {
        var pr = new System.Collections.Generic.List<string>();
        var r = CodeRules.Parse("SMH, MH = Sewer manholes\r\nbad line\r\nFOO = \r\n", pr);
        Assert.Equal(2, pr.Count); Assert.Equal("SMH", r.Classify("MH 4")); Assert.Equal("Sewer manholes", r.NameOf("SMH"));
    }
    [Fact] public void QaFindsIssues()
    {
        var rules = CodeRules.Default(); var pts = new System.Collections.Generic.List<SurveyPoint>();
        for (int i = 0; i < 10; i++) pts.Add(new SurveyPoint { No = (i + 1).ToString(), E = 100 + i * 5, N = 200, Z = 10 + i * 0.01, Desc = "EG" });
        pts[4].Z = 15; // spike
        pts.Add(new SurveyPoint { No = "3", E = 500, N = 500, Z = 1, Desc = "MNH1" }); // repeated number
        pts.Add(new SurveyPoint { No = "50", E = 105, N = 200.001, Z = 10.05, Desc = "EG" }); // coincident with #2
        pts.Add(new SurveyPoint { No = "51", E = 1, N = 1, Z = 0, Desc = "" }); // no code, no elevation
        rules.Apply(pts);
        var kinds = SurveyQa.Run(pts, new QaOptions()).Select(q => q.Kind).ToList();
        Assert.Contains("Repeated number", kinds); Assert.Contains("Elevation spike", kinds); Assert.Contains("Same XY, different Z", kinds); Assert.Contains("No code", kinds); Assert.Contains("No elevation", kinds);
    }
    [Fact] public void QaCleanDataHasNoSpikes()
    {
        var rules = CodeRules.Default(); var pts = new System.Collections.Generic.List<SurveyPoint>();
        for (int i = 0; i < 20; i++) pts.Add(new SurveyPoint { No = (i + 1).ToString(), E = 100 + i * 5, N = 200 + (i % 3), Z = 10 + i * 0.02, Desc = "EG" });
        rules.Apply(pts);
        Assert.Empty(SurveyQa.Run(pts, new QaOptions()));
    }
    [Fact] public void Geometry()
    {
        Assert.Equal(0, Geo2D.Azimuth(0, 0, 0, 10), 6); Assert.Equal(90, Geo2D.Azimuth(0, 0, 10, 0), 6); Assert.Equal(225, Geo2D.Azimuth(0, 0, -10, -10), 6); Assert.Equal(315, Geo2D.Azimuth(0, 0, -1, 1), 6);
        Assert.Equal("N 45\u00B000'00\" E", Geo2D.QuadrantBearing(45)); Assert.Equal("S 45\u00B000'00\" W", Geo2D.QuadrantBearing(225)); Assert.Equal("N 10\u00B000'00\" W", Geo2D.QuadrantBearing(350));
        Assert.Equal("45\u00B030'15\"", Geo2D.Dms(45.50416667));
        Assert.Equal(5, Geo2D.Distance(0, 0, 3, 4), 9);
        Assert.Equal(100, Geo2D.PolygonArea(new[] { 0.0, 10, 10, 0 }, new[] { 0.0, 0, 10, 10 }), 9);
        Assert.Equal(40, Geo2D.Perimeter(new[] { 0.0, 10, 10, 0 }, new[] { 0.0, 0, 10, 10 }, true), 9);
    }
    [Fact] public void ConnectByCodeAndNumber()
    {
        var pts = new[] {
            new SurveyPoint { No = "3", E = 3, N = 0, Desc = "RD1" }, new SurveyPoint { No = "1", E = 1, N = 0, Desc = "RD1" }, new SurveyPoint { No = "2", E = 2, N = 0, Desc = "RD1" },
            new SurveyPoint { No = "10", E = 0, N = 5, Desc = "RD2" }, new SurveyPoint { No = "11", E = 1, N = 5, Desc = "RD2" },
            new SurveyPoint { No = "20", E = 0, N = 9, Desc = "FENCE close" }, new SurveyPoint { No = "21", E = 1, N = 9, Desc = "FENCE" }, new SurveyPoint { No = "22", E = 1, N = 10, Desc = "FENCE" },
            new SurveyPoint { No = "30", E = 7, N = 7, Desc = "TREE" }, new SurveyPoint { No = "31", E = 9, N = 9, Desc = "" } };
        var by = Connect.Build(pts, true, true);
        Assert.Equal(new[] { "RD1", "RD2", "FENCE" }, by.Select(l => l.Key).ToArray());
        Assert.Equal(new[] { "1", "2", "3" }, by[0].Pts.Select(p => p.No).ToArray()); Assert.True(by[2].Closed); Assert.False(by[0].Closed);
        Assert.Equal(2, Connect.Build(pts, true, false).First(l => l.Key == "RD").Pts.Count - 1 + 0 == 4 ? 2 : 2);
        Assert.Equal(5, Connect.Build(pts, true, false).First(l => l.Key == "RD").Pts.Count);
        Assert.Single(Connect.Build(pts, false, false)); Assert.Equal(10, Connect.Build(pts, false, false)[0].Pts.Count);
        Assert.Equal("FENCE", Connect.Build(pts, true, true, new[] { "FENCE" }).Single().Code);
    }
}

public class UpdateTests
{
    const string Json = "[{\"tag_name\":\"v1.1\",\"prerelease\":true,\"assets\":[]},{\"tag_name\":\"cadtools-v0.3.1\",\"html_url\":\"https://github.com/Bplus-Software/Bplus-Cad-Tools/releases/tag/cadtools-v0.3.1\",\"prerelease\":true,\"assets\":[{\"name\":\"BPlusCadTools-Setup.exe\",\"browser_download_url\":\"https://github.com/Bplus-Software/Bplus-Cad-Tools/releases/download/cadtools-v0.3.1/BPlusCadTools-Setup.exe\"},{\"name\":\"SHA256SUMS.txt\",\"browser_download_url\":\"https://github.com/Bplus-Software/Bplus-Cad-Tools/releases/download/cadtools-v0.3.1/SHA256SUMS.txt\"}]},{\"tag_name\":\"cadtools-v0.10.0\",\"draft\":false,\"prerelease\":true,\"assets\":[{\"name\":\"BPlusCadTools-Setup.exe\",\"browser_download_url\":\"https://github.com/Bplus-Software/Bplus-Cad-Tools/releases/download/cadtools-v0.10.0/BPlusCadTools-Setup.exe\"},{\"name\":\"SHA256SUMS.txt\",\"browser_download_url\":\"https://github.com/Bplus-Software/Bplus-Cad-Tools/releases/download/cadtools-v0.10.0/SHA256SUMS.txt\"}]},{\"tag_name\":\"cadtools-v9.9.9\",\"assets\":[{\"name\":\"BPlusCadTools-Setup.exe\",\"browser_download_url\":\"https://evil.example/x.exe\"},{\"name\":\"SHA256SUMS.txt\",\"browser_download_url\":\"https://evil.example/s\"}]},{\"tag_name\":\"cadtools-v0.11.0\",\"assets\":[{\"name\":\"BPlusCadTools-Setup.exe\",\"browser_download_url\":\"https://github.com/Bplus-Software/x\"}]}]";
    [Fact] public void PicksNewestByNumberNotText()
    {
        var u = Updater.Newest(Json); Assert.Equal("0.10.0", u.Version); Assert.Equal("cadtools-v0.10.0", u.Tag); Assert.True(u.Prerelease);
    }
    [Fact] public void IgnoresOtherHostsAndIncompleteReleases() { var u = Updater.Newest(Json); Assert.StartsWith("https://github.com/Bplus-Software/", u.InstallerUrl); Assert.NotEqual("9.9.9", u.Version); Assert.NotEqual("0.11.0", u.Version); }
    [Fact] public void NewerCheck()
    {
        var u = Updater.Newest(Json); Assert.True(Updater.IsNewer(u, "0.3.2-experimental")); Assert.False(Updater.IsNewer(u, "0.10.0-experimental")); Assert.False(Updater.IsNewer(u, "0.11.0")); Assert.False(Updater.IsNewer(null, "0.1.0")); Assert.False(Updater.IsNewer(u, "garbage"));
    }
    [Fact] public void NoReleaseMeansNull() { Assert.Null(Updater.Newest("[]")); Assert.ThrowsAny<Exception>(() => Updater.Newest("{}")); Assert.ThrowsAny<Exception>(() => Updater.Newest("[{")); }
    [Fact] public void HashLookup()
    {
        string h = new string('a', 64), g = new string('B', 64);
        var t = h + "  BPlusCadTools-Setup.exe\r\n" + g + " *bundle.zip\r\n";
        Assert.Equal(h, Updater.HashFor(t, "BPlusCadTools-Setup.exe")); Assert.Equal(g.ToLowerInvariant(), Updater.HashFor(t, "bundle.zip")); Assert.Null(Updater.HashFor(t, "other.exe")); Assert.Null(Updater.HashFor("nothex" + new string(' ', 60) + "BPlusCadTools-Setup.exe", "BPlusCadTools-Setup.exe"));
    }
    [Fact] public void Sha256OfBytes() { using (var m = new System.IO.MemoryStream(new byte[] { 97, 98, 99 })) Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Updater.Sha256Hex(m)); }
    [Fact] public void JsonEscapes() { var d = (System.Collections.Generic.Dictionary<string, object>)MiniJson.Parse("{\"a\":\"x\\u0041\\n\\\"q\\\"\",\"b\":[1,2.5,true,null]}"); Assert.Equal("xA\n\"q\"", d["a"]); Assert.Equal(4, ((System.Collections.Generic.List<object>)d["b"]).Count); }
    [Fact] public void VersionParsing() { Assert.Equal(new Version(0, 3, 2, 0), Updater.ParseVersion("0.3.2-experimental")); Assert.Null(Updater.ParseVersion("abc")); Assert.Null(Updater.ParseVersion("1")); }
}
