namespace Neo.Plugins.L2Prover.UnitTests;

/// <summary>
/// Tests for <see cref="L2ProverPlugin"/> — the host that selects an
/// <see cref="IL2Prover"/> implementation based on the configured
/// <see cref="ProofType"/>. The Wire method dispatches stage-specific dependencies;
/// this class pins the dispatch + the helpful-error contract for unsupported / missing
/// dependencies.
/// </summary>
[TestClass]
public class UT_L2ProverPlugin
{
    private static InMemorySignerSet SampleSigners()
    {
        var priv = new byte[32]; priv[0] = 1;
        var pub = Neo.Cryptography.ECC.ECCurve.Secp256r1.G * priv;
        return new InMemorySignerSet(new[] { (pub, priv) });
    }

    [TestMethod]
    public void Constructor_DoesNotThrow()
    {
        using var plugin = new L2ProverPlugin();
    }

    [TestMethod]
    public void DefaultKind_IsMultisig()
    {
        // Pre-Configure default — pinned so a refactor that changes the field initializer
        // doesn't silently shift production deployments off Multisig (Stage 0).
        using var plugin = new L2ProverPlugin();
        Assert.AreEqual(ProofType.Multisig, plugin.Kind);
    }

    [TestMethod]
    public void Prover_BeforeWire_IsNull()
    {
        // The plugin defers prover construction to Wire so callers can inject the
        // stage-specific dependencies. Pin so a refactor that eagerly constructs the
        // prover in Configure surfaces here.
        using var plugin = new L2ProverPlugin();
        Assert.IsNull(plugin.Prover);
    }

    [TestMethod]
    public void Wire_Multisig_WithSignerSet_SetsAttestationProver()
    {
        using var plugin = new L2ProverPlugin();
        plugin.Wire(signerSet: SampleSigners());
        Assert.IsNotNull(plugin.Prover);
        Assert.IsInstanceOfType(plugin.Prover, typeof(AttestationProver));
        Assert.AreEqual(ProofType.Multisig, plugin.Prover.Kind);
    }

    [TestMethod]
    public void Wire_Multisig_WithoutSignerSet_ThrowsHelpfulError()
    {
        using var plugin = new L2ProverPlugin();
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => plugin.Wire());
        StringAssert.Contains(ex.Message, "Multisig");
        StringAssert.Contains(ex.Message, "signer set");
    }

    [TestMethod]
    public void Plugin_NameAndDescription_AreNonEmpty()
    {
        using var plugin = new L2ProverPlugin();
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.Name));
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.Description));
        StringAssert.Contains(plugin.Name, "L2Prover");
    }

    [TestMethod]
    public void Wire_Zk_WithRiscVProver_SetsMockRiscVProver()
    {
        using var plugin = new L2ProverPlugin { Kind = ProofType.Zk };
        var vk = UInt256.Parse("0x" + new string('a', 64));
        var rv = new MockRiscVProver(vk);
        plugin.Wire(riscVProver: rv);
        Assert.IsNotNull(plugin.Prover);
        Assert.AreSame(rv, plugin.Prover);
        Assert.AreEqual(ProofType.Zk, plugin.Prover.Kind);
    }

    [TestMethod]
    public void Wire_Zk_WithoutRiscVProver_ThrowsHelpfulError()
    {
        using var plugin = new L2ProverPlugin { Kind = ProofType.Zk };
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => plugin.Wire());
        StringAssert.Contains(ex.Message, "Zk");
        StringAssert.Contains(ex.Message, "RiscV");
    }

    [TestMethod]
    public void Wire_Optimistic_ThrowsWithPointerToSettlementPlugin()
    {
        // Optimistic proving lives in L2SettlementPlugin (it just signs); the prover
        // plugin can't supply that. The error must point operators at the right plugin.
        using var plugin = new L2ProverPlugin { Kind = ProofType.Optimistic };
        var ex = Assert.ThrowsExactly<NotSupportedException>(() => plugin.Wire());
        StringAssert.Contains(ex.Message, "Optimistic");
        StringAssert.Contains(ex.Message, "L2SettlementPlugin");
    }

    [TestMethod]
    public void Wire_None_ThrowsHelpfulError()
    {
        // ProofType.None is legal in the wire format (genesis / operator-trusted flows)
        // but the prover plugin can't produce a proof for it. Error must explain why
        // and point at valid alternatives (Multisig/Optimistic/Zk).
        using var plugin = new L2ProverPlugin { Kind = ProofType.None };
        var ex = Assert.ThrowsExactly<NotSupportedException>(() => plugin.Wire());
        StringAssert.Contains(ex.Message, "None");
        StringAssert.Contains(ex.Message, "Multisig");
    }
}
