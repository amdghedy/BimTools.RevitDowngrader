using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Downgrader.Core.Storage;

namespace Downgrader.Core.Util
{
    public enum ExportMode { Hybrid = 0, GeometryOnly = 1 }
    public enum ExportScope { Project = 0, Selection = 1, ActiveView = 2 }

    /// <summary>All user options. Edited in the Options dialog (PropertyGrid), stored as defaults in %AppData%\BIMTOOLS\Downgrader\defaults.json
    /// and copied into each package as downgrade.options.json (so a package always carries the options it was made with).</summary>
    public class DowngradeOptions
    {
        // ---------------------------------------------------------------- 1 General
        [Category("1 General"), DisplayName("Mode"), Description("Hybrid: rebuild natively where it verifies, DirectShape otherwise.  GeometryOnly: every element becomes a DirectShape (fastest, most robust, nothing parametric).")]
        public ExportMode Mode { get; set; } = ExportMode.Hybrid;

        [Category("1 General"), DisplayName("Scope (export)"), Description("Project: whole model.  Selection: only the elements selected in Revit.  ActiveView: only elements visible in the active view.")]
        public ExportScope Scope { get; set; } = ExportScope.Project;

        [Category("1 General"), DisplayName("Include categories"), Description("BuiltInCategory names, e.g. OST_Walls, OST_Doors. Empty = all categories.")]
        public List<string> IncludeCategories { get; set; } = new List<string>();

        [Category("1 General"), DisplayName("Exclude categories"), Description("BuiltInCategory names that are never exported, e.g. OST_Furniture.")]
        public List<string> ExcludeCategories { get; set; } = new List<string>();

        [Category("1 General"), DisplayName("Only these levels"), Description("Level names. Elements on other levels are skipped (elements without a level are kept). Empty = all levels.")]
        public List<string> LevelNames { get; set; } = new List<string>();

        [Category("1 General"), DisplayName("Force DirectShape categories (import)"), Description("BuiltInCategory names that are always imported as DirectShapes even if a native rebuild is possible.")]
        public List<string> ForceDirectShapeCategories { get; set; } = new List<string>();

        [Category("1 General"), DisplayName("Dry run (import)"), Description("Run the whole import, write the report, then roll everything back. Shows how much would be native / DirectShape without changing the project. Docs are skipped when combined with a model import.")]
        public bool DryRun { get; set; } = false;

        [Category("1 General"), DisplayName("Also export IFC (export)"), Description("Additionally write an IFC2x3 file into the package (ifc/). Open it in Revit 2020 as an independent fallback or cross-check.")]
        public bool AlsoExportIfc { get; set; } = false;

        [Category("1 General"), DisplayName("Show report when done"), Description("Open the report window (filterable list with tiers and reasons) after every export / import.")]
        public bool OpenReportWhenDone { get; set; } = true;

        // ---------------------------------------------------------------- 2 Geometry
        [Category("2 Geometry"), DisplayName("Mesh detail"), Description("Face.Triangulate level of detail, 0 (coarse) to 1 (fine). Higher = bigger package, smoother curved surfaces.")]
        public double MeshDetail { get; set; } = 0.5;

        // ---------------------------------------------------------------- 3 Native rebuild
        [Category("3 Native rebuild"), DisplayName("Rebuild families"), Description("Rebuild loadable families as real 2020 families (parameters, formulas, types, planes, extrusions).")]
        public bool RebuildFamilies { get; set; } = true;

        [Category("3 Native rebuild"), DisplayName("Allow partial families"), Description("Place instances of families that were only partly rebuilt (some solid geometry dropped). Off = those instances become DirectShapes.")]
        public bool AllowPartialFamilies { get; set; } = false;

        [Category("3 Native rebuild"), DisplayName("Native rooms"), Description("Re-create placed rooms (name, number, parameters) in 2020. Needs the enclosing walls to be native.")]
        public bool NativeRooms { get; set; } = true;

        [Category("3 Native rebuild"), DisplayName("Native beams"), Description("Re-create straight curve-driven structural framing as native beams.")]
        public bool NativeBeams { get; set; } = true;

        [Category("3 Native rebuild"), DisplayName("Native MEP curves"), Description("Re-create straight pipes, ducts, conduits and cable trays natively. Fittings and accessories stay DirectShapes.")]
        public bool NativeMep { get; set; } = true;

        [Category("3 Native rebuild"), DisplayName("Native roofs"), Description("Re-create footprint roofs (type layers, footprint, slopes) natively.")]
        public bool NativeRoofs { get; set; } = true;

        [Category("3 Native rebuild"), DisplayName("Store family type geometry"), Description("Export the measured bounds and mesh of every family type. Needed to verify rebuilt families against the source and to rebuild them frozen when they do not match.")]
        public bool StoreTypeGeometry { get; set; } = true;

        [Category("3 Native rebuild"), DisplayName("Verify rebuilt family types"), Description("After loading a rebuilt family, compare every type's geometry bounds with the source.")]
        public bool VerifyFamilyTypes { get; set; } = true;

        [Category("3 Native rebuild"), DisplayName("Family type tolerance (ft)"), Description("Largest accepted bounds difference per family type. 0.03 ft is about 9 mm.")]
        public double FamilyVerifyFeet { get; set; } = 0.03;

        [Category("3 Native rebuild"), DisplayName("Freeze families that do not match"), Description("Families with unsupported geometry (sweeps, blends, revolves, nested families, imports) or that fail verification are rebuilt as real families whose geometry per type equals the source (non-parametric). Off: such instances become DirectShapes.")]
        public bool FreezePartialFamilies { get; set; } = true;

        [Category("3 Native rebuild"), DisplayName("Verify native results"), Description("Compare every native element's bounding box with the source and fall back to a DirectShape when it deviates too much.")]
        public bool VerifyNative { get; set; } = true;

        [Category("3 Native rebuild"), DisplayName("Warn deviation (ft)"), Description("Deviation above this is reported as NativeWithLoss. 0.02 ft is about 6 mm.")]
        public double WarnDeviationFeet { get; set; } = 0.02;

        [Category("3 Native rebuild"), DisplayName("Fallback deviation (ft)"), Description("Deviation above this discards the native element and uses a DirectShape. 0.25 ft is about 76 mm.")]
        public double FlattenDeviationFeet { get; set; } = 0.25;

        [Category("3 Native rebuild"), DisplayName("Family template folder"), Description("Folder with *.rft templates. Empty = the Revit 2020 default family template path.")]
        public string TemplateFolder { get; set; }

        // ---------------------------------------------------------------- 4 Documentation
        [Category("4 Documentation"), DisplayName("Project parameters")] public bool DocsProjectParams { get; set; } = true;
        [Category("4 Documentation"), DisplayName("Views")] public bool DocsViews { get; set; } = true;
        [Category("4 Documentation"), DisplayName("Schedules")] public bool DocsSchedules { get; set; } = true;
        [Category("4 Documentation"), DisplayName("Sheets")] public bool DocsSheets { get; set; } = true;
        [Category("4 Documentation"), DisplayName("Annotations (text, detail lines, regions)")] public bool DocsAnnotations { get; set; } = true;
        [Category("4 Documentation"), DisplayName("Groups")] public bool DocsGroups { get; set; } = true;
        [Category("4 Documentation"), DisplayName("Linked models")] public bool DocsLinks { get; set; } = true;
        [Category("4 Documentation"), DisplayName("Tags")] public bool DocsTags { get; set; } = true;
        [Category("4 Documentation"), DisplayName("Revisions")] public bool DocsRevisions { get; set; } = true;
        [Category("4 Documentation"), DisplayName("Project information")] public bool DocsProjectInfo { get; set; } = true;

        [Category("4 Documentation"), DisplayName("Rebuild view templates"), Description("Re-create each view template from the first view that used it and re-assign it to every view that used it.")]
        public bool RebuildViewTemplates { get; set; } = true;

        [Category("4 Documentation"), DisplayName("Preserve worksets"), Description("Enable worksharing in the 2020 project and re-create user worksets with their element assignments.")]
        public bool PreserveWorksets { get; set; } = true;

        [Category("4 Documentation"), DisplayName("Link search folder"), Description("Folder holding the DOWNGRADED linked files (matched by name). Batch import fills this with its output folder automatically.")]
        public string LinkSearchFolder { get; set; }

        // ---------------------------------------------------------------- 5 Batch
        [Category("5 Batch"), DisplayName("Include sub-folders")] public bool BatchSubfolders { get; set; } = true;

        [Category("5 Batch"), DisplayName("Detach central files"), Description("Open workshared central files detached (with worksets preserved) so the batch never touches the central model.")]
        public bool BatchOpenDetached { get; set; } = true;

        /// <summary>Fast / Balanced / Max fidelity. Filters, folders and run switches are kept; everything else is reset first.</summary>
        public void ApplyPreset(string name)
        {
            var keep = new HashSet<string> { "IncludeCategories", "ExcludeCategories", "LevelNames", "ForceDirectShapeCategories", "Scope",
                                             "LinkSearchFolder", "TemplateFolder", "DryRun", "OpenReportWhenDone", "AlsoExportIfc" };
            var fresh = new DowngradeOptions();
            foreach (System.Reflection.PropertyInfo p in typeof(DowngradeOptions).GetProperties())
                if (p.CanWrite && !keep.Contains(p.Name)) p.SetValue(this, p.GetValue(fresh, null), null);
            switch (name)
            {
                case "Fast":
                    Mode = ExportMode.GeometryOnly; MeshDetail = 0.3; RebuildFamilies = false;
                    NativeRooms = false; NativeBeams = false; NativeMep = false; VerifyNative = false;
                    DocsAnnotations = false; DocsTags = false; RebuildViewTemplates = false;
                    break;
                case "Max fidelity":
                    MeshDetail = 0.8; WarnDeviationFeet = 0.01; FlattenDeviationFeet = 0.1; AllowPartialFamilies = false;
                    break;
            }
        }

        // ---------------------------------------------------------------- storage
        public static string DefaultsFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BIMTOOLS", "Downgrader", "defaults.json"); }
        }

        public static DowngradeOptions LoadDefaults()
        {
            try { if (File.Exists(DefaultsFile)) return Json.Read<DowngradeOptions>(DefaultsFile); } catch { }
            return new DowngradeOptions();
        }

        public static void SaveDefaults(DowngradeOptions o) { Json.Write(DefaultsFile, o, true); }

        public static DowngradeOptions LoadOrCreate(PackagePaths p)
        {
            if (File.Exists(p.Options)) return Json.Read<DowngradeOptions>(p.Options);
            DowngradeOptions o = LoadDefaults();
            Json.Write(p.Options, o, true);
            return o;
        }

        public void Save(PackagePaths p) { Json.Write(p.Options, this, true); }
    }
}
