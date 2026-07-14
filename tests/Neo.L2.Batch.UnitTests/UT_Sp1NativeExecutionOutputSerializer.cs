using Neo.Cryptography;
using Neo.L2.State;

namespace Neo.L2.Batch.UnitTests;

[TestClass]
public sealed class UT_Sp1NativeExecutionOutputSerializer
{
    [TestMethod]
    public void RustGoldenFixture_DecodesAndReencodesByteIdentically()
    {
        var bytes = FixtureBytes("native_execution_output_v1.hex");
        var output = Sp1NativeExecutionOutputSerializer.Decode(bytes);

        CollectionAssert.AreEqual(
            bytes,
            Sp1NativeExecutionOutputSerializer.Encode(output));
        Assert.AreEqual(
            ExecutionSemanticIds.Sp1StatefulNeoVmV1,
            output.ExecutionSemanticId);
    }

    [TestMethod]
    public void RustGoldenFixture_BindsExactRequestEffectsAndPublicInputs()
    {
        var artifact = ProofWitnessArtifactSerializer.Decode(
            FixtureBytes("stateful_batch_v1.hex"));
        var output = Sp1NativeExecutionOutputSerializer.Decode(
            FixtureBytes("native_execution_output_v1.hex"));
        var payloadBytes = ExecutionPayloadSerializer.Encode(artifact.ExecutionPayload);

        Assert.AreEqual(
            new UInt256(Crypto.Hash256(payloadBytes)),
            output.RequestPayloadHash);
        Assert.AreEqual(
            new UInt256(Crypto.Hash256(artifact.StateWitness.Span)),
            output.RequestStateWitnessHash);
        Assert.AreEqual(artifact.ExecutionResult, output.ExecutionResult);
        CollectionAssert.AreEqual(artifact.Effects.ToArray(), output.Effects.ToArray());
        Assert.AreEqual(
            StateRootCalculator.HashPublicInputs(artifact.PublicInputs),
            output.PublicInputHash);
        var postState = StateWitnessV1Serializer.Decode(output.PostStateWitness.Span);
        Assert.AreEqual(
            output.ExecutionResult.PostStateRoot,
            KeyedStateMerkleTree.ComputeRoot(postState.Entries.Select(static entry =>
                (entry.Key.ToArray(), entry.Value.ToArray()))));
    }

    [TestMethod]
    public void Decode_RejectsContentTamperingAndTruncation()
    {
        var bytes = FixtureBytes("native_execution_output_v1.hex");
        var tampered = bytes.ToArray();
        tampered[128] ^= 1;

        Assert.ThrowsExactly<InvalidDataException>(() =>
            Sp1NativeExecutionOutputSerializer.Decode(tampered));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            Sp1NativeExecutionOutputSerializer.Decode(bytes.AsSpan(0, bytes.Length - 1)));
    }

    [TestMethod]
    public void Encode_RejectsWrongSemanticAndPostStateRoot()
    {
        var output = Sp1NativeExecutionOutputSerializer.Decode(
            FixtureBytes("native_execution_output_v1.hex"));

        Assert.ThrowsExactly<ArgumentException>(() =>
            Sp1NativeExecutionOutputSerializer.Encode(output with
            {
                ExecutionSemanticId = ExecutionSemanticIds.ReferenceNoOpV1,
            }));
        Assert.ThrowsExactly<ArgumentException>(() =>
            Sp1NativeExecutionOutputSerializer.Encode(output with
            {
                ExecutionResult = output.ExecutionResult with
                {
                    PostStateRoot = UInt256.Zero,
                },
            }));
    }

    private static byte[] FixtureBytes(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        return Convert.FromHexString(File.ReadAllText(path).Trim());
    }
}
