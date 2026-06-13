[Setup]
AppName=NetFileConverter
AppVersion=1.0
; ИСПРАВЛЕНО: Используем актуальную константу commonpf
DefaultDirName={commonpf}\NetFileConverter
DefaultGroupName=NetFileConverter
UninstallDisplayIcon={app}\NetFileConverter.exe
Compression=lzma2
SolidCompression=yes
OutputDir=.\installer_output
OutputBaseFilename=NetFileConverter_Setup
; Явно указываем, что нам нужны права администратора для установки в Program Files
PrivilegesRequired=admin
; НОВОЕ: Задаем иконку для самого файла инсталлятора
SetupIconFile=D:\Projects\DotNet\NetFileConverter\icons\netlists.ico

[Files]
Source: "D:\Projects\DotNet\NetFileConverter\bin\Release\net8.0-windows\win-x64\publish\NetFileConverter.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\NetFileConverter"; Filename: "{app}\NetFileConverter.exe"
; ИСПРАВЛЕНО: Ярлыки создаются в общесистемных папках (common), что убирает ворнинг
Name: "{commonstartup}\NetFileConverter"; Filename: "{app}\NetFileConverter.exe"
Name: "{commondesktop}\NetFileConverter"; Filename: "{app}\NetFileConverter.exe"

[Run]
Filename: "{app}\NetFileConverter.exe"; Description: "Запустить NetFileConverter сейчас"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill', '/f /im NetFileConverter.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill', '/f /im NetFileConverter.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
