# Solana-side bridge program

Anchor program (Solana / Rust) for the Neo Elastic Network's Solana ↔
Neo cross-foreign-chain bridge. Mirrors `external/foreign-contracts/eth/`
semantically — locks SOL bound for Neo, finalizes Neo → Solana
withdrawals via committee-attested proofs — but uses ed25519 signatures
(Solana's native curve) instead of secp256k1.

See `docs/external-bridge-roadmap.md` § Phase 4 + `doc.md` §11.3.4 for
why Solana stays MPC-committee-only (Tower BFT light-client verification
is too expensive on-chain).

## Files

```
Anchor.toml                                         # Anchor workspace config
Cargo.toml                                          # Rust workspace root
programs/neo-external-bridge-router/
    Cargo.toml                                      # program manifest
    src/lib.rs                                      # the program
README.md                                            # this file
```

## Build status

**Source-only in this iteration.** The Anchor + Solana toolchains are
heavy, and a Solana validator runtime is required for `anchor test`.
Operators run:

```bash
# Prerequisites (one-time):
sh -c "$(curl -sSfL https://release.solana.com/stable/install)"   # Solana CLI
cargo install --git https://github.com/coral-xyz/anchor anchor-cli  # Anchor

# Build + test:
cd external/foreign-contracts/sol
anchor build                                        # produces target/deploy/*.so
anchor test                                         # spins up solana-test-validator
```

The source has been written carefully against Anchor 1.0 + the current Solana SDK
1.18 conventions but should be reviewed by a Solana developer before
mainnet deploy. Specifically:

- The `verify_sigverify_ix_matches` parser checks that the ed25519
  sigverify instruction verified the same in-instruction signature,
  pubkey, and message tuple that the bridge inspects. Cross-instruction
  offset references are rejected so a precompile cannot validate one
  message while the bridge compares another.
- The recipient address mapping from the canonical 20-byte format to a
  full Solana 32-byte `Pubkey` zero-pads the upper 12 bytes. Operators
  bridging to a Solana account whose pubkey doesn't fit this 20-byte
  prefix must use the full-32-byte recipient extension landing in v1.
- The `consumed_nonce` PDA seed uses raw little-endian bytes; Anchor
  tooling generates these automatically but the seed-key generation
  helpers `read_u32_le_for_seeds` / `read_u64_le_for_seeds` should be
  audited for correctness when the Vec body could be shorter than the
  required seed offsets.

## How it differs from the Eth router

| Aspect | Eth router (Solidity) | Solana router (Anchor) |
|---|---|---|
| State storage | Contract storage (key-value) | PDAs (Program Derived Addresses) |
| Replay protection | `mapping(uint => mapping(uint64 => bool))` | One PDA per `(chain_id, nonce)`, `init` constraint |
| Signature scheme | secp256k1 + Keccak256 (`ecrecover`) | ed25519 (sigverify precompile) |
| Sig verification | In-EVM (`ecrecover`) | Out-of-program (precompile) — bridge ix walks `Sysvar<Instructions>` to confirm precompile ran |
| Wire format | Same canonical `ExternalCrossChainMessage` (102B prefix + payload) | Same |
| Native asset | ETH (msg.value) | SOL (lamports via `system_program::transfer`) |

## Wire format reference

`messageBytes` passed to `finalize_withdrawal` is the canonical
`ExternalCrossChainMessage` pre-image — same 102-byte fixed prefix +
payload layout the Neo `ExternalMessageHasher`, the Eth Solidity
router, and the Rust watcher core all consume. See:

- `doc.md` §11.3.3 (Chinese, authoritative)
- `docs/external-bridge-roadmap.md` § "Canonical wire format"
- `src/Neo.L2.Messaging/ExternalMessageHasher.cs` (C# reference encoder)

## Watcher integration

The Solana-flavored off-chain watcher lives at
`watchers/neo-bridge-watcher-sol/`. It:

1. Subscribes to `Locked` events emitted by this program (via
   `solana-client`'s `program_subscribe` WebSocket — deferred to a
   future iteration).
2. Builds the canonical `ExternalCrossChainMessage` from the event
   fields.
3. Signs with the watcher's ed25519 private key
   (`Ed25519FileSigner` for dev, HSM/KMS for production).
4. Submits to Neo's `NeoHub.ExternalBridgeEscrow.Receive` with the
   ed25519-flavored `MpcCommitteePayload` (32B pubkeys + 64B sigs).

The on-chain `MpcCommitteeVerifier` reads the registered committee's
`curveTag = 2` and dispatches to `CryptoLib.VerifyWithEd25519`, mirroring
this program's expectations.

## Deploy on Solana devnet

```bash
solana-keygen new --outfile ~/.config/solana/deployer.json
solana config set --url https://api.devnet.solana.com
solana airdrop 5 -k ~/.config/solana/deployer.json
anchor build
anchor deploy --provider.cluster devnet
# Initialize the bridge_state PDA with your committee:
anchor run init -- --threshold 3 --committee <ed25519 pubkeys csv>
```

## What's not in v0

- **SPL token bridging** (`lock_spl_and_send` + corresponding withdrawal
  path). v0 is SOL-only; SPL support adds an SPL `Transfer` CPI to the
  vault PDA's associated token account. ~50 additional lines.
- **Call dispatch** (`MSG_TYPE_CALL` / `MSG_TYPE_ASSET_AND_CALL`).
  Receives revert with `MessageTypeUnsupported`.
- **Bond integration** (Solana-side equivalent of `ExternalBridgeBond`).
  v0 trusts the owner to register an honest committee; equivocation
  proofs settle on the Neo side, not here.
- **Light-client mode** (Phase D in the roadmap). Solana stays
  MPC-committee-only.
