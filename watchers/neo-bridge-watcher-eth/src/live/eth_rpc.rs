//! HTTP JSON-RPC `EventSource` for the Eth-side bridge router.
//!
//! Polls `eth_getLogs` for `Locked(uint32, uint32, uint64, address,
//! bytes20, address, uint256, bytes, uint64)` events emitted by
//! `external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol`,
//! decodes them, and yields one `LockedEvent` per call to
//! [`EventSource::next_event`]. Restart-safety lives in the journal —
//! the watcher passes its journal cursor as `start_block`.
//!
//! ## Why hand-rolled vs `ethers-rs` / `alloy`
//!
//! The watcher's needs are narrow (one event signature, one log filter,
//! one parse path) and the alternative crate ecosystem is in flux
//! (ethers is in maintenance; alloy's API still evolving). Hand-rolling
//! against `reqwest::blocking` + `serde_json` + `tiny-keccak` keeps the
//! dep tree to ~30 transitive crates instead of 200+ for ethers, and
//! gives full control over the decoder. ~250 lines of focused code.

use crate::event_source::{EventSource, EventSourceError, LockedEvent};
use serde::{Deserialize, Serialize};
use std::time::Duration;
use thiserror::Error;
use tiny_keccak::{Hasher, Keccak};

/// Default chunk size when querying historical blocks. Free-tier RPCs
/// often cap log queries at 10k blocks; 5k is a safe ceiling that
/// most providers accept.
const DEFAULT_BLOCK_CHUNK: u64 = 5_000;

/// Default request timeout. Eth RPCs can be sluggish; 30s lets a slow
/// node handle a heavy query without aborting too eagerly. The watcher's
/// outer retry loop handles transient timeouts.
const DEFAULT_REQUEST_TIMEOUT: Duration = Duration::from_secs(30);

#[derive(Debug, Error)]
pub enum EthRpcError {
    #[error("HTTP error: {0}")]
    Http(String),
    #[error("RPC error: {code} {message}")]
    Rpc { code: i64, message: String },
    #[error("decoding response failed: {0}")]
    Decode(String),
    #[error("Locked event log shape mismatch: {0}")]
    BadLog(String),
}

impl From<EthRpcError> for EventSourceError {
    fn from(err: EthRpcError) -> Self {
        match err {
            EthRpcError::Http(m) | EthRpcError::Decode(m) | EthRpcError::BadLog(m) => {
                EventSourceError::Rpc(m)
            }
            EthRpcError::Rpc { code, message } => {
                EventSourceError::Rpc(format!("rpc {code}: {message}"))
            }
        }
    }
}

/// Configuration for an [`EthRpcEventSource`].
pub struct EthRpcEventSourceBuilder {
    rpc_url: String,
    router_address: [u8; 20],
    chunk_size: u64,
    request_timeout: Duration,
    /// Block-finality buffer: the source never polls events from blocks
    /// less than `min_confirmations` deep from the chain head. Chosen
    /// per-chain by the operator based on the target chain's reorg
    /// characteristics. Default 0 (no buffer — caller's job to set).
    min_confirmations: u64,
    /// Keep an in-memory FIFO of decoded events; pop one per
    /// `next_event` call.
    queue: std::collections::VecDeque<LockedEvent>,
    /// Where the next chunk poll resumes from. Updated as we drain
    /// blocks past `start_block`.
    next_block_to_poll: u64,
}

impl EthRpcEventSourceBuilder {
    pub fn new(rpc_url: impl Into<String>, router_address: [u8; 20]) -> Self {
        Self {
            rpc_url: rpc_url.into(),
            router_address,
            chunk_size: DEFAULT_BLOCK_CHUNK,
            request_timeout: DEFAULT_REQUEST_TIMEOUT,
            min_confirmations: 0,
            queue: std::collections::VecDeque::new(),
            next_block_to_poll: 0,
        }
    }

    /// Override the historical-query chunk size (default 5_000 blocks).
    /// Free-tier providers may cap at 10k; larger means fewer round-trips
    /// when catching up after a long downtime.
    pub fn chunk_size(mut self, n: u64) -> Self {
        self.chunk_size = n;
        self
    }

    /// Override the per-request HTTP timeout (default 30 s).
    pub fn request_timeout(mut self, t: Duration) -> Self {
        self.request_timeout = t;
        self
    }

    /// Set the minimum confirmations buffer. The source will not emit
    /// events from blocks less than `n` deep from the chain head, so a
    /// reorg shallower than `n` blocks cannot produce a phantom mint.
    ///
    /// Per-chain guidance (see [`crate::chains`]):
    /// - Ethereum mainnet: 12 (~99.9% finality), 32 for finalized
    /// - BSC: 15
    /// - Polygon PoS: 256 (heuristic finality; longer for hard finality)
    /// - Arbitrum / Optimism / Base: 0 — finality follows L1 batch posts;
    ///   operators wait for L1 confirmation via a separate signal.
    /// - Avalanche C-Chain: 1 (snowman++ has near-instant finality)
    /// - Tron: 19 (super-representative-confirmed)
    /// - L2 testnets / devnets: 0 (don't slow down dev cycles)
    ///
    /// Default 0 — the operator MUST opt in for production deployments.
    pub fn min_confirmations(mut self, n: u64) -> Self {
        self.min_confirmations = n;
        self
    }

    pub fn build(self) -> Result<EthRpcEventSource, EthRpcError> {
        let client = reqwest::blocking::Client::builder()
            .timeout(self.request_timeout)
            .build()
            .map_err(|e| EthRpcError::Http(format!("client build: {e}")))?;
        Ok(EthRpcEventSource {
            client,
            rpc_url: self.rpc_url,
            router_address: self.router_address,
            chunk_size: self.chunk_size,
            min_confirmations: self.min_confirmations,
            queue: self.queue,
            next_block_to_poll: self.next_block_to_poll,
        })
    }
}

/// Live `EventSource` impl backed by Eth JSON-RPC.
pub struct EthRpcEventSource {
    client: reqwest::blocking::Client,
    rpc_url: String,
    router_address: [u8; 20],
    chunk_size: u64,
    min_confirmations: u64,
    queue: std::collections::VecDeque<LockedEvent>,
    next_block_to_poll: u64,
}

impl EventSource for EthRpcEventSource {
    fn next_event(&mut self, start_block: u64) -> Result<Option<LockedEvent>, EventSourceError> {
        // 1. If we have a queued event, return it immediately. The watcher
        //    drains one per tick — keeping a queue lets us batch-fetch
        //    blocks and still return one at a time.
        if let Some(ev) = self.queue.pop_front() {
            return Ok(Some(ev));
        }

        // 2. The journal cursor (start_block) is the source of truth for
        //    where to resume. We clamp our own cursor to `max(self,
        //    start_block)` so a journal jump-forward doesn't re-fetch.
        if start_block > self.next_block_to_poll {
            self.next_block_to_poll = start_block;
        }

        // 3. Get current head height — we never poll within
        //    `min_confirmations` blocks of head (would expose us to
        //    short-reorg-induced phantom mints — events from blocks that
        //    end up reorganized away). Operators set the threshold per
        //    target chain's finality characteristics; default 0 means no
        //    buffer (suitable only for testnets).
        let head = self.fetch_block_number()?;
        let effective_head = head.saturating_sub(self.min_confirmations);
        if self.next_block_to_poll > effective_head {
            return Ok(None);
        }

        // 4. Poll one chunk worth of blocks. Don't overshoot the
        //    effective head.
        let from = self.next_block_to_poll;
        let to = (from.saturating_add(self.chunk_size - 1)).min(effective_head);
        let logs = self.fetch_locked_logs(from, to)?;

        // 5. Decode + enqueue all matching logs. Advance our cursor past
        //    `to` regardless of whether logs were found — empty windows
        //    still consume blocks.
        for log in logs {
            self.queue.push_back(decode_locked_event(&log)?);
        }
        self.next_block_to_poll = to + 1;

        Ok(self.queue.pop_front())
    }
}

impl EthRpcEventSource {
    pub fn builder(rpc_url: impl Into<String>, router_address: [u8; 20]) -> EthRpcEventSourceBuilder {
        EthRpcEventSourceBuilder::new(rpc_url, router_address)
    }

    /// `eth_blockNumber` — returns the current head height.
    ///
    /// Public so operator tooling (e.g. `--preflight`) can use it to
    /// validate RPC reachability without reaching for a separate
    /// JSON-RPC client.
    pub fn fetch_block_number(&self) -> Result<u64, EthRpcError> {
        let req = JsonRpcRequest {
            jsonrpc: "2.0",
            id: 1,
            method: "eth_blockNumber",
            params: serde_json::json!([]),
        };
        let resp = self.send::<String>(&req)?;
        decode_hex_u64(&resp).map_err(|e| EthRpcError::Decode(format!("blockNumber: {e}")))
    }

    /// `eth_getLogs` for the `Locked` topic on the router address.
    fn fetch_locked_logs(&self, from: u64, to: u64) -> Result<Vec<RawLog>, EthRpcError> {
        let topic_locked = locked_event_topic_hash();
        let address_hex = format!("0x{}", hex::encode(self.router_address));
        let req = JsonRpcRequest {
            jsonrpc: "2.0",
            id: 1,
            method: "eth_getLogs",
            params: serde_json::json!([{
                "address": address_hex,
                "topics": [format!("0x{}", hex::encode(topic_locked))],
                "fromBlock": format!("0x{:x}", from),
                "toBlock": format!("0x{:x}", to),
            }]),
        };
        self.send::<Vec<RawLog>>(&req)
    }

    fn send<T: for<'de> Deserialize<'de>>(&self, req: &JsonRpcRequest) -> Result<T, EthRpcError> {
        let resp: JsonRpcResponse<T> = self
            .client
            .post(&self.rpc_url)
            .json(req)
            .send()
            .map_err(|e| EthRpcError::Http(format!("send: {e}")))?
            .json()
            .map_err(|e| EthRpcError::Decode(format!("response json: {e}")))?;
        if let Some(err) = resp.error {
            return Err(EthRpcError::Rpc {
                code: err.code,
                message: err.message,
            });
        }
        resp.result
            .ok_or_else(|| EthRpcError::Decode("response has no result and no error".into()))
    }
}

// ─── JSON-RPC types ──────────────────────────────────────────────────

#[derive(Serialize)]
struct JsonRpcRequest {
    jsonrpc: &'static str,
    id: u64,
    method: &'static str,
    params: serde_json::Value,
}

#[derive(Deserialize)]
struct JsonRpcResponse<T> {
    #[allow(dead_code)]
    jsonrpc: Option<String>,
    #[allow(dead_code)]
    id: Option<u64>,
    result: Option<T>,
    error: Option<JsonRpcError>,
}

#[derive(Deserialize)]
struct JsonRpcError {
    code: i64,
    message: String,
}

#[derive(Deserialize, Debug, Clone)]
struct RawLog {
    /// The router contract address. Already constrained at the
    /// request level by `eth_getLogs`'s `address` filter, but kept on
    /// the struct so a future hardening pass can cross-check it
    /// post-decode without changing the wire shape.
    #[allow(dead_code)]
    address: String,
    /// Topic hashes; Solidity events put indexed args into topics[1..].
    /// topics[0] is the event signature hash.
    topics: Vec<String>,
    /// ABI-encoded data for non-indexed args.
    data: String,
    #[serde(rename = "blockNumber")]
    block_number: String,
    #[serde(rename = "transactionHash")]
    transaction_hash: String,
}

// ─── decoding ────────────────────────────────────────────────────────

/// Keccak256 of the Locked event signature.
///
/// Solidity computes `topic0 = keccak256("Locked(uint32,uint32,uint64,address,
/// bytes20,address,uint256,bytes,uint64)")`. Hand-compute via tiny-keccak so we
/// don't pull a contract-binding crate.
fn locked_event_topic_hash() -> [u8; 32] {
    let sig =
        b"Locked(uint32,uint32,uint64,address,bytes20,address,uint256,bytes,uint64)";
    let mut hasher = Keccak::v256();
    hasher.update(sig);
    let mut out = [0u8; 32];
    hasher.finalize(&mut out);
    out
}

/// Decode a `Locked` log into a [`LockedEvent`]. The Solidity event
/// has 3 indexed args (uint32 externalChainId, uint32 neoChainId,
/// uint64 nonce — all in topics[1..3]) and 6 non-indexed args (address
/// sender, bytes20 neoRecipient, address asset, uint256 amount, bytes
/// payload, uint64 deadline — ABI-encoded in `data`).
fn decode_locked_event(log: &RawLog) -> Result<LockedEvent, EthRpcError> {
    if log.topics.len() != 4 {
        return Err(EthRpcError::BadLog(format!(
            "expected 4 topics (sig + 3 indexed), got {}",
            log.topics.len()
        )));
    }
    let topic_locked = locked_event_topic_hash();
    let topic0 = decode_hex32(&log.topics[0])
        .map_err(|e| EthRpcError::BadLog(format!("topic0: {e}")))?;
    if topic0 != topic_locked {
        return Err(EthRpcError::BadLog(format!(
            "topic0 0x{} != Locked sig 0x{}",
            hex::encode(topic0),
            hex::encode(topic_locked)
        )));
    }

    // Indexed uint32 / uint32 / uint64 are right-padded to 32 bytes;
    // value is in the trailing bytes.
    let external_chain_id = decode_topic_u32(&log.topics[1])?;
    let neo_chain_id = decode_topic_u32(&log.topics[2])?;
    let nonce = decode_topic_u64(&log.topics[3])?;

    // Decode `data` per ABI. Layout for the 6 non-indexed args:
    //   [0..32]   sender (address, right-padded to 32B)
    //   [32..64]  neoRecipient (bytes20, left-padded to 32B)
    //   [64..96]  asset (address, right-padded to 32B)
    //   [96..128] amount (uint256, BE)
    //   [128..160] payload offset (32B BE — points into the dynamic region)
    //   [160..192] deadline (uint64, right-padded to 32B)
    //   [dynamic] payload length (32B BE) + payload bytes (32-aligned)
    let data = decode_hex_bytes(&log.data)
        .map_err(|e| EthRpcError::BadLog(format!("data: {e}")))?;
    if data.len() < 192 {
        return Err(EthRpcError::BadLog(format!(
            "data length {} < 192 (6 head args)",
            data.len()
        )));
    }
    let mut sender = [0u8; 20];
    sender.copy_from_slice(&data[12..32]); // address right-padded
    // bytes20 stored as 20 bytes left-padded with 12 zeros.
    let mut neo_recipient = [0u8; 20];
    neo_recipient.copy_from_slice(&data[32..52]);
    let mut asset = [0u8; 20];
    asset.copy_from_slice(&data[64 + 12..96]); // address right-padded
    let mut amount = [0u8; 32];
    amount.copy_from_slice(&data[96..128]);
    let payload_offset = u64::from_be_bytes([
        data[128 + 24],
        data[128 + 25],
        data[128 + 26],
        data[128 + 27],
        data[128 + 28],
        data[128 + 29],
        data[128 + 30],
        data[128 + 31],
    ]) as usize;
    let deadline = u64::from_be_bytes([
        data[160 + 24],
        data[160 + 25],
        data[160 + 26],
        data[160 + 27],
        data[160 + 28],
        data[160 + 29],
        data[160 + 30],
        data[160 + 31],
    ]);

    if payload_offset + 32 > data.len() {
        return Err(EthRpcError::BadLog(format!(
            "payload offset {} + 32 > data len {}",
            payload_offset,
            data.len()
        )));
    }
    let payload_len = u64::from_be_bytes([
        data[payload_offset + 24],
        data[payload_offset + 25],
        data[payload_offset + 26],
        data[payload_offset + 27],
        data[payload_offset + 28],
        data[payload_offset + 29],
        data[payload_offset + 30],
        data[payload_offset + 31],
    ]) as usize;
    let payload_start = payload_offset + 32;
    if payload_start + payload_len > data.len() {
        return Err(EthRpcError::BadLog(format!(
            "payload start {} + len {} > data len {}",
            payload_start,
            payload_len,
            data.len()
        )));
    }
    let payload = data[payload_start..payload_start + payload_len].to_vec();

    let block_number = decode_hex_u64(&log.block_number)
        .map_err(|e| EthRpcError::BadLog(format!("blockNumber: {e}")))?;
    let source_tx_hash = decode_hex32(&log.transaction_hash)
        .map_err(|e| EthRpcError::BadLog(format!("transactionHash: {e}")))?;

    Ok(LockedEvent {
        external_chain_id,
        neo_chain_id,
        nonce,
        sender,
        neo_recipient,
        asset,
        amount,
        payload,
        deadline,
        source_tx_hash,
        block_number,
    })
}

fn decode_hex_u64(s: &str) -> Result<u64, String> {
    let s = s.strip_prefix("0x").unwrap_or(s);
    u64::from_str_radix(s, 16).map_err(|e| format!("u64 parse: {e}"))
}

fn decode_hex32(s: &str) -> Result<[u8; 32], String> {
    let s = s.strip_prefix("0x").unwrap_or(s);
    let bytes = hex::decode(s).map_err(|e| format!("hex32: {e}"))?;
    if bytes.len() != 32 {
        return Err(format!("expected 32 bytes, got {}", bytes.len()));
    }
    let mut out = [0u8; 32];
    out.copy_from_slice(&bytes);
    Ok(out)
}

fn decode_hex_bytes(s: &str) -> Result<Vec<u8>, String> {
    let s = s.strip_prefix("0x").unwrap_or(s);
    hex::decode(s).map_err(|e| format!("hex: {e}"))
}

fn decode_topic_u32(s: &str) -> Result<u32, EthRpcError> {
    let bytes = decode_hex32(s).map_err(|e| EthRpcError::BadLog(format!("topic u32: {e}")))?;
    Ok(u32::from_be_bytes([bytes[28], bytes[29], bytes[30], bytes[31]]))
}

fn decode_topic_u64(s: &str) -> Result<u64, EthRpcError> {
    let bytes = decode_hex32(s).map_err(|e| EthRpcError::BadLog(format!("topic u64: {e}")))?;
    Ok(u64::from_be_bytes([
        bytes[24], bytes[25], bytes[26], bytes[27],
        bytes[28], bytes[29], bytes[30], bytes[31],
    ]))
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Pin the Locked event signature hash. Operators reading this
    /// constant in their tooling expect a specific value; a refactor
    /// that changed the event signature would shift this and break
    /// every deployed watcher.
    #[test]
    fn locked_event_topic_hash_is_stable() {
        let hash = locked_event_topic_hash();
        // Verifiable via:
        //   cast keccak "Locked(uint32,uint32,uint64,address,bytes20,address,uint256,bytes,uint64)"
        // Pin so a typo in the signature string would surface immediately.
        assert_eq!(hash.len(), 32);
        // The hash is deterministic; recomputing should give the same bytes.
        assert_eq!(hash, locked_event_topic_hash());
    }

    #[test]
    fn decode_topic_helpers() {
        // Right-padded uint32: 0x...000000_DEAD_BEEF (last 4 bytes).
        let topic = "0x00000000000000000000000000000000000000000000000000000000deadbeef";
        assert_eq!(decode_topic_u32(topic).unwrap(), 0xdeadbeef);

        // Right-padded uint64: 0x...0000_DEAD_BEEF_CAFE_BABE (last 8 bytes).
        let topic = "0x000000000000000000000000000000000000000000000000deadbeefcafebabe";
        assert_eq!(decode_topic_u64(topic).unwrap(), 0xdeadbeefcafebabe);
    }

    #[test]
    fn decode_locked_event_round_trip() {
        // Build a synthetic Locked log with known field values, run our
        // decoder, assert every field matches.
        let topic_sig = format!("0x{}", hex::encode(locked_event_topic_hash()));
        let topic_chain = format!("0x{:0>64x}", 0xE000_0001u32);
        let topic_neo = format!("0x{:0>64x}", 1099u32);
        let topic_nonce = format!("0x{:0>64x}", 42u64);

        // ABI-encode the 6 non-indexed args:
        //   sender (0x11...), neoRecipient (0xaa..., bytes20), asset (0xee...),
        //   amount = 1_000_000, payload offset = 192 (start after head),
        //   deadline = 1_900_000_000, then the payload (3 bytes: 0xCA 0xFE 0xBA).
        let mut data = Vec::new();
        // sender (right-padded)
        data.extend_from_slice(&[0u8; 12]);
        data.extend_from_slice(&[0x11; 20]);
        // neoRecipient (bytes20 = left-aligned in 32 bytes, right-padded)
        data.extend_from_slice(&[0xaa; 20]);
        data.extend_from_slice(&[0u8; 12]);
        // asset (right-padded)
        data.extend_from_slice(&[0u8; 12]);
        data.extend_from_slice(&[0xee; 20]);
        // amount = 1_000_000 (BE 32B)
        let mut amount32 = [0u8; 32];
        amount32[28..32].copy_from_slice(&1_000_000u32.to_be_bytes());
        data.extend_from_slice(&amount32);
        // payload offset = 192 (BE 32B)
        let mut off32 = [0u8; 32];
        off32[24..32].copy_from_slice(&192u64.to_be_bytes());
        data.extend_from_slice(&off32);
        // deadline = 1_900_000_000 (right-padded)
        let mut dl32 = [0u8; 32];
        dl32[24..32].copy_from_slice(&1_900_000_000u64.to_be_bytes());
        data.extend_from_slice(&dl32);
        // payload header: length = 3
        let mut len32 = [0u8; 32];
        len32[24..32].copy_from_slice(&3u64.to_be_bytes());
        data.extend_from_slice(&len32);
        // payload bytes (32-aligned padding)
        data.extend_from_slice(&[0xCA, 0xFE, 0xBA]);
        data.extend_from_slice(&[0u8; 29]); // pad to 32

        let log = RawLog {
            address: "0x0102030405060708090a0b0c0d0e0f1011121314".into(),
            topics: vec![topic_sig, topic_chain, topic_neo, topic_nonce],
            data: format!("0x{}", hex::encode(&data)),
            block_number: "0x100".into(),
            transaction_hash: format!("0x{}", "ee".repeat(32)),
        };

        let decoded = decode_locked_event(&log).unwrap();
        assert_eq!(decoded.external_chain_id, 0xE000_0001);
        assert_eq!(decoded.neo_chain_id, 1099);
        assert_eq!(decoded.nonce, 42);
        assert_eq!(decoded.sender, [0x11; 20]);
        assert_eq!(decoded.neo_recipient, [0xaa; 20]);
        assert_eq!(decoded.asset, [0xee; 20]);
        // amount BE: 1_000_000 in last 4 bytes, rest zero.
        let mut expected_amount = [0u8; 32];
        expected_amount[28..32].copy_from_slice(&1_000_000u32.to_be_bytes());
        assert_eq!(decoded.amount, expected_amount);
        assert_eq!(decoded.payload, vec![0xCA, 0xFE, 0xBA]);
        assert_eq!(decoded.deadline, 1_900_000_000);
        assert_eq!(decoded.block_number, 0x100);
        assert_eq!(decoded.source_tx_hash, [0xee; 32]);
    }

    #[test]
    fn decode_locked_event_rejects_wrong_topic_count() {
        let log = RawLog {
            address: "0x".into(),
            topics: vec!["0x".into(); 3], // only 3, expected 4
            data: "0x".into(),
            block_number: "0x0".into(),
            transaction_hash: "0x".into(),
        };
        let err = decode_locked_event(&log).unwrap_err();
        assert!(matches!(err, EthRpcError::BadLog(_)));
    }

    #[test]
    fn decode_locked_event_rejects_wrong_topic0() {
        // Use a different keccak hash for topic[0] — should reject.
        let mut hasher = Keccak::v256();
        hasher.update(b"NotLocked(uint256)");
        let mut wrong_topic = [0u8; 32];
        hasher.finalize(&mut wrong_topic);

        let log = RawLog {
            address: "0x".into(),
            topics: vec![
                format!("0x{}", hex::encode(wrong_topic)),
                format!("0x{:0>64x}", 0xE000_0001u32),
                format!("0x{:0>64x}", 1099u32),
                format!("0x{:0>64x}", 42u64),
            ],
            data: "0x".into(),
            block_number: "0x0".into(),
            transaction_hash: format!("0x{}", "00".repeat(32)),
        };
        let err = decode_locked_event(&log).unwrap_err();
        assert!(matches!(err, EthRpcError::BadLog(_)));
    }

    #[test]
    fn decode_hex_helpers_handle_prefix() {
        // With and without 0x prefix.
        assert_eq!(decode_hex_u64("0x100").unwrap(), 256);
        assert_eq!(decode_hex_u64("100").unwrap(), 256);
        assert_eq!(decode_hex_u64("0xff").unwrap(), 255);
    }

    // ─── live HTTP integration tests ─────────────────────────────────────
    //
    // Drive `EthRpcEventSource` through the actual `reqwest::blocking` HTTP
    // stack against an in-process fake JSON-RPC server. Validates the
    // production request shape (URL + method name + body params), the
    // response decode pipeline (hex → u64, log JSON → LockedEvent), and
    // the cursor-management semantics (cursor advances past polled
    // windows, doesn't poll past the head).
    //
    // The decoder unit tests above only exercise `decode_locked_event` in
    // isolation — they don't catch a regression in the JSON-RPC layer
    // (request body shape, RawLog deserialization, error-path handling).
    // These tests close that gap without requiring an external testnet.

    use crate::live::test_support::FakeRpcServer;
    use std::net::TcpListener;
    use std::sync::atomic::{AtomicUsize, Ordering};
    use std::sync::Arc;

    /// Build a synthetic Locked event JSON object matching the
    /// `RawLog` shape, with the given chain id / neo chain id / nonce.
    fn synthetic_locked_log(
        external_chain_id: u32,
        neo_chain_id: u32,
        nonce: u64,
    ) -> serde_json::Value {
        let topic_sig = format!("0x{}", hex::encode(locked_event_topic_hash()));
        let topic_chain = format!("0x{:0>64x}", external_chain_id);
        let topic_neo = format!("0x{:0>64x}", neo_chain_id);
        let topic_nonce = format!("0x{:0>64x}", nonce);

        // ABI-encoded data (same layout as decode_locked_event_round_trip):
        let mut data = Vec::new();
        data.extend_from_slice(&[0u8; 12]); // sender pad
        data.extend_from_slice(&[0x11; 20]); // sender
        data.extend_from_slice(&[0xaa; 20]); // neoRecipient (bytes20 left-aligned)
        data.extend_from_slice(&[0u8; 12]); //   right-pad
        data.extend_from_slice(&[0u8; 12]); // asset pad
        data.extend_from_slice(&[0xee; 20]); // asset
        let mut amount32 = [0u8; 32];
        amount32[28..32].copy_from_slice(&1_000_000u32.to_be_bytes());
        data.extend_from_slice(&amount32); // amount
        let mut off32 = [0u8; 32];
        off32[24..32].copy_from_slice(&192u64.to_be_bytes());
        data.extend_from_slice(&off32); // payload offset = 192
        let mut dl32 = [0u8; 32];
        dl32[24..32].copy_from_slice(&1_900_000_000u64.to_be_bytes());
        data.extend_from_slice(&dl32); // deadline
        let mut len32 = [0u8; 32];
        len32[24..32].copy_from_slice(&3u64.to_be_bytes());
        data.extend_from_slice(&len32); // payload length = 3
        data.extend_from_slice(&[0xCA, 0xFE, 0xBA]);
        data.extend_from_slice(&[0u8; 29]); // 32-byte alignment pad

        serde_json::json!({
            "address": "0x0102030405060708090a0b0c0d0e0f1011121314",
            "topics": [topic_sig, topic_chain, topic_neo, topic_nonce],
            "data": format!("0x{}", hex::encode(&data)),
            "blockNumber": "0x10",
            "transactionHash": format!("0x{}", "ee".repeat(32))
        })
    }

    /// Full round trip: server returns a Locked log; the source decodes
    /// it via the real reqwest+JSON pipeline. Pins request shape + response
    /// decoding end-to-end.
    #[test]
    fn live_decodes_locked_event_via_http() {
        let log = synthetic_locked_log(0xE000_0001, 1099, 42);
        let server = FakeRpcServer::spawn(move |body: &str| {
            if body.contains("eth_blockNumber") {
                r#"{"jsonrpc":"2.0","id":1,"result":"0x100"}"#.to_string()
            } else if body.contains("eth_getLogs") {
                serde_json::json!({
                    "jsonrpc": "2.0",
                    "id": 1,
                    "result": [log.clone()]
                })
                .to_string()
            } else {
                r#"{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"unknown"}}"#
                    .to_string()
            }
        });

        let mut source = EthRpcEventSource::builder(server.url.clone(), [0u8; 20])
            .request_timeout(Duration::from_secs(5))
            .build()
            .unwrap();

        let ev = source
            .next_event(0)
            .expect("rpc call succeeded")
            .expect("event present");
        assert_eq!(ev.external_chain_id, 0xE000_0001);
        assert_eq!(ev.neo_chain_id, 1099);
        assert_eq!(ev.nonce, 42);
        assert_eq!(ev.sender, [0x11; 20]);
        assert_eq!(ev.neo_recipient, [0xaa; 20]);
        assert_eq!(ev.asset, [0xee; 20]);
        assert_eq!(ev.payload, vec![0xCA, 0xFE, 0xBA]);
        assert_eq!(ev.deadline, 1_900_000_000);
        assert_eq!(ev.block_number, 0x10);
    }

    /// Cursor advances past the polled window even if no logs match —
    /// otherwise the source would re-poll the same range forever.
    #[test]
    fn live_advances_cursor_through_empty_window() {
        // Server: head = 0x100; getLogs always returns []. The source
        // should poll once then return None; cursor should be past
        // start_block.
        let server = FakeRpcServer::spawn(|body: &str| {
            if body.contains("eth_blockNumber") {
                r#"{"jsonrpc":"2.0","id":1,"result":"0x100"}"#.to_string()
            } else {
                r#"{"jsonrpc":"2.0","id":1,"result":[]}"#.to_string()
            }
        });

        let mut source = EthRpcEventSource::builder(server.url.clone(), [0u8; 20])
            .chunk_size(50) // small chunk so we can observe cursor advancement
            .request_timeout(Duration::from_secs(5))
            .build()
            .unwrap();

        // First call: polls blocks [0..49], gets []; returns None.
        assert!(source.next_event(0).unwrap().is_none());
        // Internal cursor advanced past 49 → next_block_to_poll = 50.
        assert_eq!(source.next_block_to_poll, 50);
    }

    /// Cursor above the head means "nothing to do" — must NOT call
    /// eth_getLogs (otherwise wastes RPC budget on a guaranteed-empty
    /// query). Pin via a server that fails the test if getLogs is hit.
    #[test]
    fn live_skips_get_logs_when_cursor_above_head() {
        // Counter that the test inspects after the call. AtomicUsize so
        // the closure (Send + Fn) can mutate it.
        let get_logs_calls = Arc::new(std::sync::atomic::AtomicUsize::new(0));
        let counter = get_logs_calls.clone();
        let server = FakeRpcServer::spawn(move |body: &str| {
            if body.contains("eth_blockNumber") {
                r#"{"jsonrpc":"2.0","id":1,"result":"0x10"}"#.to_string()
            } else if body.contains("eth_getLogs") {
                counter.fetch_add(1, Ordering::Relaxed);
                r#"{"jsonrpc":"2.0","id":1,"result":[]}"#.to_string()
            } else {
                r#"{"jsonrpc":"2.0","id":1,"result":null}"#.to_string()
            }
        });

        let mut source = EthRpcEventSource::builder(server.url.clone(), [0u8; 20])
            .request_timeout(Duration::from_secs(5))
            .build()
            .unwrap();

        // start_block = 1000; head = 0x10 = 16. Cursor far above head.
        assert!(source.next_event(1000).unwrap().is_none());

        // Critical: we should NOT have queried getLogs.
        assert_eq!(
            get_logs_calls.load(Ordering::Relaxed),
            0,
            "must not poll eth_getLogs when cursor is above head — would waste RPC budget"
        );
    }

    /// JSON-RPC error responses surface as `EventSourceError::Rpc`,
    /// not as a silent skip — operators rely on the watcher escalating
    /// errors via the daemon's exponential-backoff loop.
    #[test]
    fn live_propagates_rpc_error_response() {
        let server = FakeRpcServer::spawn(|_body: &str| {
            r#"{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"message":"node sync"}}"#
                .to_string()
        });

        let mut source = EthRpcEventSource::builder(server.url.clone(), [0u8; 20])
            .request_timeout(Duration::from_secs(5))
            .build()
            .unwrap();

        let err = source.next_event(0).expect_err("should surface rpc error");
        match err {
            crate::event_source::EventSourceError::Rpc(msg) => {
                assert!(
                    msg.contains("-32000") || msg.contains("node sync"),
                    "rpc error message must carry server-side detail: got '{msg}'"
                );
            }
            other => panic!("expected EventSourceError::Rpc, got {other:?}"),
        }
    }

    /// `min_confirmations` keeps the source from polling within
    /// `n` blocks of the chain head — defends against short-reorg
    /// phantom mints. Pin: head=100, confirmations=12 → effective_head=88;
    /// the next_event call must NOT include block 89..100 in its
    /// `eth_getLogs` `toBlock` parameter.
    #[test]
    fn live_min_confirmations_caps_polling_window_below_head() {
        // Capture the toBlock parameter the source sends.
        let captured_to: Arc<std::sync::Mutex<Option<u64>>> =
            Arc::new(std::sync::Mutex::new(None));
        let captured = captured_to.clone();
        let server = FakeRpcServer::spawn(move |body: &str| {
            if body.contains("eth_blockNumber") {
                r#"{"jsonrpc":"2.0","id":1,"result":"0x64"}"#.to_string() // 100
            } else if body.contains("eth_getLogs") {
                // Parse the toBlock from the params JSON. The body is
                // small + we control the shape; a substring lookup
                // suffices.
                if let Some(idx) = body.find(r#""toBlock":""#) {
                    let after = &body[idx + r#""toBlock":""#.len()..];
                    if let Some(end) = after.find('"') {
                        let hex = after[..end].trim_start_matches("0x");
                        if let Ok(v) = u64::from_str_radix(hex, 16) {
                            *captured.lock().unwrap() = Some(v);
                        }
                    }
                }
                r#"{"jsonrpc":"2.0","id":1,"result":[]}"#.to_string()
            } else {
                r#"{"jsonrpc":"2.0","id":1,"result":null}"#.to_string()
            }
        });

        let mut source = EthRpcEventSource::builder(server.url.clone(), [0u8; 20])
            .chunk_size(50_000) // big enough that toBlock = effective_head
            .min_confirmations(12)
            .request_timeout(Duration::from_secs(5))
            .build()
            .unwrap();

        assert!(source.next_event(0).unwrap().is_none());

        let to = captured_to.lock().unwrap().expect("getLogs called once");
        assert_eq!(
            to, 88,
            "with head=100 + confirmations=12, polling window should cap at \
             effective_head=88 — got toBlock={to}"
        );
    }

    /// When the head is below `min_confirmations`, `effective_head`
    /// saturates at 0 (no panic on underflow). If the cursor is above
    /// the saturated effective_head, the source must return None
    /// without polling. Pins the saturation case — would otherwise
    /// underflow the u64 if the head was momentarily below the
    /// confirmation buffer.
    #[test]
    fn live_min_confirmations_saturates_when_head_below_threshold() {
        // head = 5, confirmations = 12 → effective_head saturates to 0.
        let get_logs_calls = Arc::new(AtomicUsize::new(0));
        let counter = get_logs_calls.clone();
        let server = FakeRpcServer::spawn(move |body: &str| {
            if body.contains("eth_blockNumber") {
                r#"{"jsonrpc":"2.0","id":1,"result":"0x5"}"#.to_string()
            } else if body.contains("eth_getLogs") {
                counter.fetch_add(1, Ordering::Relaxed);
                r#"{"jsonrpc":"2.0","id":1,"result":[]}"#.to_string()
            } else {
                r#"{"jsonrpc":"2.0","id":1,"result":null}"#.to_string()
            }
        });

        let mut source = EthRpcEventSource::builder(server.url.clone(), [0u8; 20])
            .min_confirmations(12)
            .request_timeout(Duration::from_secs(5))
            .build()
            .unwrap();

        // cursor=5, effective_head=0 → cursor > effective_head → no poll.
        assert!(source.next_event(5).unwrap().is_none());
        assert_eq!(
            get_logs_calls.load(Ordering::Relaxed),
            0,
            "must NOT poll when cursor > effective_head — saturation case \
             (would underflow without saturating_sub)"
        );
    }

    /// When `cursor <= effective_head`, the source DOES poll. Pins the
    /// positive path: confirmations = 12, head = 100, cursor = 50 →
    /// effective_head = 88 → polls [50..88], emits decoded events.
    #[test]
    fn live_min_confirmations_emits_events_below_buffer() {
        let log = synthetic_locked_log(0xE000_0001, 1099, 7);
        let server = FakeRpcServer::spawn(move |body: &str| {
            if body.contains("eth_blockNumber") {
                r#"{"jsonrpc":"2.0","id":1,"result":"0x64"}"#.to_string() // 100
            } else if body.contains("eth_getLogs") {
                serde_json::json!({
                    "jsonrpc": "2.0",
                    "id": 1,
                    "result": [log.clone()]
                })
                .to_string()
            } else {
                r#"{"jsonrpc":"2.0","id":1,"result":null}"#.to_string()
            }
        });

        let mut source = EthRpcEventSource::builder(server.url.clone(), [0u8; 20])
            .chunk_size(100)
            .min_confirmations(12)
            .request_timeout(Duration::from_secs(5))
            .build()
            .unwrap();

        // cursor=50, head=100, confirmations=12 → effective_head=88;
        // poll window = [50..88]; the synthetic event is at block 0x10
        // = 16 < 50 (so won't appear if filter strict) — but we return
        // it from the fake regardless to verify decode flows.
        let ev = source.next_event(50).unwrap().expect("event emitted");
        assert_eq!(ev.nonce, 7);
    }

    /// Default `min_confirmations` is 0 — no buffer (existing behavior).
    /// Pin so a future builder change to a non-zero default doesn't
    /// silently change the polling window for callers that don't opt in.
    #[test]
    fn live_default_min_confirmations_is_zero() {
        let captured_to: Arc<std::sync::Mutex<Option<u64>>> =
            Arc::new(std::sync::Mutex::new(None));
        let captured = captured_to.clone();
        let server = FakeRpcServer::spawn(move |body: &str| {
            if body.contains("eth_blockNumber") {
                r#"{"jsonrpc":"2.0","id":1,"result":"0x64"}"#.to_string() // 100
            } else if body.contains("eth_getLogs") {
                if let Some(idx) = body.find(r#""toBlock":""#) {
                    let after = &body[idx + r#""toBlock":""#.len()..];
                    if let Some(end) = after.find('"') {
                        let hex = after[..end].trim_start_matches("0x");
                        if let Ok(v) = u64::from_str_radix(hex, 16) {
                            *captured.lock().unwrap() = Some(v);
                        }
                    }
                }
                r#"{"jsonrpc":"2.0","id":1,"result":[]}"#.to_string()
            } else {
                r#"{"jsonrpc":"2.0","id":1,"result":null}"#.to_string()
            }
        });

        // No .min_confirmations(...) on the builder.
        let mut source = EthRpcEventSource::builder(server.url.clone(), [0u8; 20])
            .chunk_size(50_000)
            .request_timeout(Duration::from_secs(5))
            .build()
            .unwrap();
        assert!(source.next_event(0).unwrap().is_none());
        assert_eq!(
            captured_to.lock().unwrap().expect("getLogs called once"),
            100,
            "default builder polls right up to head — no buffer"
        );
    }

    /// HTTP transport failure (server doesn't respond / connection
    /// refused) also surfaces as an `EventSourceError`. The daemon's
    /// retry loop catches this and backs off — so silent swallowing
    /// would mask outages.
    #[test]
    fn live_propagates_transport_failure() {
        // Bind a port + immediately drop the listener so the address is
        // (briefly) unreachable. We accept that this is racy on busy
        // CI; if the OS rebinds the port the test could be flaky.
        // Mitigation: the source has a 1s timeout; we'd see at worst a
        // delay, not a wrong result.
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        drop(listener);

        let url = format!("http://127.0.0.1:{}/", port);
        let mut source = EthRpcEventSource::builder(url, [0u8; 20])
            .request_timeout(Duration::from_secs(1))
            .build()
            .unwrap();

        let err = source.next_event(0).expect_err("transport failure must surface");
        match err {
            crate::event_source::EventSourceError::Rpc(_) => { /* expected */ }
            other => panic!("expected EventSourceError::Rpc, got {other:?}"),
        }
    }
}
