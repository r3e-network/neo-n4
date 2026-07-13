from __future__ import annotations

import hashlib
import json
import unittest
from pathlib import Path
from typing import Any

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.asymmetric.utils import encode_dss_signature

from neo_n4_sdk import (
    BatchStatus,
    L2RpcClient,
    L2RpcMismatchedChainIdError,
    L2RpcProtocolError,
    L2RpcServerError,
    SecurityLevel,
)


VECTOR_PATH = Path(__file__).resolve().parents[2] / "conformance" / "vectors" / "v1.json"


def load_vectors() -> dict[str, Any]:
    return json.loads(VECTOR_PATH.read_text(encoding="utf-8"))


class VectorTransport:
    def __init__(self, response_factory):
        self.response_factory = response_factory
        self.requests: list[dict[str, Any]] = []

    def __call__(self, request: dict[str, Any]) -> dict[str, Any]:
        self.requests.append(request)
        return self.response_factory(request)


class SharedSdkConformanceTests(unittest.TestCase):
    def test_rpc_vectors_all_method_shapes_and_u64_serialization_conform(self) -> None:
        vectors = load_vectors()
        chain_id = vectors["rpc"]["chainId"]
        for test_case in vectors["rpc"]["cases"]:
            with self.subTest(case=test_case["name"]):
                transport = VectorTransport(lambda request, result=test_case["result"]: {
                    "jsonrpc": "2.0",
                    "id": request["id"],
                    "result": result,
                })
                client = L2RpcClient("http://node.example:30332", chain_id, transport=transport)
                self._invoke_case(client, test_case)
                self.assertEqual(test_case["method"], transport.requests[0]["method"])
                self.assertEqual(test_case["params"], transport.requests[0]["params"])

    def test_hash_vector_uint256_little_endian_and_rpc_display_conform(self) -> None:
        vector = load_vectors()["hash"]
        wire = bytes.fromhex(vector["wireLittleEndianHex"])
        self.assertEqual(vector["rpcDisplay"], f"0x{wire[::-1].hex()}")

    def test_error_vectors_server_id_and_version_map_to_canonical_taxonomy(self) -> None:
        vectors = load_vectors()
        for error_case in vectors["rpc"]["errors"]:
            with self.subTest(case=error_case["name"]):
                def respond(request, case=error_case):
                    response: dict[str, Any] = {
                        "jsonrpc": case["jsonrpc"],
                        "id": request["id"] + case.get("idOffset", 0),
                    }
                    if "error" in case:
                        response["error"] = case["error"]
                    else:
                        response["result"] = case["result"]
                    return response

                client = L2RpcClient(
                    "http://node.example:30332",
                    vectors["rpc"]["chainId"],
                    transport=VectorTransport(respond),
                )
                expected = L2RpcServerError if error_case["expected"] == "server" else L2RpcProtocolError
                with self.assertRaises(expected):
                    client.get_latest_state_root()

    def test_response_error_vectors_chain_shape_and_hex_fail_closed(self) -> None:
        vectors = load_vectors()
        for error_case in vectors["rpc"]["responseErrors"]:
            with self.subTest(case=error_case["name"]):
                client = L2RpcClient(
                    "http://node.example:30332",
                    vectors["rpc"]["chainId"],
                    transport=VectorTransport(lambda request, result=error_case["result"]: {
                        "jsonrpc": "2.0",
                        "id": request["id"],
                        "result": result,
                    }),
                )
                expected = (
                    L2RpcMismatchedChainIdError
                    if error_case["expected"] == "chain"
                    else L2RpcProtocolError
                )
                with self.assertRaises(expected):
                    self._invoke_response_error_case(client, error_case["name"])

    def test_domain_vector_binds_l1_reservation_l2_chain_and_network_magic(self) -> None:
        vectors = load_vectors()
        domain = vectors["domain"]
        network = int(domain["networkMagic"])
        self.assertEqual(0, domain["l1ReservedChainId"])
        self.assertEqual(vectors["rpc"]["chainId"], domain["l2ChainId"])
        self.assertEqual(vectors["transaction"]["network"], network)
        self.assertEqual(
            domain["networkMagicLittleEndianHex"],
            network.to_bytes(4, "little").hex(),
        )
        self.assertTrue(vectors["transaction"]["signDataHex"].startswith(
            domain["networkMagicLittleEndianHex"]
        ))

    def test_pagination_vector_cursors_and_u64_values_round_trip_without_loss(self) -> None:
        pagination = load_vectors()["pagination"]
        round_trip = json.loads(json.dumps(pagination))
        batch_numbers = [
            item["batchNumber"]
            for page in round_trip["pages"]
            for item in page["items"]
        ]
        self.assertEqual(pagination["expectedBatchNumbers"], batch_numbers)
        self.assertEqual("batch:9007199254740994", round_trip["pages"][0]["nextCursor"])
        self.assertIsNone(round_trip["pages"][1]["nextCursor"])

    def test_transaction_vector_deserializes_hashes_signs_and_round_trips(self) -> None:
        vector = load_vectors()["transaction"]
        raw = bytes.fromhex(vector["rawTransactionHex"])
        unsigned = bytes.fromhex(vector["unsignedTransactionHex"])
        digest = hashlib.sha256(unsigned).digest()
        self.assertEqual(unsigned, raw[:len(unsigned)])
        self.assertEqual(vector["txid"], f"0x{digest[::-1].hex()}")

        sign_data = int(vector["network"]).to_bytes(4, "little") + digest
        self.assertEqual(vector["signDataHex"], sign_data.hex())
        signature = bytes.fromhex(vector["signatureHex"])

        witness_count_offset = len(unsigned)
        self.assertEqual(1, raw[witness_count_offset])
        invocation_length = raw[witness_count_offset + 1]
        invocation_start = witness_count_offset + 2
        invocation_end = invocation_start + invocation_length
        self.assertEqual(66, invocation_length)
        self.assertEqual(bytes.fromhex("0c40"), raw[invocation_start:invocation_start + 2])
        self.assertEqual(signature, raw[invocation_start + 2:invocation_end])
        verification_length = raw[invocation_end]
        self.assertEqual(40, verification_length)
        self.assertEqual(
            bytes.fromhex(vector["verificationScriptHex"]),
            raw[invocation_end + 1:],
        )

        public_key = ec.EllipticCurvePublicKey.from_encoded_point(
            ec.SECP256R1(),
            bytes.fromhex(vector["publicKeyCompressedHex"]),
        )
        r = int.from_bytes(signature[:32], "big")
        s = int.from_bytes(signature[32:], "big")
        public_key.verify(encode_dss_signature(r, s), sign_data, ec.ECDSA(hashes.SHA256()))

    def _invoke_case(self, client: L2RpcClient, test_case: dict[str, Any]) -> None:
        name = test_case["name"]
        if name == "batch-missing-max-u64":
            self.assertIsNone(client.get_batch((1 << 64) - 1))
        elif name == "batch-complete-large-u64":
            batch = client.get_batch(9_007_199_254_740_993)
            self.assertIsNotNone(batch)
            self.assertEqual(9_007_199_254_740_993, batch.batch_number)
            self.assertEqual("AABBCC", batch.proof)
        elif name == "batch-status":
            status = client.get_batch_status(9_007_199_254_740_993)
            self.assertEqual(9_007_199_254_740_993, status.batch_number)
            self.assertEqual(BatchStatus.FINALIZED, status.status)
        elif name == "latest-state-root":
            self.assertEqual(test_case["result"], client.get_latest_state_root())
        elif name == "state-root-max-u64":
            self.assertEqual(test_case["result"], client.get_state_root_at((1 << 64) - 1))
        elif name == "withdrawal-proof":
            self.assertEqual(bytes.fromhex("CAFEBABE"), client.get_withdrawal_proof(test_case["params"][1]))
        elif name == "message-proof":
            self.assertEqual(bytes.fromhex("DEADBEEF"), client.get_message_proof(test_case["params"][1]))
        elif name == "deposit-status-max-u64":
            status = client.get_deposit_status(1, (1 << 64) - 1)
            self.assertIsNotNone(status)
            self.assertEqual((1 << 64) - 1, status.nonce)
            self.assertEqual(9_007_199_254_740_993, status.included_in_batch)
        elif name == "canonical-asset":
            self.assertEqual(test_case["result"], client.get_canonical_asset(test_case["params"][0]))
        elif name == "bridged-asset":
            self.assertEqual(test_case["result"], client.get_bridged_asset(test_case["params"][0]))
        elif name == "security-level":
            self.assertEqual(SecurityLevel.VALIDITY, client.get_security_level().level)
        elif name == "security-label":
            self.assertEqual(SecurityLevel.VALIDIUM, client.get_security_label().security_level)
        else:
            self.fail(f"unknown conformance case {name}")

    def _invoke_response_error_case(self, client: L2RpcClient, name: str) -> None:
        if name == "mismatched-chain-id":
            client.get_security_label()
        elif name == "invalid-withdrawal-proof-hex":
            client.get_withdrawal_proof(f"0x{'4' * 64}")
        elif name == "wrong-state-root-type":
            client.get_latest_state_root()
        elif name == "unsafe-numeric-u64":
            client.get_deposit_status(1, 9_007_199_254_740_992)
        else:
            self.fail(f"unknown response-error conformance case {name}")


if __name__ == "__main__":
    unittest.main()
