namespace Np2ptpGui.Services;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Np2ptpGui.Models;
using Np2ptpGui.ViewModels;

public sealed class TaskManager
{
    private readonly string _exePath;
    private readonly HistoryStore _historyStore;
    private readonly Action<Action> _uiDispatch;
    private readonly Dictionary<string, ProcessRunner> _runners = new();
    private readonly Dictionary<string, TaskHistoryEntry> _entries = new();
    private readonly Dictionary<string, (OperationType Type, string InputOrLink, List<string> Args)> _startArgsById = new();

    public ObservableCollection<OperationViewModel> Operations { get; } = new();

    public TaskManager(string exePath, HistoryStore historyStore, Action<Action>? uiDispatch = null)
    {
        _exePath = exePath;
        _historyStore = historyStore;
        _uiDispatch = uiDispatch ?? (action => action());

        foreach (var entry in _historyStore.Load())
        {
            var vm = new OperationViewModel(entry.Id, entry.Type, entry.InputOrLink)
            {
                Status = entry.Status.ToString(),
                OutputPath = entry.OutputPath,
                ErrorMessage = entry.ErrorMessage,
                ProgressFraction = entry.Status == OperationStatus.Completed ? 1.0 : 0,
            };
            _entries[entry.Id] = entry;
            Operations.Add(vm);
        }
    }

    public OperationViewModel StartFetch(string link, string outputFolder, bool useFec)
    {
        var args = new List<string> { "fetch", link, "--out", outputFolder, "--json" };
        if (useFec) args.Add("--fec");
        return Start(OperationType.Fetch, link, args);
    }

    public OperationViewModel StartServe(string nptpFile, string storeFolder, string listenAddress, string trackerUrl)
    {
        var args = new List<string>
        {
            "serve", nptpFile, "--store", storeFolder, "--listen", listenAddress, "--tracker", trackerUrl, "--json",
        };
        return Start(OperationType.Serve, nptpFile, args);
    }

    public OperationViewModel StartPack(string input, string? outputFile, string storeFolder, bool noCopy)
    {
        var args = new List<string> { "pack", input, "--store", storeFolder, "--json" };
        if (outputFile is not null) { args.Add("--out"); args.Add(outputFile); }
        if (noCopy) args.Add("--no-copy");
        return Start(OperationType.Pack, input, args);
    }

    private OperationViewModel Start(OperationType type, string inputOrLink, List<string> args)
    {
        var id = Guid.NewGuid().ToString("n");
        var vm = new OperationViewModel(id, type, inputOrLink);
        var entry = new TaskHistoryEntry
        {
            Id = id,
            Type = type,
            InputOrLink = inputOrLink,
            Status = OperationStatus.Running,
            StartedAt = DateTime.UtcNow,
        };
        _entries[id] = entry;
        _startArgsById[id] = (type, inputOrLink, args);
        Operations.Add(vm);
        PersistHistory();

        var runner = ProcessRunner.Create(_exePath, args);
        _runners[id] = runner;
        // These two callbacks fire on a background thread (Process's
        // OutputDataReceived/Exited events do not run on the UI thread), so
        // every touch of `vm` (a WPF-bound ViewModel) is marshaled via
        // _uiDispatch before mutating it.
        runner.EventReceived += evt => _uiDispatch(() =>
        {
            vm.Apply(evt);
            if (evt.Kind == NdjsonEventKind.Result)
            {
                entry.Status = OperationStatus.Completed;
                entry.OutputPath = evt.Path;
                entry.FinishedAt = DateTime.UtcNow;
                PersistHistory();
            }
            else if (evt.Kind == NdjsonEventKind.Error)
            {
                entry.Status = OperationStatus.Error;
                entry.ErrorMessage = evt.Message;
                entry.FinishedAt = DateTime.UtcNow;
                PersistHistory();
            }
        });
        runner.Exited += code => _uiDispatch(() =>
        {
            if (entry.Status == OperationStatus.Running)
            {
                entry.Status = code == 0 ? OperationStatus.Completed : OperationStatus.Error;
                entry.ErrorMessage ??= code == 0 ? null : $"process exited with code {code}";
                entry.FinishedAt = DateTime.UtcNow;
                vm.Status = entry.Status.ToString();
                if (entry.ErrorMessage is not null)
                {
                    vm.ErrorMessage = entry.ErrorMessage;
                    vm.IsError = true;
                }
                PersistHistory();
            }
        });
        runner.Start();
        return vm;
    }

    public async Task StopAsync(string operationId, TimeSpan timeout)
    {
        if (!_runners.TryGetValue(operationId, out var runner)) return;
        // Mark Stopped *before* awaiting the graceful stop, not after: once the
        // child process reacts to CTRL_C and exits, ProcessRunner.Exited fires
        // synchronously on that exit callback, while the continuation of
        // `await runner.StopGracefullyAsync(...)` below is resumed via a queued
        // (not inlined) continuation on a threadpool thread ~200ms later. If the
        // "Stopped" write happened only after the await (as a naive reading of
        // "stop, then mark stopped" would suggest), the Exited handler's own
        // `entry.Status == Running` guard would still see Running and win the
        // race, mislabeling a deliberate stop as "Completed"/"Error". Writing it
        // first makes the Exited handler's guard see the operation as already
        // finished and no-op, which is what actually makes this deterministic.
        if (_entries.TryGetValue(operationId, out var entry) && entry.Status == OperationStatus.Running)
        {
            entry.Status = OperationStatus.Stopped;
            entry.FinishedAt = DateTime.UtcNow;
            var vm = Operations.FirstOrDefault(o => o.Id == operationId);
            if (vm is not null)
            {
                vm.Status = OperationStatus.Stopped.ToString();
            }
            PersistHistory();
        }
        await runner.StopGracefullyAsync(timeout);
    }

    public async Task StopAllServesAsync(TimeSpan timeoutPerServe)
    {
        var serveIds = _entries.Values
            .Where(e => e.Type == OperationType.Serve && e.Status == OperationStatus.Running)
            .Select(e => e.Id)
            .ToList();
        foreach (var id in serveIds)
        {
            await StopAsync(id, timeoutPerServe);
        }
    }

    /// <summary>Re-runs a previously started operation with the same arguments, as a new row.</summary>
    public OperationViewModel? Retry(string operationId)
    {
        if (!_startArgsById.TryGetValue(operationId, out var original)) return null;
        return Start(original.Type, original.InputOrLink, new List<string>(original.Args));
    }

    private void PersistHistory() => _historyStore.Save(_entries.Values.ToList());
}
