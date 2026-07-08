namespace Np2ptpGui.Models;

public sealed class AppConfig
{
    public string BinaryPath { get; set; } = "";
    public string DefaultDownloadFolder { get; set; } = "";
    public string StoreFolder { get; set; } = "";
    public string DefaultListenAddress { get; set; } = "/ip4/0.0.0.0/udp/0/quic-v1";
    public string TrackerUrl { get; set; } = "";
}
