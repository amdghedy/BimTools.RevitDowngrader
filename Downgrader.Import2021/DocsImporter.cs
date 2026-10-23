using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Downgrader.Core.Schema;
using Downgrader.Core.Storage;
using Downgrader.Core.Util;
using Downgrader.RevitShared;

namespace Downgrader.Import
{
    /// <summary>Package B -> 2021 project. Run after the model import (it needs the id map for levels, groups and worksets).
    /// Every item is isolated in a sub-transaction; schedules always keep their CSV snapshot as the safety net.</summary>
    public class DocsImporter
    {
        readonly Application _app;
        readonly Document _doc;
        readonly PackagePaths _p;
        readonly DowngradeOptions _o;
        readonly ConversionReport _r;
        DocsPackage _d;
        IdMap _map;
        Resolver _res;
        readonly Dictionary<string, ElementId> _viewIds = new Dictionary<string, ElementId>();     // views + sheets
        readonly Dictionary<string, ElementId> _scheduleIds = new Dictionary<string, ElementId>();
        readonly Dictionary<string, ElementId> _revIds = new Dictionary<string, ElementId>();
        readonly HashSet<string> _viewNames = new HashSet<string>();

        public DocsImporter(Application app, Document doc, PackagePaths p, DowngradeOptions o, ConversionReport r)
        { _app = app; _doc = doc; _p = p; _o = o; _r = r; }

        public void Run()
        {
            _d = Json.Read<DocsPackage>(_p.DocsJson);
            _map = IdMap.LoadOrNew(_p);
            _res = new Resolver(_doc, _map);
            if (_map.Map.Count == 0)
                _r.Add("docs", Tiers.NativeWithLoss, "Order", "Model package not imported", null, "Run the model import first: levels, groups and worksets depend on its id map");

            if (_o.PreserveWorksets && _d.Worksets.Count > 0 && !_doc.IsWorkshared)
            {
                try { _doc.EnableWorksharing("Shared Levels and Grids", "Workset1"); }
                catch (Exception ex) { _r.Add("docs", Tiers.NativeWithLoss, "Worksets", "EnableWorksharing", null, ex.Message); }
            }

            foreach (Element v in new FilteredElementCollector(_doc).OfClass(typeof(View))) _viewNames.Add(v.Name);

            if (_o.DocsProjectInfo) RunPhase("project information", ImportProjectInfo);
            if (_o.DocsProjectParams) RunPhase("project parameters", ImportProjectParams);
            if (_o.DocsViews) RunPhase("views", ImportViews);
            if (_o.DocsSchedules) RunPhase("schedules", ImportSchedules);
            if (_o.DocsRevisions) RunPhase("revisions", ImportRevisions);
            if (_o.DocsSheets) RunPhase("sheets", ImportSheets);
            if (_o.DocsAnnotations) RunPhase("annotations", ImportAnnotations);
            if (_o.DocsTags) RunPhase("tags", ImportTags);
            if (_o.DocsGroups) RunPhase("groups", ImportGroups);
            if (_o.DocsLinks) RunPhase("links", ImportLinks);
            RunPhase("worksets", ImportWorksets);

            foreach (DesignOptionRec dop in _d.DesignOptions)
                _r.Add("docs", Tiers.NativeWithLoss, "DesignOption", dop.Name, null, dop.ElementCount + " elements were flattened into the main model (design options cannot be created through the API)");
            foreach (PhaseRec ph in _d.Phases)
                if (!new FilteredElementCollector(_doc).OfClass(typeof(Autodesk.Revit.DB.Phase)).Any(x => x.Name == ph.Name))
                    _r.Add("docs", Tiers.NativeWithLoss, "Phase", ph.Name, null, "Phase does not exist in this project and was not created; phase parameters pointing to it were skipped");
        }

        int _phase;

        void RunPhase(string name, Action a)
        {
            JobProgress.Current.Report("Docs: " + name, ++_phase, 11, true);
            using (Transaction t = Tx.Start(_doc, "Downgrade: " + name))
            {
                try { a(); t.Commit(); }
                catch (Exception ex) { t.RollBack(); _r.Add("docs", Tiers.Skipped, name, name, null, ex.Message); }
            }
        }

        /// <summary>Runs one item in a sub-transaction; returns the error text, or null on success.</summary>
        string Item(Action a)
        {
            using (var st = new SubTransaction(_doc))
            {
                st.Start();
                try { a(); st.Commit(); return null; }
                catch (Exception ex) { st.RollBack(); return ex.Message; }
            }
        }

        // ------------------------------------------------------------------ project parameters

        void ImportProjectParams()
        {
            var have = new HashSet<string>();
            DefinitionBindingMapIterator it = _doc.ParameterBindings.ForwardIterator();
            it.Reset();
            while (it.MoveNext()) have.Add(it.Key.Name);

            using (var shared = new SharedParamHelper(_app, System.IO.Path.Combine(_p.Root, "shared_params_2020.txt")))
            {
                foreach (ProjParamRec pp in _d.ProjectParams)
                {
                    if (have.Contains(pp.Name)) continue;
                    ProjParamRec cur = pp;
                    string err = Item(() =>
                    {
                        ParameterType pt;
                        if (!RevitHelpers.TryEnum(cur.Spec, out pt)) throw new InvalidOperationException("Parameter type " + cur.Spec + " unknown in 2021");
                        BuiltInParameterGroup grp;
                        if (!RevitHelpers.TryEnum(cur.Group, out grp)) grp = BuiltInParameterGroup.PG_DATA;
                        Guid g = cur.IsShared && cur.Guid != null ? new Guid(cur.Guid) : Guid.NewGuid();
                        ExternalDefinition ed = shared.GetOrCreate(cur.Name, g, pt);
                        CategorySet cs = _app.Create.NewCategorySet();
                        foreach (string cn in cur.Categories)
                        {
                            BuiltInCategory bic;
                            if (!RevitHelpers.TryEnum(cn, out bic)) continue;
                            Category c = _doc.Settings.Categories.get_Item(bic);
                            if (c != null && c.AllowsBoundParameters) cs.Insert(c);
                        }
                        if (cs.IsEmpty) throw new InvalidOperationException("No bindable categories left");
                        Binding b = cur.IsInstance ? (Binding)_app.Create.NewInstanceBinding(cs) : _app.Create.NewTypeBinding(cs);
                        _doc.ParameterBindings.Insert(ed, b, grp);
                    });
                    _r.Add("docs", err == null ? Tiers.Native : Tiers.Skipped, "ProjectParameter", pp.Name, null, err);
                }
            }
        }

        // ------------------------------------------------------------------ views

        ViewFamilyType Vft(ViewFamily vf)
        {
            return new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(t => t.ViewFamily == vf);
        }

        void ImportViews()
        {
            var plans = new FilteredElementCollector(_doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Where(v => !v.IsTemplate).ToList();
            var created = new List<KeyValuePair<ViewRec, View>>();
            foreach (ViewRec vr in _d.Views)
            {
                if (vr.SkipReason != null) { _r.Add("docs", Tiers.Skipped, "View", vr.Name, vr.Key, vr.SkipReason); continue; }
                ViewRec cur = vr;
                string err = Item(() =>
                {
                    View v = CreateOrReuse(cur, plans);
                    _viewIds[cur.Key] = v.Id;
                    created.Add(new KeyValuePair<ViewRec, View>(cur, v));
                    ApplySettings(v, cur);
                });
                bool tpl = vr.TemplateName != null;
                string tier = err != null ? Tiers.Skipped : (tpl && !_o.RebuildViewTemplates ? Tiers.NativeWithLoss : Tiers.Native);
                _r.Add("docs", tier, "View", vr.Name, vr.Key, err ?? (tpl
                    ? (_o.RebuildViewTemplates ? "Uses view template '" + vr.TemplateName + "' (rebuilt from the first view that used it)" : "View template '" + vr.TemplateName + "' not rebuilt; its effect was copied into the view")
                    : null));
            }
            if (_o.RebuildViewTemplates) RebuildTemplates(created);
        }

        View CreateOrReuse(ViewRec vr, List<ViewPlan> plans)
        {
            switch (vr.Kind)
            {
                case "FloorPlan":
                case "CeilingPlan":
                case "StructuralPlan":
                    {
                        ViewType vt = vr.Kind == "FloorPlan" ? ViewType.FloorPlan : vr.Kind == "CeilingPlan" ? ViewType.CeilingPlan : ViewType.EngineeringPlan;
                        ViewPlan ex = plans.FirstOrDefault(p => p.Name == vr.Name && p.ViewType == vt);
                        if (ex != null) return ex;
                        ElementId lid = _res.Level(vr.LevelKey);
                        if (lid == null) throw new InvalidOperationException("Level of the view was not imported");
                        ViewFamily vf = vr.Kind == "FloorPlan" ? ViewFamily.FloorPlan : vr.Kind == "CeilingPlan" ? ViewFamily.CeilingPlan : ViewFamily.StructuralPlan;
                        ViewPlan np = ViewPlan.Create(_doc, Vft(vf).Id, lid);
                        np.Name = RevitHelpers.UniqueName(vr.Name, _viewNames);
                        return np;
                    }
                case "Section":
                case "Detail":
                    {
                        BoundingBoxXYZ bb = BoxConv.FromRec(vr.Crop);
                        if (bb == null) throw new InvalidOperationException("No section box in package");
                        ViewSection s = vr.Kind == "Detail"
                            ? ViewSection.CreateDetail(_doc, Vft(ViewFamily.Detail).Id, bb)
                            : ViewSection.CreateSection(_doc, Vft(ViewFamily.Section).Id, bb);
                        s.Name = RevitHelpers.UniqueName(vr.Name, _viewNames);
                        return s;
                    }
                case "ThreeD":
                    {
                        ElementId id = Vft(ViewFamily.ThreeDimensional).Id;
                        View3D v3 = vr.Perspective ? View3D.CreatePerspective(_doc, id) : View3D.CreateIsometric(_doc, id);
                        if (vr.Eye != null) v3.SetOrientation(new ViewOrientation3D(RevitHelpers.ToXyz(vr.Eye), RevitHelpers.ToXyz(vr.Up), RevitHelpers.ToXyz(vr.Forward)));
                        v3.Name = RevitHelpers.UniqueName(vr.Name, _viewNames);
                        return v3;
                    }
                case "Drafting":
                    {
                        ViewDrafting d = ViewDrafting.Create(_doc, Vft(ViewFamily.Drafting).Id);
                        d.Name = RevitHelpers.UniqueName(vr.Name, _viewNames);
                        return d;
                    }
            }
            throw new InvalidOperationException("View kind " + vr.Kind + " not supported");
        }

        void ApplySettings(View v, ViewRec vr)
        {
            ParamConv.Apply(v, vr.Params, _res.Resolve);   // scale, detail level, discipline, phase, phase filter, title on sheet, ...
            DisplayStyle ds;
            if (RevitHelpers.TryEnum(vr.DisplayStyle, out ds)) { try { v.DisplayStyle = ds; } catch { } }

            var vp = v as ViewPlan;
            if (vp != null && vr.ViewRange.Count > 0)
            {
                try
                {
                    PlanViewRange pvr = vp.GetViewRange();
                    foreach (ViewRangeRec r in vr.ViewRange)
                    {
                        PlanViewPlane plane;
                        if (!RevitHelpers.TryEnum(r.Plane, out plane)) continue;
                        ElementId lid = r.Level.StartsWith("special:") ? new ElementId(int.Parse(r.Level.Substring(8))) : _res.Level(r.Level);
                        if (lid == null) continue;
                        pvr.SetLevelId(plane, lid);
                        pvr.SetOffset(plane, r.Offset);
                    }
                    vp.SetViewRange(pvr);
                }
                catch { }
            }
            if (vr.Crop != null && !(v is View3D) && !(v is ViewSection))
            {
                try { v.CropBox = BoxConv.FromRec(vr.Crop); v.CropBoxActive = vr.CropActive; } catch { }
            }
            foreach (string cn in vr.HiddenCategories)
            {
                try
                {
                    BuiltInCategory bic;
                    if (!RevitHelpers.TryEnum(cn, out bic)) continue;
                    Category c = _doc.Settings.Categories.get_Item(bic);
                    if (c != null && v.CanCategoryBeHidden(c.Id)) v.SetCategoryHidden(c.Id, true);
                }
                catch { }
            }
        }

        // ------------------------------------------------------------------ schedules

        void ImportSchedules()
        {
            foreach (ScheduleRec sr in _d.Schedules)
            {
                ScheduleRec cur = sr;
                var skipped = new List<string>();
                string err = Item(() =>
                {
                    ElementId cat = ElementId.InvalidElementId;
                    BuiltInCategory bic;
                    if (!string.IsNullOrEmpty(cur.Category) && RevitHelpers.TryEnum(cur.Category, out bic)) cat = new ElementId(bic);
                    ViewSchedule vs = cur.IsMaterialTakeoff ? ViewSchedule.CreateMaterialTakeoff(_doc, cat) : ViewSchedule.CreateSchedule(_doc, cat);
                    vs.Name = RevitHelpers.UniqueName(cur.Name, _viewNames);
                    ScheduleDefinition def = vs.Definition;
                    IList<SchedulableField> sfs = def.GetSchedulableFields();
                    var idx = new Dictionary<int, ScheduleFieldId>();

                    for (int i = 0; i < cur.Fields.Count; i++)
                    {
                        SchFieldRec fr = cur.Fields[i];
                        ScheduleFieldType ft;
                        if (!RevitHelpers.TryEnum(fr.FieldType, out ft)) { skipped.Add(fr.ParamName); continue; }
                        SchedulableField sf = sfs.FirstOrDefault(x => x.FieldType == ft && (ft == ScheduleFieldType.Count || x.GetName(_doc) == fr.ParamName));
                        if (sf == null) { skipped.Add(fr.ParamName); continue; }
                        ScheduleField f = def.AddField(sf);
                        if (!string.IsNullOrEmpty(fr.Heading)) f.ColumnHeading = fr.Heading;
                        f.IsHidden = fr.Hidden;
                        idx[i] = f.FieldId;
                    }
                    foreach (SchFilterRec fl in cur.Filters)
                    {
                        ScheduleFieldId fid; ScheduleFilterType ftype;
                        if (!idx.TryGetValue(fl.FieldIndex, out fid) || !RevitHelpers.TryEnum(fl.FilterType, out ftype)) continue;
                        try
                        {
                            ScheduleFilter flt = fl.ValueKind == "String" ? new ScheduleFilter(fid, ftype, fl.S ?? "")
                                : fl.ValueKind == "Double" ? new ScheduleFilter(fid, ftype, fl.D)
                                : new ScheduleFilter(fid, ftype);
                            def.AddFilter(flt);
                        }
                        catch { skipped.Add("filter on field " + fl.FieldIndex); }
                    }
                    foreach (SchSortRec so in cur.Sorts)
                    {
                        ScheduleFieldId fid; ScheduleSortOrder order;
                        if (!idx.TryGetValue(so.FieldIndex, out fid) || !RevitHelpers.TryEnum(so.SortOrder, out order)) continue;
                        var sg = new ScheduleSortGroupField(fid, order) { ShowHeader = so.ShowHeader, ShowFooter = so.ShowFooter, ShowBlankLine = so.ShowBlankLine };
                        def.AddSortGroupField(sg);
                    }
                    def.IsItemized = cur.Itemized;
                    def.ShowGrandTotal = cur.GrandTotal;
                    def.ShowTitle = cur.ShowTitle;
                    def.ShowHeaders = cur.ShowHeaders;
                    def.ShowGridLines = cur.ShowGridLines;
                    _scheduleIds[cur.Key] = vs.Id;
                });
                if (err != null) _r.Add("docs", Tiers.Skipped, "Schedule", sr.Name, sr.Key, err + " (CSV snapshot kept in " + sr.SnapshotCsv + ")");
                else if (skipped.Count > 0) _r.Add("docs", Tiers.NativeWithLoss, "Schedule", sr.Name, sr.Key, "Fields / filters not available in 2021: " + string.Join(", ", skipped) + " (see " + sr.SnapshotCsv + ")");
                else _r.Add("docs", Tiers.Native, "Schedule", sr.Name, sr.Key, null);
            }
        }

        // ------------------------------------------------------------------ sheets

        void ImportSheets()
        {
            var tbs = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
            var numbers = new HashSet<string>(new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber));
            foreach (SheetRec sr in _d.Sheets)
            {
                SheetRec cur = sr;
                string note = null;
                string err = Item(() =>
                {
                    ElementId tb = ElementId.InvalidElementId;
                    FamilySymbol sym = tbs.FirstOrDefault(x => x.FamilyName == cur.TitleBlockFamily && x.Name == cur.TitleBlockType)
                                       ?? tbs.FirstOrDefault(x => x.Name == cur.TitleBlockType);
                    if (sym != null) { if (!sym.IsActive) sym.Activate(); tb = sym.Id; }
                    else if (cur.TitleBlockType != null) note = "Title block '" + cur.TitleBlockType + "' is not loaded in this project: sheet created without one";

                    ViewSheet sheet = ViewSheet.Create(_doc, tb);
                    sheet.SheetNumber = RevitHelpers.UniqueName(cur.Number, numbers);
                    sheet.Name = cur.Name;
                    ParamConv.Apply(sheet, cur.Params, _res.Resolve);
                    _viewIds[cur.Key] = sheet.Id;
                    var rids = cur.RevisionKeys.Where(k => _revIds.ContainsKey(k)).Select(k => _revIds[k]).ToList();
                    if (rids.Count > 0) sheet.SetAdditionalRevisionIds(rids);

                    foreach (ViewportRec vp in cur.Viewports)
                    {
                        ElementId vid;
                        if (!_viewIds.TryGetValue(vp.ViewKey, out vid)) { note = (note == null ? "" : note + "; ") + "a viewport's view was not imported"; continue; }
                        if (!Viewport.CanAddViewToSheet(_doc, sheet.Id, vid)) continue;
                        Viewport.Create(_doc, sheet.Id, vid, RevitHelpers.ToXyz(vp.Center));
                    }
                    foreach (SheetScheduleRec ss in cur.ScheduleInstances)
                    {
                        ElementId sid;
                        if (_scheduleIds.TryGetValue(ss.ScheduleKey, out sid)) ScheduleSheetInstance.Create(_doc, sheet.Id, sid, RevitHelpers.ToXyz(ss.Point));
                    }
                });
                _r.Add("docs", err != null ? Tiers.Skipped : (note != null ? Tiers.NativeWithLoss : Tiers.Native), "Sheet", sr.Number + " " + sr.Name, sr.Key, err ?? note);
            }
        }

        // ------------------------------------------------------------------ annotations

        void ImportAnnotations()
        {
            ElementId defText = _doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            var textTypes = new Dictionary<string, ElementId>();
            foreach (Element t in new FilteredElementCollector(_doc).OfClass(typeof(TextNoteType))) if (!textTypes.ContainsKey(t.Name)) textTypes[t.Name] = t.Id;
            var styles = new Dictionary<string, GraphicsStyle>();
            foreach (GraphicsStyle g in new FilteredElementCollector(_doc).OfClass(typeof(GraphicsStyle)).Cast<GraphicsStyle>().Where(x => x.GraphicsStyleType == GraphicsStyleType.Projection))
                if (!styles.ContainsKey(g.Name)) styles[g.Name] = g;
            var regionTypes = new FilteredElementCollector(_doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();

            int ok = 0, fail = 0;
            foreach (TextNoteRec tn in _d.TextNotes)
            {
                ElementId vid; TextNoteRec c = tn;
                if (!_viewIds.TryGetValue(tn.OwnerKey, out vid)) continue;
                ElementId tid; if (tn.TypeName == null || !textTypes.TryGetValue(tn.TypeName, out tid)) tid = defText;
                string err = Item(() =>
                {
                    TextNote n = TextNote.Create(_doc, vid, RevitHelpers.ToXyz(c.Point), c.Text ?? "", tid);
                    if (c.Width > 0) { try { n.Width = c.Width; } catch { } }
                });
                if (err == null) ok++; else fail++;
            }
            foreach (DetailLineRec dl in _d.DetailLines)
            {
                ElementId vid; DetailLineRec c = dl;
                if (!_viewIds.TryGetValue(dl.OwnerKey, out vid)) continue;
                string err = Item(() =>
                {
                    var view = (View)_doc.GetElement(vid);
                    foreach (Curve k in CurveConv.ToCurves(c.Curve))
                    {
                        DetailCurve dc = _doc.Create.NewDetailCurve(view, k);
                        GraphicsStyle gs;
                        if (c.StyleName != null && styles.TryGetValue(c.StyleName, out gs)) dc.LineStyle = gs;
                    }
                });
                if (err == null) ok++; else fail++;
            }
            foreach (FilledRegionRec fr in _d.FilledRegions)
            {
                ElementId vid; FilledRegionRec c = fr;
                if (!_viewIds.TryGetValue(fr.OwnerKey, out vid)) continue;
                string err = Item(() =>
                {
                    FilledRegionType rt = regionTypes.FirstOrDefault(x => x.Name == c.TypeName) ?? regionTypes.First();
                    var loops = new List<CurveLoop>();
                    foreach (List<CurveRec> l in c.Loops)
                    {
                        var curves = new List<Curve>();
                        foreach (CurveRec cr in l) curves.AddRange(CurveConv.ToCurves(cr));
                        loops.Add(CurveLoop.Create(curves));
                    }
                    FilledRegion.Create(_doc, rt.Id, vid, loops);
                });
                if (err == null) ok++; else fail++;
            }
            _r.Add("docs", fail == 0 ? Tiers.Native : Tiers.NativeWithLoss, "Annotations", "Text notes, detail lines, filled regions", null, fail == 0 ? null : fail + " of " + (ok + fail) + " annotations could not be recreated");
        }

        // ------------------------------------------------------------------ groups, worksets

        void ImportGroups()
        {
            var names = new HashSet<string>(new FilteredElementCollector(_doc).OfClass(typeof(GroupType)).Select(g => g.Name));
            foreach (GroupRec gr in _d.Groups)
            {
                GroupRec cur = gr;
                var ids = new List<ElementId>();
                foreach (string k in gr.MemberKeys) { int v; if (_map.Map.TryGetValue(k, out v)) ids.Add(new ElementId(v)); }
                if (ids.Count == 0) continue;
                string err = Item(() =>
                {
                    Group g = _doc.Create.NewGroup(ids);
                    g.GroupType.Name = RevitHelpers.UniqueName(cur.Name, names);
                });
                _r.Add("docs", err == null ? (ids.Count == gr.MemberKeys.Count ? Tiers.Native : Tiers.NativeWithLoss) : Tiers.Skipped, "Group", gr.Name, gr.Key,
                    err ?? (ids.Count == gr.MemberKeys.Count ? null : (gr.MemberKeys.Count - ids.Count) + " members were not imported; each instance becomes its own group type"));
            }
        }

        void RebuildTemplates(List<KeyValuePair<ViewRec, View>> created)
        {
            foreach (var grp in created.Where(x => x.Key.TemplateName != null).GroupBy(x => x.Key.TemplateName))
            {
                string name = grp.Key;
                var members = grp.ToList();
                int assigned = 0;
                string err = Item(() =>
                {
                    View tpl = members[0].Value.CreateViewTemplate();   // template = settings of the first view that used it
                    tpl.Name = RevitHelpers.UniqueName(name, _viewNames);
                    foreach (var kv in members)
                    {
                        try { kv.Value.ViewTemplateId = tpl.Id; assigned++; } catch { }   // wrong view type for this template: skipped
                    }
                });
                _r.Add("docs", err == null ? Tiers.Native : Tiers.NativeWithLoss, "ViewTemplate", name, null, err ?? assigned + " of " + members.Count + " views re-assigned");
            }
        }

        void ImportProjectInfo()
        {
            int n = ParamConv.Apply(_doc.ProjectInformation, _d.ProjectInfo, _res.Resolve);
            _r.Add("docs", Tiers.Native, "ProjectInformation", "Project information", null, n + " fields set");
        }

        void ImportRevisions()
        {
            var existing = new FilteredElementCollector(_doc).OfClass(typeof(Revision)).Cast<Revision>().ToList();
            foreach (RevisionRec rr in _d.Revisions)
            {
                RevisionRec cur = rr;
                string err = Item(() =>
                {
                    Revision rev = existing.FirstOrDefault(x => x.Description == cur.Description && x.RevisionDate == cur.Date);
                    if (rev == null)
                    {
                        rev = Revision.Create(_doc);
                        rev.Description = cur.Description ?? "";
                        rev.RevisionDate = cur.Date ?? "";
                        rev.IssuedBy = cur.IssuedBy ?? "";
                        rev.IssuedTo = cur.IssuedTo ?? "";
                        RevisionNumberType nt; if (RevitHelpers.TryEnum(cur.NumberType, out nt)) rev.NumberType = nt;
                        RevisionVisibility vis; if (RevitHelpers.TryEnum(cur.Visibility, out vis)) rev.Visibility = vis;
                        rev.Issued = cur.Issued;   // last: issued revisions are locked
                    }
                    _revIds[cur.Key] = rev.Id;
                });
                _r.Add("docs", err == null ? Tiers.Native : Tiers.Skipped, "Revision", rr.Description, rr.Key, err);
            }
        }

        void ImportTags()
        {
            var tagTypes = new Dictionary<string, ElementId>();
            foreach (FamilySymbol s in new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
                if (s.Category != null && s.Category.CategoryType == CategoryType.Annotation)
                    tagTypes[s.FamilyName + "|" + s.Name] = s.Id;
            int ok = 0, fail = 0;
            foreach (TagRec tr in _d.Tags)
            {
                ElementId vid; int eid;
                if (!_viewIds.TryGetValue(tr.OwnerKey, out vid) || !_map.Map.TryGetValue(tr.TaggedKey, out eid)) { fail++; continue; }
                TagRec cur = tr;
                string err = Item(() =>
                {
                    Element tagged = _doc.GetElement(new ElementId(eid));
                    IndependentTag tag = IndependentTag.Create(_doc, vid, new Reference(tagged), cur.Leader, TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, RevitHelpers.ToXyz(cur.Head));
                    ElementId tid;
                    if (cur.FamilyName != null && tagTypes.TryGetValue(cur.FamilyName + "|" + cur.TypeName, out tid)) tag.ChangeTypeId(tid);
                });
                if (err == null) ok++; else fail++;
            }
            _r.Add("docs", fail == 0 ? Tiers.Native : Tiers.NativeWithLoss, "Tags", ok + " tags", null, fail == 0 ? null : fail + " tags not recreated (tagged element or view missing, or no tag family for the category loaded; the default tag type is used when the original tag family is missing)");
        }

        void ImportLinks()
        {
            string folder = _o.LinkSearchFolder;
            foreach (LinkRec lr in _d.Links)
            {
                LinkRec cur = lr;
                if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder))
                { _r.Add("docs", Tiers.NativeWithLoss, "Link", lr.Name, null, "Not re-linked: set LinkSearchFolder to the folder with the downgraded linked files"); continue; }
                string file = System.IO.Directory.GetFiles(folder, lr.Name + "*.rvt", System.IO.SearchOption.AllDirectories)
                    .FirstOrDefault(f => !System.Text.RegularExpressions.Regex.IsMatch(f, @"\.\d{4}\.rvt$"));
                if (file == null) { _r.Add("docs", Tiers.NativeWithLoss, "Link", lr.Name, null, "Downgraded file '" + lr.Name + "*.rvt' not found in LinkSearchFolder"); continue; }
                string linkFile = file;
                string err = Item(() =>
                {
                    LinkLoadResult res = RevitLinkType.Create(_doc, ModelPathUtils.ConvertUserVisiblePathToModelPath(linkFile), new RevitLinkOptions(false));
                    if (res.LoadResult != LinkLoadResultType.LinkLoaded) throw new InvalidOperationException("Link load result: " + res.LoadResult);
                    RevitLinkInstance inst = RevitLinkInstance.Create(_doc, res.ElementId);
                    if (Math.Abs(cur.Rotation) > 1e-9) ElementTransformUtils.RotateElement(_doc, inst.Id, Line.CreateBound(XYZ.Zero, XYZ.BasisZ), cur.Rotation);
                    ElementTransformUtils.MoveElement(_doc, inst.Id, RevitHelpers.ToXyz(cur.Origin));
                });
                _r.Add("docs", err == null ? Tiers.Native : Tiers.Skipped, "Link", lr.Name, null, err);
            }
        }

        void ImportWorksets()
        {
            if (!_o.PreserveWorksets || _d.Worksets.Count == 0) return;
            if (!_doc.IsWorkshared) { _r.Add("docs", Tiers.NativeWithLoss, "Worksets", "Worksets", null, "Worksharing could not be enabled; worksets were not recreated"); return; }
            var ids = new Dictionary<string, WorksetId>();
            foreach (Workset w in new FilteredWorksetCollector(_doc).OfKind(WorksetKind.UserWorkset)) ids[w.Name] = w.Id;
            foreach (string n in _d.Worksets)
                if (!ids.ContainsKey(n)) ids[n] = Autodesk.Revit.DB.Workset.Create(_doc, n).Id;
            int assigned = 0;
            foreach (ElementTagRec t in _d.ElementTags.Where(x => x.Workset != null))
            {
                int v; WorksetId wid;
                if (!_map.Map.TryGetValue(t.Key, out v) || !ids.TryGetValue(t.Workset, out wid)) continue;
                Element e = _doc.GetElement(new ElementId(v));
                Parameter p = e == null ? null : e.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (p != null && !p.IsReadOnly && p.Set(wid.IntegerValue)) assigned++;
            }
            _r.Add("docs", Tiers.Native, "Worksets", _d.Worksets.Count + " worksets", null, assigned + " elements assigned");
        }
    }
}
