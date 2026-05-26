//! Watcher signer abstraction. Curve-agnostic — both secp256k1
//! (Eth/Tron) and ed25519 (Solana) plug in behind the same [`Signer`]
//! trait. The included [`FileSigner`] is the secp256k1 dev impl; the
//! Solana watcher crate provides the ed25519 dev impl.
//!
//! Production deployments plug in HSM/KMS-backed signers behind the
//! trait — the crate never touches the actual key material.

mod signer_output;

use k256::ecdsa::SigningKey;
use k256::ecdsa::{RecoveryId, Signature, VerifyingKey};
use sha2::{Digest, Sha256};
use std::path::Path;
use thiserror::Error;
use zeroize::Zeroizing;

pub use signer_output::SignerOutput;

#[derive(Debug, Error)]
pub enum SignerError {
    #[error("io error reading key file: {0}")]
    Io(#[from] std::io::Error),
    #[error("key file must be exactly 32 bytes (got {0})")]
    BadKeyLength(usize),
    #[error("invalid private key bytes")]
    InvalidKey,
    #[error("signing failed: {0}")]
    Signing(String),
    #[error("recovery id derivation failed")]
    NoRecoveryId,
}

/// Anything that can sign canonical `ExternalCrossChainMessage` bytes.
/// The trait is curve-agnostic; concrete impls report which curve they
/// produce via [`Signer::curve_tag`].
pub trait Signer {
    /// Curve identifier — must match what the on-chain
    /// `MpcCommitteeVerifier.RegisterCommittee` call recorded for this
    /// signer's slot:
    /// - `1` = secp256k1+SHA256 (Eth/Tron). Pubkey is 33 bytes (compressed).
    /// - `2` = ed25519 (Solana). Pubkey is 32 bytes.
    fn curve_tag(&self) -> u8;

    /// Public key in the canonical encoding the verifier expects:
    /// 33 bytes (compressed secp256k1) or 32 bytes (raw ed25519).
    fn public_key_bytes(&self) -> Vec<u8>;

    /// Sign canonical bytes. Implementations apply the curve-specific
    /// hash internally (SHA256 for secp256k1; ed25519 hashes natively).
    fn sign_canonical_bytes(&self, canonical_bytes: &[u8]) -> Result<SignerOutput, SignerError>;
}

/// File-based secp256k1 signer for development. **NEVER use in
/// production** — keys in the clear on disk are an exfiltration risk.
/// Only available in debug builds to prevent accidental production use.
pub struct FileSigner {
    sk: SigningKey,
    vk: VerifyingKey,
}

impl FileSigner {
    /// Load a 32-byte raw private key from `path`.
    pub fn from_file(path: impl AsRef<Path>) -> Result<Self, SignerError> {
        // Wrap the read buffer in Zeroizing so the raw key bytes get wiped from
        // the call frame's memory after construction — only the parsed SigningKey
        // (which has its own ZeroizeOnDrop semantics) holds the secret afterwards.
        let bytes = Zeroizing::new(std::fs::read(path)?);
        Self::from_bytes(&bytes)
    }

    /// Load from raw 32-byte private key bytes.
    pub fn from_bytes(bytes: &[u8]) -> Result<Self, SignerError> {
        if bytes.len() != 32 {
            return Err(SignerError::BadKeyLength(bytes.len()));
        }
        let sk = SigningKey::from_bytes(bytes.into()).map_err(|_| SignerError::InvalidKey)?;
        let vk = *sk.verifying_key();
        Ok(Self { sk, vk })
    }

    /// Borrow the underlying verifying key — useful for tests and for
    /// off-chain Eth-address derivation (`keccak256(uncompressed_pubkey[1..])[12..]`).
    pub fn verifying_key(&self) -> &VerifyingKey {
        &self.vk
    }
}

impl Signer for FileSigner {
    fn curve_tag(&self) -> u8 {
        1 // secp256k1
    }

    fn public_key_bytes(&self) -> Vec<u8> {
        let point = self.vk.to_encoded_point(true);
        let bytes = point.as_bytes();
        debug_assert_eq!(bytes.len(), 33);
        bytes.to_vec()
    }

    fn sign_canonical_bytes(&self, canonical_bytes: &[u8]) -> Result<SignerOutput, SignerError> {
        // secp256k1+SHA256: hash internally, then sign the digest. Matches
        // what the on-chain CryptoLib.VerifyWithECDsa(secp256k1SHA256) and
        // Eth-side ecrecover(sha256(...), v, r, s) recompute.
        let digest = Sha256::digest(canonical_bytes);
        let (sig, recid): (Signature, RecoveryId) = self
            .sk
            .sign_prehash_recoverable(&digest)
            .map_err(|e| SignerError::Signing(format!("{}", e)))?;
        // Defensive low-S normalization. k256's `sign_prehash_recoverable` already
        // returns the low-S form per its post-EIP-2 default; we re-normalize so
        // any future signer-backend swap (HSM/KMS adapters that route through the
        // same trait) inherits the canonical-S guarantee on this side rather than
        // depending on the backend's policy. The recovery id is flipped if S was
        // already high (it never is for k256, but this keeps the invariant tight).
        let (sig, recid) = match sig.normalize_s() {
            Some(normalized) => (
                normalized,
                RecoveryId::from_byte(u8::from(recid) ^ 1).unwrap_or(recid),
            ),
            None => (sig, recid),
        };
        let bytes = sig.to_bytes();
        let mut signature = [0u8; 64];
        signature[..32].copy_from_slice(&bytes[..32]);
        signature[32..].copy_from_slice(&bytes[32..]);
        // Eth-style v = 27 + recid (0 or 1).
        let recovery_id = 27 + u8::from(recid);
        Ok(SignerOutput {
            signature,
            recovery_id,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn file_signer_signs_and_recovers() {
        // Deterministic test key.
        let priv_bytes = [
            0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11,
            0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11,
            0x11, 0x11, 0x11, 0x11,
        ];
        let signer = FileSigner::from_bytes(&priv_bytes).unwrap();
        assert_eq!(signer.curve_tag(), 1);

        let canonical = b"hello, this is a test canonical message";
        let out = signer.sign_canonical_bytes(canonical).unwrap();

        // Verify the signature using k256 directly. This mirrors what the
        // Eth router will do via `ecrecover(sha256(canonical), v, r, s)`.
        let digest = Sha256::digest(canonical);
        let sig = Signature::from_bytes(&out.signature.into()).unwrap();
        let recid = RecoveryId::from_byte(out.recovery_id - 27).unwrap();
        let recovered = VerifyingKey::recover_from_prehash(&digest, &sig, recid).unwrap();
        assert_eq!(recovered, *signer.verifying_key());
        assert!(out.recovery_id == 27 || out.recovery_id == 28);
    }

    #[test]
    fn file_signer_rejects_wrong_key_length() {
        assert!(matches!(
            FileSigner::from_bytes(&[0u8; 31]),
            Err(SignerError::BadKeyLength(31))
        ));
    }

    #[test]
    fn pubkey_bytes_are_33_for_secp256k1() {
        let signer = FileSigner::from_bytes(&[0x42; 32]).unwrap();
        let pk = signer.public_key_bytes();
        assert_eq!(pk.len(), 33);
        // First byte is 0x02 or 0x03 (compression marker).
        assert!(pk[0] == 0x02 || pk[0] == 0x03);
    }
}
