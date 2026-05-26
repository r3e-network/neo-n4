use super::{EventSource, EventSourceError, LockedEvent};

/// Test fixture: in-memory queue of events, returned in insertion order
/// (or filtered by start_block).
#[derive(Debug)]
pub struct MockEventSource {
    events: Vec<LockedEvent>,
}

impl MockEventSource {
    pub fn new() -> Self {
        Self { events: Vec::new() }
    }

    pub fn push(&mut self, event: LockedEvent) {
        self.events.push(event);
    }

    pub fn pending(&self) -> usize {
        self.events.len()
    }
}

impl Default for MockEventSource {
    fn default() -> Self {
        Self::new()
    }
}

impl EventSource for MockEventSource {
    fn next_event(&mut self, start_block: u64) -> Result<Option<LockedEvent>, EventSourceError> {
        // Find the first event ≥ start_block; pop it (FIFO over qualifying events).
        if let Some(idx) = self
            .events
            .iter()
            .position(|e| e.block_number >= start_block)
        {
            Ok(Some(self.events.remove(idx)))
        } else {
            Ok(None)
        }
    }
}
