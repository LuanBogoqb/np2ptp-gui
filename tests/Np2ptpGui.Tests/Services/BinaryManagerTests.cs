namespace Np2ptpGui.Tests.Services;

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Np2ptpGui.Services;
using Xunit;

public class BinaryManagerTests
{
    // Must match BinaryManager's own ExpectedSignerThumbprint constant - kept
    // as a separate literal here rather than exposed internally, since tests
    // should fail loudly if the two ever drift apart instead of silently
    // reading the same value from both sides.
    private const string ExpectedSignerThumbprint = "36477BB5DCB10D2C0381A2D79533F0386C5CCACA";

    private const string ReleaseJsonV1 = """
        {"tag_name":"v1.0.0","assets":[{"name":"np2ptp-windows-x86_64.exe","browser_download_url":"https://example.test/v1/np2ptp.exe"}]}
        """;

    private const string ReleaseJsonV2 = """
        {"tag_name":"v2.0.0","assets":[{"name":"np2ptp-windows-x86_64.exe","browser_download_url":"https://example.test/v2/np2ptp.exe"}]}
        """;

    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), "np2ptp-gui-tests-" + Guid.NewGuid());

    private static HttpClient ClientReturning(string releaseJson, byte[] assetBytes) =>
        new(new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.ToString().Contains("releases/latest"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releaseJson, Encoding.UTF8, "application/json"),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(assetBytes) };
        }));

    [Fact]
    public async Task EnsureBinaryAsync_WhenMissing_DownloadsExe()
    {
        var dir = NewTempDir();
        var client = new GitHubReleaseClient(ClientReturning(ReleaseJsonV1, new byte[] { 1, 2, 3 }));
        var manager = new BinaryManager(client, dir, _ => "1.0.0", _ => ExpectedSignerThumbprint);

        await manager.EnsureBinaryAsync(default);

        Assert.True(File.Exists(manager.ExePath));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(manager.ExePath));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task EnsureBinaryAsync_WhenAlreadyPresent_MakesNoHttpCall()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "np2ptp.exe"), new byte[] { 9 });

        var throwingClient = new GitHubReleaseClient(new HttpClient(new FakeHttpMessageHandler(
            _ => throw new InvalidOperationException("should not call HTTP when binary already exists"))));
        var manager = new BinaryManager(throwingClient, dir);

        await manager.EnsureBinaryAsync(default);

        Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(manager.ExePath));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task EnsureBinaryAsync_WhenDownloadedBytesAreUnsigned_DeletesFileAndThrows()
    {
        // End-to-end against the real AuthenticodeVerifier (public constructor,
        // no fake signature verifier) - plain bytes have no Authenticode
        // signature at all, so WinVerifyTrust must reject them.
        var dir = NewTempDir();
        var client = new GitHubReleaseClient(ClientReturning(ReleaseJsonV1, new byte[] { 1, 2, 3 }));
        var manager = new BinaryManager(client, dir);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.EnsureBinaryAsync(default));

        Assert.False(File.Exists(manager.ExePath));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenProductVersionMatchesTagMinusVPrefix_ReturnsFalseAndDoesNotRewriteBinary()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "np2ptp.exe"), new byte[] { 1 });
        var client = new GitHubReleaseClient(ClientReturning(ReleaseJsonV1, new byte[] { 9, 9 }));
        // Simulates FileVersionInfo.ProductVersion on the installed exe reading "1.0.0" -
        // the GitHub tag is "v1.0.0" - CheckForUpdateAsync must strip the "v" to match.
        // No download happens on this path, so the signature verifier is never called -
        // asserted below by making it throw if it ever is.
        var manager = new BinaryManager(client, dir, _ => "1.0.0", _ => throw new InvalidOperationException("must not verify signature when no download happens"));

        var updated = await manager.CheckForUpdateAsync(default);

        Assert.False(updated);
        Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(manager.ExePath));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenProductVersionDiffersFromTag_DownloadsNewBinaryAndReturnsTrue()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "np2ptp.exe"), new byte[] { 1 });
        var client = new GitHubReleaseClient(ClientReturning(ReleaseJsonV2, new byte[] { 2, 2 }));
        var manager = new BinaryManager(client, dir, _ => "1.0.0", _ => ExpectedSignerThumbprint);

        var updated = await manager.CheckForUpdateAsync(default);

        Assert.True(updated);
        Assert.Equal(new byte[] { 2, 2 }, await File.ReadAllBytesAsync(manager.ExePath));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenNoBinaryPresentYet_TreatsAsNeedingUpdate()
    {
        var dir = NewTempDir();
        var client = new GitHubReleaseClient(ClientReturning(ReleaseJsonV1, new byte[] { 1 }));
        var readerCalled = false;
        var manager = new BinaryManager(client, dir, _ => { readerCalled = true; return "1.0.0"; }, _ => ExpectedSignerThumbprint);

        var updated = await manager.CheckForUpdateAsync(default);

        Assert.True(updated);
        Assert.False(readerCalled); // no exe on disk yet - must not even attempt to read its version
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenProductVersionUnreadable_TreatsAsNeedingUpdate()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "np2ptp.exe"), new byte[] { 1 });
        var client = new GitHubReleaseClient(ClientReturning(ReleaseJsonV1, new byte[] { 9 }));
        // Simulates FileVersionInfo throwing/returning null for a malformed or
        // version-resource-less exe - must fall back to "needs update", not crash.
        var manager = new BinaryManager(client, dir, _ => null, _ => ExpectedSignerThumbprint);

        var updated = await manager.CheckForUpdateAsync(default);

        Assert.True(updated);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenDownloadedBinaryHasWrongSignerThumbprint_DeletesFileAndThrows()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "np2ptp.exe"), new byte[] { 1 });
        var client = new GitHubReleaseClient(ClientReturning(ReleaseJsonV2, new byte[] { 6, 6, 6 }));
        var manager = new BinaryManager(client, dir, _ => "1.0.0", _ => "SOME-OTHER-CERTIFICATES-THUMBPRINT");

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CheckForUpdateAsync(default));

        Assert.False(File.Exists(manager.ExePath));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenSkipSignatureVerificationIsTrue_KeepsBinaryEvenIfUnsigned()
    {
        // --no-check-cert escape hatch for the transitional period before
        // np2ptp's own release pipeline actually signs with the expected cert.
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "np2ptp.exe"), new byte[] { 1 });
        var client = new GitHubReleaseClient(ClientReturning(ReleaseJsonV2, new byte[] { 6, 6, 6 }));
        var manager = new BinaryManager(client, dir, _ => "1.0.0", _ => null, skipSignatureVerification: true);

        var updated = await manager.CheckForUpdateAsync(default);

        Assert.True(updated);
        Assert.Equal(new byte[] { 6, 6, 6 }, await File.ReadAllBytesAsync(manager.ExePath));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenDownloadedBinaryIsUnsigned_DeletesFileAndThrows()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "np2ptp.exe"), new byte[] { 1 });
        var client = new GitHubReleaseClient(ClientReturning(ReleaseJsonV2, new byte[] { 6, 6, 6 }));
        var manager = new BinaryManager(client, dir, _ => "1.0.0", _ => null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CheckForUpdateAsync(default));

        Assert.False(File.Exists(manager.ExePath));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task TryCheckForUpdateSilentlyAsync_WhenHttpFails_ReturnsFalseWithoutThrowing()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "np2ptp.exe"), new byte[] { 9 });
        var failingClient = new GitHubReleaseClient(new HttpClient(new FakeHttpMessageHandler(
            _ => throw new HttpRequestException("simulated: no internet"))));
        var manager = new BinaryManager(failingClient, dir);

        var updated = await manager.TryCheckForUpdateSilentlyAsync(TimeSpan.FromSeconds(1));

        Assert.False(updated);
        Directory.Delete(dir, recursive: true);
    }
}
