; Установщик Fluent Spotify (Inno Setup 6).
; Обычно собирается через installer\build-installer.ps1, который публикует приложение
; и передаёт сюда версию и путь к папке публикации:
;   ISCC.exe /DAppVersion=1.3.1 /DAppFileVersion=1.3.1.0 /DPublishDir=..\publish\win-x64 FluentSpotify.iss

#ifndef AppVersion
  #define AppVersion "1.3.1"
#endif
#ifndef AppFileVersion
  #define AppFileVersion "1.3.1.0"
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
; Диалог выбора языка — только если язык Windows не нашёлся среди переводов
ShowLanguageDialog=auto

[Languages]
; Язык мастера выбирается по языку Windows. Для китайского в Inno Setup нет
; встроенного перевода — для него используется английский.
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"

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

[CustomMessages]
DirNotEmpty=The folder "%1" already contains other files.%n%nFluent Spotify replaces the whole contents of its folder when it is installed, updated or removed. Choose an empty folder or a new folder, for example "%2".
russian.DirNotEmpty=Папка «%1» уже содержит другие файлы.%n%nПри установке, обновлении и удалении Fluent Spotify полностью заменяет содержимое своей папки. Выберите пустую или новую папку, например «%2».

[Code]
// /RELAUNCH=1 передаёт приложение при обновлении (Core/Updates/UpdateService.cs)
function RelaunchRequested: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

// [InstallDelete] и [UninstallDelete] очищают {app} целиком. Поэтому ставить можно только
// в пустую папку или в папку, где уже установлен Fluent Spotify, — иначе установка
// в «Документы» или корень диска удалила бы файлы пользователя.
function IsSafeAppDir(const Dir: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := True;
  if not DirExists(Dir) then Exit;
  if FileExists(AddBackslash(Dir) + '{#AppExeName}') then Exit;
  if FindFirst(AddBackslash(Dir) + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          Result := False;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectDir) and not IsSafeAppDir(WizardDirValue) then
  begin
    MsgBox(FmtMessage(CustomMessage('DirNotEmpty'), [WizardDirValue, AddBackslash(WizardDirValue) + '{#AppName}']), mbError, MB_OK);
    Result := False;
  end;
end;

// Тихая установка (обновление из приложения или /DIR=) не показывает страницу выбора папки —
// проверяем и здесь, чтобы /DIR="C:\" не очистил чужую папку
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not IsSafeAppDir(ExpandConstant('{app}')) then
    Result := FmtMessage(CustomMessage('DirNotEmpty'), [ExpandConstant('{app}'), AddBackslash(ExpandConstant('{app}')) + '{#AppName}']);
end;
