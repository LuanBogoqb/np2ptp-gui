# Modern Theme (Fluent / Windows 11) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a second, user-selectable UI theme family, "Modern" (Fluent/Windows 11 look, real Mica window backdrop, Windows accent color, light/dark auto-following the OS setting), alongside the existing XP Luna theme, via the WPF-UI (`Wpf.Ui`) NuGet package.

**Architecture:** `MainWindow`/`FetchOptionsDialog` become `Wpf.Ui.Controls.FluentWindow` unconditionally (a `Window` subclass, so this is safe even for the XP family). `App.xaml.cs` branches on `AppConfig.ThemeFamily` at startup: `"XpLuna"` keeps today's exact code path (`Np2ptpGui.Themes.ThemeManager`); `"Modern"` uses a new parallel `Np2ptpGui.Themes.ModernThemeManager` that wraps WPF-UI's `ApplicationThemeManager`/`ApplicationAccentColorManager` and sets `WindowBackdropType.Mica`. Switching families is restart-only; light/dark switching within whichever family is active stays live, reusing the existing `WindowsThemeService`.

**Tech Stack:** .NET 8 WPF, `Wpf.Ui` (WPF-UI) NuGet package (new dependency, Modern-only), xUnit for the config round-trip test.

## Global Constraints

- Full design spec: `docs/superpowers/specs/2026-07-09-modern-theme-design.md` — read it before starting if anything below is ambiguous.
- `AppConfig.ThemeFamily` defaults to `"XpLuna"` — existing installs/behavior must not change until the user opts into Modern.
- Switching `ThemeFamily` in Settings takes effect on next launch only — no live re-skin across families.
- Light/dark switching *within* whichever family is active must remain live (no regression to the already-shipped XP Luna live-switch behavior).
- `dotnet.exe` is never on PATH in this environment — always invoke it as `"C:\Program Files\dotnet\dotnet.exe"`. Use the PowerShell tool (not Bash) for `dotnet build`/`dotnet test`/launching the app, per this repo's established environment quirks.
- Every task must build cleanly (`0 Aviso(s)` / `0 Erro(s)`) and be manually verified in the real running app before being considered done — this repo has twice shipped theme code that passed review but broke on first real use (see the XP Luna plan's Task 3 and its post-merge ListView/Label/TabItem gap); do not skip the manual verification steps in any task below.

---

### Task 1: `AppConfig.ThemeFamily` + `ConfigStore` persistence

**Files:**
- Modify: `src/Np2ptpGui/Models/AppConfig.cs`
- Modify: `src/Np2ptpGui/Services/ConfigStore.cs`
- Test: `tests/Np2ptpGui.Tests/Services/ConfigStoreTests.cs`

**Interfaces:**
- Produces: `AppConfig.ThemeFamily` (`string`, default `"XpLuna"`) — later tasks (3, 4, 5) read/write this field by name.

- [ ] **Step 1: Add the field to `AppConfig`**

Modify `src/Np2ptpGui/Models/AppConfig.cs` — add a line after `KeepStoreByDefault`:

```csharp
namespace Np2ptpGui.Models;

public sealed class AppConfig
{
    public string BinaryPath { get; set; } = "";
    public string DefaultDownloadFolder { get; set; } = "";
    public string StoreFolder { get; set; } = "";
    public string DefaultListenAddress { get; set; } = "/ip4/0.0.0.0/udp/0/quic-v1";
    public string TrackerUrl { get; set; } = "";
    public bool AlwaysUseDownloadDefaults { get; set; } = false;
    public bool KeepStoreByDefault { get; set; } = true;
    public string ThemeFamily { get; set; } = "XpLuna";
}
```

- [ ] **Step 2: Write the failing tests**

Modify `tests/Np2ptpGui.Tests/Services/ConfigStoreTests.cs`. Add a `ThemeFamily` assertion to the
existing defaults test, extend the round-trip test, and add a new test for the "unrecognized value
falls back to default" rule from the spec's Error Handling section:

```csharp
    [Fact]
    public void Load_WhenNoFileExists_ReturnsDefaults()
    {
        var dir = NewTempDir();
        var store = new ConfigStore(dir);

        var config = store.Load();

        Assert.Equal("", config.BinaryPath);
        Assert.Equal("/ip4/0.0.0.0/udp/0/quic-v1", config.DefaultListenAddress);
        Assert.False(config.AlwaysUseDownloadDefaults);
        Assert.True(config.KeepStoreByDefault);
        Assert.Equal("XpLuna", config.ThemeFamily);
        Directory.Delete(dir, recursive: true);
    }
```

```csharp
    [Fact]
    public void Load_WhenThemeFamilyIsUnrecognized_FallsBackToDefault()
    {
        var dir = NewTempDir();
        var store = new ConfigStore(dir);
        var filePath = Path.Combine(dir, "config.ini");
        File.WriteAllText(filePath, "ThemeFamily=SomeFutureThemeNotYetInvented\n");

        var config = store.Load();

        Assert.Equal("XpLuna", config.ThemeFamily);
        Directory.Delete(dir, recursive: true);
    }
```

In `SaveThenLoad_RoundTripsAllFields`, add `ThemeFamily = "Modern",` to the `original` object literal
and `Assert.Equal(original.ThemeFamily, loaded.ThemeFamily);` after the existing assertions.

- [ ] **Step 3: Run tests to verify the new ones fail**

Run (PowerShell tool):
```
cd "C:\Users\Luan Bogo\np2ptp-gui"; & "C:\Program Files\dotnet\dotnet.exe" test --filter "FullyQualifiedName~ConfigStoreTests" 2>&1 | Select-Object -Last 30
```
Expected: `Load_WhenNoFileExists_ReturnsDefaults`, `SaveThenLoad_RoundTripsAllFields` FAIL
(`ThemeFamily` doesn't exist yet / doesn't round-trip); `Load_WhenThemeFamilyIsUnrecognized_FallsBackToDefault`
FAILs the same way once the field exists but before Step 4's parsing logic is added.

- [ ] **Step 4: Implement `ConfigStore` support**

Modify `src/Np2ptpGui/Services/ConfigStore.cs` — add one `case` to the `Load()` switch and one line
to the `Save()` array:

```csharp
                    case nameof(AppConfig.KeepStoreByDefault):
                        config.KeepStoreByDefault = TryParseBool(value, config.KeepStoreByDefault);
                        break;
                    case nameof(AppConfig.ThemeFamily):
                        config.ThemeFamily = value is "XpLuna" or "Modern" ? value : config.ThemeFamily;
                        break;
```

```csharp
                $"{nameof(AppConfig.AlwaysUseDownloadDefaults)}={config.AlwaysUseDownloadDefaults.ToString(CultureInfo.InvariantCulture)}",
                $"{nameof(AppConfig.KeepStoreByDefault)}={config.KeepStoreByDefault.ToString(CultureInfo.InvariantCulture)}",
                $"{nameof(AppConfig.ThemeFamily)}={config.ThemeFamily}",
```

- [ ] **Step 5: Run tests to verify they pass**

Run:
```
cd "C:\Users\Luan Bogo\np2ptp-gui"; & "C:\Program Files\dotnet\dotnet.exe" test --filter "FullyQualifiedName~ConfigStoreTests" 2>&1 | Select-Object -Last 30
```
Expected: all `ConfigStoreTests` PASS (5 tests: the 4 pre-existing plus the new one).

- [ ] **Step 6: Commit**

```bash
git add src/Np2ptpGui/Models/AppConfig.cs src/Np2ptpGui/Services/ConfigStore.cs tests/Np2ptpGui.Tests/Services/ConfigStoreTests.cs
git commit -m "feat: add ThemeFamily config field with safe-default fallback"
```

---

### Task 2: Add the WPF-UI NuGet dependency

**Files:**
- Modify: `src/Np2ptpGui/Np2ptpGui.csproj`

**Interfaces:**
- Produces: `Wpf.Ui` package reference — Tasks 3 and 4 use `Wpf.Ui.Controls.FluentWindow`,
  `Wpf.Ui.Controls.WindowBackdropType`, `Wpf.Ui.Appearance.ApplicationThemeManager`,
  `Wpf.Ui.Appearance.ApplicationTheme`, `Wpf.Ui.Appearance.ApplicationAccentColorManager`.

This is deliberately its own tiny task — isolating package resolution means any version/restore
problem surfaces before any code depends on it.

- [ ] **Step 1: Add the package**

Run (PowerShell tool):
```
cd "C:\Users\Luan Bogo\np2ptp-gui"; & "C:\Program Files\dotnet\dotnet.exe" add "src\Np2ptpGui\Np2ptpGui.csproj" package WPF-UI
```
This resolves and pins whatever the current latest stable 3.x version is — do not hand-edit a
version number in, let the command write it. Expected output ends with something like
`info : PackageReference for package 'WPF-UI' version '3.0.x' added to file '...Np2ptpGui.csproj'.`

- [ ] **Step 2: Verify the csproj changed as expected**

Read `src/Np2ptpGui/Np2ptpGui.csproj` — confirm a new `<PackageReference Include="WPF-UI" Version="..." />`
line was added to an `<ItemGroup>` (the SDK tooling may create a new `<ItemGroup>` for it, that's fine).

- [ ] **Step 3: Confirm the project still builds clean**

Run:
```
cd "C:\Users\Luan Bogo\np2ptp-gui"; & "C:\Program Files\dotnet\dotnet.exe" build src\Np2ptpGui\Np2ptpGui.csproj -c Debug 2>&1 | Select-Object -Last 15
```
Expected: `Compilação com êxito.` / `0 Aviso(s)` / `0 Erro(s)`. If the restore fails (network, NuGet
source config, etc.) stop and report it — do not proceed to Task 3 with a broken restore.

- [ ] **Step 4: Commit**

```bash
git add src/Np2ptpGui/Np2ptpGui.csproj
git commit -m "build: add WPF-UI package reference for the upcoming Modern theme"
```

---

### Task 3: Swap `MainWindow`/`FetchOptionsDialog` to `FluentWindow`

**Files:**
- Modify: `src/Np2ptpGui/MainWindow.xaml`
- Modify: `src/Np2ptpGui/MainWindow.xaml.cs`
- Modify: `src/Np2ptpGui/Views/FetchOptionsDialog.xaml`
- Modify: `src/Np2ptpGui/Views/FetchOptionsDialog.xaml.cs`

**Interfaces:**
- Consumes: `Wpf.Ui` package from Task 2.
- Produces: `MainWindow : FluentWindow`, `FetchOptionsDialog : FluentWindow` — Task 4 sets
  `WindowBackdropType` and re-establishes the XP `Style` reference on these instances.

`FluentWindow` is itself a `Window` subclass, so this task alone changes nothing visible — its
whole purpose is to prove the base-class swap is safe for the already-shipped XP Luna theme
*before* any Modern-specific logic exists. Keep the existing `Style="{DynamicResource {x:Type Window}}"`
attribute on both windows exactly as-is for now (Task 4 removes it once there's a Modern branch to
account for).

- [ ] **Step 1: Swap `MainWindow`'s root element and base class**

Modify `src/Np2ptpGui/MainWindow.xaml` (add the `ui` namespace, change the root element from
`Window` to `ui:FluentWindow`, and its closing tag to match):

```xml
<ui:FluentWindow x:Class="Np2ptpGui.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:Np2ptpGui.Views"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        Title="np2ptp" Height="600" Width="900"
        Icon="app.ico"
        Style="{DynamicResource {x:Type Window}}">
    <TabControl>
        <TabItem Header="Downloads">
            <views:DownloadsView />
        </TabItem>
        <TabItem Header="Seeding">
            <views:SeedingView />
        </TabItem>
        <TabItem Header="Share">
            <views:ShareView />
        </TabItem>
        <TabItem Header="Settings">
            <views:SettingsView x:Name="SettingsTab" />
        </TabItem>
    </TabControl>
</ui:FluentWindow>
```

Modify `src/Np2ptpGui/MainWindow.xaml.cs`:

```csharp
namespace Np2ptpGui;

using Wpf.Ui.Controls;

public partial class MainWindow : FluentWindow
{
    public MainWindow() => InitializeComponent();
}
```

- [ ] **Step 2: Swap `FetchOptionsDialog`'s root element and base class**

Modify `src/Np2ptpGui/Views/FetchOptionsDialog.xaml` the same way:

```xml
<ui:FluentWindow x:Class="Np2ptpGui.Views.FetchOptionsDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:Np2ptpGui.Controls"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        Title="Opções do download" Height="280" Width="480"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize"
        Style="{DynamicResource {x:Type Window}}">
    <StackPanel Margin="10">
        <Label Content="Pasta de reconstrução (onde o arquivo final vai ficar)" />
        <DockPanel Margin="0,0,0,10">
            <Button DockPanel.Dock="Right" Content="Procurar..." Click="BrowseReconstructFolder_Click" />
            <TextBox x:Name="ReconstructFolderBox"
                     controls:HintBehavior.Hint="Ex: C:\Users\voce\Downloads" />
        </DockPanel>

        <Label Content="Pasta do store (chunks dessa transferência)" />
        <DockPanel Margin="0,0,0,10">
            <Button DockPanel.Dock="Right" Content="Procurar..." Click="BrowseStoreFolder_Click" />
            <TextBox x:Name="StoreFolderBox"
                     controls:HintBehavior.Hint="Ex: C:\Users\voce\np2ptp-store" />
        </DockPanel>

        <CheckBox x:Name="KeepStoreCheckBox" Content="Manter store depois de concluir" Margin="0,0,0,20" />

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="Cancelar" Width="80" Click="Cancel_Click" Margin="0,0,10,0" />
            <Button Content="OK" Width="80" IsDefault="True" Click="Ok_Click" />
        </StackPanel>
    </StackPanel>
</ui:FluentWindow>
```

Modify `src/Np2ptpGui/Views/FetchOptionsDialog.xaml.cs` — change only the base type on the class
declaration line, everything else in the file stays identical:

```csharp
namespace Np2ptpGui.Views;

using Wpf.Ui.Controls;

public partial class FetchOptionsDialog : FluentWindow
{
    public string ReconstructFolder { get; private set; } = "";
    public string StoreFolder { get; private set; } = "";
    public bool KeepStore { get; private set; }

    public FetchOptionsDialog(string defaultReconstructFolder, string defaultStoreFolder, bool defaultKeepStore)
    {
        InitializeComponent();
        ReconstructFolderBox.Text = defaultReconstructFolder;
        StoreFolderBox.Text = defaultStoreFolder;
        KeepStoreCheckBox.IsChecked = defaultKeepStore;
    }

    private void BrowseReconstructFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog(this) == true) ReconstructFolderBox.Text = dialog.FolderName;
    }

    private void BrowseStoreFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog(this) == true) StoreFolderBox.Text = dialog.FolderName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ReconstructFolder = ReconstructFolderBox.Text;
        StoreFolder = StoreFolderBox.Text;
        KeepStore = KeepStoreCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
```

Note: `RoutedEventArgs` needs `using System.Windows;` — check the top of the file still has it
(it does in the current file; do not remove it).

- [ ] **Step 3: Build**

Run:
```
cd "C:\Users\Luan Bogo\np2ptp-gui"; & "C:\Program Files\dotnet\dotnet.exe" build src\Np2ptpGui\Np2ptpGui.csproj -c Debug 2>&1 | Select-Object -Last 15
```
Expected: `0 Aviso(s)` / `0 Erro(s)`.

- [ ] **Step 4: Manually verify XP Luna is pixel-identical to before**

The current shipped app defaults to `ThemeFamily = "XpLuna"`, so this must render exactly as it did
before this task. Launch the real built app (PowerShell tool):
```
cd "C:\Users\Luan Bogo\np2ptp-gui"; Get-Process Np2ptpGui -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Process "C:\Users\Luan Bogo\np2ptp-gui\src\Np2ptpGui\bin\Debug\net8.0-windows\Np2ptpGui.exe"
```
Screenshot the main window and the Settings tab (reuse the `GetWindowRect`+`CopyFromScreen`
PowerShell pattern already used earlier in this project's history if no simpler tool is available).
Confirm: window background/foreground colors match the current XP Luna palette, all 4 tabs render
identically to the pre-Task-3 build, no exceptions on launch. Open `FetchOptionsDialog` (type
something in the Downloads link box, click "+ New download") and confirm it also renders unchanged.
If anything looks different, the base-class swap broke something — do not proceed to Task 4 until
this is pixel-identical to the pre-change build.

- [ ] **Step 5: Run the full test suite**

Run:
```
cd "C:\Users\Luan Bogo\np2ptp-gui"; & "C:\Program Files\dotnet\dotnet.exe" test 2>&1 | Select-String "Com falha|Aprovado"
```
Expected: all tests pass (retry once if only the known `ProcessRunnerTests` Ctrl-C flake fails —
documented pre-existing issue, unrelated to this change).

- [ ] **Step 6: Commit**

```bash
git add src/Np2ptpGui/MainWindow.xaml src/Np2ptpGui/MainWindow.xaml.cs src/Np2ptpGui/Views/FetchOptionsDialog.xaml src/Np2ptpGui/Views/FetchOptionsDialog.xaml.cs
git commit -m "refactor: base MainWindow and FetchOptionsDialog on FluentWindow"
```

---

### Task 4: `ModernThemeManager` + wire Modern into startup

**Files:**
- Create: `src/Np2ptpGui/Themes/ModernThemeManager.cs`
- Modify: `src/Np2ptpGui/App.xaml.cs`
- Modify: `src/Np2ptpGui/MainWindow.xaml`
- Modify: `src/Np2ptpGui/Views/FetchOptionsDialog.xaml`
- Modify: `src/Np2ptpGui/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `AppConfig.ThemeFamily` (Task 1), `FluentWindow` base class (Task 3),
  `Np2ptpGui.Themes.ThemeManager.Initialize(bool)`/`ApplyTheme(bool)` (existing, XP-only,
  untouched by this task), `Wpf.Ui.Appearance.ApplicationThemeManager`/`ApplicationAccentColorManager`,
  `Wpf.Ui.Controls.WindowBackdropType` (Task 2's package).
- Produces: `ModernThemeManager.Initialize(bool isLight)`, `ModernThemeManager.ApplyTheme(bool isLight)`,
  `ModernThemeManager.ApplyToWindow(FluentWindow window, string themeFamily)` — this last one is
  called for every `FluentWindow` this app creates (`MainWindow` in `App.xaml.cs`, `FetchOptionsDialog`
  in `MainViewModel`), and is what removes the need for the static XAML `Style` attribute.

The static `Style="{DynamicResource {x:Type Window}}"` attribute worked by coincidence when only
XP Luna existed (it's the only family that ever merges a resource keyed `{x:Type Window}`). Once
Modern exists, that key is never merged for Modern windows, so the attribute would silently resolve
to nothing there anyway — but to avoid depending on that coincidence, this task moves the
Style-reference decision into code, applied per-window, alongside the backdrop decision.

- [ ] **Step 1: Create `ModernThemeManager`**

Create `src/Np2ptpGui/Themes/ModernThemeManager.cs`:

```csharp
namespace Np2ptpGui.Themes;

using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

public static class ModernThemeManager
{
    public const string FamilyName = "Modern";

    public static void Initialize(bool isLight)
    {
        ApplicationThemeManager.Apply(isLight ? ApplicationTheme.Light : ApplicationTheme.Dark);
        ApplicationAccentColorManager.ApplyWindowsAccentColor();
    }

    public static void ApplyTheme(bool isLight)
    {
        ApplicationThemeManager.Apply(isLight ? ApplicationTheme.Light : ApplicationTheme.Dark);
    }

    public static void ApplyToWindow(FluentWindow window, string themeFamily)
    {
        if (themeFamily == FamilyName)
        {
            window.WindowBackdropType = WindowBackdropType.Mica;
        }
        else
        {
            window.WindowBackdropType = WindowBackdropType.None;
            window.SetResourceReference(Window.StyleProperty, typeof(Window));
        }
    }
}
```

If the compiler reports a different exact overload/signature for `ApplicationThemeManager.Apply`
or `ApplicationAccentColorManager.ApplyWindowsAccentColor` than shown here (WPF-UI's public API has
shifted slightly across 3.x versions), adjust the call to match what IntelliSense/the compiler
error shows — the intent (apply light/dark, apply the Windows accent color) stays the same either
way. Note this deviation in the task report if it happens.

- [ ] **Step 2: Remove the static `Style` attribute from both windows**

Modify `src/Np2ptpGui/MainWindow.xaml` — remove `Style="{DynamicResource {x:Type Window}}"` from
the root `<ui:FluentWindow ...>` element (keep every other attribute from Task 3 unchanged).

Modify `src/Np2ptpGui/Views/FetchOptionsDialog.xaml` — same removal on its root `<ui:FluentWindow ...>`
element.

- [ ] **Step 3: Wire the branch into `App.xaml.cs`**

Modify `src/Np2ptpGui/App.xaml.cs` — replace this block:

```csharp
            var themeService = new WindowsThemeService();
            Np2ptpGui.Themes.ThemeManager.Initialize(themeService.IsLightTheme());
            themeService.ThemeChanged += isLight => Np2ptpGui.Themes.ThemeManager.ApplyTheme(isLight);

            var mainWindow = new MainWindow { DataContext = mainViewModel };
            mainWindow.SettingsTab.DataContext = settingsViewModel;
```

with:

```csharp
            var themeService = new WindowsThemeService();
            if (config.ThemeFamily == Np2ptpGui.Themes.ModernThemeManager.FamilyName)
            {
                Np2ptpGui.Themes.ModernThemeManager.Initialize(themeService.IsLightTheme());
                themeService.ThemeChanged += isLight => Np2ptpGui.Themes.ModernThemeManager.ApplyTheme(isLight);
            }
            else
            {
                Np2ptpGui.Themes.ThemeManager.Initialize(themeService.IsLightTheme());
                themeService.ThemeChanged += isLight => Np2ptpGui.Themes.ThemeManager.ApplyTheme(isLight);
            }

            var mainWindow = new MainWindow { DataContext = mainViewModel };
            mainWindow.SettingsTab.DataContext = settingsViewModel;
            Np2ptpGui.Themes.ModernThemeManager.ApplyToWindow(mainWindow, config.ThemeFamily);
```

- [ ] **Step 4: Apply the same window-level setup to `FetchOptionsDialog`**

Modify `src/Np2ptpGui/ViewModels/MainViewModel.cs` — in `StartDownloadCommand`'s `else` branch,
change:

```csharp
                var dialog = new FetchOptionsDialog(_config.DefaultDownloadFolder, _config.StoreFolder, _config.KeepStoreByDefault);
                if (dialog.ShowDialog() != true) return; // cancelled - link stays typed, nothing starts
```

to:

```csharp
                var dialog = new FetchOptionsDialog(_config.DefaultDownloadFolder, _config.StoreFolder, _config.KeepStoreByDefault);
                Np2ptpGui.Themes.ModernThemeManager.ApplyToWindow(dialog, _config.ThemeFamily);
                if (dialog.ShowDialog() != true) return; // cancelled - link stays typed, nothing starts
```

- [ ] **Step 5: Build**

Run:
```
cd "C:\Users\Luan Bogo\np2ptp-gui"; & "C:\Program Files\dotnet\dotnet.exe" build src\Np2ptpGui\Np2ptpGui.csproj -c Debug 2>&1 | Select-Object -Last 15
```
Expected: `0 Aviso(s)` / `0 Erro(s)`.

- [ ] **Step 6: Manually verify XP Luna still works (regression check)**

Launch the app with the default config (`ThemeFamily` still `"XpLuna"` — do not edit `config.ini`
yet). Confirm it renders exactly as before (same check as Task 3 Step 4). This confirms
`ApplyToWindow`'s `else` branch correctly reproduces the removed static `Style` attribute's effect.

- [ ] **Step 7: Manually verify Modern actually renders**

Close the app. Find the real config file (check `App.xaml.cs` for the path — it's
`%AppData%\np2ptp-gui\config.ini`). Hand-edit `ThemeFamily=XpLuna` to `ThemeFamily=Modern` (add the
line if it isn't there yet). Relaunch the app. Screenshot it. Confirm:
- The window shows a real Mica backdrop (translucent, shows a hint of the desktop/wallpaper behind
  it — a flat opaque color means Mica did not activate, investigate before moving on).
- Buttons/TextBoxes/CheckBoxes/ListView render with WPF-UI's Fluent look (rounded corners, Fluent
  accent-colored primary buttons), not the old XP square-bordered look.
- The accent color visually matches whatever the Windows "Personalização > Cores" accent currently
  is set to.
- Flip the Windows light/dark setting live (same registry + `WM_SETTINGCHANGE` broadcast technique
  used earlier in this project's history) without restarting — confirm Modern's light/dark switches
  live, matching XP Luna's existing live-switch behavior.
- Trigger `FetchOptionsDialog` (type a link, click "+ New download") — confirm it also shows Mica
  and Fluent-styled controls.
- Set `ThemeFamily` back to `XpLuna` in the config file and relaunch, confirming it reverts cleanly
  (this is also exercised more thoroughly in Task 6).

- [ ] **Step 8: Run the full test suite**

Run:
```
cd "C:\Users\Luan Bogo\np2ptp-gui"; & "C:\Program Files\dotnet\dotnet.exe" test 2>&1 | Select-String "Com falha|Aprovado"
```
Expected: all tests pass (retry once for the known pre-existing flake if it appears).

- [ ] **Step 9: Commit**

```bash
git add src/Np2ptpGui/Themes/ModernThemeManager.cs src/Np2ptpGui/App.xaml.cs src/Np2ptpGui/MainWindow.xaml src/Np2ptpGui/Views/FetchOptionsDialog.xaml src/Np2ptpGui/ViewModels/MainViewModel.cs
git commit -m "feat: wire Modern (Fluent/Win11) theme family into app startup"
```

---

### Task 5: Theme selector in Settings

**Files:**
- Modify: `src/Np2ptpGui/ViewModels/SettingsViewModel.cs`
- Modify: `src/Np2ptpGui/Views/SettingsView.xaml`

**Interfaces:**
- Consumes: `AppConfig.ThemeFamily` (Task 1).
- Produces: `SettingsViewModel.ThemeFamily` (`string`, bindable) — read by `SaveCommand` to persist
  back into `AppConfig`.

- [ ] **Step 1: Add the bindable property and wire it into load/save**

Modify `src/Np2ptpGui/ViewModels/SettingsViewModel.cs` — add a field/property after
`KeepStoreByDefault`:

```csharp
    private bool _keepStoreByDefault;
    public bool KeepStoreByDefault { get => _keepStoreByDefault; set => SetField(ref _keepStoreByDefault, value); }

    private string _themeFamily;
    public string ThemeFamily { get => _themeFamily; set => SetField(ref _themeFamily, value); }
```

In the constructor, after `_keepStoreByDefault = config.KeepStoreByDefault;`:

```csharp
        _keepStoreByDefault = config.KeepStoreByDefault;
        _themeFamily = config.ThemeFamily;
```

In `SaveCommand`'s body, after `_config.KeepStoreByDefault = KeepStoreByDefault;`:

```csharp
            _config.KeepStoreByDefault = KeepStoreByDefault;
            _config.ThemeFamily = ThemeFamily;
            _configStore.Save(_config);
```

- [ ] **Step 2: Add the ComboBox to `SettingsView.xaml`**

Modify `src/Np2ptpGui/Views/SettingsView.xaml` — insert a theme selector between the existing
checkboxes and the Save button:

```xml
        <CheckBox Content="Sempre usar essas configurações ao baixar" IsChecked="{Binding AlwaysUseDownloadDefaults}" Margin="0,10,0,4" />
        <CheckBox Content="Manter store por padrão" IsChecked="{Binding KeepStoreByDefault}" Margin="0,0,0,10" />

        <Label Content="Tema" />
        <ComboBox SelectedValuePath="Tag" SelectedValue="{Binding ThemeFamily}"
                  HorizontalAlignment="Left" Width="220" Margin="0,0,0,2">
            <ComboBoxItem Content="XP Luna" Tag="XpLuna" />
            <ComboBoxItem Content="Modern (Windows 11)" Tag="Modern" />
        </ComboBox>
        <TextBlock Text="Reinicie o np2ptp para aplicar a troca de tema." Foreground="Gray" FontSize="11" Margin="0,0,0,10" />

        <Button Content="Save" Command="{Binding SaveCommand}" HorizontalAlignment="Left" Width="80" />
```

- [ ] **Step 3: Build**

Run:
```
cd "C:\Users\Luan Bogo\np2ptp-gui"; & "C:\Program Files\dotnet\dotnet.exe" build src\Np2ptpGui\Np2ptpGui.csproj -c Debug 2>&1 | Select-Object -Last 15
```
Expected: `0 Aviso(s)` / `0 Erro(s)`.

- [ ] **Step 4: Manually verify the selector**

Launch the app, go to Settings. Confirm the "Tema" ComboBox shows "XP Luna" pre-selected (matching
the default config), the gray restart notice is visible, and picking "Modern (Windows 11)" then
clicking Save writes `ThemeFamily=Modern` into `config.ini` (check the file directly). Restart the
app and confirm it now launches in Modern (reuses Task 4 Step 7's verification). Switch back to
"XP Luna" via the ComboBox, Save, restart, confirm it's back to XP Luna.

- [ ] **Step 5: Run the full test suite**

Run:
```
cd "C:\Users\Luan Bogo\np2ptp-gui"; & "C:\Program Files\dotnet\dotnet.exe" test 2>&1 | Select-String "Com falha|Aprovado"
```
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Np2ptpGui/ViewModels/SettingsViewModel.cs src/Np2ptpGui/Views/SettingsView.xaml
git commit -m "feat: add theme family selector to Settings"
```

---

### Task 6: End-to-end verification of both theme families

**Files:** none (verification only — fix inline and note in the task report if something's broken,
don't create new scope for a fix that turns out to be needed).

This closes the loop on the spec's Testing section and on the exact lesson learned from the XP
Luna plan (a "final whole-branch review" can pass while the app's actual primary-use screens are
broken) — drive the real app through every combination by hand, not just what earlier tasks already
happened to check individually.

- [ ] **Step 1: Fresh install simulation (XpLuna default, unmodified)**

Delete/rename `%AppData%\np2ptp-gui\config.ini` (or move it aside) so the app starts from true
defaults. Launch. Confirm it comes up in XP Luna, identical to the currently-shipped behavior
(all 4 tabs + Settings, both light and dark via a live Windows theme flip).

- [ ] **Step 2: Switch to Modern via the UI, restart, verify**

In Settings, switch to "Modern (Windows 11)", Save, close the app fully (`Get-Process Np2ptpGui |
Stop-Process -Force` if it lingers in the tray), relaunch. Confirm: Mica backdrop, Fluent controls,
Windows accent color, all 4 tabs + Settings legible and styled, `FetchOptionsDialog` also Mica +
Fluent. Flip Windows light/dark live — confirm Modern switches live without a restart.

- [ ] **Step 3: Switch back to XpLuna via the UI, restart, verify**

In Settings (now themed as Modern), switch back to "XP Luna", Save, restart. Confirm XP Luna
renders exactly as it did in Step 1 — no Mica bleed-through, no leftover Fluent control styling,
no exceptions.

- [ ] **Step 4: Full test suite, one more time**

Run:
```
cd "C:\Users\Luan Bogo\np2ptp-gui"; & "C:\Program Files\dotnet\dotnet.exe" test 2>&1 | Select-String "Com falha|Aprovado"
```
Expected: all tests pass (retry once for the known pre-existing flake if it appears).

- [ ] **Step 5: Restore the machine's Windows theme setting**

Set the real Windows theme registry value back to whatever it was before this verification pass
started (check what it was at the very start of this task, not an assumed default — this project's
own history has gotten this wrong before by assuming "Light" when the actual starting state was
dark).

- [ ] **Step 6: Report**

Summarize what was checked and any deviations found/fixed in the task report. If everything passed
clean, this is the last task — the plan is complete and ready for
`superpowers:finishing-a-development-branch`.
