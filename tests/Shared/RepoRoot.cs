namespace Neo.L2.TestInfra;

/// <summary>
/// Resolves the repository root for tests that read files committed to this repo.
/// </summary>
/// <remarks>
/// <c>tests/Directory.Build.props</c> sets <c>RuntimeIdentifier</c> on Windows only, which inserts a
/// <c>win-x64/</c> segment into <c>AppContext.BaseDirectory</c>. A fixed-count <c>".."</c> walk
/// therefore lands one level short there, every caller self-skips with "file not found", and Linux
/// CI stays green. Probing upward for <c>Neo.L2.sln</c> depends on neither the RID nor the depth of
/// the output directory. See docs/audit/subsystem-verification-audit-2026-08-30.md §5 V4.
/// </remarks>
internal static class RepoRoot
{
    /// <summary>The nearest ancestor directory containing <c>Neo.L2.sln</c>.</summary>
    public static string Directory { get; } = Find();

    /// <summary>Absolute path of the live-testnet deployment evidence recorded under <c>docs/audit</c>.</summary>
    public static string LiveTestnetEvidence { get; } = System.IO.Path.Combine(
        Directory, "docs", "audit", "testnet-deployment-20260716-live.json");

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Neo.L2.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException(
            $"No ancestor of {AppContext.BaseDirectory} contains Neo.L2.sln, so the repository root cannot be resolved.");
    }
}
