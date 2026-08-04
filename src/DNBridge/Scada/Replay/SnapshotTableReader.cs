using System.Globalization;
using System.Text.RegularExpressions;
using DNBridge.Events;
using lib60870.CS101;

namespace DNBridge.Scada.Replay;

/// <summary>
/// <see cref="IReplayReader"/> for the current <c>*_snapshot_ok.txt</c> dumps, e.g.
/// <c>C:\_EGC\logs\Dumps\0718_125821_snapshot_ok.txt</c>.
///
/// <para>File shape: header/metadata lines start with <c>;</c> or are dashed rules; a column
/// header row <c>name  qty  id  ioaIn  ioaOut  scadaIn  scadaOut  dllIn  dllOut  valid</c>; then
/// one row per measurement. Element names contain single spaces (<c>"Propoj TS D2.2-TS SS"</c>)
/// while columns are separated by runs of 2+ spaces, so rows are split on <c>\s{2,}</c> — that
/// keeps the name intact and makes the first four fields (name, qty, id, ioaIn) positionally
/// stable regardless of the optional <c>ioaOut</c>/<c>scadaOut</c>/<c>dllOut</c> columns.</para>
///
/// <para>The <c>scadaIn</c> column holds the value <b>exactly as SCADA delivered it</b>, before the
/// DLL's <i>nasobitel</i> (multiplier) is applied to derive the SI <c>dllIn</c>/<c>dllOut</c>
/// columns. So <c>scadaIn</c> is the raw magnitude a live SCADA would put on the wire and is used
/// directly — no inverse transformation.</para>
///
/// <para><b>Reading the value — header-anchored, fixed-width.</b> Numbers in the report are
/// right-aligned so a value's last character lines up with the last character of its column
/// header. So the reader locates the <c>scadaIn</c> header once (its end column), then on each
/// data row takes the text up to that column and reads the last whitespace-delimited token
/// (i.e. the value's right edge is the header's right edge; scan left to the first space). This
/// binds to the <i>named</i> column rather than "the first decimal token", so it cannot pick up a
/// neighbouring column, survives column-width changes (the offset is re-derived from the header),
/// and correctly yields "no value" when <c>scadaIn</c> is blank on a row.</para>
/// </summary>
public class SnapshotTableReader : IReplayReader
{
    private const string ScadaInHeader = "scadaIn";
    private static readonly Regex ColumnSplit = new(@"\s{2,}", RegexOptions.Compiled);

    public IReadOnlyList<ReplaySample> Read(string path, Action<string, DnbLogLevel> log)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            log($"Replay: file not found: \"{path}\"", DnbLogLevel.Warning);
            return Array.Empty<ReplaySample>();
        }

        var samples = new List<ReplaySample>();
        int lineNum = 0, skipped = 0;
        int scadaInEnd = -1;   // exclusive char column where the scadaIn value ends (= header end); set from the header row

        foreach (var raw in File.ReadLines(path))
        {
            lineNum++;
            var line = raw.TrimEnd();

            // Metadata (";time=…"), dashed rules, and blanks: ignore.
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('-'))
                continue;

            var parts = ColumnSplit.Split(line.Trim());

            // The column header row — derive the scadaIn column offset from it, then skip it.
            if (scadaInEnd < 0)
            {
                if (parts.Length >= 4 && parts[0].Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    int h = line.IndexOf(ScadaInHeader, StringComparison.OrdinalIgnoreCase);
                    if (h < 0)
                    {
                        log($"Replay: header has no \"{ScadaInHeader}\" column — cannot read values from \"{Path.GetFileName(path)}\"",
                            DnbLogLevel.Warning);
                        return Array.Empty<ReplaySample>();
                    }
                    scadaInEnd = h + ScadaInHeader.Length;   // values right-align to the header's last char
                }
                // Stray pre-header content (e.g. the "Qmin/Qmax/Loss" summary block): skip quietly.
                continue;
            }

            // Data row: [0]=name [1]=qty [2]=id [3]=ioaIn (positionally stable); the value is read by column.
            if (parts.Length < 4)
            {
                log($"Replay: line {lineNum}: too few columns ({parts.Length}) — skipped", DnbLogLevel.Debug);
                skipped++;
                continue;
            }

            string name = parts[0];
            string qty = parts[1];

            if (!uint.TryParse(parts[3].Trim(), out uint ioa))
            {
                log($"Replay: line {lineNum}: invalid ioaIn \"{parts[3]}\" — skipped", DnbLogLevel.Debug);
                skipped++;
                continue;
            }

            // scadaIn value: text up to the header's end column, then the last whitespace-delimited token.
            // A decimal separator is required so a blank scadaIn (which would leave an earlier integer
            // column as the last token) is correctly rejected rather than mistaken for the value.
            double? scadaIn = ReadScadaIn(line, scadaInEnd);
            if (scadaIn is null)
            {
                log($"Replay: line {lineNum} ({name}): no parseable scadaIn value — skipped", DnbLogLevel.Debug);
                skipped++;
                continue;
            }

            // scadaIn is already the raw SCADA magnitude — drop it into the cache as-is.
            byte type = TypeFor(qty);
            samples.Add(new ReplaySample(name, qty, ioa, scadaIn.Value, type));
        }

        if (scadaInEnd < 0)
            log($"Replay: no column header found in \"{Path.GetFileName(path)}\"", DnbLogLevel.Warning);

        log($"Replay: parsed {samples.Count} sample(s) from \"{Path.GetFileName(path)}\"" +
            (skipped > 0 ? $", {skipped} row(s) skipped" : ""),
            samples.Count > 0 ? DnbLogLevel.Info : DnbLogLevel.Warning);

        return samples;
    }

    /// <summary>
    /// Extracts the right-aligned <c>scadaIn</c> value from a data row: take the text up to
    /// <paramref name="scadaInEnd"/> (the header's end column) and read the last whitespace-delimited
    /// token. Returns null when the column is blank or the token is not a decimal number.
    /// </summary>
    private static double? ReadScadaIn(string line, int scadaInEnd)
    {
        string cell = (line.Length >= scadaInEnd ? line[..scadaInEnd] : line).TrimEnd();
        int sp = cell.LastIndexOf(' ');
        string tok = (sp >= 0 ? cell[(sp + 1)..] : cell).Trim();

        // A real scadaIn value always carries a decimal separator; anything else means the cell was
        // blank and the scan landed on an earlier integer column (ioaIn/ioaOut) — reject it.
        if (tok.Length == 0 || (tok.IndexOf(',') < 0 && tok.IndexOf('.') < 0))
            return null;

        return double.TryParse(tok.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v
            : null;
    }

    /// <summary>IEC 104 monitoring type stamped on the element (UI display only; not sent in GetData).</summary>
    private static byte TypeFor(string qty) =>
        qty.Equals("stav", StringComparison.OrdinalIgnoreCase)
            ? (byte)TypeID.M_DP_NA_1   // 3 — double point (switch state)
            : (byte)TypeID.M_ME_NC_1;  // 13 — measured short float (P/Q/U)
}
