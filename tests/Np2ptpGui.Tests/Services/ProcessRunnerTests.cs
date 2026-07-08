namespace Np2ptpGui.Tests.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Np2ptpGui.Models;
using Np2ptpGui.Services;
using Xunit;

public class ProcessRunnerTests
{
    private static string FakeHelperPath => Path.Combine(AppContext.BaseDirectory, "FakeNp2ptpHelper.exe");
    private static string CtrlHelperPath => Path.Combine(AppContext.BaseDirectory, "CtrlSignalTestHelper.exe");

    [Fact]
    public async Task Start_EmitsParsedEventsInOrder_ThenExitsZero()
    {
        Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", "pack-ok");
        try
        {
            var events = new List<NdjsonEvent>();
            var exited = new TaskCompletionSource<int>();

            using var runner = ProcessRunner.Create(FakeHelperPath, new[] { "pack", "input", "--json" });
            runner.EventReceived += e => events.Add(e);
            runner.Exited += code => exited.TrySetResult(code);
            runner.Start();

            var code = await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(3, events.Count);
            Assert.Equal(NdjsonEventKind.Progress, events[0].Kind);
            Assert.Equal(NdjsonEventKind.Progress, events[1].Kind);
            Assert.Equal(NdjsonEventKind.Result, events[2].Kind);
            Assert.Equal("np2ptp:deadbeef", events[2].Root);
            Assert.Equal(0, code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", null);
        }
    }

    [Fact]
    public async Task Start_ErrorScenario_EmitsErrorEventAndNonZeroExit()
    {
        Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", "fetch-error");
        try
        {
            var events = new List<NdjsonEvent>();
            var exited = new TaskCompletionSource<int>();

            using var runner = ProcessRunner.Create(FakeHelperPath, new[] { "fetch", "np2ptp:x", "--json" });
            runner.EventReceived += e => events.Add(e);
            runner.Exited += code => exited.TrySetResult(code);
            runner.Start();

            var code = await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Single(events);
            Assert.Equal(NdjsonEventKind.Error, events[0].Kind);
            Assert.Equal("download failed: request to peer failed", events[0].Message);
            Assert.Equal(1, code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_NP2PTP_SCENARIO", null);
        }
    }

    [Fact]
    public async Task StopGracefullyAsync_SendsCtrlCAndWaitsForCleanExit()
    {
        var exited = new TaskCompletionSource<int>();

        using var runner = ProcessRunner.Create(CtrlHelperPath, Array.Empty<string>());
        runner.Exited += code => exited.TrySetResult(code);
        runner.Start();

        await Task.Delay(300); // let it install its CancelKeyPress handler

        await runner.StopGracefullyAsync(TimeSpan.FromSeconds(5));

        var code = await exited.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(0, code);
    }
}
