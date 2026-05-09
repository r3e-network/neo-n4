//! Off-chain watcher daemon for Neo Elastic Network's Tron ↔ Neo external
//! bridge.
//!
//! Tron uses the same secp256k1+SHA256 signature scheme as Ethereum (and
//! the same Keccak256-based address derivation), so this crate is a thin
//! re-export of [`neo_bridge_watcher_eth`] with Tron-specific
//! `externalChainId` constants. The trait abstractions
//! ([`Signer`](neo_bridge_watcher_eth::Signer),
//! [`EventSource`](neo_bridge_watcher_eth::EventSource),
//! [`NeoSubmitter`](neo_bridge_watcher_eth::NeoSubmitter),
//! [`Journal`](neo_bridge_watcher_eth::Journal)) work as-is. The
//! [`WatcherCore`](neo_bridge_watcher_eth::WatcherCore) orchestrator is
//! parameterized over the four traits, so a Tron daemon constructs:
//!
//! ```rust,no_run
//! use neo_bridge_watcher_tron::{TRON_MAINNET_CHAIN_ID, eth::*};
//!
//! # fn run<S: Signer, ES: EventSource, NS: NeoSubmitter, J: Journal>(
//! #     signer: S, source: ES, submitter: NS, journal: J,
//! # ) {
//! let core = WatcherCore::new(
//!     TRON_MAINNET_CHAIN_ID,
//!     signer,
//!     source,
//!     submitter,
//!     journal,
//! );
//! # }
//! ```
//!
//! ...with a Tron-flavored [`EventSource`](neo_bridge_watcher_eth::EventSource)
//! that subscribes to Tron-side `Locked` events (deferred — landed in a
//! later iteration alongside the Eth equivalent), a Tron-side router
//! contract (Solidity, deployable on the Tron VM since it's EVM-flavored;
//! deferred to `external/foreign-contracts/tron/`), and the same
//! [`Signer`](neo_bridge_watcher_eth::Signer) /
//! [`NeoSubmitter`](neo_bridge_watcher_eth::NeoSubmitter) /
//! [`Journal`](neo_bridge_watcher_eth::Journal) impls the Eth daemon uses
//! (production deployments share infrastructure).

/// Tron mainnet chain id. The high byte `0xE0` reserves the foreign-namespace
/// prefix; `0xE0_00_00_10` is canonical for Tron mainnet (vs `0xE0_00_00_01`
/// for Eth mainnet, `0xE0_00_00_20` for Solana mainnet).
pub const TRON_MAINNET_CHAIN_ID: u32 = 0xE000_0010;

/// Tron Nile testnet (the one most operators target for Tron testing).
pub const TRON_NILE_TESTNET_CHAIN_ID: u32 = 0xE000_0011;

/// Tron Shasta testnet (older; less commonly used). Surface so an operator
/// who happens to be on Shasta gets a stable chain-id constant.
pub const TRON_SHASTA_TESTNET_CHAIN_ID: u32 = 0xE000_0012;

/// Re-export the Eth watcher's full public API. Tron daemons import directly
/// from this module — consumers that already use the Eth crate can swap
/// between the two by changing the chain-id constant they pass to
/// `WatcherCore::new`.
pub mod eth {
    pub use neo_bridge_watcher_eth::*;
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Pin the canonical Tron chain-id constants. Off-chain operator config
    /// tooling reads these — a refactor that silently shifts a constant
    /// would break existing committee setups.
    #[test]
    fn tron_chain_ids_have_foreign_namespace_prefix() {
        for id in [
            TRON_MAINNET_CHAIN_ID,
            TRON_NILE_TESTNET_CHAIN_ID,
            TRON_SHASTA_TESTNET_CHAIN_ID,
        ] {
            assert_eq!(
                id & 0xFF00_0000,
                0xE000_0000,
                "chain id 0x{id:08X} must use the 0xE0_xx_xx_xx foreign-namespace prefix"
            );
        }
    }

    #[test]
    fn tron_chain_ids_disjoint_from_eth_and_solana() {
        // Eth uses 0xE0_00_00_01..0F (16 IDs reserved); Tron 0xE0_00_00_10..1F;
        // Solana 0xE0_00_00_20..2F. Pin so a future addition can't accidentally
        // collide.
        assert!(TRON_MAINNET_CHAIN_ID >= 0xE000_0010);
        assert!(TRON_MAINNET_CHAIN_ID < 0xE000_0020);
        assert!(TRON_NILE_TESTNET_CHAIN_ID >= 0xE000_0010);
        assert!(TRON_NILE_TESTNET_CHAIN_ID < 0xE000_0020);
        assert!(TRON_SHASTA_TESTNET_CHAIN_ID >= 0xE000_0010);
        assert!(TRON_SHASTA_TESTNET_CHAIN_ID < 0xE000_0020);
    }
}
