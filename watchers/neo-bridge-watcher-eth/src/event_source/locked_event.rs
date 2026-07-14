/// Bridge-router `Locked` event plus surrounding tx context.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct LockedEvent {
    /// `externalChainId` from the event — should equal the watcher's
    /// configured chain id (e.g. 0xE0000001 for Eth mainnet). The watcher
    /// rejects mismatches as a sanity check before processing.
    pub external_chain_id: u32,

    /// Target Neo L2 chain id.
    pub neo_chain_id: u32,

    /// Outbound nonce assigned by the router.
    pub nonce: u64,

    /// Eth-side sender (msg.sender at lock time).
    pub sender: [u8; 20],

    /// 20-byte Neo recipient.
    pub neo_recipient: [u8; 20],

    /// Opaque network-order ERC-20 address, or the canonical non-zero
    /// `NATIVE_ASSET_SENTINEL` for native ETH.
    pub asset: [u8; 20],

    /// Locked amount, big-endian uint256 (Eth's wire format). The
    /// canonical asset-transfer payload encoder converts to minimal-LE
    /// before signing.
    pub amount: [u8; 32],

    /// Reserved call data. The v0 router rejects non-empty payloads before
    /// custody, and the watcher rejects any legacy non-empty event fail-closed.
    pub payload: Vec<u8>,

    /// Deadline; 0 = no deadline.
    pub deadline: u64,

    /// Eth tx hash that emitted this event. Goes into `sourceTxRef`.
    pub source_tx_hash: [u8; 32],

    /// Block height the event was emitted at — the daemon journals this
    /// so a restart resumes from the right cursor.
    pub block_number: u64,
}
