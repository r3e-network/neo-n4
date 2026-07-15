using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.SharedBridge;

/// <summary>
/// Canonical asset escrow shared across all Neo Elastic Network L2 chains. Locks platform assets
/// (GAS / NEO / USDT / USDC / BTC) and NEP-17 tokens on deposit, releases against finalized
/// <c>withdrawalRoot</c> proofs. See doc.md §11.
/// </summary>
[DisplayName("NeoHub.SharedBridge")]
[ContractAuthor("R3E Network", "dev@r3e.network")]
[ContractDescription("Canonical asset escrow + L1↔L2 transfer for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SharedBridge")]
[ContractPermission(Permission.Any, Method.Any)]
public class SharedBridgeContract : SmartContract
{
    private const byte PrefixDepositNonce = 0x01;     // 0x01 + chainId(4B) → next nonce (8B)
    private const byte PrefixDeposit = 0x02;          // 0x02 + chainId(4B) + nonce(8B) → encoded deposit msg
    private const byte PrefixWithdrawalConsumed = 0x03; // 0x03 + chainId(4B) + leafHash(32B) → 1
    private const byte PrefixPendingTransfer = 0x04;  // 0x04 + asset(20B) + from(20B) → 1
    private const byte PrefixLockedBalance = 0x05;    // 0x05 + chainId(4B) + asset(20B) → per-chain escrowed amount (BigInteger)
    private const byte KeyLockedBalanceMigrationSealed = 0x06; // 1 once the migration backfill is sealed
    private const byte PrefixSettlementManager = 0xFD;
    private const byte PrefixTokenRegistry = 0xFE;
    private const byte PrefixEmergencyManager = 0xFC;
    private const byte KeyOwner = 0xFF;

    /// <summary>Emitted when a user deposits into the bridge.</summary>
    [DisplayName("DepositEnqueued")]
    public static event Action<uint, ulong, UInt160, UInt160, BigInteger> OnDepositEnqueued = default!;

    /// <summary>Emitted when a withdrawal is finalized and assets released.</summary>
    [DisplayName("WithdrawalFinalized")]
    public static event Action<uint, UInt160, UInt160, BigInteger> OnWithdrawalFinalized = default!;

    /// <summary>Emitted when ownership is transferred.</summary>
    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

    /// <summary>Emitted when EmergencyManager address is changed.</summary>
    [DisplayName("EmergencyManagerChanged")]
    public static event Action<UInt160> OnEmergencyManagerChanged = default!;

    /// <summary>Emitted when an upgrade migration backfills the per-chain escrow ledger.</summary>
    [DisplayName("LockedBalanceMigrated")]
    public static event Action<uint, UInt160, BigInteger> OnLockedBalanceMigrated = default!;

    /// <summary>Emitted when the locked-balance migration is permanently sealed.</summary>
    [DisplayName("LockedBalanceMigrationSealed")]
    public static event Action OnLockedBalanceMigrationSealed = default!;

    /// <summary>Set bridge wiring on deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var settlementManager = (UInt160)arr[1];
        var tokenRegistry = (UInt160)arr[2];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(settlementManager.IsValid, "invalid settlement manager");
        ExecutionEngine.Assert(tokenRegistry.IsValid, "invalid token registry");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { PrefixSettlementManager }, settlementManager);
        Storage.Put(new byte[] { PrefixTokenRegistry }, tokenRegistry);
        // Fresh deployment: the per-chain escrow ledger starts correct from the first deposit, so the
        // migration backfill window is never needed — seal it immediately so MigrateLockedBalance is
        // not a standing cap-inflation surface. An in-place UPGRADE (update == true) returns early
        // above and therefore leaves the migration window OPEN for the operator to backfill.
        Storage.Put(new byte[] { KeyLockedBalanceMigrationSealed }, new byte[] { 1 });
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

    /// <summary>Hash of the SettlementManager contract whose finalized batches we trust.</summary>
    [Safe]
    public static UInt160 GetSettlementManager()
    {
        var raw = Storage.Get(new byte[] { PrefixSettlementManager });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Hash of the TokenRegistry contract used to bind L1 payout assets to L2 withdrawal assets.</summary>
    [Safe]
    public static UInt160 GetTokenRegistry()
    {
        var raw = Storage.Get(new byte[] { PrefixTokenRegistry });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Hash of the EmergencyManager contract for pause enforcement.</summary>
    [Safe]
    public static UInt160 GetEmergencyManager()
    {
        var raw = Storage.Get(new byte[] { PrefixEmergencyManager });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Set the EmergencyManager contract hash. Owner only.</summary>
    public static void SetEmergencyManager(UInt160 emergencyManager)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Put(new byte[] { PrefixEmergencyManager }, emergencyManager);
        OnEmergencyManagerChanged(emergencyManager);
    }

    /// <summary>Check if the network is paused. Returns false if EmergencyManager is not set.</summary>
    private static bool IsPaused()
    {
        var em = GetEmergencyManager();
        if (em == UInt160.Zero) return false;
        return (bool)Contract.Call(em, "isPaused", CallFlags.ReadOnly, new object[0]);
    }

    /// <summary>
    /// Lock <paramref name="amount"/> of <paramref name="asset"/> from <see cref="Runtime.CallingScriptHash"/>'s
    /// allowance, allocate a deposit nonce for <paramref name="targetChainId"/>, and emit the
    /// canonical L1→L2 message. The L2 then consumes the message in its next batch.
    /// </summary>
    public static ulong Deposit(UInt160 asset, BigInteger amount, uint targetChainId, UInt160 l2Recipient)
    {
        ExecutionEngine.Assert(!IsPaused(), "network paused");
        ExecutionEngine.Assert(asset.IsValid && !asset.IsZero, "invalid asset");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        ExecutionEngine.Assert(l2Recipient.IsValid && !l2Recipient.IsZero, "invalid recipient");
        // chainId 0 is the L1 sentinel — without this guard a deposit to chainId=0
        // would lock tokens in escrow that no L2 would ever pick up.
        ExecutionEngine.Assert(targetChainId > 0, "targetChainId 0 is reserved for L1");

        // Refuse to lock funds for an asset that has no active L1->L2 mapping on the target chain.
        // The withdrawal path already enforces this binding; without the symmetric deposit-side
        // check a deposit of an unmapped/inactive asset is permanently locked — the L2 can never
        // mint it and there is no refund path.
        var tokenRegistry = GetTokenRegistry();
        ExecutionEngine.Assert(tokenRegistry.IsValid && !tokenRegistry.IsZero, "token registry not wired");
        var mappedL2Asset = (UInt160)Contract.Call(
            tokenRegistry, "getL2Asset", CallFlags.ReadOnly, new object[] { asset, targetChainId });
        ExecutionEngine.Assert(mappedL2Asset.IsValid && !mappedL2Asset.IsZero, "asset not mapped for target chain");
        var mappingActive = (bool)Contract.Call(
            tokenRegistry, "isActive", CallFlags.ReadOnly, new object[] { asset, targetChainId });
        ExecutionEngine.Assert(mappingActive, "asset mapping inactive");

        var caller = Runtime.CallingScriptHash;

        // Allocate nonce and commit deposit record before pulling tokens.
        // If the subsequent transfer fails, NeoVM FAULT reverts the storage write,
        // so there is no stale nonce gap. This ordering prevents a re-entrant token
        // from re-using the same nonce and overwriting a prior deposit.
        var nonce = NextDepositNonce(targetChainId);
        var encoded = EncodeDeposit(asset, amount, l2Recipient, caller, nonce);
        Storage.Put(DepositKey(targetChainId, nonce), encoded);

        // Pull tokens into escrow. The asset must be NEP-17. The pending-transfer
        // marker lets the NEP-17 hook reject unsolicited direct transfers while
        // still accepting this Deposit-initiated transfer.
        var pendingKey = PendingTransferKey(asset, caller);
        Storage.Put(pendingKey, new byte[] { 1 });
        var transferred = (bool)Contract.Call(
            asset, "transfer",
            CallFlags.All,
            new object[] { caller, Runtime.ExecutingScriptHash, amount, null! });
        ExecutionEngine.Assert(transferred, "asset transfer failed");
        Storage.Delete(pendingKey);

        // Credit the per-chain escrow ledger so withdrawals for this chain can never draw more of
        // this asset than was deposited for it — this contains a single chain's compromise to that
        // chain's own escrow instead of the whole shared pool.
        IncrementLocked(targetChainId, asset, amount);

        OnDepositEnqueued(targetChainId, nonce, caller, l2Recipient, amount);
        return nonce;
    }

    /// <summary>NEP-17 hook. Accept only transfers initiated by <see cref="Deposit"/>.</summary>
    public static void OnNEP17Payment(UInt160 from, BigInteger amount, object data)
    {
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        var asset = (UInt160)Runtime.CallingScriptHash;
        var pendingKey = PendingTransferKey(asset, from);
        ExecutionEngine.Assert(Storage.Get(pendingKey) != null,
            "direct transfer rejected — call Deposit to enqueue an L2 bridge deposit");
        Storage.Delete(pendingKey);
    }

    /// <summary>Read a previously enqueued deposit (used by L2 nodes when scanning the queue).</summary>
    [Safe]
    public static byte[] GetDeposit(uint chainId, ulong nonce)
    {
        var raw = Storage.Get(DepositKey(chainId, nonce));
        return raw == null ? new byte[0] : (byte[])raw;
    }

    /// <summary>
    /// Finalize a withdrawal anchored in the latest finalized batch on
    /// <paramref name="chainId"/>. Use <see cref="FinalizeWithdrawalAt"/> when the user's
    /// withdrawal is anchored in an older finalized batch.
    /// </summary>
    public static void FinalizeWithdrawal(
        uint chainId,
        UInt256 withdrawalLeafHash,
        UInt160 emittingContract,
        UInt160 l2Sender,
        UInt160 l2Asset,
        ulong withdrawalNonce,
        UInt160 asset,
        UInt160 recipient,
        BigInteger amount)
    {
        ExecutionEngine.Assert(!IsPaused(), "network paused");
        ValidateWithdrawalArgs(chainId, asset, recipient, amount);
        ValidateWithdrawalLeafBinding(
            chainId, withdrawalLeafHash, emittingContract, l2Sender,
            l2Asset, withdrawalNonce, asset, recipient, amount);
        var consumedKey = WithdrawalKey(chainId, withdrawalLeafHash);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "withdrawal already consumed");

        // Delegate Merkle proof verification to the SettlementManager via inter-contract call.
        var sm = GetSettlementManager();
        var verified = (bool)Contract.Call(
            sm, "verifyWithdrawalLeaf",
            CallFlags.ReadOnly,
            new object[] { chainId, withdrawalLeafHash });
        ExecutionEngine.Assert(verified, "withdrawal leaf not in finalized batch");

        ConsumeAndPayout(consumedKey, chainId, asset, recipient, amount);
    }

    /// <summary>
    /// Finalize a withdrawal anchored in a specific finalized batch on <paramref name="chainId"/>.
    /// Lets a user claim a withdrawal whose batch is no longer the latest — without this,
    /// the claim would silently fail once the chain has progressed past their batch.
    /// </summary>
    public static void FinalizeWithdrawalAt(
        uint chainId,
        ulong batchNumber,
        UInt256 withdrawalLeafHash,
        UInt160 emittingContract,
        UInt160 l2Sender,
        UInt160 l2Asset,
        ulong withdrawalNonce,
        UInt160 asset,
        UInt160 recipient,
        BigInteger amount)
    {
        ExecutionEngine.Assert(!IsPaused(), "network paused");
        ValidateWithdrawalArgs(chainId, asset, recipient, amount);
        ValidateWithdrawalLeafBinding(
            chainId, withdrawalLeafHash, emittingContract, l2Sender,
            l2Asset, withdrawalNonce, asset, recipient, amount);
        var consumedKey = WithdrawalKey(chainId, withdrawalLeafHash);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "withdrawal already consumed");

        // Verify against the explicitly-named finalized batch, not just the latest.
        var sm = GetSettlementManager();
        var verified = (bool)Contract.Call(
            sm, "verifyWithdrawalLeafAt",
            CallFlags.ReadOnly,
            new object[] { chainId, batchNumber, withdrawalLeafHash });
        ExecutionEngine.Assert(verified, "withdrawal leaf not in named finalized batch");

        ConsumeAndPayout(consumedKey, chainId, asset, recipient, amount);
    }

    /// <summary>
    /// Finalize a withdrawal using the production-shape Merkle inclusion proof. Use this
    /// when the L2 batch has many withdrawals — the user supplies the per-level sibling
    /// hashes and their leaf index so the L1 contract can re-derive the batch's
    /// withdrawalRoot and verify the user's specific leaf is in it.
    /// </summary>
    /// <remarks>
    /// Hash composition matches the off-chain <c>Neo.L2.State.MerkleTree</c>:
    /// <c>Sha256(Sha256(left || right))</c>, left/right ordered by the leaf-index bit
    /// for that level. See <c>SettlementManager.VerifyWithdrawalLeafWithProof</c>.
    /// </remarks>
    public static void FinalizeWithdrawalWithProof(
        uint chainId,
        ulong batchNumber,
        UInt256 withdrawalLeafHash,
        byte[][] siblings,
        ulong leafIndex,
        UInt160 emittingContract,
        UInt160 l2Sender,
        UInt160 l2Asset,
        ulong withdrawalNonce,
        UInt160 asset,
        UInt160 recipient,
        BigInteger amount)
    {
        ExecutionEngine.Assert(!IsPaused(), "network paused");
        ValidateWithdrawalArgs(chainId, asset, recipient, amount);
        ValidateWithdrawalLeafBinding(
            chainId, withdrawalLeafHash, emittingContract, l2Sender,
            l2Asset, withdrawalNonce, asset, recipient, amount);
        var consumedKey = WithdrawalKey(chainId, withdrawalLeafHash);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "withdrawal already consumed");

        var sm = GetSettlementManager();
        var verified = (bool)Contract.Call(
            sm, "verifyWithdrawalLeafWithProof",
            CallFlags.ReadOnly,
            new object[] { chainId, batchNumber, withdrawalLeafHash, siblings, leafIndex });
        ExecutionEngine.Assert(verified, "withdrawal leaf not in batch's Merkle root (proof failed)");

        ConsumeAndPayout(consumedKey, chainId, asset, recipient, amount);
    }

    /// <summary>
    /// Emergency withdrawal path available only while the network is paused. This mirrors
    /// <see cref="FinalizeWithdrawalWithProof"/> but inverts the pause precondition so users
    /// can still recover finalized withdrawals during incident response.
    /// </summary>
    public static void EmergencyFinalizeWithdrawalWithProof(
        uint chainId,
        ulong batchNumber,
        UInt256 withdrawalLeafHash,
        byte[][] siblings,
        ulong leafIndex,
        UInt160 emittingContract,
        UInt160 l2Sender,
        UInt160 l2Asset,
        ulong withdrawalNonce,
        UInt160 asset,
        UInt160 recipient,
        BigInteger amount)
    {
        ExecutionEngine.Assert(IsPaused(), "emergency withdrawal only valid while paused");
        ValidateWithdrawalArgs(chainId, asset, recipient, amount);
        ValidateWithdrawalLeafBinding(
            chainId, withdrawalLeafHash, emittingContract, l2Sender,
            l2Asset, withdrawalNonce, asset, recipient, amount);
        var consumedKey = WithdrawalKey(chainId, withdrawalLeafHash);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "withdrawal already consumed");

        var sm = GetSettlementManager();
        var verified = (bool)Contract.Call(
            sm, "verifyWithdrawalLeafWithProof",
            CallFlags.ReadOnly,
            new object[] { chainId, batchNumber, withdrawalLeafHash, siblings, leafIndex });
        ExecutionEngine.Assert(verified, "withdrawal leaf not in batch's Merkle root (proof failed)");

        ConsumeAndPayout(consumedKey, chainId, asset, recipient, amount);
    }

    private static void ValidateWithdrawalArgs(uint chainId, UInt160 asset, UInt160 recipient, BigInteger amount)
    {
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        ExecutionEngine.Assert(asset.IsValid && !asset.IsZero, "invalid asset");
        ExecutionEngine.Assert(recipient.IsValid && !recipient.IsZero, "invalid recipient");
    }

    private static void ValidateWithdrawalLeafBinding(
        uint chainId,
        UInt256 withdrawalLeafHash,
        UInt160 emittingContract,
        UInt160 l2Sender,
        UInt160 l2Asset,
        ulong withdrawalNonce,
        UInt160 asset,
        UInt160 recipient,
        BigInteger amount)
    {
        ExecutionEngine.Assert(emittingContract.IsValid && !emittingContract.IsZero, "invalid emitting contract");
        ExecutionEngine.Assert(l2Sender.IsValid && !l2Sender.IsZero, "invalid L2 sender");
        ExecutionEngine.Assert(l2Asset.IsValid && !l2Asset.IsZero, "invalid L2 asset");

        var expected = ComputeWithdrawalLeafHash(
            chainId, emittingContract, l2Sender, recipient, l2Asset, amount, withdrawalNonce);
        ExecutionEngine.Assert(expected.Equals(withdrawalLeafHash), "withdrawal leaf preimage mismatch");

        var registry = GetTokenRegistry();
        ExecutionEngine.Assert(registry.IsValid && !registry.IsZero, "token registry not wired");
        var mappedL2Asset = (UInt160)Contract.Call(
            registry,
            "getL2Asset",
            CallFlags.ReadOnly,
            new object[] { asset, chainId });
        ExecutionEngine.Assert(mappedL2Asset.Equals(l2Asset), "L1 asset does not map to withdrawal L2 asset");

        var active = (bool)Contract.Call(
            registry,
            "isActive",
            CallFlags.ReadOnly,
            new object[] { asset, chainId });
        ExecutionEngine.Assert(active, "asset mapping inactive");
    }

    private static UInt256 ComputeWithdrawalLeafHash(
        uint chainId,
        UInt160 emittingContract,
        UInt160 l2Sender,
        UInt160 l1Recipient,
        UInt160 l2Asset,
        BigInteger amount,
        ulong nonce)
    {
        var amountBytes = ToUnsignedLittleEndian(amount);
        ExecutionEngine.Assert(amountBytes.Length <= 64, "amount too large");

        // 4B chainId domain-separator first so an inclusion proof from one
        // L2's withdrawal root can never replay against another L2 — even if
        // the rest of the tuple coincidentally matches. Operational consumed-
        // key + per-chain Merkle root already block exploitation today; this
        // hash-level separation closes the defense-in-depth gap.
        var totalLen = 4 + 20 + 20 + 20 + 20 + 4 + amountBytes.Length + 8;
        var buf = new byte[totalLen];
        var pos = 0;

        buf[pos++] = (byte)chainId;
        buf[pos++] = (byte)(chainId >> 8);
        buf[pos++] = (byte)(chainId >> 16);
        buf[pos++] = (byte)(chainId >> 24);

        WriteUInt160(buf, pos, emittingContract);
        pos += 20;
        WriteUInt160(buf, pos, l2Sender);
        pos += 20;
        WriteUInt160(buf, pos, l1Recipient);
        pos += 20;
        WriteUInt160(buf, pos, l2Asset);
        pos += 20;

        var amountLen = amountBytes.Length;
        buf[pos++] = (byte)amountLen;
        buf[pos++] = (byte)(amountLen >> 8);
        buf[pos++] = (byte)(amountLen >> 16);
        buf[pos++] = (byte)(amountLen >> 24);
        for (var i = 0; i < amountLen; i++) buf[pos + i] = amountBytes[i];
        pos += amountLen;

        buf[pos++] = (byte)nonce;
        buf[pos++] = (byte)(nonce >> 8);
        buf[pos++] = (byte)(nonce >> 16);
        buf[pos++] = (byte)(nonce >> 24);
        buf[pos++] = (byte)(nonce >> 32);
        buf[pos++] = (byte)(nonce >> 40);
        buf[pos++] = (byte)(nonce >> 48);
        buf[pos++] = (byte)(nonce >> 56);

        var h1 = CryptoLib.Sha256((ByteString)buf);
        return (UInt256)(byte[])CryptoLib.Sha256(h1);
    }

    private static byte[] ToUnsignedLittleEndian(BigInteger value)
    {
        var raw = value.ToByteArray();
        var len = raw.Length;
        while (len > 1 && raw[len - 1] == 0) len--;

        var trimmed = new byte[len];
        for (var i = 0; i < len; i++) trimmed[i] = raw[i];
        return trimmed;
    }

    private static void WriteUInt160(byte[] destination, int offset, UInt160 value)
    {
        var bytes = (byte[])value;
        for (var i = 0; i < 20; i++) destination[offset + i] = bytes[i];
    }

    private static void ConsumeAndPayout(byte[] consumedKey, uint chainId, UInt160 asset, UInt160 recipient, BigInteger amount)
    {
        Storage.Put(consumedKey, new byte[] { 1 });

        // A chain may only withdraw up to what was escrowed for it in this asset. This caps the
        // blast radius of any single chain's compromise (or a forged withdrawalRoot) to that
        // chain's own deposits and prevents draining assets escrowed on behalf of other chains.
        var locked = GetLockedBalance(chainId, asset);
        ExecutionEngine.Assert(locked >= amount, "withdrawal exceeds chain's escrowed balance");
        Storage.Put(LockedBalanceKey(chainId, asset), locked - amount);

        var transferred = (bool)Contract.Call(
            asset, "transfer",
            CallFlags.All,
            new object[] { Runtime.ExecutingScriptHash, recipient, amount, null! });
        ExecutionEngine.Assert(transferred, "asset transfer failed");

        OnWithdrawalFinalized(chainId, asset, recipient, amount);
    }

    /// <summary>Per-chain escrowed balance of an L1 asset (incremented on deposit, decremented on
    /// withdrawal). Withdrawals for a chain can never exceed this.</summary>
    [Safe]
    public static BigInteger GetLockedBalance(uint chainId, UInt160 asset)
    {
        var raw = Storage.Get(LockedBalanceKey(chainId, asset));
        return raw == null ? 0 : (BigInteger)raw;
    }

    private static void IncrementLocked(uint chainId, UInt160 asset, BigInteger amount)
    {
        var key = LockedBalanceKey(chainId, asset);
        var current = GetLockedBalance(chainId, asset);
        Storage.Put(key, current + amount);
    }

    /// <summary>
    /// One-time, set-once-per-(chainId, asset) migration backfill for the per-chain escrow ledger.
    /// The <see cref="GetLockedBalance"/> ledger is only credited by <see cref="Deposit"/> from this
    /// contract version onward; if this contract is deployed as an in-place UPGRADE over a bridge that
    /// already holds escrow, that pre-existing escrow has no ledger entry and its withdrawals would
    /// fail the cap. An operator backfills the outstanding per-(chainId, asset) escrow here (before
    /// any deposits), then calls <see cref="SealLockedBalanceMigration"/> to make the ledger
    /// immutable to admin writes.
    /// <para>
    /// Guarded: owner-only, network MUST be paused (no withdrawals can race the backfill), only
    /// callable before the migration is sealed, and SET-ONCE per (chainId, asset) — it refuses to
    /// overwrite an already-credited entry, so an accidental double-submit cannot inflate the cap.
    /// Fresh deployments auto-seal this in _deploy (ledger starts correct from the first deposit), so
    /// the window only exists for upgrades.
    /// </para>
    /// </summary>
    public static void MigrateLockedBalance(uint chainId, UInt160 asset, BigInteger amount)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(IsPaused(), "migration requires the network to be paused");
        ExecutionEngine.Assert(!IsLockedBalanceMigrationSealed(), "locked-balance migration is sealed");
        ExecutionEngine.Assert(asset.IsValid && !asset.IsZero, "invalid asset");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        // Set-once: refuse to overwrite/double-credit an entry that already has a balance (from a
        // prior migration call or a deposit). Backfill must run on a clean ledger entry.
        ExecutionEngine.Assert(GetLockedBalance(chainId, asset) == 0, "locked balance already set for (chainId, asset)");
        Storage.Put(LockedBalanceKey(chainId, asset), amount);
        OnLockedBalanceMigrated(chainId, asset, amount);
    }

    /// <summary>Permanently disable <see cref="MigrateLockedBalance"/>. Owner only, one-way.</summary>
    public static void SealLockedBalanceMigration()
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Put(new byte[] { KeyLockedBalanceMigrationSealed }, new byte[] { 1 });
        OnLockedBalanceMigrationSealed();
    }

    /// <summary>True once <see cref="SealLockedBalanceMigration"/> has been called.</summary>
    [Safe]
    public static bool IsLockedBalanceMigrationSealed()
    {
        return Storage.Get(new byte[] { KeyLockedBalanceMigrationSealed }) != null;
    }

    private static byte[] LockedBalanceKey(uint chainId, UInt160 asset)
    {
        var k = new byte[1 + 4 + 20];
        k[0] = PrefixLockedBalance;
        k[1] = (byte)chainId; k[2] = (byte)(chainId >> 8); k[3] = (byte)(chainId >> 16); k[4] = (byte)(chainId >> 24);
        var b = (byte[])asset;
        for (var i = 0; i < 20; i++) k[5 + i] = b[i];
        return k;
    }

    private static ulong NextDepositNonce(uint chainId)
    {
        var key = NonceKey(chainId);
        var raw = Storage.Get(key);
        ulong current = raw == null ? 0UL : (ulong)(BigInteger)raw;
        var next = current + 1;
        Storage.Put(key, (BigInteger)next);
        return next;
    }

    private static byte[] NonceKey(uint chainId)
    {
        var key = new byte[5];
        key[0] = PrefixDepositNonce;
        key[1] = (byte)chainId;
        key[2] = (byte)(chainId >> 8);
        key[3] = (byte)(chainId >> 16);
        key[4] = (byte)(chainId >> 24);
        return key;
    }

    private static byte[] DepositKey(uint chainId, ulong nonce)
    {
        var key = new byte[13];
        key[0] = PrefixDeposit;
        key[1] = (byte)chainId;
        key[2] = (byte)(chainId >> 8);
        key[3] = (byte)(chainId >> 16);
        key[4] = (byte)(chainId >> 24);
        key[5] = (byte)nonce;
        key[6] = (byte)(nonce >> 8);
        key[7] = (byte)(nonce >> 16);
        key[8] = (byte)(nonce >> 24);
        key[9] = (byte)(nonce >> 32);
        key[10] = (byte)(nonce >> 40);
        key[11] = (byte)(nonce >> 48);
        key[12] = (byte)(nonce >> 56);
        return key;
    }

    private static byte[] WithdrawalKey(uint chainId, UInt256 leafHash)
    {
        var key = new byte[5 + 32];
        key[0] = PrefixWithdrawalConsumed;
        key[1] = (byte)chainId;
        key[2] = (byte)(chainId >> 8);
        key[3] = (byte)(chainId >> 16);
        key[4] = (byte)(chainId >> 24);
        var hashBytes = (byte[])leafHash;
        for (var i = 0; i < 32; i++) key[5 + i] = hashBytes[i];
        return key;
    }

    private static byte[] PendingTransferKey(UInt160 asset, UInt160 from)
    {
        var key = new byte[1 + 20 + 20];
        key[0] = PrefixPendingTransfer;
        WriteUInt160(key, 1, asset);
        WriteUInt160(key, 21, from);
        return key;
    }

    private static byte[] EncodeDeposit(UInt160 asset, BigInteger amount, UInt160 recipient, UInt160 sender, ulong nonce)
    {
        // 20B asset + 20B recipient + 20B sender + 8B nonce + amount(varbytes).
        // Amount uses the same minimal unsigned LE encoding as withdrawal leaves and
        // off-chain DepositPayload so high-MSB values do not grow an extra sign byte.
        var amountBytes = ToUnsignedLittleEndian(amount);
        var totalLen = 20 + 20 + 20 + 8 + 4 + amountBytes.Length;
        var buf = new byte[totalLen];
        var pos = 0;

        var assetBytes = (byte[])asset;
        for (var i = 0; i < 20; i++) buf[pos + i] = assetBytes[i];
        pos += 20;

        var recipBytes = (byte[])recipient;
        for (var i = 0; i < 20; i++) buf[pos + i] = recipBytes[i];
        pos += 20;

        var senderBytes = (byte[])sender;
        for (var i = 0; i < 20; i++) buf[pos + i] = senderBytes[i];
        pos += 20;

        buf[pos++] = (byte)nonce;
        buf[pos++] = (byte)(nonce >> 8);
        buf[pos++] = (byte)(nonce >> 16);
        buf[pos++] = (byte)(nonce >> 24);
        buf[pos++] = (byte)(nonce >> 32);
        buf[pos++] = (byte)(nonce >> 40);
        buf[pos++] = (byte)(nonce >> 48);
        buf[pos++] = (byte)(nonce >> 56);

        var len = amountBytes.Length;
        buf[pos++] = (byte)len;
        buf[pos++] = (byte)(len >> 8);
        buf[pos++] = (byte)(len >> 16);
        buf[pos++] = (byte)(len >> 24);
        for (var i = 0; i < len; i++) buf[pos + i] = amountBytes[i];

        return buf;
    }
}
