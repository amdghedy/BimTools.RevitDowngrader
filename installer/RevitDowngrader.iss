; Revit Downgrader unified per-user installer.
; Build the payload first with build-installer-payload.ps1.
#define AppName "BIM TOOLS Revit Downgrader"
#define AppVersion "1.0.0"
#define Publisher "BIM TOOLS"

[Setup]
AppId={{5B9A18DD-35B8-4B88-9E1D-8A8B4A6B5C71}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={userappdata}\BIM TOOLS\Revit Downgrader
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=output
OutputBaseFilename=BIMTOOLS-RevitDowngrader-AllVersions-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
Uninstallable=yes
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "payload\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins"; Flags: recursesubdirs createallsubdirs ignoreversion

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    MsgBox('Installation files have been copied for Revit 2020–2027. Restart Revit to load the installed add-ins. Only versions installed on this computer can be used. Review the README for known limitations.', mbInformation, MB_OK);
end;
