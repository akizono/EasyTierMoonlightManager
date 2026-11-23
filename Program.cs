using EasyTierMoonlightManager.Services;

namespace EasyTierMoonlightManager;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {

        // 自检模式：不打开界面，直接跑一遍核心功能，结果写到 stdout（用于验证安装）
        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            RunSelfTest();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static void RunSelfTest()
    {
        var sb = new System.Text.StringBuilder();
        void W(string s) { sb.AppendLine(s); Console.WriteLine(s); }

        try
        {
            W("== EasyTier 管理器自检 ==");

            W("easytier-core 运行中: " + EasyTierService.IsCoreRunning() + "  PID=" + EasyTierService.CorePid());

            var peers = EasyTierService.GetPeers();
            W($"peer 数量: {peers.Count}");
            foreach (var p in peers)
                W($"  - {p.Hostname} | {p.Ipv4} | {p.Cost} | {p.LatMs}ms | {p.TunnelProto} | {p.NatType}");

            var node = EasyTierService.GetNode();
            W($"本机: {node.VirtualIp}  公网={node.PublicIp}  NAT={node.NatType}");

            var text = ConfigEditor.Read();
            W("配置网络: " + ConfigEditor.GetValue(text, "network_identity", "network_name"));
            W("配置节点: " + string.Join(" , ", ConfigEditor.GetPeers(text)));

            // 配置往返测试（内存里做，不动真文件）
            var round = ConfigEditor.SetPeers(text, new[] { "tcp://a.example:11010", "udp://b.example:11010" });
            var back = ConfigEditor.GetPeers(round);
            W("配置改写往返: " + (back.Count == 2 && back[0] == "tcp://a.example:11010") + " -> " + string.Join(" , ", back));

            W("Sunshine: " + EasyTierService.SunshineState() + "  WAN允许=" + EasyTierService.SunshineAllowsWan());

            // 用当前第一个节点做一次真实连通性测试
            var first = ConfigEditor.GetPeers(text).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
            {
                W("正在实测节点 " + first + " …");
                var r = EasyTierService.TestNodeAsync(first, CancellationToken.None).GetAwaiter().GetResult();
                W($"  测试结果: {(r.Ok ? "可用" : "失败")} 延迟={r.Latency} 详情={r.Detail}");
            }
            else W("没有配置节点，跳过实测");

            W("== 自检结束 ==");
        }
        catch (Exception ex) { W("自检异常: " + ex); }

        foreach (var target in new[]
                 {
                     Path.Combine(Paths.LogDir, "selftest.txt"),
                     Path.Combine(Path.GetTempPath(), "etm-selftest.txt")
                 })
        {
            try { File.WriteAllText(target, sb.ToString(), Paths.Utf8NoBom); W("报告已写入: " + target); }
            catch (Exception ex) { W("写文件失败 " + target + " : " + ex.Message); }
        }
    }


}
