using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using BPlus.LiveMap.Core;

namespace BPlus.LiveMap
{
    /// <summary>Converts drawing coordinates to WGS 84 lon/lat and back, using (in this order):
    /// the drawing's own geographic data, then a coordinate system the user picked and saved in this drawing.</summary>
    abstract class CsProvider
    {
        public string Source, Description;
        public abstract void ToLonLat(double x, double y, out double lon, out double lat);
        public abstract void FromLonLat(double lon, double lat, out double x, out double y);
    }

    sealed class GeoDataProvider : CsProvider
    {
        readonly Database _db; readonly ObjectId _id; readonly bool _swap;
        public GeoDataProvider(Database db, ObjectId id, string cs, bool swap)
        { _db = db; _id = id; _swap = swap; Source = "drawing geographic data"; Description = string.IsNullOrEmpty(cs) ? "geographic location in this drawing" : cs; }

        public override void ToLonLat(double x, double y, out double lon, out double lat)
        {
            using (var tr = _db.TransactionManager.StartOpenCloseTransaction())
            { var g = (GeoLocationData)tr.GetObject(_id, OpenMode.ForRead); var r = g.TransformToLonLatAlt(x, y, 0); lon = r.Longitude; lat = r.Latitude; tr.Commit(); }
        }
        public override void FromLonLat(double lon, double lat, out double x, out double y)
        {
            using (var tr = _db.TransactionManager.StartOpenCloseTransaction())
            { var g = (GeoLocationData)tr.GetObject(_id, OpenMode.ForRead); var p = g.TransformFromLonLatAlt(_swap ? new Point3d(lat, lon, 0) : new Point3d(lon, lat, 0)); x = p.X; y = p.Y; tr.Commit(); }
        }
    }

    sealed class PickedProvider : CsProvider
    {
        readonly CoordSys _cs;
        public PickedProvider(CoordSys cs) { _cs = cs; Source = "coordinate system saved in this drawing"; Description = "EPSG:" + cs.Epsg + " " + cs.Name; }
        public override void ToLonLat(double x, double y, out double lon, out double lat) { _cs.ToLonLat(x, y, out lon, out lat); }
        public override void FromLonLat(double lon, double lat, out double x, out double y) { _cs.FromLonLat(lon, lat, out x, out y); }
    }

    static class DrawingCs
    {
        const string DictName = "BPLUS_LIVEMAP";

        public static double UnitToMetre(Database db)
        {
            switch (db.Insunits.ToString())
            {
                case "Millimeters": return 0.001;
                case "Centimeters": return 0.01;
                case "Decimeters": return 0.1;
                case "Kilometers": return 1000;
                case "Feet": return 0.3048;
                case "USSurveyFeet": return 1200.0 / 3937.0;
                case "Inches": return 0.0254;
                case "USSurveyInch": return 1200.0 / 3937.0 / 12;
                case "Yards": return 0.9144;
                case "Miles": return 1609.344;
                default: return 1.0; // Meters, Undefined (treated as metres)
            }
        }

        /// <summary>Returns a provider, or null when the drawing has no usable coordinate system (the picker is then shown).</summary>
        public static CsProvider Get(Database db)
        {
            try
            {
                var gid = db.GeoDataObject;
                if (!gid.IsNull)
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var g = (GeoLocationData)tr.GetObject(gid, OpenMode.ForRead);
                        string cs = ""; try { cs = g.CoordinateSystem ?? ""; } catch { }
                        // Check which axis order the inverse transform wants by round-tripping the reference point.
                        var probe = g.DesignPoint;
                        var ll = g.TransformToLonLatAlt(probe.X, probe.Y, 0);
                        var back = g.TransformFromLonLatAlt(new Point3d(ll.Longitude, ll.Latitude, 0));
                        bool ok = Math.Abs(back.X - probe.X) < 0.5 && Math.Abs(back.Y - probe.Y) < 0.5;
                        bool swap = false;
                        if (!ok)
                        {
                            var back2 = g.TransformFromLonLatAlt(new Point3d(ll.Latitude, ll.Longitude, 0));
                            swap = Math.Abs(back2.X - probe.X) < 0.5 && Math.Abs(back2.Y - probe.Y) < 0.5;
                            if (!swap) { Log.Write("Drawing geographic data found but its transform did not round-trip; ignoring it."); tr.Commit(); return PickedFor(db); }
                        }
                        tr.Commit();
                        return new GeoDataProvider(db, gid, cs, swap);
                    }
                }
            }
            catch (Exception e) { Log.Write("Reading drawing geographic data failed: " + e.GetType().Name + " " + e.Message); }
            return PickedFor(db);
        }

        static CsProvider PickedFor(Database db)
        {
            int epsg = LoadEpsg(db);
            if (epsg == 0) return null;
            var cs = CoordSys.FromEpsg(epsg);
            if (cs == null) return null;
            cs.UnitToMetre = UnitToMetre(db);
            return new PickedProvider(cs);
        }

        public static int LoadEpsg(Database db)
        {
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                    if (!nod.Contains(DictName)) return 0;
                    var xr = (Xrecord)tr.GetObject(nod.GetAt(DictName), OpenMode.ForRead);
                    foreach (TypedValue tv in xr.Data) if (tv.TypeCode == (short)DxfCode.Int32) return (int)tv.Value;
                }
            }
            catch (Exception e) { Log.Write("Reading saved coordinate system failed: " + e.Message); }
            return 0;
        }

        public static void SaveEpsg(Database db, int epsg)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                Xrecord xr;
                if (nod.Contains(DictName)) xr = (Xrecord)tr.GetObject(nod.GetAt(DictName), OpenMode.ForWrite);
                else { xr = new Xrecord(); nod.SetAt(DictName, xr); tr.AddNewlyCreatedDBObject(xr, true); }
                xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Int32, epsg));
                tr.Commit();
            }
        }
    }
}
