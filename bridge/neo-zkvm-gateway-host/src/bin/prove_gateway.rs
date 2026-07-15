use neo_zkvm_gateway_host::{DaemonConfig, build_manifest_json, run_daemon};
use std::{path::PathBuf, time::Duration};
use tracing::error;

fn main() {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new("info")),
        )
        .init();

    let args = std::env::args().skip(1).collect::<Vec<_>>();
    if args.as_slice() == ["build-manifest"] {
        println!("{}", build_manifest_json());
        return;
    }
    let config = match parse_daemon_args(&args) {
        Ok(config) => config,
        Err(message) => {
            error!("{message}");
            print_usage();
            std::process::exit(2);
        }
    };
    if let Err(error) = run_daemon(&config) {
        error!(%error, "Gateway SP1 daemon stopped");
        std::process::exit(1);
    }
}

fn parse_daemon_args(args: &[String]) -> Result<DaemonConfig, String> {
    if args.first().map(String::as_str) != Some("daemon") {
        return Err("expected daemon or build-manifest subcommand".into());
    }
    let mut queue = None;
    let mut child_proofs = None;
    let mut poll_ms = 1000u64;
    let mut index = 1;
    while index < args.len() {
        match args[index].as_str() {
            "--queue" => queue = Some(next_path(args, &mut index, "--queue")?),
            "--child-proofs" => child_proofs = Some(next_path(args, &mut index, "--child-proofs")?),
            "--poll-ms" => {
                index += 1;
                poll_ms = args
                    .get(index)
                    .ok_or("--poll-ms requires a positive integer")?
                    .parse()
                    .map_err(|_| "--poll-ms requires a positive integer")?;
                if poll_ms == 0 {
                    return Err("--poll-ms must be positive".into());
                }
            }
            value => return Err(format!("unexpected argument: {value}")),
        }
        index += 1;
    }
    Ok(DaemonConfig {
        queue_directory: queue.ok_or("--queue is required")?,
        child_proof_directory: child_proofs.ok_or("--child-proofs is required")?,
        poll_interval: Duration::from_millis(poll_ms),
    })
}

fn next_path(args: &[String], index: &mut usize, flag: &str) -> Result<PathBuf, String> {
    *index += 1;
    args.get(*index)
        .map(PathBuf::from)
        .ok_or_else(|| format!("{flag} requires a path"))
}

fn print_usage() {
    eprintln!("usage:");
    eprintln!("  prove-gateway build-manifest");
    eprintln!("  prove-gateway daemon --queue <dir> --child-proofs <dir> [--poll-ms <positive>]");
}
