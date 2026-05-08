//! `prove-batch` — operator-facing CLI for the L2 prover.
//!
//! Two modes:
//! * Default: executes the batch in the SP1 zkVM (fast, no proof).
//! * `--prove`: generates a real ZK proof and writes its bytes to a file
//!   (slow, suitable for production batch settlement).
//!
//! Usage:
//! ```text
//! prove-batch <hex-encoded BatchExecutionRequest>
//! prove-batch --prove <hex-encoded BatchExecutionRequest> [--out proof.bin]
//! ```

fn main() {
    let args: Vec<String> = std::env::args().collect();
    let mut prove_mode = false;
    let mut out_path: Option<String> = None;
    let mut positional: Option<String> = None;
    let mut i = 1;
    while i < args.len() {
        match args[i].as_str() {
            "--prove" => prove_mode = true,
            "--out" => {
                i += 1;
                if i >= args.len() {
                    eprintln!("--out requires a path");
                    std::process::exit(1);
                }
                out_path = Some(args[i].clone());
            }
            "-h" | "--help" => {
                print_usage();
                return;
            }
            other => {
                if positional.is_some() {
                    eprintln!("unexpected argument: {}", other);
                    std::process::exit(1);
                }
                positional = Some(other.to_string());
            }
        }
        i += 1;
    }
    let Some(hex) = positional else {
        print_usage();
        std::process::exit(1);
    };
    let bytes = match hex_decode(&hex) {
        Ok(b) => b,
        Err(e) => {
            eprintln!("invalid hex: {}", e);
            std::process::exit(1);
        }
    };

    if prove_mode {
        match neo_zkvm_host::prove(&bytes) {
            Ok(result) => {
                println!("public_input_hash = 0x{}", hex_encode(&result.public_input_hash));
                println!("proof_bytes_len   = {}", result.proof_bytes.len());
                println!("vk_bytes_len      = {}", result.vk_bytes.len());
                let path = out_path.unwrap_or_else(|| "proof.bin".to_string());
                if let Err(e) = std::fs::write(&path, &result.proof_bytes) {
                    eprintln!("failed to write proof to {}: {}", path, e);
                    std::process::exit(1);
                }
                let vk_path = format!("{}.vk", path.trim_end_matches(".bin"));
                if let Err(e) = std::fs::write(&vk_path, &result.vk_bytes) {
                    eprintln!("failed to write vk to {}: {}", vk_path, e);
                    std::process::exit(1);
                }
                println!("proof_path        = {}", path);
                println!("vk_path           = {}", vk_path);
            }
            Err(e) => {
                eprintln!("proof generation failed: {}", e);
                std::process::exit(1);
            }
        }
    } else {
        match neo_zkvm_host::execute(&bytes) {
            Ok(result) => {
                println!("public_input_hash = 0x{}", hex_encode(&result.public_input_hash));
                println!("cycles            = {}", result.cycles);
            }
            Err(e) => {
                eprintln!("execution failed: {}", e);
                std::process::exit(1);
            }
        }
    }
}

fn print_usage() {
    eprintln!("usage:");
    eprintln!("  prove-batch <hex-encoded BatchExecutionRequest>             # execute only");
    eprintln!("  prove-batch --prove <hex> [--out proof.bin]                 # generate ZK proof");
}

fn hex_decode(s: &str) -> Result<Vec<u8>, String> {
    let s = s.strip_prefix("0x").unwrap_or(s);
    if s.len() % 2 != 0 {
        return Err(format!("odd-length hex: {}", s.len()));
    }
    let mut out = Vec::with_capacity(s.len() / 2);
    for i in (0..s.len()).step_by(2) {
        let byte = u8::from_str_radix(&s[i..i + 2], 16).map_err(|e| e.to_string())?;
        out.push(byte);
    }
    Ok(out)
}

fn hex_encode(b: &[u8]) -> String {
    let mut s = String::with_capacity(b.len() * 2);
    for byte in b {
        s.push_str(&format!("{:02x}", byte));
    }
    s
}
