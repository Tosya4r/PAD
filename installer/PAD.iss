#define AppName "PAD"
#define AppVersion "1.0.0"

[Setup]
AppId={{6F1A7C52-3B1E-4C0D-9A52-7D0E1F4B9A10}
AppName={#AppName} — Personal Administration Desk
AppVersion={#AppVersion}
AppPublisher=PAD
DefaultDirName={autopf}\PAD
DefaultGroupName=PAD
DisableProgramGroupPage=yes
OutputDir=..\installer-out
OutputBaseFilename=PAD-Setup
SetupIconFile=..\PAD.App\Assets\pad.ico
UninstallDisplayIcon={app}\PAD.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать значок на рабочем столе"; GroupDescription: "Дополнительно:"

[Files]
Source: "..\publish\PAD.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\PAD"; Filename: "{app}\PAD.exe"
Name: "{autodesktop}\PAD"; Filename: "{app}\PAD.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PAD.exe"; Description: "Запустить PAD"; Flags: nowait postinstall skipifsilent
