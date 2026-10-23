<#
.SYNOPSIS
  Downloads RevitAPI.dll and RevitAPIUI.dll for each Revit year from NuGet (Nice3point.Revit.Api.RevitAPI / RevitAPIUI)
  into ThirdParty\RevitAPI\<year>\ so the solution can also be built offline.

.DESCRIPTION
  Not required for a normal build: Visual Studio / dotnet restore already pulls the same packages automatically.
  When ThirdParty\RevitAPI\<year>\RevitAPI.dll exists, the matching project uses these DLLs instead of the NuGet packages
  (copy-local is off, so they are never deployed next to the add-in).

.EXAMPLE
  .\Get-RevitApi.ps1                      # 2020 - 2027
  .\Get-RevitApi.ps1 -Years 2020,2025     # only some years
  .\Get-RevitApi.ps1 -Force               # re-download
#>
param(
    [int[]]$Years = (2020..2027),
    [string]$Out = (Join-Path $PSScriptRoot 'ThirdParty\RevitAPI'),
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$flat = 'https://api.nuget.org/v3-flatcontainer'
$packages = @('Nice3point.Revit.Api.RevitAPI', 'Nice3point.Revit.Api.RevitAPIUI')
$work = Join-Path ([IO.Path]::GetTempPath()) ('revitapi_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

foreach ($year in $Years) {
    $dest = Join-Path $Out "$year"
    if ((Test-Path (Join-Path $dest 'RevitAPI.dll')) -and (Test-Path (Join-Path $dest 'RevitAPIUI.dll')) -and -not $Force) {
        Write-Host "$year : already present ($dest)"; continue
    }
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    foreach ($pkg in $packages) {
        $id = $pkg.ToLowerInvariant()
        $all = (Invoke-RestMethod "$flat/$id/index.json").versions | Where-Object { $_ -like "$year.*" }
        if (-not $all) { Write-Warning "$year : no $pkg version found"; continue }
        $stable = @($all | Where-Object { $_ -notlike '*-*' })
        $pool = if ($stable.Count -gt 0) { $stable } else { @($all) }
        $ver = ($pool | Sort-Object { try { [version]($_ -replace '-.*$', '') } catch { [version]'0.0' } } | Select-Object -Last 1)

        $zip = Join-Path $work "$id.$ver.zip"
        Write-Host "$year : $pkg $ver"
        Invoke-WebRequest "$flat/$id/$ver/$id.$ver.nupkg" -OutFile $zip
        $ex = Join-Path $work "$id.$ver"
        Expand-Archive -Path $zip -DestinationPath $ex -Force

        $dllName = if ($pkg -like '*RevitAPIUI') { 'RevitAPIUI.dll' } else { 'RevitAPI.dll' }
        $cands = @(Get-ChildItem -Path (Join-Path $ex 'lib') -Recurse -Filter $dllName)
        if ($cands.Count -eq 0) { Write-Warning "$year : $dllName not found in $pkg $ver"; continue }
        # .NET Framework build for 2020-2024, newest .NET build for 2025+
        $pick = if ($year -le 2024) { $cands | Where-Object { $_.FullName -match 'net4' } | Select-Object -First 1 } else { $cands | Sort-Object FullName | Select-Object -Last 1 }
        if (-not $pick) { $pick = $cands | Select-Object -First 1 }
        Copy-Item $pick.FullName (Join-Path $dest $dllName) -Force
    }
}
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "`nDone. DLLs are in $Out"
