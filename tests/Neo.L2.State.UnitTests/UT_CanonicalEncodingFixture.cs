using Neo;
using Neo.SmartContract;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;
using System.Text.Json.Serialization;

namespace Neo.L2.State.UnitTests;

/// <summary>
/// One cross-language canonical-encoding case.
/// </summary>
/// <remarks>
/// The <c>publicInputsHex</c> field holds the 352-byte canonical public-input preimage and
/// <c>publicInputHash</c> holds its SHA-256. The bytes are produced by the .NET production
/// encoder (<c>StateRootCalculator.EncodePublicInputs</c>); the Rust side verifies the same
/// bytes against <c>neo_execution_core::hashing::hash_public_inputs_with_forced</c>.
/// </remarks>
public sealed class CanonicalEncodingCase
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("publicInputsHex")]
    public string PublicInputsHex { get; set; } = string.Empty;

    [JsonPropertyName("publicInputHash")]
    public string PublicInputHash { get; set; } = string.Empty;

    [JsonPropertyName("forcedInclusionCount")]
    public uint ForcedInclusionCount { get; set; }
}

/// <summary>The shared cross-language fixture file.</summary>
public sealed class CanonicalEncodingFixture
{
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = "neo-n4-canonical-encoding/v1";

    [JsonPropertyName("publicInputsSize")]
    public int PublicInputsSize { get; set; }

    [JsonPropertyName("cases")]
    public List<CanonicalEncodingCase> Cases { get; set; } = new();
}

/// <summary>
/// The cross-language canonical-encoding fixture, produced by the .NET production encoder
/// and verified by the Rust implementation.
/// </summary>
/// <remarks>
/// <para>
/// This is the "one true oracle" the 2026-10-21 audit asked for. The Rust implementation
/// is checked against bytes produced by the .NET production code, NOT against a Rust-side
/// mirror of itself. The audit's cross-language divergences survived precisely because each
/// side validated against its own implementation.
/// </para>
/// <para>
/// The .NET test regenerates the fixture from the live encoder and fails if the checked-in
/// copy differs, so the fixture can never drift into agreeing with a wrong implementation.
/// </para>
/// </remarks>
[TestClass]
public sealed class UT_CanonicalEncodingFixture
{
    /// <summary>Path of the shared fixture, relative to the repository root.</summary>
    private const string FixtureRelativePath = "sdk/conformance/vectors/canonical-encoding-v1.json";

    private static PublicInputs MakeInputs(uint chainId, ulong batchNumber, uint forcedInclusionCount) => new()
    {
        ChainId = chainId,
        BatchNumber = batchNumber,
        FirstBlock = 100,
        LastBlock = 200,
        PreStateRoot = Root(0x11),
        PostStateRoot = Root(0x22),
        TxRoot = Root(0x33),
        ReceiptRoot = Root(0x44),
        WithdrawalRoot = Root(0x55),
        L2ToL1MessageRoot = Root(0x66),
        L2ToL2MessageRoot = Root(0x77),
        L1MessageHash = Root(0x88),
        DACommitment = Root(0x99),
        BlockContextHash = Root(0xAA),
        ForcedInclusionCount = forcedInclusionCount,
    };

    private static UInt256 Root(byte fill) => new(Enumerable.Repeat(fill, 32).ToArray());

    /// <summary>The cases the fixture carries. The Rust side re-derives each one.</summary>
    private static IEnumerable<(string Name, PublicInputs Inputs)> Cases()
    {
        yield return ("default", MakeInputs(1099, 7, 0));
        yield return ("forced-inclusions", MakeInputs(1099, 8, 3));
        yield return ("max-u64-batch", MakeInputs(uint.MaxValue, ulong.MaxValue, 0));
        yield return ("chain-id-zero", MakeInputs(0, 0, 0));
    }

    [TestMethod]
    public void Preimage_Is352Bytes_AndHashesToTheRecordedValue()
    {
        foreach (var (name, inputs) in Cases())
        {
            var preimage = StateRootCalculator.EncodePublicInputs(inputs);
            Assert.AreEqual(352, preimage.Length, $"{name}: the canonical preimage is 352 bytes");

            var hash = StateRootCalculator.HashPublicInputs(inputs);
            Assert.AreEqual(
                Convert.ToHexString(hash.GetSpan()),
                Convert.ToHexString(Neo.Cryptography.Crypto.Hash256(preimage)),
                $"{name}: the hash must be the SHA-256 of the encoded preimage");
        }
    }

    [TestMethod]
    public void CheckedInFixture_MatchesTheLiveEncoder()
    {
        var root = Neo.L2.TestInfra.RepoRoot.Directory;
        var path = Path.Combine(root, FixtureRelativePath);
        Assert.IsTrue(File.Exists(path), $"fixture missing: {FixtureRelativePath}");

        var fixture = JsonSerializer.Deserialize<CanonicalEncodingFixture>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("fixture did not deserialize");

        Assert.AreEqual("neo-n4-canonical-encoding/v1", fixture.Schema);
        Assert.AreEqual(352, fixture.PublicInputsSize);
        Assert.AreEqual(Cases().Count(), fixture.Cases.Count, "the fixture must carry every case");

        var expected = Cases().ToDictionary(c => c.Name, c => c.Inputs, StringComparer.Ordinal);
        foreach (var testCase in fixture.Cases)
        {
            Assert.IsTrue(expected.ContainsKey(testCase.Name), $"unknown fixture case '{testCase.Name}'");
            var inputs = expected[testCase.Name];

            var preimage = StateRootCalculator.EncodePublicInputs(inputs);
            Assert.AreEqual(352, preimage.Length);
            Assert.AreEqual(
                testCase.PublicInputsHex,
                Convert.ToHexString(preimage),
                $"'{testCase.Name}': the checked-in preimage does not match the .NET encoder — "
                + "regenerate the fixture");
            Assert.AreEqual(
                testCase.PublicInputHash,
                Convert.ToHexString(StateRootCalculator.HashPublicInputs(inputs).GetSpan()),
                $"'{testCase.Name}': the checked-in hash does not match the .NET encoder");
            Assert.AreEqual(inputs.ForcedInclusionCount, testCase.ForcedInclusionCount);
        }
    }
}
