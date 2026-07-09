# Modern Theme (Fluent / Windows 11) — Design Spec

> New theme family added to np2ptp-gui's theming system, alongside the existing XP Luna theme
> (`docs/superpowers/specs/2026-07-08-xp-visual-theme-design.md`). Independent of the deferred
> XP sub-projects (cursors, sounds, Clippy) — those stay XP-only and untouched by this spec.

> **Amendment (2026-07-09, during implementation):** the original design below made
> `MainWindow`/`FetchOptionsDialog` inherit `Wpf.Ui.Controls.FluentWindow` unconditionally to get a
> real Mica window backdrop. Empirically, `FluentWindow` rendered a **completely blank window** in
> this environment — confirmed via three independent methods (GDI screenshot, `PrintWindow` with
> `PW_RENDERFULLCONTENT`, and a UI Automation tree walk showing zero content children) — even for
> the XP Luna family with `WindowBackdropType.None`, i.e. even when Modern was never selected. It
> only started rendering *anything* once the WPF-UI-documented pattern was followed in full
> (`ExtendsContentIntoTitleBar="True"` + a `ui:TitleBar` + `WindowBackdropType="Mica"`), and even
> then only the Mica tint appeared, not the actual content. Taking over the title bar this way also
> conflicts with XP Luna's own established constraint (native Windows chrome only, see the XP spec's
> Out-of-scope list) — there is no way to keep FluentWindow for Modern only, since the base class is
> fixed at compile time (see Architecture, original text below) and both families share the same
> compiled `MainWindow`/`FetchOptionsDialog` classes.
>
> Decision: **drop `FluentWindow` and real Mica entirely.** `MainWindow`/`FetchOptionsDialog` stay
> plain `Window` for both families, unchanged from what's already shipped. Modern is implemented by
> merging WPF-UI's own Fluent control resource dictionaries (via `ApplicationThemeManager.Apply`)
> the same way XP Luna merges its own — this still delivers Fluent-styled Button/TextBox/ListView/
> etc. and the real Windows accent color, just without a translucent window backdrop. Every mention
> of `FluentWindow`/Mica/window backdrop below is superseded by this amendment; read Architecture
> with that in mind. This does not affect Tasks 1 (config field) or 2 (package reference), which are
> unchanged and already complete.

## Goal

Give np2ptp-gui a second, selectable UI theme, "Modern", that looks and feels like Windows 11
(Fluent-styled controls, system accent color, light/dark that follows the Windows setting) — so the
app isn't stuck looking like it only knows how to impersonate Windows XP. User picks XP Luna or
Modern in Settings; XP Luna remains the default for existing behavior continuity. (Real Mica window
backdrop was originally in scope — see the Amendment above for why it was dropped.)

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

`MainWindow` and `FetchOptionsDialog` stay plain `Window` for both families — no base class change,
no per-window setup at all. Modern is implemented purely via `Application`-level resource merging,
the same mechanism XP Luna already uses (`Np2ptpGui.Themes.ThemeManager` merging
`XpControlStyles.xaml`/`XpColors.*.xaml` into `Application.Current.Resources.MergedDictionaries`) —
except for Modern, the dictionaries being merged are WPF-UI's own, via its `ApplicationThemeManager`
API rather than a hand-written `ResourceDictionary`. Since implicit (TargetType-keyed) styles resolve
through the standard resource-lookup chain regardless of which `Window` subclass hosts them, this
gives Fluent-styled `Button`/`TextBox`/`ListView`/etc. on an ordinary native-chrome `Window`, with no
compiled-base-class conflict between the two families to resolve.

**Startup flow (`App.xaml.cs`):**
1. Read `AppConfig.ThemeFamily`.
2. If `"XpLuna"`: unchanged existing flow — `WindowsThemeService` + `Np2ptpGui.Themes.ThemeManager`
   as shipped today.
3. If `"Modern"`: construct `WindowsThemeService` (reused, not duplicated), call
   `ApplicationThemeManager.Apply(isLight ? ApplicationTheme.Light : ApplicationTheme.Dark)` and
   `ApplicationAccentColorManager.ApplyWindowsAccentColor()`. No window-level setup — `MainWindow`/
   `FetchOptionsDialog` need no changes at all for either family.
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
   picks Light/Dark → accent color applied. Both windows pick up the Fluent control styles
   automatically the moment they're constructed, same as XP Luna today.
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
  - Launch with Modern selected, Windows in light mode: confirm Fluent-styled controls, accent color
    matches the Windows setting, all 4 tabs + Settings + FetchOptionsDialog legible and styled,
    native window chrome intact (no blank window — this exact regression was caught once already).
  - Flip Windows to dark without restarting: confirm Modern's light/dark switches live.
  - Switch `ThemeFamily` to XP Luna in Settings, restart: confirm XP Luna renders exactly as it
    does today (no WPF-UI resource bleed).
  - Switch back to Modern, restart: confirm it re-applies correctly.

## Global Constraints

- This spec explicitly **overrides** XP Luna's "no new NuGet dependency" constraint — `Wpf.Ui` is
  a new, real dependency, scoped to the Modern theme family only (XP Luna's own code paths stay
  dependency-free).
- `MainWindow` and `FetchOptionsDialog` stay plain `Window` for both families — no base class
  change (see Amendment at the top of this document for why this was walked back from the original
  `FluentWindow` design).
- Every task must build cleanly and be independently, manually verified in the running app (both
  families, both light/dark, a real restart-triggered family switch) before being considered done —
  matches this repo's established SDD task-review discipline, and directly answers the lesson
  learned from XP Luna's post-"final review" ListView/Label/TabItem coverage gap: check the actual
  primary-content screens, not just the screens a plan happened to enumerate.
