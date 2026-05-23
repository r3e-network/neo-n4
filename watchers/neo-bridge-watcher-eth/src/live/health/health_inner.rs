#[derive(Default)]
pub(super) struct HealthInner {
    pub(super) started_at_unix: u64,
    pub(super) last_tick_at_unix: Option<u64>,
    pub(super) last_tick_success_unix: Option<u64>,
    pub(super) ticks_total: u64,
    pub(super) events_processed: u64,
    pub(super) submissions_total: u64,
    pub(super) journal_cursor: u64,
    pub(super) last_error: Option<String>,
    pub(super) last_error_unix: Option<u64>,
}
