# Tron-side bridge contracts

Tron's TVM is EVM-flavored Solidity, so this directory holds **deploy
instructions** rather than separate contract source — operators deploy
the same `NeoExternalBridgeRouter.sol` from `../eth/src/` with a
Tron-specific `externalChainId` constructor argument.

## Why no separate contract source

`external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol` parameterizes
its `externalChainId` via the constructor. Eth deployments pass
`0xE0000001` (mainnet) or `0xE0000002` (Sepolia); Tron deployments pass
`0xE0000010` (mainnet) / `0xE0000011` (Nile testnet) / `0xE0000012`
(Shasta testnet). The contract namespace check `(externalChainId &
0xFF000000) == 0xE0000000` accepts both — same code, different config.

The 13 Foundry tests in `../eth/test/NeoExternalBridgeRouter.t.sol`
cover the EVM semantics; TVM's Solidity execution is byte-identical for
the opcodes this contract uses (`ecrecover`, `sha256`, `keccak256`,
storage, calls, events).

## Deploy on Tron Nile testnet (recommended for first-time wiring)

```bash
# Prerequisites:
#   - tronbox or trontool installed
#   - A funded Nile account with some test TRX
#   - JustLend / TronLink for committee key custody

# Compile via foundry — same command as Eth:
cd external/foreign-contracts/eth
forge build

# Deploy on Nile via tronbox (operator's preferred deploy tool):
#   externalChainId = 0xE0000011 (Nile)
#   owner           = the operator's TRON_OWNER_ADDR (Tron-format,
#                     starts with T...)
#
# Construct the deploy bytecode yourself — Foundry doesn't speak TRON,
# but the compiled artifact at out/NeoExternalBridgeRouter.sol/NeoExternalBridgeRouter.json
# has the bytecode + ABI tronbox needs.

tronbox migrate --network nile --reset
# (your migration script reads the foundry artifact + calls the deploy)
```

## Mainnet-ready deploy

```bash
# externalChainId = 0xE0000010 for Tron mainnet.
# Same constructor signature; operator's wallet of choice signs.

# Verify in tronscan after deploy that:
#   externalChainId (read function) returns 0xE0000010
#   owner is the multisig hash you intended
#   committee is empty (no setCommittee call yet — that's step 2)

# Step 2: register the committee.
# Use neo-external-bridge committee-blob to build the address list.
tronbox exec scripts/setCommittee.js --network mainnet
```

## Watcher wiring

The Tron watcher at `watchers/neo-bridge-watcher-tron/` re-exports the
Eth watcher's full machinery — same secp256k1+SHA256 signing, same
canonical wire format, same `WatcherCore` orchestrator. The only
operational differences:

- Subscribe to `Locked` events on the Tron router via TronGrid
  (HTTPS API) or a self-hosted Tron full node.
- Pay TRX (not ETH) for tx submission.
- Tron address format (`T...`, 21-byte) vs Eth (`0x...`, 20-byte) —
  the watcher's `LockedEvent.sender` field stores the last 20 bytes
  for cross-chain consistency. Operators converting addresses for
  display use Tron's `base58check` encoding off the same 20 bytes.

## Tron-specific risk notes

- **Energy/Bandwidth pricing**: Tron's gas model is two-resource
  (Energy for compute, Bandwidth for tx size). Budget per
  `finalizeWithdrawal` is roughly equivalent to a single ECDSA
  verification + a small SPL-equivalent transfer — usually under
  100k Energy. Operators should pre-stake enough TRX (frozen) for
  Energy on the watcher's submission account to avoid runtime fee
  spikes.
- **Solidity assembly differences**: TVM doesn't support all post-
  Constantinople opcodes (`CHAINID` works; `BASEFEE` doesn't). The
  router doesn't use either — but a future version that does should
  guard with `chainid()` checks the way `chain` deploy scripts do.
- **`block.timestamp` granularity**: Tron blocks are 3s; Eth is 12s.
  The contract's `deadline` check against `block.timestamp` is
  identical semantically but resolves at different rates. Operators
  setting deadlines should use absolute Unix timestamps with a few
  blocks of safety margin.
