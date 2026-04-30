using System;
using System.ComponentModel;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.VerifierRegistry;

/// <summary>
/// Registers verifier contracts per <c>ProofType</c> and dispatches commitment verification
/// requests from <see cref="NeoHub.SettlementManager"/>. See doc.md §3.2 (VerifierRegistry).
/// </summary>
[DisplayName("NeoHub.VerifierRegistry")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Pluggable proof verifier dispatch table for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/neo-project/neo4/tree/master/contracts/NeoHub.VerifierRegistry")]
[ContractPermission(Permission.Any, Method.Any)]
public class VerifierRegistryContract : SmartContract
{
    private const byte PrefixVerifier = 0x01;   // 0x01 + proofType(1B) → UInt160 verifier
    private const byte KeyOwner = 0xFF;

    /// <summary>
    /// Offset of the proofType byte inside a canonical L2BatchCommitment encoding.
    /// Matches Neo.L2.Batch.BatchSerializer: 4+8+8+8 + 9*32 = 316.
    /// </summary>
    public const int ProofTypeOffset = 316;

    /// <summary>Emitted whenever a verifier is registered or replaced.</summary>
    [DisplayName("VerifierRegistered")]
    public static event Action<byte, UInt160> OnVerifierRegistered = default!;

    /// <summary>Set the initial owner.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var owner = (UInt160)data;
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        Storage.Put(new byte[] { KeyOwner }, owner);
    }

    /// <summary>Governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Bind a verifier contract to a <c>ProofType</c>. Owner only.</summary>
    public static void RegisterVerifier(byte proofType, UInt160 verifier)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(verifier.IsValid && !verifier.IsZero, "invalid verifier");
        Storage.Put(new byte[] { PrefixVerifier, proofType }, verifier);
        OnVerifierRegistered(proofType, verifier);
    }

    /// <summary>Look up the registered verifier for a <c>ProofType</c>.</summary>
    [Safe]
    public static UInt160 GetVerifier(byte proofType)
    {
        var raw = Storage.Get(new byte[] { PrefixVerifier, proofType });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>
    /// Dispatch verification by reading the proofType byte from the commitment and forwarding
    /// to the registered verifier. Returns the verifier's bool result. Used by SettlementManager.
    /// </summary>
    [Safe]
    public static bool VerifyCommitment(byte[] commitmentBytes)
    {
        ExecutionEngine.Assert(commitmentBytes.Length > ProofTypeOffset, "commitment too small");
        var proofType = commitmentBytes[ProofTypeOffset];
        var verifier = GetVerifier(proofType);
        ExecutionEngine.Assert(!verifier.IsZero, "no verifier for proof type");
        return (bool)Contract.Call(verifier, "verify", CallFlags.ReadOnly, new object[] { commitmentBytes });
    }
}
