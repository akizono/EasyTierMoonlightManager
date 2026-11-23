using System.Diagnostics;
using System.Text;

namespace EasyTierMoonlightManager.Services;

public readonly record struct ProcResult(int Code, string Out, string Err)
{
    public bool Ok => Code == 0;
    public string Text => string.IsNullOrWhiteSpace(Out) ? Err : Out;
}

public static class Proc
{
    public static ProcResult Run(string file, string args, int timeoutMs = 15000, string workDir = null)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = workDir ?? Path.GetDirectoryName(file) ?? Environment.CurrentDirectory
            };
            using var p = Process.Start(psi);
            if (p == null) return new ProcResult(-1, "", "无法启动进程: " + file);

            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();

            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { /* ignore */ }
                return new ProcResult(-2, "", $"执行超时（>{timeoutMs}ms）: {Path.GetFileName(file)} {args}");
            }
            try { p.WaitForExit(); } catch { }
            return new ProcResult(p.ExitCode, outTask.GetAwaiter().GetResult(), errTask.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            return new ProcResult(-1, "", ex.Message);
        }
    }

    public static Process StartHidden(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        return Process.Start(psi);
    }
}
