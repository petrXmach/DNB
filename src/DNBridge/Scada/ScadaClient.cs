using DNBridge.Elements;
using DNBridge.Events;
using lib60870;
using lib60870.CS104;
using lib60870.CS101;

namespace DNBridge.Scada;

/// <summary>
/// IEC 60870-5-104 SCADA client wrapping lib60870.NET Connection.
///
/// Connectivity is driven by a single background <b>supervisor loop</b> that is the only
/// place a connection is created, torn down, or reconnected. lib60870 event handlers only
/// update state flags — they never reconnect and never block — which keeps the lifecycle
/// simple and race-free.
///
/// Edge cases handled:
///  • Orphaned worker threads — every close force-cancels the socket (lib60870's Close()
///    is a no-op while the socket is still connecting), and any event from a superseded
///    connection cancels that connection so it cannot hold the server's connection slot.
///  • Stuck "TCP up but STARTDT never confirmed" — bounded by a startup timeout.
///  • Missed CLOSED events — the supervisor detects a dead worker thread and reconnects.
/// </summary>
public class ScadaClient : IScadaClient
{
    private const int SupervisorIntervalMs = 1000;   // how often the supervisor re-evaluates state
    private const int ReconnectDelayMs = 5000;       // backoff between connection attempts
    private const int ConnectStuckTimeoutMs = 15000; // max time to reach "connected" (> lib60870 T0 connect timeout of 10s)
    private const int CacheWarnIntervalMs = 30000;   // rate-limit for "no element matched cache" warnings

    private const int GI_QOI = 20;     // Station interrogation
    private const int GI_CA = 0xFFFF;  // Broadcast CA for GI

    private readonly ElementCache _cache;
    private readonly Action<string, DnbLogLevel> _log;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly List<(string Host, int Port)> _serverAddresses;
    private readonly bool _cp56IsUtc;   // true = SCADA CP56Time2a tags carry UTC; false = local time

    // Connection state — all touched from the supervisor thread, lib60870 worker threads
    // and Connect/Disconnect callers, hence volatile / Interlocked.
    private volatile Connection? _connection;
    private volatile bool _isConnected;
    private volatile bool _startDtReceived;
    private volatile bool _intentionalDisconnect;

    private int _activeAttemptId;        // identifies the current connection; bumped on every (re)connect/close
    private int _currentServerIndex;
    private long _attemptStartedAt;      // Environment.TickCount64 when the current attempt began
    private long _nextConnectAllowedAt;  // Environment.TickCount64 backoff gate
    private long _lastCacheWarnAt;

    private CancellationTokenSource? _cts;
    private Task? _supervisorTask;

    public bool IsConnected => _isConnected;

    public event EventHandler<ScadaConnectionEventArgs>? ConnectionChanged;
    /// <summary>SCADA traffic (both directions): inbound ASDUs/confirmations and outbound control commands/GI.</summary>
    public event EventHandler<ScadaDataEventArgs>? DataReceived;
    public event EventHandler<ElementValueChangedEventArgs>? ElementsUpdated;

    /// <summary>Raises a SCADA traffic item for the monitor view (summary == detail for single-line entries).</summary>
    private void RaiseTraffic(string summary, string detail, bool outbound) =>
        DataReceived?.Invoke(this, new ScadaDataEventArgs(summary, detail, outbound));

    /// <summary>
    /// Raises a traffic item for a single outbound control/monitoring ASDU, in the same
    /// master/detail shape as received frames. The raw COT byte is shown by its mapped
    /// enum name so inbound and outbound lines read alike. Quality is omitted — we send a
    /// qualifier, not a quality descriptor.
    /// </summary>
    private void RaiseOutbound(TypeID typeId, Element104 element, double value, byte cot) =>
        RaiseTraffic(
            TrafficFormat.ScadaMaster(true, DateTime.Now, (int)typeId, typeId.ToString(), 1, (int)element.CA, MapCauseOfTransmission(cot)),
            TrafficFormat.ScadaDetailLine(DateTime.UtcNow, null, element.IOA, value),
            outbound: true);

    /// <summary>
    /// Creates SCADA client bound to shared element cache, logger callback,
    /// and the list of server endpoints from config.
    /// </summary>
    public ScadaClient(ElementCache cache, Action<string, DnbLogLevel> log, List<(string Host, int Port)> serverAddresses, bool cp56IsUtc = true)
    {
        _cache = cache;
        _log = log;
        _serverAddresses = serverAddresses;
        _cp56IsUtc = cp56IsUtc;
    }

    /// <summary>
    /// Starts SCADA connectivity. Idempotent: if the supervisor is already running this is a
    /// no-op (DNC may send RegisterElements — and therefore trigger connect — more than once).
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct)
    {
        _log("SCADA: ConnectAsync called", DnbLogLevel.Trace);
        await _lifecycleLock.WaitAsync(ct);
        try
        {
            if (_serverAddresses.Count == 0)
            {
                _log("SCADA: No server addresses configured — add [Scada] Addresses to dnbridge.ini", DnbLogLevel.Error);
                return;
            }

            _intentionalDisconnect = false;

            if (_supervisorTask is { IsCompleted: false })
            {
                _log("SCADA: Connectivity already managed — ConnectAsync ignored", DnbLogLevel.Debug);
                return;
            }

            _log($"SCADA: Configured servers: {string.Join(", ", _serverAddresses.Select(a => $"{a.Host}:{a.Port}"))}", DnbLogLevel.Debug);

            _currentServerIndex = 0;
            _nextConnectAllowedAt = 0; // connect immediately on first supervisor tick
            _isConnected = false;
            _startDtReceived = false;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _supervisorTask = Task.Run(() => SupervisorLoop(_cts.Token));
            _log("SCADA: Supervisor started", DnbLogLevel.Debug);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Stops the supervisor and closes the active SCADA connection.
    /// </summary>
    public async Task DisconnectAsync()
    {
        _log("SCADA: DisconnectAsync called", DnbLogLevel.Trace);

        _intentionalDisconnect = true;
        _cts?.Cancel();

        await _lifecycleLock.WaitAsync();
        try
        {
            _isConnected = false;
            _startDtReceived = false;

            // CloseConnection may Join the lib60870 worker thread — offload so we never
            // block the caller's thread (e.g. a UI thread).
            await Task.Run(CloseConnection);

            ConnectionChanged?.Invoke(this, new ScadaConnectionEventArgs(false));
            _log("SCADA: Disconnected", DnbLogLevel.Info);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    #region Supervisor — the single owner of connect / reconnect / teardown

    /// <summary>
    /// Periodically evaluates connection health and (re)connects as needed. This is the only
    /// place that creates or tears down a connection, so there is never more than one tracked
    /// connection and reconnect logic lives in exactly one spot.
    /// </summary>
    private async Task SupervisorLoop(CancellationToken ct)
    {
        _log("SCADA: Supervisor loop started", DnbLogLevel.Trace);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _lifecycleLock.WaitAsync(ct);
                try
                {
                    if (!_intentionalDisconnect)
                        SuperviseOnce();
                }
                finally
                {
                    _lifecycleLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log($"SCADA: Supervisor error — {ex.GetType().Name}: {ex.Message}", DnbLogLevel.Error);
            }

            try
            {
                await Task.Delay(SupervisorIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        _log("SCADA: Supervisor loop stopped", DnbLogLevel.Trace);
    }

    /// <summary>
    /// One supervision step. Runs under <see cref="_lifecycleLock"/> with intentional-disconnect ruled out.
    /// </summary>
    private void SuperviseOnce()
    {
        long now = Environment.TickCount64;
        var conn = _connection;

        if (conn != null)
        {
            if (_isConnected)
            {
                if (conn.IsRunning)
                    return; // healthy

                // Worker thread died without delivering a CLOSED event.
                _log("SCADA: Worker thread stopped while connected — reconnecting", DnbLogLevel.Warning);
                TearDown(now);
            }
            else
            {
                // Still establishing (TCP connect or waiting for STARTDT_CON).
                if (now - _attemptStartedAt < ConnectStuckTimeoutMs)
                    return; // give it time

                _log($"SCADA: Connection stuck establishing after {ConnectStuckTimeoutMs / 1000}s " +
                     $"(libRunning={conn.IsRunning}, startDt={_startDtReceived}) — reconnecting", DnbLogLevel.Warning);
                TearDown(now);
            }
        }

        if (_connection == null && now >= _nextConnectAllowedAt)
            StartAttempt(now);
    }

    /// <summary>
    /// Creates a fresh lib60870 connection for the current server and starts connecting (non-blocking).
    /// </summary>
    private void StartAttempt(long now)
    {
        var (host, port) = _serverAddresses[_currentServerIndex];
        _log($"SCADA: Connecting to {host}:{port} (server {_currentServerIndex + 1}/{_serverAddresses.Count})...", DnbLogLevel.Info);

        int attemptId = Interlocked.Increment(ref _activeAttemptId);
        _attemptStartedAt = now;
        _startDtReceived = false;

        try
        {
            var conn = new Connection(host, port) { DebugOutput = false };
            // Capture conn + attemptId so each connection's callbacks can identify and, if
            // superseded, cancel themselves — guaranteeing no orphan keeps the server slot.
            conn.SetConnectionHandler((_, ev) => OnConnectionEvent(conn, attemptId, ev), null);
            conn.SetASDUReceivedHandler((_, asdu) => OnAsduReceived(attemptId, asdu), null);

            _connection = conn;
            conn.ConnectAsync(); // non-blocking; failures arrive via OnConnectionEvent(CONNECT_FAILED)
            _log($"SCADA: lib60870 ConnectAsync called (attemptId={attemptId})", DnbLogLevel.Trace);
        }
        catch (Exception ex)
        {
            _log($"SCADA: Failed to start connection — {ex.GetType().Name}: {ex.Message}", DnbLogLevel.Warning);
            _connection = null;
            _nextConnectAllowedAt = now + ReconnectDelayMs;
        }
    }

    /// <summary>
    /// Force-closes the current connection, rotates to the next server, and arms the reconnect backoff.
    /// Used for supervisor-detected failures (stuck establishing / dead worker).
    /// </summary>
    private void TearDown(long now)
    {
        bool wasConnected = _isConnected;
        _isConnected = false;
        _startDtReceived = false;

        CloseConnection(); // bumps attemptId, so the resulting CLOSED is stale and won't double-react

        if (wasConnected)
        {
            _log("SCADA: Connection lost", DnbLogLevel.Warning);
            ConnectionChanged?.Invoke(this, new ScadaConnectionEventArgs(false));
        }

        RotateServer();
        _nextConnectAllowedAt = now + ReconnectDelayMs;
    }

    /// <summary>
    /// Closes and forgets the current lib60870 connection. Bumping the attempt id first means any
    /// late callback from this connection is treated as stale. Cancel() force-closes the socket —
    /// essential because lib60870's Close() is a no-op while the socket is still connecting, which
    /// would otherwise leave an orphaned worker thread holding the server's connection slot.
    /// </summary>
    private void CloseConnection()
    {
        var conn = _connection;
        _connection = null;
        if (conn == null)
            return;

        Interlocked.Increment(ref _activeAttemptId); // invalidate any further callbacks from this connection

        _log($"SCADA: Closing lib60870 connection (IsRunning={conn.IsRunning})", DnbLogLevel.Debug);
        try { conn.Cancel(); } catch (Exception ex) { _log($"SCADA: Cancel() error — {ex.GetType().Name}: {ex.Message}", DnbLogLevel.Trace); }
        try { conn.Close(); } catch (Exception ex) { _log($"SCADA: Close() error — {ex.GetType().Name}: {ex.Message}", DnbLogLevel.Trace); }
        _log("SCADA: lib60870 connection closed", DnbLogLevel.Trace);
    }

    private void RotateServer()
    {
        if (_serverAddresses.Count > 1)
        {
            _currentServerIndex = (_currentServerIndex + 1) % _serverAddresses.Count;
            var next = _serverAddresses[_currentServerIndex];
            _log($"SCADA: Next attempt will use {next.Host}:{next.Port}", DnbLogLevel.Debug);
        }
    }

    #endregion

    #region lib60870 connection event handler

    /// <summary>
    /// Handles lib60870 connection state changes. Only updates flags / arms backoff — the supervisor
    /// performs the actual (re)connect. Callbacks from a superseded connection cancel that connection.
    /// </summary>
    private void OnConnectionEvent(Connection conn, int attemptId, ConnectionEvent connectionEvent)
    {
        if (attemptId != Volatile.Read(ref _activeAttemptId))
        {
            _log($"SCADA: Ignoring stale ConnectionEvent.{connectionEvent} (attemptId={attemptId})", DnbLogLevel.Trace);

            // A superseded connection that still managed to come up would hold the server's single
            // connection slot — cancel it so the current attempt can be activated.
            if (connectionEvent is ConnectionEvent.OPENED or ConnectionEvent.STARTDT_CON_RECEIVED)
            {
                _log($"SCADA: Cancelling orphaned connection (attemptId={attemptId})", DnbLogLevel.Debug);
                Task.Run(() => { try { conn.Cancel(); } catch { /* best effort */ } });
            }
            return;
        }

        _log($"SCADA: ConnectionEvent.{connectionEvent} received (intentionalDisconnect={_intentionalDisconnect})", DnbLogLevel.Debug);

        switch (connectionEvent)
        {
            case ConnectionEvent.OPENED:
                _log("SCADA: TCP connection opened, waiting for STARTDT confirmation...", DnbLogLevel.Debug);
                break;

            case ConnectionEvent.STARTDT_CON_RECEIVED:
                // Some servers/stacks deliver STARTDT_CON more than once for a single connection.
                // The attempt-id check above already filters stale connections; this guards a
                // duplicate on the *current* one so we don't re-send GI or re-raise "connected".
                // StartAttempt resets _startDtReceived, so the next real connection still passes.
                if (_startDtReceived)
                {
                    _log("SCADA: Duplicate STARTDT_CON ignored (already started)", DnbLogLevel.Debug);
                    break;
                }
                _startDtReceived = true;
                _isConnected = true;
                var addr = _serverAddresses[_currentServerIndex];
                _log($"SCADA: Connected to {addr.Host}:{addr.Port} — sending GI", DnbLogLevel.Info);
                ConnectionChanged?.Invoke(this, new ScadaConnectionEventArgs(true, $"{addr.Host}:{addr.Port}"));

                try
                {
                    conn.SendInterrogationCommand(CauseOfTransmission.ACTIVATION, GI_CA, GI_QOI);
                    _log("SCADA: GI command sent successfully", DnbLogLevel.Debug);
                    string giSent = TrafficFormat.ScadaMaster(
                        true, DateTime.Now, (int)TypeID.C_IC_NA_1, nameof(TypeID.C_IC_NA_1), 1, GI_CA, CauseOfTransmission.ACTIVATION);
                    RaiseTraffic(giSent, "", outbound: true);
                }
                catch (ConnectionException ex)
                {
                    _log($"SCADA: Failed to send GI — {ex.Message}", DnbLogLevel.Warning);
                }
                break;

            case ConnectionEvent.CLOSED:
                bool wasConnected = _isConnected;
                _isConnected = false;
                _startDtReceived = false;
                _connection = null;
                _log($"SCADA: Connection closed (wasConnected={wasConnected}, intentional={_intentionalDisconnect})", DnbLogLevel.Debug);

                if (wasConnected)
                {
                    _log("SCADA: Connection lost", DnbLogLevel.Warning);
                    ConnectionChanged?.Invoke(this, new ScadaConnectionEventArgs(false));
                }

                if (!_intentionalDisconnect)
                {
                    RotateServer();
                    _nextConnectAllowedAt = Environment.TickCount64 + ReconnectDelayMs;
                }
                break;

            case ConnectionEvent.CONNECT_FAILED:
                _log("SCADA: Connection attempt failed", DnbLogLevel.Warning);
                _connection = null;

                if (!_intentionalDisconnect)
                {
                    RotateServer();
                    _nextConnectAllowedAt = Environment.TickCount64 + ReconnectDelayMs;
                }
                break;
        }
    }

    #endregion

    #region ASDU receive handler

    /// <summary>
    /// Receives ASDUs, updates element cache, and emits data events for host monitoring.
    /// </summary>
    private bool OnAsduReceived(int attemptId, ASDU asdu)
    {
        if (attemptId != Volatile.Read(ref _activeAttemptId))
        {
            _log($"SCADA: Ignoring stale ASDU TypeID={asdu.TypeId} (attemptId={attemptId})", DnbLogLevel.Trace);
            return true;
        }

        _log($"SCADA: ASDU received TypeID={asdu.TypeId} {TrafficFormat.CaPrefix(asdu.Ca)}COT={asdu.Cot} elements={asdu.NumberOfElements}", DnbLogLevel.Trace);

        // Surface the raw COT of every inbound control-direction frame (setpoint/command echoes
        // and confirmations). SCADA may "confirm" with a COT other than ACT_CON/ACT_TERM, which
        // would otherwise be logged only as generic "ASDU matched" monitoring data with no COT.
        if (asdu.TypeId is TypeID.C_SC_NA_1 or TypeID.C_DC_NA_1
                        or TypeID.C_SE_NC_1 or TypeID.C_SE_TC_1)
        {
            var io0 = asdu.GetElement(0);
            _log($"SCADA: Inbound control {asdu.TypeId} IOA={io0.ObjectAddress} " +
                 $"COT={asdu.Cot} negative={asdu.IsNegative}", DnbLogLevel.Debug);
        }

        // Handle interrogation confirmations
        if (asdu.TypeId == TypeID.C_IC_NA_1)
        {
            // GI carries no per-object data worth a detail row — master line only. A negative
            // confirmation has no detail row to carry the result, so it rides on the master
            // line's trailing note instead (same slot command confirmations use their detail row for).
            string? giNote = asdu.IsNegative ? "REJECTED" : null;
            string giMsg = TrafficFormat.ScadaMaster(
                false, DateTime.Now, (int)asdu.TypeId, asdu.TypeId.ToString(), asdu.NumberOfElements, asdu.Ca, asdu.Cot, giNote);
            // Shown in the SCADA Traffic tab; keep only a Debug echo in the log
            // (connection-up is already logged at Info when GI is sent).
            _log($"SCADA: {giMsg}", DnbLogLevel.Debug);
            RaiseTraffic(giMsg, "", outbound: false);
            return true;
        }

        // Handle control command confirmations (C_SC, C_DC, C_SE)
        if (asdu.Cot == CauseOfTransmission.ACTIVATION_CON ||
            asdu.Cot == CauseOfTransmission.ACTIVATION_TERMINATION)
        {
            var io = asdu.GetElement(0);
            string result = asdu.IsNegative ? "REJECTED" : "confirmed";
            // Confirmations echo the command object, so the value is decodable — show it in
            // the same detail shape as every other frame. Quality is omitted: control ASDUs
            // carry a qualifier, not a quality descriptor.
            var (ackValue, _, ackTime) = ExtractValue(asdu.TypeId, io);
            _log($"SCADA: Command {result} IOA={io.ObjectAddress} COT={asdu.Cot} TypeID={asdu.TypeId}",
                 asdu.IsNegative ? DnbLogLevel.Warning : DnbLogLevel.Debug);
            RaiseTraffic(
                TrafficFormat.ScadaMaster(false, DateTime.Now, (int)asdu.TypeId, asdu.TypeId.ToString(), 1, asdu.Ca, asdu.Cot),
                TrafficFormat.ScadaDetailLine(ackTime, null, (uint)io.ObjectAddress, ackValue, note: result),
                outbound: false);
            return true;
        }

        // Process monitoring data
        List<ElementUpdate>? updates = null;

        for (int i = 0; i < asdu.NumberOfElements; i++)
        {
            try
            {
                var io = asdu.GetElement(i);
                ulong addr = ((ulong)(ushort)asdu.Ca << 24) | ((uint)io.ObjectAddress & 0xFFFFFF);

                var elem = _cache.Find(addr);
                if (elem == null)
                    continue; // unregistered IOA — skip

                var (value, quality, timestamp) = ExtractValue(asdu.TypeId, io);

                elem.Value = value;
                elem.Quality = quality;
                elem.LastDataTime = timestamp;
                elem.Iec104Type = (byte)asdu.TypeId;

                if (ElementsUpdated != null)
                {
                    updates ??= new List<ElementUpdate>();
                    updates.Add(new ElementUpdate(addr, value, quality, timestamp));
                }
            }
            catch (Exception ex)
            {
                _log($"SCADA: Error processing element {i} of TypeID={asdu.TypeId} — {ex.Message}", DnbLogLevel.Warning);
            }
        }

        if (updates != null)
        {
            _log($"SCADA: ASDU matched {updates.Count}/{asdu.NumberOfElements} elements " +
                 $"({TrafficFormat.CaPrefix(asdu.Ca)}TypeID={asdu.TypeId} COT={asdu.Cot})", DnbLogLevel.Debug);
            ElementsUpdated?.Invoke(this, new ElementValueChangedEventArgs(updates));
        }
        else if (asdu.NumberOfElements > 0)
        {
            // Rate-limited so a persistent CA mismatch is visible without flooding the log.
            long now = Environment.TickCount64;
            if (now - _lastCacheWarnAt >= CacheWarnIntervalMs)
            {
                _lastCacheWarnAt = now;
                _log($"SCADA: ASDU CA={asdu.Ca} TypeID={asdu.TypeId} has {asdu.NumberOfElements} elements but none matched cache ({_cache.Count} registered). Check that SCADA CA matches registered elements.", DnbLogLevel.Warning);
            }
        }

        int count = asdu.NumberOfElements;
        string summary = TrafficFormat.ScadaMaster(false, DateTime.Now, (int)asdu.TypeId, asdu.TypeId.ToString(), count, asdu.Ca, asdu.Cot);
        string detail = string.Join(Environment.NewLine, Enumerable.Range(0, count).Select(i =>
        {
            try
            {
                var io = asdu.GetElement(i);
                var (value, quality, timestamp) = ExtractValue(asdu.TypeId, io);
                return ((uint)io.ObjectAddress, TrafficFormat.ScadaDetailLine(
                    timestamp,
                    CarriesQuality(asdu.TypeId) ? quality : null,
                    (uint)io.ObjectAddress,
                    value));
            }
            catch
            {
                return (uint.MaxValue, " ; ??:??:??; -; ?; error");
            }
        })
        // Multi-element blocks arrive in whatever order the SCADA packed them — sort by IOA
        // so the detail pane reads top-to-bottom like the element list, not send order.
        .OrderBy(t => t.Item1)
        .Select(t => t.Item2));

        RaiseTraffic(summary, detail, outbound: false);

        return true;
    }

    /// <summary>
    /// Whether an ASDU type actually carries a quality descriptor. Control types (C_SC / C_DC /
    /// C_SE) carry a *qualifier of command* (QU/QOS) instead, and M_ME_ND_1 is defined without
    /// quality — <see cref="ExtractValue"/> reports 0 for all of these as a neutral placeholder,
    /// so displaying it as "Quality=OK" would assert something the frame never said.
    /// </summary>
    private static bool CarriesQuality(TypeID typeId) => typeId switch
    {
        TypeID.C_SC_NA_1 or TypeID.C_DC_NA_1 or TypeID.C_SE_NC_1 or TypeID.C_SE_TC_1 => false,
        TypeID.M_ME_ND_1 => false,
        _ => true,
    };

    /// <summary>
    /// Converts supported monitoring ASDUs into normalized value/quality/timestamp tuple.
    /// Quality is 0 for types without a quality descriptor — see <see cref="CarriesQuality"/>;
    /// that 0 is a placeholder, not a "good" reading.
    /// </summary>
    private (double Value, uint Quality, DateTime Timestamp) ExtractValue(TypeID typeId, InformationObject io)
    {
        switch (typeId)
        {
            // --- Single point (bool) ---
            case TypeID.M_SP_NA_1:
            {
                var v = (SinglePointInformation)io;
                return (v.Value ? 1.0 : 0.0, v.Quality.EncodedValue, DateTime.UtcNow);
            }
            case TypeID.M_SP_TB_1:
            {
                var v = (SinglePointWithCP56Time2a)io;
                return (v.Value ? 1.0 : 0.0, v.Quality.EncodedValue, Cp56ToUtc(v.Timestamp));
            }

            // --- Double point (4-state) ---
            case TypeID.M_DP_NA_1:
            {
                var v = (DoublePointInformation)io;
                return ((double)v.Value, v.Quality.EncodedValue, DateTime.UtcNow);
            }
            case TypeID.M_DP_TB_1:
            {
                var v = (DoublePointWithCP56Time2a)io;
                return ((double)v.Value, v.Quality.EncodedValue, Cp56ToUtc(v.Timestamp));
            }

            // --- Step position ---
            case TypeID.M_ST_NA_1:
            {
                var v = (StepPositionInformation)io;
                return (v.Value, v.Quality.EncodedValue, DateTime.UtcNow);
            }
            case TypeID.M_ST_TB_1:
            {
                var v = (StepPositionWithCP56Time2a)io;
                return (v.Value, v.Quality.EncodedValue, Cp56ToUtc(v.Timestamp));
            }

            // --- Measured normalized ---
            case TypeID.M_ME_NA_1:
            {
                var v = (MeasuredValueNormalized)io;
                return (v.NormalizedValue, v.Quality.EncodedValue, DateTime.UtcNow);
            }
            case TypeID.M_ME_TD_1:
            {
                var v = (MeasuredValueNormalizedWithCP56Time2a)io;
                return (v.NormalizedValue, v.Quality.EncodedValue, Cp56ToUtc(v.Timestamp));
            }
            case TypeID.M_ME_ND_1:
            {
                var v = (MeasuredValueNormalizedWithoutQuality)io;
                return (v.NormalizedValue, 0, DateTime.UtcNow); // no quality descriptor
            }

            // --- Measured scaled ---
            case TypeID.M_ME_NB_1:
            {
                var v = (MeasuredValueScaled)io;
                return (v.ScaledValue.Value, v.Quality.EncodedValue, DateTime.UtcNow);
            }
            case TypeID.M_ME_TE_1:
            {
                var v = (MeasuredValueScaledWithCP56Time2a)io;
                return (v.ScaledValue.Value, v.Quality.EncodedValue, Cp56ToUtc(v.Timestamp));
            }

            // --- Measured short float ---
            case TypeID.M_ME_NC_1:
            {
                var v = (MeasuredValueShort)io;
                return (v.Value, v.Quality.EncodedValue, DateTime.UtcNow);
            }
            case TypeID.M_ME_TF_1:
            {
                var v = (MeasuredValueShortWithCP56Time2a)io;
                return (v.Value, v.Quality.EncodedValue, Cp56ToUtc(v.Timestamp));
            }

            // --- TEMPORARY (Main104 inputs): SCADA issues these regulation params as
            //     control commands (COT=ACTIVATION). Decode them as values; commands carry
            //     a qualifier, not a quality descriptor, so quality is reported as good (0).
            case TypeID.C_SC_NA_1:
            {
                var v = (SingleCommand)io;
                return (v.State ? 1.0 : 0.0, 0, DateTime.UtcNow);
            }
            case TypeID.C_DC_NA_1:
            {
                var v = (DoubleCommand)io;
                return (v.State, 0, DateTime.UtcNow);
            }
            case TypeID.C_SE_NC_1:
            {
                var v = (SetpointCommandShort)io;
                return (v.Value, 0, DateTime.UtcNow);
            }
            case TypeID.C_SE_TC_1:
            {
                var v = (SetpointCommandShortWithCP56Time2a)io;
                return (v.Value, 0, Cp56ToUtc(v.Timestamp));
            }

            default:
                return (0, 0x80, DateTime.UtcNow); // unknown type — mark as invalid quality
        }
    }

    /// <summary>
    /// A CP56Time2a carries a wall-clock with no zone (GetDateTime() returns Kind=Unspecified),
    /// so the SCADA's clock convention must be supplied via config (_cp56IsUtc):
    ///  • UTC SCADA → tag the bytes as UTC directly (no offset applied);
    ///  • local SCADA → interpret as local and convert to the true UTC instant.
    /// Either way the result is Kind=Utc, so it shares the clock with the untimed
    /// (DateTime.UtcNow) values and stays consistent with AddDate (UTC epoch ms) and the
    /// GetData NewerThan comparison (also UTC). A Local-kind DateTime would throw in
    /// TlvWriter.AddDate. A wrong setting shifts every timed value by the local UTC offset.
    /// </summary>
    private DateTime Cp56ToUtc(CP56Time2a t) =>
        _cp56IsUtc
            ? DateTime.SpecifyKind(t.GetDateTime(), DateTimeKind.Utc)
            : DateTime.SpecifyKind(t.GetDateTime(), DateTimeKind.Local).ToUniversalTime();

    /// <summary>
    /// Current time as a CP56Time2a tag for outbound commands, using the same clock
    /// convention as <see cref="Cp56ToUtc"/> so the SCADA reads our timestamps correctly.
    /// </summary>
    private CP56Time2a NowCp56() => new(_cp56IsUtc ? DateTime.UtcNow : DateTime.Now);

    #endregion

    #region Send control commands

    /// <summary>
    /// Sends SetpointCommandShort to SCADA for the given element.
    /// </summary>
    public void SendSetpointShort(Element104 element, double value, byte cot, bool useSbo = false)
    {
        var conn = GetConnectedOrLog("SendSetpointShort");
        if (conn == null) return;

        if (useSbo)
            _log("SCADA: SBO not yet implemented for SetpointShort — using direct execute", DnbLogLevel.Debug);

        try
        {
            var qos = new SetpointCommandQualifier(select: false, ql: 0);
            // Send as C_SE_TC_1 (type 63): short-float setpoint WITH CP56Time2a time tag,
            // stamped with the current local time (IEC 104 timestamps are local).
            // COT is forced to ACTIVATION: a control-direction command must use ACTIVATION
            // (the poke carries SPONTANEOUS, which SCADA rejects for a control ASDU). The
            // slave answers ACT_CON, optionally ACT_TERM — see OnAsduReceived.
            const byte actCot = (byte)CauseOfTransmission.ACTIVATION;
            conn.SendControlCommand(
                MapCauseOfTransmission(actCot),
                (int)element.CA,
                new SetpointCommandShortWithCP56Time2a((int)element.IOA, (float)value, qos, NowCp56()));

            _log($"SCADA: Sent SetpointShortWithTime (C_SE_TC_1) {TrafficFormat.Address(element.Address)} value={value} COT={actCot}", DnbLogLevel.Debug);
            RaiseOutbound(TypeID.C_SE_TC_1, element, value, actCot);
        }
        catch (ConnectionException ex)
        {
            _log($"SCADA: SendSetpointShort failed — {ex.Message}", DnbLogLevel.Error);
        }
    }

    /// <summary>
    /// Sends SingleCommand to SCADA for the given element.
    /// </summary>
    public void SendSingleCommand(Element104 element, bool value, byte cot, bool useSbo = false)
    {
        var conn = GetConnectedOrLog("SendSingleCommand");
        if (conn == null) return;

        if (useSbo)
            _log("SCADA: SBO not yet implemented for SingleCommand — using direct execute", DnbLogLevel.Debug);

        try
        {
            conn.SendControlCommand(
                MapCauseOfTransmission(cot),
                (int)element.CA,
                new SingleCommand((int)element.IOA, value, selectCommand: false, qu: 0));

            _log($"SCADA: Sent SingleCommand {TrafficFormat.Address(element.Address)} value={value} COT={cot}", DnbLogLevel.Debug);
            RaiseOutbound(TypeID.C_SC_NA_1, element, value ? 1 : 0, cot);
        }
        catch (ConnectionException ex)
        {
            _log($"SCADA: SendSingleCommand failed — {ex.Message}", DnbLogLevel.Error);
        }
    }

    /// <summary>
    /// Sends DoubleCommand to SCADA for the given element.
    /// </summary>
    public void SendDoubleCommand(Element104 element, int value, byte cot, bool useSbo = false)
    {
        if (value < 0 || value > 3)
        {
            _log($"SCADA: SendDoubleCommand ignored — invalid value={value}, expected 0..3", DnbLogLevel.Warning);
            return;
        }

        var conn = GetConnectedOrLog("SendDoubleCommand");
        if (conn == null) return;

        if (useSbo)
            _log("SCADA: SBO not yet implemented for DoubleCommand — using direct execute", DnbLogLevel.Debug);

        try
        {
            conn.SendControlCommand(
                MapCauseOfTransmission(cot),
                (int)element.CA,
                new DoubleCommand((int)element.IOA, value, select: false, quality: 0));

            _log($"SCADA: Sent DoubleCommand {TrafficFormat.Address(element.Address)} value={value} COT={cot}", DnbLogLevel.Debug);
            RaiseOutbound(TypeID.C_DC_NA_1, element, value, cot);
        }
        catch (ConnectionException ex)
        {
            _log($"SCADA: SendDoubleCommand failed — {ex.Message}", DnbLogLevel.Error);
        }
    }

    /// <summary>
    /// TEMPORARY (Main104 outputs): sends a single-point information WITH time tag
    /// (M_SP_TB_1, type 30) to SCADA — a master-originated monitoring ASDU. Used for the
    /// boolean Main104 output (State). Timestamp is current local time.
    /// </summary>
    public void SendSinglePointWithTime(Element104 element, bool value, byte cot)
    {
        var conn = GetConnectedOrLog("SendSinglePointWithTime");
        if (conn == null) return;

        try
        {
            var asdu = new ASDU(conn.GetApplicationLayerParameters(),
                MapCauseOfTransmission(cot), false, false, 0, (int)element.CA, false);
            asdu.AddInformationObject(new SinglePointWithCP56Time2a(
                (int)element.IOA, value, new QualityDescriptor(), NowCp56()));
            conn.SendASDU(asdu);

            _log($"SCADA: Sent SinglePointWithTime (M_SP_TB_1) {TrafficFormat.Address(element.Address)} value={value} COT={cot}", DnbLogLevel.Debug);
            RaiseOutbound(TypeID.M_SP_TB_1, element, value ? 1 : 0, cot);
        }
        catch (ConnectionException ex)
        {
            _log($"SCADA: SendSinglePointWithTime failed — {ex.Message}", DnbLogLevel.Error);
        }
    }

    /// <summary>
    /// TEMPORARY (Main104): sends a double-point information WITH time tag (M_DP_TB_1, type 31)
    /// to SCADA — a master-originated monitoring ASDU. Used for the double-point Main104 input
    /// mirror (RegMode). Value must be 0..3 (INTERMEDIATE/OFF/ON/INDETERMINATE); timestamp is
    /// the current time in the configured SCADA clock convention.
    /// </summary>
    public void SendDoublePointWithTime(Element104 element, int value, byte cot)
    {
        if (value < 0 || value > 3)
        {
            _log($"SCADA: SendDoublePointWithTime ignored — invalid value={value}, expected 0..3", DnbLogLevel.Warning);
            return;
        }

        var conn = GetConnectedOrLog("SendDoublePointWithTime");
        if (conn == null) return;

        try
        {
            var asdu = new ASDU(conn.GetApplicationLayerParameters(),
                MapCauseOfTransmission(cot), false, false, 0, (int)element.CA, false);
            asdu.AddInformationObject(new DoublePointWithCP56Time2a(
                (int)element.IOA, (DoublePointValue)value, new QualityDescriptor(), NowCp56()));
            conn.SendASDU(asdu);

            _log($"SCADA: Sent DoublePointWithTime (M_DP_TB_1) {TrafficFormat.Address(element.Address)} value={value} COT={cot}", DnbLogLevel.Debug);
            RaiseOutbound(TypeID.M_DP_TB_1, element, value, cot);
        }
        catch (ConnectionException ex)
        {
            _log($"SCADA: SendDoublePointWithTime failed — {ex.Message}", DnbLogLevel.Error);
        }
    }

    /// <summary>
    /// TEMPORARY (Main104 outputs): sends a measured short float WITH time tag
    /// (M_ME_TF_1, type 36) to SCADA — a master-originated monitoring ASDU. Used for the
    /// float Main104 outputs (Qmin/Qmax/Loss). Timestamp is current local time.
    /// </summary>
    public void SendMeasuredShortWithTime(Element104 element, double value, byte cot)
    {
        var conn = GetConnectedOrLog("SendMeasuredShortWithTime");
        if (conn == null) return;

        try
        {
            var asdu = new ASDU(conn.GetApplicationLayerParameters(),
                MapCauseOfTransmission(cot), false, false, 0, (int)element.CA, false);
            asdu.AddInformationObject(new MeasuredValueShortWithCP56Time2a(
                (int)element.IOA, (float)value, new QualityDescriptor(), NowCp56()));
            conn.SendASDU(asdu);

            _log($"SCADA: Sent MeasuredShortWithTime (M_ME_TF_1) {TrafficFormat.Address(element.Address)} value={value} COT={cot}", DnbLogLevel.Debug);
            RaiseOutbound(TypeID.M_ME_TF_1, element, value, cot);
        }
        catch (ConnectionException ex)
        {
            _log($"SCADA: SendMeasuredShortWithTime failed — {ex.Message}", DnbLogLevel.Error);
        }
    }

    /// <summary>
    /// Returns the active connection only when the IEC104 transport is fully up (STARTDT confirmed),
    /// otherwise logs and returns null. Snapshotting the reference avoids it being nulled mid-send.
    /// </summary>
    private Connection? GetConnectedOrLog(string methodName)
    {
        var conn = _connection;
        if (conn == null || !_startDtReceived || !_isConnected)
        {
            _log($"SCADA: {methodName} ignored — not connected (conn={conn != null}, startDt={_startDtReceived}, isConn={_isConnected})", DnbLogLevel.Warning);
            return null;
        }
        return conn;
    }

    /// <summary>
    /// Maps raw COT byte from TLV protocol to lib60870 CauseOfTransmission enum.
    /// Falls back to ACTIVATION when value is unknown.
    /// </summary>
    private static CauseOfTransmission MapCauseOfTransmission(byte cot)
    {
        return Enum.IsDefined(typeof(CauseOfTransmission), (int)cot)
            ? (CauseOfTransmission)cot
            : CauseOfTransmission.ACTIVATION;
    }

    #endregion
}
