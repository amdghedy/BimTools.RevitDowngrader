# Revit Downgrader (2022 -> 2020)

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
| 2020 | `Downgrader.Import2020` | net47 | none | the importer |
All exporters compile the **same source** (Export2024 and Export2025 link the Export2022 files and define `REVIT2024_OR_NEWER`);
the handful of API differences live in `Shared/Compat.cs`. Packages from either exporter are identical in format
(2025 data is normalised to the Revit 2020 vocabulary at export time), so one 2020 importer reads them all.

Both add-ins appear on the **BIM TOOLS** ribbon tab (panels "Downgrade (export)" / "Downgrade (import)"). For many files use Batch Export, then Batch Import: no manual steps in between.

## Build (Visual Studio 2022)
1. Open `RevitDowngrader.sln`. **RevitAPI / RevitAPIUI are restored from NuGet** (`Nice3point.Revit.Api.*`, matching year), so no Revit install is needed to compile; use `/p:UseLocalRevitApi=true` to reference the DLLs of an installed Revit instead. Revit API paths (only used for that) default to `C:\Program Files\Autodesk\Revit 2025` / `2022` / `2020`
   (override with `/p:RevitDir=...`). Each add-in project copies its DLLs + `.addin` into `%AppData%\Autodesk\Revit\Addins\<year>`.
2. Build `Downgrader.Export2025` (needs the .NET 8 SDK and Revit 2025 installed), `Downgrader.Export2022` and/or `Downgrader.Import2020`.
   Build only the exporters for the Revit versions you use (any of 2021 - 2027). In Visual Studio unload the projects you do not need, or build one with `dotnet build Downgrader.Export2026\\Downgrader.Export2026.csproj -c Release`. `Downgrader.Core` multi-targets net47 (2020 / 2022) and net8.0 (2025).
3. Core uses the framework's own JSON (System.Web.Extensions on net47, System.Text.Json on net8), so there is no Newtonsoft clash with Revit.
4. Core multi-targets net47 (used by the net48 exporters and the 2020 importer) and net8.0 (used by the 2025 / 2026 / 2027 exporters; a net10 add-in can reference net8).

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

## Known gaps / next milestones
Nested families, sweeps/blends/revolves in families, symbolic lines and masking regions in families, wall joins, roofs and ceilings as
native elements (roofs, ceilings), dimensions, keynotes, revision clouds, view filters, section boxes in 3D views, shared-coordinate alignment, legends
(not creatable by the API), design options and phases (not creatable by the 2020 API).

## Important: this code has not been compiled yet
It was written without the Revit SDK available. Expect a handful of compile fixes on first build (API member names that differ by version
are the usual suspects: e.g. `ScheduleDefinition.ShowGridLines`, `FamilyType.AsElementId`, `Sketch.GetAllElements`, `Phase`-related members).
2025-specific points to check first: the net8 build of `Downgrader.Core`, `Definition.GetDataType()` / `GetGroupTypeId()` mapping in `Shared/Compat.cs` (unknown data types or groups are exported empty and the 2020 importer falls back to its defaults), `ElementId.Value`, `IndependentTag.GetTaggedLocalElements`.
Newer calls to check first: `IndependentTag.TaggedLocalElementId` / `HasLeader` (tags), `View.CreateViewTemplate`, `Revision` members, `IFCExportOptions`.
Test in this order: Core -> Export on a small model -> Import on a blank 2020 project -> inspect `report/*.txt` -> then a real project.


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
