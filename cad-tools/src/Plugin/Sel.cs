using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace BPlus.LiveMap
{
    /// <summary>Select first, then run the tool (or run the tool, then select).</summary>
    public class SelValue { public ObjectId[] Ids; public int Count { get { return Ids.Length; } } public ObjectId[] GetObjectIds() { return Ids; } }
    public class SelResult { public PromptStatus Status; public SelValue Value; }
    public static class Sel
    {
        /// <summary>Objects already selected in the drawing (does not change the selection).</summary>
        public static ObjectId[] Peek(Editor ed)
        {
            try { var r = ed.SelectImplied(); if (r.Status == PromptStatus.OK && r.Value != null) return r.Value.GetObjectIds(); } catch { }
            return new ObjectId[0];
        }
        static bool Match(ObjectId id, string[] names)
        {
            if (names == null) return true;
            string d = id.ObjectClass.DxfName ?? "";
            return names.Any(n => string.Equals(n, d, StringComparison.OrdinalIgnoreCase));
        }
        /// <summary>Use the objects already selected (those that fit the filter), otherwise ask. dxfNames: e.g. "LINE,LWPOLYLINE" or null for anything.</summary>
        public static SelResult Get(Editor ed, PromptSelectionOptions opt, string dxfNames, ObjectId[] pre = null)
        {
            var names = dxfNames == null ? null : dxfNames.Split(',');
            var have = (pre ?? Peek(ed)).Where(i => !i.IsNull && Match(i, names)).ToArray();
            if (have.Length > 0)
            {
                try { ed.SetImpliedSelection(new ObjectId[0]); } catch { }
                ed.WriteMessage(Ar.T("\n[B+] Using ") + have.Length + Ar.T(" object(s) already selected.\n"));
                return new SelResult { Status = PromptStatus.OK, Value = new SelValue { Ids = have } };
            }
            PromptSelectionResult r = names == null ? ed.GetSelection(opt)
                : ed.GetSelection(opt, new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, dxfNames) }));
            return new SelResult { Status = r.Status, Value = r.Status == PromptStatus.OK ? new SelValue { Ids = r.Value.GetObjectIds() } : new SelValue { Ids = new ObjectId[0] } };
        }
    }
}
