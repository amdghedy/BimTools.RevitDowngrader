using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Downgrader.Core.Schema;
using Downgrader.Core.Storage;
using Downgrader.Core.Util;
using Downgrader.RevitShared;

namespace Downgrader.Import
{
    /// <summary>Rebuilds each extracted family as a real 2020 family (.rfa): parameters, formulas, types, reference planes,
    /// labelled dimensions, extrusions locked to their planes. Result files land in rebuilt_rfa_2020/ and are loaded by the importer.</summary>
    public class FamilyBuilder
    {
        readonly Application _app;
        readonly PackagePaths _p;
        readonly DowngradeOptions _o;
        readonly ConversionReport _r;
        readonly SharedParamHelper _shared;
        public readonly Dictionary<string, string> Built = new Dictionary<string, string>();   // family name -> rfa path
        public readonly HashSet<string> Frozen = new HashSet<string>();                        // families rebuilt with frozen per-type geometry
        Dictionary<string, FamilyParameter> _showParams = new Dictionary<string, FamilyParameter>();

        static readonly Dictionary<string, string[]> TemplateNames = new Dictionary<string, string[]>
        {
            { "OST_Doors", new[] { "Metric Door", "Door" } },
            { "OST_Windows", new[] { "Metric Window", "Window" } },
            { "OST_Furniture", new[] { "Metric Furniture", "Furniture" } },
            { "OST_Casework", new[] { "Metric Casework", "Casework" } },
            { "OST_Columns", new[] { "Metric Column", "Column" } },
            { "OST_StructuralColumns", new[] { "Metric Structural Column", "Structural Column" } },
            { "OST_PlumbingFixtures", new[] { "Metric Plumbing Fixture", "Plumbing Fixture" } },
            { "OST_SpecialityEquipment", new[] { "Metric Specialty Equipment", "Specialty Equipment" } },
            { "OST_GenericModel", new[] { "Metric Generic Model", "Generic Model" } }
        };

        public FamilyBuilder(Application app, PackagePaths p, DowngradeOptions o, ConversionReport r, SharedParamHelper shared)
        { _app = app; _p = p; _o = o; _r = r; _shared = shared; }

        public void BuildAll(IEnumerable<FamilyRec> fams)
        {
            var famList = fams.ToList();
            int famDone = 0;
            foreach (FamilyRec fr in famList)
            {
                JobProgress.Current.Report("Rebuilding families", ++famDone, famList.Count, true);
                if (fr.DefFile == null) { _r.Add("family", Tiers.DirectShape, fr.Category, fr.Name, fr.Key, "No family definition in package"); continue; }
                try { Build(fr, fr.Fidelity == "Partial" && _o.FreezePartialFamilies && _o.Mode == ExportMode.Hybrid); }
                catch (Exception ex) { _r.Add("family", Tiers.DirectShape, fr.Category, fr.Name, fr.Key, "Rebuild failed: " + ex.Message); }
            }
        }

        string FindTemplate(string category)
        {
            string root = !string.IsNullOrEmpty(_o.TemplateFolder) ? _o.TemplateFolder : _app.FamilyTemplatePath;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in Directory.GetFiles(root, "*.rft", SearchOption.AllDirectories))
            {
                string n = Path.GetFileNameWithoutExtension(f);
                if (!files.ContainsKey(n)) files[n] = f;
            }
            string[] names;
            if (!TemplateNames.TryGetValue(category ?? "", out names)) names = TemplateNames["OST_GenericModel"];
            foreach (string n in names.Concat(TemplateNames["OST_GenericModel"]))
            {
                string path;
                if (files.TryGetValue(n, out path)) return path;
            }
            return null;
        }

        /// <summary>Rebuilds one family. frozen = geometry per type from the stored meshes instead of parametric solid forms.</summary>
        public void Build(FamilyRec fr, bool frozen)
        {
            FamilyDefRec def = Json.Read<FamilyDefRec>(Path.Combine(_p.FamiliesDir, fr.DefFile));
            string tpl = FindTemplate(def.Category);
            if (tpl == null) { _r.Add("family", Tiers.DirectShape, fr.Category, fr.Name, fr.Key, "No family template (*.rft) found; set TemplateFolder in downgrade.options.json"); return; }
            Document fd = _app.NewFamilyDocument(tpl);
            if (fd == null) { _r.Add("family", Tiers.DirectShape, fr.Category, fr.Name, fr.Key, "Could not create family document"); return; }
            var issues = new List<string>();
            try
            {
                using (Transaction t = Tx.Start(fd, "Rebuild family"))
                {
                    _showParams = new Dictionary<string, FamilyParameter>();
                    Populate(fd, def, fr, frozen, issues);
                    t.Commit();
                }
                Directory.CreateDirectory(_p.RebuiltDir);
                string path = Path.Combine(_p.RebuiltDir, RevitHelpers.SafeFileName(def.Name) + ".rfa");
                fd.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
                Built[def.Name] = path;
                if (frozen) Frozen.Add(def.Name); else Frozen.Remove(def.Name);
                bool clean = def.Fidelity == "Full" && def.Dropped.Count == 0 && issues.Count == 0 && !frozen;
                string reason = clean ? null : (frozen ? "Geometry frozen per type (equals the source, not parametric); " : "") + string.Join("; ", def.Dropped.Concat(issues));
                _r.Add("family", clean ? Tiers.Native : Tiers.NativeWithLoss, fr.Category, def.Name, fr.Key, reason);
            }
            finally { fd.Close(false); }
        }

        void Populate(Document fd, FamilyDefRec def, FamilyRec fr, bool frozen, List<string> issues)
        {
            FamilyManager fm = fd.FamilyManager;
            Step(issues, "category", () => SetCategory(fd, def.Category));
            Step(issues, "family settings", () => ParamConv.Apply(fd.OwnerFamily, def.FamilyParams, null));   // Always vertical, Cut with voids, Shared, Room calculation point, OmniClass ...
            Step(issues, "subcategories", () => EnsureSubcategories(fd, def));
            Step(issues, "parameters", () => AddParams(fm, def, issues));
            Dictionary<string, ReferencePlane> planes = null;
            Step(issues, "reference planes", () => planes = BuildRefPlanes(fd, def, issues));
            if (planes == null) planes = new Dictionary<string, ReferencePlane>();
            Step(issues, "dimensions", () => BuildDims(fd, fm, def, planes, issues));
            Step(issues, "extrusions", () => BuildExtrusions(fd, fm, def, planes, issues, frozen));
            if (frozen) Step(issues, "frozen type geometry", () => BuildFrozen(fd, fm, fr, issues));
            Step(issues, "symbolic lines", () => BuildSymbolic(fd, def, issues));
            fd.Regenerate();
            Step(issues, "types", () => BuildTypes(fd, fm, def, issues));
            Step(issues, "formulas", () => ApplyFormulas(fd, fm, def, issues));
        }

        static void Step(List<string> issues, string name, Action a)
        {
            try { a(); } catch (Exception ex) { issues.Add(name + ": " + ex.Message); }
        }

        // ------------------------------------------------------------ steps

        void SetCategory(Document fd, string category)
        {
            BuiltInCategory bic;
            if (!RevitHelpers.TryEnum(category, out bic)) return;
            Category cat = fd.Settings.Categories.get_Item(bic);
            Category cur = fd.OwnerFamily.FamilyCategory;
            if (cat != null && (cur == null || cur.Id != cat.Id)) fd.OwnerFamily.FamilyCategory = cat;
        }

        void EnsureSubcategories(Document fd, FamilyDefRec def)
        {
            Category parent = fd.OwnerFamily.FamilyCategory;
            if (parent == null) return;
            foreach (string name in def.Extrusions.Select(e => e.Subcategory).Where(n => !string.IsNullOrEmpty(n)).Distinct())
                if (!parent.SubCategories.Contains(name)) fd.Settings.Categories.NewSubcategory(parent, name);
        }

        static FamilyParameter FindFp(FamilyManager fm, string name)
        {
            return fm.Parameters.Cast<FamilyParameter>().FirstOrDefault(p => p.Definition.Name == name);
        }

        void AddParams(FamilyManager fm, FamilyDefRec def, List<string> issues)
        {
            foreach (FamParamRec pr in def.Params)
            {
                if (pr.IsBuiltIn || FindFp(fm, pr.Name) != null) continue;
                ParameterType pt;
                if (!RevitHelpers.TryEnum(pr.Spec, out pt) || pt == ParameterType.FamilyType || pt == ParameterType.Invalid)
                { issues.Add("parameter '" + pr.Name + "' of type " + pr.Spec + " not recreated"); continue; }
                BuiltInParameterGroup g;
                if (!RevitHelpers.TryEnum(pr.Group, out g)) g = BuiltInParameterGroup.PG_DATA;
                try
                {
                    if (pr.IsShared && !string.IsNullOrEmpty(pr.Guid))
                        fm.AddParameter(_shared.GetOrCreate(pr.Name, new Guid(pr.Guid), pt), g, pr.IsInstance);
                    else
                        fm.AddParameter(pr.Name, g, pt, pr.IsInstance);
                }
                catch (Exception ex) { issues.Add("parameter '" + pr.Name + "': " + ex.Message); }
            }
        }

        static View FindView(Document fd, string name, double[] wantDir)
        {
            var views = new FilteredElementCollector(fd).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && (v is ViewPlan || v is ViewSection)).ToList();
            if (name != null)
            {
                View byName = views.FirstOrDefault(v => v.Name == name);
                if (byName != null) return byName;
            }
            if (wantDir != null)
            {
                XYZ d = RevitHelpers.ToXyz(wantDir).Normalize();
                View m = views.FirstOrDefault(v => Math.Abs(Math.Abs(v.ViewDirection.DotProduct(d)) - 1) < 1e-6);
                if (m != null) return m;
            }
            return views.FirstOrDefault();
        }

        Dictionary<string, ReferencePlane> BuildRefPlanes(Document fd, FamilyDefRec def, List<string> issues)
        {
            var map = new Dictionary<string, ReferencePlane>();
            foreach (ReferencePlane rp0 in new FilteredElementCollector(fd).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>())
                if (!string.IsNullOrEmpty(rp0.Name) && !map.ContainsKey(rp0.Name)) map[rp0.Name] = rp0;
            foreach (RefPlaneRec rr in def.RefPlanes)
            {
                ReferencePlane rp;
                if (!map.TryGetValue(rr.Name, out rp))   // template already has Left/Right/Front/Back/Center planes: reuse by name
                {
                    try
                    {
                        XYZ b = RevitHelpers.ToXyz(rr.BubbleEnd), f = RevitHelpers.ToXyz(rr.FreeEnd), n = RevitHelpers.ToXyz(rr.Normal);
                        XYZ cut = n.CrossProduct(f - b).Normalize();
                        rp = fd.FamilyCreate.NewReferencePlane(b, f, cut, FindView(fd, null, RevitHelpers.Pt(cut)));
                        map[rr.Name] = rp;
                    }
                    catch (Exception ex) { issues.Add("reference plane '" + rr.Name + "': " + ex.Message); continue; }
                }
                ParamConv.Apply(rp, rr.Params, null);   // Is Reference, Defines Origin, name
                try { if (rp.Name != rr.Name) rp.Name = rr.Name; } catch { }
            }
            return map;
        }

        void BuildDims(Document fd, FamilyManager fm, FamilyDefRec def, Dictionary<string, ReferencePlane> planes, List<string> issues)
        {
            // dimensions the template already carries (Width, Height of door templates, ...) must not be duplicated
            var existing = new HashSet<string>();
            foreach (Dimension d0 in new FilteredElementCollector(fd).OfClass(typeof(Dimension)).Cast<Dimension>())
            {
                try
                {
                    if (d0.FamilyLabel == null || d0.References.Size != 2) continue;
                    var names = new List<string>();
                    foreach (Reference r in d0.References) { var rp = fd.GetElement(r) as ReferencePlane; if (rp != null) names.Add(rp.Name); }
                    names.Sort();
                    existing.Add(d0.FamilyLabel.Definition.Name + "|" + string.Join("|", names));
                }
                catch { }
            }
            foreach (FamDimRec dr in def.Dims)
            {
                try
                {
                    var sorted = dr.Planes.OrderBy(x => x).ToList();
                    if (existing.Contains(dr.Label + "|" + string.Join("|", sorted))) continue;
                    FamilyParameter fp = FindFp(fm, dr.Label);
                    ReferencePlane p1, p2;
                    if (fp == null || !planes.TryGetValue(dr.Planes[0], out p1) || !planes.TryGetValue(dr.Planes[1], out p2)) { issues.Add("dimension '" + dr.Label + "' skipped (missing parameter or plane)"); continue; }
                    var refs = new ReferenceArray();
                    refs.Append(p1.GetReference());
                    refs.Append(p2.GetReference());
                    Line line = Line.CreateBound(RevitHelpers.ToXyz(dr.A), RevitHelpers.ToXyz(dr.B));
                    View v = FindView(fd, dr.ViewName, null);
                    Dimension dim = fd.FamilyCreate.NewDimension(v, line, refs);
                    dim.FamilyLabel = fp;
                }
                catch (Exception ex) { issues.Add("dimension '" + dr.Label + "': " + ex.Message); }
            }
        }

        ElementId FamilyMaterial(Document fd, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Element m = new FilteredElementCollector(fd).OfClass(typeof(Material)).FirstOrDefault(x => x.Name == name);
            return m != null ? m.Id : Material.Create(fd, name);
        }

        static ModelCurve MatchCurve(List<ModelCurve> mcs, CurveRec r)
        {
            XYZ a = RevitHelpers.ToXyz(r.A), b = RevitHelpers.ToXyz(r.B);
            foreach (ModelCurve mc in mcs)
            {
                var ln = mc.GeometryCurve as Line;
                if (ln == null) continue;
                XYZ s = ln.GetEndPoint(0), e = ln.GetEndPoint(1);
                if ((s.DistanceTo(a) < 1e-5 && e.DistanceTo(b) < 1e-5) || (s.DistanceTo(b) < 1e-5 && e.DistanceTo(a) < 1e-5)) return mc;
            }
            return null;
        }

        void BuildExtrusions(Document fd, FamilyManager fm, FamilyDefRec def, Dictionary<string, ReferencePlane> planes, List<string> issues, bool frozen)
        {
            Category parent = fd.OwnerFamily.FamilyCategory;
            Func<ParamRec, ElementId> resolve = r => r.RefKind == "Material" ? FamilyMaterial(fd, r.RefName) : null;
            foreach (ExtrusionRec er in def.Extrusions)
            {
                if (frozen && er.IsSolid) continue;   // solids come from the frozen geometry; voids stay real so hosted families still cut their host
                try
                {
                    SketchPlane sp;
                    ReferencePlane rp;
                    if (er.SketchPlaneName != null && planes.TryGetValue(er.SketchPlaneName, out rp)) sp = SketchPlane.Create(fd, rp.Id);
                    else sp = SketchPlane.Create(fd, Plane.CreateByNormalAndOrigin(RevitHelpers.ToXyz(er.PlaneNormal), RevitHelpers.ToXyz(er.PlaneOrigin)));

                    var caa = new CurveArrArray();
                    foreach (List<CurveRec> loop in er.Loops)
                    {
                        var ca = new CurveArray();
                        foreach (CurveRec cr in loop) foreach (Curve c in CurveConv.ToCurves(cr)) ca.Append(c);
                        caa.Append(ca);
                    }
                    Extrusion ex = fd.FamilyCreate.NewExtrusion(er.IsSolid, caa, sp, er.End);
                    Parameter ps = RevitHelpers.Bip(ex, "EXTRUSION_START_PARAM");
                    if (ps != null && !ps.IsReadOnly) ps.Set(er.Start);
                    if (!string.IsNullOrEmpty(er.Subcategory) && parent != null && parent.SubCategories.Contains(er.Subcategory))
                        ex.Subcategory = parent.SubCategories.get_Item(er.Subcategory);
                    ParamConv.Apply(ex, er.Params, resolve);
                    ParamConv.ApplyAssoc(ex, er.Params, fm);
                    try
                    {
                        // visibility / detail level of the form: Coarse, Medium, Fine, plan, front/back, left/right, top/bottom
                        FamilyElementVisibilityType vt;
                        if (!RevitHelpers.TryEnum(er.VisibilityType, out vt)) vt = FamilyElementVisibilityType.Model;
                        var vis = new FamilyElementVisibility(vt);
                        foreach (KeyValuePair<string, bool> kv in er.Visibility)
                        {
                            System.Reflection.PropertyInfo pi = vis.GetType().GetProperty(kv.Key);
                            if (pi != null && pi.CanWrite && pi.PropertyType == typeof(bool)) pi.SetValue(vis, kv.Value, null);
                        }
                        if (er.Visibility.Count > 0) ex.SetVisibility(vis);
                    }
                    catch { }
                    fd.Regenerate();

                    // lock profile lines back to the reference planes they sat on: this is what keeps the geometry parametric
                    var mcs = ex.Sketch.GetDependentElements(new ElementClassFilter(typeof(CurveElement)))
                        .Select(id => fd.GetElement(id)).OfType<ModelCurve>().ToList();
                    if (mcs.Count == 0)
                        mcs = new FilteredElementCollector(fd).OfClass(typeof(CurveElement)).OfType<ModelCurve>().ToList();
                    View av = FindView(fd, null, er.PlaneNormal);
                    for (int l = 0; l < er.Loops.Count; l++)
                        for (int c = 0; c < er.Loops[l].Count; c++)
                        {
                            string bp = er.BoundPlanes[l][c];
                            ReferencePlane brp;
                            if (bp == null || er.Loops[l][c].Kind != "Line" || !planes.TryGetValue(bp, out brp)) continue;
                            ModelCurve mc = MatchCurve(mcs, er.Loops[l][c]);
                            if (mc == null) continue;
                            try
                            {
                                Dimension al = fd.FamilyCreate.NewAlignment(av, brp.GetReference(), mc.GeometryCurve.Reference);
                                try { al.IsLocked = true; } catch { }
                            }
                            catch (Exception exA) { issues.Add("alignment to '" + bp + "' failed: " + exA.Message); }
                        }
                }
                catch (Exception ex) { issues.Add("extrusion: " + ex.Message); }
            }
        }

        void BuildSymbolic(Document fd, FamilyDefRec def, List<string> issues)
        {
            if (def.Symbolics.Count == 0) return;
            var styles = new Dictionary<string, GraphicsStyle>();
            foreach (GraphicsStyle g in new FilteredElementCollector(fd).OfClass(typeof(GraphicsStyle)).Cast<GraphicsStyle>().Where(x => x.GraphicsStyleType == GraphicsStyleType.Projection))
                if (!styles.ContainsKey(g.Name)) styles[g.Name] = g;
            int failed = 0;
            foreach (SymbolicRec sr in def.Symbolics)
            {
                try
                {
                    SketchPlane sp = SketchPlane.Create(fd, Plane.CreateByNormalAndOrigin(RevitHelpers.ToXyz(sr.PlaneNormal), RevitHelpers.ToXyz(sr.PlaneOrigin)));
                    foreach (Curve c in CurveConv.ToCurves(sr.Curve))
                    {
                        SymbolicCurve sc = fd.FamilyCreate.NewSymbolicCurve(c, sp);
                        GraphicsStyle gs;
                        if (sr.LineStyle != null && styles.TryGetValue(sr.LineStyle, out gs)) { try { sc.LineStyle = gs; } catch { } }
                    }
                }
                catch { failed++; }
            }
            if (failed > 0) issues.Add(failed + " symbolic lines not recreated");
        }

        /// <summary>Solid shapes of every type, built from the stored meshes (one free-form solid per closed shell).
        /// With more than one type, a Yes/No type parameter per type drives the visibility of that type's shapes.</summary>
        void BuildFrozen(Document fd, FamilyManager fm, FamilyRec fr, List<string> issues)
        {
            List<TypeBoxRec> withGeo = fr.TypeGeometry.Where(t => t.GeoOffset >= 0).ToList();
            if (withGeo.Count == 0) { issues.Add("no type geometry was stored (only types in use are measured), the family has no solid geometry"); return; }
            int failed = 0, made = 0, n = 0;
            using (var rd = new GeometryPackReader(_p.GeometryPack))
            {
                foreach (TypeBoxRec tb in withGeo)
                {
                    FamilyParameter show = null;
                    if (withGeo.Count > 1)
                    {
                        string pn = "DG_Show_" + (++n);
                        fm.AddParameter(pn, BuiltInParameterGroup.PG_VISIBILITY, ParameterType.YesNo, false);
                        show = FindFp(fm, pn);
                        _showParams[tb.Name] = show;
                    }
                    foreach (MeshRec mesh in rd.Read(tb.GeoOffset))
                        foreach (MeshRec comp in MeshOps.Split(mesh))
                        {
                            try
                            {
                                Solid solid = MakeSolid(comp);
                                FreeFormElement ff = FreeFormElement.Create(fd, solid);
                                ElementId mat = FamilyMaterial(fd, comp.Material);
                                Parameter mp = RevitHelpers.Bip(ff, "MATERIAL_ID_PARAM");
                                if (mat != null && mp != null && !mp.IsReadOnly) mp.Set(mat);
                                if (show != null)
                                {
                                    Parameter vis = RevitHelpers.Bip(ff, "IS_VISIBLE_PARAM");
                                    if (vis != null) fm.AssociateElementParameterToFamilyParameter(vis, show);
                                }
                                made++;
                            }
                            catch { failed++; }
                        }
                }
            }
            if (failed > 0) issues.Add(failed + " shells could not be turned into solids (" + made + " created)");
        }

        static Solid MakeSolid(MeshRec m)
        {
            var b = new TessellatedShapeBuilder();
            b.OpenConnectedFaceSet(true);
            for (int i = 0; i + 2 < m.Indices.Length; i += 3)
            {
                XYZ p0 = new XYZ(m.Vertices[m.Indices[i] * 3], m.Vertices[m.Indices[i] * 3 + 1], m.Vertices[m.Indices[i] * 3 + 2]);
                XYZ p1 = new XYZ(m.Vertices[m.Indices[i + 1] * 3], m.Vertices[m.Indices[i + 1] * 3 + 1], m.Vertices[m.Indices[i + 1] * 3 + 2]);
                XYZ p2 = new XYZ(m.Vertices[m.Indices[i + 2] * 3], m.Vertices[m.Indices[i + 2] * 3 + 1], m.Vertices[m.Indices[i + 2] * 3 + 2]);
                if ((p1 - p0).CrossProduct(p2 - p0).GetLength() < 1e-9) continue;
                b.AddFace(new TessellatedFace(new List<XYZ> { p0, p1, p2 }, ElementId.InvalidElementId));
            }
            b.CloseConnectedFaceSet();
            b.Target = TessellatedShapeBuilderTarget.Solid;
            b.Fallback = TessellatedShapeBuilderFallback.Abort;
            b.Build();
            foreach (GeometryObject go in b.GetBuildResult().GetGeometricalObjects())
            {
                var s = go as Solid;
                if (s != null) return s;
            }
            throw new InvalidOperationException("The mesh is not a closed solid");
        }

        void BuildTypes(Document fd, FamilyManager fm, FamilyDefRec def, List<string> issues)
        {
            Func<ParamRec, ElementId> resolve = r => r.RefKind == "Material" ? FamilyMaterial(fd, r.RefName) : null;
            var existing = fm.Types.Cast<FamilyType>().Select(t => t.Name).ToList();
            foreach (FamTypeRec tr in def.Types)
            {
                try
                {
                    FamilyType ft = fm.Types.Cast<FamilyType>().FirstOrDefault(t => t.Name == tr.Name) ?? fm.NewType(tr.Name);
                    fm.CurrentType = ft;
                    foreach (KeyValuePair<string, FamilyParameter> sp in _showParams)   // frozen geometry: show only this type's shapes
                    {
                        try { fm.Set(sp.Value, sp.Key == tr.Name ? 1 : 0); } catch { }
                    }
                    foreach (ParamRec v in tr.Values)
                    {
                        FamilyParameter fp = FindFp(fm, v.Name);
                        if (fp == null || fp.IsReporting || fp.IsDeterminedByFormula) continue;
                        try
                        {
                            switch (fp.StorageType)
                            {
                                case StorageType.Double: fm.Set(fp, v.D); break;
                                case StorageType.Integer: fm.Set(fp, v.I); break;
                                case StorageType.String: fm.Set(fp, v.S ?? ""); break;
                                case StorageType.ElementId:
                                    ElementId id = v.RefKind == "Material" ? resolve(v) : null;
                                    if (id != null) fm.Set(fp, id);
                                    break;
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex) { issues.Add("type '" + tr.Name + "': " + ex.Message); }
            }
            if (def.CurrentTypeName != null)
            {
                FamilyType cur = fm.Types.Cast<FamilyType>().FirstOrDefault(t => t.Name == def.CurrentTypeName);
                if (cur != null) fm.CurrentType = cur;
            }
            fd.Regenerate();
        }

        void ApplyFormulas(Document fd, FamilyManager fm, FamilyDefRec def, List<string> issues)
        {
            foreach (FamParamRec pr in def.Params.Where(p => !string.IsNullOrEmpty(p.Formula)))
            {
                try
                {
                    FamilyParameter fp = FindFp(fm, pr.Name);
                    if (fp != null) fm.SetFormula(fp, pr.Formula);
                }
                catch (Exception ex) { issues.Add("formula of '" + pr.Name + "': " + ex.Message); }
            }
            fd.Regenerate();
        }
    }
}
