using System;
using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BPlus.LiveMap
{
    static class Ribbon
    {
        const string TabId = "BPLUS_CADTOOLS_TAB";
        static bool _hooked;
        public static void Install()
        {
            if (TryBuild()) return;
            if (_hooked) return; _hooked = true;
            ComponentManager.ItemInitialized += OnInit;
            AcApp.Idle += OnIdle;
        }
        static void OnInit(object s, RibbonItemEventArgs e) { if (ComponentManager.Ribbon != null && TryBuild()) Unhook(); }
        static void OnIdle(object s, EventArgs e) { if (ComponentManager.Ribbon != null && TryBuild()) Unhook(); }
        static void Unhook() { ComponentManager.ItemInitialized -= OnInit; AcApp.Idle -= OnIdle; _hooked = false; }

        static bool TryBuild()
        {
            try
            {
                var rc = ComponentManager.Ribbon; if (rc == null) return false;
                foreach (var t in rc.Tabs) if (t.Id == TabId) return true;
                var tab = new RibbonTab { Title = "B+ CAD Tools", Id = TabId };
                AddPanel(tab, "Points", L("Import\npoints", "BPIMPORT ", "import", "Import a points file (txt, csv, xlsx) as points with codes and layers"), L("Export\npoints", "BPEXPORT ", "export", "Export points to a clean text or csv file"), L("Show / hide", "BPISOLATE ", "layers", "Show or hide point categories"), L("Codes", "BPCODES ", "codes", "Edit the point code rules"));
                AddPanel(tab, "Draw", L("Connect\npoints", "BPCONNECT ", "connect", "Draw lines through points by code and point number"));
                AddPanel(tab, "Polylines", L("Lengths", "BPLENGTHS ", "lengths", "List length and area of selected polylines, with table, CSV and labels"), L("Join", "BPJOIN ", "join", "Join lines, polylines and arcs, closing small gaps"), L("Trim", "BPTRIM ", "trim", "Trim to a closed boundary: keep inside or outside"), L("Label", "BPLABEL ", "label", "Write length and area next to the selected polylines"), L("Auto\nleader", "BPLEADER ", "leader", "Select shapes: make a leader from each with its length or area"), L("Vertices to\npoints", "BPVERTS ", "verts", "Make a numbered point at every vertex"), L("Coordinates", "BPCOORD ", "coord", "Write E and N at points and vertices"));
                AddPanel(tab, "Hatch", L("Merge", "BPHATCHMERGE ", "merge", "Combine several hatches into one"), L("Split", "BPHATCHSPLIT ", "split", "Split a hatch into one hatch per separate area"));
                AddPanel(tab, "Survey QA", L("QA / QC", "BPQAQC ", "qa", "Check points: repeated numbers, duplicates, missing code or elevation, spikes"), L("Shift", "BPSHIFT ", "shift", "Move the selection by dE, dN, dZ"), L("Convert\npoints", "BPCONVPTS ", "convpts", "Convert AutoCAD points to Civil 3D COGO points and back. Needs Civil 3D"));
                AddPanel(tab, "Plot", L("Plot\nlayouts", "BPPLOT ", "plot", "Plot the layouts you tick: each layout's own page setup or your settings, to PDF or a printer"));
                AddPanel(tab, "Measure", L("Area", "BPAREA ", "area", "Area of closed polylines in m2, with perimeter in m"), L("Distance", "BPDIST ", "dist", "Distance, azimuth and bearing between two points"), L("Chain\nlength", "BPCHAIN ", "chain", "Length along many picked points, with each segment"), L("Area by\npoints", "BPAREAPTS ", "areapts", "Area and perimeter of picked corners"), L("Angle", "BPANGLE ", "angle", "Angle between two lines, from three points"), L("Pick\ncoordinates", "BPPICK ", "pickxy", "E, N, Z of picked points, with latitude and longitude when a coordinate system is set"));
                AddPanel(tab, "Quantities", L("Sinks", "BPSINKS ", "sinks", "Find where water collects on a surface: sinks with E, N, Z, depth, area and volume"), L("Cut and\nfill", "BPCUTFILL ", "cutfill", "Cut and fill volumes (m3) between the ground and another surface, a level or a plane"));
                var src = new RibbonPanelSource { Title = "Live Map" };
                var panel = new RibbonPanel { Source = src };
                var main = new RibbonButton { Text = "Live\nmap", ShowText = true, ShowImage = true, Size = RibbonItemSize.Large, Orientation = System.Windows.Controls.Orientation.Vertical, LargeImage = Icon(32, "map"), Image = Icon(16, "map"), CommandHandler = new Cmd("BPGMAP "), CommandParameter = "BPGMAP ", ToolTip = "Show or hide the live Google map behind the drawing" };
                src.Items.Add(main);
                src.Items.Add(new RibbonButton { Text = "Map\noptions", ShowText = true, ShowImage = true, Size = RibbonItemSize.Large, Orientation = System.Windows.Controls.Orientation.Vertical, LargeImage = Icon(32, "gear"), Image = Icon(16, "gear"), CommandHandler = new Cmd("BPMAPOPT "), ToolTip = "Map type, opacity, refresh, coordinate system and Google key" });
                tab.Panels.Add(panel);
                AddPanel(tab, "Settings", L("Coordinate\nsystem", "BPGMAPCS ", "cs", "Choose the coordinate system of the drawing: search by name, EPSG or country"), L("Settings", "BPSETTINGS ", "gear", "Options, point defaults, updates"), L("Check\nupdates", "BPUPDATE ", "update", "Check GitHub for a newer version and install it"), L("About", "BPABOUT ", "about", "Version and where settings are kept"));
                rc.Tabs.Add(tab);
                Log.Write("Ribbon tab added.");
                return true;
            }
            catch (Exception e) { Log.Write("Ribbon failed: " + e.Message); return false; }
        }

        static string[] L(string text, string cmd, string icon, string tip) { return new[] { text, cmd, icon, tip }; }
        static void AddPanel(RibbonTab tab, string title, params string[][] btns)
        {
            var src = new RibbonPanelSource { Title = title };
            foreach (var b in btns)
                src.Items.Add(new RibbonButton { Text = b[0], ShowText = true, ShowImage = true, Size = RibbonItemSize.Large, Orientation = System.Windows.Controls.Orientation.Vertical, LargeImage = Icon(32, b[2]), Image = Icon(16, b[2]), CommandHandler = new Cmd(b[1]), CommandParameter = b[1], ToolTip = b[3] });
            tab.Panels.Add(new RibbonPanel { Source = src });
        }

        static RibbonButton Item(string text, string cmd) { return new RibbonButton { Text = text, ShowText = true, ShowImage = false, CommandHandler = new Cmd(), CommandParameter = cmd }; }

        sealed class Cmd : System.Windows.Input.ICommand
        {
            readonly string _cmd;
            public Cmd(string cmd = null) { _cmd = cmd; }
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object p) { return true; }
            public void Execute(object p)
            {
                string cmd = _cmd;
                if (cmd == null) { var b = p as RibbonButton; cmd = b != null ? b.CommandParameter as string : null; }
                if (cmd == null) { Log.Write("Ribbon click with no command."); return; }
                var d = AcApp.DocumentManager.MdiActiveDocument; if (d != null) d.SendStringToExecute(cmd, true, false, false);
            }
        }

        static BitmapSource Icon(int size, string kind)
        {
            using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                float s = size / 32f; var white = new Pen(Color.FromArgb(235, 238, 242), 2f * s);
                Func<float, float> k = v => v * s;
                if (kind == "map")
                {
                    var pts = new[] { new PointF(4 * s, 8 * s), new PointF(12 * s, 5 * s), new PointF(20 * s, 8 * s), new PointF(28 * s, 5 * s), new PointF(28 * s, 24 * s), new PointF(20 * s, 27 * s), new PointF(12 * s, 24 * s), new PointF(4 * s, 27 * s) };
                    g.DrawPolygon(white, pts); g.DrawLine(white, 12 * s, 5 * s, 12 * s, 24 * s); g.DrawLine(white, 20 * s, 8 * s, 20 * s, 27 * s);
                }
                else if (kind == "import" || kind == "export")
                {
                    g.DrawLines(white, new[] { new PointF(k(5), k(20)), new PointF(k(5), k(27)), new PointF(k(27), k(27)), new PointF(k(27), k(20)) });
                    bool down = kind == "import"; float y0 = down ? 4 : 21, y1 = down ? 21 : 4;
                    g.DrawLine(white, k(16), k(y0), k(16), k(y1)); float d = down ? -5 : 5;
                    g.DrawLines(white, new[] { new PointF(k(10), k(y1 + d)), new PointF(k(16), k(y1)), new PointF(k(22), k(y1 + d)) });
                }
                else if (kind == "layers")
                {
                    for (int i = 0; i < 3; i++) { float y = 8 + i * 6; g.DrawPolygon(white, new[] { new PointF(k(16), k(y - 4)), new PointF(k(28), k(y + 1)), new PointF(k(16), k(y + 6)), new PointF(k(4), k(y + 1)) }); }
                }
                else if (kind == "codes")
                {
                    g.DrawPolygon(white, new[] { new PointF(k(4), k(5)), new PointF(k(16), k(5)), new PointF(k(28), k(17)), new PointF(k(17), k(28)), new PointF(k(4), k(15)) });
                    g.DrawEllipse(white, k(8), k(9), k(4), k(4));
                }
                else if (kind == "connect")
                {
                    var q = new[] { new PointF(k(6), k(24)), new PointF(k(13), k(10)), new PointF(k(21), k(20)), new PointF(k(27), k(6)) };
                    g.DrawLines(white, q); foreach (var pt in q) g.FillEllipse(Brushes.White, pt.X - k(2.5f), pt.Y - k(2.5f), k(5), k(5));
                }
                else if (kind == "qa")
                {
                    g.DrawEllipse(white, k(4), k(4), k(24), k(24)); g.DrawLines(white, new[] { new PointF(k(10), k(17)), new PointF(k(14), k(21)), new PointF(k(22), k(11)) });
                }
                else if (kind == "shift")
                {
                    g.DrawLine(white, k(16), k(4), k(16), k(28)); g.DrawLine(white, k(4), k(16), k(28), k(16));
                    g.DrawLines(white, new[] { new PointF(k(12), k(8)), new PointF(k(16), k(4)), new PointF(k(20), k(8)) });
                    g.DrawLines(white, new[] { new PointF(k(24), k(12)), new PointF(k(28), k(16)), new PointF(k(24), k(20)) });
                }
                else if (kind == "area")
                {
                    var poly = new[] { new PointF(k(5), k(22)), new PointF(k(9), k(7)), new PointF(k(25), k(5)), new PointF(k(27), k(24)) };
                    using (var br = new SolidBrush(Color.FromArgb(70, 235, 238, 242))) g.FillPolygon(br, poly); g.DrawPolygon(white, poly);
                }
                else if (kind == "update")
                {
                    g.DrawArc(white, k(5), k(5), k(22), k(22), 30, 270); g.DrawLines(white, new[] { new PointF(k(22), k(3)), new PointF(k(26), k(10)), new PointF(k(18), k(11)) });
                    g.DrawLine(white, k(16), k(11), k(16), k(20)); g.DrawLines(white, new[] { new PointF(k(12), k(17)), new PointF(k(16), k(21)), new PointF(k(20), k(17)) });
                }
                else if (kind == "join")
                {
                    g.DrawLines(white, new[] { new PointF(k(4), k(22)), new PointF(k(13), k(22)) }); g.DrawLines(white, new[] { new PointF(k(19), k(22)), new PointF(k(28), k(22)), new PointF(k(28), k(8)) });
                    using (var dp = new Pen(Color.FromArgb(0, 150, 255), 2f * s)) g.DrawLine(dp, k(13), k(22), k(19), k(22));
                }
                else if (kind == "trim")
                {
                    g.DrawRectangle(white, k(8), k(8), k(16), k(16));
                    using (var dp = new Pen(Color.FromArgb(0, 150, 255), 2f * s)) g.DrawLine(dp, k(3), k(16), k(29), k(16));
                }
                else if (kind == "cutfill")
                {
                    g.DrawLines(white, new[] { new PointF(k(3), k(14)), new PointF(k(11), k(8)), new PointF(k(21), k(20)), new PointF(k(29), k(14)) });
                    using (var dp = new Pen(Color.FromArgb(0, 150, 255), 2f * s)) g.DrawLine(dp, k(3), k(16), k(29), k(16));
                }
                else if (kind == "cutfill")
                {
                    g.DrawLines(white, new[] { new PointF(k(3), k(14)), new PointF(k(11), k(8)), new PointF(k(21), k(20)), new PointF(k(29), k(14)) });
                    using (var dp = new Pen(Color.FromArgb(0, 150, 255), 2f * s)) g.DrawLine(dp, k(3), k(16), k(29), k(16));
                }
                else if (kind == "chain")
                {
                    g.DrawLines(white, new[] { new PointF(k(3), k(24)), new PointF(k(10), k(10)), new PointF(k(19), k(20)), new PointF(k(29), k(7)) });
                    using (var dp = new Pen(Color.FromArgb(0, 150, 255), 2f * s)) { g.DrawEllipse(dp, k(1), k(22), k(4), k(4)); g.DrawEllipse(dp, k(27), k(5), k(4), k(4)); }
                }
                else if (kind == "areapts")
                {
                    g.DrawPolygon(white, new[] { new PointF(k(5), k(24)), new PointF(k(8), k(8)), new PointF(k(25), k(6)), new PointF(k(28), k(22)) });
                    using (var dp = new Pen(Color.FromArgb(0, 150, 255), 2f * s)) g.DrawLine(dp, k(10), k(16), k(22), k(16));
                }
                else if (kind == "angle")
                {
                    g.DrawLine(white, k(4), k(26), k(28), k(26)); g.DrawLine(white, k(4), k(26), k(22), k(6));
                    using (var dp = new Pen(Color.FromArgb(0, 150, 255), 2f * s)) g.DrawArc(dp, k(-2), k(20), k(12), k(12), -60, 60);
                }
                else if (kind == "pickxy")
                {
                    g.DrawLine(white, k(16), k(4), k(16), k(28)); g.DrawLine(white, k(4), k(16), k(28), k(16));
                    using (var dp = new Pen(Color.FromArgb(0, 150, 255), 2f * s)) g.DrawEllipse(dp, k(10), k(10), k(12), k(12));
                }
                else if (kind == "cs")
                {
                    g.DrawEllipse(white, k(4), k(4), k(24), k(24)); g.DrawArc(white, k(10), k(4), k(12), k(24), 0, 360); g.DrawLine(white, k(4), k(16), k(28), k(16));
                    using (var dp = new Pen(Color.FromArgb(0, 150, 255), 2f * s)) g.DrawLine(dp, k(16), k(4), k(16), k(28));
                }
                else if (kind == "convpts")
                {
                    g.DrawEllipse(white, k(4), k(10), k(8), k(8)); g.DrawEllipse(white, k(20), k(10), k(8), k(8));
                    using (var dp = new Pen(Color.FromArgb(0, 150, 255), 2f * s)) { g.DrawLine(dp, k(13), k(12), k(19), k(12)); g.DrawLine(dp, k(19), k(16), k(13), k(16)); }
                }
                else if (kind == "plot")
                {
                    g.DrawRectangle(white, k(8), k(4), k(16), k(10)); g.DrawRectangle(white, k(4), k(14), k(24), k(10)); g.DrawRectangle(white, k(9), k(20), k(14), k(9));
                    using (var dp = new Pen(Color.FromArgb(0, 150, 255), 2f * s)) g.DrawLine(dp, k(11), k(24), k(21), k(24));
                }
                else if (kind == "sinks")
                {
                    g.DrawLines(white, new[] { new PointF(k(3), k(12)), new PointF(k(10), k(12)), new PointF(k(16), k(24)), new PointF(k(22), k(12)), new PointF(k(29), k(12)) });
                    using (var dp = new Pen(Color.FromArgb(0, 150, 255), 2f * s)) g.DrawLine(dp, k(11), k(18), k(21), k(18));
                }
                else if (kind == "about")
                {
                    g.DrawEllipse(white, k(4), k(4), k(24), k(24)); g.DrawLine(white, k(16), k(14), k(16), k(23)); g.FillEllipse(Brushes.White, k(14.5f), k(8.5f), k(3), k(3));
                }
                else if (kind == "lengths")
                {
                    g.DrawLine(white, k(4), k(26), k(28), k(26)); for (int i = 0; i <= 6; i++) g.DrawLine(white, k(4 + i * 4), k(26), k(4 + i * 4), k(i % 2 == 0 ? 18 : 22));
                    g.DrawLines(white, new[] { new PointF(k(5), k(14)), new PointF(k(12), k(6)), new PointF(k(19), k(12)), new PointF(k(27), k(5)) });
                }
                else if (kind == "label")
                {
                    g.DrawLine(white, k(4), k(22), k(28), k(10)); g.DrawRectangle(white, k(12), k(3), k(16), k(8)); g.DrawLine(white, k(14), k(7), k(26), k(7));
                }
                else if (kind == "leader")
                {
                    g.DrawLine(white, k(4), k(26), k(16), k(14)); g.FillEllipse(Brushes.White, k(2), k(24), k(5), k(5)); g.DrawLine(white, k(16), k(14), k(26), k(14)); g.DrawLine(white, k(18), k(8), k(28), k(8)); g.DrawLine(white, k(18), k(4), k(24), k(4));
                }
                else if (kind == "verts")
                {
                    var q = new[] { new PointF(k(5), k(24)), new PointF(k(10), k(8)), new PointF(k(22), k(14)), new PointF(k(27), k(5)) };
                    g.DrawLines(white, q); foreach (var pt in q) g.DrawRectangle(white, pt.X - k(2.5f), pt.Y - k(2.5f), k(5), k(5));
                }
                else if (kind == "coord")
                {
                    g.DrawLine(white, k(6), k(6), k(6), k(26)); g.DrawLine(white, k(6), k(26), k(26), k(26)); g.DrawEllipse(white, k(14), k(12), k(8), k(8)); g.DrawLine(white, k(18), k(16), k(18), k(26)); g.DrawLine(white, k(6), k(16), k(18), k(16));
                }
                else if (kind == "merge")
                {
                    g.DrawRectangle(white, k(4), k(8), k(10), k(16)); g.DrawRectangle(white, k(18), k(8), k(10), k(16)); g.DrawLine(white, k(14), k(16), k(18), k(16));
                }
                else if (kind == "split")
                {
                    g.DrawRectangle(white, k(4), k(6), k(24), k(20)); g.DrawLine(white, k(16), k(3), k(16), k(29));
                    for (int i = 0; i < 4; i++) { g.DrawLine(white, k(6), k(10 + i * 5), k(10), k(6 + i * 5)); g.DrawLine(white, k(20), k(10 + i * 5), k(24), k(6 + i * 5)); }
                }
                else if (kind == "renum")
                {
                    g.DrawEllipse(white, k(3), k(8), k(10), k(10)); g.DrawEllipse(white, k(19), k(8), k(10), k(10)); g.DrawArc(white, k(8), k(14), k(16), k(12), 20, 140); g.DrawLines(white, new[] { new PointF(k(21), k(22)), new PointF(k(24), k(26)), new PointF(k(25), k(21)) });
                }
                else if (kind == "dist")
                {
                    g.DrawLine(white, k(4), k(24), k(28), k(8)); g.DrawLine(white, k(4), k(19), k(4), k(28)); g.DrawLine(white, k(28), k(3), k(28), k(13));
                }
                else
                {
                    g.DrawEllipse(white, 10 * s, 10 * s, 12 * s, 12 * s);
                    for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(white, (float)(16 * s + Math.Cos(a) * 8 * s), (float)(16 * s + Math.Sin(a) * 8 * s), (float)(16 * s + Math.Cos(a) * 12 * s), (float)(16 * s + Math.Sin(a) * 12 * s)); }
                }
                var ms = new MemoryStream(); bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); ms.Position = 0;
                var bi = new BitmapImage(); bi.BeginInit(); bi.StreamSource = ms; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze(); return bi;
            }
        }
    }
}
