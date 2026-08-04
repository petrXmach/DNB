using DNBridge.Commands;
using DNBridge.DncServer;
using DNBridge.Elements;
using DNBridge.Events;
using DNBridge.Scada;
using DNBridge.Scada.Replay;

namespace DNBridge.Core;

public class DnbEngine : IDnbEngine
{
    private readonly ElementCache _cache = new();
    private CancellationTokenSource? _cts;
    private DncTcpServer? _dncServer;
    private IScadaClient? _scadaClient;
    private CommandExecutor? _executor;
    private DncSession? _session;
    private IReplaySource? _replaySource;   // non-null only when started in replay mode
    private ScadaFileLogger? _scadaFileLogger;
    private bool _disposed;
    private bool _configFileFound;

    // TEMPORARY quick file logger — replace with a production logger later.
    // Wires the (otherwise unused) Config.LogFile into a minimal append-only file sink so
    // there is a persistent record to inspect. One StreamWriter + AutoFlush + one lock.
    private StreamWriter? _logFile;
    private readonly object _logFileLock = new();

    public bool IsRunning { get; private set; }
    public bool IsDncConnected { get; private set; }
    public bool IsScadaConnected { get; private set; }
    public DnbConfig Config { get; }
    public bool ReplayMode { get; set; }
    public string? ReplayFilePath { get; set; }
    public IReadOnlyDictionary<ulong, Element104> Elements => _cache.All;

    public event EventHandler<LogEventArgs>? LogMessage;
    public event EventHandler<IsRunningEventArgs>? IsRunningChanged;
    public event EventHandler<DncConnectionEventArgs>? DncConnectionChanged;
    public event EventHandler<ScadaConnectionEventArgs>? ScadaConnectionChanged;
    public event EventHandler<CommandReceivedEventArgs>? DncCommandReceived;
    public event EventHandler<ScadaDataEventArgs>? ScadaDataReceived;
    public event EventHandler<ElementsRegisteredEventArgs>? ElementsRegistered;
    public event EventHandler<ElementValueChangedEventArgs>? ElementValueChanged;
    public event EventHandler<ElementPokeEventArgs>? ElementPokeConfirmed;

    public DnbEngine()
    {
        var path = Path.Combine(AppContext.BaseDirectory, DNBridge.Core.Config.FileName);
        _configFileFound = File.Exists(path);
        Config = DNBridge.Core.Config.Load(path);
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (IsRunning)
            return Task.CompletedTask;

        // TEMPORARY quick file logger — open the sink before anything is logged.
        OpenLogFile();

        // Log config values (WPF is subscribed by this point)
        Config.Log(OnLogMessage, _configFileFound);

        // Validate mandatory fields — abort before touching any server state.
        // In replay mode the [Scada] addresses are irrelevant, so they are not required.
        if (!Config.Validate(OnLogMessage, requireScadaAddresses: !ReplayMode))
        {
            OnLogMessage("Engine cannot start — fix dnbridge.ini and restart", DnbLogLevel.Error);
            CloseLogFile();             // TEMPORARY: don't leak the file handle on the abort path
            return Task.CompletedTask;  // IsRunning stays false, no IsRunningChanged fired
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsRunning = true;
        IsRunningChanged?.Invoke(this, new IsRunningEventArgs(true));

        OnLogMessage("Engine started");

        // Create the SCADA data source: live IEC104 client, or an offline replay source that
        // feeds recorded snapshots into the same cache. Both implement IScadaClient, so the rest
        // of the wiring (events, CommandExecutor, Poke handling) is identical.
        if (ReplayMode)
        {
            OnLogMessage("Replay mode: live SCADA disabled — using snapshot replay source", DnbLogLevel.Info);
            var replay = new ReplayScadaClient(_cache, OnLogMessage, new SnapshotTableReader());
            _replaySource = replay;
            _scadaClient = replay;
        }
        else
        {
            _replaySource = null;
            _scadaClient = new ScadaClient(_cache, OnLogMessage, Config.ScadaAddresses, Config.ScadaTimeIsUtc);
        }
        _scadaClient.ConnectionChanged += OnScadaConnectionChanged;
        _scadaClient.DataReceived += OnScadaDataReceived;
        _scadaClient.ElementsUpdated += OnScadaElementsUpdated;

        // SCADA file logging (same format as the WPF SCADA Traffic detail pane) — enabled
        // when [Scada] Scada_log names a folder. No-op subscription when disabled.
        _scadaFileLogger = new ScadaFileLogger(Config.ScadaLog);
        _scadaFileLogger.LogStart();
        _scadaClient.DataReceived += _scadaFileLogger.LogTraffic;

        // Pre-load a snapshot chosen before Start so the first DNC GetData already has data
        // (once elements register; the user can re-Inject afterwards to catch late batches).
        if (ReplayMode && !string.IsNullOrWhiteSpace(ReplayFilePath))
            _replaySource!.LoadFile(ReplayFilePath!);

        // Create command executor and session.
        // Pass the engine token so the SCADA connection's reconnect/health loops are
        // torn down by _cts.Cancel() in StopAsync, not only by ScadaClient.DisconnectAsync.
        _executor = new CommandExecutor(_cache, _scadaClient, OnLogMessage, _cts.Token);
        _executor.OnElementsRegistered = args => ElementsRegistered?.Invoke(this, args);
        _executor.OnPokeExecuted = args => ElementPokeConfirmed?.Invoke(this, args);
        _session = new DncSession();

        // Start DNC TCP server
        _dncServer = new DncTcpServer(Config.TcpPort);
        _dncServer.ClientConnected += OnDncClientConnected;
        _dncServer.ClientDisconnected += OnDncClientDisconnected;
        _dncServer.ClientHandlerCreated += OnClientHandlerCreated;
        _dncServer.FrameReceived += OnDncFrameReceived;
        _dncServer.LogMessage += OnDncLogMessage;
        _dncServer.StartAsync(_cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
            return;

        _cts?.Cancel();

        // Tear down each side independently. A failure stopping one must not prevent
        // stopping the other, nor skip the final state reset / events below — otherwise
        // the host is left half-stopped (buttons stranded, checkboxes never cleared).
        if (_dncServer != null)
        {
            _dncServer.ClientConnected -= OnDncClientConnected;
            _dncServer.ClientDisconnected -= OnDncClientDisconnected;
            _dncServer.ClientHandlerCreated -= OnClientHandlerCreated;
            _dncServer.FrameReceived -= OnDncFrameReceived;
            _dncServer.LogMessage -= OnDncLogMessage;

            try
            {
                await _dncServer.StopAsync();
                await _dncServer.DisposeAsync();
            }
            catch (Exception ex)
            {
                OnLogMessage($"Error stopping DNC server: {ex.Message}", DnbLogLevel.Error);
            }
            _dncServer = null;
        }

        if (_scadaClient != null)
        {
            _scadaClient.ConnectionChanged -= OnScadaConnectionChanged;
            _scadaClient.DataReceived -= OnScadaDataReceived;
            _scadaClient.ElementsUpdated -= OnScadaElementsUpdated;
            if (_scadaFileLogger != null)
                _scadaClient.DataReceived -= _scadaFileLogger.LogTraffic;

            try
            {
                await _scadaClient.DisconnectAsync();
            }
            catch (Exception ex)
            {
                OnLogMessage($"Error disconnecting SCADA: {ex.Message}", DnbLogLevel.Error);
            }
            _scadaClient = null;
        }
        _replaySource = null;
        _scadaFileLogger?.Dispose();
        _scadaFileLogger = null;

        _session?.Clear();
        _executor = null;

        IsDncConnected = false;
        IsScadaConnected = false;
        IsRunning = false;
        IsRunningChanged?.Invoke(this, new IsRunningEventArgs(false));

        DncConnectionChanged?.Invoke(this, new DncConnectionEventArgs(false));
        ScadaConnectionChanged?.Invoke(this, new ScadaConnectionEventArgs(false));
        OnLogMessage("Engine stopped");

        CloseLogFile();  // TEMPORARY: flush + release the file handle on stop
    }

    private void OnClientHandlerCreated(DncClientHandler handler)
    {
        handler.SetExecutor(_executor!, _session!);
        // TEMPORARY (Main104): let the executor push reverse-Pokes to this DNC client.
        _executor!.SetDncSender(handler.SendAsync);
    }

    private void OnDncClientConnected(object? sender, DncConnectionEventArgs e)
    {
        // Reset session for new connection
        _session?.Clear();

        IsDncConnected = true;
        DncConnectionChanged?.Invoke(this, e);
    }

    private void OnDncClientDisconnected(object? sender, DncConnectionEventArgs e)
    {
        _session?.Clear();
        _executor?.SetDncSender(null);  // TEMPORARY (Main104): no DNC client to reverse-Poke

        IsDncConnected = false;
        DncConnectionChanged?.Invoke(this, e);
    }

    private void OnScadaConnectionChanged(object? sender, ScadaConnectionEventArgs e)
    {
        IsScadaConnected = e.IsConnected;
        ScadaConnectionChanged?.Invoke(this, e);
    }

    private void OnScadaDataReceived(object? sender, ScadaDataEventArgs e)
    {
        ScadaDataReceived?.Invoke(this, e);
    }

    private void OnScadaElementsUpdated(object? sender, ElementValueChangedEventArgs e)
    {
        // TEMPORARY (Main104): forward SCADA updates of input regulation params to DNC
        // via reverse-Poke (the only channel that reaches DNC's SetParameter).
        if (_session != null)
            _executor?.SendReversePokes(e.Updates, _session);

        ElementValueChanged?.Invoke(this, e);
    }

    private void OnDncFrameReceived(object? sender, CommandReceivedEventArgs e)
    {
        DncCommandReceived?.Invoke(this, e);
    }

    private void OnDncLogMessage(object? sender, LogEventArgs e)
    {
        WriteLogFile(e);  // TEMPORARY: DNC TCP server logs already arrive pre-filtered
        LogMessage?.Invoke(this, e);
    }

    private void OnLogMessage(string message, DnbLogLevel level = DnbLogLevel.Info)
    {
        if ((int)level < Config.LogLevel)
            return;

        var e = new LogEventArgs(message, level);
        WriteLogFile(e);  // TEMPORARY quick file logger
        LogMessage?.Invoke(this, e);
    }

    #region TEMPORARY quick file logger — replace with a production logger later

    /// <summary>Opens the append-only log file if Config.LogFile is set. A bad path is logged but never aborts the engine.</summary>
    private void OpenLogFile()
    {
        if (string.IsNullOrWhiteSpace(Config.LogFile))
            return;

        // Resolve a relative Log_File against the exe directory (not the process working
        // directory) so a copied binary + ini logs predictably regardless of how it is launched.
        var logPath = Path.IsPathRooted(Config.LogFile)
            ? Config.LogFile
            : Path.Combine(AppContext.BaseDirectory, Config.LogFile);

        try
        {
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            lock (_logFileLock)
                _logFile = new StreamWriter(logPath, append: true) { AutoFlush = true };

            LogMessage?.Invoke(this, new LogEventArgs($"Log file opened: {logPath}", DnbLogLevel.Info));
        }
        catch (Exception ex)
        {
            // Surface to the UI/console sink only — the file sink is unavailable.
            LogMessage?.Invoke(this, new LogEventArgs($"Could not open log file \"{logPath}\": {ex.Message}", DnbLogLevel.Warning));
        }
    }

    /// <summary>Appends one line: [yyyy-MM-dd HH:mm:ss.fff] [Level] Message. Thread-safe; no-op if no file is open.</summary>
    private void WriteLogFile(LogEventArgs e)
    {
        lock (_logFileLock)
        {
            if (_logFile == null)
                return;

            try
            {
                _logFile.WriteLine($"[{e.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{e.Level}] {e.Message}");
            }
            catch
            {
                // Never let a logging failure disturb the engine.
            }
        }
    }

    private void CloseLogFile()
    {
        lock (_logFileLock)
        {
            _logFile?.Dispose();
            _logFile = null;
        }
    }

    #endregion

    /// <summary>Load (and inject) a replay snapshot. Also updates <see cref="ReplayFilePath"/>.</summary>
    public void LoadReplayFile(string path)
    {
        ReplayFilePath = path;
        if (_replaySource == null)
        {
            OnLogMessage("Replay: LoadReplayFile ignored — not running in replay mode", DnbLogLevel.Warning);
            return;
        }
        _replaySource.LoadFile(path);
    }

    /// <summary>Re-inject the loaded replay snapshot.</summary>
    public void InjectReplay()
    {
        if (_replaySource == null)
        {
            OnLogMessage("Replay: InjectReplay ignored — not running in replay mode", DnbLogLevel.Warning);
            return;
        }
        _replaySource.Inject();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();
        CloseLogFile();  // TEMPORARY: ensure the file handle is released
    }
}
