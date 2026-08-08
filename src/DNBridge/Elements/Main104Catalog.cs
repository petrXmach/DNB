namespace DNBridge.Commands;

/// <summary>
/// Names for the Main104 regulation parameters (bidirectional config/output elements).
/// Single source of truth shared by the core traffic formatter and the WPF host, so the
/// id→name mapping cannot drift between them. Ids are the bare (unflagged) Main104 ids;
/// callers may pass an id with <see cref="ElementStub.Main104Flag"/> still set.
/// </summary>
public static class Main104Catalog
{
    /// <summary>Bare id → (Name, Direction). Inputs 1–7 (SCADA→DNC), outputs 101–106 (DNC→SCADA).</summary>
    public static readonly IReadOnlyDictionary<uint, (string Name, string Direction)> Entries =
        new Dictionary<uint, (string, string)>
        {
            // REMOVED 2026-07-29: Active / Active_ACK no longer wired in DNBridge (see
            // XChng.cfg) — DNCoRS already forces Active=true, calc always on. Unknown ids
            // fall back to "M104_<id>" via TryGetName/Describe below. Uncomment to restore.
            // [1]   = ("Active",     "Input"),
            [2]   = ("RegMode",    "Input"),
            [3]   = ("RegBranch",  "Input"),
            [4]   = ("UNet_max",   "Input"),
            [5]   = ("UNet_min",   "Input"),
            [6]   = ("Qvvn",       "Input"),
            [7]   = ("Q_tor",      "Input"),
            // [101] = ("Active_ACK", "Output"),
            [102] = ("State",      "Output"),
            [103] = ("Q_min",      "Output"),
            [104] = ("Q_max",      "Output"),
            [105] = ("Losses",     "Output"),
            [106] = ("Weak",       "Output"),
        };

    /// <summary>Strip the Main104Flag (if present) to get the bare regulation id.</summary>
    public static uint BareId(uint id) => id & ~ElementStub.Main104Flag;

    /// <summary>
    /// Resolve a (possibly flag-carrying) id to its parameter name. Returns true with the
    /// catalog name when known; false with a "M104_&lt;base&gt;" fallback otherwise.
    /// </summary>
    public static bool TryGetName(uint id, out string name)
    {
        uint bare = BareId(id);
        if (Entries.TryGetValue(bare, out var entry))
        {
            name = entry.Name;
            return true;
        }
        name = $"M104_{bare}";
        return false;
    }

    /// <summary>Name + Direction for a (possibly flagged) id; falls back for unknown ids.</summary>
    public static (string Name, string Direction) Describe(uint id)
    {
        uint bare = BareId(id);
        return Entries.TryGetValue(bare, out var entry) ? entry : ($"M104_{bare}", "—");
    }
}
