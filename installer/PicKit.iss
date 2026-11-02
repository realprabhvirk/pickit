; Per-user installer for PicKit. No administrator rights are needed, and nothing outside the
; user's own profile is touched. Build with tools/build-installer.ps1.

#define AppName "PicKit"
#define AppVersion "1.0.0"
#define Publisher "PicKit"
#define PublishDir GetEnv("LOCALAPPDATA") + "\PicKit-build\publish"
#define OutputFolder GetEnv("LOCALAPPDATA") + "\PicKit-build\installer"

[Setup]
AppId={{7C1E9B52-4C0B-4C6B-9A77-3E1E6F2A8D10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputFolder}
OutputBaseFilename=PicKit-{#AppVersion}-Setup
SetupIconFile=..\src\PicKit\Assets\PicKit.ico
UninstallDisplayIcon={app}\PicKit.exe
UninstallDisplayName={#AppName}
Compression=lzma2/fast
SolidCompression=no
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "explorermenu"; Description: "Show PicKit when right-clicking images in File Explorer"
Name: "openwith"; Description: "List PicKit under ""Open with"" for image files"; Flags: unchecked
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\PicKit.exe"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\PicKit.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PicKit.exe"; Parameters: "--register-menu"; Tasks: explorermenu; Flags: runhidden
Filename: "{app}\PicKit.exe"; Parameters: "--register-open-with"; Tasks: openwith; Flags: runhidden
Filename: "{app}\PicKit.exe"; Description: "Open PicKit"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\PicKit.exe"; Parameters: "--unregister"; Flags: runhidden; RunOnceId: "RemoveExplorerIntegration"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\PicKit"
