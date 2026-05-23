use serde::Deserialize;

/// Optional health-check HTTP endpoint config. When `bind` is unset
/// the daemon runs without a health server (suitable for one-off
/// CLI runs); k8s/systemd deployments set `bind = "0.0.0.0:9090"`
/// and probe `/healthz` for readiness/liveness.
#[derive(Deserialize, Default)]
pub(crate) struct HealthConfig {
    /// Bind address (e.g. "0.0.0.0:9090" or "127.0.0.1:9090"). Unset
    /// = no health server.
    pub(crate) bind: Option<String>,
    /// Seconds without a successful tick before /healthz returns 503.
    /// Default 120 — covers a 12s poll interval + up to 60s backoff
    /// with margin for one full retry cycle.
    #[serde(default = "default_health_threshold")]
    pub(crate) threshold_secs: u64,
}

fn default_health_threshold() -> u64 {
    120
}
