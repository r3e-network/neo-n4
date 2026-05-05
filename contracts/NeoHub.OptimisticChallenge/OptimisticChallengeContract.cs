using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.OptimisticChallenge;

/// <summary>
/// Phase-3 optimistic-rollup challenge window. When a batch is submitted with
/// <c>ProofType.Optimistic</c>, <see cref="NeoHub.SettlementManager"/> places it in
/// <c>Challengeable</c> status; for the duration of the configured window, anyone may submit
/// a fraud proof here. If accepted, this contract calls SettlementManager.RevertBatch and
/// SequencerBond.Slash and pays the challenger.
/// </summary>
/// <remarks>
/// See doc.md §15 (key flows), §17 (sequencer-fraud mitigations), §18 Phase 3.
/// </remarks>
[DisplayName("NeoHub.OptimisticChallenge")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Phase-3 optimistic-rollup challenge window for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/neo-project/neo4/tree/master/contracts/NeoHub.OptimisticChallenge")]
[ContractPermission(Permission.Any, Method.Any)]
public class OptimisticChallengeContract : SmartContract
{
    private const byte PrefixDeadline = 0x01;        // 0x01 + chainId(4B) + batchNum(8B) → 4B unix-sec deadline
    private const byte PrefixAcceptedFraud = 0x02;   // 0x02 + chainId(4B) + batchNum(8B) → reporter address
    private const byte PrefixSequencer = 0x03;       // 0x03 + chainId(4B) + batchNum(8B) → 20B sequencer address
    private const byte KeyChallengeWindowSeconds = 0x04;
    private const byte KeyChallengerRewardBps = 0x05;
    private const byte KeySettlementManager = 0xFC;
    private const byte KeySequencerBond = 0xFD;
    private const byte KeyOwner = 0xFF;

    /// <summary>Default challenge window — 1 hour.</summary>
    public const uint DefaultWindowSeconds = 3600;

    /// <summary>Default challenger reward — 50% of slashed bond, in basis points.</summary>
    public const ushort DefaultChallengerRewardBps = 5000;

    /// <summary>Total basis points in a slash split.</summary>
    public const ushort BasisPointsTotal = 10_000;

    /// <summary>Emitted when SettlementManager opens the challenge window for a batch.</summary>
    [DisplayName("WindowOpened")]
    public static event Action<uint, ulong, uint, UInt160> OnWindowOpened = default!;

    /// <summary>Emitted on a successful fraud-proof submission.</summary>
    [DisplayName("ChallengeAccepted")]
    public static event Action<uint, ulong, UInt160, BigInteger> OnChallengeAccepted = default!;

    /// <summary>Emitted when the window expires unchallenged.</summary>
    [DisplayName("WindowFinalized")]
    public static event Action<uint, ulong> OnWindowFinalized = default!;

    /// <summary>Set wiring on deploy. <c>data</c> = [owner, settlementManager, sequencerBond].</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var settlementManager = (UInt160)arr[1];
        var sequencerBond = (UInt160)arr[2];
        // Surface typo'd zero / invalid hashes here. Without these guards an opChallenge
        // with sequencerBond=0 would silently fail to slash on a successful challenge —
        // the worst kind of "looks deployed but actually broken" outcome.
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(settlementManager.IsValid && !settlementManager.IsZero, "invalid settlement manager");
        ExecutionEngine.Assert(sequencerBond.IsValid && !sequencerBond.IsZero, "invalid sequencer bond");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeySettlementManager }, settlementManager);
        Storage.Put(new byte[] { KeySequencerBond }, sequencerBond);
        Storage.Put(new byte[] { KeyChallengeWindowSeconds }, (BigInteger)DefaultWindowSeconds);
        Storage.Put(new byte[] { KeyChallengerRewardBps }, (BigInteger)DefaultChallengerRewardBps);
    }

    /// <summary>Governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Configured challenge window in seconds.</summary>
    [Safe]
    public static uint GetWindowSeconds()
    {
        var raw = Storage.Get(new byte[] { KeyChallengeWindowSeconds });
        return raw == null ? DefaultWindowSeconds : (uint)(BigInteger)raw;
    }

    /// <summary>Configured challenger reward (basis points of the slash payout).</summary>
    [Safe]
    public static ushort GetChallengerRewardBps()
    {
        var raw = Storage.Get(new byte[] { KeyChallengerRewardBps });
        return raw == null ? DefaultChallengerRewardBps : (ushort)(uint)(BigInteger)raw;
    }

    /// <summary>Update challenge window. Owner only.</summary>
    public static void SetWindowSeconds(uint seconds)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(seconds >= 60 && seconds <= 7 * 86400, "window out of bounds [60s, 7d]");
        Storage.Put(new byte[] { KeyChallengeWindowSeconds }, (BigInteger)seconds);
    }

    /// <summary>Update challenger reward. Owner only.</summary>
    public static void SetChallengerRewardBps(ushort bps)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(bps > 0 && bps <= BasisPointsTotal, "bps out of (0, 10000]");
        Storage.Put(new byte[] { KeyChallengerRewardBps }, (BigInteger)bps);
    }

    /// <summary>
    /// SettlementManager calls this when a batch lands with <c>ProofType.Optimistic</c>.
    /// Records the deadline + responsible sequencer for later challenge dispatch.
    /// </summary>
    public static uint OpenWindow(uint chainId, ulong batchNumber, UInt160 sequencer)
    {
        var sm = (UInt160)(Storage.Get(new byte[] { KeySettlementManager }) ?? throw new Exception("sm unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(sm), "not settlement manager");
        // chainId 0 is the L1 sentinel; would be meaningless. sequencer=0 means a
        // successful challenge later would fail to slash any actual sequencer — the
        // economic security of Phase-3 depends on a valid sequencer hash here.
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(sequencer.IsValid && !sequencer.IsZero, "invalid sequencer");

        var deadline = (uint)(Runtime.Time / 1000) + GetWindowSeconds();
        var deadlineKey = DeadlineKey(chainId, batchNumber);
        ExecutionEngine.Assert(Storage.Get(deadlineKey) == null, "window already open");
        Storage.Put(deadlineKey, EncodeUInt32(deadline));
        Storage.Put(SequencerKey(chainId, batchNumber), sequencer);

        OnWindowOpened(chainId, batchNumber, deadline, sequencer);
        return deadline;
    }

    /// <summary>
    /// Submit a fraud proof against a pending optimistic batch. The on-chain check is shallow
    /// (the proof bytes' shape — that's all storage knows about); the actual cryptographic
    /// validation is a verifier-contract call.
    /// </summary>
    /// <param name="fraudVerifier">Address of the contract that decodes + verifies the proof.</param>
    public static void Challenge(uint chainId, ulong batchNumber, UInt160 challenger, byte[] fraudProofBytes, UInt160 fraudVerifier)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(challenger), "no witness for challenger");
        ExecutionEngine.Assert(fraudProofBytes.Length > 0, "empty fraud proof");
        // Without these guards, a zero challenger would reach the bond payout step
        // and silently pay the reward to address 0; a zero fraudVerifier would NRE
        // inside Contract.Call instead of surfacing the misconfig clearly.
        ExecutionEngine.Assert(challenger.IsValid && !challenger.IsZero, "invalid challenger");
        ExecutionEngine.Assert(fraudVerifier.IsValid && !fraudVerifier.IsZero, "invalid fraud verifier");

        var deadlineKey = DeadlineKey(chainId, batchNumber);
        var rawDeadline = Storage.Get(deadlineKey);
        ExecutionEngine.Assert(rawDeadline != null, "no open window");
        var deadline = DecodeUInt32((byte[])rawDeadline!);
        ExecutionEngine.Assert((uint)(Runtime.Time / 1000) <= deadline, "challenge window closed");

        ExecutionEngine.Assert(Storage.Get(AcceptedFraudKey(chainId, batchNumber)) == null, "already accepted");

        // Hand off proof verification.
        var verified = (bool)Contract.Call(fraudVerifier, "verifyFraud", CallFlags.ReadOnly,
            new object[] { chainId, batchNumber, fraudProofBytes });
        ExecutionEngine.Assert(verified, "fraud proof rejected");

        // Read sequencer + slash via SequencerBond.
        var sequencerRaw = Storage.Get(SequencerKey(chainId, batchNumber));
        ExecutionEngine.Assert(sequencerRaw != null, "no recorded sequencer");
        var sequencer = (UInt160)sequencerRaw!;

        var bondContract = (UInt160)(Storage.Get(new byte[] { KeySequencerBond }) ?? throw new Exception("bond unset"));
        // Slash the entire current bond and split per challenger reward.
        var bondBalance = (BigInteger)Contract.Call(bondContract, "getBalance", CallFlags.ReadOnly,
            new object[] { chainId, sequencer });
        ExecutionEngine.Assert(bondBalance > 0, "no bond to slash");

        var rewardBps = GetChallengerRewardBps();
        var challengerCut = bondBalance * rewardBps / BasisPointsTotal;

        // Pay challenger first.
        Contract.Call(bondContract, "slash", CallFlags.All,
            new object[] { chainId, sequencer, challengerCut, challenger });
        // Burn (or treasury) the rest.
        var remaining = bondBalance - challengerCut;
        if (remaining > 0)
        {
            Contract.Call(bondContract, "slash", CallFlags.All,
                new object[] { chainId, sequencer, remaining, UInt160.Zero });
        }

        Storage.Put(AcceptedFraudKey(chainId, batchNumber), challenger);

        // Tell SettlementManager the batch is reverted.
        var sm = (UInt160)(Storage.Get(new byte[] { KeySettlementManager }) ?? throw new Exception("sm unset"));
        Contract.Call(sm, "revertBatch", CallFlags.All, new object[] { chainId, batchNumber });

        OnChallengeAccepted(chainId, batchNumber, challenger, bondBalance);
    }

    /// <summary>
    /// Once the window has expired and no challenge was accepted, anyone calls this to
    /// move the batch from Challengeable → Finalized via SettlementManager.
    /// </summary>
    public static void FinalizeIfPastWindow(uint chainId, ulong batchNumber)
    {
        var deadlineKey = DeadlineKey(chainId, batchNumber);
        var rawDeadline = Storage.Get(deadlineKey);
        ExecutionEngine.Assert(rawDeadline != null, "no open window");
        var deadline = DecodeUInt32((byte[])rawDeadline!);
        ExecutionEngine.Assert((uint)(Runtime.Time / 1000) > deadline, "challenge window still open");

        ExecutionEngine.Assert(Storage.Get(AcceptedFraudKey(chainId, batchNumber)) == null,
            "batch was challenged; cannot finalize");

        var sm = (UInt160)(Storage.Get(new byte[] { KeySettlementManager }) ?? throw new Exception("sm unset"));
        Contract.Call(sm, "finalizeBatch", CallFlags.All, new object[] { chainId, batchNumber });

        OnWindowFinalized(chainId, batchNumber);
    }

    /// <summary>True if a challenge window is currently open for (chain, batch).</summary>
    [Safe]
    public static bool IsWindowOpen(uint chainId, ulong batchNumber, uint nowUnixSeconds)
    {
        var raw = Storage.Get(DeadlineKey(chainId, batchNumber));
        if (raw == null) return false;
        var deadline = DecodeUInt32((byte[])raw);
        return nowUnixSeconds <= deadline;
    }

    /// <summary>Read the deadline (or 0 if no window).</summary>
    [Safe]
    public static uint GetDeadline(uint chainId, ulong batchNumber)
    {
        var raw = Storage.Get(DeadlineKey(chainId, batchNumber));
        return raw == null ? 0u : DecodeUInt32((byte[])raw);
    }

    private static byte[] DeadlineKey(uint chainId, ulong batchNumber) => BuildKey(PrefixDeadline, chainId, batchNumber);
    private static byte[] AcceptedFraudKey(uint chainId, ulong batchNumber) => BuildKey(PrefixAcceptedFraud, chainId, batchNumber);
    private static byte[] SequencerKey(uint chainId, ulong batchNumber) => BuildKey(PrefixSequencer, chainId, batchNumber);

    private static byte[] BuildKey(byte prefix, uint chainId, ulong number)
    {
        var k = new byte[13];
        k[0] = prefix;
        k[1] = (byte)chainId; k[2] = (byte)(chainId >> 8); k[3] = (byte)(chainId >> 16); k[4] = (byte)(chainId >> 24);
        k[5] = (byte)number; k[6] = (byte)(number >> 8); k[7] = (byte)(number >> 16); k[8] = (byte)(number >> 24);
        k[9] = (byte)(number >> 32); k[10] = (byte)(number >> 40); k[11] = (byte)(number >> 48); k[12] = (byte)(number >> 56);
        return k;
    }

    private static byte[] EncodeUInt32(uint value)
    {
        return new byte[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) };
    }

    private static uint DecodeUInt32(byte[] data)
    {
        return (uint)data[0] | ((uint)data[1] << 8) | ((uint)data[2] << 16) | ((uint)data[3] << 24);
    }
}
