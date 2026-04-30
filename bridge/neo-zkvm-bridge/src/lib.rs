//! # neo-zkvm-bridge
//!
//! A thin C ABI shim that lets C# call `neo-zkvm`'s `NeoProver` over P/Invoke.
//!
//! The exported symbols are stable across Rust → C# upgrades; internal ABI churn in
//! `neo-zkvm-prover` does NOT propagate. Build as a `cdylib` and place the resulting
//! `libneo_zkvm_bridge.{so,dylib,dll}` next to the C# binaries.
//!
//! ## Symbols
//!
//! All `extern "C"` functions return an `i32` status:
//!   *  `0` — success
//!   * `-1` — invalid input
//!   * `-2` — proof generation failed (real-prover feature only)
//!   * `-3` — verification rejected
//!   * `-9` — feature `real-prover` not compiled in (mock fallback)
//!
//! Output buffers are heap-allocated by the bridge; the caller MUST free them via
//! `neo_zkvm_free_buffer`.
//!
//! ```c
//! // Rough C signature equivalent.
//! int32_t neo_zkvm_prove(
//!     const uint8_t *input_ptr, size_t input_len,
//!     uint8_t **output_ptr, size_t *output_len);
//! int32_t neo_zkvm_verify(
//!     const uint8_t *proof_ptr, size_t proof_len);
//! void neo_zkvm_free_buffer(uint8_t *ptr, size_t len);
//! uint32_t neo_zkvm_abi_version(void);
//! ```

use std::slice;

/// Bumped on any incompatible ABI change. C# checks this at startup.
pub const ABI_VERSION: u32 = 1;

/// Status codes returned by every `extern "C"` function.
pub const STATUS_OK: i32 = 0;
pub const STATUS_INVALID_INPUT: i32 = -1;
pub const STATUS_PROVE_FAILED: i32 = -2;
pub const STATUS_VERIFY_REJECTED: i32 = -3;
pub const STATUS_NOT_IMPLEMENTED: i32 = -9;

/// Returns the bridge's ABI version. C# matches this against its expected version
/// at startup before issuing any other call.
#[no_mangle]
pub extern "C" fn neo_zkvm_abi_version() -> u32 {
    ABI_VERSION
}

/// Prove. `input_ptr` is the canonical-encoded `ProofInput` bytes (caller-supplied).
/// On success, `*output_ptr` is set to a heap-allocated buffer and `*output_len` to
/// its length; the caller MUST free via `neo_zkvm_free_buffer`.
///
/// # Safety
/// `input_ptr` must point to `input_len` valid bytes. Both output pointers must be writable.
#[no_mangle]
pub unsafe extern "C" fn neo_zkvm_prove(
    input_ptr: *const u8,
    input_len: usize,
    output_ptr: *mut *mut u8,
    output_len: *mut usize,
) -> i32 {
    if input_ptr.is_null() || output_ptr.is_null() || output_len.is_null() {
        return STATUS_INVALID_INPUT;
    }

    let _input_bytes = unsafe { slice::from_raw_parts(input_ptr, input_len) };

    #[cfg(feature = "real-prover")]
    {
        // Decode ProofInput, build NeoProver with default config, prove, encode NeoProof.
        // bincode used for now; swap to a stable canonical encoding when defined.
        match bincode::deserialize(_input_bytes) {
            Ok(input) => {
                let prover = neo_zkvm_prover::NeoProver::new(neo_zkvm_prover::ProverConfig::default());
                match prover.prove_strict(input) {
                    Ok(proof) => match bincode::serialize(&proof) {
                        Ok(bytes) => write_owned_buffer(bytes, output_ptr, output_len),
                        Err(_) => STATUS_PROVE_FAILED,
                    },
                    Err(_) => STATUS_PROVE_FAILED,
                }
            }
            Err(_) => STATUS_INVALID_INPUT,
        }
    }
    #[cfg(not(feature = "real-prover"))]
    {
        STATUS_NOT_IMPLEMENTED
    }
}

/// Verify. `proof_ptr` is the canonical-encoded `NeoProof` bytes.
/// Returns `STATUS_OK` if valid, `STATUS_VERIFY_REJECTED` otherwise.
///
/// # Safety
/// `proof_ptr` must point to `proof_len` valid bytes.
#[no_mangle]
pub unsafe extern "C" fn neo_zkvm_verify(proof_ptr: *const u8, proof_len: usize) -> i32 {
    if proof_ptr.is_null() {
        return STATUS_INVALID_INPUT;
    }

    let _proof_bytes = unsafe { slice::from_raw_parts(proof_ptr, proof_len) };

    #[cfg(feature = "real-prover")]
    {
        match bincode::deserialize(_proof_bytes) {
            Ok(proof) => {
                let verifier = neo_zkvm_prover::NeoProver::new(neo_zkvm_prover::ProverConfig::default());
                if verifier.verify(&proof) { STATUS_OK } else { STATUS_VERIFY_REJECTED }
            }
            Err(_) => STATUS_INVALID_INPUT,
        }
    }
    #[cfg(not(feature = "real-prover"))]
    {
        STATUS_NOT_IMPLEMENTED
    }
}

/// Free a buffer previously returned by `neo_zkvm_prove`.
///
/// # Safety
/// `ptr` must come from a prior bridge call and `len` must match the returned length.
#[no_mangle]
pub unsafe extern "C" fn neo_zkvm_free_buffer(ptr: *mut u8, len: usize) {
    if ptr.is_null() {
        return;
    }
    // Reconstruct the Vec to drop it (with the same layout we boxed it as).
    let _v = unsafe { Vec::from_raw_parts(ptr, len, len) };
}

#[cfg(feature = "real-prover")]
fn write_owned_buffer(bytes: Vec<u8>, output_ptr: *mut *mut u8, output_len: *mut usize) -> i32 {
    let len = bytes.len();
    let mut boxed = bytes.into_boxed_slice();
    unsafe {
        *output_ptr = boxed.as_mut_ptr();
        *output_len = len;
    }
    std::mem::forget(boxed); // ownership passed to caller via free_buffer
    STATUS_OK
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn abi_version_matches_constant() {
        assert_eq!(neo_zkvm_abi_version(), ABI_VERSION);
    }

    #[test]
    fn prove_with_default_features_returns_not_implemented() {
        let mut output_ptr: *mut u8 = std::ptr::null_mut();
        let mut output_len: usize = 0;
        let input = [0u8; 4];
        let status = unsafe {
            neo_zkvm_prove(input.as_ptr(), input.len(), &mut output_ptr, &mut output_len)
        };
        // Default build skips the real prover; bridge reports NOT_IMPLEMENTED so callers fallback.
        #[cfg(not(feature = "real-prover"))]
        assert_eq!(status, STATUS_NOT_IMPLEMENTED);
        #[cfg(feature = "real-prover")]
        let _ = status; // real prover may succeed or fail depending on input.
    }
}
