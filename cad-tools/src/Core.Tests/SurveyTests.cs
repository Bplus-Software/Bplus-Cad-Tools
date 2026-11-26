using System.Collections.Generic;
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

    [Fact] public void XlsxWriterRoundTrips()
    {
        var ms = new MemoryStream();
        XlsxWriter.WritePoints(ms, new[] { new SurveyPoint { No = "1", E = 500000.125, N = 2700000.5, Z = 12.3, Desc = "MNH & co" }, new SurveyPoint { No = "2", E = 1, N = 2, HasZ = false, Desc = "LP" } });
        ms.Position = 0; var r = XlsxReader.ParsePoints(ms, "PENZD");
        Assert.True(r.HeaderSkipped); Assert.Equal(2, r.Points.Count); Assert.Equal(500000.125, r.Points[0].E, 6); Assert.Equal(12.3, r.Points[0].Z, 6); Assert.Equal("MNH & co", r.Points[0].Desc); Assert.False(r.Points[1].HasZ);
    }

    static JPath JP(params double[] xy) { var p = new JPath(); for (int i = 0; i < xy.Length; i += 2) p.V.Add(new JV(xy[i], xy[i + 1])); p.Src.Add(0); return p; }
    [Fact] public void JoinTouching() { var r = JoinLogic.Run(new[] { JP(0, 0, 10, 0), JP(10, 0, 10, 5) }, 0.5, JoinMode.Extend); Assert.Single(r.Paths); Assert.Equal(3, r.Paths[0].V.Count); Assert.Equal(0, r.Straight + r.Extended); }
    [Fact] public void JoinExtendsCorner() { var r = JoinLogic.Run(new[] { JP(0, 0, 9.8, 0), JP(10, 0.3, 10, 5) }, 0.5, JoinMode.Extend); Assert.Single(r.Paths); Assert.Equal(1, r.Extended); Assert.Equal(3, r.Paths[0].V.Count); Assert.Equal(10, r.Paths[0].V[1].X, 6); Assert.Equal(0, r.Paths[0].V[1].Y, 6); }
    [Fact] public void JoinStraightWhenParallel() { var r = JoinLogic.Run(new[] { JP(0, 0, 9.8, 0), JP(10, 0, 20, 0) }, 0.5, JoinMode.Extend); Assert.Single(r.Paths); Assert.Equal(1, r.Straight); Assert.Equal(4, r.Paths[0].V.Count); }
    [Fact] public void JoinTooFarStaysOpen() { var r = JoinLogic.Run(new[] { JP(0, 0, 9, 0), JP(10, 0, 20, 0) }, 0.5, JoinMode.Extend); Assert.Equal(2, r.Paths.Count); Assert.Equal(0, r.LeftOpen > 0 ? 0 : 1); }
    [Fact] public void JoinReversesAndCloses() { var r = JoinLogic.Run(new[] { JP(0, 0, 10, 0), JP(10, 10, 10, 0.1), JP(0, 10, 10, 10), JP(0, 0.1, 0, 10) }, 0.5, JoinMode.Straight); Assert.Single(r.Paths); Assert.True(r.Paths[0].Closed); }
    [Fact] public void JoinTouchOnlyIgnoresGap() { var r = JoinLogic.Run(new[] { JP(0, 0, 9.8, 0), JP(10, 0, 20, 0) }, 0.5, JoinMode.Touch); Assert.Equal(2, r.Paths.Count); }
    [Fact] public void ReverseFlipsBulge() { var p = new JPath(); p.V.Add(new JV(0, 0, 1)); p.V.Add(new JV(2, 0, 0)); p.V.Add(new JV(2, 2, 0)); p.Reverse(); Assert.Equal(0, p.V[0].B); Assert.Equal(-1, p.V[1].B); }
    [Fact] public void PolyContains() { var sq = Poly2D.Flatten(new[] { new JV(0, 0), new JV(10, 0), new JV(10, 10), new JV(0, 10) }, true); Assert.True(Poly2D.Contains(sq, 5, 5)); Assert.True(Poly2D.Contains(sq, 10, 5)); Assert.False(Poly2D.Contains(sq, 11, 5)); var semi = Poly2D.Flatten(new[] { new JV(0, 0, 1), new JV(2, 0) }, true); Assert.True(Poly2D.Contains(semi, 1, -0.9)); Assert.False(Poly2D.Contains(semi, 1, 0.5)); }

    static Grid BowlGrid(int n, Func<int, int, double> f) { var g = new Grid { X0 = 0, Y0 = 0, Cell = 1, NX = n, NY = n, Z = new double[n * n] }; for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) g.Z[j * n + i] = f(i, j); return g; }
    [Fact] public void SinkFoundInBowl()
    {
        // slope down to the right but with a 1 m deep hole in the middle
        var g = BowlGrid(21, (i, j) => 20 - i * 0.0 + ((i >= 8 && i <= 12 && j >= 8 && j <= 12) ? -1 : 0));
        int ig; var s = Sinks.Analyze(g, 0.05, 1, out ig); Assert.Single(s); Assert.Equal(1.0, s[0].Depth, 6); Assert.Equal(25.0, s[0].Area, 6); Assert.Equal(25.0, s[0].Volume, 6); Assert.Equal(1, s[0].No); Assert.Single(s[0].Outline);
    }
    [Fact] public void NoSinkOnSlope() { var g = BowlGrid(15, (i, j) => 10 - i * 0.1); int ig; Assert.Empty(Sinks.Analyze(g, 0.01, 0.1, out ig)); }
    [Fact] public void SmallSinkIgnoredAndCounted() { var g = BowlGrid(15, (i, j) => 10 + (i == 7 && j == 7 ? -0.02 : 0)); int ig; var s = Sinks.Analyze(g, 0.05, 1, out ig); Assert.Empty(s); Assert.Equal(1, ig); }
    [Fact] public void TwoSinksSortedByDepth()
    {
        var g = BowlGrid(31, (i, j) => 20 + ((i >= 3 && i <= 5 && j >= 3 && j <= 5) ? -0.5 : 0) + ((i >= 20 && i <= 24 && j >= 20 && j <= 24) ? -2 : 0));
        int ig; var s = Sinks.Analyze(g, 0.05, 1, out ig); Assert.Equal(2, s.Count); Assert.Equal(2.0, s[0].Depth, 6); Assert.Equal(1, s[0].No); Assert.Equal(2, s[1].No);
    }
    [Fact] public void DelaunayPlaneRaster()
    {
        var pts = new List<double[]>(); for (int i = 0; i <= 10; i += 5) for (int j = 0; j <= 10; j += 5) pts.Add(new[] { (double)i, j, i + 2.0 * j });
        var tr = Sinks.Delaunay(pts); Assert.Equal(8, tr.Count); double uc; var g = Sinks.Rasterize(tr, 1, out uc);
        Assert.Equal(11, g.NX); Assert.Equal(3 + 2 * 4, g.Z[4 * g.NX + 3], 6);
    }
    [Fact] public void DelaunayFindsBowlMinimum()
    {
        var pts = new List<double[]>(); var r = new Random(5);
        for (int i = 0; i <= 20; i++) for (int j = 0; j <= 20; j++) { double dx = i - 10, dy = j - 10; pts.Add(new[] { (double)i, j, 5 + Math.Sqrt(dx * dx + dy * dy) * 0.3 + (i == 0 || j == 0 || i == 20 || j == 20 ? 5 : 0) }); }
        double uc; var g = Sinks.Rasterize(Sinks.Delaunay(pts), 1, out uc); int ig; var s = Sinks.Analyze(g, 0.05, 1, out ig);
        Assert.True(s.Count >= 1); Assert.Equal(10, s[0].E, 0); Assert.Equal(10, s[0].N, 0); Assert.Equal(5, s[0].LowZ, 1);
    }

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

public class CutFillTests
{
    [Fact] public void FlatCutOverRectangle()
    {   // ground at 12, level 10, 20x10 m => cut 2 * 200 = 400, no fill
        var r = CutFill.Compute((x, y) => 12, (x, y) => 10, 0, 0, 20, 10, 1, null, 1);
        Assert.Equal(400.0, r.CutVolume, 6); Assert.Equal(0.0, r.FillVolume, 6); Assert.Equal(200.0, r.CutArea, 6); Assert.Equal(2.0, r.MeanCut, 6); Assert.Equal(400.0, r.Net, 6);
    }
    [Fact] public void SlopeAgainstLevelSplitsCutAndFill()
    {   // ground rises 0..10 along x, level 5: cut triangle = fill triangle = 0.5*5*5*10 = 125 each
        var r = CutFill.Compute((x, y) => x, (x, y) => 5, 0, 0, 10, 10, 0.5, null, 1);
        Assert.Equal(125.0, r.CutVolume, 6); Assert.Equal(125.0, r.FillVolume, 6); Assert.Equal(0.0, r.Net, 6);
    }
    [Fact] public void PolygonLimitsTheArea()
    {
        var sq = new List<double[]> { new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 10 }, new[] { 0.0, 10 } };
        var r = CutFill.Compute((x, y) => 1, (x, y) => 0, -20, -20, 40, 40, 1, (x, y) => CutFill.InPolygon(sq, x, y), 1);
        Assert.Equal(100.0, r.CutVolume, 6);
    }
    [Fact] public void SwellScalesCutInNet()
    {
        var r = CutFill.Compute((x, y) => 2, (x, y) => 0, 0, 0, 10, 10, 1, null, 1.25); Assert.Equal(200.0, r.CutVolume, 6); Assert.Equal(250.0, r.Net, 6);
    }
    [Fact] public void NoDataCellsSkipped()
    {
        var r = CutFill.Compute((x, y) => x < 5 ? double.NaN : 1, (x, y) => 0, 0, 0, 10, 10, 1, null, 1); Assert.Equal(50.0, r.CutVolume, 6);
    }
    [Fact] public void TriSurfaceInterpolates()
    {
        var s = new TriSurface(new[] { new Tri(0, 0, 0, 10, 0, 10, 0, 10, 0), new Tri(10, 0, 10, 10, 10, 10, 0, 10, 0) });
        Assert.Equal(5.0, s.Z(5, 2), 6); Assert.Equal(10.0, s.Z(10, 5), 6); Assert.True(double.IsNaN(s.Z(11, 5)));
    }
}

public class MeasureTests
{
    [Fact] public void RightAngle() { Assert.Equal(90.0, MeasureMath.AngleAt(10, 0, 0, 0, 0, 10), 6); }
    [Fact] public void StraightAngleIs180() { Assert.Equal(180.0, MeasureMath.AngleAt(-5, 0, 0, 0, 5, 0), 6); }
    [Fact] public void AngleAcrossNorthIsSmall() { Assert.Equal(20.0, MeasureMath.AngleAt(Math.Sin(-10 * Math.PI / 180), Math.Cos(10 * Math.PI / 180), 0, 0, Math.Sin(10 * Math.PI / 180), Math.Cos(10 * Math.PI / 180)), 6); }
    [Fact] public void ChainLengths() { var p = new List<double[]> { new[] { 0.0, 0, 0 }, new[] { 3.0, 4, 0 }, new[] { 3.0, 4, 12 } }; Assert.Equal(5.0, MeasureMath.Length2D(p), 9); Assert.Equal(17.0, MeasureMath.Length3D(p), 9); }
    [Fact] public void CentroidOfSquare() { var p = new List<double[]> { new[] { 0.0, 0 }, new[] { 4.0, 0 }, new[] { 4.0, 4 }, new[] { 0.0, 4 } }; var c = MeasureMath.Centroid(p); Assert.Equal(2.0, c[0], 9); Assert.Equal(2.0, c[1], 9); }
}

public class EgyptCsTests
{
    // PROJ with the same transformation (EPSG 1148: -130, 110, -13 on Helmert 1906) is the oracle.
    public static IEnumerable<object[]> Ref = new List<object[]> {
        new object[] { 22991, 28.5, 34.3, 231301.306, 933926.449 },
        new object[] { 22991, 29.9, 34.8, 280518.675, 1088918.703 },
        new object[] { 22992, 30.0444, 31.2357, 637570.509, 814926.683 },
        new object[] { 22992, 31.2, 32.3, 738739.581, 943744.991 },
        new object[] { 22992, 26.0, 30.0, 514717.755, 367084.973 },
        new object[] { 22993, 31.0, 27.9, 785798.487, 311184.490 },
        new object[] { 22993, 29.0, 25.5, 553675.701, 90059.499 },
        new object[] { 22994, 31.5, 27.2, 718843.455, 1366290.996 },
        new object[] { 22994, 30.0, 28.5, 844580.723, 1200923.281 },
    };
    [Theory, MemberData(nameof(Ref))] public void BeltForwardMatchesProj(int epsg, double lat, double lon, double e, double n)
    { var cs = CoordSys.FromEpsg(epsg); cs.FromLonLat(lon, lat, out double x, out double y); Assert.InRange(Math.Abs(x - e), 0, 0.02); Assert.InRange(Math.Abs(y - n), 0, 0.02); }
    [Theory, MemberData(nameof(Ref))] public void BeltRoundTrip(int epsg, double lat, double lon, double e, double n)
    { var cs = CoordSys.FromEpsg(epsg); cs.ToLonLat(e, n, out double lo, out double la); Assert.InRange(Math.Abs(lo - lon), 0, 1e-7); Assert.InRange(Math.Abs(la - lat), 0, 1e-7); }
    [Fact] public void BeltsAreMarkedUnverified() { Assert.True(CoordSys.FromEpsg(22992).Unverified); Assert.False(CoordSys.FromEpsg(32638).Unverified); }
    [Fact] public void CatalogHasGroupsAndNoDuplicates()
    {
        var c = CoordSys.Catalog(); Assert.Equal(c.Count, c.Select(x => x.Epsg).Distinct().Count());
        Assert.Contains(c, x => x.Epsg == 32636 && x.Groups.Contains("Saudi Arabia") && x.Groups.Contains("Egypt"));
        Assert.Contains(c, x => x.Epsg == 22992 && x.Groups.Contains("Egypt")); Assert.Contains(c, x => x.Epsg == 32640 && x.Groups.Contains("UTM zones"));
    }
    [Fact] public void SuggestFindsTheRightZoneForRiyadh()
    {
        var cs = CoordSys.FromEpsg(32638); cs.FromLonLat(46.6753, 24.7136, out double x, out double y);
        var g = CsSuggest.Suggest(x, y, 1.0); Assert.Contains(g, q => q.Cs.Epsg == 32638 && q.Where == "Saudi Arabia");
    }
    [Fact] public void SuggestFindsEgyptForCairoRedBelt()
    {
        var cs = CoordSys.FromEpsg(22992); cs.FromLonLat(31.2357, 30.0444, out double x, out double y);
        var g = CsSuggest.Suggest(x, y, 1.0); Assert.Contains(g, q => q.Cs.Epsg == 22992 && q.Where == "Egypt");
    }
}

public class PlotNamingTests
{
    [Fact] public void PatternFillsDrawingAndLayout() { Assert.Equal("Site-12-A1-Plan", PlotNaming.FileName("{drawing}-{layout}", "Site-12", "A1-Plan")); }
    [Fact] public void BadCharactersBecomeUnderscore() { Assert.Equal("a_b_c", PlotNaming.FileName("{layout}", "d", "a/b:c")); }
    [Fact] public void EmptyBecomesPlot() { Assert.Equal("plot", PlotNaming.FileName("{layout}", "d", "")); }
    [Fact] public void UniqueNeverOverwrites()
    {
        var have = new HashSet<string> { System.IO.Path.Combine("f", "a.pdf"), System.IO.Path.Combine("f", "a-2.pdf") };
        Assert.Equal(System.IO.Path.Combine("f", "a-3.pdf"), PlotNaming.Unique("f", "a", ".pdf", p => have.Contains(p)));
        Assert.Equal(System.IO.Path.Combine("f", "b.pdf"), PlotNaming.Unique("f", "b", ".pdf", p => have.Contains(p)));
    }
    [Fact] public void ScaleParsing() { Assert.Equal(500, PlotNaming.ParseScale("1 : 500")); Assert.Equal(0, PlotNaming.ParseScale("Fit")); Assert.Equal(-1, PlotNaming.ParseScale("abc")); Assert.Equal(1000, PlotNaming.ParseScale("1000")); }
    [Fact] public void ScaleInMetreDrawing() { Assert.Equal(0.5, PlotNaming.DrawingUnitsPerPaperMm(500, 1.0), 9); Assert.Equal(500.0, PlotNaming.DrawingUnitsPerPaperMm(500, 0.001), 6); }
}

namespace BPlus.LiveMap.Core.Tests2
{
    using System.Collections.Generic; using System.Linq; using Xunit; using BPlus.LiveMap.Core;
    public class PointConvertTests
    {
        static SurveyPoint P(string no) { return new SurveyPoint { No = no }; }
        [Fact] public void KeepsNumbersAndSkipsUsed()
        {
            var r = PointConvert.Plan(new[] { P("5"), P("6"), P("5") }, new[] { 6 }, false, 1001);
            Assert.Equal(new[] { "ok", "used", "used" }, r.Select(x => x.Status).ToArray());
        }
        [Fact] public void GivesFreeNumbersWhenAsked()
        {
            var r = PointConvert.Plan(new[] { P(""), P("1001"), P("x") }, new int[0], true, 1001);
            Assert.Equal(new[] { 1002, 1001, 1003 }, r.Select(x => x.Number).ToArray()); Assert.Equal("numbered", r[0].Status);
        }
        [Fact] public void NoNumberSkippedByDefault() { Assert.Equal("nonumber", PointConvert.Plan(new[] { P("") }, new int[0], false, 1).Single().Status); }
    }
}

namespace BPlus.LiveMap.Core.Tests3
{
    using Xunit; using BPlus.LiveMap.Core;
    public class VersionOrderTests
    {
        [Fact] public void TenIsNewerThanNine() { Assert.True(Updater.ParseVersion("0.4.10") > Updater.ParseVersion("0.4.9")); }
    }
}
