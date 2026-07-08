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
    public async Task EnsureBinaryAsync_WhenMissing_DownloadsExeAndWritesVersionFile()
    {
        var dir = NewTempDir();
        var client = new GitHubReleaseClient(ClientReturning(ReleaseJsonV1, new byte[] { 1, 2, 3 }));
        var manager = new BinaryManager(client, dir);

        await manager.EnsureBinaryAsync(default);

        Assert.True(File.Exists(manager.ExePath));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(manager.ExePath));
        Assert.Equal("v1.0.0", (await File.ReadAllTextAsync(Path.Combine(dir, "version.txt"))).Trim());
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
    public async Task CheckForUpdateAsync_WhenTagUnchanged_ReturnsFalseAndDoesNotRewriteBinary()
    {
        var dir = NewTempDir();
        var client = new GitHubReleaseClient(ClientReturning(ReleaseJsonV1, new byte[] { 1 }));
        var manager = new BinaryManager(client, dir);
        await manager.EnsureBinaryAsync(default);

        var updated = await manager.CheckForUpdateAsync(default);

        Assert.False(updated);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenTagChanged_DownloadsNewBinaryAndReturnsTrue()
    {
        var dir = NewTempDir();
        var v1Client = new GitHubReleaseClient(ClientReturning(ReleaseJsonV1, new byte[] { 1 }));
        var manager = new BinaryManager(v1Client, dir);
        await manager.EnsureBinaryAsync(default);

        var v2Client = new GitHubReleaseClient(ClientReturning(ReleaseJsonV2, new byte[] { 2, 2 }));
        var managerWithV2Client = new BinaryManager(v2Client, dir);

        var updated = await managerWithV2Client.CheckForUpdateAsync(default);

        Assert.True(updated);
        Assert.Equal(new byte[] { 2, 2 }, await File.ReadAllBytesAsync(manager.ExePath));
        Assert.Equal("v2.0.0", (await File.ReadAllTextAsync(Path.Combine(dir, "version.txt"))).Trim());
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
