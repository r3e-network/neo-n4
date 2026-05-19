// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "forge-std/Test.sol";
import "../src/NeoExternalBridgeRouter.sol";

contract MockERC20 {
    string public name = "Mock";
    string public symbol = "MCK";
    uint8 public decimals = 18;
    uint256 public totalSupply;
    mapping(address => uint256) public balanceOf;
    mapping(address => mapping(address => uint256)) public allowance;

    event Transfer(address indexed from, address indexed to, uint256 amount);
    event Approval(address indexed owner, address indexed spender, uint256 amount);

    function mint(address to, uint256 amount) external {
        balanceOf[to] += amount;
        totalSupply += amount;
        emit Transfer(address(0), to, amount);
    }

    function approve(address spender, uint256 amount) external returns (bool) {
        allowance[msg.sender][spender] = amount;
        emit Approval(msg.sender, spender, amount);
        return true;
    }

    function transfer(address to, uint256 amount) external returns (bool) {
        balanceOf[msg.sender] -= amount;
        balanceOf[to] += amount;
        emit Transfer(msg.sender, to, amount);
        return true;
    }

    function transferFrom(address from, address to, uint256 amount) external returns (bool) {
        allowance[from][msg.sender] -= amount;
        balanceOf[from] -= amount;
        balanceOf[to] += amount;
        emit Transfer(from, to, amount);
        return true;
    }
}

contract NeoExternalBridgeRouterTest is Test {
    uint32 constant ETH_SEPOLIA = 0xE0000002;
    uint32 constant NEO_L2 = 1099;

    NeoExternalBridgeRouter router;
    MockERC20 token;

    // Three watcher private keys + their derived Eth addresses.
    uint256 priv0 = 0x1111111111111111111111111111111111111111111111111111111111111111;
    uint256 priv1 = 0x2222222222222222222222222222222222222222222222222222222222222222;
    uint256 priv2 = 0x3333333333333333333333333333333333333333333333333333333333333333;
    address signer0;
    address signer1;
    address signer2;

    function setUp() public {
        signer0 = vm.addr(priv0);
        signer1 = vm.addr(priv1);
        signer2 = vm.addr(priv2);

        router = new NeoExternalBridgeRouter(ETH_SEPOLIA, address(this));
        token = new MockERC20();

        address[] memory committee = new address[](3);
        committee[0] = signer0;
        committee[1] = signer1;
        committee[2] = signer2;
        router.setCommittee(committee, 2);
    }

    // ─── constructor + setCommittee ──────────────────────────────────────

    function test_Constructor_RejectsNonNamespacedChainId() public {
        vm.expectRevert("externalChainId not in 0xE0_xx_xx_xx namespace");
        new NeoExternalBridgeRouter(uint32(1099), address(this));
    }

    function test_SetCommittee_RejectsBadThreshold() public {
        address[] memory members = new address[](2);
        members[0] = signer0;
        members[1] = signer1;
        vm.expectRevert("bad threshold");
        router.setCommittee(members, 0);
        vm.expectRevert("bad threshold");
        router.setCommittee(members, 3);
    }

    function test_SetCommittee_RejectsDuplicates() public {
        address[] memory members = new address[](3);
        members[0] = signer0;
        members[1] = signer0; // dup
        members[2] = signer2;
        vm.expectRevert("duplicate member");
        router.setCommittee(members, 2);
    }

    // ─── ownership transfer (two-step) ───────────────────────────────────

    function test_TransferOwnership_TwoStep_RequiresAccept() public {
        address newOwner = address(0xC0FFEE);
        // Step 1: current owner initiates — does NOT change `owner`.
        router.transferOwnership(newOwner);
        assertEq(router.owner(), address(this), "owner must not change on initiate");
        assertEq(router.pendingOwner(), newOwner, "pendingOwner recorded");
        // Step 2: new owner finalizes from their own wallet.
        vm.prank(newOwner);
        router.acceptOwnership();
        assertEq(router.owner(), newOwner, "owner flips on accept");
        assertEq(router.pendingOwner(), address(0), "pendingOwner cleared on accept");
    }

    function test_AcceptOwnership_RejectsNonPending() public {
        router.transferOwnership(address(0xC0FFEE));
        // A third party can't claim the pending transfer.
        vm.prank(address(0xBAD));
        vm.expectRevert("not pending owner");
        router.acceptOwnership();
        // Old owner can't accept either — only `pendingOwner` can.
        vm.expectRevert("not pending owner");
        router.acceptOwnership();
    }

    function test_TransferOwnership_OverwritesPending() public {
        // Initiating twice replaces the pending address — typo recovery while still
        // unchanged-owner. Old `pendingOwner` loses its right to accept.
        router.transferOwnership(address(0xC0FFEE));
        router.transferOwnership(address(0xBEEF));
        assertEq(router.pendingOwner(), address(0xBEEF));
        vm.prank(address(0xC0FFEE));
        vm.expectRevert("not pending owner");
        router.acceptOwnership();
        vm.prank(address(0xBEEF));
        router.acceptOwnership();
        assertEq(router.owner(), address(0xBEEF));
    }

    // ─── outbound (Eth → Neo) ────────────────────────────────────────────

    function test_LockETHAndSend_EmitsLocked() public {
        vm.deal(address(this), 1 ether);
        bytes20 recipient = bytes20(uint160(0xDEADBEEF));

        vm.expectEmit(true, true, true, true, address(router));
        emit NeoExternalBridgeRouter.Locked(
            ETH_SEPOLIA, NEO_L2, 1, address(this), recipient, address(0), 1 ether, "", 0
        );
        uint64 nonce = router.lockETHAndSend{value: 1 ether}(NEO_L2, recipient, "", 0);
        assertEq(nonce, 1);
        assertEq(router.lockedBalances(address(0)), 1 ether);
        assertEq(address(router).balance, 1 ether);
        assertEq(router.outboundNonces(NEO_L2), 1);
    }

    function test_LockERC20AndSend_EmitsLocked() public {
        token.mint(address(this), 1000);
        token.approve(address(router), 1000);
        bytes20 recipient = bytes20(uint160(0xCAFEBABE));

        uint64 nonce =
            router.lockERC20AndSend(NEO_L2, recipient, address(token), 1000, hex"AABBCC", 0);
        assertEq(nonce, 1);
        assertEq(router.lockedBalances(address(token)), 1000);
        assertEq(token.balanceOf(address(router)), 1000);
    }

    function test_LockETHAndSend_RejectsZeroAmount() public {
        bytes20 recipient = bytes20(uint160(0xDEAD));
        vm.expectRevert("zero amount");
        router.lockETHAndSend{value: 0}(NEO_L2, recipient, "", 0);
    }

    function test_BareETHTransfer_Reverts() public {
        vm.deal(address(this), 1 ether);
        (bool sent,) = address(router).call{value: 0.5 ether}("");
        assertFalse(sent, "bare transfer must revert");
    }

    // ─── inbound (Neo → Eth) ─────────────────────────────────────────────

    /// Build canonical ExternalCrossChainMessage bytes for an asset transfer.
    /// Layout matches Neo.L2.Messaging.ExternalMessageHasher exactly.
    function _buildTransferMessage(
        uint64 nonce,
        address recipient,
        address asset,
        uint256 amount,
        uint64 deadline
    ) internal pure returns (bytes memory) {
        return _buildTransferMessageWithTxRef(nonce, recipient, asset, amount, deadline, bytes32(0));
    }

    /// Same as _buildTransferMessage but accepts an explicit sourceTxRef. Used by the
    /// regression test that pins MESSAGE_TYPE_OFFSET = 97 (a buggy value of 81 would
    /// read messageType out of the middle of sourceTxRef and silently mis-dispatch or
    /// revert on non-zero tx refs — exactly what production watchers always emit).
    function _buildTransferMessageWithTxRef(
        uint64 nonce,
        address recipient,
        address asset,
        uint256 amount,
        uint64 deadline,
        bytes32 sourceTxRef
    ) internal pure returns (bytes memory) {
        // Asset-transfer payload: [20B foreignAsset][4B amountLen][amountLen B amount LE]
        bytes memory amountBytes = _amountToLE(amount);
        bytes memory payload =
            abi.encodePacked(asset, uint32_LE(uint32(amountBytes.length)), amountBytes);

        return abi.encodePacked(
            uint32_LE(ETH_SEPOLIA), // externalChainId  4B
            uint32_LE(NEO_L2), // neoChainId        4B
            uint64_LE(nonce), // nonce             8B
            uint8(1), // direction=NeoToForeign  1B
            bytes20(0), // sender            20B (zero on Neo side)
            bytes20(uint160(recipient)), // recipient         20B
            uint64_LE(deadline), // deadline          8B
            sourceTxRef, // sourceTxRef       32B
            uint8(0), // messageType=AssetTransfer  1B
            uint32_LE(uint32(payload.length)),
            payload
        );
    }

    function _amountToLE(uint256 amount) internal pure returns (bytes memory) {
        // Smallest non-zero LE encoding. 1 ether = 0x0DE0B6B3A7640000 = 8 bytes.
        if (amount == 0) return hex"00";
        uint256 v = amount;
        uint256 len = 0;
        uint256 tmp = v;
        while (tmp > 0) {
            len++;
            tmp >>= 8;
        }
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
        for (uint256 i = 0; i < 8; i++) {
            b[i] = bytes1(uint8(v >> (i * 8)));
        }
        return b;
    }

    function _signBy(uint256 priv, bytes memory messageBytes)
        internal
        pure
        returns (uint8 v, bytes32 r, bytes32 s)
    {
        bytes32 digest = sha256(messageBytes);
        return vm.sign(priv, digest);
    }

    function _buildProof(
        uint8[] memory indices,
        uint8[] memory vs,
        bytes32[] memory rs,
        bytes32[] memory ss
    ) internal pure returns (bytes memory) {
        require(indices.length == vs.length, "len");
        require(indices.length == rs.length, "len");
        require(indices.length == ss.length, "len");
        bytes memory out = new bytes(2 + indices.length * 66);
        out[0] = bytes1(uint8(indices.length));
        out[1] = bytes1(uint8(indices.length >> 8));
        for (uint256 i = 0; i < indices.length; i++) {
            uint256 base = 2 + i * 66;
            out[base] = bytes1(indices[i]);
            for (uint256 j = 0; j < 32; j++) {
                out[base + 1 + j] = rs[i][j];
            }
            for (uint256 j = 0; j < 32; j++) {
                out[base + 33 + j] = ss[i][j];
            }
            out[base + 65] = bytes1(vs[i]);
        }
        return out;
    }

    function test_FinalizeWithdrawal_HappyPath_ETH() public {
        // First lock 1 ETH outbound so the inbound has something to release.
        vm.deal(address(this), 1 ether);
        router.lockETHAndSend{value: 1 ether}(NEO_L2, bytes20(uint160(0xDEAD)), "", 0);

        address recipient = address(0xBEEF);
        bytes memory msgBytes = _buildTransferMessage(42, recipient, address(0), 1 ether, 0);

        // Watchers 0 and 1 sign — meets threshold of 2.
        (uint8 v0, bytes32 r0, bytes32 s0) = _signBy(priv0, msgBytes);
        (uint8 v1, bytes32 r1, bytes32 s1) = _signBy(priv1, msgBytes);

        uint8[] memory idx = new uint8[](2);
        idx[0] = 0;
        idx[1] = 1;
        uint8[] memory vs = new uint8[](2);
        vs[0] = v0;
        vs[1] = v1;
        bytes32[] memory rs = new bytes32[](2);
        rs[0] = r0;
        rs[1] = r1;
        bytes32[] memory ss = new bytes32[](2);
        ss[0] = s0;
        ss[1] = s1;
        bytes memory proof = _buildProof(idx, vs, rs, ss);

        router.finalizeWithdrawal(msgBytes, proof);
        assertEq(recipient.balance, 1 ether);
        assertEq(router.lockedBalances(address(0)), 0);
        assertTrue(router.consumedInbound(NEO_L2, 42));
    }

    /// Regression for the MESSAGE_TYPE_OFFSET = 97 invariant. Production watchers always
    /// emit a real Neo tx hash in sourceTxRef (offset 65..97). If MESSAGE_TYPE_OFFSET ever
    /// drifts back to a buggy value like 81, the contract would read messageType out of the
    /// middle of sourceTxRef — non-zero refs would then either revert ("not yet supported"
    /// when byte = 1/2) or fall through the unreachable `else` branch (when byte > 2),
    /// silently breaking legitimate withdrawals. The happy-path tests use bytes32(0) for
    /// sourceTxRef so they don't catch this; this one uses a maximally byte-varying ref
    /// to surface the drift on offset miscalculations.
    function test_FinalizeWithdrawal_HappyPath_WithNonZeroSourceTxRef() public {
        vm.deal(address(this), 1 ether);
        router.lockETHAndSend{value: 1 ether}(NEO_L2, bytes20(uint160(0xDEAD)), "", 0);
        address recipient = address(0xBEEF);

        // Every byte non-zero; in particular byte 81 (mid sourceTxRef, what a regressed
        // offset would read) differs visibly from byte 97 (the canonical messageType).
        // sourceTxRef bytes are 0x01..0x32 sequentially; sourceTxRef[16] = 0x17 lands at
        // absolute offset 81; messageType=AssetTransfer=0x00 lands at absolute offset 97.
        bytes32 nonTrivialTxRef = bytes32(
            uint256(0x0102030405060708091011121314151617181920212223242526272829303132)
        );

        bytes memory msgBytes = _buildTransferMessageWithTxRef(
            42, recipient, address(0), 1 ether, 0, nonTrivialTxRef
        );

        // Sanity-check the offset arithmetic so the test is self-explanatory.
        assertEq(uint8(msgBytes[65]), 0x01, "sourceTxRef starts at offset 65");
        assertEq(uint8(msgBytes[81]), 0x17, "byte 81 lives inside sourceTxRef (=23)");
        assertEq(uint8(msgBytes[97]), 0x00, "messageType at offset 97 (AssetTransfer)");

        (uint8 v0, bytes32 r0, bytes32 s0) = _signBy(priv0, msgBytes);
        (uint8 v1, bytes32 r1, bytes32 s1) = _signBy(priv1, msgBytes);

        uint8[] memory idx = new uint8[](2);
        idx[0] = 0;
        idx[1] = 1;
        uint8[] memory vs = new uint8[](2);
        vs[0] = v0;
        vs[1] = v1;
        bytes32[] memory rs = new bytes32[](2);
        rs[0] = r0;
        rs[1] = r1;
        bytes32[] memory ss = new bytes32[](2);
        ss[0] = s0;
        ss[1] = s1;
        bytes memory proof = _buildProof(idx, vs, rs, ss);

        router.finalizeWithdrawal(msgBytes, proof);
        assertEq(recipient.balance, 1 ether);
        assertTrue(router.consumedInbound(NEO_L2, 42));
    }

    function test_FinalizeWithdrawal_RejectsBeforeCommitteeRegistered() public {
        NeoExternalBridgeRouter fresh = new NeoExternalBridgeRouter(ETH_SEPOLIA, address(this));
        vm.deal(address(this), 1 ether);
        fresh.lockETHAndSend{value: 1 ether}(NEO_L2, bytes20(uint160(0xDEAD)), "", 0);

        bytes memory msgBytes = _buildTransferMessage(41, address(0xBEEF), address(0), 1 ether, 0);
        bytes memory emptyProof = hex"0000";

        vm.expectRevert("committee not registered");
        fresh.finalizeWithdrawal(msgBytes, emptyProof);
    }

    function test_FinalizeWithdrawal_RejectsReplay() public {
        vm.deal(address(this), 1 ether);
        router.lockETHAndSend{value: 1 ether}(NEO_L2, bytes20(uint160(0xDEAD)), "", 0);

        address recipient = address(0xBEEF);
        bytes memory msgBytes = _buildTransferMessage(42, recipient, address(0), 0.5 ether, 0);
        (uint8 v0, bytes32 r0, bytes32 s0) = _signBy(priv0, msgBytes);
        (uint8 v1, bytes32 r1, bytes32 s1) = _signBy(priv1, msgBytes);
        uint8[] memory idx = new uint8[](2);
        idx[0] = 0;
        idx[1] = 1;
        uint8[] memory vs = new uint8[](2);
        vs[0] = v0;
        vs[1] = v1;
        bytes32[] memory rs = new bytes32[](2);
        rs[0] = r0;
        rs[1] = r1;
        bytes32[] memory ss = new bytes32[](2);
        ss[0] = s0;
        ss[1] = s1;
        bytes memory proof = _buildProof(idx, vs, rs, ss);

        router.finalizeWithdrawal(msgBytes, proof);
        vm.expectRevert("nonce already consumed");
        router.finalizeWithdrawal(msgBytes, proof);
    }

    function test_FinalizeWithdrawal_RejectsBelowThreshold() public {
        vm.deal(address(this), 1 ether);
        router.lockETHAndSend{value: 1 ether}(NEO_L2, bytes20(uint160(0xDEAD)), "", 0);

        bytes memory msgBytes = _buildTransferMessage(43, address(0xBEEF), address(0), 0.5 ether, 0);
        (uint8 v0, bytes32 r0, bytes32 s0) = _signBy(priv0, msgBytes);
        // Only 1 sig but threshold is 2.
        uint8[] memory idx = new uint8[](1);
        idx[0] = 0;
        uint8[] memory vs = new uint8[](1);
        vs[0] = v0;
        bytes32[] memory rs = new bytes32[](1);
        rs[0] = r0;
        bytes32[] memory ss = new bytes32[](1);
        ss[0] = s0;
        bytes memory proof = _buildProof(idx, vs, rs, ss);

        vm.expectRevert("below threshold");
        router.finalizeWithdrawal(msgBytes, proof);
    }

    function test_FinalizeWithdrawal_RejectsDuplicateSigner() public {
        vm.deal(address(this), 1 ether);
        router.lockETHAndSend{value: 1 ether}(NEO_L2, bytes20(uint160(0xDEAD)), "", 0);

        bytes memory msgBytes = _buildTransferMessage(44, address(0xBEEF), address(0), 0.5 ether, 0);
        (uint8 v0, bytes32 r0, bytes32 s0) = _signBy(priv0, msgBytes);
        // Same signer twice claiming different indices is caught by pubkey
        // check; same signer twice claiming the same index is the bitmap dup.
        uint8[] memory idx = new uint8[](2);
        idx[0] = 0;
        idx[1] = 0;
        uint8[] memory vs = new uint8[](2);
        vs[0] = v0;
        vs[1] = v0;
        bytes32[] memory rs = new bytes32[](2);
        rs[0] = r0;
        rs[1] = r0;
        bytes32[] memory ss = new bytes32[](2);
        ss[0] = s0;
        ss[1] = s0;
        bytes memory proof = _buildProof(idx, vs, rs, ss);

        vm.expectRevert("duplicate signer");
        router.finalizeWithdrawal(msgBytes, proof);
    }

    function test_FinalizeWithdrawal_RejectsPastDeadline() public {
        vm.deal(address(this), 1 ether);
        router.lockETHAndSend{value: 1 ether}(NEO_L2, bytes20(uint160(0xDEAD)), "", 0);

        // deadline = 1; block.timestamp will be > 1.
        vm.warp(1000);
        bytes memory msgBytes = _buildTransferMessage(45, address(0xBEEF), address(0), 0.5 ether, 1);
        (uint8 v0, bytes32 r0, bytes32 s0) = _signBy(priv0, msgBytes);
        (uint8 v1, bytes32 r1, bytes32 s1) = _signBy(priv1, msgBytes);
        uint8[] memory idx = new uint8[](2);
        idx[0] = 0;
        idx[1] = 1;
        uint8[] memory vs = new uint8[](2);
        vs[0] = v0;
        vs[1] = v1;
        bytes32[] memory rs = new bytes32[](2);
        rs[0] = r0;
        rs[1] = r1;
        bytes32[] memory ss = new bytes32[](2);
        ss[0] = s0;
        ss[1] = s1;
        bytes memory proof = _buildProof(idx, vs, rs, ss);

        vm.expectRevert("message past deadline");
        router.finalizeWithdrawal(msgBytes, proof);
    }

    function test_FinalizeWithdrawal_RejectsWrongChainId() public {
        vm.deal(address(this), 1 ether);
        router.lockETHAndSend{value: 1 ether}(NEO_L2, bytes20(uint160(0xDEAD)), "", 0);

        // Build a message claiming a DIFFERENT externalChainId (Tron) — the
        // router for Eth must reject.
        bytes memory msgBytes = abi.encodePacked(
            uint32_LE(0xE0000010), // Tron, not Sepolia
            uint32_LE(NEO_L2),
            uint64_LE(46),
            uint8(1),
            bytes20(0),
            bytes20(uint160(0xBEEF)),
            uint64_LE(0),
            bytes32(0),
            uint8(0),
            uint32_LE(0)
        );
        bytes memory proof = hex"0000"; // empty proof — won't get there
        vm.expectRevert("externalChainId mismatch");
        router.finalizeWithdrawal(msgBytes, proof);
    }
}
