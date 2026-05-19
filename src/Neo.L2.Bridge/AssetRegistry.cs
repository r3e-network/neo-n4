namespace Neo.L2.Bridge;

/// <summary>
/// L2-side cache of <c>NeoHub.TokenRegistry</c> entries. Lookups are bidirectional —
/// L2 contracts ask "what's the canonical L1 asset for this L2 token", and the deposit
/// processor asks "what's the L2 token for this incoming L1 asset?".
/// </summary>
/// <remarks>
/// The registry is updated either through governance messages (kind <see cref="MessageType.Governance"/>)
/// or by direct sync against L1 state. Production implementations should make this thread-safe;
/// this in-memory variant uses a lock.
/// </remarks>
public sealed class AssetRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(UInt160 L1Asset, uint L2ChainId), AssetMapping> _byL1 = new();
    private readonly Dictionary<UInt160, AssetMapping> _byL2 = new();

    /// <summary>Number of mappings currently registered.</summary>
    public int Count
    {
        get { lock (_gate) return _byL2.Count; }
    }

    /// <summary>Insert or replace a mapping.</summary>
    /// <remarks>
    /// When replacing, both indexes are kept consistent: if the (L1Asset, L2ChainId) slot
    /// previously held a mapping with a *different* L2Asset, the stale `_byL2` entry is
    /// removed. The reverse cleanup handles the symmetric case (same L2Asset, different
    /// L1 key). Without this, a registry that re-points an L2 token at a new L1 asset
    /// would leak orphan entries that the lookups would happily return — a silent
    /// inconsistency.
    /// </remarks>
    public void Register(AssetMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        // Both index keys depend on these UInt160 fields. A null L2Asset throws
        // ArgumentNullException from `_byL2[null]` deep in Dictionary; a null L1Asset
        // creates a tuple key `(null, chainId)` that lookups would interpret oddly.
        // Catch them at the API boundary so the operator sees the bad field directly.
        ArgumentNullException.ThrowIfNull(mapping.L1Asset);
        ArgumentNullException.ThrowIfNull(mapping.L2Asset);
        AssetAmount.ValidateDecimals(mapping.L1Decimals, nameof(mapping.L1Decimals));
        AssetAmount.ValidateDecimals(mapping.L2Decimals, nameof(mapping.L2Decimals));
        lock (_gate)
        {
            var l1Key = (mapping.L1Asset, mapping.L2ChainId);
            if (_byL1.TryGetValue(l1Key, out var oldByL1) && !oldByL1.L2Asset.Equals(mapping.L2Asset))
                _byL2.Remove(oldByL1.L2Asset);
            if (_byL2.TryGetValue(mapping.L2Asset, out var oldByL2)
                && (!oldByL2.L1Asset.Equals(mapping.L1Asset) || oldByL2.L2ChainId != mapping.L2ChainId))
                _byL1.Remove((oldByL2.L1Asset, oldByL2.L2ChainId));

            _byL1[l1Key] = mapping;
            _byL2[mapping.L2Asset] = mapping;
        }
    }

    /// <summary>Look up the mapping for an L1 asset on a given L2 chain.</summary>
    public bool TryGetByL1(UInt160 l1Asset, uint l2ChainId, out AssetMapping? mapping)
    {
        // Surface bad input at the API boundary. Without this, Dictionary<UInt160, T>'s
        // TryGetValue(null) throws ArgumentNullException with a generic "key" message;
        // here the operator sees which arg is wrong. Same iter-148 Register pattern.
        ArgumentNullException.ThrowIfNull(l1Asset);
        lock (_gate)
        {
            if (_byL1.TryGetValue((l1Asset, l2ChainId), out var m))
            {
                mapping = m;
                return true;
            }
            mapping = null;
            return false;
        }
    }

    /// <summary>Look up the mapping for a known L2 asset.</summary>
    public bool TryGetByL2(UInt160 l2Asset, out AssetMapping? mapping)
    {
        ArgumentNullException.ThrowIfNull(l2Asset);
        lock (_gate)
        {
            if (_byL2.TryGetValue(l2Asset, out var m))
            {
                mapping = m;
                return true;
            }
            mapping = null;
            return false;
        }
    }

    /// <summary>Mark an existing mapping active or inactive (governance pause).</summary>
    public bool SetActive(UInt160 l2Asset, bool active)
    {
        ArgumentNullException.ThrowIfNull(l2Asset);
        lock (_gate)
        {
            if (!_byL2.TryGetValue(l2Asset, out var existing)) return false;
            var updated = existing with { Active = active };
            _byL2[l2Asset] = updated;
            _byL1[(existing.L1Asset, existing.L2ChainId)] = updated;
            return true;
        }
    }

    /// <summary>Snapshot of all currently registered mappings.</summary>
    public IReadOnlyList<AssetMapping> Snapshot()
    {
        lock (_gate)
            return _byL2.Values.ToArray();
    }
}
