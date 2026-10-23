using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Downgrader.Core.Schema;
using Downgrader.Core.Storage;
using Downgrader.Core.Util;
using Downgrader.RevitShared;

namespace Downgrader.Export
{
    /// <summary>Package B: everything that is not model geometry. Each step is isolated so one failing feature never loses the rest.</summary>
    public class DocsExporter
    {
        readonly Document _doc;
        readonly PackagePaths _p;
        readonly DowngradeOptions _o;
        readonly ConversionReport _r;
        readonly DocsPackage _d = new DocsPackage();
        readonly List<View> _views = new List<View>();
        readonly List<ViewSheet> _sheets = new List<ViewSheet>();
        readonly HashSet<string> _scheduleKeys = new HashSet<string>();
        readonly Dictionary<string, ElementTagRec> _tags = new Dictionary<string, ElementTagRec>();

        public DocsExporter(Document doc, PackagePaths p, DowngradeOptions o, ConversionReport r)
        { _doc = doc; _p = p; _o = o; _r = r; }

        public void Run(PackageManifest manifest)
        {
            Step("phases", ExportPhases);
            if (_o.DocsProjectInfo) Step("project information", () => { _d.ProjectInfo = ParamConv.Read(_doc.ProjectInformation); });
            if (_o.DocsRevisions) Step("revisions", ExportRevisions);
            if (_o.DocsProjectParams) Step("project parameters", ExportProjectParams);
            if (_o.DocsViews) Step("views", ExportViews);
            if (_o.DocsSchedules) Step("schedules", ExportSchedules);
            if (_o.DocsSheets) Step("sheets", ExportSheets);
            if (_o.DocsAnnotations) Step("annotations", ExportAnnotations);
            if (_o.DocsGroups) Step("groups", ExportGroups);
            if (_o.DocsLinks) Step("links", ExportLinks);
            if (_o.PreserveWorksets) Step("worksets", ExportWorksets);
            Step("design options", ExportDesignOptions);
            _d.ElementTags = _tags.Values.ToList();
            Json.Write(_p.DocsJson, _d, true);
            manifest.Counts["docs.views"] = _d.Views.Count;
            manifest.Counts["docs.sheets"] = _d.Sheets.Count;
            manifest.Counts["docs.schedules"] = _d.Schedules.Count;
        }

        int _step;

        void Step(string name, Action a)
        {
            JobProgress.Current.Report("Docs: " + name, ++_step, 12, true);
            try { a(); }
            catch (Exception ex) { _r.Add("export-docs", Tiers.Skipped, name, name, null, ex.Message); }
        }

        ElementTagRec Tag(string key)
        {
            ElementTagRec t;
            if (!_tags.TryGetValue(key, out t)) { t = new ElementTagRec { Key = key }; _tags[key] = t; }
            return t;
        }

        /// <summary>Reads a property by name; "" when this Revit version does not have it (e.g. Revision.NumberType in newer releases).</summary>
        static string PropText(object o, string name)
        {
            var p = o.GetType().GetProperty(name);
            object v = p == null ? null : p.GetValue(o, null);
            return v == null ? "" : v.ToString();
        }

        void ExportRevisions()
        {
            foreach (Revision rv in new FilteredElementCollector(_doc).OfClass(typeof(Revision)).Cast<Revision>().OrderBy(r => r.SequenceNumber))
                _d.Revisions.Add(new RevisionRec
                {
                    Key = rv.UniqueId, Sequence = rv.SequenceNumber, Description = rv.Description, Date = rv.RevisionDate,
                    IssuedBy = rv.IssuedBy, IssuedTo = rv.IssuedTo, Issued = rv.Issued,
                    NumberType = PropText(rv, "NumberType"), Visibility = PropText(rv, "Visibility")
                });
        }

        void ExportTags(View v, string ownerKey)
        {
            foreach (IndependentTag tg in new FilteredElementCollector(_doc, v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
            {
                try
                {
                    Element tagged = Compat.TaggedElement(tg, _doc);
                    if (tagged == null) continue;
                    var tt = _doc.GetElement(tg.GetTypeId()) as ElementType;
                    _d.Tags.Add(new TagRec
                    {
                        OwnerKey = ownerKey, TaggedKey = tagged.UniqueId, Head = RevitHelpers.Pt(tg.TagHeadPosition), Leader = PropText(tg, "HasLeader") == "True",
                        FamilyName = tt != null ? tt.FamilyName : null, TypeName = tt != null ? tt.Name : null
                    });
                }
                catch { }
            }
        }

        void ExportLinks()
        {
            foreach (RevitLinkInstance li in new FilteredElementCollector(_doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                var lt = _doc.GetElement(li.GetTypeId()) as RevitLinkType;
                if (lt == null) continue;
                Transform tf = li.GetTotalTransform();
                string name = Path.GetFileNameWithoutExtension(lt.Name);
                _d.Links.Add(new LinkRec { Name = name, Origin = RevitHelpers.Pt(tf.Origin), Rotation = Math.Atan2(tf.BasisX.Y, tf.BasisX.X) });
                _r.Add("export-docs", Tiers.NativeWithLoss, "Link", name, li.UniqueId, "Re-linked on import only if the downgraded linked file is found in LinkSearchFolder");
            }
        }

        void ExportPhases()
        {
            foreach (Phase ph in _doc.Phases) _d.Phases.Add(new PhaseRec { Name = ph.Name });
        }

        void ExportProjectParams()
        {
            var shared = new Dictionary<string, Guid>();
            foreach (SharedParameterElement spe in new FilteredElementCollector(_doc).OfClass(typeof(SharedParameterElement)).Cast<SharedParameterElement>())
                shared[spe.Name] = spe.GuidValue;
            DefinitionBindingMapIterator it = _doc.ParameterBindings.ForwardIterator();
            it.Reset();
            while (it.MoveNext())
            {
                Definition def = it.Key;
                var b = it.Current as ElementBinding;
                if (b == null) continue;
                var pp = new ProjParamRec { Name = def.Name, Spec = Compat.SpecName(def), Group = Compat.GroupName(def), IsInstance = b is InstanceBinding };
                Guid g;
                if (shared.TryGetValue(def.Name, out g)) { pp.IsShared = true; pp.Guid = g.ToString(); }
                foreach (Category c in b.Categories)
                {
                    string n = RevitHelpers.CategoryName(c);
                    if (n.Length > 0) pp.Categories.Add(n);
                }
                _d.ProjectParams.Add(pp);
            }
        }

        static string KindOf(ViewType t)
        {
            switch (t)
            {
                case ViewType.FloorPlan: return "FloorPlan";
                case ViewType.CeilingPlan: return "CeilingPlan";
                case ViewType.EngineeringPlan: return "StructuralPlan";
                case ViewType.Elevation: return "Section";     // re-created as a section view (elevation markers cannot be created from a box)
                case ViewType.Section: return "Section";
                case ViewType.Detail: return "Detail";
                case ViewType.ThreeD: return "ThreeD";
                case ViewType.DraftingView: return "Drafting";
                case ViewType.Legend: return "Legend";
                default: return null;
            }
        }

        void ExportViews()
        {
            foreach (View v in new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>())
            {
                try
                {
                    if (v.IsTemplate) continue;
                    string kind = KindOf(v.ViewType);
                    if (kind == null) continue;
                    if (v.GetPrimaryViewId() != ElementId.InvalidElementId)
                    {
                        _r.Add("export-docs", Tiers.Skipped, "View", v.Name, v.UniqueId, "Dependent view");
                        continue;
                    }
                    var rec = new ViewRec { Key = v.UniqueId, Name = v.Name, ViewType = v.ViewType.ToString(), Kind = kind };
                    if (kind == "Legend") rec.SkipReason = "Legends cannot be created through the API";
                    try { rec.Scale = v.Scale; } catch { }
                    try { rec.DisplayStyle = v.DisplayStyle.ToString(); } catch { }
                    var vp = v as ViewPlan;
                    if (vp != null)
                    {
                        if (vp.GenLevel != null) rec.LevelKey = vp.GenLevel.UniqueId;
                        PlanViewRange vr = vp.GetViewRange();
                        foreach (PlanViewPlane plane in new[] { PlanViewPlane.TopClipPlane, PlanViewPlane.CutPlane, PlanViewPlane.BottomClipPlane, PlanViewPlane.ViewDepthPlane })
                        {
                            ElementId lid = vr.GetLevelId(plane);
                            var lv = _doc.GetElement(lid) as Level;
                            rec.ViewRange.Add(new ViewRangeRec { Plane = plane.ToString(), Level = lv != null ? lv.UniqueId : "special:" + Compat.IdValue(lid), Offset = vr.GetOffset(plane) });
                        }
                    }
                    var v3 = v as View3D;
                    if (v3 != null)
                    {
                        rec.Perspective = v3.IsPerspective;
                        try
                        {
                            ViewOrientation3D o = v3.GetOrientation();
                            rec.Eye = RevitHelpers.Pt(o.EyePosition); rec.Up = RevitHelpers.Pt(o.UpDirection); rec.Forward = RevitHelpers.Pt(o.ForwardDirection);
                        }
                        catch { rec.SkipReason = "Locked or unreadable 3D orientation"; }
                    }
                    else if (kind != "Drafting" && kind != "Legend")
                    {
                        try { rec.CropActive = v.CropBoxActive; rec.Crop = BoxConv.ToRec(v.CropBox); } catch { }
                    }
                    if (v.ViewTemplateId != ElementId.InvalidElementId)
                    {
                        var tpl = _doc.GetElement(v.ViewTemplateId);
                        if (tpl != null) rec.TemplateName = tpl.Name;
                    }
                    foreach (Category c in _doc.Settings.Categories)
                    {
                        try
                        {
                            if (!c.get_AllowsVisibilityControl(v) || !v.GetCategoryHidden(c.Id)) continue;
                            string n = RevitHelpers.CategoryName(c);
                            if (n.Length > 0) rec.HiddenCategories.Add(n);
                        }
                        catch { }
                    }
                    rec.Params = ParamConv.Read(v);
                    _d.Views.Add(rec);
                    _views.Add(v);
                    string note = kind == "Legend" ? rec.SkipReason : (rec.TemplateName != null && !_o.RebuildViewTemplates ? "View template '" + rec.TemplateName + "' is not rebuilt (settings were copied from the view)" : null);
                    _r.Add("export-docs", note == null ? Tiers.Native : Tiers.NativeWithLoss, "View", v.Name, v.UniqueId, note);
                }
                catch (Exception ex) { _r.Add("export-docs", Tiers.Skipped, "View", v.Name, v.UniqueId, ex.Message); }
            }
        }

        void ExportSchedules()
        {
            Directory.CreateDirectory(_p.SchedulesDir);
            var usedFiles = new HashSet<string>();
            foreach (ViewSchedule vs in new FilteredElementCollector(_doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>())
            {
                try
                {
                    if (vs.IsTemplate || vs.IsTitleblockRevisionSchedule || vs.IsInternalKeynoteSchedule) continue;
                    ScheduleDefinition def = vs.Definition;
                    var rec = new ScheduleRec
                    {
                        Key = vs.UniqueId, Name = vs.Name, IsMaterialTakeoff = def.IsMaterialTakeoff, Itemized = def.IsItemized,
                        GrandTotal = def.ShowGrandTotal, ShowTitle = def.ShowTitle, ShowHeaders = def.ShowHeaders, ShowGridLines = def.ShowGridLines
                    };
                    if (def.CategoryId != ElementId.InvalidElementId) rec.Category = RevitHelpers.CategoryName(Category.GetCategory(_doc, def.CategoryId));
                    IList<ScheduleFieldId> order = def.GetFieldOrder();
                    foreach (ScheduleFieldId fid in order)
                    {
                        ScheduleField f = def.GetField(fid);
                        rec.Fields.Add(new SchFieldRec { ParamName = f.GetName(), Heading = f.ColumnHeading, FieldType = f.FieldType.ToString(), Hidden = f.IsHidden });
                    }
                    for (int i = 0; i < def.GetFilterCount(); i++)
                    {
                        ScheduleFilter flt = def.GetFilter(i);
                        var fr = new SchFilterRec { FieldIndex = order.IndexOf(flt.FieldId), FilterType = flt.FilterType.ToString() };
                        try { fr.S = flt.GetStringValue(); fr.ValueKind = "String"; }
                        catch { try { fr.D = flt.GetDoubleValue(); fr.ValueKind = "Double"; } catch { fr.ValueKind = "Other"; } }
                        rec.Filters.Add(fr);
                    }
                    for (int i = 0; i < def.GetSortGroupFieldCount(); i++)
                    {
                        ScheduleSortGroupField sg = def.GetSortGroupField(i);
                        rec.Sorts.Add(new SchSortRec { FieldIndex = order.IndexOf(sg.FieldId), SortOrder = sg.SortOrder.ToString(), ShowHeader = sg.ShowHeader, ShowFooter = sg.ShowFooter, ShowBlankLine = sg.ShowBlankLine });
                    }
                    string file = RevitHelpers.UniqueName(RevitHelpers.SafeFileName(vs.Name), usedFiles) + ".csv";
                    WriteCsv(vs, Path.Combine(_p.SchedulesDir, file));
                    rec.SnapshotCsv = "schedules/" + file;
                    _d.Schedules.Add(rec);
                    _scheduleKeys.Add(vs.UniqueId);
                    _r.Add("export-docs", Tiers.Native, "Schedule", vs.Name, vs.UniqueId, null);
                }
                catch (Exception ex) { _r.Add("export-docs", Tiers.Skipped, "Schedule", vs.Name, vs.UniqueId, ex.Message); }
            }
        }

        static void WriteCsv(ViewSchedule vs, string path)
        {
            var sb = new StringBuilder();
            TableData td = vs.GetTableData();
            foreach (SectionType st in new[] { SectionType.Header, SectionType.Body })
            {
                TableSectionData sd = td.GetSectionData(st);
                for (int r = 0; r < sd.NumberOfRows; r++)
                {
                    for (int c = 0; c < sd.NumberOfColumns; c++)
                    {
                        if (c > 0) sb.Append(',');
                        sb.Append('"').Append((vs.GetCellText(st, r, c) ?? "").Replace("\"", "\"\"")).Append('"');
                    }
                    sb.AppendLine();
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        void ExportSheets()
        {
            foreach (ViewSheet sh in new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
            {
                try
                {
                    var rec = new SheetRec { Key = sh.UniqueId, Number = sh.SheetNumber, Name = sh.Name, Params = ParamConv.Read(sh) };
                    foreach (ElementId rid in sh.GetAdditionalRevisionIds())
                    {
                        Element rv = _doc.GetElement(rid);
                        if (rv != null) rec.RevisionKeys.Add(rv.UniqueId);
                    }
                    var tb = new FilteredElementCollector(_doc, sh.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().OfType<FamilyInstance>().FirstOrDefault();
                    if (tb != null) { rec.TitleBlockFamily = tb.Symbol.FamilyName; rec.TitleBlockType = tb.Symbol.Name; }
                    foreach (ElementId vid in sh.GetAllViewports())
                    {
                        var vp = _doc.GetElement(vid) as Viewport;
                        if (vp == null) continue;
                        var view = _doc.GetElement(vp.ViewId);
                        var vt = _doc.GetElement(vp.GetTypeId());
                        if (view != null) rec.Viewports.Add(new ViewportRec { ViewKey = view.UniqueId, Center = RevitHelpers.Pt(vp.GetBoxCenter()), TypeName = vt != null ? vt.Name : null });
                    }
                    foreach (ScheduleSheetInstance ssi in new FilteredElementCollector(_doc, sh.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
                    {
                        var s = _doc.GetElement(ssi.ScheduleId);
                        if (s != null && _scheduleKeys.Contains(s.UniqueId)) rec.ScheduleInstances.Add(new SheetScheduleRec { ScheduleKey = s.UniqueId, Point = RevitHelpers.Pt(ssi.Point) });
                    }
                    _d.Sheets.Add(rec);
                    _sheets.Add(sh);
                    _r.Add("export-docs", tb != null ? Tiers.Native : Tiers.NativeWithLoss, "Sheet", sh.SheetNumber + " " + sh.Name, sh.UniqueId, tb != null ? null : "No title block found on sheet");
                }
                catch (Exception ex) { _r.Add("export-docs", Tiers.Skipped, "Sheet", sh.Name, sh.UniqueId, ex.Message); }
            }
        }

        void ExportAnnotations()
        {
            var owners = new List<View>(_views);
            owners.AddRange(_sheets.Cast<View>());
            foreach (View v in owners)
            {
                string key = v.UniqueId;
                foreach (TextNote tn in new FilteredElementCollector(_doc, v.Id).OfClass(typeof(TextNote)).Cast<TextNote>())
                {
                    var tt = _doc.GetElement(tn.GetTypeId());
                    _d.TextNotes.Add(new TextNoteRec { OwnerKey = key, Text = tn.Text, Point = RevitHelpers.Pt(tn.Coord), TypeName = tt != null ? tt.Name : null, Width = tn.Width });
                }
                foreach (CurveElement ce in new FilteredElementCollector(_doc, v.Id).OfClass(typeof(CurveElement)).Cast<CurveElement>())
                {
                    var dc = ce as DetailCurve;
                    if (dc == null) continue;
                    _d.DetailLines.Add(new DetailLineRec { OwnerKey = key, Curve = CurveConv.ToRec(dc.GeometryCurve), StyleName = dc.LineStyle != null ? dc.LineStyle.Name : null });
                }
                foreach (FilledRegion fr in new FilteredElementCollector(_doc, v.Id).OfClass(typeof(FilledRegion)).Cast<FilledRegion>())
                {
                    var rec = new FilledRegionRec { OwnerKey = key };
                    var ft = _doc.GetElement(fr.GetTypeId());
                    rec.TypeName = ft != null ? ft.Name : null;
                    foreach (CurveLoop cl in fr.GetBoundaries())
                    {
                        var loop = new List<CurveRec>();
                        foreach (Curve c in cl) loop.Add(CurveConv.ToRec(c));
                        rec.Loops.Add(loop);
                    }
                    _d.FilledRegions.Add(rec);
                }
                if (_o.DocsTags) ExportTags(v, key);
            }
            _r.Add("export-docs", Tiers.NativeWithLoss, "Annotations", "Dimensions, keynotes, revision clouds", null, "Text notes, detail lines, filled regions and tags are carried over; dimensions, keynotes and revision clouds must be re-created in 2020");
        }

        void ExportGroups()
        {
            foreach (Group g in new FilteredElementCollector(_doc).OfClass(typeof(Group)).Cast<Group>())
            {
                if (g.ViewSpecific) continue;
                var rec = new GroupRec { Key = g.UniqueId, Name = g.GroupType != null ? g.GroupType.Name : g.Name };
                foreach (ElementId id in g.GetMemberIds())
                {
                    Element m = _doc.GetElement(id);
                    if (m != null) rec.MemberKeys.Add(m.UniqueId);
                }
                _d.Groups.Add(rec);
            }
        }

        void ExportWorksets()
        {
            if (!_doc.IsWorkshared) return;
            foreach (Workset ws in new FilteredWorksetCollector(_doc).OfKind(WorksetKind.UserWorkset))
            {
                _d.Worksets.Add(ws.Name);
                foreach (Element e in new FilteredElementCollector(_doc).WherePasses(new ElementWorksetFilter(ws.Id)))
                    Tag(e.UniqueId).Workset = ws.Name;
            }
        }

        void ExportDesignOptions()
        {
            foreach (DesignOption dop in new FilteredElementCollector(_doc).OfClass(typeof(DesignOption)).Cast<DesignOption>())
            {
                int n = 0;
                foreach (Element e in new FilteredElementCollector(_doc).WherePasses(new ElementDesignOptionFilter(dop.Id)))
                { Tag(e.UniqueId).DesignOption = dop.Name; n++; }
                _d.DesignOptions.Add(new DesignOptionRec { Name = dop.Name, IsPrimary = dop.IsPrimary, ElementCount = n });
                _r.Add("export-docs", Tiers.NativeWithLoss, "DesignOption", dop.Name, dop.UniqueId, "Design options cannot be created through the API; elements are flattened into the main model and tagged");
            }
        }
    }
}
