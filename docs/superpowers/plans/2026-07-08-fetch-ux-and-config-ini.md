# Fetch UX Overhaul, Pickers, Hint Text, INI Config — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add folder/file picker dialogs, placeholder hint text, and a proper Fetch options flow (per-download store isolation + keep/discard toggle) to np2ptp-gui, and switch config persistence from JSON to INI.

**Architecture:** No new dependencies. Folder/file pickers use .NET 8's built-in `Microsoft.Win32.OpenFolderDialog`/`OpenFileDialog` (never `System.Windows.Forms`, avoiding the documented `CS0104` trap). Hint text is a small attached-property + `Style` overlay. INI parsing is hand-rolled (flat `key=value`, ~7 fields — not worth a dependency). The Fetch flow gets one new modal `Window` (`FetchOptionsDialog`) and `TaskManager.StartFetch` gains store-folder isolation (`<StoreFolder>/<operationId>`) plus an optional post-success cleanup hook.

**Tech Stack:** C#/.NET 8, WPF, xUnit. Same as the existing codebase — no additions.

## Global Constraints

- No new NuGet packages (spec: "avoids a dependency purely to save ~30 lines").
- Folder/file pickers must use `Microsoft.Win32.OpenFolderDialog`/`OpenFileDialog`, never `System.Windows.Forms` (spec: avoid the `CS0104` ambiguity trap documented in `docs/LESSONS-LEARNED.md`).
- `ConfigStore.Save`/`Load` must keep the existing atomic-write (`.tmp` + `File.Move`) and instance-lock pattern — only the serialization format changes.
- No migration of existing `config.json` (spec: unreleased app, one-time re-entry of settings is acceptable).
- Every fetch's store lives at `<StoreFolder>/<operationId>`, never the bare root (spec: prerequisite for safe keep/discard).
- No `IDialogService`-style abstraction — call `Microsoft.Win32.*` dialogs directly from ViewModels/code-behind, matching the existing direct `System.Windows.MessageBox.Show(...)` precedent in `MainViewModel.StopOperationCommand`.
- Branch: `dev`. Commit after every task, following this repo's existing commit style (`feat: ...` / `fix: ...`).

---

### Task 1: AppConfig fields + ConfigStore → INI

**Files:**
- Modify: `src/Np2ptpGui/Models/AppConfig.cs`
- Modify: `src/Np2ptpGui/Services/ConfigStore.cs`
- Modify: `tests/Np2ptpGui.Tests/Services/ConfigStoreTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `AppConfig.AlwaysUseDownloadDefaults` (bool, default `false`), `AppConfig.KeepStoreByDefault` (bool, default `true`). `ConfigStore` reads/writes `config.ini` (not `config.json`) with the same public `Load()`/`Save(AppConfig)` signatures. Task 3 (Settings UI) binds the two new fields; Task 5/6 (Fetch flow) read `KeepStoreByDefault`/`AlwaysUseDownloadDefaults`.

- [ ] **Step 1: Add the two new fields to `AppConfig`**

Edit `src/Np2ptpGui/Models/AppConfig.cs` — full file becomes:

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
}
```

- [ ] **Step 2: Write the failing INI tests**

Replace the full contents of `tests/Np2ptpGui.Tests/Services/ConfigStoreTests.cs`:

```csharp
namespace Np2ptpGui.Tests.Services;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Np2ptpGui.Models;
using Np2ptpGui.Services;
using Xunit;

public class ConfigStoreTests
{
    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), "np2ptp-gui-tests-" + Guid.NewGuid());

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
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Load_WhenFileIsCorruptedText_ReturnsDefaultsInsteadOfThrowing()
    {
        var dir = NewTempDir();
        var store = new ConfigStore(dir);
        var filePath = Path.Combine(dir, "config.ini");
        File.WriteAllText(filePath, "this is not a valid ini line at all === !!\nAlwaysUseDownloadDefaults=notabool\n");

        var config = store.Load();

        Assert.Equal("", config.BinaryPath);
        Assert.False(config.AlwaysUseDownloadDefaults);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Save_DoesNotLeaveTempFileBehind()
    {
        var dir = NewTempDir();
        var store = new ConfigStore(dir);

        store.Save(new AppConfig { BinaryPath = @"C:\bins\np2ptp.exe" });

        Assert.True(File.Exists(Path.Combine(dir, "config.ini")));
        Assert.False(File.Exists(Path.Combine(dir, "config.ini.tmp")));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var dir = NewTempDir();
        var store = new ConfigStore(dir);
        var original = new AppConfig
        {
            BinaryPath = @"C:\bins\np2ptp.exe",
            DefaultDownloadFolder = @"C:\Downloads",
            StoreFolder = @"C:\Store",
            DefaultListenAddress = "/ip4/0.0.0.0/udp/4001/quic-v1",
            TrackerUrl = "https://np2ptp.vercel.app",
            AlwaysUseDownloadDefaults = true,
            KeepStoreByDefault = false,
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original.BinaryPath, loaded.BinaryPath);
        Assert.Equal(original.DefaultDownloadFolder, loaded.DefaultDownloadFolder);
        Assert.Equal(original.StoreFolder, loaded.StoreFolder);
        Assert.Equal(original.DefaultListenAddress, loaded.DefaultListenAddress);
        Assert.Equal(original.TrackerUrl, loaded.TrackerUrl);
        Assert.Equal(original.AlwaysUseDownloadDefaults, loaded.AlwaysUseDownloadDefaults);
        Assert.Equal(original.KeepStoreByDefault, loaded.KeepStoreByDefault);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Save_CalledConcurrentlyFromManyThreads_DoesNotThrow()
    {
        // Regression test for the fixed-temp-file race: two threads calling
        // Save() on the same instance at nearly the same time used to be able
        // to collide on "<file>.tmp" and blow up File.Move with an
        // UnauthorizedAccessException. The instance-level lock in Save() must
        // serialize these calls instead. Format-agnostic; stays as-is for INI.
        var dir = NewTempDir();
        var store = new ConfigStore(dir);

        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            store.Save(new AppConfig { BinaryPath = $@"C:\bins\np2ptp{i}.exe" });
        }));

        await Task.WhenAll(tasks);

        Assert.True(File.Exists(Path.Combine(dir, "config.ini")));
        Assert.False(File.Exists(Path.Combine(dir, "config.ini.tmp")));
        Directory.Delete(dir, recursive: true);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run (PowerShell): `& "C:\Program Files\dotnet\dotnet.exe" test tests\Np2ptpGui.Tests --filter ConfigStoreTests`
Expected: FAIL — `config.ini` is never written (current code still writes `config.json`), and the two new `AppConfig` properties don't round-trip yet (`ConfigStore` doesn't serialize them).

- [ ] **Step 4: Rewrite `ConfigStore` to use INI**

Replace the full contents of `src/Np2ptpGui/Services/ConfigStore.cs`:

```csharp
namespace Np2ptpGui.Services;

using System;
using System.Globalization;
using System.IO;
using Np2ptpGui.Models;

public sealed class ConfigStore
{
    private readonly string _filePath;
    private readonly object _saveLock = new();

    public ConfigStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "config.ini");
    }

    public AppConfig Load()
    {
        var config = new AppConfig();
        if (!File.Exists(_filePath)) return config;
        try
        {
            foreach (var line in File.ReadAllLines(_filePath))
            {
                var separatorIndex = line.IndexOf('=');
                if (separatorIndex < 0) continue;
                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();
                switch (key)
                {
                    case nameof(AppConfig.BinaryPath): config.BinaryPath = value; break;
                    case nameof(AppConfig.DefaultDownloadFolder): config.DefaultDownloadFolder = value; break;
                    case nameof(AppConfig.StoreFolder): config.StoreFolder = value; break;
                    case nameof(AppConfig.DefaultListenAddress): config.DefaultListenAddress = value; break;
                    case nameof(AppConfig.TrackerUrl): config.TrackerUrl = value; break;
                    case nameof(AppConfig.AlwaysUseDownloadDefaults):
                        config.AlwaysUseDownloadDefaults = TryParseBool(value, config.AlwaysUseDownloadDefaults);
                        break;
                    case nameof(AppConfig.KeepStoreByDefault):
                        config.KeepStoreByDefault = TryParseBool(value, config.KeepStoreByDefault);
                        break;
                }
            }
            return config;
        }
        catch (Exception)
        {
            // A hand-rolled parser has no single expected exception type the
            // way JsonException was for the old JSON reader; catch broadly so
            // a corrupted or foreign file can never brick startup (this also
            // closes the previously-acknowledged "Load() only catches
            // JsonException, not IOException" gap as a side effect).
            return new AppConfig();
        }
    }

    private static bool TryParseBool(string value, bool fallback) =>
        bool.TryParse(value.Trim(), out var parsed) ? parsed : fallback;

    public void Save(AppConfig config)
    {
        // Guards against concurrent Save() calls on the same instance racing on
        // the shared fixed temp-file path (write-write and/or a sharing
        // violation on File.Move). Serializing here is sufficient regardless of
        // which thread(s) call in from.
        lock (_saveLock)
        {
            var lines = new[]
            {
                $"{nameof(AppConfig.BinaryPath)}={config.BinaryPath}",
                $"{nameof(AppConfig.DefaultDownloadFolder)}={config.DefaultDownloadFolder}",
                $"{nameof(AppConfig.StoreFolder)}={config.StoreFolder}",
                $"{nameof(AppConfig.DefaultListenAddress)}={config.DefaultListenAddress}",
                $"{nameof(AppConfig.TrackerUrl)}={config.TrackerUrl}",
                $"{nameof(AppConfig.AlwaysUseDownloadDefaults)}={config.AlwaysUseDownloadDefaults.ToString(CultureInfo.InvariantCulture)}",
                $"{nameof(AppConfig.KeepStoreByDefault)}={config.KeepStoreByDefault.ToString(CultureInfo.InvariantCulture)}",
            };
            var tempPath = _filePath + ".tmp";
            File.WriteAllLines(tempPath, lines);
            File.Move(tempPath, _filePath, overwrite: true);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run (PowerShell): `& "C:\Program Files\dotnet\dotnet.exe" test tests\Np2ptpGui.Tests --filter ConfigStoreTests`
Expected: PASS, all 5 tests.

- [ ] **Step 6: Full build check**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Np2ptpGui.sln`
Expected: 0 errors. (`SettingsViewModel`/`App.xaml.cs` still reference `AppConfig` fields unchanged, so nothing else breaks.)

- [ ] **Step 7: Commit**

```bash
git add src/Np2ptpGui/Models/AppConfig.cs src/Np2ptpGui/Services/ConfigStore.cs tests/Np2ptpGui.Tests/Services/ConfigStoreTests.cs
git commit -m "feat: switch ConfigStore to INI format, add download-default settings fields"
```

---

### Task 2: Hint-text watermark behavior, applied everywhere

**Files:**
- Create: `src/Np2ptpGui/Controls/HintBehavior.cs`
- Modify: `src/Np2ptpGui/App.xaml`
- Modify: `src/Np2ptpGui/Views/SettingsView.xaml`
- Modify: `src/Np2ptpGui/Views/DownloadsView.xaml`
- Modify: `src/Np2ptpGui/Views/ShareView.xaml`

**Interfaces:**
- Consumes: nothing.
- Produces: attached property `Np2ptpGui.Controls.HintBehavior.Hint` (string) and a shared `StaticResource` named `HintTextBoxStyle`, both usable by any future view (Task 4's `FetchOptionsDialog` uses both).

No automated tests — WPF watermark rendering needs a live dispatcher to observe, same rationale already established for this codebase's UI wiring (e.g. `SettingsViewModel`/`SettingsView` in the original plan). Verified manually in Step 4.

- [ ] **Step 1: Write the attached property**

Create `src/Np2ptpGui/Controls/HintBehavior.cs`:

```csharp
namespace Np2ptpGui.Controls;

using System.Windows;

public static class HintBehavior
{
    public static readonly DependencyProperty HintProperty =
        DependencyProperty.RegisterAttached(
            "Hint", typeof(string), typeof(HintBehavior), new PropertyMetadata(""));

    public static string GetHint(DependencyObject element) => (string)element.GetValue(HintProperty);
    public static void SetHint(DependencyObject element, string value) => element.SetValue(HintProperty, value);
}
```

- [ ] **Step 2: Add the shared watermark style to `App.xaml`**

Replace the full contents of `src/Np2ptpGui/App.xaml`:

```xml
<Application x:Class="Np2ptpGui.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:Np2ptpGui.Controls">
    <Application.Resources>
        <Style x:Key="HintTextBoxStyle" TargetType="TextBox">
            <Style.Triggers>
                <Trigger Property="Text" Value="">
                    <Setter Property="Background">
                        <Setter.Value>
                            <VisualBrush AlignmentX="Left" AlignmentY="Center" Stretch="None">
                                <VisualBrush.Visual>
                                    <TextBlock Foreground="Gray" Margin="4,0,0,0"
                                               Text="{Binding RelativeSource={RelativeSource AncestorType=TextBox},
                                                              Path=(controls:HintBehavior.Hint)}" />
                                </VisualBrush.Visual>
                            </VisualBrush>
                        </Setter.Value>
                    </Setter>
                </Trigger>
            </Style.Triggers>
        </Style>
    </Application.Resources>
</Application>
```

- [ ] **Step 3: Apply the style + hint text to every free-text input**

Edit `src/Np2ptpGui/Views/SettingsView.xaml` — add `xmlns:controls` to the root `UserControl` tag and `Style`/`controls:HintBehavior.Hint` to the four editable text boxes (`BinaryPath` stays untouched — it's read-only):

```xml
<UserControl x:Class="Np2ptpGui.Views.SettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:Np2ptpGui.Controls">
    <StackPanel Margin="10">
        <Label Content="np2ptp.exe path" />
        <DockPanel>
            <Button DockPanel.Dock="Right" Content="Check for update" Command="{Binding CheckForUpdateCommand}" />
            <TextBox Text="{Binding BinaryPath}" IsReadOnly="True" />
        </DockPanel>
        <TextBlock Text="{Binding UpdateStatus}" Margin="0,4,0,10" />

        <Label Content="Default download folder" />
        <TextBox Text="{Binding DefaultDownloadFolder, UpdateSourceTrigger=PropertyChanged}"
                 Style="{StaticResource HintTextBoxStyle}" controls:HintBehavior.Hint="Ex: C:\Users\voce\Downloads"
                 Margin="0,0,0,10" />

        <Label Content="Store folder" />
        <TextBox Text="{Binding StoreFolder, UpdateSourceTrigger=PropertyChanged}"
                 Style="{StaticResource HintTextBoxStyle}" controls:HintBehavior.Hint="Ex: C:\Users\voce\np2ptp-store"
                 Margin="0,0,0,10" />

        <Label Content="Default listen address" />
        <TextBox Text="{Binding DefaultListenAddress, UpdateSourceTrigger=PropertyChanged}"
                 Style="{StaticResource HintTextBoxStyle}" controls:HintBehavior.Hint="Ex: /ip4/0.0.0.0/udp/0/quic-v1"
                 Margin="0,0,0,10" />

        <Label Content="Tracker URL" />
        <TextBox Text="{Binding TrackerUrl, UpdateSourceTrigger=PropertyChanged}"
                 Style="{StaticResource HintTextBoxStyle}" controls:HintBehavior.Hint="Ex: https://np2ptp.vercel.app"
                 Margin="0,0,0,10" />

        <Button Content="Save" Command="{Binding SaveCommand}" HorizontalAlignment="Left" Width="80" />
    </StackPanel>
</UserControl>
```

Edit `src/Np2ptpGui/Views/DownloadsView.xaml` — add `xmlns:controls` and the style/hint to the link textbox:

```xml
<UserControl x:Class="Np2ptpGui.Views.DownloadsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:Np2ptpGui.Controls">
    <DockPanel Margin="10">
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,10">
            <TextBox Width="500" Text="{Binding DownloadLinkInput, UpdateSourceTrigger=PropertyChanged}"
                     Style="{StaticResource HintTextBoxStyle}" controls:HintBehavior.Hint="Cole aqui um link np2ptp:... ou caminho de um .nptp" />
            <Button Content="+ New download" Command="{Binding StartDownloadCommand}" Margin="10,0,0,0" />
        </StackPanel>
        <ListView ItemsSource="{Binding FetchOperations}">
            <ListView.View>
                <GridView>
                    <GridViewColumn Header="Link" DisplayMemberBinding="{Binding InputOrLink}" Width="300" />
                    <GridViewColumn Header="Status" DisplayMemberBinding="{Binding Status}" Width="100" />
                    <GridViewColumn Header="Detail" DisplayMemberBinding="{Binding DetailText}" Width="250" />
                    <GridViewColumn Header="Progress" Width="150">
                        <GridViewColumn.CellTemplate>
                            <DataTemplate>
                                <ProgressBar Minimum="0" Maximum="1" Value="{Binding ProgressFraction, Mode=OneWay}" Width="130" Height="16" />
                            </DataTemplate>
                        </GridViewColumn.CellTemplate>
                    </GridViewColumn>
                    <GridViewColumn Header="" Width="80">
                        <GridViewColumn.CellTemplate>
                            <DataTemplate>
                                <Button Content="Retry"
                                        Command="{Binding DataContext.RetryOperationCommand, RelativeSource={RelativeSource AncestorType=ListView}}"
                                        CommandParameter="{Binding Id}" />
                            </DataTemplate>
                        </GridViewColumn.CellTemplate>
                    </GridViewColumn>
                </GridView>
            </ListView.View>
        </ListView>
    </DockPanel>
</UserControl>
```

Edit `src/Np2ptpGui/Views/ShareView.xaml` — add `xmlns:controls` and the style/hint to the pack-input textbox (button layout for Task 3's file/folder pickers is added later; only the hint goes in now):

```xml
<UserControl x:Class="Np2ptpGui.Views.ShareView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:Np2ptpGui.Controls">
    <DockPanel Margin="10">
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,10">
            <TextBox Width="500" Text="{Binding PackInputPath, UpdateSourceTrigger=PropertyChanged}"
                     Style="{StaticResource HintTextBoxStyle}" controls:HintBehavior.Hint="Arquivo ou pasta a compartilhar" />
            <Button Content="+ Pack" Command="{Binding StartPackCommand}" Margin="10,0,0,0" />
        </StackPanel>
        <ListView ItemsSource="{Binding PackOperations}">
            <ListView.View>
                <GridView>
                    <GridViewColumn Header="Input" DisplayMemberBinding="{Binding InputOrLink}" Width="300" />
                    <GridViewColumn Header="Status" DisplayMemberBinding="{Binding Status}" Width="100" />
                    <GridViewColumn Header="Detail" DisplayMemberBinding="{Binding DetailText}" Width="250" />
                    <GridViewColumn Header="Link" DisplayMemberBinding="{Binding ResultLink}" Width="200" />
                    <GridViewColumn Header="" Width="80">
                        <GridViewColumn.CellTemplate>
                            <DataTemplate>
                                <Button Content="Retry"
                                        Command="{Binding DataContext.RetryOperationCommand, RelativeSource={RelativeSource AncestorType=ListView}}"
                                        CommandParameter="{Binding Id}" />
                            </DataTemplate>
                        </GridViewColumn.CellTemplate>
                    </GridViewColumn>
                </GridView>
            </ListView.View>
        </ListView>
    </DockPanel>
</UserControl>
```

- [ ] **Step 4: Build and manually verify**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Np2ptpGui.sln`
Expected: 0 errors.

Run: `& "C:\Program Files\dotnet\dotnet.exe" run --project src\Np2ptpGui`
Expected: gray placeholder text appears in the four Settings fields, the Downloads link box, and the Share input box whenever they're empty, and disappears as soon as you type a character in each. Close the app.

- [ ] **Step 5: Commit**

```bash
git add src/Np2ptpGui/Controls/HintBehavior.cs src/Np2ptpGui/App.xaml src/Np2ptpGui/Views/SettingsView.xaml src/Np2ptpGui/Views/DownloadsView.xaml src/Np2ptpGui/Views/ShareView.xaml
git commit -m "feat: add placeholder hint text to all free-text inputs"
```

---

### Task 3: Folder/file pickers (Settings, Share) + the two new Settings checkboxes

**Files:**
- Modify: `src/Np2ptpGui/ViewModels/SettingsViewModel.cs`
- Modify: `src/Np2ptpGui/Views/SettingsView.xaml`
- Modify: `src/Np2ptpGui/ViewModels/MainViewModel.cs`
- Modify: `src/Np2ptpGui/Views/ShareView.xaml`

**Interfaces:**
- Consumes: `AppConfig.AlwaysUseDownloadDefaults`/`KeepStoreByDefault` (Task 1).
- Produces: `SettingsViewModel.BrowseDownloadFolderCommand`, `SettingsViewModel.BrowseStoreFolderCommand`, `SettingsViewModel.AlwaysUseDownloadDefaults` (bool, bindable), `SettingsViewModel.KeepStoreByDefault` (bool, bindable) — all persisted by the existing `SaveCommand`. `MainViewModel.BrowsePackFileCommand`, `MainViewModel.BrowsePackFolderCommand`. Task 6 reads `_config.AlwaysUseDownloadDefaults`/`KeepStoreByDefault` (already wired here) from `MainViewModel`.

No automated tests — dialog invocation needs a live WPF dispatcher/user interaction, same rationale as Task 2. Verified manually in Step 4.

**Note (post-Task-2 correction):** Task 2 originally planned a `Style="{StaticResource HintTextBoxStyle}"` attribute alongside `controls:HintBehavior.Hint` on every hinted `TextBox`. That `Style`/`VisualBrush`/`Binding` approach turned out to crash the app on startup (`App.xaml` has no `StartupUri`, so `Application.Resources` never actually loads in this project) and, even after working around that, never rendered the hint text at all (`RelativeSource AncestorType` can't resolve from inside a `VisualBrush.Visual`, which is a disconnected visual tree). Task 2 ended up replacing the whole mechanism with a code-behind approach in `HintBehavior.cs` that manages `TextBox.Background` directly — no `Style` or resource lookup involved anywhere anymore. Every `TextBox` below carries `controls:HintBehavior.Hint="..."` only, with **no `Style=` attribute** — do not add one back.

- [ ] **Step 1: Add Browse commands + the two checkboxes to `SettingsViewModel`**

Replace the full contents of `src/Np2ptpGui/ViewModels/SettingsViewModel.cs`:

```csharp
namespace Np2ptpGui.ViewModels;

using System;
using System.IO;
using System.Threading;
using Np2ptpGui.Models;
using Np2ptpGui.Services;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ConfigStore _configStore;
    private readonly BinaryManager _binaryManager;
    private readonly AppConfig _config;

    private string _binaryPath;
    public string BinaryPath { get => _binaryPath; set => SetField(ref _binaryPath, value); }

    private string _defaultDownloadFolder;
    public string DefaultDownloadFolder { get => _defaultDownloadFolder; set => SetField(ref _defaultDownloadFolder, value); }

    private string _storeFolder;
    public string StoreFolder { get => _storeFolder; set => SetField(ref _storeFolder, value); }

    private string _defaultListenAddress;
    public string DefaultListenAddress { get => _defaultListenAddress; set => SetField(ref _defaultListenAddress, value); }

    private string _trackerUrl;
    public string TrackerUrl { get => _trackerUrl; set => SetField(ref _trackerUrl, value); }

    private bool _alwaysUseDownloadDefaults;
    public bool AlwaysUseDownloadDefaults { get => _alwaysUseDownloadDefaults; set => SetField(ref _alwaysUseDownloadDefaults, value); }

    private bool _keepStoreByDefault;
    public bool KeepStoreByDefault { get => _keepStoreByDefault; set => SetField(ref _keepStoreByDefault, value); }

    private string _updateStatus = "";
    public string UpdateStatus { get => _updateStatus; set => SetField(ref _updateStatus, value); }

    public RelayCommand SaveCommand { get; }
    public RelayCommand CheckForUpdateCommand { get; }
    public RelayCommand BrowseDownloadFolderCommand { get; }
    public RelayCommand BrowseStoreFolderCommand { get; }

    public SettingsViewModel(ConfigStore configStore, BinaryManager binaryManager, AppConfig config)
    {
        _configStore = configStore;
        _binaryManager = binaryManager;
        _config = config;

        _binaryPath = binaryManager.ExePath;
        _defaultDownloadFolder = config.DefaultDownloadFolder;
        _storeFolder = config.StoreFolder;
        _defaultListenAddress = config.DefaultListenAddress;
        _trackerUrl = config.TrackerUrl;
        _alwaysUseDownloadDefaults = config.AlwaysUseDownloadDefaults;
        _keepStoreByDefault = config.KeepStoreByDefault;

        SaveCommand = new RelayCommand(_ =>
        {
            _config.DefaultDownloadFolder = DefaultDownloadFolder;
            _config.StoreFolder = StoreFolder;
            _config.DefaultListenAddress = DefaultListenAddress;
            _config.TrackerUrl = TrackerUrl;
            _config.AlwaysUseDownloadDefaults = AlwaysUseDownloadDefaults;
            _config.KeepStoreByDefault = KeepStoreByDefault;
            _configStore.Save(_config);
        });

        CheckForUpdateCommand = new RelayCommand(async _ =>
        {
            UpdateStatus = "checking...";
            try
            {
                var updated = await _binaryManager.CheckForUpdateAsync(CancellationToken.None);
                UpdateStatus = updated ? "updated to the latest release" : "already up to date";
            }
            catch (Exception ex)
            {
                UpdateStatus = $"check failed: {ex.Message}";
            }
        });

        BrowseDownloadFolderCommand = new RelayCommand(_ =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                InitialDirectory = Directory.Exists(DefaultDownloadFolder) ? DefaultDownloadFolder : "",
            };
            if (dialog.ShowDialog() == true) DefaultDownloadFolder = dialog.FolderName;
        });

        BrowseStoreFolderCommand = new RelayCommand(_ =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                InitialDirectory = Directory.Exists(StoreFolder) ? StoreFolder : "",
            };
            if (dialog.ShowDialog() == true) StoreFolder = dialog.FolderName;
        });
    }
}
```

- [ ] **Step 2: Add Browse buttons + checkboxes to Settings view**

Replace the full contents of `src/Np2ptpGui/Views/SettingsView.xaml`:

```xml
<UserControl x:Class="Np2ptpGui.Views.SettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:Np2ptpGui.Controls">
    <StackPanel Margin="10">
        <Label Content="np2ptp.exe path" />
        <DockPanel>
            <Button DockPanel.Dock="Right" Content="Check for update" Command="{Binding CheckForUpdateCommand}" />
            <TextBox Text="{Binding BinaryPath}" IsReadOnly="True" />
        </DockPanel>
        <TextBlock Text="{Binding UpdateStatus}" Margin="0,4,0,10" />

        <Label Content="Default download folder" />
        <DockPanel Margin="0,0,0,10">
            <Button DockPanel.Dock="Right" Content="Procurar..." Command="{Binding BrowseDownloadFolderCommand}" />
            <TextBox Text="{Binding DefaultDownloadFolder, UpdateSourceTrigger=PropertyChanged}"
                     controls:HintBehavior.Hint="Ex: C:\Users\voce\Downloads" />
        </DockPanel>

        <Label Content="Store folder" />
        <DockPanel Margin="0,0,0,10">
            <Button DockPanel.Dock="Right" Content="Procurar..." Command="{Binding BrowseStoreFolderCommand}" />
            <TextBox Text="{Binding StoreFolder, UpdateSourceTrigger=PropertyChanged}"
                     controls:HintBehavior.Hint="Ex: C:\Users\voce\np2ptp-store" />
        </DockPanel>

        <Label Content="Default listen address" />
        <TextBox Text="{Binding DefaultListenAddress, UpdateSourceTrigger=PropertyChanged}"
                 controls:HintBehavior.Hint="Ex: /ip4/0.0.0.0/udp/0/quic-v1"
                 Margin="0,0,0,10" />

        <Label Content="Tracker URL" />
        <TextBox Text="{Binding TrackerUrl, UpdateSourceTrigger=PropertyChanged}"
                 controls:HintBehavior.Hint="Ex: https://np2ptp.vercel.app"
                 Margin="0,0,0,10" />

        <CheckBox Content="Sempre usar essas configurações ao baixar" IsChecked="{Binding AlwaysUseDownloadDefaults}" Margin="0,10,0,4" />
        <CheckBox Content="Manter store por padrão" IsChecked="{Binding KeepStoreByDefault}" Margin="0,0,0,10" />

        <Button Content="Save" Command="{Binding SaveCommand}" HorizontalAlignment="Left" Width="80" />
    </StackPanel>
</UserControl>
```

- [ ] **Step 3: Add file/folder picker buttons to Share view + `MainViewModel`**

Edit `src/Np2ptpGui/ViewModels/MainViewModel.cs` — add two commands and their fields. Insert after the existing `RetryOperationCommand` property declaration (line with `public RelayCommand RetryOperationCommand { get; }`):

```csharp
    public RelayCommand RetryOperationCommand { get; }
    public RelayCommand BrowsePackFileCommand { get; }
    public RelayCommand BrowsePackFolderCommand { get; }
```

Insert the two new command initializations at the end of the constructor, right before the closing brace, after the existing `RetryOperationCommand = new RelayCommand(...)` block:

```csharp
        RetryOperationCommand = new RelayCommand(param =>
        {
            if (param is string id) _taskManager.Retry(id);
        });

        BrowsePackFileCommand = new RelayCommand(_ =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            if (dialog.ShowDialog() == true) PackInputPath = dialog.FileName;
        });

        BrowsePackFolderCommand = new RelayCommand(_ =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (dialog.ShowDialog() == true) PackInputPath = dialog.FolderName;
        });
    }
}
```

(That replaces the file's final `});\n    }\n}` with the block above — the net effect is two new `RelayCommand` assignments added after the existing `RetryOperationCommand` one, before the constructor's closing braces.)

Edit `src/Np2ptpGui/Views/ShareView.xaml` — add the two buttons next to the input box (width trimmed from 500 to 380 to make room):

```xml
<UserControl x:Class="Np2ptpGui.Views.ShareView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:Np2ptpGui.Controls">
    <DockPanel Margin="10">
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,10">
            <TextBox Width="380" Text="{Binding PackInputPath, UpdateSourceTrigger=PropertyChanged}"
                     controls:HintBehavior.Hint="Arquivo ou pasta a compartilhar" />
            <Button Content="Arquivo..." Command="{Binding BrowsePackFileCommand}" Margin="10,0,0,0" />
            <Button Content="Pasta..." Command="{Binding BrowsePackFolderCommand}" Margin="5,0,0,0" />
            <Button Content="+ Pack" Command="{Binding StartPackCommand}" Margin="10,0,0,0" />
        </StackPanel>
        <ListView ItemsSource="{Binding PackOperations}">
            <ListView.View>
                <GridView>
                    <GridViewColumn Header="Input" DisplayMemberBinding="{Binding InputOrLink}" Width="300" />
                    <GridViewColumn Header="Status" DisplayMemberBinding="{Binding Status}" Width="100" />
                    <GridViewColumn Header="Detail" DisplayMemberBinding="{Binding DetailText}" Width="250" />
                    <GridViewColumn Header="Link" DisplayMemberBinding="{Binding ResultLink}" Width="200" />
                    <GridViewColumn Header="" Width="80">
                        <GridViewColumn.CellTemplate>
                            <DataTemplate>
                                <Button Content="Retry"
                                        Command="{Binding DataContext.RetryOperationCommand, RelativeSource={RelativeSource AncestorType=ListView}}"
                                        CommandParameter="{Binding Id}" />
                            </DataTemplate>
                        </GridViewColumn.CellTemplate>
                    </GridViewColumn>
                </GridView>
            </ListView.View>
        </ListView>
    </DockPanel>
</UserControl>
```

- [ ] **Step 4: Build and manually verify**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Np2ptpGui.sln`
Expected: 0 errors.

Run: `& "C:\Program Files\dotnet\dotnet.exe" run --project src\Np2ptpGui`
Expected: Settings shows "Procurar..." buttons next to Download folder/Store folder that open a native folder picker and fill the textbox on selection; the two new checkboxes appear and toggle; Share shows "Arquivo..."/"Pasta..." buttons that open the matching native picker and fill the input box. Close the app.

- [ ] **Step 5: Commit**

```bash
git add src/Np2ptpGui/ViewModels/SettingsViewModel.cs src/Np2ptpGui/Views/SettingsView.xaml src/Np2ptpGui/ViewModels/MainViewModel.cs src/Np2ptpGui/Views/ShareView.xaml
git commit -m "feat: add folder/file pickers and download-default checkboxes to Settings/Share"
```

---

### Task 4: `FetchOptionsDialog` (standalone modal, not yet wired in)

**Files:**
- Create: `src/Np2ptpGui/Views/FetchOptionsDialog.xaml`
- Create: `src/Np2ptpGui/Views/FetchOptionsDialog.xaml.cs`

**Interfaces:**
- Consumes: `Np2ptpGui.Controls.HintBehavior.Hint` (Task 2).
- Produces: `Np2ptpGui.Views.FetchOptionsDialog(string defaultReconstructFolder, string defaultStoreFolder, bool defaultKeepStore)` — a `Window` with public read-only `ReconstructFolder` (string), `StoreFolder` (string), `KeepStore` (bool) populated after `ShowDialog()` returns `true`. Task 6 constructs this and reads those three properties.

No automated tests — a WPF `Window` needs a live STA dispatcher to construct/show, which this test project has no infrastructure for (no `[StaFact]`/WPF test host anywhere in the existing suite). Verified manually in Task 6's end-to-end check, once it's actually reachable from the running app.

**Note (post-Task-2 correction):** Task 2 replaced the original `Style="{StaticResource HintTextBoxStyle}"` mechanism with a pure code-behind approach in `HintBehavior.cs` (it manages `TextBox.Background` directly via a `TextChanged` handler — no `Style`, no `VisualBrush`-with-`Binding`, no resource dictionary anywhere). The XAML below only needs `controls:HintBehavior.Hint="..."` on each `TextBox` — no `Style=` attribute, and no resource merging of any kind. This is why the two `TextBox`es below carry only the `Hint` attribute.

- [ ] **Step 1: Write the dialog XAML**

Create `src/Np2ptpGui/Views/FetchOptionsDialog.xaml`:

```xml
<Window x:Class="Np2ptpGui.Views.FetchOptionsDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:Np2ptpGui.Controls"
        Title="Opções do download" Height="280" Width="480"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize">
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
</Window>
```

- [ ] **Step 2: Write the code-behind**

Create `src/Np2ptpGui/Views/FetchOptionsDialog.xaml.cs`:

```csharp
namespace Np2ptpGui.Views;

using System.Windows;

public partial class FetchOptionsDialog : Window
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

- [ ] **Step 3: Build**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Np2ptpGui.sln`
Expected: 0 errors. (Not reachable from the running app yet — nothing constructs `FetchOptionsDialog` until Task 6 — so there's no manual-run check here beyond a clean build.)

- [ ] **Step 4: Commit**

```bash
git add src/Np2ptpGui/Views/FetchOptionsDialog.xaml src/Np2ptpGui/Views/FetchOptionsDialog.xaml.cs
git commit -m "feat: add standalone FetchOptionsDialog window"
```

---

### Task 5: `TaskManager` store isolation + keep/discard cleanup

**Files:**
- Modify: `src/Np2ptpGui/Services/TaskManager.cs`
- Modify: `src/Np2ptpGui/ViewModels/MainViewModel.cs`
- Modify: `tests/helpers/FakeNp2ptpHelper/Program.cs`
- Modify: `tests/Np2ptpGui.Tests/Services/TaskManagerTests.cs`

**Interfaces:**
- Consumes: nothing new from other tasks in this plan (works standalone against the existing `AppConfig`/`HistoryStore`/`ProcessRunner`).
- Produces: `TaskManager.StartFetch(string link, string reconstructFolder, string storeFolder, bool keepStore, bool useFec)` — **signature change** from the current `StartFetch(string link, string outputFolder, bool useFec)`. Task 6 changes the call site again to add the dialog branch, but this task must already update `MainViewModel`'s call site to the new 5-arg form (using `_config` defaults directly) so the build stays green in between.

- [ ] **Step 1: Write the failing tests**

Add these three tests to `tests/Np2ptpGui.Tests/Services/TaskManagerTests.cs`, right after `Constructor_WithExistingHistoryOnDisk_SeedsOperationsAndEntries` (before `StartPack_OnResultEvent_...`):

```csharp
    [Fact]
    public async Task StartFetch_StoresUnderStoreFolderSlashOperationId_AndDeletesItOnSuccessWhenNotKeeping()
    {
        var dir = NewTempDir();
        var historyStore = new HistoryStore(dir);
        var manager = new TaskManager(FakeHelperPath, historyStore);
        var storeRoot = Path.Combine(dir, "store-root");

        Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", "fetch-ok");
        try
        {
            var vm = manager.StartFetch("np2ptp:deadbeef", Path.Combine(dir, "out"), storeRoot, keepStore: false, useFec: false);
            var storeSubfolder = Path.Combine(storeRoot, vm.Id);

            // FakeNp2ptpHelper doesn't touch disk itself; simulate that the real
            // np2ptp process would have created chunk files under the store
            // subfolder np2ptp-gui computed for this operation.
            Directory.CreateDirectory(storeSubfolder);
            File.WriteAllText(Path.Combine(storeSubfolder, "chunk0"), "fake chunk data");

            await WaitUntilAsync(() => vm.Status == "Completed", TimeSpan.FromSeconds(5));

            Assert.False(Directory.Exists(storeSubfolder));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", null);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task StartFetch_OnSuccessWithKeepStoreTrue_LeavesTheStoreSubfolderInPlace()
    {
        var dir = NewTempDir();
        var historyStore = new HistoryStore(dir);
        var manager = new TaskManager(FakeHelperPath, historyStore);
        var storeRoot = Path.Combine(dir, "store-root");

        Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", "fetch-ok");
        try
        {
            var vm = manager.StartFetch("np2ptp:deadbeef", Path.Combine(dir, "out"), storeRoot, keepStore: true, useFec: false);
            var storeSubfolder = Path.Combine(storeRoot, vm.Id);
            Directory.CreateDirectory(storeSubfolder);
            File.WriteAllText(Path.Combine(storeSubfolder, "chunk0"), "fake chunk data");

            await WaitUntilAsync(() => vm.Status == "Completed", TimeSpan.FromSeconds(5));

            Assert.True(Directory.Exists(storeSubfolder));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", null);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Retry_OfAFetch_DoesNotDeleteTheOriginalStoreSubfolderOnItsOwnSuccess()
    {
        var dir = NewTempDir();
        var historyStore = new HistoryStore(dir);
        var manager = new TaskManager(FakeHelperPath, historyStore);
        var storeRoot = Path.Combine(dir, "store-root");

        Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", "fetch-ok");
        try
        {
            var original = manager.StartFetch("np2ptp:deadbeef", Path.Combine(dir, "out"), storeRoot, keepStore: false, useFec: false);
            var storeSubfolder = Path.Combine(storeRoot, original.Id);
            Directory.CreateDirectory(storeSubfolder);
            File.WriteAllText(Path.Combine(storeSubfolder, "chunk0"), "fake chunk data");
            await WaitUntilAsync(() => original.Status == "Completed", TimeSpan.FromSeconds(5));
            // keepStore: false means the original run's own subfolder is gone by now.
            Assert.False(Directory.Exists(storeSubfolder));

            // Retry re-runs with the SAME args (including the original --store path),
            // which is a feature (content-addressed store resumes from what's there),
            // but does not get its own cleanup-on-success hook - matches the
            // already-accepted "retried rows don't get full first-class treatment"
            // limitation documented in docs/LESSONS-LEARNED.md.
            var retried = manager.Retry(original.Id);
            Assert.NotNull(retried);
            Directory.CreateDirectory(storeSubfolder);
            File.WriteAllText(Path.Combine(storeSubfolder, "chunk0"), "fake chunk data again");

            await WaitUntilAsync(() => retried!.Status == "Completed", TimeSpan.FromSeconds(5));

            Assert.True(Directory.Exists(storeSubfolder));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", null);
            Directory.Delete(dir, recursive: true);
        }
    }
```

Then update the two existing tests that call the old 3-arg `StartFetch` — `StartFetch_OnErrorEvent_MarksOperationErrorAndPersistsMessage` and `Retry_AfterError_StartsANewOperationWithTheSameInput` — replacing:

```csharp
            var vm = manager.StartFetch("np2ptp:deadbeef", @"C:\downloads", useFec: false);
```

with:

```csharp
            var vm = manager.StartFetch("np2ptp:deadbeef", @"C:\downloads", @"C:\store", keepStore: true, useFec: false);
```

and:

```csharp
            var failed = manager.StartFetch("np2ptp:deadbeef", @"C:\downloads", useFec: false);
```

with:

```csharp
            var failed = manager.StartFetch("np2ptp:deadbeef", @"C:\downloads", @"C:\store", keepStore: true, useFec: false);
```

- [ ] **Step 2: Run tests to verify they fail**

Run (PowerShell): `& "C:\Program Files\dotnet\dotnet.exe" test tests\Np2ptpGui.Tests --filter TaskManagerTests`
Expected: compile error — `StartFetch` doesn't have a 5-arg overload yet, and the `"fetch-ok"` scenario doesn't exist in `FakeNp2ptpHelper` yet.

- [ ] **Step 3: Add the `fetch-ok` scenario to `FakeNp2ptpHelper`**

Edit `tests/helpers/FakeNp2ptpHelper/Program.cs` — add a new `case` between `"pack-ok"` and `"fetch-error"`:

```csharp
    case "fetch-ok":
        Console.WriteLine("""{"event":"progress","op":"fetch","chunks_done":5,"chunks_total":10}""");
        Thread.Sleep(150);
        Console.WriteLine("""{"event":"result","op":"fetch","path":"C:\\downloads\\file.bin","chunks_total":10,"chunks_new":10}""");
        return 0;
```

The 150ms delay before the result line gives tests a window to create the simulated store subfolder after `StartFetch` returns (and they know the generated operation id) but before the success/cleanup handler runs — same delay technique already used by the `"pack-ok"` scenario.

- [ ] **Step 4: Rewrite `TaskManager.StartFetch` and `Start` for store isolation + cleanup**

Edit `src/Np2ptpGui/Services/TaskManager.cs`. Add `using System.IO;` to the top usings block (alongside the existing `using System;` etc).

Replace `StartFetch`:

```csharp
    public OperationViewModel StartFetch(string link, string reconstructFolder, string storeFolder, bool keepStore, bool useFec)
    {
        var id = Guid.NewGuid().ToString("n");
        var storeSubfolder = Path.Combine(storeFolder, id);
        var args = new List<string> { "fetch", link, "--out", reconstructFolder, "--store", storeSubfolder, "--json" };
        if (useFec) args.Add("--fec");
        return Start(id, OperationType.Fetch, link, args,
            onCompletedSuccessfully: keepStore ? null : () => TryDeleteDirectory(storeSubfolder));
    }
```

Replace `StartServe` and `StartPack` (each now generates its own id before calling `Start`):

```csharp
    public OperationViewModel StartServe(string nptpFile, string storeFolder, string listenAddress, string trackerUrl)
    {
        var args = new List<string>
        {
            "serve", nptpFile, "--store", storeFolder, "--listen", listenAddress, "--tracker", trackerUrl, "--json",
        };
        return Start(Guid.NewGuid().ToString("n"), OperationType.Serve, nptpFile, args);
    }

    public OperationViewModel StartPack(string input, string? outputFile, string storeFolder, bool noCopy)
    {
        var args = new List<string> { "pack", input, "--store", storeFolder, "--json" };
        if (outputFile is not null) { args.Add("--out"); args.Add(outputFile); }
        if (noCopy) args.Add("--no-copy");
        return Start(Guid.NewGuid().ToString("n"), OperationType.Pack, input, args);
    }
```

Replace the private `Start` method's signature and body (keep everything else about `Start` identical — locking, try/catch, `_uiDispatch` usage — only the signature and the two lines noted below change):

```csharp
    private OperationViewModel Start(string id, OperationType type, string inputOrLink, List<string> args, Action? onCompletedSuccessfully = null)
    {
        var vm = new OperationViewModel(id, type, inputOrLink);
        var entry = new TaskHistoryEntry
        {
            Id = id,
            Type = type,
            InputOrLink = inputOrLink,
            Status = OperationStatus.Running,
            StartedAt = DateTime.UtcNow,
        };
        _entries[id] = entry;
        _startArgsById[id] = (type, inputOrLink, args);
        Operations.Add(vm);
        PersistHistory();

        var runner = ProcessRunner.Create(_exePath, args);
        _runners[id] = runner;
        // These two callbacks fire on a background thread (Process's
        // OutputDataReceived/Exited events do not run on the UI thread), so
        // every touch of `vm` (a WPF-bound ViewModel) is marshaled via
        // _uiDispatch before mutating it.
        runner.EventReceived += evt => _uiDispatch(() =>
        {
            lock (entry)
            {
                try
                {
                    vm.Apply(evt);
                    if (evt.Kind == NdjsonEventKind.Result)
                    {
                        entry.Status = OperationStatus.Completed;
                        entry.OutputPath = evt.Path;
                        entry.FinishedAt = DateTime.UtcNow;
                        PersistHistory();
                        onCompletedSuccessfully?.Invoke();
                    }
                    else if (evt.Kind == NdjsonEventKind.Error)
                    {
                        entry.Status = OperationStatus.Error;
                        entry.ErrorMessage = evt.Message;
                        entry.FinishedAt = DateTime.UtcNow;
                        PersistHistory();
                    }
                }
                catch (Exception)
                {
                    if (entry.Status == OperationStatus.Running)
                    {
                        entry.Status = OperationStatus.Error;
                        entry.ErrorMessage ??= "internal error while recording operation result";
                        entry.FinishedAt ??= DateTime.UtcNow;
                        vm.Status = entry.Status.ToString();
                        vm.ErrorMessage = entry.ErrorMessage;
                        vm.IsError = true;
                    }
                }
            }
        });
        runner.Exited += code => _uiDispatch(() =>
        {
            lock (entry)
            {
                try
                {
                    if (entry.Status == OperationStatus.Running)
                    {
                        entry.Status = code == 0 ? OperationStatus.Completed : OperationStatus.Error;
                        entry.ErrorMessage ??= code == 0 ? null : $"process exited with code {code}";
                        entry.FinishedAt = DateTime.UtcNow;
                        vm.Status = entry.Status.ToString();
                        if (entry.ErrorMessage is not null)
                        {
                            vm.ErrorMessage = entry.ErrorMessage;
                            vm.IsError = true;
                        }
                        PersistHistory();
                    }
                }
                catch (Exception)
                {
                    if (entry.Status == OperationStatus.Running)
                    {
                        entry.Status = OperationStatus.Error;
                        entry.ErrorMessage ??= "internal error while recording operation result";
                        entry.FinishedAt ??= DateTime.UtcNow;
                        vm.Status = entry.Status.ToString();
                        vm.ErrorMessage = entry.ErrorMessage;
                        vm.IsError = true;
                    }
                }
            }
        });
        runner.Start();
        return vm;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception)
        {
            // Best-effort cleanup: a locked file or an already-gone directory
            // must never crash the app, same "best effort" pattern already
            // used for history persistence failures elsewhere in this class.
        }
    }
```

Note `onCompletedSuccessfully` is only invoked from the `NdjsonEventKind.Result` branch, not from `Exited(code == 0)` — a real `np2ptp fetch` always emits a `result` JSON line before exiting 0 on success, and hooking both would risk a double-invoke race between the two event handlers for no benefit (the `entry.Status == Running` guard on the `Exited` branch already prevents that in practice once `Result` has set it to `Completed`, but there's no reason to rely on that guard for correctness here — simpler to only wire the one, documented path).

Replace `Retry` (now generates a fresh id before calling `Start`):

```csharp
    /// <summary>Re-runs a previously started operation with the same arguments, as a new row.</summary>
    public OperationViewModel? Retry(string operationId)
    {
        if (!_startArgsById.TryGetValue(operationId, out var original)) return null;
        return Start(Guid.NewGuid().ToString("n"), original.Type, original.InputOrLink, new List<string>(original.Args));
    }
```

- [ ] **Step 5: Update `MainViewModel`'s call site to the new signature (defaults-only, no dialog yet)**

Edit `src/Np2ptpGui/ViewModels/MainViewModel.cs` — replace the `StartDownloadCommand` body:

```csharp
        StartDownloadCommand = new RelayCommand(_ =>
        {
            if (string.IsNullOrWhiteSpace(DownloadLinkInput)) return;
            _taskManager.StartFetch(DownloadLinkInput, _config.DefaultDownloadFolder, _config.StoreFolder, _config.KeepStoreByDefault, useFec: false);
            DownloadLinkInput = "";
        });
```

(This is intentionally the "always use defaults" behavior for now — Task 6 adds the dialog branch on top of this.)

- [ ] **Step 6: Run tests to verify they pass**

Run (PowerShell): `& "C:\Program Files\dotnet\dotnet.exe" test tests\Np2ptpGui.Tests --filter TaskManagerTests`
Expected: PASS, all 8 tests (5 existing + 3 new).

- [ ] **Step 7: Full test suite + build, 3x via PowerShell**

Run three times: `& "C:\Program Files\dotnet\dotnet.exe" test Np2ptpGui.sln`
Expected: all runs green (build already covers `MainViewModel`'s updated call site). If the pre-existing `STATUS_CONTROL_C_EXIT` cold-start flake (documented in `docs/LESSONS-LEARNED.md`) appears on a first run after a fresh build, re-run once — that's a known, unrelated, non-deterministic timing issue, not a regression from this task.

- [ ] **Step 8: Commit**

```bash
git add src/Np2ptpGui/Services/TaskManager.cs src/Np2ptpGui/ViewModels/MainViewModel.cs tests/helpers/FakeNp2ptpHelper/Program.cs tests/Np2ptpGui.Tests/Services/TaskManagerTests.cs
git commit -m "feat: isolate each fetch's store under StoreFolder/<operationId>, add keep/discard cleanup"
```

---

### Task 6: Wire the Fetch options dialog into `MainViewModel`

**Files:**
- Modify: `src/Np2ptpGui/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `Np2ptpGui.Views.FetchOptionsDialog` (Task 4), `TaskManager.StartFetch(string, string, string, bool, bool)` (Task 5), `AppConfig.AlwaysUseDownloadDefaults`/`KeepStoreByDefault` (Task 1, set via Task 3's Settings UI).
- Produces: final end-user-visible behavior — nothing further consumes this; it's the integration point.

No automated tests — this is the same category of WPF dialog-invocation wiring as Tasks 2-4. Verified manually in Step 3, which is also the first point at which `FetchOptionsDialog` becomes reachable from the running app.

- [ ] **Step 1: Replace `StartDownloadCommand` with the dialog-gated version**

Edit `src/Np2ptpGui/ViewModels/MainViewModel.cs`. Add `using Np2ptpGui.Views;` to the top usings block (alongside the existing `using Np2ptpGui.Models;`/`using Np2ptpGui.Services;`) — `MainViewModel` lives in `namespace Np2ptpGui.ViewModels`, a sibling of `Np2ptpGui.Views`, so an explicit `using` is needed to reference `FetchOptionsDialog` unqualified (there's no existing cross-namespace reference like this elsewhere in the codebase to follow, so don't rely on implicit namespace nesting - just import it).

Replace the `StartDownloadCommand` body written in Task 5 Step 5:

```csharp
        StartDownloadCommand = new RelayCommand(_ =>
        {
            if (string.IsNullOrWhiteSpace(DownloadLinkInput)) return;

            string reconstructFolder;
            string storeFolder;
            bool keepStore;
            if (_config.AlwaysUseDownloadDefaults)
            {
                reconstructFolder = _config.DefaultDownloadFolder;
                storeFolder = _config.StoreFolder;
                keepStore = _config.KeepStoreByDefault;
            }
            else
            {
                var dialog = new FetchOptionsDialog(_config.DefaultDownloadFolder, _config.StoreFolder, _config.KeepStoreByDefault);
                if (dialog.ShowDialog() != true) return; // cancelled - link stays typed, nothing starts
                reconstructFolder = dialog.ReconstructFolder;
                storeFolder = dialog.StoreFolder;
                keepStore = dialog.KeepStore;
            }

            _taskManager.StartFetch(DownloadLinkInput, reconstructFolder, storeFolder, keepStore, useFec: false);
            DownloadLinkInput = "";
        });
```

- [ ] **Step 2: Build**

Run: `& "C:\Program Files\dotnet\dotnet.exe" build Np2ptpGui.sln`
Expected: 0 errors.

- [ ] **Step 3: Manually verify both paths end-to-end**

Run: `& "C:\Program Files\dotnet\dotnet.exe" run --project src\Np2ptpGui`

With "Sempre usar essas configurações ao baixar" **unchecked** (default): paste a link into Downloads, click "+ New download" — the `FetchOptionsDialog` opens pre-filled with the Settings defaults; adjust a folder via "Procurar...", toggle "Manter store", click OK — a new row starts in the Downloads list. Click "+ New download" again and click Cancelar this time — no new row appears and the link stays in the box.

Go to Settings, check "Sempre usar essas configurações ao baixar", Save. Back in Downloads, paste a link and click "+ New download" — no dialog appears, a row starts immediately using the Settings defaults.

Close the app.

- [ ] **Step 4: Commit**

```bash
git add src/Np2ptpGui/ViewModels/MainViewModel.cs
git commit -m "feat: wire FetchOptionsDialog into the download flow, gated by Always-use-defaults"
```

---

## Post-plan note

After Task 6, run the full suite one more time (PowerShell, 3x) as a final sanity check before considering this mergeable — same "don't trust two green reviews without your own extra run" lesson from the original build (see `docs/LESSONS-LEARNED.md`, "Bugs de concorrência achados"). Nothing in this plan touches shared mutable state the way the earlier `HistoryStore`/`TaskManager` regression did, but the habit is cheap and the plan doesn't get to skip it just because the risk looks lower this time.
