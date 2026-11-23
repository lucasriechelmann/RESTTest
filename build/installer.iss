; Inno Setup script for RESTTest. Built by build/release.ps1, which passes:
;   /DAppVersion=1.2.3   the version from RESTTest.csproj
;   /DSourceDir=...      the self-contained publish folder
;   /DOutputDir=...      where the setup exe is written
; Compile by hand only through that script, so the version and files always match.

#ifndef AppVersion
  #error AppVersion must be defined (run build/release.ps1)
#endif
#ifndef SourceDir
  #error SourceDir must be defined (run build/release.ps1)
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define AppName "RESTTest"
#define AppExe "RESTTest.exe"
#define AppPublisher "Lucas Riechelmann Ramos"
#define AppUrl "https://lucasriechelmann.github.io/RESTTest/"
#define RepoUrl "https://github.com/lucasriechelmann/RESTTest"

[Setup]
; Never change AppId: Windows uses it to recognise an upgrade of the same app.
AppId={{C928F379-4A03-44DE-AC92-DD661E559504}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}

; Per-user by default (no admin prompt); the dialog offers an all-users install instead.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

; The publish is self-contained x64: no .NET install needed, but 64-bit Windows 10/11 only.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-{#AppVersion}-setup-x64
SetupIconFile=..\RESTTest\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Upgrading while the app is open would fail to replace the exe, so ask to close it.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Straight in Programs, not a one-item folder, as single-app installers usually do.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; Collections, history and cookies in %APPDATA%\RestTest are the user's work and are left in place
; on uninstall on purpose; a reinstall picks them up again.
