# Design: Fetch UX overhaul, folder/file pickers, hint text, INI config

## Context

First hands-on look at the running app surfaced four UX gaps against the original design spec:
no file/folder pickers (everything is a raw textbox), no placeholder/hint text on inputs, config
persisted as JSON (user wants `.ini`), and the Fetch flow starts immediately on click with no way
to control where the reconstructed file goes, where its store lives, or whether to keep that store
around afterward.

Investigating the last point surfaced a real latent bug: `TaskManager.StartFetch` never passes
`--store` to `np2ptp fetch` today, so every fetch falls back to the CLI's shared default store
directory. Any "delete the store when done" feature built on top of that would risk deleting chunks
belonging to unrelated operations. This design fixes that as a prerequisite for the keep/discard
toggle to be safe at all.

Out of scope for this doc (separate, already-flagged effort): the Windows XP–style asset pack
(`src/Np2ptpGui/assets/`) and any theming/dark-mode work. That gets its own brainstorm.

## Goals

1. `ConfigStore` persists to `config.ini` (flat `key=value`, no sections) instead of `config.json`.
   No migration of existing `config.json` — app is unreleased, a one-time re-entry of settings is
   acceptable.
2. Folder/file picker buttons wherever the user currently types a path: Settings (download folder,
   store folder), Share/Pack (file **or** folder input), and the new Fetch options dialog below.
3. Placeholder/hint text on every free-text input so the user knows what belongs in each field.
4. New Fetch flow:
   - Settings gains two checkboxes: **"Always use these settings when downloading"** and **"Keep
     store by default"**.
   - If the first is checked, a new download starts immediately using the Settings defaults (today's
     behavior, just now store-isolated — see below).
   - If unchecked, a modal dialog appears before every new download, pre-filled from the Settings
     defaults, letting the user override: reconstruct-to folder, store folder, and keep-store
     checkbox (pre-filled from "Keep store by default").
   - Every fetch gets its own store subfolder (`<StoreFolder>/<operationId>`), never the bare root,
     so a discarded store can never touch another operation's chunks.
   - On successful completion, if "keep store" was off, the operation's store subfolder is deleted
     (best-effort; a failed delete never crashes the app).

## Non-goals

- No changes to Serve or Pack's own store-folder semantics beyond adding a picker button — they
  already pass an explicit `--store` and aren't affected by the fetch-isolation bug.
- No per-operation persistence of the keep-store choice across app restarts. A fetch that's still
  "Running" at restart is already marked `Interrupted` by existing `HistoryStore.MarkRunningAsInterrupted`
  and never resumes automatically; there's nothing to clean up for it. Retrying a historical/fetch
  row (existing `Retry`) re-runs with the **original** store subfolder (which is a feature — the
  store is content-addressed, so retry resumes from whatever chunks are still there) but does *not*
  get its own cleanup-on-success hook. This matches the already-accepted limitation that retried/
  historical rows don't get full first-class treatment (documented in `docs/LESSONS-LEARNED.md`).
- No abstraction layer (`IDialogService` or similar) for the picker/dialog calls. The codebase
  already calls `System.Windows.MessageBox.Show(...)` directly from ViewModels
  (`MainViewModel.StopOperationCommand`); this design follows the same pragmatic precedent rather
  than introducing a testable-but-unused seam. None of this is unit-testable without a live WPF
  dispatcher regardless of abstraction.

## Components

### 1. `ConfigStore` → INI

`AppConfig` gains two fields:
```csharp
public bool AlwaysUseDownloadDefaults { get; set; } = false;
public bool KeepStoreByDefault { get; set; } = true;
```

`ConfigStore` rewrite:
- File is `config.ini` instead of `config.json`.
- `Save`: same lock + atomic-temp-file-then-`File.Move` pattern already in place, just serializes
  as `Key=Value\n` lines (one per `AppConfig` property) instead of JSON.
- `Load`: parses `Key=Value` lines (split on first `=`, trim both sides); unknown or malformed
  lines are skipped, missing keys keep the `AppConfig` default. Booleans parsed case-insensitively
  ("true"/"false"); anything else falls back to the property's default.
- `Load`'s try/catch widens from `catch (JsonException)` to `catch (Exception)` — a hand-rolled
  parser doesn't throw a specific type, and this closes the previously-acknowledged gap ("Load()
  only catches JsonException, not IOException") as a side effect.
- No external INI library — five-ish flat fields, trivial to hand-roll, avoids a dependency purely
  to save ~30 lines.

`HistoryStore` is untouched (stays JSON — user only asked for config to move to INI).

### 2. Folder/file pickers

.NET 8 ships `Microsoft.Win32.OpenFolderDialog` (native folder picker) alongside the existing
`Microsoft.Win32.OpenFileDialog`. Both live in `Microsoft.Win32`, **not** `System.Windows.Forms`,
so none of this touches the `CS0104` ambiguity trap documented in `docs/LESSONS-LEARNED.md`.

- **Settings**: "Procurar..." button next to `DefaultDownloadFolder` and `StoreFolder` (same
  `DockPanel` pattern already used for the `BinaryPath` row). Opens `OpenFolderDialog` seeded with
  the current field value (if it's a valid existing path) or falls back to no initial directory.
- **Share/Pack**: two buttons next to the input textbox, "Arquivo..." (`OpenFileDialog`) and
  "Pasta..." (`OpenFolderDialog`), both write to the same `PackInputPath` binding — matches that
  `np2ptp pack` accepts either a file or a directory as input.
- **Fetch options dialog**: two `OpenFolderDialog` buttons, one per folder field (see below).

All picker buttons are wired directly in each ViewModel's `RelayCommand`, following the existing
`MessageBox.Show` precedent — no new indirection.

### 3. Hint/placeholder text

New `Np2ptpGui.Controls.HintBehavior` static class exposing one attached `DependencyProperty`,
`Hint` (string). A shared `Style` (added to `App.xaml`'s resources) targets `TextBox`, overlaying a
gray `TextBlock` bound to the attached `Hint` value, visible only while `Text` is empty (a
`DataTrigger`/`MultiTrigger` on `Text=""` — standard WPF watermark pattern, no adorner layer
needed). Applied via `Style="{StaticResource HintTextBoxStyle}" local:HintBehavior.Hint="..."` on:
- Settings: download folder, store folder, listen address, tracker URL (not `BinaryPath` — it's
  read-only, managed by the app).
- Downloads: the link input.
- Share: the pack input path.
- Fetch options dialog: both folder fields.

### 4. Fetch flow overhaul

**`TaskManager` changes:**

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

`Start`'s private signature gains a pre-generated `id` parameter (currently generated inside
`Start`; callers that don't need it ahead of time — `StartServe`, `StartPack`, `Retry` — just
generate it right before calling, same as today) and an optional `onCompletedSuccessfully` callback,
invoked inside the existing `NdjsonEventKind.Result` branch, inside the same `lock (entry)` /
`try/catch` that's already there — a cleanup failure is swallowed the same way a persistence
failure already is, never crashes the process.

`TryDeleteDirectory`: `Directory.Delete(path, recursive: true)` wrapped in `try/catch (Exception)`,
best-effort, matches the existing "best effort, never crash" pattern already used for history
persistence failures.

**`MainViewModel.StartDownloadCommand`:**

```csharp
StartDownloadCommand = new RelayCommand(_ =>
{
    if (string.IsNullOrWhiteSpace(DownloadLinkInput)) return;
    string reconstructFolder, storeFolder;
    bool keepStore;
    if (_config.AlwaysUseDownloadDefaults)
    {
        reconstructFolder = _config.DefaultDownloadFolder;
        storeFolder = _config.StoreFolder;
        keepStore = _config.KeepStoreByDefault;
    }
    else
    {
        var dialog = new Views.FetchOptionsDialog(_config.DefaultDownloadFolder, _config.StoreFolder, _config.KeepStoreByDefault);
        if (dialog.ShowDialog() != true) return; // cancelled — link stays typed, nothing starts
        (reconstructFolder, storeFolder, keepStore) = (dialog.ReconstructFolder, dialog.StoreFolder, dialog.KeepStore);
    }
    _taskManager.StartFetch(DownloadLinkInput, reconstructFolder, storeFolder, keepStore, useFec: false);
    DownloadLinkInput = "";
});
```

Note the existing unconditional `DownloadLinkInput = ""` moves to *after* the dialog gate — if the
user cancels the dialog, the link they typed/pasted stays in the box instead of being silently
discarded.

**`Views/FetchOptionsDialog.xaml(.cs)`** (new): a small modal `Window`, plain code-behind (no
ViewModel — same "not worth abstracting" call as the picker buttons; nothing here is unit-testable
regardless). Constructor takes the three defaults, pre-fills two folder textboxes (each with its
own Browse button, per section 2) and a "Manter store" checkbox. OK/Cancel buttons set
`DialogResult` and close; public `ReconstructFolder`/`StoreFolder`/`KeepStore` properties are read
by the caller after `ShowDialog()` returns `true`.

## Data flow (new Fetch, dialog path)

1. User types/pastes a link, clicks "+ New download".
2. `AlwaysUseDownloadDefaults` is off → `FetchOptionsDialog` opens, pre-filled from Settings.
3. User adjusts folders/checkbox (or just clicks OK to accept the pre-filled defaults) → OK.
4. `TaskManager.StartFetch` generates an operation id, builds `--store <StoreFolder>/<id>`, spawns
   `np2ptp fetch`.
5. On the `Result` NDJSON event (success), if `keepStore` was false, the store subfolder is deleted
   in the background; the operation still shows `Completed` regardless of whether cleanup succeeded.
6. On `Error`/process exit/manual Stop, no cleanup runs — the store subfolder (partial or complete)
   is left in place, so Retry can resume from it.

## Testing

Unit-testable:
- `ConfigStoreTests`: round-trip rewritten for `.ini` (all 7 fields including the 2 new bools),
  corrupted-file-returns-defaults (garbage text, not just garbage JSON), concurrent-`Save()`
  regression test already in place stays as-is (format-agnostic).
- `TaskManagerTests`: new test asserting `StartFetch` passes `--store <root>/<id>` (via
  `FakeNp2ptpHelper` echoing its args, or asserting on the spawned process's arguments) and that a
  successful completion with `keepStore: false` deletes that subfolder while `keepStore: true`
  leaves it. A third test confirms `Retry` of a fetch does *not* delete the store on its own
  completion (no cleanup hook carried over).

Not unit-testable (WPF dispatcher-dependent UI plumbing, consistent with existing precedent for
tray/startup wiring): picker buttons, hint-text watermark behavior, `FetchOptionsDialog` itself.
Verified manually instead — run the app, exercise each picker, confirm hint text appears/disappears,
walk the Fetch dialog both paths (defaults-on skip, defaults-off with an override), confirm the
store subfolder is/isn't deleted after a real fetch.

## Open risk carried forward, not addressed here

The `assets/` folder (XP-era icons/cursors/Clippy/etc., ~89 MB, likely Microsoft IP) is unrelated to
this spec. Flagged to the user already: fine for now while the repo is private, needs a decision
before the repo goes public. Tracked as part of the future theming brainstorm, not here.
