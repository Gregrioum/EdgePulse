#ifndef Arch
  #define Arch "x64"
#endif
#ifndef Payload
  #define Payload "..\work\publish\win-" + Arch
#endif
#ifndef Destination
  #define Destination "..\release"
#endif
#define AppVersion "1.2.0"

[Setup]
AppId={{E40B417F-2F02-40C0-AC44-36E6EC3F7DD8}
AppName=EdgePulse
AppVersion={#AppVersion}
AppPublisher=EdgePulse
DefaultDirName={localappdata}\Programs\EdgePulse
DefaultGroupName=EdgePulse
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.22000
OutputDir={#Destination}
OutputBaseFilename=EdgePulse-{#AppVersion}-win-{#Arch}-Setup
SetupIconFile=..\src\EdgePulse\Assets\EdgePulse.ico
UninstallDisplayIcon={app}\EdgePulse.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\EdgePulse.Widget
LicenseFile=..\LICENSE.txt

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start EdgePulse when I sign in to Windows"; Flags: unchecked

[Files]
Source: "{#Payload}\EdgePulse.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\DOTNET-LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\DOTNET-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\EdgePulse"; Filename: "{app}\EdgePulse.exe"
Name: "{autodesktop}\EdgePulse"; Filename: "{app}\EdgePulse.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EdgePulse"; ValueData: """{app}\EdgePulse.exe"" --startup"; Tasks: startup

[Run]
Filename: "{app}\EdgePulse.exe"; Description: "Launch EdgePulse"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'EdgePulse', StartupCommand) then
      if Pos(Lowercase(ExpandConstant('{app}\EdgePulse.exe')), Lowercase(StartupCommand)) > 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'EdgePulse');
  end;
end;
