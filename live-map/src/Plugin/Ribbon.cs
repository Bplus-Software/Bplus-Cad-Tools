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
                if (kind == "map")
                {
                    var pts = new[] { new PointF(4 * s, 8 * s), new PointF(12 * s, 5 * s), new PointF(20 * s, 8 * s), new PointF(28 * s, 5 * s), new PointF(28 * s, 24 * s), new PointF(20 * s, 27 * s), new PointF(12 * s, 24 * s), new PointF(4 * s, 27 * s) };
                    g.DrawPolygon(white, pts); g.DrawLine(white, 12 * s, 5 * s, 12 * s, 24 * s); g.DrawLine(white, 20 * s, 8 * s, 20 * s, 27 * s);
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
