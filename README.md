# DietPlanner — WinUI 3

Це адаптована версія DietPlanner, у якій **UI перенесено з WPF на WinUI 3 / Windows App SDK**.

## Архітектура

Бізнес-частина збережена:

- `Models/` — доменні моделі;
- `Data/` — EF Core + SQLite та початкові JSON-дані;
- `Services/` — бізнес-операції та контракти;
- `ViewModels/` — MVVM через CommunityToolkit.Mvvm.

Переписано presentation layer:

- `App.xaml` / `App.xaml.cs`;
- `MainWindow.xaml` / `MainWindow.xaml.cs`;
- усі `Views/*.xaml` та code-behind;
- `NavigationService`;
- converters;
- localization resource loading;
- XAML styles/theme.

WPF `DataGrid` замінено на WinUI `ListView` + XAML-розмітку, щоб не тягнути сторонні UI-бібліотеки.

## Версії проєкту

- .NET 10
- Windows App SDK `2.5.1`
- Windows SDK Build Tools `10.0.26100.9169`
- target framework: `net10.0-windows10.0.26100.0`
- minimum Windows: `10.0.19041.0`
- platform: `x64`
- deployment mode: **unpackaged** (`WindowsPackageType=None`)

## Що потрібно встановити на Windows

1. **Visual Studio 2026**.
2. У Visual Studio Installer → Workloads увімкнути **WinUI application development**.
3. Переконатися, що встановлений Windows SDK 26100.x.
4. У Windows увімкнути Developer Mode.
5. Встановити .NET 10 SDK.

Microsoft рекомендує саме WinUI application development workload для створення WinUI 3 застосунків; Developer Mode теж потрібен для стандартного середовища розробки. Див. офіційний quick start Microsoft Learn.

## Запуск — рекомендований спосіб

### 1. Розпакуй архів

Наприклад:

```text
C:\Projects\DietPlanner\
```

Усередині повинен бути:

```text
DietPlanner.csproj
DietPlanner.sln
App.xaml
MainWindow.xaml
...
```

### 2. Відкрий solution

Відкрий:

```text
DietPlanner.sln
```

Якщо Visual Studio не хоче відкривати solution, відкрий напряму:

```text
DietPlanner.csproj
```

### 3. Дочекайся NuGet restore

Visual Studio повинна підтягнути:

```text
Microsoft.WindowsAppSDK 2.5.1
Microsoft.Windows.SDK.BuildTools 10.0.26100.9169
CommunityToolkit.Mvvm 8.4.2
EntityFrameworkCore.Sqlite 10.0.12
...
```

### 4. Вибери x64

У верхній панелі Visual Studio:

```text
Debug | x64
```

Не вибирай `Any CPU`.

### 5. Вибери профіль запуску Unpackaged

У dropdown біля кнопки запуску вибери:

```text
Unpackaged
```

Для unpackaged WinUI 3 це важливо: Microsoft окремо вказує, що при запуску з Visual Studio потрібно використовувати саме **Unpackaged launch profile**.

### 6. Запусти

Натисни:

```text
F5
```

або:

```text
Ctrl + F5
```

При першому запуску NuGet/Windows App SDK можуть трохи довше готуватися.

## Запуск через PowerShell

Після встановлення .NET SDK можна перевірити:

```powershell
dotnet --version
dotnet --info
```

Потім перейти в папку проєкту:

```powershell
cd C:\Projects\DietPlanner
```

Очистити та відновити пакети:

```powershell
dotnet nuget locals all --clear
dotnet restore .\DietPlanner.csproj
```

Для цього проєкту основним способом запуску я рекомендую **Visual Studio → Unpackaged → F5**, а не ручний `dotnet run`, тому що WinUI 3 unpackaged запуск має додаткову Windows App SDK bootstrap/deployment логіку.

## Якщо Build падає через Windows SDK

Перевір, що встановлений SDK 26100.x.

У Visual Studio Installer:

```text
Modify
→ Individual components
→ Windows SDK
```

Проєкт зараз націлений на:

```xml
<TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
```

Якщо на ПК немає SDK 26100, встанови його або зміни target framework на встановлену версію SDK.

## Якщо отримав помилку Developer Mode

Windows Settings → System → Advanced → For developers → **Developer Mode = On**.

Після встановлення/оновлення Visual Studio workload або Windows App SDK перезапусти Visual Studio.

## Як виглядає ключова частина `.csproj`

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
  <TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
  <Platforms>x64</Platforms>
  <PlatformTarget>x64</PlatformTarget>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <WindowsPackageType>None</WindowsPackageType>
  <ApplicationManifest>app.manifest</ApplicationManifest>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>
```

і:

```xml
<PackageReference Include="Microsoft.WindowsAppSDK" Version="2.5.1" />
<PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.9169" />
```

## Якщо щось не запускається

Найперше надішли мені **повний текст першої помилки з Error List / Output**, а не тільки червоний рядок. Для WinUI 3 це важливо, бо помилка часто виникає ще під час генерації XAML або restore, а не в самому UI.

## Перевірка цього архіву

У поточному середовищі Windows/.NET Desktop toolchain недоступний, тому реальний WinUI build на Windows тут не виконувався.

Перед упаковкою було виконано статичну перевірку:

- XAML-файли коректно парсяться як XML;
- WPF namespace/API-релікти у `.cs` та `.xaml` відсутні;
- `DataGrid`, `WrapPanel`, `UniformGrid`, `DynamicResource`, `UpdateSourceTrigger` та WPF `RelativeSource` видалені;
- перевірено `Grid.Column` без відповідних `ColumnDefinitions`;
- `bin/` і `obj/` у пакет не включені.
](https://github.com/Xydik1otto/DietPlaner_NoAi)](https://github.com/Xydik1otto/DietPlaner_NoAi)](https://github.com/Xydik1otto/DietPlaner_NoAi)
