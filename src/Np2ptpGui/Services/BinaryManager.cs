namespace Np2ptpGui.Services;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed class BinaryManager
{
    private const string AssetName = "np2ptp-windows-x86_64.exe";
    private readonly GitHubReleaseClient _client;
    private readonly string _binsDirectory;

    public BinaryManager(GitHubReleaseClient client, string binsDirectory)
    {
        _client = client;
        _binsDirectory = binsDirectory;
        Directory.CreateDirectory(_binsDirectory);
    }

    public string ExePath => Path.Combine(_binsDirectory, "np2ptp.exe");
    private string VersionFilePath => Path.Combine(_binsDirectory, "version.txt");
    public bool IsBinaryPresent => File.Exists(ExePath);

    public async Task EnsureBinaryAsync(CancellationToken ct)
    {
        if (IsBinaryPresent) return;
        var release = await _client.GetLatestReleaseAsync(ct);
        await DownloadReleaseAsync(release, ct);
    }

    public async Task<bool> CheckForUpdateAsync(CancellationToken ct)
    {
        var release = await _client.GetLatestReleaseAsync(ct);
        var currentTag = File.Exists(VersionFilePath) ? File.ReadAllText(VersionFilePath).Trim() : null;
        if (release.TagName == currentTag) return false;
        await DownloadReleaseAsync(release, ct);
        return true;
    }

    public async Task<bool> TryCheckForUpdateSilentlyAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            return await CheckForUpdateAsync(cts.Token);
        }
        catch
        {
            // No internet, GitHub unreachable, or timed out — keep using the
            // binary already on disk silently. A truly missing binary is
            // handled (and surfaced) separately by EnsureBinaryAsync.
            return false;
        }
    }

    private async Task DownloadReleaseAsync(GitHubRelease release, CancellationToken ct)
    {
        var asset = release.Assets.FirstOrDefault(a => a.Name == AssetName)
            ?? throw new InvalidOperationException($"release {release.TagName} has no asset named {AssetName}");
        var bytes = await _client.DownloadAssetAsync(asset.BrowserDownloadUrl, ct);
        await File.WriteAllBytesAsync(ExePath, bytes, ct);
        await File.WriteAllTextAsync(VersionFilePath, release.TagName, ct);
    }
}
