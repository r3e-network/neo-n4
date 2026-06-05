use super::{Curve, MAX_SIGNERS, ProofBuildError, PubkeySignature};

/// Encode the proof bytes for `NeoHub.MpcCommitteeVerifier.VerifyInboundMessage`.
///
/// Layout: `[2B sigCount LE] + sigCount × ([keyLen B pubkey][64B sig])`.
/// Same shape `Neo.L2.Bridge.External.MpcCommitteePayload.Encode` produces.
pub struct NeoProofBytes;

impl NeoProofBytes {
    pub fn encode(curve: Curve, sigs: &[PubkeySignature]) -> Result<Vec<u8>, ProofBuildError> {
        if sigs.len() > MAX_SIGNERS {
            return Err(ProofBuildError::TooManySigners(sigs.len()));
        }
        let key_len = curve.pubkey_len();
        for s in sigs {
            if s.pubkey.len() != key_len {
                return Err(ProofBuildError::BadPubkeyLength {
                    curve,
                    got: s.pubkey.len(),
                    expected: key_len,
                });
            }
        }

        let size = 2 + sigs.len() * (key_len + 64);
        let mut out = Vec::with_capacity(size);
        out.extend_from_slice(&(sigs.len() as u16).to_le_bytes());
        for s in sigs {
            out.extend_from_slice(&s.pubkey);
            out.extend_from_slice(&s.signature);
        }
        Ok(out)
    }
}
