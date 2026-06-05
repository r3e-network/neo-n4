use super::{IndexedRsv, MAX_SIGNERS, ProofBuildError};

/// Encode the proof bytes for `NeoExternalBridgeRouter.finalizeWithdrawal`.
///
/// Layout: `[2B sigCount LE] + sigCount × (1B signerIdx, 32B r, 32B s, 1B v)`.
pub struct EthProofBytes;

impl EthProofBytes {
    pub fn encode(sigs: &[IndexedRsv]) -> Result<Vec<u8>, ProofBuildError> {
        if sigs.len() > MAX_SIGNERS {
            return Err(ProofBuildError::TooManySigners(sigs.len()));
        }
        for s in sigs {
            if s.v != 27 && s.v != 28 {
                return Err(ProofBuildError::BadVByte(s.v));
            }
        }

        let size = 2 + sigs.len() * 66;
        let mut out = Vec::with_capacity(size);
        out.extend_from_slice(&(sigs.len() as u16).to_le_bytes());
        for s in sigs {
            out.push(s.signer_idx);
            out.extend_from_slice(&s.r);
            out.extend_from_slice(&s.s);
            out.push(s.v);
        }
        Ok(out)
    }
}
