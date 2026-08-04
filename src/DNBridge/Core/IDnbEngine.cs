using DNBridge.Elements;
using DNBridge.Events;

namespace DNBridge.Core;

public interface IDnbEngine : IDisposable
{
    // --- Lifecycle ---
    Task StartAsync(CancellationToken ct);
    Task StopAsync();

    // --- Status ---
    bool IsRunning { get; }
    bool IsDncConnected { get; }
    bool IsScadaConnected { get; }

    // --- Config ---
    DnbConfig Config { get; }

    // --- Replay mode (offline SCADA simulation) ---
    /// <summary>When true, Start uses a replay source instead of the live SCADA client. Set before Start.</summary>
    bool ReplayMode { get; set; }
    /// <summary>Snapshot file to replay. Set before Start, or via <see cref="LoadReplayFile"/> while running.</summary>
    string? ReplayFilePath { get; set; }
    /// <summary>Load (and inject) a snapshot file. No-op with a warning unless running in replay mode.</summary>
    void LoadReplayFile(string path);
    /// <summary>Re-inject the loaded snapshot. No-op with a warning unless running in replay mode.</summary>
    void InjectReplay();

    // --- Read-only view of element cache (for UI) ---
    IReadOnlyDictionary<ulong, Element104> Elements { get; }

    // --- Events raised toward host ---
    event EventHandler<LogEventArgs> LogMessage;
    event EventHandler<IsRunningEventArgs> IsRunningChanged;
    event EventHandler<DncConnectionEventArgs> DncConnectionChanged;
    event EventHandler<ScadaConnectionEventArgs> ScadaConnectionChanged;
    event EventHandler<CommandReceivedEventArgs> DncCommandReceived;
    event EventHandler<ScadaDataEventArgs> ScadaDataReceived;

    // --- Element cache events ---
    event EventHandler<ElementsRegisteredEventArgs> ElementsRegistered;
    event EventHandler<ElementValueChangedEventArgs> ElementValueChanged;
    event EventHandler<ElementPokeEventArgs> ElementPokeConfirmed;
}
