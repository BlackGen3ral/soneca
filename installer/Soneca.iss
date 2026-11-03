#ifndef AppVersion
 #define AppVersion "1.0.1"
#endif
[Setup]
AppId={{E91D840B-33AE-467D-93ED-7D8EA88B2EB1}
AppName=Soneca
AppVersion={#AppVersion}
AppPublisher=Artho
AppPublisherURL=https://github.com/BlackGen3ral/soneca
DefaultDirName={localappdata}\Programs\Soneca
DefaultGroupName=Soneca
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=Soneca-Setup
SetupIconFile=..\Soneca\Assets\soneca.ico
UninstallDisplayIcon={app}\Soneca.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\Soneca
[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; Flags: unchecked
[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Soneca"; Filename: "{app}\Soneca.exe"
Name: "{autodesktop}\Soneca"; Filename: "{app}\Soneca.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\Soneca.exe"; Description: "Abrir Soneca"; Flags: nowait postinstall skipifsilent
Filename: "{app}\Soneca.exe"; Flags: nowait; Check: IsUpdate
[Code]
function IsUpdate: Boolean;
begin
 Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;
[UninstallRun]
Filename: "{app}\Soneca.exe"; Parameters: "--cancel-schedule"; Flags: runhidden waituntilterminated; RunOnceId: "CancelSonecaSchedule"
