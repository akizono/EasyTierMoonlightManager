using System.Text;
using System.Text.RegularExpressions;

namespace EasyTierMoonlightManager.Services;

/// <summary>对 config.toml 做最小化、保结构的读写（不引入 TOML 依赖）。</summary>
public static class ConfigEditor
{
    public static string Read()
        => File.Exists(Paths.ConfigFile) ? File.ReadAllText(Paths.ConfigFile, Encoding.UTF8) : "";

    public static void Write(string text)
    {
        Paths.EnsureDirs();
        File.WriteAllText(Paths.ConfigFile, text, Paths.Utf8NoBom);
    }

    // ---------------------------------------------------------------- peers
    public static List<string> GetPeers(string text)
    {
        var list = new List<string>();
        foreach (var line in SplitLines(text))
        {
            var m = Regex.Match(line, @"^\s*uri\s*=\s*""([^""]*)""");
            if (m.Success)
            {
                var v = m.Groups[1].Value.Trim();
                if (v.Length > 0) list.Add(v);
            }
        }
        return list;
    }

    public static string SetPeers(string text, IEnumerable<string> uris)
    {
        var keep = new List<string>();
        var lines = SplitLines(text);
        int i = 0;
        while (i < lines.Count)
        {
            if (Regex.IsMatch(lines[i], @"^\s*\[\[peer\]\]"))
            {
                i++;
                while (i < lines.Count && !Regex.IsMatch(lines[i], @"^\s*\[")) i++;
                continue;
            }
            if (Regex.IsMatch(lines[i], @"^\s*#.*公共共享节点")) { i++; continue; }
            keep.Add(lines[i]);
            i++;
        }

        var block = new List<string> { "", "# 公共共享节点（可写多条，互为备份；打洞失败时由它们中转）" };
        foreach (var u in uris)
        {
            block.Add("[[peer]]");
            block.Add("uri = \"" + u.Trim() + "\"");
        }
        block.Add("");

        int idx = keep.FindIndex(l => Regex.IsMatch(l, @"^\s*\[flags\]"));
        if (idx >= 0) keep.InsertRange(idx, block);
        else keep.AddRange(block);

        return string.Join(Environment.NewLine, TrimTrailing(keep)) + Environment.NewLine;
    }

    // ---------------------------------------------------------------- simple key/value
    public static string GetValue(string text, string section, string key)
    {
        string cur = null;
        foreach (var line in SplitLines(text))
        {
            var sm = Regex.Match(line, @"^\s*\[([^\[\]]+)\]\s*$");
            if (sm.Success) { cur = sm.Groups[1].Value.Trim(); continue; }
            if (!SectionMatch(cur, section)) continue;
            var m = Regex.Match(line, @"^\s*" + Regex.Escape(key) + @"\s*=\s*(.+?)\s*$");
            if (m.Success) return Unquote(m.Groups[1].Value);
        }
        return "";
    }

    public static string SetValue(string text, string section, string key, string value)
    {
        var lines = SplitLines(text);
        string cur = null;
        int sectionStart = -1, insertAt = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            var sm = Regex.Match(lines[i], @"^\s*\[([^\[\]]+)\]\s*$");
            if (sm.Success)
            {
                cur = sm.Groups[1].Value.Trim();
                if (SectionMatch(cur, section)) sectionStart = i;
                continue;
            }
            if (SectionMatch(cur, section))
            {
                insertAt = i;
                var m = Regex.Match(lines[i], @"^\s*" + Regex.Escape(key) + @"\s*=");
                if (m.Success)
                {
                    lines[i] = key + " = \"" + value + "\"";
                    return string.Join(Environment.NewLine, TrimTrailing(lines)) + Environment.NewLine;
                }
            }
        }

        var newLine = key + " = \"" + value + "\"";
        if (section == null)
        {
            lines.Insert(0, newLine);
        }
        else if (sectionStart >= 0)
        {
            lines.Insert((insertAt >= 0 ? insertAt : sectionStart) + 1, newLine);
        }
        else
        {
            lines.Add("");
            lines.Add("[" + section + "]");
            lines.Add(newLine);
        }
        return string.Join(Environment.NewLine, TrimTrailing(lines)) + Environment.NewLine;
    }

    // ---------------------------------------------------------------- helpers
    private static bool SectionMatch(string current, string wanted)
    {
        if (wanted == null) return current == null;
        return string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase);
    }

    private static string Unquote(string v)
    {
        v = v.Trim();
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') return v[1..^1];
        return v;
    }

    private static List<string> SplitLines(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

    private static List<string> TrimTrailing(List<string> lines)
    {
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }
}
