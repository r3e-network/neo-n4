use crate::{hash160, types::{ExecutionError, ParsedTransaction}};

use alloc::vec::Vec;

use sha2::{Digest, Sha256};

// First 4 bytes of SHA256 of the interop service name — the SYSCALL operand the Neo VM uses to
// dispatch `System.Crypto.CheckSig` / `System.Crypto.CheckMultisig`.
const CHECKSIG_SYSCALL: [u8; 4] = [0x56, 0xe7, 0xb3, 0x27];
const CHECKMULTISIG_SYSCALL: [u8; 4] = [0x9e, 0xd0, 0xdc, 0x3a];

/// Neo N3 `OpCode.SYSCALL` (Neo2 used `0x68`).
const OP_SYSCALL: u8 = 0x41;

// Single-byte push opcodes 0x51..=0x60 push the constants 1..=16, capping the standard
// multi-signature shapes at 16 parties. Direct push opcodes 0x01..=0x4b push that many
// raw bytes (Neo `ScriptBuilder.EmitPush` for a 33-byte compressed pubkey emits `0x21`).
const MAX_MULTISIG_PARTIES: usize = 16;

/// Verify every witness of a parsed transaction against its signers.
///
/// Only the standard Neo N3 account forms are accepted: a single-signature witness
/// (`0x21 <pubkey> 0x41 <CheckSig>`) and an m-of-n multi-signature witness with single-byte push
/// counts (m, n ≤ 16). The signature message is the Neo sign data — `network(u32 LE) ‖ txHash`,
/// ECDSA/secp256r1 over `SHA256(message)` — matching `Crypto.VerifySignature(GetSignData(network))`
/// on the N3 core. `txHash` is the Neo inventory id (single SHA-256). The account binding
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
fn parse_single_sig(verification: &[u8]) -> Option<&[u8]> {
    if verification.len() == 39
        && verification[0] == 0x21
        && verification[34] == OP_SYSCALL
        && verification[35..39] == CHECKSIG_SYSCALL
    {
        return Some(&verification[1..34]);
    }
    None
}

/// `0x40 <64-byte compact signature>`
fn parse_single_signature(invocation: &[u8]) -> Option<&[u8]> {
    if invocation.len() == 65 && invocation[0] == 0x40 {
        return Some(&invocation[1..65]);
    }
    None
}

/// k×[`0x40 <64-byte signature>`] with 1 ≤ k ≤ 16
fn parse_signature_list(invocation: &[u8]) -> Option<Vec<&[u8]>> {
    if invocation.is_empty() || invocation.len() % 65 != 0 {
        return None;
    }
    let count = invocation.len() / 65;
    if count > MAX_MULTISIG_PARTIES {
        return None;
    }
    let mut signatures = Vec::with_capacity(count);
    for index in 0..count {
        let start = index * 65;
        if invocation[start] != 0x40 {
            return None;
        }
        signatures.push(&invocation[start + 1..start + 65]);
    }
    Some(signatures)
}

/// `<push m> n×[0x21 <33-byte pubkey>] <push n> 0x41 <4-byte CheckMultisig syscall>`
/// Matches Neo `CreateMultiSigRedeemScript`: collect **n** keys, then require `n >= m`.
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
        if verification.get(position) != Some(&0x21) {
            break;
        }
        let end = position + 34;
        pubkeys.push(verification.get(position + 1..end)?);
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

fn push_constant(byte: u8) -> Option<usize> {
    match byte {
        0x51..=0x60 => Some(byte as usize - 0x50),
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
