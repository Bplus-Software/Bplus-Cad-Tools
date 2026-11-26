using System;
using System.Collections.Generic;
using BPlus.LiveMap.Core;
using Xunit;

public class GeoTests
{
    public static IEnumerable<object[]> Ref = new List<object[]> {
        new object[]{32637,21.5433,39.1728,517893.1274058068,2382287.976939654},
        new object[]{32638,24.7136,46.6753,669459.6216098682,2734271.6718420503},
        new object[]{32638,24.0,45.0,500000.0,2654226.5384411346},
        new object[]{32638,24.7,43.2,317904.33770506346,2732925.352913943},
        new object[]{32639,26.4207,50.0888,409134.15700976865,2922596.5824802136},
        new object[]{32631,48.8584,2.2945,448252.0013753649,5411954.909947274},
        new object[]{20437,21.5433,39.1728,517986.50303345214,2382278.9316887786},
        new object[]{20438,24.7136,46.6753,669525.2114795733,2734259.173342715},
        new object[]{20438,24.0,45.0,500065.73456395656,2654214.129851803},
        new object[]{20438,24.7,43.2,317970.22841856675,2732912.8528773165},
        new object[]{20439,26.4207,50.0888,409171.65463988075,2922581.5519715184},
        new object[]{9357,21.5433,39.1728,517893.35472377203,2382288.1589252064},
        new object[]{9358,24.7136,46.6753,669459.8409100572,2734271.8790998915},
        new object[]{9359,26.4207,50.0888,409134.37741914805,2922596.7946458617},
        new object[]{9356,28.38,36.57,849897.5044085465,3144483.897885143},
        new object[]{9360,23.6,55.3,326543.7688739939,2610972.760311516},
        new object[]{8837,21.5433,39.1728,517893.1274058464,2382287.976866453},
        new object[]{8838,24.7136,46.6753,669459.6216103545,2734271.671759945},
        new object[]{8839,26.4207,50.0888,409134.15700947354,2922596.5823936043},
        new object[]{2318,24.7136,46.6753,-132688.3950542391,-40637.199480562},
        new object[]{2318,21.5433,39.1728,-906564.8261228101,-359607.050356184},
        new object[]{2318,26.4207,50.0888,206455.88247620463,147604.42154505165},
        new object[]{20440,23.6,55.3,326552.4958548533,2610957.0229078466},
    };

    // pyproj (PROJ) is the oracle. WGS 84 UTM must agree to 1 mm; Ain el Abd (3-parameter shift) to 3 m
    // because PROJ may pick a different published transformation for that datum.
    [Theory, MemberData(nameof(Ref))]
    public void ForwardMatchesProj(int epsg, double lat, double lon, double e, double n)
    {
        var cs = CoordSys.FromEpsg(epsg);
        cs.FromLonLat(lon, lat, out double x, out double y);
        double tol = (epsg >= 32000 || epsg >= 8836 && epsg <= 9360) ? 0.002 : 3.0;
        Assert.InRange(Math.Abs(x - e), 0, tol); Assert.InRange(Math.Abs(y - n), 0, tol);
    }

    [Theory, MemberData(nameof(Ref))]
    public void RoundTrip(int epsg, double lat, double lon, double e, double n)
    {
        var cs = CoordSys.FromEpsg(epsg);
        cs.FromLonLat(lon, lat, out double x, out double y);
        cs.ToLonLat(x, y, out double lon2, out double lat2);
        Assert.InRange(Math.Abs(lon2 - lon), 0, 1e-8); Assert.InRange(Math.Abs(lat2 - lat), 0, 1e-8);
    }

    [Fact] public void UnknownEpsgIsNull() { Assert.Null(CoordSys.FromEpsg(2319)); }

    [Fact] public void FeetUnits()
    {
        var cs = CoordSys.FromEpsg(32638); cs.UnitToMetre = 0.3048;
        cs.FromLonLat(46.6753, 24.7136, out double x, out double y);
        cs.ToLonLat(x, y, out double lon, out double lat);
        Assert.InRange(Math.Abs(lon - 46.6753), 0, 1e-8);
        Assert.True(x > 2000000); // feet, so larger than the metre value 669459
    }

    [Fact] public void TileMath()
    {
        Assert.Equal(0.0, Mercator.TileXf(-180, 3));
        Assert.InRange(Math.Abs(Mercator.TileYf(0, 1) - 1.0), 0, 1e-9);
        Assert.InRange(Math.Abs(Mercator.TileLon(Mercator.TileXf(46.6753, 18), 18) - 46.6753), 0, 1e-9);
        Assert.InRange(Math.Abs(Mercator.TileLat(Mercator.TileYf(24.7136, 18), 18) - 24.7136), 0, 1e-9);
        // known: zoom 0 pixel is ~156543 m at the equator
        Assert.InRange(Mercator.MetresPerPixel(0, 0), 156543, 156544);
        // Riyadh longitude at z17, value computed independently in Python
        int z = 17; Assert.Equal(82529, (int)Math.Floor(Mercator.TileXf(46.6753, z)));
    }

    [Fact] public void CoverLowersZoomToLimit()
    {
        int z = 20; var l = Mercator.Cover(46.6, 24.6, 46.8, 24.8, ref z, 40);
        Assert.True(l.Count <= 40 && l.Count > 0); Assert.True(z < 20);
    }
}

public class CacheTests
{
    [Fact] public void RespectsMaxAgeAndDropsStale()
    {
        DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var c = new TileCache(10, () => now);
        var id = new TileId(1, 1, 1);
        c.Put(id, new byte[] { 1 }, "private, max-age=3600");
        Assert.True(c.TryGetFresh(id, out var d));
        now = now.AddSeconds(3601);
        Assert.False(c.TryGetFresh(id, out d)); Assert.Equal(0, c.Count);
    }
    [Fact] public void NoHeaderOrNoStoreIsNotCached()
    {
        var c = new TileCache(10); var id = new TileId(1, 2, 2);
        c.Put(id, new byte[] { 1 }, null); Assert.Equal(0, c.Count);
        c.Put(id, new byte[] { 1 }, "no-store, max-age=100"); Assert.Equal(0, c.Count);
        c.Put(id, new byte[] { 1 }, "max-age=0"); Assert.Equal(0, c.Count);
    }
    [Fact] public void LruLimit()
    {
        var c = new TileCache(3);
        for (int i = 0; i < 6; i++) c.Put(new TileId(1, i, 0), new byte[] { 1 }, "max-age=100");
        Assert.Equal(3, c.Count);
    }
}

public class GoogleParseTests
{
    [Fact] public void ParsesSession()
    {
        string j = "{\"session\":\"abc-123\",\"expiry\":\"1800000000\",\"tileWidth\":256,\"tileHeight\":256,\"imageFormat\":\"jpeg\"}";
        Assert.Equal("abc-123", GoogleTiles.ParseSession(j));
        Assert.Equal(new DateTime(2027, 1, 15, 8, 0, 0, DateTimeKind.Utc), GoogleTiles.ParseExpiry(j));
    }
    [Fact] public void ParsesViewport()
    {
        string j = "{\"copyright\":\"Imagery ©2026 Maxar\",\"maxZoomRects\":[{\"maxZoom\":19},{\"maxZoom\":21}]}";
        Assert.Equal(21, GoogleTiles.ParseMaxZoom(j)); Assert.Contains("Maxar", GoogleTiles.ParseCopyright(j));
    }
    [Fact] public void SessionBodies()
    {
        Assert.Contains("satellite", GoogleTiles.SessionBody(MapKind.Satellite));
        Assert.Contains("layerRoadmap", GoogleTiles.SessionBody(MapKind.Hybrid));
        Assert.Contains("roadmap", GoogleTiles.SessionBody(MapKind.Roadmap));
    }
}
