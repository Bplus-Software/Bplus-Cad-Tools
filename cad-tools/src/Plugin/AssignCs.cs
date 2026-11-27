using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using BPlus.LiveMap.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BPlus.LiveMap
{
    /// <summary>Optional step after the user picks a coordinate system: also set it as the drawing's own coordinate system (the geographic data AutoCAD and Civil 3D read).
    /// Only systems that exist in the Autodesk library of this install can be assigned (found by EPSG number). Objects are never moved: the drawing coordinates are declared to BE the grid coordinates.</summary>
    static class AssignCs
    {
        /// <summary>What the drawing says today: "none" or the library id and type.</summary>
        public static string Describe(Database db)
        {
            try
            {
                var gid = db.GeoDataObject; if (gid.IsNull) return "none (no coordinate system set in this drawing)";
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var g = (GeoLocationData)tr.GetObject(gid, OpenMode.ForRead); string cs = ""; try { cs = g.CoordinateSystem ?? ""; } catch { }
                    return (string.IsNullOrEmpty(cs) ? "geographic data without a system" : cs) + "  (" + g.TypeOfCoordinates + ")";
                }
            }
            catch (System.Exception e) { Log.Write("AssignCs.Describe: " + e.Message); return "could not be read"; }
        }

        /// <summary>The EPSG number of the drawing's own system, or 0 when none or not known.</summary>
        public static int DrawingEpsg(Database db)
        {
            try
            {
                var gid = db.GeoDataObject; if (gid.IsNull) return 0; string id;
                using (var tr = db.TransactionManager.StartOpenCloseTransaction()) { var g = (GeoLocationData)tr.GetObject(gid, OpenMode.ForRead); id = g.CoordinateSystem; }
                if (string.IsNullOrEmpty(id)) return 0; using (var cs = GeoCoordinateSystem.Create(id)) return cs.EPSGcode;
            }
            catch (System.Exception e) { Log.Write("AssignCs.DrawingEpsg: " + e.Message); return 0; }
        }

        /// <summary>The library system with this EPSG number, or null (also null when the library cannot be read).</summary>
        public static GeoCoordinateSystem FindByEpsg(int epsg, out string problem)
        {
            problem = null; GeoCoordinateSystem found = null;
            try
            {
                var all = GeoCoordinateSystem.CreateAll();
                foreach (var cs in all) { bool hit = false; try { hit = cs.EPSGcode == epsg; } catch { } if (hit && found == null) found = cs; else { try { cs.Dispose(); } catch { } } }
            }
            catch (System.Exception e) { problem = e.Message; Log.Write("AssignCs library: " + e.Message); }
            return found;
        }

        /// <summary>Sets the system on the drawing. Returns null when done, else the reason it was not.</summary>
        public static string Apply(Document doc, GeoCoordinateSystem lib)
        {
            var db = doc.Database;
            try
            {
                using (doc.LockDocument())
                {
                    double cx = 0, cy = 0; try { var a = db.Extmin; var b = db.Extmax; if (b.X > a.X && Math.Abs(b.X) < 1e9) { cx = (a.X + b.X) / 2; cy = (a.Y + b.Y) / 2; } } catch { }
                    if (db.GeoDataObject.IsNull)
                    {
                        var gd = new GeoLocationData(); gd.BlockTableRecordId = SymbolUtilityServices.GetBlockModelSpaceId(db); gd.PostToDb();
                    }
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var g = (GeoLocationData)tr.GetObject(db.GeoDataObject, OpenMode.ForWrite);
                        g.TypeOfCoordinates = TypeOfCoordinates.CoordinateTypeGrid;
                        g.CoordinateSystem = lib.ID;
                        g.DesignPoint = new Point3d(cx, cy, 0); g.ReferencePoint = new Point3d(cx, cy, 0);
                        try { g.HorizontalUnits = db.Insunits == UnitsValue.Undefined ? UnitsValue.Meters : db.Insunits; } catch { }
                        try { g.ScaleEstimationMethod = ScaleEstimationMethod.ScaleEstMethodUnity; } catch { }
                        tr.Commit();
                    }
                }
                return null;
            }
            catch (System.Exception e) { Log.Write("AssignCs.Apply: " + e); return e.Message; }
        }

        /// <summary>Shows the choice. 0 = cancel, 1 = use only in B+ tools, 2 = also set on the drawing (done), 3 = set and objects moved.</summary>
        public static int Offer(Document doc, CoordSys chosen, int previousPick)
        {
            string problem; var lib = FindByEpsg(chosen.Epsg, out problem);
            string before = Describe(doc.Database);
            int srcEpsg = DrawingEpsg(doc.Database); if (srcEpsg <= 0 || srcEpsg == chosen.Epsg) srcEpsg = previousPick;
            CoordSys src = srcEpsg > 0 && srcEpsg != chosen.Epsg ? CoordSys.FromEpsg(srcEpsg) : null;
            string libText = lib != null ? lib.ID + "  " + SafeDesc(lib) : (problem != null ? "the Autodesk library could not be read (" + problem + ")" : "not in the Autodesk library of this AutoCAD");
            string note = lib != null ? "Setting it does not move or change any object. The drawing's coordinates are declared to be this system's grid coordinates. Your drawing unit is " + doc.Database.Insunits + "." : "This system is not in your Autodesk library, so it cannot be set on the drawing. B+ tools still use it. To set it on the drawing, choose it in Civil 3D (Drawing Settings, Units and Zone) or Map's library tools.";
            int movable = 0; var others = new Dictionary<string, int>(); try { Commands.ScanModel(doc, out movable, out others); } catch (System.Exception e) { Log.Write("ScanModel: " + e.Message); }
            int otherN = 0; foreach (var kv in others) otherN += kv.Value;
            int res = 0;
            using (var f = Theme.Base("B+ CAD Tools - Set on the drawing?", 640, 520))
            {
                f.Controls.Add(Theme.Lbl("Drawing now", 16, 16, 140, true)); f.Controls.Add(Theme.Lbl(before, 160, 16, 460));
                f.Controls.Add(Theme.Lbl("You chose", 16, 46, 140, true)); f.Controls.Add(Theme.Lbl("EPSG:" + chosen.Epsg + "  " + chosen.Name, 160, 46, 460));
                f.Controls.Add(Theme.Lbl("Autodesk library", 16, 76, 140, true)); f.Controls.Add(Theme.Lbl(libText, 160, 76, 460));
                var n = Theme.Lbl(note, 16, 110, 604, true); n.Height = 54; f.Controls.Add(n);
                var mv = new CheckBox { Left = 16, Top = 176, Width = 604, Height = 26, Checked = false, Enabled = lib != null && src != null && movable > 0, ForeColor = Theme.Text, BackColor = Color.Transparent,
                    Text = src != null ? "Also move the objects from EPSG:" + src.Epsg + " " + src.Name + " to the new system" : "Also move the objects (needs a known current system: none found)" };
                f.Controls.Add(mv);
                var w = Theme.Lbl("", 16, 206, 604, true); w.Height = 150; w.ForeColor = Color.FromArgb(255, 190, 80); f.Controls.Add(w);
                Action wu = () =>
                {
                    if (!mv.Checked) { w.Text = "Off (default): nothing is moved."; w.ForeColor = Theme.Dim; return; }
                    w.ForeColor = Color.FromArgb(255, 190, 80);
                    string x = "Warning. Every object is moved: a wrong choice moves the whole drawing to the wrong place. Save a backup copy first. UNDO should reverse it (not tested).\r\n" + movable + " object(s) will be moved (points, lines, polylines, circles, text, block insertion points).";
                    if (otherN > 0) x += "\r\n" + otherN + " other object(s) will NOT move and will be left behind (" + string.Join(", ", others.Take(6).Select(kv => kv.Value + " " + kv.Key)) + "). That makes the drawing inconsistent.";
                    if (CoordConvert.IsApprox(src) || CoordConvert.IsApprox(chosen)) x += "\r\nAin el Abd / Aramco are only accurate to about 18 m.";
                    x += "\r\nBlocks keep their size and rotation; polyline arcs and circles keep their radius.";
                    w.Text = x;
                };
                mv.CheckedChanged += (s, e) => wu(); wu();
                var set = Theme.Btn("Set on the drawing", 16, 430, 180, true); set.Enabled = lib != null;
                var only = Theme.Btn("Only in B+ tools", 206, 430, 150); var cancel = Theme.Btn("Cancel", 520, 430, 100);
                set.Click += (s, e) =>
                {
                    if (mv.Checked && MessageBox.Show(f, "Move " + movable + " object(s) from EPSG:" + src.Epsg + " to EPSG:" + chosen.Epsg + "?\r\n\r\nHave you saved a backup copy?", "B+ CAD Tools", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    if (mv.Checked) { try { Commands.MoveAllModel(doc, src, chosen); } catch (System.Exception ex) { Log.Write("MoveAllModel: " + ex); MessageBox.Show(f, "Objects not moved: " + ex.Message, "B+ CAD Tools"); return; } }
                    var err = Apply(doc, lib); if (err == null) { res = mv.Checked ? 3 : 2; f.DialogResult = DialogResult.OK; } else MessageBox.Show(f, (mv.Checked ? "The objects were moved but the drawing setting was NOT changed: " : "Not changed: ") + err, "B+ CAD Tools");
                };
                only.Click += (s, e) => { res = 1; f.DialogResult = DialogResult.OK; }; cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel; f.CancelButton = cancel;
                f.Controls.AddRange(new Control[] { set, only, cancel }); AcApp.ShowModalDialog(f);
            }
            if (lib != null) { try { lib.Dispose(); } catch { } }
            return res;
        }
        static string SafeDesc(GeoCoordinateSystem c) { try { return c.Description ?? ""; } catch { return ""; } }
    }
}
