namespace Neo.Stack.Cli.UnitTests;

/// <summary>
/// The single assertion behind the three guards that pin "a shipped template or sample is internally
/// consistent": <c>create-chain</c> per template, <c>new-l2</c> per template, and the walk over
/// <c>samples/*.config.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>neo-stack validate</c> emits two different classes of <c>⚠</c> about the securityLabel/proofType
/// pair, and only one of them means "this config is broken":
/// </para>
/// <list type="number">
/// <item><description>
/// <c>accepts only proofType=…</c> — the config under-delivers: <c>SettlementManager.IsProofTypeCompatible</c>
/// rejects the pair, so the first <c>submitBatch</c> faults. Never acceptable in a shipped config.
/// </description></item>
/// <item><description>
/// <c>no verifier route in the shipped production bundle</c> — the pair is legal, but <c>Neo.Hub.Deploy</c>
/// registers only the Zk route before it one-way locks <c>VerifierRegistry</c>, so the chain needs an
/// operator-supplied route. The sidechain template intends exactly this: committee attestation is what a
/// sidechain promises, and <c>samples/README.md</c> documents the caveat.
/// </description></item>
/// </list>
/// <para>
/// Before audit finding <c>H18</c> these guards asserted "zero <c>⚠</c>" against a validator that called
/// the rollup template's unserved default fully clean — the warning class that mattered did not exist. So
/// a shipped config must now be silent apart from its documented caveat, and a documented caveat must
/// actually still be printed.
/// </para>
/// </remarks>
internal static class ShippedConfigWarningPolicy
{
    /// <summary>Substrings identifying the under-delivery warning <c>validate</c> prints for a rejected pair.</summary>
    private const string UnderDeliveryWarning = "accepts only proofType";

    /// <summary>Substrings identifying the missing-verifier-route warning for a legal-but-unserved pair.</summary>
    private const string UnservedRouteWarning = "no verifier route in the shipped production bundle";

    /// <summary>Shipped configs allowed one missing-verifier-route caveat, keyed by template/sample name.</summary>
    private static readonly Dictionary<string, string> DocumentedUnservedRouteCaveats = new()
    {
        ["sidechain"] = "proofType=Multisig is legal for securityLevel=Sidechain",
        ["privacy-sidechain"] = "proofType=Multisig is legal for securityLevel=Sidechain",
    };

    /// <summary>
    /// Assert that <paramref name="output"/> (stdout of <c>neo-stack validate</c> for the shipped config
    /// named <paramref name="name"/>) is warning-free apart from that config's documented caveat.
    /// </summary>
    public static void AssertConsistent(string name, string output)
    {
        Assert.IsFalse(output.Contains(UnderDeliveryWarning),
            $"{name} advertises a securityLevel its proofType cannot deliver — every submitBatch would fault.\nOutput:\n{output}");

        if (DocumentedUnservedRouteCaveats.TryGetValue(name, out var caveat))
        {
            StringAssert.Contains(output, caveat,
                $"{name} is shipped as a documented missing-verifier-route case; validate must keep saying so "
                + "(samples/README.md repeats it).\nOutput:\n{output}");
            StringAssert.Contains(output, UnservedRouteWarning,
                $"{name}'s caveat must be the missing-verifier-route warning.\nOutput:\n{output}");
            Assert.AreEqual(1, CountWarnings(output),
                $"{name} must emit its one documented caveat and nothing else.\nOutput:\n{output}");
            return;
        }

        Assert.IsFalse(output.Contains("⚠"),
            $"{name} emits a cross-field warning — it is internally inconsistent.\nOutput:\n{output}");
    }

    private static int CountWarnings(string output)
    {
        var count = 0;
        foreach (var line in output.Split('\n'))
        {
            if (line.Contains("⚠")) count++;
        }
        return count;
    }
}
