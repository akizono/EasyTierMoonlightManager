using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EasyTierMoonlightManager.Services;

public static class EasyTierService
{
    // ---------------------------------------------------------------- 进程 / 任务
    // 注意：EasyTier 以 SYSTEM 身份运行，普通用户枚举进程看不到它，所以用 tasklist + RPC 双重判断
    public static bool IsCoreRunning()
    {
        if (CorePid() > 0) return true;
        return Proc.Run(Paths.CliExe, "peer", 6000).Ok;
    }

    public static int CorePid()
    {
        var r = Proc.Run("tasklist.exe", "/FI \"IMAGENAME eq easytier-core.exe\" /NH /FO CSV", 8000);
        if (r.Ok)
        {
            var m = Regex.Match(r.Out, "\"easytier-core\\.exe\",\"(\\d+)\"");
            if (m.Success) return int.Parse(m.Groups[1].Value);
        }
        return 0;
    }

    /// <summary>直接以当前（管理员）身份拉起 easytier-core，不依赖任何计划任务/服务。</summary>
    public static string StartCore()
    {
        if (IsCoreRunning()) return "";
        try
        {
            var psi = new ProcessStartInfo(Paths.CoreExe,
                $"-c \"{Paths.ConfigFile}\" --file-log-dir \"{Paths.LogDir}\" --file-log-level info --console-log-level warn")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Paths.Root
            };
            Process.Start(psi);
            Thread.Sleep(2500);
            return IsCoreRunning() ? "" : "easytier-core 没有启动成功（请确认本程序是以管理员身份运行的）";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>结束所有 easytier-core 进程（含残留实例）。</summary>
    public static string StopCore()
    {
        foreach (var p in Process.GetProcessesByName("easytier-core"))
        {
            try { p.Kill(true); } catch { }
        }
        for (int i = 0; i < 12 && IsCoreRunning(); i++) Thread.Sleep(300);
        return "";
    }

    public static string RestartCore()
    {
        StopCore();
        Thread.Sleep(1200);
        return StartCore();
    }

    // ---------------------------------------------------------------- 状态查询
    public static List<PeerInfo> GetPeers()
    {
        var list = new List<PeerInfo>();
        var r = Proc.Run(Paths.CliExe, "-o json peer", 12000);
        if (!r.Ok || string.IsNullOrWhiteSpace(r.Out)) return list;

        try
        {
            using var doc = JsonDocument.Parse(r.Out);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                list.Add(new PeerInfo
                {
                    Cidr = Str(e, "cidr"),
                    Ipv4 = Str(e, "ipv4"),
                    Hostname = Str(e, "hostname"),
                    Cost = Str(e, "cost"),
                    LatMs = Str(e, "lat_ms"),
                    LossRate = Str(e, "loss_rate"),
                    RxBytes = Str(e, "rx_bytes"),
                    TxBytes = Str(e, "tx_bytes"),
                    TunnelProto = Str(e, "tunnel_proto"),
                    NatType = Str(e, "nat_type"),
                    Version = Str(e, "version")
                });
            }
        }
        catch { /* 解析失败按空处理 */ }
        return list;
    }

    public static NodeSnapshot GetNode()
    {
        var snap = new NodeSnapshot();
        var r = Proc.Run(Paths.CliExe, "node", 12000);
        if (!r.Ok) return snap;

        foreach (var raw in r.Out.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('|')) continue;
            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length < 4) continue;
            var key = cells[1];
            var val = cells[2];
            switch (key)
            {
                case "Virtual IP": snap.VirtualIp = val; break;
                case "Hostname": snap.Hostname = val; break;
                case "Public IPv4": snap.PublicIp = val; break;
                case "UDP Stun Type": snap.NatType = val; break;
                case "Listener 1":
                case "Listener 2":
                case "Listener 3":
                case "Listener 4":
                    if (!string.IsNullOrWhiteSpace(val)) snap.Listeners.Add(val);
                    break;
            }
        }
        return snap;
    }

    // ---------------------------------------------------------------- 节点连通性测试
    public static async Task<NodeTestResult> TestNodeAsync(string uri, CancellationToken ct)
    {
        var text = ConfigEditor.Read();
        var net = ConfigEditor.GetValue(text, "network_identity", "network_name");
        var secret = ConfigEditor.GetValue(text, "network_identity", "network_secret");
        if (string.IsNullOrWhiteSpace(net)) return new NodeTestResult(false, "", "本地配置里没有网络名称");

        int port = FindFreeRpcPort(15891);
        string args =
            $"--no-tun --no-listener --network-name \"{net}\" --network-secret \"{secret}\" " +
            $"--ipv4 10.126.126.250 --rpc-portal 127.0.0.1:{port} --instance-name nodetest " +
            $"--console-log-level error -p \"{uri}\"";

        Process proc = null;
        try
        {
            proc = Proc.StartHidden(Paths.CoreExe, args);
            if (proc == null) return new NodeTestResult(false, "", "无法启动 easytier-core");

            for (int i = 0; i < 16; i++)
            {
                if (ct.IsCancellationRequested) return new NodeTestResult(false, "", "已取消");
                await Task.Delay(750, ct);

                var r = Proc.Run(Paths.CliExe, $"-p 127.0.0.1:{port} -o json peer", 6000);
                if (!r.Ok) continue;

                try
                {
                    using var doc = JsonDocument.Parse(r.Out);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                    foreach (var e in doc.RootElement.EnumerateArray())
                    {
                        var host = Str(e, "hostname");
                        var cost = Str(e, "cost");
                        if (string.IsNullOrWhiteSpace(host)) continue;
                        if (string.Equals(cost, "Local", StringComparison.OrdinalIgnoreCase)) continue;
                        if (host.StartsWith("DESKTOP-", StringComparison.OrdinalIgnoreCase) &&
                            host.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                            continue;
                        return new NodeTestResult(true, Str(e, "lat_ms"), host);
                    }
                }
                catch { }
            }
            return new NodeTestResult(false, "", "16 秒内没能握手成功");
        }
        catch (OperationCanceledException) { return new NodeTestResult(false, "", "已取消"); }
        catch (Exception ex) { return new NodeTestResult(false, "", ex.Message); }
        finally
        {
            try { proc?.Kill(true); } catch { }
            try { proc?.Dispose(); } catch { }
        }
    }

    private static int FindFreeRpcPort(int start)
    {
        for (int p = start; p < start + 30; p++)
        {
            try
            {
                var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, p);
                l.Start();
                l.Stop();
                return p;
            }
            catch { }
        }
        return start;
    }

    // ---------------------------------------------------------------- Sunshine
    public static string SunshineState()
    {
        var r = Proc.Run("sc.exe", "query " + Paths.SunshineService, 10000);
        if (!r.Ok) return "未知";
        if (r.Out.Contains("RUNNING")) return "运行中";
        if (r.Out.Contains("STOPPED")) return "已停止";
        return "未知";
    }

    public static bool PortListening(int port)
    {
        try
        {
            using var c = new System.Net.Sockets.TcpClient();
            var t = c.ConnectAsync("127.0.0.1", port);
            return t.Wait(600);
        }
        catch { return false; }
    }

    public static bool SunshineAllowsWan()
    {
        try
        {
            if (!File.Exists(Paths.SunshineConf)) return false;
            var txt = File.ReadAllText(Paths.SunshineConf);
            var m = Regex.Match(txt, @"origin_web_ui_allowed\s*=\s*(\w+)");
            return !m.Success || m.Groups[1].Value.Equals("wan", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static string SetSunshineWan(bool wan)
    {
        try
        {
            if (!File.Exists(Paths.SunshineConf)) return "找不到 Sunshine 配置文件";
            var lines = File.ReadAllText(Paths.SunshineConf).Replace("\r\n", "\n").Split('\n').ToList();
            lines = lines.Where(l => !Regex.IsMatch(l, @"^\s*origin_web_ui_allowed\s*=")).ToList();
            while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            lines.Add("origin_web_ui_allowed = " + (wan ? "wan" : "lan"));

            var bak = Paths.SunshineConf + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(Paths.SunshineConf, bak, true);
            File.WriteAllText(Paths.SunshineConf, string.Join(Environment.NewLine, lines) + Environment.NewLine, Paths.Utf8NoBom);

            Proc.Run("sc.exe", "stop " + Paths.SunshineService, 30000);
            Thread.Sleep(2500);
            Proc.Run("sc.exe", "start " + Paths.SunshineService, 30000);
            Thread.Sleep(4000);
            return "";
        }
        catch (Exception ex) { return ex.Message; }
    }

    // ---------------------------------------------------------------- 日志
    public static string TailLog(int lines)
    {
        try
        {
            if (!File.Exists(Paths.MainLog)) return "(暂无日志)";
            using var fs = new FileStream(Paths.MainLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var all = sr.ReadToEnd().Replace("\r", "").Split('\n');
            var take = all.Skip(Math.Max(0, all.Length - lines));
            return string.Join(Environment.NewLine, take);
        }
        catch (Exception ex) { return "读取日志失败: " + ex.Message; }
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
