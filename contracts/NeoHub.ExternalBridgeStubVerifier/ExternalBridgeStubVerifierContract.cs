using System.ComponentModel;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.ExternalBridgeStubVerifier;

/// <summary>
/// Phase-A acceptance-test verifier: implements the <c>verifyInboundMessage</c> /
/// <c>bridgeKind</c> ABI but always returns <c>true</c>. Lets devnet exercise
/// the full <c>L2Native.ExternalBridge → ExternalBridgeRegistry → verifier →
/// ExternalBridgeEscrow</c> round-trip before any real verifier is wired.
/// </summary>
/// <remarks>
/// MUST NOT be deployed to mainnet — replace with
/// <c>NeoHub.MpcCommitteeVerifier</c> (Phase B) or later. The stub is here to
/// validate that the seam itself is correct: registry dispatch routes calls,
/// the canonical message format round-trips through devnet, escrow accepts
/// and pays out. No security guarantees beyond "registered".
/// </remarks>
[DisplayName("NeoHub.ExternalBridgeStubVerifier")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Phase-A stub verifier — always returns true. Devnet only.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ExternalBridgeStubVerifier")]
[ContractPermission(Permission.Any, Method.Any)]
public class ExternalBridgeStubVerifierContract : SmartContract
{
    /// <summary>Always returns <c>true</c>. The stub is the simplest possible
    /// implementation of the verifier ABI; production verifiers do real
    /// cryptographic work here.</summary>
    [Safe]
    public static bool VerifyInboundMessage(uint externalChainId, byte[] messageBytes, byte[] proofBytes)
    {
        // Touch the args so they're not stripped from the manifest. The stub
        // is intentionally permissive — it's testing the seam, not security.
        return externalChainId != 0 || messageBytes != null || proofBytes != null || true;
    }

    /// <summary>Returns 0 — "stub", explicitly NOT one of the production
    /// kinds (1 MPC / 2 Optimistic / 3 ZK). Calling code that branches on
    /// kind can refuse the stub in production builds.</summary>
    [Safe]
    public static byte BridgeKind() => 0;
}
