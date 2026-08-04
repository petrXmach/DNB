namespace DNBridge;

/// <summary>
/// Single source of truth for the DNC-tab / GetData / Poke traffic line format — see
/// <see cref="Summary"/> / <see cref="DetailLine"/>. The SCADA-tab format
/// (<see cref="ScadaMaster"/> / <see cref="ScadaDetailLine"/>) is a separate, more
/// compact layout used only by <c>ScadaClient</c>/<c>ReplayScadaClient</c>; the two are
/// intentionally allowed to diverge.
///
/// <para>Master line:  <c>{n} element(s) (COT={cot})</c> — the view prefixes the
/// TypeID / command name, so it is never repeated here.</para>
/// <para>Detail line:  <c>IOA={ioa}; Value={v}; Quality={q}; Time={hh:mm:ss.fff}</c> —
/// fields that do not exist for a given frame are omitted rather than faked.</para>
///
/// <para><b>Addresses are always the decimal IOA</b> (<c>1107</c>), never the dotted
/// <c>CCC.CCC.III.III.III</c> wire form. The CA is suppressed while it equals
/// <see cref="DefaultCa"/> — this deployment is single-CA, so printing it on every line is
/// noise; a CA that is genuinely different (or the GI broadcast CA) still shows, which is
/// exactly when it matters.</para>
///
/// Display-only: nothing here touches the wire format.
/// </summary>
public static class TrafficFormat
{
    /// <summary>The CA every element in this deployment uses; omitted from display.</summary>
    public const int DefaultCa = 1;

    /// <summary>Master line for a frame carrying <paramref name="count"/> information objects.</summary>
    public static string Summary(int count, int ca, object cot) =>
        $"{count} element{(count == 1 ? "" : "s")} ({CaPrefix(ca)}COT={cot})";

    /// <summary>
    /// Decimal IOA of a packed address, e.g. <c>0x00000100_0453</c> → <c>"1107"</c>.
    /// Prefixed with <c>CA=n </c> only when the CA is not <see cref="DefaultCa"/>.
    /// </summary>
    public static string Address(ulong address) =>
        $"{CaPrefix((int)((address >> 24) & 0xFFFF))}IOA={address & 0xFFFFFFUL}";

    /// <summary>"" for the default CA, otherwise "CA=n " ready to prepend.</summary>
    public static string CaPrefix(int ca) => ca == DefaultCa ? "" : $"CA={ca} ";

    /// <summary>
    /// One detail line. <paramref name="quality"/> / <paramref name="timeUtc"/> are optional:
    /// control commands carry a qualifier rather than a quality descriptor, and untimed frames
    /// have no timestamp worth showing. <paramref name="note"/> appends a frame-specific
    /// remark (e.g. "confirmed", "SUPPRESSED (replay)").
    /// </summary>
    public static string DetailLine(
        uint ioa,
        double value,
        uint? quality = null,
        DateTime? timeUtc = null,
        string? note = null)
    {
        var parts = new List<string>(5)
        {
            $"IOA={ioa}",
            $"Value={value:G6}",
        };

        if (quality.HasValue)
            parts.Add($"Quality={FormatQuality(quality.Value)}");

        if (timeUtc.HasValue)
            parts.Add($"Time={ToLocal(timeUtc.Value):HH:mm:ss.fff}");

        if (!string.IsNullOrEmpty(note))
            parts.Add(note);

        return string.Join("; ", parts);
    }

    /// <summary>"OK" for a clear quality descriptor, hex otherwise.</summary>
    public static string FormatQuality(uint quality) => quality == 0 ? "OK" : $"0x{quality:X2}";

    /// <summary>
    /// Traffic timestamps are shown in local time. Cached values are stamped UTC
    /// (<see cref="DateTimeKind.Utc"/>); an Unspecified value is assumed UTC too, since every
    /// producer in the pipeline stores UTC.
    /// </summary>
    private static DateTime ToLocal(DateTime t) =>
        t.Kind == DateTimeKind.Local
            ? t
            : DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime();

    // -------------------------------------------------------------------------
    // SCADA-tab-only compact format. Fields are positional (";"-separated, no labels) —
    // see ScadaDataEventArgs for how these lines reach the WPF traffic view.

    /// <summary>
    /// SCADA-tab master line: <c>R/S; hh:mm:ss; typeId; typeName; N elem.[; CA=n]; COT</c>.
    /// <paramref name="note"/> appends a trailing field (e.g. "REJECTED") for frames that have
    /// no per-object detail row to carry it, such as a negative GI confirmation.
    /// </summary>
    public static string ScadaMaster(
        bool outbound,
        DateTime timestamp,
        int typeId,
        string typeName,
        int count,
        int ca,
        object cot,
        string? note = null)
    {
        string dir = outbound ? "S" : "R";
        string caPart = ca == DefaultCa ? "" : $"; CA={ca}";
        string notePart = string.IsNullOrEmpty(note) ? "" : $"; {note}";
        return $"{dir}; {ToLocal(timestamp):HH:mm:ss}; {typeId}; {typeName}; {count} elem.{caPart}; {cot}{notePart}";
    }

    /// <summary>
    /// SCADA-tab detail row: <c> ; hh:mm:ss; OK/KO/-; {ioa,4}; value[; note]</c>.
    /// <paramref name="quality"/> null → "-" (frame carries a qualifier, not a quality
    /// descriptor); 0 → "OK"; anything else → "KO". Value is rounded to at most 3 decimals.
    /// </summary>
    public static string ScadaDetailLine(
        DateTime timeUtc,
        uint? quality,
        uint ioa,
        double value,
        string? note = null)
    {
        string q = quality is null ? "-" : quality.Value == 0 ? "OK" : "KO";
        string line = $" ; {ToLocal(timeUtc):HH:mm:ss}; {q}; {ioa,4}; {value.ToString("0.###")}";
        return string.IsNullOrEmpty(note) ? line : $"{line}; {note}";
    }
}
