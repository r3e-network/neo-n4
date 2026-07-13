using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.ChainRegistry;

/// <summary>
/// Registers Neo Elastic Network L2 chains and their configs. Anyone can read; only the
/// governance owner can mutate. See doc.md §3.2 (ChainRegistry).
/// </summary>
[DisplayName("NeoHub.ChainRegistry")]
[ContractAuthor("R3E Network", "dev@r3e.network")]
[ContractDescription("L2 chain admission and per-chain config registry for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ChainRegistry")]
[ContractPermission(Permission.Any, Method.Any)]
public class ChainRegistryContract : SmartContract
{
    /// <summary>Storage prefix for the per-chain config record.</summary>
    private const byte PrefixConfig = 0x01;

    /// <summary>Storage prefix for the index of all registered chain ids.</summary>
    private const byte PrefixChainIndex = 0x02;

    /// <summary>Storage key for the GovernanceController contract hash. Set post-deploy by
    /// the owner; consulted by <see cref="RegisterChainPublic"/> for §16.1 admission policy.</summary>
    private const byte KeyGovernanceController = 0x03;

    /// <summary>Storage prefix for contracts authorized to pause a chain on proven censorship.</summary>
    private const byte PrefixPauser = 0x04;

    /// <summary>Storage key for the one-way governance lock. Once set, the instant owner
    /// <see cref="UpdateChain"/> path is disabled and config mutations must go through the
    /// council-gated <see cref="UpdateChainViaProposal"/>.</summary>
    private const byte KeyGovernanceLocked = 0x05;

    /// <summary>Storage prefix for consumed UpdateChain proposal ids (replay protection).</summary>
    private const byte PrefixConsumedUpdateProposal = 0x06; // 0x06 + proposalId(8B) → 1

    /// <summary>Storage key for the owner address.</summary>
    private const byte KeyOwner = 0xFF;

    /// <summary>
    /// Encoded length of an L2ChainConfig. See doc.md §3.2 + §16.2.
    /// Layout (91 bytes total):
    /// <list type="table">
    ///   <item><description>0..3   — chainId (4B LE uint)</description></item>
    ///   <item><description>4..23  — operatorManager (20B UInt160)</description></item>
    ///   <item><description>24..43 — verifier (20B UInt160)</description></item>
    ///   <item><description>44..63 — bridgeAdapter (20B UInt160)</description></item>
    ///   <item><description>64..83 — messageAdapter (20B UInt160)</description></item>
    ///   <item><description>84     — securityLevel (1B; doc.md §16.2)</description></item>
    ///   <item><description>85     — daMode (1B; doc.md §12)</description></item>
    ///   <item><description>86     — gatewayEnabled (1B bool)</description></item>
    ///   <item><description>87     — permissionlessExit (1B bool)</description></item>
    ///   <item><description>88     — sequencerModel (1B; doc.md §16.2 — Centralized=0/DbftCommittee=1/Decentralized=2)</description></item>
    ///   <item><description>89     — exitModel (1B; doc.md §16.2 — Permissionless=0/Delayed=1/OperatorAssisted=2)</description></item>
    ///   <item><description>90     — active (1B bool; <see cref="ConfigSize"/> - 1 — Pause/Resume mutate this byte)</description></item>
    /// </list>
    /// </summary>
    public const int ConfigSize = 4 + 20 * 4 + 7;

    /// <summary>Offset of the securityLevel byte within the encoded config.</summary>
    public const int OffsetSecurityLevel = 84;

    /// <summary>Offset of the daMode byte within the encoded config.</summary>
    public const int OffsetDAMode = 85;

    /// <summary>Offset of the gatewayEnabled flag byte within the encoded config.</summary>
    public const int OffsetGatewayEnabled = 86;

    /// <summary>Offset of the permissionlessExit flag byte within the encoded config.</summary>
    public const int OffsetPermissionlessExit = 87;

    /// <summary>Offset of the sequencerModel byte within the encoded config.</summary>
    public const int OffsetSequencerModel = 88;

    /// <summary>Offset of the exitModel byte within the encoded config.</summary>
    public const int OffsetExitModel = 89;

    private const byte SecurityLevelValidity = 3;
    private const byte SecurityLevelValidium = 4;
    private const byte DAModeL1 = 0;

    /// <summary>Emitted whenever a chain is registered or updated.</summary>
    [DisplayName("ChainRegistered")]
    public static event Action<uint, byte[]> OnChainRegistered = default!;

    /// <summary>Emitted whenever a chain is paused.</summary>
    [DisplayName("ChainPaused")]
    public static event Action<uint> OnChainPaused = default!;

    /// <summary>Emitted whenever a paused chain is resumed.</summary>
    [DisplayName("ChainResumed")]
    public static event Action<uint> OnChainResumed = default!;

    /// <summary>Emitted when ownership is transferred.</summary>
    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

    /// <summary>Emitted when the GovernanceController is wired.</summary>
    [DisplayName("GovernanceControllerChanged")]
    public static event Action<UInt160> OnGovernanceControllerChanged = default!;

    /// <summary>Emitted when a contract is authorized to pause chains.</summary>
    [DisplayName("PauserRegistered")]
    public static event Action<UInt160> OnPauserRegistered = default!;

    /// <summary>Emitted when a contract is removed from the chain-pauser set.</summary>
    [DisplayName("PauserRevoked")]
    public static event Action<UInt160> OnPauserRevoked = default!;

    /// <summary>Emitted the first time governance is locked. Re-locking is a no-op and does not re-emit.</summary>
    [DisplayName("GovernanceLocked")]
    public static event Action OnGovernanceLocked = default!;

    /// <summary>Initial owner is set on deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var initialOwner = (UInt160)data;
        ExecutionEngine.Assert(initialOwner.IsValid && !initialOwner.IsZero, "invalid initial owner");
        Storage.Put(new byte[] { KeyOwner }, initialOwner);
    }

    /// <summary>Look up the governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Transfer governance ownership. Old owner only.</summary>
    public static void SetOwner(UInt160 newOwner)
    {
        ExecutionEngine.Assert(newOwner.IsValid && !newOwner.IsZero, "invalid new owner");
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        var oldOwner = GetOwner();
        Storage.Put(new byte[] { KeyOwner }, newOwner);
        OnOwnerChanged(oldOwner, newOwner);
    }

    /// <summary>Register a new L2 chain. Owner only (the §16.1 "permissioned" admission
    /// path). Idempotent on chainId. <see cref="RegisterChainPublic"/> is the
    /// non-owner path gated by GovernanceController's admission mode.</summary>
    public static void RegisterChain(uint chainId, byte[] configBytes)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        // Once governance is locked, RegisterChain must not be a lock-bypass for rewriting an
        // EXISTING chain's verifier / securityLevel / daMode — those changes have to go through the
        // council-gated UpdateChainViaProposal. Registering a brand-new chainId stays allowed.
        if (Storage.Get(ConfigKey(chainId)) != null)
            ExecutionEngine.Assert(!IsGovernanceLocked(),
                "governance locked — use UpdateChainViaProposal to change an existing chain");
        WriteChainConfig(chainId, configBytes);
    }

    /// <summary>Wire the GovernanceController contract hash that <see cref="RegisterChainPublic"/>
    /// consults for the §16.1 admission policy. Owner only.</summary>
    public static void SetGovernanceController(UInt160 governanceController)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(governanceController.IsValid && !governanceController.IsZero,
            "invalid governance controller");
        Storage.Put(new byte[] { KeyGovernanceController }, governanceController);
        OnGovernanceControllerChanged(governanceController);
    }

    /// <summary>Look up the wired GovernanceController hash, or <see cref="UInt160.Zero"/>
    /// if not yet set.</summary>
    [Safe]
    public static UInt160 GetGovernanceController()
    {
        var raw = Storage.Get(new byte[] { KeyGovernanceController });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Owner-only: authorize a contract to pause chains after proving a protocol fault.</summary>
    public static void RegisterPauser(UInt160 pauser)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(pauser.IsValid && !pauser.IsZero, "invalid pauser");
        Storage.Put(PauserKey(pauser), new byte[] { 1 });
        OnPauserRegistered(pauser);
    }

    /// <summary>Owner-only: revoke a chain-pauser contract.</summary>
    public static void RevokePauser(UInt160 pauser)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Delete(PauserKey(pauser));
        OnPauserRevoked(pauser);
    }

    /// <summary>True when <paramref name="who"/> may pause chains without owner witness.</summary>
    [Safe]
    public static bool IsPauser(UInt160 who) => Storage.Get(PauserKey(who)) != null;

    /// <summary>
    /// Permissionless / semi-permissionless registration path (§16.1). Reads the admission
    /// mode from <see cref="GetGovernanceController"/>:
    /// <list type="bullet">
    ///   <item><description>mode 0 (permissioned) → reject with a clear "use RegisterChain" hint</description></item>
    ///   <item><description>mode 1 (semi-permissionless) → defer until the GovernanceController
    ///   approved-verifier / approved-bridge sets are wired (§16.1-approved-sets in the
    ///   plan)</description></item>
    ///   <item><description>mode 2 (permissionless) → any caller, with the standard
    ///   chainId / size / consistency checks</description></item>
    /// </list>
    /// </summary>
    public static void RegisterChainPublic(uint chainId, byte[] configBytes)
    {
        ExecutionEngine.Assert(Storage.Get(ConfigKey(chainId)) == null,
            "chain already registered — use owner-governed UpdateChain");
        var gc = GetGovernanceController();
        ExecutionEngine.Assert(gc != UInt160.Zero,
            "governance controller not wired — owner must call SetGovernanceController first");
        var mode = (byte)(BigInteger)Contract.Call(gc, "getAdmissionMode",
            CallFlags.ReadOnly, new object[0]);
        if (mode == 0)
        {
            ExecutionEngine.Assert(false,
                "admission mode = permissioned; use RegisterChain (owner-only)");
        }
        else if (mode == 1)
        {
            // semi-permissionless: any caller, but the L2's declared verifier + bridgeAdapter
            // must both be in the GovernanceController's approved sets. The config layout
            // is "[4B chainId][20B operator][20B verifier][20B bridge][20B msg]...":
            // verifier at offset 24..43, bridge at 44..63.
            ExecutionEngine.Assert(configBytes.Length >= 64, "config too short for verifier+bridge read");
            var verifierBytes = new byte[20];
            var bridgeBytes = new byte[20];
            for (var i = 0; i < 20; i++) verifierBytes[i] = configBytes[24 + i];
            for (var i = 0; i < 20; i++) bridgeBytes[i] = configBytes[44 + i];
            var verifier = (UInt160)verifierBytes;
            var bridge = (UInt160)bridgeBytes;

            var verifierApproved = (bool)Contract.Call(gc, "isApprovedVerifier",
                CallFlags.ReadOnly, new object[] { verifier });
            ExecutionEngine.Assert(verifierApproved,
                "verifier not in GovernanceController approved set (semi-permissionless mode)");
            var bridgeApproved = (bool)Contract.Call(gc, "isApprovedBridgeAdapter",
                CallFlags.ReadOnly, new object[] { bridge });
            ExecutionEngine.Assert(bridgeApproved,
                "bridge adapter not in GovernanceController approved set (semi-permissionless mode)");
        }
        // mode 2 (permissionless) falls through to the same write path.
        WriteChainConfig(chainId, configBytes);
    }

    private static void WriteChainConfig(uint chainId, byte[] configBytes)
    {
        // chainId 0 is the L1 sentinel (see L2Outbox.L1ChainId) — registering a chain
        // with id 0 would silently break L2→L2 routing for every other chain.
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(configBytes.Length == ConfigSize, "config size mismatch");
        ExecutionEngine.Assert(ReadChainId(configBytes) == chainId, "chainId mismatch");
        AssertSecurityConfigurationCompatible(configBytes);

        var key = ConfigKey(chainId);
        var existing = Storage.Get(key);
        Storage.Put(key, configBytes);
        if (existing == null)
            Storage.Put(IndexKey(chainId), new byte[] { 1 });

        OnChainRegistered(chainId, configBytes);
    }

    /// <summary>
    /// Update an already-registered chain's config. Owner only — the instant bootstrap path.
    /// <para>
    /// SECURITY NOTE: this is an instant, owner-witness-only mutation — it has NO timelock
    /// and NO council-veto gate, so it can rewrite a chain's verifier / securityLevel /
    /// daMode / active byte in a single block. To close the rogue-owner hole, the operator
    /// MUST call <see cref="LockGovernance"/> at production launch: once locked this instant
    /// path reverts and config mutations are forced through the council multisig + timelock
    /// (<see cref="UpdateChainViaProposal"/>). For any production deployment the owner SHOULD
    /// additionally be the <c>GovernanceController</c> contract hash, not a single EOA.
    /// </para>
    /// </summary>
    public static void UpdateChain(uint chainId, byte[] configBytes)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(!IsGovernanceLocked(),
            "governance locked — instant owner path disabled; use UpdateChainViaProposal");
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(configBytes.Length == ConfigSize, "config size mismatch");
        ExecutionEngine.Assert(ReadChainId(configBytes) == chainId, "chainId mismatch");
        AssertSecurityConfigurationCompatible(configBytes);
        ExecutionEngine.Assert(Storage.Get(ConfigKey(chainId)) != null, "chain not registered");

        Storage.Put(ConfigKey(chainId), configBytes);
        OnChainRegistered(chainId, configBytes);
    }

    /// <summary>
    /// Council-veto path for updating a chain config — the only way to mutate a chain's
    /// verifier / securityLevel / daMode / active byte once <see cref="LockGovernance"/> has
    /// closed the instant owner path. The proposalId must be approved by the council multisig
    /// AND have cleared the timelock (consulted on the wired GovernanceController), then this
    /// call is replay-protected per proposalId and bound to the EXACT (chainId, configBytes)
    /// via <see cref="BuildUpdateChainAction"/> so council members vote on the precise config,
    /// not opaque bytes that could be repurposed. Anyone may submit; authority is the
    /// proposal's approval state, not the caller's witness.
    /// </summary>
    public static void UpdateChainViaProposal(uint chainId, byte[] configBytes, ulong proposalId)
    {
        var gc = GetGovernanceController();
        ExecutionEngine.Assert(gc != UInt160.Zero,
            "governance controller not wired — owner must call SetGovernanceController first");
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(configBytes.Length == ConfigSize, "config size mismatch");
        ExecutionEngine.Assert(ReadChainId(configBytes) == chainId, "chainId mismatch");
        AssertSecurityConfigurationCompatible(configBytes);
        ExecutionEngine.Assert(Storage.Get(ConfigKey(chainId)) != null, "chain not registered");

        var consumedKey = ConsumedUpdateProposalKey(proposalId);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "proposal already consumed");

        var ok = (bool)Contract.Call(gc, "isApprovedAndTimelocked",
            CallFlags.ReadOnly, new object[] { proposalId });
        ExecutionEngine.Assert(ok,
            "proposal not approved + timelocked (council multisig + timelock not satisfied)");

        // Bind the proposal payload to (chainId, configBytes) so an approved proposal can't be
        // applied with a different config than the council voted on.
        var expectedAction = BuildUpdateChainAction(chainId, configBytes);
        var bound = (bool)Contract.Call(gc, "matchesProposalPayload",
            CallFlags.ReadOnly, new object[] { proposalId, expectedAction });
        ExecutionEngine.Assert(bound,
            "proposal payload does not match (chainId, configBytes) action args (council voted on different bytes)");

        Storage.Put(consumedKey, new byte[] { 1 });
        Storage.Put(ConfigKey(chainId), configBytes);
        OnChainRegistered(chainId, configBytes);
    }

    /// <summary>
    /// Permanently disable the instant owner-only <see cref="UpdateChain"/> path so chain
    /// config mutations must go through the council multisig + timelock
    /// (<see cref="UpdateChainViaProposal"/>). Owner only; one-way (there is no unlock).
    /// Idempotent — re-locking is a no-op. The GovernanceController must be wired first
    /// (<see cref="SetGovernanceController"/>) so the proposal path is usable once the instant
    /// path is closed.
    /// </summary>
    public static void LockGovernance()
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(GetGovernanceController() != UInt160.Zero,
            "wire GovernanceController before locking — else no chain config could ever be updated");
        var key = new byte[] { KeyGovernanceLocked };
        if (Storage.Get(key) == null)
        {
            Storage.Put(key, new byte[] { 1 });
            OnGovernanceLocked();
        }
    }

    /// <summary>True once <see cref="LockGovernance"/> has been called — the instant owner
    /// <see cref="UpdateChain"/> path is then permanently disabled.</summary>
    [Safe]
    public static bool IsGovernanceLocked()
    {
        return Storage.Get(new byte[] { KeyGovernanceLocked }) != null;
    }

    /// <summary>
    /// Canonical encoding for an "update chain" action — what the council votes on when they
    /// create the proposal that <see cref="UpdateChainViaProposal"/> executes. Off-chain
    /// tooling computes this and submits it as the proposal payload via the
    /// GovernanceController's <c>CreateProposal</c>; the execution call re-derives the same
    /// bytes from its args and asserts byte-equality against the stored payload. Layout:
    /// <c>"neo4-gov:updateChain" || chainId(4B LE) || configBytes(ConfigSize)</c>. The
    /// "neo4-gov:" prefix + distinct method id prevents cross-method payload reuse.
    /// </summary>
    [Safe]
    public static byte[] BuildUpdateChainAction(uint chainId, byte[] configBytes)
    {
        var tag = ActionTagUpdateChain;
        var buf = new byte[tag.Length + 4 + configBytes.Length];
        for (var i = 0; i < tag.Length; i++) buf[i] = tag[i];
        buf[tag.Length] = (byte)chainId;
        buf[tag.Length + 1] = (byte)(chainId >> 8);
        buf[tag.Length + 2] = (byte)(chainId >> 16);
        buf[tag.Length + 3] = (byte)(chainId >> 24);
        for (var i = 0; i < configBytes.Length; i++) buf[tag.Length + 4 + i] = configBytes[i];
        return buf;
    }

    // ASCII bytes for the "neo4-gov:updateChain" action tag (20 bytes). Kept as a const
    // byte[] (not a string) so the on-chain bytecode is the literal byte sequence — same
    // idiom as the GovernanceController action tags.
    private static readonly byte[] ActionTagUpdateChain = new byte[]
    {
        (byte)'n', (byte)'e', (byte)'o', (byte)'4', (byte)'-',
        (byte)'g', (byte)'o', (byte)'v', (byte)':',
        (byte)'u', (byte)'p', (byte)'d', (byte)'a', (byte)'t', (byte)'e',
        (byte)'C', (byte)'h', (byte)'a', (byte)'i', (byte)'n'
    };

    private static byte[] ConsumedUpdateProposalKey(ulong proposalId)
    {
        var k = new byte[1 + 8];
        k[0] = PrefixConsumedUpdateProposal;
        k[1] = (byte)proposalId; k[2] = (byte)(proposalId >> 8);
        k[3] = (byte)(proposalId >> 16); k[4] = (byte)(proposalId >> 24);
        k[5] = (byte)(proposalId >> 32); k[6] = (byte)(proposalId >> 40);
        k[7] = (byte)(proposalId >> 48); k[8] = (byte)(proposalId >> 56);
        return k;
    }

    /// <summary>
    /// Validate the public security label and DA mode together before storing a chain config.
    /// Validity is a ZK rollup and therefore requires L1 DA; Validium uses the same ZK proof
    /// requirement with off-chain DA (NeoFS, external DA, or DAC). Rejecting contradictory
    /// labels here prevents a chain from advertising stronger guarantees than its DA mode
    /// provides. See doc.md §12 and §16.2.
    /// </summary>
    private static void AssertSecurityConfigurationCompatible(byte[] configBytes)
    {
        var securityLevel = configBytes[OffsetSecurityLevel];
        var daMode = configBytes[OffsetDAMode];

        ExecutionEngine.Assert(securityLevel <= SecurityLevelValidium,
            "securityLevel must be 0..4 (Sidechain/Settled/Optimistic/Validity/Validium)");
        ExecutionEngine.Assert(daMode <= 3,
            "daMode must be 0..3 (L1/NeoFS/External/DAC)");

        if (securityLevel == SecurityLevelValidity)
            ExecutionEngine.Assert(daMode == DAModeL1,
                "Validity security level requires L1 DA");

        if (securityLevel == SecurityLevelValidium)
            ExecutionEngine.Assert(daMode != DAModeL1,
                "Validium security level requires off-chain DA");
    }

    /// <summary>Pause a chain. Owner only. Sets active=false in stored config.</summary>
    public static void PauseChain(uint chainId)
    {
        var caller = Runtime.CallingScriptHash;
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()) || IsPauser(caller),
            "not authorized");
        var key = ConfigKey(chainId);
        var raw = Storage.Get(key);
        ExecutionEngine.Assert(raw != null, "chain not registered");
        // raw is a NeoVM ByteString (immutable) — index-assigning it FAULTs at runtime
        // ("Invalid type for SETITEM: ByteString"), which would break the chain-pause /
        // censorship-pause path entirely. Copy into a fresh mutable buffer and clear the active
        // byte (the very last byte of the encoded config) there.
        var src = (byte[])raw!;
        var bytes = new byte[ConfigSize];
        for (var i = 0; i < ConfigSize; i++) bytes[i] = src[i];
        bytes[ConfigSize - 1] = 0;
        Storage.Put(key, bytes);
        OnChainPaused(chainId);
    }

    /// <summary>Resume a paused chain. Owner only.</summary>
    public static void ResumeChain(uint chainId)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        var key = ConfigKey(chainId);
        var raw = Storage.Get(key);
        ExecutionEngine.Assert(raw != null, "chain not registered");
        // Copy into a fresh mutable buffer (raw is an immutable ByteString — see PauseChain) and
        // set the active byte there.
        var src = (byte[])raw!;
        var bytes = new byte[ConfigSize];
        for (var i = 0; i < ConfigSize; i++) bytes[i] = src[i];
        bytes[ConfigSize - 1] = 1;
        Storage.Put(key, bytes);
        OnChainResumed(chainId);
    }

    /// <summary>Read the canonical encoded L2ChainConfig. Empty bytes if not registered.</summary>
    [Safe]
    public static byte[] GetChainConfig(uint chainId)
    {
        var raw = Storage.Get(ConfigKey(chainId));
        return raw == null ? new byte[0] : (byte[])raw;
    }

    /// <summary>True if chainId is registered AND active=1.</summary>
    [Safe]
    public static bool IsActive(uint chainId)
    {
        var raw = Storage.Get(ConfigKey(chainId));
        if (raw == null) return false;
        var bytes = (byte[])raw;
        return bytes[ConfigSize - 1] == 1;
    }

    /// <summary>Read the securityLevel byte (doc.md §12). Values are Sidechain(0), Settled(1),
    /// Optimistic(2), Validity(3), and Validium(4). Returns 0 for an unregistered chain; this is
    /// safe because SettlementManager rejects unregistered chains through its <c>isActive</c>
    /// check before evaluating proof compatibility.</summary>
    [Safe]
    public static byte GetSecurityLevel(uint chainId)
    {
        var raw = Storage.Get(ConfigKey(chainId));
        if (raw == null) return 0;
        return ((byte[])raw)[OffsetSecurityLevel];
    }

    /// <summary>Read the daMode byte (doc.md §12.1). Returns 0 (L1 — the highest-security
    /// default) if the chain is not registered.</summary>
    [Safe]
    public static byte GetDAMode(uint chainId)
    {
        var raw = Storage.Get(ConfigKey(chainId));
        if (raw == null) return 0;
        return ((byte[])raw)[OffsetDAMode];
    }

    /// <summary>Read the gatewayEnabled flag. Returns false if the chain is not registered.</summary>
    [Safe]
    public static bool GetGatewayEnabled(uint chainId)
    {
        var raw = Storage.Get(ConfigKey(chainId));
        if (raw == null) return false;
        return ((byte[])raw)[OffsetGatewayEnabled] == 1;
    }

    /// <summary>Read the permissionlessExit flag. Returns false if the chain is not registered
    /// (defaults to operator-gated for safety).</summary>
    [Safe]
    public static bool GetPermissionlessExit(uint chainId)
    {
        var raw = Storage.Get(ConfigKey(chainId));
        if (raw == null) return false;
        return ((byte[])raw)[OffsetPermissionlessExit] == 1;
    }

    /// <summary>Read the sequencerModel byte (doc.md §16.2). Returns 0 (Centralized) if
    /// the chain is not registered.</summary>
    [Safe]
    public static byte GetSequencerModel(uint chainId)
    {
        var raw = Storage.Get(ConfigKey(chainId));
        if (raw == null) return 0;
        return ((byte[])raw)[OffsetSequencerModel];
    }

    /// <summary>Read the exitModel byte (doc.md §16.2). Returns 0 (Permissionless) if
    /// the chain is not registered.</summary>
    [Safe]
    public static byte GetExitModel(uint chainId)
    {
        var raw = Storage.Get(ConfigKey(chainId));
        if (raw == null) return 0;
        return ((byte[])raw)[OffsetExitModel];
    }

    private static byte[] ConfigKey(uint chainId)
    {
        var key = new byte[5];
        key[0] = PrefixConfig;
        key[1] = (byte)chainId;
        key[2] = (byte)(chainId >> 8);
        key[3] = (byte)(chainId >> 16);
        key[4] = (byte)(chainId >> 24);
        return key;
    }

    private static byte[] IndexKey(uint chainId)
    {
        var key = new byte[5];
        key[0] = PrefixChainIndex;
        key[1] = (byte)chainId;
        key[2] = (byte)(chainId >> 8);
        key[3] = (byte)(chainId >> 16);
        key[4] = (byte)(chainId >> 24);
        return key;
    }

    private static byte[] PauserKey(UInt160 pauser)
    {
        var key = new byte[1 + 20];
        key[0] = PrefixPauser;
        var bytes = (byte[])pauser;
        for (var i = 0; i < 20; i++) key[1 + i] = bytes[i];
        return key;
    }

    private static uint ReadChainId(byte[] bytes)
    {
        return (uint)bytes[0]
            | ((uint)bytes[1] << 8)
            | ((uint)bytes[2] << 16)
            | ((uint)bytes[3] << 24);
    }
}
