using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo;
using Neo.Cryptography;
using Neo.L2.Settlement.Rpc;
using Neo.L2.State;

namespace Neo.L2.Batch.FormalVerification;

/// <summary>Sampled production encoding and challenge-client guard regressions.</summary>
/// <remarks>
/// See doc.md §8 and §17. These checks do not verify governance transitions, committee
/// rotation or on-chain challenge timing. Contract-level evidence belongs in
/// NeoHub.Contracts.VmTests; local transition tables and counters are not evidence
/// that the deployed contracts enforce those properties.
/// </remarks>
[TestClass]
public class UT_Phase4GovernanceProperties
{
    [TestMethod]
    public void PublicInputs_CanonicalEncodingMatchesProductionHash()
    {
        var rng = new Random(16000);
        for (var index = 0; index < 200; index++)
        {
            var first = NextUInt64(rng);
            var last = NextUInt64(rng);
            var inputs = new PublicInputs
            {
                ChainId = (uint)NextUInt64(rng),
                BatchNumber = NextUInt64(rng),
                FirstBlock = Math.Min(first, last),
                LastBlock = Math.Max(first, last),
                PreStateRoot = NextRoot(rng),
                PostStateRoot = NextRoot(rng),
                TxRoot = NextRoot(rng),
                ReceiptRoot = NextRoot(rng),
                WithdrawalRoot = NextRoot(rng),
                L2ToL1MessageRoot = NextRoot(rng),
                L2ToL2MessageRoot = NextRoot(rng),
                L1MessageHash = NextRoot(rng),
                DACommitment = NextRoot(rng),
                BlockContextHash = NextRoot(rng),
                ForcedInclusionCount = (uint)NextUInt64(rng),
            };

            var encoded = BatchSerializer.EncodePublicInputs(inputs);
            Assert.AreEqual(352, encoded.Length);
            Assert.AreEqual(inputs, BatchSerializer.DecodePublicInputs(encoded));
            Assert.AreEqual(new UInt256(Crypto.Hash256(encoded)),
                StateRootCalculator.HashPublicInputs(inputs));
            for (var offset = 0; offset < 4; offset++)
                Assert.AreEqual((byte)(inputs.ForcedInclusionCount >> (8 * offset)), encoded[348 + offset]);
        }
    }

    [TestMethod]
    public async Task ChallengeClient_RejectsZeroIdentityBeforeNetworkAccess()
    {
        using var rpc = new JsonRpcClient("http://127.0.0.1:1");
        Assert.ThrowsExactly<ArgumentException>(() => new RpcOptimisticChallengeClient(
            rpc, UInt160.Zero, UnexpectedSend));

        var address = new byte[20];
        address[0] = 1;
        var client = new RpcOptimisticChallengeClient(rpc, new UInt160(address), UnexpectedSend);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await client.GetChallengeDeadlineAsync(0, 1));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await client.GetChallengeDeadlineAsync(1, 0));
    }

    private static ValueTask<RpcTransactionReceipt> UnexpectedSend(
        ReadOnlyMemory<byte> script, System.Threading.CancellationToken cancellationToken)
        => throw new AssertFailedException("Invalid identity must not submit a transaction.");

    private static ulong NextUInt64(Random rng)
    {
        var bytes = new byte[8];
        rng.NextBytes(bytes);
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    private static UInt256 NextRoot(Random rng)
    {
        var bytes = new byte[32];
        rng.NextBytes(bytes);
        return new UInt256(bytes);
    }
}
