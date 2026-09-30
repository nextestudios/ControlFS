; ControlFS — instalador por usuário (sem administrador). Gerado por build\Publish-ControlFS.ps1:
;   ISCC /DAppVersion=0.1.0 /DSourceDir=<pasta do publish> /DOutputDir=<dist> ControlFS.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0.0"
#endif
#ifndef SourceDir
  #error SourceDir (pasta do dotnet publish) é obrigatório
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "ControlFS"
#define AppExe "ControlFS.exe"
#define RepoUrl "https://github.com/nextestudios/ControlFS"

[Setup]
; Nunca altere o AppId: é por ele que as atualizações encontram a instalação existente.
AppId={{29C18AD1-61FD-4D75-A2F9-1EBA66E7192F}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=nextestudios
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#NumericVersion}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UsePreviousAppDir=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=ControlFS-Setup-x64
SetupIconFile=..\assets\controlfs.ico
; O ícone do atalho vem do controlfs.ico ao lado do app (um caminho novo para o Windows) e o instalador avisa o Shell no fim:
; o cache de ícones do Windows guarda a imagem antiga pelo caminho do .exe e sobrevivia à reinstalação (#187).
ChangesAssociations=yes
UninstallDisplayIcon={app}\controlfs.ico
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
UsePreviousLanguage=yes
; As atualizações rodam em silêncio a partir do app; o Restart Manager fecha o app se ainda estiver aberto.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ptbr.LaunchApp=Abrir o ControlFS
en.LaunchApp=Open ControlFS
ptbr.DesktopIcon=Criar atalho na Área de Trabalho
en.DesktopIcon=Create a desktop shortcut

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Marcador: indica ao app que ele foi instalado (habilita a atualização automática).
Source: "ControlFS.installed"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\controlfs.ico"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\controlfs.ico"; Tasks: desktopicon

[Registry]
; Links controlfs://start|stop|show para automação (ProtocolRegistration registra as mesmas chaves ao iniciar).
Root: HKCU; Subkey: "Software\Classes\controlfs"; ValueType: string; ValueData: "URL:ControlFS"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\controlfs"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\controlfs\DefaultIcon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"
Root: HKCU; Subkey: "Software\Classes\controlfs\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent
; Atualização "instalar e reiniciar" (o app passa /RELAUNCH=1): reabre o app depois da instalação silenciosa.
Filename: "{app}\{#AppExe}"; Parameters: "--relaunch"; Flags: nowait; Check: ShouldRelaunch

[UninstallDelete]
; Somente o cache de downloads de atualização do próprio app; preferências do usuário são preservadas.
Type: filesandordirs; Name: "{localappdata}\ControlFS\updates"

[Code]
function ShouldRelaunch: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:RELAUNCH|0}') = '1');
end;
