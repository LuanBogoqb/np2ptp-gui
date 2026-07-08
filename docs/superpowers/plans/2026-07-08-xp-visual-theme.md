# XP Visual Theme (Core) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give np2ptp-gui a Windows XP Luna visual theme (colors, Tahoma font, control styling) with automatic light/dark variants that follow and live-track the Windows system theme setting.

**Architecture:** Two color `ResourceDictionary` files (`XpColors.Light.xaml` / `XpColors.Dark.xaml`, same brush keys, different values) plus one shared `XpControlStyles.xaml` that styles `Button`/`CheckBox`/`TextBox`/`Border`/base font via `DynamicResource` references to those brush keys. A static `ThemeManager` merges styles once and swaps only the colors dictionary per registered root (`MainWindow`, `FetchOptionsDialog`) when the theme changes. A `WindowsThemeService` reads the Windows registry light/dark setting and raises a live-change event via `SystemEvents.UserPreferenceChanged`.

**Tech Stack:** WPF (.NET 8), `Microsoft.Win32.Registry`, `Microsoft.Win32.SystemEvents`, xunit (existing test project).

## Global Constraints

- No new NuGet dependencies. `Microsoft.Win32.Registry` and `Microsoft.Win32.SystemEvents` are both available out of the box to a `net8.0-windows` WPF project (`UseWPF=true`) — confirmed no extra `PackageReference` needed for either.
- Every theme resource dictionary is merged directly into each `Window`'s own `Resources.MergedDictionaries` in code-behind — **never** into `App.xaml`'s `Application.Resources`. This project's `App.xaml` has no `StartupUri`, so the WPF markup compiler never emits `App.InitializeComponent()` and `Application.Resources` is unreachable dead code (confirmed via `ex.ToString()` on a real `XamlParseException` and an isolated `dotnet new wpf` repro during the prior fetch-ux plan). Any task that tries to put a themed resource in `App.xaml` is wrong — flag it as a bug during review, not a style nit.
- Only two `FrameworkElement` roots need `ThemeManager.Register(this)`: `MainWindow` and `FetchOptionsDialog`. `DownloadsView`, `SeedingView`, `SettingsView`, `ShareView` are `UserControl`s hosted as `TabItem` content **inside** `MainWindow`'s logical tree, not separate windows — WPF resolves implicit (`TargetType`-keyed) styles and `DynamicResource` lookups by walking up the logical tree from the requesting element to the root, so registering `MainWindow` alone themes everything hosted inside its tabs. Do not add `ThemeManager.Register` calls to the four `UserControl` code-behind files — that would be redundant (they'd re-merge the same dictionaries a second time into a nested `Resources`, wasting memory for no visual effect) and reviewers should flag it as scope creep.
- Tahoma is already copied to `src/Np2ptpGui/Fonts/tahoma.ttf` (present in the repo as of this plan). Reference it as an embedded WPF font resource, not a loose file copied next to the .exe.
- `dotnet.exe` is never on PATH — always invoke it via the full path `"C:\Program Files\dotnet\dotnet.exe"`.
- Every task must build cleanly (`dotnet build`) AND be manually verified by actually launching the running app and looking at it (screenshot or direct observation) — `dotnet build` succeeding is not sufficient proof a WPF resource/style change works, per this repo's documented history of two silent WPF failures (`Application.Resources` dead code; `RelativeSource` failing inside `VisualBrush.Visual`) that a clean build did not catch in the prior plan.

---

### Task 1: Theme resource dictionaries + ThemeManager, wired into MainWindow

**Files:**
- Create: `src/Np2ptpGui/Themes/XpColors.Light.xaml`
- Create: `src/Np2ptpGui/Themes/XpColors.Dark.xaml`
- Create: `src/Np2ptpGui/Themes/XpControlStyles.xaml`
- Create: `src/Np2ptpGui/Themes/ThemeManager.cs`
- Modify: `src/Np2ptpGui/Np2ptpGui.csproj` (register the font as a resource)
- Modify: `src/Np2ptpGui/MainWindow.xaml.cs`

**Interfaces:**
- Produces: `Np2ptpGui.Themes.ThemeManager` with two public static members later tasks depend on:
  - `ThemeManager.Register(FrameworkElement root)` — merges the styles dictionary once and the currently-active colors dictionary into `root.Resources.MergedDictionaries`; if `root` is a `Window`, unregisters it on `Closed`.
  - `ThemeManager.ApplyTheme(bool isLight)` — records the active theme and swaps the colors dictionary (only) on every currently-registered root.

- [ ] **Step 1: Add the font resource to the csproj**

Open `src/Np2ptpGui/Np2ptpGui.csproj` and add the font next to the existing `app.ico` resource:

```xml
  <ItemGroup>
    <Resource Include="app.ico" />
    <Resource Include="Fonts\tahoma.ttf" />
  </ItemGroup>
```

- [ ] **Step 2: Create the light color dictionary**

Create `src/Np2ptpGui/Themes/XpColors.Light.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <SolidColorBrush x:Key="XpWindowBackgroundBrush" Color="#ECE9D8" />
    <SolidColorBrush x:Key="XpControlBackgroundBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="XpControlBorderBrush" Color="#0054E3" />
    <SolidColorBrush x:Key="XpAccentTopBrush" Color="#3D95FF" />
    <SolidColorBrush x:Key="XpAccentBottomBrush" Color="#0050EE" />
    <SolidColorBrush x:Key="XpAccentPressedBrush" Color="#00299E" />
    <SolidColorBrush x:Key="XpTextBrush" Color="#000000" />
    <SolidColorBrush x:Key="XpDisabledTextBrush" Color="#7B7B7B" />

</ResourceDictionary>
```

- [ ] **Step 3: Create the dark color dictionary**

Create `src/Np2ptpGui/Themes/XpColors.Dark.xaml` — same keys, XP-Luna-derived dark values (invented; real XP had no dark mode):

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <SolidColorBrush x:Key="XpWindowBackgroundBrush" Color="#2B2B24" />
    <SolidColorBrush x:Key="XpControlBackgroundBrush" Color="#3C3C34" />
    <SolidColorBrush x:Key="XpControlBorderBrush" Color="#3D95FF" />
    <SolidColorBrush x:Key="XpAccentTopBrush" Color="#1B4C99" />
    <SolidColorBrush x:Key="XpAccentBottomBrush" Color="#0B2E6E" />
    <SolidColorBrush x:Key="XpAccentPressedBrush" Color="#061A44" />
    <SolidColorBrush x:Key="XpTextBrush" Color="#F0F0F0" />
    <SolidColorBrush x:Key="XpDisabledTextBrush" Color="#9A9A9A" />

</ResourceDictionary>
```

- [ ] **Step 4: Create the shared control styles dictionary**

Create `src/Np2ptpGui/Themes/XpControlStyles.xaml`. This references the color keys above via `DynamicResource` (so it never needs to be re-merged when the theme swaps — only the colors dictionary above gets swapped) and declares the Tahoma font family from the embedded font resource:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <FontFamily x:Key="XpFontFamily">pack://application:,,,/Fonts/#Tahoma</FontFamily>

    <Style TargetType="{x:Type Control}">
        <Setter Property="FontFamily" Value="{DynamicResource XpFontFamily}" />
        <Setter Property="FontSize" Value="12" />
    </Style>

    <Style TargetType="{x:Type Window}">
        <Setter Property="FontFamily" Value="{DynamicResource XpFontFamily}" />
        <Setter Property="Background" Value="{DynamicResource XpWindowBackgroundBrush}" />
    </Style>

    <Style TargetType="{x:Type Button}">
        <Setter Property="FontFamily" Value="{DynamicResource XpFontFamily}" />
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Foreground" Value="{DynamicResource XpTextBrush}" />
        <Setter Property="Padding" Value="10,4" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type Button}">
                    <Border x:Name="ButtonBorder"
                            CornerRadius="3"
                            BorderThickness="1"
                            BorderBrush="{DynamicResource XpControlBorderBrush}">
                        <Border.Background>
                            <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
                                <GradientStop Color="{Binding Source={DynamicResource XpAccentTopBrush}, Path=Color}" Offset="0" />
                                <GradientStop Color="{Binding Source={DynamicResource XpAccentBottomBrush}, Path=Color}" Offset="1" />
                            </LinearGradientBrush>
                        </Border.Background>
                        <ContentPresenter HorizontalAlignment="Center"
                                          VerticalAlignment="Center"
                                          Margin="{TemplateBinding Padding}" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="ButtonBorder" Property="Background" Value="{DynamicResource XpAccentPressedBrush}" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter TargetName="ButtonBorder" Property="Background" Value="{DynamicResource XpControlBackgroundBrush}" />
                            <Setter Property="Foreground" Value="{DynamicResource XpDisabledTextBrush}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="{x:Type TextBox}">
        <Setter Property="FontFamily" Value="{DynamicResource XpFontFamily}" />
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Background" Value="{DynamicResource XpControlBackgroundBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource XpTextBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource XpControlBorderBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Padding" Value="4,3" />
    </Style>

    <Style TargetType="{x:Type CheckBox}">
        <Setter Property="FontFamily" Value="{DynamicResource XpFontFamily}" />
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Foreground" Value="{DynamicResource XpTextBrush}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type CheckBox}">
                    <StackPanel Orientation="Horizontal">
                        <Border Width="14" Height="14"
                                BorderThickness="1"
                                BorderBrush="{DynamicResource XpControlBorderBrush}"
                                Background="{DynamicResource XpControlBackgroundBrush}"
                                VerticalAlignment="Center">
                            <Path x:Name="CheckMark"
                                  Data="M 1,5 L 5,9 L 12,1"
                                  Stroke="{DynamicResource XpAccentBottomBrush}"
                                  StrokeThickness="2"
                                  Visibility="Collapsed" />
                        </Border>
                        <ContentPresenter Margin="6,0,0,0" VerticalAlignment="Center" />
                    </StackPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="CheckMark" Property="Visibility" Value="Visible" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

</ResourceDictionary>
```

- [ ] **Step 5: Create ThemeManager**

Create `src/Np2ptpGui/Themes/ThemeManager.cs`:

```csharp
namespace Np2ptpGui.Themes;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

public static class ThemeManager
{
    private static readonly Uri StylesUri = new("/Np2ptpGui;component/Themes/XpControlStyles.xaml", UriKind.Relative);
    private static readonly Uri LightColorsUri = new("/Np2ptpGui;component/Themes/XpColors.Light.xaml", UriKind.Relative);
    private static readonly Uri DarkColorsUri = new("/Np2ptpGui;component/Themes/XpColors.Dark.xaml", UriKind.Relative);

    private static readonly List<FrameworkElement> Roots = new();
    private static bool _isLight = true;

    public static void ApplyTheme(bool isLight)
    {
        _isLight = isLight;
        var colorsUri = isLight ? LightColorsUri : DarkColorsUri;
        foreach (var root in Roots.ToList())
        {
            root.Resources.MergedDictionaries[1] = new ResourceDictionary { Source = colorsUri };
        }
    }

    public static void Register(FrameworkElement root)
    {
        root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = StylesUri });
        root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = _isLight ? LightColorsUri : DarkColorsUri });
        Roots.Add(root);

        if (root is Window window)
        {
            window.Closed += (_, _) => Roots.Remove(root);
        }
    }
}
```

- [ ] **Step 6: Wire MainWindow to register with ThemeManager**

Modify `src/Np2ptpGui/MainWindow.xaml.cs`:

```csharp
namespace Np2ptpGui;

using System.Windows;
using Np2ptpGui.Themes;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.Register(this);
    }
}
```

- [ ] **Step 7: Build**

Run: `"C:\Program Files\dotnet\dotnet.exe" build Np2ptpGui.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 8: Manually verify in the running app**

Run: `"C:\Program Files\dotnet\dotnet.exe" run --project src\Np2ptpGui\Np2ptpGui.csproj` (or launch the built `.exe` directly), wait for the window, then take a screenshot (e.g. via a PowerShell `System.Drawing` capture, matching the approach used in the prior fetch-ux plan).

Expected observations:
- Every visible `Button` shows the blue XP-Luna gradient (not the default flat gray WPF button).
- Every `TextBox` shows a blue-bordered, white-background box.
- Any visible `CheckBox` shows the custom square box (not the default OS checkbox glyph).
- Text is rendered in Tahoma (visibly different from the default Segoe UI — compare letterforms, e.g. the lowercase "a").
- All 4 tabs (Downloads, Seeding, Share, Settings) show the themed controls, confirming the single `MainWindow` registration themes the hosted `UserControl`s too.

If any of these fail, this is a defect in this task, not a later one — do not proceed to Task 2 until fixed. If a `XamlParseException` occurs, capture the full `ex.ToString()` (not just `ex.Message`) before attempting a fix, matching this repo's established diagnostic practice.

- [ ] **Step 9: Commit**

```bash
git add src/Np2ptpGui/Themes/ src/Np2ptpGui/Fonts/tahoma.ttf src/Np2ptpGui/Np2ptpGui.csproj src/Np2ptpGui/MainWindow.xaml.cs
git commit -m "feat: add XP Luna theme dictionaries and ThemeManager, wire into MainWindow"
```

---

### Task 2: WindowsThemeService (registry detection + live change event)

**Files:**
- Create: `src/Np2ptpGui/Services/WindowsThemeService.cs`
- Test: `tests/Np2ptpGui.Tests/Services/WindowsThemeServiceTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `Np2ptpGui.Services.WindowsThemeService`, used by Task 3:
  - `public WindowsThemeService()` — real constructor, reads the actual registry.
  - `internal WindowsThemeService(Func<int?> registryReader)` — test seam constructor.
  - `public bool IsLightTheme()` — live read, returns `true` when the registry value is `1` or missing/unreadable, `false` only when the value is present and `0`.
  - `public event Action<bool>? ThemeChanged` — raised with the new `isLight` value only when a `SystemEvents.UserPreferenceChanged` (`UserPreferenceCategory.General`) fires AND the light/dark value actually changed since the last read.

- [ ] **Step 1: Write the failing tests**

Create `tests/Np2ptpGui.Tests/Services/WindowsThemeServiceTests.cs`:

```csharp
namespace Np2ptpGui.Tests.Services;

using Np2ptpGui.Services;
using Xunit;

public class WindowsThemeServiceTests
{
    [Fact]
    public void IsLightTheme_WhenRegistryValueIsOne_ReturnsTrue()
    {
        var service = new WindowsThemeService(() => 1);

        Assert.True(service.IsLightTheme());
    }

    [Fact]
    public void IsLightTheme_WhenRegistryValueIsZero_ReturnsFalse()
    {
        var service = new WindowsThemeService(() => 0);

        Assert.False(service.IsLightTheme());
    }

    [Fact]
    public void IsLightTheme_WhenRegistryValueIsMissing_ReturnsTrue()
    {
        var service = new WindowsThemeService(() => null);

        Assert.True(service.IsLightTheme());
    }

    [Fact]
    public void IsLightTheme_WhenReaderThrows_ReturnsTrue()
    {
        var service = new WindowsThemeService(() => throw new System.InvalidOperationException("boom"));

        Assert.True(service.IsLightTheme());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run (PowerShell tool, this test class touches no `ConsoleCtrl`/`ProcessRunner` code so either shell is fine, but stay consistent with this repo's convention of using the PowerShell tool for the test project):
`"C:\Program Files\dotnet\dotnet.exe" test tests\Np2ptpGui.Tests\Np2ptpGui.Tests.csproj --filter WindowsThemeServiceTests`
Expected: FAIL to build — `WindowsThemeService` does not exist yet.

- [ ] **Step 3: Implement WindowsThemeService**

Create `src/Np2ptpGui/Services/WindowsThemeService.cs`:

```csharp
namespace Np2ptpGui.Services;

using System;
using Microsoft.Win32;

public sealed class WindowsThemeService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "AppsUseLightTheme";

    private readonly Func<int?> _registryReader;
    private bool _lastIsLight;

    public event Action<bool>? ThemeChanged;

    public WindowsThemeService() : this(ReadRegistryValue)
    {
    }

    internal WindowsThemeService(Func<int?> registryReader)
    {
        _registryReader = registryReader;
        _lastIsLight = IsLightTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public bool IsLightTheme()
    {
        int? value;
        try
        {
            value = _registryReader();
        }
        catch
        {
            value = null;
        }

        return value != 0;
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;

        var isLight = IsLightTheme();
        if (isLight == _lastIsLight) return;

        _lastIsLight = isLight;
        ThemeChanged?.Invoke(isLight);
    }

    private static int? ReadRegistryValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        var value = key?.GetValue(ValueName);
        return value is int intValue ? intValue : null;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `"C:\Program Files\dotnet\dotnet.exe" test tests\Np2ptpGui.Tests\Np2ptpGui.Tests.csproj --filter WindowsThemeServiceTests`
Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add src/Np2ptpGui/Services/WindowsThemeService.cs tests/Np2ptpGui.Tests/Services/WindowsThemeServiceTests.cs
git commit -m "feat: add WindowsThemeService for light/dark registry detection with live-change event"
```

---

### Task 3: Wire detection into App startup, register FetchOptionsDialog, full manual verification

**Files:**
- Modify: `src/Np2ptpGui/App.xaml.cs`
- Modify: `src/Np2ptpGui/Views/FetchOptionsDialog.xaml.cs`

**Interfaces:**
- Consumes: `Np2ptpGui.Themes.ThemeManager.ApplyTheme(bool)` (Task 1), `Np2ptpGui.Services.WindowsThemeService` (Task 2: constructor, `IsLightTheme()`, `ThemeChanged` event).
- Produces: nothing further — this is the last task in this plan.

- [ ] **Step 1: Wire WindowsThemeService into App startup**

Modify `src/Np2ptpGui/App.xaml.cs`. Add the using and apply the initial theme before `mainWindow.Show()`, then subscribe to live changes. The relevant existing block is:

```csharp
            var mainWindow = new MainWindow { DataContext = mainViewModel };
            mainWindow.SettingsTab.DataContext = settingsViewModel;

            _trayIconManager = new TrayIconManager(mainWindow, taskManager);

            MainWindow = mainWindow;
            mainWindow.Show();
```

Replace it with:

```csharp
            var themeService = new WindowsThemeService();
            Np2ptpGui.Themes.ThemeManager.ApplyTheme(themeService.IsLightTheme());
            themeService.ThemeChanged += isLight => Np2ptpGui.Themes.ThemeManager.ApplyTheme(isLight);

            var mainWindow = new MainWindow { DataContext = mainViewModel };
            mainWindow.SettingsTab.DataContext = settingsViewModel;

            _trayIconManager = new TrayIconManager(mainWindow, taskManager);

            MainWindow = mainWindow;
            mainWindow.Show();
```

`ThemeManager.ApplyTheme` is called here (before `MainWindow`'s constructor runs `ThemeManager.Register(this)`) so that when `MainWindow` registers itself it picks up the already-correct initial theme, per `ThemeManager`'s `_isLight` field set in Task 1.

Add `using Np2ptpGui.Services;` is already present in this file (it has `using Np2ptpGui.Services;` for `ConfigStore`, `HistoryStore`, etc. — `WindowsThemeService` lives in the same namespace, no new `using` needed). Do not add a `using Np2ptpGui.Themes;` at the top — the fully-qualified `Np2ptpGui.Themes.ThemeManager` calls above are deliberate to keep this diff small; either is correct, but stay consistent within the file if you choose to add the `using` instead.

Keep `_trayIconManager` field and every other line in `OnStartup` unchanged.

- [ ] **Step 2: Register FetchOptionsDialog with ThemeManager**

Read `src/Np2ptpGui/Views/FetchOptionsDialog.xaml.cs` first to find its constructor. Add the same two lines used in `MainWindow.xaml.cs`: an added `using Np2ptpGui.Themes;` and a `ThemeManager.Register(this);` call immediately after `InitializeComponent();` inside the constructor body, before any other constructor logic. Do not change the constructor's existing parameters or any other logic in the file.

- [ ] **Step 3: Build**

Run: `"C:\Program Files\dotnet\dotnet.exe" build Np2ptpGui.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Run the full test suite**

Run (PowerShell tool — this repo's `ConsoleCtrl`-tagged tests require it): `"C:\Program Files\dotnet\dotnet.exe" test Np2ptpGui.sln`
Expected: all tests pass (a `STATUS_CONTROL_C_EXIT` failure on `ProcessRunnerTests.StopGracefullyAsync_SendsCtrlCAndWaitsForCleanExit` on the very first run after a fresh build is a known, pre-existing flake — retry once if only that one test fails; it is not caused by this task's changes).

- [ ] **Step 5: Manually verify light mode**

With the Windows system theme set to **light** (Settings → Personalization → Colors → "Choose your mode" → Light), launch the built app, open the Fetch dialog (Downloads tab → "+ New download" with `AlwaysUseDownloadDefaults` off, or toggle it off first in Settings if it's on from a prior test session), and screenshot both `MainWindow` and the open `FetchOptionsDialog`.

Expected: both show the XP Luna **light** palette (`#ECE9D8` window background, blue gradient buttons) from Task 1's Step 8 verification, and `FetchOptionsDialog`'s buttons/textboxes are themed identically to `MainWindow`'s (confirming its own, separate `ThemeManager.Register` call worked).

- [ ] **Step 6: Manually verify live dark-mode switching**

Without closing the running app, switch the Windows system theme to **dark** (Settings → Personalization → Colors → "Choose your mode" → Dark). Screenshot `MainWindow` again (and re-open `FetchOptionsDialog` if it was closed, or screenshot it too if still open).

Expected: both now show the dark palette from `XpColors.Dark.xaml` (`#2B2B24` window background, darker blue gradient buttons) **without restarting the app** — this proves the `SystemEvents.UserPreferenceChanged` → `ThemeChanged` → `ThemeManager.ApplyTheme` path works live, not just at startup.

Switch the Windows theme back to light afterward so you don't leave the test machine in a changed state, and confirm the app follows back to light too (same live-switch path, opposite direction).

If dark mode does not apply live, this is a defect in this task — do not mark the task done until it visibly switches without an app restart.

- [ ] **Step 7: Commit**

```bash
git add src/Np2ptpGui/App.xaml.cs src/Np2ptpGui/Views/FetchOptionsDialog.xaml.cs
git commit -m "feat: detect Windows light/dark theme at startup and live-switch the XP theme"
```
