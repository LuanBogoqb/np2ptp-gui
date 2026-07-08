namespace Np2ptpGui.Tests.Services;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    public void Load_WhenFileIsCorruptedJson_ReturnsDefaultsInsteadOfThrowing()
    {
        var dir = NewTempDir();
        var store = new ConfigStore(dir);
        var filePath = Path.Combine(dir, "config.json");
        File.WriteAllText(filePath, "{ not valid json");

        var config = store.Load();

        Assert.Equal("", config.BinaryPath);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Save_DoesNotLeaveTempFileBehind()
    {
        var dir = NewTempDir();
        var store = new ConfigStore(dir);

        store.Save(new AppConfig { BinaryPath = @"C:\bins\np2ptp.exe" });

        Assert.True(File.Exists(Path.Combine(dir, "config.json")));
        Assert.False(File.Exists(Path.Combine(dir, "config.json.tmp")));
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

    [Fact]
    public async Task Save_CalledConcurrentlyFromManyThreads_DoesNotThrow()
    {
        // Regression test for the fixed-temp-file race: two threads calling
        // Save() on the same instance at nearly the same time used to be able
        // to collide on "<file>.tmp" and blow up File.Move with an
        // UnauthorizedAccessException. The instance-level lock in Save() must
        // serialize these calls instead.
        var dir = NewTempDir();
        var store = new ConfigStore(dir);

        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            store.Save(new AppConfig { BinaryPath = $@"C:\bins\np2ptp{i}.exe" });
        }));

        await Task.WhenAll(tasks);

        Assert.True(File.Exists(Path.Combine(dir, "config.json")));
        Assert.False(File.Exists(Path.Combine(dir, "config.json.tmp")));
        Directory.Delete(dir, recursive: true);
    }
}
