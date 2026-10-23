<#
  Builds the Revit 2027 exporter (.NET 10) from the command line.
  Visual Studio 2022 cannot target .NET 10 (error NETSDK1209), but the dotnet CLI can once the .NET 10 SDK is installed.
  Use the same SDK / Visual Studio 2026 you used for your earlier .NET 10 project.
#>
$sdks = (& dotnet --list-sdks) 2>$null
if (-not ($sdks | Select-String '^10\.')) {
    Write-Host "No .NET 10 SDK found. Installed SDKs:`n$sdks`nInstall it from https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Yellow
    exit 1
}
dotnet build "$PSScriptRoot\Downgrader.Export2027\Downgrader.Export2027.csproj" -c Release
if ($LASTEXITCODE -eq 0) { Write-Host "`nBuilt. The .addin and DLLs were copied to %AppData%\Autodesk\Revit\Addins\2027" -ForegroundColor Green }
