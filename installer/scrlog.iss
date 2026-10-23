; Установщик Scrlog. Собирается в GitHub Actions (.github/workflows/build.yml):
;   ISCC.exe /DAppVersion=2.0.0 installer\scrlog.iss
; Ожидает собранную программу в ..\publish и ffmpeg.exe в ..\ffmpeg\x64 и ..\ffmpeg\x86.
; Установщик один на обе архитектуры: на 64-битной Windows ставится 64-битный ffmpeg, на 32-битной — 32-битный.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "Scrlog"
#define AppExe "scrlog.exe"

[Setup]
AppId={{6B1E3C52-8F4A-4D0E-9C61-2A7F5B3D9E10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Speedware
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x86compatible x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=Output
OutputBaseFilename=scrlog-setup-{#AppVersion}
SetupIconFile=..\Assets\scrlog.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Закрываем запущенный Scrlog перед обновлением (программа корректно завершит запись)
CloseApplications=yes
; Просим Explorer обновить иконки после установки (иначе ярлык может показывать старую из кэша)
ChangesAssociations=yes

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "autostart"; Description: "Запускать Scrlog при входе в Windows (для всех пользователей)"; GroupDescription: "Дополнительно:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\{#AppExe}.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\ffmpeg\x64\ffmpeg.exe"; DestDir: "{app}"; Flags: ignoreversion; Check: Is64BitInstallMode
Source: "..\ffmpeg\x86\ffmpeg.exe"; DestDir: "{app}"; Flags: ignoreversion; Check: not Is64BitInstallMode

[Dirs]
; Настройки и журнал общие для всех пользователей ПК
Name: "{commonappdata}\Scrlog"; Permissions: users-modify
Name: "{sd}\ScreenLogs"; Permissions: users-modify; Flags: uninsneveruninstall

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Scrlog"; \
  ValueData: """{app}\{#AppExe}"" --minimized"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
; Сброс кэша иконок Windows 10/11
Filename: "{sys}\ie4uinit.exe"; Parameters: "-show"; Flags: runhidden skipifdoesntexist runasoriginaluser
Filename: "{app}\{#AppExe}"; Description: "Запустить {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "KillScrlog"

[Code]
// .NET Framework 4.7.2 входит в Windows 10 1803+, но на старых сборках его может не быть
function IsDotNet472Installed: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
            and (Release >= 461808);
end;

function InitializeSetup: Boolean;
begin
  Result := IsDotNet472Installed;
  if not Result then
    MsgBox('Для работы Scrlog нужен .NET Framework 4.7.2 или новее.' + #13#10 +
           'Установите его с сайта Microsoft и запустите установку снова.', mbError, MB_OK);
end;
