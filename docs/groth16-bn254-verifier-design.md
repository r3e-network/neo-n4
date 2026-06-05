# BN254 Groth16 verifier contract — design

Status: draft for review. Target: a deployable NeoHub verifier contract that performs a
real on-chain Groth16 pairing check over BN254 (alt_bn128), using the CryptoLib `bn254*`
native methods, so that `ContractZkVerifier` can dispatch `ProofType.Zk` settlement to it
instead of falling back to envelope-only acceptance.

## 1. Problem and context

`contracts/NeoHub.ContractZkVerifier` is a router. It parses the canonical N4 batch
commitment, extracts the RISC-V ZK proof payload, checks the proof system and that the
verification-key id is governance-registered, then either:

- dispatches to a governance-registered downstream verifier via
  `Contract.Call(verifier, "verifyZkProof", CallFlags.ReadOnly, { proofSystem, verificationKeyId, publicInputHash, proofBytes })`, or
- accepts the envelope without proof math, when (and only when) envelope-only mode is
  explicitly enabled for that proof system.

Today no downstream verifier implementing `verifyZkProof` does real proof-system math, so
every accepted batch relies on the envelope-only escape hatch. This contract closes that
gap for the proof systems whose end proof is a Groth16 proof over BN254 (SP1 and RISC-Zero
both expose a Groth16/BN254 "wrapper" proof; this verifier serves those paths).

The router ABI is fixed and is the integration contract we must satisfy:

```
verifyZkProof(byte proofSystem, byte[] verificationKeyId, byte[] publicInputHash, byte[] proofBytes) : bool
```

## 2. Cryptographic background (Groth16 over BN254)

A Groth16 verifying key is `(α ∈ G1, β ∈ G2, γ ∈ G2, δ ∈ G2, IC ∈ G1^{ℓ+1})`. A proof is
`(A ∈ G1, B ∈ G2, C ∈ G1)`. For public inputs `a₁..a_ℓ ∈ F_r` the verifier forms

```
vk_x = IC₀ + Σ_{i=1..ℓ} aᵢ · ICᵢ            (in G1)
```

and accepts iff

```
e(A, B) = e(α, β) · e(vk_x, γ) · e(C, δ)
```

where `e` is the optimal-ate pairing into the target group GT, and `·` is the GT group
operation (multiplication in F_p12). This is exactly four pairings and a fixed number of
G1 operations — no attacker-influenced loop bounds.

## 3. Mapping to the CryptoLib bn254 surface

The native methods (already implemented and tested in core) give us first-class,
composable group elements rather than a single boolean precompile:

| Need | CryptoLib call | Returns |
|------|----------------|---------|
| Decode a G1/G2 point | `Bn254Deserialize(byte[])` | interop point (G1 from 64 B, G2 from 128 B) |
| `aᵢ · ICᵢ` | `Bn254Mul(point, scalar)` | G1 point |
| `vk_x` accumulation | `Bn254Add(g1, g1)` | G1 point |
| `e(·,·)` | `Bn254Pairing(g1, g2)` | GT element |
| GT product | `Bn254Add(gt, gt)` | GT element (F_p12 multiply) |
| Final compare | `Bn254Equal(gt, gt)` | bool |

Decode-time guarantees we rely on (enforced by core, see `BN254.TryDeserializeG1/G2`):
canonical big-endian coordinates below the field modulus, on-curve, and — for G2 — prime
-order subgroup membership. A malformed or off-subgroup proof point therefore faults during
decode rather than yielding a spurious pairing result. `Bn254Mul` reduces its 32-byte
big-endian scalar modulo the curve order, so any 32-byte public input is a valid exponent.

Note we intentionally do **not** mirror `ExampleZKP.cs`, which compares pairing results by
casting the interop value to `byte[]` and using `==`. We compare GT elements with
`Bn254Equal`, which is defined as value equality on the deserialized F_p12 element — the
correct and intended primitive for this check.

## 4. N4 public-input convention (the key interface decision)

The router passes a single 32-byte `publicInputHash` (the public-values digest carried in
the batch commitment at offset 284), plus a `verificationKeyId`. The N4 model therefore is:

- The **verifying key identity** is handled out of band: `verificationKeyId` selects a
  governance-registered VK. It is *not* a circuit public input.
- The **circuit exposes exactly one public input**: the public-values digest, supplied as
  `publicInputHash` and interpreted as a big-endian scalar reduced mod r.

So `ℓ = 1` and a registered VK carries exactly `IC₀` and `IC₁`, and

```
vk_x = IC₀ + (publicInputHash mod r) · IC₁
```

This matches how SP1/RISC-Zero Groth16 wrappers expose a single field element committing to
the public outputs. Circuits needing more than one public input are out of scope for this
ABI; supporting them would require a router/ABI change to pass the input vector, and is
called out as future work rather than silently approximated.

## 5. Contract design

Proposed contract: `contracts/NeoHub.Groth16Verifier/Groth16VerifierContract.cs`,
`[DisplayName("NeoHub.Groth16Verifier")]`, following the conventions already used by
`ContractZkVerifier` (governance owner in storage under `0xFF`, owner-gated mutations via
`Runtime.CheckWitness`, events on every state change, `[Safe]` reads).

### 5.1 Storage

| Prefix | Key | Value |
|--------|-----|-------|
| `0xFF` | — | owner `UInt160` |
| `0x01` | `verificationKeyId` (32 B) | encoded VK material (see 5.3) |

### 5.2 Methods

- `_deploy(object data, bool update)` — set initial owner from `data` (a `UInt160`).
- `GetOwner() : UInt160` `[Safe]`, `SetOwner(UInt160)` — ownership, mirroring the router.
- `RegisterVerifyingKey(byte[] verificationKeyId, byte[] vk)` — owner-gated. Validates the
  encoding (length and that every point decodes) by round-tripping each point through
  `Bn254Deserialize`, then stores `vk`. Emits `VerifyingKeyRegistered`.
- `RemoveVerifyingKey(byte[] verificationKeyId)` — owner-gated; deletes; emits the event
  with `registered = false`.
- `IsVerifyingKeyRegistered(byte[] verificationKeyId) : bool` `[Safe]`.
- `verifyZkProof(byte proofSystem, byte[] verificationKeyId, byte[] publicInputHash, byte[] proofBytes) : bool`
  `[Safe]` — the router entry point. Does **not** check witness (it is a pure verification
  function; trust comes from the registered VK and the proof math). Steps:
  1. Load the VK for `verificationKeyId`; fault if absent.
  2. Decode `α, β, γ, δ, IC₀, IC₁` from the stored VK.
  3. Require `publicInputHash.Length == 32`; require `proofBytes.Length == 256`.
  4. Decode `A = proofBytes[0..64]`, `B = proofBytes[64..192]`, `C = proofBytes[192..256]`.
  5. `vk_x = Bn254Add(IC₀, Bn254Mul(IC₁, publicInputHash))`.
  6. `lhs = Bn254Pairing(A, B)`.
  7. `rhs = Bn254Add(Bn254Add(Bn254Pairing(α, β), Bn254Pairing(vk_x, γ)), Bn254Pairing(C, δ))`.
  8. return `Bn254Equal(lhs, rhs)`.

`proofSystem` is accepted as an argument for ABI parity with the router and may be range
-checked, but the verifier is proof-system-agnostic: SP1 and RISC-Zero Groth16/BN254 proofs
share this check.

### 5.3 Encodings

All points use the EIP-197 big-endian encoding the native layer already enforces.

- G1 point: 64 bytes `[x‖y]`.
- G2 point: 128 bytes `[x_im‖x_re‖y_im‖y_re]`.
- VK material (fixed, `ℓ = 1`): `α(64) ‖ β(128) ‖ γ(128) ‖ δ(128) ‖ IC₀(64) ‖ IC₁(64)` = 576 bytes.
- Proof: `A(64) ‖ B(128) ‖ C(64)` = 256 bytes. Produced by the off-chain prover adapter
  from the SP1/RISC-Zero Groth16 proof.

## 6. Security considerations

- **Trusted VK only.** A Groth16 verifier is only sound against an honestly generated VK.
  Registration is owner-gated; an attacker who could register an arbitrary VK could forge
  acceptances. This is the same trust assumption as the router's `RegisterVerificationKey`
  allow-list, and the two ids are kept consistent by governance.
- **Point validation.** Every VK and proof point is decoded through `Bn254Deserialize`,
  which rejects non-canonical, off-curve, and (for G2) off-subgroup encodings. Subgroup
  enforcement on B and on the VK G2 points is what prevents small-subgroup forgeries.
- **Bounded cost / no DoS.** The work is constant: one `Bn254Mul`, one G1 `Bn254Add`, four
  `Bn254Pairing`, two GT `Bn254Add`, one `Bn254Equal`. There is no loop over
  attacker-controlled length (IC length is fixed by the registered VK, not by input).
- **Determinism.** All operations are pure field/curve arithmetic; no time, randomness, or
  external state beyond the registered VK read.
- **Read-only.** `verifyZkProof` performs no writes and is dispatched with
  `CallFlags.ReadOnly` by the router.

## 7. Estimated cost

Dominated by four pairings at `CpuFee = 1 << 23` each, plus a scalar-mul at `1 << 21` and a
few adds/compare. This is a fixed per-call ceiling; the router already prices a single proof
verification as one dispatch, and this verifier's cost is independent of proof size beyond
the fixed 256-byte proof and 32-byte input.

## 8. Test plan (TDD — written before the contract)

Tests run the compiled contract through `Neo.SmartContract.Testing`, which executes against
an in-process Neo engine. **Prerequisite:** that engine must be the bn254-enabled core (see
§9), otherwise `bn254*` calls fault as "method not found".

Vectors: a Groth16/BN254 verifying key, proof, and public input produced by a standard
toolchain (snarkjs/circom on bn128, or gnark BN254) for a minimal circuit, committed as a
fixture. Cases:

1. **Valid proof, valid public input** → `verifyZkProof` returns `true`.
2. **Tampered proof** (flip one byte of A/B/C) → returns `false` (not a fault: the pairing
   simply does not balance).
3. **Wrong public input** (use a different digest) → returns `false`.
4. **Unregistered `verificationKeyId`** → faults ("verifying key not registered").
5. **Malformed VK / proof lengths** → faults with a specific message.
6. **Off-curve / non-canonical proof point** → faults at decode (forgery attempt rejected).
7. **Governance:** non-owner `RegisterVerifyingKey` faults; owner succeeds and emits the
   event; `IsVerifyingKeyRegistered` reflects state.

A round-trip test also confirms the VK and proof encodings in §5.3 decode to the same points
the fixture defines.

## 9. Build & test integration (open item, decided before #39 implementation)

The devpack framework binding (`CryptoLib.BN254.cs`, added to
`external/neo-devpack-dotnet/src/Neo.SmartContract.Framework/Native`) lets the contract
type-check and lets nccs emit `bn254*` calls by name — these need no native manifest at
compile time, so `dotnet build` and nccs generation work unchanged.

Execution is the open question. `Neo.SmartContract.Testing` resolves Neo core from the NuGet
package pinned by `NeoCorePackageVersion` (currently `3.9.3-CI02036`), which predates bn254.
For the §8 tests to exercise real pairing math, the test engine must load the local
bn254-enabled core at `external/neo/src`. Options, to be chosen before implementing #39:

1. Pack `external/neo` to a local feed and bump `NeoCorePackageVersion` to that build
   (closest to the project's intended publish-and-pin flow; least structural change).
2. Add a `Directory.Build.props` redirect so the contract test project references the local
   core projects instead of the package (keeps everything source-built in the monorepo).

This is a build-wiring decision with no effect on the contract design above; it is recorded
here so the implementation step starts from an explicit choice rather than discovering the
gap at test time.
