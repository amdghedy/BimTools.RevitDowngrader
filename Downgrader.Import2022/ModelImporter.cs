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
    /// <summary>Package A -> 2022 project. Policy per element: try a native rebuild, verify it against the source bounding box,
    /// and fall back to a DirectShape (same category, same geometry, same materials) when it cannot be rebuilt or does not match.</summary>
    public class ModelImporter
    {
        readonly Application _app;
        readonly Document _doc;
        readonly PackagePaths _p;
        readonly DowngradeOptions _o;
        readonly ConversionReport _r;
        ModelPackage _m;
        IdMap _map;
        Resolver _res;
        Dictionary<string, string> _built = new Dictionary<string, string>();
        readonly Dictionary<string, ElementId> _typeIds = new Dictionary<string, ElementId>();
        readonly Dictionary<string, FamilySymbol> _symbols = new Dictionary<string, FamilySymbol>();
        readonly Dictionary<string, int> _created = new Dictionary<string, int>();
        Dictionary<string, string> _levelNames;
        int _filtered;
        HashSet<string> _frozen = new HashSet<string>();                          // families rebuilt with frozen per-type geometry
        readonly HashSet<string> _badFamilies = new HashSet<string>();            // rebuilt families whose types do not match the source: instances become DirectShapes
        List<FamilyRec> _failedFamilies = new List<FamilyRec>();
        readonly Dictionary<string, double> _familyDev = new Dictionary<string, double>();

        public ModelImporter(Application app, Document doc, PackagePaths p, DowngradeOptions o, ConversionReport r)
        { _app = app; _doc = doc; _p = p; _o = o; _r = r; }

        public void Run()
        {
            _m = Json.Read<ModelPackage>(_p.ModelJson);
            _map = IdMap.LoadOrNew(_p);
            _res = new Resolver(_doc, _map);

            // 1. families are rebuilt in their own documents first (no project transaction open)
            using (var shared = new SharedParamHelper(_app, Path.Combine(_p.Root, "shared_params_2020.txt")))
            {
                var fb = new FamilyBuilder(_app, _p, _o, _r, shared);
                if (_o.RebuildFamilies && _o.Mode == ExportMode.Hybrid) fb.BuildAll(_m.Families);
                _built = fb.Built;
                _frozen = fb.Frozen;
            }

            // 2. project setup
            using (Transaction t = Tx.Start(_doc, "Downgrade: setup"))
            {
                ImportMaterials();
                ImportLevels();
                ImportGrids();
                ImportTypes();
                LoadFamilies();
                t.Commit();
            }

            // 2b. families whose types do not match the source after loading are rebuilt with frozen (exact) per-type geometry
            RebuildMismatchedFamilies();
            ReportFamilyVerification();

            // 3. elements
            using (Transaction t = Tx.Start(_doc, "Downgrade: elements"))
            {
                ImportElements();
                t.Commit();
            }

            // Revit may have removed elements while resolving failures at commit time
            foreach (var kv in _created.ToList())
                if (_doc.GetElement(new ElementId(kv.Value)) == null)
                {
                    _map.Map.Remove(kv.Key);
                    _r.Add("model", Tiers.Skipped, "", kv.Key, kv.Key, "Removed by Revit while resolving a failure at commit");
                }
            if (_filtered > 0) _r.Add("model", Tiers.Skipped, "Filter", _filtered + " elements", null, "Excluded by the category / level filter in the options");
            if (!_o.DryRun) _map.Save(_p);   // a dry run is rolled back, so its ids must not be stored
        }

        // ------------------------------------------------------------------ setup

        void ImportMaterials()
        {
            foreach (MaterialRec mr in _m.Materials)
            {
                try
                {
                    if (_res.Material(mr.Name) != null) continue;
                    var mat = (Material)_doc.GetElement(Material.Create(_doc, mr.Name));
                    if (mr.Rgb != null) mat.Color = new Color((byte)mr.Rgb[0], (byte)mr.Rgb[1], (byte)mr.Rgb[2]);
                    mat.Transparency = mr.Transparency;
                    mat.Shininess = mr.Shininess;
                    mat.Smoothness = mr.Smoothness;
                    if (!string.IsNullOrEmpty(mr.MaterialClass)) mat.MaterialClass = mr.MaterialClass;
                }
                catch (Exception ex) { _r.Add("model", Tiers.NativeWithLoss, "Material", mr.Name, null, ex.Message); }
            }
            _res.Invalidate();
        }

        void ImportLevels()
        {
            var existing = new Dictionary<string, Level>();
            foreach (Level l in new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>()) existing[l.Name] = l;
            foreach (LevelRec lr in _m.Levels)
            {
                try
                {
                    Level lv;
                    if (existing.TryGetValue(lr.Name, out lv)) { if (Math.Abs(lv.Elevation - lr.Elevation) > 1e-6) lv.Elevation = lr.Elevation; }
                    else { lv = Level.Create(_doc, lr.Elevation); lv.Name = lr.Name; }
                    _map.Map[lr.Key] = lv.Id.IntegerValue;
                    _r.Add("model", Tiers.Native, "Level", lr.Name, lr.Key, null);
                }
                catch (Exception ex) { _r.Add("model", Tiers.Skipped, "Level", lr.Name, lr.Key, ex.Message); }
            }
        }

        void ImportGrids()
        {
            var names = new HashSet<string>(new FilteredElementCollector(_doc).OfClass(typeof(Grid)).Select(g => g.Name));
            foreach (GridRec gr in _m.Grids)
            {
                try
                {
                    if (names.Contains(gr.Name)) continue;
                    Curve c = CurveConv.ToCurves(gr.Curve).FirstOrDefault();
                    if (c == null) continue;
                    Grid g = c is Line ? Grid.Create(_doc, (Line)c) : Grid.Create(_doc, (Arc)c);
                    g.Name = gr.Name;
                    _map.Map[gr.Key] = g.Id.IntegerValue;
                    _r.Add("model", Tiers.Native, "Grid", gr.Name, gr.Key, null);
                }
                catch (Exception ex) { _r.Add("model", Tiers.Skipped, "Grid", gr.Name, gr.Key, ex.Message); }
            }
        }

        static CompoundStructure BuildStructure(TypeRec t, Resolver res)
        {
            var layers = new List<CompoundStructureLayer>();
            foreach (LayerRec l in t.Layers.Where(x => x.Thickness > 1e-6))
            {
                MaterialFunctionAssignment fn;
                if (!RevitHelpers.TryEnum(l.Function, out fn)) fn = MaterialFunctionAssignment.Structure;
                layers.Add(new CompoundStructureLayer(l.Thickness, fn, res.Material(l.Material) ?? ElementId.InvalidElementId));
            }
            if (layers.Count == 0) return null;
            CompoundStructure cs = CompoundStructure.CreateSimpleCompoundStructure(layers);
            if (layers.Count == t.Layers.Count)   // layer indices are only meaningful when no layer was dropped
            {
                try
                {
                    cs.SetNumberOfShellLayers(ShellLayerType.Exterior, t.ShellExterior);
                    cs.SetNumberOfShellLayers(ShellLayerType.Interior, t.ShellInterior);
                }
                catch { }
                try { if (t.StructuralIndex >= 0 && t.StructuralIndex < layers.Count) cs.StructuralMaterialIndex = t.StructuralIndex; } catch { }
            }
            return cs;
        }

        void ImportTypes()
        {
            foreach (TypeRec t in _m.Types.Where(x => x.Kind == "Wall" || x.Kind == "Floor" || x.Kind == "Roof"))
            {
                try
                {
                    Type cls = t.Kind == "Wall" ? typeof(WallType) : (t.Kind == "Floor" ? typeof(FloorType) : typeof(RoofType));
                    ElementType found = new FilteredElementCollector(_doc).OfClass(cls).Cast<ElementType>().FirstOrDefault(x => x.Name == t.Name);
                    if (found != null) { _typeIds[t.Key] = found.Id; continue; }

                    CompoundStructure cs = BuildStructure(t, _res);
                    ElementType dup;
                    if (t.Kind == "Wall")
                    {
                        var basis = new FilteredElementCollector(_doc).OfClass(typeof(WallType)).Cast<WallType>().First(x => x.Kind == WallKind.Basic);
                        var wt = (WallType)basis.Duplicate(t.Name);
                        if (cs != null) wt.SetCompoundStructure(cs);
                        dup = wt;
                    }
                    else if (t.Kind == "Floor")
                    {
                        var basis = new FilteredElementCollector(_doc).OfClass(typeof(FloorType)).Cast<FloorType>().First(x => !x.IsFoundationSlab);
                        var ft = (FloorType)basis.Duplicate(t.Name);
                        if (cs != null) ft.SetCompoundStructure(cs);
                        dup = ft;
                    }
                    else
                    {
                        var all = new FilteredElementCollector(_doc).OfClass(typeof(RoofType)).Cast<RoofType>().ToList();
                        RoofType basis = all.FirstOrDefault(x => x.FamilyName == "Basic Roof") ?? all.First();
                        var rt = (RoofType)basis.Duplicate(t.Name);
                        if (cs != null) rt.SetCompoundStructure(cs);
                        dup = rt;
                    }
                    ParamConv.Apply(dup, t.Params, _res.Resolve);
                    _typeIds[t.Key] = dup.Id;
                    _r.Add("model", Tiers.Native, t.Kind + "Type", t.Name, t.Key, null);
                }
                catch (Exception ex) { _r.Add("model", Tiers.NativeWithLoss, t.Kind + "Type", t.Name, t.Key, "Type not recreated, elements of this type fall back to DirectShape: " + ex.Message); }
            }
        }

        void LoadFamilies()
        {
            foreach (var kv in _built)
            {
                try { Family fam; _doc.LoadFamily(kv.Value, new FamilyLoadOptions(), out fam); }
                catch (Exception ex) { _r.Add("family", Tiers.DirectShape, "Family", kv.Key, null, "Load failed: " + ex.Message); }
            }
            RefreshSymbols();
            ApplyFamilyTypeParams();
            _failedFamilies = VerifyFamilies(_m.Families);
        }

        void RefreshSymbols()
        {
            _symbols.Clear();
            foreach (FamilySymbol s in new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
                _symbols[s.FamilyName + "|" + s.Name] = s;
        }

        /// <summary>Type parameters of the rebuilt families (Description, Model, Manufacturer, Type Mark, Keynote, Cost, ...).</summary>
        void ApplyFamilyTypeParams()
        {
            foreach (TypeRec t in _m.Types.Where(x => x.Kind == "Family" && x.FamilyName != null && _built.ContainsKey(x.FamilyName)))
            {
                FamilySymbol s;
                if (_symbols.TryGetValue(t.FamilyName + "|" + t.Name, out s)) ParamConv.Apply(s, t.Params, _res.Resolve);
            }
        }

        static double MaxDelta3(double[] a, double[] b)
        {
            return Math.Max(Math.Abs(a[0] - b[0]), Math.Max(Math.Abs(a[1] - b[1]), Math.Abs(a[2] - b[2])));
        }

        /// <summary>Compares the bounds of every type of every rebuilt family with the bounds measured in the source.</summary>
        List<FamilyRec> VerifyFamilies(IEnumerable<FamilyRec> families)
        {
            var failed = new List<FamilyRec>();
            if (!_o.VerifyFamilyTypes) return failed;
            foreach (FamilyRec fr in families)
            {
                if (!_built.ContainsKey(fr.Name) || fr.TypeGeometry.Count == 0) continue;
                double worst = 0;
                foreach (TypeBoxRec tb in fr.TypeGeometry)
                {
                    FamilySymbol s;
                    if (tb.Min == null || !_symbols.TryGetValue(fr.Name + "|" + tb.Name, out s)) continue;
                    try
                    {
                        if (!s.IsActive) s.Activate();
                        _doc.Regenerate();
                        double[] mn, mx;
                        double d = SymbolGeometry.Bounds(s, out mn, out mx) ? Math.Max(MaxDelta3(mn, tb.Min), MaxDelta3(mx, tb.Max)) : double.MaxValue;
                        if (d > worst) worst = d;
                    }
                    catch { worst = double.MaxValue; }
                }
                _familyDev[fr.Name] = worst;
                if (worst > _o.FamilyVerifyFeet) failed.Add(fr);
            }
            return failed;
        }

        void RebuildMismatchedFamilies()
        {
            if (_failedFamilies.Count == 0) return;
            var redo = new List<FamilyRec>();
            using (var shared = new SharedParamHelper(_app, Path.Combine(_p.Root, "shared_params_2020.txt")))
            {
                var fb = new FamilyBuilder(_app, _p, _o, _r, shared);
                int n = 0;
                foreach (FamilyRec fr in _failedFamilies)
                {
                    JobProgress.Current.Report("Rebuilding family geometry (frozen)", ++n, _failedFamilies.Count, true);
                    if (!_o.FreezePartialFamilies || _frozen.Contains(fr.Name) || fr.TypeGeometry.All(t => t.GeoOffset < 0)) { _badFamilies.Add(fr.Name); continue; }
                    try { fb.Build(fr, true); redo.Add(fr); }
                    catch (Exception ex) { _badFamilies.Add(fr.Name); _r.Add("family", Tiers.DirectShape, fr.Category, fr.Name, fr.Key, "Frozen rebuild failed: " + ex.Message); }
                }
                foreach (string f in fb.Frozen) _frozen.Add(f);
                foreach (var kv in fb.Built) _built[kv.Key] = kv.Value;
            }
            if (redo.Count == 0) return;
            using (Transaction t = Tx.Start(_doc, "Downgrade: frozen families"))
            {
                foreach (FamilyRec fr in redo)
                {
                    try { Family f; _doc.LoadFamily(_built[fr.Name], new FamilyLoadOptions(), out f); }
                    catch (Exception ex) { _badFamilies.Add(fr.Name); _r.Add("family", Tiers.DirectShape, fr.Category, fr.Name, fr.Key, "Reload failed: " + ex.Message); }
                }
                RefreshSymbols();
                ApplyFamilyTypeParams();
                foreach (FamilyRec fr in VerifyFamilies(redo)) _badFamilies.Add(fr.Name);
                t.Commit();
            }
        }

        void ReportFamilyVerification()
        {
            foreach (FamilyRec fr in _m.Families.Where(f => _familyDev.ContainsKey(f.Name)))
            {
                double d = _familyDev[fr.Name];
                if (_badFamilies.Contains(fr.Name))
                    _r.Add("family-verify", Tiers.DirectShape, fr.Category, fr.Name, fr.Key, "Type geometry still differs from the source by " + (d > 1e6 ? "(no geometry)" : d.ToString("0.000") + " ft") + "; its instances become DirectShapes");
                else if (_frozen.Contains(fr.Name))
                    _r.Add("family-verify", Tiers.NativeWithLoss, fr.Category, fr.Name, fr.Key, "Geometry per type equals the source (max deviation " + d.ToString("0.000") + " ft) but the family is not parametric");
                else
                    _r.Add("family-verify", Tiers.Native, fr.Category, fr.Name, fr.Key, null);
            }
        }

        // ------------------------------------------------------------------ elements

        static int Rank(ElementRec r)
        {
            switch (r.Class)
            {
                case "Wall": return 0;
                case "Floor": case "Roof": return 1;
                case "FamilyInstance": case "Beam": case "Pipe": case "Duct": case "Conduit": case "CableTray": return 2;
                case "Room": return 3;      // rooms need their bounding walls to exist
                default: return 4;
            }
        }

        void ImportElements()
        {
            using (var geo = new GeometryPackReader(_p.GeometryPack))
            {
                var todo = _m.Elements.Where(AllowedImport).OrderBy(Rank).ToList();
                int done = 0;
                foreach (ElementRec rec in todo)
                {
                    JobProgress.Current.Report("Importing elements", ++done, todo.Count);
                    string name = ((rec.FamilyName ?? "") + " : " + (rec.TypeName ?? "")).Trim(' ', ':');
                    string why = null;
                    string tier = Tiers.Native;
                    ElementId id = null;
                    ElementId native = null;
                    double dev = 0;

                    // ---- attempt 1: native rebuild, verified
                    string forced = ForcedReason(rec);
                    if (forced == null)
                    {
                        using (var st = new SubTransaction(_doc))
                        {
                            st.Start();
                            bool ok = false;
                            try
                            {
                                native = TryNative(rec, out why);
                                if (native != null)
                                {
                                    _doc.Regenerate();
                                    dev = Deviation(_doc.GetElement(native), rec);
                                    if (_o.VerifyNative && dev > _o.FlattenDeviationFeet)
                                        why = "Native rebuild deviated " + dev.ToString("0.000") + " ft from the source geometry";
                                    else ok = true;
                                }
                            }
                            catch (Exception ex) { why = "Native creation failed: " + ex.Message; }
                            if (ok) st.Commit(); else { st.RollBack(); native = null; }
                        }
                    }
                    else why = forced;

                    if (native != null)
                    {
                        id = native;
                        if (dev > _o.WarnDeviationFeet) { tier = Tiers.NativeWithLoss; why = "Rebuilt natively, bounding box differs by " + dev.ToString("0.000") + " ft"; }
                        else why = null;
                    }
                    else
                    {
                        // ---- attempt 2: DirectShape fallback
                        using (var st = new SubTransaction(_doc))
                        {
                            st.Start();
                            try
                            {
                                List<MeshRec> meshes = rec.GeoOffset >= 0 ? geo.Read(rec.GeoOffset) : new List<MeshRec>();
                                string note;
                                id = DirectShapeBuilder.Create(_doc, rec, meshes, n => _res.Material(n), out note);
                                tier = id != null ? Tiers.DirectShape : Tiers.Skipped;
                                if (note != null) why = (why == null ? "" : why + "; ") + note;
                                st.Commit();
                            }
                            catch (Exception ex)
                            {
                                st.RollBack();
                                id = null; tier = Tiers.Skipped; why = (why == null ? "" : why + "; ") + ex.Message;
                            }
                        }
                    }
                    if (id != null) { _map.Map[rec.Key] = id.IntegerValue; _created[rec.Key] = id.IntegerValue; }
                    _r.Add("model", tier, rec.CategoryName, name, rec.Key, why);
                }
            }
        }

        ElementId TryNative(ElementRec rec, out string why)
        {
            why = null;
            if ((rec.Class == "FamilyInstance" || rec.Class == "Beam") && rec.FamilyName != null && _badFamilies.Contains(rec.FamilyName))
            { why = "The rebuilt family does not match the source geometry (see family-verify in the report)"; return null; }
            switch (rec.Class)
            {
                case "Roof": return CreateRoof(rec, out why);
                case "Wall": return CreateWall(rec, out why);
                case "Floor": return CreateFloor(rec, out why);
                case "FamilyInstance": return CreateInstance(rec, out why);
                case "Beam": return CreateBeam(rec, out why);
                case "Room": return CreateRoom(rec, out why);
                case "Pipe": case "Duct": case "Conduit": case "CableTray": return CreateMep(rec, out why);
            }
            why = "Class " + rec.Class + " has no native rebuild";
            return null;
        }

        ElementId CreateWall(ElementRec rec, out string why)
        {
            why = null;
            ElementId tid;
            if (rec.TypeKey == null || !_typeIds.TryGetValue(rec.TypeKey, out tid)) { why = "Wall type could not be recreated"; return null; }
            ElementId lid = _res.Level(rec.LevelKey);
            if (lid == null) { why = "Base level not found"; return null; }
            Curve c = CurveConv.ToCurves(rec.Loc.Curve).FirstOrDefault();
            if (c == null) { why = "Wall curve too short"; return null; }
            double h = 10;
            ParamRec hp = rec.Params.FirstOrDefault(p => p.BuiltIn == "WALL_USER_HEIGHT_PARAM");
            if (hp != null && hp.D > 0.01) h = hp.D;
            bool structural = rec.Params.Any(p => p.BuiltIn == "WALL_STRUCTURAL_SIGNIFICANT" && p.I == 1);

            Wall w = Wall.Create(_doc, c, tid, lid, h, 0, rec.Flipped, structural);
            ParamConv.Apply(w, rec.Params, _res.Resolve);                 // constraints, offsets, location line...
            try { ((LocationCurve)w.Location).Curve = c; } catch { }      // re-assert the location curve after the location-line change
            if (w.Flipped != rec.Flipped) { try { w.Flip(); } catch { } }
            return w.Id;
        }

        static double LoopSize(List<CurveRec> loop)
        {
            var pts = new List<double[]>();
            foreach (CurveRec c in loop)
            {
                if (c.A != null) pts.Add(c.A);
                if (c.B != null) pts.Add(c.B);
                if (c.Pts != null) pts.AddRange(c.Pts);
                if (c.Center != null) { pts.Add(new[] { c.Center[0] - c.Radius, c.Center[1] - c.Radius, 0 }); pts.Add(new[] { c.Center[0] + c.Radius, c.Center[1] + c.Radius, 0 }); }
            }
            if (pts.Count == 0) return 0;
            return (pts.Max(p => p[0]) - pts.Min(p => p[0])) * (pts.Max(p => p[1]) - pts.Min(p => p[1]));
        }

        static CurveArray ToCurveArray(List<CurveRec> loop, double z)
        {
            var ca = new CurveArray();
            foreach (CurveRec c in loop) foreach (Curve k in CurveConv.ToCurves(c, z)) ca.Append(k);
            return ca;
        }

        ElementId CreateFloor(ElementRec rec, out string why)
        {
            why = null;
            ElementId tid;
            if (rec.TypeKey == null || !_typeIds.TryGetValue(rec.TypeKey, out tid)) { why = "Floor type could not be recreated"; return null; }
            ElementId lid = _res.Level(rec.LevelKey);
            if (lid == null) { why = "Level not found"; return null; }
            var lvl = (Level)_doc.GetElement(lid);
            var loops = rec.Loops.OrderByDescending(LoopSize).ToList();
            bool structural = rec.Params.Any(p => p.BuiltIn == "FLOOR_PARAM_IS_STRUCTURAL" && p.I == 1);

            Floor f = _doc.Create.NewFloor(ToCurveArray(loops[0], lvl.Elevation), (FloorType)_doc.GetElement(tid), lvl, structural);
            for (int i = 1; i < loops.Count; i++)
                try { _doc.Create.NewOpening(f, ToCurveArray(loops[i], lvl.Elevation), true); } catch { }
            ParamConv.Apply(f, rec.Params, _res.Resolve);   // height offset etc.
            return f.Id;
        }

        ElementId CreateInstance(ElementRec rec, out string why)
        {
            why = null;
            FamilySymbol sym;
            if (!_symbols.TryGetValue((rec.FamilyName ?? "") + "|" + (rec.TypeName ?? ""), out sym)) { why = "No matching family type in 2022 (family not rebuilt or type missing)"; return null; }
            FamilyRec fr = _m.Families.FirstOrDefault(f => f.Name == rec.FamilyName);
            if (fr != null && _built.ContainsKey(rec.FamilyName) && fr.Fidelity != "Full" && !_o.AllowPartialFamilies && !_frozen.Contains(rec.FamilyName))
            { why = "Family only partially rebuilt (" + string.Join(", ", fr.Dropped) + ")"; return null; }
            ElementId lid = _res.Level(rec.LevelKey);
            if (lid == null) { why = "Level not found"; return null; }
            var lvl = (Level)_doc.GetElement(lid);
            if (!sym.IsActive) sym.Activate();

            XYZ pt = RevitHelpers.ToXyz(rec.Loc.Point);
            Autodesk.Revit.DB.Structure.StructuralType st = rec.Category == "OST_StructuralColumns" ? Autodesk.Revit.DB.Structure.StructuralType.Column : Autodesk.Revit.DB.Structure.StructuralType.NonStructural;
            FamilyInstance fi;
            if (rec.HostKey != null)
            {
                int hid;
                if (!_map.Map.TryGetValue(rec.HostKey, out hid)) { why = "Host wall was not created"; return null; }
                var host = _doc.GetElement(new ElementId(hid)) as Wall;
                if (host == null) { why = "Host wall was flattened to a DirectShape"; return null; }
                fi = _doc.Create.NewFamilyInstance(pt, sym, host, lvl, st);
            }
            else
            {
                fi = _doc.Create.NewFamilyInstance(pt, sym, lvl, st);
                if (Math.Abs(rec.Loc.Rotation) > 1e-9)
                    ElementTransformUtils.RotateElement(_doc, fi.Id, Line.CreateBound(pt, pt + XYZ.BasisZ), rec.Loc.Rotation);
            }
            if (fi.CanFlipFacing && fi.FacingFlipped != rec.FacingFlipped) fi.flipFacing();
            if (fi.CanFlipHand && fi.HandFlipped != rec.HandFlipped) fi.flipHand();
            ParamConv.Apply(fi, rec.Params, _res.Resolve);
            return fi.Id;
        }

        bool AllowedImport(ElementRec rec)
        {
            bool ok = true;
            if (_o.IncludeCategories.Count > 0 && !_o.IncludeCategories.Contains(rec.Category)) ok = false;
            else if (_o.ExcludeCategories.Contains(rec.Category)) ok = false;
            else if (_o.LevelNames.Count > 0 && rec.LevelKey != null)
            {
                if (_levelNames == null) _levelNames = _m.Levels.ToDictionary(l => l.Key, l => l.Name);
                string ln;
                if (_levelNames.TryGetValue(rec.LevelKey, out ln) && !_o.LevelNames.Contains(ln)) ok = false;
            }
            if (!ok) _filtered++;
            return ok;
        }

        ElementId CreateRoof(ElementRec rec, out string why)
        {
            why = null;
            ElementId tid;
            if (rec.TypeKey == null || !_typeIds.TryGetValue(rec.TypeKey, out tid)) { why = "Roof type could not be recreated"; return null; }
            ElementId lid = _res.Level(rec.LevelKey);
            if (lid == null) { why = "Base level not found"; return null; }
            if (rec.Loops == null || rec.Loops.Count == 0 || rec.LoopDefinesSlope == null) { why = "Roof footprint missing"; return null; }
            var lvl = (Level)_doc.GetElement(lid);

            List<CurveRec> loop = rec.Loops.OrderByDescending(LoopSize).First();   // outer footprint (inner loops would be roof openings)
            int li = rec.Loops.IndexOf(loop);
            var footprint = new CurveArray();
            var defs = new List<bool>();
            var slopes = new List<double>();
            for (int i = 0; i < loop.Count; i++)
                foreach (Curve k in CurveConv.ToCurves(loop[i], lvl.Elevation))
                {
                    footprint.Append(k);
                    defs.Add(rec.LoopDefinesSlope[li][i]);
                    slopes.Add(rec.LoopSlope[li][i]);
                }
            ModelCurveArray mcs;
            FootPrintRoof roof = _doc.Create.NewFootPrintRoof(footprint, lvl, (RoofType)_doc.GetElement(tid), out mcs);
            int n = 0;
            foreach (ModelCurve mc in mcs)
            {
                if (n < defs.Count)
                {
                    roof.set_DefinesSlope(mc, defs[n]);
                    if (defs[n]) roof.set_SlopeAngle(mc, slopes[n]);
                }
                n++;
            }
            ParamConv.Apply(roof, rec.Params, _res.Resolve);   // base offset, cutoff, rafter cut ...
            if (rec.Loops.Count > 1) why = "Roof openings (inner loops) were not recreated";
            return roof.Id;
        }

        string ForcedReason(ElementRec rec)
        {
            if (rec.FlattenReason != null) return rec.FlattenReason;
            if (_o.Mode == ExportMode.GeometryOnly) return "Geometry-only mode";
            if (_o.ForceDirectShapeCategories.Contains(rec.Category)) return "Category forced to DirectShape in options";
            return null;
        }

        Level LevelOrNearest(ElementRec rec, double z)
        {
            ElementId lid = _res.Level(rec.LevelKey);
            if (lid != null) return (Level)_doc.GetElement(lid);
            return new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => Math.Abs(l.Elevation - z)).FirstOrDefault();
        }

        ElementId CreateBeam(ElementRec rec, out string why)
        {
            why = null;
            FamilySymbol sym;
            if (!_symbols.TryGetValue((rec.FamilyName ?? "") + "|" + (rec.TypeName ?? ""), out sym)) { why = "No matching beam type in 2022 (family not rebuilt or type missing)"; return null; }
            FamilyRec fr = _m.Families.FirstOrDefault(f => f.Name == rec.FamilyName);
            if (fr != null && _built.ContainsKey(rec.FamilyName) && fr.Fidelity != "Full" && !_o.AllowPartialFamilies && !_frozen.Contains(rec.FamilyName))
            { why = "Beam family only partially rebuilt (" + string.Join(", ", fr.Dropped) + ")"; return null; }
            Curve c = CurveConv.ToCurves(rec.Loc.Curve).FirstOrDefault();
            if (c == null) { why = "Beam curve too short"; return null; }
            Level lvl = LevelOrNearest(rec, c.GetEndPoint(0).Z);
            if (lvl == null) { why = "No level in project"; return null; }
            if (!sym.IsActive) sym.Activate();
            FamilyInstance fi = _doc.Create.NewFamilyInstance(c, sym, lvl, Autodesk.Revit.DB.Structure.StructuralType.Beam);
            ParamConv.Apply(fi, rec.Params, _res.Resolve);
            return fi.Id;
        }

        ElementId CreateRoom(ElementRec rec, out string why)
        {
            why = null;
            ElementId lid = _res.Level(rec.LevelKey);
            if (lid == null) { why = "Level not found"; return null; }
            XYZ p = RevitHelpers.ToXyz(rec.Loc.Point);
            Autodesk.Revit.DB.Architecture.Room room = _doc.Create.NewRoom((Level)_doc.GetElement(lid), new UV(p.X, p.Y));
            if (room == null) { why = "Room could not be placed"; return null; }
            _doc.Regenerate();
            if (room.Area < 1e-6) { why = "Room is not enclosed in 2022 (bounding walls missing or flattened)"; return null; }
            ParamConv.Apply(room, rec.Params, _res.Resolve);   // name, number, offsets, ...
            return room.Id;
        }

        ElementId TypeByName(Type t, string name)
        {
            var all = new FilteredElementCollector(_doc).OfClass(t).Cast<Element>().ToList();
            Element m = all.FirstOrDefault(x => x.Name == name) ?? all.FirstOrDefault();
            return m == null ? null : m.Id;
        }

        ElementId CreateMep(ElementRec rec, out string why)
        {
            why = null;
            var line = CurveConv.ToCurves(rec.Loc.Curve).FirstOrDefault() as Line;
            if (line == null) { why = "Segment too short"; return null; }
            XYZ a = line.GetEndPoint(0), b = line.GetEndPoint(1);
            Level lvl = LevelOrNearest(rec, a.Z);
            if (lvl == null) { why = "No level in project"; return null; }
            Element created = null;
            switch (rec.Class)
            {
                case "Duct":
                    {
                        ElementId type = TypeByName(typeof(Autodesk.Revit.DB.Mechanical.DuctType), rec.TypeName);
                        ElementId sys = TypeByName(typeof(Autodesk.Revit.DB.Mechanical.MechanicalSystemType), rec.SystemName);
                        if (type == null || sys == null) { why = "No duct type or mechanical system type in project"; return null; }
                        created = Autodesk.Revit.DB.Mechanical.Duct.Create(_doc, sys, type, lvl.Id, a, b);
                        break;
                    }
                case "Pipe":
                    {
                        ElementId type = TypeByName(typeof(Autodesk.Revit.DB.Plumbing.PipeType), rec.TypeName);
                        ElementId sys = TypeByName(typeof(Autodesk.Revit.DB.Plumbing.PipingSystemType), rec.SystemName);
                        if (type == null || sys == null) { why = "No pipe type or piping system type in project"; return null; }
                        created = Autodesk.Revit.DB.Plumbing.Pipe.Create(_doc, sys, type, lvl.Id, a, b);
                        break;
                    }
                case "Conduit":
                    {
                        ElementId type = TypeByName(typeof(Autodesk.Revit.DB.Electrical.ConduitType), rec.TypeName);
                        if (type == null) { why = "No conduit type in project"; return null; }
                        created = Autodesk.Revit.DB.Electrical.Conduit.Create(_doc, type, a, b, lvl.Id);
                        break;
                    }
                case "CableTray":
                    {
                        ElementId type = TypeByName(typeof(Autodesk.Revit.DB.Electrical.CableTrayType), rec.TypeName);
                        if (type == null) { why = "No cable tray type in project"; return null; }
                        created = Autodesk.Revit.DB.Electrical.CableTray.Create(_doc, type, a, b, lvl.Id);
                        break;
                    }
            }
            if (created == null) return null;
            ParamConv.Apply(created, rec.Params, _res.Resolve);   // size, offset, slope...
            return created.Id;
        }

        static double MaxDelta(XYZ a, double[] b)
        {
            return Math.Max(Math.Abs(a.X - b[0]), Math.Max(Math.Abs(a.Y - b[1]), Math.Abs(a.Z - b[2])));
        }

        static double Deviation(Element el, ElementRec rec)
        {
            if (rec.BBoxMin == null || rec.BBoxMax == null) return 0;
            BoundingBoxXYZ bb = el == null ? null : el.get_BoundingBox(null);
            if (bb == null) return double.MaxValue;
            return Math.Max(MaxDelta(bb.Min, rec.BBoxMin), MaxDelta(bb.Max, rec.BBoxMax));
        }
    }
}
