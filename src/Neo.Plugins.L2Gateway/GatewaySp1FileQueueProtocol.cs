using System.Text.Json.Serialization;

namespace Neo.Plugins.L2Gateway;

/// <summary>Stable filenames and discriminators for the dedicated Gateway SP1 daemon.</summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). The client atomically publishes the request payload first and the
/// request manifest second. The daemon atomically publishes proof artifacts first and the result
/// manifest last. A manifest is therefore the only readiness marker; partial files are never
/// consumed. Every filename is derived from the Hash256 request id, not trusted from arbitrary
/// paths supplied by another process.
/// </remarks>
public static class GatewaySp1FileQueueProtocol
{
    /// <summary>Current request/result manifest schema.</summary>
    public const int SchemaVersion = 1;

    /// <summary>SP1 proof-system discriminator accepted by NeoHub.Sp1Groth16Verifier.</summary>
    public const byte Sp1ProofSystem = 1;

    /// <summary>Dedicated recursive Gateway aggregation backend.</summary>
    public const byte RecursiveAggregationBackendId = 0xC2;

    /// <summary>Successful terminal result status.</summary>
    public const string SucceededStatus = "succeeded";

    /// <summary>Request payload suffix.</summary>
    public const string RequestPayloadSuffix = ".gateway-request.bin";

    /// <summary>Request readiness-manifest suffix.</summary>
    public const string RequestManifestSuffix = ".gateway-request.json";

    /// <summary>SP1 Groth16 proof suffix.</summary>
    public const string ProofSuffix = ".gateway-proof.bin";

    /// <summary>Raw 32-byte SP1 program verification-key suffix.</summary>
    public const string VerificationKeySuffix = ".gateway-verification-key.bin";

    /// <summary>Committed 33-byte public-values suffix.</summary>
    public const string PublicValuesSuffix = ".gateway-public-values.bin";

    /// <summary>Result readiness-manifest suffix.</summary>
    public const string ResultManifestSuffix = ".gateway-result.json";

    internal static string FileName(string requestId, string suffix) => requestId + suffix;
}

/// <summary>Ready marker consumed by the dedicated Gateway SP1 daemon.</summary>
/// <remarks>See doc.md §4 (Neo Gateway).</remarks>
public sealed record GatewaySp1ProofRequestManifest
{
    /// <summary>Manifest schema version.</summary>
    [JsonPropertyName("schemaVersion")]
    public required int SchemaVersion { get; init; }

    /// <summary>Lowercase Hash256 request id.</summary>
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    /// <summary>Lowercase Hash256 of the exact binary request payload.</summary>
    [JsonPropertyName("requestHash")]
    public required string RequestHash { get; init; }

    /// <summary>Lowercase Hash256 of the canonical 170-byte Gateway binding.</summary>
    [JsonPropertyName("bindingHash")]
    public required string BindingHash { get; init; }

    /// <summary>Must be SP1 (1).</summary>
    [JsonPropertyName("proofSystem")]
    public required byte ProofSystem { get; init; }

    /// <summary>Must be the dedicated recursive Gateway backend (0xC2).</summary>
    [JsonPropertyName("aggregationBackendId")]
    public required byte AggregationBackendId { get; init; }

    /// <summary>Lowercase raw 32-byte Gateway guest program verification key.</summary>
    [JsonPropertyName("verificationKey")]
    public required string VerificationKey { get; init; }

    /// <summary>Exact request payload filename derived from <see cref="RequestId"/>.</summary>
    [JsonPropertyName("requestFile")]
    public required string RequestFile { get; init; }
}

/// <summary>Terminal ready marker published after all Gateway SP1 result artifacts.</summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). The Rust daemon must host-verify the Groth16 proof before writing
/// this manifest. The .NET client independently validates every binding field, artifact digest,
/// fixed length, verification key, and committed public value before returning the proof.
/// </remarks>
public sealed record GatewaySp1ProofResultManifest
{
    /// <summary>Manifest schema version.</summary>
    [JsonPropertyName("schemaVersion")]
    public required int SchemaVersion { get; init; }

    /// <summary>Must be <c>succeeded</c>.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>Lowercase Hash256 request id.</summary>
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    /// <summary>Lowercase Hash256 of the exact request payload.</summary>
    [JsonPropertyName("requestHash")]
    public required string RequestHash { get; init; }

    /// <summary>Lowercase Hash256 of the canonical Gateway binding.</summary>
    [JsonPropertyName("bindingHash")]
    public required string BindingHash { get; init; }

    /// <summary>Must be SP1 (1).</summary>
    [JsonPropertyName("proofSystem")]
    public required byte ProofSystem { get; init; }

    /// <summary>Must be the dedicated recursive Gateway backend (0xC2).</summary>
    [JsonPropertyName("aggregationBackendId")]
    public required byte AggregationBackendId { get; init; }

    /// <summary>Lowercase raw 32-byte Gateway guest program verification key.</summary>
    [JsonPropertyName("verificationKey")]
    public required string VerificationKey { get; init; }

    /// <summary>Exact request payload filename.</summary>
    [JsonPropertyName("requestFile")]
    public required string RequestFile { get; init; }

    /// <summary>Exact 356-byte proof filename.</summary>
    [JsonPropertyName("proofFile")]
    public required string ProofFile { get; init; }

    /// <summary>Exact 32-byte verification-key filename.</summary>
    [JsonPropertyName("verificationKeyFile")]
    public required string VerificationKeyFile { get; init; }

    /// <summary>Exact 33-byte public-values filename.</summary>
    [JsonPropertyName("publicValuesFile")]
    public required string PublicValuesFile { get; init; }

    /// <summary>Lowercase SHA-256 digest of the proof artifact.</summary>
    [JsonPropertyName("proofSha256")]
    public required string ProofSha256 { get; init; }

    /// <summary>Lowercase SHA-256 digest of the verification-key artifact.</summary>
    [JsonPropertyName("verificationKeySha256")]
    public required string VerificationKeySha256 { get; init; }

    /// <summary>Lowercase SHA-256 digest of the public-values artifact.</summary>
    [JsonPropertyName("publicValuesSha256")]
    public required string PublicValuesSha256 { get; init; }
}
