//! Off-chain watcher daemon for Neo Elastic Network's Solana ↔ Neo
//! external bridge.
//!
//! Solana uses ed25519 signatures (the curve native to its account
//! identities), so this crate provides an ed25519-flavored
//! [`Ed25519FileSigner`] implementing the
//! [`neo_bridge_watcher_eth::Signer`] trait with `curve_tag = 2`. The
//! [`WatcherCore`](neo_bridge_watcher_eth::WatcherCore) orchestrator and
//! all the other trait abstractions transfer unchanged — confirmation
//! that the Phase-B abstractions held up across curve families.
//!
//! On-chain dispatch: when the daemon submits to
//! `NeoHub.MpcCommitteeVerifier`, the contract reads the registered
//! committee's `curveTag` and routes to `CryptoLib.VerifyWithEd25519`
//! (vs `VerifyWithECDsa(secp256k1SHA256)` for Eth/Tron).
//!
//! ## Solana light-client status
//!
//! Per `docs/external-bridge-roadmap.md` and `doc.md` §11.3.4, Solana
//! stays MPC-committee-only through Phase 3. Tower BFT light-client
//! verification (~1500 validators, lockouts, epoch rotation) is
//! genuinely expensive on-chain, and Helius / Pyth / Wormhole all
//! punted on it. ZK Solana is a Phase 4 R&D item — until then, the
//! committee model below is the only path.

use ed25519_dalek::{Signer as DalekSigner, SigningKey, VerifyingKey};
use neo_bridge_watcher_eth::{Signer, SignerError, SignerOutput};
use std::path::Path;

/// Solana mainnet-beta. The high byte `0xE0` reserves the foreign-namespace
/// prefix; `0xE0_00_00_20` is canonical for Solana mainnet (vs
/// `0xE0_00_00_01` for Eth, `0xE0_00_00_10` for Tron). The full canonical
/// chain-id table — including all EVM-family chains the Eth watcher
/// covers — lives in [`chains`] (re-exported from
/// [`neo_bridge_watcher_eth`]).
pub const SOLANA_MAINNET_CHAIN_ID: u32 = 0xE000_0020;

/// Solana devnet — the canonical "throwaway" Solana network operators
/// target for early integration testing.
pub const SOLANA_DEVNET_CHAIN_ID: u32 = 0xE000_0021;

/// Solana testnet — the validator-staking-aware test network. Less
/// commonly used by application devs but pinned here for completeness.
pub const SOLANA_TESTNET_CHAIN_ID: u32 = 0xE000_0022;

/// Canonical foreign-namespace chain-id table for the entire framework
/// — Eth + every EVM family chain + Solana. Re-exported here so a Solana
/// daemon that needs to cross-reference an EVM constant (e.g., to share
/// committee config across chains) doesn't have to reach into
/// `neo_bridge_watcher_eth` directly.
pub use neo_bridge_watcher_eth::chains;

/// Re-export the Eth watcher's full public API; ed25519 signers and
/// secp256k1 signers both plug into the same trait surface.
pub mod eth {
    pub use neo_bridge_watcher_eth::*;
}

/// File-based ed25519 signer for development. **NEVER use in
/// production** — keys in the clear on disk are an exfiltration risk.
/// Production deployments plug HSM/KMS-backed signers behind the
/// [`Signer`] trait.
pub struct Ed25519FileSigner {
    sk: SigningKey,
    vk: VerifyingKey,
}

impl Ed25519FileSigner {
    /// Load a 32-byte raw ed25519 private key from `path`.
    pub fn from_file(path: impl AsRef<Path>) -> Result<Self, SignerError> {
        let bytes = std::fs::read(path).map_err(SignerError::Io)?;
        Self::from_bytes(&bytes)
    }

    /// Load from raw 32-byte private key bytes.
    pub fn from_bytes(bytes: &[u8]) -> Result<Self, SignerError> {
        if bytes.len() != 32 {
            return Err(SignerError::BadKeyLength(bytes.len()));
        }
        let arr: [u8; 32] = bytes.try_into().map_err(|_| SignerError::InvalidKey)?;
        let sk = SigningKey::from_bytes(&arr);
        let vk = sk.verifying_key();
        Ok(Self { sk, vk })
    }

    /// Borrow the verifying key — useful for tests + for signature
    /// re-verification before submitting on-chain.
    pub fn verifying_key(&self) -> &VerifyingKey {
        &self.vk
    }
}

impl Signer for Ed25519FileSigner {
    fn curve_tag(&self) -> u8 {
        2 // ed25519
    }

    fn public_key_bytes(&self) -> Vec<u8> {
        self.vk.to_bytes().to_vec()
    }

    fn sign_canonical_bytes(&self, canonical_bytes: &[u8]) -> Result<SignerOutput, SignerError> {
        // ed25519 hashes the input internally (via SHA-512). Unlike
        // secp256k1+SHA256, there's no separate "prehash" step — the signer
        // takes the message directly. The on-chain
        // CryptoLib.VerifyWithEd25519 mirrors that call shape.
        let signature = self.sk.sign(canonical_bytes);
        Ok(SignerOutput {
            signature: signature.to_bytes(),
            // ed25519 has no recovery_id concept; ECDSA conventions don't
            // apply. Return 0 — consumers that branch on curve_tag() will
            // ignore this byte for ed25519 signers.
            recovery_id: 0,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use ed25519_dalek::Verifier;

    #[test]
    fn solana_chain_ids_have_foreign_namespace_prefix() {
        for id in [
            SOLANA_MAINNET_CHAIN_ID,
            SOLANA_DEVNET_CHAIN_ID,
            SOLANA_TESTNET_CHAIN_ID,
        ] {
            assert_eq!(
                id & 0xFF00_0000,
                0xE000_0000,
                "chain id 0x{id:08X} must use the 0xE0_xx_xx_xx foreign-namespace prefix"
            );
        }
    }

    #[test]
    fn solana_chain_ids_disjoint_from_eth_and_tron() {
        // Slot allocation (matches eth/tron crate constants):
        //   0xE0_00_00_01..0F  Eth + Eth testnets
        //   0xE0_00_00_10..1F  Tron + Tron testnets
        //   0xE0_00_00_20..2F  Solana + Solana testnets
        for id in [
            SOLANA_MAINNET_CHAIN_ID,
            SOLANA_DEVNET_CHAIN_ID,
            SOLANA_TESTNET_CHAIN_ID,
        ] {
            assert!((0xE000_0020..0xE000_0030).contains(&id));
        }
    }

    #[test]
    fn ed25519_signer_signs_and_verifies() {
        let priv_bytes = [0x42u8; 32];
        let signer = Ed25519FileSigner::from_bytes(&priv_bytes).unwrap();
        assert_eq!(signer.curve_tag(), 2);

        let pk_bytes = signer.public_key_bytes();
        assert_eq!(pk_bytes.len(), 32, "ed25519 pubkey is exactly 32 bytes");

        let canonical = b"hello, this is a Solana test canonical message";
        let out = signer.sign_canonical_bytes(canonical).unwrap();
        assert_eq!(out.signature.len(), 64);
        assert_eq!(out.recovery_id, 0, "ed25519 has no recovery id");

        // Re-verify using ed25519-dalek directly — same call shape the
        // on-chain CryptoLib.VerifyWithEd25519 makes.
        let vk = signer.verifying_key();
        let sig = ed25519_dalek::Signature::from_bytes(&out.signature);
        vk.verify(canonical, &sig)
            .expect("signature must verify under signer's pubkey");
    }

    #[test]
    fn ed25519_signer_rejects_wrong_key_length() {
        assert!(matches!(
            Ed25519FileSigner::from_bytes(&[0u8; 31]),
            Err(SignerError::BadKeyLength(31))
        ));
    }

    #[test]
    fn ed25519_pubkey_is_exactly_32_bytes() {
        // Pin the curve-distinguishing length so a future Signer impl
        // returning the wrong length fails loudly at the call site.
        let signer = Ed25519FileSigner::from_bytes(&[0x33; 32]).unwrap();
        assert_eq!(signer.public_key_bytes().len(), 32);
    }

    #[test]
    fn signer_trait_dispatches_by_curve_tag() {
        // Polymorphism across curves: a Vec<Box<dyn Signer>> can hold both
        // secp256k1 and ed25519 signers, and curve_tag distinguishes them.
        // Pin so a future trait change can't accidentally make impls
        // non-object-safe.
        let secp = neo_bridge_watcher_eth::FileSigner::from_bytes(&[0x11; 32]).unwrap();
        let ed = Ed25519FileSigner::from_bytes(&[0x22; 32]).unwrap();
        let signers: Vec<Box<dyn Signer>> = vec![Box::new(secp), Box::new(ed)];
        assert_eq!(signers[0].curve_tag(), 1);
        assert_eq!(signers[0].public_key_bytes().len(), 33);
        assert_eq!(signers[1].curve_tag(), 2);
        assert_eq!(signers[1].public_key_bytes().len(), 32);
    }
}
