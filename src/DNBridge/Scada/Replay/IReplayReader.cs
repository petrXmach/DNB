using DNBridge.Events;

namespace DNBridge.Scada.Replay;

/// <summary>
/// Reads a replay snapshot file into a list of cache-ready <see cref="ReplaySample"/>s.
///
/// <para>This is the single seam meant to change when the input file format changes: all
/// parsing lives behind this interface, so the injector (<c>ReplayScadaClient.Inject</c>) stays
/// format-agnostic. Implementations should be tolerant — skip lines they cannot parse rather
/// than throwing — and surface problems via the supplied log callback.</para>
/// </summary>
public interface IReplayReader
{
    /// <summary>
    /// Parses <paramref name="path"/> and returns the raw, cache-ready samples. Returns an
    /// empty list (never null) when the file is missing/empty/unparseable; a missing file is
    /// itself logged as a warning.
    /// </summary>
    IReadOnlyList<ReplaySample> Read(string path, Action<string, DnbLogLevel> log);
}
