/// One (pubkey, signature) pair.
#[derive(Debug, Clone)]
pub struct PubkeySignature {
    pub pubkey: Vec<u8>,
    pub signature: [u8; 64],
}
