using DNBridge.DncServer;

namespace DNBridge.Commands;

/// <summary>
/// Builds the human-readable one-line summary for a Poke frame shown in the DNC Traffic tab.
/// Resolves the element's decimal SCADA IOA and a friendly label (Main104 parameter name, or
/// the decimal schema id for plain setpoints — whose names are not carried on the wire).
/// Display-only; does not touch the wire format.
/// </summary>
public static class PokeTrafficFormatter
{
    /// <summary>
    /// e.g. "Poke: Active [IOA 1103] = Bool(True, COT=3)" (Main104) or
    /// "Poke: id 1687 [IOA 910] = Float(120.4160, COT=3)" (setpoint).
    /// </summary>
    public static string FormatPokeSummary(PokeCommand cmd, DncSession session)
    {
        uint id = cmd.ElementId;

        string label = (id & ElementStub.Main104Flag) != 0
            ? Main104Catalog.Describe(id).Name
            : $"id {id}";

        string ioa = ResolveIoa(id, session) is uint resolved ? resolved.ToString() : "?";

        string values = cmd.Values.Count > 0
            ? string.Join(", ", cmd.Values.Select(v => v.GetDetails()))
            : "none";

        return $"Poke: {label} [IOA {ioa}] = {values}";
    }

    /// <summary>
    /// Resolve the decimal IEC104 IOA for a poked element id. Setpoints and Main104 outputs
    /// live in <see cref="DncSession.CommandElements"/>; Main104 inputs are only recorded in
    /// <see cref="DncSession.Main104Inputs"/> (address → flagged id), so those are reverse-looked-up.
    /// </summary>
    private static uint? ResolveIoa(uint id, DncSession session)
    {
        if (session.CommandElements.TryGetValue(id, out var elem))
            return elem.IOA;

        foreach (var kv in session.Main104Inputs)
        {
            if (kv.Value == id)
                return (uint)(kv.Key & 0xFFFFFF);
        }

        return null;
    }
}
