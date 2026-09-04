; Inno Setup script for FieldJobs
; Build with:  "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\installer.iss
; (build.ps1 does the publish + this compile in one step)

#define AppName "FieldJobs"
#define AppVersion "0.1.0"
#define AppPublisher "FieldJobs"
#define AppExeName "FieldJobs.exe"

[Setup]
AppId={{FDB821F1-6352-40CE-B5BB-97038423F5C2}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={localappdata}\Programs\FieldJobs
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
OutputDir=..\dist
OutputBaseFilename=FieldJobs-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
; The entire self-contained publish folder produced by build.ps1
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

; NOTE: Your data (database, attached files, backups) lives in
;   %LOCALAPPDATA%\FieldJobs
; It is deliberately NOT removed by the uninstaller so your records survive a reinstall.
