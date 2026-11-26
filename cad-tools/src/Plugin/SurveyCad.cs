using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;
using BPlus.LiveMap.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BPlus.LiveMap
{
    sealed class CadPoint { public ObjectId Id; public SurveyPoint P; public bool IsCogo; }

    static class SurveyCad
    {
        public const string XApp = "BPLUS_PT";
        static readonly short[] Palette = { 1, 3, 2, 30, 5, 6, 4, 40, 150, 210, 94, 8 };

        public static string CodesFile { get { return Path.Combine(Paths.Dir, "codes.txt"); } }
        public static CodeRules LoadRules()
        {
            try { if (File.Exists(CodesFile)) return CodeRules.Parse(File.ReadAllText(CodesFile, Encoding.UTF8)); } catch (Exception e) { Log.Write("codes.txt: " + e.Message); }
            return CodeRules.Default();
        }
        public static string RulesText() { try { if (File.Exists(CodesFile)) return File.ReadAllText(CodesFile, Encoding.UTF8); } catch { } return CodeRules.DefaultText; }
        public static void SaveRulesText(string t) { File.WriteAllText(CodesFile, t.Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false)); }

        public static string SafeName(string s) { var sb = new StringBuilder(); foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'); return sb.Length == 0 ? "X" : sb.ToString(); }
        public static short ColorFor(CodeRules r, string cat)
        {
            int i = r.Cats.FindIndex(c => c.Code == cat);
            if (i < 0) return cat == CodeRules.Uncoded ? (short)8 : (short)7;
            return Palette[i % Palette.Length];
        }

        static void EnsureApp(Transaction tr, Database db)
        {
            var t = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!t.Has(XApp)) { t.UpgradeOpen(); var r = new RegAppTableRecord { Name = XApp }; t.Add(r); tr.AddNewlyCreatedDBObject(r, true); }
        }
        static ObjectId EnsureLayer(Transaction tr, Database db, string name, short color)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];
            lt.UpgradeOpen();
            var l = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, color) };
            var id = lt.Add(l); tr.AddNewlyCreatedDBObject(l, true); return id;
        }

        static object Prop(object o, string n) { var p = o.GetType().GetProperty(n); return p == null ? null : p.GetValue(o, null); }
        static double Dbl(object o) { try { return o == null ? 0 : Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return 0; } }

        // Points in model space (optionally limited to a selection). Plain AutoCAD points use XData; Civil 3D COGO points are read by reflection.
        public static List<CadPoint> Collect(Document doc, ICollection<ObjectId> only = null)
        {
            var res = new List<CadPoint>(); var db = doc.Database; int seq = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (only != null && !only.Contains(id)) continue;
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity; if (ent == null) continue;
                    var dp = ent as DBPoint;
                    if (dp != null)
                    {
                        seq++; var sp = new SurveyPoint { No = "", E = dp.Position.X, N = dp.Position.Y, Z = dp.Position.Z, HasZ = true };
                        var rb = dp.GetXDataForApplication(XApp);
                        if (rb != null) { var v = rb.AsArray(); if (v.Length > 1) sp.No = Convert.ToString(v[1].Value); if (v.Length > 2) sp.Desc = Convert.ToString(v[2].Value); }
                        if (sp.No.Length == 0) sp.No = "";
                        res.Add(new CadPoint { Id = id, P = sp }); continue;
                    }
                    if (ent.GetType().Name == "CogoPoint")
                    {
                        try
                        {
                            var sp = new SurveyPoint { No = Convert.ToString(Prop(ent, "PointNumber")), E = Dbl(Prop(ent, "Easting")), N = Dbl(Prop(ent, "Northing")), Z = Dbl(Prop(ent, "Elevation")), Desc = Convert.ToString(Prop(ent, "RawDescription")) ?? "" };
                            res.Add(new CadPoint { Id = id, P = sp, IsCogo = true });
                        }
                        catch (Exception e) { Log.Write("COGO read: " + e.Message); }
                    }
                }
                tr.Commit();
            }
            var rules = LoadRules(); rules.Apply(res.Select(c => c.P));
            return res;
        }

        public static int Import(Document doc, List<SurveyPoint> pts, CodeRules rules, bool labels, double textH, bool setStyle)
        {
            var db = doc.Database; int n = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureApp(tr, db);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                rules.Apply(pts);
                foreach (var p in pts)
                {
                    short col = ColorFor(rules, p.Category); string cn = SafeName(p.Category);
                    var lay = EnsureLayer(tr, db, "BP-PT-" + cn, col);
                    var dp = new DBPoint(new Point3d(p.E, p.N, p.HasZ ? p.Z : 0)) { LayerId = lay };
                    ms.AppendEntity(dp); tr.AddNewlyCreatedDBObject(dp, true);
                    dp.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, XApp), new TypedValue((int)DxfCode.ExtendedDataAsciiString, p.No ?? ""), new TypedValue((int)DxfCode.ExtendedDataAsciiString, p.Desc ?? ""));
                    if (labels)
                    {
                        var tl = EnsureLayer(tr, db, "BP-TXT-" + cn, col);
                        string text = (p.No ?? "") + (string.IsNullOrEmpty(p.Desc) ? "" : " " + p.Desc);
                        var t = new DBText { TextString = text, Height = textH, Position = new Point3d(p.E + textH * 0.5, p.N + textH * 0.5, p.HasZ ? p.Z : 0), LayerId = tl };
                        ms.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
                    }
                    n++;
                }
                tr.Commit();
            }
            if (setStyle) { try { AcApp.SetSystemVariable("PDMODE", 35); AcApp.SetSystemVariable("PDSIZE", -2); } catch (Exception e) { Log.Write("PDMODE: " + e.Message); } }
            return n;
        }

        public static List<string> SurveyLayers(Document doc)
        {
            var res = new List<string>();
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt) { var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead); if (l.Name.StartsWith("BP-PT-") || l.Name.StartsWith("BP-TXT-") || l.Name.StartsWith("BP-LINE-")) res.Add(l.Name); }
                tr.Commit();
            }
            res.Sort(StringComparer.OrdinalIgnoreCase); return res;
        }
        public static Dictionary<string, bool> LayerOnState(Document doc)
        {
            var res = new Dictionary<string, bool>();
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt) { var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead); res[l.Name] = !l.IsOff; }
                tr.Commit();
            }
            return res;
        }
        public static void SetLayers(Document doc, Dictionary<string, bool> on)
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
                foreach (var kv in on)
                {
                    if (!lt.Has(kv.Key)) continue;
                    var l = (LayerTableRecord)tr.GetObject(lt[kv.Key], OpenMode.ForWrite);
                    if (l.Id == doc.Database.Clayer && !kv.Value) continue; // the current layer cannot be turned off
                    l.IsOff = !kv.Value;
                }
                tr.Commit();
            }
            doc.Editor.Regen();
        }

        public static int DrawLines(Document doc, List<ConnectLine> lines, CodeRules rules, bool byCode)
        {
            var db = doc.Database; int n = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                foreach (var l in lines)
                {
                    string cat = byCode ? rules.Classify(l.Code) : "ALL";
                    var lay = EnsureLayer(tr, db, "BP-LINE-" + SafeName(byCode ? l.Code : "ALL"), byCode ? ColorFor(rules, cat) : (short)7);
                    var pc = new Point3dCollection(); foreach (var p in l.Pts) pc.Add(new Point3d(p.E, p.N, p.HasZ ? p.Z : 0));
                    var pl = new Polyline3d(Poly3dType.SimplePoly, pc, l.Closed) { LayerId = lay };
                    ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true); n++;
                }
                tr.Commit();
            }
            return n;
        }

        public static void ZoomTo(Document doc, double e, double n)
        {
            var ed = doc.Editor; var v = ed.GetCurrentView();
            v.CenterPoint = new Point2d(e, n); ed.SetCurrentView(v);
        }

        public static string Azi(double a) { return Geo2D.Dms(a); }
    }
}
