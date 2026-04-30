namespace Neo.L2.UnitTests;

/// <summary>
/// Type-shape tests: the interfaces are public and the surface matches doc.md §19.
/// </summary>
[TestClass]
public class UT_Interfaces
{
    [TestMethod]
    public void IL2BatchExecutor_IsPublic()
    {
        var t = typeof(IL2BatchExecutor);
        Assert.IsTrue(t.IsInterface);
        Assert.IsTrue(t.IsPublic);
        Assert.IsNotNull(t.GetMethod("ApplyBatchAsync"));
    }

    [TestMethod]
    public void IL2ProofVerifier_IsPublic()
    {
        var t = typeof(IL2ProofVerifier);
        Assert.IsTrue(t.IsInterface);
        Assert.IsNotNull(t.GetMethod("VerifyAsync"));
        Assert.IsNotNull(t.GetProperty("Kind"));
    }

    [TestMethod]
    public void IL2Prover_IsPublic()
    {
        var t = typeof(IL2Prover);
        Assert.IsTrue(t.IsInterface);
        Assert.IsNotNull(t.GetMethod("ProveAsync"));
        Assert.IsNotNull(t.GetProperty("Kind"));
    }

    [TestMethod]
    public void IDAWriter_IsPublic()
    {
        var t = typeof(IDAWriter);
        Assert.IsTrue(t.IsInterface);
        Assert.IsNotNull(t.GetMethod("PublishAsync"));
        Assert.IsNotNull(t.GetMethod("IsAvailableAsync"));
        Assert.IsNotNull(t.GetProperty("Mode"));
    }

    [TestMethod]
    public void IMessageRouter_IsPublic()
    {
        var t = typeof(IMessageRouter);
        Assert.IsTrue(t.IsInterface);
        Assert.IsNotNull(t.GetMethod("DequeueL1MessagesAsync"));
        Assert.IsNotNull(t.GetMethod("EnqueueOutboundAsync"));
        Assert.IsNotNull(t.GetMethod("GetMessageProofAsync"));
    }

    [TestMethod]
    public void ISettlementClient_IsPublic()
    {
        var t = typeof(ISettlementClient);
        Assert.IsTrue(t.IsInterface);
        Assert.IsNotNull(t.GetMethod("SubmitBatchAsync"));
        Assert.IsNotNull(t.GetMethod("GetCanonicalStateRootAsync"));
        Assert.IsNotNull(t.GetMethod("GetBatchStatusAsync"));
    }

    [TestMethod]
    public void IBridgeAdapter_IsPublic()
    {
        var t = typeof(IBridgeAdapter);
        Assert.IsTrue(t.IsInterface);
        Assert.IsNotNull(t.GetMethod("ApplyDepositAsync"));
        Assert.IsNotNull(t.GetMethod("EnqueueWithdrawalAsync"));
        Assert.IsNotNull(t.GetMethod("ResolveCanonicalAsync"));
    }

    [TestMethod]
    public void BatchStatus_HasFiveStates()
    {
        Assert.AreEqual(5, Enum.GetValues<BatchStatus>().Length);
    }
}
