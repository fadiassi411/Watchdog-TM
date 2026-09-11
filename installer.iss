[Setup]
AppId={{A21B8367-73C7-4F76-B873-B986CA8B2441}
AppName=Watchdog TM V4.2.1
AppVersion=4.2.1
AppVerName=Watchdog TM V4.2.1
AppPublisher=MicroBrain
DefaultDirName={localappdata}\Programs\Watchdog TM
DefaultGroupName=Watchdog TM
PrivilegesRequired=lowest
OutputDir=..
OutputBaseFilename=Watchdog-TM-4.2.1-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=Watch Dog.ico
UninstallDisplayIcon={app}\Watchdog TM.exe
AppMutex=Global\WatchdogTMMonitoring
WizardStyle=modern
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
[Files]
Source: "app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Watchdog TM V4.2.1"; Filename: "{app}\Watchdog TM.exe"
Name: "{userdesktop}\Watchdog TM V4.2.1"; Filename: "{app}\Watchdog TM.exe"; Tasks: desktopicon
Name: "{group}\Customer guide"; Filename: "{app}\docs\Customer-guide.html"
[Run]
Filename: "{app}\Watchdog TM.exe"; Description: "Launch Watchdog TM V4.2.1"; Flags: nowait postinstall skipifsilent
