using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace BPlus.LiveMap
{
    /// <summary>One decoded tile placed in drawing coordinates. Owns its unmanaged pixel buffer.</summary>
    sealed class PlacedTile : IDisposable
    {
        public ImageBGRA32 Image; public Point3d Origin; public Vector3d U, V; IntPtr _buf;
        public static PlacedTile From(byte[] encoded, Point3d origin, Vector3d u, Vector3d v, int opacityPercent, bool flipRows)
        {
            using (var ms = new MemoryStream(encoded))
            using (var bmp = new Bitmap(ms))
            {
                int w = bmp.Width, h = bmp.Height;
                var rect = new Rectangle(0, 0, w, h);
                var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                IntPtr buf = Marshal.AllocHGlobal(w * h * 4);
                try
                {
                    byte alpha = (byte)Math.Max(1, Math.Min(255, opacityPercent * 255 / 100));
                    var row = new byte[w * 4];
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, w * 4);
                        if (alpha != 255) for (int i = 3; i < row.Length; i += 4) row[i] = (byte)(row[i] * alpha / 255);
                        int dstRow = flipRows ? (h - 1 - y) : y;
                        Marshal.Copy(row, 0, IntPtr.Add(buf, dstRow * w * 4), w * 4);
                    }
                }
                finally { bmp.UnlockBits(data); }
                return new PlacedTile { Image = new ImageBGRA32((uint)w, (uint)h, buf), Origin = origin, U = u, V = v, _buf = buf };
            }
        }
        public void Dispose() { if (_buf != IntPtr.Zero) { Marshal.FreeHGlobal(_buf); _buf = IntPtr.Zero; } }
    }

    /// <summary>Temporary graphic (never stored in the drawing): tiles plus the Google attribution text.</summary>
    sealed class MapDrawable : Drawable
    {
        public MapDrawable() : base(IntPtr.Zero, false) { }
        public List<PlacedTile> Tiles = new List<PlacedTile>();
        public string Attribution = ""; public Point3d AttrPos; public double AttrHeight = 1; public Vector3d AttrDir = Vector3d.XAxis;

        protected override int SubSetAttributes(DrawableTraits traits) { return (int)DrawableAttributes.None; }

        protected override bool SubWorldDraw(WorldDraw wd)
        {
            var geo = wd.Geometry;
            foreach (var t in Tiles) geo.Image(t.Image, t.Origin, t.U, t.V, TransparencyMode.Transparency8Bit);
            if (!string.IsNullOrEmpty(Attribution))
                geo.Text(AttrPos, Vector3d.ZAxis, AttrDir, AttrHeight, 1.0, 0.0, Attribution);
            return true;
        }

        protected override void SubViewportDraw(ViewportDraw vd) { }
        protected override int SubViewportDrawLogicalFlags(ViewportDraw vd) { return 0; }
    }
}
