namespace Np2ptpGui.Services;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Np2ptpGui.Models;

public sealed class HistoryStore
{
    private readonly string _filePath;

    public HistoryStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "history.json");
    }

    public List<TaskHistoryEntry> Load()
    {
        if (!File.Exists(_filePath)) return new List<TaskHistoryEntry>();
        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<List<TaskHistoryEntry>>(json) ?? new List<TaskHistoryEntry>();
    }

    public void Save(IReadOnlyList<TaskHistoryEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    public void MarkRunningAsInterrupted()
    {
        var entries = Load();
        var changed = false;
        foreach (var entry in entries.Where(e => e.Status == OperationStatus.Running))
        {
            entry.Status = OperationStatus.Interrupted;
            entry.FinishedAt ??= System.DateTime.UtcNow;
            changed = true;
        }
        if (changed) Save(entries);
    }
}
