namespace Np2ptpGui.Models;

public sealed class AppConfig
{
    public string BinaryPath { get; set; } = "";
    public string DefaultDownloadFolder { get; set; } = "";
    public string StoreFolder { get; set; } = "";
    public string DefaultListenAddress { get; set; } = "/ip4/0.0.0.0/udp/0/quic-v1";
    public string TrackerUrl { get; set; } = "https://nptp.bogotec.uk";
    public bool AlwaysUseDownloadDefaults { get; set; } = false;
    public bool KeepStoreByDefault { get; set; } = true;
    public bool AutoSeedOnDownloadComplete { get; set; } = true;
    public bool AutoSeedAfterSharing { get; set; } = true;
    public string ThemeFamily { get; set; } = "Modern";
}
