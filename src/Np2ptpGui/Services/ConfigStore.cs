namespace Np2ptpGui.Services;

using System.IO;
using System.Text.Json;
using Np2ptpGui.Models;

public sealed class ConfigStore
{
    private readonly string _filePath;
    private readonly object _saveLock = new();

    public ConfigStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "config.json");
    }

    public AppConfig Load()
    {
        if (!File.Exists(_filePath)) return new AppConfig();
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch (JsonException)
        {
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        // Guards against concurrent Save() calls on the same instance racing on
        // the shared fixed temp-file path (write-write and/or a sharing
        // violation on File.Move). Serializing here is sufficient regardless of
        // which thread(s) call in from.
        lock (_saveLock)
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
    }
}
