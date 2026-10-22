; Inno Setup script for Mouse Clicker.
; Built by .github/workflows/release.yml with:
;   ISCC /DAppVersion=<version> /DSourceDir=<folder with MouseClicker.exe> installer\MouseClicker.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\portable"
#endif

[Setup]
; Keep this GUID fixed so upgrades replace the previous install.
AppId={{B7E2C1A4-5F3D-4C8E-9A61-2D7F0E4B8C19}
AppName=Mouse Clicker
AppVersion={#AppVersion}
AppVerName=Mouse Clicker {#AppVersion}
AppPublisher=Maciejul
DefaultDirName={autopf}\Mouse Clicker
DefaultGroupName=Mouse Clicker
UninstallDisplayIcon={app}\MouseClicker.exe
SetupIconFile=..\src\MouseClicker\Assets\app.ico
OutputBaseFilename=MouseClicker-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Per-user install by default, so no admin prompt. The user can still pick an all-users folder.
PrivilegesRequiredOverridesAllowed=dialog
PrivilegesRequired=lowest
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\MouseClicker.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Mouse Clicker"; Filename: "{app}\MouseClicker.exe"
Name: "{group}\Uninstall Mouse Clicker"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Mouse Clicker"; Filename: "{app}\MouseClicker.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MouseClicker.exe"; Description: "Launch Mouse Clicker"; Flags: nowait postinstall skipifsilent
