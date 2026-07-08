namespace Np2ptpGui.Tests.Services;

using System;
using System.IO;
using System.Linq;
using Np2ptpGui.Models;
using Np2ptpGui.Services;
using Xunit;

public class HistoryStoreTests
{
    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), "np2ptp-gui-tests-" + Guid.NewGuid());

    [Fact]
    public void Load_WhenNoFileExists_ReturnsEmptyList()
    {
        var dir = NewTempDir();
        var store = new HistoryStore(dir);

        var entries = store.Load();

        Assert.Empty(entries);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEntries()
    {
        var dir = NewTempDir();
        var store = new HistoryStore(dir);
        var entry = new TaskHistoryEntry
        {
            Id = "abc123",
            Type = OperationType.Fetch,
            InputOrLink = "np2ptp:deadbeef",
            OutputPath = @"C:\downloads\thing",
            Status = OperationStatus.Completed,
            StartedAt = new DateTime(2026, 7, 8, 10, 0, 0, DateTimeKind.Utc),
            FinishedAt = new DateTime(2026, 7, 8, 10, 5, 0, DateTimeKind.Utc),
        };

        store.Save(new[] { entry });
        var loaded = store.Load();

        Assert.Single(loaded);
        Assert.Equal(entry.Id, loaded[0].Id);
        Assert.Equal(entry.Type, loaded[0].Type);
        Assert.Equal(entry.InputOrLink, loaded[0].InputOrLink);
        Assert.Equal(entry.OutputPath, loaded[0].OutputPath);
        Assert.Equal(entry.Status, loaded[0].Status);
        Assert.Equal(entry.StartedAt, loaded[0].StartedAt);
        Assert.Equal(entry.FinishedAt, loaded[0].FinishedAt);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Load_WhenFileIsCorruptedJson_ReturnsEmptyListInsteadOfThrowing()
    {
        var dir = NewTempDir();
        var store = new HistoryStore(dir);
        var filePath = Path.Combine(dir, "history.json");
        File.WriteAllText(filePath, "{ this is not valid json ][");

        var entries = store.Load();

        Assert.Empty(entries);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Save_DoesNotLeaveTempFileBehind()
    {
        var dir = NewTempDir();
        var store = new HistoryStore(dir);

        store.Save(new[]
        {
            new TaskHistoryEntry { Id = "1", Type = OperationType.Pack, InputOrLink = "a", Status = OperationStatus.Completed },
        });

        Assert.True(File.Exists(Path.Combine(dir, "history.json")));
        Assert.False(File.Exists(Path.Combine(dir, "history.json.tmp")));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void MarkRunningAsInterrupted_OnlyChangesRunningEntries()
    {
        var dir = NewTempDir();
        var store = new HistoryStore(dir);
        store.Save(new[]
        {
            new TaskHistoryEntry { Id = "1", Type = OperationType.Serve, InputOrLink = "a.nptp", Status = OperationStatus.Running },
            new TaskHistoryEntry { Id = "2", Type = OperationType.Pack, InputOrLink = "b", Status = OperationStatus.Completed },
        });

        store.MarkRunningAsInterrupted();
        var loaded = store.Load();

        Assert.Equal(OperationStatus.Interrupted, loaded.Single(e => e.Id == "1").Status);
        Assert.Equal(OperationStatus.Completed, loaded.Single(e => e.Id == "2").Status);
        Directory.Delete(dir, recursive: true);
    }
}
