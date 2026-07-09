namespace Np2ptpGui.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed class BinaryManager
{
    private const string AssetName = "np2ptp-windows-x86_64.exe";

    // The only certificate np2ptp's own release pipeline signs with. Any
    // downloaded exe not signed by exactly this cert is refused - protects
    // against a compromised/tampered release asset being silently trusted
    // and later executed (np2ptp is meant to be the one official binary).
    private const string ExpectedSignerThumbprint = "36477BB5DCB10D2C0381A2D79533F0386C5CCACA";

    private readonly GitHubReleaseClient _client;
    private readonly string _binsDirectory;
    private readonly Func<string, string?> _productVersionReader;
    private readonly Func<string, string?> _signatureVerifier;

    public BinaryManager(GitHubReleaseClient client, string binsDirectory)
        : this(client, binsDirectory, ReadProductVersion, AuthenticodeVerifier.GetVerifiedSignerThumbprint)
    {
    }

    internal BinaryManager(
        GitHubReleaseClient client,
        string binsDirectory,
        Func<string, string?> productVersionReader,
        Func<string, string?> signatureVerifier)
    {
        _client = client;
        _binsDirectory = binsDirectory;
        _productVersionReader = productVersionReader;
        _signatureVerifier = signatureVerifier;
        Directory.CreateDirectory(_binsDirectory);
    }

    public string ExePath => Path.Combine(_binsDirectory, "np2ptp.exe");
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
        var currentVersion = IsBinaryPresent ? _productVersionReader(ExePath) : null;
        var latestVersion = release.TagName.TrimStart('v', 'V');
        if (latestVersion == currentVersion) return false;
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

        var signerThumbprint = _signatureVerifier(ExePath);
        if (!string.Equals(signerThumbprint, ExpectedSignerThumbprint, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(ExePath);
            throw new InvalidOperationException(
                $"release {release.TagName}'s {AssetName} is not signed by the expected certificate " +
                $"(thumbprint {ExpectedSignerThumbprint}) - refusing to keep it. The release may have " +
                "been tampered with, or the signing certificate changed unexpectedly.");
        }
    }

    // The np2ptp release build embeds its tag (minus the leading "v") as the
    // exe's Product Version file property - read that directly instead of a
    // separate sidecar file, so the recorded version can never drift from
    // whatever binary is actually sitting on disk (e.g. if it gets replaced
    // by hand). GetVersionInfo doesn't throw for a missing/malformed version
    // resource, but guard anyway - a bad read must fall back to "unknown"
    // (null), which CheckForUpdateAsync treats as needing an update.
    private static string? ReadProductVersion(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path).ProductVersion;
        }
        catch
        {
            return null;
        }
    }
}
