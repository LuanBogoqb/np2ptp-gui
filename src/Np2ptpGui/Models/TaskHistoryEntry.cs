namespace Np2ptpGui.Models;

using System;

public enum OperationType
{
    Pack,
    Serve,
    Fetch,
}

public enum OperationStatus
{
    Running,
    Completed,
    Error,
    Stopped,
    Interrupted,
}

public sealed class TaskHistoryEntry
{
    public required string Id { get; init; }
    public required OperationType Type { get; set; }
    public required string InputOrLink { get; set; }
    public string? OutputPath { get; set; }
    public OperationStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
