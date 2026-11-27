using System;
using System.Collections.Generic;
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
                AddStackPanel(tab, "Points", 3, L("Import points", "BPIMPORT ", "import", "Import a points file (txt, csv, xlsx) as points with codes and layers"), L("Show / hide", "BPISOLATE ", "layers", "Show or hide point categories"), L("Connect points", "BPCONNECT ", "connect", "Draw lines through points by code and point number"), L("Vertices to points", "BPVERTS ", "verts", "Make a numbered point at every vertex"), L("Convert points", "BPCONVPTS ", "convpts", "Convert AutoCAD points to Civil 3D COGO points and back. Needs Civil 3D"), L("QA / QC", "BPQAQC ", "qa", "Check points: repeated numbers, duplicates, missing code or elevation, spikes"), L("Shift", "BPSHIFT ", "shift", "Move the selection by dE, dN, dZ"), L("Quick profile", "BPPROFILE ", "profile", "Quick longitudinal profile along a line from points or the line's own levels (drawn as plain geometry, not a Civil 3D profile)"), L("Parking", "BPPARKING ", "parking", "Draw parking stalls (parallel, 90 or 45 degrees) along a line, with editable sizes"), L("Quick section", "BPSECTION ", "section", "Cross-sections along a line from points and/or a typical or existing road (lanes, sidewalks). Drawn as plain geometry, not Civil 3D sections"));
                AddStackPanel(tab, "Coordinates", 3, L("Coordinate system", "BPGMAPCS ", "cs", "Choose the coordinate system of the drawing: search by name, EPSG or country"), L("Convert coordinates", "BPCONVCS ", "convcs", "Convert selected objects or a coordinate file from one coordinate system to another"));
                AddStackPanel(tab, "Edit", 3, L("Join", "BPJOIN ", "join", "Join lines, polylines and arcs, closing small gaps"), L("Trim", "BPTRIM ", "trim", "Trim to a closed boundary: keep inside or outside"), L("Label", "BPLABEL ", "label", "Write length and area next to the selected polylines"), L("Auto leader", "BPLEADER ", "leader", "Select shapes: make a leader from each with its length or area"), L("Merge hatch", "BPHATCHMERGE ", "merge", "Combine several hatches into one"), L("Split hatch", "BPHATCHSPLIT ", "split", "Split a hatch into one hatch per separate area"));
                AddStackPanel(tab, "Measure", 3, L("Area", "BPAREA ", "area", "Area of closed polylines in m2, with perimeter in m"), L("Distance", "BPDIST ", "dist", "Distance, azimuth and bearing between two points"), L("Chain length", "BPCHAIN ", "chain", "Length along many picked points, with each segment"), L("Area by points", "BPAREAPTS ", "areapts", "Area and perimeter of picked corners"), L("Angle", "BPANGLE ", "angle", "Angle between two lines, from three points"), L("Pick coordinates", "BPPICK ", "pickxy", "E, N, Z of picked points, with latitude and longitude when a coordinate system is set"));
                AddStackPanel(tab, "Quantities", 3, L("Sinks", "BPSINKS ", "sinks", "Find where water collects on a surface: sinks with E, N, Z, depth, area and volume"), L("Cut and fill", "BPCUTFILL ", "cutfill", "Cut and fill volumes (m3) between the ground and another surface, a level or a plane"), L("Lengths", "BPLENGTHS ", "lengths", "List length and area of selected polylines, with table, CSV and labels"));
                AddStackPanel(tab, "Output", 3, L("Plot layouts", "BPPLOT ", "plot", "Plot the layouts you tick: each layout's own page setup or your settings, to PDF or a printer"), L("Export points", "BPEXPORT ", "export", "Export points to a clean text or csv file"), L("Coordinates", "BPCOORD ", "coord", "Write E and N at points and vertices"));
                AddStackPanel(tab, "Settings", 3, L("Settings", "BPSETTINGS ", "sliders", "Options, point defaults, updates"), L("Check updates", "BPUPDATE ", "update", "Check GitHub for a newer version and install it"), L("About", "BPABOUT ", "about", "Version and where settings are kept"), L("Clean drawing", "BPCLEAN ", "clean", "Remove empty layers, unused blocks and styles, zero-length objects (and duplicates if ticked) to make the file smaller. Saves a backup first"));
                rc.Tabs.Add(tab);
                Log.Write("Ribbon tab added.");
                return true;
            }
            catch (Exception e) { Log.Write("Ribbon failed: " + e.Message); return false; }
        }


        static RibbonButton Small(string[] b)
        {
            return new RibbonButton { Text = b[0].Replace("\n", " "), ShowText = true, ShowImage = true, Size = RibbonItemSize.Standard, Orientation = System.Windows.Controls.Orientation.Horizontal, Image = Icon(16, b[2]), LargeImage = Icon(32, b[2]), CommandHandler = new Cmd(b[1]), CommandParameter = b[1], ToolTip = b[3] };
        }
        /// <summary>Small buttons stacked in columns of up to 'rows' (plain buttons, no dropdowns), so the tab stays short.</summary>
        static void AddStack(RibbonPanelSource src, int rows, params string[][] btns)
        {
            var rp = new RibbonRowPanel();
            for (int r = 0; r < rows; r++)
            {
                bool any = false;
                for (int i = r; i < btns.Length; i += rows) { var sb = Small(btns[i]); int mx = 0; for (int j = i - r; j < Math.Min(btns.Length, i - r + rows); j++) mx = Math.Max(mx, btns[j][0].Replace("\n", " ").Length); sb.Width = Math.Max(84, mx * 6.9 + 44); rp.Items.Add(sb); any = true; }
                if (any && r < rows - 1 && r + 1 < btns.Length) rp.Items.Add(new RibbonRowBreak());
            }
            src.Items.Add(rp);
        }
        static void AddStackPanel(RibbonTab tab, string title, int rows, params string[][] btns)
        {
            var src = new RibbonPanelSource { Title = title }; AddStack(src, rows, btns); tab.Panels.Add(new RibbonPanel { Source = src });
        }
        /// <summary>Live map (large) with Map options and the coordinate system stacked beside it.</summary>
        static void AddMapPanel(RibbonTab tab, params string[][] settings)
        {
            var src = new RibbonPanelSource { Title = "Live Map" };
            src.Items.Add(new RibbonButton { Text = "Live\nmap", ShowText = true, ShowImage = true, Size = RibbonItemSize.Large, Orientation = System.Windows.Controls.Orientation.Vertical, LargeImage = Icon(32, "map"), Image = Icon(16, "map"), CommandHandler = new Cmd("BPGMAP "), CommandParameter = "BPGMAP ", ToolTip = "Show or hide the live Google map behind the drawing" });
            var all = new List<string[]> { L("Map options", "BPMAPOPT ", "gear", "Map type, opacity, refresh, coordinate system and Google key") }; all.AddRange(settings);
            AddStack(src, 3, all.ToArray());
            tab.Panels.Add(new RibbonPanel { Source = src });
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

        /// <summary>Every icon is redrawn big, trimmed to its drawing, and scaled to the same box and centred, so all icons have the same size and position.</summary>
        static BitmapSource Icon(int size, string kind)
        {
            using (var big = DrawIcon(128, kind))
            {
                int x0 = big.Width, y0 = big.Height, x1 = -1, y1 = -1;
                for (int y = 0; y < big.Height; y++) for (int x = 0; x < big.Width; x++) if (big.GetPixel(x, y).A > 24) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
                if (x1 < 0) { x0 = 0; y0 = 0; x1 = big.Width - 1; y1 = big.Height - 1; }
                int w = x1 - x0 + 1, h = y1 - y0 + 1; float box = Math.Max(w, h), target = size - (size >= 32 ? 4 : 2), sc = target / box;
                using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent); g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality; g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    float dw = w * sc, dh = h * sc; g.DrawImage(big, new RectangleF((size - dw) / 2f, (size - dh) / 2f, dw, dh), new RectangleF(x0, y0, w, h), GraphicsUnit.Pixel);
                    var ms = new MemoryStream(); bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); ms.Position = 0;
                    var bi = new BitmapImage(); bi.BeginInit(); bi.StreamSource = ms; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze(); return bi;
                }
            }
        }

        static Bitmap DrawIcon(int size, string kind)
        {
            using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                float s = size / 32f; var white = new Pen(Color.FromArgb(235, 238, 242), 2f * s);
                Func<float, float> k = v => v * s;
                if (kind == "clean")
                {
                    g.DrawRectangle(white, k(5), k(6), k(18), k(20)); g.DrawLine(white, k(9), k(12), k(19), k(12)); g.DrawLine(white, k(9), k(17), k(19), k(17)); g.DrawLine(white, k(9), k(22), k(15), k(22));
                    g.DrawLine(white, k(20), k(28), k(29), k(5)); g.DrawLine(white, k(24), k(28), k(29), k(16));
                }
                else if (kind == "convcs")
                {
                    g.DrawRectangle(white, k(4), k(6), k(11), k(11)); g.DrawRectangle(white, k(17), k(17), k(11), k(11));
                    g.DrawLine(white, k(16), k(9), k(26), k(9)); g.DrawLine(white, k(26), k(9), k(26), k(15)); g.DrawLine(white, k(26), k(15), k(23), k(12)); g.DrawLine(white, k(26), k(15), k(29), k(12));
                }
                else if (kind == "map")
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
                else if (kind == "section")
                {
                    g.DrawLine(white, k(3), k(27), k(29), k(27));
                    g.DrawLines(white, new[] { new PointF(k(3), k(20)), new PointF(k(11), k(20)), new PointF(k(13), k(15)), new PointF(k(19), k(15)), new PointF(k(21), k(20)), new PointF(k(29), k(20)) });
                    g.DrawLine(white, k(16), k(5), k(16), k(15));
                }
                else if (kind == "parking")
                {
                    g.DrawLine(white, k(3), k(26), k(29), k(26));
                    for (int i = 0; i < 4; i++) g.DrawLine(white, k(3 + i * 7), k(26), k(3 + i * 7), k(8));
                    g.DrawLine(white, k(3), k(8), k(24), k(8)); g.DrawLine(white, k(24), k(8), k(24), k(26));
                }
                else if (kind == "profile")
                {
                    g.DrawLine(white, k(5), k(4), k(5), k(27)); g.DrawLine(white, k(5), k(27), k(28), k(27));
                    g.DrawLines(white, new[] { new PointF(k(5), k(20)), new PointF(k(11), k(13)), new PointF(k(17), k(18)), new PointF(k(23), k(8)), new PointF(k(28), k(12)) });
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
                else if (kind == "sliders")
                {
                    g.DrawLine(white, k(5), k(9), k(27), k(9)); g.DrawLine(white, k(5), k(16), k(27), k(16)); g.DrawLine(white, k(5), k(23), k(27), k(23));
                    g.FillEllipse(Brushes.White, k(9), k(6), k(6), k(6)); g.FillEllipse(Brushes.White, k(18), k(13), k(6), k(6)); g.FillEllipse(Brushes.White, k(11), k(20), k(6), k(6));
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
                return (Bitmap)bmp.Clone();
            }
        }
    }
}
