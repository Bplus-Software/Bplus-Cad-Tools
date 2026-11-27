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
                    return (string.IsNullOrEmpty(cs) ? "geographic data without a system" : Readable(cs)) + "  (" + g.TypeOfCoordinates + ")";
                }
            }
            catch (System.Exception e) { Log.Write("AssignCs.Describe: " + e.Message); return "could not be read"; }
        }

        /// <summary>A short readable name for the stored system: a library id as is; an XML definition parsed into name / code / EPSG.</summary>
        public static string Readable(string cs)
        {
            cs = (cs ?? "").Trim(); if (cs.Length == 0) return "";
            if (!cs.StartsWith("<")) return cs;
            try
            {
                try { using (var c = GeoCoordinateSystem.Create(cs)) { string d = ""; try { d = c.Description; } catch { } int e = 0; try { e = c.EPSGcode; } catch { } string id = ""; try { id = c.ID; } catch { } if (!string.IsNullOrEmpty(id) || e > 0) return id + (e > 0 ? " (EPSG:" + e + ")" : "") + (string.IsNullOrEmpty(d) ? "" : "  " + d); } } catch { }
                var doc = System.Xml.Linq.XDocument.Parse(cs.Substring(cs.IndexOf('<') == 0 && cs.StartsWith("<?xml") ? 0 : 0).Replace("encoding=\"UTF-16\"", "encoding=\"UTF-8\""));
                string F(params string[] names) { foreach (var n in names) { var el = doc.Descendants().FirstOrDefault(x => string.Equals(x.Name.LocalName, n, StringComparison.OrdinalIgnoreCase) && !x.HasElements && x.Value.Trim().Length > 0); if (el != null) return el.Value.Trim(); } return ""; }
                string name = F("Name", "Description", "Desc", "CsName", "CoordinateSystemName"), code = F("Code", "Id", "ID", "CsCode"), epsg = F("EPSG", "EpsgCode", "EPSGcode");
                var parts = new List<string>(); if (name != "") parts.Add(name); if (code != "") parts.Add("code " + code); if (epsg != "") parts.Add("EPSG:" + epsg);
                if (parts.Count > 0) return string.Join(", ", parts) + " (stored as XML)";
            }
            catch (System.Exception e) { Log.Write("AssignCs.Readable: " + e.Message); }
            return "a coordinate system stored as an XML definition (name could not be read)";
        }

        /// <summary>The EPSG number of the drawing's own system, or 0 when none or not known.</summary>
        public static int DrawingEpsg(Database db)
        {
            try
            {
                var gid = db.GeoDataObject; if (gid.IsNull) return 0; string id;
                using (var tr = db.TransactionManager.StartOpenCloseTransaction()) { var g = (GeoLocationData)tr.GetObject(gid, OpenMode.ForRead); id = g.CoordinateSystem; }
                if (string.IsNullOrEmpty(id)) return 0; if (id.TrimStart().StartsWith("<")) { var m = System.Text.RegularExpressions.Regex.Match(id, "EPSG[^0-9<]{0,12}([0-9]{4,5})", System.Text.RegularExpressions.RegexOptions.IgnoreCase); if (m.Success) return int.Parse(m.Groups[1].Value); } using (var cs = GeoCoordinateSystem.Create(id)) return cs.EPSGcode;
            }
            catch (System.Exception e) { Log.Write("AssignCs.DrawingEpsg: " + e.Message); return 0; }
        }


        public sealed class Cand { public string Id = "", Desc = "", Datum = "", Proj = "", Cat = ""; public int Epsg; public int Score; public bool NameMatch; public string Text { get { return Id + "  " + Desc + (Datum != "" ? "  [datum " + Datum + "]" : "") + (Epsg > 0 ? "  EPSG:" + Epsg : ""); } } }

        static string P(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }
        static Cand Info(GeoCoordinateSystem c)
        {
            var k = new Cand { Id = P(() => c.ID), Desc = P(() => c.Description), Datum = P(() => c.Datum.Id), Proj = P(() => c.ProjectionCode.ToString()), Cat = "" };
            try { k.Epsg = c.EPSGcode; } catch { }
            return k;
        }

        /// <summary>Library entries that look like the chosen system, best first. Matching is by text (name, description, code, datum, projection), not only by EPSG number.</summary>
        public static List<Cand> Candidates(CoordSys chosen, out string problem)
        {
            problem = null; var res = new List<Cand>();
            string name = (chosen.Name ?? "").ToUpperInvariant();
            var toks = new List<string>(); foreach (var t in System.Text.RegularExpressions.Regex.Split(name, "[^A-Z0-9]+")) if (t.Length >= 3 && t != "ZONE" && t != "UTM") toks.Add(t);
            var zm = System.Text.RegularExpressions.Regex.Match(name, @"ZONE\s*(\d{1,2})\s*([NS])?"); string zone = zm.Success ? zm.Groups[1].Value : "", hemi = zm.Success && zm.Groups[2].Success ? zm.Groups[2].Value : "";
            bool utm = name.Contains("UTM");
            string key = toks.FirstOrDefault(t => t.Any(char.IsDigit) && t.Any(char.IsLetter) && !t.StartsWith("EPSG"));   // e.g. GRF17
            try
            {
                foreach (var c in GeoCoordinateSystem.CreateAll())
                {
                    try
                    {
                        var k = Info(c); string hay = (k.Id + " " + k.Desc + " " + k.Datum + " " + k.Proj + " " + k.Cat).ToUpperInvariant(); int sc = 0;
                        if (k.Epsg == chosen.Epsg) sc += 100;
                        foreach (var t in toks) if (hay.Contains(t)) sc += (t == key ? 8 : 2);
                        if (hay.Contains("SAUDI") || hay.Contains("KSA")) sc += 2;
                        if (utm && zone != "")
                        {
                            bool z = System.Text.RegularExpressions.Regex.IsMatch(hay, @"(UTM|ZONE)[^0-9]{0,10}0?" + zone + @"(?![0-9])") || System.Text.RegularExpressions.Regex.IsMatch(hay, @"(?<![0-9])0?" + zone + hemi + @"(?![A-Z0-9])");
                            if (z) sc += 3;
                        }
                        if (hay.Contains("ITRF") || hay.Contains("GRF")) sc += 1;
                        k.Score = sc; k.NameMatch = key != null && hay.Contains(key) && (zone == "" || System.Text.RegularExpressions.Regex.IsMatch(hay, @"(?<![0-9])0?" + zone + @"(?![0-9])"));
                        if (sc >= 4) res.Add(k);
                    }
                    finally { try { c.Dispose(); } catch { } }
                }
            }
            catch (System.Exception e) { problem = e.Message; Log.Write("AssignCs.Candidates: " + e.Message); }
            return res.OrderByDescending(x => x.Score).ThenBy(x => x.Id).Take(12).ToList();
        }


        /// <summary>Writes the exact parameters of the chosen system to a text file the user can type into Autodesk's own library manager. Nothing in Autodesk's files is touched.</summary>
        public static string WriteDefinition(CoordSys cs)
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BPlusCadTools"); System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "coordinate-system-EPSG" + cs.Epsg + ".txt");
            int zone = 0; string ell = "WGS 84 (6378137, 1/f 298.257223563)", datum = "WGS 84", tw = "none (WGS 84 itself)", acc = "";
            if (cs.Epsg >= 9356 && cs.Epsg <= 9360) { zone = cs.Epsg - 9320; ell = "GRS 1980 (6378137, 1/f 298.257222101)"; datum = "Kingdom of Saudi Arabia Geodetic Reference Frame 2017 (KSA-GRF17)"; tw = "EPSG transformation 9383: rotations -8.393, 0.749, -10.276 milli-arcsec, no shift; accuracy 1 m"; acc = "Treat WGS 84 / UTM as identical to within about 1 m."; }
            else if (cs.Epsg >= 32601 && cs.Epsg <= 32660) zone = cs.Epsg - 32600;
            else if (cs.Epsg >= 32701 && cs.Epsg <= 32760) zone = cs.Epsg - 32700;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Coordinate system definition written by B+ CAD Tools"); sb.AppendLine("Name: " + cs.Name); sb.AppendLine("EPSG: " + cs.Epsg);
            if (zone > 0)
            {
                sb.AppendLine("Datum: " + datum); sb.AppendLine("Ellipsoid: " + ell); sb.AppendLine("Projection: Transverse Mercator (UTM zone " + zone + ")");
                sb.AppendLine("Central meridian: " + (zone * 6 - 183) + " E"); sb.AppendLine("Latitude of origin: 0"); sb.AppendLine("Scale factor: 0.9996"); sb.AppendLine("False easting: 500000 m"); sb.AppendLine("False northing: " + ((cs.Epsg >= 32701 && cs.Epsg <= 32760) ? "10000000" : "0") + " m");
                sb.AppendLine("Datum shift to WGS 84: " + tw); if (acc != "") sb.AppendLine("Note: " + acc);
            }
            else sb.AppendLine("Parameters: see https://epsg.io/" + cs.Epsg + " (this tool has no printed parameter list for this system).");
            sb.AppendLine("Source: https://epsg.io/" + cs.Epsg + " (EPSG registry)");
            sb.AppendLine("How to use: in Civil 3D / Map open the coordinate system library manager (command MAPCSLIBRARY), add a new system in your own category and type these values. Do not edit Autodesk's own entries.");
            System.IO.File.WriteAllText(path, sb.ToString()); return path;
        }

        /// <summary>Writes every library entry whose code, description, datum or projection contains the word (all entries when empty) to a text file. Returns the path.</summary>
        public static string DumpLibrary(string word)
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BPlusCadTools"); System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "coordinate-library-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            var sb = new System.Text.StringBuilder(); int total = 0, hit = 0;
            sb.AppendLine("B+ CAD Tools coordinate library dump. Filter: " + (string.IsNullOrWhiteSpace(word) ? "(none)" : word));
            sb.AppendLine("Columns: code | description | datum | projection | category | EPSG");
            try
            {
                foreach (var c in GeoCoordinateSystem.CreateAll())
                {
                    try
                    {
                        total++; var k = Info(c); string line = k.Id + " | " + k.Desc + " | " + k.Datum + " | " + k.Proj + " | " + k.Cat + " | " + (k.Epsg > 0 ? k.Epsg.ToString() : "");
                        if (string.IsNullOrWhiteSpace(word) || line.IndexOf(word.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) { sb.AppendLine(line); hit++; }
                    }
                    finally { try { c.Dispose(); } catch { } }
                }
            }
            catch (System.Exception e) { sb.AppendLine("Library could not be read: " + e.Message); }
            sb.Insert(0, "Entries in library: " + total + ", listed: " + hit + "\r\n");
            System.IO.File.WriteAllText(path, sb.ToString()); return path;
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
            int srcEpsg = DrawingEpsg(doc.Database); bool fromPick = false; if (srcEpsg <= 0 || srcEpsg == chosen.Epsg) { srcEpsg = previousPick; fromPick = srcEpsg > 0; }
            CoordSys src = srcEpsg > 0 && srcEpsg != chosen.Epsg ? CoordSys.FromEpsg(srcEpsg) : null;
            List<Cand> cands = new List<Cand>(); string cproblem = null; if (lib == null && problem == null) cands = Candidates(chosen, out cproblem);
            int dy = cands.Count > 0 ? 44 : 0; Cand picked = null;
            string libText = lib != null ? lib.ID + "  " + SafeDesc(lib) : (problem != null ? "the Autodesk library could not be read (" + problem + ")" : "not in the Autodesk library of this AutoCAD");
            string note = lib != null ? "Setting it does not move or change any object. The drawing's coordinates are declared to be this system's grid coordinates. Your drawing unit is " + doc.Database.Insunits + "." : "This system is not in your Autodesk library, so it cannot be set on the drawing. B+ tools still use it." + (cands.Count > 0 ? " Pick the closest library entry below to set it on the drawing, or use Civil 3D Drawing Settings. To send me the library list, run BPLIBDUMP." : " To set it on the drawing, choose it in Civil 3D (Drawing Settings, Units and Zone). To see what your library holds, run BPLIBDUMP and send the text file it writes.");
            int movable = 0; var others = new Dictionary<string, int>(); try { Commands.ScanModel(doc, out movable, out others); } catch (System.Exception e) { Log.Write("ScanModel: " + e.Message); }
            int otherN = 0; foreach (var kv in others) otherN += kv.Value;
            int res = 0;
            using (var f = Theme.Base("B+ CAD Tools - Set on the drawing?", 640, 520 + dy))
            {
                f.Controls.Add(Theme.Lbl("Drawing now", 16, 16, 140, true)); f.Controls.Add(Theme.Lbl(before, 160, 16, 460));
                f.Controls.Add(Theme.Lbl("You chose", 16, 46, 140, true)); f.Controls.Add(Theme.Lbl("EPSG:" + chosen.Epsg + "  " + chosen.Name, 160, 46, 460));
                f.Controls.Add(Theme.Lbl("Autodesk library", 16, 76, 140, true)); f.Controls.Add(Theme.Lbl(libText, 160, 76, 460));
                var n = Theme.Lbl(note, 16, 110, 604, true); n.Height = 54; f.Controls.Add(n);
                var mv = new CheckBox { Left = 16, Top = 176 + dy, Width = 604, Height = 26, Checked = false, Enabled = lib != null && src != null && movable > 0, ForeColor = Theme.Text, BackColor = Color.Transparent,
                    Text = src != null ? "Also move the objects from EPSG:" + src.Epsg + " " + src.Name + (fromPick ? " (your earlier B+ choice)" : "") + " to the new system" : "Also move the objects (needs a known current system: none found)" };
                f.Controls.Add(mv);
                var pickNote = Theme.Lbl("", 16, 200, 604, true); pickNote.ForeColor = Color.FromArgb(255, 190, 80); pickNote.Height = 20; ComboBox cb = null;
                if (cands.Count > 0)
                {
                    cb = new ComboBox { Left = 16, Top = 166, Width = 604, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.Panel, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat };
                    cb.Items.Add("(choose a library entry)"); foreach (var k in cands) cb.Items.Add((k.NameMatch ? "name match: " : "closest: ") + k.Text); cb.SelectedIndex = 0;
                    f.Controls.Add(cb); f.Controls.Add(pickNote);
                }
                var w = Theme.Lbl("", 16, 206 + dy, 604, true); w.Height = 150; w.ForeColor = Color.FromArgb(255, 190, 80); f.Controls.Add(w);
                Action wu = () =>
                {
                    if (!mv.Checked) { w.Text = "Off (default): nothing is moved."; w.ForeColor = Theme.Dim; return; }
                    w.ForeColor = Color.FromArgb(255, 190, 80);
                    string x = fromPick ? "The drawing itself does not say which system its coordinates are in. EPSG:" + src.Epsg + " is only what you picked earlier in B+ tools. Move ONLY if you know the objects really are in it.\r\n" : "";
                    x += "Warning. Every object is moved: a wrong choice moves the whole drawing to the wrong place. Save a backup copy first. UNDO should reverse it (not tested).\r\n" + movable + " object(s) will be moved (points, lines, polylines, circles, text, block insertion points).";
                    if (otherN > 0) x += "\r\n" + otherN + " other object(s) will NOT move and will be left behind (" + string.Join(", ", others.Take(6).Select(kv => kv.Value + " " + kv.Key)) + "). That makes the drawing inconsistent.";
                    if (CoordConvert.IsApprox(src) || CoordConvert.IsApprox(chosen)) x += "\r\nAin el Abd / Aramco are only accurate to about 18 m.";
                    x += "\r\nBlocks keep their size and rotation; polyline arcs and circles keep their radius.";
                    w.Text = x;
                };
                mv.CheckedChanged += (s, e) => wu(); wu();
                Button setRef = null;
                var set = Theme.Btn("Set on the drawing", 16, 430 + dy, 180, true); set.Enabled = lib != null; setRef = set;
                if (cb != null) cb.SelectedIndexChanged += (s, e) =>
                {
                    if (picked != null && lib != null) { try { lib.Dispose(); } catch { } } lib = null; picked = null;
                    if (cb.SelectedIndex > 0)
                    {
                        picked = cands[cb.SelectedIndex - 1];
                        try { lib = GeoCoordinateSystem.Create(picked.Id); } catch (System.Exception ex) { Log.Write("AssignCs pick: " + ex.Message); }
                        pickNote.Text = lib == null ? "That entry could not be opened." : picked.NameMatch ? "Matched by name; the library does not store EPSG:" + chosen.Epsg + " for it. Check the datum before you rely on it." : "Closest match, NOT identical (datum or parameters may differ). Setting it declares the drawing to be in THIS system.";
                    }
                    else pickNote.Text = "";
                    set.Enabled = lib != null; mv.Enabled = lib != null && src != null && movable > 0;
                };
                var only = Theme.Btn("Only in B+ tools", 206, 430 + dy, 150); var save = Theme.Btn("Save definition file", 366, 430 + dy, 144);
                save.Click += (s, e) => { try { string pth = WriteDefinition(chosen); MessageBox.Show(f, "Saved:\r\n" + pth + "\r\n\r\nIt lists the exact parameters (datum, ellipsoid, projection) to add the system in Autodesk's library manager.", "B+ CAD Tools"); try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + pth + "\""); } catch { } } catch (System.Exception ex) { MessageBox.Show(f, "Not saved: " + ex.Message, "B+ CAD Tools"); } }; var cancel = Theme.Btn("Cancel", 520, 430 + dy, 100);
                set.Click += (s, e) =>
                {
                    if (mv.Checked && MessageBox.Show(f, "Move " + movable + " object(s) from EPSG:" + src.Epsg + " to EPSG:" + chosen.Epsg + "?\r\n\r\nHave you saved a backup copy?", "B+ CAD Tools", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    if (mv.Checked) { try { Commands.MoveAllModel(doc, src, chosen); } catch (System.Exception ex) { Log.Write("MoveAllModel: " + ex); MessageBox.Show(f, "Objects not moved: " + ex.Message, "B+ CAD Tools"); return; } }
                    var err = Apply(doc, lib); if (err == null) { res = mv.Checked ? 3 : 2; f.DialogResult = DialogResult.OK; } else MessageBox.Show(f, (mv.Checked ? "The objects were moved but the drawing setting was NOT changed: " : "Not changed: ") + err, "B+ CAD Tools");
                };
                only.Click += (s, e) => { res = 1; f.DialogResult = DialogResult.OK; }; cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel; f.CancelButton = cancel;
                f.Controls.AddRange(new Control[] { set, only, save, cancel }); AcApp.ShowModalDialog(f);
            }
            if (lib != null) { try { lib.Dispose(); } catch { } }
            return res;
        }
        static string SafeDesc(GeoCoordinateSystem c) { try { return c.Description ?? ""; } catch { return ""; } }
    }
}
