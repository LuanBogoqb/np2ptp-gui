# np2ptp-gui — desktop GUI for np2ptp

## Problem

`np2ptp` is a Rust CLI (`crates/np2ptp-node`, binary `np2ptp.exe` on Windows).
It already supports pack/get/serve/fetch, and a recently-added `--json` flag
that emits NDJSON progress/status/result/error events on stdout — but there
is no graphical way to drive it. Using it requires a terminal, remembering
flags, and manually tracking multiple in-flight transfers. The user wants a
Windows desktop app that makes it approachable: progress bars, a list of
what's been downloaded/is being shared, and a settings screen — comparable in
spirit to a torrent client like qBittorrent (multiple concurrent operations,
none of them blocking the UI or each other).

## Scope

In scope (v1):
- A new, separate repository (`np2ptp-gui`), WPF (.NET 8), Windows-only.
- Drives `np2ptp.exe` as a child process per operation; no FFI/bindings into
  the Rust crates. Parses the existing `--json` NDJSON event stream.
- Three operations: `fetch` (download over the network — the "Downloads"
  tab), `serve` (seed a `.nptp` — the "Seeding" tab), `pack` (create a
  `.nptp` + share link — the "Share" tab).
- Concurrent operations: each is an independent child process; the UI never
  blocks waiting on one to finish.
- Auto-fetch of the `np2ptp.exe` binary from the project's GitHub Releases on
  first run, with a best-effort, non-blocking update check on later runs.
- A settings screen: binary path, default download folder, store folder,
  default listen address, tracker URL.
- System tray behavior: closing the main window minimizes to tray and keeps
  any active `serve` running; only "Exit" from the tray menu fully quits (and
  cleanly stops active operations first).
- Local persistence of settings and task history (JSON files under
  `%AppData%\np2ptp-gui\`).

Out of scope (v1):
- `get` (the offline/local store-to-store transfer) — `fetch` (network path,
  the common case: paste a link, optionally discovered via tracker with no
  `--peer` needed) covers the "download something" story end to end. `get`
  can be added later as an advanced/offline option if needed.
- `info`/`relay` commands — no GUI surface for inspecting a raw `.nptp` file
  outside of a pack/fetch result, or running this machine as a public relay
  node.
- Auto-resuming operations that were mid-flight when the app was last fully
  exited (Exit, not just minimized) — they're marked "interrupted" in
  history, not silently retried.
- Non-Windows builds. The np2ptp GitHub release ships a Linux binary too, but
  this GUI targets WPF/Windows only for v1.
- Linking the Rust crates directly (e.g. via a Tauri/egui native GUI, or C
  FFI) — deliberately rejected in favor of subprocess + `--json`, see
  Architecture.

## Architecture

**No FFI.** The GUI never links `np2ptp-core`/`np2ptp-net`/etc. It only knows
how to launch `np2ptp.exe <subcommand> ...args --json` as a child process,
read its stdout NDJSON events, and (for `serve`) send it a stop signal. This
keeps the GUI decoupled from the Rust project's internals — any protocol
change in np2ptp requires zero GUI code changes, only a new binary.

**MVVM.** Standard WPF pattern: Views (XAML) bind to ViewModels, which expose
`ObservableCollection<OperationViewModel>` for the task list and plain
properties (with `INotifyPropertyChanged`) for progress/status fields. The UI
updates itself via data binding as ViewModel state changes — no manual
UI-thread polling code.

**TaskManager (the execution engine).** A single app-wide `TaskManager` owns
the list of `OperationViewModel`s (one per pack/serve/fetch, active or
finished). Starting an operation:

1. Build the argument list for `np2ptp.exe` from user input + settings
   defaults (e.g. `fetch np2ptp:<root> --out <folder> --json`,
   optionally `--fec`).
2. Launch via `System.Diagnostics.Process` with `RedirectStandardOutput`,
   reading lines asynchronously (`OutputDataReceived` or an async read loop).
3. Deserialize each NDJSON line (`{"event": "progress"|"result"|"status"|"error", ...}`)
   into a typed event and update the corresponding `OperationViewModel`
   property — bound UI elements (progress bar, status text, peers count for
   `serve`) update automatically.
4. **Stopping `serve`** (the only long-running, non-terminating operation):
   `np2ptp`'s Rust side calls `tokio::signal::ctrl_c()`, which on Windows
   only reacts to `CTRL_C_EVENT` (not `CTRL_BREAK_EVENT`). Since
   `GenerateConsoleCtrlEvent(CTRL_C_EVENT, ...)` can only be targeted at
   "every process sharing the caller's current console" (unlike
   `CTRL_BREAK_EVENT`, it can't be aimed at one specific process group), the
   GUI briefly attaches itself to the child's own (hidden) console via
   `AttachConsole(childPid)`, disables its own ctrl-handler for that instant
   via `SetConsoleCtrlHandler(NULL, TRUE)`, calls
   `GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0)`, then detaches
   (`FreeConsole()`). Because the child is the only process on that console,
   this reaches only it. If the process hasn't exited within ~3s, fall back
   to `Process.Kill()`.

Because each operation is a separate OS process, running several
concurrently (a fetch, a serve, and a pack all at once) requires no extra
scheduling logic in the GUI — the OS handles it, and the WPF UI thread only
ever reacts to incoming events, never blocks on a process.

## Binary bootstrap and update

On startup, the app checks `<app root>\bins\np2ptp.exe`:

- **Missing:** call `GET https://api.github.com/repos/LuanBogoqb/np2ptp/releases/latest`,
  download the `np2ptp-windows-x86_64.exe` asset, save it as
  `bins\np2ptp.exe`, and record the release tag in `bins\version.txt`. This
  is blocking and required — the app can't do anything without a binary; a
  failure here (e.g. no internet on first run) shows a blocking error dialog
  with a retry option.
- **Present:** attempt the same GitHub API call with a short timeout
  (~2–3s). If it succeeds and the latest tag differs from `version.txt`,
  download and replace the binary. If the call fails or times out (no
  internet, GitHub unreachable), silently continue with the existing binary
  — no error shown, no startup delay beyond the timeout.

Settings also expose a manual "check for update" button that repeats this
check on demand and reports the outcome (already up to date / updated to
vX.Y.Z / check failed).

## Screens

Main window: a central task list (qBittorrent-style — one row per operation:
name, type, progress, status/speed, action buttons) with a left-side
selector or tabs splitting context:

- **Downloads** (`fetch`): "+ New download" — paste a `np2ptp:<root>` link
  or browse to a `.nptp` file, choose destination (defaults from settings),
  toggle `--fec`. Row shows byte/chunk progress; on completion becomes a
  "done" row with an "Open folder" button.
- **Seeding** (`serve`): "+ Seed" — pick an existing `.nptp` (typically one
  just downloaded or packed). Row shows connected peers and bytes
  served/received (from the periodic `status` event), with a "Stop" button
  (graceful stop per Architecture above).
- **Share** (`pack`): "+ Pack" — pick a file or folder, optional name,
  toggle `--no-copy`. On completion, shows the `np2ptp:<root>` link with a
  copy button, and a "Seed now" shortcut that starts a `serve` for the
  resulting `.nptp` immediately.
- **Settings**: binary path (with "check for update" button), default
  download folder, store folder, default listen address, tracker URL.

**Tray behavior:** closing the main window (the X button) hides it to the
system tray instead of exiting; a tray icon menu offers "Open" and "Exit".
"Exit" performs a real shutdown: sends the graceful stop signal to every
active `serve` (waiting up to ~3s each, then force-killing stragglers)
before the process exits. This is the only path that fully quits the app and
its child processes.

## Persistence

Under `%AppData%\np2ptp-gui\`:

- `config.json` — the four settings fields + resolved binary path.
- `history.json` — the task list (operation type, link/file, output path,
  final status, timestamps), reloaded on startup so past downloads/shares
  survive an app restart. Any task still marked "running" when the app last
  fully exited (via tray Exit, not just window-close-to-tray) is shown as
  "interrupted" on reload — never silently auto-resumed.
- `bins\np2ptp.exe` + `bins\version.txt` — the managed binary and its
  release tag.

## Error handling

- A `{"event":"error",...}` NDJSON line marks that operation's row as
  "error" (red), with the message available via tooltip/expanded text, and
  a "Retry" button that re-runs the same operation with the same arguments.
- If a child process exits non-zero without ever having emitted an `error`
  event (e.g. it crashed or failed before printing anything), the row is
  still marked "error" with a generic "process exited with code N" message.
- Failure to obtain any usable `np2ptp.exe` at all (first run, no internet,
  download fails) is the one case that blocks the whole app — a modal error
  dialog with a "Retry download" action, since nothing else in the GUI can
  function without the binary.

## Testing

- Unit tests around NDJSON event parsing (feed sample lines from the real
  event shapes documented in np2ptp's own
  `docs/superpowers/specs/2026-07-07-json-progress-status-design.md`,
  assert the right ViewModel fields update).
- Unit tests for the binary bootstrap logic against a fake/mocked GitHub API
  response (missing binary → download; stale version tag → re-download;
  network failure → silent no-op when a binary already exists).
- Manual/integration verification (no automated UI test framework in v1):
  run a real `pack` → `serve` → `fetch` loop between two local folders using
  the built app, confirming progress bars move, the tray-minimize/Exit
  behavior works, and a `serve` stopped via the UI actually shuts down the
  underlying `np2ptp.exe` process (not left orphaned).
