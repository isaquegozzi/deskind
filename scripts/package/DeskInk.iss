#ifndef PackageRoot
  #error PackageRoot define is required
#endif
#ifndef OutputDirectory
  #error OutputDirectory define is required
#endif
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{5F8DF68C-B483-43D1-A286-9CE0B50D5741}
AppName=DeskInk
AppVersion={#AppVersion}
AppPublisher=DeskInk
DefaultDirName={localappdata}\Programs\DeskInk
DefaultGroupName=DeskInk
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDirectory}
OutputBaseFilename=DeskInk-Setup-{#AppVersion}-win-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\DeskInk.WindowsApplication
UninstallDisplayIcon={app}\DeskInk.exe
VersionInfoVersion={#AppVersion}
VersionInfoProductName=DeskInk
VersionInfoDescription=DeskInk Windows Installer

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Área de Trabalho"; GroupDescription: "Atalhos adicionais:"; Flags: unchecked

[Files]
Source: "{#PackageRoot}\DeskInk.WindowsHost.exe"; DestDir: "{app}"; DestName: "DeskInk.exe"; Flags: ignoreversion
Source: "{#PackageRoot}\DeskInk-Android-{#AppVersion}.apk"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageRoot}\LEIA-ME.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\DeskInk"; Filename: "{app}\DeskInk.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\DeskInk"; Filename: "{app}\DeskInk.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\DeskInk.exe"; Description: "Abrir DeskInk"; Flags: nowait postinstall skipifsilent
