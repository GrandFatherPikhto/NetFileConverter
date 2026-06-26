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