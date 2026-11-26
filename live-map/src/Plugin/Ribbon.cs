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
        const string TabId = "BPLUS_LIVEMAP_TAB";
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
                var tab = new RibbonTab { Title = "BPlus", Id = TabId };
                AddPanel(tab, "Points", L("Import points", "BPIMPORT ", "import", "Import a points file (txt, csv, xlsx) as points with codes and layers"), L("Export points", "BPEXPORT ", "export", "Export points to a clean text or csv file"), L("Show / hide", "BPISOLATE ", "layers", "Show or hide point categories"), L("Codes", "BPCODES ", "codes", "Edit the point code rules"));
                AddPanel(tab, "Draw", L("Connect points", "BPCONNECT ", "connect", "Draw lines through points by code and point number"));
                AddPanel(tab, "Survey QA", L("QA / QC", "BPQAQC ", "qa", "Check points: repeated numbers, duplicates, missing code or elevation, spikes"), L("Shift", "BPSHIFT ", "shift", "Move the selection by dE, dN, dZ"));
                AddPanel(tab, "Quantities", L("Area", "BPAREA ", "area", "Area of closed polylines in m2, feddan, qirat, sahm"), L("Distance", "BPDIST ", "dist", "Distance, azimuth and bearing between two points"));
                var src = new RibbonPanelSource { Title = "Live Map" };
                var panel = new RibbonPanel { Source = src };
                var main = new RibbonButton { Text = "Live map", ShowText = true, ShowImage = true, Size = RibbonItemSize.Large, Orientation = System.Windows.Controls.Orientation.Vertical, LargeImage = Icon(32, "map"), Image = Icon(16, "map"), CommandHandler = new Cmd(), CommandParameter = "BPGMAP ", ToolTip = "Show or hide the live Google map behind the drawing" };
                src.Items.Add(main);
                var split = new RibbonSplitButton { Text = "Options", ShowText = true, ShowImage = true, Size = RibbonItemSize.Large, Orientation = System.Windows.Controls.Orientation.Vertical, LargeImage = Icon(32, "gear"), Image = Icon(16, "gear"), IsSplit = false, ListButtonStyle = Autodesk.Private.Windows.RibbonListButtonStyle.MenuButton };
                split.Items.Add(Item("Map type...", "BPGMAPTYPE "));
                split.Items.Add(Item("Opacity...", "BPGMAPOPACITY "));
                split.Items.Add(Item("Refresh map", "BPGMAPREFRESH "));
                split.Items.Add(new RibbonSeparator());
                split.Items.Add(Item("Coordinate system...", "BPGMAPCS "));
                split.Items.Add(Item("Flip image rows", "BPGMAPFLIP "));
                split.Items.Add(Item("Test card", "BPGMAPTEST "));
                split.Items.Add(new RibbonSeparator());
                split.Items.Add(Item("Settings and Google key...", "BPGMAPSET "));
                split.Items.Add(Item("Check installation", "BPCHECK "));
                src.Items.Add(split);
                tab.Panels.Add(panel);
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
                src.Items.Add(new RibbonButton { Text = b[0], ShowText = true, ShowImage = true, Size = RibbonItemSize.Large, Orientation = System.Windows.Controls.Orientation.Vertical, LargeImage = Icon(32, b[2]), Image = Icon(16, b[2]), CommandHandler = new Cmd(), CommandParameter = b[1], ToolTip = b[3] });
            tab.Panels.Add(new RibbonPanel { Source = src });
        }

        static RibbonButton Item(string text, string cmd) { return new RibbonButton { Text = text, ShowText = true, ShowImage = false, CommandHandler = new Cmd(), CommandParameter = cmd }; }

        sealed class Cmd : System.Windows.Input.ICommand
        {
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object p) { return true; }
            public void Execute(object p)
            {
                var b = p as RibbonButton; string cmd = b != null ? b.CommandParameter as string : null; if (cmd == null) return;
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
