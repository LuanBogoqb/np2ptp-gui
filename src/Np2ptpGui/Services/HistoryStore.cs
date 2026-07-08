namespace Np2ptpGui.Services;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Np2ptpGui.Models;

public sealed class HistoryStore
{
    private readonly string _filePath;
    private readonly object _saveLock = new();

    public HistoryStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "history.json");
    }

    public List<TaskHistoryEntry> Load()
    {
        if (!File.Exists(_filePath)) return new List<TaskHistoryEntry>();
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<TaskHistoryEntry>>(json) ?? new List<TaskHistoryEntry>();
        }
        catch (JsonException)
        {
            return new List<TaskHistoryEntry>();
        }
    }

    public void Save(IReadOnlyList<TaskHistoryEntry> entries)
    {
        // Guards against concurrent Save() calls on the same instance racing on
        // the shared fixed temp-file path (write-write and/or a sharing
        // violation on File.Move). Serializing here is sufficient regardless of
        // which thread(s) call in from.
        lock (_saveLock)
        {
            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
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
