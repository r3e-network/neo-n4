//! Integration tests using mockito for the in-process HTTP stub.
//! Mirrors the .NET / TS SDK test patterns: stub canned JSON-RPC responses,
//! exercise the full request/response cycle.

use mockito::Server;
use neo_n4_sdk::*;

fn rpc_response(id: i64, result: serde_json::Value) -> String {
    serde_json::json!({
        "jsonrpc": "2.0",
        "id": id,
        "result": result,
    })
    .to_string()
}

fn rpc_error(id: i64, code: i32, message: &str) -> String {
    serde_json::json!({
        "jsonrpc": "2.0",
        "id": id,
        "error": { "code": code, "message": message },
    })
    .to_string()
}

#[test]
fn ctor_rejects_zero_chain_id() {
    let err = L2RpcClient::new("http://x.example", 0).unwrap_err();
    assert!(matches!(err, L2RpcError::Protocol { .. }));
}

#[test]
fn ctor_rejects_non_http_scheme() {
    let err = L2RpcClient::new("ftp://x.example", 1).unwrap_err();
    assert!(matches!(err, L2RpcError::Protocol { .. }));
}

#[test]
fn ctor_rejects_invalid_url() {
    let err = L2RpcClient::new("not-a-url", 1).unwrap_err();
    assert!(matches!(err, L2RpcError::Protocol { .. }));
}

#[tokio::test]
async fn get_latest_state_root_returns_string() {
    let mut server = Server::new_async().await;
    let mock = server
        .mock("POST", "/")
        .with_status(200)
        .with_header("content-type", "application/json")
        .with_body(rpc_response(1, serde_json::json!("0xabc")))
        .create_async()
        .await;

    let client = L2RpcClient::new(server.url(), 1099).unwrap();
    let got = client.get_latest_state_root().await.unwrap();
    assert_eq!(got, "0xabc");
    mock.assert_async().await;
}

#[tokio::test]
async fn get_security_label_decodes_all_dimensions() {
    let mut server = Server::new_async().await;
    let _ = server
        .mock("POST", "/")
        .with_status(200)
        .with_body(rpc_response(
            1,
            serde_json::json!({
                "chainId": 1099,
                "securityLevel": 4,  // Validium
                "daMode": 1,          // NeoFS
                "gatewayEnabled": true,
                "sequencer": 1,       // DbftCommittee
                "exit": 1,            // Delayed
            }),
        ))
        .create_async()
        .await;

    let client = L2RpcClient::new(server.url(), 1099).unwrap();
    let label = client.get_security_label().await.unwrap();
    assert_eq!(label.security_level(), SecurityLevel::Validium);
    assert_eq!(label.da_mode(), DAMode::NeoFS);
    assert!(label.gateway_enabled);
    assert_eq!(label.sequencer(), SequencerModel::DbftCommittee);
    assert_eq!(label.exit(), ExitModel::Delayed);
}

#[tokio::test]
async fn get_withdrawal_proof_decodes_hex() {
    let mut server = Server::new_async().await;
    let _ = server
        .mock("POST", "/")
        .with_status(200)
        .with_body(rpc_response(1, serde_json::json!("CAFEBABE")))
        .create_async()
        .await;

    let client = L2RpcClient::new(server.url(), 1099).unwrap();
    let got = client
        .get_withdrawal_proof("0x1111111111111111111111111111111111111111111111111111111111111111")
        .await
        .unwrap();
    assert_eq!(got, Some(vec![0xca, 0xfe, 0xba, 0xbe]));
}

#[tokio::test]
async fn get_withdrawal_proof_null_returns_none() {
    let mut server = Server::new_async().await;
    let _ = server
        .mock("POST", "/")
        .with_status(200)
        .with_body(rpc_response(1, serde_json::Value::Null))
        .create_async()
        .await;

    let client = L2RpcClient::new(server.url(), 1099).unwrap();
    let got = client.get_withdrawal_proof("0x").await.unwrap();
    assert_eq!(got, None);
}

#[tokio::test]
async fn server_error_surfaces_with_code() {
    let mut server = Server::new_async().await;
    let _ = server
        .mock("POST", "/")
        .with_status(200)
        .with_body(rpc_error(1, -32000, "node not synced"))
        .create_async()
        .await;

    let client = L2RpcClient::new(server.url(), 1099).unwrap();
    let err = client.get_latest_state_root().await.unwrap_err();
    match err {
        L2RpcError::Server { code, message, .. } => {
            assert_eq!(code, -32000);
            assert!(message.contains("node not synced"));
        }
        other => panic!("expected Server error, got {:?}", other),
    }
}

#[tokio::test]
async fn http_502_surfaces_as_transport_error() {
    let mut server = Server::new_async().await;
    let _ = server
        .mock("POST", "/")
        .with_status(502)
        .with_body("upstream node unavailable")
        .create_async()
        .await;

    let client = L2RpcClient::new(server.url(), 1099).unwrap();
    let err = client.get_latest_state_root().await.unwrap_err();
    assert!(matches!(err, L2RpcError::Transport { .. }));
}

#[tokio::test]
async fn mismatched_chain_id_surfaces_as_mismatch_error() {
    let mut server = Server::new_async().await;
    let _ = server
        .mock("POST", "/")
        .with_status(200)
        .with_body(rpc_response(
            1,
            serde_json::json!({"chainId": 9999, "level": 2}),
        ))
        .create_async()
        .await;

    let client = L2RpcClient::new(server.url(), 1099).unwrap();
    let err = client.get_security_level().await.unwrap_err();
    match err {
        L2RpcError::MismatchedChainId { expected, got, .. } => {
            assert_eq!(expected, 1099);
            assert_eq!(got, 9999);
        }
        other => panic!("expected MismatchedChainId, got {:?}", other),
    }
}
