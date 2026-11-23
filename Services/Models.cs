namespace EasyTierMoonlightManager.Services;

public sealed class PeerInfo
{
    public string Cidr { get; set; } = "";
    public string Ipv4 { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string Cost { get; set; } = "";
    public string LatMs { get; set; } = "";
    public string LossRate { get; set; } = "";
    public string RxBytes { get; set; } = "";
    public string TxBytes { get; set; } = "";
    public string TunnelProto { get; set; } = "";
    public string NatType { get; set; } = "";
    public string Version { get; set; } = "";

    public bool IsSelf => string.Equals(Cost, "Local", StringComparison.OrdinalIgnoreCase);
}

public sealed class NodeSnapshot
{
    public string VirtualIp { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string PublicIp { get; set; } = "";
    public string NatType { get; set; } = "";
    public List<string> Listeners { get; set; } = new();
}

public sealed class NodeEntry
{
    public string Name { get; set; } = "";
    public string Uri { get; set; } = "";
    public string Source { get; set; } = "内置";
    public string LastResult { get; set; } = "";     // "" / OK / FAIL
    public string LastLatency { get; set; } = "";    // "183.4"
    public string LastTestedAt { get; set; } = "";   // "10:23:41"

    public string ResultText => LastResult switch
    {
        "OK" => "可用",
        "FAIL" => "连不上",
        _ => "未测试"
    };
}

public readonly record struct NodeTestResult(bool Ok, string Latency, string Detail);
