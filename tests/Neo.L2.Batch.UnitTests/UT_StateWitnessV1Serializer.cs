using Neo.L2.State;

namespace Neo.L2.Batch.UnitTests;

[TestClass]
public class UT_StateWitnessV1Serializer
{
    [TestMethod]
    public void RustGoldenFixture_DecodesAndReencodesByteIdentically()
    {
        var artifact = GoldenArtifact();
        var witness = StateWitnessV1Serializer.Decode(artifact.StateWitness.Span);

        CollectionAssert.AreEqual(
            artifact.StateWitness.ToArray(),
            StateWitnessV1Serializer.Encode(witness));
        Assert.AreEqual(2, witness.Entries.Count);
        Assert.AreEqual(1, witness.Contracts.Count);
        Assert.AreEqual(0xff, witness.Entries[1].Key.Span[0]);
        Assert.AreEqual(
            artifact.ExecutionPayload.PreStateRoot,
            KeyedStateMerkleTree.ComputeRoot(
                witness.Entries.Select(static entry =>
                    (entry.Key.ToArray(), entry.Value.ToArray()))));
    }

    [TestMethod]
    public void ContractBinding_MatchesRustGoldenBytes()
    {
        var witness = StateWitnessV1Serializer.Decode(GoldenArtifact().StateWitness.Span);
        var contract = witness.Contracts.Single();
        var binding = witness.Entries.Single(entry =>
            entry.Key.Span.SequenceEqual(
                StateWitnessV1Serializer.ContractBindingKey(contract.Hash)));

        CollectionAssert.AreEqual(
            Convert.FromHexString(
                "f55c786591ed202461901fbbd0d482b9369edbbb2b84d78aafd5f8c16a16c2c9"),
            StateWitnessV1Serializer.ContractBindingHash(contract).GetSpan().ToArray());
        CollectionAssert.AreEqual(
            binding.Value.ToArray(),
            StateWitnessV1Serializer.ContractBindingHash(contract).GetSpan().ToArray());
    }

    [TestMethod]
    public void Encode_RejectsTamperedBindingOrderAndProtocolConfig()
    {
        var witness = StateWitnessV1Serializer.Decode(GoldenArtifact().StateWitness.Span);
        var tamperedEntries = witness.Entries
            .Select(static entry => entry with
            {
                Key = entry.Key.ToArray(),
                Value = entry.Value.ToArray(),
            })
            .ToArray();
        tamperedEntries[1] = tamperedEntries[1] with { Value = new byte[32] };
        Assert.ThrowsExactly<ArgumentException>(() =>
            StateWitnessV1Serializer.Encode(witness with { Entries = tamperedEntries }));
        Assert.ThrowsExactly<ArgumentException>(() =>
            StateWitnessV1Serializer.Encode(witness with
            {
                Entries = witness.Entries.Reverse().ToArray(),
            }));
        Assert.ThrowsExactly<ArgumentException>(() =>
            StateWitnessV1Serializer.Encode(witness with
            {
                ProtocolConfig = witness.ProtocolConfig with { ExecFeeFactor = 31 },
            }));
    }

    [TestMethod]
    public void Decode_RejectsTruncationTrailingBytesAndBindingTampering()
    {
        var bytes = GoldenArtifact().StateWitness.ToArray();
        Assert.ThrowsExactly<InvalidDataException>(() =>
            StateWitnessV1Serializer.Decode(bytes.AsSpan(0, bytes.Length - 1)));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            StateWitnessV1Serializer.Decode([.. bytes, 0x00]));

        var tampered = bytes.ToArray();
        tampered[99] ^= 0x01;
        Assert.ThrowsExactly<InvalidDataException>(() =>
            StateWitnessV1Serializer.Decode(tampered));
    }

    [TestMethod]
    public void ValueSemantics_CompareMemoryByContent()
    {
        var witness = StateWitnessV1Serializer.Decode(GoldenArtifact().StateWitness.Span);
        var clone = witness with
        {
            Entries = witness.Entries.Select(static entry => entry with
            {
                Key = entry.Key.ToArray(),
                Value = entry.Value.ToArray(),
            }).ToArray(),
            Contracts = witness.Contracts.Select(static contract => contract with
            {
                Hash = new UInt160(contract.Hash.GetSpan()),
                Script = contract.Script.ToArray(),
                Manifest = contract.Manifest.ToArray(),
            }).ToArray(),
        };

        Assert.AreEqual(witness, clone);
        Assert.AreEqual(witness.GetHashCode(), clone.GetHashCode());
    }

    private static ProofWitnessArtifactV1 GoldenArtifact()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "stateful_batch_v1.hex");
        var bytes = Convert.FromHexString(File.ReadAllText(path).Trim());
        return ProofWitnessArtifactSerializer.Decode(bytes);
    }
}
