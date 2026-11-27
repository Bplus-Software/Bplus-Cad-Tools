using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BPlus.LiveMap.Core
{
    /// <summary>One element of a hand-built road section. Side: L, C (median only) or R. W metres. Slope in percent, positive falls away from the centre.
    /// A and B depend on the type: Kerb A = height; Median A = kerb height; Tree A = trunk height, B = canopy width; Walkway A = 1 draws a person; Slope A = 1:n, B = longest length.</summary>
    public sealed class AsmElem
    {
        public string Type = "Lane", Side = "R"; public double W = 3.5, Slope = 2, A, B;
        public AsmElem Copy() { return new AsmElem { Type = Type, Side = Side, W = W, Slope = Slope, A = A, B = B }; }
    }

    public sealed class SecAssembly
    {
        public string Name = ""; public List<AsmElem> Items = new List<AsmElem>(); public double CentreLevel = double.NaN;
        public SecAssembly Copy() { var a = new SecAssembly { Name = Name, CentreLevel = CentreLevel }; foreach (var i in Items) a.Items.Add(i.Copy()); return a; }
    }

    public sealed class AsmGeo
    {
        public List<ProfSeg> Segs = new List<ProfSeg>(); public List<double[]> Dims = new List<double[]>(); public List<string> DimText = new List<string>(); public List<string> Notes = new List<string>();
        public double Left, Right, MinZ = double.MaxValue, MaxZ = double.MinValue;
        public void Add(double x1, double z1, double x2, double z2, string kind)
        {
            Segs.Add(new ProfSeg { X1 = x1, Y1 = z1, X2 = x2, Y2 = z2, Kind = kind });
            Left = Math.Min(Left, Math.Min(x1, x2)); Right = Math.Max(Right, Math.Max(x1, x2)); MinZ = Math.Min(MinZ, Math.Min(z1, z2)); MaxZ = Math.Max(MaxZ, Math.Max(z1, z2));
        }
    }

    public static class AssemblyMath
    {
        public static readonly string[] Types = { "Lane", "Shoulder", "Kerb", "Sidewalk", "Walkway", "Planting", "Tree", "Median", "Ditch", "Slope to ground", "Custom" };
        static string R(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        static bool Num(string t, out double v) { return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v); }

        public static AsmElem Make(string type, string side)
        {
            var e = new AsmElem { Type = type, Side = side };
            switch (type)
            {
                case "Lane": e.W = 3.5; e.Slope = 2; break;
                case "Shoulder": e.W = 1.5; e.Slope = 4; break;
                case "Kerb": e.W = 0.15; e.Slope = 0; e.A = 0.15; break;
                case "Sidewalk": e.W = 1.8; e.Slope = -2; break;
                case "Walkway": e.W = 2.0; e.Slope = 0; e.A = 0; break;
                case "Planting": e.W = 2.0; e.Slope = 0; break;
                case "Tree": e.W = 2.0; e.Slope = 0; e.A = 3.0; e.B = 3.0; break;
                case "Median": e.Side = "C"; e.W = 3.0; e.Slope = 0; e.A = 0.15; break;
                case "Ditch": e.W = 1.5; e.Slope = 10; break;
                case "Slope to ground": e.W = 0; e.Slope = 0; e.A = 2; e.B = 30; break;
                default: e.W = 1.0; e.Slope = 0; break;
            }
            return e;
        }

        public static List<SecAssembly> Defaults()
        {
            var a = new SecAssembly { Name = "Local 2 lane (example)" };
            a.Items.Add(Make("Sidewalk", "L")); a.Items.Add(Make("Kerb", "L")); a.Items.Add(Make("Lane", "L")); a.Items.Add(Make("Lane", "R")); a.Items.Add(Make("Kerb", "R")); a.Items.Add(Make("Sidewalk", "R"));
            var b = new SecAssembly { Name = "Arterial with median, trees and walkway (example)" };
            var l = new List<AsmElem> { Make("Slope to ground", "L"), Make("Walkway", "L"), Make("Tree", "L"), Make("Sidewalk", "L"), Make("Kerb", "L"), Make("Lane", "L"), Make("Lane", "L") };
            foreach (var e in l) b.Items.Add(e); b.Items.Add(Make("Median", "C"));
            var r = l.Select(e => e.Copy()).ToList(); foreach (var e in r) e.Side = "R"; r.Reverse(); foreach (var e in r) b.Items.Add(e);
            foreach (var e in b.Items) { if (e.Type == "Lane") e.W = 3.65; if (e.Type == "Walkway") e.A = 1; }
            return new List<SecAssembly> { a, b };
        }

        /// <summary>Mirror: the right side becomes the left side reversed.</summary>
        public static void MirrorLeftToRight(SecAssembly a)
        {
            var left = a.Items.Where(e => e.Side == "L").ToList(); var c = a.Items.Where(e => e.Side == "C").ToList();
            var right = left.Select(e => { var x = e.Copy(); x.Side = "R"; return x; }).Reverse().ToList();
            a.Items = left.Concat(c).Concat(right).ToList();
        }

        static string Clean(string n) { return new string((n ?? "").Where(ch => ch != '|' && ch != ';' && ch != '#' && ch != '@' && ch != '\n' && ch != '\r').ToArray()).Trim(); }
        public static string Pack(IList<SecAssembly> l)
        {
            return string.Join("##", l.Where(a => Clean(a.Name).Length > 0).Select(a => Clean(a.Name) + "@" + string.Join(";", a.Items.Select(e => e.Type + "|" + e.Side + "|" + R(e.W) + "|" + R(e.Slope) + "|" + R(e.A) + "|" + R(e.B)))));
        }
        public static List<SecAssembly> Unpack(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Defaults(); var res = new List<SecAssembly>();
            foreach (var part in s.Split(new[] { "##" }, StringSplitOptions.RemoveEmptyEntries))
            {
                int at = part.IndexOf('@'); if (at < 1) continue; var a = new SecAssembly { Name = part.Substring(0, at) };
                foreach (var it in part.Substring(at + 1).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var f = it.Split('|'); double w, sl, x, y; if (f.Length < 6 || !Types.Contains(f[0]) || !(f[1] == "L" || f[1] == "R" || f[1] == "C") || !Num(f[2], out w) || !Num(f[3], out sl) || !Num(f[4], out x) || !Num(f[5], out y)) continue;
                    if (w < 0 || w > 100 || Math.Abs(sl) > 100) continue; a.Items.Add(new AsmElem { Type = f[0], Side = f[1], W = w, Slope = sl, A = x, B = y });
                }
                res.Add(a);
            }
            return res.Count == 0 ? Defaults() : res;
        }

        /// <summary>First problem found in an element, or empty text.</summary>
        public static string Check(AsmElem e)
        {
            if (e.Type == "Median" && e.Side != "C") return "A median goes in the centre."; if (e.Type != "Median" && e.Side == "C") return e.Type + " must be on the left or the right.";
            if (e.Type == "Slope to ground") { if (e.A <= 0.1 || e.A > 20) return "Slope to ground: the ratio (1:n) must be between 0.1 and 20."; if (e.B <= 0 || e.B > 200) return "Slope to ground: the longest length must be between 0 and 200 m."; return ""; }
            if (e.W <= 0 || e.W > 100) return e.Type + ": width must be above 0 and at most 100 m.";
            if (Math.Abs(e.Slope) > 100) return e.Type + ": slope must be within 100 %.";
            if (e.Type == "Kerb" && (e.A < 0 || e.A > 1)) return "Kerb: height must be between 0 and 1 m.";
            if (e.Type == "Median" && (e.A < 0 || e.A > 1)) return "Median: kerb height must be between 0 and 1 m.";
            if (e.Type == "Tree" && (e.A <= 0 || e.A > 30 || e.B <= 0 || e.B > 30)) return "Tree: trunk height and canopy width must be between 0 and 30 m.";
            return "";
        }

        static string F(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        /// <summary>Geometry of the assembly in (offset, level). Left of the line is negative. ground can be null (slopes then run to their longest length).</summary>
        public static AsmGeo Layout(SecAssembly a, double cl, IList<SecPt> ground)
        {
            var g = new AsmGeo(); var gr = ground == null ? new List<SecPt>() : ground.OrderBy(p => p.Off).ToList();
            double half = a.Items.Where(e => e.Side == "C" && e.Type == "Median").Sum(e => e.W) / 2;
            double zc = cl;
            if (half > 0)
            {
                double kh = a.Items.Where(e => e.Type == "Median").Max(e => e.A);
                g.Add(-half, cl, -half, cl + kh, "walk"); g.Add(-half, cl + kh, half, cl + kh, "walk"); g.Add(half, cl + kh, half, cl, "walk"); g.Dims.Add(new[] { 0.0 }); g.DimText.Add(F(half * 2));
            }
            else g.Add(0, cl, 0, cl, "road");
            foreach (int dir in new[] { -1, 1 })
            {
                string side = dir < 0 ? "L" : "R"; var items = a.Items.Where(e => e.Side == side).ToList(); if (dir < 0) items.Reverse(); // outward from the centre
                double o = dir * half, z = zc;
                foreach (var e in items)
                {
                    if (e.Type == "Slope to ground") { Slope(g, gr, e, dir, o, z); break; }
                    double o2 = o + dir * e.W, z2 = z - e.Slope / 100.0 * e.W; string kind = e.Type == "Lane" || e.Type == "Shoulder" || e.Type == "Custom" ? "road" : e.Type == "Sidewalk" ? "walk" : e.Type == "Walkway" ? "ped" : "plant";
                    if (e.Type == "Kerb") { g.Add(o, z, o, z + e.A, "walk"); g.Add(o, z + e.A, o2, z + e.A, "walk"); z2 = z + e.A; }
                    else if (e.Type == "Ditch") { g.Add(o, z, o + dir * e.W / 2, z - Math.Abs(e.Slope) / 100.0 * e.W / 2, "plant"); g.Add(o + dir * e.W / 2, z - Math.Abs(e.Slope) / 100.0 * e.W / 2, o2, z, "plant"); z2 = z; }
                    else g.Add(o, z, o2, z2, kind);
                    if (e.Type == "Tree") Tree(g, (o + o2) / 2, (z + z2) / 2, e);
                    if (e.Type == "Walkway" && e.A >= 1) Person(g, (o + o2) / 2, (z + z2) / 2);
                    if (e.W >= 0.5) { g.Dims.Add(new[] { (o + o2) / 2 }); g.DimText.Add(F(e.W)); } o = o2; z = z2;
                }
            }
            if (g.Segs.Count > 0) { if (g.Left > 0) g.Left = 0; if (g.Right < 0) g.Right = 0; }
            return g;
        }

        static void Tree(AsmGeo g, double x, double z, AsmElem e)
        {
            g.Add(x, z, x, z + e.A, "tree"); double cx = x, cz = z + e.A, r = e.B / 2; double px = cx + r, pz = cz;
            for (int i = 1; i <= 16; i++) { double t = i * Math.PI / 8, nx = cx + r * Math.Cos(t), nz = cz + r * Math.Sin(t); g.Add(px, pz, nx, nz, "tree"); px = nx; pz = nz; }
        }

        static void Person(AsmGeo g, double x, double z)
        {   // simple 1.7 m figure: legs, body, arms, head
            g.Add(x - 0.2, z, x, z + 0.85, "ped"); g.Add(x + 0.2, z, x, z + 0.85, "ped"); g.Add(x, z + 0.85, x, z + 1.45, "ped"); g.Add(x - 0.3, z + 0.95, x, z + 1.35, "ped"); g.Add(x + 0.3, z + 0.95, x, z + 1.35, "ped");
            double r = 0.13, cz = z + 1.58, px = x + r, pz = cz; for (int i = 1; i <= 8; i++) { double t = i * Math.PI / 4, nx = x + r * Math.Cos(t), nz = cz + r * Math.Sin(t); g.Add(px, pz, nx, nz, "ped"); px = nx; pz = nz; }
        }

        static void Slope(AsmGeo g, List<SecPt> gr, AsmElem e, int dir, double o, double z)
        {
            double n = e.A, max = e.B; double g0 = GroundOrNaN(gr, o);
            if (double.IsNaN(g0)) { double zz = z - max / n; g.Add(o, z, o + dir * max, zz, "slope"); g.Notes.Add("no ground at the " + (dir < 0 ? "left" : "right") + " edge: slope drawn down for its longest length (" + F(max) + " m)"); return; }
            int d = z >= g0 ? -1 : 1; double step = 0.05, prevT = 0, prevD = z - g0;
            for (double t = step; t <= max + 1e-9; t += step)
            {
                double gz = GroundOrNaN(gr, o + dir * t); double lz = z + d * t / n;
                if (double.IsNaN(gz)) { g.Add(o, z, o + dir * prevT, z + d * prevT / n, "slope"); g.Notes.Add("the ground ends before the " + (dir < 0 ? "left" : "right") + " slope meets it: drawn to the last point with ground"); return; }
                double df = lz - gz;
                if (df * prevD <= 0) { double k = prevD == df ? 0 : prevD / (prevD - df); double tt = prevT + k * (t - prevT); g.Add(o, z, o + dir * tt, z + d * tt / n, "slope"); return; }
                prevT = t; prevD = df;
            }
            g.Add(o, z, o + dir * max, z + d * max / n, "slope"); g.Notes.Add("the " + (dir < 0 ? "left" : "right") + " slope did not meet the ground within " + F(max) + " m");
        }
        static double GroundOrNaN(List<SecPt> gr, double off) { return gr.Count < 2 ? double.NaN : SectionMath.GroundAt(gr, off); }
    }
}
