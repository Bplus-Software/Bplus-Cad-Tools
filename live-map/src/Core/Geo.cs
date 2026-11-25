using System;
using System.Collections.Generic;

namespace BPlus.LiveMap.Core
{
    /// <summary>Reference ellipsoid.</summary>
    public sealed class Ellipsoid
    {
        public readonly double A, F, E2;
        public Ellipsoid(double a, double invF) { A = a; F = 1.0 / invF; E2 = F * (2 - F); }
        public static readonly Ellipsoid Wgs84 = new Ellipsoid(6378137.0, 298.257223563);
        public static readonly Ellipsoid International1924 = new Ellipsoid(6378388.0, 297.0);
    }

    /// <summary>Transverse Mercator (Kruger series, 6th order), accurate to well under a millimetre inside a UTM zone.</summary>
    public sealed class TransverseMercator
    {
        readonly Ellipsoid _e; readonly double _lon0, _k0, _fe, _fn;
        readonly double _bigA; readonly double[] _alpha = new double[7], _beta = new double[7];

        public TransverseMercator(Ellipsoid e, double lon0Deg, double k0, double falseEasting, double falseNorthing)
        {
            _e = e; _lon0 = lon0Deg * Math.PI / 180; _k0 = k0; _fe = falseEasting; _fn = falseNorthing;
            double n = e.F / (2 - e.F);
            double n2 = n * n, n3 = n2 * n, n4 = n3 * n, n5 = n4 * n, n6 = n5 * n;
            _bigA = e.A / (1 + n) * (1 + n2 / 4 + n4 / 64 + n6 / 256);
            _alpha[1] = n / 2 - 2 * n2 / 3 + 5 * n3 / 16 + 41 * n4 / 180 - 127 * n5 / 288 + 7891 * n6 / 37800;
            _alpha[2] = 13 * n2 / 48 - 3 * n3 / 5 + 557 * n4 / 1440 + 281 * n5 / 630 - 1983433 * n6 / 1935360;
            _alpha[3] = 61 * n3 / 240 - 103 * n4 / 140 + 15061 * n5 / 26880 + 167603 * n6 / 181440;
            _alpha[4] = 49561 * n4 / 161280 - 179 * n5 / 168 + 6601661 * n6 / 7257600;
            _alpha[5] = 34729 * n5 / 80640 - 3418889 * n6 / 1995840;
            _alpha[6] = 212378941 * n6 / 319334400;
            _beta[1] = n / 2 - 2 * n2 / 3 + 37 * n3 / 96 - n4 / 360 - 81 * n5 / 512 + 96199 * n6 / 604800;
            _beta[2] = n2 / 48 + n3 / 15 - 437 * n4 / 1440 + 46 * n5 / 105 - 1118711 * n6 / 3870720;
            _beta[3] = 17 * n3 / 480 - 37 * n4 / 840 - 209 * n5 / 4480 + 5569 * n6 / 90720;
            _beta[4] = 4397 * n4 / 161280 - 11 * n5 / 504 - 830251 * n6 / 7257600;
            _beta[5] = 4583 * n5 / 161280 - 108847 * n6 / 3991680;
            _beta[6] = 20648693 * n6 / 638668800;
        }

        public void Forward(double latDeg, double lonDeg, out double easting, out double northing)
        {
            double lat = latDeg * Math.PI / 180, dLon = lonDeg * Math.PI / 180 - _lon0;
            double n = _e.F / (2 - _e.F);
            double t = Math.Sinh(Atanh(Math.Sin(lat)) - 2 * Math.Sqrt(n) / (1 + n) * Atanh(2 * Math.Sqrt(n) / (1 + n) * Math.Sin(lat)));
            double xiP = Math.Atan2(t, Math.Cos(dLon));
            double etaP = Atanh(Math.Sin(dLon) / Math.Sqrt(1 + t * t));
            double xi = xiP, eta = etaP;
            for (int j = 1; j <= 6; j++)
            {
                xi += _alpha[j] * Math.Sin(2 * j * xiP) * Math.Cosh(2 * j * etaP);
                eta += _alpha[j] * Math.Cos(2 * j * xiP) * Math.Sinh(2 * j * etaP);
            }
            easting = _fe + _k0 * _bigA * eta;
            northing = _fn + _k0 * _bigA * xi;
        }

        public void Inverse(double easting, double northing, out double latDeg, out double lonDeg)
        {
            double xi = (northing - _fn) / (_k0 * _bigA), eta = (easting - _fe) / (_k0 * _bigA);
            double xiP = xi, etaP = eta;
            for (int j = 1; j <= 6; j++)
            {
                xiP -= _beta[j] * Math.Sin(2 * j * xi) * Math.Cosh(2 * j * eta);
                etaP -= _beta[j] * Math.Cos(2 * j * xi) * Math.Sinh(2 * j * eta);
            }
            double chi = Math.Asin(Math.Sin(xiP) / Math.Cosh(etaP));
            // conformal latitude -> geodetic latitude by fixed-point iteration (converges in a few steps)
            double e = Math.Sqrt(_e.E2);
            double lat = chi;
            for (int i = 0; i < 12; i++)
            {
                double s = Math.Sin(lat);
                lat = 2 * Math.Atan(Math.Exp(Atanh(Math.Sin(chi)) + e * Atanh(e * s))) - Math.PI / 2;
            }
            double dLon = Math.Atan2(Math.Sinh(etaP), Math.Cos(xiP));
            latDeg = lat * 180 / Math.PI;
            lonDeg = (_lon0 + dLon) * 180 / Math.PI;
        }

        static double Atanh(double x) { return 0.5 * Math.Log((1 + x) / (1 - x)); }
    }

    /// <summary>Geocentric 3-parameter datum shift (translation only).</summary>
    public static class Datum
    {
        public static void ToXyz(Ellipsoid e, double latDeg, double lonDeg, double h, out double x, out double y, out double z)
        {
            double lat = latDeg * Math.PI / 180, lon = lonDeg * Math.PI / 180;
            double sl = Math.Sin(lat), cl = Math.Cos(lat);
            double nu = e.A / Math.Sqrt(1 - e.E2 * sl * sl);
            x = (nu + h) * cl * Math.Cos(lon); y = (nu + h) * cl * Math.Sin(lon); z = (nu * (1 - e.E2) + h) * sl;
        }

        public static void FromXyz(Ellipsoid e, double x, double y, double z, out double latDeg, out double lonDeg)
        {
            double p = Math.Sqrt(x * x + y * y);
            double lat = Math.Atan2(z, p * (1 - e.E2));
            for (int i = 0; i < 10; i++)
            {
                double sl = Math.Sin(lat);
                double nu = e.A / Math.Sqrt(1 - e.E2 * sl * sl);
                lat = Math.Atan2(z + e.E2 * nu * sl, p);
            }
            latDeg = lat * 180 / Math.PI; lonDeg = Math.Atan2(y, x) * 180 / Math.PI;
        }

        public static void Shift(Ellipsoid from, Ellipsoid to, double dx, double dy, double dz, double latDeg, double lonDeg, out double lat2, out double lon2)
        {
            double x, y, z; ToXyz(from, latDeg, lonDeg, 0, out x, out y, out z);
            FromXyz(to, x + dx, y + dy, z + dz, out lat2, out lon2);
        }
    }

    /// <summary>The coordinate systems the tool can convert by itself (used when the drawing carries no geographic data).</summary>
    public sealed class CoordSys
    {
        public int Epsg { get; private set; }
        public string Name { get; private set; }
        public bool IsGeographic { get; private set; }
        /// <summary>Metres per drawing unit.</summary>
        public double UnitToMetre { get; set; }
        readonly TransverseMercator _tm; readonly bool _ainElAbd; readonly bool _webMerc;

        CoordSys(int epsg, string name, TransverseMercator tm, bool ain, bool geo, bool webMerc)
        { Epsg = epsg; Name = name; _tm = tm; _ainElAbd = ain; IsGeographic = geo; _webMerc = webMerc; UnitToMetre = 1.0; }

        // Ain el Abd to WGS 84, geocentric translations (EPSG transformation 1 as used for Saudi Arabia).
        public const double AinDx = -143, AinDy = -236, AinDz = 7;

        public static CoordSys FromEpsg(int epsg)
        {
            if (epsg == 4326) return new CoordSys(epsg, "WGS 84 (lat / long)", null, false, true, false);
            if (epsg == 3857) return new CoordSys(epsg, "WGS 84 / Pseudo-Mercator", null, false, false, true);
            if (epsg >= 32601 && epsg <= 32660) return Utm(epsg, epsg - 32600, true, Ellipsoid.Wgs84, false, "WGS 84 / UTM zone " + (epsg - 32600) + "N");
            if (epsg >= 32701 && epsg <= 32760) return Utm(epsg, epsg - 32700, false, Ellipsoid.Wgs84, false, "WGS 84 / UTM zone " + (epsg - 32700) + "S");
            if (epsg >= 20435 && epsg <= 20439) return Utm(epsg, epsg - 20400, true, Ellipsoid.International1924, true, "Ain el Abd / UTM zone " + (epsg - 20400) + "N");
            return null;
        }

        static CoordSys Utm(int epsg, int zone, bool north, Ellipsoid e, bool ain, string name)
        {
            double lon0 = zone * 6 - 183;
            return new CoordSys(epsg, name, new TransverseMercator(e, lon0, 0.9996, 500000, north ? 0 : 10000000), ain, false, false);
        }

        /// <summary>The list shown in the picker (Saudi Arabia first, then generic UTM zones).</summary>
        public static List<CoordSys> PickerList()
        {
            var l = new List<CoordSys>();
            foreach (int c in new[] { 32637, 32638, 32639, 20437, 20438, 20439, 4326, 3857 }) l.Add(FromEpsg(c));
            return l;
        }

        /// <summary>Drawing coordinates (x, y) to WGS 84 longitude / latitude.</summary>
        public void ToLonLat(double x, double y, out double lon, out double lat)
        {
            x *= UnitToMetre; y *= UnitToMetre;
            if (IsGeographic) { lon = x / UnitToMetre; lat = y / UnitToMetre; return; }
            if (_webMerc)
            {
                const double R = 6378137.0;
                lon = x / R * 180 / Math.PI; lat = (2 * Math.Atan(Math.Exp(y / R)) - Math.PI / 2) * 180 / Math.PI; return;
            }
            double la, lo; _tm.Inverse(x, y, out la, out lo);
            if (_ainElAbd) Datum.Shift(Ellipsoid.International1924, Ellipsoid.Wgs84, AinDx, AinDy, AinDz, la, lo, out la, out lo);
            lat = la; lon = lo;
        }

        /// <summary>WGS 84 longitude / latitude to drawing coordinates.</summary>
        public void FromLonLat(double lon, double lat, out double x, out double y)
        {
            if (IsGeographic) { x = lon; y = lat; return; }
            if (_webMerc)
            {
                const double R = 6378137.0;
                x = R * lon * Math.PI / 180; y = R * Math.Log(Math.Tan(Math.PI / 4 + lat * Math.PI / 360));
            }
            else
            {
                double la = lat, lo = lon;
                if (_ainElAbd) Datum.Shift(Ellipsoid.Wgs84, Ellipsoid.International1924, -AinDx, -AinDy, -AinDz, lat, lon, out la, out lo);
                _tm.Forward(la, lo, out x, out y);
            }
            x /= UnitToMetre; y /= UnitToMetre;
        }
    }
}
