using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.L2.Audit;

namespace Neo.L2.Batch.FormalVerification;

/// <summary>
/// Regression coverage for the production relative state-root continuity check.
/// This does not prove execution correctness or anchor the first batch to genesis.
/// </summary>
/// <remarks>See doc.md §7.3. Finite fixtures do not establish statistical confidence.</remarks>
[TestClass]
public class UT_ExtendedVerification_Properties
{
    [TestMethod]
    public async Task ContinuityCheck_AcceptsLinkedRoots_ReportsEachBrokenLink()
    {
        const int batchCount = 32;
        var batches = new L2BatchCommitment[batchCount];
        var previousRoot = UInt256.Zero;
        for (var index = 0; index < batchCount; index++)
        {
            var rootBytes = new byte[32];
            rootBytes[index] = (byte)(index + 1);
            var postStateRoot = new UInt256(rootBytes);
            batches[index] = new L2BatchCommitment
            {
                ChainId = 1,
                BatchNumber = (ulong)index + 1,
                FirstBlock = (ulong)index * 100,
                LastBlock = (ulong)index * 100 + 99,
                PreStateRoot = previousRoot,
                PostStateRoot = postStateRoot,
                TxRoot = UInt256.Zero,
                ReceiptRoot = UInt256.Zero,
                WithdrawalRoot = UInt256.Zero,
                L2ToL1MessageRoot = UInt256.Zero,
                L2ToL2MessageRoot = UInt256.Zero,
                DACommitment = UInt256.Zero,
                PublicInputHash = UInt256.Zero,
                ProofType = ProofType.Optimistic,
                Proof = Array.Empty<byte>(),
            };
            previousRoot = postStateRoot;
        }

        var check = new ContinuityCheck();
        var validFindings = await check.RunAsync(batches);
        Assert.AreEqual(1, validFindings.Count);
        Assert.IsTrue(validFindings[0].Passed);
        Assert.AreEqual(check.Name, validFindings[0].Check);

        for (var index = 1; index < batchCount; index++)
        {
            var broken = (L2BatchCommitment[])batches.Clone();
            var changedRoot = batches[index].PreStateRoot.GetSpan().ToArray();
            changedRoot[index % changedRoot.Length] ^= 0x80;
            broken[index] = batches[index] with { PreStateRoot = new UInt256(changedRoot) };

            var findings = await check.RunAsync(broken);
            Assert.AreEqual(1, findings.Count, $"Broken link at index {index}");
            Assert.IsFalse(findings[0].Passed);
            Assert.AreEqual(check.Name, findings[0].Check);
            Assert.AreEqual(batches[index].BatchNumber, findings[0].BatchNumber);
            StringAssert.Contains(findings[0].Detail, "preStateRoot");
        }
    }
}
