namespace Np2ptpGui.Tests.Interop;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Np2ptpGui.Interop;
using Xunit;

[Collection("ConsoleCtrl")]
public class ConsoleCtrlTests
{
    [Fact]
    public void TrySendCtrlC_DeliversSignalToChildConsoleProcess()
    {
        var helperPath = Path.Combine(AppContext.BaseDirectory, "CtrlSignalTestHelper.exe");
        var psi = new ProcessStartInfo(helperPath)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)!;

        Thread.Sleep(300); // let the helper install its CancelKeyPress handler

        var sent = ConsoleCtrl.TrySendCtrlC(process.Id);
        Assert.True(sent);

        var output = process.StandardOutput.ReadToEnd();
        var exited = process.WaitForExit(5000);

        Assert.True(exited);
        Assert.Contains("SIGNAL:ControlC", output);
    }
}
