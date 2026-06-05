import unittest

from neo_n4_sdk import (
    DAMode,
    ExitModel,
    L2RpcClient,
    L2RpcMismatchedChainIdError,
    L2RpcProtocolError,
    L2RpcServerError,
    L2RpcTransportError,
    SecurityLevel,
    SequencerModel,
    __version__,
)


class StubTransport:
    def __init__(self, handler):
        self.handler = handler
        self.requests = []

    def __call__(self, request):
        self.requests.append(request)
        return self.handler(request)


def ok_response(request, result):
    return {"jsonrpc": "2.0", "id": request["id"], "result": result}


class L2RpcClientTests(unittest.TestCase):
    endpoint = "http://node.example:30332"
    chain_id = 1099

    def test_package_exposes_version(self):
        self.assertEqual(__version__, "0.1.0")

    def test_ctor_rejects_zero_chain_id(self):
        with self.assertRaises(L2RpcProtocolError):
            L2RpcClient(self.endpoint, 0)

    def test_ctor_rejects_non_http_endpoint(self):
        with self.assertRaises(L2RpcProtocolError):
            L2RpcClient("ftp://node.example", 1)

    def test_ctor_rejects_invalid_url(self):
        with self.assertRaises(L2RpcProtocolError):
            L2RpcClient("not-a-url", 1)

    def test_get_latest_state_root_returns_string(self):
        expected = "0x" + "a" * 64
        transport = StubTransport(lambda request: ok_response(request, expected))
        client = L2RpcClient(self.endpoint, self.chain_id, transport=transport)

        self.assertEqual(client.get_latest_state_root(), expected)
        self.assertEqual(transport.requests[0]["method"], "getl2stateroot")
        self.assertEqual(transport.requests[0]["params"], [self.chain_id])

    def test_get_state_root_at_sends_batch_number_as_string(self):
        expected = "0x" + "b" * 64
        big_batch = (1 << 60) + 17
        transport = StubTransport(lambda request: ok_response(request, expected))
        client = L2RpcClient(self.endpoint, self.chain_id, transport=transport)

        self.assertEqual(client.get_state_root_at(big_batch), expected)
        self.assertEqual(transport.requests[0]["method"], "getl2stateroot")
        self.assertEqual(transport.requests[0]["params"], [self.chain_id, str(big_batch)])

    def test_get_withdrawal_proof_decodes_hex_and_null(self):
        responses = ["CAFEBABE", None]
        transport = StubTransport(lambda request: ok_response(request, responses.pop(0)))
        client = L2RpcClient(self.endpoint, self.chain_id, transport=transport)

        self.assertEqual(client.get_withdrawal_proof("0x" + "1" * 64), bytes([0xCA, 0xFE, 0xBA, 0xBE]))
        self.assertIsNone(client.get_withdrawal_proof("0x" + "2" * 64))

    def test_get_deposit_status_decodes_nullable_batch_and_checks_source_chain(self):
        transport = StubTransport(lambda request: ok_response(request, {
            "sourceChainId": 1,
            "nonce": "42",
            "consumedOnL2": False,
            "includedInBatch": None,
        }))
        client = L2RpcClient(self.endpoint, self.chain_id, transport=transport)

        status = client.get_deposit_status(1, 42)
        self.assertEqual(status.source_chain_id, 1)
        self.assertEqual(status.nonce, 42)
        self.assertFalse(status.consumed_on_l2)
        self.assertIsNone(status.included_in_batch)
        self.assertEqual(transport.requests[0]["params"], [1, "42"])

    def test_get_security_label_decodes_all_dimensions(self):
        transport = StubTransport(lambda request: ok_response(request, {
            "chainId": self.chain_id,
            "securityLevel": SecurityLevel.VALIDIUM,
            "daMode": DAMode.NEOFS,
            "gatewayEnabled": True,
            "sequencer": SequencerModel.DBFT_COMMITTEE,
            "exit": ExitModel.DELAYED,
        }))
        client = L2RpcClient(self.endpoint, self.chain_id, transport=transport)

        label = client.get_security_label()
        self.assertEqual(label.security_level, SecurityLevel.VALIDIUM)
        self.assertEqual(label.da_mode, DAMode.NEOFS)
        self.assertTrue(label.gateway_enabled)
        self.assertEqual(label.sequencer, SequencerModel.DBFT_COMMITTEE)
        self.assertEqual(label.exit, ExitModel.DELAYED)

    def test_server_error_surfaces_with_code(self):
        def handler(request):
            return {"jsonrpc": "2.0", "id": request["id"], "error": {"code": -32000, "message": "node not synced"}}

        client = L2RpcClient(self.endpoint, self.chain_id, transport=StubTransport(handler))
        with self.assertRaises(L2RpcServerError) as raised:
            client.get_latest_state_root()
        self.assertEqual(raised.exception.code, -32000)

    def test_transport_exception_is_retry_safe_transport_error(self):
        def handler(_request):
            raise TimeoutError("deadline")

        client = L2RpcClient(self.endpoint, self.chain_id, transport=StubTransport(handler))
        with self.assertRaises(L2RpcTransportError):
            client.get_latest_state_root()

    def test_response_id_mismatch_is_protocol_error(self):
        client = L2RpcClient(
            self.endpoint,
            self.chain_id,
            transport=StubTransport(lambda _request: {"jsonrpc": "2.0", "id": 99999, "result": "0xabc"}),
        )
        with self.assertRaises(L2RpcProtocolError):
            client.get_latest_state_root()

    def test_chain_id_mismatch_is_config_error(self):
        client = L2RpcClient(
            self.endpoint,
            self.chain_id,
            transport=StubTransport(lambda request: ok_response(request, {"chainId": 9999, "level": SecurityLevel.OPTIMISTIC})),
        )
        with self.assertRaises(L2RpcMismatchedChainIdError) as raised:
            client.get_security_level()
        self.assertEqual(raised.exception.expected, self.chain_id)
        self.assertEqual(raised.exception.got, 9999)


if __name__ == "__main__":
    unittest.main()
