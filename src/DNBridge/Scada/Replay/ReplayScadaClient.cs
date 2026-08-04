using DNBridge.Elements;
using DNBridge.Events;
using lib60870.CS101;

namespace DNBridge.Scada.Replay;

/// <summary>
/// Drop-in <see cref="IScadaClient"/> that feeds recorded measurement snapshots into the shared
/// <see cref="ElementCache"/> as if they arrived from a live SCADA server. Used for offline
/// testing of the whole DNC↔DNBridge pipeline without a SCADA connection.
///
/// <para>Because it implements <see cref="IScadaClient"/>, the engine, <c>CommandExecutor</c>, and
/// every event stay identical to the live path. Key differences:
/// <list type="bullet">
///   <item><see cref="ConnectAsync"/> just marks "connected" so the UI status shows — it does
///     <b>not</b> inject (injection is driven from the UI: on file open and on the Inject button).</item>
///   <item>All <c>Send*</c> control methods are no-ops — nothing is sent to any SCADA.</item>
///   <item><see cref="Inject"/> updates the cache exactly like <c>ScadaClient.OnAsduReceived</c>
///     (find by address, skip unregistered, stamp value/quality/time/type) and raises the same
///     <see cref="ElementsUpdated"/> / <see cref="DataReceived"/> events.</item>
/// </list></para>
/// </summary>
public class ReplayScadaClient : IScadaClient, IReplaySource
{
    // CA is fixed at 1 for this deployment, so a row's cache key is (1<<24)|IOA — the same
    // encoding Element104 uses and ScadaClient forms from (CA<<24)|IOA.
    private const ulong ReplayCa = 1UL;

    private readonly ElementCache _cache;
    private readonly Action<string, DnbLogLevel> _log;
    private readonly IReplayReader _reader;
    private readonly object _sync = new();

    private IReadOnlyList<ReplaySample> _samples = Array.Empty<ReplaySample>();
    private string? _loadedFile;
    private volatile bool _isConnected;

    public bool IsConnected => _isConnected;
    public bool HasSamples => _samples.Count > 0;

    public event EventHandler<ScadaConnectionEventArgs>? ConnectionChanged;
    public event EventHandler<ScadaDataEventArgs>? DataReceived;
    public event EventHandler<ElementValueChangedEventArgs>? ElementsUpdated;

    public ReplayScadaClient(ElementCache cache, Action<string, DnbLogLevel> log, IReplayReader reader)
    {
        _cache = cache;
        _log = log;
        _reader = reader;
    }

    public Task ConnectAsync(CancellationToken ct)
    {
        _isConnected = true;
        _log("Replay: source active (no live SCADA)", DnbLogLevel.Info);
        ConnectionChanged?.Invoke(this, new ScadaConnectionEventArgs(true, "Replay"));
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _isConnected = false;
        ConnectionChanged?.Invoke(this, new ScadaConnectionEventArgs(false));
        _log("Replay: source stopped", DnbLogLevel.Info);
        return Task.CompletedTask;
    }

    /// <summary>Parse a snapshot file, remember its samples, and inject them once.</summary>
    public void LoadFile(string path)
    {
        var samples = _reader.Read(path, _log);
        lock (_sync)
        {
            _samples = samples;
            _loadedFile = path;
        }
        Inject();
    }

    /// <summary>
    /// Apply the loaded snapshot to the cache with a fresh "now" timestamp. Elements not yet
    /// registered by DNC are skipped (like an ASDU for an unregistered IOA). No-op with a warning
    /// when nothing is loaded or nothing matched.
    /// </summary>
    public void Inject()
    {
        IReadOnlyList<ReplaySample> samples;
        string? file;
        lock (_sync)
        {
            samples = _samples;
            file = _loadedFile;
        }

        if (samples.Count == 0)
        {
            _log("Replay: nothing to inject — no snapshot loaded", DnbLogLevel.Warning);
            return;
        }

        var now = DateTime.UtcNow;
        var updates = new List<ElementUpdate>(samples.Count);
        int missed = 0;

        foreach (var s in samples)
        {
            ulong addr = (ReplayCa << 24) | (s.Ioa & 0xFFFFFF);
            var elem = _cache.Find(addr);
            if (elem == null)
            {
                missed++;
                continue; // IOA not registered by DNC yet — skip, like the live path
            }

            elem.Value = s.Value;
            elem.Quality = 0;           // "set as valid"
            elem.LastDataTime = now;
            elem.Iec104Type = s.Iec104Type;

            updates.Add(new ElementUpdate(addr, s.Value, 0, now));
        }

        if (updates.Count == 0)
        {
            _log($"Replay: injected 0/{samples.Count} — none of the snapshot IOAs are registered yet " +
                 "(let DNC register elements, then Inject again)", DnbLogLevel.Warning);
            return;
        }

        ElementsUpdated?.Invoke(this, new ElementValueChangedEventArgs(updates));

        string fileName = file != null ? Path.GetFileName(file) : "snapshot";
        string logSummary = $"Injected {updates.Count}/{samples.Count} element(s) from {fileName}" +
                            (missed > 0 ? $" ({missed} not registered)" : "");
        _log($"Replay: {logSummary}", DnbLogLevel.Info);

        // Not a real ASDU — typeId 0 / "REPLAY" / "INJECT" are placeholders so this fits the
        // same master-line shape as live SCADA traffic.
        string master = TrafficFormat.ScadaMaster(
            false, DateTime.Now, 0, "REPLAY", updates.Count, TrafficFormat.DefaultCa, "INJECT",
            missed > 0 ? $"{missed} not registered" : null);
        RaiseTraffic(master,
            string.Join(Environment.NewLine, updates
                .OrderBy(u => u.Address & 0xFFFFFF)
                .Select(u => TrafficFormat.ScadaDetailLine(u.UpdatedAt, u.Quality, (uint)(u.Address & 0xFFFFFF), u.Value))),
            outbound: false);
    }

    #region Send* — no-ops (replay never transmits to SCADA)

    public void SendSetpointShort(Element104 element, double value, byte cot, bool useSbo = false) =>
        SuppressedSend(TypeID.C_SE_TC_1, element, value, cot);

    public void SendSingleCommand(Element104 element, bool value, byte cot, bool useSbo = false) =>
        SuppressedSend(TypeID.C_SC_NA_1, element, value ? 1 : 0, cot);

    public void SendDoubleCommand(Element104 element, int value, byte cot, bool useSbo = false) =>
        SuppressedSend(TypeID.C_DC_NA_1, element, value, cot);

    public void SendSinglePointWithTime(Element104 element, bool value, byte cot) =>
        SuppressedSend(TypeID.M_SP_TB_1, element, value ? 1 : 0, cot);

    public void SendDoublePointWithTime(Element104 element, int value, byte cot) =>
        SuppressedSend(TypeID.M_DP_TB_1, element, value, cot);

    public void SendMeasuredShortWithTime(Element104 element, double value, byte cot) =>
        SuppressedSend(TypeID.M_ME_TF_1, element, value, cot);

    private void SuppressedSend(TypeID typeId, Element104 element, double value, byte cot)
    {
        _log($"Replay: outbound {typeId} suppressed — IOA={element.IOA} value={value} COT={cot}", DnbLogLevel.Debug);
        RaiseTraffic(
            TrafficFormat.ScadaMaster(true, DateTime.Now, (int)typeId, typeId.ToString(), 1, (int)element.CA, cot),
            TrafficFormat.ScadaDetailLine(DateTime.UtcNow, null, element.IOA, value, note: "SUPPRESSED (replay)"),
            outbound: true);
    }

    #endregion

    private void RaiseTraffic(string summary, string detail, bool outbound) =>
        DataReceived?.Invoke(this, new ScadaDataEventArgs(summary, detail, outbound));
}
