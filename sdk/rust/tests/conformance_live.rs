use neo_n4_sdk::{L2RpcClient, L2RpcError};
use serde_json::Value;
use std::path::Path;

const REQUIRED_VARIABLES: [&str; 5] = [
    "NEO_SDK_LIVE",
    "NEO_N3_RPC_URL",
    "NEO_N4_RPC_URL",
    "NEO_N4_CHAIN_ID",
    "NEO_SDK_LIVE_FIXTURE",
];

fn configuration() -> (String, String, u32, Value, Value) {
    let missing: Vec<&str> = REQUIRED_VARIABLES
        .into_iter()
        .filter(|name| std::env::var(name).map_or(true, |value| value.trim().is_empty()))
        .collect();
    assert!(
        missing.is_empty(),
        "live SDK conformance requires {}",
        missing.join(", ")
    );
    assert_eq!(
        std::env::var("NEO_SDK_LIVE").unwrap(),
        "1",
        "NEO_SDK_LIVE must equal 1"
    );
    let chain_id: u32 = std::env::var("NEO_N4_CHAIN_ID")
        .unwrap()
        .parse()
        .expect("NEO_N4_CHAIN_ID must be an unsigned integer");
    assert_ne!(chain_id, 0, "NEO_N4_CHAIN_ID must be non-zero");
    let fixture_path = std::env::var("NEO_SDK_LIVE_FIXTURE").unwrap();
    let fixture: Value = serde_json::from_slice(
        &std::fs::read(Path::new(&fixture_path)).expect("live fixture must be readable"),
    )
    .expect("live fixture must contain valid JSON");
    assert_eq!(fixture["schema"], "neo-n4-sdk-live-fixture/v1");
    assert_eq!(fixture["n4"]["chainId"], chain_id);
    (
        std::env::var("NEO_N3_RPC_URL").unwrap(),
        std::env::var("NEO_N4_RPC_URL").unwrap(),
        chain_id,
        fixture["n3"].clone(),
        fixture["n4"].clone(),
    )
}

async fn raw_rpc(endpoint: &str, method: &str, params: Value) -> Value {
    let response = reqwest::Client::new()
        .post(endpoint)
        .json(&serde_json::json!({
            "jsonrpc": "2.0",
            "method": method,
            "params": params,
            "id": 1,
        }))
        .send()
        .await
        .unwrap_or_else(|error| panic!("{method} transport failed: {error}"));
    assert!(
        response.status().is_success(),
        "{method} returned {}",
        response.status()
    );
    let envelope: Value = response.json().await.expect("RPC response must be JSON");
    assert_eq!(envelope["jsonrpc"], "2.0");
    assert_eq!(envelope["id"], 1);
    assert!(envelope.get("error").is_none_or(Value::is_null));
    envelope["result"].clone()
}

async fn assert_base_node(endpoint: &str, expected: &Value) {
    let version = raw_rpc(endpoint, "getversion", serde_json::json!([])).await;
    assert_eq!(version["protocol"]["network"], expected["networkMagic"]);
    let block_count = raw_rpc(endpoint, "getblockcount", serde_json::json!([]))
        .await
        .as_u64()
        .expect("getblockcount result must be an unsigned integer");
    assert!(block_count >= expected["minimumBlockCount"].as_u64().unwrap());
    let genesis = raw_rpc(endpoint, "getblockhash", serde_json::json!([0]))
        .await
        .as_str()
        .expect("getblockhash result must be a string")
        .to_string();
    assert!(is_hash(&genesis));
    assert_eq!(genesis, expected["genesisHash"]);
}

async fn assert_typed_case(client: &L2RpcClient, test_case: &Value) {
    let params = &test_case["params"];
    let expected = &test_case["result"];
    match test_case["name"].as_str().unwrap() {
        "batch" => {
            let batch = client
                .get_batch(read_u64(&params[1]))
                .await
                .unwrap()
                .unwrap();
            assert_eq!(batch.chain_id, expected["chainId"].as_u64().unwrap() as u32);
            assert_eq!(batch.batch_number, read_u64(&expected["batchNumber"]));
            assert_eq!(batch.first_block, read_u64(&expected["firstBlock"]));
            assert_eq!(batch.last_block, read_u64(&expected["lastBlock"]));
            assert_eq!(
                serde_json::json!({
                    "preStateRoot": batch.pre_state_root,
                    "postStateRoot": batch.post_state_root,
                    "txRoot": batch.tx_root,
                    "receiptRoot": batch.receipt_root,
                    "withdrawalRoot": batch.withdrawal_root,
                    "l2ToL1MessageRoot": batch.l2_to_l1_message_root,
                    "l2ToL2MessageRoot": batch.l2_to_l2_message_root,
                    "daCommitment": batch.da_commitment,
                    "publicInputHash": batch.public_input_hash,
                    "proofType": batch.proof_type,
                    "proof": batch.proof,
                    "encoded": batch.encoded,
                }),
                serde_json::json!({
                    "preStateRoot": expected["preStateRoot"],
                    "postStateRoot": expected["postStateRoot"],
                    "txRoot": expected["txRoot"],
                    "receiptRoot": expected["receiptRoot"],
                    "withdrawalRoot": expected["withdrawalRoot"],
                    "l2ToL1MessageRoot": expected["l2ToL1MessageRoot"],
                    "l2ToL2MessageRoot": expected["l2ToL2MessageRoot"],
                    "daCommitment": expected["daCommitment"],
                    "publicInputHash": expected["publicInputHash"],
                    "proofType": expected["proofType"],
                    "proof": expected["proof"],
                    "encoded": expected["encoded"],
                })
            );
        }
        "batch-status" => {
            let status = client.get_batch_status(read_u64(&params[1])).await.unwrap();
            assert_eq!(
                status.chain_id,
                expected["chainId"].as_u64().unwrap() as u32
            );
            assert_eq!(status.batch_number, read_u64(&expected["batchNumber"]));
            assert_eq!(status.status, expected["status"].as_u64().unwrap() as u8);
            assert_eq!(status.status_name, expected["statusName"].as_str().unwrap());
        }
        "latest-state-root" => assert_eq!(client.get_latest_state_root().await.unwrap(), *expected),
        "historical-state-root" => assert_eq!(
            client
                .get_state_root_at(read_u64(&params[1]))
                .await
                .unwrap(),
            *expected
        ),
        "withdrawal-proof" => assert_eq!(
            hex::encode_upper(
                client
                    .get_withdrawal_proof(params[1].as_str().unwrap())
                    .await
                    .unwrap()
                    .unwrap()
            ),
            expected.as_str().unwrap()
        ),
        "message-proof" => assert_eq!(
            hex::encode_upper(
                client
                    .get_message_proof(params[1].as_str().unwrap())
                    .await
                    .unwrap()
                    .unwrap()
            ),
            expected.as_str().unwrap()
        ),
        "deposit-status" => {
            let status = client
                .get_deposit_status(params[0].as_u64().unwrap() as u32, read_u64(&params[1]))
                .await
                .unwrap()
                .unwrap();
            assert_eq!(
                status.source_chain_id,
                expected["sourceChainId"].as_u64().unwrap() as u32
            );
            assert_eq!(status.nonce, read_u64(&expected["nonce"]));
            assert_eq!(
                status.consumed_on_l2,
                expected["consumedOnL2"].as_bool().unwrap()
            );
            let expected_included = if expected["includedInBatch"].is_null() {
                None
            } else {
                Some(read_u64(&expected["includedInBatch"]))
            };
            assert_eq!(status.included_in_batch, expected_included);
        }
        "canonical-asset" => assert_eq!(
            client
                .get_canonical_asset(params[0].as_str().unwrap())
                .await
                .unwrap()
                .unwrap(),
            expected.as_str().unwrap()
        ),
        "bridged-asset" => assert_eq!(
            client
                .get_bridged_asset(params[0].as_str().unwrap())
                .await
                .unwrap()
                .unwrap(),
            expected.as_str().unwrap()
        ),
        "security-level" => {
            let level = client.get_security_level().await.unwrap();
            assert_eq!(level.chain_id, expected["chainId"].as_u64().unwrap() as u32);
            assert_eq!(level.level, expected["level"].as_u64().unwrap() as u8);
        }
        "security-label" => {
            let label = client.get_security_label().await.unwrap();
            assert_eq!(label.chain_id, expected["chainId"].as_u64().unwrap() as u32);
            assert_eq!(
                label.security_level,
                expected["securityLevel"].as_u64().unwrap() as u8
            );
            assert_eq!(label.da_mode, expected["daMode"].as_u64().unwrap() as u8);
            assert_eq!(
                label.gateway_enabled,
                expected["gatewayEnabled"].as_bool().unwrap()
            );
            assert_eq!(
                label.sequencer,
                expected["sequencer"].as_u64().unwrap() as u8
            );
            assert_eq!(label.exit, expected["exit"].as_u64().unwrap() as u8);
        }
        name => panic!("unknown live conformance case {name}"),
    }
}

fn read_u64(value: &Value) -> u64 {
    let text = value
        .as_str()
        .expect("fixture u64 must be a canonical decimal string");
    assert!(
        text == "0" || (!text.starts_with('0') && text.bytes().all(|byte| byte.is_ascii_digit())),
        "fixture u64 must be a canonical decimal string"
    );
    text.parse().expect("fixture u64 exceeds u64 range")
}

fn is_hash(value: &str) -> bool {
    value.len() == 66
        && value.starts_with("0x")
        && value[2..].bytes().all(|byte| byte.is_ascii_hexdigit())
}

#[tokio::test]
#[ignore = "requires explicit live switch, N3/N4 endpoints, chain id, and deployment fixture"]
async fn n3_node_base_rpc_methods_match_deployment_fixture() {
    let (n3, _, _, n3_fixture, _) = configuration();
    assert_base_node(&n3, &n3_fixture).await;
}

#[tokio::test]
#[ignore = "requires explicit live switch, N3/N4 endpoints, chain id, and deployment fixture"]
async fn n4_node_base_rpc_methods_match_deployment_fixture() {
    let (_, n4, _, _, n4_fixture) = configuration();
    assert_base_node(&n4, &n4_fixture).await;
}

#[tokio::test]
#[ignore = "requires explicit live switch, N3/N4 endpoints, chain id, and deployment fixture"]
async fn n4_node_all_typed_l2_queries_and_wrong_chain_failure_match_deployment_fixture() {
    let (_, n4, chain_id, _, n4_fixture) = configuration();
    let client = L2RpcClient::new(&n4, chain_id).unwrap();
    for test_case in n4_fixture["cases"].as_array().unwrap() {
        let actual = raw_rpc(
            &n4,
            test_case["method"].as_str().unwrap(),
            test_case["params"].clone(),
        )
        .await;
        assert_eq!(actual, test_case["result"], "live RPC result drifted");
        assert_typed_case(&client, test_case).await;
    }

    let wrong_chain_id = n4_fixture["wrongChainId"].as_u64().unwrap() as u32;
    let wrong_client = L2RpcClient::new(n4, wrong_chain_id).unwrap();
    assert!(matches!(
        wrong_client.get_security_label().await.unwrap_err(),
        L2RpcError::Server { .. }
    ));
}
