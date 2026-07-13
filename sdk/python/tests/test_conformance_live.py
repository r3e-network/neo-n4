from __future__ import annotations

import json
import os
import re
import unittest
import urllib.request
from pathlib import Path
from typing import Any

from neo_n4_sdk import L2RpcClient, L2RpcServerError


REQUIRED_VARIABLES = (
    "NEO_SDK_LIVE",
    "NEO_N3_RPC_URL",
    "NEO_N4_RPC_URL",
    "NEO_N4_CHAIN_ID",
    "NEO_SDK_LIVE_FIXTURE",
)
MISSING_VARIABLES = tuple(name for name in REQUIRED_VARIABLES if not os.environ.get(name, "").strip())
LIVE_CONFIGURED = not MISSING_VARIABLES
SKIP_REASON = f"requires {', '.join(MISSING_VARIABLES or REQUIRED_VARIABLES)}"
HASH_PATTERN = re.compile(r"^0x[0-9a-fA-F]{64}$")


def configuration() -> tuple[str, str, int, dict[str, Any], dict[str, Any]]:
    if os.environ["NEO_SDK_LIVE"] != "1":
        raise AssertionError("NEO_SDK_LIVE must equal 1")
    chain_id = int(os.environ["NEO_N4_CHAIN_ID"])
    if chain_id <= 0 or chain_id > 0xFFFF_FFFF:
        raise AssertionError("NEO_N4_CHAIN_ID must be an unsigned non-zero integer")
    fixture = json.loads(Path(os.environ["NEO_SDK_LIVE_FIXTURE"]).read_text(encoding="utf-8"))
    if fixture.get("schema") != "neo-n4-sdk-live-fixture/v1":
        raise AssertionError("live fixture schema must be neo-n4-sdk-live-fixture/v1")
    if fixture.get("n4", {}).get("chainId") != chain_id:
        raise AssertionError("live fixture n4.chainId must match NEO_N4_CHAIN_ID")
    return (
        os.environ["NEO_N3_RPC_URL"],
        os.environ["NEO_N4_RPC_URL"],
        chain_id,
        fixture["n3"],
        fixture["n4"],
    )


def raw_rpc(endpoint: str, method: str, params: list[Any]) -> Any:
    payload = json.dumps({"jsonrpc": "2.0", "method": method, "params": params, "id": 1}).encode()
    request = urllib.request.Request(endpoint, data=payload, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=30) as response:
        if response.status < 200 or response.status >= 300:
            raise AssertionError(f"{method} returned HTTP {response.status}")
        envelope = json.load(response)
    if envelope.get("jsonrpc") != "2.0" or envelope.get("id") != 1:
        raise AssertionError(f"{method} returned a non-canonical JSON-RPC envelope")
    if envelope.get("error") is not None:
        raise AssertionError(f"{method} returned {envelope['error']}")
    return envelope.get("result")


def assert_base_node(test: unittest.TestCase, endpoint: str, expected: dict[str, Any]) -> None:
    version = raw_rpc(endpoint, "getversion", [])
    test.assertIsInstance(version, dict)
    test.assertEqual(expected["networkMagic"], version["protocol"]["network"])
    test.assertGreaterEqual(int(raw_rpc(endpoint, "getblockcount", [])), expected["minimumBlockCount"])
    genesis = str(raw_rpc(endpoint, "getblockhash", [0]))
    test.assertRegex(genesis, HASH_PATTERN)
    test.assertEqual(expected["genesisHash"].lower(), genesis.lower())


def read_u64(value: Any) -> int:
    if isinstance(value, bool) or not isinstance(value, (int, str)):
        raise AssertionError("fixture u64 must be a number or decimal string")
    return int(value)


def assert_typed_case(test: unittest.TestCase, client: L2RpcClient, test_case: dict[str, Any]) -> None:
    params = test_case["params"]
    expected = test_case["result"]
    name = test_case["name"]
    if name == "batch":
        batch = client.get_batch(read_u64(params[1]))
        test.assertIsNotNone(batch)
        normalized_expected = dict(expected)
        for field in ("batchNumber", "firstBlock", "lastBlock"):
            normalized_expected[field] = read_u64(normalized_expected[field])
        test.assertEqual(normalized_expected, {
            "chainId": batch.chain_id,
            "batchNumber": batch.batch_number,
            "firstBlock": batch.first_block,
            "lastBlock": batch.last_block,
            "preStateRoot": batch.pre_state_root,
            "postStateRoot": batch.post_state_root,
            "txRoot": batch.tx_root,
            "receiptRoot": batch.receipt_root,
            "withdrawalRoot": batch.withdrawal_root,
            "l2ToL1MessageRoot": batch.l2_to_l1_message_root,
            "l2ToL2MessageRoot": batch.l2_to_l2_message_root,
            "daCommitment": batch.da_commitment,
            "publicInputHash": batch.public_input_hash,
            "proofType": int(batch.proof_type),
            "proof": batch.proof,
            "encoded": batch.encoded,
        })
    elif name == "batch-status":
        status = client.get_batch_status(read_u64(params[1]))
        normalized_expected = dict(expected)
        normalized_expected["batchNumber"] = read_u64(normalized_expected["batchNumber"])
        test.assertEqual(normalized_expected, {
            "chainId": status.chain_id,
            "batchNumber": status.batch_number,
            "status": int(status.status),
            "statusName": status.status_name,
        })
    elif name == "latest-state-root":
        test.assertEqual(expected, client.get_latest_state_root())
    elif name == "historical-state-root":
        test.assertEqual(expected, client.get_state_root_at(read_u64(params[1])))
    elif name == "withdrawal-proof":
        test.assertEqual(expected, client.get_withdrawal_proof(params[1]).hex().upper())
    elif name == "message-proof":
        test.assertEqual(expected, client.get_message_proof(params[1]).hex().upper())
    elif name == "deposit-status":
        status = client.get_deposit_status(params[0], read_u64(params[1]))
        test.assertIsNotNone(status)
        normalized_expected = dict(expected)
        normalized_expected["nonce"] = read_u64(normalized_expected["nonce"])
        if normalized_expected["includedInBatch"] is not None:
            normalized_expected["includedInBatch"] = read_u64(normalized_expected["includedInBatch"])
        test.assertEqual(normalized_expected, {
            "sourceChainId": status.source_chain_id,
            "nonce": status.nonce,
            "consumedOnL2": status.consumed_on_l2,
            "includedInBatch": status.included_in_batch,
        })
    elif name == "canonical-asset":
        test.assertEqual(expected, client.get_canonical_asset(params[0]))
    elif name == "bridged-asset":
        test.assertEqual(expected, client.get_bridged_asset(params[0]))
    elif name == "security-level":
        level = client.get_security_level()
        test.assertEqual(expected["chainId"], level.chain_id)
        test.assertEqual(expected["level"], int(level.level))
    elif name == "security-label":
        label = client.get_security_label()
        test.assertEqual(expected["chainId"], label.chain_id)
        test.assertEqual(expected["securityLevel"], int(label.security_level))
        test.assertEqual(expected["daMode"], int(label.da_mode))
        test.assertEqual(expected["gatewayEnabled"], label.gateway_enabled)
        test.assertEqual(expected["sequencer"], int(label.sequencer))
        test.assertEqual(expected["exit"], int(label.exit))
    else:
        test.fail(f"unknown live conformance case {name}")


@unittest.skipUnless(LIVE_CONFIGURED, SKIP_REASON)
class LiveSdkConformanceTests(unittest.TestCase):
    def test_n3_node_base_rpc_methods_match_deployment_fixture(self) -> None:
        n3, _, _, n3_fixture, _ = configuration()
        assert_base_node(self, n3, n3_fixture)

    def test_n4_node_base_rpc_methods_match_deployment_fixture(self) -> None:
        _, n4, _, _, n4_fixture = configuration()
        assert_base_node(self, n4, n4_fixture)

    def test_n4_node_all_typed_l2_queries_and_wrong_chain_failure_match_deployment_fixture(self) -> None:
        _, n4, chain_id, _, n4_fixture = configuration()
        client = L2RpcClient(n4, chain_id)
        for test_case in n4_fixture["cases"]:
            with self.subTest(case=test_case["name"]):
                self.assertEqual(
                    test_case["result"],
                    raw_rpc(n4, test_case["method"], test_case["params"]),
                )
                assert_typed_case(self, client, test_case)

        wrong_chain_id = n4_fixture["wrongChainId"]
        with self.assertRaises(L2RpcServerError):
            L2RpcClient(n4, wrong_chain_id).get_security_label()


if __name__ == "__main__":
    unittest.main()
