using System.Globalization;
using DNBridge.DncServer;
using DNBridge.Elements;
using DNBridge.Events;
using DNBridge.Scada;
using DNBridge.Tlv;
using lib60870.CS101;

namespace DNBridge.Commands;

/// <summary>
/// Executes deserialized DNC commands against the element cache and SCADA client.
/// Replaces C++ Commands_Srv.cpp Exec() methods.
/// </summary>
public class CommandExecutor
{
    /// <summary>
    /// TEMPORARY (Main104 mirrors): the State output id (bare 102, OR-ed with Main104Flag).
    /// DNC pokes it once per calc cycle — on both the OK path and the error path (State then
    /// carries the error code) — so it is the trigger for echoing the input mirrors to SCADA.
    /// Remove with the XChng.cfg loader.
    /// </summary>
    private const uint Main104StateId = 102u | ElementStub.Main104Flag; // 0xC0000066

    private readonly ElementCache _cache;
    private readonly IScadaClient _scadaClient;
    private readonly Action<string, DnbLogLevel> _log;

    /// <summary>
    /// Engine-lifetime token. The SCADA connection (and its reconnect/health loops)
    /// must live across individual DNC commands but be torn down when the engine stops,
    /// so it is bound to this token rather than the per-command (None) token.
    /// </summary>
    private readonly CancellationToken _engineToken;

    /// <summary>Set by DnbEngine to receive notification after RegisterElements completes.</summary>
    public Action<ElementsRegisteredEventArgs>? OnElementsRegistered { get; set; }

    /// <summary>Set by DnbEngine to receive notification after each Poke value is sent to SCADA.</summary>
    public Action<ElementPokeEventArgs>? OnPokeExecuted { get; set; }

    /// <summary>
    /// TEMPORARY (Main104): sender for unsolicited frames to the connected DNC client
    /// (set by the DNC client handler). Used for reverse-Poke of Main104 inputs. Null when
    /// no DNC client is connected. Remove with the Main104 reverse-Poke path.
    /// </summary>
    private Func<byte[], CancellationToken, Task>? _dncSender;

    /// <summary>Wire the active DNC client's send method so reverse-Pokes can reach DNC.</summary>
    public void SetDncSender(Func<byte[], CancellationToken, Task>? sender) => _dncSender = sender;

    public CommandExecutor(ElementCache cache, IScadaClient scadaClient, Action<string, DnbLogLevel> log, CancellationToken engineToken)
    {
        _cache = cache;
        _scadaClient = scadaClient;
        _log = log;
        _engineToken = engineToken;
    }

    public async Task ExecuteAsync(DnbCommand command, DncSession session, CancellationToken ct)
    {
        switch (command)
        {
            case InitCommand init:
                ExecuteInit(init, session);
                break;
            case RegisterElementsCommand regElems:
                await ExecuteRegisterElementsAsync(regElems, session, ct);
                break;
            case GetDataCommand getData:
                ExecuteGetData(getData, session);
                break;
            case PokeCommand poke:
                ExecutePoke(poke, session);
                break;
            default:
                _log($"CommandExecutor: unknown command type {command.GetType().Name}", DnbLogLevel.Warning);
                break;
        }
    }

    private void ExecuteInit(InitCommand cmd, DncSession session)
    {
        session.ServerName = cmd.ServerName;
        _log($"Init: ServerName=\"{cmd.ServerName}\"", DnbLogLevel.Info);

        cmd.Answer = new InitAnswer
        {
            SessionId = cmd.SessionId,
            IsOk = true,
            Mode = InitAnswer.ModeIec104
        };
    }

    private async Task ExecuteRegisterElementsAsync(RegisterElementsCommand cmd, DncSession session, CancellationToken ct)
    {
        bool ok = true;

        try
        {
            foreach (var stub in cmd.Elements)
            {
                var elem = _cache.FindOrCreate(stub.Address104, stub.IsSetPoint);

                bool isMain104 = (stub.Id & ElementStub.Main104Flag) == ElementStub.Main104Flag;
                if (stub.IsSetPoint || isMain104)
                {
                    elem.IsSetPoint = true;
                    session.CommandElements[stub.Id] = elem;
                }
                else
                {
                    session.MonitorElements[stub.Id] = elem;
                }

                // DIAGNOSTIC: show the raw inputs that decide routing, so a later failed Poke
                // can be matched against exactly what was registered (ID / flag / address).
                _log($"RegisterElements: id=0x{stub.Id:X8} setPoint={stub.IsSetPoint} main104={isMain104} " +
                     $"{TrafficFormat.Address(stub.Address104)} type={stub.Iec104Type} -> " +
                     $"{(stub.IsSetPoint || isMain104 ? "Command" : "Monitor")}", DnbLogLevel.Debug);
            }

            // TEMPORARY: Load command elements from XChng.cfg (will be removed when added to RegisterElements).
            // Load exactly once per session: DNC sends RegisterElements several times (double-fire +
            // batching), and the loader appends to Main104Mirrors — reloading would duplicate the
            // mirrors so each is sent 2-3x per calc cycle.
            // Main104 input startup values (XChng.cfg defaults, or live SCADA values on a DNC
            // reconnect). Dispatched below, AFTER OnElementsRegistered — see the call site.
            IReadOnlyList<ElementUpdate> initialMain104 = Array.Empty<ElementUpdate>();
            if (session.TryBeginXChngLoad())
                initialMain104 = LoadXChngCfgElements(session);

            _log($"RegisterElements: {session.MonitorElements.Count} monitor, {session.CommandElements.Count} command elements", DnbLogLevel.Info);

            // DIAGNOSTIC: dump the full CommandElements key set — this is exactly the list a
            // Poke must match against. Compare these IDs to the incoming Poke ElementId.
            if (session.CommandElements.Count > 0)
                _log($"RegisterElements: CommandElements keys = [{string.Join(", ", session.CommandElements.Keys.Select(k => $"0x{k:X8}"))}]", DnbLogLevel.Debug);

            // Notify host with categorized element lists
            if (OnElementsRegistered != null)
            {
                // Each descriptor carries the element's CURRENT cached value: the host rebuilds its
                // rows on every RegisterElements (DNC sends it several times), so a value omitted
                // here is a value blanked in the UI. See ElementInfo's remarks.
                static ElementInfo Describe(uint id, Element104 e) =>
                    new(id, e.Address, e.CA, e.IOA, e.Iec104Type, e.Value, e.Quality, e.LastDataTime);

                var monitor = session.MonitorElements
                    .Select(kvp => Describe(kvp.Key, kvp.Value))
                    .ToList();

                var command = session.CommandElements
                    .Where(kvp => (kvp.Key & ElementStub.Main104Flag) != ElementStub.Main104Flag)
                    .Select(kvp => Describe(kvp.Key, kvp.Value))
                    .ToList();

                // Main104 OUTPUTS (id >= 100): live in CommandElements with the Main104Flag.
                var main104 = session.CommandElements
                    .Where(kvp => (kvp.Key & ElementStub.Main104Flag) == ElementStub.Main104Flag)
                    .Select(kvp => Describe(kvp.Key, kvp.Value))
                    .ToList();

                // Main104 INPUTS (id < 100): only recorded in Main104Inputs (address -> flagged id),
                // never in CommandElements, so surface them here through the SAME ElementInfo path so
                // the host renders them in the Main104 tab identically to the outputs (name/direction
                // from Main104Catalog, live IOA/value). Skips any address missing from the cache
                // (the XChng loader FindOrCreate's them, so this should not happen).
                foreach (var (address, flaggedId) in session.Main104Inputs)
                {
                    var elem = _cache.Find(address);
                    if (elem != null)
                        main104.Add(Describe(flaggedId, elem));
                }

                OnElementsRegistered(new ElementsRegisteredEventArgs(monitor, command, main104));
            }

            // Push the Main104 input startup values to DNC, overwriting its own ini/hardcoded
            // defaults. Safe in either order against DNC's Reg_DoneOK -> SendData_to_DRS, which
            // only reads its fields and pokes them back out (we drop that echo) — it never
            // overwrites what we set here. The host does not need an event for these: the values
            // are already in the cache, so OnElementsRegistered above carries them.
            SendReversePokes(initialMain104, session);

            // Connect to SCADA after elements are registered.
            // Bind to the engine token (not the per-command ct) so the SCADA connection
            // outlives this command but is torn down when the engine stops.
            if (!string.IsNullOrEmpty(session.ServerName))
            {
                await _scadaClient.ConnectAsync(_engineToken);
            }
            else
            {
                _log("RegisterElements: no ServerName set (Init not received?)", DnbLogLevel.Warning);
                ok = false;
            }
        }
        catch (Exception ex)
        {
            _log($"RegisterElements failed: {ex.Message}", DnbLogLevel.Error);
            session.MonitorElements.Clear();
            session.CommandElements.Clear();
            ok = false;
        }

        cmd.Answer = new RegisterElementsAnswer
        {
            SessionId = cmd.SessionId,
            IsOk = ok
        };
    }

    private void ExecuteGetData(GetDataCommand cmd, DncSession session)
    {
        var answer = new GetDataAnswer { SessionId = cmd.SessionId };

        if (cmd.Start)
        {
            session.ResetDataCursor();

            // DIAGNOSTIC: expose the comparison that decides "new data" — NewerThan vs the
            // newest cached LastDataTime. A negative skew here (newest < NewerThan) is the
            // classic CP56Time2a UTC-vs-local mismatch and explains an empty GetData answer.
            if (session.MonitorElements.Count > 0)
            {
                var newest = session.MonitorElements.Values.Max(e => e.LastDataTime);
                _log($"GetData: monitor={session.MonitorElements.Count} elems, " +
                     $"NewerThan={cmd.NewerThan:yyyy-MM-dd HH:mm:ss.fff}Z, " +
                     $"newest LastDataTime={newest:yyyy-MM-dd HH:mm:ss.fff}Z, " +
                     $"skew={(newest - cmd.NewerThan).TotalSeconds:F1}s", DnbLogLevel.Debug);
            }
        }

        int count = 0;
        while (!session.IsCursorExhausted)
        {
            var next = session.GetNext();
            if (next == null)
                break;

            var elem = next.Value.Element;

            if (elem.LastDataTime > cmd.NewerThan)
            {
                answer.Values.Add(new ElementValue
                {
                    Address = elem.Address,
                    Value = elem.Value,
                    Quality = elem.Quality,
                    DateTime = elem.LastDataTime
                });

                if (++count >= GetDataAnswer.MaxRecordsPerAnswer)
                    break;
            }
        }

        answer.IsFinal = session.IsCursorExhausted;
        cmd.Answer = answer;

        _log($"GetData: returned {answer.Values.Count} value(s), isFinal={answer.IsFinal}", DnbLogLevel.Debug);
    }

    private void ExecutePoke(PokeCommand cmd, DncSession session)
    {
        // DIAGNOSTIC: log the incoming poke (id + values) before any routing decision.
        _log($"Poke received: {cmd.GetDetails()}", DnbLogLevel.Debug);

        if (!session.CommandElements.TryGetValue(cmd.ElementId, out var elem))
        {
            // Main104 INPUT ids (SCADA → DNC) are not poke targets: DNC echoes them outbound
            // (e.g. at startup) but they are mirrored back via reverse-Poke, never forwarded to
            // SCADA. Dropping them is expected, so log at Debug rather than warning.
            if (session.Main104Inputs.ContainsValue(cmd.ElementId))
            {
                _log($"Poke: element 0x{cmd.ElementId:X8} is a Main104 input — not mirrored to SCADA (ignored)", DnbLogLevel.Debug);
                return;
            }

            // DIAGNOSTIC: dump the known command-element keys so the mismatch is visible
            // (missing ID, wrong Main104Flag, or the element was routed to MonitorElements).
            var known = session.CommandElements.Count == 0
                ? "none registered"
                : string.Join(", ", session.CommandElements.Keys.Select(k => $"0x{k:X8}"));
            _log($"Poke: element 0x{cmd.ElementId:X8} not found in command elements " +
                 $"({session.CommandElements.Count} known: {known})", DnbLogLevel.Warning);
            return;
        }

        if (cmd.Values.Count == 0)
        {
            _log($"Poke: element 0x{cmd.ElementId:X8} mapped but carried no poke values", DnbLogLevel.Warning);
            return;
        }

        foreach (var pokeValue in cmd.Values)
        {
            double sentValue;
            string pokeType="Not supported!";

            // TEMPORARY (Main104 outputs): when the element carries a configured monitoring
            // type (M_SP_TB_1 / M_ME_TF_1), send it as that ASDU rather than the poke-kind
            // default control type. Regular setpoints (no such type) keep the switch below.
            if (elem.Iec104Type == (byte)TypeID.M_SP_TB_1 || elem.Iec104Type == (byte)TypeID.M_ME_TF_1)
            {
                (double v, byte cot) = ExtractPokeValue(pokeValue);
                if (elem.Iec104Type == (byte)TypeID.M_SP_TB_1)
                    _scadaClient.SendSinglePointWithTime(elem, v > 0.5, cot);
                else
                    _scadaClient.SendMeasuredShortWithTime(elem, v, cot);

                elem.Value = v;
                pokeType = elem.Iec104Type == (byte)TypeID.M_SP_TB_1 ? "M_SP_TB_1" : "M_ME_TF_1";
                OnPokeExecuted?.Invoke(new ElementPokeEventArgs(cmd.ElementId, elem.Address, elem.CA, elem.IOA, v, pokeType));
                continue;
            }

            switch (pokeValue)
            {
                case FloatPokeValue fpv:
                    _scadaClient.SendSetpointShort(elem, fpv.Value, fpv.COT);
                    elem.Value = fpv.Value;
                    elem.Iec104Type = (byte)TypeID.M_ME_NC_1;
                    sentValue = fpv.Value;
                    pokeType = "Float";
                    break;

                case BoolPokeValue bpv:
                    _scadaClient.SendSingleCommand(elem, bpv.Value, bpv.COT);
                    elem.Value = bpv.Value ? 1.0 : 0.0;
                    elem.Iec104Type = (byte)TypeID.M_SP_NA_1;
                    sentValue = elem.Value;
                    pokeType = "Bool";
                    break;

                case FourStatePokeValue dpv:
                    _scadaClient.SendDoubleCommand(elem, dpv.Value, dpv.COT);
                    elem.Value = dpv.Value;
                    elem.Iec104Type = (byte)TypeID.M_DP_NA_1;
                    sentValue = dpv.Value;
                    pokeType = "4State";
                    break;

                default:
					_log($"Poke: element 0x{cmd.ElementId:X8}, pokeType {pokeValue} not supported", DnbLogLevel.Error);
					continue;
            }

            OnPokeExecuted?.Invoke(new ElementPokeEventArgs(cmd.ElementId, elem.Address, elem.CA, elem.IOA, sentValue, pokeType));
        }

        // TEMPORARY (Main104 mirrors): the State poke closes a calc cycle — echo the current
        // cached input values back to SCADA at their mirror output addresses.
        if (cmd.ElementId == Main104StateId)
            SendMain104Mirrors(session);
    }

    /// <summary>
    /// TEMPORARY (Main104 mirrors): re-sends each registered input's current cached value to
    /// SCADA at its mirror OUTPUT address as a time-tagged MONITORING ASDU typed by the target
    /// (30 M_SP_TB_1 / 31 M_DP_TB_1 / 36 M_ME_TF_1). Sent spontaneously (COT=SPONTANEOUS) with a
    /// current CP56Time2a timestamp so the operator sees the value DNBridge received and when.
    /// Remove with the XChng.cfg loader.
    /// </summary>
    private void SendMain104Mirrors(DncSession session)
    {
        if (session.Main104Mirrors.Count == 0)
            return;

        const byte cot = (byte)CauseOfTransmission.SPONTANEOUS;

        foreach (var m in session.Main104Mirrors)
        {
            double v = m.Source.Value;
            var t = m.Target;
            string pokeType;

            switch (t.Iec104Type)
            {
                case (byte)TypeID.M_SP_TB_1: // 30 — single point (bool) with time
                    _scadaClient.SendSinglePointWithTime(t, v > 0.5, cot);
                    pokeType = "Mirror M_SP_TB_1";
                    break;
                case (byte)TypeID.M_DP_TB_1: // 31 — double point (0..3) with time
                    _scadaClient.SendDoublePointWithTime(t, (int)Math.Round(v), cot);
                    pokeType = "Mirror M_DP_TB_1";
                    break;
                case (byte)TypeID.M_ME_TF_1: // 36 — measured short float with time
                    _scadaClient.SendMeasuredShortWithTime(t, v, cot);
                    pokeType = "Mirror M_ME_TF_1";
                    break;
                default:
                    _log($"Mirror: id=0x{(m.SourceId | ElementStub.Main104Flag):X8} unsupported target type " +
                         $"{t.Iec104Type} at {TrafficFormat.Address(t.Address)} — skipped", DnbLogLevel.Warning);
                    continue;
            }

            t.Value = v;
            _log($"Mirror -> SCADA: srcId={m.SourceId} value={v} -> {TrafficFormat.Address(t.Address)} " +
                 $"({pokeType})", DnbLogLevel.Debug);
            OnPokeExecuted?.Invoke(new ElementPokeEventArgs(
                m.SourceId | ElementStub.Main104Flag, t.Address, t.CA, t.IOA, v, pokeType));
        }
    }

    /// <summary>Value + COT from any poke value kind (bool → 0/1), for type-driven sending.</summary>
    private static (double Value, byte COT) ExtractPokeValue(PokeValue pokeValue) => pokeValue switch
    {
        FloatPokeValue f => (f.Value, f.COT),
        BoolPokeValue b => (b.Value ? 1.0 : 0.0, b.COT),
        FourStatePokeValue d => (d.Value, d.COT),
        _ => (0.0, (byte)CauseOfTransmission.SPONTANEOUS)
    };

    #region TEMPORARY: XChng.cfg loader — remove when these elements are included in RegisterElements command from DNC

    /// <summary>
    /// Loads Main104 elements from XChng.cfg in the exe directory.
    /// Original C++ behavior: cl_104_Client::GetXChngCfg() in cl_104_Client.cpp.
    /// Format: tab-separated lines "IEC104_Address\tID\tType", comments start with // or #.
    /// Each id is OR-ed with Main104Flag (0xC0000000). Direction is inferred from the id:
    /// id &gt;= 100 = OUTPUT (DNC → SCADA) → session.CommandElements (Poke target);
    /// id &lt; 100 = INPUT (SCADA → DNC) → session.Main104Inputs (reverse-Poke source).
    /// An INPUT line may carry an optional 4th column = default value, seeded into the
    /// cache as if SCADA had sent it (see below).
    /// </summary>
    /// <returns>
    /// The Main104 input values to push to DNC as reverse-Pokes at registration — every
    /// input that has a value, whether just seeded from a default or already live from
    /// SCADA. Empty if XChng.cfg is absent.
    /// </returns>
    private IReadOnlyList<ElementUpdate> LoadXChngCfgElements(DncSession session)
    {
        var cfgPath = Path.Combine(AppContext.BaseDirectory, "XChng.cfg");
        if (!File.Exists(cfgPath))
            return Array.Empty<ElementUpdate>();

        // Idempotency (defense in depth against a re-entry): the loader repopulates these
        // collections from scratch. Main104Mirrors is a List that would otherwise accumulate
        // duplicates on a reload; the dictionaries overwrite by key but are cleared for symmetry.
        session.Main104Mirrors.Clear();
        session.Main104Inputs.Clear();

        int loaded = 0;
        int lineNum = 0;

        foreach (var line in File.ReadLines(cfgPath))
        {
            lineNum++;

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//") || line.StartsWith("#"))
                continue;

            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                _log($"XChng.cfg line {lineNum}: expected 3 tab-separated fields, got {parts.Length}", DnbLogLevel.Warning);
                continue;
            }

            if (!ElementStub.TryParseAddress(parts[0].Trim(), out var address))
            {
                _log($"XChng.cfg line {lineNum}: invalid address \"{parts[0]}\"", DnbLogLevel.Warning);
                continue;
            }

            if (!uint.TryParse(parts[1].Trim(), out var id))
            {
                _log($"XChng.cfg line {lineNum}: invalid ID \"{parts[1]}\"", DnbLogLevel.Warning);
                continue;
            }

            if (!byte.TryParse(parts[2].Trim(), out var type))
            {
                _log($"XChng.cfg line {lineNum}: invalid Type \"{parts[2]}\"", DnbLogLevel.Warning);
                continue;
            }

            var flaggedId = id | ElementStub.Main104Flag;

            // MIRROR line ("M" 4th column): 'address' is a NEW output address, 'id' is the
            // SOURCE input id to echo, 'type' is the control type to send it with. The input
            // must be declared earlier in the file so its cached element already exists.
            bool isMirror = parts.Length >= 4 && parts[3].Trim().Equals("M", StringComparison.OrdinalIgnoreCase);
            if (isMirror)
            {
                var src = session.Main104Inputs
                    .Where(kv => kv.Value == flaggedId)
                    .Select(kv => _cache.Find(kv.Key))
                    .FirstOrDefault();
                if (src == null)
                {
                    _log($"XChng.cfg line {lineNum}: mirror source input id {id} not found " +
                         $"(declare the input line before its mirror)", DnbLogLevel.Warning);
                    continue;
                }

                var target = _cache.FindOrCreate(address, isSetPoint: true);
                target.Iec104Type = type;
                session.Main104Mirrors.Add(new Main104Mirror(src, target, id));
                loaded++;
                continue;
            }

            var elem = _cache.FindOrCreate(address, isSetPoint: id >= 100);
            elem.Iec104Type = type;

            if (id >= 100)
            {
                // OUTPUT (DNC -> SCADA): DNC pokes id|flag; ExecutePoke forwards it to SCADA,
                // typed by Iec104Type (M_SP_TB_1 / M_ME_TF_1 for these points).
                elem.IsSetPoint = true;
                session.CommandElements[flaggedId] = elem;
            }
            else
            {
                // INPUT (SCADA -> DNC): keep it in the cache (so SCADA updates land on it) and
                // record address -> flagged id so a SCADA update triggers a reverse-Poke to DNC.
                session.Main104Inputs[address] = flaggedId;

                // Optional 4th column = default value, in the wire units SCADA would send
                // (% for UNet, kVAr for Qvvn/Q_tor). InvariantCulture is required: on a
                // cs-CZ machine the ambient culture wants a decimal comma and would reject "-8.0".
                if (parts.Length >= 4)
                {
                    if (!double.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var def))
                    {
                        _log($"XChng.cfg line {lineNum}: invalid default \"{parts[3]}\" for input id {id} " +
                             $"(use a decimal point, not a comma) — input left unseeded", DnbLogLevel.Warning);
                    }
                    else if (elem.LastDataTime == DateTime.MinValue)
                    {
                        // Seed as if SCADA had sent it, so the mirrors echo it and the UI shows it.
                        // The MinValue guard is load-bearing: the cache is engine-scoped and outlives
                        // a DNC reconnect, and this loader re-runs per session — without it, a
                        // reconnect would overwrite live SCADA values with defaults.
                        elem.Value = def;
                        elem.Quality = 0;
                        elem.LastDataTime = DateTime.UtcNow;
                        _log($"XChng.cfg: seeded Main104 input id {id} with default " +
                             $"{def.ToString(CultureInfo.InvariantCulture)} ({TrafficFormat.Address(address)})",
                             DnbLogLevel.Debug);
                    }
                }
            }
            loaded++;
        }

        if (loaded > 0)
        {
            int mirrors = session.Main104Mirrors.Count;
            int inputs = session.Main104Inputs.Count;
            _log($"XChng.cfg: loaded {loaded} Main104 elements " +
                 $"({inputs} inputs, {loaded - inputs - mirrors} outputs, {mirrors} mirrors)", DnbLogLevel.Info);
        }

        // Every input that HAS a value — seeded from a default just now, or already live from
        // SCADA (a DNC reconnect against a warm cache). Reading the cache rather than only the
        // freshly-seeded defaults is what makes a reconnect re-sync DNC to real SCADA data.
        // Inputs still at MinValue (no default, nothing from SCADA) are skipped: pushing 0.0
        // would zero DNC's parameters.
        var initial = new List<ElementUpdate>();
        foreach (var (address, _) in session.Main104Inputs)
        {
            var elem = _cache.Find(address);
            if (elem != null && elem.LastDataTime != DateTime.MinValue)
                initial.Add(new ElementUpdate(address, elem.Value, elem.Quality, elem.LastDataTime));
        }
        return initial;
    }

    /// <summary>
    /// On SCADA value updates, push a reverse-Poke to DNC for any address registered as a
    /// Main104 INPUT. This is the only channel that reaches DNC's SetParameter — GetData
    /// ignores Main104 addresses (DNC's cl_Data_Answer::Exec looks them up only in item_104).
    /// All values are sent as FloatPokeValue because DNC acts only on the float case in
    /// cl_Poke_Command::Exec (bool/4-state are ignored there). Remove with the loader.
    /// </summary>
    public void SendReversePokes(IReadOnlyList<ElementUpdate> updates, DncSession session)
    {
        var sender = _dncSender;
        if (sender == null || session.Main104Inputs.Count == 0)
            return;

        foreach (var update in updates)
        {
            if (!session.Main104Inputs.TryGetValue(update.Address, out var flaggedId))
                continue;

            var poke = new PokeCommand { ElementId = flaggedId };
            poke.Values.Add(new FloatPokeValue { Value = update.Value, COT = (byte)CauseOfTransmission.SPONTANEOUS });

            var writer = new TlvWriter();
            writer.SerializeObject(poke);
            byte[] data = writer.ToEnvelope();

            _ = sender(data, CancellationToken.None);
            _log($"Reverse-Poke -> DNC: id=0x{flaggedId:X8} value={update.Value} " +
                 $"({TrafficFormat.Address(update.Address)})", DnbLogLevel.Debug);
        }
    }

    #endregion
}
