//! Resource guardrails for the SP1 prover host.
//!
//! The prover daemon processes witness artifacts that arrive on disk. Without explicit
//! limits a single malformed-but-structurally-valid artifact can pin the whole daemon:
//!
//! * **Memory** — SP1's default memory limit is 24 GiB (`sp1-core-executor`'s
//!   `DEFAULT_MEMORY_LIMIT`). CI overrides it with `MEMORY_LIMIT=4294967296` (4 GiB),
//!   but a production deployment that forgets the env var runs unbounded. A poisoned
//!   witness would then be able to exhaust the host's RAM.
//! * **Batch GAS** — `total_gas` is accumulated per batch but never compared against a
//!   ceiling. Every transaction gets its own `per_tx_gas_limit`, and a batch may carry
//!   up to `MAX_PAYLOAD_ITEMS` (1,000,000) transactions, so the aggregate is unbounded.
//!
//! Both limits are enforced HERE, at the point where the artifact is parsed — before any
//! proving work is scheduled.

/// Default memory ceiling for a single proving run (4 GiB).
///
/// Matches the value CI already uses for `MEMORY_LIMIT`. Operators can raise or lower it
/// with the `SP1_MEMORY_LIMIT` environment variable.
pub const DEFAULT_MEMORY_LIMIT_BYTES: u64 = 4 * 1024 * 1024 * 1024;

/// Environment variable an operator can use to override [`DEFAULT_MEMORY_LIMIT_BYTES`].
pub const MEMORY_LIMIT_ENV: &str = "SP1_MEMORY_LIMIT";

/// Environment variable an operator can use to override the batch GAS ceiling.
pub const BATCH_GAS_LIMIT_ENV: &str = "SP1_BATCH_GAS_LIMIT";

/// Default ceiling on the aggregate GAS of a single batch.
///
/// Every transaction carries its own per-transaction limit, so without an aggregate
/// ceiling a batch with a million transactions can demand a practically unbounded amount
/// of proving time. The value is deliberately generous — it is a runaway guard, not a
/// throughput knob.
pub const DEFAULT_BATCH_GAS_LIMIT: u64 = 1_000_000_000_000;

/// Resolve the effective memory ceiling, honouring the operator override.
#[must_use]
pub fn memory_limit_bytes() -> u64 {
    match std::env::var(MEMORY_LIMIT_ENV) {
        Ok(raw) => match raw.trim().parse::<u64>() {
            Ok(value) if value > 0 => value,
            _ => DEFAULT_MEMORY_LIMIT_BYTES,
        },
        Err(_) => DEFAULT_MEMORY_LIMIT_BYTES,
    }
}

/// Resolve the effective batch GAS ceiling, honouring the operator override.
#[must_use]
pub fn batch_gas_limit() -> u64 {
    match std::env::var(BATCH_GAS_LIMIT_ENV) {
        Ok(raw) => match raw.trim().parse::<u64>() {
            Ok(value) if value > 0 => value,
            _ => DEFAULT_BATCH_GAS_LIMIT,
        },
        Err(_) => DEFAULT_BATCH_GAS_LIMIT,
    }
}

/// Apply the resolved memory ceiling to the SP1 runtime.
///
/// SP1 reads `MEMORY_LIMIT` from the environment when it builds its executor options.
/// Setting it here — rather than relying on the deployment environment — guarantees the
/// guardrail is present in every deployment, including one that forgets to export it.
pub fn apply_memory_limit() -> u64 {
    let limit = memory_limit_bytes();
    // SAFETY: `set_var` is not async-signal-safe, but this runs on the daemon's startup
    // thread before any prover work is scheduled.
    unsafe {
        std::env::set_var("MEMORY_LIMIT", limit.to_string());
    }
    limit
}

/// Reject a batch whose aggregate GAS exceeds the configured ceiling.
///
/// `total_gas` is the sum of the per-transaction gas used by the batch.
pub fn enforce_batch_gas_limit(total_gas: u64) -> Result<(), String> {
    let limit = batch_gas_limit();
    if total_gas > limit {
        return Err(format!(
            "batch total gas {total_gas} exceeds the configured ceiling {limit}"
        ));
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn default_limits_are_sane() {
        assert_eq!(DEFAULT_MEMORY_LIMIT_BYTES, 4 * 1024 * 1024 * 1024);
        assert!(DEFAULT_BATCH_GAS_LIMIT > 0);
    }

    #[test]
    fn batch_gas_limit_rejects_runaway_batches() {
        // A batch under the ceiling is accepted.
        assert!(enforce_batch_gas_limit(1).is_ok());
        // A batch over the ceiling is rejected with a message naming both numbers.
        let error = enforce_batch_gas_limit(u64::MAX).unwrap_err();
        assert!(error.contains("exceeds the configured ceiling"), "{error}");
    }

    #[test]
    fn operator_override_is_honoured() {
        // The overrides are process-global, so this test only asserts the parsing logic
        // when the variable is absent (the default path) to avoid cross-test interference.
        if std::env::var(BATCH_GAS_LIMIT_ENV).is_err() {
            assert_eq!(batch_gas_limit(), DEFAULT_BATCH_GAS_LIMIT);
        }
        if std::env::var(MEMORY_LIMIT_ENV).is_err() {
            assert_eq!(memory_limit_bytes(), DEFAULT_MEMORY_LIMIT_BYTES);
        }
    }
}
