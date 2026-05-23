use neo_bridge_watcher_eth::live::{NeoRpcError, SignAndSend};

/// v0 stub: emits a clear warning + returns a synthetic tx hash so the
/// watcher's journal advances, but does NOT actually sign + submit a
/// Neo transaction. Operators replace with a real HSM/KMS-backed signer
/// in production.
pub(crate) struct StubSignAndSend;

impl SignAndSend for StubSignAndSend {
    fn sign_and_send(&mut self, script: &[u8]) -> Result<[u8; 32], NeoRpcError> {
        eprintln!(
            "STUB: would submit Neo tx with script ({} bytes): 0x{}",
            script.len(),
            hex::encode(script),
        );
        eprintln!(
            "      Replace StubSignAndSend with an HSM/KMS-backed impl that wraps the script in a signed Neo Transaction + POSTs `sendrawtransaction`. The watcher journals as if submission succeeded so the cursor advances; an operator must externally confirm the tx landed on Neo before relying on the journal state."
        );
        // Synthetic tx hash: SHA256 of the script. Stable + identifiable
        // in operator logs; collisions are a non-issue for v0.
        use sha2::{Digest, Sha256};
        let mut hasher = Sha256::new();
        hasher.update(script);
        let digest = hasher.finalize();
        let mut out = [0u8; 32];
        out.copy_from_slice(&digest);
        Ok(out)
    }
}
