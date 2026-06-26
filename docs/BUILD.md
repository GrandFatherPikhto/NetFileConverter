# Основные команды по сборке и настройке проекта

## Очистка всей сборки

### Самая жёсткая:

```bash
Remove-Item -Recurse -Force .\obj\, .\bin\, .\NetFileConverter.Core\obj\, .\NetFileConverter.Core\bin\, .\NetFileConverter.Core.Tests\obj\, .\NetFileConverter.Core.Tests\bin\
```

или пошагово:

```bash
Remove-Item -Recurse -Force .\obj\, .\bin\
Remove-Item -Recurse -Force .\NetFileConverter.Core.Tests\obj\, .\NetFileConverter.Core.Tests\bin\
```

## Создание .sln-файла для управления процессом сборки

###  За что отвечает .sln

Файл .sln (Solution / Решение) — это, по сути, конфигурационный манифест и пульт управления процессом сборки для .NET CLI и сред разработки (Visual Studio, Rider).
Сам по себе он не содержит исходного кода и не компилируется, но выполняет три критические функции управления:

#### 1. Явное определение границ (Сборка по списку)

Без .sln утилита dotnet build работает "вслепую" на основе файловой структуры: она сканирует папки и компилирует всё, что найдет.
Файл .sln переводит сборку на декларативный режим. Он говорит компилятору: «Вот точный список проектов, которые существуют в этой экосистеме. Работай только с ними и строго в их границах».

#### 2. Управление порядком и графом зависимостей

Файл решения анализирует теги <ProjectReference> внутри ваших проектов и строит правильную очередь компиляции. Он гарантирует, что:

* Сначала полностью соберется NetFileConverter.Core.
* Только потом начнется сборка NetFileConverter.Core.Tests (так как тесты зависят от Core).
* Процессы сборки не будут пытаться одновременно писать файлы в одни и те же папки, вызывая конфликты блокировки.

#### 3. Разделение конфигураций
Через .sln управляются режимы сборки всего решения целиком — например, массовое переключение всех проектов из Debug в Release или выбор целевой платформы (x64, Any CPU).
------------------------------

#### 4. Пример настройки сборки

```bash
# Выйдите в корень, где лежит ваш .csproj
dotnet new sln -n NetFileConverter

# Принудительно добавьте главный проект в решение
dotnet sln add NetFileConverter.csproj

# Добавьте тестовый проект из вложенной папки
dotnet sln add NetFileConverter.Core.Tests/NetFileConverter.Core.Tests.csproj
```

## Создание новых подпроектов

### Пример

Создать проект Infrastructure:

```bash
dotnet new classlib -n NetFileConverter.Infrastructure -f net10.0
dotnet sln add NetFileConverter.Infrastructure/NetFileConverter.Infrastructure.csproj
```

Добавить ссылки на Core:

```bash
dotnet add NetFileConverter.Infrastructure/NetFileConverter.Infrastructure.csproj reference NetFileConverter.Core/NetFileConverter.Core.csproj
```

## 🛠 Разработка и сборка

### Подготовка окружения

Установите .NET 10 SDK, затем:

```bash
dotnet restore
```

### Тестовая сборка (Debug)

Важно указать файл проекта!

```bash
dotnet run --project NetFileConverter.csproj
```

### Публикация (Release) – один .exe-файл

```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishReadyToRun=true /p:IncludeNativeLibrariesForSelfExtract=true
```

или, если прежний .sln-файл содержит конфигурацию старого проекта (проект был обновлён)

```bash
 dotnet publish NetFileConverter.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishReadyToRun=true /p:IncludeNativeLibrariesForSelfExtract=true
```

Так же, можно обновить .sln файл:

```bash
dotnet new sln -n NetFileConverter
dotnet sln add NetFileConverter.csproj
```

Результат: `bin\Release\net8.0-windows\win-x64\publish\NetFileConverter.exe`

> **Важно:** `appsettings.json` из папки публикации не используется – программа при первом запуске создаст свой конфиг в `%APPDATA%\NetFileConverter\`. Это гарантирует, что обычный пользователь сможет изменять настройки даже при установке в `Program Files`.

### Сборка инсталлятора (Inno Setup)

1. Установите **Inno Setup** (бесплатно).
2. Поместите скрипт `installer.iss` в корень проекта (см. пример ниже).
3. Откройте `installer.iss` в Inno Setup Compiler и нажмите `Ctrl+F9` (Compile).
4. Готовый установщик `NetFileConverter_Setup.exe` появится в папке `.\installer_output\`.

#### Пример `installer.iss` (адаптируйте пути под свою структуру)

```iss
[Setup]
AppName=NetFileConverter
AppVersion=1.3
DefaultDirName={pf}\NetFileConverter
DefaultGroupName=NetFileConverter
UninstallDisplayIcon={app}\NetFileConverter.exe
OutputDir=installer_output
OutputBaseFilename=NetFileConverter_Setup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin

[Files]
Source: "bin\Release\net8.0-windows\win-x64\publish\NetFileConverter.exe"; DestDir: "{app}"
; НЕ копируем appsettings.json – он будет создан приложением в AppData

[Icons]
Name: "{commonstartup}\NetFileConverter"; Filename: "{app}\NetFileConverter.exe"
Name: "{commondesktop}\NetFileConverter"; Filename: "{app}\NetFileConverter.exe"

[Registry]
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\NetFileConverter.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\NetFileConverter.exe"

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill', '/f /im NetFileConverter.exe', '', 0, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
```

---
