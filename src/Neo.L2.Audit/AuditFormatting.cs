namespace Neo.L2.Audit;

/// <summary>
/// Shared formatting helpers used by audit-check failure reports. Lifted from
/// the previously-duplicated private helpers in <c>PublicInputHashConsistencyCheck</c>
/// and <c>ContinuityCheck</c> so a future change to the truncation convention
/// (e.g. show more middle bytes) flows through a single seam.
/// </summary>
internal static class AuditFormatting
{
    /// <summary>
    /// Render a <see cref="UInt256"/> as a compact identifier for audit failure
    /// messages. Long hashes are abbreviated to <c>0xabcdef1234…123456</c> (10
    /// leading hex chars + "…" + 6 trailing). Short hashes pass through unchanged.
    /// </summary>
    internal static string Truncate(UInt256 root)
    {
        var s = root.ToString();
        return s.Length <= 18 ? s : s[..10] + "…" + s[^6..];
    }
}
