#define MyAppName "中山大学深圳校区图书管理系统"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Database Course"
#define MyAppExeName "LibrarySystem.Desktop.exe"

[Setup]
AppId={{3D0D4E0B-38E0-46F5-A7A9-80B5693D1D16}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\LibrarySystem
DefaultGroupName={#MyAppName}
OutputDir=out
OutputBaseFilename=LibrarySystemSetup
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "out\desktop\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "out\server\*"; DestDir: "{app}\server"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "out\database\*"; DestDir: "{app}\database"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "out\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "out\docker-compose.yml"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
