// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "forge-std/Test.sol";
import "../src/NeoExternalBridgeRouter.sol";

/// Validates that NeoExternalBridgeRouter.sol — without modification —
/// deploys + functions correctly across the entire EVM-family chain-id
/// space (Ethereum, BSC, Polygon, Arbitrum, Optimism, Base, Avalanche,
/// Linea, zkSync Era, Scroll, Mantle, Fantom, Celo). The chain-id table
/// in `watchers/neo-bridge-watcher-eth/src/chains.rs` claims "no per-chain
/// code"; this test pins that claim end-to-end on the Solidity side:
///
/// - **Constructor accepts every canonical mainnet slot** in the
///   `0xE0_xx_xx_xx` foreign namespace, and rejects any id outside it.
/// - **Each deployed router carries its own externalChainId** through
///   to the Locked event — operators watching different routers see
///   different chain ids.
/// - **Cross-chain message rejection works** — a router for BSC must
///   refuse a finalizeWithdrawal whose canonical messageBytes claim
///   to be for Polygon (the chain-id mismatch reverts before any
///   signature work).
/// - **Nonces are per-router-instance** — concurrent routers don't
///   leak nonce state into each other.
/// - **Committees are per-router-instance** — a Polygon-committee
///   signer is not authorized on the BSC router.
contract NeoExternalBridgeRouterMultiChainTest is Test {
    // Canonical mainnet slots from
    // watchers/neo-bridge-watcher-eth/src/chains.rs. Keep this list in
    // sync with that module's constants — the `chain_ids_distinct`
    // unit test there pins uniqueness on the Rust side; this test pins
    // construction-time validity on the Solidity side.
    uint32 constant ETH_MAINNET = 0xE000_0001;
    uint32 constant TRON_MAINNET = 0xE000_0010;     // EVM-flavored TVM
    uint32 constant BSC_MAINNET = 0xE000_0030;
    uint32 constant POLYGON_MAINNET = 0xE000_0040;
    uint32 constant POLYGON_ZKEVM = 0xE000_0042;    // ZK rollup variant of Polygon
    uint32 constant ARBITRUM_ONE = 0xE000_0050;
    uint32 constant ARBITRUM_NOVA = 0xE000_0052;    // AnyTrust data-sharing variant
    uint32 constant OPTIMISM_MAINNET = 0xE000_0060;
    uint32 constant BASE_MAINNET = 0xE000_0070;
    uint32 constant AVALANCHE_C_MAINNET = 0xE000_0080;
    uint32 constant LINEA_MAINNET = 0xE000_0090;
    uint32 constant ZKSYNC_ERA_MAINNET = 0xE000_00A0;
    uint32 constant SCROLL_MAINNET = 0xE000_00B0;
    uint32 constant MANTLE_MAINNET = 0xE000_00C0;
    uint32 constant FANTOM_OPERA = 0xE000_00D0;
    uint32 constant SONIC_MAINNET = 0xE000_00D1;    // Rebranded Fantom (separate chainId)
    uint32 constant CELO_MAINNET = 0xE000_00E0;

    uint32 constant NEO_L2 = 1099;

    // Watcher private keys for the cross-committee test.
    uint256 constant priv_bsc_a = 0xAAAA1111111111111111111111111111111111111111111111111111111111;
    uint256 constant priv_bsc_b = 0xBBBB2222222222222222222222222222222222222222222222222222222222;
    uint256 constant priv_polygon_a = 0xCCCC3333333333333333333333333333333333333333333333333333333333;
    uint256 constant priv_polygon_b = 0xDDDD4444444444444444444444444444444444444444444444444444444444;

    address bscSignerA;
    address bscSignerB;
    address polygonSignerA;
    address polygonSignerB;

    function setUp() public {
        bscSignerA = vm.addr(priv_bsc_a);
        bscSignerB = vm.addr(priv_bsc_b);
        polygonSignerA = vm.addr(priv_polygon_a);
        polygonSignerB = vm.addr(priv_polygon_b);
    }

    // ─── 1: every canonical family-bank mainnet constructs ────────────────

    /// Iterate every canonical mainnet slot in the EVM family + assert
    /// the router constructs cleanly + records its externalChainId. This
    /// is the load-bearing claim from `docs/external-bridge-evm-chains.md`:
    /// adding a new EVM chain takes 5 steps and writes ZERO new code.
    /// Solidity-side proof of that claim lives here.
    function test_AllFamilyBankMainnetsConstruct() public {
        uint32[17] memory ids = [
            ETH_MAINNET,
            TRON_MAINNET,
            BSC_MAINNET,
            POLYGON_MAINNET,
            POLYGON_ZKEVM,
            ARBITRUM_ONE,
            ARBITRUM_NOVA,
            OPTIMISM_MAINNET,
            BASE_MAINNET,
            AVALANCHE_C_MAINNET,
            LINEA_MAINNET,
            ZKSYNC_ERA_MAINNET,
            SCROLL_MAINNET,
            MANTLE_MAINNET,
            FANTOM_OPERA,
            SONIC_MAINNET,
            CELO_MAINNET
        ];
        for (uint256 i = 0; i < ids.length; i++) {
            NeoExternalBridgeRouter r = new NeoExternalBridgeRouter(ids[i], address(this));
            assertEq(
                r.externalChainId(),
                ids[i],
                "router must record its constructor-passed externalChainId verbatim"
            );
            // Initial state pins: no committee, no balances, no nonces.
            assertEq(r.threshold(), 0);
            assertEq(r.lockedBalances(address(0)), 0);
            assertEq(r.outboundNonces(NEO_L2), 0);
        }
    }

    /// Variants that should ALSO accept (testnets in each bank):
    function test_TestnetSlotsAlsoConstruct() public {
        uint32[6] memory testnetIds = [
            uint32(0xE000_0002), // ETH_SEPOLIA
            uint32(0xE000_0011), // TRON_NILE_TESTNET
            uint32(0xE000_0031), // BSC_TESTNET
            uint32(0xE000_0041), // POLYGON_AMOY_TESTNET
            uint32(0xE000_0051), // ARBITRUM_SEPOLIA
            uint32(0xE000_0071)  // BASE_SEPOLIA
        ];
        for (uint256 i = 0; i < testnetIds.length; i++) {
            NeoExternalBridgeRouter r = new NeoExternalBridgeRouter(testnetIds[i], address(this));
            assertEq(r.externalChainId(), testnetIds[i]);
        }
    }

    // ─── 2: out-of-namespace ids rejected ─────────────────────────────────

    /// Pin the namespace prefix check across the boundary. A typo in any
    /// non-`0xE0` byte must revert at construction — would otherwise
    /// silently route into a non-Neo namespace, with the watcher's
    /// signature work happening against an externalChainId Neo's verifier
    /// won't accept.
    function test_OutOfNamespaceIdsRejected() public {
        uint32[5] memory bad = [
            uint32(0x00000001),                  // no prefix
            uint32(0xDF000001),                  // one below 0xE0
            uint32(0xE100_0001),                 // one above 0xE0
            uint32(0xF0_00_00_30),               // BSC slot but wrong family byte
            uint32(0x99000001)                   // arbitrary non-prefix
        ];
        for (uint256 i = 0; i < bad.length; i++) {
            vm.expectRevert("externalChainId not in 0xE0_xx_xx_xx namespace");
            new NeoExternalBridgeRouter(bad[i], address(this));
        }
    }

    // ─── 3: each router stamps its own chain id into Locked ───────────────

    /// Two routers (BSC + Polygon) lock ETH; each emits a Locked event
    /// with its OWN externalChainId. A regression that hardcoded a chain
    /// id somewhere would surface as a wrong topic[1] here.
    function test_EachRouterStampsItsOwnChainIdInLocked() public {
        NeoExternalBridgeRouter bsc = new NeoExternalBridgeRouter(BSC_MAINNET, address(this));
        NeoExternalBridgeRouter polygon = new NeoExternalBridgeRouter(POLYGON_MAINNET, address(this));

        // Provide some ETH; both routers share the test contract balance.
        vm.deal(address(this), 4 ether);
        bytes20 recipient = bytes20(uint160(0xCAFE));

        // BSC lock: expect Locked(externalChainId=0xE0000030, ...).
        vm.expectEmit(true, true, true, true, address(bsc));
        emit NeoExternalBridgeRouter.Locked(
            BSC_MAINNET, NEO_L2, 1, address(this), recipient, address(0), 1 ether, "", 0
        );
        bsc.lockETHAndSend{value: 1 ether}(NEO_L2, recipient, "", 0);

        // Polygon lock: expect Locked(externalChainId=0xE0000040, ...) with
        // its OWN nonce starting at 1 (independent of BSC).
        vm.expectEmit(true, true, true, true, address(polygon));
        emit NeoExternalBridgeRouter.Locked(
            POLYGON_MAINNET, NEO_L2, 1, address(this), recipient, address(0), 2 ether, "", 0
        );
        polygon.lockETHAndSend{value: 2 ether}(NEO_L2, recipient, "", 0);

        assertEq(bsc.outboundNonces(NEO_L2), 1);
        assertEq(polygon.outboundNonces(NEO_L2), 1);
        assertEq(bsc.lockedBalances(address(0)), 1 ether);
        assertEq(polygon.lockedBalances(address(0)), 2 ether);
    }

    // ─── 4: cross-chain message rejected ──────────────────────────────────

    /// A canonical message claiming chain A → routed at router B reverts
    /// at the externalChainId-mismatch check. Without this defense, a
    /// committee co-signature for one chain could finalize a withdrawal
    /// on another (since secp256k1+sha256 doesn't bind to a contract
    /// address, only to the message bytes).
    function test_BscRouterRejectsPolygonMessage() public {
        NeoExternalBridgeRouter bsc = new NeoExternalBridgeRouter(BSC_MAINNET, address(this));

        // Build a message claiming externalChainId = POLYGON_MAINNET.
        // We never reach signature verification — chain-id mismatch
        // reverts first.
        bytes memory polyMsg = _buildAssetTransferMessage(
            POLYGON_MAINNET, NEO_L2, 99, address(0xDEAD), 1 ether, 0
        );
        bytes memory dummyProof = hex"0000"; // sigCount = 0 (won't be checked)

        vm.expectRevert("externalChainId mismatch");
        bsc.finalizeWithdrawal(polyMsg, dummyProof);
    }

    // ─── 5: nonces are per-router-instance ───────────────────────────────

    /// Nonces from interleaved locks across two routers MUST be
    /// independent. A regression that put nonce state in a shared
    /// location would interleave them.
    function test_NoncesAreIndependentPerRouter() public {
        NeoExternalBridgeRouter bsc = new NeoExternalBridgeRouter(BSC_MAINNET, address(this));
        NeoExternalBridgeRouter polygon = new NeoExternalBridgeRouter(POLYGON_MAINNET, address(this));

        vm.deal(address(this), 6 ether);
        bytes20 recipient = bytes20(uint160(0xBEEF));

        // BSC lock #1
        uint64 n1 = bsc.lockETHAndSend{value: 1 ether}(NEO_L2, recipient, "", 0);
        // Polygon lock #1
        uint64 n2 = polygon.lockETHAndSend{value: 1 ether}(NEO_L2, recipient, "", 0);
        // BSC lock #2
        uint64 n3 = bsc.lockETHAndSend{value: 1 ether}(NEO_L2, recipient, "", 0);
        // Polygon lock #2
        uint64 n4 = polygon.lockETHAndSend{value: 1 ether}(NEO_L2, recipient, "", 0);
        // BSC lock #3
        uint64 n5 = bsc.lockETHAndSend{value: 1 ether}(NEO_L2, recipient, "", 0);

        assertEq(n1, 1, "BSC first nonce");
        assertEq(n2, 1, "Polygon first nonce (independent of BSC)");
        assertEq(n3, 2);
        assertEq(n4, 2);
        assertEq(n5, 3);
    }

    // ─── 6: committees are per-router-instance ───────────────────────────

    /// A Polygon-committee signer cannot satisfy a BSC quorum, even if
    /// the externalChainId in the message matches BSC. This pins that
    /// `setCommittee` writes per-instance state — no shared committee
    /// registry. (If there were one, a BSC committee signer's signature
    /// would also satisfy Polygon's quorum, which would be a critical
    /// bug.)
    function test_PolygonCommitteeNotAuthorizedOnBscRouter() public {
        // Two routers, two committees.
        NeoExternalBridgeRouter bsc = new NeoExternalBridgeRouter(BSC_MAINNET, address(this));
        NeoExternalBridgeRouter polygon = new NeoExternalBridgeRouter(POLYGON_MAINNET, address(this));

        address[] memory bscCommittee = new address[](2);
        bscCommittee[0] = bscSignerA;
        bscCommittee[1] = bscSignerB;
        bsc.setCommittee(bscCommittee, 2);

        address[] memory polygonCommittee = new address[](2);
        polygonCommittee[0] = polygonSignerA;
        polygonCommittee[1] = polygonSignerB;
        polygon.setCommittee(polygonCommittee, 2);

        // Lock so the BSC router has something to release.
        vm.deal(address(this), 1 ether);
        bsc.lockETHAndSend{value: 1 ether}(NEO_L2, bytes20(uint160(0xDEAD)), "", 0);

        // Build a message claiming externalChainId = BSC (so it passes
        // the chain-id check), sign with POLYGON signers — which are
        // not on BSC's committee.
        address recipient = address(0xBEEF);
        bytes memory msgBytes = _buildAssetTransferMessage(
            BSC_MAINNET, NEO_L2, 7, recipient, 1 ether, 0
        );
        (uint8 v0, bytes32 r0, bytes32 s0) = vm.sign(priv_polygon_a, sha256(msgBytes));
        (uint8 v1, bytes32 r1, bytes32 s1) = vm.sign(priv_polygon_b, sha256(msgBytes));

        // Build proof claiming committee indices 0 and 1 of BSC, but
        // using Polygon signers' actual signatures. ecrecover yields
        // the Polygon addresses, which don't match BSC's committee[0/1].
        bytes memory proof = _buildProof(0, v0, r0, s0, 1, v1, r1, s1);

        vm.expectRevert("signer != committee[idx] (sig not from claimed member)");
        bsc.finalizeWithdrawal(msgBytes, proof);

        // Sanity: a message with the right Polygon committee + correct
        // chain id (POLYGON_MAINNET) DOES finalize on the polygon router
        // — but only with sufficient locked balance. Lock first.
        vm.deal(address(this), 1 ether);
        polygon.lockETHAndSend{value: 1 ether}(NEO_L2, bytes20(uint160(0xDEAD)), "", 0);

        bytes memory polyMsg = _buildAssetTransferMessage(
            POLYGON_MAINNET, NEO_L2, 7, recipient, 1 ether, 0
        );
        (uint8 vp0, bytes32 rp0, bytes32 sp0) = vm.sign(priv_polygon_a, sha256(polyMsg));
        (uint8 vp1, bytes32 rp1, bytes32 sp1) = vm.sign(priv_polygon_b, sha256(polyMsg));
        bytes memory polyProof = _buildProof(0, vp0, rp0, sp0, 1, vp1, rp1, sp1);

        polygon.finalizeWithdrawal(polyMsg, polyProof);
        assertTrue(polygon.consumedInbound(NEO_L2, 7));
    }

    // ─── helpers (standalone — no shared base needed) ────────────────────

    /// Build canonical ExternalCrossChainMessage bytes for an asset
    /// transfer on a given (externalChainId, neoChainId, nonce).
    function _buildAssetTransferMessage(
        uint32 externalChainId,
        uint32 neoChainId,
        uint64 nonce,
        address recipient,
        uint256 amount,
        uint64 deadline
    ) internal pure returns (bytes memory) {
        bytes memory amountBytes = _amountToLE(amount);
        bytes memory payload = abi.encodePacked(
            address(0),                               // foreignAsset = native
            uint32_LE(uint32(amountBytes.length)),
            amountBytes
        );
        return abi.encodePacked(
            uint32_LE(externalChainId),       // 4B
            uint32_LE(neoChainId),            // 4B
            uint64_LE(nonce),                 // 8B
            uint8(1),                         // direction = NeoToForeign
            bytes20(0),                       // sender
            bytes20(uint160(recipient)),      // recipient
            uint64_LE(deadline),              // 8B
            bytes32(0),                       // sourceTxRef
            uint8(0),                         // messageType = AssetTransfer
            uint32_LE(uint32(payload.length)),
            payload
        );
    }

    function _amountToLE(uint256 amount) internal pure returns (bytes memory) {
        if (amount == 0) return hex"00";
        uint256 v = amount;
        uint256 len = 0;
        uint256 tmp = v;
        while (tmp > 0) { len++; tmp >>= 8; }
        bytes memory b = new bytes(len);
        for (uint256 i = 0; i < len; i++) {
            b[i] = bytes1(uint8(v >> (i * 8)));
        }
        return b;
    }

    function uint32_LE(uint32 v) internal pure returns (bytes memory) {
        return abi.encodePacked(uint8(v), uint8(v >> 8), uint8(v >> 16), uint8(v >> 24));
    }

    function uint64_LE(uint64 v) internal pure returns (bytes memory) {
        bytes memory b = new bytes(8);
        for (uint256 i = 0; i < 8; i++) b[i] = bytes1(uint8(v >> (i * 8)));
        return b;
    }

    /// Build a 2-signer proof (sigCount=2 + 2×66B sig records).
    function _buildProof(
        uint8 idx0, uint8 v0, bytes32 r0, bytes32 s0,
        uint8 idx1, uint8 v1, bytes32 r1, bytes32 s1
    ) internal pure returns (bytes memory) {
        bytes memory out = new bytes(2 + 2 * 66);
        out[0] = bytes1(uint8(2));
        out[1] = bytes1(uint8(0));

        // sig 0 at offset 2..67
        out[2] = bytes1(idx0);
        for (uint256 j = 0; j < 32; j++) out[3 + j] = r0[j];
        for (uint256 j = 0; j < 32; j++) out[35 + j] = s0[j];
        out[67] = bytes1(v0);

        // sig 1 at offset 68..133
        out[68] = bytes1(idx1);
        for (uint256 j = 0; j < 32; j++) out[69 + j] = r1[j];
        for (uint256 j = 0; j < 32; j++) out[101 + j] = s1[j];
        out[133] = bytes1(v1);

        return out;
    }

    /// Allow this test contract to receive ETH from finalized withdrawals
    /// (the polygon path in test_PolygonCommitteeNotAuthorizedOnBscRouter
    /// transfers ETH to address(0xBEEF), which doesn't need this — but
    /// kept for safety if a test recipient is ever the test contract).
    receive() external payable {}
}
