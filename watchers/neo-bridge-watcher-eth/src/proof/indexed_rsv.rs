/// One indexed signer + its (r, s, v) tuple as the Eth router expects.
#[derive(Debug, Clone)]
pub struct IndexedRsv {
    /// Index into the committee array stored on the Eth router.
    pub signer_idx: u8,
    /// 32 bytes.
    pub r: [u8; 32],
    /// 32 bytes.
    pub s: [u8; 32],
    /// 27 or 28 — the recovery id.
    pub v: u8,
}
