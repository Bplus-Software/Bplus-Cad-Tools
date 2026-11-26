using System.IO;
using System.Linq;
using BPlus.LiveMap.Core;
using Xunit;

public class FormatTests
{
    [Fact] public void KmlRoundTrip()
    {
        var f = new[] { new KmlFeature { Name = "A", Kind = "Point", Coords = { new[] { 46.7, 24.7, 600.0 } } }, new KmlFeature { Name = "L", Kind = "Line", Coords = { new[] { 46.0, 24.0, 0.0 }, new[] { 46.1, 24.1, 0.0 } } }, new KmlFeature { Name = "P", Kind = "Polygon", Coords = { new[] { 46.0, 24.0, 0.0 }, new[] { 46.1, 24.0, 0.0 }, new[] { 46.1, 24.1, 0.0 } } } };
        string kml = Kml.Write(f, "t");
        var back = Kml.Read(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(kml)), false);
        Assert.Equal(3, back.Count); Assert.Equal("Polygon", back[2].Kind); Assert.Equal(3, back[2].Coords.Count); Assert.Equal(46.7, back[0].Coords[0][0], 6); Assert.Equal(600, back[0].Coords[0][2], 3);
        var ms = new MemoryStream(); Kml.WriteKmz(ms, kml); ms.Position = 0;
        Assert.Equal(3, Kml.Read(ms, true).Count);
    }
    [Fact] public void KmlRejectsDtd()
    {
        string x = "<?xml version=\"1.0\"?><!DOCTYPE k [<!ENTITY e \"x\">]><kml><Placemark><Point><coordinates>1,2,0</coordinates></Point></Placemark></kml>";
        Assert.ThrowsAny<System.Exception>(() => Kml.Read(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(x)), false));
    }
    [Fact] public void GsiRoundTrip()
    {
        var pts = new[] { new SurveyPoint { No = "12", E = 1000.123, N = 2000.456, Z = 55.5, Desc = "RD" }, new SurveyPoint { No = "13", E = -5.5, N = 7, HasZ = false } };
        foreach (var sixteen in new[] { true, false })
        {
            string t = Gsi.Format(pts, sixteen); var r = Gsi.Parse(t.Split('\n'));
            Assert.Equal(2, r.Points.Count); Assert.Equal("12", r.Points[0].No); Assert.Equal(1000.123, r.Points[0].E, 3); Assert.Equal(2000.456, r.Points[0].N, 3); Assert.Equal(55.5, r.Points[0].Z, 3); Assert.Equal("RD", r.Points[0].Desc); Assert.Equal(-5.5, r.Points[1].E, 3); Assert.False(r.Points[1].HasZ);
        }
    }
    [Fact] public void GsiKnownLine()
    {
        var r = Gsi.Parse(new[] { "*110001+00000005 81..00+00001234 82..00-00000500 83..00+00000100" });
        Assert.Single(r.Points); Assert.Equal("5", r.Points[0].No); Assert.Equal(1.234, r.Points[0].E, 4); Assert.Equal(-0.5, r.Points[0].N, 4);
    }
    [Fact] public void SdrRoundTrip()
    {
        var pts = new[] { new SurveyPoint { No = "P1", E = 1000.1234, N = 2000.5, Z = 10, Desc = "TREE" }, new SurveyPoint { No = "2", E = 3, N = 4, HasZ = false } };
        var r = Sdr.Parse(Sdr.Format(pts).Split('\n'));
        Assert.Equal(2, r.Points.Count); Assert.Equal("P1", r.Points[0].No); Assert.Equal(1000.1234, r.Points[0].E, 4); Assert.Equal(2000.5, r.Points[0].N, 4); Assert.Equal("TREE", r.Points[0].Desc); Assert.False(r.Points[1].HasZ);
    }
    [Fact] public void RenumberPlans()
    {
        var pts = new[] { new SurveyPoint { No = "10", E = 5, N = 1 }, new SurveyPoint { No = "2", E = 1, N = 2 }, new SurveyPoint { No = "7", E = 3, N = 3 } };
        Assert.Equal(new[] { "102", "100", "101" }, Renumber.Plan(pts, 100, 1, "no").ToArray());
        Assert.Equal(new[] { "102", "100", "101" }, Renumber.Plan(pts, 100, 1, "east").ToArray());
        Assert.Equal(new[] { "1", "3", "5" }, Renumber.Plan(pts, 1, 2, "keep").ToArray());
    }
    static System.Collections.Generic.IList<double[]> Sq(double x, double y, double s) { return new System.Collections.Generic.List<double[]> { new[] { x, y }, new[] { x + s, y }, new[] { x + s, y + s }, new[] { x, y + s } }; }
    [Fact] public void LoopGrouping()
    {
        var loops = new System.Collections.Generic.List<System.Collections.Generic.IList<double[]>> { Sq(0, 0, 10), Sq(2, 2, 3), Sq(20, 0, 5), Sq(3, 3, 1) };
        var g = LoopGroups.Group(loops);
        Assert.Equal(0, g[0]); Assert.Equal(0, g[1]); Assert.Equal(2, g[2]); Assert.Equal(3, g[3]); // island inside island starts its own group
    }
}
