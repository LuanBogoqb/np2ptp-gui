namespace Np2ptpGui.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Np2ptpGui.Interop;
using Np2ptpGui.Models;

public sealed class ProcessRunner : IDisposable
{
    private readonly Process _process;
    private readonly TaskCompletionSource<int> _exitCode = new();

    public event Action<NdjsonEvent>? EventReceived;
    public event Action<int>? Exited;

    public int ProcessId => _process.Id;

    private ProcessRunner(Process process)
    {
        _process = process;
    }

    public static ProcessRunner Create(string exePath, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(exePath)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        return new ProcessRunner(process);
    }

    public void Start()
    {
        _process.OutputDataReceived += OnOutputLine;
        _process.Exited += (_, _) =>
        {
            var code = _process.ExitCode;
            _exitCode.TrySetResult(code);
            Exited?.Invoke(code);
        };
        _process.Start();
        _process.BeginOutputReadLine();
    }

    private void OnOutputLine(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data is null) return;
        if (NdjsonParser.TryParse(e.Data, out var evt) && evt is not null)
        {
            EventReceived?.Invoke(evt);
        }
    }

    public async Task StopGracefullyAsync(TimeSpan timeout)
    {
        if (_process.HasExited) return;
        // ConsoleCtrl.TrySendCtrlC blocks its calling thread for ~200ms (a
        // safety-critical internal Thread.Sleep — see ConsoleCtrl.cs). Run it
        // on a thread-pool thread so StopGracefullyAsync doesn't block its
        // caller (in production, the WPF UI thread) for that duration.
        await Task.Run(() => ConsoleCtrl.TrySendCtrlC(_process.Id));
        var completed = await Task.WhenAny(_exitCode.Task, Task.Delay(timeout));
        if (completed != _exitCode.Task && !_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }
    }

    public void Dispose() => _process.Dispose();
}
