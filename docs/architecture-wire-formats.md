# Architecture: Wire formats

> Byte-by-byte canonical wire formats that cross trust boundaries
> in the Neo Elastic Network. Operators debugging cross-tier issues,
> auditors verifying signatures, and SDK implementers building wire
> compatibility all need this reference.
>
> Companion to [`architecture-l2-lifecycle.md`](./architecture-l2-lifecycle.md)
> (which covers the *flow*; this doc covers the *bytes*).

## Table of contents

1. [Why canonical wire formats?](#1-why-canonical-wire-formats)
2. [`L2BatchCommitment` — sealed batch (321 + N bytes)](#2-l2batchcommitment--sealed-batch-321--n-bytes)
3. [`PublicInputs` — proof public-input commitment (332 bytes)](#3-publicinputs--proof-public-input-commitment-332-bytes)
4. [`L2ChainConfig` — registry-stored chain config (91 bytes)](#4-l2chainconfig--registry-stored-chain-config-91-bytes)
5. [`ExternalCrossChainMessage` — external bridge (102 + N bytes)](#5-externalcrosschainmessage--external-bridge-102--n-bytes)
6. [`DepositPayload` — L1→L2 bridge (44 + amountLen bytes)](#6-depositpayload--l1l2-bridge-44--amountlen-bytes)
7. [Common conventions](#7-common-conventions)

---

## 1. Why canonical wire formats?

Every wire format below is "canonical": there is exactly one
byte-for-byte encoding for any given value. Two consequences
follow that operators rely on:

- **Hash determinism.** Cross-tier signatures are over the canonical
  bytes (e.g., the watcher's secp256k1 signature is over
  `sha256(canonical_message_bytes)`). Both endpoints recompute the
  hash from the wire bytes; the contract never trusts an off-wire
  hash. A different encoding on either side breaks all signatures.

- **Wire-shape stability.** A tx that lands on L1 today will still
  parse correctly years later. There's no JSON ambiguity (key
  ordering, whitespace, unicode normalization), no protobuf
  tag-number drift; the bytes are positional + fixed-width or
  length-prefixed.

All multi-byte integers are **little-endian** (matches Neo's
on-chain convention). `UInt160` and `UInt256` types are encoded
as their raw 20- and 32-byte payloads; no length prefix —
they're fixed-width.

---

## 2. `L2BatchCommitment` — sealed batch (321 + N bytes)

The wire format that NeoHub's `SettlementManager` reads. Every
sealed L2 batch is encoded into this layout before submission.

Source: [`src/Neo.L2.Batch/BatchSerializer.cs`](../src/Neo.L2.Batch/BatchSerializer.cs).

<p align="center">
  <img src="figures/architecture/byte-layout-l2batchcommitment.svg" alt="L2BatchCommitment byte layout: 16 fields totaling 321 bytes fixed prefix plus N bytes variable proof. Color-grouped by concern: identity (chainId, batchNumber, firstBlock, lastBlock — blue), state roots (preStateRoot, postStateRoot — green), Merkle roots (txRoot, receiptRoot, withdrawalRoot, l2ToL1MessageRoot, l2ToL2MessageRoot — orange), DA + proof binding (daCommitment, publicInputHash — purple), proof header (proofType, proofLen — red), variable proof suffix (red dashed)" width="900">
</p>

**Defensive limits enforced at serialize:**
- `proof.Length ≤ 1,048,576` (1 MiB) — matches NeoHub's defensive
  limit. Encode rejects oversized proofs rather than landing a
  serialized blob the round-trip would later fail on.
- `proofType` must be in the defined enum range; out-of-range
  values rejected at encode (you can't sneak in a future proof
  type via the wire).

---

## 3. `PublicInputs` — proof public-input commitment (332 bytes)

The bytes the prover commits to as the proof's public input.
The L1 verifier recomputes this hash from the on-chain commitment
fields and matches against the proof's claimed public-input.
Mismatch → reject.

Source: same file, `PublicInputs` section.

```text
   byte
  offset  size  field
  ──────  ────  ─────
       0    4   chainId
       4    8   batchNumber
      12   32   preStateRoot
      44   32   postStateRoot
      76   32   txRoot
     108   32   receiptRoot
     140   32   withdrawalRoot
     172   32   l2ToL1MessageRoot
     204   32   l2ToL2MessageRoot
     236   32   l1MessageHash       ← extra vs. L2BatchCommitment
     268   32   daCommitment
     300   32   blockContextHash    ← extra vs. L2BatchCommitment
  ──────
     332   total (no variable suffix)
```

**Why two distinct layouts?**
`L2BatchCommitment` carries `firstBlock` + `lastBlock` (block
range) in its public-facing fields; `PublicInputs` carries
`l1MessageHash` + `blockContextHash` instead. The block range is
metadata for L1 consumers; the message+context hashes are what
the prover actually commits to. Both share the same chainId +
batchNumber + 7 of the same root hashes; the publicInputHash on
L1 ties them together.

The 12 fields × 32-byte hashes (mostly) keep this format simple
to verify byte-by-byte even from a non-Rust/non-C# implementation
(useful for indexers, block explorers, alerting tools).

---

## 4. `L2ChainConfig` — registry-stored chain config (91 bytes)

The bytes that `NeoHub.ChainRegistry` stores per registered chain.
`neo-stack register-chain` emits this hex; the operator's wallet
passes it as the `configBytes` argument to
`ChainRegistry.RegisterChain`.

Source: [`src/Neo.L2.Abstractions/Models/L2ChainConfigSerializer.cs`](../src/Neo.L2.Abstractions/Models/L2ChainConfigSerializer.cs).

```text
   byte
  offset  size  field                    notes
  ──────  ────  ─────                    ─────
       0    4   chainId                  uint32 LE
       4   20   operatorManager          UInt160 — multisig managing this chain
      24   20   verifier                 UInt160 — VerifierRegistry slot
      44   20   bridgeAdapter            UInt160 — L2NativeBridgeContract hash
      64   20   messageAdapter           UInt160 — L2MessageContract hash
                ── 84 bytes of identifiers above ──
      84    1   securityLevel            byte 0..3
      85    1   daMode                   byte 0=InMemory · 1=External · 2=L1 · 3=DAC
      86    1   gatewayEnabled           byte 0=false · 1=true
      87    1   permissionlessExit       byte 0=false · 1=true
      88    1   sequencerModel           byte 0=Solo · 1=Committee · 2=Permissionless
      89    1   exitModel                byte 0=Optimistic · 1=Permissionless · 2=ZkValidity
      90    1   active                   byte 0=disabled · 1=active
  ──────
      91   total (fixed-size; no varbytes)

  Total = 4 + 20×4 + 7 = 91. The 5 §16.2 dimensions span the last 7 bytes:

  ┌────────────────────────────────────────────────────┐
  │  identifiers (84 bytes)                            │
  │  ┌────┬─────────┬─────────┬─────────┬─────────┐    │
  │  │ ID │ operMgr │ verifier│  bridge │ message │    │
  │  └────┴─────────┴─────────┴─────────┴─────────┘    │
  └────────────────────────────────────────────────────┘
  ┌────────────────────────────────────────────────────┐
  │  §16.2 dimensions (7 bytes, 1 byte each)           │
  │  ┌─┬─┬─┬─┬─┬─┬─┐                                   │
  │  │S│D│G│P│Q│X│A│  S=securityLevel · D=daMode       │
  │  └─┴─┴─┴─┴─┴─┴─┘  G=gatewayEnabled · P=permExit    │
  │                   Q=sequencerModel · X=exitModel   │
  │                   A=active                         │
  └────────────────────────────────────────────────────┘
```

**Why fixed-size?** Storage on L1 charges per byte. Fixed-size
keeps the cost predictable — no operator can pad their chain
config to inflate the registry. Configs that "need" extra fields
(arbitrary metadata) belong in operator-supplied off-chain
manifests, not in the L1 registry entry.

**Templates** (from [`launching-an-l2.md`](./launching-an-l2.md))
populate the last 7 bytes:

| Template      | sec | da | gw | pex | seq | exit | act |
|---------------|-----|----|----|-----|-----|------|-----|
| `rollup`      | 2   | 2  | 1  | 0   | 1   | 0    | 1   |
| `zk-rollup`   | 3   | 2  | 1  | 0   | 1   | 2    | 1   |
| `validium`    | 2   | 3  | 1  | 0   | 1   | 0    | 1   |
| `sidechain`   | 1   | 0  | 0  | 1   | 0   | 1    | 1   |

---

## 5. `ExternalCrossChainMessage` — external bridge (102 + N bytes)

The wire format the off-chain watcher signs and the on-chain
`ExternalBridgeEscrow` verifies. Foreign chain (Eth/EVM family /
Tron / Solana) → Neo direction primarily; the same shape works
in the reverse direction with `direction` flipped.

Source: [`src/Neo.L2.Messaging/ExternalMessageHasher.cs`](../src/Neo.L2.Messaging/ExternalMessageHasher.cs).

```text
   byte
  offset  size  field                 notes
  ──────  ────  ─────                 ─────
       0    4   externalChainId       uint32 LE — must be 0xE0_xx_xx_xx (foreign namespace)
       4    4   neoChainId            uint32 LE — Neo L2 chain receiving (or sending) the message
       8    8   nonce                 uint64 LE — replay-protected per (externalChainId, nonce)
      16    1   direction             byte 1=NeoToForeign · 2=ForeignToNeo
      17   20   sender                20 bytes — last 20B of the source-side address
      37   20   recipient             20 bytes — last 20B of the destination-side address
      57    8   deadlineUnixSeconds   uint64 LE — 0 means no deadline
      65   32   sourceTxRef           32 bytes — source-side tx hash (or 0 if not yet on-chain)
      97    1   messageType           byte 0=AssetTransfer · 1=Call · 2=AssetAndCall
      98    4   payloadLen            int32 LE — length of the payload bytes
     102    N   payload               N = payloadLen bytes — type-specific encoding
  ──────
   102+N   total

  ┌─────────────────────────────────────────────────────────────┐
  │  fixed prefix (102 bytes)                                   │
  │  ┌────┬────┬────┬─┬───────┬────────┬────┬─────────┬─┬────┐  │
  │  │ext │neo │nonc│d│ sender│recipien│dlin│sourceTxR│m│pLen│  │
  │  └────┴────┴────┴─┴───────┴────────┴────┴─────────┴─┴────┘  │
  └─────────────────────────────────────────────────────────────┘
              followed by:
  ┌─────────────────────────────────────────────────────────────┐
  │  variable payload (payloadLen bytes)                        │
  │                                                             │
  │  AssetTransfer (messageType=0):                             │
  │    20  foreignAsset   |  4  amountLen  |  N  amount LE      │
  │                                                             │
  │  Call (messageType=1):     [reserved — v0 contract rejects] │
  │  AssetAndCall (=2):        [reserved]                       │
  └─────────────────────────────────────────────────────────────┘
```

**Replay protection** is per `(externalChainId, nonce)`. The
on-chain escrow keeps a `consumedInbound[chainId][nonce]` set;
once a `(chainId, nonce)` tuple lands successfully, any future
attempt to submit the same tuple reverts.

**Foreign-namespace prefix.** `externalChainId` MUST satisfy
`(externalChainId & 0xFF000000) == 0xE0000000`. Both the on-chain
verifier and the off-chain encoder enforce this. The 16-slot
family-bank allocation in
[`watchers/neo-bridge-watcher-eth/src/chains.rs`](../watchers/neo-bridge-watcher-eth/src/chains.rs)
covers Ethereum / BSC / Polygon / Arbitrum / Optimism / Base /
Avalanche / Linea / zkSync / Scroll / Mantle / Fantom / Celo /
Tron — see [`external-bridge-evm-chains.md`](./external-bridge-evm-chains.md)
for the full table.

**Cryptographic boundary.** The watcher signs
`sha256(canonical_bytes)` with secp256k1+SHA256 (Eth/Tron/EVM family)
or ed25519 (Solana). Neo's `CryptoLib.VerifyWithECDsa(secp256k1SHA256)`
hashes the same bytes internally + verifies; Eth's `ecrecover`
takes the digest directly. Same bytes → same digest on both sides.

---

## 6. `DepositPayload` — L1→L2 bridge (44 + amountLen bytes)

Embedded in the `payload` field of the `CrossChainMessage` that
travels from `NeoHub.SharedBridge` to `L2NativeBridgeContract`.

Source: [`src/Neo.L2.Bridge/DepositPayload.cs`](../src/Neo.L2.Bridge/DepositPayload.cs).

```text
   byte
  offset  size       field          notes
  ──────  ────       ─────          ─────
       0   20        l1Asset        UInt160 — L1 asset hash locked at SharedBridge
      20   20        l2Recipient    UInt160 — L2 address that gets the wrapped asset
      40    4        amountLen      int32 LE — length of the amount bytes
      44    N        amount         N = amountLen bytes (BigInteger LE, isUnsigned: true)
  ──────
    44+N   total
```

**Why a varbytes amount?** Token amounts have wildly different
ranges — micro-cents for stablecoins, billions for some governance
tokens. Encoding amount as a varbytes BigInteger (LE, unsigned)
avoids a fixed cap that would either (a) waste storage on small
amounts or (b) underflow on whales. Cap is `amountLen ≤ 64 bytes`
(supports tokens with up to ~10^154 minor units; way past any
realistic supply).

---

## 7. Common conventions

| Convention                          | Why                                                                                                |
|-------------------------------------|----------------------------------------------------------------------------------------------------|
| Little-endian for multi-byte ints   | Matches Neo's on-chain convention (`BinaryPrimitives.WriteUInt32LittleEndian` etc.)                |
| Fixed-width for `UInt160` / `UInt256` | Their byte layout is part of the type; no varbytes wrapping                                      |
| Length-prefix for variable bytes    | `int32 LE length` + the bytes. Decoders can preallocate; operators can scan for boundaries        |
| 32-byte fields for hashes           | All hashes / roots / commitments. SHA256 / Keccak256 / RIPEMD160 outputs are zero-padded to 32B   |
| Defensive caps at encode + decode   | E.g. proof ≤ 1 MiB, payload ≤ chain-config policy. Prevents the wire from carrying unverifiable size |
| Validate-then-decode                | Decoders length-check first, then parse. A truncated record never reaches business logic         |
| All wire formats are byte-canonical | One bytes-encoding per logical value. No JSON / no protobuf / no ambiguity                        |

### Where the implementations live

| Wire format               | Source                                                                |
|---------------------------|-----------------------------------------------------------------------|
| `L2BatchCommitment`       | `src/Neo.L2.Batch/BatchSerializer.cs`                                 |
| `PublicInputs`            | `src/Neo.L2.Batch/BatchSerializer.cs` (same file, separate function)  |
| `L2ChainConfig`           | `src/Neo.L2.Abstractions/Models/L2ChainConfigSerializer.cs`           |
| `ExternalCrossChainMessage` | `src/Neo.L2.Messaging/ExternalMessageHasher.cs` + `ExternalMessageBuilder.cs` |
| `DepositPayload`          | `src/Neo.L2.Bridge/DepositPayload.cs`                                 |
| `CrossChainMessage`       | `src/Neo.L2.Messaging/MessageBuilder.cs` + `MessageHasher`            |
| `WithdrawalRecord`        | `src/Neo.L2.Bridge/WithdrawalRecord.cs`                               |
| `MerkleProofSerializer`   | `src/Neo.L2.State/MerkleProofSerializer.cs`                           |
| `MultisigProofPayload`    | `src/Neo.L2.Proving/MultisigProofPayload.cs`                          |
| `RiscVProofPayload`       | `src/Neo.L2.Proving/RiscVProofPayload.cs`                             |
| `OptimisticProofPayload`  | `src/Neo.L2.Challenge/OptimisticProofPayload.cs`                      |
| `FraudProofPayload`       | `src/Neo.L2.Challenge/FraudProofPayload.cs`                           |

Each has matching parity tests:
- `.NET unit tests` in `tests/Neo.L2.*.UnitTests/` pin the byte-layout invariants.
- For the watcher: `watchers/neo-bridge-watcher-eth/tests/parity.rs` pins
  Rust ↔ C# byte-for-byte equivalence (the same canonical bytes hash to
  the same digest in both languages).

---

## See also

- [`architecture-l2-lifecycle.md`](./architecture-l2-lifecycle.md) — how
  L2 chains are created/deployed/connected (the *flow*; this doc covers
  the *bytes*).
- [`architecture-walkthrough.md`](./architecture-walkthrough.md) — narrative
  tour with the per-transaction lifecycle.
- [`external-bridge-evm-chains.md`](./external-bridge-evm-chains.md) —
  the 16-slot foreign-namespace allocation + onboarding runbook for
  `ExternalCrossChainMessage`.
- [`security-model.md`](./security-model.md) — threat model;
  cryptographic-boundary discussion is in §3.
- [`doc.md`](../doc.md) — master spec (authoritative).
