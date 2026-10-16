; Установщик Fluent Spotify (Inno Setup 6).
; Обычно собирается через installer\build-installer.ps1, который публикует приложение
; и передаёт сюда версию и путь к папке публикации:
;   ISCC.exe /DAppVersion=0.1.03-beta /DAppFileVersion=0.1.3.0 /DPublishDir=..\publish\win-x64 FluentSpotify.iss

#ifndef AppVersion
  #define AppVersion "0.1.03-beta"
#endif
#ifndef AppFileVersion
  #define AppFileVersion "0.1.3.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish\win-x64"
#endif

#define AppName "Fluent Spotify"
#define AppExeName "fluent-spotify.exe"
#define AppPublisher "justaprunk"
#define AppUrl "https://github.com/justaprunk/fluent-spotify"

[Setup]
; Постоянный AppId: по нему новые версии обновляют уже установленную
AppId={{6F1B6C2E-9A4D-4E7B-8C51-3D2F0A9E7B14}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppFileVersion}
VersionInfoProductVersion={#AppFileVersion}
VersionInfoProductTextVersion={#AppVersion}

; Установка для текущего пользователя, без прав администратора
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809 — минимум для Windows App SDK
MinVersion=10.0.17763

SetupIconFile=..\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

OutputDir=Output
OutputBaseFilename=FluentSpotify-{#AppVersion}-Setup-x64

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; Обновление из приложения: тихая установка с /RELAUNCH=1 снова запускает приложение
Filename: "{app}\{#AppExeName}"; Flags: nowait skipifnotsilent; Check: RelaunchRequested

[InstallDelete]
; Убираем файлы прошлой версии, чтобы не оставались устаревшие DLL
Type: filesandordirs; Name: "{app}\*"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
// /RELAUNCH=1 передаёт приложение при обновлении (Core/Updates/UpdateService.cs)
function RelaunchRequested: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;
