//! Watcher signer abstraction. Production deployments plug in HSM /
//! KMS-backed signers behind the [`Signer`] trait; the included
//! [`FileSigner`] is for development only — it loads a 32-byte secp256k1
//! private key from a file and uses `k256` to sign.

use k256::ecdsa::{signature::hazmat::PrehashSigner, SigningKey};
use k256::ecdsa::{RecoveryId, Signature, VerifyingKey};
use k256::elliptic_curve::sec1::ToEncodedPoint;
use sha2::{Digest, Sha256};
use std::path::Path;
use thiserror::Error;

#[derive(Debug, Error)]
pub enum SignerError {
    #[error("io error reading key file: {0}")]
    Io(#[from] std::io::Error),
    #[error("key file must be exactly 32 bytes (got {0})")]
    BadKeyLength(usize),
    #[error("invalid secp256k1 private key bytes")]
    InvalidKey,
    #[error("signing failed: {0}")]
    Signing(String),
    #[error("recovery id derivation failed")]
    NoRecoveryId,
}

/// Anything that can sign a 32-byte digest with secp256k1 + ECDSA.
/// The trait does NOT include the SHA256 step — callers pre-hash the
/// canonical message bytes (because Neo and Eth verifiers both run
/// `sha256(messageBytes)` and then ECDSA-verify against that digest).
pub trait Signer {
    /// Compressed 33-byte secp256k1 public key.
    fn public_key_compressed(&self) -> [u8; 33];

    /// Eth-style address: `keccak256(pubkey)[12:]`. NOT used for
    /// verification (Eth uses the address; Neo uses the pubkey), just
    /// emitted alongside so operators can register the same identity on
    /// both sides without a separate address-derivation step.
    fn eth_address(&self) -> [u8; 20];

    /// Sign a 32-byte digest. Returns (r, s, v) where r||s is 64 bytes
    /// and v is 27 or 28 (Ethereum-style recovery id).
    fn sign_prehashed(&self, digest: [u8; 32]) -> Result<([u8; 32], [u8; 32], u8), SignerError>;

    /// Convenience helper: sign canonical `ExternalCrossChainMessage`
    /// bytes by computing `sha256(messageBytes)` first. This matches
    /// what the Eth router's `ecrecover(sha256(messageBytes), v, r, s)`
    /// and what Neo's `CryptoLib.VerifyWithECDsa(secp256k1SHA256)`
    /// internally hash.
    fn sign_canonical_bytes(
        &self,
        canonical_bytes: &[u8],
    ) -> Result<([u8; 32], [u8; 32], u8), SignerError> {
        let digest = Sha256::digest(canonical_bytes);
        self.sign_prehashed(digest.into())
    }
}

/// File-based signer for development. **NEVER use in production** — keys
/// in the clear on disk are an exfiltration risk.
pub struct FileSigner {
    sk: SigningKey,
    vk: VerifyingKey,
}

impl FileSigner {
    /// Load a 32-byte raw private key from `path`.
    pub fn from_file(path: impl AsRef<Path>) -> Result<Self, SignerError> {
        let bytes = std::fs::read(path)?;
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
}

impl Signer for FileSigner {
    fn public_key_compressed(&self) -> [u8; 33] {
        let point = self.vk.to_encoded_point(true);
        let bytes = point.as_bytes();
        debug_assert_eq!(bytes.len(), 33);
        let mut out = [0u8; 33];
        out.copy_from_slice(bytes);
        out
    }

    fn eth_address(&self) -> [u8; 20] {
        // Eth address = keccak256(uncompressed_pubkey[1..])[12..32].
        // We don't pull in the keccak crate here for v0 — production
        // deployments derive the Eth address off-chain anyway. Return
        // zeros and document that the operator computes this once at
        // committee setup time. The compressed pubkey is the
        // load-bearing identity; the address is just a UX field.
        [0u8; 20]
    }

    fn sign_prehashed(&self, digest: [u8; 32]) -> Result<([u8; 32], [u8; 32], u8), SignerError> {
        let (sig, recid): (Signature, RecoveryId) = self
            .sk
            .sign_prehash_recoverable(&digest)
            .map_err(|e| SignerError::Signing(format!("{}", e)))?;
        let bytes = sig.to_bytes();
        let mut r = [0u8; 32];
        let mut s = [0u8; 32];
        r.copy_from_slice(&bytes[..32]);
        s.copy_from_slice(&bytes[32..]);
        // Eth-style v = 27 + recid (0 or 1). Ethereum's ecrecover convention.
        let v = 27 + u8::from(recid);
        Ok((r, s, v))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn file_signer_signs_and_recovers() {
        // Deterministic test key.
        let priv_bytes = [
            0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11,
            0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11,
            0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11,
            0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11,
        ];
        let signer = FileSigner::from_bytes(&priv_bytes).unwrap();

        let canonical = b"hello, this is a test canonical message";
        let (r, s, v) = signer.sign_canonical_bytes(canonical).unwrap();

        // Verify the signature using k256 directly. This mirrors what the
        // Eth router will do via `ecrecover(sha256(canonical), v, r, s)`.
        let digest = Sha256::digest(canonical);
        let mut sig_bytes = [0u8; 64];
        sig_bytes[..32].copy_from_slice(&r);
        sig_bytes[32..].copy_from_slice(&s);
        let sig = Signature::from_bytes(&sig_bytes.into()).unwrap();
        let recid = RecoveryId::from_byte(v - 27).unwrap();
        let recovered = VerifyingKey::recover_from_prehash(&digest, &sig, recid).unwrap();
        assert_eq!(recovered, *signer.sk.verifying_key());
        assert!(v == 27 || v == 28);
    }

    #[test]
    fn file_signer_rejects_wrong_key_length() {
        assert!(matches!(
            FileSigner::from_bytes(&[0u8; 31]),
            Err(SignerError::BadKeyLength(31))
        ));
    }

    #[test]
    fn pubkey_compressed_is_33_bytes() {
        let signer = FileSigner::from_bytes(&[0x42; 32]).unwrap();
        let pk = signer.public_key_compressed();
        // First byte is 0x02 or 0x03 (compression marker).
        assert!(pk[0] == 0x02 || pk[0] == 0x03);
    }
}
