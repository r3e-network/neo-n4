//! HTTP JSON-RPC `NeoSubmitter` for posting verified bridge inbounds to
//! Neo's `NeoHub.ExternalBridgeEscrow.Receive(...)`.
//!
//! Two-stage flow keeps Neo tx-signing fully external:
//!
//! 1. **Pre-check** via `invokefunction`. Neo's RPC node builds the call
//!    script, runs it in a read-only ApplicationEngine, and returns the
//!    final VM `state` (`HALT`/`FAULT`) + the constructed `script` bytes.
//!    A `FAULT` here means the verifier rejected the proof or the
//!    escrow rejected for some other reason — the watcher gets a typed
//!    error before paying gas.
//!
//! 2. **Sign + send** via an operator-supplied callback. The callback
//!    receives the script bytes from step 1, wraps them in a signed Neo
//!    transaction (using the operator's HSM / wallet of choice), and
//!    POSTs `sendrawtransaction`. We don't construct or sign the tx in
//!    this crate — Neo tx signing is operator infrastructure (HSM
//!    integrations vary), and a watcher daemon shouldn't ship its own
//!    Neo wallet.
//!
//! The callback returns the tx hash on success; the submitter returns
//! that as the `submit_inbound` result.

use crate::submitter::SubmitterError;
use std::time::Duration;
use thiserror::Error;

mod invoke_function_result;
mod neo_rpc_submitter;
mod neo_rpc_submitter_builder;

pub use neo_rpc_submitter::NeoRpcSubmitter;
pub use neo_rpc_submitter_builder::NeoRpcSubmitterBuilder;

const DEFAULT_REQUEST_TIMEOUT: Duration = Duration::from_secs(30);

#[derive(Debug, Error)]
pub enum NeoRpcError {
    #[error("HTTP error: {0}")]
    Http(String),
    #[error("RPC error: {code} {message}")]
    Rpc { code: i64, message: String },
    #[error("decoding response failed: {0}")]
    Decode(String),
    #[error("invokefunction returned FAULT: {0}")]
    Fault(String),
    #[error("sign_and_send callback failed: {0}")]
    SignAndSend(String),
}

impl From<NeoRpcError> for SubmitterError {
    fn from(err: NeoRpcError) -> Self {
        match err {
            // Match common variations of the "nonce already consumed" FAULT.
            // The L1 contract throws with "already consumed" but message
            // wording may vary across Neo node versions or localization.
            NeoRpcError::Fault(msg)
                if msg.contains("already consumed")
                    || msg.contains("already-consumed")
                    || msg.contains("nonce consumed")
                    || msg.contains("replay detected") =>
            {
                SubmitterError::AlreadyConsumed
            }
            NeoRpcError::Fault(msg) => SubmitterError::VerifierRejected(msg),
            NeoRpcError::Http(m) | NeoRpcError::Decode(m) | NeoRpcError::SignAndSend(m) => {
                SubmitterError::Rpc(m)
            }
            NeoRpcError::Rpc { code, message } => {
                SubmitterError::Rpc(format!("rpc {code}: {message}"))
            }
        }
    }
}

/// Operator-supplied signer/sender. Receives the script bytes from
/// `invokefunction` (which already validated the call would HALT),
/// wraps them in a Neo `Transaction`, signs with the operator's
/// witnessing identity, POSTs `sendrawtransaction`, and returns the
/// 32-byte tx hash. Production deployments wire this to an HSM / KMS.
///
/// Returning `Err` short-circuits the submitter — the journal cursor
/// MUST NOT advance, so the caller can retry on the next tick.
pub trait SignAndSend: Send {
    fn sign_and_send(&mut self, script: &[u8]) -> Result<[u8; 32], NeoRpcError>;
}

impl<F> SignAndSend for F
where
    F: FnMut(&[u8]) -> Result<[u8; 32], NeoRpcError> + Send,
{
    fn sign_and_send(&mut self, script: &[u8]) -> Result<[u8; 32], NeoRpcError> {
        self(script)
    }
}

fn decode_hex_bytes(s: &str) -> Result<Vec<u8>, String> {
    crate::live::json_rpc_types::decode_hex_bytes(s)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::submitter::{InboundSubmission, NeoSubmitter};

    /// Pin the `invokefunction` JSON shape. This is what the operator's
    /// neo-cli RPC node sees; a refactor that drops a field or shifts a
    /// type breaks every Neo node that processed our calls before.
    #[test]
    fn invokefunction_request_shape() {
        let escrow = [0xAB; 20];
        let signer = [0xCD; 20];
        let dummy_callback = |_script: &[u8]| -> Result<[u8; 32], NeoRpcError> { Ok([0u8; 32]) };
        let s = NeoRpcSubmitterBuilder::new("http://x", escrow, signer, dummy_callback)
            .build()
            .unwrap();
        let req = s
            .build_invokefunction(&InboundSubmission {
                external_chain_id: 0xE000_0001,
                message_bytes: vec![0xAA, 0xBB, 0xCC],
                proof_bytes: vec![0xDE, 0xAD, 0xBE, 0xEF],
            })
            .unwrap();
        let arr = req.as_array().unwrap();
        assert_eq!(
            arr.len(),
            4,
            "params: [contract, method, callargs, signers]"
        );
        // Contract hash is LE-hex with 0x prefix (20 bytes = 40 hex chars).
        assert_eq!(
            arr[0].as_str().unwrap(),
            "0xabababababababababababababababababababab"
        );
        assert_eq!(arr[1].as_str().unwrap(), "receive");
        // Three call args: Integer chainId, ByteArray message, ByteArray proof.
        let call_args = arr[2].as_array().unwrap();
        assert_eq!(call_args.len(), 3);
        assert_eq!(call_args[0]["type"].as_str().unwrap(), "Integer");
        assert_eq!(call_args[0]["value"].as_str().unwrap(), "3758096385"); // 0xE0000001
        assert_eq!(call_args[1]["type"].as_str().unwrap(), "ByteArray");
        assert_eq!(call_args[1]["value"].as_str().unwrap(), "aabbcc");
        assert_eq!(call_args[2]["type"].as_str().unwrap(), "ByteArray");
        assert_eq!(call_args[2]["value"].as_str().unwrap(), "deadbeef");
        // Signers list: one entry, CalledByEntry scope.
        let signers = arr[3].as_array().unwrap();
        assert_eq!(signers.len(), 1);
        assert_eq!(signers[0]["scopes"].as_str().unwrap(), "CalledByEntry");
        assert_eq!(
            signers[0]["account"].as_str().unwrap(),
            "0xcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd" // 20 bytes = 40 hex chars
        );
    }

    /// Pin the FAULT → SubmitterError translation: an "already consumed"
    /// fault produces SubmitterError::AlreadyConsumed (so the watcher's
    /// retry loop doesn't try forever on a duplicate).
    #[test]
    fn fault_translation() {
        let translated: SubmitterError =
            NeoRpcError::Fault("nonce already consumed (replay)".into()).into();
        assert!(matches!(translated, SubmitterError::AlreadyConsumed));

        let translated: SubmitterError =
            NeoRpcError::Fault("verifier rejected proof".into()).into();
        match translated {
            SubmitterError::VerifierRejected(m) => assert!(m.contains("verifier")),
            other => panic!("expected VerifierRejected, got {other:?}"),
        }

        let translated: SubmitterError = NeoRpcError::Http("connection refused".into()).into();
        match translated {
            SubmitterError::Rpc(m) => assert!(m.contains("connection")),
            other => panic!("expected Rpc, got {other:?}"),
        }
    }

    // ─── live HTTP integration tests ─────────────────────────────────────
    //
    // Drive `NeoRpcSubmitter` through the actual `reqwest::blocking` HTTP
    // stack against an in-process fake JSON-RPC server. The unit tests
    // above pin the request shape + the error-translation table; these
    // tests pin the full pipeline (HTTP → parsed result → callback
    // invocation OR error translation), including:
    //
    // - Pre-check HALT path: script bytes flow into the SignAndSend
    //   callback, the callback's tx hash flows back as the function
    //   result.
    // - Pre-check FAULT path: the script bytes are NOT extracted, the
    //   callback is NOT invoked, the error is surfaced. Important
    //   because a bug here would invoke the operator's HSM with garbage
    //   on a verifier-rejected proof.
    // - "already consumed" FAULT detection: the watcher's retry loop
    //   relies on this distinction to not loop forever on duplicates.

    use crate::live::test_support::FakeRpcServer;
    use std::sync::atomic::{AtomicUsize, Ordering};
    use std::sync::Arc;

    /// Happy path: invokefunction returns HALT + a script hex; callback
    /// receives the script bytes; callback's tx hash bubbles up to the
    /// caller.
    #[test]
    fn live_submit_halt_invokes_callback_and_returns_tx_hash() {
        // Server responds with HALT state + a known script hex.
        let server = FakeRpcServer::spawn(|body: &str| {
            assert!(
                body.contains(r#""method":"invokefunction""#),
                "unexpected method on first call: {body}"
            );
            // Stub HALT result. `script` is the NeoVM bytecode the operator
            // would wrap + sign + submit; we just want the bytes flowing.
            r#"{
                "jsonrpc": "2.0",
                "id": 1,
                "result": {
                    "state": "HALT",
                    "script": "0x0c0566656c6c6f1234abcd",
                    "exception": null,
                    "gasconsumed": "1000000"
                }
            }"#
            .to_string()
        });

        let received_script: Arc<std::sync::Mutex<Vec<u8>>> =
            Arc::new(std::sync::Mutex::new(Vec::new()));
        let received_clone = received_script.clone();
        let callback = move |script: &[u8]| -> Result<[u8; 32], NeoRpcError> {
            received_clone.lock().unwrap().extend_from_slice(script);
            // Synthetic tx hash to bubble back.
            Ok([0xAB; 32])
        };

        let mut submitter = NeoRpcSubmitter::builder(
            server.url.clone(),
            [0xCC; 20], // escrow
            [0xDD; 20], // signer
            callback,
        )
        .request_timeout(Duration::from_secs(5))
        .build()
        .unwrap();

        let tx_hash = submitter
            .submit_inbound(InboundSubmission {
                external_chain_id: 0xE000_0001,
                message_bytes: vec![0x11, 0x22],
                proof_bytes: vec![0x33, 0x44],
            })
            .expect("submit succeeded");
        assert_eq!(
            tx_hash, [0xAB; 32],
            "callback's tx hash bubbles up unchanged"
        );

        let received = received_script.lock().unwrap().clone();
        assert_eq!(
            hex::encode(&received),
            "0c0566656c6c6f1234abcd",
            "callback received the script bytes from invokefunction.result.script"
        );
    }

    /// FAULT path: invokefunction returns FAULT; the SignAndSend
    /// callback MUST NOT be invoked (would waste an HSM op + potentially
    /// produce a malformed tx). The error must surface as
    /// `VerifierRejected`.
    #[test]
    fn live_submit_fault_skips_callback_and_returns_verifier_rejected() {
        let server = FakeRpcServer::spawn(|_body: &str| {
            r#"{
                "jsonrpc": "2.0",
                "id": 1,
                "result": {
                    "state": "FAULT",
                    "script": "0xdeadbeef",
                    "exception": "verifier rejected: signature 3 invalid",
                    "gasconsumed": "1000000"
                }
            }"#
            .to_string()
        });

        let callback_invocations = Arc::new(AtomicUsize::new(0));
        let cb = callback_invocations.clone();
        let callback = move |_: &[u8]| -> Result<[u8; 32], NeoRpcError> {
            cb.fetch_add(1, Ordering::Relaxed);
            Ok([0u8; 32])
        };

        let mut submitter =
            NeoRpcSubmitter::builder(server.url.clone(), [0xCC; 20], [0xDD; 20], callback)
                .request_timeout(Duration::from_secs(5))
                .build()
                .unwrap();

        let err = submitter
            .submit_inbound(InboundSubmission {
                external_chain_id: 0xE000_0001,
                message_bytes: vec![0xAA],
                proof_bytes: vec![0xBB],
            })
            .expect_err("FAULT must surface as error");
        match err {
            SubmitterError::VerifierRejected(msg) => {
                assert!(
                    msg.contains("verifier rejected"),
                    "carries server-side detail"
                );
            }
            other => panic!("expected VerifierRejected, got {other:?}"),
        }
        assert_eq!(
            callback_invocations.load(Ordering::Relaxed),
            0,
            "operator's signing callback MUST NOT be invoked when pre-check FAULTs"
        );
    }

    /// FAULT with the "already consumed" sentinel must translate to
    /// `SubmitterError::AlreadyConsumed` so the watcher's outer retry
    /// loop can short-circuit instead of looping forever on a
    /// duplicate-nonce inbound.
    #[test]
    fn live_submit_already_consumed_fault_translates() {
        let server = FakeRpcServer::spawn(|_body: &str| {
            r#"{
                "jsonrpc": "2.0",
                "id": 1,
                "result": {
                    "state": "FAULT",
                    "script": "0x",
                    "exception": "nonce already consumed (chainId=0xE0000001, nonce=42)",
                    "gasconsumed": "100"
                }
            }"#
            .to_string()
        });

        let callback = |_: &[u8]| -> Result<[u8; 32], NeoRpcError> { Ok([0u8; 32]) };
        let mut submitter =
            NeoRpcSubmitter::builder(server.url.clone(), [0xCC; 20], [0xDD; 20], callback)
                .request_timeout(Duration::from_secs(5))
                .build()
                .unwrap();

        let err = submitter
            .submit_inbound(InboundSubmission {
                external_chain_id: 0xE000_0001,
                message_bytes: vec![],
                proof_bytes: vec![],
            })
            .expect_err("FAULT must surface as error");
        assert!(
            matches!(err, SubmitterError::AlreadyConsumed),
            "expected AlreadyConsumed, got {err:?}"
        );
    }

    /// JSON-RPC error response (e.g. malformed call args, node sync) is
    /// surfaced as `SubmitterError::Rpc` — distinct from `VerifierRejected`
    /// so the daemon's retry loop knows it's a transport / RPC issue, not
    /// a contract rejection.
    #[test]
    fn live_submit_propagates_rpc_error_response() {
        let server = FakeRpcServer::spawn(|_body: &str| {
            r#"{
                "jsonrpc": "2.0",
                "id": 1,
                "error": {"code": -32602, "message": "Invalid params"}
            }"#
            .to_string()
        });

        let callback_invocations = Arc::new(AtomicUsize::new(0));
        let cb = callback_invocations.clone();
        let callback = move |_: &[u8]| -> Result<[u8; 32], NeoRpcError> {
            cb.fetch_add(1, Ordering::Relaxed);
            Ok([0u8; 32])
        };
        let mut submitter =
            NeoRpcSubmitter::builder(server.url.clone(), [0xCC; 20], [0xDD; 20], callback)
                .request_timeout(Duration::from_secs(5))
                .build()
                .unwrap();

        let err = submitter
            .submit_inbound(InboundSubmission {
                external_chain_id: 0xE000_0001,
                message_bytes: vec![],
                proof_bytes: vec![],
            })
            .expect_err("rpc error must surface");
        match err {
            SubmitterError::Rpc(msg) => {
                assert!(
                    msg.contains("-32602") || msg.contains("Invalid params"),
                    "surfaces server-side detail: {msg}"
                );
            }
            other => panic!("expected Rpc, got {other:?}"),
        }
        assert_eq!(
            callback_invocations.load(Ordering::Relaxed),
            0,
            "callback NOT invoked on RPC-error response (no script available)"
        );
    }

    /// The SignAndSend callback can fail (HSM down, network error to
    /// `sendrawtransaction`); that must surface as `SubmitterError::Rpc`
    /// so the journal cursor doesn't advance and the daemon retries.
    #[test]
    fn live_submit_propagates_callback_failure() {
        let server = FakeRpcServer::spawn(|_body: &str| {
            r#"{
                "jsonrpc": "2.0",
                "id": 1,
                "result": {
                    "state": "HALT",
                    "script": "0xabcd",
                    "exception": null,
                    "gasconsumed": "1000"
                }
            }"#
            .to_string()
        });

        let callback = |_: &[u8]| -> Result<[u8; 32], NeoRpcError> {
            Err(NeoRpcError::SignAndSend("HSM unavailable".into()))
        };
        let mut submitter =
            NeoRpcSubmitter::builder(server.url.clone(), [0xCC; 20], [0xDD; 20], callback)
                .request_timeout(Duration::from_secs(5))
                .build()
                .unwrap();

        let err = submitter
            .submit_inbound(InboundSubmission {
                external_chain_id: 0xE000_0001,
                message_bytes: vec![0xAA],
                proof_bytes: vec![0xBB],
            })
            .expect_err("callback failure must surface");
        match err {
            SubmitterError::Rpc(msg) => assert!(msg.contains("HSM")),
            other => panic!("expected Rpc, got {other:?}"),
        }
    }

    /// Transport-level failure (server unreachable) surfaces as
    /// `SubmitterError::Rpc`. Mirrors the eth-side
    /// `live_propagates_transport_failure`.
    #[test]
    fn live_submit_propagates_transport_failure() {
        // Bind + drop to find an unused port; the submitter will fail
        // to connect.
        let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        drop(listener);

        let url = format!("http://127.0.0.1:{}/", port);
        let callback_invocations = Arc::new(AtomicUsize::new(0));
        let cb = callback_invocations.clone();
        let callback = move |_: &[u8]| -> Result<[u8; 32], NeoRpcError> {
            cb.fetch_add(1, Ordering::Relaxed);
            Ok([0u8; 32])
        };
        let mut submitter = NeoRpcSubmitter::builder(url, [0xCC; 20], [0xDD; 20], callback)
            .request_timeout(Duration::from_secs(1))
            .build()
            .unwrap();

        let err = submitter
            .submit_inbound(InboundSubmission {
                external_chain_id: 0xE000_0001,
                message_bytes: vec![],
                proof_bytes: vec![],
            })
            .expect_err("transport failure must surface");
        match err {
            SubmitterError::Rpc(_) => { /* expected */ }
            other => panic!("expected Rpc, got {other:?}"),
        }
        assert_eq!(
            callback_invocations.load(Ordering::Relaxed),
            0,
            "callback not invoked when pre-check transport fails"
        );
    }
}
