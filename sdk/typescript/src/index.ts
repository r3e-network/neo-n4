/**
 * @neo-n4/sdk — typed TypeScript client for the Neo Elastic Network L2 RPC surface.
 * Wire-compatible with any node running Neo.Plugins.L2Rpc (10 methods, doc.md §14.1).
 *
 * Mirrors the .NET reference SDK in src/Neo.L2.Sdk: same method names, same
 * 4-way exception split, same chainId cross-check.
 */

/* ─────────────────────── enum mirrors of doc.md §16.2 ─────────────────────── */

export enum SecurityLevel {
  Sidechain = 0,
  Settled = 1,
  Optimistic = 2,
  Validity = 3,
  Validium = 4,
}

export enum DAMode {
  L1 = 0,
  NeoFS = 1,
  External = 2,
  DAC = 3,
  /**
   * Local filesystem DA — a development-only mode.
   *
   * The C# `DAMode.Local` value is `byte.MaxValue` (255), which is OUTSIDE the range of the
   * four production modes above. It must be represented here explicitly, otherwise a node
   * running in local-DA mode would return 255 and the SDK would hand the caller a bare
   * number that no enum member describes.
   */
  Local = 255,
}

export enum SequencerModel {
  Centralized = 0,
  DbftCommittee = 1,
  /**
   * Permissionless decentralized sequencer set — anyone may propose ordering.
   *
   * Named to match the authoritative C# `SequencerModel.Decentralized`. The previous
   * "PoSRotation" name described a DIFFERENT model (proof-of-stake rotation), so the SDK
   * was advertising a sequencer model the chain does not actually run.
   */
  Decentralized = 2,
}

export enum ExitModel {
  Permissionless = 0,
  Delayed = 1,
  OperatorAssisted = 2,
}

export enum ProofType {
  None = 0,
  Multisig = 1,
  Optimistic = 2,
  Zk = 3,
}

export enum BatchStatus {
  Unknown = 0,
  Pending = 1,
  Challengeable = 2,
  Finalized = 3,
  Reverted = 4,
}

/* ─────────────────────────── typed RPC responses ──────────────────────────── */

export interface L2BatchView {
  chainId: number;
  batchNumber: bigint;
  firstBlock: bigint;
  lastBlock: bigint;
  preStateRoot: string;
  postStateRoot: string;
  txRoot: string;
  receiptRoot: string;
  withdrawalRoot: string;
  l2ToL1MessageRoot: string;
  l2ToL2MessageRoot: string;
  daCommitment: string;
  publicInputHash: string;
  proofType: ProofType;
  /** Raw proof bytes as hex string. */
  proof: string;
  /** Canonical wire-format batch encoding as hex string. */
  encoded: string;
}

export interface BatchStatusResponse {
  chainId: number;
  batchNumber: bigint;
  status: BatchStatus;
  statusName: string;
}

export interface DepositStatusResponse {
  sourceChainId: number;
  nonce: bigint;
  consumedOnL2: boolean;
  /** Null when still pending; otherwise the batch number that consumed the deposit. */
  includedInBatch: bigint | null;
}

export interface SecurityLevelResponse {
  chainId: number;
  level: SecurityLevel;
}

export interface SecurityLabelResponse {
  chainId: number;
  securityLevel: SecurityLevel;
  daMode: DAMode;
  gatewayEnabled: boolean;
  sequencer: SequencerModel;
  exit: ExitModel;
}

/* ─────────────────────────── error taxonomy ────────────────────────────── */

export abstract class L2RpcError extends Error {
  constructor(public readonly method: string, message: string) {
    super(`${method}: ${message}`);
    this.name = new.target.name;
  }
}

/** HTTP-layer failure (timeout, connection refused, non-2xx). Retry-safe. */
export class L2RpcTransportError extends L2RpcError {}

/** JSON-RPC envelope or parse failure (bad shape, mismatched id). NOT retry-safe. */
export class L2RpcProtocolError extends L2RpcError {}

/** Server returned a JSON-RPC error response. Carries the int code. */
export class L2RpcServerError extends L2RpcError {
  /**
   * @param data The server's `data` field, when present.
   *
   * Neo's `RpcError.ToJson()` carries the actionable detail (e.g. the parameter-validation
   * message) in `data`. Exposing it lets callers branch on the real cause instead of
   * parsing the combined message string.
   */
  constructor(
    method: string,
    public readonly code: number,
    message: string,
    public readonly data?: string,
  ) {
    super(method, `server error ${code}: ${message}`);
  }
}

/** Server's chainId differs from the client's. Config error — don't retry. */
export class L2RpcMismatchedChainIdError extends L2RpcError {
  constructor(method: string, public readonly expected: number, public readonly got: number) {
    super(method, `server returned chainId ${got}, expected ${expected}`);
  }
}

/* ─────────────────────────── client ────────────────────────────── */

export interface L2RpcClientOptions {
  /** RPC endpoint URL (http:// or https://). */
  endpoint: string;
  /** Expected chain id; cross-checked on every response field that includes one. */
  chainId: number;
  /** Optional fetch impl (default: globalThis.fetch). Lets tests inject a stub. */
  fetch?: typeof fetch;
  /** Optional request timeout in ms (default 30000). */
  timeoutMs?: number;
}

/**
 * Production client for an L2 node's RPC endpoint. Thread-safe: each call gets
 * its own monotonic JSON-RPC id. Constructor validates the endpoint URL +
 * rejects non-http(s) schemes so misconfiguration surfaces early.
 */
export class L2RpcClient {
  private readonly endpoint: URL;
  public readonly chainId: number;
  private readonly fetchImpl: typeof fetch;
  private readonly timeoutMs: number;
  private nextId = 0;

  constructor(opts: L2RpcClientOptions) {
    if (!opts.endpoint) throw new Error("endpoint required");
    let url: URL;
    try {
      url = new URL(opts.endpoint);
    } catch (e) {
      throw new Error(`invalid endpoint URL: ${(e as Error).message}`);
    }
    if (url.protocol !== "http:" && url.protocol !== "https:") {
      throw new Error(`endpoint scheme '${url.protocol}' must be http(s):`);
    }
    requireU32(opts.chainId, "chainId", false);
    this.endpoint = url;
    this.chainId = opts.chainId;
    this.fetchImpl = opts.fetch ?? globalThis.fetch.bind(globalThis);
    this.timeoutMs = opts.timeoutMs ?? 30_000;
  }

  /** getl2batch — full batch commitment for batchNumber; null if not yet sealed. */
  async getBatch(batchNumber: bigint): Promise<L2BatchView | null> {
    // Send as JSON string to preserve full u64 precision — `Number(bigint)` silently
    // truncates above 2^53-1. Server's L2RpcMethods.ReadULong accepts JString via
    // ulong.Parse, matching the Rust + .NET SDKs which pass full 64-bit unsigned.
    const result = await this.call("getl2batch", [this.chainId, u64Wire(batchNumber, "batchNumber")]);
    if (result === null) return null;
    if (typeof result !== "object" || result === null)
      throw new L2RpcProtocolError("getl2batch", "expected object response");
    this.assertChainId(result as object, "getl2batch");
    const batch = parseBatchView(result as Record<string, unknown>, "getl2batch");
    if (batch.batchNumber.toString() !== batchNumber.toString())
      throw new L2RpcProtocolError(
        "getl2batch",
        `response batchNumber ${batch.batchNumber} does not match request ${batchNumber}`,
      );
    return batch;
  }

  /** getl2batchstatus — pending / finalized / challenged / etc. */
  async getBatchStatus(batchNumber: bigint): Promise<BatchStatusResponse> {
    const result = await this.call("getl2batchstatus", [this.chainId, u64Wire(batchNumber, "batchNumber")]);
    if (typeof result !== "object" || result === null)
      throw new L2RpcProtocolError("getl2batchstatus", "expected object response");
    this.assertChainId(result, "getl2batchstatus");
    const r = result as Record<string, unknown>;
    const response: BatchStatusResponse = {
      chainId: requireU32Value(r.chainId, "getl2batchstatus", "chainId"),
      batchNumber: parseU64Wire(r.batchNumber, "getl2batchstatus", "batchNumber"),
      status: requireEnumValue(r.status, "getl2batchstatus", "status", ALLOWED_BATCH_STATUSES) as BatchStatus,
      statusName: String(r.statusName ?? ""),
    };
    if (response.batchNumber.toString() !== batchNumber.toString())
      throw new L2RpcProtocolError(
        "getl2batchstatus",
        `response batchNumber ${response.batchNumber} does not match request ${batchNumber}`,
      );
    return response;
  }

  /** getl2stateroot — latest sealed state root. */
  async getLatestStateRoot(): Promise<string> {
    const result = await this.call("getl2stateroot", [this.chainId]);
    if (typeof result !== "string")
      throw new L2RpcProtocolError("getl2stateroot", "expected string");
    return result;
  }

  /**
   * getl2stateroot at a specific batch height.
   *
   * Returns `null` when the batch is unknown to the node. The zero hash is a legal state
   * root (a genesis batch), so "unknown" must stay distinguishable from "root is zero".
   */
  async getStateRootAt(batchNumber: bigint): Promise<string | null> {
    const result = await this.call("getl2stateroot", [this.chainId, u64Wire(batchNumber, "batchNumber")]);
    if (result === null) return null;
    if (typeof result !== "string")
      throw new L2RpcProtocolError("getl2stateroot", "expected string");
    return result;
  }

  /** getl2withdrawalproof — Merkle proof bytes for a leaf; null if unknown. */
  async getWithdrawalProof(leaf: string): Promise<Uint8Array | null> {
    const result = await this.call("getl2withdrawalproof", [this.chainId, leaf]);
    if (result === null) return null;
    if (typeof result !== "string")
      throw new L2RpcProtocolError("getl2withdrawalproof", "expected hex string");
    return hexToBytes("getl2withdrawalproof", result);
  }

  /** getl2messageproof — Merkle proof bytes for a cross-chain message; null if unknown. */
  async getMessageProof(messageHash: string): Promise<Uint8Array | null> {
    const result = await this.call("getl2messageproof", [this.chainId, messageHash]);
    if (result === null) return null;
    if (typeof result !== "string")
      throw new L2RpcProtocolError("getl2messageproof", "expected hex string");
    return hexToBytes("getl2messageproof", result);
  }

  /** getl1depositstatus — has an L1 deposit (sourceChain, nonce) been consumed? null if untracked. */
  async getDepositStatus(sourceChainId: number, nonce: bigint): Promise<DepositStatusResponse | null> {
    requireU32(sourceChainId, "sourceChainId", true);
    const result = await this.call("getl1depositstatus", [sourceChainId, u64Wire(nonce, "nonce")]);
    if (result === null) return null;
    if (typeof result !== "object")
      throw new L2RpcProtocolError("getl1depositstatus", "expected object response");
    const r = result as Record<string, unknown>;
    // Cross-check the requested sourceChainId matches what the server returned — a
    // misbehaving server returning another chain's deposit would otherwise sail through.
    const respChain = requireU32Value(r.sourceChainId, "getl1depositstatus", "sourceChainId");
    if (respChain !== sourceChainId)
      throw new L2RpcMismatchedChainIdError("getl1depositstatus", sourceChainId, respChain);
    const response: DepositStatusResponse = {
      sourceChainId: respChain,
      nonce: parseU64Wire(r.nonce, "getl1depositstatus", "nonce"),
      consumedOnL2: requireBoolean(r.consumedOnL2, "getl1depositstatus", "consumedOnL2"),
      includedInBatch: r.includedInBatch === null || r.includedInBatch === undefined
        ? null
        : parseU64Wire(r.includedInBatch, "getl1depositstatus", "includedInBatch"),
    };
    if (response.nonce.toString() !== nonce.toString())
      throw new L2RpcProtocolError(
        "getl1depositstatus",
        `response nonce ${response.nonce} does not match request ${nonce}`,
      );
    return response;
  }

  /** getcanonicalasset — L2-side asset hash → L1 asset hash; null if not bridged. */
  async getCanonicalAsset(l2Asset: string): Promise<string | null> {
    const result = await this.call("getcanonicalasset", [l2Asset]);
    if (result === null) return null;
    if (typeof result !== "string")
      throw new L2RpcProtocolError("getcanonicalasset", "expected string");
    return result;
  }

  /** getbridgedasset — L1 asset hash → L2 bridged hash; null if not bridged. */
  async getBridgedAsset(l1Asset: string): Promise<string | null> {
    const result = await this.call("getbridgedasset", [l1Asset, this.chainId]);
    if (result === null) return null;
    if (typeof result !== "string")
      throw new L2RpcProtocolError("getbridgedasset", "expected string");
    return result;
  }

  /** getsecuritylevel — single-dimension §16.2 chain-type label. */
  async getSecurityLevel(): Promise<SecurityLevelResponse> {
    const result = await this.call("getsecuritylevel", [this.chainId]);
    if (typeof result !== "object" || result === null)
      throw new L2RpcProtocolError("getsecuritylevel", "expected object response");
    this.assertChainId(result, "getsecuritylevel");
    const r = result as Record<string, unknown>;
    return {
      chainId: requireU32Value(r.chainId, "getsecuritylevel", "chainId"),
      level: requireEnumValue(r.level, "getsecuritylevel", "level", ALLOWED_SECURITY_LEVELS) as SecurityLevel,
    };
  }

  /** getsecuritylabel — full doc.md §16.2 5-dimension label. */
  async getSecurityLabel(): Promise<SecurityLabelResponse> {
    const result = await this.call("getsecuritylabel", [this.chainId]);
    if (typeof result !== "object" || result === null)
      throw new L2RpcProtocolError("getsecuritylabel", "expected object response");
    this.assertChainId(result, "getsecuritylabel");
    const r = result as Record<string, unknown>;
    return {
      chainId: requireU32Value(r.chainId, "getsecuritylabel", "chainId"),
      securityLevel: requireEnumValue(r.securityLevel, "getsecuritylabel", "securityLevel", ALLOWED_SECURITY_LEVELS) as SecurityLevel,
      daMode: requireEnumValue(r.daMode, "getsecuritylabel", "daMode", ALLOWED_DA_MODES) as DAMode,
      gatewayEnabled: requireBoolean(r.gatewayEnabled, "getsecuritylabel", "gatewayEnabled"),
      sequencer: requireEnumValue(r.sequencer, "getsecuritylabel", "sequencer", ALLOWED_SEQUENCER_MODELS) as SequencerModel,
      exit: requireEnumValue(r.exit, "getsecuritylabel", "exit", ALLOWED_EXIT_MODELS) as ExitModel,
    };
  }

  private assertChainId(obj: object, method: string): void {
    const r = obj as Record<string, unknown>;
    const responseChainId = requireU32Value(r.chainId, method, "chainId");
    if (responseChainId !== this.chainId)
      throw new L2RpcMismatchedChainIdError(method, this.chainId, responseChainId);
  }

  private async call(method: string, params: unknown[]): Promise<unknown> {
    const id = ++this.nextId;
    const body = JSON.stringify({ jsonrpc: "2.0", method, params, id });

    const controller = new AbortController();
    const timeoutHandle = setTimeout(() => controller.abort(), this.timeoutMs);

    let parsed: unknown;
    try {
      const response = await this.fetchImpl(this.endpoint.toString(), {
        method: "POST",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body,
        signal: controller.signal,
      });

      if (!response.ok) {
        const text = await response.text();
        const snippet = text.length <= 200 ? text : text.slice(0, 200);
        throw new L2RpcTransportError(method, `http ${response.status}: ${snippet}`);
      }

      // The body read MUST stay inside the timeout window. Clearing the timer as soon as
      // the response HEADERS arrive left the body read unbounded: a server that sends
      // headers and then stalls (or never finishes the body) could hang the caller
      // forever, and the documented `timeoutMs` would silently not apply. The timer is
      // therefore cleared only after the body has been consumed.
      try {
        parsed = await response.json();
      } catch (e) {
        const err = e as Error;
        if (err.name === "AbortError") {
          throw new L2RpcTransportError(method, `timeout after ${this.timeoutMs}ms`);
        }
        throw new L2RpcProtocolError(method, `parse error: ${err.message}`);
      }
    } catch (e) {
      if (e instanceof L2RpcTransportError || e instanceof L2RpcProtocolError) throw e;
      const err = e as Error;
      if (err.name === "AbortError") {
        throw new L2RpcTransportError(method, `timeout after ${this.timeoutMs}ms`);
      }
      throw new L2RpcTransportError(method, `fetch failed: ${err.message}`);
    } finally {
      clearTimeout(timeoutHandle);
    }

    if (typeof parsed !== "object" || parsed === null) {
      throw new L2RpcProtocolError(method, "non-object response");
    }
    const env = parsed as Record<string, unknown>;
    if (env.jsonrpc !== "2.0") {
      throw new L2RpcProtocolError(method, "response jsonrpc must be '2.0'");
    }
    if (typeof env.id !== "number" || !Number.isSafeInteger(env.id) || env.id !== id) {
      throw new L2RpcProtocolError(method, `response id ${String(env.id)} does not match request id ${id}`);
    }
    if (env.error !== undefined && env.error !== null) {
      // A non-object `error` payload (some proxies relay it as a plain string) must not be
      // flattened into a generic "rpc error" — that erases the server's actual message.
      if (typeof env.error !== "object") {
        throw new L2RpcServerError(method, -32603, String(env.error));
      }
      const err = env.error as Record<string, unknown>;
      const code = typeof err.code === "number" ? err.code : -32603;
      const msg = typeof err.message === "string" ? err.message : "rpc error";
      // Neo's `RpcError.ToJson()` emits a `data` field carrying the error detail (for
      // instance the parameter-validation message). Dropping it loses the only actionable
      // part of the failure.
      const data = typeof err.data === "string" ? err.data : undefined;
      throw new L2RpcServerError(method, code, data ? `${msg} - ${data}` : msg, data);
    }
    if (!Object.prototype.hasOwnProperty.call(env, "result"))
      throw new L2RpcProtocolError(method, "response is missing result");
    return env.result;
  }
}

/* ─────────────────────────── helpers ────────────────────────────── */

function parseBatchView(r: Record<string, unknown>, method: string): L2BatchView {
  return {
    chainId: requireU32Value(r.chainId, method, "chainId"),
    batchNumber: parseU64Wire(r.batchNumber, method, "batchNumber"),
    firstBlock: parseU64Wire(r.firstBlock, method, "firstBlock"),
    lastBlock: parseU64Wire(r.lastBlock, method, "lastBlock"),
    preStateRoot: requireString(r.preStateRoot, method, "preStateRoot"),
    postStateRoot: requireString(r.postStateRoot, method, "postStateRoot"),
    txRoot: requireString(r.txRoot, method, "txRoot"),
    receiptRoot: requireString(r.receiptRoot, method, "receiptRoot"),
    withdrawalRoot: requireString(r.withdrawalRoot, method, "withdrawalRoot"),
    l2ToL1MessageRoot: requireString(r.l2ToL1MessageRoot, method, "l2ToL1MessageRoot"),
    l2ToL2MessageRoot: requireString(r.l2ToL2MessageRoot, method, "l2ToL2MessageRoot"),
    daCommitment: requireString(r.daCommitment, method, "daCommitment"),
    publicInputHash: requireString(r.publicInputHash, method, "publicInputHash"),
    proofType: requireEnumValue(r.proofType, method, "proofType", ALLOWED_PROOF_TYPES) as ProofType,
    proof: requireHexString(r.proof, method, "proof"),
    encoded: requireHexString(r.encoded, method, "encoded"),
  };
}

function parseU64Wire(value: unknown, method: string, field: string): bigint {
  if (typeof value === "string" && /^(0|[1-9][0-9]*)$/.test(value)) {
    const parsed = BigInt(value);
    if (parsed <= 0xffff_ffff_ffff_ffffn) return parsed;
  }
  throw new L2RpcProtocolError(method, `field ${field} must be a canonical decimal u64 string`);
}

function requireU32Value(value: unknown, method: string, field: string): number {
  if (typeof value !== "number" || !Number.isInteger(value) || value < 0 || value > 0xffff_ffff)
    throw new L2RpcProtocolError(method, `field ${field} must be a u32`);
  return value;
}

function requireU8Value(value: unknown, method: string, field: string): number {
  if (typeof value !== "number" || !Number.isInteger(value) || value < 0 || value > 0xff)
    throw new L2RpcProtocolError(method, `field ${field} must be a byte`);
  return value;
}

function requireBoolean(value: unknown, method: string, field: string): boolean {
  if (typeof value !== "boolean")
    throw new L2RpcProtocolError(method, `field ${field} must be a boolean`);
  return value;
}

/**
 * Validate that a byte is a member of the given enum.
 *
 * The `.NET` reference SDK range-checks every enum dimension it decodes; the TypeScript SDK
 * used a bare `as` cast, which is a COMPILE-TIME-only assertion. A server that returned an
 * out-of-range byte would therefore produce a value that the SDK's own type declaration
 * claims is valid but no enum member describes — and the caller's `switch` would silently
 * fall through to `default`.
 */
function requireEnumValue<T extends number>(
  value: unknown,
  method: string,
  field: string,
  allowed: readonly T[],
): T {
  const byte = requireU8Value(value, method, field);
  if (!allowed.includes(byte as T))
    throw new L2RpcProtocolError(
      method,
      `field ${field} must be one of [${allowed.join(", ")}], got ${byte}`,
    );
  return byte as T;
}

const ALLOWED_SECURITY_LEVELS = [0, 1, 2, 3, 4] as const;
const ALLOWED_DA_MODES = [0, 1, 2, 3, 255] as const;
const ALLOWED_SEQUENCER_MODELS = [0, 1, 2] as const;
const ALLOWED_EXIT_MODELS = [0, 1, 2] as const;
const ALLOWED_BATCH_STATUSES = [0, 1, 2, 3, 4] as const;
const ALLOWED_PROOF_TYPES = [0, 1, 2, 3] as const;

function requireString(value: unknown, method: string, field: string): string {
  if (typeof value !== "string")
    throw new L2RpcProtocolError(method, `field ${field} must be a string`);
  return value;
}

function requireHexString(value: unknown, method: string, field: string): string {
  const text = requireString(value, method, field);
  const clean = /^0x/i.test(text) ? text.slice(2) : text;
  if (clean.length % 2 !== 0 || !/^[0-9a-fA-F]*$/.test(clean))
    throw new L2RpcProtocolError(method, `field ${field} must be an even-length hex string`);
  return text;
}

function hexToBytes(method: string, hex: string): Uint8Array {
  const clean = /^0x/i.test(hex) ? hex.slice(2) : hex;
  if (clean.length % 2 !== 0 || !/^[0-9a-fA-F]*$/.test(clean))
    throw new L2RpcProtocolError(method, `invalid hex string: ${hex}`);
  const bytes = new Uint8Array(clean.length / 2);
  for (let i = 0; i < bytes.length; i++) {
    bytes[i] = parseInt(clean.substring(i * 2, i * 2 + 2), 16);
  }
  return bytes;
}

function requireU32(value: number, name: string, allowZero: boolean): number {
  if (!Number.isInteger(value) || value < (allowZero ? 0 : 1) || value > 0xffff_ffff)
    throw new L2RpcProtocolError("<arg>", `${name} must be a ${allowZero ? "" : "non-zero "}u32`);
  return value;
}

/**
 * Encode a u64 argument for the wire.
 *
 * The runtime type check is load-bearing, not defensive noise: `bigint` is a TypeScript-only
 * annotation, and plain-JS consumers (the package's `main` entry is `dist/index.js`) can
 * pass a `number`. A `number` above 2^53 has already lost precision by the time it arrives,
 * and `value.toString()` would emit the truncated double — silently asking the server for
 * the wrong batch. The `bigint < 0n` / `bigint > MAX` comparisons do NOT catch this, because
 * JS defines mixed number/BigInt relational comparisons as well-defined rather than throwing.
 */
function u64Wire(value: unknown, name: string): string {
  if (typeof value !== "bigint")
    throw new L2RpcProtocolError(
      "<arg>",
      `${name} must be a bigint (a JS number above 2^53 loses precision; use BigInt)`,
    );
  if (value < 0n || value > 0xffff_ffff_ffff_ffffn)
    throw new L2RpcProtocolError("<arg>", `${name} must be a u64`);
  return value.toString();
}
