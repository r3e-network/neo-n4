using Neo.L2.Executor.ProofWitness;

namespace Neo.L2.Executor.UnitTests;

/// <summary>Content-equality coverage for canonical proof-witness execution outputs.</summary>
[TestClass]
public sealed class UT_ProofWitnessExecution
{
    [TestMethod]
    public void Equality_UsesCanonicalByteContent()
    {
        var left = Result(new byte[] { 1, 2 }, new byte[] { 3, 4 });
        var right = Result(new byte[] { 1, 2 }, new byte[] { 3, 4 });

        Assert.AreEqual(left, right);
        Assert.AreEqual(left.GetHashCode(), right.GetHashCode());
        Assert.IsFalse(left.Equals(null));
    }

    [TestMethod]
    public void Equality_RejectsDifferentStateOrEffects()
    {
        var baseline = Result(new byte[] { 1, 2 }, new byte[] { 3, 4 });

        Assert.AreNotEqual(baseline, Result(new byte[] { 1, 9 }, new byte[] { 3, 4 }));
        Assert.AreNotEqual(baseline, Result(new byte[] { 1, 2 }, new byte[] { 3, 9 }));
        Assert.AreNotEqual(
            baseline,
            baseline with { WitnessAuthenticated = false });
        Assert.AreNotEqual(
            baseline,
            baseline with { ExecutionSemanticId = UInt256.Zero });
        Assert.AreNotEqual(
            baseline,
            baseline with
            {
                ExecutionResult = baseline.ExecutionResult with { GasConsumed = 8 },
            });
    }

    private static ProofWitnessExecutionResult Result(byte[] stateWitness, byte[] effects) => new()
    {
        ExecutionResult = new BatchExecutionResult
        {
            PostStateRoot = Hash('1'),
            ReceiptRoot = Hash('2'),
            WithdrawalRoot = Hash('3'),
            L2ToL1MessageRoot = Hash('4'),
            L2ToL2MessageRoot = Hash('5'),
            TxRoot = Hash('6'),
            GasConsumed = 7,
        },
        ExecutionSemanticId = Hash('a'),
        WitnessAuthenticated = true,
        StateWitness = stateWitness,
        Effects = effects,
    };

    private static UInt256 Hash(char value) => UInt256.Parse("0x" + new string(value, 64));
}
