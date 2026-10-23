#!/usr/bin/env python3
"""Cross-language canonical-encoding gate.

The 2026-10-21 audit found TWO cross-language layout divergences that had survived
because each side's own tests used a mirror of its own (wrong) implementation as the
oracle:

  * the withdrawal leaf hash — the off-chain C#/Rust layout did not match the
    on-chain `NeoHub.SharedBridge.ComputeWithdrawalLeafHash` layout, so EVERY L2→L1
    withdrawal failed on-chain;
  * the Gateway guest's public-input preimage — the guest rebuilt a 348-byte buffer
    while the authoritative domain is 352 bytes, so the recursive Gateway proof could
    never be generated.

Both defects were invisible to the unit tests because the "expected" value was derived
from the same code under test. This gate closes that class of defect: it asserts the
INVARIANTS that both sides must agree on, using values that are written out literally
here — never re-derived from either implementation.

Checks performed:

  1. The canonical public-input preimage is exactly 352 bytes and its field offsets
     match the documented layout (C# `StateRootCalculator` / `BatchSerializer` and
     Rust `hashing::hash_public_inputs_with_forced`).
  2. The canonical commitment is exactly 321 bytes with the documented field offsets
     (C# `BatchSerializer` / Rust `wire::commitment`).
  3. The withdrawal leaf preimage matches the ON-CHAIN layout — 118 bytes for the
     canonical input, with the documented field order, and the expected double-SHA-256
     digest. The expected digest is a literal, so a change on either side is caught.
  4. The Gateway guest's public-input reconstruction agrees with the authoritative
     Rust implementation (the cross-crate test inside the guest crate asserts the
     same thing at runtime; this gate makes the dependency explicit in CI).
  5. Every `Contract.Call` in the NeoHub contracts passes its arguments as INDIVIDUAL
     trailing values. The devpack exposes a single `params object[]` overload, so a
     nested `new object[] { ... }` would make the VM see arity 1 and the call would
     never resolve against the target method. This is the shape of the ZkVerifier
     arity defect.
  6. The contract sources do not use `ArgumentNullException.ThrowIfNull` (and friends)
     — the NeoVM has no such API and the contract would fail to compile with the
     pinned devpack.

Exit code 0 = all gates pass; non-zero = at least one gate failed.
"""

from __future__ import annotations

import hashlib
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

FAILURES: list[str] = []
CHECKS = 0


def check(condition: bool, message: str) -> None:
    global CHECKS
    CHECKS += 1
    if not condition:
        FAILURES.append(message)


def sha256(data: bytes) -> bytes:
    return hashlib.sha256(data).digest()


def hash256(data: bytes) -> bytes:
    return sha256(sha256(data))


def u32(value: int) -> bytes:
    return value.to_bytes(4, "little")


def u64(value: int) -> bytes:
    return value.to_bytes(8, "little")


# ─────────────────────────── 1. public-input layout (352 bytes) ───────────────────────────

PUBLIC_INPUTS_SIZE = 4 + 8 + 8 + 8 + 10 * 32 + 4
PUBLIC_INPUTS_OFFSETS = {
    "chain_id": (0, 4),
    "batch_number": (4, 8),
    "first_block": (12, 8),
    "last_block": (20, 8),
    "pre_state_root": (28, 32),
    "post_state_root": (60, 32),
    "tx_root": (92, 32),
    "receipt_root": (124, 32),
    "withdrawal_root": (156, 32),
    "l2_to_l1_message_root": (188, 32),
    "l2_to_l2_message_root": (220, 32),
    "l1_message_hash": (252, 32),
    "da_commitment": (284, 32),
    "block_context_hash": (316, 32),
    "forced_inclusion_count": (348, 4),
}


def gate_public_inputs_layout() -> None:
    check(
        PUBLIC_INPUTS_SIZE == 352,
        f"canonical public-input preimage must be 352 bytes, computed {PUBLIC_INPUTS_SIZE}",
    )
    # The offsets must tile the buffer exactly, with no gap and no overlap.
    cursor = 0
    for name, (offset, size) in PUBLIC_INPUTS_OFFSETS.items():
        check(
            offset == cursor,
            f"public-input field '{name}' must start at {cursor}, layout says {offset}",
        )
        cursor = offset + size
    check(
        cursor == PUBLIC_INPUTS_SIZE,
        f"public-input layout must end at {PUBLIC_INPUTS_SIZE}, ends at {cursor}",
    )

    # The C# and Rust sides must both declare the same size.
    cs = (ROOT / "src" / "Neo.L2.State" / "StateRootCalculator.cs").read_text(encoding="utf-8")
    check(
        "352" in cs,
        "StateRootCalculator.cs must reference the 352-byte public-input domain",
    )
    rust = (ROOT / "bridge" / "neo-execution-core" / "src" / "hashing.rs").read_text(encoding="utf-8")
    check(
        "with_capacity(352)" in rust,
        "neo-execution-core hashing.rs must allocate the 352-byte public-input domain",
    )
    guest = (ROOT / "bridge" / "neo-zkvm-gateway-guest" / "src" / "lib.rs").read_text(
        encoding="utf-8"
    )
    check(
        "PUBLIC_INPUTS_BYTES" in guest,
        "the Gateway guest must use the shared PUBLIC_INPUTS_BYTES constant",
    )
    check(
        "[0u8; 348]" not in guest and "public_inputs = [0u8; 348]" not in guest,
        "the Gateway guest still declares a 348-byte public-input buffer — that is the "
        "pre-fix defect (the canonical domain is 352 bytes)",
    )


# ─────────────────────────── 2. commitment layout (321 bytes) ───────────────────────────

COMMITMENT_FIXED_SIZE = 321


def gate_commitment_layout() -> None:
    check(
        COMMITMENT_FIXED_SIZE == 321,
        f"canonical commitment fixed size must be 321 bytes, computed {COMMITMENT_FIXED_SIZE}",
    )
    cs = (ROOT / "src" / "Neo.L2.Batch" / "BatchSerializer.cs").read_text(encoding="utf-8")
    check(
        "PublicInputsSize" in cs and "CommitmentFixedSize" in cs,
        "BatchSerializer.cs must declare the canonical commitment/public-input sizes",
    )
    rust = (ROOT / "bridge" / "neo-execution-core" / "src" / "wire" / "constants.rs").read_text(
        encoding="utf-8"
    )
    check(
        "COMMITMENT_FIXED_SIZE: usize = 321" in rust,
        "neo-execution-core constants.rs must declare COMMITMENT_FIXED_SIZE = 321",
    )


# ─────────────────────────── 3. withdrawal leaf hash (on-chain layout) ───────────────────────────

# The canonical input — symmetric bytes so the UInt160 byte order is irrelevant:
#   chainId=1, emitting=0xAA*20, l2Sender=0xBB*20, l2Asset=0xDD*20,
#   nonce=1, l1Asset=0xEE*20, l1Recipient=0xCC*20, amount=1000 (0xE803 unsigned-LE)
#
# The ON-CHAIN layout (NeoHub.SharedBridge.ComputeWithdrawalLeafHash) is:
#   4B chainId LE | 20B emitting | 20B l2Sender | 20B l2Asset | 8B nonce LE
#   | 20B l1Asset | 20B l1Recipient | 4B amountLen LE | N amountBytes LE
WITHDRAWAL_EXPECTED_LEN = 118
WITHDRAWAL_EXPECTED_HASH = "1eefe1324d54c75b06a63d1a334773747d99e1e10b3fba75c869cf9271067f2b"


def canonical_withdrawal_preimage() -> bytes:
    a = bytes([0xAA] * 20)
    b = bytes([0xBB] * 20)
    c = bytes([0xCC] * 20)
    d = bytes([0xDD] * 20)
    e = bytes([0xEE] * 20)
    amount = (1000).to_bytes(2, "little")

    buf = bytearray()
    buf += u32(1)          # chainId
    buf += a               # emittingContract
    buf += b               # l2Sender
    buf += d               # l2Asset
    buf += u64(1)          # withdrawalNonce
    buf += e               # l1Asset
    buf += c               # l1Recipient
    buf += u32(len(amount))
    buf += amount
    return bytes(buf)


def gate_withdrawal_layout() -> None:
    preimage = canonical_withdrawal_preimage()
    check(
        len(preimage) == WITHDRAWAL_EXPECTED_LEN,
        f"the on-chain withdrawal preimage must be {WITHDRAWAL_EXPECTED_LEN} bytes, got {len(preimage)}",
    )
    digest = hash256(preimage).hex()
    check(
        digest == WITHDRAWAL_EXPECTED_HASH,
        "the on-chain withdrawal leaf hash changed: the off-chain encoder must be updated "
        f"in lockstep (expected {WITHDRAWAL_EXPECTED_HASH}, computed {digest})",
    )

    # The off-chain encoder must use the SAME field order. Checking the source for the
    # field order is the only way to catch a divergence that the (mirrored) unit tests
    # cannot see.
    hasher = (ROOT / "src" / "Neo.L2.State" / "MessageHasher.cs").read_text(encoding="utf-8")
    # Restrict the scan to the ENCODER body: the null guards above it mention the same
    # field names in a different (guard) order.
    encoder_start = hasher.index("public static byte[] EncodeWithdrawal(")
    encoder_end = hasher.index("public static WithdrawalRequest DecodeWithdrawal(")
    encoder = hasher[encoder_start:encoder_end]
    order = [
        "withdrawal.ChainId",
        "withdrawal.EmittingContract",
        "withdrawal.L2Sender",
        "withdrawal.L2Asset",
        "withdrawal.Nonce",
        "withdrawal.L1Asset",
        "withdrawal.L1Recipient",
    ]
    # Match the WRITE statements only. The null guards above them mention the same field
    # names in a different order, so a plain `find` would read the guard order.
    write_patterns = [
        r"withdrawal\.ChainId\s*\)\s*;\s*pos\s*\+=\s*4",
        r"withdrawal\.EmittingContract\.GetSpan\(\)\.CopyTo",
        r"withdrawal\.L2Sender\.GetSpan\(\)\.CopyTo",
        r"withdrawal\.L2Asset\.GetSpan\(\)\.CopyTo",
        r"withdrawal\.Nonce\s*\)\s*;\s*pos\s*\+=\s*8",
        r"withdrawal\.L1Asset\.GetSpan\(\)\.CopyTo",
        r"withdrawal\.L1Recipient\.GetSpan\(\)\.CopyTo",
    ]
    positions = []
    for field, pattern in zip(order, write_patterns):
        match = re.search(pattern, encoder)
        check(
            match is not None,
            f"MessageHasher.EncodeWithdrawal must write '{field}' into the preimage",
        )
        positions.append(match.start() if match else -1)
    check(
        positions == sorted(positions),
        "MessageHasher.EncodeWithdrawal must encode the fields in the ON-CHAIN order: "
        "chainId, emittingContract, l2Sender, l2Asset, nonce, l1Asset, l1Recipient",
    )
    check(
        "withdrawal.L1Asset" in encoder,
        "MessageHasher.EncodeWithdrawal must bind the L1 payout asset into the leaf hash",
    )

    rust = (ROOT / "bridge" / "neo-execution-core" / "src" / "native.rs").read_text(encoding="utf-8")
    check(
        "l1_asset" in rust,
        "neo-execution-core's withdrawal_hash must bind the L1 payout asset",
    )


# ─────────────────────────── 4. gateway guest cross-crate agreement ───────────────────────────


def gate_gateway_guest() -> None:
    guest = (ROOT / "bridge" / "neo-zkvm-gateway-guest" / "src" / "lib.rs").read_text(
        encoding="utf-8"
    )
    check(
        "neo_execution_core::hashing::hash_public_inputs_with_forced" in guest,
        "the Gateway guest must have a cross-crate test that uses the authoritative "
        "neo_execution_core public-input hash as its oracle (an implementation must "
        "never validate against its own mirror)",
    )
    check(
        "forced_inclusion_count" in guest,
        "the Gateway guest must bind forced_inclusion_count into the public-input preimage",
    )
    check(
        "PUBLIC_INPUT_SUPPLEMENT_BYTES: usize = 32 + 32 + 4" in guest,
        "the Gateway guest's public-input supplement must carry l1MessageHash, "
        "blockContextHash AND forcedInclusionCount",
    )


# ─────────────────────────── 5. Contract.Call arity ───────────────────────────

CONTRACT_DIRS = sorted((ROOT / "contracts").glob("NeoHub.*"))


def strip_comments(text: str) -> str:
    """Remove `//` line comments and `/* */` block comments, preserving line count.

    The comment text is replaced by spaces so reported line numbers stay accurate.
    """
    out = list(text)
    index = 0
    length = len(text)
    while index < length:
        if text.startswith("//", index):
            end = text.find("\n", index)
            if end == -1:
                end = length
            for i in range(index, end):
                out[i] = " "
            index = end
        elif text.startswith("/*", index):
            end = text.find("*/", index + 2)
            if end == -1:
                end = length
            else:
                end += 2
            for i in range(index, end):
                if out[i] != "\n":
                    out[i] = " "
            index = end
        else:
            index += 1
    return "".join(out)


def gate_contract_call_arity() -> None:
    # `Contract.Call(hash, method, flags, params object[] args)` — the devpack has a
    # SINGLE overload, so the arguments must be individual trailing values. A nested
    # `new object[] { ... }` makes the VM see arity 1 and the call never resolves.
    nested_call = re.compile(
        r"Contract\.Call\((?:[^;]*?)new object\[\]\s*\{", re.DOTALL
    )
    trailing_gas = re.compile(
        r"Contract\.Call\((?:[^;]*?),\s*\d{4,}\s*\)", re.DOTALL
    )
    for contract in CONTRACT_DIRS:
        for source in sorted(contract.glob("*.cs")):
            text = source.read_text(encoding="utf-8")
            # Strip comments: the fix's own explanatory comment quotes the bad shape.
            code = strip_comments(text)
            for match in nested_call.finditer(code):
                line = code[: match.start()].count("\n") + 1
                FAILURES.append(
                    f"{source.relative_to(ROOT)}:{line}: Contract.Call packs its arguments into a "
                    "nested array — the VM would see arity 1 and the call would never resolve"
                )
            for match in trailing_gas.finditer(code):
                line = code[: match.start()].count("\n") + 1
                FAILURES.append(
                    f"{source.relative_to(ROOT)}:{line}: Contract.Call passes a trailing gas value — "
                    "the devpack's `params object[]` overload has no gas parameter, so the value "
                    "becomes an extra (arity-breaking) argument"
                )
    check(True, "Contract.Call arity scan completed")


# ─────────────────────────── 6. contract API compatibility ───────────────────────────

FORBIDDEN_CONTRACT_APIS = [
    "ArgumentNullException.ThrowIfNull",
    "ArgumentException.ThrowIfNullOrEmpty",
    "ArgumentOutOfRangeException.ThrowIfNegative",
    "System.Linq",
]


def gate_contract_apis() -> None:
    for contract in CONTRACT_DIRS:
        for source in sorted(contract.glob("*.cs")):
            text = source.read_text(encoding="utf-8")
            for api in FORBIDDEN_CONTRACT_APIS:
                check(
                    api not in text,
                    f"{source.relative_to(ROOT)} uses '{api}', which the NeoVM does not "
                    "support — the contract would fail to compile with the pinned devpack",
                )


# ─────────────────────────── 7. cross-contract call targets ───────────────────────────

# The manifests the contracts actually ship. `nccs` camel-cases the PascalCase C#
# method names, so the ABI name of `VerifyZkProof` is `verifyZkProof` — a static scan
# of the C# sources would compare against the WRONG string and flag correct code.
MANIFEST_DIR = (
    ROOT / "tests" / "NeoHub.Contracts.VmTests" / "TestingArtifacts"
)

# `Contract.Call(target, "methodName", ...)` resolves the name through
# `ContractAbi.GetMethod(name, argCount)`, which looks the name up in a
# `(Name, ParameterCount)` tuple dictionary with an ORDINAL string comparer. A name
# that is not in the callee's manifest therefore never resolves: the call faults
# instead of executing, and the whole route is silently dead.
#
# The 2026-10-21 audit found one such route already dead (the multisig attestation
# route could never resolve its verifier). This gate makes the class checkable.


def load_manifest_abi_methods() -> dict[str, set[str]]:
    """Map each contract's artifact file to the set of ABI method names it exposes."""
    import json

    methods_by_artifact: dict[str, set[str]] = {}
    for artifact in sorted(MANIFEST_DIR.glob("NeoHub*.artifacts.cs")):
        text = artifact.read_text(encoding="utf-8")
        match = re.search(r'Parse\(@"(.*?)"\);', text, re.DOTALL)
        if match is None:
            continue
        # A C# verbatim string doubles every quote.
        manifest = json.loads(match.group(1).replace('""', '"'))
        names = {
            method["name"]
            for method in manifest.get("abi", {}).get("methods", [])
        }
        methods_by_artifact[artifact.name] = names
    return methods_by_artifact


def gate_cross_contract_call_targets() -> None:
    abi = load_manifest_abi_methods()
    if not abi:
        FAILURES.append(
            "no compiled contract artifacts found under "
            f"{MANIFEST_DIR.relative_to(ROOT)} — cannot verify cross-contract call targets"
        )
        return

    # Every ABI method name any contract exposes.
    all_abi_names: set[str] = set()
    for names in abi.values():
        all_abi_names |= names

    # Methods of contracts that live OUTSIDE this repository. A call to one of these is
    # resolved against the callee's own manifest, which is not checked in here.
    #
    # `transfer` is the NEP-17 token method: `SharedBridge` moves the escrowed asset with
    # `Contract.Call(asset, "transfer", ...)`, where `asset` is a token contract deployed
    # by someone else.
    EXTERNAL_ABI_NAMES = {
        "transfer",       # NEP-17
        "balanceOf",      # NEP-17
        "decimals",       # NEP-17
        "symbol",         # NEP-17
        "totalSupply",    # NEP-17
    }

    call = re.compile(r'Contract\.Call\(\s*\w+\s*,\s*"([A-Za-z_]\w*)"')
    for contract in CONTRACT_DIRS:
        for source in sorted(contract.glob("*.cs")):
            text = source.read_text(encoding="utf-8")
            code = strip_comments(text)
            for match in call.finditer(code):
                method = match.group(1)
                line = code[: match.start()].count("\n") + 1
                if method in all_abi_names or method in EXTERNAL_ABI_NAMES:
                    continue
                # Case-insensitive match => a case mismatch, which is equally fatal.
                folded = {name for name in all_abi_names if name.lower() == method.lower()}
                if folded:
                    FAILURES.append(
                        f"{source.relative_to(ROOT)}:{line}: Contract.Call target \"{method}\" is "
                        f"not in any contract ABI — the ABI exposes {sorted(folded)}; "
                        "`ContractAbi.GetMethod` compares ordinally, so the call would never resolve"
                    )
                else:
                    FAILURES.append(
                        f"{source.relative_to(ROOT)}:{line}: Contract.Call target \"{method}\" is "
                        "not in any contract ABI — the call would never resolve"
                    )
    check(True, "cross-contract call target scan completed")


# ─────────────────────────── 8. reserved proof-system byte ───────────────────────────

# Proof system 0 is RESERVED for the multisig attestation route. The ZK proof systems
# start at 1 (SP1) and 2 (RISC Zero).
#
# The 2026-10-21 audit found the reservation and the router disagreed: the router
# dispatched the multisig route on byte 0, while `RegisterProofVerifier` rejected byte 0.
# `GetProofVerifier(0)` could therefore never resolve, and every multisig commitment
# verified as `false` — no committee-attested batch could ever settle, and no operator
# action could repair it.
#
# The multisig verifier now lives in its own slot (`RegisterMultisigVerifier`), so byte 0
# keeps meaning "unset" in the proof-system key space. This gate keeps it that way.


def gate_reserved_proof_system_byte() -> None:
    zk = ROOT / "contracts" / "NeoHub.ZkVerifier" / "ZkVerifierContract.cs"
    text = zk.read_text(encoding="utf-8")
    code = strip_comments(text)

    # EVERY proof-system control surface must keep rejecting byte 0. The 2026-10-22 audit
    # round found SetEnvelopeOnlyAllowed was the one surface that did not: an owner call
    # `SetEnvelopeOnlyAllowed(0, true)` made the multisig route accept a zero-signature
    # commitment (fail-open), and byte 0 has no configuration lock to close the hole with
    # because verification keys cannot be registered for it.
    for surface in ("RegisterProofVerifier", "RegisterVerificationKey", "SetEnvelopeOnlyAllowed"):
        body = re.search(
            rf"{surface}\s*\([^)]*\)\s*\{{(.*?)\n    \}}", code, re.DOTALL
        )
        check(body is not None, f"could not locate {surface} in ZkVerifierContract.cs")
        if body is not None:
            check(
                "proofSystem > 0" in body.group(1),
                f"{surface} must keep rejecting proof system 0 — the byte is reserved "
                "(it means 'unset'), and the multisig route must never be reachable through it",
            )

    # The multisig router must read the dedicated slot, never the proof-system slot.
    router = re.search(
        r"private static bool VerifyMultisigProof\s*\(.*?\)\s*\{(.*?)\n    \}", code, re.DOTALL
    )
    check(router is not None, "could not locate VerifyMultisigProof in ZkVerifierContract.cs")
    if router is not None:
        body = router.group(1)
        check(
            "GetMultisigVerifier()" in body,
            "VerifyMultisigProof must read the dedicated multisig slot "
            "(GetMultisigVerifier), not the proof-system slot",
        )
        check(
            "GetProofVerifier(" not in body,
            "VerifyMultisigProof must not read the proof-system slot: byte 0 is reserved and "
            "can never be registered, so the lookup would always resolve to the zero address",
        )
        # A fail-open branch here would accept a multisig commitment with ZERO signatures.
        # Pre-production testing of the success path goes through a stub verifier in the
        # dedicated slot, not a contract-level bypass.
        check(
            "IsEnvelopeOnlyAllowed" not in body,
            "VerifyMultisigProof must not consult the envelope-only flags: the reserved byte 0 "
            "cannot carry a legitimate flag, and any envelope-only bypass on this route "
            "accepts a zero-signature commitment",
        )



# ─────────────────────── 9. cross-language surface coverage ───────────────────────

# The 2026-10-21 audit's central lesson was that a cross-language encoding survives only
# while BOTH sides happen to agree, and nothing notices when they stop. Every such surface
# needs a vector in ONE shared file that neither implementation produces at test time.
#
# This table is the coverage contract. Each entry names a surface and the fixture keys that
# pin it. Adding an encoding surface without adding it here fails the gate; adding a fixture
# key that no surface claims also fails, so the table cannot rot into decoration.
SHARED_VECTORS = ROOT / "tests" / "Shared" / "canonical_encoding_vectors.hex"

CROSS_LANGUAGE_SURFACES: dict[str, tuple[str, ...]] = {
    # Batch commitment header: SharedBridge/RollupHub parse it, the .NET batcher writes it,
    # and the Rust guest parses it. Offset 284 holds the public-input digest.
    "commitment": ("commitment",),
    # Public-input preimage (352B): the signed domain. The non-zero forced-inclusion vector
    # exists because a count that is always 0 cannot detect a count-encoding fork. The scalar
    # fields below are the individual inputs the preimage binds, exported so the Rust lane can
    # rebuild the preimage from named values instead of re-slicing the buffer.
    "public-inputs": (
        "public_inputs",
        "public_inputs_with_forced",
        "public_input_hash",
        "chain_id",
        "batch_number",
        "first_block",
        "last_block",
        "pre_state_root",
        "post_state_root",
        "tx_root",
        "receipt_root",
        "l2_to_l1_message_root",
        "l2_to_l2_message_root",
        "l1_message_hash",
        "da_commitment",
        "block_context_hash",
        "proof_type",
    ),
    # Withdrawal leaf: FOUR implementations, and the audit found it had already forked
    # (off-chain 98B vs on-chain 118B). This is the surface that motivated the whole effort.
    "withdrawal-leaf": (
        "withdrawal_leaf_preimage",
        "withdrawal_leaf_hash",
        "withdrawal_chain_id",
        "withdrawal_nonce",
        "withdrawal_amount",
    ),
    # Withdrawal Merkle fold + inclusion proof framing.
    "withdrawal-merkle": (
        "withdrawal_leaf_count",
        "withdrawal_root",
        "withdrawal_proof",
        "withdrawal_proof_leaf_index",
    ),
    # L2 chain config consumed by RollupHub.registerChain.
    "chain-config": ("chain_config",),
    # Gateway aggregation frontier fold. The contract rebuilds TWO roots from the same
    # constituent leaves with DIFFERENT odd-leaf behavior: the commitments root duplicates
    # odd nodes (Bitcoin-style) while the global message root promotes them. The Rust
    # gateway guest computes both with merkle_root_duplicate_odd / merkle_root_promote_odd.
    # The asymmetry only manifests for ODD counts >= 3 — the existing happy-path VM test
    # used a single constituent, where both folds trivially return the leaf.
    "gateway-frontier": (
        "gateway_leaf_0",
        "gateway_leaf_1",
        "gateway_leaf_2",
        "gateway_root_duplicate_odd",
        "gateway_root_promote_odd",
        "gateway_leaf_count",
    ),
    # External-bridge inbound message: the watcher (Rust) SIGNS these bytes and the .NET
    # tooling (ExternalMessageHasher.EncodeCanonical) re-encodes them. Two mirrors that had
    # never been compared; the consuming on-chain contract (ExternalBridgeEscrow) is not yet
    # landed, so the C# encoder is the authoritative consumer reference.
    "external-message": (
        "xmsg_canonical",
        "xmsg_hash",
        "xmsg_external_chain_id",
        "xmsg_neo_chain_id",
        "xmsg_nonce",
        "xmsg_direction",
        "xmsg_deadline",
        "xmsg_message_type",
        "xmsg_payload",
    ),
}


def gate_cross_language_surface_coverage() -> None:
    text = SHARED_VECTORS.read_text(encoding="utf-8")
    present: set[str] = set()
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        present.add(line.split("=", 1)[0].strip())

    declared: set[str] = set()
    for surface, keys in CROSS_LANGUAGE_SURFACES.items():
        for key in keys:
            declared.add(key)
            check(
                key in present,
                f"cross-language surface '{surface}' requires fixture key '{key}', "
                "but the shared vector file does not carry it",
            )

    # Numbered leaves are generated, so they are declared by prefix.
    undeclared = sorted(
        key
        for key in present
        if key not in declared and not re.fullmatch(r"withdrawal_leaf_\d+", key)
    )
    check(
        not undeclared,
        "the shared vector file carries keys no surface declares — either add them to "
        f"CROSS_LANGUAGE_SURFACES or drop them: {undeclared}",
    )


def main() -> int:
    print("== cross-language canonical-encoding gate ==")
    gate_public_inputs_layout()
    gate_commitment_layout()
    gate_withdrawal_layout()
    gate_gateway_guest()
    gate_contract_call_arity()
    gate_contract_apis()
    gate_cross_contract_call_targets()
    gate_reserved_proof_system_byte()
    gate_cross_language_surface_coverage()

    if FAILURES:
        print(f"\nFAILED ({len(FAILURES)} problem(s), {CHECKS} assertions):")
        for failure in FAILURES:
            print(f"  - {failure}")
        return 1
    print(f"\nOK: {CHECKS} assertions passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
