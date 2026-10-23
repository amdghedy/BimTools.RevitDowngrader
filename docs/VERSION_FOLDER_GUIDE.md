# Version and Folder Guide

This guide explains where to find each version-specific conversion tool and which project to use for a conversion workflow.

## Quick selection

| Your source model version | Export project to use | Destination importer | Notes |
|---|---|---|---|
| 2027 | `Downgrader.Export2027` | `Downgrader.Import2021` or `Downgrader.Import2020` | The 2027 exporter is built separately with the .NET 10 SDK. Validate the importer/API combination before use. |
| 2026 | `Downgrader.Export2026` | `Downgrader.Import2021` or `Downgrader.Import2020` | Use the exporter that matches the version opening the source model. |
| 2025 | `Downgrader.Export2025` | `Downgrader.Import2021` or `Downgrader.Import2020` | Review export report and rebuilt output. |
| 2024 | `Downgrader.Export2024` | `Downgrader.Import2021` or `Downgrader.Import2020` | Review export report and rebuilt output. |
| 2023 | `Downgrader.Export2023` | `Downgrader.Import2021` or `Downgrader.Import2020` | Review export report and rebuilt output. |
| 2022 | `Downgrader.Export2022` | `Downgrader.Import2021` or `Downgrader.Import2020` | Review export report and rebuilt output. |
| 2021 | `Downgrader.Export2021` | `Downgrader.Import2021` or `Downgrader.Import2020` | For a same-version export/import path, verify whether conversion is needed first. |
| Package already exported | No exporter needed | `Downgrader.Import2021` or `Downgrader.Import2020` | Choose the importer that matches the Revit version in which you want to rebuild the model. |

**Important:** Exporter projects read the model in the source application version and write the shared downgrade package. Importer projects read that package and rebuild it in the destination application version. This is not a direct RVT-to-RVT file rewrite. Model elements and documentation may be reconstructed with different fidelity; read the reports and validate the result. The table describes intended workflows, not proof that every source/target combination has compiled or passed runtime tests.

## Repository map

```text
BimTools.RevitDowngrader/
├── Downgrader.Core/                 # Shared package format, schemas, options, reports and utilities
│   ├── Schema/                      # Model, family and documentation data schemas
│   ├── Storage/                     # Package read/write and geometry storage
│   └── Util/                        # Conversion options, support checks and reporting
├── Shared/                          # UI, ribbon, compatibility and shared helpers
├── Downgrader.Export2021/           # Exporter loaded by the 2021 source application
├── Downgrader.Export2022/           # Exporter loaded by the 2022 source application
├── Downgrader.Export2023/           # Exporter loaded by the 2023 source application
├── Downgrader.Export2024/           # Exporter loaded by the 2024 source application
├── Downgrader.Export2025/           # Exporter loaded by the 2025 source application
├── Downgrader.Export2026/           # Exporter loaded by the 2026 source application
├── Downgrader.Export2027/           # Exporter loaded by the 2027 source application
├── Downgrader.Import2020/           # Importer loaded in the 2020 destination application
├── Downgrader.Import2021/           # Importer loaded in the 2021 destination application
├── RevitDowngrader.sln              # Main solution for supported non-2027 targets
├── RevitDowngrader.Revit2027.sln    # Separate solution for the 2027/.NET 10 exporter
├── Get-RevitApi.ps1                 # Optional helper for downloading API assemblies
├── README.md                        # Main user guide
└── docs/
    └── VERSION_FOLDER_GUIDE.md      # This version and folder guide
```

## What belongs where?

| Folder | Responsibility | When should I open it? |
|---|---|---|
| `Downgrader.ExportYYYY` | Version-specific add-in that reads a model in source year YYYY and exports the package | Start here when the source model is opened in that year |
| `Downgrader.ImportYYYY` | Version-specific add-in that reads a package and rebuilds a model in destination year YYYY | Start here when selecting the destination application version |
| `Downgrader.Core` | Shared package schemas, storage, conversion options and reporting | When changing package structure or shared conversion behavior |
| `Shared` | Common UI, ribbon and compatibility helpers used by multiple projects | When fixing shared add-in behavior or compatibility |
| `RevitDowngrader.sln` | Main multi-project solution | For the normal development/build workflow described in README |
| `RevitDowngrader.Revit2027.sln` | Separate solution for the 2027 exporter | When building the .NET 10 exporter with a compatible SDK/toolchain |
| `docs/` | User-facing guides and reference documents | Before choosing a project or changing the folder structure |

## Recommended conversion workflow

1. Identify the **source version** of the model.
2. Build/install the matching `Downgrader.ExportYYYY` project in that source application.
3. Export the model to a downgrade package and preserve the generated report.
4. Identify the **destination version** and install its matching `Downgrader.ImportYYYY` project.
5. Import the package into a new destination project, then inspect the report.
6. Compare representative geometry, families, parameters, coordinates, links, views, schedules, sheets and other required documentation.
7. Keep the original source model and do not use the converted model for formal delivery until it has been independently checked.

## Naming convention for future versions

Keep one exporter folder per source version and one importer folder per destination version:

- `Downgrader.ExportYYYY` — source-version exporter.
- `Downgrader.ImportYYYY` — destination-version importer.

A new version should be added as its own project, with its own API references, manifest and deployment target. Update this guide and the main README's compatibility table when the project is added. Do not claim support merely because a folder exists; record build and runtime test status separately.
