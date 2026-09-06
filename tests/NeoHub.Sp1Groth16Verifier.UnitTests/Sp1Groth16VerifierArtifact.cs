using System.Security.Cryptography;

namespace NeoHub.Sp1Groth16Verifier.UnitTests;

/// <summary>Loads the currently compiled dedicated verifier artifact from contracts/bin/sc.
/// The VM harness and fresh-artifact gate therefore exercise the same NEF that deployment uses,
/// rather than a stale embedded bytecode snapshot.</summary>
internal static class Sp1Groth16VerifierArtifact
{
    private static string Root
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Neo.L2.sln"))) return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate neo-n4 repository root.");
        }
    }

    private static string Directory => Path.Combine(Root, "contracts", "NeoHub.Sp1Groth16Verifier", "bin", "sc");

    internal static string SourceSha256 => Convert.ToHexString(
        SHA256.HashData(File.ReadAllBytes(Path.Combine(Root, "contracts", "NeoHub.Sp1Groth16Verifier", "Sp1Groth16VerifierContract.cs"))))
        .ToLowerInvariant();

    internal static string NefSha256 => Convert.ToHexString(SHA256.HashData(Nef)).ToLowerInvariant();

    internal static string ManifestJson => File.ReadAllText(
        Path.Combine(Directory, "NeoHub.Sp1Groth16Verifier.manifest.json"));

    internal static byte[] Nef => File.ReadAllBytes(
        Path.Combine(Directory, "NeoHub.Sp1Groth16Verifier.nef"));
}
