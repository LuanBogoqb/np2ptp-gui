# Modern Theme (Fluent / Windows 11) — Design Spec

> New theme family added to np2ptp-gui's theming system, alongside the existing XP Luna theme
> (`docs/superpowers/specs/2026-07-08-xp-visual-theme-design.md`). Independent of the deferred
> XP sub-projects (cursors, sounds, Clippy) — those stay XP-only and untouched by this spec.

## Goal

Give np2ptp-gui a second, selectable UI theme, "Modern", that actually looks and feels like
Windows 11 (Fluent design, real Mica window backdrop, system accent color, light/dark that
follows the Windows setting) — so the app isn't stuck looking like it only knows how to impersonate
Windows XP. User picks XP Luna or Modern in Settings; XP Luna remains the default for existing
behavior continuity.

## Background

The XP Luna theme (shipped, `src/Np2ptpGui/Themes/XpControlStyles.xaml` + `XpColors.{Light,Dark}.xaml`)
is 100% hand-rolled vector XAML with zero new dependencies, by design. Reproducing genuine Windows 11
visuals (Mica/acrylic backdrop, Fluent control states, Segoe Fluent icon set) by hand is
disproportionately expensive and would still look approximate. This spec deliberately breaks the
XP theme's "no new NuGet dependency" constraint and adopts **WPF-UI** (`Wpf.Ui`, MIT-licensed,
actively maintained, targets `net8.0-windows`), which ships Fluent-styled implicit control
styles, a `FluentWindow` base class with real Mica/Acrylic composition, an `ApplicationThemeManager`
for light/dark, and `ApplicationAccentColorManager` for reading the Windows accent color.

## Scope

**In scope:**
- New NuGet dependency: `Wpf.Ui` (latest stable 3.x at implementation time — confirm exact version
  then).
- "Modern" theme family with Light + Dark variants, auto-following the Windows theme setting via
  the existing `WindowsThemeService`, exactly like XP Luna's light/dark already does.
- Real Mica window backdrop (not just tinted colors) on `MainWindow` and `FetchOptionsDialog`,
  active only when Modern is selected.
- Windows accent color (via `ApplicationAccentColorManager.ApplyWindowsAccentColor()`) drives
  Modern's accent/highlight color — not a fixed blue.
- A theme-family selector in Settings (XP Luna / Modern), persisted to `config.ini` via a new
  `AppConfig.ThemeFamily` field (default `"XpLuna"`, so existing installs/behavior don't change
  until the user opts in).
- Switching `ThemeFamily` takes effect after an app restart (Settings shows a "restart required"
  note on change; no live re-skin across families). Light/dark switching *within* whichever family
  is active remains live, matching current XP Luna behavior.

**Out of scope (unchanged from XP Luna's own boundaries, still deferred):**
- Custom cursors, UI sounds, Clippy assistant — unrelated to either theme family.
- Any further work on XP Luna itself beyond what's already shipped.
- Rewriting `DownloadsView`/`SeedingView`/`ShareView`/`SettingsView` layouts — Modern reskins the
  same existing controls (`Button`, `TextBox`, `CheckBox`, `ListView`, `GridViewColumnHeader`,
  `TabControl`/`TabItem`, `Label`), it does not redesign the screens.

## Architecture

**The compiled-base-class problem:** a XAML file's root element fixes its code-behind class's base
type at compile time — `MainWindow`/`FetchOptionsDialog` can't be `: Window` in one run and
`: Wpf.Ui.Controls.FluentWindow` in another depending on which theme is active. Since `FluentWindow`
is itself a `Window` subclass that behaves like a plain window when no backdrop is applied, the
fix is to make `MainWindow` and `FetchOptionsDialog` inherit from `FluentWindow`
**unconditionally**, and decide at runtime whether to activate Mica:
- XP Luna active → `WindowBackdropType.None`, XP's own `ThemeManager`/resource dictionaries apply
  exactly as today. Visually unchanged from the current shipped behavior.
- Modern active → `WindowBackdropType.Mica`, WPF-UI's Fluent resource dictionaries and
  `ApplicationThemeManager`/`ApplicationAccentColorManager` apply instead of XP's dictionaries.

**Startup flow (`App.xaml.cs`):**
1. Read `AppConfig.ThemeFamily`.
2. If `"XpLuna"`: unchanged existing flow — `WindowsThemeService` + `Np2ptpGui.Themes.ThemeManager`
   as shipped today.
3. If `"Modern"`: construct `WindowsThemeService` (reused, not duplicated), call
   `ApplicationThemeManager.Apply(isLight ? ApplicationTheme.Light : ApplicationTheme.Dark)` and
   `ApplicationAccentColorManager.ApplyWindowsAccentColor()`, and set
   `WindowBackdropType.Mica` on each `FluentWindow` as it's constructed.
4. Live light/dark switching reuses the existing `WindowsThemeService.ThemeChanged` event in both
   cases — only the handler differs (XP's `ThemeManager.ApplyTheme` vs WPF-UI's
   `ApplicationThemeManager.Apply`). Switching the *family* itself is restart-only (Scope, above).

**Settings:** a `ComboBox` (or two `RadioButton`s) bound to a new `SettingsViewModel.ThemeFamily`
property, saved via the existing `SaveCommand`/`ConfigStore` path. Changing it while running shows
an inline notice that the change applies after restart — no attempt to hot-swap families.

**Control coverage:** WPF-UI ships its own implicit styles for the standard controls this app
already uses (`Button`, `TextBox`, `CheckBox`, `ListView`, `GridViewColumnHeader`, `TabControl`/
`TabItem`), merged in via its own resource setup once `ApplicationThemeManager.Apply` runs — so
Modern should not need the same manual per-control-type styling pass XP Luna required (and where a
real coverage gap was found and fixed post-launch: ListView/GridViewColumnHeader/TabItem/Label).
Verify this claim empirically during implementation rather than assuming it holds for every
control this app happens to use.

## Data Flow

1. App starts → reads `ThemeFamily` from config → branches to either the existing XP flow or the
   new Modern flow (above).
2. Modern flow: `WindowsThemeService` reads the registry once → `ApplicationThemeManager.Apply`
   picks Light/Dark → accent color applied → each `FluentWindow` gets `Mica` backdrop.
3. User changes the Windows theme while running (either family) → existing
   `SystemEvents.UserPreferenceChanged` → `WindowsThemeService.ThemeChanged` → the active family's
   handler re-applies (XP's per-dictionary swap, or WPF-UI's `ApplicationThemeManager.Apply`).
4. User changes `ThemeFamily` in Settings → saved to `config.ini` → notice shown → takes effect on
   next launch.

## Error Handling

- Accent-color read failure or Mica unsupported on the running Windows build: WPF-UI's own APIs
  degrade gracefully by design (fall back to a default accent / solid background) — no extra
  try/catch needed on top of what the library already guarantees, confirm this during
  implementation rather than assuming.
- `ThemeFamily` value that's missing/unrecognized in `config.ini` (e.g. hand-edited or from a
  future version) → defaults to `"XpLuna"`, matching this project's existing "bad config value →
  safe default" convention (see `ConfigStore`'s existing `JsonException`/corrupt-file handling).

## Testing

- Unit test: `ConfigStore` round-trips `ThemeFamily` correctly (default when absent, persists when
  set) — same pattern as the existing `AlwaysUseDownloadDefaults`/`KeepStoreByDefault` tests.
- No unit tests for the Fluent visuals themselves (not meaningfully unit-testable, same reasoning
  as XP Luna) — manual verification instead, mandatory per this repo's established practice:
  - Launch with Modern selected, Windows in light mode: confirm real Mica backdrop, accent color
    matches the Windows setting, all 4 tabs + Settings + FetchOptionsDialog legible and styled.
  - Flip Windows to dark without restarting: confirm Modern's light/dark switches live.
  - Switch `ThemeFamily` to XP Luna in Settings, restart: confirm XP Luna renders exactly as it
    does today (no Mica leaking through, no WPF-UI resource bleed).
  - Switch back to Modern, restart: confirm it re-applies correctly.

## Global Constraints

- This spec explicitly **overrides** XP Luna's "no new NuGet dependency" constraint — `Wpf.Ui` is
  a new, real dependency, scoped to the Modern theme family only (XP Luna's own code paths stay
  dependency-free).
- `MainWindow` and `FetchOptionsDialog` change base class from `Window` to
  `Wpf.Ui.Controls.FluentWindow` unconditionally (needed for the restart-time backdrop toggle to
  work at all) — this is a real, if low-risk, structural change to both windows, not additive-only.
- Every task must build cleanly and be independently, manually verified in the running app (both
  families, both light/dark, a real restart-triggered family switch) before being considered done —
  matches this repo's established SDD task-review discipline, and directly answers the lesson
  learned from XP Luna's post-"final review" ListView/Label/TabItem coverage gap: check the actual
  primary-content screens, not just the screens a plan happened to enumerate.
