#![no_std]

extern crate alloc;

use alloc::{format, string::String, vec::Vec};
use core::fmt;
use sha2::{Digest, Sha256};

pub const REQUEST_MAGIC: &[u8; 8] = b"NEO4GWP1";
pub const BINDING_MAGIC: &[u8; 8] = b"NEO4GWR2";
pub const BINDING_BYTES: usize = 170;
pub const REQUEST_HEADER_BYTES: usize = 8 + BINDING_BYTES + 4;
pub const COMMITMENT_FIXED_BYTES: usize = 321;
pub const MAX_CONSTITUENTS: usize = 4096;
pub const MAX_REQUEST_BYTES: usize = 64 * 1024 * 1024;
pub const MAX_BATCH_PROOF_BYTES: usize = 1024 * 1024;
pub const MAX_CHILD_SIDECAR_BYTES: usize = 512 * 1024 * 1024;
pub const PUBLIC_INPUT_SUPPLEMENT_BYTES: usize = 64;
pub const CHILD_SIDECAR_HEADER_BYTES: usize = 8 + 4 + 8 + 32 + 32 + 32 + 4;
pub const CHILD_SIDECAR_MAGIC: &[u8; 8] = b"NEO4GCS1";
pub const CHILD_SIDECAR_SUFFIX: &str = ".sp1-recursive-child.bin";
pub const SP1_PROOF_SYSTEM: u8 = 1;
pub const RECURSIVE_BACKEND_ID: u8 = 0xc2;
pub const ZK_PROOF_TYPE: u8 = 3;

include!(concat!(env!("OUT_DIR"), "/batch_vk.rs"));

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum GatewayError {
    OversizedRequest,
    Truncated,
    TrailingBytes,
    RequestMagic,
    BindingMagic,
    ZeroBindingField,
    InvalidConstituentCount,
    BindingCountMismatch,
    InvalidBackend,
    InvalidProofSystem,
    GatewayVerificationKeyMismatch,
    InvalidCommitmentLength,
    InvalidBlockRange,
    InvalidProofType,
    EmptyBatchProof,
    InvalidBatchProofLength,
    ZeroPublicInputHash,
    NonCanonicalOrder,
    ConstituentRootMismatch,
    GlobalMessageRootMismatch,
    ChildPublicValuesCount,
    ChildPublicValuesMismatch,
    PublicInputSupplementCount,
    InvalidPublicInputSupplement,
    PublicInputHashMismatch,
    InvalidChildSidecar,
}

impl fmt::Display for GatewayError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(formatter, "{self:?}")
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct GatewayBinding {
    pub bytes: [u8; BINDING_BYTES],
    pub global_message_root: [u8; 32],
    pub constituent_root: [u8; 32],
    pub constituent_count: u32,
    pub aggregation_backend_id: u8,
    pub proof_system: u8,
    pub verification_key_id: [u8; 32],
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BatchCommitment {
    pub canonical_bytes: Vec<u8>,
    pub chain_id: u32,
    pub batch_number: u64,
    pub l2_to_l2_message_root: [u8; 32],
    pub public_input_hash: [u8; 32],
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct GatewayRequest {
    pub binding: GatewayBinding,
    pub constituents: Vec<BatchCommitment>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RecursiveChildSidecar {
    pub chain_id: u32,
    pub batch_number: u64,
    pub public_input_hash: [u8; 32],
    pub l1_message_hash: [u8; 32],
    pub block_context_hash: [u8; 32],
    pub proof_bytes: Vec<u8>,
}

pub fn parse_request(input: &[u8]) -> Result<GatewayRequest, GatewayError> {
    parse_request_with_gateway_vk(input, None)
}

pub fn parse_request_with_gateway_vk(
    input: &[u8],
    expected_gateway_vk: Option<&[u8; 32]>,
) -> Result<GatewayRequest, GatewayError> {
    if input.len() > MAX_REQUEST_BYTES {
        return Err(GatewayError::OversizedRequest);
    }
    if input.len() < REQUEST_HEADER_BYTES {
        return Err(GatewayError::Truncated);
    }
    if &input[..8] != REQUEST_MAGIC {
        return Err(GatewayError::RequestMagic);
    }

    let binding = parse_binding(&input[8..8 + BINDING_BYTES], expected_gateway_vk)?;
    let request_count = read_u32(input, 8 + BINDING_BYTES)? as usize;
    if request_count == 0 || request_count > MAX_CONSTITUENTS {
        return Err(GatewayError::InvalidConstituentCount);
    }
    if binding.constituent_count as usize != request_count {
        return Err(GatewayError::BindingCountMismatch);
    }

    let mut position = REQUEST_HEADER_BYTES;
    let mut constituents = Vec::with_capacity(request_count);
    for _ in 0..request_count {
        let encoded_len = read_u32(input, position)? as usize;
        position = position.checked_add(4).ok_or(GatewayError::Truncated)?;
        if !(COMMITMENT_FIXED_BYTES..=COMMITMENT_FIXED_BYTES + MAX_BATCH_PROOF_BYTES)
            .contains(&encoded_len)
        {
            return Err(GatewayError::InvalidCommitmentLength);
        }
        let end = position
            .checked_add(encoded_len)
            .ok_or(GatewayError::Truncated)?;
        let bytes = input.get(position..end).ok_or(GatewayError::Truncated)?;
        constituents.push(parse_commitment(bytes)?);
        position = end;
    }
    if position != input.len() {
        return Err(GatewayError::TrailingBytes);
    }

    validate_order(&constituents)?;
    let leaves = constituents
        .iter()
        .map(|batch| hash256(&batch.canonical_bytes))
        .collect::<Vec<_>>();
    if merkle_root_duplicate_odd(&leaves) != binding.constituent_root {
        return Err(GatewayError::ConstituentRootMismatch);
    }
    let message_roots = constituents
        .iter()
        .map(|batch| batch.l2_to_l2_message_root)
        .collect::<Vec<_>>();
    if merkle_root_promote_odd(&message_roots) != binding.global_message_root {
        return Err(GatewayError::GlobalMessageRootMismatch);
    }

    Ok(GatewayRequest {
        binding,
        constituents,
    })
}

pub fn expected_child_public_values(request: &GatewayRequest) -> Vec<[u8; 33]> {
    request
        .constituents
        .iter()
        .map(|batch| {
            let mut public_values = [0u8; 33];
            public_values[1..].copy_from_slice(&batch.public_input_hash);
            public_values
        })
        .collect()
}

pub fn validate_child_public_values(
    request: &GatewayRequest,
    public_values: &[Vec<u8>],
) -> Result<(), GatewayError> {
    if public_values.len() != request.constituents.len() {
        return Err(GatewayError::ChildPublicValuesCount);
    }
    for (actual, expected) in public_values
        .iter()
        .zip(expected_child_public_values(request).iter())
    {
        if actual.as_slice() != expected {
            return Err(GatewayError::ChildPublicValuesMismatch);
        }
    }
    Ok(())
}

pub fn validate_public_input_supplements(
    request: &GatewayRequest,
    supplements: &[Vec<u8>],
) -> Result<(), GatewayError> {
    if supplements.len() != request.constituents.len() {
        return Err(GatewayError::PublicInputSupplementCount);
    }
    for (batch, supplement) in request.constituents.iter().zip(supplements) {
        if supplement.len() != PUBLIC_INPUT_SUPPLEMENT_BYTES {
            return Err(GatewayError::InvalidPublicInputSupplement);
        }
        let mut public_inputs = [0u8; 332];
        public_inputs[..4].copy_from_slice(&batch.chain_id.to_le_bytes());
        public_inputs[4..12].copy_from_slice(&batch.batch_number.to_le_bytes());
        public_inputs[12..236].copy_from_slice(&batch.canonical_bytes[28..252]);
        public_inputs[236..268].copy_from_slice(&supplement[..32]);
        public_inputs[268..300].copy_from_slice(&batch.canonical_bytes[252..284]);
        public_inputs[300..332].copy_from_slice(&supplement[32..]);
        if hash256(&public_inputs) != batch.public_input_hash {
            return Err(GatewayError::PublicInputHashMismatch);
        }
    }
    Ok(())
}

#[must_use]
pub fn canonical_child_sidecar_filename(
    chain_id: u32,
    batch_number: u64,
    public_input_hash: &[u8; 32],
) -> String {
    format!(
        "{chain_id:08x}-{batch_number:016x}-{}{CHILD_SIDECAR_SUFFIX}",
        hex_lower(public_input_hash)
    )
}

pub fn encode_child_sidecar(
    chain_id: u32,
    batch_number: u64,
    public_input_hash: &[u8; 32],
    l1_message_hash: &[u8; 32],
    block_context_hash: &[u8; 32],
    proof_bytes: &[u8],
) -> Result<Vec<u8>, GatewayError> {
    if proof_bytes.is_empty()
        || proof_bytes.len() > MAX_CHILD_SIDECAR_BYTES - CHILD_SIDECAR_HEADER_BYTES
    {
        return Err(GatewayError::InvalidChildSidecar);
    }
    let proof_len =
        u32::try_from(proof_bytes.len()).map_err(|_| GatewayError::InvalidChildSidecar)?;
    let mut encoded = Vec::with_capacity(CHILD_SIDECAR_HEADER_BYTES + proof_bytes.len());
    encoded.extend_from_slice(CHILD_SIDECAR_MAGIC);
    encoded.extend_from_slice(&chain_id.to_le_bytes());
    encoded.extend_from_slice(&batch_number.to_le_bytes());
    encoded.extend_from_slice(public_input_hash);
    encoded.extend_from_slice(l1_message_hash);
    encoded.extend_from_slice(block_context_hash);
    encoded.extend_from_slice(&proof_len.to_le_bytes());
    encoded.extend_from_slice(proof_bytes);
    Ok(encoded)
}

pub fn parse_child_sidecar(input: &[u8]) -> Result<RecursiveChildSidecar, GatewayError> {
    if input.len() < CHILD_SIDECAR_HEADER_BYTES || input.len() > MAX_CHILD_SIDECAR_BYTES {
        return Err(GatewayError::InvalidChildSidecar);
    }
    if &input[..8] != CHILD_SIDECAR_MAGIC {
        return Err(GatewayError::InvalidChildSidecar);
    }
    let proof_len = read_u32(input, CHILD_SIDECAR_HEADER_BYTES - 4)? as usize;
    if proof_len == 0 || CHILD_SIDECAR_HEADER_BYTES + proof_len != input.len() {
        return Err(GatewayError::InvalidChildSidecar);
    }
    Ok(RecursiveChildSidecar {
        chain_id: read_u32(input, 8)?,
        batch_number: read_u64(input, 12)?,
        public_input_hash: input[20..52].try_into().unwrap(),
        l1_message_hash: input[52..84].try_into().unwrap(),
        block_context_hash: input[84..116].try_into().unwrap(),
        proof_bytes: input[CHILD_SIDECAR_HEADER_BYTES..].to_vec(),
    })
}

#[must_use]
pub fn gateway_public_values(binding: &GatewayBinding) -> [u8; 33] {
    let mut public_values = [0u8; 33];
    public_values[1..].copy_from_slice(&hash256(&binding.bytes));
    public_values
}

#[must_use]
pub fn hash256(input: &[u8]) -> [u8; 32] {
    let first = Sha256::digest(input);
    let second = Sha256::digest(first);
    second.into()
}

#[must_use]
pub fn merkle_root_duplicate_odd(leaves: &[[u8; 32]]) -> [u8; 32] {
    merkle_root(leaves, false)
}

#[must_use]
pub fn merkle_root_promote_odd(leaves: &[[u8; 32]]) -> [u8; 32] {
    merkle_root(leaves, true)
}

fn parse_binding(
    bytes: &[u8],
    expected_gateway_vk: Option<&[u8; 32]>,
) -> Result<GatewayBinding, GatewayError> {
    let bytes: [u8; BINDING_BYTES] = bytes.try_into().map_err(|_| GatewayError::Truncated)?;
    if &bytes[..8] != BINDING_MAGIC {
        return Err(GatewayError::BindingMagic);
    }
    for range in [8..28, 28..60, 68..100, 100..132, 138..170] {
        if bytes[range].iter().all(|byte| *byte == 0) {
            return Err(GatewayError::ZeroBindingField);
        }
    }
    let constituent_count = u32::from_le_bytes(bytes[132..136].try_into().unwrap());
    if constituent_count == 0 || constituent_count as usize > MAX_CONSTITUENTS {
        return Err(GatewayError::InvalidConstituentCount);
    }
    if bytes[136] != RECURSIVE_BACKEND_ID {
        return Err(GatewayError::InvalidBackend);
    }
    if bytes[137] != SP1_PROOF_SYSTEM {
        return Err(GatewayError::InvalidProofSystem);
    }
    let verification_key_id = bytes[138..170].try_into().unwrap();
    if expected_gateway_vk.is_some_and(|expected| expected != &verification_key_id) {
        return Err(GatewayError::GatewayVerificationKeyMismatch);
    }

    Ok(GatewayBinding {
        bytes,
        global_message_root: bytes[68..100].try_into().unwrap(),
        constituent_root: bytes[100..132].try_into().unwrap(),
        constituent_count,
        aggregation_backend_id: bytes[136],
        proof_system: bytes[137],
        verification_key_id,
    })
}

fn parse_commitment(bytes: &[u8]) -> Result<BatchCommitment, GatewayError> {
    if bytes.len() < COMMITMENT_FIXED_BYTES
        || bytes.len() > COMMITMENT_FIXED_BYTES + MAX_BATCH_PROOF_BYTES
    {
        return Err(GatewayError::InvalidCommitmentLength);
    }
    let chain_id = read_u32(bytes, 0)?;
    let batch_number = read_u64(bytes, 4)?;
    let first_block = read_u64(bytes, 12)?;
    let last_block = read_u64(bytes, 20)?;
    if last_block < first_block {
        return Err(GatewayError::InvalidBlockRange);
    }
    if bytes[316] != ZK_PROOF_TYPE {
        return Err(GatewayError::InvalidProofType);
    }
    let proof_len = i32::from_le_bytes(
        bytes[317..321]
            .try_into()
            .map_err(|_| GatewayError::Truncated)?,
    );
    if proof_len <= 0 {
        return Err(GatewayError::EmptyBatchProof);
    }
    let proof_len =
        usize::try_from(proof_len).map_err(|_| GatewayError::InvalidBatchProofLength)?;
    if proof_len > MAX_BATCH_PROOF_BYTES || COMMITMENT_FIXED_BYTES + proof_len != bytes.len() {
        return Err(GatewayError::InvalidBatchProofLength);
    }
    let public_input_hash = bytes[284..316].try_into().unwrap();
    if public_input_hash == [0u8; 32] {
        return Err(GatewayError::ZeroPublicInputHash);
    }

    Ok(BatchCommitment {
        canonical_bytes: bytes.to_vec(),
        chain_id,
        batch_number,
        l2_to_l2_message_root: bytes[220..252].try_into().unwrap(),
        public_input_hash,
    })
}

fn validate_order(constituents: &[BatchCommitment]) -> Result<(), GatewayError> {
    for pair in constituents.windows(2) {
        let left = (pair[0].chain_id, pair[0].batch_number);
        let right = (pair[1].chain_id, pair[1].batch_number);
        if left >= right {
            return Err(GatewayError::NonCanonicalOrder);
        }
    }
    Ok(())
}

fn merkle_root(leaves: &[[u8; 32]], promote_odd: bool) -> [u8; 32] {
    if leaves.is_empty() {
        return [0u8; 32];
    }
    let mut level = leaves.to_vec();
    while level.len() > 1 {
        let mut next = Vec::with_capacity(level.len().div_ceil(2));
        for pair in level.chunks(2) {
            match pair {
                [left, right] => next.push(hash_pair(left, right)),
                [left] if promote_odd => next.push(*left),
                [left] => next.push(hash_pair(left, left)),
                _ => unreachable!(),
            }
        }
        level = next;
    }
    level[0]
}

fn hash_pair(left: &[u8; 32], right: &[u8; 32]) -> [u8; 32] {
    let mut bytes = [0u8; 64];
    bytes[..32].copy_from_slice(left);
    bytes[32..].copy_from_slice(right);
    hash256(&bytes)
}

fn hex_lower(bytes: &[u8]) -> String {
    const HEX: &[u8; 16] = b"0123456789abcdef";
    let mut output = String::with_capacity(bytes.len() * 2);
    for byte in bytes {
        output.push(HEX[(byte >> 4) as usize] as char);
        output.push(HEX[(byte & 0x0f) as usize] as char);
    }
    output
}

fn read_u32(bytes: &[u8], position: usize) -> Result<u32, GatewayError> {
    let end = position.checked_add(4).ok_or(GatewayError::Truncated)?;
    Ok(u32::from_le_bytes(
        bytes
            .get(position..end)
            .ok_or(GatewayError::Truncated)?
            .try_into()
            .unwrap(),
    ))
}

fn read_u64(bytes: &[u8], position: usize) -> Result<u64, GatewayError> {
    let end = position.checked_add(8).ok_or(GatewayError::Truncated)?;
    Ok(u64::from_le_bytes(
        bytes
            .get(position..end)
            .ok_or(GatewayError::Truncated)?
            .try_into()
            .unwrap(),
    ))
}

#[cfg(test)]
mod tests {
    use super::*;
    use alloc::vec;

    fn commitment(chain_id: u32, batch_number: u64, message: u8, public_input: u8) -> Vec<u8> {
        let mut bytes = vec![0u8; COMMITMENT_FIXED_BYTES + 4];
        bytes[0..4].copy_from_slice(&chain_id.to_le_bytes());
        bytes[4..12].copy_from_slice(&batch_number.to_le_bytes());
        bytes[12..20].copy_from_slice(&(batch_number * 10).to_le_bytes());
        bytes[20..28].copy_from_slice(&(batch_number * 10 + 9).to_le_bytes());
        for root in 0..9 {
            bytes[28 + root * 32..28 + (root + 1) * 32].fill((root + 1) as u8);
        }
        bytes[220..252].fill(message);
        bytes[284..316].fill(public_input);
        bytes[316] = ZK_PROOF_TYPE;
        bytes[317..321].copy_from_slice(&4i32.to_le_bytes());
        bytes[321..].copy_from_slice(&[1, 2, 3, 4]);
        bytes
    }

    fn request(commitments: &[Vec<u8>]) -> Vec<u8> {
        let leaves = commitments
            .iter()
            .map(|value| hash256(value))
            .collect::<Vec<_>>();
        let messages = commitments
            .iter()
            .map(|value| value[220..252].try_into().unwrap())
            .collect::<Vec<[u8; 32]>>();
        let mut binding = [0u8; BINDING_BYTES];
        binding[..8].copy_from_slice(BINDING_MAGIC);
        binding[8..28].fill(0x11);
        binding[28..60].fill(0x22);
        binding[60..68].copy_from_slice(&7u64.to_le_bytes());
        binding[68..100].copy_from_slice(&merkle_root_promote_odd(&messages));
        binding[100..132].copy_from_slice(&merkle_root_duplicate_odd(&leaves));
        binding[132..136].copy_from_slice(&(commitments.len() as u32).to_le_bytes());
        binding[136] = RECURSIVE_BACKEND_ID;
        binding[137] = SP1_PROOF_SYSTEM;
        binding[138..170].fill(0x33);

        let mut request = Vec::new();
        request.extend_from_slice(REQUEST_MAGIC);
        request.extend_from_slice(&binding);
        request.extend_from_slice(&(commitments.len() as u32).to_le_bytes());
        for commitment in commitments {
            request.extend_from_slice(&(commitment.len() as u32).to_le_bytes());
            request.extend_from_slice(commitment);
        }
        request
    }

    #[test]
    fn parses_strict_canonical_request() {
        let input = request(&[commitment(1, 2, 0x41, 0x51), commitment(2, 1, 0x42, 0x52)]);
        let parsed = parse_request(&input).unwrap();
        assert_eq!(parsed.constituents.len(), 2);
        assert_eq!(parsed.constituents[0].chain_id, 1);
        assert_eq!(parsed.constituents[0].batch_number, 2);
        assert_eq!(parsed.constituents[0].public_input_hash, [0x51; 32]);
    }

    #[test]
    fn rejects_magic_length_and_trailing_tamper() {
        let valid = request(&[commitment(1, 1, 0x41, 0x51)]);
        let mut bad_magic = valid.clone();
        bad_magic[0] ^= 1;
        assert_eq!(parse_request(&bad_magic), Err(GatewayError::RequestMagic));
        assert_eq!(
            parse_request(&valid[..valid.len() - 1]),
            Err(GatewayError::Truncated)
        );
        let mut trailing = valid;
        trailing.push(0);
        assert_eq!(parse_request(&trailing), Err(GatewayError::TrailingBytes));
    }

    #[test]
    fn rejects_non_zk_empty_and_noncanonical_proofs() {
        let mut non_zk = commitment(1, 1, 0x41, 0x51);
        non_zk[316] = 2;
        assert_eq!(
            parse_request(&request(&[non_zk])),
            Err(GatewayError::InvalidProofType)
        );

        let mut empty = commitment(1, 1, 0x41, 0x51);
        empty.truncate(COMMITMENT_FIXED_BYTES);
        empty[317..321].copy_from_slice(&0i32.to_le_bytes());
        assert_eq!(
            parse_request(&request(&[empty])),
            Err(GatewayError::EmptyBatchProof)
        );

        let mut wrong_length = commitment(1, 1, 0x41, 0x51);
        wrong_length[317..321].copy_from_slice(&3i32.to_le_bytes());
        assert_eq!(
            parse_request(&request(&[wrong_length])),
            Err(GatewayError::InvalidBatchProofLength)
        );
    }

    #[test]
    fn rejects_noncanonical_batch_order() {
        let descending = request(&[commitment(2, 1, 0x41, 0x51), commitment(1, 2, 0x42, 0x52)]);
        assert_eq!(
            parse_request(&descending),
            Err(GatewayError::NonCanonicalOrder)
        );

        let duplicate = request(&[commitment(1, 1, 0x41, 0x51), commitment(1, 1, 0x42, 0x52)]);
        assert_eq!(
            parse_request(&duplicate),
            Err(GatewayError::NonCanonicalOrder)
        );
    }

    #[test]
    fn rejects_constituent_and_message_root_tamper() {
        let valid = request(&[
            commitment(1, 1, 0x41, 0x51),
            commitment(2, 1, 0x42, 0x52),
            commitment(3, 1, 0x43, 0x53),
        ]);
        let mut constituent = valid.clone();
        constituent[8 + 100] ^= 1;
        assert_eq!(
            parse_request(&constituent),
            Err(GatewayError::ConstituentRootMismatch)
        );
        let mut global = valid;
        global[8 + 68] ^= 1;
        assert_eq!(
            parse_request(&global),
            Err(GatewayError::GlobalMessageRootMismatch)
        );
    }

    #[test]
    fn odd_leaf_rules_match_dotnet_protocol() {
        let leaves = [[1u8; 32], [2u8; 32], [3u8; 32]];
        let duplicated = hash_pair(
            &hash_pair(&leaves[0], &leaves[1]),
            &hash_pair(&leaves[2], &leaves[2]),
        );
        let promoted = hash_pair(&hash_pair(&leaves[0], &leaves[1]), &leaves[2]);
        assert_eq!(merkle_root_duplicate_odd(&leaves), duplicated);
        assert_eq!(merkle_root_promote_odd(&leaves), promoted);
        assert_ne!(duplicated, promoted);
    }

    #[test]
    fn child_public_values_are_exact_status_and_public_input_hash() {
        let parsed = parse_request(&request(&[commitment(1, 1, 0x41, 0x51)])).unwrap();
        let expected = vec![[[0u8; 1].as_slice(), &[0x51; 32]].concat()];
        validate_child_public_values(&parsed, &expected).unwrap();
        let mut tampered = expected;
        tampered[0][0] = 1;
        assert_eq!(
            validate_child_public_values(&parsed, &tampered),
            Err(GatewayError::ChildPublicValuesMismatch)
        );
    }

    #[test]
    fn supplements_bind_every_commitment_public_input_field() {
        let mut encoded = commitment(1, 7, 0x41, 0x00);
        let mut supplement = vec![0x91; PUBLIC_INPUT_SUPPLEMENT_BYTES];
        supplement[32..].fill(0xa1);
        let mut public_inputs = [0u8; 332];
        public_inputs[..4].copy_from_slice(&1u32.to_le_bytes());
        public_inputs[4..12].copy_from_slice(&7u64.to_le_bytes());
        public_inputs[12..236].copy_from_slice(&encoded[28..252]);
        public_inputs[236..268].copy_from_slice(&supplement[..32]);
        public_inputs[268..300].copy_from_slice(&encoded[252..284]);
        public_inputs[300..].copy_from_slice(&supplement[32..]);
        encoded[284..316].copy_from_slice(&hash256(&public_inputs));

        let parsed = parse_request(&request(&[encoded.clone()])).unwrap();
        validate_public_input_supplements(&parsed, &[supplement.clone()]).unwrap();

        let mut tampered_supplement = supplement;
        tampered_supplement[0] ^= 1;
        assert_eq!(
            validate_public_input_supplements(&parsed, &[tampered_supplement]),
            Err(GatewayError::PublicInputHashMismatch)
        );

        encoded[220] ^= 1;
        let tampered_commitment = parse_request(&request(&[encoded])).unwrap();
        let mut correct_supplement = vec![0x91; PUBLIC_INPUT_SUPPLEMENT_BYTES];
        correct_supplement[32..].fill(0xa1);
        assert_eq!(
            validate_public_input_supplements(&tampered_commitment, &[correct_supplement]),
            Err(GatewayError::PublicInputHashMismatch)
        );
    }

    #[test]
    fn child_sidecar_round_trip_is_strict_and_tuple_bound() {
        let encoded =
            encode_child_sidecar(7, 42, &[0x51; 32], &[0x61; 32], &[0x71; 32], &[1, 2, 3, 4])
                .unwrap();
        let decoded = parse_child_sidecar(&encoded).unwrap();
        assert_eq!(decoded.chain_id, 7);
        assert_eq!(decoded.batch_number, 42);
        assert_eq!(decoded.public_input_hash, [0x51; 32]);
        assert_eq!(decoded.l1_message_hash, [0x61; 32]);
        assert_eq!(decoded.block_context_hash, [0x71; 32]);
        assert_eq!(decoded.proof_bytes, [1, 2, 3, 4]);
        assert_eq!(
            canonical_child_sidecar_filename(7, 42, &[0xab; 32]),
            format!(
                "00000007-000000000000002a-{}{}",
                "ab".repeat(32),
                CHILD_SIDECAR_SUFFIX
            )
        );

        let mut trailing = encoded.clone();
        trailing.push(0);
        assert_eq!(
            parse_child_sidecar(&trailing),
            Err(GatewayError::InvalidChildSidecar)
        );
        let mut bad_magic = encoded;
        bad_magic[0] ^= 1;
        assert_eq!(
            parse_child_sidecar(&bad_magic),
            Err(GatewayError::InvalidChildSidecar)
        );
    }

    #[test]
    fn gateway_public_values_are_status_plus_hash256_binding() {
        let parsed = parse_request(&request(&[commitment(1, 1, 0x41, 0x51)])).unwrap();
        let values = gateway_public_values(&parsed.binding);
        assert_eq!(values[0], 0);
        assert_eq!(values[1..], hash256(&parsed.binding.bytes));
    }

    #[test]
    fn host_can_lock_exact_gateway_vk() {
        let input = request(&[commitment(1, 1, 0x41, 0x51)]);
        let expected = [0x33; 32];
        parse_request_with_gateway_vk(&input, Some(&expected)).unwrap();
        assert_eq!(
            parse_request_with_gateway_vk(&input, Some(&[0x34; 32])),
            Err(GatewayError::GatewayVerificationKeyMismatch)
        );
    }

    #[test]
    fn compile_time_batch_vk_is_nonzero_and_test_only_is_explicit() {
        assert!(BATCH_VK_WORDS.iter().any(|word| *word != 0));
        assert_eq!(
            core::hint::black_box(BATCH_VK_IS_TEST_ONLY),
            cfg!(feature = "test-only-vk")
        );
    }
}
