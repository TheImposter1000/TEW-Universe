#define AppVersion GetEnv("APP_VERSION")
#ifndef AppVersion
  #error APP_VERSION environment variable must be set by the build workflow.
#endif

[Setup]
AppId={{BA305AFB-650B-4BAA-BEDB-68B06CD3E6FB}
AppName=TEW Universe Studio
AppVersion={#AppVersion}
AppPublisher=TEW Universe Community Project
AppPublisherURL=https://github.com/TheImposter1000/TEW-Universe
AppSupportURL=https://github.com/TheImposter1000/TEW-Universe/issues
DefaultDirName={localappdata}\Programs\TEW Universe Studio
DefaultGroupName=TEW Universe Studio
DisableProgramGroupPage=yes
OutputDir=..\publish\installer
OutputBaseFilename=TEW-Universe-Setup-{#AppVersion}
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\TEWUniverse.Studio.exe
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "..\publish\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\updater\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\TEW Universe Studio"; Filename: "{app}\TEWUniverse.Studio.exe"
Name: "{autodesktop}\TEW Universe Studio"; Filename: "{app}\TEWUniverse.Studio.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\TEWUniverse.Studio.exe"; Description: "Launch TEW Universe Studio"; Flags: nowait postinstall skipifsilent
