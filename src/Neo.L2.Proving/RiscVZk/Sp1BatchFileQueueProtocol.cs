using System.Text.Json.Serialization;

namespace Neo.L2.Proving.RiscVZk;

/// <summary>Stable filenames and discriminators for the SP1 batch prover daemon.</summary>
/// <remarks>See doc.md §7.5 and §8.</remarks>
public static class Sp1BatchFileQueueProtocol
{
    /// <summary>Current result-manifest schema.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Successful terminal status.</summary>
    public const string SucceededStatus = "succeeded";

    /// <summary>Canonical witness request suffix.</summary>
    public const string RequestSuffix = ".batch.bin";

    /// <summary>SP1 Groth16 proof suffix.</summary>
    public const string ProofSuffix = ".proof.bin";

    /// <summary>Raw 32-byte program verification-key suffix.</summary>
    public const string VerificationKeySuffix = ".proof.vk";

    /// <summary>Committed 33-byte public-values suffix.</summary>
    public const string PublicValuesSuffix = ".proof.public-values.bin";

    /// <summary>Terminal readiness-manifest suffix.</summary>
    public const string ResultManifestSuffix = ".proof.result.json";

    /// <summary>Durable settlement acknowledgement consumed by the Rust prover daemon.</summary>
    public const string SettlementAcknowledgementSuffix = ".proof.ack";

    internal static string FileName(string requestId, string suffix) => requestId + suffix;
}

/// <summary>Terminal marker published after the Rust daemon verifies every proof artifact.</summary>
/// <remarks>See doc.md §7.5 and §8.</remarks>
public sealed record Sp1BatchProofResultManifest
{
    /// <summary>Manifest schema version.</summary>
    [JsonPropertyName("schemaVersion")]
    public required int SchemaVersion { get; init; }

    /// <summary>Must be <c>succeeded</c>.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>Lowercase canonical witness content hash.</summary>
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    /// <summary>Lowercase SHA-256 of the exact canonical witness bytes.</summary>
    [JsonPropertyName("requestSha256")]
    public required string RequestSha256 { get; init; }

    /// <summary>Lowercase canonical witness content hash.</summary>
    [JsonPropertyName("artifactContentHash")]
    public required string ArtifactContentHash { get; init; }

    /// <summary>Lowercase canonical 32-byte public-input hash.</summary>
    [JsonPropertyName("publicInputHash")]
    public required string PublicInputHash { get; init; }

    /// <summary>Must be SP1 (1).</summary>
    [JsonPropertyName("proofSystem")]
    public required byte ProofSystem { get; init; }

    /// <summary>Lowercase exact execution-semantic identifier.</summary>
    [JsonPropertyName("executionSemanticId")]
    public required string ExecutionSemanticId { get; init; }

    /// <summary>Lowercase raw 32-byte SP1 program verification key.</summary>
    [JsonPropertyName("verificationKey")]
    public required string VerificationKey { get; init; }

    /// <summary>Exact request filename.</summary>
    [JsonPropertyName("requestFile")]
    public required string RequestFile { get; init; }

    /// <summary>Exact proof filename.</summary>
    [JsonPropertyName("proofFile")]
    public required string ProofFile { get; init; }

    /// <summary>Exact verification-key filename.</summary>
    [JsonPropertyName("verificationKeyFile")]
    public required string VerificationKeyFile { get; init; }

    /// <summary>Exact public-values filename.</summary>
    [JsonPropertyName("publicValuesFile")]
    public required string PublicValuesFile { get; init; }

    /// <summary>Lowercase SHA-256 of the proof.</summary>
    [JsonPropertyName("proofSha256")]
    public required string ProofSha256 { get; init; }

    /// <summary>Lowercase SHA-256 of the verification key.</summary>
    [JsonPropertyName("verificationKeySha256")]
    public required string VerificationKeySha256 { get; init; }

    /// <summary>Lowercase SHA-256 of the committed public values.</summary>
    [JsonPropertyName("publicValuesSha256")]
    public required string PublicValuesSha256 { get; init; }
}
