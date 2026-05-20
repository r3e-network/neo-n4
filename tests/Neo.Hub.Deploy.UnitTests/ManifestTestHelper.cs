using System.IO;
using Neo.Json;

namespace Neo.Hub.Deploy.UnitTests;

/// <summary>
/// Shared loader for `contracts/&lt;Contract&gt;/bin/sc/&lt;Contract&gt;.manifest.json`
/// used by the manifest-integrity invariant tests. Encodes three outcomes:
/// <list type="bullet">
///   <item><term>Fresh</term><description>Manifest present and at least as new as
///   every source <c>*.cs</c> in <c>contracts/&lt;Contract&gt;/</c>. The test
///   asserts against it normally.</description></item>
///   <item><term>Stale</term><description>Manifest present but a source <c>*.cs</c>
///   is newer. We print a <c>[manifest-stale]</c> hint to test output and treat
///   the same as <c>Missing</c> (pass-through) — without this, the test would
///   fire false negatives against the old ABI surface whenever a developer edits
///   contract source without re-running <c>nccs</c>.</description></item>
///   <item><term>Missing</term><description>No manifest on disk — local dev
///   without <c>nccs</c> on <c>PATH</c>. Pass-through; the CI <c>contracts</c>
///   job (which runs <c>nccs</c>) is the authoritative gate.</description></item>
/// </list>
/// </summary>
internal static class ManifestTestHelper
{
    /// <summary>Look up the freshly-built manifest for a contract, walking
    /// upward from the test binary's directory to the repo root.</summary>
    /// <returns>The parsed manifest as a <see cref="JObject"/>, or <c>null</c>
    /// when missing/stale (with a one-line hint already written to test output
    /// when stale).</returns>
    public static JObject? LoadFreshManifest(string contractName)
    {
        var manifestRel = $"contracts/{contractName}/bin/sc/{contractName}.manifest.json";
        var srcDirRel = $"contracts/{contractName}";
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir != null; i++)
        {
            var manifestPath = Path.Combine(dir, manifestRel);
            if (File.Exists(manifestPath))
            {
                var srcDir = Path.Combine(dir, srcDirRel);
                var staleSrc = FindStaleSource(manifestPath, srcDir);
                if (staleSrc is not null)
                {
                    Console.WriteLine(
                        $"[manifest-stale] {contractName}: source '{Path.GetFileName(staleSrc)}' " +
                        $"is newer than .manifest.json — re-run nccs in '{srcDirRel}' to refresh. " +
                        $"Skipping invariant check until then.");
                    return null;
                }
                return (JObject)JToken.Parse(File.ReadAllText(manifestPath))!;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string? FindStaleSource(string manifestPath, string srcDir)
    {
        if (!Directory.Exists(srcDir)) return null;
        var manifestMtime = File.GetLastWriteTimeUtc(manifestPath);
        foreach (var src in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            if (File.GetLastWriteTimeUtc(src) > manifestMtime) return src;
        }
        return null;
    }
}
