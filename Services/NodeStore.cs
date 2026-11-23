using System.Text.Encodings.Web;
using System.Text.Json;

namespace EasyTierMoonlightManager.Services;

public static class NodeStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static List<NodeEntry> Defaults() => new()
    {
        new NodeEntry { Name = "公益节点 225284（TCP）", Uri = "tcp://225284.xyz:11010", Source = "内置" },
        new NodeEntry { Name = "公益节点 225284（UDP）", Uri = "udp://225284.xyz:11010", Source = "内置" },
        new NodeEntry { Name = "风起社中继·日本（TCP）", Uri = "tcp://161.33.207.13:51010", Source = "内置" },
        new NodeEntry { Name = "风起社中继·日本（UDP）", Uri = "udp://161.33.207.13:51010", Source = "内置" },
        new NodeEntry { Name = "风起社中继·日本（QUIC）", Uri = "quic://161.33.207.13:51012", Source = "内置" },
        new NodeEntry { Name = "Slarker 美国（TCP）", Uri = "tcp://easytier-us.slarker.me:11010", Source = "内置" },
        new NodeEntry { Name = "SITMC 社区节点", Uri = "tcp://web.sitmc.club:11010", Source = "内置" },
        new NodeEntry { Name = "225284 备用线路", Uri = "tcp://38.147.105.178:11010", Source = "内置" },
        new NodeEntry { Name = "官方旧节点 .cn", Uri = "tcp://public.easytier.cn:11010", Source = "内置" },
        new NodeEntry { Name = "官方旧节点 .top", Uri = "tcp://public.easytier.top:11010", Source = "内置" },
    };

    public static List<NodeEntry> Load()
    {
        try
        {
            if (File.Exists(Paths.NodesFile))
            {
                var json = File.ReadAllText(Paths.NodesFile);
                var list = JsonSerializer.Deserialize<List<NodeEntry>>(json, JsonOpts);
                if (list is { Count: > 0 }) return list;
            }
        }
        catch { }
        var seed = Defaults();
        Save(seed);
        return seed;
    }

    public static void Save(IEnumerable<NodeEntry> nodes)
    {
        try
        {
            Paths.EnsureDirs();
            File.WriteAllText(Paths.NodesFile, JsonSerializer.Serialize(nodes.ToList(), JsonOpts), Paths.Utf8NoBom);
        }
        catch { }
    }
}
