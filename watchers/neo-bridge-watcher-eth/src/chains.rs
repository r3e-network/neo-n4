//! Canonical foreign-namespace slot allocation for EVM and EVM-flavored
//! chains.
//!
//! The Neo Elastic Network's external-bridge namespace is `0xE0_xx_xx_xx`
//! (high byte `0xE0`, 24 bits of chain identity). Within that namespace
//! we use the following stable slot allocation so committee blobs +
//! deploy bundles + chain-id checks across the framework agree:
//!
//! ```text
//! 0xE0_00_00_00..FF    Bank-by-family (16 slots per chain family)
//!   0x00..0F           Ethereum (mainnet, Sepolia, Holesky, ...)
//!   0x10..1F           Tron (mainnet, Nile, Shasta, ...)
//!   0x20..2F           Solana (mainnet, devnet, testnet, ...)
//!   0x30..3F           BSC
//!   0x40..4F           Polygon (PoS + Amoy + zkEVM)
//!   0x50..5F           Arbitrum (One, Sepolia, Nova)
//!   0x60..6F           Optimism
//!   0x70..7F           Base
//!   0x80..8F           Avalanche (C + Fuji)
//!   0x90..9F           Linea
//!   0xA0..AF           zkSync Era
//!   0xB0..BF           Scroll
//!   0xC0..CF           Mantle
//!   0xD0..DF           Fantom / Sonic
//!   0xE0..EF           Celo
//!   0xF0..FF           Reserved
//! 0xE0_00_01_xx..FF_FF Open registry for EVM chains by EIP-155 chainId
//!                      (when chainId fits in 16 bits, lower bytes match
//!                      the EIP-155 value, e.g. Gnosis chainId 100 →
//!                      0xE0_00_00_64 — but that would collide with
//!                      Optimism's `0x64` slot since 100 falls in the
//!                      bank range. Strictly, only chainIds ≥ 256 are
//!                      eligible for direct mapping; anything else needs
//!                      a slot allocation above).
//! 0xE0_01_xx_xx..FF    EVM chains by EIP-155 chainId (chainId in 65536..16M)
//! 0xE0_xx_xx_xx        Reserved for future namespaces
//! ```
//!
//! ## Onboarding a new EVM chain
//!
//! See `docs/external-bridge-evm-chains.md` for the full operator runbook.
//! Short version:
//!
//! 1. Pick a `foreign_chain_id` from this table — or assign a new slot
//!    if the chain isn't listed. Submit a PR adding the constant.
//! 2. Deploy `external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol`
//!    on the target chain with `externalChainId = your_foreign_chain_id`
//!    in the constructor.
//! 3. Run `neo-bridge-watcher-eth --config watcher-<chain>.toml`
//!    pointed at the chain's RPC + the deployed router address.
//! 4. On Neo: use `neo-external-bridge committee-blob` + `deploy-bundle`
//!    to register the committee on `MpcCommitteeVerifier` + bind the
//!    verifier to the new chain id on `ExternalBridgeRegistry`.
//! 5. End users call `lockETHAndSend` / `lockERC20AndSend` on the
//!    deployed router; the watcher relays to Neo.
//!
//! No new Rust crates, no new Solidity contracts — the Eth-side router
//! parameterizes on `externalChainId` via constructor, and the watcher
//! is fully chain-id-driven.

// ─── 0x00..0F: Ethereum family ────────────────────────────────────────

pub const ETH_MAINNET: u32 = 0xE000_0001;
pub const ETH_SEPOLIA: u32 = 0xE000_0002;
pub const ETH_HOLESKY: u32 = 0xE000_0003;
// 0xE000_0004..0F reserved for Eth variants (forks, future testnets)

// ─── 0x10..1F: Tron family ────────────────────────────────────────────

pub const TRON_MAINNET: u32 = 0xE000_0010;
pub const TRON_NILE_TESTNET: u32 = 0xE000_0011;
pub const TRON_SHASTA_TESTNET: u32 = 0xE000_0012;

// ─── 0x20..2F: Solana family ──────────────────────────────────────────

pub const SOLANA_MAINNET: u32 = 0xE000_0020;
pub const SOLANA_DEVNET: u32 = 0xE000_0021;
pub const SOLANA_TESTNET: u32 = 0xE000_0022;

// ─── 0x30..3F: BSC ────────────────────────────────────────────────────

/// Binance Smart Chain mainnet (EIP-155 chainId 56).
pub const BSC_MAINNET: u32 = 0xE000_0030;
/// BSC testnet (EIP-155 chainId 97).
pub const BSC_TESTNET: u32 = 0xE000_0031;

// ─── 0x40..4F: Polygon ────────────────────────────────────────────────

/// Polygon PoS mainnet (EIP-155 chainId 137).
pub const POLYGON_MAINNET: u32 = 0xE000_0040;
/// Polygon Amoy testnet (EIP-155 chainId 80002; replaced Mumbai).
pub const POLYGON_AMOY_TESTNET: u32 = 0xE000_0041;
/// Polygon zkEVM mainnet (EIP-155 chainId 1101).
pub const POLYGON_ZKEVM: u32 = 0xE000_0042;
/// Polygon zkEVM Cardona testnet (EIP-155 chainId 2442).
pub const POLYGON_ZKEVM_CARDONA: u32 = 0xE000_0043;

// ─── 0x50..5F: Arbitrum ───────────────────────────────────────────────

/// Arbitrum One mainnet (EIP-155 chainId 42161).
pub const ARBITRUM_ONE: u32 = 0xE000_0050;
/// Arbitrum Sepolia testnet (EIP-155 chainId 421614).
pub const ARBITRUM_SEPOLIA: u32 = 0xE000_0051;
/// Arbitrum Nova mainnet (EIP-155 chainId 42170).
pub const ARBITRUM_NOVA: u32 = 0xE000_0052;

// ─── 0x60..6F: Optimism ───────────────────────────────────────────────

/// Optimism mainnet (EIP-155 chainId 10).
pub const OPTIMISM_MAINNET: u32 = 0xE000_0060;
/// Optimism Sepolia testnet (EIP-155 chainId 11155420).
pub const OPTIMISM_SEPOLIA: u32 = 0xE000_0061;

// ─── 0x70..7F: Base ───────────────────────────────────────────────────

/// Base mainnet (EIP-155 chainId 8453).
pub const BASE_MAINNET: u32 = 0xE000_0070;
/// Base Sepolia testnet (EIP-155 chainId 84532).
pub const BASE_SEPOLIA: u32 = 0xE000_0071;

// ─── 0x80..8F: Avalanche ──────────────────────────────────────────────

/// Avalanche C-Chain mainnet (EIP-155 chainId 43114).
pub const AVALANCHE_C_MAINNET: u32 = 0xE000_0080;
/// Avalanche Fuji testnet (EIP-155 chainId 43113).
pub const AVALANCHE_FUJI: u32 = 0xE000_0081;

// ─── 0x90..9F: Linea ──────────────────────────────────────────────────

/// Linea mainnet (EIP-155 chainId 59144).
pub const LINEA_MAINNET: u32 = 0xE000_0090;
/// Linea Sepolia testnet (EIP-155 chainId 59141).
pub const LINEA_SEPOLIA: u32 = 0xE000_0091;

// ─── 0xA0..AF: zkSync Era ─────────────────────────────────────────────

/// zkSync Era mainnet (EIP-155 chainId 324).
pub const ZKSYNC_ERA_MAINNET: u32 = 0xE000_00A0;
/// zkSync Sepolia testnet (EIP-155 chainId 300).
pub const ZKSYNC_SEPOLIA: u32 = 0xE000_00A1;

// ─── 0xB0..BF: Scroll ─────────────────────────────────────────────────

/// Scroll mainnet (EIP-155 chainId 534352).
pub const SCROLL_MAINNET: u32 = 0xE000_00B0;
/// Scroll Sepolia testnet (EIP-155 chainId 534351).
pub const SCROLL_SEPOLIA: u32 = 0xE000_00B1;

// ─── 0xC0..CF: Mantle ─────────────────────────────────────────────────

/// Mantle mainnet (EIP-155 chainId 5000).
pub const MANTLE_MAINNET: u32 = 0xE000_00C0;
/// Mantle Sepolia testnet (EIP-155 chainId 5003).
pub const MANTLE_SEPOLIA: u32 = 0xE000_00C1;

// ─── 0xD0..DF: Fantom / Sonic ─────────────────────────────────────────

/// Fantom Opera mainnet (EIP-155 chainId 250).
pub const FANTOM_OPERA: u32 = 0xE000_00D0;
/// Sonic mainnet (EIP-155 chainId 146; Fantom's successor network).
pub const SONIC_MAINNET: u32 = 0xE000_00D1;

// ─── 0xE0..EF: Celo ───────────────────────────────────────────────────

/// Celo mainnet (EIP-155 chainId 42220).
pub const CELO_MAINNET: u32 = 0xE000_00E0;
/// Celo Alfajores testnet (EIP-155 chainId 44787).
pub const CELO_ALFAJORES: u32 = 0xE000_00E1;

// ─── helpers ──────────────────────────────────────────────────────────

/// Validate a 32-bit value is a well-formed foreign chain id (carries
/// the `0xE0_xx_xx_xx` namespace prefix). Other framework code already
/// enforces this at multiple layers; the helper makes the rule
/// inspectable.
pub const fn is_foreign_chain_id(id: u32) -> bool {
    id & 0xFF00_0000 == 0xE000_0000
}

/// Human-readable name for the major canonical chain ids — useful for
/// daemon startup logs + error messages. Returns `None` for unknown ids
/// (which are still valid, just not in the curated table).
pub fn name_for_chain_id(id: u32) -> Option<&'static str> {
    match id {
        ETH_MAINNET => Some("Ethereum mainnet"),
        ETH_SEPOLIA => Some("Ethereum Sepolia"),
        ETH_HOLESKY => Some("Ethereum Holesky"),
        TRON_MAINNET => Some("Tron mainnet"),
        TRON_NILE_TESTNET => Some("Tron Nile testnet"),
        TRON_SHASTA_TESTNET => Some("Tron Shasta testnet"),
        SOLANA_MAINNET => Some("Solana mainnet-beta"),
        SOLANA_DEVNET => Some("Solana devnet"),
        SOLANA_TESTNET => Some("Solana testnet"),
        BSC_MAINNET => Some("BNB Smart Chain mainnet"),
        BSC_TESTNET => Some("BNB Smart Chain testnet"),
        POLYGON_MAINNET => Some("Polygon PoS mainnet"),
        POLYGON_AMOY_TESTNET => Some("Polygon Amoy testnet"),
        POLYGON_ZKEVM => Some("Polygon zkEVM mainnet"),
        POLYGON_ZKEVM_CARDONA => Some("Polygon zkEVM Cardona testnet"),
        ARBITRUM_ONE => Some("Arbitrum One"),
        ARBITRUM_SEPOLIA => Some("Arbitrum Sepolia"),
        ARBITRUM_NOVA => Some("Arbitrum Nova"),
        OPTIMISM_MAINNET => Some("Optimism mainnet"),
        OPTIMISM_SEPOLIA => Some("Optimism Sepolia"),
        BASE_MAINNET => Some("Base mainnet"),
        BASE_SEPOLIA => Some("Base Sepolia"),
        AVALANCHE_C_MAINNET => Some("Avalanche C-Chain"),
        AVALANCHE_FUJI => Some("Avalanche Fuji testnet"),
        LINEA_MAINNET => Some("Linea mainnet"),
        LINEA_SEPOLIA => Some("Linea Sepolia"),
        ZKSYNC_ERA_MAINNET => Some("zkSync Era mainnet"),
        ZKSYNC_SEPOLIA => Some("zkSync Sepolia"),
        SCROLL_MAINNET => Some("Scroll mainnet"),
        SCROLL_SEPOLIA => Some("Scroll Sepolia"),
        MANTLE_MAINNET => Some("Mantle mainnet"),
        MANTLE_SEPOLIA => Some("Mantle Sepolia"),
        FANTOM_OPERA => Some("Fantom Opera"),
        SONIC_MAINNET => Some("Sonic mainnet"),
        CELO_MAINNET => Some("Celo mainnet"),
        CELO_ALFAJORES => Some("Celo Alfajores testnet"),
        _ => None,
    }
}

/// EVM chains in this table that share the same secp256k1+SHA256
/// crypto path — i.e., the entire "EVM family" the watcher's existing
/// `FileSigner` + `EthRpcEventSource` + `NeoExternalBridgeRouter.sol`
/// stack covers without modification.
///
/// Tron is included because TVM is EVM-flavored Solidity (same opcode
/// set for the contract's needs, same secp256k1+Keccak256 address
/// scheme). Solana is excluded — different crypto (ed25519) requires
/// the `neo-bridge-watcher-sol` crate.
pub const EVM_FAMILY_CHAINS: &[u32] = &[
    ETH_MAINNET,
    ETH_SEPOLIA,
    ETH_HOLESKY,
    TRON_MAINNET,
    TRON_NILE_TESTNET,
    TRON_SHASTA_TESTNET,
    BSC_MAINNET,
    BSC_TESTNET,
    POLYGON_MAINNET,
    POLYGON_AMOY_TESTNET,
    POLYGON_ZKEVM,
    POLYGON_ZKEVM_CARDONA,
    ARBITRUM_ONE,
    ARBITRUM_SEPOLIA,
    ARBITRUM_NOVA,
    OPTIMISM_MAINNET,
    OPTIMISM_SEPOLIA,
    BASE_MAINNET,
    BASE_SEPOLIA,
    AVALANCHE_C_MAINNET,
    AVALANCHE_FUJI,
    LINEA_MAINNET,
    LINEA_SEPOLIA,
    ZKSYNC_ERA_MAINNET,
    ZKSYNC_SEPOLIA,
    SCROLL_MAINNET,
    SCROLL_SEPOLIA,
    MANTLE_MAINNET,
    MANTLE_SEPOLIA,
    FANTOM_OPERA,
    SONIC_MAINNET,
    CELO_MAINNET,
    CELO_ALFAJORES,
];

/// Is this chain id in the secp256k1 EVM family (i.e., the existing
/// `neo-bridge-watcher-eth` stack works as-is)? Returns false for
/// Solana ids and unknown ids.
pub fn is_evm_family(chain_id: u32) -> bool {
    EVM_FAMILY_CHAINS.contains(&chain_id)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::HashSet;

    /// Pin the namespace prefix on every constant. A typo (`0xF0...`
    /// instead of `0xE0...`) here would silently route to a non-Neo
    /// namespace — operators would only catch it at deploy time.
    #[test]
    fn all_chain_ids_carry_foreign_namespace_prefix() {
        for id in EVM_FAMILY_CHAINS {
            assert_eq!(
                id & 0xFF00_0000,
                0xE000_0000,
                "chain id 0x{id:08X} must use the 0xE0_xx_xx_xx prefix"
            );
            assert!(is_foreign_chain_id(*id));
        }
        // Solana ids too.
        for id in [SOLANA_MAINNET, SOLANA_DEVNET, SOLANA_TESTNET] {
            assert_eq!(id & 0xFF00_0000, 0xE000_0000);
        }
    }

    /// Pin no two chains share an id. A typo would silently overlap
    /// the BSC ↔ Polygon banks (or any other adjacent pair), which a
    /// committee blob registered for one would route inbounds for the
    /// other.
    #[test]
    fn chain_ids_are_distinct() {
        let mut seen: HashSet<u32> = HashSet::new();
        for &id in EVM_FAMILY_CHAINS {
            assert!(seen.insert(id), "chain id 0x{id:08X} appears twice");
        }
        // Solana ids are also distinct from EVM family.
        for id in [SOLANA_MAINNET, SOLANA_DEVNET, SOLANA_TESTNET] {
            assert!(seen.insert(id),
                "Solana id 0x{id:08X} collides with an EVM-family id");
        }
    }

    /// Pin every constant has a name in the lookup table — so the
    /// daemon's startup log can print a human-readable label. A new
    /// constant added without a matching `name_for_chain_id` arm
    /// would surface here.
    #[test]
    fn every_constant_has_a_human_name() {
        let all = [
            (ETH_MAINNET, "ETH_MAINNET"),
            (ETH_SEPOLIA, "ETH_SEPOLIA"),
            (ETH_HOLESKY, "ETH_HOLESKY"),
            (TRON_MAINNET, "TRON_MAINNET"),
            (TRON_NILE_TESTNET, "TRON_NILE_TESTNET"),
            (TRON_SHASTA_TESTNET, "TRON_SHASTA_TESTNET"),
            (SOLANA_MAINNET, "SOLANA_MAINNET"),
            (SOLANA_DEVNET, "SOLANA_DEVNET"),
            (SOLANA_TESTNET, "SOLANA_TESTNET"),
            (BSC_MAINNET, "BSC_MAINNET"),
            (BSC_TESTNET, "BSC_TESTNET"),
            (POLYGON_MAINNET, "POLYGON_MAINNET"),
            (POLYGON_AMOY_TESTNET, "POLYGON_AMOY_TESTNET"),
            (POLYGON_ZKEVM, "POLYGON_ZKEVM"),
            (POLYGON_ZKEVM_CARDONA, "POLYGON_ZKEVM_CARDONA"),
            (ARBITRUM_ONE, "ARBITRUM_ONE"),
            (ARBITRUM_SEPOLIA, "ARBITRUM_SEPOLIA"),
            (ARBITRUM_NOVA, "ARBITRUM_NOVA"),
            (OPTIMISM_MAINNET, "OPTIMISM_MAINNET"),
            (OPTIMISM_SEPOLIA, "OPTIMISM_SEPOLIA"),
            (BASE_MAINNET, "BASE_MAINNET"),
            (BASE_SEPOLIA, "BASE_SEPOLIA"),
            (AVALANCHE_C_MAINNET, "AVALANCHE_C_MAINNET"),
            (AVALANCHE_FUJI, "AVALANCHE_FUJI"),
            (LINEA_MAINNET, "LINEA_MAINNET"),
            (LINEA_SEPOLIA, "LINEA_SEPOLIA"),
            (ZKSYNC_ERA_MAINNET, "ZKSYNC_ERA_MAINNET"),
            (ZKSYNC_SEPOLIA, "ZKSYNC_SEPOLIA"),
            (SCROLL_MAINNET, "SCROLL_MAINNET"),
            (SCROLL_SEPOLIA, "SCROLL_SEPOLIA"),
            (MANTLE_MAINNET, "MANTLE_MAINNET"),
            (MANTLE_SEPOLIA, "MANTLE_SEPOLIA"),
            (FANTOM_OPERA, "FANTOM_OPERA"),
            (SONIC_MAINNET, "SONIC_MAINNET"),
            (CELO_MAINNET, "CELO_MAINNET"),
            (CELO_ALFAJORES, "CELO_ALFAJORES"),
        ];
        for (id, label) in all {
            assert!(
                name_for_chain_id(id).is_some(),
                "no human name for {label} (0x{id:08X}) — add it to name_for_chain_id"
            );
        }
        // Sanity: an unknown id returns None.
        assert!(name_for_chain_id(0xE000_FFFF).is_none());
    }

    /// Pin `is_evm_family` excludes Solana (different crypto) and
    /// includes Tron (EVM-flavored).
    #[test]
    fn evm_family_classification() {
        assert!(is_evm_family(ETH_MAINNET));
        assert!(is_evm_family(BSC_MAINNET));
        assert!(is_evm_family(POLYGON_MAINNET));
        assert!(is_evm_family(TRON_MAINNET),
            "Tron is EVM-flavored secp256k1; must classify as EVM-family");
        assert!(!is_evm_family(SOLANA_MAINNET),
            "Solana is ed25519; the eth watcher stack doesn't apply");
        assert!(!is_evm_family(0xE000_FFFF),
            "unknown id rejects (operator must add it explicitly)");
    }

    /// Pin the slot allocation: each chain family lives in a 16-slot
    /// bank. A new constant outside its bank would be a copy-paste
    /// error.
    #[test]
    fn family_banks_align_to_16_slots() {
        // (start, end, chain_ids)
        let banks: &[(u32, u32, &[u32])] = &[
            (0xE000_0000, 0xE000_000F, &[ETH_MAINNET, ETH_SEPOLIA, ETH_HOLESKY]),
            (0xE000_0010, 0xE000_001F, &[TRON_MAINNET, TRON_NILE_TESTNET, TRON_SHASTA_TESTNET]),
            (0xE000_0020, 0xE000_002F, &[SOLANA_MAINNET, SOLANA_DEVNET, SOLANA_TESTNET]),
            (0xE000_0030, 0xE000_003F, &[BSC_MAINNET, BSC_TESTNET]),
            (0xE000_0040, 0xE000_004F, &[POLYGON_MAINNET, POLYGON_AMOY_TESTNET, POLYGON_ZKEVM, POLYGON_ZKEVM_CARDONA]),
            (0xE000_0050, 0xE000_005F, &[ARBITRUM_ONE, ARBITRUM_SEPOLIA, ARBITRUM_NOVA]),
            (0xE000_0060, 0xE000_006F, &[OPTIMISM_MAINNET, OPTIMISM_SEPOLIA]),
            (0xE000_0070, 0xE000_007F, &[BASE_MAINNET, BASE_SEPOLIA]),
            (0xE000_0080, 0xE000_008F, &[AVALANCHE_C_MAINNET, AVALANCHE_FUJI]),
            (0xE000_0090, 0xE000_009F, &[LINEA_MAINNET, LINEA_SEPOLIA]),
            (0xE000_00A0, 0xE000_00AF, &[ZKSYNC_ERA_MAINNET, ZKSYNC_SEPOLIA]),
            (0xE000_00B0, 0xE000_00BF, &[SCROLL_MAINNET, SCROLL_SEPOLIA]),
            (0xE000_00C0, 0xE000_00CF, &[MANTLE_MAINNET, MANTLE_SEPOLIA]),
            (0xE000_00D0, 0xE000_00DF, &[FANTOM_OPERA, SONIC_MAINNET]),
            (0xE000_00E0, 0xE000_00EF, &[CELO_MAINNET, CELO_ALFAJORES]),
        ];
        for (start, end, ids) in banks {
            for id in *ids {
                assert!(
                    *id >= *start && *id <= *end,
                    "chain id 0x{:08X} outside its family bank [0x{:08X}, 0x{:08X}]",
                    id, start, end
                );
            }
        }
    }
}
