namespace Np2ptpGui.Tests.Services;

using System;
using System.IO;
using Np2ptpGui.Models;
using Np2ptpGui.Services;
using Xunit;

public class ConfigStoreTests
{
    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), "np2ptp-gui-tests-" + Guid.NewGuid());

    [Fact]
    public void Load_WhenNoFileExists_ReturnsDefaults()
    {
        var dir = NewTempDir();
        var store = new ConfigStore(dir);

        var config = store.Load();

        Assert.Equal("", config.BinaryPath);
        Assert.Equal("/ip4/0.0.0.0/udp/0/quic-v1", config.DefaultListenAddress);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var dir = NewTempDir();
        var store = new ConfigStore(dir);
        var original = new AppConfig
        {
            BinaryPath = @"C:\bins\np2ptp.exe",
            DefaultDownloadFolder = @"C:\Downloads",
            StoreFolder = @"C:\Store",
            DefaultListenAddress = "/ip4/0.0.0.0/udp/4001/quic-v1",
            TrackerUrl = "https://np2ptp.vercel.app",
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original.BinaryPath, loaded.BinaryPath);
        Assert.Equal(original.DefaultDownloadFolder, loaded.DefaultDownloadFolder);
        Assert.Equal(original.StoreFolder, loaded.StoreFolder);
        Assert.Equal(original.DefaultListenAddress, loaded.DefaultListenAddress);
        Assert.Equal(original.TrackerUrl, loaded.TrackerUrl);
        Directory.Delete(dir, recursive: true);
    }
}
