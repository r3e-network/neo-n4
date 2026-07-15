# External Bridge Roadmap

## Goal

Add a pluggable bridge to foreign chains (Ethereum, Tron, Solana) that's
separate from `NeoHub.SharedBridge` (which serves Neo L1 ↔ Neo L2). One
application API; the verifier underneath swaps from MPC → Optimistic → ZK
without breaking it. Same operational pattern the framework already uses
for L2 settlement (`NeoHub.VerifierRegistry` lets a chain move from
Multisig → Optimistic → ZK without app rebuilds) — this is the same
primitive applied to cross-foreign-chain messaging.

## 1. Architecture

### 1.1 Contract surface

Three new contracts plus an interface:

- **`NeoHub.ExternalBridgeRegistry`** — maps `externalChainId` (uint32,
  see §1.3) → `IExternalBridgeVerifier` UInt160. Same shape as
  `NeoHub.VerifierRegistry`: owner-set + governance-proposal path with
  replay protection. Adds an `externalChainId → bridgeKind` byte
  (`1=MPC`, `2=Optimistic`, `3=ZK`) so dApps can read what trust model
  they're actually riding.
- **`NeoHub.ExternalBridgeEscrow`** — holds locked NEP-17 assets bound
  for foreign chains and atomically pays out or dispatches a versioned
  credit/mint adapter after a verified inbound proof. Routes bind both
  chain domains, nonce, deadline, foreign/Neo asset mapping, recipient,
  and amount; verification is delegated to the registry rather than
  `SettlementManager`.
- **`L2NativeExternalBridgeContract`** — Neo core native L2 counterpart so an L2
  dApp can `Send(externalChainId, recipient, asset, amount, calldata)`
  directly. Mirrors `L2BridgeContract` but its withdrawals are
  emitted to a separate "external withdrawal root" that's committed
  alongside the existing batch roots.

### 1.2 The verifier seam

```csharp
interface IExternalBridgeVerifier {
  bool VerifyInboundMessage(uint externalChainId, byte[] messageBytes, byte[] proofBytes);
  byte BridgeKind();   // 1=MPC, 2=Optimistic, 3=ZK
}
```

`ExternalBridgeRegistry.VerifyInbound(externalChainId, msg, proof)` reads
the wired verifier hash and `Contract.Call`s `VerifyInboundMessage`. App
code never sees which kind. Phase 2 adds Optimistic by deploying a new
verifier and bumping the registry pointer through governance. Phase 3
ditto for ZK. Same wire format, same registry, no app rebuild — exactly
how `NeoHub.VerifierRegistry` already moves a settlement chain from
Multisig → Zk.

### 1.3 Canonical wire format

`ExternalCrossChainMessage` (mirrors `Neo.L2.Messaging.CrossChainMessage`
+ `MessageHasher`):

```
4B  externalChainId       (Eth mainnet=0xE0_00_00_01, Sepolia=0xE0_00_00_02,
                           Tron=0xE0_00_00_10, Solana=0xE0_00_00_20 — high-bit
                           prefix 0xE0 reserves a namespace disjoint from Neo
                           L2 chainIds, which start at 1)
4B  neoChainId            (target Neo domain; 0 = L1, non-zero = L2 chainId)
8B  nonce                 (per-direction, per-pair; replay key)
1B  direction             (1 = Neo→Foreign, 2 = Foreign→Neo)
20B sender                (UInt160 on Neo side; on foreign side, last 20B
                           of address — natural for Eth/Tron, packed for SOL)
20B recipient
8B  deadlineUnixSeconds   (0 = none)
32B sourceTxRef           (Eth tx hash / Tron tx hash / Solana sig truncated)
1B  messageType           (0=AssetTransfer, 1=Call, 2=AssetAndCall)
4B  payloadLength         (little-endian)
N   payload
```

The canonical wire value is exactly `102 + N` bytes. `MessageHash` is derived
off-wire metadata populated by `ExternalMessageBuilder` as Neo `Hash256`
(double-SHA256) over these bytes; it is never serialized as a caller-supplied
trailing field. The escrow validates the signed domains, direction, deadline,
payload length, and asset payload before dispatch.

Asset payloads reuse one canonical encoding:

```
20B foreignAsset || 4B amountLength || 1..32B minimal unsigned LE amount
```

`AssetAndCall` appends adapter calldata after that prefix. Zero/non-minimal/
oversized amounts and zero foreign assets are rejected identically by C#,
Rust, and the on-chain contract.

### 1.4 Production payout boundary

- A zero adapter is valid only for an escrow bound to the Neo L1 domain
  (`neoChainId == 0`). It performs direct NEP-17 release from the chain-specific
  funded escrow pool. `FundLiquidity` accepts only an existing, active direct
  route so operators cannot orphan tokens in an unreachable pool; insufficient
  liquidity or a false token return reverts replay consumption and accounting atomically.
- A non-zero adapter receives every signed routing/value field plus the original
  canonical bytes. It must report `payoutVersion() == 1` and be a fresh
  `UpdateCounter == 0` deployment. The route pins that counter, so an in-place
  code upgrade fails closed even if it still claims ABI v1.
- A non-zero L2 destination must use a non-zero adapter. The adapter may return
  `true` only after atomically persisting or enqueueing the target credit/mint
  instruction; final target-chain execution is asynchronous and is not implied
  by verifier acceptance or by an event alone.
- Foreign-to-Neo asset mappings are immutable and reverse-unique per external
  chain. Adapter rotation/deactivation remains possible, but production locks
  direct owner administration and requires exact payload-bound timelocked
  proposals with one-shot proposal ids. Action bytes include the escrow script
  hash and immutable Neo destination domain, preventing cross-instance or
  cross-domain proposal reuse.

## 2. Cryptography

Neo's `CryptoLib` already exposes `VerifyWithECDsa` for **secp256k1 +
SHA256** and `VerifyWithEd25519` as native syscalls. So:

- **Eth/Tron**: secp256k1+SHA256 committee verification on-chain is cheap and
  already there.
- **Solana**: ed25519 verification on-chain works. The pain isn't
  signature verification — it's the *Tower BFT light client*. Solana's
  validator set is ~1500, rotates every epoch, finality requires
  reasoning about lockouts. A fully trustless Solana light client is
  genuinely expensive (Helius, Pyth, and Wormhole all punted).
  Therefore: **Solana stays MPC-committee-only through Phase 3**; ZK
  Solana is a Phase 4 R&D item.
- **Eth ZK light client**: BLS12-381 sync-committee verification needs
  either pairing precompiles or a SNARK-of-SNARK design — Phase 3 uses
  Succinct's SP1 proofs (we already host SP1 via `bridge/neo-zkvm-host/`)
  verified by a generic Groth16/Plonky-style verifier rather than adding
  pairing syscalls. Reusing the existing zkVM seam is the win.

## 3. Off-chain components (`watchers/`)

New top-level `watchers/` directory; each watcher is a Rust binary.

- `watchers/neo-bridge-watcher-eth/` — ethers-rs RPC client. Subscribes
  to events on the Eth-side `NeoExternalBridgeRouter.sol` (deployed via
  `external/foreign-contracts/`). For each event, builds the canonical
  `ExternalCrossChainMessage`, signs with secp256k1+SHA256, posts to
  `NeoHub.MpcCommitteeVerifier`'s `Attest` endpoint on Neo. Mirrors
  `Neo.L2.Sequencer.RpcSequencerCommitteeProvider` patterns.
- `watchers/neo-bridge-watcher-tron/` — same shape using `tron-rust`.
- `watchers/neo-bridge-watcher-sol/` — same using `solana-client`.

**Bonding/slashing.** Each watcher is bonded via a new
`NeoHub.ExternalBridgeBond` (clone `NeoHub.SequencerBond` 1:1 with
`(externalChainId, operator)` indexing). Slash conditions:
1. Signing two distinct messages for the same `(externalChainId, nonce)`
   (equivocation, provable on-chain).
2. Failing to produce a quorum signature when the foreign chain has
   finalized the source tx for >`livenessTimeout` (proven by an
   alternate watcher submitting the foreign-chain inclusion proof).

The optimistic verifier in Phase C uses (2) directly as its fraud proof.

## 4. Phased plan

### Phase A — Foundation (4–6 weeks)

`Neo.L2.Messaging/ExternalMessageBuilder.cs`,
`ExternalMessageHasher.cs`, the three contracts,
`IExternalBridgeVerifier` interface, registry wiring, governance hooks.
No verifier yet — just the seam.

**Acceptance:** deploy + register a stub verifier that returns `true`;
round-trip a message through `Send` → registry → noop verifier → escrow
payout in devnet.

> **Operator note — production deployments MUST NOT register
> `NeoHub.ExternalBridgeStubVerifier`.** The stub returns `true` for any
> message and reports `bridgeKind() == 0` as a sentinel distinct from
> the production kinds (1 = MPC committee, 2 = Optimistic, 3 = ZK).
> The on-chain `ExternalBridgeRegistry.RegisterVerifier(externalChainId,
> verifier, bridgeKind)` rejects bridge kind 0. An operator deploying
> through governance SHOULD still script a check that
> `verifier.BridgeKind()` returns the intended production kind. The
> contract description string and XML remarks of
> `ExternalBridgeStubVerifier` declare "Devnet only" explicitly.

### Phase B — MPC committee + Eth (6–8 weeks)

`NeoHub.MpcCommitteeVerifier` (clone `AttestationVerifier` shape),
`NeoHub.ExternalBridgeBond`, `watchers/neo-bridge-watcher-eth`, an
Eth-side `NeoExternalBridgeRouter.sol` deployed via Hardhat scripts in
`external/foreign-contracts/`. Tron watcher follows the same pattern
(~2 weeks marginal). Solana watcher likewise (~3 weeks marginal — RPC
quirks).

**Acceptance:** a Sepolia user bridges 1 USDC to L2 chainId=1 in
<2 minutes; reverse direction works; equivocation slashing has a unit
test on devnet.

### Phase C — Optimistic challenge (4 weeks)

`NeoHub.ExternalOptimisticChallenge` cloning `NeoHub.OptimisticChallenge`
+ a `NeoHub.MpcCommitteeFraudVerifier` that verifies "the committee
signed message X but the foreign chain's tx ref doesn't match."
Registry pointer for ETH flips MPC → Optimistic; Solana stays MPC.

**Acceptance:** simulated equivocation gets slashed via the challenge
path; finalization happens after the window expires.

### Phase D — ZK light client per chain (12+ weeks each)

Eth first via Succinct/SP1 (`watchers/neo-bridge-prover-eth/` runs
sync-committee proofs in `bridge/neo-zkvm-host/`'s SP1 container, posts
to a `NeoHub.EthSyncCommitteeVerifier` that calls a Groth16 verifier
sitting in `Neo.L2.Proving/RiscVZk/`). Tron next (DPOS is simpler —
~6 weeks). Solana noted as committee-only.

**Acceptance:** Eth bridge runs trustlessly without committee
signatures; gas cost <5 GAS per inbound proof.

## 5. Repository layout

```
contracts/
  NeoHub.ExternalBridgeRegistry/          # Phase A
  NeoHub.ExternalBridgeEscrow/            # Phase A
  NeoHub.ExternalBridgeBond/              # Phase B
  NeoHub.MpcCommitteeVerifier/            # Phase B
  NeoHub.MpcCommitteeFraudVerifier/       # Phase C
  NeoHub.ExternalOptimisticChallenge/     # Phase C
  NeoHub.EthSyncCommitteeVerifier/        # Phase D
  external/neo/.../L2NativeContracts.cs   # Phase A L2 native bridge
src/
  Neo.L2.Messaging/ExternalMessageBuilder.cs        # Phase A
  Neo.L2.Messaging/ExternalMessageHasher.cs         # Phase A
  Neo.L2.Messaging/ExternalCrossChainMessage.cs     # Phase A
  Neo.L2.Bridge/ExternalDepositPayload.cs           # Phase A
  Neo.L2.Bridge/ExternalWithdrawalProcessor.cs      # Phase A
  Neo.L2.Proving/External/MpcCommitteePayload.cs    # Phase B
  Neo.L2.Proving/External/MpcCommitteeVerifier.cs   # Phase B
watchers/
  neo-bridge-watcher-eth/                 # Phase B (Rust)
  neo-bridge-watcher-tron/                # Phase B (Rust)
  neo-bridge-watcher-sol/                 # Phase B (Rust)
  neo-bridge-prover-eth/                  # Phase D (Rust, wraps bridge/neo-zkvm-host)
external/
  foreign-contracts/eth/NeoExternalBridgeRouter.sol    # Phase B
  foreign-contracts/tron/NeoExternalBridgeRouter.sol   # Phase B
  foreign-contracts/sol/                                # Phase B (Anchor)
```

## 6. User-facing API

**L2 dApp → foreign chain:**

```csharp
ExternalBridge.Send(
    externalChainId: 0xE000_0001,           // Eth mainnet
    recipient:       new Bytes20("0xabc..."),
    asset:           usdcL2Hash,            // or UInt160.Zero for native
    amount:          1_000_000,             // 1 USDC
    calldata:        Array.Empty<byte>(),   // or arbitrary bytes
    deadline:        Runtime.Time + 86400);
```

Returns a `(externalChainId, nonce)` the user can track. The app never
names a verifier kind.

**Foreign chain → Neo (Ethereum example):**

```solidity
NeoExternalBridgeRouter.lockAndSend(
    uint32 neoChainId,             // 1 = first L2
    bytes20 neoRecipient,
    address asset,                  // 0x0 = ETH
    uint256 amount,
    bytes calldata payload,
    uint64 deadline);
```

Watchers observe the `Locked` event and submit the attested canonical message.
After registry verification, `ExternalBridgeEscrow` either releases funded
NEP-17 on its hosting chain or invokes the pinned payout-v1 adapter that
atomically persists/enqueues the target credit instruction with L1 replay
consumption. Cross-network L2 execution is necessarily asynchronous: the final
application is performed by the durable relay through the system-account-gated
`L2NativeExternalBridgeContract.ApplyPayout` boundary. That endpoint revalidates
the exact canonical bytes/hash, mapped asset, and every payout field before storing
the one-time receipt and minting; the relay then acknowledges the L1 queue with the
L2 transaction hash. Verification or queueing alone never implies that target-L2
assets were already minted.

The seam guarantees these two surfaces stay byte-identical when the
registry flips MPC → Optimistic → ZK; only the verifier contract
underneath changes.

## 7. Reference implementations to follow

When implementing each phase, use these existing contracts as direct
shape references — same patterns, same error-handling, same testing
posture:

- `contracts/NeoHub.VerifierRegistry/` — for `ExternalBridgeRegistry`
  (governance-mediated verifier dispatch with replay protection).
- `contracts/NeoHub.SharedBridge/` — for `ExternalBridgeEscrow`
  (escrow + finalize-with-proof flow, just routed through the registry
  instead of SettlementManager).
- `contracts/NeoHub.OptimisticChallenge/` — for
  `ExternalOptimisticChallenge` (challenge window + bisection game,
  same shape as the existing fraud-proof system).
- `src/Neo.L2.Proving/Attestation/AttestationVerifier.cs` — for
  `MpcCommitteeVerifier` (M-of-N secp256k1+SHA256 over canonical bytes).
  Foreign-chain ops will replace the curve to secp256k1 / ed25519 as
  appropriate via Neo's CryptoLib syscalls.
- `src/Neo.L2.State/MessageHasher.cs` — for `ExternalMessageHasher.cs`
  (Hash256 over canonical wire-format bytes).
