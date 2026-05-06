using System.Buffers.Binary;

namespace Neo.L2;

/// <summary>
/// Canonical encoder/decoder for the 91-byte wire format that
/// <c>NeoHub.ChainRegistry.RegisterChain</c> consumes. Pinning the layout off-chain
/// gives the CLI + deploy planner + operator wallets a single source of truth — any
/// future <c>ConfigSize</c> bump in the contract that's not mirrored here surfaces as
/// the "config size mismatch" assertion at registration time, not as a silent
/// mis-parse later.
/// </summary>
/// <remarks>
/// See doc.md §3.2 (ChainRegistry) + §16.2 (security label).
///
/// Layout (91 bytes total — must match
/// <c>NeoHub.ChainRegistry.ChainRegistryContract.ConfigSize</c>):
/// <list type="table">
///   <item><description>0..3   — chainId (4B little-endian uint)</description></item>
///   <item><description>4..23  — operatorManager (20B UInt160)</description></item>
///   <item><description>24..43 — verifier (20B UInt160)</description></item>
///   <item><description>44..63 — bridgeAdapter (20B UInt160)</description></item>
///   <item><description>64..83 — messageAdapter (20B UInt160)</description></item>
///   <item><description>84     — securityLevel (1B)</description></item>
///   <item><description>85     — daMode (1B)</description></item>
///   <item><description>86     — gatewayEnabled (1B bool)</description></item>
///   <item><description>87     — permissionlessExit (1B bool)</description></item>
///   <item><description>88     — sequencerModel (1B)</description></item>
///   <item><description>89     — exitModel (1B)</description></item>
///   <item><description>90     — active (1B bool)</description></item>
/// </list>
/// </remarks>
public static class L2ChainConfigSerializer
{
    /// <summary>
    /// On-chain <c>ChainRegistry.ConfigSize</c> mirror. Off-chain encoders + on-chain
    /// parser must agree on this value byte-for-byte.
    /// </summary>
    public const int ConfigSize = 4 + 20 * 4 + 7;

    private const int OffsetChainId = 0;
    private const int OffsetOperator = 4;
    private const int OffsetVerifier = 24;
    private const int OffsetBridge = 44;
    private const int OffsetMessage = 64;
    private const int OffsetSecurityLevel = 84;
    private const int OffsetDAMode = 85;
    private const int OffsetGatewayEnabled = 86;
    private const int OffsetPermissionlessExit = 87;
    private const int OffsetSequencerModel = 88;
    private const int OffsetExitModel = 89;
    private const int OffsetActive = 90;

    /// <summary>
    /// Encode an <see cref="L2ChainConfig"/> into the 91-byte wire format
    /// <c>ChainRegistry.RegisterChain</c> expects as its <c>configBytes</c> argument.
    /// </summary>
    public static byte[] Encode(L2ChainConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        // UInt160 is a reference type — `required` enforces "must be set," not non-null.
        // Same defense-in-depth pattern as DepositPayload.
        ArgumentNullException.ThrowIfNull(config.OperatorManager);
        ArgumentNullException.ThrowIfNull(config.Verifier);
        ArgumentNullException.ThrowIfNull(config.BridgeAdapter);
        ArgumentNullException.ThrowIfNull(config.MessageAdapter);

        var bytes = new byte[ConfigSize];
        var span = bytes.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OffsetChainId, 4), config.ChainId);
        config.OperatorManager.GetSpan().CopyTo(span.Slice(OffsetOperator, 20));
        config.Verifier.GetSpan().CopyTo(span.Slice(OffsetVerifier, 20));
        config.BridgeAdapter.GetSpan().CopyTo(span.Slice(OffsetBridge, 20));
        config.MessageAdapter.GetSpan().CopyTo(span.Slice(OffsetMessage, 20));

        bytes[OffsetSecurityLevel] = (byte)config.SecurityLevel;
        bytes[OffsetDAMode] = (byte)config.DAMode;
        bytes[OffsetGatewayEnabled] = config.GatewayEnabled ? (byte)1 : (byte)0;
        bytes[OffsetPermissionlessExit] = config.PermissionlessExit ? (byte)1 : (byte)0;
        bytes[OffsetSequencerModel] = (byte)config.Sequencer;
        bytes[OffsetExitModel] = (byte)config.Exit;
        bytes[OffsetActive] = config.Active ? (byte)1 : (byte)0;

        return bytes;
    }

    /// <summary>
    /// Decode a 91-byte wire encoding back into an <see cref="L2ChainConfig"/> record.
    /// </summary>
    /// <exception cref="ArgumentException">Length is not exactly <see cref="ConfigSize"/>.</exception>
    public static L2ChainConfig Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ConfigSize)
            throw new ArgumentException(
                $"L2ChainConfig wire format must be exactly {ConfigSize} bytes (got {bytes.Length})",
                nameof(bytes));

        var chainId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(OffsetChainId, 4));
        var op = new UInt160(bytes.Slice(OffsetOperator, 20));
        var verifier = new UInt160(bytes.Slice(OffsetVerifier, 20));
        var bridge = new UInt160(bytes.Slice(OffsetBridge, 20));
        var msg = new UInt160(bytes.Slice(OffsetMessage, 20));

        // Range-check enum bytes so a stored-but-corrupt config doesn't silently
        // round-trip as a `(SecurityLevel)99` cast that misleads downstream code.
        var securityByte = bytes[OffsetSecurityLevel];
        if (securityByte > 4) throw new ArgumentException($"securityLevel byte out of range: {securityByte}");
        var daByte = bytes[OffsetDAMode];
        if (daByte > 3) throw new ArgumentException($"daMode byte out of range: {daByte}");
        var sequencerByte = bytes[OffsetSequencerModel];
        if (sequencerByte > 2) throw new ArgumentException($"sequencerModel byte out of range: {sequencerByte}");
        var exitByte = bytes[OffsetExitModel];
        if (exitByte > 2) throw new ArgumentException($"exitModel byte out of range: {exitByte}");

        return new L2ChainConfig
        {
            ChainId = chainId,
            OperatorManager = op,
            Verifier = verifier,
            BridgeAdapter = bridge,
            MessageAdapter = msg,
            SecurityLevel = (SecurityLevel)securityByte,
            DAMode = (DAMode)daByte,
            GatewayEnabled = bytes[OffsetGatewayEnabled] != 0,
            PermissionlessExit = bytes[OffsetPermissionlessExit] != 0,
            Sequencer = (SequencerModel)sequencerByte,
            Exit = (ExitModel)exitByte,
            Active = bytes[OffsetActive] != 0,
        };
    }
}
