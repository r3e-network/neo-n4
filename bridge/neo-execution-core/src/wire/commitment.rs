use alloc::vec::Vec;

use crate::{
    hashing::hash256,
    types::{ExecutionError, L2BatchCommitment, L2ChainConfig, MerkleProof, ProofType, UInt256},
};

use super::{
    constants::{
        CHAIN_CONFIG_SIZE, COMMITMENT_FIXED_SIZE, MAX_MERKLE_DEPTH, MAX_PROOF_BYTES,
        MERKLE_PROOF_HEADER_SIZE,
    },
    io::Reader,
};

pub fn encode_commitment(commitment: &L2BatchCommitment) -> Result<Vec<u8>, ExecutionError> {
    if commitment.proof.len() > MAX_PROOF_BYTES {
        return Err(ExecutionError::Oversized("proof"));
    }
    let proof_len = commitment.proof.len();
    let total_len = COMMITMENT_FIXED_SIZE + proof_len;
    let mut bytes = Vec::with_capacity(total_len);
    bytes.extend_from_slice(&commitment.chain_id.to_le_bytes());
    bytes.extend_from_slice(&commitment.batch_number.to_le_bytes());
    bytes.extend_from_slice(&commitment.first_block.to_le_bytes());
    bytes.extend_from_slice(&commitment.last_block.to_le_bytes());
    bytes.extend_from_slice(&commitment.pre_state_root);
    bytes.extend_from_slice(&commitment.post_state_root);
    bytes.extend_from_slice(&commitment.tx_root);
    bytes.extend_from_slice(&commitment.receipt_root);
    bytes.extend_from_slice(&commitment.withdrawal_root);
    bytes.extend_from_slice(&commitment.l2_to_l1_message_root);
    bytes.extend_from_slice(&commitment.l2_to_l2_message_root);
    bytes.extend_from_slice(&commitment.da_commitment);
    bytes.extend_from_slice(&commitment.public_input_hash);
    bytes.push(commitment.proof_type as u8);
    bytes.extend_from_slice(&(proof_len as i32).to_le_bytes());
    bytes.extend_from_slice(&commitment.proof);
    Ok(bytes)
}

pub fn parse_commitment(bytes: &[u8]) -> Result<L2BatchCommitment, ExecutionError> {
    if bytes.len() < COMMITMENT_FIXED_SIZE {
        return Err(ExecutionError::Truncated);
    }
    let mut reader = Reader::new(bytes);
    let chain_id = reader.read_u32()?;
    let batch_number = reader.read_u64()?;
    let first_block = reader.read_u64()?;
    let last_block = reader.read_u64()?;
    if last_block < first_block {
        return Err(ExecutionError::Invalid("last_block < first_block in commitment"));
    }
    let pre_state_root = reader.read_fixed::<32>()?;
    let post_state_root = reader.read_fixed::<32>()?;
    let tx_root = reader.read_fixed::<32>()?;
    let receipt_root = reader.read_fixed::<32>()?;
    let withdrawal_root = reader.read_fixed::<32>()?;
    let l2_to_l1_message_root = reader.read_fixed::<32>()?;
    let l2_to_l2_message_root = reader.read_fixed::<32>()?;
    let da_commitment = reader.read_fixed::<32>()?;
    let public_input_hash = reader.read_fixed::<32>()?;
    let proof_type = ProofType::try_from(reader.read_u8()?)?;
    let proof_len_raw = reader.read_i32()?;
    if proof_len_raw < 0 {
        return Err(ExecutionError::Invalid("negative proof length"));
    }
    let proof_len = proof_len_raw as usize;
    if proof_len > MAX_PROOF_BYTES {
        return Err(ExecutionError::Oversized("proof"));
    }
    if bytes.len() != COMMITMENT_FIXED_SIZE + proof_len {
        return Err(ExecutionError::Invalid("commitment length mismatch"));
    }
    let proof = reader.read_bytes(proof_len)?.to_vec();
    reader.ensure_end("commitment")?;

    Ok(L2BatchCommitment {
        chain_id,
        batch_number,
        first_block,
        last_block,
        pre_state_root,
        post_state_root,
        tx_root,
        receipt_root,
        withdrawal_root,
        l2_to_l1_message_root,
        l2_to_l2_message_root,
        da_commitment,
        public_input_hash,
        proof_type,
        proof,
    })
}

pub fn encode_chain_config(config: &L2ChainConfig) -> [u8; CHAIN_CONFIG_SIZE] {
    let mut bytes = [0u8; CHAIN_CONFIG_SIZE];
    bytes[0..4].copy_from_slice(&config.chain_id.to_le_bytes());
    bytes[4..24].copy_from_slice(&config.operator_manager);
    bytes[24..44].copy_from_slice(&config.verifier);
    bytes[44..64].copy_from_slice(&config.bridge_adapter);
    bytes[64..84].copy_from_slice(&config.message_adapter);
    bytes[84] = config.security_level;
    bytes[85] = config.da_mode;
    bytes[86] = u8::from(config.gateway_enabled);
    bytes[87] = u8::from(config.permissionless_exit);
    bytes[88] = config.sequencer_model;
    bytes[89] = config.exit_model;
    bytes[90] = u8::from(config.active);
    bytes
}

pub fn parse_chain_config(bytes: &[u8]) -> Result<L2ChainConfig, ExecutionError> {
    if bytes.len() != CHAIN_CONFIG_SIZE {
        return Err(ExecutionError::Invalid("chain config length must be 91 bytes"));
    }
    let chain_id = u32::from_le_bytes(
        bytes[0..4]
            .try_into()
            .map_err(|_| ExecutionError::Invalid("chain_id"))?,
    );
    let mut operator_manager = [0u8; 20];
    operator_manager.copy_from_slice(&bytes[4..24]);
    let mut verifier = [0u8; 20];
    verifier.copy_from_slice(&bytes[24..44]);
    let mut bridge_adapter = [0u8; 20];
    bridge_adapter.copy_from_slice(&bytes[44..64]);
    let mut message_adapter = [0u8; 20];
    message_adapter.copy_from_slice(&bytes[64..84]);
    let security_level = bytes[84];
    let da_mode = bytes[85];
    let gateway_enabled = match bytes[86] {
        0 => false,
        1 => true,
        _ => return Err(ExecutionError::Invalid("gateway_enabled boolean")),
    };
    let permissionless_exit = match bytes[87] {
        0 => false,
        1 => true,
        _ => return Err(ExecutionError::Invalid("permissionless_exit boolean")),
    };
    let sequencer_model = bytes[88];
    let exit_model = bytes[89];
    let active = match bytes[90] {
        0 => false,
        1 => true,
        _ => return Err(ExecutionError::Invalid("active boolean")),
    };

    Ok(L2ChainConfig {
        chain_id,
        operator_manager,
        verifier,
        bridge_adapter,
        message_adapter,
        security_level,
        da_mode,
        gateway_enabled,
        permissionless_exit,
        sequencer_model,
        exit_model,
        active,
    })
}

pub fn encode_merkle_proof(proof: &MerkleProof) -> Result<Vec<u8>, ExecutionError> {
    if proof.siblings.len() > MAX_MERKLE_DEPTH {
        return Err(ExecutionError::Oversized("merkle proof depth exceeds 64"));
    }
    if proof.leaf_index > i32::MAX as u32 {
        return Err(ExecutionError::Invalid("leaf_index exceeds signed int positive range"));
    }
    let total_len = MERKLE_PROOF_HEADER_SIZE + 32 * proof.siblings.len();
    let mut bytes = Vec::with_capacity(total_len);
    bytes.extend_from_slice(&proof.leaf);
    bytes.extend_from_slice(&proof.leaf_index.to_le_bytes());
    bytes.extend_from_slice(&proof.path_bitmap.to_le_bytes());
    bytes.extend_from_slice(&(proof.siblings.len() as u32).to_le_bytes());
    for sibling in &proof.siblings {
        bytes.extend_from_slice(sibling);
    }
    Ok(bytes)
}

pub fn parse_merkle_proof(bytes: &[u8]) -> Result<MerkleProof, ExecutionError> {
    if bytes.len() < MERKLE_PROOF_HEADER_SIZE {
        return Err(ExecutionError::Truncated);
    }
    let mut reader = Reader::new(bytes);
    let leaf = reader.read_fixed::<32>()?;
    let leaf_index = reader.read_u32()?;
    if leaf_index > i32::MAX as u32 {
        return Err(ExecutionError::Invalid("leaf_index exceeds signed int positive range"));
    }
    let path_bitmap = reader.read_u64()?;
    let sibling_count = reader.read_u32()? as usize;
    if sibling_count > MAX_MERKLE_DEPTH {
        return Err(ExecutionError::Oversized("merkle proof depth exceeds 64"));
    }
    let expected_len = MERKLE_PROOF_HEADER_SIZE + 32 * sibling_count;
    if bytes.len() != expected_len {
        return Err(ExecutionError::Invalid("merkle proof length mismatch"));
    }
    let mut siblings = Vec::with_capacity(sibling_count);
    for _ in 0..sibling_count {
        siblings.push(reader.read_fixed::<32>()?);
    }
    reader.ensure_end("merkle proof")?;

    Ok(MerkleProof {
        leaf,
        leaf_index,
        path_bitmap,
        siblings,
    })
}

#[must_use]
pub fn verify_merkle_proof(proof: &MerkleProof, expected_root: &UInt256) -> bool {
    if proof.siblings.len() > MAX_MERKLE_DEPTH {
        return false;
    }
    let mut expected_bitmap = 0u64;
    let mut residual = proof.leaf_index as u64;
    for d in 0..proof.siblings.len() {
        if (residual & 1) != 0 {
            expected_bitmap |= 1 << d;
        }
        residual >>= 1;
    }
    if residual != 0 || proof.path_bitmap != expected_bitmap {
        return false;
    }
    let mut current = proof.leaf;
    for (d, sibling) in proof.siblings.iter().enumerate() {
        let sibling_is_left = ((proof.path_bitmap >> d) & 1) == 1;
        let mut buf = [0u8; 64];
        if sibling_is_left {
            buf[..32].copy_from_slice(sibling);
            buf[32..].copy_from_slice(&current);
        } else {
            buf[..32].copy_from_slice(&current);
            buf[32..].copy_from_slice(sibling);
        }
        current = hash256(&buf);
    }
    &current == expected_root
}

#[cfg(test)]
mod tests {
    use super::*;
    use alloc::vec;
    use crate::types::ExecutionError;

    fn sample_commitment(proof: Option<Vec<u8>>) -> L2BatchCommitment {
        L2BatchCommitment {
            chain_id: 0xCAFEBABE,
            batch_number: 0xDEAD_BEEF_F00D_BABE,
            first_block: 100,
            last_block: 200,
            pre_state_root: [1u8; 32],
            post_state_root: [2u8; 32],
            tx_root: [3u8; 32],
            receipt_root: [4u8; 32],
            withdrawal_root: [5u8; 32],
            l2_to_l1_message_root: [6u8; 32],
            l2_to_l2_message_root: [7u8; 32],
            da_commitment: [8u8; 32],
            public_input_hash: [9u8; 32],
            proof_type: ProofType::Zk,
            proof: proof.unwrap_or_else(|| vec![0xAA, 0xBB, 0xCC]),
        }
    }

    #[test]
    fn commitment_round_trips() {
        let original = sample_commitment(None);
        let bytes = encode_commitment(&original).expect("encode succeeds");
        assert_eq!(bytes.len(), COMMITMENT_FIXED_SIZE + 3);
        let decoded = parse_commitment(&bytes).expect("parse succeeds");
        assert_eq!(original, decoded);
    }

    #[test]
    fn commitment_round_trips_empty_proof() {
        let original = sample_commitment(Some(vec![]));
        let bytes = encode_commitment(&original).expect("encode succeeds");
        assert_eq!(bytes.len(), COMMITMENT_FIXED_SIZE);
        let decoded = parse_commitment(&bytes).expect("parse succeeds");
        assert_eq!(original, decoded);
    }

    #[test]
    fn commitment_layout_matches_spec() {
        let original = sample_commitment(Some(vec![0xFF]));
        let bytes = encode_commitment(&original).expect("encode succeeds");

        // chain_id at 0..4 LE
        assert_eq!(&bytes[0..4], &0xCAFEBABEu32.to_le_bytes());
        // batch_number at 4..12 LE
        assert_eq!(&bytes[4..12], &0xDEAD_BEEF_F00D_BABE_u64.to_le_bytes());
        // first_block at 12..20 LE
        assert_eq!(&bytes[12..20], &100u64.to_le_bytes());
        // last_block at 20..28 LE
        assert_eq!(&bytes[20..28], &200u64.to_le_bytes());
        // pre_state_root at 28..60
        assert_eq!(&bytes[28..60], &[1u8; 32]);
        // proof_type at 316
        assert_eq!(bytes[316], ProofType::Zk as u8);
        // proof_len at 317..321 LE
        assert_eq!(&bytes[317..321], &1i32.to_le_bytes());
        // proof byte at 321
        assert_eq!(bytes[321], 0xFF);
    }

    #[test]
    fn commitment_rejects_inverted_blocks() {
        let mut original = sample_commitment(None);
        original.first_block = 200;
        original.last_block = 100;
        let bytes = encode_commitment(&original).expect("encode succeeds");
        let err = parse_commitment(&bytes).expect_err("inverted blocks must fail");
        assert!(matches!(err, ExecutionError::Invalid("last_block < first_block in commitment")));
    }

    #[test]
    fn commitment_rejects_invalid_proof_type() {
        let mut bytes = encode_commitment(&sample_commitment(None)).unwrap();
        bytes[316] = 5; // invalid ProofType
        let err = parse_commitment(&bytes).expect_err("invalid proof type must fail");
        assert!(matches!(err, ExecutionError::Invalid("proof type")));
    }

    fn sample_chain_config() -> L2ChainConfig {
        L2ChainConfig {
            chain_id: 0x12345678,
            operator_manager: [0x11; 20],
            verifier: [0x22; 20],
            bridge_adapter: [0x33; 20],
            message_adapter: [0x44; 20],
            security_level: 2, // Optimistic
            da_mode: 1,        // NeoFS
            gateway_enabled: true,
            permissionless_exit: true,
            sequencer_model: 1, // DbftCommittee
            exit_model: 2,      // Permissionless
            active: true,
        }
    }

    #[test]
    fn chain_config_round_trips() {
        let original = sample_chain_config();
        let bytes = encode_chain_config(&original);
        assert_eq!(bytes.len(), CHAIN_CONFIG_SIZE);
        let decoded = parse_chain_config(&bytes).expect("parse succeeds");
        assert_eq!(original, decoded);
    }

    #[test]
    fn chain_config_layout_matches_spec() {
        let original = sample_chain_config();
        let bytes = encode_chain_config(&original);

        assert_eq!(&bytes[0..4], &[0x78, 0x56, 0x34, 0x12]);
        assert_eq!(&bytes[4..24], &[0x11; 20]);
        assert_eq!(&bytes[24..44], &[0x22; 20]);
        assert_eq!(&bytes[44..64], &[0x33; 20]);
        assert_eq!(&bytes[64..84], &[0x44; 20]);
        assert_eq!(bytes[84], 2);
        assert_eq!(bytes[85], 1);
        assert_eq!(bytes[86], 1);
        assert_eq!(bytes[87], 1);
        assert_eq!(bytes[88], 1);
        assert_eq!(bytes[89], 2);
        assert_eq!(bytes[90], 1);
    }

    #[test]
    fn chain_config_rejects_invalid_boolean() {
        let mut bytes = encode_chain_config(&sample_chain_config());
        bytes[86] = 2; // invalid boolean
        let err = parse_chain_config(&bytes).expect_err("non-bool must fail");
        assert!(matches!(err, ExecutionError::Invalid("gateway_enabled boolean")));
    }

    #[test]
    fn merkle_proof_round_trip_and_verify() {
        // Build 2 leaves: leaf0 and leaf1
        let mut leaf0 = [0u8; 32];
        leaf0[0] = 1;
        let mut leaf1 = [0u8; 32];
        leaf1[0] = 2;

        let mut pair = [0u8; 64];
        pair[..32].copy_from_slice(&leaf0);
        pair[32..].copy_from_slice(&leaf1);
        let root = hash256(&pair);

        // Proof for leaf0 at index 0 (sibling leaf1 is to the right, pathBitmap bit 0 is 0)
        let proof = MerkleProof {
            leaf: leaf0,
            leaf_index: 0,
            path_bitmap: 0,
            siblings: vec![leaf1],
        };

        let bytes = encode_merkle_proof(&proof).expect("encode succeeds");
        assert_eq!(bytes.len(), MERKLE_PROOF_HEADER_SIZE + 32);
        let decoded = parse_merkle_proof(&bytes).expect("parse succeeds");
        assert_eq!(proof, decoded);

        assert!(verify_merkle_proof(&decoded, &root));

        // Tampered leaf must fail verification
        let mut tampered = decoded.clone();
        tampered.leaf[0] = 99;
        assert!(!verify_merkle_proof(&tampered, &root));

        // Proof for leaf1 at index 1 (sibling leaf0 is to the left, pathBitmap bit 0 is 1)
        let proof1 = MerkleProof {
            leaf: leaf1,
            leaf_index: 1,
            path_bitmap: 1,
            siblings: vec![leaf0],
        };
        let bytes1 = encode_merkle_proof(&proof1).expect("encode succeeds");
        let decoded1 = parse_merkle_proof(&bytes1).expect("parse succeeds");
        assert!(verify_merkle_proof(&decoded1, &root));
    }

    #[test]
    fn merkle_proof_rejects_depth_exceeding_max() {
        let proof = MerkleProof {
            leaf: [1u8; 32],
            leaf_index: 0,
            path_bitmap: 0,
            siblings: vec![[2u8; 32]; MAX_MERKLE_DEPTH + 1],
        };
        let err = encode_merkle_proof(&proof).expect_err("exceeding depth must fail");
        assert!(matches!(err, ExecutionError::Oversized("merkle proof depth exceeds 64")));
    }
}
