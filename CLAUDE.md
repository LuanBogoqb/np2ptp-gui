# CLAUDE.md: guide for AI agents working on np2ptp-gui

This repo is a Windows-only WPF desktop client for np2ptp: paste a link, pick a folder,
watch it download. It downloads and manages the actual `np2ptp.exe` binary (from GitHub
Releases of the sibling `np2ptp` repo) rather than reimplementing the protocol.
`docs/LESSONS-LEARNED.md` is the source of truth for hard-won gotchas; read it before
touching process handling, threading, or the WPF/WinForms interop. `docs/BUILDING.md`,
`docs/FEATURES.md`, `docs/THEMES.md`, and `docs/flags.md` cover the rest.

## Build & test

```
dotnet build Np2ptpGui.sln
dotnet test Np2ptpGui.sln
```

This is a Windows-only project (`net8.0-windows`, WPF, WinForms tray icon) and must be built
on a Windows host; it cannot target or run on Linux/macOS. `src/Np2ptpGui/Np2ptpGui.csproj`
is the app; `tests/Np2ptpGui.Tests` covers the pieces that don't need a real np2ptp binary
(config parsing, task manager, process handling). Two of the test-project's own dependencies
are tiny helper console apps under `tests/helpers/` (`CtrlSignalTestHelper`,
`FakeNp2ptpHelper`), not part of the shipped app.

If `dotnet` is not on PATH in the current terminal, call it by full path (`C:\Program
Files\dotnet\dotnet.exe` from PowerShell, `/c/Program Files/dotnet/dotnet.exe` from Bash)
rather than assuming the build is broken. Do not add `2>&1` when running a native `.exe`
from PowerShell 5.1: it wraps stdout/stderr into `NativeCommandError` and flips `$?` to
false even on exit code 0. Tests that touch `ConsoleCtrl`/`StopGracefullyAsync` must be run
via PowerShell, not Bash: Git Bash's pseudo-console does not propagate
`GenerateConsoleCtrlEvent` to the child process, so a failure there under Bash is a terminal
limitation, not a real bug. A `ProcessRunnerTests` Ctrl+C test and a `TaskManagerTests`
cleanup test are documented flakes on the first `dotnet test` right after a fresh build
(timing/JIT cold start); the release CI retries the whole test run once for exactly this
reason, and that always passes.

## Golden rules

- **Never touch `BinaryManager.ExpectedSignerThumbprint` without a real reason.** It pins
  the exact Authenticode signer of the downloaded `np2ptp.exe`; a mismatch deletes the file
  and refuses it. This is deliberately not `X509Certificate.CreateFromSignedFile` (which
  reads embedded metadata without validating the signature actually matches the file), it
  calls `WinVerifyTrust` via `AuthenticodeVerifier`. If this ever needs to change, it is
  because np2ptp's own signing cert rotated, and the new thumbprint must come from a
  verified release, not be guessed.
- **`WinVerifyTrust` is called with `WTD_HASH_ONLY_FLAG`, not full CA chain validation.**
  This was a deliberate fix (`dae2670`): the default policy fails np2ptp's self-signed cert
  on any machine that hasn't imported it, even for a genuinely untampered file. The pinned
  thumbprint in `BinaryManager` is the actual trust anchor; don't "fix" this by re-enabling
  chain-of-trust checks. The two halves are only safe together: the flag alone accepts any
  signer, so a patch that drops or loosens the thumbprint comparison while keeping
  `WTD_HASH_ONLY_FLAG` turns the whole verification into theater. If you remove one, remove
  both and say so out loud.
- **Any change to `ConsoleCtrl.cs` must keep the `AttachConsole` -> ignore CTRL_C -> send ->
  `Thread.Sleep(200)` -> unignore -> `FreeConsole` order, including the sleep.** The CTRL_C
  event is only queued, not delivered synchronously; removing or shrinking the sleep risks
  the GUI process killing itself with the same signal it sent to the child. The whole
  sequence runs under a static `SemaphoreSlim` because `AttachConsole`/`FreeConsole` are
  process-wide Win32 state; two concurrent stops without that lock previously caused real
  races.
- **`ProcessRunner.EventReceived`/`Exited` fire on background threads, never the UI
  thread.** Any code that touches a bound ViewModel from those callbacks must go through the
  `uiDispatch` callback (`Dispatcher.Invoke` in production, wired in `App.xaml.cs`).
  `TaskManager` additionally wraps each callback body in `lock (entry)` plus try/catch,
  because `EventReceived` and `Exited` for the same operation can land on different
  threadpool threads microseconds apart, and an unhandled exception escaping a
  `Dispatcher.Invoke` callback crashes the whole process. Don't remove either the lock or
  the try/catch when editing these handlers.
- **In `TaskManager.StopAsync`, the entry is marked `Stopped` before awaiting
  `StopGracefullyAsync`, not after.** The child's `Exited` handler can fire before that
  continuation resumes; if the state write happened last, `Exited`'s own "still Running"
  guard would win the race and mislabel a deliberate stop as Completed/Error.
- **`ConfigStore.Save`/`HistoryStore.Save` write to a fixed `.tmp` path then `File.Move` it
  into place, under an instance lock.** The atomic-write pattern itself was added to survive
  a crash mid-write, but a fixed temp filename without the lock let two concurrent `Save()`
  calls on the same instance collide and throw `UnauthorizedAccessException` in production
  (event callbacks can run close together on different threadpool threads). Keep both the
  lock and the rename-based write together.
- **`UseWPF` and `UseWindowsForms` are both `true` in the csproj (needed for the tray
  `NotifyIcon`).** This makes `Application`, `MessageBox`, `Timer`, `Cursor`, etc. ambiguous
  (`CS0104`) in any file that also uses `System.Windows.Forms` types. Qualify explicitly
  (`System.Windows.MessageBox.Show(...)`) or alias with `using Application =
  System.Windows.Application;`; this is expected, not a sign something is broken.
- **Version comes from the running `np2ptp.exe`'s embedded `ProductVersion`
  (`FileVersionInfo`), never a sidecar file.** A separate version file could silently drift
  if someone replaces the exe by hand; a missing/unreadable version must fall back to "needs
  update", not throw.
- **Release signing (`.github/workflows/release.yml`) decodes the code-signing cert from
  `CERT_ENC`/`CERT_PASS` secrets into a temp file and deletes it in an `if: always()`
  step.** Never log, echo, or persist those secrets or the decoded `.pfx`; third-party
  Actions in that workflow are pinned to a commit SHA, not a mutable tag, for supply-chain
  reasons, keep that pattern for any new step you add.

## Conventions

MVVM: `ViewModels/` hold state and `RelayCommand`s, `ViewModelBase` provides
`SetField`/`INotifyPropertyChanged`, views are thin XAML + minimal code-behind. Services
(`Services/`) are plain, mostly-sealed classes with constructor-injected dependencies;
several take an internal `Func<...>` seam (e.g. `productVersionReader`, `signatureVerifier`)
purely so tests can fake OS-level effects without hitting real files or Win32 calls, follow
that pattern for new I/O-touching services rather than mocking frameworks. Background-thread
callbacks (process events, timers) are marshaled to the UI thread via an injected
`Action<Action>` dispatcher, never called directly against a ViewModel. Error handling
favors best-effort catch-and-continue for non-critical paths (history persistence, directory
cleanup) with a comment explaining why swallowing is correct there, and fail-loud (throw)
for correctness-critical paths like signature verification. Commit messages are `type: short
summary` subject lines (feat/fix/docs/chore/ci/refactor/style/build) with a body explaining
why a change was made and, for anything nontrivial, how it was actually verified (test
counts, a live manual run), not just what changed.

## Layout

`src/Np2ptpGui/`: the app. `Views/` + `.xaml.cs` code-behind per tab (Downloads, Seeding,
Share, Settings) plus `MainWindow`; `ViewModels/` for state/commands; `Services/` for
process management (`ProcessRunner`, `TaskManager`), persistence (`ConfigStore`,
`HistoryStore`), the np2ptp binary lifecycle (`BinaryManager`, `GitHubReleaseClient`,
`AuthenticodeVerifier`), NDJSON parsing of the child process's output (`NdjsonParser`), and
Windows theme detection; `Interop/ConsoleCtrl.cs` for the CTRL_C P/Invoke; `Themes/` for the
XP Luna and Modern (WPF-UI/Fluent) theme dictionaries and managers; `Models/` for plain data
(`AppConfig`, `TaskHistoryEntry`, `NdjsonEvent`). `tests/Np2ptpGui.Tests/` mirrors that
structure; `tests/helpers/` holds two standalone helper executables used only by tests (a
Ctrl+C signal target, a fake np2ptp CLI). The sibling protocol repo is
`E:\Repos\np2ptp-project\np2ptp` (Rust, its own `CLAUDE.md`); this repo only consumes its
released binary, it does not build or vendor its source.
