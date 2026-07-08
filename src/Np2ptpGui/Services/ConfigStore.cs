namespace Np2ptpGui.Services;

using System;
using System.Globalization;
using System.IO;
using Np2ptpGui.Models;

public sealed class ConfigStore
{
    private readonly string _filePath;
    private readonly object _saveLock = new();

    public ConfigStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "config.ini");
    }

    public AppConfig Load()
    {
        var config = new AppConfig();
        if (!File.Exists(_filePath)) return config;
        try
        {
            foreach (var line in File.ReadAllLines(_filePath))
            {
                var separatorIndex = line.IndexOf('=');
                if (separatorIndex < 0) continue;
                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();
                switch (key)
                {
                    case nameof(AppConfig.BinaryPath): config.BinaryPath = value; break;
                    case nameof(AppConfig.DefaultDownloadFolder): config.DefaultDownloadFolder = value; break;
                    case nameof(AppConfig.StoreFolder): config.StoreFolder = value; break;
                    case nameof(AppConfig.DefaultListenAddress): config.DefaultListenAddress = value; break;
                    case nameof(AppConfig.TrackerUrl): config.TrackerUrl = value; break;
                    case nameof(AppConfig.AlwaysUseDownloadDefaults):
                        config.AlwaysUseDownloadDefaults = TryParseBool(value, config.AlwaysUseDownloadDefaults);
                        break;
                    case nameof(AppConfig.KeepStoreByDefault):
                        config.KeepStoreByDefault = TryParseBool(value, config.KeepStoreByDefault);
                        break;
                }
            }
            return config;
        }
        catch (Exception)
        {
            // A hand-rolled parser has no single expected exception type the
            // way JsonException was for the old JSON reader; catch broadly so
            // a corrupted or foreign file can never brick startup (this also
            // closes the previously-acknowledged "Load() only catches
            // JsonException, not IOException" gap as a side effect).
            return new AppConfig();
        }
    }

    private static bool TryParseBool(string value, bool fallback) =>
        bool.TryParse(value.Trim(), out var parsed) ? parsed : fallback;

    public void Save(AppConfig config)
    {
        // Guards against concurrent Save() calls on the same instance racing on
        // the shared fixed temp-file path (write-write and/or a sharing
        // violation on File.Move). Serializing here is sufficient regardless of
        // which thread(s) call in from.
        lock (_saveLock)
        {
            var lines = new[]
            {
                $"{nameof(AppConfig.BinaryPath)}={config.BinaryPath}",
                $"{nameof(AppConfig.DefaultDownloadFolder)}={config.DefaultDownloadFolder}",
                $"{nameof(AppConfig.StoreFolder)}={config.StoreFolder}",
                $"{nameof(AppConfig.DefaultListenAddress)}={config.DefaultListenAddress}",
                $"{nameof(AppConfig.TrackerUrl)}={config.TrackerUrl}",
                $"{nameof(AppConfig.AlwaysUseDownloadDefaults)}={config.AlwaysUseDownloadDefaults.ToString(CultureInfo.InvariantCulture)}",
                $"{nameof(AppConfig.KeepStoreByDefault)}={config.KeepStoreByDefault.ToString(CultureInfo.InvariantCulture)}",
            };
            var tempPath = _filePath + ".tmp";
            File.WriteAllLines(tempPath, lines);
            File.Move(tempPath, _filePath, overwrite: true);
        }
    }
}
