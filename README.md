# Revit Downgrader: Rebuild newer Revit models in Revit 2020 or 2021

**A cross-version model reconstruction toolkit for Revit 2021–2027 to Revit 2020 or 2021. The new Downgrader.Import2021 target adds a separate importer for Revit 2021; the existing Revit 2020 importer remains available.** It exports model geometry, supported native element data, family definitions, and project documentation into a portable package, then rebuilds the package inside Revit 2020.

> **Important:** This is reconstruction, not Autodesk's native backward file conversion. Some content may be represented as DirectShape geometry or have documented losses. Always test on copies and review reports before relying on the result.

## Contents

- Capabilities at a glance
- Supported Revit versions
- Quick start: one model
- Batch processing
- Export interface and options
- Import interface and options
- What gets rebuilt
- Package contents and reports
- Install and build
- Troubleshooting and safe use
- Known limitations
- Development status

## Capabilities at a glance

| Capability | What it does |
|---|---|
| Export from Revit 2021–2027 | Writes model and documentation packages for the Revit 2020 or 2021 importer |
| Hybrid reconstruction | Attempts supported native elements, verifies results, and uses DirectShape fallback when needed |
| Geometry-only mode | Prioritizes robust visual geometry over native editability |
| Scope controls | Export the whole project, current selection, or elements visible in the active view |
| Category and level filters | Include or exclude categories and optionally restrict to named levels |
| Family reconstruction | Rebuilds supported family parameters, formulas, types, reference planes, labelled dimensions, and extrusion geometry |
| Documentation transfer | Supports configured views, schedules, sheets, annotations, groups, parameters, tags, revisions, project information, linked models, and worksets |
| Verification and reports | Records element outcomes, reasons, category summaries, and geometry deviation checks |
| Batch workflows | Exports multiple RVT files or imports multiple packages into new Revit 2020 projects |
| Dry run | Runs model import and generates a report, then rolls back model changes |
| Optional IFC | Can add an IFC2x3 export as an independent fallback or comparison |


RVT files are forward-only, so this tool **rebuilds** a 2022 model in 2020 instead of converting the file.
Two add-ins share one package format:

```
Revit 2021..2027  --[Downgrader.Export<year>]-->  <Project>.downgrade/  --[Downgrader.Import2020]-->  Revit 2020
```

| Revit | Project | Framework | Build symbols | Notes |
|---|---|---|---|---|
| 2021 | `Downgrader.Export2021` | net48 | none | old ParameterType path, `TaggedLocalElementId` |
| 2022 | `Downgrader.Export2022` | net48 | `REVIT2022_OR_NEWER` | holds the shared exporter source |
| 2023 | `Downgrader.Export2023` | net48 | `REVIT2022_OR_NEWER` | `ElementId.IntegerValue` still exists |
| 2024 | `Downgrader.Export2024` | net48 | + `REVIT2024_OR_NEWER` | `ElementId.Value` (long) |
| 2025 | `Downgrader.Export2025` | net8.0-windows | + `REVIT2024_OR_NEWER` | .NET 8 |
| 2026 | `Downgrader.Export2026` | net8.0-windows | + `REVIT2024_OR_NEWER` | .NET 8 (Autodesk plans to move the runtime to .NET 10; net8 add-ins are expected to keep loading) |
| 2027 | `Downgrader.Export2027` | net10.0-windows | + `REVIT2024_OR_NEWER` | needs the .NET 10 SDK. **Not in `RevitDowngrader.sln`** (Visual Studio 2022 reports NETSDK1209): open `RevitDowngrader.Revit2027.sln` in Visual Studio 2026, or run `build-export2027.cmd` (dotnet CLI) |
| 2020 | `Downgrader.Import2020` | net47 | none | importer for Revit 2020 |
| 2021 | `Downgrader.Import2021` | net48 | none | new importer target for Revit 2021; uses the shared package format |
All exporters compile the **same source** (Export2024 and Export2025 link the Export2022 files and define `REVIT2024_OR_NEWER`);
the handful of API differences live in `Shared/Compat.cs`. Packages from either exporter are identical in format
(2025 data is normalised to the Revit 2020 vocabulary at export time), so one 2020 importer reads them all.

Export add-ins appear on the **BIM TOOLS** ribbon tab. Importers are target-specific: install Downgrader.Import2020 in Revit 2020 or Downgrader.Import2021 in Revit 2021. For many files use Batch Export, then the matching target's Batch Import.

## Build (Visual Studio 2022)
1. Open `RevitDowngrader.sln`. **RevitAPI / RevitAPIUI are restored from NuGet** (`Nice3point.Revit.Api.*`, matching year), so no Revit install is needed to compile; use `/p:UseLocalRevitApi=true` to reference the DLLs of an installed Revit instead. Revit API paths (only used for that) default to `C:\Program Files\Autodesk\Revit 2025` / `2022` / `2020`
   (override with `/p:RevitDir=...`). Each add-in project copies its DLLs + `.addin` into `%AppData%\Autodesk\Revit\Addins\<year>`.
2. Build the exporter for your source Revit version and the importer matching your destination (Downgrader.Import2020 or Downgrader.Import2021). Revit 2021 importer targets .NET Framework 4.8 and uses Revit 2021 API packages.
   Build only the exporters for the Revit versions you use (any of 2021 - 2027). In Visual Studio unload the projects you do not need, or build one with `dotnet build Downgrader.Export2026\\Downgrader.Export2026.csproj -c Release`. `Downgrader.Core` multi-targets net47 (2020 / 2022) and net8.0 (2025).
3. Core uses the framework's own JSON (System.Web.Extensions on net47, System.Text.Json on net8), so there is no Newtonsoft clash with Revit.
4. Core multi-targets net47 (used by the net48 exporters and the 2020 importer) and net8.0 (used by the 2025 / 2026 / 2027 exporters; a net10 add-in can reference net8).

## Quick start: one model

1. Make a backup and work from a copy of the source RVT.
2. Open the project in its matching Revit version, 2021 through 2027.
3. On the BIM TOOLS tab, open Downgrade (export) and choose Export.
4. Choose the scope: whole project, selected elements, or elements visible in the active view.
5. Choose Model package, Docs package, or both. For a typical migration, select both.
6. Review category and level filters, then the Mode & fidelity, Documentation, Advanced, and Output tabs.
7. Choose the output folder and package name, export, and inspect the report.
8. Open Revit 2020 and create a new project from the appropriate template.
9. On BIM TOOLS > Downgrade (import), choose Import and select the .downgrade package.
10. Import Model first, then Docs. Import Both runs the stages in order.
11. Review the report and validate geometry, parameters, hosts, views, schedules, sheets, and links in Revit 2020.

**Recommended first test:** use a small representative model and a test template before processing a large or contractual project.

## Batch processing

### Batch Export

1. In the source Revit version, choose BIM TOOLS > Downgrade (export) > Batch Export.
2. Select the folder containing source RVT files and a separate output folder for packages.
3. Confirm the file count and monitor progress. The operation can be cancelled.
4. Review the batch log and each package report.

The Include sub-folders option controls recursive discovery. Detach central files opens workshared models detached while preserving worksets, helping avoid changes to central models. Revit backup files with year-style suffixes are excluded.

### Batch Import

1. In Revit 2020, choose Batch Import.
2. Select the folder containing .downgrade packages, a project template (.rte), and an output folder.
3. Confirm and monitor progress.

Each package is imported into a new project and saved with the _2020.rvt suffix. A batch-import-log.txt file is written to the output folder. Check it for failures.

## Workflow
1. **Revit 2021 - 2027**, open the project: `Model Package` then `Docs Package` -> choose a folder; a `<name>.downgrade` folder is created.
2. Optional: edit `downgrade.options.json` in the package (mesh detail, verification tolerances, family template folder, ...).
3. **Revit 2020**, open a new project from your template: `Import Model (1st)` then `Import Docs (2nd)`; pick the package folder.
4. Read `report/import-model.txt` and `report/import-docs.txt`: every item is tagged Native / NativeWithLoss / DirectShape / Skipped with the reason.

Package layout: `model/model.json` + `model/geometry.pack` + `model/families/*.family.json` (package A);
`docs/docs.json` + `docs/schedules/*.csv` (package B); `rebuilt_rfa_2020/*.rfa` (families rebuilt by the importer); `idmap.json`; `report/`.

## How each difficulty is handled
| Difficulty | Approach |
|---|---|
| Families (.rfa forward-only) | Exporter opens each family with `EditFamily` and writes parameters, formulas, types, reference planes, labelled dimensions and extrusions (solid/void) with the reference planes each profile line is locked to. Importer rebuilds a real 2020 family from the category's `.rft`, locks and labels it, saves it to `rebuilt_rfa_2020/` and loads it. |
| Parametric intelligence | Formulas, type tables, labelled dimensions, plane locks and parameter associations (material / visibility) are recreated. Families with unsupported solid geometry (sweeps, blends, revolves, imports, nested families) are marked **Partial**, and their instances become DirectShapes unless `AllowPartialFamilies` is set. |
| Non-model content | Package B: views (plans, sections, 3D, drafting; view range, crop, hidden categories, parameters), schedules (fields, filters, sorting; always with a CSV snapshot), sheets + viewports + schedule instances, text notes, detail lines, filled regions, project/shared parameters (GUIDs kept), groups, worksets. Phases map by name; design options are flattened and reported. |
| Features newer than 2020 | Enum members are parsed **by name at runtime**, so anything missing in 2020 degrades instead of crashing. Every element is exported with a triangle mesh; if a native rebuild is impossible, fails, or its bounding box deviates more than `FlattenDeviationFeet` from the source, a **DirectShape** (same category, materials, identity in `ApplicationDataId`) is created. |

## Native rebuild coverage
Levels, grids, materials, wall and floor types (compound layers), plain straight/curved walls, flat floors (with openings),
point-based families (level-based, hosted doors/windows, two-level columns), **rooms**, **straight structural beams**,
**straight pipes, ducts, conduits and cable trays**. Everything else (roofs, ceilings, stairs, railings, curtain walls, fittings,
sloped/shape-edited floors, edited-profile walls, in-place families ...) arrives as verified-geometry DirectShapes.

## Buttons
| Revit 2022 (export) | Revit 2020 (import) |
|---|---|
| **Export Both** - model + docs in one step | **Import Both** - model, then docs |
| Model Package / Docs Package | Import Model / Import Docs |
| **Batch Export** - every .rvt in a folder, opened detached, no manual work | **Batch Import** - every *.downgrade package -> new project from a template -> saved `<name>_2020.rvt` |
| **Options** - defaults for new packages | **Package Info** - what a package contains and what to expect, nothing touched |
| | **Options** - edit the options stored in one package |

## Options (PropertyGrid dialog, stored as JSON; each package keeps the options it was made with)
- **Mode**: Hybrid (native where verified) or GeometryOnly (everything DirectShape: fastest, most robust).
- **Scope**: whole project, current selection, or active view. Plus include / exclude categories and "only these levels".
- **Force DirectShape categories** (import): e.g. flatten all walls while keeping families native.
- **Mesh detail**, **verification on/off and tolerances** (warn / fallback deviation), family template folder.
- Switches for rebuilding families, partial families, rooms, beams, MEP curves.
- Documentation switches: project parameters, views, schedules, sheets, annotations, groups, linked models, worksets.
- **Link search folder**: linked models are re-linked (position and Z rotation kept) when their downgraded file is found by name.
- Batch: include sub-folders, detach central files.
- **Dry run** (import): runs everything, writes the report, rolls the project back. The model import is otherwise a single Undo step.
- **Also export IFC**: adds an IFC2x3 file to the package as an independent fallback / cross-check.
- **Rebuild view templates**: each template is re-created from the first view that used it and re-assigned.
- Import-side filters: the include / exclude category and level lists also work on an existing package, so you can import a slice of a full package.
- Documentation extras: tags (re-attached to the re-created elements), revisions and their sheet assignments, project information.
- Reports are written as `.txt`, `.csv` (open in Excel, filter by tier), `-by-category.csv` (native / loss / DirectShape / skipped per category) and `.json`.

## Export interface and options

The Export studio groups settings into tabs. Options shown can vary by version and project context.

### What to export

- **Scope:** Whole project, selected elements, or elements visible in the active view. Selection scope is unavailable when nothing is selected.
- **Model package:** model element identity, geometry, supported model data, and family data.
- **Docs package:** supported documentation and project organization content.
- **Categories:** scan the model, then include all categories, only checked categories, or all except checked categories.
- **Levels:** include all levels or restrict processing to selected level names. Elements without a level are retained.
- **Save these settings as my defaults:** saves current settings for future exports.

### Mode, fidelity, and native rebuild

- **Hybrid:** attempts native reconstruction where supported and verified; falls back to DirectShape when native reconstruction is unsupported, fails, or deviates beyond tolerance.
- **GeometryOnly:** prioritizes robust geometry by representing model elements as DirectShapes, with less parametric editability.
- **Fast / Balanced / Max fidelity presets:** provide starting points for common trade-offs. Review settings after applying a preset.
- **Mesh detail:** ranges from coarse to fine; higher values can improve curved surfaces but increase package size.
- **Verify native results:** compares rebuilt native element bounds with the source.
- **Warn deviation (ft):** deviations above this value can be reported as NativeWithLoss.
- **Fallback deviation (ft):** deviations above this value can trigger DirectShape fallback.
- **Also export IFC:** optionally adds an IFC2x3 file where supported.
- **Rebuild families:** rebuild supported loadable families as Revit 2020 families.
- **Allow partial families:** permits use of partially rebuilt families when some geometry was not reconstructed.
- **Freeze families that do not match:** can preserve measured per-type geometry when full parametric reconstruction is not possible; this is not equivalent to a fully editable parametric family.
- **Store family type geometry / Verify rebuilt family types:** store source geometry and compare rebuilt type bounds.
- **Family type tolerance (ft):** maximum accepted bounds difference for family verification.
- **Native rooms:** attempts to recreate rooms; enclosing room-bounding geometry must be suitable.
- **Native beams:** attempts to recreate supported straight structural framing.
- **Native MEP curves:** attempts native straight pipes, ducts, conduits, and cable trays. Fittings and accessories are not covered by this switch.
- **Native roofs:** option for native footprint roof reconstruction; validate results carefully.
- **Family template folder:** optional folder containing Revit 2020 .rft templates. Empty uses Revit's default template path.

### Documentation options

The following switches control supported documentation content: Project parameters, Views, Schedules, Sheets, Annotations (text notes, detail lines, filled regions), Groups, Linked models, Tags, Revisions, Project information, Rebuild view templates, and Preserve worksets.

**Link search folder** points to the folder containing downgraded linked files. Links are matched by name; batch import uses its output folder as a fallback. Validate link placement and coordinates after import.

### Advanced and import-side options

- **Include categories / Exclude categories:** filters which categories are processed. An empty include list means all categories unless exclusions apply.
- **Only these levels:** restricts processing to named levels; an empty list means all levels.
- **Force DirectShape categories:** always imports specified categories as DirectShapes, even if native rebuilding may be possible.
- **Dry run (import):** performs model import for assessment, writes reports, then rolls back model changes. If model and docs are selected together, docs are skipped during dry run because no model elements remain for documentation to attach to.
- **Show report when done:** opens the report window after a run.
- **Include sub-folders / Detach central files:** batch export controls described above.

Options are stored in downgrade.options.json inside each package. Defaults for new packages are stored at %AppData%\\BIMTOOLS\\Downgrader\\defaults.json. Package-specific options travel with the package.

## Import interface and options

On BIM TOOLS > Downgrade (import) in Revit 2020:

- **Import:** opens the import studio, offers recent packages, displays package contents, and lets you choose model and/or documentation import.
- **Import Model:** imports model data only.
- **Import Docs:** imports documentation only. Import model first where documentation needs recreated elements.
- **Import Both:** imports model, then documentation.
- **Batch Import:** creates a new project from a selected template for each package and saves the result.
- **Package Info:** summarizes package contents and mode/dry-run/verification settings without importing.
- **Options:** edits settings stored in the selected package, including filters, forced DirectShape categories, dry run, and link search folder.
- **Last Report:** reopens the most recent report.

Model import is grouped into a single Revit Undo step. Cancelling model import rolls it back; cancelling documentation import may leave partial documentation changes, so inspect the project and report after cancellation.

## Troubleshooting and safe use

| Symptom | Checks |
|---|---|
| Downgrade panel does not appear | Confirm the matching year add-in is in that year's Addins folder, the manifest assembly path exists, and the correct Revit version is running. Check %AppData%\\BIMTOOLS\\Downgrader\\ribbon-error.txt if present. |
| Export or import fails | Read the error dialog and the package report folder. Preserve the package and error report for diagnosis. |
| Geometry appears but is not editable | The item may be a DirectShape. Check its tier and reason in the report. |
| Family is incomplete or non-parametric | Review family options and report entries. Unsupported sweeps, blends, revolves, nested families, and imported geometry may not rebuild parametrically. |
| Views or schedules differ | Validate view ranges, crop, visibility, schedule fields, filters, sorting, sheet viewports, and schedule instances manually. |
| Linked models are missing | Set Link search folder to the location of the corresponding downgraded linked files. Links are matched by name. |
| Build fails for a target year | Check the target framework and SDK, restore matching API packages, and review version-specific API errors. Every target is not yet verified. |
| Batch output is incomplete | Review the batch log and each package report, then rerun failed files individually. |

### Before using a rebuilt model for delivery

- Keep untouched copies of all source RVT files.
- Use a dedicated output folder and avoid overwriting valuable files.
- Compare representative views and model areas against the source.
- Validate geometry, levels, coordinates, hosts, constraints, family parameters, schedules, sheets, tags, phases, worksets, and links relevant to your project.
- Review skipped and loss-marked items and resolve them with the design team.
- Do not treat successful import as proof of design, coordination, quantity, or contractual equivalence.

## Known gaps / next milestones
Nested families, sweeps/blends/revolves in families, symbolic lines and masking regions in families, wall joins, roofs and ceilings as
native elements (roofs, ceilings), dimensions, keynotes, revision clouds, view filters, section boxes in 3D views, shared-coordinate alignment, legends
(not creatable by the API), design options and phases (not creatable by the 2020 API).

## Development status and validation

**The existing project notes state that the code has not yet been compiled.** Treat the repository as experimental until you build it successfully against the intended Revit APIs and run export/import tests. The target matrix is not proof that every version has passed compilation or runtime testing.

## Important: this code has not been compiled yet
It was written without the Revit SDK available. Expect a handful of compile fixes on first build (API member names that differ by version
are the usual suspects: e.g. `ScheduleDefinition.ShowGridLines`, `FamilyType.AsElementId`, `Sketch.GetAllElements`, `Phase`-related members).
2025-specific points to check first: the net8 build of `Downgrader.Core`, `Definition.GetDataType()` / `GetGroupTypeId()` mapping in `Shared/Compat.cs` (unknown data types or groups are exported empty and the 2020 importer falls back to its defaults), `ElementId.Value`, `IndependentTag.GetTaggedLocalElements`.
Newer calls to check first: `IndependentTag.TaggedLocalElementId` / `HasLeader` (tags), `View.CreateViewTemplate`, `Revision` members, `IFCExportOptions`.
Test in this order: Core -> Export on a small model -> Import on a blank 2020 project -> inspect `report/*.txt` -> then a real project.


## Install and build

### Prerequisites

- Windows and Visual Studio with the workload for the target framework/runtime, or the matching .NET SDK and dotnet CLI.
- Revit API reference assemblies for the target year. Projects default to the Nice3point Revit API NuGet packages; a local Revit installation can be selected instead.
- The target Revit version for runtime testing. Compiling against API packages does not prove the add-in works correctly in Revit.

### Build workflow

1. Clone the repository and open RevitDowngrader.sln for the supported solution targets.
2. Restore NuGet packages.
3. Build Downgrader.Core, then build the exporter for the source Revit year and Downgrader.Import2020.
4. Revit 2027 is in RevitDowngrader.Revit2027.sln because its .NET 10 target may not be supported by the Visual Studio 2022 SDK setup. Use a .NET 10 SDK with build-export2027.cmd or a compatible Visual Studio version.
5. Confirm build outputs and .addin manifest assembly paths point to the intended files.
6. Deploy only the add-in matching the Revit version being tested and verify the BIM TOOLS ribbon panel appears.
7. Run a small export/import test and inspect all reports before wider use.

Depending on project settings, builds may copy assemblies and .addin files to %AppData%\\Autodesk\\Revit\\Addins\\<year>. Review deployment settings before building on a production machine.

## RevitAPI.dll / RevitAPIUI.dll from NuGet
- **Default:** every project has `PackageReference`s to `Nice3point.Revit.Api.RevitAPI` and `...RevitAPIUI` for its Revit year
  (2020.x ... 2027.x), restored automatically from nuget.org by Visual Studio / `dotnet restore` (see `nuget.config`). Copy-local is off, so Revit's own DLLs are never overwritten.
- **Offline / vendored copy:** run `.\Get-RevitApi.ps1` (PowerShell) once. It downloads both DLLs for 2020 - 2027 from NuGet into `ThirdParty\RevitAPI\<year>\`;
  projects then pick them up automatically. Use `-Years 2020,2025` for a subset, `-Force` to refresh.
- **Installed Revit instead:** `/p:UseLocalRevitApi=true /p:RevitDir="C:\Program Files\Autodesk\Revit 2025"`.

## Revit 2027 (.NET 10) build
`RevitDowngrader.sln` (Visual Studio 2022) contains everything except the 2027 exporter, because VS 2022 bundles SDK 9 and refuses `net10.0` (NETSDK1209).
Build 2027 with either `build-export2027.cmd` (needs the .NET 10 SDK; checks for it and runs `dotnet build -c Release`) or by opening
`RevitDowngrader.Revit2027.sln` in a Visual Studio that supports .NET 10 (Visual Studio 2026). It deploys to `%AppData%\Autodesk\Revit\Addins\2027`.

## BIM TOOLS ribbon tab
Every add-in (exporters 2021 - 2027 and the 2020 importer) places its panel inside the **BIM TOOLS** tab through `Shared/RibbonHelper.cs`:
the tab is created if it does not exist, reused if another BIM TOOLS add-in already created it, the panel / buttons are never duplicated, and if the tab
cannot be used at all the panel falls back to Revit's Add-Ins tab, so Revit never shows an error at startup (details go to
`%AppData%\BIMTOOLS\Downgrader\ribbon-error.txt`). `RibbonHelper.cs` has no dependencies other than `Autodesk.Revit.UI`:
copy it into your other BIM TOOLS add-ins and call `BimToolsRibbon.GetOrCreatePanel(app, "My panel")` instead of `CreateRibbonTab` / `CreateRibbonPanel`.
If one of those add-ins still calls `CreateRibbonTab("BIM TOOLS")` without try/catch, Revit reports an error when it loads second, so switch them to the helper.

## User interface (all versions)
- **Export...** (Revit 2021 - 2027) opens one window with tabs: *What to export* (scope: project / selection / active view; category list scanned from the model with counts and
  All / Only checked / All except checked; level list; model + docs package), *Mode & fidelity* (Fast / Balanced / Max fidelity presets, hybrid or geometry-only, native switches, tolerances),
  *Documentation* (every docs switch), *Advanced* (every option) and *Output* (folder, package name, report). A progress window with **Cancel** runs the export.
- **Import...** (Revit 2020) picks a package (recent list), shows what is inside, lets you choose model / docs, categories and levels from the package, the dry-run switch,
  link folder and forced-DirectShape categories, then imports with progress; the model import is a single Undo step, and Cancel rolls it back.
- After every run the **report window** lists every item with its tier (colour-coded), a tier filter, text search, *Copy summary*, *Open report folder* and *Open HTML report*.
  **Last Report** reopens it any time. Reports are also written as `.html`, `.csv`, `-by-category.csv`, `.txt` and `.json`.
- Quick Export / Quick Import keep the one-click flow; Batch Export / Batch Import show per-file progress, can be cancelled, and remember their folders and template.
- Everything is plain WinForms, so the same dialogs run on .NET Framework 4.7 / 4.8, .NET 8 and .NET 10 and follow Windows display scaling.

## Conversion quality: families and model (how "equal to native" is enforced)
Every rebuilt family goes through this pipeline, and anything that does not match the source is caught and replaced:
1. **Measure** (export): for every family type the exporter records the bounds and a fine mesh of the type's real geometry (`Store family type geometry`).
2. **Rebuild** (import): parameters, formulas, types, reference planes, labelled dimensions, extrusions locked to their planes, **visibility / detail level of every form
   (Coarse / Medium / Fine, plan, front/back, left/right, top/bottom)**, **symbolic lines** (door swings, plan symbols), and the **family settings**
   (Always vertical, Cut with voids when loaded, Shared, Room calculation point, OmniClass ...). Type parameters (Description, Model, Manufacturer, Type Mark, Keynote, Cost ...) are copied onto the loaded types.
3. **Verify** (import): after loading, the bounds of every type are compared with the source (`Family type tolerance`, default 0.03 ft = 9 mm).
4. **Freeze** when needed: families with geometry that cannot be rebuilt parametrically (sweeps, blends, revolves, nested families, imports) or that fail verification
   are rebuilt as real families whose **geometry per type is the exact source geometry** (closed solids, per-type Yes/No visibility parameters, voids kept so hosted
   families still cut their host). They are not parametric, so the report marks them NativeWithLoss, but every instance equals the native one.
5. **Last resort**: if even the frozen family does not match, its instances become DirectShapes. Nothing is silently wrong: see `family-verify` in the report.
Limits to know: only types that are used in the project have geometry to measure (unused types of a frozen family appear empty); nested/sweep forms are frozen, not parametric.

Model improvements in the same release: **native roofs** (footprint, slope lines, roof type layers), compound structures with **core / shell layers and structural layer**
for wall, floor and roof types, verification of every native element against the source bounding box with automatic DirectShape fallback.


## New target: downgrade Revit 2027–2021 models into Revit 2021

The `feature/revit-2021-target` branch adds `Downgrader.Import2021`, a separate importer project targeting **Revit 2021 / .NET Framework 4.8**. It is intended to reuse the existing `.downgrade` package format produced by the Revit 2021–2027 exporters, while preserving the original Revit 2020 importer.

### Revit 2021 workflow

1. Build and install the exporter matching the source model's Revit version (2021–2027).
2. Export the model package and, if needed, the documentation package.
3. Open Revit 2021 and create a new project from a suitable template.
4. Build/install `Downgrader.Import2021` and confirm its BIM TOOLS > Downgrade (import) panel appears.
5. Use Import or Batch Import, then review the generated reports and validate the rebuilt model.

### Build the Revit 2021 importer

- Project: `Downgrader.Import2021/Downgrader.Import2021.csproj`
- Target framework: `net48`
- API packages: `Nice3point.Revit.Api.RevitAPI` and `Nice3point.Revit.Api.RevitAPIUI`, version `2021.*`
- Add-in deployment folder: `%AppData%\Autodesk\Revit\Addins\2021`
- The project references the shared `Downgrader.Core` and `Shared` source.

**Validation status:** this new target has been added to source control but has not been compiled or runtime-tested in Revit 2021. Treat it as experimental until the importer builds against the Revit 2021 API and passes model, family, documentation, and batch-import tests. Keep the original source model and review reports before using any output for delivery.
