#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ExecutionError {
    Truncated,
    InvalidVersion(u8),
    OversizedField(&'static str),
}
