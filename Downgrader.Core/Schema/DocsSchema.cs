using System.Collections.Generic;

namespace Downgrader.Core.Schema
{
    /// <summary>Package B: docs.json (+ schedules/*.csv snapshots).</summary>
    public class DocsPackage
    {
        public List<PhaseRec> Phases { get; set; } = new List<PhaseRec>();
        public List<ProjParamRec> ProjectParams { get; set; } = new List<ProjParamRec>();
        public List<ViewRec> Views { get; set; } = new List<ViewRec>();
        public List<ScheduleRec> Schedules { get; set; } = new List<ScheduleRec>();
        public List<SheetRec> Sheets { get; set; } = new List<SheetRec>();
        public List<TextNoteRec> TextNotes { get; set; } = new List<TextNoteRec>();
        public List<DetailLineRec> DetailLines { get; set; } = new List<DetailLineRec>();
        public List<FilledRegionRec> FilledRegions { get; set; } = new List<FilledRegionRec>();
        public List<GroupRec> Groups { get; set; } = new List<GroupRec>();
        public List<ElementTagRec> ElementTags { get; set; } = new List<ElementTagRec>();
        public List<string> Worksets { get; set; } = new List<string>();
        public List<DesignOptionRec> DesignOptions { get; set; } = new List<DesignOptionRec>();
        public List<LinkRec> Links { get; set; } = new List<LinkRec>();
        public List<TagRec> Tags { get; set; } = new List<TagRec>();
        public List<RevisionRec> Revisions { get; set; } = new List<RevisionRec>();
        public List<ParamRec> ProjectInfo { get; set; } = new List<ParamRec>();
    }

    public class TagRec
    {
        public string OwnerKey { get; set; }      // view UniqueId
        public string TaggedKey { get; set; }     // tagged element UniqueId (resolved through the id map)
        public double[] Head { get; set; }
        public bool Leader { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
    }

    public class RevisionRec
    {
        public string Key { get; set; }
        public int Sequence { get; set; }
        public string Description { get; set; }
        public string Date { get; set; }
        public string IssuedBy { get; set; }
        public string IssuedTo { get; set; }
        public bool Issued { get; set; }
        public string NumberType { get; set; }
        public string Visibility { get; set; }
    }

    public class LinkRec
    {
        public string Name { get; set; }          // link type name without extension; matched against files in LinkSearchFolder
        public double[] Origin { get; set; }
        public double Rotation { get; set; }      // about Z, radians
    }

    public class PhaseRec { public string Name { get; set; } }

    public class DesignOptionRec
    {
        public string Name { get; set; }
        public bool IsPrimary { get; set; }
        public int ElementCount { get; set; }
    }

    public class ProjParamRec
    {
        public string Name { get; set; }
        public string Spec { get; set; }
        public string Group { get; set; }
        public bool IsInstance { get; set; }
        public bool IsShared { get; set; }
        public string Guid { get; set; }
        public List<string> Categories { get; set; } = new List<string>();
    }

    public class ViewRangeRec
    {
        public string Plane { get; set; }         // PlanViewPlane name
        public string Level { get; set; }         // "special:<int>" or level UniqueId
        public double Offset { get; set; }
    }

    public class ViewRec
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string ViewType { get; set; }
        public string Kind { get; set; }          // FloorPlan | CeilingPlan | StructuralPlan | Section | Detail | ThreeD | Drafting | Legend
        public string SkipReason { get; set; }
        public int Scale { get; set; }
        public string DisplayStyle { get; set; }
        public string LevelKey { get; set; }
        public bool CropActive { get; set; }
        public BoxRec Crop { get; set; }
        public string TemplateName { get; set; }  // recorded only: view templates are not rebuilt
        public List<ViewRangeRec> ViewRange { get; set; } = new List<ViewRangeRec>();
        public double[] Eye { get; set; }
        public double[] Up { get; set; }
        public double[] Forward { get; set; }
        public bool Perspective { get; set; }
        public List<string> HiddenCategories { get; set; } = new List<string>();
        public List<ParamRec> Params { get; set; } = new List<ParamRec>();
    }

    public class ViewportRec { public string ViewKey { get; set; } public double[] Center { get; set; } public string TypeName { get; set; } }
    public class SheetScheduleRec { public string ScheduleKey { get; set; } public double[] Point { get; set; } }

    public class SheetRec
    {
        public string Key { get; set; }
        public string Number { get; set; }
        public string Name { get; set; }
        public string TitleBlockFamily { get; set; }
        public string TitleBlockType { get; set; }
        public List<ViewportRec> Viewports { get; set; } = new List<ViewportRec>();
        public List<SheetScheduleRec> ScheduleInstances { get; set; } = new List<SheetScheduleRec>();
        public List<string> RevisionKeys { get; set; } = new List<string>();
        public List<ParamRec> Params { get; set; } = new List<ParamRec>();
    }

    public class SchFieldRec
    {
        public string ParamName { get; set; }
        public string Heading { get; set; }
        public string FieldType { get; set; }     // ScheduleFieldType name
        public bool Hidden { get; set; }
    }

    public class SchFilterRec
    {
        public int FieldIndex { get; set; }
        public string FilterType { get; set; }
        public string ValueKind { get; set; }     // String | Double | Other
        public string S { get; set; }
        public double D { get; set; }
    }

    public class SchSortRec
    {
        public int FieldIndex { get; set; }
        public string SortOrder { get; set; }
        public bool ShowHeader { get; set; }
        public bool ShowFooter { get; set; }
        public bool ShowBlankLine { get; set; }
    }

    public class ScheduleRec
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }      // BuiltInCategory name, empty = multi-category
        public bool IsMaterialTakeoff { get; set; }
        public bool Itemized { get; set; }
        public bool GrandTotal { get; set; }
        public bool ShowTitle { get; set; }
        public bool ShowHeaders { get; set; }
        public bool ShowGridLines { get; set; }
        public List<SchFieldRec> Fields { get; set; } = new List<SchFieldRec>();
        public List<SchFilterRec> Filters { get; set; } = new List<SchFilterRec>();
        public List<SchSortRec> Sorts { get; set; } = new List<SchSortRec>();
        public string SnapshotCsv { get; set; }   // docs/schedules/<name>.csv - always written, the safety net when a schedule cannot be rebuilt
    }

    public class TextNoteRec
    {
        public string OwnerKey { get; set; }      // view or sheet UniqueId
        public string Text { get; set; }
        public double[] Point { get; set; }
        public string TypeName { get; set; }
        public double Width { get; set; }
    }

    public class DetailLineRec
    {
        public string OwnerKey { get; set; }
        public CurveRec Curve { get; set; }
        public string StyleName { get; set; }
    }

    public class FilledRegionRec
    {
        public string OwnerKey { get; set; }
        public string TypeName { get; set; }
        public List<List<CurveRec>> Loops { get; set; } = new List<List<CurveRec>>();
    }

    public class GroupRec
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public List<string> MemberKeys { get; set; } = new List<string>();
    }

    /// <summary>Per-element metadata that lives in package B (so package A stays purely geometric).</summary>
    public class ElementTagRec
    {
        public string Key { get; set; }
        public string Workset { get; set; }
        public string DesignOption { get; set; }
    }
}
