using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.L2.Batch;

namespace Neo.L2.IntegrationTests;

/// <summary>
/// CI gate: C# public-inputs wire size must stay locked to the Wave-2 352-byte domain
/// (348 historical layout ‖ forcedInclusionCount u32 LE).
/// </summary>
/// <remarks>
/// Paired with <c>bridge/neo-execution-core/tests/canonical_encoding_parity.rs</c> which asserts
/// the shared hex fixture is 352 bytes and hashes equal <c>hash_public_inputs</c>.
/// See docs/audit/architecture-iteration-2026-09-07-wave2.md.
/// </remarks>
[TestClass]
public class UT_PublicInputsWireDigestGate
{
    [TestMethod]
    public void PublicInputsSize_Is352ByteDomain()
    {
        Assert.AreEqual(352, BatchSerializer.PublicInputsSize);
    }
}
