use std::path::Path;

#[test]
fn zk_host_execution_result_name_is_domain_specific() {
    let root = Path::new(env!("CARGO_MANIFEST_DIR"));
    let lib = std::fs::read_to_string(root.join("src/lib.rs")).expect("read src/lib.rs");

    assert!(
        !root.join("src/execution_result.rs").exists(),
        "neo-zkvm-host should not expose a generic ExecutionResult that is easy to confuse with neo-vm-rs::ExecutionResult"
    );
    assert!(
        root.join("src/zk_execution_result.rs").exists(),
        "SP1 host execution output should have a zk-domain-specific module name"
    );
    assert!(
        lib.contains("pub use zk_execution_result::ZkExecutionResult;"),
        "SP1 host execution output should be exported as ZkExecutionResult"
    );
    assert!(
        !lib.contains("pub use execution_result::ExecutionResult;")
            && !lib.contains("Result<ExecutionResult, String>"),
        "neo-zkvm-host public API should not use the generic ExecutionResult name"
    );
}
