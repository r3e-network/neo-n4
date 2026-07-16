using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.SmartContract;
using Neo.Wallets;

namespace Neo.L2.Sequencer.UnitTests;

[TestClass]
public class UT_SequencerCommitteeHasher
{
    [TestMethod]
    public void Compute_Empty_ReturnsZero()
    {
        Assert.AreEqual(UInt256.Zero, SequencerCommitteeHasher.Compute(Array.Empty<ECPoint>()));
        Assert.AreEqual(UInt256.Zero, SequencerCommitteeHasher.Compute(Array.Empty<CommitteeMember>()));
    }

    [TestMethod]
    public void Compute_IsOrderIndependent()
    {
        var a = Key(1);
        var b = Key(2);
        var c = Key(3);

        var forward = SequencerCommitteeHasher.Compute([a, b, c]);
        var reverse = SequencerCommitteeHasher.Compute([c, b, a]);
        Assert.AreEqual(forward, reverse);
        Assert.AreNotEqual(UInt256.Zero, forward);
    }

    [TestMethod]
    public void Compute_MatchesManualHash256OfSortedCompressedKeys()
    {
        var keys = new[] { Key(9), Key(1), Key(5) };
        var encoded = keys
            .Select(k => k.EncodePoint(true))
            .OrderBy(bytes => Convert.ToHexString(bytes), StringComparer.Ordinal)
            .ToArray();
        var joined = encoded.SelectMany(b => b).ToArray();
        var expected = new UInt256(Crypto.Hash256(joined));

        Assert.AreEqual(expected, SequencerCommitteeHasher.Compute(keys));
    }

    [TestMethod]
    public void Compute_Members_UsesPublicKeys()
    {
        var key = Key(7);
        var members = new[]
        {
            new CommitteeMember
            {
                PublicKey = key,
                L1Address = Contract.CreateSignatureRedeemScript(key).ToScriptHash(),
                Status = 1,
                ExitsAtUnixSeconds = 0,
            },
        };
        Assert.AreEqual(
            SequencerCommitteeHasher.Compute([key]),
            SequencerCommitteeHasher.Compute(members));
    }

    [TestMethod]
    public void CreateSyncProvider_ReadsActiveCommittee()
    {
        var key = Key(4);
        using var provider = new InMemorySequencerCommitteeProvider(chainId: 1001);
        provider.Register(key, Contract.CreateSignatureRedeemScript(key).ToScriptHash());

        var sync = SequencerCommitteeHasher.CreateSyncProvider(provider);
        var hash = sync();
        Assert.AreEqual(SequencerCommitteeHasher.Compute([key]), hash);
    }

    private static ECPoint Key(byte seed)
    {
        // Deterministic non-zero private key in [1, n) for secp256r1.
        var privateKey = new byte[32];
        privateKey[^1] = seed;
        privateKey[^2] = (byte)(seed + 1);
        return new KeyPair(privateKey).PublicKey;
    }
}
