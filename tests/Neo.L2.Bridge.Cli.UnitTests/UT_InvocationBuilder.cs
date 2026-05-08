using System;
using System.Numerics;
using Neo;
using Neo.L2.Bridge.Cli.Commands;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.L2.Bridge.Cli.UnitTests;

/// <summary>
/// Tests for the canonical invocation-script builders. Pins:
///   1. Scripts are non-empty + start with the known PUSH-call opcode pattern.
///   2. The script reproduces byte-identically for the same input (so the
///      operator's wallet sees the same script across runs).
///   3. Argument validation rejects the spec-impossible cases (zero amount,
///      zero target chain, etc.) at script-build time, not at wallet-submit time.
/// </summary>
[TestClass]
public class UT_InvocationBuilder
{
    private static readonly UInt160 BridgeHash = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 AssetHash = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 RecipientHash = UInt160.Parse("0x" + new string('c', 40));
    private static readonly UInt256 LeafHash = UInt256.Parse("0x" + new string('d', 64));
    private const uint TestChainId = 1099;

    [TestMethod]
    public void Deposit_ProducesNonEmptyScript_AndReproducible()
    {
        var s1 = InvocationBuilder.BuildDeposit(BridgeHash, AssetHash, 1_000_000, TestChainId, RecipientHash);
        var s2 = InvocationBuilder.BuildDeposit(BridgeHash, AssetHash, 1_000_000, TestChainId, RecipientHash);
        Assert.IsTrue(s1.Length > 0);
        CollectionAssert.AreEqual(s1, s2, "identical inputs MUST produce byte-identical scripts");
    }

    [TestMethod]
    public void Deposit_DifferentAmounts_ProduceDifferentScripts()
    {
        // Sanity: different inputs MUST emit different scripts (catches a regression
        // where the builder ignored an argument).
        var sA = InvocationBuilder.BuildDeposit(BridgeHash, AssetHash, 1_000_000, TestChainId, RecipientHash);
        var sB = InvocationBuilder.BuildDeposit(BridgeHash, AssetHash, 2_000_000, TestChainId, RecipientHash);
        CollectionAssert.AreNotEqual(sA, sB);
    }

    [TestMethod]
    public void Deposit_RejectsZeroAmount()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            InvocationBuilder.BuildDeposit(BridgeHash, AssetHash, 0, TestChainId, RecipientHash));
        Assert.ThrowsExactly<ArgumentException>(() =>
            InvocationBuilder.BuildDeposit(BridgeHash, AssetHash, -1, TestChainId, RecipientHash));
    }

    [TestMethod]
    public void Deposit_RejectsZeroTargetChain()
    {
        // Mirrors the contract's guard: chainId 0 is reserved for L1; depositing to it
        // would lock tokens with no L2 to consume them.
        Assert.ThrowsExactly<ArgumentException>(() =>
            InvocationBuilder.BuildDeposit(BridgeHash, AssetHash, 1, targetChainId: 0, RecipientHash));
    }

    [TestMethod]
    public void Deposit_ScriptDecodesToCorrectMethodName()
    {
        // Round-trip: parse the script back and verify the embedded method-name string is "deposit".
        var script = InvocationBuilder.BuildDeposit(BridgeHash, AssetHash, 1_000_000, TestChainId, RecipientHash);
        Assert.IsTrue(ContainsMethodName(script, "deposit"),
            "deposit script must contain the embedded method name 'deposit'");
    }

    [TestMethod]
    public void FinalizeWithdrawalWithProof_ProducesNonEmptyScript_AndReproducible()
    {
        var siblings = new[] {
            UInt256.Parse("0x" + new string('1', 64)),
            UInt256.Parse("0x" + new string('2', 64)),
        };
        var s1 = InvocationBuilder.BuildFinalizeWithdrawalWithProof(
            BridgeHash, TestChainId, batchNumber: 5, LeafHash, siblings, leafIndex: 1,
            AssetHash, RecipientHash, amount: 12345);
        var s2 = InvocationBuilder.BuildFinalizeWithdrawalWithProof(
            BridgeHash, TestChainId, batchNumber: 5, LeafHash, siblings, leafIndex: 1,
            AssetHash, RecipientHash, amount: 12345);
        Assert.IsTrue(s1.Length > 0);
        CollectionAssert.AreEqual(s1, s2);
        Assert.IsTrue(ContainsMethodName(s1, "finalizeWithdrawalWithProof"));
    }

    [TestMethod]
    public void FinalizeWithdrawalWithProof_DifferentSiblingCount_DifferentScript()
    {
        // Catch a regression where the siblings argument is ignored. Tree depth 1 vs 3
        // produces meaningfully different scripts.
        var depth1 = new[] { UInt256.Parse("0x" + new string('1', 64)) };
        var depth3 = new[] {
            UInt256.Parse("0x" + new string('1', 64)),
            UInt256.Parse("0x" + new string('2', 64)),
            UInt256.Parse("0x" + new string('3', 64)),
        };
        var s1 = InvocationBuilder.BuildFinalizeWithdrawalWithProof(
            BridgeHash, TestChainId, 5, LeafHash, depth1, 0, AssetHash, RecipientHash, 1);
        var s3 = InvocationBuilder.BuildFinalizeWithdrawalWithProof(
            BridgeHash, TestChainId, 5, LeafHash, depth3, 0, AssetHash, RecipientHash, 1);
        CollectionAssert.AreNotEqual(s1, s3);
    }

    [TestMethod]
    public void FinalizeWithdrawalWithProof_RejectsZeroChainId()
    {
        var siblings = new[] { UInt256.Parse("0x" + new string('1', 64)) };
        Assert.ThrowsExactly<ArgumentException>(() =>
            InvocationBuilder.BuildFinalizeWithdrawalWithProof(
                BridgeHash, chainId: 0, 5, LeafHash, siblings, 0, AssetHash, RecipientHash, 1));
    }

    [TestMethod]
    public void FinalizeWithdrawalWithProof_RejectsZeroAmount()
    {
        var siblings = new[] { UInt256.Parse("0x" + new string('1', 64)) };
        Assert.ThrowsExactly<ArgumentException>(() =>
            InvocationBuilder.BuildFinalizeWithdrawalWithProof(
                BridgeHash, TestChainId, 5, LeafHash, siblings, 0, AssetHash, RecipientHash, amount: 0));
    }

    /// <summary>Search for the exact ASCII method name embedded in the script as PUSHDATA bytes.</summary>
    private static bool ContainsMethodName(byte[] script, string name)
    {
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        if (nameBytes.Length > script.Length) return false;
        for (var i = 0; i <= script.Length - nameBytes.Length; i++)
        {
            var match = true;
            for (var j = 0; j < nameBytes.Length; j++)
                if (script[i + j] != nameBytes[j]) { match = false; break; }
            if (match) return true;
        }
        return false;
    }
}
