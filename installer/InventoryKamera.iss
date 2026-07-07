; Inno Setup script for Inventory Kamera.
;
; Version is injected by the build (release.yml) via the command line, e.g.
;   ISCC /DAppVersion=1.4.5 installer\InventoryKamera.iss
; Falling back to 0.0.0 for a bare local compile so the script still builds.
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "Inventory Kamera"
#define AppExeName "InventoryKamera.exe"
#define AppPublisher "taiwenlee"
#define AppURL "https://github.com/taiwenlee/Inventory_Kamera"

[Setup]
; AppId uniquely identifies this application for upgrade-in-place and uninstall tracking.
; It must stay constant across all future releases -- do NOT regenerate it.
AppId={{425C7575-32DA-4BD0-9BBC-A653F5124774}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases

; Per-user install: no install-time UAC prompt, and the install directory stays user-writable
; so the app's relative writes (./logging, GOOD exports next to the exe) keep working. The app
; still self-elevates at runtime via its requireAdministrator manifest -- install privilege and
; run privilege are independent.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

OutputDir=Output
OutputBaseFilename=InventoryKamera-Setup-{#AppVersion}
SetupIconFile=..\InventoryKamera\Item_Special_Kamera.ico
WizardStyle=modern
Compression=lzma2
SolidCompression=yes

; The app is published -r win-x64; only install/run on 64-bit Windows.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Grabs the whole publish output: the single-file InventoryKamera.exe plus the loose
; tessdata\*.traineddata (single-file can't embed those since they're None/CopyToOutput content).
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
