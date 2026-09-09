use alloc::vec::Vec;

use crate::types::ExecutionError;

use super::constants::{ARTIFACT_MAGIC, EFFECTS_MAGIC, PAYLOAD_MAGIC, STATE_WITNESS_MAGIC};

pub(crate) fn require_version_flags(
    reader: &mut Reader<'_>,
    field: &'static str,
) -> Result<(), ExecutionError> {
    if reader.read_u16()? != 1 || reader.read_u16()? != 0 {
        return Err(ExecutionError::Invalid(field));
    }
    Ok(())
}

pub(crate) struct Reader<'a> {
    bytes: &'a [u8],
    position: usize,
}

impl<'a> Reader<'a> {
    pub(crate) fn new(bytes: &'a [u8]) -> Self {
        Self { bytes, position: 0 }
    }

    pub(crate) fn read_u8(&mut self) -> Result<u8, ExecutionError> {
        let value = *self
            .bytes
            .get(self.position)
            .ok_or(ExecutionError::Truncated)?;
        self.position += 1;
        Ok(value)
    }

    pub(crate) fn read_u16(&mut self) -> Result<u16, ExecutionError> {
        Ok(u16::from_le_bytes(self.read_fixed::<2>()?))
    }

    pub(crate) fn read_u32(&mut self) -> Result<u32, ExecutionError> {
        Ok(u32::from_le_bytes(self.read_fixed::<4>()?))
    }

    pub(crate) fn read_i32(&mut self) -> Result<i32, ExecutionError> {
        Ok(i32::from_le_bytes(self.read_fixed::<4>()?))
    }

    pub(crate) fn read_u64(&mut self) -> Result<u64, ExecutionError> {
        Ok(u64::from_le_bytes(self.read_fixed::<8>()?))
    }

    pub(crate) fn read_i64(&mut self) -> Result<i64, ExecutionError> {
        Ok(i64::from_le_bytes(self.read_fixed::<8>()?))
    }

    pub(crate) fn read_fixed<const N: usize>(&mut self) -> Result<[u8; N], ExecutionError> {
        let end = self
            .position
            .checked_add(N)
            .ok_or(ExecutionError::Truncated)?;
        let value = self
            .bytes
            .get(self.position..end)
            .ok_or(ExecutionError::Truncated)?
            .try_into()
            .map_err(|_| ExecutionError::Truncated)?;
        self.position = end;
        Ok(value)
    }

    pub(crate) fn require_magic(
        &mut self,
        magic: &[u8; 8],
        field: &'static str,
    ) -> Result<(), ExecutionError> {
        if self.read_fixed::<8>()? != *magic {
            return Err(ExecutionError::Invalid(field));
        }
        Ok(())
    }

    pub(crate) fn read_count(&mut self, maximum: usize, field: &'static str) -> Result<usize, ExecutionError> {
        let value =
            usize::try_from(self.read_u32()?).map_err(|_| ExecutionError::Oversized(field))?;
        if value > maximum {
            return Err(ExecutionError::Oversized(field));
        }
        // Every counted element occupies at least one byte of the buffer, so a well-formed
        // payload can never declare more items than there are bytes remaining. Rejecting an
        // inflated count here (before any `Vec::with_capacity`) prevents a truncated body from
        // forcing a large transient allocation off an untrusted length prefix.
        let remaining = self.bytes.len().saturating_sub(self.position);
        if value > remaining {
            return Err(ExecutionError::Truncated);
        }
        Ok(value)
    }

    pub(crate) fn read_length_prefixed(
        &mut self,
        maximum: usize,
        field: &'static str,
    ) -> Result<Vec<u8>, ExecutionError> {
        let length = self.read_count(maximum, field)?;
        let end = self
            .position
            .checked_add(length)
            .ok_or(ExecutionError::Truncated)?;
        let value = self
            .bytes
            .get(self.position..end)
            .ok_or(ExecutionError::Truncated)?
            .to_vec();
        self.position = end;
        Ok(value)
    }

    pub(crate) fn read_bytes(&mut self, length: usize) -> Result<&'a [u8], ExecutionError> {
        let end = self
            .position
            .checked_add(length)
            .ok_or(ExecutionError::Truncated)?;
        let value = self
            .bytes
            .get(self.position..end)
            .ok_or(ExecutionError::Truncated)?;
        self.position = end;
        Ok(value)
    }

    pub(crate) fn read_optional_bytes(
        &mut self,
        maximum: usize,
        field: &'static str,
    ) -> Result<Option<Vec<u8>>, ExecutionError> {
        match self.read_u8()? {
            0 => Ok(None),
            1 => Ok(Some(self.read_length_prefixed(maximum, field)?)),
            _ => Err(ExecutionError::Invalid("optional byte presence")),
        }
    }

    pub(crate) fn ensure_end(&self, field: &'static str) -> Result<(), ExecutionError> {
        if self.position == self.bytes.len() {
            Ok(())
        } else {
            Err(ExecutionError::Invalid(field))
        }
    }
}

pub(crate) enum ArtifactlessMagic {
    Artifact,
    Payload,
    StateWitness,
    Effects,
}

pub(crate) struct Writer {
    bytes: Vec<u8>,
}

impl Writer {
    pub(crate) fn new() -> Self {
        Self { bytes: Vec::new() }
    }

    pub(crate) fn push(&mut self, magic: ArtifactlessMagic) {
        self.write_bytes(match magic {
            ArtifactlessMagic::Artifact => ARTIFACT_MAGIC,
            ArtifactlessMagic::Payload => PAYLOAD_MAGIC,
            ArtifactlessMagic::StateWitness => STATE_WITNESS_MAGIC,
            ArtifactlessMagic::Effects => EFFECTS_MAGIC,
        });
    }

    pub(crate) fn write_version_flags(&mut self) {
        self.write_u16(1);
        self.write_u16(0);
    }

    pub(crate) fn write_u8(&mut self, value: u8) {
        self.bytes.push(value);
    }

    pub(crate) fn write_u16(&mut self, value: u16) {
        self.bytes.extend_from_slice(&value.to_le_bytes());
    }

    pub(crate) fn write_u32(&mut self, value: u32) {
        self.bytes.extend_from_slice(&value.to_le_bytes());
    }

    pub(crate) fn write_i32(&mut self, value: i32) {
        self.bytes.extend_from_slice(&value.to_le_bytes());
    }

    pub(crate) fn write_u64(&mut self, value: u64) {
        self.bytes.extend_from_slice(&value.to_le_bytes());
    }

    pub(crate) fn write_i64(&mut self, value: i64) {
        self.bytes.extend_from_slice(&value.to_le_bytes());
    }

    pub(crate) fn write_bytes(&mut self, value: &[u8]) {
        self.bytes.extend_from_slice(value);
    }

    pub(crate) fn write_count(
        &mut self,
        value: usize,
        maximum: usize,
        field: &'static str,
    ) -> Result<(), ExecutionError> {
        if value > maximum {
            return Err(ExecutionError::Oversized(field));
        }
        let value = u32::try_from(value).map_err(|_| ExecutionError::Oversized(field))?;
        self.write_u32(value);
        Ok(())
    }

    pub(crate) fn write_length_prefixed(
        &mut self,
        value: &[u8],
        maximum: usize,
        field: &'static str,
    ) -> Result<(), ExecutionError> {
        self.write_count(value.len(), maximum, field)?;
        self.write_bytes(value);
        Ok(())
    }

    pub(crate) fn write_optional_bytes(
        &mut self,
        value: &Option<Vec<u8>>,
        maximum: usize,
        field: &'static str,
    ) -> Result<(), ExecutionError> {
        match value {
            Some(value) => {
                self.write_u8(1);
                self.write_length_prefixed(value, maximum, field)?;
            }
            None => self.write_u8(0),
        }
        Ok(())
    }

    pub(crate) fn finish(self) -> Vec<u8> {
        self.bytes
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::types::ExecutionError;
    use super::super::constants::MAX_PAYLOAD_ITEMS;

    // Regression for audit finding M1: a length/count prefix larger than the bytes actually
    // present must be rejected up front (so the caller's `Vec::with_capacity` never allocates
    // tens of MB off a truncated, untrusted witness), while a count backed by enough bytes passes.
    #[test]
    fn read_count_rejects_count_larger_than_remaining_bytes() {
        // u32 little-endian count of 1_000_000 (0x000F4240) followed by only 3 body bytes.
        let bytes = [0x40u8, 0x42, 0x0F, 0x00, 0xAA, 0xBB, 0xCC];
        let mut reader = Reader::new(&bytes);
        let err = reader
            .read_count(MAX_PAYLOAD_ITEMS, "payload transactions")
            .expect_err("inflated count with truncated body must not be accepted");
        assert!(matches!(err, ExecutionError::Truncated), "got {err:?}");
    }

    #[test]
    fn read_count_accepts_count_backed_by_enough_bytes() {
        // count = 2 with exactly 2 body bytes remaining -> valid, must not error.
        let bytes = [0x02u8, 0x00, 0x00, 0x00, 0x11, 0x22];
        let mut reader = Reader::new(&bytes);
        assert_eq!(
            reader
                .read_count(MAX_PAYLOAD_ITEMS, "L1 messages")
                .expect("valid count backed by bytes"),
            2
        );
    }
}
