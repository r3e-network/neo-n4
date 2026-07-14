using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeoHub.Sp1Groth16Verifier.UnitTests;

internal static class Sp1Groth16PositiveVector
{
    private static readonly Lazy<ReleaseVector> Vector = new(Load);

    internal static int SchemaVersion => Vector.Value.SchemaVersion;
    internal static string Sp1Version => Vector.Value.Sp1Version;
    internal static string GuestElfSha256 => Vector.Value.GuestElfSha256;
    internal static string WitnessPath => Vector.Value.WitnessPath;
    internal static string WitnessSha256 => Vector.Value.WitnessSha256;
    internal static string ProofSha256 => Vector.Value.ProofSha256;
    internal static string ProgramVKeySha256 => Vector.Value.ProgramVKeySha256;
    internal static string PublicValuesSha256 => Vector.Value.PublicValuesSha256;
    internal static string PublicInputHashSha256 => Vector.Value.PublicInputHashSha256;

    internal static byte[] Proof => Convert.FromHexString(Vector.Value.ProofHex);
    internal static byte[] ProgramVKey => Convert.FromHexString(Vector.Value.ProgramVKeyHex);
    internal static byte[] PublicValues => Convert.FromHexString(Vector.Value.PublicValuesHex);
    internal static byte[] PublicInputHash => Convert.FromHexString(Vector.Value.PublicInputHashHex);

    private static ReleaseVector Load()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "fixtures",
            "sp1-groth16-positive-vector-v1.json");
        return JsonSerializer.Deserialize<ReleaseVector>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"Failed to deserialize release vector: {path}");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Neo.L2.sln"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the neo-n4 repository root.");
    }

    private sealed class ReleaseVector
    {
        [JsonPropertyName("schemaVersion")]
        public required int SchemaVersion { get; init; }

        [JsonPropertyName("sp1Version")]
        public required string Sp1Version { get; init; }

        [JsonPropertyName("guestElfSha256")]
        public required string GuestElfSha256 { get; init; }

        [JsonPropertyName("witnessPath")]
        public required string WitnessPath { get; init; }

        [JsonPropertyName("witnessSha256")]
        public required string WitnessSha256 { get; init; }

        [JsonPropertyName("proofHex")]
        public required string ProofHex { get; init; }

        [JsonPropertyName("proofSha256")]
        public required string ProofSha256 { get; init; }

        [JsonPropertyName("publicValuesHex")]
        public required string PublicValuesHex { get; init; }

        [JsonPropertyName("publicValuesSha256")]
        public required string PublicValuesSha256 { get; init; }

        [JsonPropertyName("programVkeyHex")]
        public required string ProgramVKeyHex { get; init; }

        [JsonPropertyName("programVkeySha256")]
        public required string ProgramVKeySha256 { get; init; }

        [JsonPropertyName("publicInputHashHex")]
        public required string PublicInputHashHex { get; init; }

        [JsonPropertyName("publicInputHashSha256")]
        public required string PublicInputHashSha256 { get; init; }
    }
}
