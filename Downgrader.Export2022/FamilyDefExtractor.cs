using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Downgrader.Core.Schema;
using Downgrader.RevitShared;

namespace Downgrader.Export
{
    /// <summary>Reads a family document (opened with Document.EditFamily) into a FamilyDefRec: parameters, formulas, types,
    /// reference planes, labelled dimensions and extrusions with their locks. Anything it cannot represent is listed in Dropped,
    /// and solid geometry that is dropped marks the family Partial so the importer will not pretend it is a faithful rebuild.</summary>
    public static class FamilyDefExtractor
    {
        public static FamilyDefRec Extract(Document fd)
        {
            var def = new FamilyDefRec();
            FamilyManager fm = fd.FamilyManager;
            Family owner = fd.OwnerFamily;
            def.Name = owner.Name;
            def.Category = RevitHelpers.CategoryName(owner.FamilyCategory);
            if (fm.CurrentType != null) def.CurrentTypeName = fm.CurrentType.Name;
            def.FamilyParams = ParamConv.Read(owner);      // family settings (Always vertical, Cut with voids, Shared, Room calculation point, OmniClass, ...)

            // ---- parameters + formulas
            List<FamilyParameter> fps = fm.Parameters.Cast<FamilyParameter>().ToList();
            foreach (FamilyParameter fp in fps)
            {
                var pr = new FamParamRec
                {
                    Name = fp.Definition.Name,
                    Group = Compat.GroupName(fp.Definition),
                    Spec = Compat.SpecName(fp.Definition),
                    Storage = fp.StorageType.ToString(),
                    IsInstance = fp.IsInstance,
                    IsReporting = fp.IsReporting,
                    IsBuiltIn = Compat.IdValue(fp.Id) < 0,
                    IsShared = fp.IsShared,
                    Formula = fp.Formula
                };
                if (fp.IsShared) pr.Guid = fp.GUID.ToString();
                def.Params.Add(pr);
            }

            // ---- types x values (read per type, no need to switch the current type)
            foreach (FamilyType ft in fm.Types)
            {
                var tr = new FamTypeRec { Name = ft.Name };
                foreach (FamilyParameter fp in fps)
                {
                    try
                    {
                        if (!ft.HasValue(fp)) continue;
                        var v = new ParamRec { Name = fp.Definition.Name, Storage = fp.StorageType.ToString() };
                        switch (fp.StorageType)
                        {
                            case StorageType.Double: double? d = ft.AsDouble(fp); if (!d.HasValue) continue; v.D = d.Value; break;
                            case StorageType.Integer: int? i = ft.AsInteger(fp); if (!i.HasValue) continue; v.I = i.Value; break;
                            case StorageType.String: v.S = ft.AsString(fp); break;
                            case StorageType.ElementId:
                                ElementId id = ft.AsElementId(fp);
                                ParamConv.FillRef(v, fd, id);
                                if (v.RefKind == "Skip") continue;
                                break;
                            default: continue;
                        }
                        tr.Values.Add(v);
                    }
                    catch { }
                }
                def.Types.Add(tr);
            }

            // ---- reference planes
            var planes = new FilteredElementCollector(fd).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().ToList();
            foreach (ReferencePlane rp in planes)
            {
                def.RefPlanes.Add(new RefPlaneRec
                {
                    Name = rp.Name,
                    BubbleEnd = RevitHelpers.Pt(rp.BubbleEnd),
                    FreeEnd = RevitHelpers.Pt(rp.FreeEnd),
                    Normal = RevitHelpers.Pt(rp.Normal),
                    Params = ParamConv.Read(rp)
                });
            }

            // ---- labelled dimensions between two reference planes (what makes the family parametric)
            var dropped = new Dictionary<string, int>();
            foreach (Dimension d in new FilteredElementCollector(fd).OfClass(typeof(Dimension)).Cast<Dimension>())
            {
                try
                {
                    if (d.FamilyLabel == null) continue;
                    var names = new List<string>();
                    if (d.References.Size == 2)
                        foreach (Reference r in d.References)
                        {
                            var rp = fd.GetElement(r) as ReferencePlane;
                            if (rp == null) break;
                            names.Add(rp.Name);
                        }
                    Line ln = d.Curve as Line;
                    if (names.Count != 2 || ln == null) { Drop(dropped, "LabelledDimension(not plane-to-plane)"); continue; }
                    def.Dims.Add(new FamDimRec
                    {
                        Label = d.FamilyLabel.Definition.Name,
                        Planes = names,
                        ViewName = d.View != null ? d.View.Name : null,
                        A = RevitHelpers.Pt(ln.GetEndPoint(0)),
                        B = RevitHelpers.Pt(ln.GetEndPoint(1))
                    });
                }
                catch { }
            }

            // ---- extrusions (solid + void)
            bool partial = false;
            foreach (Extrusion ex in new FilteredElementCollector(fd).OfClass(typeof(Extrusion)).Cast<Extrusion>())
            {
                try
                {
                    SketchPlane sp = ex.Sketch.SketchPlane;
                    Plane pl = sp.GetPlane();
                    var er = new ExtrusionRec
                    {
                        IsSolid = ex.IsSolid,
                        SketchPlaneName = sp.Name,
                        PlaneOrigin = RevitHelpers.Pt(pl.Origin),
                        PlaneNormal = RevitHelpers.Pt(pl.Normal),
                        Start = RevitHelpers.GetDouble(ex, "EXTRUSION_START_PARAM", 0),
                        End = RevitHelpers.GetDouble(ex, "EXTRUSION_END_PARAM", 0),
                        Params = ParamConv.Read(ex)
                    };
                    if (ex.Subcategory != null) er.Subcategory = ex.Subcategory.Name;
                    try
                    {
                        // visibility / detail level of the form (Coarse, Medium, Fine, plan, front/back, left/right, top/bottom)
                        FamilyElementVisibility vis = ex.GetVisibility();
                        if (vis != null)
                        {
                            System.Reflection.PropertyInfo vt = vis.GetType().GetProperty("VisibilityType");
                            if (vt != null) er.VisibilityType = Convert.ToString(vt.GetValue(vis, null));
                            foreach (System.Reflection.PropertyInfo p in vis.GetType().GetProperties())
                                if (p.PropertyType == typeof(bool) && p.Name.StartsWith("IsShownIn")) er.Visibility[p.Name] = (bool)p.GetValue(vis, null);
                        }
                    }
                    catch { }
                    foreach (CurveArray ca in ex.Sketch.Profile)
                    {
                        var loop = new List<CurveRec>();
                        var bound = new List<string>();
                        foreach (Curve c in ca)
                        {
                            loop.Add(CurveConv.ToRec(c));
                            bound.Add(BoundPlane(c, planes, pl.Normal));
                        }
                        er.Loops.Add(loop);
                        er.BoundPlanes.Add(bound);
                    }
                    def.Extrusions.Add(er);
                }
                catch (Exception) { partial = true; Drop(dropped, "Extrusion(unreadable)"); }
            }

            // ---- everything else: record it, and decide whether it costs solid geometry
            foreach (Element el in new FilteredElementCollector(fd).WhereElementIsNotElementType())
            {
                if (el is Extrusion) continue;
                if (el is FamilyInstance)
                {
                    var fi = (FamilyInstance)el;
                    var lp = fi.Location as LocationPoint;
                    def.Nested.Add(new NestedRec { FamilyName = fi.Symbol.FamilyName, TypeName = fi.Symbol.Name, Origin = lp != null ? RevitHelpers.Pt(lp.Point) : null });
                    partial = true; Drop(dropped, "NestedFamily:" + fi.Symbol.FamilyName);
                }
                else if (el is GenericForm) { partial = true; Drop(dropped, el.GetType().Name); }          // Blend / Revolution / Sweep / SweptBlend
                else if (el is ImportInstance) { partial = true; Drop(dropped, "ImportInstance"); }
                else if (el is SymbolicCurve)
                {
                    try
                    {
                        var sc = (SymbolicCurve)el;
                        Plane pl = sc.SketchPlane.GetPlane();
                        def.Symbolics.Add(new SymbolicRec
                        {
                            Curve = CurveConv.ToRec(sc.GeometryCurve),
                            PlaneOrigin = RevitHelpers.Pt(pl.Origin),
                            PlaneNormal = RevitHelpers.Pt(pl.Normal),
                            LineStyle = sc.LineStyle != null ? sc.LineStyle.Name : null
                        });
                    }
                    catch { Drop(dropped, "SymbolicLines"); }
                }
                else if (el is FilledRegion) Drop(dropped, "MaskingOrFilledRegions");
                else if (el is TextNote) Drop(dropped, "TextNotes");
            }

            def.Dropped = dropped.Select(kv => kv.Key + " x" + kv.Value).ToList();
            def.Fidelity = partial ? "Partial" : "Full";
            return def;
        }

        static void Drop(Dictionary<string, int> d, string key)
        {
            int n; d.TryGetValue(key, out n); d[key] = n + 1;
        }

        /// <summary>Name of the reference plane a profile line lies on (so the importer can lock it back), or null.</summary>
        static string BoundPlane(Curve c, List<ReferencePlane> planes, XYZ sketchNormal)
        {
            if (!(c is Line)) return null;
            XYZ a = c.GetEndPoint(0), b = c.GetEndPoint(1);
            foreach (ReferencePlane rp in planes)
            {
                Plane p = rp.GetPlane();
                if (Math.Abs(Math.Abs(p.Normal.DotProduct(sketchNormal)) - 1) < 1e-6) continue;   // parallel to the sketch plane: cannot constrain a line in it
                if (Math.Abs(p.Normal.DotProduct(a - p.Origin)) < 1e-6 && Math.Abs(p.Normal.DotProduct(b - p.Origin)) < 1e-6)
                    return rp.Name;
            }
            return null;
        }
    }
}
