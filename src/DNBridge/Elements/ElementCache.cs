using System.Collections.Concurrent;

namespace DNBridge.Elements;

/// <summary>
/// Thread-safe cache of all registered IEC 104 elements.
/// Keyed by 5-byte encoded address (CA + IOA as ulong).
/// </summary>
public class ElementCache
{
    private readonly ConcurrentDictionary<ulong, Element104> _elements = new();

    /// <summary>
    /// Find existing element by address or create a new one.
    /// </summary>
    public Element104 FindOrCreate(ulong address, bool isSetPoint)
    {
        return _elements.GetOrAdd(address, addr => new Element104(addr, isSetPoint));
    }

    /// <summary>
    /// Find element by address. Returns null if not found.
    /// </summary>
    public Element104? Find(ulong address)
    {
        return _elements.TryGetValue(address, out var elem) ? elem : null;
    }

    /// <summary>Read-only view of all cached elements.</summary>
    public IReadOnlyDictionary<ulong, Element104> All => _elements;

    public int Count => _elements.Count;

    public void Clear() => _elements.Clear();
}
