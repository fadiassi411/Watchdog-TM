[Setup]
AppId={{AA929CEE-03B8-41B4-B697-34D502BD1EC9}
AppName=Watchdog TM Server
AppVersion=4.2.1
AppPublisher=MicroBrain
DefaultDirName={autopf}\Watchdog TM Server
DefaultGroupName=Watchdog TM
PrivilegesRequired=admin
OutputDir=..\artifacts
OutputBaseFilename=Watchdog-TM-Server-4.2.1-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\Watch Dog.ico
UninstallDisplayIcon={app}\Watchdog.TM.Web.exe
WizardStyle=modern
[Files]
Source: "..\server\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Install-Service.ps1"; DestDir: "{app}"
Source: "Remove-Service.ps1"; DestDir: "{app}"
Source: "Launch.vbs"; DestDir: "{app}"
[Icons]
Name: "{userdesktop}\Watchdog TM Server"; Filename: "{sys}\wscript.exe"; Parameters: """{app}\Launch.vbs"""; IconFilename: "{app}\Watchdog.TM.Web.exe"
Name: "{group}\Watchdog TM Server"; Filename: "{sys}\wscript.exe"; Parameters: """{app}\Launch.vbs"""; IconFilename: "{app}\Watchdog.TM.Web.exe"
[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Remove-Service.ps1"""; Flags: runhidden waituntilterminated
[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer;
begin
  if CurStep = ssPostInstall then begin
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\Install-Service.ps1') + '" -InstallDirectory "' + ExpandConstant('{app}') + '" -LegacyDatabase "' + ExpandConstant('{localappdata}\WatchdogTM\watchdog.db') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      RaiseException('Server installation failed. Close the desktop Watchdog TM and run the installer again. Your original database is retained.');
  end;
end;
