namespace Np2ptpGui.ViewModels;

using System.Globalization;
using Np2ptpGui.Models;

public sealed class OperationViewModel : ViewModelBase
{
    public string Id { get; }
    public OperationType Type { get; }
    public string InputOrLink { get; }

    private string _status = "Running";
    public string Status { get => _status; set => SetField(ref _status, value); }

    private double _progressFraction;
    public double ProgressFraction { get => _progressFraction; set => SetField(ref _progressFraction, value); }

    private string _detailText = "";
    public string DetailText { get => _detailText; set => SetField(ref _detailText, value); }

    private string? _resultLink;
    public string? ResultLink { get => _resultLink; set => SetField(ref _resultLink, value); }

    private string? _outputPath;
    public string? OutputPath { get => _outputPath; set => SetField(ref _outputPath, value); }

    private string? _errorMessage;
    public string? ErrorMessage { get => _errorMessage; set => SetField(ref _errorMessage, value); }

    private bool _isError;
    public bool IsError { get => _isError; set => SetField(ref _isError, value); }

    public OperationViewModel(string id, OperationType type, string inputOrLink)
    {
        Id = id;
        Type = type;
        InputOrLink = inputOrLink;
    }

    public void Apply(NdjsonEvent evt)
    {
        switch (evt.Kind)
        {
            case NdjsonEventKind.Progress:
                ApplyProgress(evt);
                break;
            case NdjsonEventKind.Result:
                Status = "Completed";
                ProgressFraction = 1.0;
                ResultLink = evt.Root;
                OutputPath = evt.Path;
                DetailText = BuildResultDetail(evt);
                break;
            case NdjsonEventKind.Status:
                DetailText = $"peers: {evt.Peers ?? 0}, served: {evt.BytesServed ?? 0}B, received: {evt.BytesReceived ?? 0}B";
                break;
            case NdjsonEventKind.Error:
                Status = "Error";
                ErrorMessage = evt.Message;
                IsError = true;
                break;
        }
    }

    private void ApplyProgress(NdjsonEvent evt)
    {
        if (evt.BytesTotal is > 0 && evt.BytesDone is not null)
        {
            ProgressFraction = (double)evt.BytesDone.Value / evt.BytesTotal.Value;
            DetailText = $"{evt.BytesDone.Value.ToString("N0", CultureInfo.InvariantCulture)} / {evt.BytesTotal.Value.ToString("N0", CultureInfo.InvariantCulture)} bytes";
        }
        else if (evt.ChunksTotal is > 0 && evt.ChunksDone is not null)
        {
            ProgressFraction = (double)evt.ChunksDone.Value / evt.ChunksTotal.Value;
            DetailText = evt.Phase is null
                ? $"{evt.ChunksDone.Value.ToString("N0", CultureInfo.InvariantCulture)} / {evt.ChunksTotal.Value.ToString("N0", CultureInfo.InvariantCulture)} chunks"
                : $"{evt.Phase}: {evt.ChunksDone.Value.ToString("N0", CultureInfo.InvariantCulture)} / {evt.ChunksTotal.Value.ToString("N0", CultureInfo.InvariantCulture)} chunks";
        }
    }

    private static string BuildResultDetail(NdjsonEvent evt) => evt.Op switch
    {
        "pack" => $"packed: {evt.ChunksTotal?.ToString("N0", CultureInfo.InvariantCulture)} chunks ({evt.ChunksNew?.ToString("N0", CultureInfo.InvariantCulture)} new)",
        "fetch" or "get" => $"fetched {evt.ChunksFetched?.ToString("N0", CultureInfo.InvariantCulture)} chunks ({evt.ChunksDeduped?.ToString("N0", CultureInfo.InvariantCulture)} deduped)",
        _ => "done",
    };
}
