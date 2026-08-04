namespace DNBridge.Scada.Replay;

/// <summary>
/// The replay control surface the host drives (via the engine): load a snapshot file and
/// re-inject the loaded snapshot on demand. Implemented by <see cref="ReplayScadaClient"/>.
/// </summary>
public interface IReplaySource
{
    /// <summary>True once a snapshot has been loaded (whether or not any IOA matched the cache).</summary>
    bool HasSamples { get; }

    /// <summary>Parse a snapshot file, keep its samples, and inject them once.</summary>
    void LoadFile(string path);

    /// <summary>Re-apply the loaded snapshot to the cache with a fresh timestamp.</summary>
    void Inject();
}
