using System.Text;
using System.Text.Json;

namespace EasyTierMoonlightManager.Services;

/// <summary>
/// 运行时路径。所有默认值都可以用 <c>%ProgramData%\EasyTier\settings.json</c> 覆盖，
/// 便于 EasyTier / Sunshine 安装在非默认目录的机器使用。
/// </summary>
public static class Paths
{
    public static string Root { get; private set; }
    public static string ConfigFile { get; private set; }
    public static string NodesFile { get; private set; }
    public static string SettingsFile { get; private set; }
    public static string LogDir { get; private set; }
    public static string MainLog { get; private set; }

    public static string CoreExe { get; private set; }
    public static string CliExe { get; private set; }

    public static string SunshineService { get; private set; }
    public static string SunshineConf { get; private set; }

    public static readonly UTF8Encoding Utf8NoBom = new(false);

    static Paths()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData)) programData = @"C:\ProgramData";

        Root = Path.Combine(programData, "EasyTier");
        ConfigFile = Path.Combine(Root, "config.toml");
        NodesFile = Path.Combine(Root, "nodes.json");
        SettingsFile = Path.Combine(Root, "settings.json");
        LogDir = Path.Combine(Root, "logs");
        MainLog = Path.Combine(LogDir, "easytier.log");
        SunshineService = "SunshineService";

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        SunshineConf = Path.Combine(programFiles, "Sunshine", "config", "sunshine.conf");

        CoreExe = LocateCore();
        CliExe = string.IsNullOrEmpty(CoreExe) ? "" : Path.Combine(Path.GetDirectoryName(CoreExe)!, "easytier-cli.exe");

        ApplyOverrides();
    }

    /// <summary>在常见位置寻找 easytier-core.exe（可用 settings.json / 环境变量覆盖）。</summary>
    private static string LocateCore()
    {
        var candidates = new List<string>();

        var env = Environment.GetEnvironmentVariable("EASYTIER_HOME");
        if (!string.IsNullOrWhiteSpace(env)) candidates.Add(Path.Combine(env, "easytier-core.exe"));

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "easytier-core.exe"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "easytier", "easytier-core.exe"));
        candidates.Add(Path.Combine(Root, "bin", "easytier-core.exe"));

        // 解压即用版常见落点
        try
        {
            foreach (var dir in Directory.GetDirectories(@"C:\Software", "easytier-windows-*", SearchOption.AllDirectories))
            {
                var exe = Path.Combine(dir, "easytier-core.exe");
                if (File.Exists(exe)) { candidates.Add(exe); break; }
            }
        }
        catch { /* 目录不存在或不可访问 */ }

        try
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            candidates.Add(Path.Combine(pf, "EasyTier", "easytier-core.exe"));
            candidates.Add(Path.Combine(pf, "EasyTier", "easytier-windows-x86_64", "easytier-core.exe"));
        }
        catch { }

        return candidates.FirstOrDefault(File.Exists) ?? "";
    }

    private static void ApplyOverrides()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsFile, Encoding.UTF8));
            var r = doc.RootElement;

            var core = Get(r, "coreExe");
            if (!string.IsNullOrWhiteSpace(core) && File.Exists(core))
            {
                CoreExe = core;
                CliExe = Path.Combine(Path.GetDirectoryName(core)!, "easytier-cli.exe");
            }

            var cli = Get(r, "cliExe");
            if (!string.IsNullOrWhiteSpace(cli) && File.Exists(cli)) CliExe = cli;

            var cfg = Get(r, "configFile");
            if (!string.IsNullOrWhiteSpace(cfg))
            {
                ConfigFile = cfg;
                Root = Path.GetDirectoryName(cfg) ?? Root;
                NodesFile = Path.Combine(Root, "nodes.json");
                SettingsFile = Path.Combine(Root, "settings.json");
                LogDir = Path.Combine(Root, "logs");
                MainLog = Path.Combine(LogDir, "easytier.log");
            }

            var sun = Get(r, "sunshineConf");
            if (!string.IsNullOrWhiteSpace(sun)) SunshineConf = sun;

            var svc = Get(r, "sunshineService");
            if (!string.IsNullOrWhiteSpace(svc)) SunshineService = svc;
        }
        catch { /* 配置损坏时退回默认值 */ }
    }

    private static string Get(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    public static bool HasCore => !string.IsNullOrEmpty(CoreExe) && File.Exists(CoreExe);

    public static void EnsureDirs()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogDir);
    }
}
