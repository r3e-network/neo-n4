# Eth-side bridge contracts

Solidity counterpart to `contracts/NeoHub.ExternalBridgeEscrow` +
`NeoHub.MpcCommitteeVerifier`. Locks ETH / ERC-20 tokens bound for a
Neo chain, emits events watchers attest, and finalizes
Neo → Eth withdrawals when a quorum of registered committee members
has signed the message.

See `docs/external-bridge-roadmap.md` § "Phase B" for the bigger picture
and `doc.md` § 11.3 for the protocol spec.

## Files

```
src/NeoExternalBridgeRouter.sol      # the contract
test/NeoExternalBridgeRouter.t.sol   # 13 Foundry tests with real ecrecover
foundry.toml                          # solc 0.8.24, via_ir, optimizer-on
```

## Setup

```bash
# 1. Foundry (one-time):
curl -L https://foundry.paradigm.xyz | bash
foundryup

# 2. Clone forge-std into lib/ (gitignored — never tracked):
git clone --depth=1 https://github.com/foundry-rs/forge-std.git lib/forge-std

# 3. Build + test:
forge build
forge test -vv
```

13 tests cover: constructor namespace check, committee management,
ETH + ERC-20 lock paths, full withdrawal happy path with real
secp256k1 signatures, and every guard (replay, below-threshold,
duplicate signer, past-deadline, wrong-chainId).

## Deployment to Sepolia

```bash
# Make sure forge-std is on disk first.
git clone --depth=1 https://github.com/foundry-rs/forge-std.git lib/forge-std

# Deploy. externalChainId 0xE0000002 = Sepolia.
forge create src/NeoExternalBridgeRouter.sol:NeoExternalBridgeRouter \
    --rpc-url $SEPOLIA_RPC_URL \
    --private-key $DEPLOY_KEY \
    --broadcast \
    --constructor-args 0xE0000002 $OWNER_ADDRESS
```

After deployment, register the committee:

```solidity
NeoExternalBridgeRouter(routerAddr).setCommittee(
    [member0, member1, member2, member3, member4],   // 5 watchers
    3                                                 // threshold M
);
```

The committee addresses are Eth-style: `keccak256(secp256k1_pubkey)[12:]`.
The Neo side stores the same identity as the 33-byte compressed pubkey
in `NeoHub.MpcCommitteeVerifier.RegisterCommittee` — operators wire
both sides together with a `tools/Neo.External.Bridge.Cli` keygen +
deploy command (deferred to a future iteration).

## Wire format

Outbound user call (Eth → Neo):

```solidity
// ETH:
router.lockETHAndSend{value: 1 ether}(
    1099,                                  // neoChainId
    bytes20(0xabc...),                     // neoRecipient (last 20B)
    "",                                    // payload (calldata for the L2)
    0                                      // deadline (0 = none)
);

// ERC-20:
IERC20(usdc).approve(routerAddr, 1_000_000);
router.lockERC20AndSend(1099, neoRecipient, usdc, 1_000_000, "", 0);
```

Both emit a `Locked` event watchers subscribe to:

```solidity
event Locked(
    uint32 indexed externalChainId,
    uint32 indexed neoChainId,
    uint64 indexed nonce,
    address sender,
    bytes20 neoRecipient,
    address asset,
    uint256 amount,
    bytes payload,
    uint64 deadline
);
```

Inbound finalization (Neo → Eth) — called by anyone with a valid proof:

```solidity
router.finalizeWithdrawal(messageBytes, proofBytes);
```

`messageBytes` = the canonical 102-byte fixed prefix + payload bytes
the Neo `ExternalMessageHasher` hashes over (NOT including the trailing
32-byte messageHash field — the contract recomputes via
`sha256(messageBytes)`).

`proofBytes` layout:

```
[2B sigCount LE]
[sigCount × (1B signerIdx, 32B r, 32B s, 1B v)]
```

Indexed signers (1 byte) save gas vs. embedding the address each time,
and let the contract verify in O(N) without loading the full committee
array per signature.

## Security model

- **Curve**: secp256k1 + SHA256 over the canonical pre-image. Same
  bytes signed verify on both Neo and Eth (Neo's
  `CryptoLib.VerifyWithECDsa(secp256k1SHA256)` does sha256 internally;
  here we run `ecrecover(sha256(...), v, r, s)`).
- **Committee trust**: the M-of-N committee can collude. Phase C adds
  optimistic challenge + slashing on equivocation. Phase D replaces
  the verifier with a ZK light client. The router contract surface
  doesn't change across phases — only `setCommittee` becomes inert
  if the deployment moves to ZK verifier mode.
- **Replay**: per-(neoChainId, nonce). Inbound nonces are recorded in
  `consumedInbound`; outbound counts up via `outboundNonces`.
- **Asset accounting**: `lockedBalances[asset]` tracks how much was
  ever locked. `finalizeWithdrawal` rejects amounts > locked balance,
  so the router can't release more than was deposited even if a
  malicious committee signs an inflated withdrawal.
- **Reentrancy**: `nonReentrant` guards `lock*AndSend` and
  `finalizeWithdrawal`.
- **Bare transfers**: `receive()` reverts so users can't accidentally
  send ETH and lose it.

## What's NOT in v0

- `MSG_TYPE_CALL` and `MSG_TYPE_ASSET_AND_CALL` — the verifier accepts
  the proof but the dispatcher reverts. A future router can add a
  registry of permitted call targets without touching the committee
  state.
- Asset metadata mapping (Neo-side asset hash ↔ Eth ERC-20 address) —
  the watchers / off-chain operator config currently hold this. A
  future router can pull from a registry contract.
- Bonding integration — the Eth-side equivalent of
  `NeoHub.ExternalBridgeBond` lives in a future iteration. v0 trusts
  the owner to register honest committees.
