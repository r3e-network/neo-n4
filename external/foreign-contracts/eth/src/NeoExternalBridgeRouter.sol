// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

/// @title NeoExternalBridgeRouter
/// @notice Eth-side counterpart to NeoHub.ExternalBridgeEscrow. Locks ETH /
///         ERC-20 bound for a Neo chain (emits events watchers attest), and
///         finalizes Neo → Eth withdrawals when a quorum of registered
///         committee members has attested.
/// @dev    Mirrors what NeoHub.MpcCommitteeVerifier does on the Neo side.
///         The watcher signs the canonical ExternalCrossChainMessage bytes
///         (102B prefix + payload, no trailing messageHash) — the same bytes
///         Neo's verifier hashes via SHA256 internally. Eth side recomputes
///         sha256(messageBytes) and runs ecrecover against it; ecrecover
///         doesn't care which hash function produced the digest, so the
///         single signature verifies on both sides.
///
///         External chain id namespacing: Neo's foreign-namespace 0xE0_xx_xx_xx
///         is mirrored here. THIS_EXTERNAL_CHAIN_ID identifies the Eth
///         deployment (mainnet=0xE0000001, Sepolia=0xE0000002).
interface IERC20 {
    function transfer(address to, uint256 amount) external returns (bool);
    function transferFrom(address from, address to, uint256 amount) external returns (bool);
}

contract NeoExternalBridgeRouter {
    // ─── constants ────────────────────────────────────────────────────────
    /// @notice The externalChainId Neo's verifier sees for this deployment.
    uint32 public immutable externalChainId;

    /// @notice Maximum committee size (defensive; production typically 7-21).
    uint8 public constant MAX_COMMITTEE_SIZE = 64;

    /// @notice Wire-format constants for the canonical ExternalCrossChainMessage
    ///         (matches Neo.L2.Messaging.ExternalMessageHasher).
    uint16 private constant FIXED_PREFIX_LEN = 102;
    uint16 private constant NONCE_OFFSET = 8;
    uint16 private constant DIRECTION_OFFSET = 16;
    uint16 private constant MESSAGE_TYPE_OFFSET = 81;

    /// @notice Direction values.
    uint8 private constant DIR_NEO_TO_FOREIGN = 1;
    uint8 private constant DIR_FOREIGN_TO_NEO = 2;

    /// @notice Message types.
    uint8 private constant MSG_TYPE_ASSET_TRANSFER = 0;
    uint8 private constant MSG_TYPE_CALL = 1;
    uint8 private constant MSG_TYPE_ASSET_AND_CALL = 2;

    // ─── state ────────────────────────────────────────────────────────────

    address public owner;

    /// @notice Per-(neoChainId) outbound nonce counter.
    mapping(uint32 => uint64) public outboundNonces;

    /// @notice Replay protection: which inbound (Neo→Eth) nonces have been
    ///         consumed already, indexed by srcChainId (the Neo chain that
    ///         sent the message).
    mapping(uint32 => mapping(uint64 => bool)) public consumedInbound;

    /// @notice Authorized signer addresses. The address is derived from the
    ///         secp256k1 public key as ecrecover would: keccak256(pubkey)[12:].
    ///         Watchers MUST register the address here, NOT the raw pubkey,
    ///         because ecrecover's output is an address.
    address[] public committee;
    mapping(address => bool) public isCommitteeMember;

    /// @notice Signatures required for inbound finalization.
    uint8 public threshold;

    /// @notice Token-specific lock accounting (so the contract can't release
    ///         more than was ever locked — matches NeoHub.ExternalBridgeEscrow).
    mapping(address => uint256) public lockedBalances;

    // ─── events ───────────────────────────────────────────────────────────

    /// @notice Emitted on Lock — watchers subscribe to this.
    /// @param  externalChainId  Always THIS_EXTERNAL_CHAIN_ID; included so
    ///         watchers can reuse the same decoder across chains.
    /// @param  neoChainId       Target Neo L2 (or 0 for L1).
    /// @param  nonce            Per-(neoChainId) outbound counter.
    /// @param  sender           Eth-side sender (msg.sender).
    /// @param  neoRecipient     Last 20B of the Neo recipient address.
    /// @param  asset            ERC-20 hash; address(0) for native ETH.
    /// @param  amount           Locked amount (token-decimals-native).
    /// @param  payload          Arbitrary call payload, copied verbatim into
    ///                          the canonical ExternalCrossChainMessage.
    /// @param  deadline         Unix-seconds; 0 = no deadline.
    event Locked(
        uint32 indexed externalChainId,
        uint32 indexed neoChainId,
        uint64 indexed nonce,
        address sender,
        bytes20 neoRecipient,
        address asset,
        uint256 amount,
        bytes payload,
        uint64 deadline
    );

    /// @notice Emitted when a Neo → Eth withdrawal is finalized.
    event WithdrawalFinalized(
        uint32 indexed neoChainId,
        uint64 indexed nonce,
        address recipient,
        address asset,
        uint256 amount
    );

    /// @notice Emitted when the committee is registered or replaced.
    event CommitteeRegistered(address[] members, uint8 threshold);

    /// @notice Emitted when ownership is transferred.
    event OwnershipTransferred(address indexed previousOwner, address indexed newOwner);

    // ─── modifiers ────────────────────────────────────────────────────────

    modifier onlyOwner() {
        require(msg.sender == owner, "not owner");
        _;
    }

    /// @notice Reentrancy guard. Cheaper than OZ's because we have a single
    ///         re-entrant surface (finalizeWithdrawal calls into the asset).
    uint256 private _locked = 1;
    modifier nonReentrant() {
        require(_locked == 1, "reentrant");
        _locked = 2;
        _;
        _locked = 1;
    }

    // ─── constructor ──────────────────────────────────────────────────────

    constructor(uint32 _externalChainId, address _owner) {
        require(
            _externalChainId & 0xFF000000 == 0xE0000000,
            "externalChainId not in 0xE0_xx_xx_xx namespace"
        );
        require(_owner != address(0), "zero owner");
        externalChainId = _externalChainId;
        owner = _owner;
        emit OwnershipTransferred(address(0), _owner);
    }

    // ─── owner ────────────────────────────────────────────────────────────

    /// @notice Transfer contract ownership.
    function transferOwnership(address newOwner) external onlyOwner {
        require(newOwner != address(0), "zero newOwner");
        emit OwnershipTransferred(owner, newOwner);
        owner = newOwner;
    }

    /// @notice Register (or replace) the committee. The watchers' addresses
    ///         must be canonical Eth addresses derived from their secp256k1
    ///         pubkeys via keccak256(pubkey)[12:]. The Neo side stores the
    ///         33-byte compressed pubkey for the same identity.
    function setCommittee(address[] calldata members, uint8 _threshold) external onlyOwner {
        require(members.length > 0, "empty committee");
        require(members.length <= MAX_COMMITTEE_SIZE, "committee too large");
        require(_threshold > 0 && _threshold <= members.length, "bad threshold");

        // Wipe old membership map.
        for (uint256 i = 0; i < committee.length; i++) {
            isCommitteeMember[committee[i]] = false;
        }

        delete committee;
        for (uint256 i = 0; i < members.length; i++) {
            address m = members[i];
            require(m != address(0), "zero member");
            require(!isCommitteeMember[m], "duplicate member");
            committee.push(m);
            isCommitteeMember[m] = true;
        }
        threshold = _threshold;
        emit CommitteeRegistered(members, _threshold);
    }

    // ─── outbound (Eth → Neo) ─────────────────────────────────────────────

    /// @notice Lock ERC-20 tokens bound for a Neo chain. Caller must approve
    ///         this contract for `amount` first.
    /// @return nonce The (neoChainId, nonce) pair watchers use as the lookup
    ///         key for the canonical ExternalCrossChainMessage.
    function lockERC20AndSend(
        uint32 neoChainId,
        bytes20 neoRecipient,
        address asset,
        uint256 amount,
        bytes calldata payload,
        uint64 deadline
    ) external nonReentrant returns (uint64 nonce) {
        require(asset != address(0), "use lockETHAndSend for native ETH");
        require(amount > 0, "zero amount");
        require(neoRecipient != bytes20(0), "zero recipient");

        bool ok = IERC20(asset).transferFrom(msg.sender, address(this), amount);
        require(ok, "ERC-20 transferFrom failed");

        lockedBalances[asset] += amount;
        nonce = _allocateNonce(neoChainId);
        emit Locked(
            externalChainId,
            neoChainId,
            nonce,
            msg.sender,
            neoRecipient,
            asset,
            amount,
            payload,
            deadline
        );
    }

    /// @notice Lock native ETH bound for a Neo chain. Pass `amount` ETH as
    ///         msg.value.
    function lockETHAndSend(
        uint32 neoChainId,
        bytes20 neoRecipient,
        bytes calldata payload,
        uint64 deadline
    ) external payable nonReentrant returns (uint64 nonce) {
        require(msg.value > 0, "zero amount");
        require(neoRecipient != bytes20(0), "zero recipient");

        lockedBalances[address(0)] += msg.value;
        nonce = _allocateNonce(neoChainId);
        emit Locked(
            externalChainId,
            neoChainId,
            nonce,
            msg.sender,
            neoRecipient,
            address(0),
            msg.value,
            payload,
            deadline
        );
    }

    function _allocateNonce(uint32 neoChainId) private returns (uint64) {
        uint64 next = outboundNonces[neoChainId] + 1;
        outboundNonces[neoChainId] = next;
        return next;
    }

    // ─── inbound (Neo → Eth) ──────────────────────────────────────────────

    /// @notice Finalize a Neo → Eth withdrawal. The committee must have
    ///         attested by signing `messageBytes` (the canonical
    ///         ExternalCrossChainMessage pre-image — same bytes Neo's
    ///         verifier hashes). `proofBytes` carries the indexed-signer
    ///         attestation:
    ///
    ///             [2B sigCount LE]
    ///             [sigCount × (1B signerIdx, 32B r, 32B s, 1B v)]
    ///
    ///         sigCount must be ≥ threshold; signers must be distinct
    ///         committee indices; ecrecover(sha256(messageBytes), v, r, s)
    ///         must equal committee[signerIdx] for each.
    ///
    ///         For AssetTransfer messages the function transfers asset to
    ///         the recipient embedded in messageBytes. For Call / AssetAndCall,
    ///         this v0 router routes to a registered handler (deferred to
    ///         a future version — currently rejects).
    function finalizeWithdrawal(bytes calldata messageBytes, bytes calldata proofBytes)
        external
        nonReentrant
    {
        require(messageBytes.length >= FIXED_PREFIX_LEN, "messageBytes too short");

        // 1. Parse the canonical layout.
        // [4B externalChainId][4B neoChainId][8B nonce][1B direction][20B sender]
        // [20B recipient][8B deadline][32B sourceTxRef][1B messageType][4B payloadLen][payload]
        uint32 msgExternalChainId = _readUint32LE(messageBytes, 0);
        require(msgExternalChainId == externalChainId, "externalChainId mismatch");
        uint32 srcNeoChainId = _readUint32LE(messageBytes, 4);
        uint64 nonce = _readUint64LE(messageBytes, NONCE_OFFSET);
        uint8 direction = uint8(messageBytes[DIRECTION_OFFSET]);
        require(direction == DIR_NEO_TO_FOREIGN, "direction != NeoToForeign");

        require(!consumedInbound[srcNeoChainId][nonce], "nonce already consumed");

        // Recipient lives at offset 37 (20 bytes).
        address recipient = _readAddress(messageBytes, 37);
        // Deadline at offset 57.
        uint64 deadline = _readUint64LE(messageBytes, 57);
        if (deadline != 0) {
            require(block.timestamp <= deadline, "message past deadline");
        }
        uint8 messageType = uint8(messageBytes[MESSAGE_TYPE_OFFSET]);

        // 2. Verify signatures over sha256(messageBytes).
        bytes32 digest = sha256(messageBytes);
        _verifyQuorum(digest, proofBytes);

        // 3. Decode payload + dispatch.
        if (messageType == MSG_TYPE_ASSET_TRANSFER) {
            // Payload layout: [20B foreignAsset][4B amountLen][N B amount LE]
            uint32 payloadLen = _readUint32LE(messageBytes, 98);
            require(messageBytes.length == FIXED_PREFIX_LEN + payloadLen, "payload length mismatch");
            require(payloadLen >= 24, "asset-transfer payload too short");
            address asset = _readAddress(messageBytes, FIXED_PREFIX_LEN);
            uint32 amountLen = _readUint32LE(messageBytes, FIXED_PREFIX_LEN + 20);
            require(amountLen > 0 && amountLen <= 32, "amountLen out of bounds");
            require(payloadLen == 24 + amountLen, "payload length != 24 + amountLen");
            uint256 amount = _readUintLE(messageBytes, FIXED_PREFIX_LEN + 24, amountLen);
            require(lockedBalances[asset] >= amount, "amount exceeds locked balance");
            lockedBalances[asset] -= amount;

            consumedInbound[srcNeoChainId][nonce] = true;

            if (asset == address(0)) {
                (bool sent,) = recipient.call{value: amount}("");
                require(sent, "ETH transfer failed");
            } else {
                bool ok = IERC20(asset).transfer(recipient, amount);
                require(ok, "ERC-20 transfer failed");
            }
            emit WithdrawalFinalized(srcNeoChainId, nonce, recipient, asset, amount);
        } else {
            // MSG_TYPE_CALL and MSG_TYPE_ASSET_AND_CALL — out of v0 scope.
            // Ship the verifier path here so a future router can extend
            // dispatch without re-deploying the committee state.
            revert("messageType not yet supported");
        }
    }

    /// @notice Verify ≥ threshold distinct committee members signed the digest.
    /// @dev    Reverts on any failure (below threshold, duplicate signer,
    ///         signature recovers to non-committee address, etc.).
    function _verifyQuorum(bytes32 digest, bytes calldata proofBytes) private view {
        uint256 committeeLen = committee.length;
        require(committeeLen > 0, "committee not registered");
        require(threshold > 0 && threshold <= committeeLen, "invalid threshold");

        require(proofBytes.length >= 2, "proofBytes too short");
        uint16 sigCount = uint16(uint8(proofBytes[0])) | (uint16(uint8(proofBytes[1])) << 8);
        require(sigCount >= threshold, "below threshold");
        require(sigCount <= committeeLen, "too many signatures");
        // 1B signerIdx + 32B r + 32B s + 1B v = 66 bytes per sig.
        require(proofBytes.length == 2 + uint256(sigCount) * 66, "proofBytes length mismatch");

        uint256 seenBitmap = 0; // bit i set iff committee[i] has signed

        for (uint256 i = 0; i < sigCount; i++) {
            uint256 base = 2 + i * 66;
            uint8 idx = uint8(proofBytes[base]);
            require(idx < committeeLen, "signerIdx out of range");
            uint256 bit = uint256(1) << idx;
            require(seenBitmap & bit == 0, "duplicate signer");
            seenBitmap |= bit;

            bytes32 r;
            bytes32 s;
            assembly {
                let off := add(proofBytes.offset, add(base, 1))
                r := calldataload(off)
                s := calldataload(add(off, 32))
            }
            uint8 v = uint8(proofBytes[base + 65]);

            address signer = ecrecover(digest, v, r, s);
            require(signer != address(0), "ecrecover failed (malformed sig)");
            require(
                signer == committee[idx], "signer != committee[idx] (sig not from claimed member)"
            );
        }
    }

    // ─── byte-readers ─────────────────────────────────────────────────────

    function _readUint32LE(bytes calldata data, uint256 offset) private pure returns (uint32) {
        return uint32(uint8(data[offset])) | (uint32(uint8(data[offset + 1])) << 8)
            | (uint32(uint8(data[offset + 2])) << 16) | (uint32(uint8(data[offset + 3])) << 24);
    }

    function _readUint64LE(bytes calldata data, uint256 offset) private pure returns (uint64) {
        uint64 v = 0;
        for (uint256 i = 0; i < 8; i++) {
            v |= uint64(uint8(data[offset + i])) << (8 * uint64(i));
        }
        return v;
    }

    function _readUintLE(bytes calldata data, uint256 offset, uint256 length)
        private
        pure
        returns (uint256)
    {
        uint256 v = 0;
        for (uint256 i = 0; i < length; i++) {
            v |= uint256(uint8(data[offset + i])) << (8 * i);
        }
        return v;
    }

    function _readAddress(bytes calldata data, uint256 offset) private pure returns (address) {
        // Read 20 bytes starting at offset and pack as an address.
        // Solidity stores addresses big-endian; the canonical message
        // format stores them as the raw 20-byte UInt160 (which is the
        // same byte order Eth uses).
        bytes20 packed;
        assembly {
            let off := add(data.offset, offset)
            // calldataload returns 32 bytes; we want the first 20.
            packed := calldataload(off)
        }
        return address(packed);
    }

    /// @notice Accept ETH only via lockETHAndSend; reject bare transfers so
    ///         tokens can't sit orphaned in the contract.
    receive() external payable {
        revert("call lockETHAndSend to bridge ETH");
    }
}
