namespace Np2ptpGui.Tests.Services;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Np2ptpGui.Models;
using Np2ptpGui.Services;
using Xunit;

// StartServe_ThenStopAllServesAsync_StopsTheRunningServe exercises
// ConsoleCtrl.TrySendCtrlC (via StopAllServesAsync -> StopAsync ->
// ProcessRunner.StopGracefullyAsync), the same process-wide
// AttachConsole/FreeConsole/SetConsoleCtrlHandler state that
// ConsoleCtrlTests and ProcessRunnerTests' CTRL_C test touch (see
// ConsoleCtrlCollection.cs). Without this, xUnit can run this class's
// tests in parallel with those, and even with ConsoleCtrl's internal
// semaphore serializing the signal-send itself, that's not sufficient to
// prevent rare cross-test flakiness observed in full-suite runs.
[Collection("ConsoleCtrl")]
public class TaskManagerTests
{
    private static string FakeHelperPath => Path.Combine(AppContext.BaseDirectory, "FakeNp2ptpHelper.exe");

    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), "np2ptp-gui-tests-" + Guid.NewGuid());

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.True(condition(), "condition not met within timeout");
    }

    [Fact]
    public async Task Constructor_WithExistingHistoryOnDisk_SeedsOperationsAndEntries()
    {
        var dir = NewTempDir();
        try
        {
            var seedingStore = new HistoryStore(dir);
            seedingStore.Save(new[]
            {
                new TaskHistoryEntry
                {
                    Id = "old-1",
                    Type = OperationType.Pack,
                    InputOrLink = @"C:\some\input",
                    OutputPath = @"C:\store\old-1.nptp",
                    Status = OperationStatus.Completed,
                    StartedAt = DateTime.UtcNow.AddDays(-1),
                    FinishedAt = DateTime.UtcNow.AddDays(-1),
                },
                new TaskHistoryEntry
                {
                    Id = "old-2",
                    Type = OperationType.Fetch,
                    InputOrLink = "np2ptp:deadbeef",
                    Status = OperationStatus.Error,
                    ErrorMessage = "boom",
                    StartedAt = DateTime.UtcNow.AddDays(-1),
                    FinishedAt = DateTime.UtcNow.AddDays(-1),
                },
            });

            // A fresh HistoryStore instance over the same directory, mirroring how a new
            // app session constructs its own HistoryStore against the persisted appDataDir.
            var historyStore = new HistoryStore(dir);
            var manager = new TaskManager(FakeHelperPath, historyStore);

            Assert.Equal(2, manager.Operations.Count);

            var completed = manager.Operations.Single(o => o.Id == "old-1");
            Assert.Equal("Completed", completed.Status);
            Assert.Equal(@"C:\store\old-1.nptp", completed.OutputPath);
            Assert.Equal(1.0, completed.ProgressFraction);

            var errored = manager.Operations.Single(o => o.Id == "old-2");
            Assert.Equal("Error", errored.Status);
            Assert.Equal("boom", errored.ErrorMessage);
            Assert.Equal(0, errored.ProgressFraction);

            // Starting a brand-new operation and persisting should NOT clobber the
            // pre-existing history rows loaded at construction time.
            Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", "pack-ok");
            var vm = manager.StartPack(@"C:\some\other-input", null, @"C:\store", noCopy: false);
            var persistedImmediately = historyStore.Load();
            Assert.Equal(3, persistedImmediately.Count);
            Assert.Contains(persistedImmediately, e => e.Id == "old-1");
            Assert.Contains(persistedImmediately, e => e.Id == "old-2");
            Assert.Contains(persistedImmediately, e => e.Id == vm.Id);

            // Wait for the FakeNp2ptpHelper child process to actually exit before this
            // test's `finally` deletes the temp directory out from under it - otherwise
            // a background PersistHistory() call can race the delete and throw an
            // unhandled DirectoryNotFoundException on a ThreadPool thread, which aborts
            // the whole test host process instead of just failing this one test.
            await WaitUntilAsync(() => vm.Status == "Completed", TimeSpan.FromSeconds(5));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", null);
            Directory.Delete(dir, recursive: true);
        }
    }

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

    [Fact]
    public async Task StartPack_OnResultEvent_MarksOperationCompletedAndPersistsHistory()
    {
        var dir = NewTempDir();
        var historyStore = new HistoryStore(dir);
        var manager = new TaskManager(FakeHelperPath, historyStore);

        Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", "pack-ok");
        try
        {
            var vm = manager.StartPack(@"C:\some\input", null, @"C:\store", noCopy: false);

            await WaitUntilAsync(() => vm.Status == "Completed", TimeSpan.FromSeconds(5));

            Assert.Equal(1.0, vm.ProgressFraction);
            Assert.Equal("np2ptp:deadbeef", vm.ResultLink);

            var persisted = historyStore.Load();
            Assert.Single(persisted);
            Assert.Equal(OperationStatus.Completed, persisted[0].Status);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", null);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task StartFetch_OnErrorEvent_MarksOperationErrorAndPersistsMessage()
    {
        var dir = NewTempDir();
        var historyStore = new HistoryStore(dir);
        var manager = new TaskManager(FakeHelperPath, historyStore);

        Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", "fetch-error");
        try
        {
            var vm = manager.StartFetch("np2ptp:deadbeef", @"C:\downloads", @"C:\store", keepStore: true, useFec: false);

            await WaitUntilAsync(() => vm.Status == "Error", TimeSpan.FromSeconds(5));

            Assert.Equal("download failed: request to peer failed", vm.ErrorMessage);

            var persisted = historyStore.Load();
            Assert.Single(persisted);
            Assert.Equal(OperationStatus.Error, persisted[0].Status);
            Assert.Equal("download failed: request to peer failed", persisted[0].ErrorMessage);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", null);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task StartServe_ThenStopAllServesAsync_StopsTheRunningServe()
    {
        var dir = NewTempDir();
        var historyStore = new HistoryStore(dir);
        var manager = new TaskManager(FakeHelperPath, historyStore);

        Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", "serve-ok");
        try
        {
            var vm = manager.StartServe(@"C:\some\file.nptp", @"C:\store", "/ip4/0.0.0.0/udp/0/quic-v1", "https://np2ptp.vercel.app");

            await WaitUntilAsync(() => vm.DetailText.Contains("peers"), TimeSpan.FromSeconds(5));

            await manager.StopAllServesAsync(TimeSpan.FromSeconds(5));

            var persisted = historyStore.Load();
            Assert.Single(persisted);
            Assert.Equal(OperationStatus.Stopped, persisted[0].Status);
            Assert.Equal("Stopped", vm.Status);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", null);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Retry_AfterError_StartsANewOperationWithTheSameInput()
    {
        var dir = NewTempDir();
        var historyStore = new HistoryStore(dir);
        var manager = new TaskManager(FakeHelperPath, historyStore);

        Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", "fetch-error");
        try
        {
            var failed = manager.StartFetch("np2ptp:deadbeef", @"C:\downloads", @"C:\store", keepStore: true, useFec: false);
            await WaitUntilAsync(() => failed.Status == "Error", TimeSpan.FromSeconds(5));

            var retried = manager.Retry(failed.Id);

            Assert.NotNull(retried);
            Assert.NotEqual(failed.Id, retried!.Id);
            Assert.Equal(failed.InputOrLink, retried.InputOrLink);
            Assert.Equal(OperationType.Fetch, retried.Type);

            await WaitUntilAsync(() => retried.Status == "Error", TimeSpan.FromSeconds(5));
            Assert.Equal(2, manager.Operations.Count);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", null);
            Directory.Delete(dir, recursive: true);
        }
    }
}
