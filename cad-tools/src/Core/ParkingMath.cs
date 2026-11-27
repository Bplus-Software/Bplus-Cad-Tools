using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BPlus.LiveMap.Core
{
    /// <summary>A stall type. Angle 0 = parallel to the curb, 90 = perpendicular, anything between = angled. All sizes in metres.</summary>
    public sealed class ParkPreset
    {
        public string Name = ""; public double Width, Length, Angle, Aisle;
        public ParkPreset Copy() { return new ParkPreset { Name = Name, Width = Width, Length = Length, Angle = Angle, Aisle = Aisle }; }
        /// <summary>Distance along the curb that one stall takes.</summary>
        public double Pitch { get { return Angle <= 0.001 ? Length : Angle >= 89.999 ? Width : Width / Math.Sin(Angle * Math.PI / 180.0); } }
        /// <summary>How far the stall reaches away from the curb.</summary>
        public double Depth { get { return Angle <= 0.001 ? Width : Angle >= 89.999 ? Length : Length * Math.Sin(Angle * Math.PI / 180.0); } }
    }

    public static class ParkingMath
    {
        /// <summary>Ministry of Municipalities and Housing sizes: parallel 2.5 x 6.5, perpendicular and angled 2.8 x 5.5 (istitlaa.ncc.gov.sa, Parking design standards). Aisle values are NOT from a source and are editable.</summary>
        public static List<ParkPreset> Defaults()
        {
            return new List<ParkPreset> {
                new ParkPreset { Name = "Parallel", Width = 2.5, Length = 6.5, Angle = 0, Aisle = 3.5 },
                new ParkPreset { Name = "90 degrees", Width = 2.8, Length = 5.5, Angle = 90, Aisle = 6.0 },
                new ParkPreset { Name = "45 degrees", Width = 2.8, Length = 5.5, Angle = 45, Aisle = 3.5 } };
        }

        public static string Pack(IList<ParkPreset> l) { return string.Join(";", l.Select(p => p.Name + "|" + p.Width.ToString("R", CultureInfo.InvariantCulture) + "|" + p.Length.ToString("R", CultureInfo.InvariantCulture) + "|" + p.Angle.ToString("R", CultureInfo.InvariantCulture) + "|" + p.Aisle.ToString("R", CultureInfo.InvariantCulture))); }

        /// <summary>Reads saved presets; anything missing or wrong falls back to the default for that type.</summary>
        public static List<ParkPreset> Unpack(string s)
        {
            var res = Defaults();
            if (string.IsNullOrWhiteSpace(s)) return res;
            var parts = s.Split(';');
            for (int i = 0; i < res.Count && i < parts.Length; i++)
            {
                var f = parts[i].Split('|'); if (f.Length < 5) continue; double w, l, a, ai;
                if (double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out w) && double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out l) && double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out ai) && double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out a)
                    && w > 0 && w <= 20 && l > 0 && l <= 30 && ai >= 0 && ai <= 30 && Math.Abs(a - res[i].Angle) < 0.001) { res[i].Width = w; res[i].Length = l; res[i].Aisle = ai; }
            }
            return res;
        }

        /// <summary>How many whole stalls fit on a line of this length.</summary>
        public static int Fit(double lineLength, ParkPreset p) { return (int)Math.Floor((lineLength + 1e-9) / p.Pitch); }

        /// <summary>Four corners (x,y x4) of a stall. A and B are the stall's two points on the curb, in order along the line; nx,ny is the unit normal pointing to the parking side.</summary>
        public static double[] Corners(double ax, double ay, double bx, double by, double nx, double ny, ParkPreset p)
        {
            double tx = bx - ax, ty = by - ay, len = Math.Sqrt(tx * tx + ty * ty); if (len < 1e-9) throw new ArgumentException("zero-length stall");
            tx /= len; ty /= len; double dx, dy;
            if (p.Angle <= 0.001) { dx = nx * p.Width; dy = ny * p.Width; }
            else if (p.Angle >= 89.999) { dx = nx * p.Length; dy = ny * p.Length; }
            else { double a = p.Angle * Math.PI / 180.0; dx = (nx * Math.Sin(a) + tx * Math.Cos(a)) * p.Length; dy = (ny * Math.Sin(a) + ty * Math.Cos(a)) * p.Length; }
            return new[] { ax, ay, bx, by, bx + dx, by + dy, ax + dx, ay + dy };
        }
    }
}
