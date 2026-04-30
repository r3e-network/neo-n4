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
    public void Register(AssetMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        lock (_gate)
        {
            _byL1[(mapping.L1Asset, mapping.L2ChainId)] = mapping;
            _byL2[mapping.L2Asset] = mapping;
        }
    }

    /// <summary>Look up the mapping for an L1 asset on a given L2 chain.</summary>
    public bool TryGetByL1(UInt160 l1Asset, uint l2ChainId, out AssetMapping? mapping)
    {
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
