$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $PSScriptRoot "payload"
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Path $payload | Out-Null

$targets = @(
    @{ Year = 2020; Project = "Downgrader.Import2020"; Path = "Downgrader.Import2020\Downgrader.Import2020.csproj"; Manifest = "Downgrader.Import2020\Downgrader.Import2020.addin" },
    @{ Year = 2021; Project = "Downgrader.Export2021"; Path = "Downgrader.Export2021\Downgrader.Export2021.csproj"; Manifest = "Downgrader.Export2021\Downgrader.Export2021.addin" },
    @{ Year = 2021; Project = "Downgrader.Import2021"; Path = "Downgrader.Import2021\Downgrader.Import2021.csproj"; Manifest = "Downgrader.Import2021\Downgrader.Import2021.addin" },
    @{ Year = 2022; Project = "Downgrader.Export2022"; Path = "Downgrader.Export2022\Downgrader.Export2022.csproj"; Manifest = "Downgrader.Export2022\Downgrader.Export2022.addin" },
    @{ Year = 2022; Project = "Downgrader.Import2022"; Path = "Downgrader.Import2022\Downgrader.Import2022.csproj"; Manifest = "Downgrader.Import2022\Downgrader.Import2022.addin" },
    @{ Year = 2023; Project = "Downgrader.Export2023"; Path = "Downgrader.Export2023\Downgrader.Export2023.csproj"; Manifest = "Downgrader.Export2023\Downgrader.Export2023.addin" },
    @{ Year = 2024; Project = "Downgrader.Export2024"; Path = "Downgrader.Export2024\Downgrader.Export2024.csproj"; Manifest = "Downgrader.Export2024\Downgrader.Export2024.addin" },
    @{ Year = 2025; Project = "Downgrader.Export2025"; Path = "Downgrader.Export2025\Downgrader.Export2025.csproj"; Manifest = "Downgrader.Export2025\Downgrader.Export2025.addin" },
    @{ Year = 2026; Project = "Downgrader.Export2026"; Path = "Downgrader.Export2026\Downgrader.Export2026.csproj"; Manifest = "Downgrader.Export2026\Downgrader.Export2026.addin" },
    @{ Year = 2027; Project = "Downgrader.Export2027"; Path = "Downgrader.Export2027\Downgrader.Export2027.csproj"; Manifest = "Downgrader.Export2027\Downgrader.Export2027.addin" }
)
foreach ($item in $targets) {
    $fullProject = Join-Path $root $item.Path
    if (-not (Test-Path $fullProject)) { throw "Missing project: $fullProject" }
    Write-Host "`n=== Building $($item.Project) ===" -ForegroundColor Cyan
    dotnet build $fullProject --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $($item.Project)" }
    $outDir = Join-Path (Split-Path $fullProject -Parent) "bin"
    $dest = Join-Path $payload "$($item.Year)\Downgrader"
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    $dll = Join-Path $outDir "$($item.Project).dll"
    $core = Join-Path $outDir "Downgrader.Core.dll"
    if (-not (Test-Path $dll)) { throw "Expected output not found: $dll" }
    Copy-Item $dll $dest -Force
    if (Test-Path $core) { Copy-Item $core $dest -Force }
    Copy-Item (Join-Path $root $item.Manifest) (Join-Path $payload "$($item.Year)\$($item.Project).addin") -Force
}
Write-Host "`nInstaller payload created at: $payload" -ForegroundColor Green
