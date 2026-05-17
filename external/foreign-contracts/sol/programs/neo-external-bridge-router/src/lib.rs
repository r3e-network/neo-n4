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

use anchor_lang::prelude::*;
use anchor_lang::solana_program::{
    instruction::Instruction,
    sysvar::instructions::{load_current_index_checked, load_instruction_at_checked},
};

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
/// Pinned to the same offsets the Neo-side encoder uses.
const FIXED_PREFIX_LEN: usize = 102;
const NONCE_OFFSET: usize = 8;
const DIRECTION_OFFSET: usize = 16;
const MESSAGE_TYPE_OFFSET: usize = 81;

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
        require!(state.owner == ctx.accounts.owner.key(), BridgeError::NotOwner);
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
            ctx.accounts.system_program.to_account_info(),
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
        require!(
            direction == DIR_NEO_TO_FOREIGN,
            BridgeError::WrongDirection
        );

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
            &ctx.accounts.instructions_sysvar,
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
        require!(
            recipient_account.key().to_bytes()[..20] == recipient_b20[..],
            BridgeError::RecipientMismatch
        );
        // Vault → recipient. The vault PDA is owned by the program;
        // direct lamport-mutation works because we control the vault.
        let vault_lamports = ctx.accounts.vault.lamports();
        require!(
            vault_lamports >= amount_u64,
            BridgeError::InsufficientVault
        );
        **ctx.accounts.vault.try_borrow_mut_lamports()? -= amount_u64;
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
    pub vault: AccountInfo<'info>,

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
    pub vault: AccountInfo<'info>,

    /// CHECK: validated against the recipient bytes embedded in
    /// `message_bytes` inside the instruction handler.
    #[account(mut)]
    pub recipient: AccountInfo<'info>,

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
    #[account(address = anchor_lang::solana_program::sysvar::instructions::ID)]
    pub instructions_sysvar: AccountInfo<'info>,

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
    #[msg("malformed recipient bytes")]
    MalformedRecipient,
    #[msg("vault has insufficient lamports for this withdrawal")]
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

fn validate_committee(
    committee: &[[u8; ED25519_PUBKEY_LEN]],
    threshold: u8,
) -> Result<()> {
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
    u32::from_le_bytes(data[offset..offset + 4].try_into().unwrap())
}

fn read_u64_le(data: &[u8], offset: usize) -> u64 {
    u64::from_le_bytes(data[offset..offset + 8].try_into().unwrap())
}

fn read_uint_le(data: &[u8], offset: usize, length: usize) -> u128 {
    let mut v: u128 = 0;
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
    if data.len() < offset + 4 { 0 } else { read_u32_le(data, offset) }
}
fn read_u64_le_for_seeds(data: &[u8], offset: usize) -> u64 {
    if data.len() < offset + 8 { 0 } else { read_u64_le(data, offset) }
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
            ix.program_id == anchor_lang::solana_program::ed25519_program::ID,
            BridgeError::SigVerifyMismatch
        );
        // The data layout is documented in solana-program. For v0 we
        // do a structural check: extract the pubkey + message slices
        // and assert they match committee[idx] + expected_message.
        // Production code should parse the offsets header strictly;
        // this v0 trusts that runtime sigverify has validated the
        // crypto and we're just authenticating the bound (pubkey, msg).
        verify_sigverify_ix_matches(
            &ix.data,
            &committee[idx as usize],
            expected_message,
        )?;
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
    let pubkey_offset = u16::from_le_bytes([data[6], data[7]]) as usize;
    let message_data_offset = u16::from_le_bytes([data[10], data[11]]) as usize;
    let message_data_size = u16::from_le_bytes([data[12], data[13]]) as usize;
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
