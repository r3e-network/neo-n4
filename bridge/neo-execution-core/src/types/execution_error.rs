#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ExecutionError {
    Truncated,
    InvalidVersion(u8),
    OversizedField(&'static str),
    GasExceeded,
}

impl core::fmt::Display for ExecutionError {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        match self {
            ExecutionError::Truncated => write!(f, "input truncated"),
            ExecutionError::InvalidVersion(v) => write!(f, "unsupported version {v}"),
            ExecutionError::OversizedField(field) => write!(f, "field {field} exceeds size limit"),
            ExecutionError::GasExceeded => write!(f, "gas consumed exceeds per-tx limit"),
        }
    }
}

impl core::error::Error for ExecutionError {}
