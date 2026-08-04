namespace DNBridge.Scada.Replay;

/// <summary>
/// One measurement parsed from a replay snapshot file — the stable contract between the
/// (swappable) <see cref="IReplayReader"/> and the injector in <c>ReplayScadaClient</c>.
///
/// <para><b>Value is the RAW SCADA magnitude</b> (the file's <c>scadaIn</c> column, taken as-is by
/// the reader), i.e. exactly what a live SCADA server would put on the wire, so the injector can
/// drop it straight into the cache and DNC's downstream SI scaling stays correct. Keeping all
/// parsing inside the reader means a new file format only reimplements
/// <see cref="IReplayReader"/> — this record and the injector never change.</para>
/// </summary>
/// <param name="Name">Element name from the schema (display/logging only).</param>
/// <param name="Qty">Measured quantity tag from the file: <c>P</c>, <c>Q</c>, <c>U</c>, or <c>stav</c>.</param>
/// <param name="Ioa">SCADA information-object address (the file's <c>ioaIn</c>). CA is fixed at 1.</param>
/// <param name="Value">Raw, cache-ready value (the file's <c>scadaIn</c> column, as SCADA delivered it).</param>
/// <param name="Iec104Type">IEC 104 monitoring TypeID to stamp on the cached element (UI only).</param>
public record ReplaySample(string Name, string Qty, uint Ioa, double Value, byte Iec104Type);
