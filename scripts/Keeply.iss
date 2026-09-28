#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\Keeply-win-x64"
#endif

[Setup]
AppId={{2F43BC13-D619-4CA4-A018-3CA41D24578C}
AppName=Keeply
AppVersion={#MyAppVersion}
AppPublisher=Keeply
DefaultDirName={localappdata}\Programs\Keeply
DefaultGroupName=Keeply
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputBaseFilename=Keeply-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Keeply.exe
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Keeply"; Filename: "{app}\Keeply.exe"
Name: "{autodesktop}\Keeply"; Filename: "{app}\Keeply.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Keeply.exe"; Description: "Launch Keeply"; Flags: postinstall nowait skipifsilent
