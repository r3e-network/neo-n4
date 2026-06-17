using System.Numerics;
using Neo.SmartContract.Testing;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// VM-level (NeoVM TestEngine) tests for NeoHub.ExternalBridgeStubVerifier — the Phase-A
/// devnet-only stub that implements the verifier ABI (<c>verifyInboundMessage</c> /
/// <c>bridgeKind</c>) but performs NO cryptographic verification.
///
/// The stub has no <c>_deploy</c>, no storage, no witness gates and no cross-contract calls, so
/// there is no auth/replay/accounting state to exercise. The ONE security-load-bearing property
/// it carries is its trust-model self-declaration: <c>bridgeKind() == 0</c>. The production
/// registry (ExternalBridgeRegistry) only accepts verifiers whose self-declared kind is in the
/// set {1 MPC, 2 Optimistic, 3 ZK} and asserts requested-kind == verifier.bridgeKind() at
/// registration. Kind 0 ("stub") is deliberately outside that set, so this single byte is the
/// mechanism that prevents the always-true stub from ever being wired in as a real verifier.
/// These tests pin (a) bridgeKind is exactly 0 and never drifts into a production value, and
/// (b) verifyInboundMessage is unconditionally permissive — confirming it offers no security and
/// must be gated out by the registry's bridgeKind check, not trusted on its own.
/// </summary>
[TestClass]
public class UT_ExternalBridgeStubVerifier_Vm
{
    private static NeoHubExternalBridgeStubVerifier Deploy()
    {
        var engine = new TestEngine(true);
        // No _deploy / constructor args — the stub has no deploy parameters or initialization.
        return engine.Deploy<NeoHubExternalBridgeStubVerifier>(
            NeoHubExternalBridgeStubVerifier.Nef, NeoHubExternalBridgeStubVerifier.Manifest, new object[] { });
    }

    /// <summary>
    /// bridgeKind() MUST be exactly 0. This is the load-bearing trust-model signal: the production
    /// registry rejects any kind outside {1,2,3}, and 0 is intentionally "stub". If this ever
    /// returned a production kind, the always-true stub would pass the registry's verifier check
    /// and silently become a security-bypassing bridge verifier on a real network.
    /// </summary>
    [TestMethod]
    public void BridgeKind_IsZero_OutsideProductionKindSet()
    {
        var v = Deploy();

        Assert.AreEqual((BigInteger)0, v.BridgeKind!,
            "stub must self-declare bridgeKind 0 so the registry refuses it (0 ∉ {1 MPC, 2 Optimistic, 3 ZK})");

        // Explicitly assert it is none of the three production kinds — the exact values the
        // registry would accept. A regression to any of these would be a critical trust-model break.
        Assert.AreNotEqual((BigInteger)1, v.BridgeKind!, "stub must never masquerade as MPC (kind 1)");
        Assert.AreNotEqual((BigInteger)2, v.BridgeKind!, "stub must never masquerade as Optimistic (kind 2)");
        Assert.AreNotEqual((BigInteger)3, v.BridgeKind!, "stub must never masquerade as ZK (kind 3)");
    }

    /// <summary>
    /// verifyInboundMessage is documented to ALWAYS return true with no real verification. Pin that
    /// it accepts a well-formed-looking input. This is not a "good" property — it is the explicit,
    /// documented insecurity of the stub. Pinning it guarantees the seam stays exercisable on devnet
    /// while making any accidental partial-logic regression visible, and documents WHY the registry's
    /// bridgeKind==0 rejection (above) is the actual safety mechanism.
    /// </summary>
    [TestMethod]
    public void VerifyInboundMessage_AlwaysAcceptsValidLookingInput()
    {
        var v = Deploy();

        Assert.IsTrue(v.VerifyInboundMessage(1, new byte[] { 0xAB, 0xCD }, new byte[] { 0x01, 0x02 })!,
            "stub verifier accepts any message with a non-empty proof");
    }

    /// <summary>
    /// The stub provides NO security: it accepts even adversarial / degenerate inputs that a real
    /// verifier would reject outright — chainId 0, an empty message, and an EMPTY proof (i.e. no
    /// cryptographic evidence at all). This is the concrete demonstration that the stub cannot be
    /// trusted on its own and is exactly why bridgeKind()==0 must keep it out of production.
    /// </summary>
    [TestMethod]
    public void VerifyInboundMessage_AcceptsAdversarialInputs_NoSecurityGuarantee()
    {
        var v = Deploy();

        // Empty proof bytes — a real verifier would reject "no proof"; the stub accepts it.
        Assert.IsTrue(v.VerifyInboundMessage(7, new byte[] { 0x11 }, System.Array.Empty<byte>())!,
            "stub accepts an empty proof — proof of its no-security trust model");

        // chainId 0 + empty message + empty proof: maximally degenerate, still accepted.
        Assert.IsTrue(v.VerifyInboundMessage(0, System.Array.Empty<byte>(), System.Array.Empty<byte>())!,
            "stub accepts even an all-empty / zero-chain request");
    }

    /// <summary>
    /// Both methods are [Safe] (read-only, no storage, no witness). Repeated invocation on the same
    /// instance must be deterministic and never mutate observable state — the stub is a pure function
    /// of its inputs. This pins that no hidden state/counter was introduced behind the safe surface.
    /// </summary>
    [TestMethod]
    public void Methods_AreDeterministic_AndStateless()
    {
        var v = Deploy();

        Assert.AreEqual((BigInteger)0, v.BridgeKind!);
        Assert.AreEqual((BigInteger)0, v.BridgeKind!, "bridgeKind must be invariant across calls");

        Assert.IsTrue(v.VerifyInboundMessage(42, new byte[] { 0xFF }, new byte[] { 0xFF })!);
        Assert.IsTrue(v.VerifyInboundMessage(42, new byte[] { 0xFF }, new byte[] { 0xFF })!,
            "verifyInboundMessage must be a deterministic, side-effect-free function of its inputs");
    }
}
