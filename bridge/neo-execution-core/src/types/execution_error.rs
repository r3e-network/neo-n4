#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
pub enum ExecutionError {
    #[error("input truncated")]
    Truncated,
    #[error("unsupported version {0}")]
    InvalidVersion(u8),
    #[error("field {0} exceeds size limit")]
    OversizedField(&'static str),
    #[error("gas consumed exceeds per-tx limit")]
    GasExceeded,
}
