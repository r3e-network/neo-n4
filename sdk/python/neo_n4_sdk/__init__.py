"""Typed Python client for the Neo Elastic Network L2 RPC surface.

The module mirrors the C#, TypeScript, and Rust SDKs: the same 10 JSON-RPC
methods, the same four-way error taxonomy, and the same chain-id cross-checks.
It intentionally uses only Python's standard library so operators can run it in
deployment scripts without vendoring a dependency stack.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import IntEnum
from importlib.metadata import PackageNotFoundError, version
import json
from typing import Any, Callable, Mapping
import urllib.error
import urllib.parse
import urllib.request

JsonObject = dict[str, Any]
Transport = Callable[[JsonObject], Mapping[str, Any]]

_U32_MAX = (1 << 32) - 1
_U64_MAX = (1 << 64) - 1
_SOURCE_VERSION = "0.1.0"

try:
    __version__ = version("neo-n4-sdk")
except PackageNotFoundError:
    __version__ = _SOURCE_VERSION


class SecurityLevel(IntEnum):
    """doc.md §16.2 security-level dimension."""

    SIDECHAIN = 0
    SETTLED = 1
    OPTIMISTIC = 2
    VALIDITY = 3
    VALIDIUM = 4


class DAMode(IntEnum):
    """doc.md §16.2 data-availability dimension."""

    L1 = 0
    NEOFS = 1
    EXTERNAL = 2
    DAC = 3


class SequencerModel(IntEnum):
    """doc.md §16.2 sequencer-governance dimension."""

    CENTRALIZED = 0
    DBFT_COMMITTEE = 1
    POS_ROTATION = 2


class ExitModel(IntEnum):
    """doc.md §16.2 withdrawal/exit dimension."""

    PERMISSIONLESS = 0
    DELAYED = 1
    OPERATOR_ASSISTED = 2


class ProofType(IntEnum):
    """Batch proof type returned by getl2batch."""

    NONE = 0
    MULTISIG = 1
    OPTIMISTIC = 2
    ZK = 3


class BatchStatus(IntEnum):
    """Batch lifecycle status returned by getl2batchstatus."""

    UNKNOWN = 0
    PENDING = 1
    CHALLENGEABLE = 2
    FINALIZED = 3
    REVERTED = 4


@dataclass(frozen=True)
class L2BatchView:
    chain_id: int
    batch_number: int
    first_block: int
    last_block: int
    pre_state_root: str
    post_state_root: str
    tx_root: str
    receipt_root: str
    withdrawal_root: str
    l2_to_l1_message_root: str
    l2_to_l2_message_root: str
    da_commitment: str
    public_input_hash: str
    proof_type: ProofType | int
    proof: str
    encoded: str


@dataclass(frozen=True)
class BatchStatusResponse:
    chain_id: int
    batch_number: int
    status: BatchStatus | int
    status_name: str


@dataclass(frozen=True)
class DepositStatusResponse:
    source_chain_id: int
    nonce: int
    consumed_on_l2: bool
    included_in_batch: int | None


@dataclass(frozen=True)
class SecurityLevelResponse:
    chain_id: int
    level: SecurityLevel | int


@dataclass(frozen=True)
class SecurityLabelResponse:
    chain_id: int
    security_level: SecurityLevel | int
    da_mode: DAMode | int
    gateway_enabled: bool
    sequencer: SequencerModel | int
    exit: ExitModel | int


class L2RpcError(Exception):
    """Base class for all Neo N4 L2 RPC client errors."""

    def __init__(self, method: str, message: str) -> None:
        self.method = method
        super().__init__(f"{method}: {message}")


class L2RpcTransportError(L2RpcError):
    """HTTP-layer failure such as timeout, connection refused, or non-2xx."""


class L2RpcProtocolError(L2RpcError):
    """JSON-RPC envelope, parse, or response-shape failure."""


class L2RpcServerError(L2RpcError):
    """JSON-RPC server-side error carrying the integer error code."""

    def __init__(self, method: str, code: int, message: str) -> None:
        self.code = code
        super().__init__(method, f"server error {code}: {message}")


class L2RpcMismatchedChainIdError(L2RpcError):
    """Server returned a chain id different from the client configuration."""

    def __init__(self, method: str, expected: int, got: int) -> None:
        self.expected = expected
        self.got = got
        super().__init__(method, f"server returned chainId {got}, expected {expected}")


class L2RpcClient:
    """Production JSON-RPC client for a Neo Elastic Network L2 node."""

    def __init__(
        self,
        endpoint: str,
        chain_id: int,
        *,
        transport: Transport | None = None,
        timeout: float = 30.0,
    ) -> None:
        parsed = urllib.parse.urlparse(endpoint)
        if parsed.scheme not in {"http", "https"} or not parsed.netloc:
            raise L2RpcProtocolError("<ctor>", "endpoint URL must be absolute http(s)")
        self.chain_id = _require_uint(chain_id, "chain_id", _U32_MAX)
        if self.chain_id == 0:
            raise L2RpcProtocolError("<ctor>", "chainId 0 is reserved for L1")
        self.endpoint = endpoint
        self._transport = transport
        self._timeout = timeout
        self._next_id = 0

    def get_batch(self, batch_number: int) -> L2BatchView | None:
        result = self._call("getl2batch", [self.chain_id, _u64_wire(batch_number, "batch_number")])
        if result is None:
            return None
        obj = _require_mapping(result, "getl2batch")
        self._assert_chain_id(obj, "getl2batch")
        batch = _parse_batch_view(obj, "getl2batch")
        if batch.batch_number != batch_number:
            raise L2RpcProtocolError(
                "getl2batch",
                f"response batchNumber {batch.batch_number} does not match request {batch_number}",
            )
        return batch

    def get_batch_status(self, batch_number: int) -> BatchStatusResponse:
        result = self._call("getl2batchstatus", [self.chain_id, _u64_wire(batch_number, "batch_number")])
        obj = _require_mapping(result, "getl2batchstatus")
        self._assert_chain_id(obj, "getl2batchstatus")
        response = BatchStatusResponse(
            chain_id=_int_field(obj, "chainId"),
            batch_number=_u64_field(obj, "batchNumber", "getl2batchstatus"),
            status=_enum_field(BatchStatus, obj, "status"),
            status_name=str(obj.get("statusName", "")),
        )
        if response.batch_number != batch_number:
            raise L2RpcProtocolError(
                "getl2batchstatus",
                f"response batchNumber {response.batch_number} does not match request {batch_number}",
            )
        return response

    def get_latest_state_root(self) -> str:
        result = self._call("getl2stateroot", [self.chain_id])
        if not isinstance(result, str):
            raise L2RpcProtocolError("getl2stateroot", "expected string")
        return result

    def get_state_root_at(self, batch_number: int) -> str:
        result = self._call("getl2stateroot", [self.chain_id, _u64_wire(batch_number, "batch_number")])
        if not isinstance(result, str):
            raise L2RpcProtocolError("getl2stateroot", "expected string")
        return result

    def get_withdrawal_proof(self, leaf: str) -> bytes | None:
        return self._get_optional_hex("getl2withdrawalproof", [self.chain_id, leaf])

    def get_message_proof(self, message_hash: str) -> bytes | None:
        return self._get_optional_hex("getl2messageproof", [self.chain_id, message_hash])

    def get_deposit_status(self, source_chain_id: int, nonce: int) -> DepositStatusResponse | None:
        source = _require_uint(source_chain_id, "source_chain_id", _U32_MAX)
        result = self._call("getl1depositstatus", [source, _u64_wire(nonce, "nonce")])
        if result is None:
            return None
        obj = _require_mapping(result, "getl1depositstatus")
        got = _int_field(obj, "sourceChainId")
        if got != source:
            raise L2RpcMismatchedChainIdError("getl1depositstatus", source, got)
        included = obj.get("includedInBatch")
        response = DepositStatusResponse(
            source_chain_id=got,
            nonce=_u64_field(obj, "nonce", "getl1depositstatus"),
            consumed_on_l2=_bool_field(obj, "consumedOnL2", "getl1depositstatus"),
            included_in_batch=(
                None
                if included is None
                else _parse_u64(included, "getl1depositstatus", "includedInBatch")
            ),
        )
        if response.nonce != nonce:
            raise L2RpcProtocolError(
                "getl1depositstatus",
                f"response nonce {response.nonce} does not match request {nonce}",
            )
        return response

    def get_canonical_asset(self, l2_asset: str) -> str | None:
        return self._get_optional_string("getcanonicalasset", [l2_asset])

    def get_bridged_asset(self, l1_asset: str) -> str | None:
        return self._get_optional_string("getbridgedasset", [l1_asset, self.chain_id])

    def get_security_level(self) -> SecurityLevelResponse:
        result = self._call("getsecuritylevel", [self.chain_id])
        obj = _require_mapping(result, "getsecuritylevel")
        self._assert_chain_id(obj, "getsecuritylevel")
        return SecurityLevelResponse(
            chain_id=_int_field(obj, "chainId"),
            level=_enum_field(SecurityLevel, obj, "level"),
        )

    def get_security_label(self) -> SecurityLabelResponse:
        result = self._call("getsecuritylabel", [self.chain_id])
        obj = _require_mapping(result, "getsecuritylabel")
        self._assert_chain_id(obj, "getsecuritylabel")
        return SecurityLabelResponse(
            chain_id=_int_field(obj, "chainId"),
            security_level=_enum_field(SecurityLevel, obj, "securityLevel"),
            da_mode=_enum_field(DAMode, obj, "daMode"),
            gateway_enabled=_bool_field(obj, "gatewayEnabled", "getsecuritylabel"),
            sequencer=_enum_field(SequencerModel, obj, "sequencer"),
            exit=_enum_field(ExitModel, obj, "exit"),
        )

    def _get_optional_hex(self, method: str, params: list[Any]) -> bytes | None:
        result = self._call(method, params)
        if result is None:
            return None
        if not isinstance(result, str):
            raise L2RpcProtocolError(method, "expected hex string")
        clean = result[2:] if result.startswith(("0x", "0X")) else result
        if len(clean) % 2:
            raise L2RpcProtocolError(method, f"invalid hex string length: {len(clean)}")
        try:
            return bytes.fromhex(clean)
        except ValueError as exc:
            raise L2RpcProtocolError(method, f"invalid hex: {exc}") from exc

    def _get_optional_string(self, method: str, params: list[Any]) -> str | None:
        result = self._call(method, params)
        if result is None:
            return None
        if not isinstance(result, str):
            raise L2RpcProtocolError(method, "expected string")
        return result

    def _assert_chain_id(self, obj: Mapping[str, Any], method: str) -> None:
        if "chainId" not in obj:
            raise L2RpcProtocolError(method, "response chainId is missing")
        got = _int_field(obj, "chainId")
        if got != self.chain_id:
            raise L2RpcMismatchedChainIdError(method, self.chain_id, got)

    def _call(self, method: str, params: list[Any]) -> Any:
        self._next_id += 1
        request: JsonObject = {
            "jsonrpc": "2.0",
            "method": method,
            "params": params,
            "id": self._next_id,
        }

        try:
            response = self._transport(request) if self._transport is not None else self._http_call(request, method)
        except L2RpcError:
            raise
        except (TimeoutError, OSError) as exc:
            raise L2RpcTransportError(method, f"request failed: {exc}") from exc

        envelope = _require_mapping(response, method)
        if envelope.get("jsonrpc") != "2.0":
            raise L2RpcProtocolError(method, "response jsonrpc must be '2.0'")
        response_id = envelope.get("id")
        if isinstance(response_id, bool) or not isinstance(response_id, int):
            raise L2RpcProtocolError(method, "response id must be a JSON integer")
        if response_id != request["id"]:
            raise L2RpcProtocolError(method, f"response id {response_id} does not match request id {request['id']}")

        error = envelope.get("error")
        if error is not None:
            err = _require_mapping(error, method)
            code = int(err.get("code", -32603))
            message = str(err.get("message", "rpc error"))
            raise L2RpcServerError(method, code, message)

        if "result" not in envelope:
            raise L2RpcProtocolError(method, "response is missing result")
        return envelope["result"]

    def _http_call(self, request: JsonObject, method: str) -> Mapping[str, Any]:
        body = json.dumps(request, separators=(",", ":")).encode("utf-8")
        http_request = urllib.request.Request(
            self.endpoint,
            data=body,
            headers={"Content-Type": "application/json", "Accept": "application/json"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(http_request, timeout=self._timeout) as response:
                status = response.status
                raw = response.read()
        except urllib.error.HTTPError as exc:
            snippet = exc.read(200).decode("utf-8", errors="replace")
            raise L2RpcTransportError(method, f"http {exc.code}: {snippet}") from exc
        except urllib.error.URLError as exc:
            raise L2RpcTransportError(method, f"http request failed: {exc.reason}") from exc

        if status < 200 or status >= 300:
            snippet = raw[:200].decode("utf-8", errors="replace")
            raise L2RpcTransportError(method, f"http {status}: {snippet}")
        try:
            parsed = json.loads(raw.decode("utf-8"))
        except json.JSONDecodeError as exc:
            raise L2RpcProtocolError(method, f"parse error: {exc}") from exc
        return _require_mapping(parsed, method)


def _parse_batch_view(obj: Mapping[str, Any], method: str) -> L2BatchView:
    return L2BatchView(
        chain_id=_int_field(obj, "chainId"),
        batch_number=_u64_field(obj, "batchNumber", method),
        first_block=_u64_field(obj, "firstBlock", method),
        last_block=_u64_field(obj, "lastBlock", method),
        pre_state_root=_string_field(obj, "preStateRoot", method),
        post_state_root=_string_field(obj, "postStateRoot", method),
        tx_root=_string_field(obj, "txRoot", method),
        receipt_root=_string_field(obj, "receiptRoot", method),
        withdrawal_root=_string_field(obj, "withdrawalRoot", method),
        l2_to_l1_message_root=_string_field(obj, "l2ToL1MessageRoot", method),
        l2_to_l2_message_root=_string_field(obj, "l2ToL2MessageRoot", method),
        da_commitment=_string_field(obj, "daCommitment", method),
        public_input_hash=_string_field(obj, "publicInputHash", method),
        proof_type=_enum_field(ProofType, obj, "proofType"),
        proof=_hex_field(obj, "proof", method),
        encoded=_hex_field(obj, "encoded", method),
    )


def _require_mapping(value: Any, method: str) -> Mapping[str, Any]:
    if not isinstance(value, Mapping):
        raise L2RpcProtocolError(method, "expected object response")
    return value


def _int_field(obj: Mapping[str, Any], key: str) -> int:
    try:
        value = obj[key]
        if isinstance(value, bool) or not isinstance(value, int):
            raise ValueError("boolean is not an integer")
        return value
    except (KeyError, TypeError, ValueError) as exc:
        raise L2RpcProtocolError("<decode>", f"field {key} must be an integer") from exc


def _parse_u64(value: Any, method: str, key: str) -> int:
    if isinstance(value, str) and (value == "0" or value.isdigit() and not value.startswith("0")):
        parsed = int(value)
    else:
        raise L2RpcProtocolError(method, f"field {key} must be a canonical decimal u64 string")
    if parsed < 0 or parsed > _U64_MAX:
        raise L2RpcProtocolError(method, f"field {key} must be a canonical decimal u64 string")
    return parsed


def _u64_field(obj: Mapping[str, Any], key: str, method: str) -> int:
    if key not in obj:
        raise L2RpcProtocolError(method, f"field {key} is missing")
    return _parse_u64(obj[key], method, key)


def _string_field(obj: Mapping[str, Any], key: str, method: str) -> str:
    value = obj.get(key)
    if not isinstance(value, str):
        raise L2RpcProtocolError(method, f"field {key} must be a string")
    return value


def _hex_field(obj: Mapping[str, Any], key: str, method: str) -> str:
    value = _string_field(obj, key, method)
    clean = value[2:] if value.startswith(("0x", "0X")) else value
    if len(clean) % 2:
        raise L2RpcProtocolError(method, f"field {key} must be an even-length hex string")
    try:
        bytes.fromhex(clean)
    except ValueError as exc:
        raise L2RpcProtocolError(method, f"field {key} must be a hex string") from exc
    return value


def _bool_field(obj: Mapping[str, Any], key: str, method: str) -> bool:
    value = obj.get(key)
    if not isinstance(value, bool):
        raise L2RpcProtocolError(method, f"field {key} must be a boolean")
    return value


def _enum_field(enum_type: type[IntEnum], obj: Mapping[str, Any], key: str) -> IntEnum | int:
    value = _int_field(obj, key)
    try:
        return enum_type(value)
    except ValueError:
        return value


def _u64_wire(value: int, name: str) -> str:
    return str(_require_uint(value, name, _U64_MAX))


def _require_uint(value: int, name: str, max_value: int) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        raise L2RpcProtocolError("<arg>", f"{name} must be an integer")
    if value < 0 or value > max_value:
        raise L2RpcProtocolError("<arg>", f"{name} must be in range 0..{max_value}")
    return value


__all__ = [
    "BatchStatus",
    "BatchStatusResponse",
    "DAMode",
    "DepositStatusResponse",
    "ExitModel",
    "L2BatchView",
    "L2RpcClient",
    "L2RpcError",
    "L2RpcMismatchedChainIdError",
    "L2RpcProtocolError",
    "L2RpcServerError",
    "L2RpcTransportError",
    "ProofType",
    "SecurityLabelResponse",
    "SecurityLevel",
    "SecurityLevelResponse",
    "SequencerModel",
    "__version__",
]
