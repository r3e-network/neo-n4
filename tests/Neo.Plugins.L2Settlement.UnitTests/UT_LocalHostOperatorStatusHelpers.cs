using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.L2;
using Neo.Plugins.L2Settlement;

namespace Neo.Plugins.L2Settlement.UnitTests;

[TestClass]
public sealed class UT_LocalHostOperatorStatusHelpers
{
    [TestMethod]
    public void IsSecurityLevelPairedWithProofType_RecommendedPairings()
    {
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Validity, ProofType.Zk));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Validium, ProofType.Zk));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Optimistic, ProofType.Optimistic));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Optimistic, ProofType.Multisig));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Sidechain, ProofType.Multisig));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Settled, ProofType.None));

        Assert.IsFalse(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Validity, ProofType.Multisig));
        Assert.IsFalse(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Optimistic, ProofType.Zk));
        Assert.IsFalse(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Sidechain, ProofType.Optimistic));
    }

    [TestMethod]
    public void IsSecurityLevelPairedWithDaMode_RecommendedPairings()
    {
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Validity, DAMode.L1));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Validium, DAMode.NeoFS));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Validium, DAMode.External));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Validium, DAMode.DAC));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Optimistic, DAMode.Local));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Settled, DAMode.L1));

        Assert.IsFalse(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Validity, DAMode.NeoFS));
        Assert.IsFalse(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Validium, DAMode.L1));
        Assert.IsFalse(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Validium, DAMode.Local));
    }
}
