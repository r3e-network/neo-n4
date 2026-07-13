# SP1 Groth16/BN254 verifier contract

Status: implemented and version-pinned. The production contract is
`contracts/NeoHub.Sp1Groth16Verifier`.

## 1. Security boundary

`NeoHub.ContractZkVerifier` validates the canonical N4 batch commitment, the
`RiscVProofPayload` envelope, the proof-system tag, the registered program verification-key
identifier, and the 32-byte N4 public-input hash. For `ProofSystem.Sp1`, it then calls:

```text
Sp1Groth16Verifier.verifyZkProof(
  proofSystem,
  programVKey,
  publicInputHash,
  proofBytes)
```

The terminal verifier performs the full SP1 Groth16 pairing equation on Neo's native BN254
surface. Production deployment registers this contract and irreversibly calls
`ContractZkVerifier.DisableEnvelopeOnlyPermanently(ProofSystem.Sp1=1)`, then calls
`LockProofSystemConfiguration(ProofSystem.Sp1=1, programVKey)` to freeze one exact
program VK and terminal verifier before routing `ProofType.Zk` settlement to the router.
Devnet envelope-only mode is not part of the production plan.

This verifier is deliberately **SP1-specific**. It must not be reused for Risc0, Halo2,
Axiom, another SP1 circuit version, or a different wrapper verifying key. Each proof system
and wrapper version requires its own reviewed terminal verifier deployment.

## 2. Pinned SP1 format

The contract implements the SP1 v6.1-compatible Groth16 wrapper used by SP1 6.2.x. The
wrapper exposes five BN254 scalar public inputs, in order:

1. the raw 32-byte program verification-key digest (`vk.bytes32()`),
2. the masked SHA-256 digest of committed public values,
3. the guest exit code,
4. the SP1 recursion verification-key root,
5. the Groth16 nonce.

The N4 guest commits exactly 33 bytes:

```text
0x00 || publicInputHash[32]
```

The terminal verifier reconstructs those bytes, computes SHA-256, and clears the top three
bits of the digest's first byte exactly as SP1's BN254 public-value hashing does. It rejects
all five public inputs that are not canonical field elements; it never silently reduces an
attacker-controlled value modulo the scalar field.

The accepted proof is exactly 356 bytes:

| Offset | Size | Value |
|---:|---:|---|
| 0 | 4 | pinned Groth16 verifier selector `0x4388a21c` |
| 4 | 32 | zero exit code |
| 36 | 32 | pinned recursion VK root |
| 68 | 32 | nonce |
| 100 | 64 | Groth16 `A` in G1 |
| 164 | 128 | Groth16 `B` in G2 |
| 292 | 64 | Groth16 `C` in G1 |

The pinned recursion VK root is
`0x002f850ee998974d6cc00e50cd0814b098c05bfade466d28573240d057f25352`.

## 3. Pairing equation

The fixed SP1 wrapper verifying key contains `alpha`, `beta`, `gamma`, `delta`, and
`IC[0..5]`. For public inputs `a1..a5`, the verifier computes:

```text
linearCombination = IC0 + a1*IC1 + a2*IC2 + a3*IC3 + a4*IC4 + a5*IC5
```

and accepts only when:

```text
e(A, B) = e(alpha, beta) * e(linearCombination, gamma) * e(C, delta)
```

Neo's `CryptoLib.Bn254Deserialize` enforces canonical EIP-196/EIP-197 big-endian
coordinates, curve membership, and G2 subgroup membership. The contract uses
`Bn254Equal` for target-group equality and composes pairing results with `Bn254Add`, whose
GT operation is field multiplication.

## 4. Mutability and permissions

The contract has no owner, storage, upgrade hook, or runtime VK registration. Its constants
are part of the reviewed bytecode. Updating SP1's wrapper circuit therefore requires a new
contract artifact and an explicit governance route change rather than an in-place key swap.

Its manifest grants calls only to the native CryptoLib contract
`0x726cb6e0cd8628a1350a611384688911ab75f51b` and only to:

- `sha256`
- `bn254Deserialize`
- `bn254Add`
- `bn254Mul`
- `bn254Pairing`
- `bn254Equal`

No wildcard contract or method permission is present.

## 5. Cost and denial-of-service controls

Verification has fixed work: five G1 scalar multiplications, five G1 additions, four
pairings, two GT multiplications, point decoding, and one equality check. Input lengths are
checked before cryptographic work and there are no attacker-controlled loops. The N4 Core
execution test records the actual fee and enforces a 100 GAS upper bound; documentation and
release evidence should be updated if native BN254 pricing changes.

## 6. Verification evidence

`tests/NeoHub.Sp1Groth16Verifier.UnitTests` runs the compiled NEF directly through the
current vendored N4 `ApplicationEngine`, not the older public
`Neo.SmartContract.Testing` package. It pins the NEF SHA-256 and least-privilege manifest,
checks selector/root constants, rejects malformed envelopes, non-canonical scalars and
invalid points, and executes both the positive and invalid-pairing paths under the fee ceiling.

The committed release-quality positive vector contains all four byte-for-byte values produced
by the Rust SP1 prover:

- `proof.bytes()` (356 bytes),
- `proof.public_values` (33 bytes),
- raw `vk.bytes32()` bytes (32 bytes, without Neo textual hash reversal),
- the extracted N4 `publicInputHash` (bytes 1..32 of public values).

The positive proof verifies through both `Sp1Groth16Verifier` and `ContractZkVerifier`.
Changing the program VK, N4 public-input hash, selector, recursion root, nonce, or any proof
point fails closed. Public-network release evidence must repeat these vectors against the exact
deployed NEF/VK pair.

## 7. Operator sequence

The generated production plan requires this order:

1. deploy `ContractZkVerifier` and `Sp1Groth16Verifier`,
2. register the exact raw SP1 program VK for `ProofSystem.Sp1`,
3. register `Sp1Groth16Verifier` as the SP1 terminal verifier,
4. permanently disable SP1 envelope-only acceptance,
5. freeze the exact program VK and terminal with `LockProofSystemConfiguration`,
6. register `ContractZkVerifier` as the `ProofType.Zk` route.

The lock stores the exact VK, not just a boolean. Once active, previously registered
bootstrap keys are rejected and no owner can add/remove keys, replace the terminal,
or alter envelope-only state. A proof-system upgrade deploys a new immutable terminal
and versioned router, then uses outer `VerifierRegistry` governance to switch routes.

Operators must compare raw bytes, not displayed `UInt256` text, when moving
`vk.bytes32()` between Rust and Neo tooling.
