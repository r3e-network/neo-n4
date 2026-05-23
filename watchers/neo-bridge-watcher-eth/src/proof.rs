//! Proof-bytes encoders for both bridge sides.
//!
//! The same set of (pubkey, signature) pairs gets serialized two different
//! ways depending on which side is consuming the proof:
//!
//! - **Neo** ([`NeoProofBytes`]): `[2B sigCount LE] + N × ([keyLen B pubkey][64B sig])`
//!   where `keyLen = 33` for secp256k1 / `32` for ed25519. The
//!   `NeoHub.MpcCommitteeVerifier` decodes this and finds each pubkey in
//!   its committee blob.
//! - **Eth** ([`EthProofBytes`]): `[2B sigCount LE] + N × (1B signerIdx, 32B r, 32B s, 1B v)`.
//!   The `NeoExternalBridgeRouter` indexes into a stored committee array
//!   and runs `ecrecover(sha256(messageBytes), v, r, s)`. Indexed signers
//!   save gas (no full-pubkey embedding per signature).
//!
//! Watchers produce both formats from one signing round and submit each
//! to the appropriate side.

mod eth_proof_bytes;
mod indexed_rsv;
mod neo_proof_bytes;
mod pubkey_signature;

use thiserror::Error;

pub use eth_proof_bytes::EthProofBytes;
pub use indexed_rsv::IndexedRsv;
pub use neo_proof_bytes::NeoProofBytes;
pub use pubkey_signature::PubkeySignature;

/// Curve identifier — must match what the committee was registered with.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum Curve {
    /// secp256k1 (Eth / Tron). 33-byte compressed pubkey.
    Secp256k1 = 1,
    /// ed25519 (Solana). 32-byte raw pubkey.
    Ed25519 = 2,
}

impl Curve {
    pub fn pubkey_len(self) -> usize {
        match self {
            Curve::Secp256k1 => 33,
            Curve::Ed25519 => 32,
        }
    }
}

#[derive(Debug, Error)]
pub enum ProofBuildError {
    #[error("signature count {0} exceeds MaxSigners 256")]
    TooManySigners(usize),
    #[error("pubkey length {got} != {expected} for curve {curve:?}")]
    BadPubkeyLength {
        curve: Curve,
        got: usize,
        expected: usize,
    },
    #[error("signature length {0} != 64")]
    BadSignatureLength(usize),
    #[error("signerIdx {idx} > committee size {size}")]
    SignerIdxOutOfRange { idx: u8, size: usize },
    #[error("v byte {0} not 27 or 28")]
    BadVByte(u8),
}

/// Maximum signers — matches C# `MpcCommitteePayload.MaxSigners`.
pub const MAX_SIGNERS: usize = 256;

#[cfg(test)]
mod tests {
    use super::*;

    fn make_sig(fill: u8, key_len: usize) -> PubkeySignature {
        PubkeySignature {
            pubkey: vec![fill; key_len],
            signature: [fill ^ 0xFF; 64],
        }
    }

    #[test]
    fn neo_proof_secp256k1_layout() {
        let sigs = vec![make_sig(1, 33), make_sig(2, 33), make_sig(3, 33)];
        let bytes = NeoProofBytes::encode(Curve::Secp256k1, &sigs).unwrap();
        // Header (2B) + 3 × (33 + 64) = 2 + 291 = 293
        assert_eq!(bytes.len(), 2 + 3 * (33 + 64));
        // Header is sigCount LE.
        assert_eq!(u16::from_le_bytes([bytes[0], bytes[1]]), 3);
        // First pubkey at offset 2.
        assert_eq!(&bytes[2..2 + 33], &[1u8; 33]);
        // First signature at offset 35.
        assert_eq!(&bytes[35..35 + 64], &[0xFEu8; 64]);
    }

    #[test]
    fn neo_proof_ed25519_layout() {
        let sigs = vec![make_sig(0x10, 32), make_sig(0x20, 32)];
        let bytes = NeoProofBytes::encode(Curve::Ed25519, &sigs).unwrap();
        // Header (2B) + 2 × (32 + 64) = 2 + 192 = 194
        assert_eq!(bytes.len(), 2 + 2 * (32 + 64));
    }

    #[test]
    fn neo_proof_rejects_wrong_pubkey_length() {
        let sigs = vec![make_sig(1, 32)]; // 32B with secp256k1 (expects 33)
        let err = NeoProofBytes::encode(Curve::Secp256k1, &sigs).unwrap_err();
        assert!(matches!(err, ProofBuildError::BadPubkeyLength { .. }));
    }

    #[test]
    fn eth_proof_layout() {
        let sigs = vec![
            IndexedRsv {
                signer_idx: 0,
                r: [0x11; 32],
                s: [0x22; 32],
                v: 27,
            },
            IndexedRsv {
                signer_idx: 2,
                r: [0x33; 32],
                s: [0x44; 32],
                v: 28,
            },
        ];
        let bytes = EthProofBytes::encode(&sigs).unwrap();
        // Header (2B) + 2 × 66 = 2 + 132 = 134
        assert_eq!(bytes.len(), 2 + 2 * 66);
        assert_eq!(u16::from_le_bytes([bytes[0], bytes[1]]), 2);
        // First entry: idx, r, s, v at offsets 2, 3, 35, 67.
        assert_eq!(bytes[2], 0); // signerIdx
        assert_eq!(&bytes[3..35], &[0x11u8; 32]); // r
        assert_eq!(&bytes[35..67], &[0x22u8; 32]); // s
        assert_eq!(bytes[67], 27); // v
    }

    #[test]
    fn eth_proof_rejects_bad_v() {
        let sigs = vec![
            IndexedRsv {
                signer_idx: 0,
                r: [0; 32],
                s: [0; 32],
                v: 26,
            }, // not 27 or 28
        ];
        let err = EthProofBytes::encode(&sigs).unwrap_err();
        assert!(matches!(err, ProofBuildError::BadVByte(26)));
    }
}
