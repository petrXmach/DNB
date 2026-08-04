using DNBridge.Events;

namespace DNBridge.Core;

public class DnbConfig
{
    public string? LogFile        { get; set; }
    public int     LogLevel       { get; set; } = 2;
    public int     TcpPort        { get; set; } = 9000;

    /// <summary>
    /// Folder for the SCADA traffic log ([Scada] Scada_log). Null/empty = disabled. Files are
    /// named "scada_MMdd.log", one per calendar date — see <see cref="Scada.ScadaFileLogger"/>.
    /// </summary>
    public string? ScadaLog       { get; set; }

    /// <summary>
    /// IEC104 server addresses loaded from the [Scada] Addresses key.
    /// Populated by Config.Load(); empty list when not configured.
    /// </summary>
    public List<(string Host, int Port)> ScadaAddresses { get; set; } = new();

    /// <summary>
    /// How to interpret the wall-clock in SCADA CP56Time2a tags. true = the SCADA sends UTC
    /// (use the bytes as-is); false = it sends local time (convert local→UTC). CP56Time2a
    /// carries no zone, so this must match the SCADA's clock or all timed values land skewed
    /// (a wrong setting shifts every timestamp by the UTC offset and breaks GetData filtering).
    /// Loaded from [Scada] TimeIsUtc (1/0); default true.
    /// </summary>
    public bool ScadaTimeIsUtc { get; set; } = true;

    public void Log(Action<string, DnbLogLevel> log, bool fileFound)
    {
        log(fileFound
            ? "Config: loaded from dnbridge.ini"
            : "Config: dnbridge.ini not found — using defaults",
            fileFound ? DnbLogLevel.Info : DnbLogLevel.Warning);

        log($"Config: TcpPort     = {TcpPort}", DnbLogLevel.Info);
        log($"Config: LogLevel    = {LogLevel}", DnbLogLevel.Info);

        if (LogFile is not null)
            log($"Config: LogFile     = {LogFile}", DnbLogLevel.Info);

        if (ScadaLog is not null)
            log($"Config: ScadaLog    = {ScadaLog}", DnbLogLevel.Info);

        if (ScadaAddresses.Count == 0)
            log("Config: ScadaAddresses = (none)", DnbLogLevel.Warning);
        else
            for (int i = 0; i < ScadaAddresses.Count; i++)
                log($"Config: ScadaAddress[{i}] = {ScadaAddresses[i].Host}:{ScadaAddresses[i].Port}", DnbLogLevel.Info);

        log($"Config: ScadaTimeIsUtc = {ScadaTimeIsUtc} (CP56Time2a interpreted as {(ScadaTimeIsUtc ? "UTC" : "local")})", DnbLogLevel.Info);
    }

    /// <summary>
    /// Returns true if valid, false if engine must not start. Logs each error itself.
    /// <paramref name="requireScadaAddresses"/> is false in replay mode, where no live SCADA
    /// connection is made and the [Scada] addresses are irrelevant.
    /// </summary>
    public bool Validate(Action<string, DnbLogLevel> log, bool requireScadaAddresses = true)
    {
        bool ok = true;

        if (TcpPort <= 0 || TcpPort > 65535)
        {
            log($"Config ERROR: TcpPort={TcpPort} is invalid (must be 1–65535)", DnbLogLevel.Error);
            ok = false;
        }
        else if (TcpPort == 9000)
            log("Config WARNING: TcpPort is still the default (9000) — verify this is intended", DnbLogLevel.Warning);

        if (requireScadaAddresses && ScadaAddresses.Count == 0)
        {
            log("Config ERROR: No SCADA addresses — add [Scada] Addresses=host:port to dnbridge.ini", DnbLogLevel.Error);
            ok = false;
        }

        return ok;
    }
}
