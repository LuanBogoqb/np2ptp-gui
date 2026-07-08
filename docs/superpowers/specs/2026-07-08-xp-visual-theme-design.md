# XP Visual Theme (Core) — Design Spec

> Sub-project 1 of 4 in the "np2ptp-gui XP theming" initiative. The other three (custom cursors, UI sounds, Clippy assistant) are separate specs, built on top of this one, in that order.

## Goal

Give np2ptp-gui a Windows XP Luna look (colors, fonts, control styling) with automatic light/dark variants that follow the Windows system theme setting, without touching window chrome, wallpaper, cursors, sounds, or the Clippy assistant — those are later sub-projects.

## Background

`src/Np2ptpGui/assets/` holds an ~89MB Windows XP asset pack (Clippy sprite sheets, cursors, fonts, icons, sounds, wallpapers, a UI reference sheet). This spec only consumes:
- `assets/Fonts/tahoma.ttf`
- `assets/Frame/UI Theme.png` (visual reference only — not sliced at runtime, see Approach below)

Everything else in `assets/` is out of scope here.

## Scope

**In scope:**
- Two theme resource dictionaries: XP Luna (light) and an XP-Luna-derived dark variant (invented — real XP never had a dark mode).
- Vector XAML control styles for `Button`, `CheckBox`, `TextBox`/`Border` reproducing the XP Luna look (blue gradients, frame borders) shown in `UI Theme.png`.
- Tahoma as the app-wide `FontFamily`, embedded as a resource (not relying on it being installed system-wide).
- Automatic light/dark selection from the Windows registry setting, applied at launch and re-applied live if the user changes the Windows theme while the app is running.
- Applying the theme to all existing windows: `MainWindow`, `DownloadsView`, `SeedingView`, `SettingsView`, `ShareView`, `FetchOptionsDialog`.

**Out of scope (deferred to later sub-projects or not planned):**
- Custom window chrome / title bar (stays native Windows chrome).
- Wallpaper images.
- App/window icon replacement.
- Custom cursors, UI sounds, Clippy assistant (separate specs).

## Architecture

Two `ResourceDictionary` XAML files:
- `src/Np2ptpGui/Themes/XpLightTheme.xaml`
- `src/Np2ptpGui/Themes/XpDarkTheme.xaml`

Each defines: color brushes (window background, control background, border, accent blue, text), an embedded Tahoma `FontFamily` resource, and `Style` entries (implicit, keyed by `TargetType`) for `Button`, `CheckBox`, `TextBox`. Both dictionaries expose the same resource keys so either can be merged interchangeably — callers never branch on which theme is active, they just get whichever dictionary was merged in.

**Why not `App.xaml`:** this project's `App.xaml` has no `StartupUri`, so the WPF markup compiler never emits `App.InitializeComponent()` and `Application.Resources` is unreachable dead code (confirmed during the previous fetch-ux plan, via `dotnet new wpf` repro). Every theme dictionary must instead be merged directly into each `Window`/`UserControl`'s own `Resources.MergedDictionaries`.

**Applying/swapping a theme:** a small helper, `Themes/ThemeManager.cs`, holds the currently active dictionary's `Uri` and exposes `ApplyTo(FrameworkElement root)` (clears and re-merges) plus a list of currently-open roots to refresh together when the OS theme changes. Each `Window`/`UserControl`'s constructor calls `ThemeManager.Register(this)` after `InitializeComponent()`.

**Detection:** `Services/WindowsThemeService.cs` reads `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` (`DWORD`, `0` = dark, `1` = light; missing key or any registry exception → treat as light/default). It exposes `IsLightTheme(): bool` and an event `ThemeChanged` raised from a `SystemEvents.UserPreferenceChanged` handler (filtered to `UserPreferenceCategory.General`, which is what fires on a theme toggle). `App.xaml.cs`'s startup path wires this service to `ThemeManager` once, at construction: initial read picks the starting dictionary, and every `ThemeChanged` event triggers `ThemeManager` to swap the dictionary on every registered root.

## Data Flow

1. App starts → `WindowsThemeService` reads the registry once → `ThemeManager` picks `XpLightTheme.xaml` or `XpDarkTheme.xaml`.
2. Each window/view registers itself with `ThemeManager` in its constructor → gets the currently active dictionary merged in immediately (so windows opened after startup, like `FetchOptionsDialog`, still pick up the right theme without extra wiring).
3. User changes Windows theme while app is running → `SystemEvents.UserPreferenceChanged` fires → `WindowsThemeService` re-reads the registry, raises `ThemeChanged` if the light/dark value actually flipped → `ThemeManager` re-merges the new dictionary into every currently-registered root.
4. Window closes → `ThemeManager` unregisters it (hook `Closed` event) so closed windows aren't retained/refreshed after disposal.

## Error Handling

- Registry key missing, access denied, or any exception while reading → `WindowsThemeService.IsLightTheme()` returns `true` (light/default), logged nowhere (not worth a dependency for this), never throws out of the service.
- `SystemEvents.UserPreferenceChanged` handler wraps its registry re-read in the same try/catch-and-default-to-light logic — a bad read during a live event must not crash the running app.

## Testing

- Unit tests for `WindowsThemeService`: since direct registry reads are awkward to unit test hermetically, wrap the raw registry access behind a small internal seam (e.g. a `Func<int?>` reader injected via constructor default parameter) so tests can simulate: key present & light (1), key present & dark (0), key missing (null → light default), and a thrown exception (→ light default) — all without touching the real registry.
- No unit test for the XAML styles themselves (WPF styles aren't meaningfully unit-testable) — verified manually instead.
- Manual verification (mandatory per this repo's established practice): launch the real app with the Windows theme set to light, screenshot every themed view; switch Windows to dark, screenshot again without restarting the app to confirm the live-switch path; confirm Tahoma is visibly applied and no view is left unthemed.

## Global Constraints

- No new NuGet dependencies. Registry access uses `Microsoft.Win32.Registry` (BCL, always available). Live theme-change detection uses `Microsoft.Win32.SystemEvents.UserPreferenceChanged`, which ships in `System.Windows.Extensions` and is available to a `net8.0-windows` WPF project out of the box — it does **not** require the `System.Windows.Forms` reference this project deliberately avoids (that avoidance was specifically about the `OpenFileDialog`/`OpenFolderDialog` ambiguity, not about `SystemEvents`). Confirm this at implementation time; if `SystemEvents` is for some reason unavailable, fall back to polling `SystemParameters` on a timer and note the deviation in the task report.
- Tahoma font file must be embedded as a WPF font resource (`Resource` build action + `pack://application:,,,/Fonts/#Tahoma` style reference), not copied loose next to the .exe.
- Every task must build cleanly and be independently, manually verifiable in the running app before being considered done (matches this repo's established SDD task-review discipline).
