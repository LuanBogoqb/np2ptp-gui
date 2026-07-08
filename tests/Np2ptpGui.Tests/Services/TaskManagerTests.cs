namespace Np2ptpGui.Tests.Services;

using System;
using System.IO;
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
            var vm = manager.StartFetch("np2ptp:deadbeef", @"C:\downloads", useFec: false);

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
            var failed = manager.StartFetch("np2ptp:deadbeef", @"C:\downloads", useFec: false);
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
