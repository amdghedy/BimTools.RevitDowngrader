using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Downgrader.Core.Schema;
using Downgrader.Core.Storage;
using Downgrader.Core.Util;
using Downgrader.RevitShared;

namespace Downgrader.Export
{
    /// <summary>Package A. Every model element is exported with its geometry (so there is always a DirectShape fallback);
    /// elements the importer can rebuild natively additionally carry placement, type and parameters.</summary>
    public class ModelExporter
    {
        readonly Document _doc;
        readonly PackagePaths _p;
        readonly DowngradeOptions _o;
        readonly ConversionReport _r;
        readonly ModelPackage _m = new ModelPackage();
        readonly Dictionary<string, TypeRec> _types = new Dictionary<string, TypeRec>();
        readonly Dictionary<string, FamilyRec> _fams = new Dictionary<string, FamilyRec>();
        readonly Dictionary<string, Family> _famObjs = new Dictionary<string, Family>();

        readonly HashSet<long> _scope;
        int _consecutiveFailures;   // null = whole project; otherwise only these element ids (selection / active view)

        public ModelExporter(Document doc, PackagePaths p, DowngradeOptions o, ConversionReport r, HashSet<long> scope = null)
        { _doc = doc; _p = p; _o = o; _r = r; _scope = scope; }

        public void Run(PackageManifest manifest)
        {
            ExportMaterials();
            ExportLevels();
            ExportGrids();
            using (var geo = new GeometryPackWriter(_p.GeometryPack))
            {
                ExportElements(geo);
                ExportFamilies(geo);
            }
            _m.Types = _types.Values.ToList();
            _m.Families = _fams.Values.ToList();
            Json.Write(_p.ModelJson, _m, false);
            manifest.Counts["model.elements"] = _m.Elements.Count;
            manifest.Counts["model.families"] = _m.Families.Count;
            manifest.Counts["model.types"] = _m.Types.Count;
        }

        void ExportMaterials()
        {
            foreach (Material mat in new FilteredElementCollector(_doc).OfClass(typeof(Material)).Cast<Material>())
            {
                var rec = new MaterialRec { Name = mat.Name, Transparency = mat.Transparency, Shininess = mat.Shininess, Smoothness = mat.Smoothness, MaterialClass = mat.MaterialClass };
                Color c = mat.Color;
                if (c != null && c.IsValid) rec.Rgb = new[] { (int)c.Red, (int)c.Green, (int)c.Blue };
                _m.Materials.Add(rec);
            }
        }

        void ExportLevels()
        {
            foreach (Level l in new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>())
                _m.Levels.Add(new LevelRec { Key = l.UniqueId, Name = l.Name, Elevation = l.Elevation });
        }

        void ExportGrids()
        {
            foreach (Grid g in new FilteredElementCollector(_doc).OfClass(typeof(Grid)).Cast<Grid>())
            {
                try { _m.Grids.Add(new GridRec { Key = g.UniqueId, Name = g.Name, Curve = CurveConv.ToRec(g.Curve) }); }
                catch (Exception ex) { _r.Add("model", Tiers.Skipped, "Grid", g.Name, g.UniqueId, "Multi-segment or unreadable grid: " + ex.Message); }
            }
        }

        void ExportElements(GeometryPackWriter geo)
        {
            int total = new FilteredElementCollector(_doc).WhereElementIsNotElementType().GetElementCount();
            int done = 0;
            foreach (Element e in new FilteredElementCollector(_doc).WhereElementIsNotElementType())
            {
                JobProgress.Current.Report("Exporting model elements", ++done, total);
                try
                {
                    if (e.Category == null || e.Category.CategoryType != CategoryType.Model) continue;
                    if (e is Level || e is Grid || e is Group || e is ElementType || e.ViewSpecific) continue;
                    if (e is RevitLinkInstance)
                    {
                        _r.Add("model", Tiers.Skipped, e.Category.Name, e.Name, e.UniqueId, "Linked model not exported: downgrade the linked file too and re-link it in 2020");
                        continue;
                    }
                    if (!Allowed(e, RevitHelpers.CategoryName(e))) continue;
                    var fiCheck = e as FamilyInstance;
                    if (fiCheck != null && fiCheck.SuperComponent != null) continue;   // sub-components are part of their parent's geometry

                    var rec = new ElementRec { Key = e.UniqueId, SourceId = Compat.IdValue(e.Id), Class = "Other", Category = RevitHelpers.CategoryName(e), CategoryName = e.Category.Name };
                    var et = _doc.GetElement(e.GetTypeId()) as ElementType;
                    if (et != null) { rec.TypeName = et.Name; rec.FamilyName = et.FamilyName; rec.TypeKey = AddType(et).Key; }
                    var lvl = _doc.GetElement(e.LevelId) as Level;
                    if (lvl != null) rec.LevelKey = lvl.UniqueId;

                    if (_o.Mode == ExportMode.GeometryOnly) rec.FlattenReason = "Geometry-only mode";
                    else if (e is Wall) ClassifyWall((Wall)e, rec);
                    else if (e is Floor) ClassifyFloor((Floor)e, rec);
                    else if (e is FootPrintRoof) ClassifyRoof((FootPrintRoof)e, rec);
                    else if (e is FamilyInstance) ClassifyFamilyInstance((FamilyInstance)e, rec);
                    else if (e is Autodesk.Revit.DB.Architecture.Room) ClassifyRoom((Autodesk.Revit.DB.Architecture.Room)e, rec);
                    else if (e is Autodesk.Revit.DB.Plumbing.Pipe) ClassifyMep(e, rec, "Pipe", "RBS_PIPING_SYSTEM_TYPE_PARAM");
                    else if (e is Autodesk.Revit.DB.Mechanical.Duct) ClassifyMep(e, rec, "Duct", "RBS_DUCT_SYSTEM_TYPE_PARAM");
                    else if (e is Autodesk.Revit.DB.Electrical.Conduit) ClassifyMep(e, rec, "Conduit", null);
                    else if (e is Autodesk.Revit.DB.Electrical.CableTray) ClassifyMep(e, rec, "CableTray", null);
                    else rec.FlattenReason = "Category not rebuilt natively yet (" + rec.CategoryName + ")";

                    List<MeshRec> meshes = GeometryMesher.Mesh(e, _doc, _o.MeshDetail);
                    if (meshes.Count > 0) { rec.GeoOffset = geo.Write(meshes); rec.GeoMeshes = meshes.Count; }
                    BoundingBoxXYZ bb = e.get_BoundingBox(null);
                    if (bb != null) { rec.BBoxMin = RevitHelpers.Pt(bb.Min); rec.BBoxMax = RevitHelpers.Pt(bb.Max); }

                    if (meshes.Count == 0 && rec.Class == "Other")
                    {
                        _r.Add("model", Tiers.Skipped, rec.CategoryName, e.Name, rec.Key, "No 3D geometry");
                        continue;
                    }
                    if (rec.FlattenReason != null)
                    {
                        // keep only Mark / Comments for elements that will become DirectShapes
                        rec.Params = ParamConv.Read(e).Where(x => x.BuiltIn == "ALL_MODEL_MARK" || x.BuiltIn == "ALL_MODEL_INSTANCE_COMMENTS").ToList();
                        _r.Add("export-model", Tiers.DirectShape, rec.CategoryName, rec.FamilyName + " : " + rec.TypeName, rec.Key, rec.FlattenReason);
                    }
                    else _r.Add("export-model", Tiers.Native, rec.CategoryName, rec.FamilyName + " : " + rec.TypeName, rec.Key, null);
                    _m.Elements.Add(rec);
                    _consecutiveFailures = 0;
                }
                catch (Exception ex)
                {
                    _r.Add("model", Tiers.Skipped, e.Category != null ? e.Category.Name : "", e.Name, e.UniqueId, ex.Message);
                    if (++_consecutiveFailures >= 50)
                        throw new InvalidOperationException("Model export stopped: 50 elements in a row failed with the same kind of error. Last error: " + ex.Message, ex);
                }
            }
        }

        // ------------------------------------------------------------------ classification

        static bool HasSketch(Element e)
        {
            return e.GetDependentElements(new ElementClassFilter(typeof(Sketch))).Count > 0;
        }

        void ClassifyWall(Wall w, ElementRec rec)
        {
            string why = null;
            if (w.WallType.Kind != WallKind.Basic) why = "Wall kind " + w.WallType.Kind + " not rebuilt natively yet";
            else if (!(w.Location is LocationCurve)) why = "Wall has no location curve";
            else if (w.IsStackedWallMember) why = "Stacked wall member";
            else if (RevitHelpers.GetInt(w, "WALL_TOP_IS_ATTACHED", 0) == 1 || RevitHelpers.GetInt(w, "WALL_BOTTOM_IS_ATTACHED", 0) == 1) why = "Wall top/base attached to another element";
            else if (HasSketch(w)) why = "Wall with edited profile";
            if (why != null) { rec.FlattenReason = why; return; }
            rec.Class = "Wall";
            rec.Loc = new LocRec { Kind = "Curve", Curve = CurveConv.ToRec(((LocationCurve)w.Location).Curve) };
            rec.Flipped = w.Flipped;
            rec.Params = ParamConv.Read(w);
        }

        void ClassifyFloor(Floor f, ElementRec rec)
        {
            string why = null;
            Sketch sk = null;
            try
            {
                var id = f.GetDependentElements(new ElementClassFilter(typeof(Sketch))).FirstOrDefault();
                if (id != null) sk = _doc.GetElement(id) as Sketch;
            }
            catch { }
            if (sk == null) why = "Floor sketch not found";
            else if (Compat.IsSlabShapeEdited(f)) why = "Floor with edited shape (slab shape editor)";
            else
            {
                CompoundStructure cs = f.FloorType.GetCompoundStructure();
                BoundingBoxXYZ bb = f.get_BoundingBox(null);
                if (cs == null || bb == null) why = "Floor structure unreadable";
                else if (bb.Max.Z - bb.Min.Z > cs.GetWidth() + 0.01) why = "Sloped floor";
            }
            if (why != null) { rec.FlattenReason = why; return; }
            rec.Class = "Floor";
            rec.Loops = new List<List<CurveRec>>();
            foreach (CurveArray ca in sk.Profile)
            {
                var loop = new List<CurveRec>();
                foreach (Curve c in ca) loop.Add(CurveConv.ToRec(c));
                rec.Loops.Add(loop);
            }
            rec.Params = ParamConv.Read(f);
        }

        void ClassifyFamilyInstance(FamilyInstance fi, ElementRec rec)
        {
            Family fam = fi.Symbol.Family;
            string why = null;
            string pt = fam.FamilyPlacementType.ToString();
            bool beam = _o.NativeBeams && pt == "CurveDrivenStructural" && fi.Location is LocationCurve && ((LocationCurve)fi.Location).Curve is Line;
            if (!_o.RebuildFamilies) why = "Family rebuild disabled in options";
            else if (fam.IsInPlace) why = "In-place family";
            else if (!fam.IsEditable) why = "Family is not editable";
            else if (fi.Mirrored) why = "Mirrored instance";
            else if (!beam && !(fi.Location is LocationPoint)) why = "Instance is not point-located";
            else if (!beam && pt != "OneLevelBased" && pt != "OneLevelBasedHosted" && pt != "TwoLevelsBased") why = "Placement type " + pt + " not rebuilt natively yet";
            else if (fi.Host != null)
            {
                var hw = fi.Host as Wall;
                if (hw == null || hw.CurtainGrid != null) why = "Host is not a plain wall";
                else rec.HostKey = hw.UniqueId;
            }
            if (why != null) { rec.FlattenReason = why; return; }
            if (beam)
            {
                rec.Class = "Beam";
                rec.Loc = new LocRec { Kind = "Curve", Curve = CurveConv.ToRec(((LocationCurve)fi.Location).Curve) };
            }
            else
            {
                rec.Class = "FamilyInstance";
                var lp = (LocationPoint)fi.Location;
                rec.Loc = new LocRec { Kind = "Point", Point = RevitHelpers.Pt(lp.Point), Rotation = lp.Rotation };
            }
            rec.FacingFlipped = fi.FacingFlipped;
            rec.HandFlipped = fi.HandFlipped;
            rec.Params = ParamConv.Read(fi);
            Register(fam);
        }

        bool Allowed(Element e, string cat)
        {
            if (_scope != null && !_scope.Contains(Compat.IdValue(e.Id))) return false;
            if (_o.IncludeCategories.Count > 0 && !_o.IncludeCategories.Contains(cat)) return false;
            if (_o.ExcludeCategories.Contains(cat)) return false;
            if (_o.LevelNames.Count > 0)
            {
                var lvl = _doc.GetElement(e.LevelId) as Level;
                if (lvl != null && !_o.LevelNames.Contains(lvl.Name)) return false;
            }
            return true;
        }

        void ClassifyRoom(Autodesk.Revit.DB.Architecture.Room room, ElementRec rec)
        {
            var lp = room.Location as LocationPoint;
            if (!_o.NativeRooms || lp == null || room.Area <= 0) { rec.FlattenReason = "Room not placed, or native rooms disabled"; return; }
            rec.Class = "Room";
            rec.Loc = new LocRec { Kind = "Point", Point = RevitHelpers.Pt(lp.Point) };
            rec.Params = ParamConv.Read(room);
        }

        void ClassifyMep(Element e, ElementRec rec, string cls, string systemBip)
        {
            var lc = e.Location as LocationCurve;
            if (!_o.NativeMep) { rec.FlattenReason = "Native MEP disabled in options"; return; }
            if (lc == null || !(lc.Curve is Line)) { rec.FlattenReason = cls + " is not a straight segment"; return; }
            rec.Class = cls;
            rec.Loc = new LocRec { Kind = "Curve", Curve = CurveConv.ToRec(lc.Curve) };
            rec.Params = ParamConv.Read(e);
            if (systemBip != null)
            {
                Parameter sp = RevitHelpers.Bip(e, systemBip);
                if (sp != null && sp.HasValue && sp.StorageType == StorageType.ElementId)
                {
                    Element sys = _doc.GetElement(sp.AsElementId());
                    if (sys != null) rec.SystemName = sys.Name;
                }
            }
        }

        void ClassifyRoof(FootPrintRoof roof, ElementRec rec)
        {
            if (!_o.NativeRoofs) { rec.FlattenReason = "Native roofs disabled in options"; return; }
            ModelCurveArrArray profiles = null;
            try { profiles = roof.GetProfiles(); } catch { }
            if (profiles == null || profiles.Size == 0) { rec.FlattenReason = "Roof footprint not readable"; return; }
            // slope information is read by name so a renamed / removed member in a newer Revit only costs the slopes, not the export
            System.Reflection.MethodInfo getDef = typeof(FootPrintRoof).GetMethod("get_DefinesSlope");
            System.Reflection.MethodInfo getSlope = typeof(FootPrintRoof).GetMethod("get_SlopeAngle");
            rec.Loops = new List<List<CurveRec>>();
            rec.LoopDefinesSlope = new List<List<bool>>();
            rec.LoopSlope = new List<List<double>>();
            foreach (ModelCurveArray arr in profiles)
            {
                var loop = new List<CurveRec>();
                var defs = new List<bool>();
                var slopes = new List<double>();
                foreach (ModelCurve mc in arr)
                {
                    loop.Add(CurveConv.ToRec(mc.GeometryCurve));
                    bool d = false; double a = 0;
                    try
                    {
                        if (getDef != null) d = (bool)getDef.Invoke(roof, new object[] { mc });
                        if (d && getSlope != null) a = (double)getSlope.Invoke(roof, new object[] { mc });
                    }
                    catch { }
                    defs.Add(d); slopes.Add(a);
                }
                rec.Loops.Add(loop); rec.LoopDefinesSlope.Add(defs); rec.LoopSlope.Add(slopes);
            }
            rec.Class = "Roof";
            rec.Params = ParamConv.Read(roof);
        }

        /// <summary>Measures every type of a family (bounds + mesh). Symbols that are not active have no geometry, so they are activated
        /// inside a transaction that is always rolled back.</summary>
        void MeasureTypes(Family fam, FamilyRec fr, GeometryPackWriter geo)
        {
            try
            {
                using (var t = new Transaction(_doc, "Downgrade: read family type geometry"))
                {
                    t.Start();
                    try
                    {
                        var syms = fam.GetFamilySymbolIds().Select(id => _doc.GetElement(id) as FamilySymbol).Where(s => s != null).ToList();
                        foreach (FamilySymbol s in syms) if (!s.IsActive) s.Activate();
                        _doc.Regenerate();
                        foreach (FamilySymbol s in syms)
                        {
                            double[] mn, mx;
                            if (!SymbolGeometry.Bounds(s, out mn, out mx)) continue;
                            var rec = new TypeBoxRec { Name = s.Name, Min = mn, Max = mx };
                            List<MeshRec> meshes = GeometryMesher.Mesh(s, _doc, Math.Max(_o.MeshDetail, 0.6));
                            if (meshes.Count > 0) rec.GeoOffset = geo.Write(meshes);
                            fr.TypeGeometry.Add(rec);
                        }
                    }
                    finally { t.RollBack(); }
                }
            }
            catch (Exception ex)
            {
                _r.Add("export-family", Tiers.NativeWithLoss, fr.Category, fr.Name, fr.Key, "Type geometry not measured (no verification possible): " + ex.Message);
            }
        }

        void Register(Family fam)
        {
            if (_fams.ContainsKey(fam.UniqueId)) return;
            _fams[fam.UniqueId] = new FamilyRec { Key = fam.UniqueId, Name = fam.Name, Category = RevitHelpers.CategoryName(fam.FamilyCategory), PlacementType = fam.FamilyPlacementType.ToString(), Editable = fam.IsEditable };
            _famObjs[fam.UniqueId] = fam;
        }

        TypeRec AddType(ElementType et)
        {
            TypeRec t;
            if (_types.TryGetValue(et.UniqueId, out t)) return t;
            t = new TypeRec { Key = et.UniqueId, Name = et.Name, FamilyName = et.FamilyName, Category = RevitHelpers.CategoryName(et.Category), Kind = "Other" };
            CompoundStructure cs = null;
            var wt = et as WallType;
            var ft = et as FloorType;
            if (wt != null) { t.Kind = "Wall"; t.Width = wt.Width; cs = wt.GetCompoundStructure(); }
            else if (ft != null) { t.Kind = "Floor"; cs = ft.GetCompoundStructure(); if (cs != null) t.Width = cs.GetWidth(); }
            else if (et is RoofType) { t.Kind = "Roof"; cs = ((RoofType)et).GetCompoundStructure(); if (cs != null) t.Width = cs.GetWidth(); }
            else if (et is FamilySymbol) t.Kind = "Family";
            if (cs != null)
                foreach (CompoundStructureLayer l in cs.GetLayers())
                {
                    var mat = _doc.GetElement(l.MaterialId) as Material;
                    t.Layers.Add(new LayerRec { Material = mat != null ? mat.Name : null, Thickness = l.Width, Function = l.Function.ToString() });
                }
            if (cs != null)
            {
                try
                {
                    t.ShellExterior = cs.GetNumberOfShellLayers(ShellLayerType.Exterior);
                    t.ShellInterior = cs.GetNumberOfShellLayers(ShellLayerType.Interior);
                    t.StructuralIndex = cs.StructuralMaterialIndex;
                }
                catch { }
            }
            if (t.Kind != "Other") t.Params = ParamConv.Read(et);
            _types[et.UniqueId] = t;
            return t;
        }

        // ------------------------------------------------------------------ families

        void ExportFamilies(GeometryPackWriter geo)
        {
            int famDone = 0;
            foreach (FamilyRec fr in _fams.Values)
            {
                JobProgress.Current.Report("Extracting families", ++famDone, _fams.Count, true);
                Family fam = _famObjs[fr.Key];
                foreach (ElementId id in fam.GetFamilySymbolIds())
                {
                    var s = _doc.GetElement(id) as FamilySymbol;
                    if (s != null) fr.TypeNames.Add(s.Name);
                }
                fr.Fidelity = "None";
                if (!_o.RebuildFamilies || !fam.IsEditable) continue;
                if (_o.StoreTypeGeometry) MeasureTypes(fam, fr, geo);
                Document fd = null;
                try
                {
                    fd = _doc.EditFamily(fam);
                    FamilyDefRec def = FamilyDefExtractor.Extract(fd);
                    string file = RevitHelpers.SafeFileName(fam.Name) + ".family.json";
                    Json.Write(Path.Combine(_p.FamiliesDir, file), def, true);
                    fr.DefFile = file;
                    fr.Fidelity = def.Fidelity;
                    fr.Dropped = def.Dropped;
                    bool clean = def.Fidelity == "Full" && def.Dropped.Count == 0;
                    _r.Add("export-family", clean ? Tiers.Native : Tiers.NativeWithLoss, fr.Category, fam.Name, fr.Key,
                        clean ? null : (def.Fidelity == "Partial" ? "Partial (solid geometry not representable): " : "Dropped: ") + string.Join(", ", def.Dropped));
                }
                catch (Exception ex)
                {
                    _r.Add("export-family", Tiers.DirectShape, fr.Category, fam.Name, fr.Key, "Could not open family for extraction: " + ex.Message);
                }
                finally { if (fd != null) fd.Close(false); }
            }
        }
    }
}
