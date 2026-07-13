use mockito::{Matcher, Server};
use neo_n4_sdk::{BatchStatus, L2RpcClient, L2RpcError, SecurityLevel};
use p256::ecdsa::{Signature, VerifyingKey, signature::Verifier};
use serde_json::Value;
use sha2::{Digest, Sha256};

fn vectors() -> Value {
    serde_json::from_str(include_str!("../../conformance/vectors/v1.json"))
        .expect("canonical conformance vectors must parse")
}

fn rpc_response(id: i64, jsonrpc: &str, result: Value) -> String {
    serde_json::json!({"jsonrpc": jsonrpc, "id": id, "result": result}).to_string()
}

#[tokio::test]
async fn rpc_vectors_all_method_shapes_and_u64_serialization_conform() {
    let vectors = vectors();
    let rpc = &vectors["rpc"];
    let chain_id = rpc["chainId"].as_u64().unwrap() as u32;

    for test_case in rpc["cases"].as_array().unwrap() {
        let mut server = Server::new_async().await;
        let expected_request = serde_json::json!({
            "jsonrpc": "2.0",
            "method": test_case["method"],
            "params": test_case["params"],
            "id": 1,
        });
        let request = server
            .mock("POST", "/")
            .match_body(Matcher::Json(expected_request))
            .with_status(200)
            .with_header("content-type", "application/json")
            .with_body(rpc_response(1, "2.0", test_case["result"].clone()))
            .create_async()
            .await;
        let client = L2RpcClient::new(server.url(), chain_id).unwrap();

        match test_case["name"].as_str().unwrap() {
            "batch-missing-max-u64" => assert!(client.get_batch(u64::MAX).await.unwrap().is_none()),
            "batch-complete-large-u64" => {
                let batch = client
                    .get_batch(9_007_199_254_740_993)
                    .await
                    .unwrap()
                    .unwrap();
                assert_eq!(batch.batch_number, 9_007_199_254_740_993);
                assert_eq!(batch.proof, "AABBCC");
            }
            "batch-status" => {
                let status = client
                    .get_batch_status(9_007_199_254_740_993)
                    .await
                    .unwrap();
                assert_eq!(status.batch_number, 9_007_199_254_740_993);
                assert_eq!(status.status(), BatchStatus::Finalized);
            }
            "latest-state-root" => {
                assert_eq!(
                    client.get_latest_state_root().await.unwrap(),
                    test_case["result"]
                )
            }
            "state-root-max-u64" => {
                assert_eq!(
                    client.get_state_root_at(u64::MAX).await.unwrap(),
                    test_case["result"]
                )
            }
            "withdrawal-proof" => assert_eq!(
                client
                    .get_withdrawal_proof(test_case["params"][1].as_str().unwrap())
                    .await
                    .unwrap(),
                Some(vec![0xca, 0xfe, 0xba, 0xbe])
            ),
            "message-proof" => assert_eq!(
                client
                    .get_message_proof(test_case["params"][1].as_str().unwrap())
                    .await
                    .unwrap(),
                Some(vec![0xde, 0xad, 0xbe, 0xef])
            ),
            "deposit-status-max-u64" => {
                let status = client
                    .get_deposit_status(1, u64::MAX)
                    .await
                    .unwrap()
                    .unwrap();
                assert_eq!(status.nonce, u64::MAX);
                assert_eq!(status.included_in_batch, Some(9_007_199_254_740_993));
            }
            "canonical-asset" => assert_eq!(
                client
                    .get_canonical_asset(test_case["params"][0].as_str().unwrap())
                    .await
                    .unwrap()
                    .as_deref(),
                test_case["result"].as_str()
            ),
            "bridged-asset" => assert_eq!(
                client
                    .get_bridged_asset(test_case["params"][0].as_str().unwrap())
                    .await
                    .unwrap()
                    .as_deref(),
                test_case["result"].as_str()
            ),
            "security-level" => assert_eq!(
                client.get_security_level().await.unwrap().level(),
                SecurityLevel::Validity
            ),
            "security-label" => assert_eq!(
                client.get_security_label().await.unwrap().security_level(),
                SecurityLevel::Validium
            ),
            name => panic!("unknown conformance case {name}"),
        }
        request.assert_async().await;
    }
}

#[test]
fn hash_vector_uint256_little_endian_and_rpc_display_conform() {
    let vectors = vectors();
    let mut wire = hex::decode(vectors["hash"]["wireLittleEndianHex"].as_str().unwrap()).unwrap();
    wire.reverse();
    assert_eq!(
        format!("0x{}", hex::encode(wire)),
        vectors["hash"]["rpcDisplay"]
    );
}

#[tokio::test]
async fn error_vectors_server_id_and_version_map_to_canonical_taxonomy() {
    let vectors = vectors();
    let rpc = &vectors["rpc"];
    let chain_id = rpc["chainId"].as_u64().unwrap() as u32;

    for error_case in rpc["errors"].as_array().unwrap() {
        let mut server = Server::new_async().await;
        let id = 1 + error_case
            .get("idOffset")
            .and_then(Value::as_i64)
            .unwrap_or(0);
        let body = if error_case.get("error").is_some() {
            serde_json::json!({
                "jsonrpc": error_case["jsonrpc"],
                "id": id,
                "error": error_case["error"],
            })
        } else {
            serde_json::json!({
                "jsonrpc": error_case["jsonrpc"],
                "id": id,
                "result": error_case["result"],
            })
        };
        let request = server
            .mock("POST", "/")
            .with_status(200)
            .with_header("content-type", "application/json")
            .with_body(body.to_string())
            .create_async()
            .await;
        let client = L2RpcClient::new(server.url(), chain_id).unwrap();
        let error = client.get_latest_state_root().await.unwrap_err();

        match error_case["expected"].as_str().unwrap() {
            "server" => assert!(matches!(error, L2RpcError::Server { code: -32000, .. })),
            "protocol" => assert!(matches!(error, L2RpcError::Protocol { .. })),
            expected => panic!("unknown error category {expected}"),
        }
        request.assert_async().await;
    }
}

#[tokio::test]
async fn response_error_vectors_chain_shape_and_hex_fail_closed() {
    let vectors = vectors();
    let rpc = &vectors["rpc"];
    let chain_id = rpc["chainId"].as_u64().unwrap() as u32;

    for error_case in rpc["responseErrors"].as_array().unwrap() {
        let mut server = Server::new_async().await;
        let request = server
            .mock("POST", "/")
            .with_status(200)
            .with_header("content-type", "application/json")
            .with_body(rpc_response(1, "2.0", error_case["result"].clone()))
            .create_async()
            .await;
        let client = L2RpcClient::new(server.url(), chain_id).unwrap();
        let error = match error_case["name"].as_str().unwrap() {
            "mismatched-chain-id" => client.get_security_label().await.unwrap_err(),
            "invalid-withdrawal-proof-hex" => client
                .get_withdrawal_proof(&format!("0x{}", "4".repeat(64)))
                .await
                .unwrap_err(),
            "wrong-state-root-type" => client.get_latest_state_root().await.unwrap_err(),
            "unsafe-numeric-u64" => client
                .get_deposit_status(1, 9_007_199_254_740_992)
                .await
                .unwrap_err(),
            name => panic!("unknown response-error conformance case {name}"),
        };

        match error_case["expected"].as_str().unwrap() {
            "chain" => assert!(matches!(error, L2RpcError::MismatchedChainId { .. })),
            "protocol" => assert!(matches!(error, L2RpcError::Protocol { .. })),
            expected => panic!("unknown response-error category {expected}"),
        }
        request.assert_async().await;
    }
}

#[test]
fn domain_vector_binds_l1_reservation_l2_chain_and_network_magic() {
    let vectors = vectors();
    let domain = &vectors["domain"];
    let transaction = &vectors["transaction"];
    let network = domain["networkMagic"].as_u64().unwrap() as u32;

    assert_eq!(domain["l1ReservedChainId"], 0);
    assert_eq!(domain["l2ChainId"], vectors["rpc"]["chainId"]);
    assert_eq!(domain["networkMagic"], transaction["network"]);
    assert_eq!(
        hex::encode(network.to_le_bytes()),
        domain["networkMagicLittleEndianHex"]
    );
    let expected_prefix = domain["networkMagicLittleEndianHex"].as_str().unwrap();
    assert!(
        transaction["signDataHex"]
            .as_str()
            .unwrap()
            .starts_with(expected_prefix)
    );
}

#[test]
fn pagination_vector_cursors_and_u64_values_round_trip_without_loss() {
    let vectors = vectors();
    let serialized = serde_json::to_string(&vectors["pagination"]).unwrap();
    let round_trip: Value = serde_json::from_str(&serialized).unwrap();
    let batch_numbers: Vec<&str> = round_trip["pages"]
        .as_array()
        .unwrap()
        .iter()
        .flat_map(|page| page["items"].as_array().unwrap())
        .map(|item| item["batchNumber"].as_str().unwrap())
        .collect();
    let expected: Vec<&str> = vectors["pagination"]["expectedBatchNumbers"]
        .as_array()
        .unwrap()
        .iter()
        .map(|value| value.as_str().unwrap())
        .collect();

    assert_eq!(expected, batch_numbers);
    assert_eq!(
        round_trip["pages"][0]["nextCursor"],
        "batch:9007199254740994"
    );
    assert!(round_trip["pages"][1]["nextCursor"].is_null());
}

#[test]
fn transaction_vector_deserializes_hashes_signs_and_round_trips() {
    let vectors = vectors();
    let vector = &vectors["transaction"];
    let raw = hex::decode(vector["rawTransactionHex"].as_str().unwrap()).unwrap();
    let unsigned = hex::decode(vector["unsignedTransactionHex"].as_str().unwrap()).unwrap();
    let expected_sign_data = hex::decode(vector["signDataHex"].as_str().unwrap()).unwrap();
    let signature_bytes = hex::decode(vector["signatureHex"].as_str().unwrap()).unwrap();
    let verification_script =
        hex::decode(vector["verificationScriptHex"].as_str().unwrap()).unwrap();

    assert_eq!(&raw[..unsigned.len()], unsigned);
    let digest = Sha256::digest(&unsigned);
    let mut display = digest.to_vec();
    display.reverse();
    assert_eq!(format!("0x{}", hex::encode(display)), vector["txid"]);

    let mut sign_data = (vector["network"].as_u64().unwrap() as u32)
        .to_le_bytes()
        .to_vec();
    sign_data.extend_from_slice(&digest);
    assert_eq!(sign_data, expected_sign_data);

    let witness_count_offset = unsigned.len();
    assert_eq!(raw[witness_count_offset], 1);
    let invocation_length = raw[witness_count_offset + 1] as usize;
    assert_eq!(invocation_length, 66);
    let invocation_start = witness_count_offset + 2;
    let invocation_end = invocation_start + invocation_length;
    assert_eq!(&raw[invocation_start..invocation_start + 2], &[0x0c, 0x40]);
    assert_eq!(&raw[invocation_start + 2..invocation_end], signature_bytes);
    let verification_length = raw[invocation_end] as usize;
    assert_eq!(verification_length, verification_script.len());
    assert_eq!(&raw[invocation_end + 1..], verification_script);

    let verifying_key = VerifyingKey::from_sec1_bytes(
        &hex::decode(vector["publicKeyCompressedHex"].as_str().unwrap()).unwrap(),
    )
    .unwrap();
    let signature = Signature::from_slice(&signature_bytes).unwrap();
    verifying_key.verify(&sign_data, &signature).unwrap();
}
