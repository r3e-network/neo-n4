use std::{
    env,
    ffi::OsString,
    fs::{self, OpenOptions},
    io::Write,
    path::{Path, PathBuf},
    process,
};

use neo_execution_core::{
    MAX_EXECUTION_PAYLOAD_BYTES, MAX_NATIVE_EXECUTION_OUTPUT_BYTES, MAX_STATE_WITNESS_BYTES,
    NativeExecutionOutputV1, SP1_STATEFUL_NEO_VM_V1_EXECUTION_SEMANTIC_ID,
    encode_native_execution_output, hash256, parse_execution_payload, parse_state_witness,
};

struct Arguments {
    payload: PathBuf,
    state_witness: PathBuf,
    output: PathBuf,
}

fn main() {
    if let Err(error) = run() {
        eprintln!("neo-zkvm-executor: {error}");
        process::exit(1);
    }
}

fn run() -> Result<(), String> {
    let arguments = parse_arguments()?;
    let payload_bytes = read_bounded(
        &arguments.payload,
        MAX_EXECUTION_PAYLOAD_BYTES,
        "execution payload",
    )?;
    let state_witness_bytes = read_bounded(
        &arguments.state_witness,
        MAX_STATE_WITNESS_BYTES,
        "state witness",
    )?;
    let payload = parse_execution_payload(&payload_bytes).map_err(|error| error.to_string())?;
    let state_witness =
        parse_state_witness(&state_witness_bytes).map_err(|error| error.to_string())?;
    let transition = neo_zkvm_guest::compute_batch_transition(&payload, &state_witness)
        .map_err(|error| error.to_string())?;
    let output = NativeExecutionOutputV1 {
        request_payload_hash: hash256(&payload_bytes),
        request_state_witness_hash: hash256(&state_witness_bytes),
        execution_semantic_id: SP1_STATEFUL_NEO_VM_V1_EXECUTION_SEMANTIC_ID,
        execution_result: transition.batch.execution_result,
        effects_bytes: transition.batch.effects_bytes,
        post_state_witness_bytes: transition.post_state_witness_bytes,
        public_input_hash: transition.batch.public_input_hash,
    };
    let encoded = encode_native_execution_output(&output).map_err(|error| error.to_string())?;
    if encoded.len() > MAX_NATIVE_EXECUTION_OUTPUT_BYTES {
        return Err("native execution output exceeds its protocol limit".into());
    }
    write_new(&arguments.output, &encoded)
}

fn parse_arguments() -> Result<Arguments, String> {
    let mut payload = None;
    let mut state_witness = None;
    let mut output = None;
    let mut arguments = env::args_os().skip(1);
    while let Some(argument) = arguments.next() {
        let value = arguments
            .next()
            .ok_or_else(|| format!("missing value for {}", argument.to_string_lossy()))?;
        match argument.to_str() {
            Some("--payload") if payload.is_none() => payload = Some(value),
            Some("--state-witness") if state_witness.is_none() => state_witness = Some(value),
            Some("--output") if output.is_none() => output = Some(value),
            _ => {
                return Err(format!(
                    "unknown or duplicate argument: {}",
                    argument.to_string_lossy()
                ));
            }
        }
    }
    Ok(Arguments {
        payload: required_path(payload, "--payload")?,
        state_witness: required_path(state_witness, "--state-witness")?,
        output: required_path(output, "--output")?,
    })
}

fn required_path(value: Option<OsString>, name: &str) -> Result<PathBuf, String> {
    let value = value.ok_or_else(|| format!("missing required argument {name}"))?;
    if value.is_empty() {
        return Err(format!("{name} must not be empty"));
    }
    Ok(PathBuf::from(value))
}

fn read_bounded(path: &Path, maximum: usize, description: &str) -> Result<Vec<u8>, String> {
    let metadata = fs::metadata(path)
        .map_err(|error| format!("cannot inspect {description} {}: {error}", path.display()))?;
    if !metadata.is_file() || metadata.len() > maximum as u64 {
        return Err(format!(
            "{description} {} is not a regular file within the {maximum}-byte limit",
            path.display()
        ));
    }
    let bytes = fs::read(path)
        .map_err(|error| format!("cannot read {description} {}: {error}", path.display()))?;
    if bytes.len() > maximum || bytes.len() as u64 != metadata.len() {
        return Err(format!(
            "{description} {} changed while being read",
            path.display()
        ));
    }
    Ok(bytes)
}

fn write_new(path: &Path, bytes: &[u8]) -> Result<(), String> {
    let mut file = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(path)
        .map_err(|error| format!("cannot create output {}: {error}", path.display()))?;
    file.write_all(bytes)
        .and_then(|()| file.sync_all())
        .map_err(|error| format!("cannot persist output {}: {error}", path.display()))
}
