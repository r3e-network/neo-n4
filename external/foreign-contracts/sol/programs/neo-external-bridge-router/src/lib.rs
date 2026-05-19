//! Solana-side router for the Neo Elastic Network cross-foreign-chain
//! bridge.
//!
//! Mirrors `external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol`
//! semantically, with two structural differences forced by Solana:
//!
//! 1. **State lives in PDAs**, not contract storage. We use one
//!    `BridgeState` PDA holding the committee + threshold + outbound
//!    nonce counter, plus a per-`(neo_chain_id, nonce)` `ConsumedNonce`
//!    PDA acting as the "already-finalized" replay guard.
//! 2. **ed25519 verification uses Solana's sigverify precompile**, not
//!    in-program. The watcher submits a transaction containing
//!    `ed25519_program::ID` instructions BEFORE the bridge instruction;
//!    `finalize_withdrawal` reads the `Sysvar<Instructions>` to confirm
//!    the precompile ran and matches the message+pubkey it expects.
//!    This is the canonical Solana pattern (Wormhole / Neon / etc. do
//!    the same) — saves ~30k CU per signature vs in-program ed25519.
//!
//! Per `doc.md` §11.3.4 + `docs/external-bridge-roadmap.md` § Phase 4,
//! Solana stays MPC-committee-only because Tower BFT light-client
//! verification is genuinely expensive on-chain. This program implements
//! the committee model.
//!
//! ## Build status
//!
//! Source-only in this iteration. The `anchor` toolchain (and Solana
//! validator runtime) is heavy; operators run `anchor build` + `anchor
//! test` against `solana-test-validator` to compile + exercise. This
//! source has been written carefully but should be reviewed by a Solana
//! developer before mainnet deploy.

#![allow(unexpected_cfgs)]
// Anchor's `#[program]` macro currently expands to code that trips this
// Clippy lint under Rust 1.95; keep the allowance at crate scope until the
// generated code no longer needs it.
#![allow(clippy::diverging_sub_expression)]

use anchor_lang::prelude::*;
use anchor_lang::solana_program::instruction::Instruction;
use solana_instructions_sysvar::{load_current_index_checked, load_instruction_at_checked};

declare_id!("34B8qwavepu4eY3KiCwNeLL5kJNu3aZJcSb1xv48s7eu");

/// 0xE0_xx_xx_xx foreign-namespace prefix any `external_chain_id`
/// passed in must carry. Mirrors C# / Rust off-chain validators.
const FOREIGN_NAMESPACE_PREFIX: u32 = 0xE000_0000;
const FOREIGN_NAMESPACE_MASK: u32 = 0xFF00_0000;

/// Hard cap on committee size — defensive ceiling matching the Neo-side
/// `MpcCommitteeVerifier.MaxCommitteeSize`.
const MAX_COMMITTEE_SIZE: usize = 64;

/// ed25519 pubkey size on Solana (and in the canonical encoding the
/// Neo-side `MpcCommitteeVerifier` stores for `curveTag = 2`).
const ED25519_PUBKEY_LEN: usize = 32;

/// Wire-format constants for the canonical `ExternalCrossChainMessage`.
/// Pinned to the same offsets the Neo-side encoder uses. Cumulative fixed-prefix
/// offsets, in order: ecid 0, ncid 4, nonce 8, direction 16, sender 17,
/// recipient 37, deadline 57, sourceTxRef 65, messageType 97, payloadLen 98,
/// payload 102 → fixed prefix = 102 bytes.
const FIXED_PREFIX_LEN: usize = 102;
const NONCE_OFFSET: usize = 8;
const DIRECTION_OFFSET: usize = 16;
const MESSAGE_TYPE_OFFSET: usize = 97;

const DIR_NEO_TO_FOREIGN: u8 = 1;
const MSG_TYPE_ASSET_TRANSFER: u8 = 0;

#[program]
pub mod neo_external_bridge_router {
    use super::*;

    /// One-time program init. Owner sets up the bridge state PDA with
    /// the watcher committee. `external_chain_id` must use the
    /// `0xE0_xx_xx_xx` namespace (Solana mainnet = 0xE0000020).
    pub fn initialize(
        ctx: Context<Initialize>,
        external_chain_id: u32,
        threshold: u8,
        committee: Vec<[u8; ED25519_PUBKEY_LEN]>,
    ) -> Result<()> {
        require!(
            external_chain_id & FOREIGN_NAMESPACE_MASK == FOREIGN_NAMESPACE_PREFIX,
            BridgeError::BadNamespace
        );
        validate_committee(&committee, threshold)?;

        let state = &mut ctx.accounts.bridge_state;
        state.owner = ctx.accounts.owner.key();
        state.external_chain_id = external_chain_id;
        state.threshold = threshold;
        state.committee = committee;
        state.outbound_nonce_counter = 0;
        state.bump = ctx.bumps.bridge_state;
        Ok(())
    }

    /// Owner-only: rotate the committee (e.g., MPC → Optimistic → ZK
    /// upgrade path on the Neo side gets a matching off-chain change).
    pub fn set_committee(
        ctx: Context<SetCommittee>,
        threshold: u8,
        committee: Vec<[u8; ED25519_PUBKEY_LEN]>,
    ) -> Result<()> {
        let state = &mut ctx.accounts.bridge_state;
        require!(
            state.owner == ctx.accounts.owner.key(),
            BridgeError::NotOwner
        );
        validate_committee(&committee, threshold)?;
        state.committee = committee;
        state.threshold = threshold;
        Ok(())
    }

    /// Lock SOL bound for a Neo chain. Caller transfers `amount`
    /// lamports into the program's vault PDA; we record the outbound
    /// in an event log the watcher subscribes to.
    pub fn lock_sol_and_send(
        ctx: Context<LockSolAndSend>,
        neo_chain_id: u32,
        neo_recipient: [u8; 20],
        amount: u64,
        payload: Vec<u8>,
        deadline: u64,
    ) -> Result<()> {
        require!(amount > 0, BridgeError::ZeroAmount);
        require!(neo_recipient != [0u8; 20], BridgeError::ZeroRecipient);

        // Transfer lamports caller → vault.
        let cpi_ctx = CpiContext::new(
            ctx.accounts.system_program.key(),
            anchor_lang::system_program::Transfer {
                from: ctx.accounts.sender.to_account_info(),
                to: ctx.accounts.vault.to_account_info(),
            },
        );
        anchor_lang::system_program::transfer(cpi_ctx, amount)?;

        // Allocate the next outbound nonce. Stored as u64; counter
        // starts at 0 so first message is nonce = 1.
        let state = &mut ctx.accounts.bridge_state;
        state.outbound_nonce_counter = state
            .outbound_nonce_counter
            .checked_add(1)
            .ok_or(BridgeError::NonceOverflow)?;
        let nonce = state.outbound_nonce_counter;

        emit!(LockedEvent {
            external_chain_id: state.external_chain_id,
            neo_chain_id,
            nonce,
            sender: ctx.accounts.sender.key(),
            neo_recipient,
            asset: Pubkey::default(), // native SOL
            amount,
            payload,
            deadline,
        });
        Ok(())
    }

    /// Finalize a Neo → Solana withdrawal. Verifies the committee
    /// quorum signed `messageBytes` via the ed25519 sigverify
    /// precompile + message-hash check, then transfers SOL from the
    /// vault to the recipient encoded inside `messageBytes`.
    pub fn finalize_withdrawal(
        ctx: Context<FinalizeWithdrawal>,
        message_bytes: Vec<u8>,
        signer_indices: Vec<u8>,
    ) -> Result<()> {
        let state = &ctx.accounts.bridge_state;
        require!(
            message_bytes.len() >= FIXED_PREFIX_LEN,
            BridgeError::MessageTooShort
        );

        // Parse canonical layout.
        let msg_external_chain_id = read_u32_le(&message_bytes, 0);
        require!(
            msg_external_chain_id == state.external_chain_id,
            BridgeError::ChainIdMismatch
        );
        let src_neo_chain_id = read_u32_le(&message_bytes, 4);
        let nonce = read_u64_le(&message_bytes, NONCE_OFFSET);
        let direction = message_bytes[DIRECTION_OFFSET];
        require!(direction == DIR_NEO_TO_FOREIGN, BridgeError::WrongDirection);

        // Replay protection: the consumed_nonce account is a PDA
        // derived from `(src_neo_chain_id, nonce)`. Anchor's `init`
        // constraint will fail if it already exists, giving us O(1)
        // replay rejection without an extra storage read.
        let _consumed = &ctx.accounts.consumed_nonce; // Anchor ensured init.

        // Recipient at offset 37 (20B) — read the Solana pubkey from
        // bytes 37..69. This is a 32-byte read, so we use offsets
        // 37..69 (extending 12 bytes into the deadline+sourceTxRef
        // region intentionally — Solana addresses are 32-byte, but
        // the canonical wire format reserves 20B per address). For v0
        // we restrict Solana recipients to the 20-byte-aligned form
        // by zero-padding the upper 12 bytes, and the watcher must
        // honor that; richer 32-byte recipients arrive in v1.
        let recipient_b20: [u8; 20] = message_bytes[37..57]
            .try_into()
            .map_err(|_| BridgeError::MalformedRecipient)?;
        // Deadline at offset 57 (8B LE).
        let deadline = read_u64_le(&message_bytes, 57);
        if deadline != 0 {
            let now = Clock::get()?.unix_timestamp as u64;
            require!(now <= deadline, BridgeError::PastDeadline);
        }
        let message_type = message_bytes[MESSAGE_TYPE_OFFSET];
        require!(
            message_type == MSG_TYPE_ASSET_TRANSFER,
            BridgeError::MessageTypeUnsupported
        );

        // Verify quorum via Solana's ed25519 sigverify precompile —
        // the watcher submitted N preceding instructions invoking the
        // precompile with `(committee[idx].pubkey, sig, message_bytes)`
        // tuples. We walk those instructions to confirm:
        //   1. count >= threshold
        //   2. each invokes the ed25519 program
        //   3. each pubkey is `committee[indices[i]]`
        //   4. each message matches our `message_bytes`
        //   5. distinct signer indices (no duplicates)
        verify_ed25519_quorum_precompile(
            ctx.accounts.instructions_sysvar.as_ref(),
            &state.committee,
            state.threshold,
            &message_bytes,
            &signer_indices,
        )?;

        // Read amount from asset-transfer payload at offset 102+:
        //   [20B foreignAsset (we ignore — SOL only)]
        //   [4B amountLength LE]
        //   [amountLength × amount LE]
        let payload_len = read_u32_le(&message_bytes, 98) as usize;
        require!(
            message_bytes.len() == FIXED_PREFIX_LEN + payload_len,
            BridgeError::PayloadLengthMismatch
        );
        require!(payload_len >= 24, BridgeError::AssetPayloadTooShort);
        let amount_len = read_u32_le(&message_bytes, FIXED_PREFIX_LEN + 20) as usize;
        require!(
            amount_len > 0 && amount_len <= 32,
            BridgeError::AmountLenOutOfBounds
        );
        require!(
            payload_len == 24 + amount_len,
            BridgeError::PayloadLengthMismatch
        );
        let amount = read_uint_le(&message_bytes, FIXED_PREFIX_LEN + 24, amount_len);
        require!(amount <= u64::MAX as u128, BridgeError::AmountOverflowU64);
        let amount_u64 = amount as u64;

        // Transfer SOL from vault to recipient.
        let recipient_account = &ctx.accounts.recipient;
        // Enforce the wire-format invariant that v0 Solana recipients zero-pad
        // the upper 12 bytes of the 32B address (the canonical message format
        // truncates to 20B). Without this check, an attacker who controls the
        // tx submission can bind any of ~2^96 "near-collision" 32-byte pubkeys
        // to the same 20-byte attested recipient — receiving funds intended
        // for someone else.
        let recipient_bytes = recipient_account.key().to_bytes();
        require!(
            recipient_bytes[..20] == recipient_b20[..],
            BridgeError::RecipientMismatch
        );
        require!(
            recipient_bytes[20..] == [0u8; 12],
            BridgeError::RecipientNotSolanaCanonical
        );
        // Vault → recipient. The vault PDA is owned by the program; direct
        // lamport-mutation works because we control the vault. Preserve the
        // vault's rent-exempt minimum so the runtime can't garbage-collect the
        // vault account out from under us (which would drop all bookkeeping
        // state with it).
        let vault_account_info = ctx.accounts.vault.to_account_info();
        let vault_data_len = vault_account_info.data_len();
        let rent_min = Rent::get()?.minimum_balance(vault_data_len);
        let vault_lamports = vault_account_info.lamports();
        let withdrawable = vault_lamports.saturating_sub(rent_min);
        require!(amount_u64 <= withdrawable, BridgeError::InsufficientVault);
        **vault_account_info.try_borrow_mut_lamports()? -= amount_u64;
        **recipient_account.try_borrow_mut_lamports()? += amount_u64;

        emit!(WithdrawalFinalizedEvent {
            neo_chain_id: src_neo_chain_id,
            nonce,
            recipient: recipient_account.key(),
            amount: amount_u64,
        });
        Ok(())
    }
}

// ─── account contexts ────────────────────────────────────────────────

#[derive(Accounts)]
#[instruction(external_chain_id: u32, threshold: u8, committee: Vec<[u8; ED25519_PUBKEY_LEN]>)]
pub struct Initialize<'info> {
    #[account(mut)]
    pub owner: Signer<'info>,

    #[account(
        init,
        payer = owner,
        space = BridgeState::space(MAX_COMMITTEE_SIZE),
        seeds = [b"bridge_state"],
        bump,
    )]
    pub bridge_state: Account<'info, BridgeState>,

    pub system_program: Program<'info, System>,
}

#[derive(Accounts)]
pub struct SetCommittee<'info> {
    pub owner: Signer<'info>,

    #[account(mut, seeds = [b"bridge_state"], bump = bridge_state.bump)]
    pub bridge_state: Account<'info, BridgeState>,
}

#[derive(Accounts)]
pub struct LockSolAndSend<'info> {
    #[account(mut)]
    pub sender: Signer<'info>,

    #[account(mut, seeds = [b"bridge_state"], bump = bridge_state.bump)]
    pub bridge_state: Account<'info, BridgeState>,

    /// SOL vault PDA — receives lamports from `sender`.
    /// CHECK: lamport-only PDA owned by this program; no data layout.
    #[account(mut, seeds = [b"vault"], bump)]
    pub vault: UncheckedAccount<'info>,

    pub system_program: Program<'info, System>,
}

#[derive(Accounts)]
#[instruction(message_bytes: Vec<u8>, signer_indices: Vec<u8>)]
pub struct FinalizeWithdrawal<'info> {
    #[account(mut)]
    pub payer: Signer<'info>,

    #[account(seeds = [b"bridge_state"], bump = bridge_state.bump)]
    pub bridge_state: Account<'info, BridgeState>,

    /// CHECK: lamport-only PDA owned by this program.
    #[account(mut, seeds = [b"vault"], bump)]
    pub vault: UncheckedAccount<'info>,

    /// CHECK: validated against the recipient bytes embedded in
    /// `message_bytes` inside the instruction handler.
    #[account(mut)]
    pub recipient: UncheckedAccount<'info>,

    /// Replay-protection PDA. `init` here forces creation; if a
    /// previous `finalize_withdrawal` already created it for the same
    /// (chain_id, nonce), this fails with "account already in use".
    #[account(
        init,
        payer = payer,
        space = ConsumedNonce::SPACE,
        seeds = [
            b"consumed".as_ref(),
            read_u32_le_for_seeds(&message_bytes, 4).to_le_bytes().as_ref(),
            read_u64_le_for_seeds(&message_bytes, NONCE_OFFSET).to_le_bytes().as_ref(),
        ],
        bump,
    )]
    pub consumed_nonce: Account<'info, ConsumedNonce>,

    /// Sysvar reading the current transaction's instructions —
    /// required to inspect the ed25519 sigverify precompile calls
    /// that precede this instruction.
    /// CHECK: this is the well-known Instructions sysvar address.
    #[account(address = solana_instructions_sysvar::ID)]
    pub instructions_sysvar: UncheckedAccount<'info>,

    pub system_program: Program<'info, System>,
}

// ─── account data ────────────────────────────────────────────────────

#[account]
pub struct BridgeState {
    pub owner: Pubkey,
    pub external_chain_id: u32,
    pub threshold: u8,
    pub committee: Vec<[u8; ED25519_PUBKEY_LEN]>,
    pub outbound_nonce_counter: u64,
    pub bump: u8,
}

impl BridgeState {
    pub fn space(max_committee: usize) -> usize {
        // Anchor account discriminator (8) + Pubkey (32) + u32 (4) +
        // u8 (1) + Vec header (4) + max_committee × 32 + u64 (8) + u8 (1)
        8 + 32 + 4 + 1 + 4 + max_committee * ED25519_PUBKEY_LEN + 8 + 1
    }
}

#[account]
pub struct ConsumedNonce {
    pub created_at_slot: u64,
}

impl ConsumedNonce {
    pub const SPACE: usize = 8 + 8;
}

// ─── events ──────────────────────────────────────────────────────────

#[event]
pub struct LockedEvent {
    pub external_chain_id: u32,
    pub neo_chain_id: u32,
    pub nonce: u64,
    pub sender: Pubkey,
    pub neo_recipient: [u8; 20],
    pub asset: Pubkey,
    pub amount: u64,
    pub payload: Vec<u8>,
    pub deadline: u64,
}

#[event]
pub struct WithdrawalFinalizedEvent {
    pub neo_chain_id: u32,
    pub nonce: u64,
    pub recipient: Pubkey,
    pub amount: u64,
}

// ─── errors ──────────────────────────────────────────────────────────

#[error_code]
pub enum BridgeError {
    #[msg("externalChainId must use the 0xE0_xx_xx_xx foreign-namespace prefix")]
    BadNamespace,
    #[msg("threshold must be in [1, committee.len()]")]
    BadThreshold,
    #[msg("committee.len() must be in [1, MAX_COMMITTEE_SIZE]")]
    BadCommitteeSize,
    #[msg("committee contains a duplicate pubkey")]
    DuplicateCommitteeMember,
    #[msg("not the owner")]
    NotOwner,
    #[msg("amount must be positive")]
    ZeroAmount,
    #[msg("recipient must be non-zero")]
    ZeroRecipient,
    #[msg("nonce counter overflowed u64")]
    NonceOverflow,
    #[msg("messageBytes shorter than the 102-byte fixed prefix")]
    MessageTooShort,
    #[msg("message externalChainId doesn't match this router's externalChainId")]
    ChainIdMismatch,
    #[msg("direction must be NeoToForeign (1) for inbound finalization")]
    WrongDirection,
    #[msg("messageType not supported in v0 (only AssetTransfer = 0)")]
    MessageTypeUnsupported,
    #[msg("payload length doesn't match declared")]
    PayloadLengthMismatch,
    #[msg("asset-transfer payload shorter than 24-byte header")]
    AssetPayloadTooShort,
    #[msg("amountLen out of bounds [1, 32]")]
    AmountLenOutOfBounds,
    #[msg("amount overflows u64 (Solana lamports)")]
    AmountOverflowU64,
    #[msg("recipient account doesn't match recipient bytes in messageBytes")]
    RecipientMismatch,
    #[msg("recipient pubkey is not in canonical Solana form (upper 12 bytes must be zero in v0)")]
    RecipientNotSolanaCanonical,
    #[msg("malformed recipient bytes")]
    MalformedRecipient,
    #[msg("vault has insufficient lamports for this withdrawal (preserving rent-exempt minimum)")]
    InsufficientVault,
    #[msg("message past deadline")]
    PastDeadline,
    #[msg("ed25519 sigverify precompile not invoked / mismatched")]
    SigVerifyMismatch,
    #[msg("signature count below threshold")]
    BelowThreshold,
    #[msg("signer indices contain a duplicate")]
    DuplicateSignerIdx,
    #[msg("signerIdx >= committee size")]
    SignerIdxOutOfRange,
}

// ─── helpers ─────────────────────────────────────────────────────────

fn validate_committee(committee: &[[u8; ED25519_PUBKEY_LEN]], threshold: u8) -> Result<()> {
    require!(
        !committee.is_empty() && committee.len() <= MAX_COMMITTEE_SIZE,
        BridgeError::BadCommitteeSize
    );
    require!(
        threshold > 0 && (threshold as usize) <= committee.len(),
        BridgeError::BadThreshold
    );
    // Reject duplicates (O(n²) acceptable for committees ≤ 64).
    for i in 0..committee.len() {
        for j in (i + 1)..committee.len() {
            require!(
                committee[i] != committee[j],
                BridgeError::DuplicateCommitteeMember
            );
        }
    }
    Ok(())
}

fn read_u32_le(data: &[u8], offset: usize) -> u32 {
    let mut bytes = [0u8; 4];
    if let Some(slice) = data.get(offset..offset.saturating_add(bytes.len())) {
        bytes.copy_from_slice(slice);
    }
    u32::from_le_bytes(bytes)
}

fn read_u64_le(data: &[u8], offset: usize) -> u64 {
    let mut bytes = [0u8; 8];
    if let Some(slice) = data.get(offset..offset.saturating_add(bytes.len())) {
        bytes.copy_from_slice(slice);
    }
    u64::from_le_bytes(bytes)
}

fn read_uint_le(data: &[u8], offset: usize, length: usize) -> u128 {
    let mut v: u128 = 0;
    if data.len() < offset.saturating_add(length) {
        return v;
    }
    for i in 0..length {
        v |= (data[offset + i] as u128) << (8 * i);
    }
    v
}

// Helper variants used inside Anchor's #[account(seeds = ...)] expansion.
// The macro evaluates seeds at handler-entry, before the Vec body has
// been validated for length, so guard against under-length with a
// fallback rather than panicking inside the seed expression.
fn read_u32_le_for_seeds(data: &[u8], offset: usize) -> u32 {
    if data.len() < offset + 4 {
        0
    } else {
        read_u32_le(data, offset)
    }
}
fn read_u64_le_for_seeds(data: &[u8], offset: usize) -> u64 {
    if data.len() < offset + 8 {
        0
    } else {
        read_u64_le(data, offset)
    }
}

/// Walk the current transaction's pre-instructions to confirm a quorum
/// of committee members invoked Solana's ed25519 sigverify precompile
/// with the right `(pubkey, message)` tuples.
///
/// Solana's sigverify program (`Ed25519SigVerify111111111111111111111111111`)
/// accepts an instruction whose data encodes `n` signature offsets +
/// pubkey offsets + message offsets. The precompile validates each tuple
/// or the entire transaction reverts at the runtime layer — so by the
/// time this program runs, we know the cryptographic check already
/// passed for whatever was in those instructions. Our job is to confirm
/// what was checked matches what we expect.
fn verify_ed25519_quorum_precompile(
    instructions_sysvar: &AccountInfo,
    committee: &[[u8; ED25519_PUBKEY_LEN]],
    threshold: u8,
    expected_message: &[u8],
    signer_indices: &[u8],
) -> Result<()> {
    require!(
        signer_indices.len() >= threshold as usize,
        BridgeError::BelowThreshold
    );

    // No duplicate indices (bitmap dedup mirroring the on-chain
    // MpcCommitteeVerifier's check).
    let mut seen: u64 = 0;
    for &idx in signer_indices {
        require!(
            (idx as usize) < committee.len(),
            BridgeError::SignerIdxOutOfRange
        );
        let bit = 1u64 << idx;
        require!(seen & bit == 0, BridgeError::DuplicateSignerIdx);
        seen |= bit;
    }

    // Walk the preceding instructions: each ed25519 sigverify call
    // must match a committee[indices[i]] + expected_message tuple.
    // Solana ordering: the watcher submits one Ed25519SigVerify
    // instruction per signer, in the same order as `signer_indices`,
    // immediately before the bridge instruction.
    let bridge_ix_index = load_current_index_checked(instructions_sysvar)? as usize;
    require!(
        bridge_ix_index >= signer_indices.len(),
        BridgeError::SigVerifyMismatch
    );
    for (i, &idx) in signer_indices.iter().enumerate() {
        let ix_pos = bridge_ix_index - signer_indices.len() + i;
        let ix: Instruction = load_instruction_at_checked(ix_pos, instructions_sysvar)?;
        // Sanity: program id must be the ed25519 sigverify program.
        require!(
            ix.program_id == solana_sdk_ids::ed25519_program::ID,
            BridgeError::SigVerifyMismatch
        );
        // The data layout is documented in solana-program. We require the
        // signature, pubkey, and message offsets to point into the same
        // ed25519 instruction whose data we inspect, then compare the
        // pubkey + message slices against the committee and canonical bytes.
        verify_sigverify_ix_matches(&ix.data, &committee[idx as usize], expected_message)?;
    }
    Ok(())
}

/// Parse a Solana ed25519 sigverify instruction's data and confirm the
/// `pubkey` + `message` portions match what we expect.
///
/// Layout (per Solana docs):
///   [0]    num_signatures (u8) — must be 1 for our use
///   [1]    padding (u8)
///   [2..4] sig_offset (u16 LE)
///   [4..6] sig_instruction_index (u16 LE) — 0xFFFF = same instruction
///   [6..8] pubkey_offset (u16 LE)
///   [8..10] pubkey_instruction_index (u16 LE)
///   [10..12] message_data_offset (u16 LE)
///   [12..14] message_data_size (u16 LE)
///   [14..16] message_instruction_index (u16 LE)
///   ...sig + pubkey + message bytes follow at the offsets above
fn verify_sigverify_ix_matches(
    data: &[u8],
    expected_pubkey: &[u8; ED25519_PUBKEY_LEN],
    expected_message: &[u8],
) -> Result<()> {
    require!(data.len() >= 16, BridgeError::SigVerifyMismatch);
    require!(data[0] == 1, BridgeError::SigVerifyMismatch); // exactly 1 signature
    let sig_offset = u16::from_le_bytes([data[2], data[3]]) as usize;
    let sig_instruction_index = u16::from_le_bytes([data[4], data[5]]);
    let pubkey_offset = u16::from_le_bytes([data[6], data[7]]) as usize;
    let pubkey_instruction_index = u16::from_le_bytes([data[8], data[9]]);
    let message_data_offset = u16::from_le_bytes([data[10], data[11]]) as usize;
    let message_data_size = u16::from_le_bytes([data[12], data[13]]) as usize;
    let message_instruction_index = u16::from_le_bytes([data[14], data[15]]);

    // The Solana ed25519 program can verify signature/pubkey/message bytes
    // loaded from another instruction when these indices are not u16::MAX.
    // The bridge's parser compares slices in this instruction, so require the
    // precompile to have verified the same in-instruction tuple we inspect.
    require!(
        sig_instruction_index == u16::MAX
            && pubkey_instruction_index == u16::MAX
            && message_instruction_index == u16::MAX,
        BridgeError::SigVerifyMismatch
    );
    require!(
        sig_offset + 64 <= data.len(),
        BridgeError::SigVerifyMismatch
    );
    require!(
        pubkey_offset + ED25519_PUBKEY_LEN <= data.len(),
        BridgeError::SigVerifyMismatch
    );
    require!(
        message_data_offset + message_data_size <= data.len(),
        BridgeError::SigVerifyMismatch
    );
    let pubkey_in_ix = &data[pubkey_offset..pubkey_offset + ED25519_PUBKEY_LEN];
    require!(
        pubkey_in_ix == &expected_pubkey[..],
        BridgeError::SigVerifyMismatch
    );
    let message_in_ix = &data[message_data_offset..message_data_offset + message_data_size];
    require!(
        message_in_ix == expected_message,
        BridgeError::SigVerifyMismatch
    );
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn sigverify_ix_data(
        pubkey: &[u8; ED25519_PUBKEY_LEN],
        message: &[u8],
        sig_instruction_index: u16,
        pubkey_instruction_index: u16,
        message_instruction_index: u16,
    ) -> Vec<u8> {
        let sig_offset = 16u16;
        let pubkey_offset = sig_offset + 64;
        let message_offset = pubkey_offset + ED25519_PUBKEY_LEN as u16;
        let mut data = Vec::with_capacity(message_offset as usize + message.len());
        data.push(1); // num_signatures
        data.push(0); // padding
        data.extend_from_slice(&sig_offset.to_le_bytes());
        data.extend_from_slice(&sig_instruction_index.to_le_bytes());
        data.extend_from_slice(&pubkey_offset.to_le_bytes());
        data.extend_from_slice(&pubkey_instruction_index.to_le_bytes());
        data.extend_from_slice(&message_offset.to_le_bytes());
        data.extend_from_slice(&(message.len() as u16).to_le_bytes());
        data.extend_from_slice(&message_instruction_index.to_le_bytes());
        data.extend_from_slice(&[0xAA; 64]);
        data.extend_from_slice(pubkey);
        data.extend_from_slice(message);
        data
    }

    #[test]
    fn sigverify_parser_accepts_same_instruction_offsets() {
        let pubkey = [0x42u8; ED25519_PUBKEY_LEN];
        let message = b"canonical message";
        let data = sigverify_ix_data(&pubkey, message, u16::MAX, u16::MAX, u16::MAX);

        assert!(verify_sigverify_ix_matches(&data, &pubkey, message).is_ok());
    }

    #[test]
    fn sigverify_parser_rejects_cross_instruction_message_offset() {
        let pubkey = [0x42u8; ED25519_PUBKEY_LEN];
        let message = b"canonical message";
        let data = sigverify_ix_data(&pubkey, message, u16::MAX, u16::MAX, 0);

        assert!(verify_sigverify_ix_matches(&data, &pubkey, message).is_err());
    }

    #[test]
    fn sigverify_parser_rejects_cross_instruction_pubkey_offset() {
        let pubkey = [0x42u8; ED25519_PUBKEY_LEN];
        let message = b"canonical message";
        let data = sigverify_ix_data(&pubkey, message, u16::MAX, 0, u16::MAX);

        assert!(verify_sigverify_ix_matches(&data, &pubkey, message).is_err());
    }

    // ── validate_committee ───────────────────────────────────────────

    #[test]
    fn validate_committee_accepts_well_formed() {
        let members: Vec<[u8; ED25519_PUBKEY_LEN]> =
            (0..3).map(|i| [i as u8; ED25519_PUBKEY_LEN]).collect();
        assert!(validate_committee(&members, 2).is_ok());
    }

    #[test]
    fn validate_committee_rejects_empty() {
        let members: Vec<[u8; ED25519_PUBKEY_LEN]> = vec![];
        assert!(validate_committee(&members, 1).is_err());
    }

    #[test]
    fn validate_committee_rejects_too_large() {
        let members: Vec<[u8; ED25519_PUBKEY_LEN]> = (0..(MAX_COMMITTEE_SIZE + 1))
            .map(|i| [(i & 0xFF) as u8; ED25519_PUBKEY_LEN])
            .collect();
        assert!(validate_committee(&members, 1).is_err());
    }

    #[test]
    fn validate_committee_rejects_zero_threshold() {
        let members: Vec<[u8; ED25519_PUBKEY_LEN]> =
            (0..3).map(|i| [i as u8; ED25519_PUBKEY_LEN]).collect();
        assert!(validate_committee(&members, 0).is_err());
    }

    #[test]
    fn validate_committee_rejects_threshold_above_size() {
        let members: Vec<[u8; ED25519_PUBKEY_LEN]> =
            (0..3).map(|i| [i as u8; ED25519_PUBKEY_LEN]).collect();
        assert!(validate_committee(&members, 4).is_err());
    }

    #[test]
    fn validate_committee_rejects_duplicate_member() {
        let pk = [0x42u8; ED25519_PUBKEY_LEN];
        let members = vec![pk, [0x43u8; ED25519_PUBKEY_LEN], pk];
        assert!(validate_committee(&members, 2).is_err());
    }

    #[test]
    fn validate_committee_accepts_unanimity_threshold() {
        let members: Vec<[u8; ED25519_PUBKEY_LEN]> =
            (0..5).map(|i| [i as u8; ED25519_PUBKEY_LEN]).collect();
        assert!(validate_committee(&members, 5).is_ok());
    }

    #[test]
    fn validate_committee_accepts_max_size() {
        let members: Vec<[u8; ED25519_PUBKEY_LEN]> = (0..MAX_COMMITTEE_SIZE)
            .map(|i| [(i & 0xFF) as u8; ED25519_PUBKEY_LEN])
            .collect();
        assert!(validate_committee(&members, MAX_COMMITTEE_SIZE as u8).is_ok());
    }

    // ── little-endian readers ────────────────────────────────────────

    #[test]
    fn read_u32_le_happy_path() {
        let data = [0x78u8, 0x56, 0x34, 0x12, 0xAA, 0xBB];
        assert_eq!(read_u32_le(&data, 0), 0x12345678);
        assert_eq!(read_u32_le(&data, 2), 0xBBAA1234);
    }

    #[test]
    fn read_u32_le_returns_zero_on_underflow() {
        let data = [0x78u8, 0x56];
        assert_eq!(read_u32_le(&data, 0), 0);
        assert_eq!(read_u32_le(&data, 10), 0);
    }

    #[test]
    fn read_u64_le_happy_path() {
        let data = [0x88u8, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11];
        assert_eq!(read_u64_le(&data, 0), 0x1122334455667788);
    }

    #[test]
    fn read_u64_le_returns_zero_on_underflow() {
        let data = [0u8; 7];
        assert_eq!(read_u64_le(&data, 0), 0);
    }

    #[test]
    fn read_uint_le_variable_length() {
        let data = [0x11u8, 0x22, 0x33, 0x44, 0x55, 0x66];
        // 1-byte read = 0x11
        assert_eq!(read_uint_le(&data, 0, 1), 0x11);
        // 3-byte LE at offset 1 = 0x44 33 22 = 0x443322
        assert_eq!(read_uint_le(&data, 1, 3), 0x443322);
        // Reading 8 bytes from a 6-byte buffer → out-of-bounds → 0
        assert_eq!(read_uint_le(&data, 0, 8), 0);
    }

    #[test]
    fn read_for_seeds_under_length_returns_zero() {
        // Anchor's #[account(seeds = ...)] evaluates seeds BEFORE the handler's
        // own length checks run. The for_seeds variants guard against that with
        // a fallback so the seed-derivation doesn't panic on a too-short input.
        let too_short = [0u8; 3];
        assert_eq!(read_u32_le_for_seeds(&too_short, 0), 0);
        let still_too_short = [0u8; 7];
        assert_eq!(read_u64_le_for_seeds(&still_too_short, 0), 0);
    }

    // ── canonical message layout regression (MESSAGE_TYPE_OFFSET = 97) ──

    #[test]
    fn canonical_message_offsets_are_pinned() {
        // Layout per Neo.L2.Messaging.ExternalMessageHasher:
        //   4 ecid + 4 ncid + 8 nonce + 1 dir + 20 sender + 20 recipient
        //   + 8 deadline + 32 sourceTxRef + 1 messageType + 4 payloadLen = 102
        assert_eq!(FIXED_PREFIX_LEN, 102, "fixed prefix size baked into the encoder");
        assert_eq!(NONCE_OFFSET, 8, "nonce starts at byte 8");
        assert_eq!(DIRECTION_OFFSET, 16, "direction at byte 16");
        // CRITICAL: messageType at byte 97, NOT byte 81. A buggy 81 lands in
        // the middle of sourceTxRef (which production watchers always populate
        // with a real Neo tx hash), causing silent mis-dispatch ~255/256 of
        // the time. Pin the correct offset so future refactors can't regress.
        assert_eq!(MESSAGE_TYPE_OFFSET, 97, "messageType at byte 97 (after sourceTxRef)");
    }

    #[test]
    fn canonical_message_layout_round_trips() {
        // Construct a 102-byte fixed prefix and confirm every reader pulls out
        // the right field. This pins the encoder/decoder agreement.
        let mut msg = [0u8; 102];
        // ecid = 0xE0000001 (Eth mainnet slot)
        msg[0] = 0x01; msg[1] = 0x00; msg[2] = 0x00; msg[3] = 0xE0;
        // ncid = 1099 (sample L2)
        msg[4] = 0x4B; msg[5] = 0x04; msg[6] = 0x00; msg[7] = 0x00;
        // nonce = 0xCAFEBABE
        for (i, b) in 0xCAFEBABEu64.to_le_bytes().iter().enumerate() { msg[8 + i] = *b; }
        // direction = 1 (NeoToForeign)
        msg[16] = 1;
        // sender bytes 17..37 — left as zero
        // recipient bytes 37..57 — left as zero
        // deadline = 0
        // sourceTxRef bytes 65..97 — non-zero so a buggy MESSAGE_TYPE_OFFSET = 81
        // would read garbage. Use 0x11..0x30 to make the bug visible.
        for i in 0..32 { msg[65 + i] = (0x11 + i) as u8; }
        // messageType = MSG_TYPE_ASSET_TRANSFER (0) at byte 97
        msg[97] = 0;
        // payloadLen = 0
        // (bytes 98..102 already zero)

        assert_eq!(read_u32_le(&msg, 0), 0xE0000001, "ecid round-trips");
        assert_eq!(read_u32_le(&msg, 4), 1099, "ncid round-trips");
        assert_eq!(read_u64_le(&msg, NONCE_OFFSET), 0xCAFEBABE, "nonce round-trips");
        assert_eq!(msg[DIRECTION_OFFSET], 1, "direction round-trips");
        assert_eq!(msg[MESSAGE_TYPE_OFFSET], 0, "messageType read from byte 97");
        // Sanity: byte 81 (the OLD buggy offset) is INSIDE sourceTxRef and non-zero
        // — proves the regression test actually catches the bug if the offset slides.
        assert_ne!(msg[81], msg[MESSAGE_TYPE_OFFSET],
            "byte 81 must differ from messageType so a regressed offset would surface");
    }

    // ── direction / message-type constants ───────────────────────────

    #[test]
    fn direction_constants_disjoint() {
        assert_eq!(DIR_NEO_TO_FOREIGN, 1, "Neo→Foreign direction tag");
        // The opposing direction (Foreign→Neo = 2) is enforced off-chain;
        // pin the Solana side's expected value.
    }

    #[test]
    fn message_type_constants() {
        assert_eq!(MSG_TYPE_ASSET_TRANSFER, 0, "asset-transfer is the v0 default");
    }
}
