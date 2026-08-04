namespace DNBridge.Core;

/// <summary>
/// Reads dnbridge.ini from the exe directory and populates a DnbConfig.
/// INI format: [Section] headers, Key=Value pairs.
/// Comment lines start with ; or #. Keys are case-insensitive.
/// </summary>
public static class Config
{
    public const string FileName = "dnbridge.ini";

    /// <summary>
    /// Loads config from the given path, or from dnbridge.ini in the exe directory if omitted.
    /// Returns defaults when the file does not exist.
    /// </summary>
    public static DnbConfig Load(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, FileName);

        var cfg = new DnbConfig(); // property initializers supply defaults

        if (!File.Exists(path))
            return cfg;

        var sections = ParseIni(path);

        if (sections.TryGetValue("Config", out var s))
        {
            if (s.TryGetValue("Log_File",  out var v)) cfg.LogFile = v;
            if (s.TryGetValue("Log_Level", out v) && int.TryParse(v, out var ll)) cfg.LogLevel = ll;
            if (s.TryGetValue("TCP_Port",  out v) && int.TryParse(v, out var port)) cfg.TcpPort = port;
        }

        if (sections.TryGetValue("Scada", out s))
        {
            if (s.TryGetValue("Addresses", out var v))
                cfg.ScadaAddresses = ParseAddresses(v);
            if (s.TryGetValue("TimeIsUtc", out v))
                cfg.ScadaTimeIsUtc = v == "1";
            if (s.TryGetValue("Scada_log", out v))
                cfg.ScadaLog = v;
        }

        return cfg;
    }

    // -------------------------------------------------------------------------

    private static Dictionary<string, Dictionary<string, string>> ParseIni(string path)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        string? section = null;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim();
                if (!result.ContainsKey(section))
                    result[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            if (section is null)
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            result[section][line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        return result;
    }

    /// <summary>
    /// Parses semicolon-separated "host:port" or "host" entries.
    /// Default port is 2404 when omitted.
    /// </summary>
    private static List<(string Host, int Port)> ParseAddresses(string raw)
    {
        const int DefaultPort = 2404;
        var list = new List<(string Host, int Port)>();

        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var col = part.LastIndexOf(':');
            if (col > 0 && int.TryParse(part[(col + 1)..], out var port))
                list.Add((part[..col], port));
            else
                list.Add((part, DefaultPort));
        }

        return list;
    }
}
