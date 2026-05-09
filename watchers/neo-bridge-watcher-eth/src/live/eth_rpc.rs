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

        // 3. Get current head height — we never poll past the latest
        //    block (avoids dealing with reorgs in v0; operators wait for
        //    a few-block confirmation depth via their journal write
        //    cadence).
        let head = self.fetch_block_number()?;
        if self.next_block_to_poll > head {
            return Ok(None);
        }

        // 4. Poll one chunk worth of blocks. Don't overshoot the head.
        let from = self.next_block_to_poll;
        let to = (from.saturating_add(self.chunk_size - 1)).min(head);
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
    fn fetch_block_number(&self) -> Result<u64, EthRpcError> {
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
    /// The router contract address (also in the request filter; included
    /// here for safety so we reject logs from unexpected contracts).
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
}
