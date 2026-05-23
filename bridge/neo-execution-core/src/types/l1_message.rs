use alloc::vec::Vec;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct L1Message {
    pub bytes: Vec<u8>,
}
