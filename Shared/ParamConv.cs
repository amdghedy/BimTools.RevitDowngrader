using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Downgrader.Core.Schema;

namespace Downgrader.RevitShared
{
    /// <summary>Generic parameter copy. Parameters are matched by BuiltInParameter name first (parsed at runtime), then by name.</summary>
    public static class ParamConv
    {
        // identity / bookkeeping parameters that must never be copied across
        static readonly HashSet<string> Skip = new HashSet<string>
        {
            "ELEM_PARTITION_PARAM", "DESIGN_OPTION_PARAM", "DESIGN_OPTION_ID", "ELEM_FAMILY_PARAM", "ELEM_TYPE_PARAM",
            "SYMBOL_ID_PARAM", "SYMBOL_NAME_PARAM", "ELEM_FAMILY_AND_TYPE_PARAM", "FAMILY_LEVEL_PARAM", "HOST_ID_PARAM",
            "VIEW_NAME", "VIEW_TEMPLATE", "SHEET_NUMBER", "SHEET_NAME"
        };

        public static List<ParamRec> Read(Element e)
        {
            var list = new List<ParamRec>();
            Document doc = e.Document;
            FamilyManager fm = doc.IsFamilyDocument ? doc.FamilyManager : null;
            foreach (Parameter p in e.Parameters)
            {
                try
                {
                    if (p.IsReadOnly || !p.HasValue || p.Definition == null) continue;
                    var rec = new ParamRec { Name = p.Definition.Name };
                    var idef = p.Definition as InternalDefinition;
                    if (idef != null && idef.BuiltInParameter != BuiltInParameter.INVALID)
                    {
                        rec.BuiltIn = idef.BuiltInParameter.ToString();
                        if (Skip.Contains(rec.BuiltIn)) continue;
                    }
                    rec.Spec = Compat.SpecName(p.Definition);
                    switch (p.StorageType)
                    {
                        case StorageType.Double: rec.Storage = "Double"; rec.D = p.AsDouble(); break;
                        case StorageType.Integer: rec.Storage = "Integer"; rec.I = p.AsInteger(); break;
                        case StorageType.String: rec.Storage = "String"; rec.S = p.AsString(); break;
                        case StorageType.ElementId: rec.Storage = "ElementId"; FillRef(rec, doc, p.AsElementId()); break;
                        default: continue;
                    }
                    if (rec.Storage == "ElementId" && rec.RefKind == "Skip") continue;
                    if (fm != null)
                    {
                        FamilyParameter fp = fm.GetAssociatedFamilyParameter(p);
                        if (fp != null) rec.Assoc = fp.Definition.Name;
                    }
                    list.Add(rec);
                }
                catch { }
            }
            return list;
        }

        public static void FillRef(ParamRec rec, Document doc, ElementId id)
        {
            long iv = Compat.IdValue(id);
            rec.I = (int)iv;
            if (id == ElementId.InvalidElementId || iv < 0) { rec.RefKind = "Special"; return; }
            Element el = doc.GetElement(id);
            if (el == null) { rec.RefKind = "Skip"; return; }
            rec.RefName = el.Name;
            if (el is Level) { rec.RefKind = "Level"; rec.RefKey = el.UniqueId; }
            else if (el is Material) rec.RefKind = "Material";
            else if (el is Phase) rec.RefKind = "Phase";
            else if (el is PhaseFilter) rec.RefKind = "PhaseFilter";
            else rec.RefKind = "Skip";
        }

        public static Parameter Find(Element e, ParamRec r)
        {
            BuiltInParameter b;
            if (!string.IsNullOrEmpty(r.BuiltIn) && RevitHelpers.TryEnum(r.BuiltIn, out b))
            {
                Parameter p = null;
                try { p = e.get_Parameter(b); } catch { }
                if (p != null) return p;
            }
            return string.IsNullOrEmpty(r.Name) ? null : e.LookupParameter(r.Name);
        }

        /// <summary>Applies recs to e. ElementId parameters go first (constraints before offsets). Returns how many were set.</summary>
        public static int Apply(Element e, IEnumerable<ParamRec> recs, Func<ParamRec, ElementId> resolve)
        {
            if (recs == null) return 0;
            var all = recs.ToList();
            int ok = 0;
            foreach (ParamRec r in all.Where(x => x.Storage == "ElementId").Concat(all.Where(x => x.Storage != "ElementId")))
                if (TrySet(e, r, resolve)) ok++;
            return ok;
        }

        static bool TrySet(Element e, ParamRec r, Func<ParamRec, ElementId> resolve)
        {
            try
            {
                Parameter p = Find(e, r);
                if (p == null || p.IsReadOnly) return false;
                switch (r.Storage)
                {
                    case "Double": return p.StorageType == StorageType.Double && p.Set(r.D);
                    case "Integer": return p.StorageType == StorageType.Integer && p.Set(r.I);
                    case "String": return p.StorageType == StorageType.String && p.Set(r.S ?? "");
                    case "ElementId":
                        {
                            if (p.StorageType != StorageType.ElementId) return false;
                            ElementId id = r.RefKind == "Special" ? Compat.MakeId(r.I) : (resolve != null ? resolve(r) : null);
                            return id != null && p.Set(id);
                        }
                }
            }
            catch { }
            return false;
        }

        /// <summary>Family documents only: re-creates "associate family parameter" links (material, visibility, ...).</summary>
        public static void ApplyAssoc(Element e, IEnumerable<ParamRec> recs, FamilyManager fm)
        {
            if (recs == null) return;
            var fps = fm.Parameters.Cast<FamilyParameter>().ToList();
            foreach (ParamRec r in recs.Where(x => !string.IsNullOrEmpty(x.Assoc)))
            {
                try
                {
                    Parameter p = Find(e, r);
                    FamilyParameter fp = fps.FirstOrDefault(x => x.Definition.Name == r.Assoc);
                    if (p != null && fp != null) fm.AssociateElementParameterToFamilyParameter(p, fp);
                }
                catch { }
            }
        }
    }
}
