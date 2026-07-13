using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.SequencerRegistry;

/// <summary>
/// Per-chain registry of dBFT sequencer pubkeys. Integrates with <c>NeoHub.SequencerBond</c>
/// for the min-bond eligibility gate and supports an exit window before unregister takes
/// effect. See doc.md §7.1 (sequencer / dBFT committee) and §16 (governance).
/// </summary>
[DisplayName("NeoHub.SequencerRegistry")]
[ContractAuthor("R3E Network", "dev@r3e.network")]
[ContractDescription("Per-chain dBFT sequencer pubkey registry for Neo Elastic Network L2s.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SequencerRegistry")]
[ContractPermission(Permission.Any, Method.Any)]
public class SequencerRegistryContract : SmartContract
{
    private const byte PrefixSequencer = 0x01;       // 0x01 + chainId(4B) + pubKey(33B) → encoded entry
    private const byte PrefixCount = 0x02;            // 0x02 + chainId(4B) → BigInteger active count
    private const byte KeyMaxCommitteeSize = 0x03;
    private const byte KeyExitWindowSeconds = 0x04;
    private const byte KeyBondContract = 0xFD;
    private const byte KeyOwner = 0xFF;

    /// <summary>Default max committee size (matches Neo's typical 7-of-21 ratio).</summary>
    public const byte DefaultMaxCommitteeSize = 21;

    /// <summary>Default exit window — sequencer keeps signing for this long after Unregister.</summary>
    public const uint DefaultExitWindowSeconds = 86400;  // 24 hours

    /// <summary>Status byte values stored in the per-sequencer entry.</summary>
    public const byte StatusActive = 1;
    public const byte StatusExiting = 2;

    /// <summary>Emitted when a sequencer is registered.</summary>
    [DisplayName("SequencerRegistered")]
    public static event Action<uint, ECPoint> OnSequencerRegistered = default!;

    /// <summary>Emitted when a sequencer initiates exit (status → Exiting).</summary>
    [DisplayName("SequencerExiting")]
    public static event Action<uint, ECPoint, uint> OnSequencerExiting = default!;

    /// <summary>Emitted when a sequencer's exit window completes and they're removed.</summary>
    [DisplayName("SequencerRemoved")]
    public static event Action<uint, ECPoint> OnSequencerRemoved = default!;

    /// <summary>Emitted when MaxCommitteeSize is changed.</summary>
    [DisplayName("MaxCommitteeSizeChanged")]
    public static event Action<byte, byte> OnMaxCommitteeSizeChanged = default!;

    /// <summary>Emitted when ExitWindowSeconds is changed.</summary>
    [DisplayName("ExitWindowSecondsChanged")]
    public static event Action<uint, uint> OnExitWindowSecondsChanged = default!;

    /// <summary>Emitted when ownership is transferred.</summary>
    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

    /// <summary>Set wiring at deploy time.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var bondContract = (UInt160)arr[1];
        // Without these guards a bondContract=0 would silently let unbonded sequencers
        // register — the worst kind of "looks deployed but actually broken" outcome
        // for a Phase-3 bonded committee.
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(bondContract.IsValid && !bondContract.IsZero, "invalid bond contract");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyBondContract }, bondContract);
        Storage.Put(new byte[] { KeyMaxCommitteeSize }, new byte[] { DefaultMaxCommitteeSize });
        Storage.Put(new byte[] { KeyExitWindowSeconds }, (BigInteger)DefaultExitWindowSeconds);
    }

    /// <summary>Governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Transfer governance ownership. Owner only.</summary>
    public static void SetOwner(UInt160 newOwner)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(newOwner.IsValid && !newOwner.IsZero, "invalid new owner");
        var oldOwner = GetOwner();
        Storage.Put(new byte[] { KeyOwner }, newOwner);
        OnOwnerChanged(oldOwner, newOwner);
    }

    /// <summary>Maximum allowed committee size per chain.</summary>
    [Safe]
    public static byte GetMaxCommitteeSize()
    {
        var raw = Storage.Get(new byte[] { KeyMaxCommitteeSize });
        return raw == null ? DefaultMaxCommitteeSize : ((byte[])raw)[0];
    }

    /// <summary>Configured exit window in seconds.</summary>
    [Safe]
    public static uint GetExitWindowSeconds()
    {
        var raw = Storage.Get(new byte[] { KeyExitWindowSeconds });
        return raw == null ? DefaultExitWindowSeconds : (uint)(BigInteger)raw;
    }

    /// <summary>Update the max committee size. Owner only.</summary>
    public static void SetMaxCommitteeSize(byte size)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(size >= 1 && size <= 64, "size must be in [1, 64]");
        var old = GetMaxCommitteeSize();
        Storage.Put(new byte[] { KeyMaxCommitteeSize }, new byte[] { size });
        OnMaxCommitteeSizeChanged(old, size);
    }

    /// <summary>Update the exit window. Owner only.</summary>
    public static void SetExitWindowSeconds(uint seconds)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(seconds >= 60 && seconds <= 7 * 86400, "exit window out of bounds [60s, 7d]");
        var old = GetExitWindowSeconds();
        Storage.Put(new byte[] { KeyExitWindowSeconds }, (BigInteger)seconds);
        OnExitWindowSecondsChanged(old, seconds);
    }

    /// <summary>
    /// Register <paramref name="sequencerKey"/> for <paramref name="chainId"/>. The caller must
    /// hold a witness for the sequencer key, AND <c>SequencerBond.HasMinBond(chainId, sequencer)</c>
    /// must be true. Cannot exceed <see cref="GetMaxCommitteeSize"/>.
    /// </summary>
    public static void Register(uint chainId, ECPoint sequencerKey, UInt160 sequencerAddress)
    {
        // chainId 0 is the L1 sentinel; a registration there is meaningless because no
        // L2 with chainId=0 exists. Reject at the source so storage stays clean.
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(Runtime.CheckWitness(sequencerKey), "no witness for sequencer key");
        ExecutionEngine.Assert(sequencerAddress.IsValid && !sequencerAddress.IsZero, "invalid sequencer address");
        // Bind the consensus key to the bond/payout address by requiring a witness for BOTH. Without
        // this, an operator controlling only sequencerKey could attach it to someone else's bonded
        // address, so that victim's bond would be slashed for the operator's misbehaviour.
        ExecutionEngine.Assert(Runtime.CheckWitness(sequencerAddress), "no witness for sequencer address");

        // Min-bond gate via inter-contract call.
        var bondContract = (UInt160)(Storage.Get(new byte[] { KeyBondContract }) ?? throw new Exception("bond contract unset"));
        var hasBond = (bool)Contract.Call(bondContract, "hasMinBond", CallFlags.ReadOnly,
            new object[] { chainId, sequencerAddress });
        ExecutionEngine.Assert(hasBond, "insufficient bond");

        var key = SequencerKey(chainId, sequencerKey);
        ExecutionEngine.Assert(Storage.Get(key) == null, "already registered");

        // Max committee size gate.
        var current = GetActiveCount(chainId);
        ExecutionEngine.Assert(current < GetMaxCommitteeSize(), "committee full");

        // Encode entry: 1B status + 20B sequencerAddress + 4B exitsAtUnix (0 when Active).
        var entry = new byte[1 + 20 + 4];
        entry[0] = StatusActive;
        var addrBytes = (byte[])sequencerAddress;
        for (var i = 0; i < 20; i++) entry[1 + i] = addrBytes[i];
        // exitsAtUnix stays zero.

        Storage.Put(key, entry);
        SetActiveCount(chainId, current + 1);

        OnSequencerRegistered(chainId, sequencerKey);
    }

    /// <summary>
    /// Initiate exit for <paramref name="sequencerKey"/>. Records the unix timestamp at which the
    /// exit window completes; until then the sequencer is still active and signing.
    /// </summary>
    public static uint Unregister(uint chainId, ECPoint sequencerKey)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(sequencerKey), "not authorized");
        var key = SequencerKey(chainId, sequencerKey);
        var raw = Storage.Get(key);
        ExecutionEngine.Assert(raw != null, "not registered");

        var src = (byte[])raw!;
        ExecutionEngine.Assert(src[0] == StatusActive, "not currently active");

        var exitsAt = (uint)(Runtime.Time / 1000) + GetExitWindowSeconds();
        // src is a NeoVM ByteString (immutable) — index-assigning it FAULTs at runtime, which would
        // make sequencer exit impossible. Copy into a fresh mutable buffer (1B status + 20B address
        // + 4B exitsAt) and write the new status + exit timestamp there.
        var entry = new byte[1 + 20 + 4];
        for (var i = 0; i < entry.Length; i++) entry[i] = src[i];
        entry[0] = StatusExiting;
        entry[21] = (byte)exitsAt;
        entry[22] = (byte)(exitsAt >> 8);
        entry[23] = (byte)(exitsAt >> 16);
        entry[24] = (byte)(exitsAt >> 24);
        Storage.Put(key, entry);

        OnSequencerExiting(chainId, sequencerKey, exitsAt);
        return exitsAt;
    }

    /// <summary>
    /// Once an exiting sequencer's window has elapsed, anyone may call to remove them from the
    /// registry. Counts toward decrementing the active count.
    /// </summary>
    public static void Finalize(uint chainId, ECPoint sequencerKey)
    {
        var key = SequencerKey(chainId, sequencerKey);
        var raw = Storage.Get(key);
        ExecutionEngine.Assert(raw != null, "not registered");
        var entry = (byte[])raw!;
        ExecutionEngine.Assert(entry[0] == StatusExiting, "not exiting");

        var exitsAt = (uint)entry[21] | ((uint)entry[22] << 8) | ((uint)entry[23] << 16) | ((uint)entry[24] << 24);
        ExecutionEngine.Assert((uint)(Runtime.Time / 1000) >= exitsAt, "exit window still open");

        Storage.Delete(key);
        SetActiveCount(chainId, GetActiveCount(chainId) - 1);
        OnSequencerRemoved(chainId, sequencerKey);
    }

    /// <summary>Number of currently registered (Active or Exiting) sequencers on a chain.</summary>
    [Safe]
    public static uint GetActiveCount(uint chainId)
    {
        var raw = Storage.Get(CountKey(chainId));
        return raw == null ? 0u : (uint)(BigInteger)raw;
    }

    /// <summary>True if the sequencer is currently registered (any status).</summary>
    [Safe]
    public static bool IsRegistered(uint chainId, ECPoint sequencerKey)
    {
        return Storage.Get(SequencerKey(chainId, sequencerKey)) != null;
    }

    /// <summary>Status byte for a registered sequencer (or 0 if not present).</summary>
    [Safe]
    public static byte GetStatus(uint chainId, ECPoint sequencerKey)
    {
        var raw = Storage.Get(SequencerKey(chainId, sequencerKey));
        return raw == null ? (byte)0 : ((byte[])raw)[0];
    }

    /// <summary>Read the L1 address tied to a sequencer's pubkey (or zero).</summary>
    [Safe]
    public static UInt160 GetSequencerAddress(uint chainId, ECPoint sequencerKey)
    {
        var raw = Storage.Get(SequencerKey(chainId, sequencerKey));
        if (raw == null) return UInt160.Zero;
        var bytes = (byte[])raw;
        var addr = new byte[20];
        for (var i = 0; i < 20; i++) addr[i] = bytes[1 + i];
        return (UInt160)addr;
    }

    private static void SetActiveCount(uint chainId, uint count)
    {
        Storage.Put(CountKey(chainId), (BigInteger)count);
    }

    private static byte[] SequencerKey(uint chainId, ECPoint sequencerKey)
    {
        var k = new byte[1 + 4 + 33];
        k[0] = PrefixSequencer;
        k[1] = (byte)chainId; k[2] = (byte)(chainId >> 8); k[3] = (byte)(chainId >> 16); k[4] = (byte)(chainId >> 24);
        var pk = (byte[])sequencerKey;
        for (var i = 0; i < 33; i++) k[5 + i] = pk[i];
        return k;
    }

    private static byte[] CountKey(uint chainId)
    {
        var k = new byte[5];
        k[0] = PrefixCount;
        k[1] = (byte)chainId; k[2] = (byte)(chainId >> 8); k[3] = (byte)(chainId >> 16); k[4] = (byte)(chainId >> 24);
        return k;
    }
}
