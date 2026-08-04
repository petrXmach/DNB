using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DNBridge.Core;
using DNBridge.Events;
using System.Windows.Media;

namespace DNBridge.Wpf;

// ── Simple data classes for DataGrid rows (no INotifyPropertyChanged) ────

public class MonitorElement
{
    public ushort CA          { get; set; }
    public uint   IOA         { get; set; }
    public uint   SchemaId    { get; set; }
    public string TypeName    { get; set; } = "—";
    public string ValueText   { get; set; } = "—";
    public string QualityText { get; set; } = "—";
    public string UpdatedText { get; set; } = "—";
}

public class Main104Element
{
    public string Name        { get; set; } = "—";
    public string Direction   { get; set; } = "—";
    public ushort CA          { get; set; }
    public uint   IOA         { get; set; }
    public uint   SchemaId    { get; set; }
    public string ValueText   { get; set; } = "—";
    public string UpdatedText { get; set; } = "—";
}

public class CommandElement
{
    public ushort CA            { get; set; }
    public uint   IOA           { get; set; }
    public uint   SchemaId      { get; set; }
    public string PokeValueText { get; set; } = "—";
    public string PokeType      { get; set; } = "—";
    public string SentAtText    { get; set; } = "—";
}

// ── MainWindow ──────────────────────────────────────────────────────────

public partial class MainWindow : Window
{
    private readonly DnbEngine _engine;
    private CancellationTokenSource? _cts;

    // ── Throttled text output ────────────────────────────────────────────
    // High-rate engine events (log, DNC traffic, SCADA traffic) enqueue from
    // background threads; a single DispatcherTimer flushes to the UI ~4x/sec.
    // Each TextBox is backed by a bounded rolling buffer so memory and per-flush
    // cost stay constant regardless of incoming message rate — this prevents the
    // UI thread from being saturated (and the window from freezing) under load.
    private const int MaxLogLines = 500;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);

    private readonly ConcurrentQueue<string> _logQueue   = new();
    private readonly ConcurrentQueue<TrafficItem> _dncQueue   = new();
    private readonly ConcurrentQueue<TrafficItem> _scadaQueue = new();
    private readonly Queue<string> _logBuffer = new();
    private readonly DispatcherTimer _flushTimer;

    // Element collections bound to DataGrids
    private readonly ObservableCollection<MonitorElement>  _monitorItems  = new();
    private readonly ObservableCollection<Main104Element>  _main104Items  = new();
    private readonly ObservableCollection<CommandElement>  _commandItems  = new();

    // Address-keyed lookup for fast value updates
    private readonly Dictionary<ulong, MonitorElement>  _monitorByAddress  = new();
    private readonly Dictionary<ulong, Main104Element>  _main104ByAddress  = new();
    private readonly Dictionary<ulong, CommandElement>  _commandByAddress  = new();

    // Named parameters for the Main104 regulation elements are sourced from the shared
    // core catalog (DNBridge.Commands.Main104Catalog) so the two never drift.

    public MainWindow()
    {
        InitializeComponent();

        // Bind DataGrids to collections
        MonitorGrid.ItemsSource   = _monitorItems;
        Main104Grid.ItemsSource   = _main104Items;
        SetpointsGrid.ItemsSource = _commandItems;

        _engine = new DnbEngine();

        _engine.LogMessage            += Engine_LogMessage;
        _engine.IsRunningChanged      += Engine_IsRunningChanged;
        _engine.DncConnectionChanged  += Engine_DncConnectionChanged;
        _engine.ScadaConnectionChanged += Engine_ScadaConnectionChanged;
        _engine.DncCommandReceived    += Engine_DncCommandReceived;
        _engine.ScadaDataReceived     += Engine_ScadaDataReceived;
        _engine.ElementsRegistered    += Engine_ElementsRegistered;
        _engine.ElementValueChanged   += Engine_ElementValueChanged;
        _engine.ElementPokeConfirmed  += Engine_ElementPokeConfirmed;

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = FlushInterval
        };
        _flushTimer.Tick += FlushTimer_Tick;
        _flushTimer.Start();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        StartButton.IsEnabled = false;
        StopButton.IsEnabled  = true;

        // Lock in the data source chosen before Start.
        _engine.ReplayMode = ReplayModeRadio.IsChecked == true;
        _engine.ReplayFilePath = string.IsNullOrWhiteSpace(ReplayFileBox.Text) ? null : ReplayFileBox.Text;

        await _engine.StartAsync(_cts.Token);

        if (!_engine.IsRunning)
        {
            StartButton.IsEnabled = true;
            StopButton.IsEnabled  = false;
        }
        UpdateReplayControls();
    }

    // ── Replay mode controls ────────────────────────────────────────────

    private void ModeRadio_Changed(object sender, RoutedEventArgs e) => UpdateReplayControls();

    private void BrowseReplayButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select replay snapshot",
            Filter = "Snapshot/text files (*.txt)|*.txt|All files (*.*)|*.*",
        };
        const string defaultDir = @"C:\_EGC\logs\scada";
        if (System.IO.Directory.Exists(defaultDir))
            dlg.InitialDirectory = defaultDir;

        if (dlg.ShowDialog(this) != true)
            return;

        ReplayFileBox.Text = dlg.FileName;
        _engine.ReplayFilePath = dlg.FileName;

        // Auto-inject on open when already running in replay mode.
        if (_engine.IsRunning && ReplayModeRadio.IsChecked == true)
            _engine.LoadReplayFile(dlg.FileName);

        UpdateReplayControls();
    }

    private void InjectReplayButton_Click(object sender, RoutedEventArgs e) => _engine.InjectReplay();

    /// <summary>Enables/disables the replay controls for the current running + mode state.</summary>
    private void UpdateReplayControls()
    {
        if (_engine is null) return;   // Checked can fire during InitializeComponent, before _engine exists

        bool running = _engine.IsRunning;
        bool replay  = ReplayModeRadio.IsChecked == true;

        // Source is fixed while running — Stop to change it.
        LiveModeRadio.IsEnabled   = !running;
        ReplayModeRadio.IsEnabled = !running;

        // File box + Browse are available whenever Replay is the chosen source.
        ReplayFileBox.IsEnabled      = replay;
        BrowseReplayButton.IsEnabled = replay;

        // Inject only applies once running in replay mode with a snapshot chosen.
        InjectReplayButton.IsEnabled = running && replay && !string.IsNullOrWhiteSpace(ReplayFileBox.Text);
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        // Disable immediately so a slow shutdown can't be re-triggered.
        StopButton.IsEnabled = false;

        try
        {
            _cts?.Cancel();
            // Run off the UI thread: StopAsync ultimately calls lib60870 Connection.Close(),
            // which blocks on workerThread.Join() while SCADA is connected. Awaiting it on
            // the UI thread would freeze the window.
            await Task.Run(() => _engine.StopAsync());
        }
        finally
        {
            // Always restore the buttons, even if shutdown threw — otherwise the UI
            // is stranded with both buttons disabled.
            StartButton.IsEnabled = true;
        }
    }

    // Open the standalone an3f4w calc-engine test window (non-modal so the monitor stays usable).
    // The window hosts the basic DLL probe + the per-kind calculation tests.
    private void DllTestButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new CalcTestWindow { Owner = this };
        window.Show();
    }

    private void Engine_LogMessage(object? sender, LogEventArgs e)
    {
        // Enqueue only — flushed to the UI by FlushTimer_Tick. Safe from any thread.
        _logQueue.Enqueue(e.ToString());
    }

    // Drains the queued streams into their views at most once per FlushInterval,
    // keeping UI cost constant under load. Log stays a rolling-text box; the two
    // traffic streams feed master/detail views (each self-bounds to its own cap).
    private void FlushTimer_Tick(object? sender, EventArgs e)
    {
        FlushInto(_logQueue, _logBuffer, LogTextBox);

        while (_dncQueue.TryDequeue(out var item))
            DncTrafficView.Append(item);

        while (_scadaQueue.TryDequeue(out var item))
            ScadaTrafficView.Append(item);
    }

    private static void FlushInto(ConcurrentQueue<string> source, Queue<string> buffer, TextBox target)
    {
        bool changed = false;
        while (source.TryDequeue(out var line))
        {
            buffer.Enqueue(line);
            changed = true;
        }

        if (!changed)
            return;

        while (buffer.Count > MaxLogLines)
            buffer.Dequeue();

        target.Text = string.Join(Environment.NewLine, buffer);
        target.CaretIndex = target.Text.Length;
        target.ScrollToEnd();
    }

    private void Engine_IsRunningChanged(object? sender, IsRunningEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            IsRunningCheckBox.IsChecked = e.IsRunning;
            UpdateReplayControls();
        });
    }

    private void Engine_DncConnectionChanged(object? sender, DncConnectionEventArgs e)
    {
        Dispatcher.BeginInvoke(() => IsDncConnectedCheckBox.IsChecked = e.IsConnected);
    }

    private void Engine_ScadaConnectionChanged(object? sender, ScadaConnectionEventArgs e)
    {
        Dispatcher.BeginInvoke(() => IsScadaConnectedCheckBox.IsChecked = e.IsConnected);
    }

    private void Engine_DncCommandReceived(object? sender, CommandReceivedEventArgs e)
    {
        // Summary line for the master list; full detail (with header) for the pane.
        _dncQueue.Enqueue(new TrafficItem
        {
            Summary  = e.ToString(),
            Detail   = BuildDetail(e.ToString(), e.Detail),
            Outbound = e.IsOutbound,
        });
    }

    private void Engine_ScadaDataReceived(object? sender, ScadaDataEventArgs e)
    {
        _scadaQueue.Enqueue(new TrafficItem
        {
            Summary  = e.ToString(),
            Detail   = BuildDetail(e.ToString(), e.Detail),
            Outbound = e.IsOutbound,
        });
    }

    // Detail pane always opens with the timestamped/direction master line, then the payload
    // body beneath it. The body is appended whenever it adds something the master line does
    // not already say — a SINGLE-object frame (one-IOA ASDU, one outbound command) has a
    // one-line body and must not be dropped, which is why this is not a multi-line test.
    private static string BuildDetail(string masterLine, string detail) =>
        string.IsNullOrEmpty(detail) || masterLine.EndsWith(detail, StringComparison.Ordinal)
            ? masterLine
            : $"{masterLine}{Environment.NewLine}{detail}";

    // Registration-time cell text. An element with no value yet (nothing from SCADA and no
    // XChng.cfg default) has UpdatedAt == MinValue and stays blank rather than showing a fake 0.
    private static bool HasValue(ElementInfo el) => el.UpdatedAt != DateTime.MinValue;
    private static string ValueTextOf(ElementInfo el) => HasValue(el) ? el.Value.ToString("G6") : "—";
    private static string QualityTextOf(ElementInfo el) =>
        HasValue(el) ? (el.Quality == 0 ? "OK" : $"0x{el.Quality:X2}") : "—";
    private static string UpdatedTextOf(ElementInfo el) =>
        HasValue(el) ? el.UpdatedAt.ToLocalTime().ToString("HH:mm:ss") : "—";

    private void Engine_ElementsRegistered(object? sender, ElementsRegisteredEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Clear previous registration
            _monitorItems.Clear();
            _main104Items.Clear();
            _commandItems.Clear();
            _monitorByAddress.Clear();
            _main104ByAddress.Clear();
            _commandByAddress.Clear();

            // Populate Monitor elements
            foreach (var el in e.MonitorElements)
            {
                var item = new MonitorElement
                {
                    CA          = el.CA,
                    IOA         = el.IOA,
                    SchemaId    = el.SchemaId,
                    TypeName    = el.Iec104Type == 0 ? "—" : el.Iec104Type.ToString(),
                    ValueText   = ValueTextOf(el),
                    QualityText = QualityTextOf(el),
                    UpdatedText = UpdatedTextOf(el),
                };
                _monitorItems.Add(item);
                _monitorByAddress[el.Address] = item;
            }

            // Populate Main104 regulation elements
            foreach (var el in e.Main104Elements)
            {
                var (name, dir) = DNBridge.Commands.Main104Catalog.Describe(el.SchemaId);

                var item = new Main104Element
                {
                    Name        = name,
                    Direction   = dir,
                    CA          = el.CA,
                    IOA         = el.IOA,
                    SchemaId    = el.SchemaId,
                    ValueText   = ValueTextOf(el),
                    UpdatedText = UpdatedTextOf(el),
                };
                _main104Items.Add(item);
                _main104ByAddress[el.Address] = item;
            }

            // Populate Setpoint / command elements
            foreach (var el in e.CommandElements)
            {
                var item = new CommandElement
                {
                    CA       = el.CA,
                    IOA      = el.IOA,
                    SchemaId = el.SchemaId,
                };
                _commandItems.Add(item);
                _commandByAddress[el.Address] = item;
            }

            // Update tab headers with counts
            MonitorTab.Header   = $"Monitor ({_monitorItems.Count})";
            Main104Tab.Header   = $"Main104 ({_main104Items.Count})";
            SetpointsTab.Header = $"Setpoints ({_commandItems.Count})";
        });
    }

    private void Engine_ElementValueChanged(object? sender, ElementValueChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var u in e.Updates)
            {
                if (_monitorByAddress.TryGetValue(u.Address, out var item))
                {
                    var newItem = new MonitorElement
                    {
                        CA          = item.CA,
                        IOA         = item.IOA,
                        SchemaId    = item.SchemaId,
                        TypeName    = item.TypeName,
                        ValueText   = u.Value.ToString("G6"),
                        QualityText = u.Quality == 0 ? "OK" : $"0x{u.Quality:X2}",
                        UpdatedText = u.UpdatedAt.ToLocalTime().ToString("HH:mm:ss"),
                    };
                    var idx = _monitorItems.IndexOf(item);
                    if (idx >= 0)
                    {
                        _monitorItems[idx] = newItem;
                        _monitorByAddress[u.Address] = newItem;
                    }
                }
                else if (_main104ByAddress.TryGetValue(u.Address, out var m4))
                {
                    var newM4 = new Main104Element
                    {
                        Name        = m4.Name,
                        Direction   = m4.Direction,
                        CA          = m4.CA,
                        IOA         = m4.IOA,
                        SchemaId    = m4.SchemaId,
                        ValueText   = u.Value.ToString("G6"),
                        UpdatedText = u.UpdatedAt.ToLocalTime().ToString("HH:mm:ss"),
                    };
                    var idx = _main104Items.IndexOf(m4);
                    if (idx >= 0)
                    {
                        _main104Items[idx] = newM4;
                        _main104ByAddress[u.Address] = newM4;
                    }
                }
            }
        });
    }

    private void Engine_ElementPokeConfirmed(object? sender, ElementPokeEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_main104ByAddress.TryGetValue(e.Address, out var m4))
            {
                var newM4 = new Main104Element
                {
                    Name        = m4.Name,
                    Direction   = m4.Direction,
                    CA          = m4.CA,
                    IOA         = m4.IOA,
                    SchemaId    = m4.SchemaId,
                    ValueText   = e.Value.ToString("G6"),
                    UpdatedText = e.SentAt.ToString("HH:mm:ss"),
                };
                var idx = _main104Items.IndexOf(m4);
                if (idx >= 0)
                {
                    _main104Items[idx] = newM4;
                    _main104ByAddress[e.Address] = newM4;
                }
            }
            else if (_commandByAddress.TryGetValue(e.Address, out var cmd))
            {
                var newCmd = new CommandElement
                {
                    CA            = cmd.CA,
                    IOA           = cmd.IOA,
                    SchemaId      = cmd.SchemaId,
                    PokeValueText = e.Value.ToString("G6"),
                    PokeType      = e.PokeType,
                    SentAtText    = e.SentAt.ToString("HH:mm:ss"),
                };
                var idx = _commandItems.IndexOf(cmd);
                if (idx >= 0)
                {
                    _commandItems[idx] = newCmd;
                    _commandByAddress[e.Address] = newCmd;
                }
            }
        });
    }

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _flushTimer.Stop();

        _engine.LogMessage            -= Engine_LogMessage;
        _engine.IsRunningChanged      -= Engine_IsRunningChanged;
        _engine.DncConnectionChanged  -= Engine_DncConnectionChanged;
        _engine.ScadaConnectionChanged -= Engine_ScadaConnectionChanged;
        _engine.DncCommandReceived    -= Engine_DncCommandReceived;
        _engine.ScadaDataReceived     -= Engine_ScadaDataReceived;
        _engine.ElementsRegistered    -= Engine_ElementsRegistered;
        _engine.ElementValueChanged   -= Engine_ElementValueChanged;
        _engine.ElementPokeConfirmed  -= Engine_ElementPokeConfirmed;

        _cts?.Cancel();
        await Task.Run(() => _engine.StopAsync());
        _engine.Dispose();
    }
}
