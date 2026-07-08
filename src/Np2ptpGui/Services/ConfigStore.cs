namespace Np2ptpGui.Services;

using System.IO;
using System.Text.Json;
using Np2ptpGui.Models;

public sealed class ConfigStore
{
    private readonly string _filePath;

    public ConfigStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "config.json");
    }

    public AppConfig Load()
    {
        if (!File.Exists(_filePath)) return new AppConfig();
        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
    }

    public void Save(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
