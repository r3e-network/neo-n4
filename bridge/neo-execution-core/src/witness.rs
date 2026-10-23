use crate::{
    hash160,
    types::{ExecutionError, ParsedTransaction},
};

use alloc::vec::Vec;

use sha2::{Digest, Sha256};

// First 4 bytes of SHA256 of the interop service name — the SYSCALL operand the Neo VM uses to
// dispatch `System.Crypto.CheckSig` / `System.Crypto.CheckMultisig`. The service names are
// unchanged from N3, so the hash operands match the stock VM.
const CHECKSIG_SYSCALL: [u8; 4] = [0x56, 0xe7, 0xb3, 0x27];
const CHECKMULTISIG_SYSCALL: [u8; 4] = [0x9e, 0xd0, 0xdc, 0x3a];

/// Neo `OpCode.SYSCALL` (unchanged by the fork: 0x41, 4-byte hash operand).
const OP_SYSCALL: u8 = 0x41;

/// Neo `OpCode.PUSHDATA1` — the fork's VM REPLACED the stock N3 PUSHBYTES1..75 opcodes with
/// Bitcoin-style length-prefixed PUSHDATA1/2/4. In the fork's opcode table:
/// `PUSHDATA1 = 0x0C`, `PUSHINT8 = 0x00`, `NOP = 0x21`, `PUSH1..PUSH16 = 0x11..0x20`.
/// The stock N3 encodings (0x21 = PUSHBYTES33, 0x51 = PUSH1) are therefore NOT valid here:
/// 0x21 is NOP and 0x51 is a truncated LDARG-style instruction. Both the C# core VM
/// (`Contract.CreateSignatureRedeemScript` emits `0C 21 <pubkey>`) and the Rust VM
/// (`external/neo-vm-rs` opcode table) speak this dialect — the witness parser must too.
const OP_PUSHDATA1: u8 = 0x0C;

/// Single-signature redeem script frame: `PUSHDATA1 0x21 <33-byte pubkey> SYSCALL <hash>`.
const SINGLE_SIG_FRAME: usize = 2 + 33 + 1 + 4;

/// Invocation frame per signature: `PUSHDATA1 0x40 <64-byte compact signature>`.
const SIGNATURE_FRAME: usize = 2 + 64;

// The fork keeps small-integer pushes at 0x11..=0x20 (PUSH1..PUSH16), capping the standard
// multi-signature shapes at 16 parties — same bound as N3, different byte range.
const MAX_MULTISIG_PARTIES: usize = 16;

/// Verify every witness of a parsed transaction against its signers.
///
/// Only the standard fork-VM account forms are accepted: a single-signature witness
/// (`PUSHDATA1 0x21 <pubkey> SYSCALL <CheckSig>`) and an m-of-n multi-signature witness
/// (`PUSHm | n×[PUSHDATA1 <pubkey>] | PUSHn | SYSCALL <CheckMultisig>` with m, n ≤ 16,
/// where PUSHm/PUSHn are the fork's 0x11..0x20 constant pushes). The signature message is
/// the Neo sign data — `network(u32 LE) ‖ txHash`, ECDSA/secp256r1 over `SHA256(message)` —
/// matching `Crypto.VerifySignature(GetSignData(network))` on the core. `txHash` is the Neo
/// inventory id (single SHA-256). The account binding
/// (`Hash160(verificationScript) == signer.Account`) and the absence of any other script shape
/// are enforced fail-closed: the V1 validity profile proves authorization for standard accounts
/// only, so an unverifiable witness invalidates the whole batch rather than degrading to
/// "trusted signer".
pub fn verify_transaction_witnesses(
    transaction: &ParsedTransaction,
    network: u32,
) -> Result<(), ExecutionError> {
    let mut hasher = Sha256::new();
    hasher.update(network.to_le_bytes());
    hasher.update(transaction.hash);
    let message_digest = hasher.finalize();

    for (signer, witness) in transaction.signers.iter().zip(&transaction.witnesses) {
        let verification = witness.verification_script.as_slice();
        if verification.is_empty() {
            return Err(ExecutionError::Unsupported(
                "contract-backed transaction witness",
            ));
        }
        if hash160(verification) != signer.account {
            return Err(ExecutionError::Invalid("transaction witness account"));
        }
        if !verify_witness(witness, verification, &message_digest)? {
            return Err(ExecutionError::Invalid("transaction witness"));
        }
    }
    Ok(())
}

fn verify_witness(
    witness: &crate::types::TransactionWitness,
    verification: &[u8],
    message_digest: &[u8],
) -> Result<bool, ExecutionError> {
    if let Some(pubkey) = parse_single_sig(verification) {
        let Some(signature) = parse_single_signature(&witness.invocation_script) else {
            return Ok(false);
        };
        return verify_signature(pubkey, signature, message_digest);
    }
    if let Some((m, pubkeys, _n)) = parse_multisig(verification) {
        let Some(signatures) = parse_signature_list(&witness.invocation_script) else {
            return Ok(false);
        };
        // Same greedy ordered match as the N3 core's CheckMultisig: signatures count is the
        // checked threshold and must not exceed the published key count.
        if signatures.len() > pubkeys.len() {
            return Ok(false);
        }
        let mut matched = 0usize;
        let mut candidate = 0usize;
        while matched < signatures.len() && candidate < pubkeys.len() {
            if verify_signature(pubkeys[candidate], signatures[matched], message_digest)? {
                matched += 1;
            }
            candidate += 1;
            if signatures.len() - matched > pubkeys.len() - candidate {
                return Ok(false);
            }
        }
        return Ok(matched == signatures.len() && matched >= m);
    }
    Err(ExecutionError::Unsupported(
        "transaction witness script shape",
    ))
}

/// `0x21 <33-byte pubkey> 0x41 <4-byte CheckSig syscall>` — Neo3 `CreateSignatureRedeemScript`.
/// `PUSHDATA1 0x21 <33-byte pubkey> SYSCALL <CheckSig>` — the fork-VM encoding that
/// `Contract.CreateSignatureRedeemScript` emits. The stock-N3 shape
/// (`0x21 <pubkey> 0x41 <hash>`, 39 bytes) is NOT accepted: in the fork's opcode table
/// `0x21` is `NOP`, so that shape is not a CheckSig script at all.
fn parse_single_sig(verification: &[u8]) -> Option<&[u8]> {
    if verification.len() == SINGLE_SIG_FRAME
        && verification[0] == OP_PUSHDATA1
        && verification[1] == 33
        && verification[35] == OP_SYSCALL
        && verification[36..40] == CHECKSIG_SYSCALL
    {
        return Some(&verification[2..35]);
    }
    None
}

/// `PUSHDATA1 0x40 <64-byte compact signature>` — the fork-VM invocation encoding.
fn parse_single_signature(invocation: &[u8]) -> Option<&[u8]> {
    if invocation.len() == SIGNATURE_FRAME && invocation[0] == OP_PUSHDATA1 && invocation[1] == 64 {
        return Some(&invocation[2..66]);
    }
    None
}

/// k×[`PUSHDATA1 0x40 <64-byte signature>`] with 1 ≤ k ≤ 16
fn parse_signature_list(invocation: &[u8]) -> Option<Vec<&[u8]>> {
    if invocation.is_empty() || invocation.len() % SIGNATURE_FRAME != 0 {
        return None;
    }
    let count = invocation.len() / SIGNATURE_FRAME;
    if count > MAX_MULTISIG_PARTIES {
        return None;
    }
    let mut signatures = Vec::with_capacity(count);
    for index in 0..count {
        let start = index * SIGNATURE_FRAME;
        if invocation[start] != OP_PUSHDATA1 || invocation[start + 1] != 64 {
            return None;
        }
        signatures.push(&invocation[start + 2..start + SIGNATURE_FRAME]);
    }
    Some(signatures)
}

/// `<push m> n×[PUSHDATA1 0x21 <33-byte pubkey>] <push n> SYSCALL <CheckMultisig syscall>`
/// Matches the fork's `CreateMultiSigRedeemScript`: collect **n** keys, then require `n >= m`.
fn parse_multisig(verification: &[u8]) -> Option<(usize, Vec<&[u8]>, usize)> {
    if verification.len() < 4 {
        return None;
    }
    let m = push_constant(verification[0])?;
    let mut position = 1usize;
    // Peek keys until we hit the `n` push + SYSCALL trailer. We do not yet know n, so walk
    // pubkey frames and stop when the remaining suffix matches `<push n> SYSCALL CheckMultisig`
    // with pubkey count == n.
    let mut pubkeys = Vec::new();
    while position + 5 < verification.len() {
        if verification.get(position) != Some(&OP_PUSHDATA1)
            || verification.get(position + 1) != Some(&33)
        {
            break;
        }
        let end = position + 2 + 33;
        pubkeys.push(verification.get(position + 2..end)?);
        position = end;
        if pubkeys.len() > MAX_MULTISIG_PARTIES {
            return None;
        }
    }
    let n = push_constant(*verification.get(position)?)?;
    position += 1;
    if verification.get(position) != Some(&OP_SYSCALL) {
        return None;
    }
    if verification.get(position + 1..position + 5) != Some(&CHECKMULTISIG_SYSCALL[..]) {
        return None;
    }
    if position + 5 != verification.len() || pubkeys.len() != n || n < m || m == 0 {
        return None;
    }
    Some((m, pubkeys, n))
}

/// The fork's PUSH1..PUSH16 opcodes (0x11..=0x20) decode to the constants 1..=16.
/// The stock-N3 range (0x51..=0x60) is deliberately NOT accepted — those bytes are
/// different instructions in the fork's opcode table.
fn push_constant(byte: u8) -> Option<usize> {
    match byte {
        0x11..=0x20 => Some(byte as usize - 0x10),
        _ => None,
    }
}

fn verify_signature(
    pubkey: &[u8],
    signature: &[u8],
    message_digest: &[u8],
) -> Result<bool, ExecutionError> {
    use p256::ecdsa::signature::hazmat::PrehashVerifier;
    use p256::ecdsa::{Signature, VerifyingKey};

    let key = VerifyingKey::from_sec1_bytes(pubkey)
        .map_err(|_| ExecutionError::Invalid("transaction witness public key"))?;
    let signature = Signature::from_slice(signature)
        .map_err(|_| ExecutionError::Invalid("transaction witness signature encoding"))?;
    Ok(key.verify_prehash(message_digest, &signature).is_ok())
}
