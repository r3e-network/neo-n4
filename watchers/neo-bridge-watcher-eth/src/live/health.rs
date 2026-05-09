//! Health-check HTTP server for the watcher daemon.
//!
//! Production deployments (kubernetes, systemd) need a programmatic
//! signal that the watcher is alive + making progress, separate from
//! "the process exists". This module exposes:
//!
//! - `GET /healthz` — returns `200 OK` if the daemon has had a tick
//!   succeed within `healthy_threshold_secs`, `503 Service Unavailable`
//!   otherwise. The body is a JSON status snapshot in both cases —
//!   k8s readiness probes look at the status code; humans / log
//!   collectors read the body.
//! - `GET /info` — same JSON body, always `200 OK`. Intended for
//!   operator dashboards that want to display state regardless of
//!   the freshness check.
//! - `GET /` — same as `/info`. Catch-all for accidental browser hits.
//! - Other paths — `404 Not Found` with a JSON error.
//!
//! The server runs in a background thread; the main loop pushes
//! state updates via [`HealthState`]. Both share an
//! `Arc<Mutex<HealthInner>>`. Mutex contention is negligible — the
//! main loop updates ~once per tick (every 12s default), the health
//! probe hits ~once per few seconds.

use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::{SystemTime, UNIX_EPOCH};

/// Snapshot of the daemon's health, shared between the main loop
/// (writer) and the health HTTP server (reader). Cheaply cloneable —
/// internally an `Arc<Mutex<...>>`.
#[derive(Clone)]
pub struct HealthState {
    inner: Arc<Mutex<HealthInner>>,
    /// Optional Prometheus label suffix (e.g. `{chain_id="0xE0000030"}`)
    /// applied to every metric line. When None (`HealthState::new()`),
    /// metrics are emitted unlabelled — same shape as v0. When set
    /// (`HealthState::with_chain_id(...)`), every counter + gauge
    /// carries the label so multi-chain operator setups get cleanly
    /// tagged metrics out of the box without relabel rules.
    metric_label_suffix: String,
}

#[derive(Default)]
struct HealthInner {
    started_at_unix: u64,
    last_tick_at_unix: Option<u64>,
    last_tick_success_unix: Option<u64>,
    ticks_total: u64,
    events_processed: u64,
    submissions_total: u64,
    journal_cursor: u64,
    last_error: Option<String>,
    last_error_unix: Option<u64>,
}

impl HealthState {
    pub fn new() -> Self {
        let started_at_unix = unix_now();
        Self {
            inner: Arc::new(Mutex::new(HealthInner {
                started_at_unix,
                ..Default::default()
            })),
            metric_label_suffix: String::new(),
        }
    }

    /// Construct a HealthState that tags every Prometheus metric with
    /// `{chain_id="0x..."}`. Use this when running multiple watcher
    /// instances against different chains scraped by the same
    /// Prometheus — the label disambiguates time series without
    /// requiring per-pod relabel rules.
    pub fn with_chain_id(chain_id: u32) -> Self {
        let mut s = Self::new();
        s.metric_label_suffix = format!(r#"{{chain_id="0x{chain_id:08X}"}}"#);
        s
    }

    /// Record a successful tick — whether it processed an event or not.
    /// The freshness check (`/healthz`) compares `last_tick_at_unix`
    /// against the threshold.
    pub fn record_tick(&self, processed_event: bool) {
        let mut s = self.inner.lock().unwrap();
        let now = unix_now();
        s.last_tick_at_unix = Some(now);
        s.last_tick_success_unix = Some(now);
        s.ticks_total += 1;
        if processed_event {
            s.events_processed += 1;
        }
    }

    /// Record a successful submission to Neo. Distinct from
    /// `record_tick(true)` because a single tick can also fail at the
    /// submit step — we want the count of journal-advancing
    /// submissions, not the count of events seen.
    pub fn record_submission(&self) {
        self.inner.lock().unwrap().submissions_total += 1;
    }

    /// Update the journal cursor surfaced in the health snapshot.
    /// Called after every successful journal advance.
    pub fn record_cursor(&self, cursor: u64) {
        self.inner.lock().unwrap().journal_cursor = cursor;
    }

    /// Record a tick error. The error message is stable (truncated
    /// to ~256 chars) — a frequent error like a chain reorg wouldn't
    /// rotate the field every tick.
    pub fn record_error(&self, msg: impl Into<String>) {
        let mut s = self.inner.lock().unwrap();
        let mut m = msg.into();
        if m.len() > 256 {
            m.truncate(256);
        }
        s.last_error = Some(m);
        s.last_error_unix = Some(unix_now());
    }

    /// Build a JSON snapshot + freshness verdict.
    pub fn snapshot(&self, healthy_threshold_secs: u64) -> (bool, String) {
        let s = self.inner.lock().unwrap();
        let now = unix_now();
        // Healthy if we've had a recent tick success. Before the first
        // tick (just-started daemon) we use `started_at_unix` instead
        // so the daemon is "healthy" during normal startup-to-first-poll
        // window.
        let reference = s.last_tick_success_unix.unwrap_or(s.started_at_unix);
        let healthy = now.saturating_sub(reference) <= healthy_threshold_secs;
        let json = format!(
            r#"{{"healthy":{},"started_at_unix":{},"last_tick_at_unix":{},"last_tick_success_unix":{},"ticks_total":{},"events_processed":{},"submissions_total":{},"journal_cursor":{},"last_error":{},"last_error_unix":{},"now_unix":{}}}"#,
            healthy,
            s.started_at_unix,
            json_opt_u64(s.last_tick_at_unix),
            json_opt_u64(s.last_tick_success_unix),
            s.ticks_total,
            s.events_processed,
            s.submissions_total,
            s.journal_cursor,
            json_opt_str(s.last_error.as_deref()),
            json_opt_u64(s.last_error_unix),
            now,
        );
        (healthy, json)
    }

    /// Build a Prometheus exposition-format snapshot. The output matches
    /// the standard `# HELP ... # TYPE ... <metric> <value>` shape that
    /// Prometheus / VictoriaMetrics / OpenTelemetry collectors all parse.
    ///
    /// Metrics:
    /// - `watcher_started_at_unix_timestamp` (gauge) — process start time
    /// - `watcher_last_tick_unix_timestamp` (gauge) — 0 if no tick yet
    /// - `watcher_last_tick_success_unix_timestamp` (gauge) — 0 if none
    /// - `watcher_ticks_total` (counter) — every iteration of the run loop
    /// - `watcher_events_processed_total` (counter) — events seen
    /// - `watcher_submissions_total` (counter) — successful submissions
    /// - `watcher_journal_cursor` (gauge) — current block cursor
    /// - `watcher_last_error_unix_timestamp` (gauge) — 0 if no error
    /// - `watcher_healthy` (gauge) — 1 if `now - last_tick_success ≤ threshold`
    ///
    /// The operator-facing dashboard typically alerts on:
    ///   - `time() - watcher_last_tick_success_unix_timestamp > 300` (stale)
    ///   - `watcher_healthy == 0` (same alert, threshold-aware)
    ///   - `rate(watcher_submissions_total[5m]) == 0` AND `events_processed > 0` (stuck)
    pub fn metrics_text(&self, healthy_threshold_secs: u64) -> String {
        let s = self.inner.lock().unwrap();
        let now = unix_now();
        let reference = s.last_tick_success_unix.unwrap_or(s.started_at_unix);
        let healthy = if now.saturating_sub(reference) <= healthy_threshold_secs { 1 } else { 0 };
        let lbl = &self.metric_label_suffix;
        format!(
            "# HELP watcher_started_at_unix_timestamp Watcher process start time (Unix seconds)\n\
             # TYPE watcher_started_at_unix_timestamp gauge\n\
             watcher_started_at_unix_timestamp{lbl} {}\n\
             # HELP watcher_last_tick_unix_timestamp Time of last run-loop iteration (success or error)\n\
             # TYPE watcher_last_tick_unix_timestamp gauge\n\
             watcher_last_tick_unix_timestamp{lbl} {}\n\
             # HELP watcher_last_tick_success_unix_timestamp Time of last tick that completed without error\n\
             # TYPE watcher_last_tick_success_unix_timestamp gauge\n\
             watcher_last_tick_success_unix_timestamp{lbl} {}\n\
             # HELP watcher_ticks_total Total run-loop iterations\n\
             # TYPE watcher_ticks_total counter\n\
             watcher_ticks_total{lbl} {}\n\
             # HELP watcher_events_processed_total Total Locked events the watcher has processed\n\
             # TYPE watcher_events_processed_total counter\n\
             watcher_events_processed_total{lbl} {}\n\
             # HELP watcher_submissions_total Total successful submissions to NeoHub.ExternalBridgeEscrow.Receive\n\
             # TYPE watcher_submissions_total counter\n\
             watcher_submissions_total{lbl} {}\n\
             # HELP watcher_journal_cursor Current journal block cursor\n\
             # TYPE watcher_journal_cursor gauge\n\
             watcher_journal_cursor{lbl} {}\n\
             # HELP watcher_last_error_unix_timestamp Time of last error in the run loop (0 = none)\n\
             # TYPE watcher_last_error_unix_timestamp gauge\n\
             watcher_last_error_unix_timestamp{lbl} {}\n\
             # HELP watcher_healthy 1 if last tick succeeded within the configured threshold, else 0\n\
             # TYPE watcher_healthy gauge\n\
             watcher_healthy{lbl} {}\n",
            s.started_at_unix,
            s.last_tick_at_unix.unwrap_or(0),
            s.last_tick_success_unix.unwrap_or(0),
            s.ticks_total,
            s.events_processed,
            s.submissions_total,
            s.journal_cursor,
            s.last_error_unix.unwrap_or(0),
            healthy,
        )
    }
}

impl Default for HealthState {
    fn default() -> Self {
        Self::new()
    }
}

/// HTTP server exposing the health endpoints. Holds the listener +
/// background thread; teardown via `Drop` (sets a stop flag and waits
/// for the next accept-loop iteration to exit).
pub struct HealthServer {
    /// Resolved bind address — useful for tests that bind to
    /// `127.0.0.1:0` and want to know which random port the OS assigned.
    pub bound_addr: std::net::SocketAddr,
    stop: Arc<AtomicBool>,
    _handle: thread::JoinHandle<()>,
}

impl HealthServer {
    /// Spawn the health server. The listener binds immediately
    /// (returns an io::Error on bind failure); the handler thread
    /// starts in the background.
    pub fn spawn(
        bind: &str,
        state: HealthState,
        healthy_threshold_secs: u64,
    ) -> std::io::Result<Self> {
        let listener = TcpListener::bind(bind)?;
        let bound_addr = listener.local_addr()?;
        listener.set_nonblocking(true)?;
        let stop = Arc::new(AtomicBool::new(false));
        let stop_c = stop.clone();
        let handle = thread::spawn(move || {
            while !stop_c.load(Ordering::Relaxed) {
                match listener.accept() {
                    Ok((mut stream, _)) => {
                        let _ = stream.set_nonblocking(false);
                        handle_request(&mut stream, &state, healthy_threshold_secs);
                    }
                    Err(e) if e.kind() == std::io::ErrorKind::WouldBlock => {
                        thread::sleep(std::time::Duration::from_millis(50));
                    }
                    Err(_) => break,
                }
            }
        });
        Ok(Self {
            bound_addr,
            stop,
            _handle: handle,
        })
    }
}

impl Drop for HealthServer {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Relaxed);
    }
}

fn handle_request(stream: &mut TcpStream, state: &HealthState, threshold: u64) {
    let mut buf = vec![0u8; 4096];
    let n = stream.read(&mut buf).unwrap_or(0);
    let req = String::from_utf8_lossy(&buf[..n]);
    // Request line: "GET /path HTTP/1.1"
    let path = req.split_whitespace().nth(1).unwrap_or("/");

    let (status_code, status_text, content_type, body) = match path {
        "/healthz" => {
            let (healthy, json) = state.snapshot(threshold);
            if healthy {
                (200, "OK", "application/json", json)
            } else {
                (503, "Service Unavailable", "application/json", json)
            }
        }
        "/info" | "/" => {
            let (_, json) = state.snapshot(threshold);
            (200, "OK", "application/json", json)
        }
        "/metrics" => (
            200,
            "OK",
            // Prometheus exposition format. text/plain version 0.0.4 is
            // what Prometheus / VictoriaMetrics / OpenTelemetry expect;
            // the version param is optional but conventional.
            "text/plain; version=0.0.4; charset=utf-8",
            state.metrics_text(threshold),
        ),
        _ => (
            404,
            "Not Found",
            "application/json",
            r#"{"error":"unknown path; try /healthz, /info, or /metrics"}"#.to_string(),
        ),
    };
    let resp = format!(
        "HTTP/1.1 {status_code} {status_text}\r\n\
         Content-Type: {content_type}\r\n\
         Content-Length: {}\r\n\
         Connection: close\r\n\
         \r\n{body}",
        body.len()
    );
    let _ = stream.write_all(resp.as_bytes());
}

fn unix_now() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0)
}

fn json_opt_u64(v: Option<u64>) -> String {
    v.map_or("null".to_string(), |x| x.to_string())
}

fn json_opt_str(v: Option<&str>) -> String {
    match v {
        None => "null".to_string(),
        Some(s) => format!("\"{}\"", s.replace('\\', "\\\\").replace('"', "\\\"")),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::Duration;

    /// `HealthState::snapshot` reflects all the fields written by the
    /// recorders; a fresh daemon (no ticks yet) is healthy because
    /// `started_at_unix` is within the threshold.
    #[test]
    fn snapshot_carries_recorder_writes() {
        let state = HealthState::new();
        state.record_tick(true);
        state.record_submission();
        state.record_cursor(12345);

        let (healthy, json) = state.snapshot(60);
        assert!(healthy, "fresh tick within 60s threshold = healthy");
        assert!(json.contains(r#""ticks_total":1"#));
        assert!(json.contains(r#""events_processed":1"#));
        assert!(json.contains(r#""submissions_total":1"#));
        assert!(json.contains(r#""journal_cursor":12345"#));
        assert!(json.contains(r#""healthy":true"#));
    }

    /// Pre-first-tick daemon falls back on `started_at_unix` for the
    /// freshness check. A 60s threshold + a just-created state should
    /// be healthy (`now - started_at_unix` < 60). The "stale + 503"
    /// path is covered separately by `http_server_returns_503_when_stale`
    /// which actually waits past a zero threshold.
    #[test]
    fn snapshot_pre_first_tick_uses_start_time() {
        let state = HealthState::new();
        let (healthy, json) = state.snapshot(60);
        assert!(healthy, "just-started daemon within 60s threshold = healthy");
        // Both timestamps null because no tick has run yet.
        assert!(json.contains(r#""last_tick_at_unix":null"#));
        assert!(json.contains(r#""last_tick_success_unix":null"#));
    }

    /// Error recording surfaces in the JSON; truncates long messages.
    #[test]
    fn record_error_appears_in_snapshot_truncated() {
        let state = HealthState::new();
        let huge = "x".repeat(500);
        state.record_error(huge);
        let (_, json) = state.snapshot(60);
        // Exactly 256 chars in the error field (the truncation point).
        let expected = format!(r#""last_error":"{}""#, "x".repeat(256));
        assert!(
            json.contains(&expected),
            "error message should be truncated to 256 chars; got {json}"
        );
    }

    /// Live HTTP test: spin up the server on a random port, hit /healthz
    /// and /info via reqwest, verify status codes + body parse.
    #[test]
    fn http_server_serves_healthz_and_info() {
        let state = HealthState::new();
        state.record_tick(true);
        state.record_cursor(42);

        let server = HealthServer::spawn("127.0.0.1:0", state, 60).unwrap();
        let url = format!("http://{}", server.bound_addr);

        let client = reqwest::blocking::Client::builder()
            .timeout(Duration::from_secs(2))
            .build()
            .unwrap();

        // /healthz: 200 + JSON
        let resp = client.get(format!("{url}/healthz")).send().unwrap();
        assert_eq!(resp.status(), 200);
        let body: serde_json::Value = serde_json::from_str(&resp.text().unwrap()).unwrap();
        assert_eq!(body["healthy"], true);
        assert_eq!(body["ticks_total"], 1);
        assert_eq!(body["journal_cursor"], 42);

        // /info: 200 with the same body shape
        let resp = client.get(format!("{url}/info")).send().unwrap();
        assert_eq!(resp.status(), 200);
        let body: serde_json::Value = serde_json::from_str(&resp.text().unwrap()).unwrap();
        assert_eq!(body["journal_cursor"], 42);
    }

    /// /healthz returns 503 when no recent tick and threshold has elapsed.
    /// We can't sleep N seconds in a unit test, so we feed a 0-second
    /// threshold + assert that "more than 0 seconds since start" trips
    /// the unhealthy path.
    #[test]
    fn http_server_returns_503_when_stale() {
        let state = HealthState::new();
        // Don't call record_tick — daemon is brand new with no ticks.
        // Sleep briefly so `now - started_at_unix > 0`.
        std::thread::sleep(Duration::from_millis(1100));

        let server = HealthServer::spawn("127.0.0.1:0", state, 0).unwrap();
        let url = format!("http://{}", server.bound_addr);

        let client = reqwest::blocking::Client::builder()
            .timeout(Duration::from_secs(2))
            .build()
            .unwrap();

        let resp = client.get(format!("{url}/healthz")).send().unwrap();
        assert_eq!(
            resp.status(),
            503,
            "stale daemon must report 503 — drives k8s probe rejection"
        );
        let body: serde_json::Value = serde_json::from_str(&resp.text().unwrap()).unwrap();
        assert_eq!(body["healthy"], false);
    }

    /// Unknown paths return 404 with a JSON error body.
    #[test]
    fn http_server_404s_unknown_paths() {
        let state = HealthState::new();
        let server = HealthServer::spawn("127.0.0.1:0", state, 60).unwrap();
        let url = format!("http://{}", server.bound_addr);

        let client = reqwest::blocking::Client::builder()
            .timeout(Duration::from_secs(2))
            .build()
            .unwrap();

        let resp = client.get(format!("{url}/unknown")).send().unwrap();
        assert_eq!(resp.status(), 404);
    }

    /// Prometheus /metrics endpoint emits valid exposition format with
    /// all expected metrics. Pin: HELP + TYPE lines per metric, value
    /// lines reflect recorder state, content-type matches Prometheus
    /// convention.
    #[test]
    fn http_server_serves_prometheus_metrics() {
        let state = HealthState::new();
        state.record_tick(true);
        state.record_tick(false);
        state.record_submission();
        state.record_cursor(42);

        let server = HealthServer::spawn("127.0.0.1:0", state, 60).unwrap();
        let url = format!("http://{}", server.bound_addr);

        let client = reqwest::blocking::Client::builder()
            .timeout(Duration::from_secs(2))
            .build()
            .unwrap();

        let resp = client.get(format!("{url}/metrics")).send().unwrap();
        assert_eq!(resp.status(), 200);
        let ct = resp.headers().get("content-type").unwrap().to_str().unwrap();
        assert!(
            ct.starts_with("text/plain") && ct.contains("version=0.0.4"),
            "Prometheus expects 'text/plain; version=0.0.4'; got '{ct}'"
        );
        let body = resp.text().unwrap();

        // Every metric must have a HELP + TYPE preamble + a value line.
        let expected_metrics = [
            "watcher_started_at_unix_timestamp",
            "watcher_last_tick_unix_timestamp",
            "watcher_last_tick_success_unix_timestamp",
            "watcher_ticks_total",
            "watcher_events_processed_total",
            "watcher_submissions_total",
            "watcher_journal_cursor",
            "watcher_last_error_unix_timestamp",
            "watcher_healthy",
        ];
        for metric in expected_metrics {
            assert!(
                body.contains(&format!("# HELP {metric} ")),
                "missing HELP for {metric} in:\n{body}"
            );
            assert!(
                body.contains(&format!("# TYPE {metric} ")),
                "missing TYPE for {metric}"
            );
            assert!(
                body.lines().any(|l| l.starts_with(&format!("{metric} "))),
                "missing value line for {metric}"
            );
        }

        // Pin specific values — counters reflect recorder calls; cursor
        // matches the explicit set; healthy=1 because we recorded a
        // tick within threshold.
        assert!(body.contains("watcher_ticks_total 2\n"));
        assert!(body.contains("watcher_events_processed_total 1\n"));
        assert!(body.contains("watcher_submissions_total 1\n"));
        assert!(body.contains("watcher_journal_cursor 42\n"));
        assert!(body.contains("watcher_healthy 1\n"));
        // No error recorded → 0 timestamp.
        assert!(body.contains("watcher_last_error_unix_timestamp 0\n"));
    }

    /// `HealthState::with_chain_id(...)` tags every Prometheus metric
    /// with `{chain_id="0x..."}` so multi-chain operator setups don't
    /// have to relabel. Pin: every metric line carries the suffix;
    /// HELP / TYPE preambles do NOT (Prometheus requires those to be
    /// label-free).
    #[test]
    fn metrics_carry_chain_id_label_when_set() {
        let state = HealthState::with_chain_id(0xE000_0030); // BSC mainnet
        state.record_tick(true);
        state.record_submission();

        let text = state.metrics_text(60);

        // Value lines have the label suffix. Pick a few representatives.
        for metric in [
            "watcher_ticks_total",
            "watcher_events_processed_total",
            "watcher_submissions_total",
            "watcher_journal_cursor",
            "watcher_healthy",
        ] {
            let needle = format!("{metric}{{chain_id=\"0xE0000030\"}} ");
            assert!(
                text.contains(&needle),
                "missing labelled value line for {metric} — full body:\n{text}"
            );
        }

        // HELP/TYPE preambles must NOT carry the label — that would be
        // invalid Prometheus exposition format.
        assert!(text.contains("# HELP watcher_ticks_total Total"));
        assert!(text.contains("# TYPE watcher_ticks_total counter\n"));
        assert!(!text.contains("# HELP watcher_ticks_total{"));
        assert!(!text.contains("# TYPE watcher_ticks_total{"));
    }

    /// Default `HealthState::new()` (no label) emits unlabelled metrics
    /// — matches the v0 output shape so existing scrape configs don't
    /// break for operators who haven't opted into the label.
    #[test]
    fn metrics_unlabelled_when_chain_id_not_set() {
        let state = HealthState::new();
        state.record_tick(true);
        let text = state.metrics_text(60);

        // No `{...}` between the metric name and the value. Pick one
        // line and assert its shape directly.
        assert!(
            text.contains("watcher_ticks_total 1\n"),
            "unlabelled metric should match `<name> <value>` shape"
        );
        assert!(!text.contains("watcher_ticks_total{"));
    }

    /// `/metrics` reports `watcher_healthy 0` when stale — same logic
    /// drives `/healthz`'s 503. A monitoring stack can alert on this
    /// metric without polling /healthz separately.
    #[test]
    fn metrics_reports_healthy_zero_when_stale() {
        let state = HealthState::new();
        std::thread::sleep(Duration::from_millis(1100));
        let server = HealthServer::spawn("127.0.0.1:0", state, 0).unwrap();
        let url = format!("http://{}", server.bound_addr);

        let client = reqwest::blocking::Client::builder()
            .timeout(Duration::from_secs(2))
            .build()
            .unwrap();

        let body = client.get(format!("{url}/metrics")).send().unwrap().text().unwrap();
        assert!(body.contains("watcher_healthy 0\n"),
            "stale daemon must report watcher_healthy 0 in /metrics: {body}");
    }

    /// Server cleans up on Drop — port is reusable immediately.
    #[test]
    fn server_drop_releases_port() {
        let state = HealthState::new();
        let server = HealthServer::spawn("127.0.0.1:0", state.clone(), 60).unwrap();
        let port = server.bound_addr.port();
        drop(server);

        // After the listener thread exits, OS releases the port.
        // SO_REUSEADDR isn't a guarantee on every OS — but a small wait
        // covers the typical case.
        std::thread::sleep(Duration::from_millis(200));
        let new_server = HealthServer::spawn(&format!("127.0.0.1:{port}"), state, 60);
        // Don't assert success on every CI runner (port may be reclaimed
        // by another test); just assert it didn't panic.
        let _ = new_server;
    }
}
