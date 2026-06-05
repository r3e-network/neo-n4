use std::time::Duration;

use super::{DEFAULT_REQUEST_TIMEOUT, NeoRpcError, NeoRpcSubmitter, SignAndSend};

/// Configuration for a [`NeoRpcSubmitter`].
pub struct NeoRpcSubmitterBuilder<S: SignAndSend> {
    pub(super) rpc_url: String,
    pub(super) escrow_address: [u8; 20],
    /// Account hash that will be the tx signer. Passed to
    /// `invokefunction` so the pre-check sees `Runtime.CheckWitness`
    /// against this address as TRUE - same as a real signed tx would.
    pub(super) signer: [u8; 20],
    pub(super) sign_and_send: S,
    pub(super) request_timeout: Duration,
}

impl<S: SignAndSend> NeoRpcSubmitterBuilder<S> {
    pub fn new(
        rpc_url: impl Into<String>,
        escrow_address: [u8; 20],
        signer: [u8; 20],
        sign_and_send: S,
    ) -> Self {
        Self {
            rpc_url: rpc_url.into(),
            escrow_address,
            signer,
            sign_and_send,
            request_timeout: DEFAULT_REQUEST_TIMEOUT,
        }
    }

    pub fn request_timeout(mut self, t: Duration) -> Self {
        self.request_timeout = t;
        self
    }

    pub fn build(self) -> Result<NeoRpcSubmitter<S>, NeoRpcError> {
        let client = reqwest::blocking::Client::builder()
            .timeout(self.request_timeout)
            .build()
            .map_err(|e| NeoRpcError::Http(format!("client build: {e}")))?;
        Ok(NeoRpcSubmitter {
            client,
            rpc_url: self.rpc_url,
            escrow_address: self.escrow_address,
            signer: self.signer,
            sign_and_send: self.sign_and_send,
            next_request_id: std::sync::atomic::AtomicU64::new(1),
        })
    }
}
