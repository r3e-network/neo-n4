using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.ForcedInclusion;

/// <summary>
/// Anti-censorship: a user can post any L2 transaction here and the L2 sequencer is obliged
/// to include it before <c>InclusionDeadlineSeconds</c> elapses. Failure to include slashes
/// the sequencer's bond and pauses the L2 in <c>NeoHub.SettlementManager</c>.
/// </summary>
/// <remarks>
/// See doc.md §15.4 (Forced Inclusion) and §17 (sequencer-censorship mitigation).
/// <para>
/// Spam control: <see cref="EnqueueForcedTransaction"/> charges <see cref="GetFee"/> GAS
/// (NEP-17 transfer from the caller to <see cref="GetFeeRecipient"/>) before storing the
/// entry. Development deployments can remain fee-free, but production deployment tooling
/// must configure every dependency and require <see cref="IsProductionReady"/> before release.
/// A non-zero fee MUST have a non-zero GAS token and recipient or the enqueue rejects.
/// </para>
/// </remarks>
[DisplayName("NeoHub.ForcedInclusion")]
[ContractAuthor("R3E Network", "dev@r3e.network")]
[ContractDescription("Forced-inclusion queue per L2 chain — anti-censorship primitive.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ForcedInclusion")]
[ContractPermission(Permission.Any, Method.Any)]
public class ForcedInclusionContract : SmartContract
{
    private const byte PrefixNonce = 0x01;            // 0x01 + chainId(4B) → next nonce
    private const byte PrefixEntry = 0x02;             // 0x02 + chainId(4B) + nonce(8B) → encoded forced entry
    private const byte PrefixConsumed = 0x03;          // 0x03 + chainId(4B) + nonce(8B) → 1
    private const byte KeyDeadlineSeconds = 0x04;
    private const byte KeyFeeAmount = 0x05;            // GAS amount charged per enqueue (BigInteger)
    private const byte KeyFeeRecipient = 0x06;         // 20B address that receives the fees
    private const byte KeyGasToken = 0x07;             // 20B GAS contract hash (operator-configurable for testnet/local)
    private const byte PrefixReported = 0x08;          // 0x08 + chainId(4B) + nonce(8B) → 1
    private const byte KeySequencerBond = 0x09;        // 20B SequencerBond contract hash
    private const byte KeyChainRegistry = 0x0A;        // 20B ChainRegistry contract hash for per-chain pause
    private const byte KeyCensorshipSlashAmount = 0x0B;// BigInteger amount slashed per overdue entry
    private const byte PrefixSlashed = 0x0C;           // 0x0C + chainId(4B) + nonce(8B) → 1 (censorship slash settled)
    private const byte KeySettlementManager = 0xFD;
    private const byte KeyOwner = 0xFF;
    private const int MaxTransactionProofDepth = 64;

    /// <summary>Default time the sequencer has to include a forced tx before censorship kicks in.</summary>
    public const uint DefaultDeadlineSeconds = 7200;   // 2 hours

    /// <summary>Emitted whenever a user enqueues a forced transaction.</summary>
    [DisplayName("ForcedTxEnqueued")]
    public static event Action<uint, ulong, UInt160, UInt256> OnForcedTxEnqueued = default!;

    /// <summary>Emitted after a finalized-batch transaction proof confirms inclusion.</summary>
    [DisplayName("ForcedTxConsumed")]
    public static event Action<uint, ulong> OnForcedTxConsumed = default!;

    /// <summary>Emitted when a sequencer is reported missing the deadline.</summary>
    [DisplayName("SequencerCensorshipReported")]
    public static event Action<uint, ulong, UInt160> OnSequencerCensorshipReported = default!;

    /// <summary>Emitted when governance slashes a sequencer for a reported censorship.</summary>
    [DisplayName("SequencerSlashedForCensorship")]
    public static event Action<uint, ulong, UInt160> OnSequencerSlashedForCensorship = default!;

    /// <summary>Emitted when the deadline is changed.</summary>
    [DisplayName("DeadlineSecondsChanged")]
    public static event Action<uint, uint> OnDeadlineSecondsChanged = default!;

    /// <summary>Emitted when the fee is changed.</summary>
    [DisplayName("FeeChanged")]
    public static event Action<BigInteger, BigInteger> OnFeeChanged = default!;

    /// <summary>Emitted when the fee recipient is changed.</summary>
    [DisplayName("FeeRecipientChanged")]
    public static event Action<UInt160, UInt160> OnFeeRecipientChanged = default!;

    /// <summary>Emitted when the gas token is changed.</summary>
    [DisplayName("GasTokenChanged")]
    public static event Action<UInt160, UInt160> OnGasTokenChanged = default!;

    /// <summary>Emitted when an enqueue-fee is charged. <c>(payer, recipient, amount)</c>.</summary>
    [DisplayName("ForcedInclusionFeeCharged")]
    public static event Action<UInt160, UInt160, BigInteger> OnFeeCharged = default!;

    /// <summary>Emitted when SequencerBond wiring changes.</summary>
    [DisplayName("SequencerBondChanged")]
    public static event Action<UInt160> OnSequencerBondChanged = default!;

    /// <summary>Emitted when ChainRegistry wiring changes.</summary>
    [DisplayName("ChainRegistryChanged")]
    public static event Action<UInt160> OnChainRegistryChanged = default!;

    /// <summary>Emitted when the per-report slash amount changes.</summary>
    [DisplayName("CensorshipSlashAmountChanged")]
    public static event Action<BigInteger, BigInteger> OnCensorshipSlashAmountChanged = default!;

    /// <summary>Emitted when ownership is transferred.</summary>
    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

    /// <summary>
    /// Set wiring at deploy time.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var settlementManager = (UInt160)arr[1];
        // Surface a typo'd zero / invalid hash here, not at first call.
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(settlementManager.IsValid && !settlementManager.IsZero, "invalid settlement manager");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeySettlementManager }, settlementManager);
        var deadline = arr.Length >= 3 ? (uint)(BigInteger)arr[2] : DefaultDeadlineSeconds;
        // A zero deadline would let any sequencer pass the censorship check by definition;
        // surface the misconfig at deploy time.
        ExecutionEngine.Assert(deadline > 0, "deadline must be positive");
        Storage.Put(new byte[] { KeyDeadlineSeconds }, (BigInteger)deadline);

        // Optional GAS token — operator can defer setting this and run fee-free; sets when
        // they want to enable fees later via SetGasToken + SetFee.
        if (arr.Length >= 4)
        {
            var gas = (UInt160)arr[3];
            ExecutionEngine.Assert(gas.IsValid && !gas.IsZero, "invalid gas token hash");
            Storage.Put(new byte[] { KeyGasToken }, gas);
        }
        if (arr.Length >= 5)
        {
            var sequencerBond = (UInt160)arr[4];
            ExecutionEngine.Assert(sequencerBond.IsValid && !sequencerBond.IsZero,
                "invalid sequencer bond");
            Storage.Put(new byte[] { KeySequencerBond }, sequencerBond);
        }
        if (arr.Length >= 6)
        {
            var chainRegistry = (UInt160)arr[5];
            ExecutionEngine.Assert(chainRegistry.IsValid && !chainRegistry.IsZero,
                "invalid chain registry");
            Storage.Put(new byte[] { KeyChainRegistry }, chainRegistry);
        }
        if (arr.Length >= 7)
        {
            var slashAmount = (BigInteger)arr[6];
            ExecutionEngine.Assert(slashAmount >= 0, "slash amount must be non-negative");
            Storage.Put(new byte[] { KeyCensorshipSlashAmount }, slashAmount);
        }
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

    /// <summary>Configured censorship deadline in seconds.</summary>
    [Safe]
    public static uint GetDeadlineSeconds()
    {
        var raw = Storage.Get(new byte[] { KeyDeadlineSeconds });
        return raw == null ? DefaultDeadlineSeconds : (uint)(BigInteger)raw;
    }

    /// <summary>Update the deadline. Owner only.</summary>
    public static void SetDeadlineSeconds(uint seconds)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(seconds >= 60 && seconds <= 86400, "deadline out of bounds [60, 86400]");
        var old = GetDeadlineSeconds();
        Storage.Put(new byte[] { KeyDeadlineSeconds }, (BigInteger)seconds);
        OnDeadlineSecondsChanged(old, seconds);
    }

    /// <summary>GAS amount charged per <see cref="EnqueueForcedTransaction"/> call. 0 = no fee.</summary>
    [Safe]
    public static BigInteger GetFee()
    {
        var raw = Storage.Get(new byte[] { KeyFeeAmount });
        return raw == null ? BigInteger.Zero : (BigInteger)raw;
    }

    /// <summary>Address that receives accumulated fees. Zero address = fees disabled.</summary>
    [Safe]
    public static UInt160 GetFeeRecipient()
    {
        var raw = Storage.Get(new byte[] { KeyFeeRecipient });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>GAS NEP-17 contract hash used for fee transfers.</summary>
    [Safe]
    public static UInt160 GetGasToken()
    {
        var raw = Storage.Get(new byte[] { KeyGasToken });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>SequencerBond contract used for censorship slashing. Zero means warning-only mode.</summary>
    [Safe]
    public static UInt160 GetSequencerBond()
    {
        var raw = Storage.Get(new byte[] { KeySequencerBond });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>ChainRegistry contract used to pause a censored chain. Zero means no auto-pause.</summary>
    [Safe]
    public static UInt160 GetChainRegistry()
    {
        var raw = Storage.Get(new byte[] { KeyChainRegistry });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Amount slashed from the responsible sequencer per accepted censorship report.</summary>
    [Safe]
    public static BigInteger GetCensorshipSlashAmount()
    {
        var raw = Storage.Get(new byte[] { KeyCensorshipSlashAmount });
        return raw == null ? BigInteger.Zero : (BigInteger)raw;
    }

    /// <summary>
    /// True only when spam control, censorship pausing, and sequencer slashing are all wired.
    /// Production deployment tooling treats a false value as a release-blocking failure.
    /// </summary>
    [Safe]
    public static bool IsProductionReady()
    {
        var feeRecipient = GetFeeRecipient();
        var gasToken = GetGasToken();
        var sequencerBond = GetSequencerBond();
        var chainRegistry = GetChainRegistry();
        return GetFee() > 0
            && feeRecipient.IsValid && !feeRecipient.IsZero
            && gasToken.IsValid && !gasToken.IsZero
            && sequencerBond.IsValid && !sequencerBond.IsZero
            && chainRegistry.IsValid && !chainRegistry.IsZero
            && GetCensorshipSlashAmount() > 0;
    }

    /// <summary>
    /// Owner-gated: set the per-enqueue fee in smallest GAS units. Set to 0 to disable
    /// fee collection entirely. Non-zero requires <see cref="GetFeeRecipient"/> to be set.
    /// </summary>
    public static void SetFee(BigInteger amount)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(amount >= 0, "fee must be non-negative");
        // Defense-in-depth: a non-zero fee with a zero recipient would silently transfer
        // GAS to the zero address (typically rejected, but the failure would surface as
        // "fee transfer failed" deep in EnqueueForcedTransaction). Surface the misconfig
        // at SetFee time instead.
        if (amount > 0)
        {
            var rec = GetFeeRecipient();
            ExecutionEngine.Assert(rec.IsValid && !rec.IsZero, "set feeRecipient before non-zero fee");
            var gas = GetGasToken();
            ExecutionEngine.Assert(gas.IsValid && !gas.IsZero, "set gasToken before non-zero fee");
        }
        var old = GetFee();
        Storage.Put(new byte[] { KeyFeeAmount }, amount);
        OnFeeChanged(old, amount);
    }

    /// <summary>Owner-gated: set the address that receives forced-inclusion fees.</summary>
    public static void SetFeeRecipient(UInt160 recipient)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(recipient.IsValid && !recipient.IsZero, "invalid recipient");
        var old = GetFeeRecipient();
        Storage.Put(new byte[] { KeyFeeRecipient }, recipient);
        OnFeeRecipientChanged(old, recipient);
    }

    /// <summary>Owner-gated: set the GAS NEP-17 contract hash used for fee transfers.</summary>
    public static void SetGasToken(UInt160 gasContract)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(gasContract.IsValid && !gasContract.IsZero, "invalid gas hash");
        var old = GetGasToken();
        Storage.Put(new byte[] { KeyGasToken }, gasContract);
        OnGasTokenChanged(old, gasContract);
    }

    /// <summary>Owner-gated: wire SequencerBond so reports slash the responsible sequencer.</summary>
    public static void SetSequencerBond(UInt160 sequencerBond)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(sequencerBond.IsValid && !sequencerBond.IsZero,
            "invalid sequencer bond");
        Storage.Put(new byte[] { KeySequencerBond }, sequencerBond);
        OnSequencerBondChanged(sequencerBond);
    }

    /// <summary>Owner-gated: wire ChainRegistry so reports pause the affected L2 chain.</summary>
    public static void SetChainRegistry(UInt160 chainRegistry)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(chainRegistry.IsValid && !chainRegistry.IsZero,
            "invalid chain registry");
        Storage.Put(new byte[] { KeyChainRegistry }, chainRegistry);
        OnChainRegistryChanged(chainRegistry);
    }

    /// <summary>Owner-gated: set the per-report sequencer slash amount. Zero = warning-only.</summary>
    public static void SetCensorshipSlashAmount(BigInteger amount)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(amount >= 0, "slash amount must be non-negative");
        var old = GetCensorshipSlashAmount();
        Storage.Put(new byte[] { KeyCensorshipSlashAmount }, amount);
        OnCensorshipSlashAmountChanged(old, amount);
    }

    /// <summary>
    /// Submit a forced L2 transaction. Stores the encoded payload + the L1 timestamp at which
    /// the sequencer must include it.
    /// </summary>
    /// <param name="chainId">Target L2 chain.</param>
    /// <param name="encodedTx">Canonical Neo-serialized transaction the L2 must execute.</param>
    /// <param name="txHash">Hash of <paramref name="encodedTx"/> (caller pre-computes for cheap lookup).</param>
    public static ulong EnqueueForcedTransaction(uint chainId, byte[] encodedTx, UInt256 txHash)
    {
        // chainId 0 is the L1 sentinel — without this guard, anyone could enqueue
        // forced txs for chainId=0 and bloat L1 storage with entries that no L2
        // would ever consume.
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(encodedTx.Length > 0, "empty tx");
        ExecutionEngine.Assert(HashTransaction(encodedTx).Equals(txHash), "txHash does not match encodedTx");
        var caller = Runtime.CallingScriptHash;

        // Charge the configured spam-control fee BEFORE storing any state. If the
        // transfer fails (insufficient balance, no allowance, GAS contract reverts),
        // the whole enqueue fails — caller's nonce stays untouched and no entry is
        // stored. Fee == 0 is the legacy fee-free path; non-zero requires the recipient
        // + gas-token to be wired (SetFee enforces this at config time).
        // Write nonce + entry BEFORE fee transfer (CEI ordering). If the
        // fee transfer subsequently fails, NeoVM FAULT reverts both writes
        // so there is no stale nonce or orphaned entry. A re-entrant token
        // cannot re-use the same nonce.
        var nonceKey = NonceKey(chainId);
        var raw = Storage.Get(nonceKey);
        var nonce = raw == null ? 1UL : (ulong)(BigInteger)raw + 1UL;
        Storage.Put(nonceKey, (BigInteger)nonce);

        var deadline = GetDeadlineSeconds();
        var enqueuedAt = (uint)(Runtime.Time / 1000u); // ms → seconds
        var payload = EncodeEntry(caller, txHash, encodedTx, enqueuedAt + deadline);
        Storage.Put(EntryKey(chainId, nonce), payload);

        var fee = GetFee();
        if (fee > 0)
        {
            var recipient = GetFeeRecipient();
            var gas = GetGasToken();
            ExecutionEngine.Assert(recipient.IsValid && !recipient.IsZero, "fee recipient unset");
            ExecutionEngine.Assert(gas.IsValid && !gas.IsZero, "gas token unset");
            var transferred = (bool)Contract.Call(gas, "transfer", CallFlags.All,
                new object[] { caller, recipient, fee, null! });
            ExecutionEngine.Assert(transferred, "fee transfer failed");
            OnFeeCharged(caller, recipient, fee);
        }

        OnForcedTxEnqueued(chainId, nonce, caller, txHash);
        return nonce;
    }

    /// <summary>Read a forced-inclusion entry by (chainId, nonce). Empty bytes if not present.</summary>
    [Safe]
    public static byte[] GetEntry(uint chainId, ulong nonce)
    {
        var raw = Storage.Get(EntryKey(chainId, nonce));
        return raw == null ? new byte[0] : (byte[])raw;
    }

    /// <summary>
    /// Permissionlessly mark a forced transaction consumed after proving its transaction hash is
    /// included in a finalized batch's canonical transaction root.
    /// </summary>
    /// <remarks>
    /// See doc.md §15.4. Siblings are ordered leaf-to-root and use Neo Hash256 over
    /// <c>left || right</c>. Bit i of <paramref name="leafIndex"/> selects the leaf side at level i.
    /// </remarks>
    public static void Consume(
        uint chainId,
        ulong batchNumber,
        ulong nonce,
        byte[][] siblings,
        ulong leafIndex)
    {
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(nonce > 0, "nonce must be positive");
        ExecutionEngine.Assert(siblings != null, "siblings required");
        var proofSiblings = siblings!;
        ExecutionEngine.Assert(proofSiblings.Length <= MaxTransactionProofDepth, "proof too deep");

        var entry = Storage.Get(EntryKey(chainId, nonce));
        ExecutionEngine.Assert(entry != null, "entry not found");
        var encodedEntry = (byte[])entry!;
        ExecutionEngine.Assert(encodedEntry.Length >= 60, "entry malformed");
        var txHash = ReadUInt256(encodedEntry, 20);

        var sm = (UInt160)(Storage.Get(new byte[] { KeySettlementManager }) ?? throw new Exception("sm unset"));
        var txRoot = (UInt256)Contract.Call(
            sm,
            "getFinalizedTxRoot",
            CallFlags.ReadOnly,
            new object[] { chainId, batchNumber });
        ExecutionEngine.Assert(!txRoot.Equals(UInt256.Zero), "batch is not finalized or has no transactions");
        ExecutionEngine.Assert(VerifyMerkleProof(txHash, txRoot, proofSiblings, leafIndex),
            "invalid forced-transaction proof");

        var key = ConsumedKey(chainId, nonce);
        ExecutionEngine.Assert(Storage.Get(key) == null, "already consumed");
        Storage.Put(key, new byte[] { 1 });
        OnForcedTxConsumed(chainId, nonce);
    }

    /// <summary>True if (chainId, nonce) has been marked consumed.</summary>
    [Safe]
    public static bool IsConsumed(uint chainId, ulong nonce)
    {
        return Storage.Get(ConsumedKey(chainId, nonce)) != null;
    }

    /// <summary>True if (chainId, nonce) has already produced a censorship report.</summary>
    [Safe]
    public static bool IsCensorshipReported(uint chainId, ulong nonce)
    {
        return Storage.Get(ReportedKey(chainId, nonce)) != null;
    }

    /// <summary>
    /// Permissionless censorship report: anyone can flag censorship once the entry's deadline has
    /// passed and it is still unconsumed. Records an at-most-once report and pauses the affected L2
    /// chain through ChainRegistry (liveness protection). Returns true on a successful report.
    /// NOTE: the auto-pause is best-effort — it only fires when ChainRegistry is wired
    /// (<see cref="SetChainRegistry"/>); operators MUST wire it before relying on pause-on-report.
    /// <para>
    /// SECURITY: this method does NOT slash. The responsible sequencer/proposer for the censored
    /// window cannot be attributed on-chain (the contract records no proposer-per-block, and
    /// SequencerRegistry membership is keyed by consensus key, not address), so allowing an
    /// arbitrary caller to name the victim let a griefer slash an innocent bonded sequencer.
    /// Slashing is performed separately by governance via <see cref="SlashReportedCensorship"/>.
    /// The <paramref name="sequencer"/> argument is recorded in the event for off-chain
    /// attribution only.
    /// </para>
    /// </summary>
    public static bool ReportCensorship(uint chainId, ulong nonce, UInt160 sequencer)
    {
        ExecutionEngine.Assert(sequencer.IsValid && !sequencer.IsZero, "invalid sequencer");
        ExecutionEngine.Assert(!IsConsumed(chainId, nonce), "already consumed");
        var reportedKey = ReportedKey(chainId, nonce);
        ExecutionEngine.Assert(Storage.Get(reportedKey) == null, "censorship already reported");
        var rawEntry = Storage.Get(EntryKey(chainId, nonce));
        ExecutionEngine.Assert(rawEntry != null, "entry not found");

        var entry = (byte[])rawEntry!;
        // Minimum entry size: sender(20) + txHash(32) + txLen(4) + deadline(4) = 60
        ExecutionEngine.Assert(entry.Length >= 60, "entry malformed");
        var deadline = ReadUInt32(entry, entry.Length - 4); // deadline is last 4 bytes
        var nowSec = (uint)(Runtime.Time / 1000u);
        if (nowSec < deadline) return false;

        Storage.Put(reportedKey, new byte[] { 1 });

        var chainRegistry = GetChainRegistry();
        if (chainRegistry.IsValid && !chainRegistry.IsZero)
        {
            Contract.Call(chainRegistry, "pauseChain", CallFlags.All, new object[] { chainId });
        }

        OnSequencerCensorshipReported(chainId, nonce, sequencer);
        return true;
    }

    /// <summary>
    /// Governance-gated slashing for a previously-reported censorship. Owner only — governance is
    /// the party that can attribute the responsible proposer for the censored window off-chain,
    /// which the protocol cannot do on-chain. Requires an existing, unconsumed, not-yet-slashed
    /// report for (chainId, nonce), then slashes the named sequencer's bond. Separating this from
    /// the permissionless <see cref="ReportCensorship"/> prevents an arbitrary caller from
    /// slashing an innocent bonded sequencer.
    /// </summary>
    public static void SlashReportedCensorship(uint chainId, ulong nonce, UInt160 sequencer)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(sequencer.IsValid && !sequencer.IsZero, "invalid sequencer");
        ExecutionEngine.Assert(Storage.Get(ReportedKey(chainId, nonce)) != null, "no censorship report");
        // NOTE: intentionally NOT gated on !IsConsumed. The report already proved the deadline was
        // missed (censorship occurred); a belated Consume afterwards must not immunize the
        // sequencer from the slash. The slashed flag below still makes this at-most-once.
        var slashedKey = SlashedKey(chainId, nonce);
        ExecutionEngine.Assert(Storage.Get(slashedKey) == null, "already slashed");

        var slashAmount = GetCensorshipSlashAmount();
        ExecutionEngine.Assert(slashAmount > 0, "slash amount not configured");
        var sequencerBond = GetSequencerBond();
        ExecutionEngine.Assert(sequencerBond.IsValid && !sequencerBond.IsZero, "sequencer bond unset");

        Storage.Put(slashedKey, new byte[] { 1 });
        Contract.Call(sequencerBond, "slash", CallFlags.All,
            new object[] { chainId, sequencer, slashAmount, Runtime.CallingScriptHash });

        OnSequencerSlashedForCensorship(chainId, nonce, sequencer);
    }

    /// <summary>True if a reported censorship at (chainId, nonce) has been slash-settled.</summary>
    [Safe]
    public static bool IsCensorshipSlashed(uint chainId, ulong nonce)
    {
        return Storage.Get(SlashedKey(chainId, nonce)) != null;
    }

    private static byte[] NonceKey(uint chainId)
    {
        var k = new byte[5];
        k[0] = PrefixNonce;
        k[1] = (byte)chainId; k[2] = (byte)(chainId >> 8); k[3] = (byte)(chainId >> 16); k[4] = (byte)(chainId >> 24);
        return k;
    }

    private static byte[] EntryKey(uint chainId, ulong nonce) =>
        BuildKey(PrefixEntry, chainId, nonce);

    private static byte[] ConsumedKey(uint chainId, ulong nonce) =>
        BuildKey(PrefixConsumed, chainId, nonce);

    private static byte[] ReportedKey(uint chainId, ulong nonce) =>
        BuildKey(PrefixReported, chainId, nonce);

    private static byte[] SlashedKey(uint chainId, ulong nonce) =>
        BuildKey(PrefixSlashed, chainId, nonce);

    private static byte[] BuildKey(byte prefix, uint chainId, ulong number)
    {
        var k = new byte[13];
        k[0] = prefix;
        k[1] = (byte)chainId; k[2] = (byte)(chainId >> 8); k[3] = (byte)(chainId >> 16); k[4] = (byte)(chainId >> 24);
        k[5] = (byte)number; k[6] = (byte)(number >> 8); k[7] = (byte)(number >> 16); k[8] = (byte)(number >> 24);
        k[9] = (byte)(number >> 32); k[10] = (byte)(number >> 40); k[11] = (byte)(number >> 48); k[12] = (byte)(number >> 56);
        return k;
    }

    private static byte[] EncodeEntry(UInt160 sender, UInt256 txHash, byte[] tx, uint deadlineUnixSec)
    {
        // 20B sender + 32B txHash + 4B txLen + tx bytes + 4B deadline = 60 + tx.Length.
        var size = 20 + 32 + 4 + tx.Length + 4;
        var buf = new byte[size];
        var pos = 0;
        var s = (byte[])sender;
        for (var i = 0; i < 20; i++) buf[pos + i] = s[i];
        pos += 20;
        var h = (byte[])txHash;
        for (var i = 0; i < 32; i++) buf[pos + i] = h[i];
        pos += 32;
        buf[pos++] = (byte)tx.Length; buf[pos++] = (byte)(tx.Length >> 8);
        buf[pos++] = (byte)(tx.Length >> 16); buf[pos++] = (byte)(tx.Length >> 24);
        for (var i = 0; i < tx.Length; i++) buf[pos + i] = tx[i];
        pos += tx.Length;
        buf[pos++] = (byte)deadlineUnixSec; buf[pos++] = (byte)(deadlineUnixSec >> 8);
        buf[pos++] = (byte)(deadlineUnixSec >> 16); buf[pos++] = (byte)(deadlineUnixSec >> 24);
        return buf;
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return (uint)data[offset]
            | ((uint)data[offset + 1] << 8)
            | ((uint)data[offset + 2] << 16)
            | ((uint)data[offset + 3] << 24);
    }

    private static UInt256 HashTransaction(byte[] encodedTx)
    {
        var first = CryptoLib.Sha256((ByteString)encodedTx);
        return (UInt256)(byte[])CryptoLib.Sha256(first);
    }

    private static UInt256 ReadUInt256(byte[] data, int offset)
    {
        var bytes = new byte[32];
        for (var i = 0; i < 32; i++) bytes[i] = data[offset + i];
        return (UInt256)bytes;
    }

    private static bool VerifyMerkleProof(
        UInt256 leafHash,
        UInt256 expectedRoot,
        byte[][] siblings,
        ulong leafIndex)
    {
        var current = (byte[])leafHash;
        var index = leafIndex;
        for (var level = 0; level < siblings.Length; level++)
        {
            var sibling = siblings[level];
            ExecutionEngine.Assert(sibling != null && sibling.Length == 32,
                "sibling must be 32 bytes");
            var combined = new byte[64];
            if ((index & 1UL) == 0UL)
            {
                for (var i = 0; i < 32; i++) combined[i] = current[i];
                for (var i = 0; i < 32; i++) combined[32 + i] = sibling[i];
            }
            else
            {
                for (var i = 0; i < 32; i++) combined[i] = sibling[i];
                for (var i = 0; i < 32; i++) combined[32 + i] = current[i];
            }

            var first = CryptoLib.Sha256((ByteString)combined);
            current = (byte[])CryptoLib.Sha256(first);
            index >>= 1;
        }

        return index == 0 && expectedRoot.Equals((UInt256)current);
    }
}
