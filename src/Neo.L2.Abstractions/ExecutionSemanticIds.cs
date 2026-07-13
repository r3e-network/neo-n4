using System.Text;
using Neo.Cryptography;

namespace Neo.L2;

/// <summary>Versioned identifiers for execution semantics that a ZK path must not mix.</summary>
/// <remarks>See doc.md §8.1–§8.2 and §17.1.</remarks>
public static class ExecutionSemanticIds
{
    /// <summary>Fallback semantic for compatibility executors without explicit metadata.</summary>
    public static UInt256 UnspecifiedV1 { get; } = FromName("neo-l2/unspecified-execution/v1");

    /// <summary>Reference no-op transaction semantics.</summary>
    public static UInt256 ReferenceNoOpV1 { get; } = FromName("neo-l2/reference-noop/v1");

    /// <summary>Legacy Neo N3 ApplicationEngine compatibility semantics.</summary>
    public static UInt256 NeoN3ApplicationEngineV1 { get; } = FromName("neo-l2/neo-n3-application-engine/v1");

    /// <summary>Stateless PolkaVM host preview semantics.</summary>
    public static UInt256 NeoVm2PolkaVmStatelessPreviewV1 { get; } = FromName("neo-l2/neovm2-polkavm-stateless-preview/v1");

    /// <summary>Legacy Neo N3 VM semantics used by the current SP1 compatibility guest.</summary>
    public static UInt256 Sp1LegacyNeoN3GuestV1 { get; } = FromName("neo-l2/sp1-legacy-neo-n3-guest/v1");

    /// <summary>Derive a stable Hash256 identifier from an ASCII semantic name.</summary>
    public static UInt256 FromName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new UInt256(Crypto.Hash256(Encoding.ASCII.GetBytes(name)));
    }
}
