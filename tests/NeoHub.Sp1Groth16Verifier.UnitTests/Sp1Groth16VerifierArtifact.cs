using System.Security.Cryptography;

namespace NeoHub.Sp1Groth16Verifier.UnitTests;

/// <summary>
/// Loads the checked-in compiled verifier artifact from
/// <c>tests/NeoHub.Contracts.VmTests/TestingArtifacts/</c>.
/// </summary>
/// <remarks>
/// <para>
/// This previously read <c>contracts/NeoHub.Sp1Groth16Verifier/bin/sc/</c>. That directory is
/// excluded by <c>.gitignore</c>, so a fresh checkout never contains it and EVERY test in this
/// project failed with <c>DirectoryNotFoundException</c> — the whole SP1 verifier had zero
/// effective coverage outside a machine that happened to have run <c>nccs</c>.
/// </para>
/// <para>
/// The checked-in artifact is the same NEF the deployment tooling emits, and CI keeps it
/// honest: the <c>Verify audited verifier artifacts against fresh nccs output</c> step
/// re-compiles the contract and compares the manifest, and
/// <c>scripts/contracts/regenerate-testing-artifacts.sh</c> refreshes the file whenever the
/// contract source changes.
/// </para>
/// </remarks>
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

    private static string ArtifactDirectory => Path.Combine(
        Root, "tests", "NeoHub.Contracts.VmTests", "TestingArtifacts");

    internal static string SourceSha256 => Convert.ToHexString(
        SHA256.HashData(File.ReadAllBytes(Path.Combine(Root, "contracts", "NeoHub.Sp1Groth16Verifier", "Sp1Groth16VerifierContract.cs"))))
        .ToLowerInvariant();

    internal static string NefSha256 => Convert.ToHexString(SHA256.HashData(Nef)).ToLowerInvariant();

    internal static string ManifestJson => ReadArtifact("NeoHubSp1Groth16Verifier.artifacts.cs", "Manifest");

    internal static byte[] Nef => Convert.FromBase64String(
        ReadArtifact("NeoHubSp1Groth16Verifier.artifacts.cs", "Nef"));

    /// <summary>
    /// Extract a `public static ... &lt;Name&gt; =&gt; ...;` member's string literal from a
    /// generated <c>*.artifacts.cs</c> file.
    /// </summary>
    /// <remarks>
    /// The generated artifacts embed the manifest as a verbatim string literal and the NEF as
    /// a base64 literal, so they are read from the source rather than from a compiled
    /// assembly — this project does not reference the artifacts project.
    /// </remarks>
    private static string ReadArtifact(string fileName, string memberName)
    {
        var text = File.ReadAllText(Path.Combine(ArtifactDirectory, fileName));
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("public static ", StringComparison.Ordinal)) continue;
            var arrow = trimmed.IndexOf("=>", StringComparison.Ordinal);
            if (arrow < 0) continue;
            var declared = trimmed["public static ".Length..arrow].Trim();
            var name = declared.Split('<')[^1].Trim().Split(' ')[^1];
            if (!string.Equals(name, memberName, StringComparison.Ordinal)) continue;
            return ReadVerbatimLiteral(trimmed, arrow);
        }

        throw new InvalidDataException(
            $"{fileName} does not expose a static '{memberName}' member");
    }

    /// <summary>Read the `@"..."` verbatim literal that follows an arrow expression.</summary>
    private static string ReadVerbatimLiteral(string line, int arrowIndex)
    {
        var body = line[(arrowIndex + 2)..].TrimStart();
        var start = body.IndexOf("@\"", StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidDataException("malformed artifacts member: no verbatim literal");
        }
        // A verbatim literal ends at the first `"` that is not doubled ("" is an escaped
        // quote inside the literal). The manifest JSON uses "" for every quote, so the
        // terminator is the first single `"` followed by `)`.
        var index = start + 2;
        while (index < body.Length)
        {
            if (body[index] == '"')
            {
                if (index + 1 < body.Length && body[index + 1] == '"')
                {
                    index += 2; // escaped quote inside the literal
                    continue;
                }
                var raw = body[(start + 2)..index];
                // Undo the verbatim-literal escaping: `""` inside the literal is one `"`.
                return raw.Replace("\"\"", "\"", StringComparison.Ordinal);
            }
            index++;
        }
        throw new InvalidDataException("malformed artifacts member: unterminated literal");
    }
}
