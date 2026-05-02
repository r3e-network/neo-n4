namespace Neo.L2;

/// <summary>
/// Shared validator for L2 chain identifiers supplied by plugin configuration.
/// </summary>
/// <remarks>
/// Chain id <c>0</c> is reserved as the L1 sentinel (see <c>L2Outbox.L1ChainId</c>) — an L2
/// chain that adopts it would cause the per-message router to misclassify L2→L2 messages as
/// L2→L1, sending them out the wrong outbox subtree. Without parse-time validation, an
/// operator who omits <c>ChainId</c> from <c>config.json</c> silently lands on the default
/// <c>uint</c> value (<c>0</c>) and misroutes every cross-chain message.
/// </remarks>
public static class ChainIdValidator
{
    /// <summary>Reserved id for Neo L1 (matches <c>L2Outbox.L1ChainId</c>).</summary>
    public const uint L1ChainId = 0;

    /// <summary>
    /// Throw <see cref="System.IO.InvalidDataException"/> if <paramref name="chainId"/> is
    /// reserved (currently just <see cref="L1ChainId"/> = 0). Returns the value on success.
    /// </summary>
    public static uint ValidateL2(uint chainId, string settingName = "ChainId")
    {
        if (chainId == L1ChainId)
            throw new System.IO.InvalidDataException(
                $"{settingName} {chainId} is reserved for Neo L1 — set a non-zero L2 chain id");
        return chainId;
    }
}
