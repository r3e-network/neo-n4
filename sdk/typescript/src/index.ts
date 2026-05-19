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
}

export enum SequencerModel {
  Centralized = 0,
  DbftCommittee = 1,
  PoSRotation = 2,
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
  Pending = 0,
  Challengeable = 1,
  Finalized = 2,
  Challenged = 3,
  Slashed = 4,
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
  constructor(method: string, public readonly code: number, message: string) {
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
    if (opts.chainId === 0) {
      throw new Error("chainId 0 is reserved for L1 — supply a non-zero L2 chainId");
    }
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
    const result = await this.call("getl2batch", [this.chainId, batchNumber.toString()]);
    if (result === null) return null;
    if (typeof result !== "object" || result === null)
      throw new L2RpcProtocolError("getl2batch", "expected object response");
    this.assertChainId(result as object, "getl2batch");
    return parseBatchView(result as Record<string, unknown>);
  }

  /** getl2batchstatus — pending / finalized / challenged / etc. */
  async getBatchStatus(batchNumber: bigint): Promise<BatchStatusResponse> {
    const result = await this.call("getl2batchstatus", [this.chainId, batchNumber.toString()]);
    if (typeof result !== "object" || result === null)
      throw new L2RpcProtocolError("getl2batchstatus", "expected object response");
    this.assertChainId(result, "getl2batchstatus");
    const r = result as Record<string, unknown>;
    return {
      chainId: Number(r.chainId),
      batchNumber: BigInt(r.batchNumber as number | string),
      status: Number(r.status) as BatchStatus,
      statusName: String(r.statusName ?? ""),
    };
  }

  /** getl2stateroot — latest sealed state root. */
  async getLatestStateRoot(): Promise<string> {
    const result = await this.call("getl2stateroot", [this.chainId]);
    if (typeof result !== "string")
      throw new L2RpcProtocolError("getl2stateroot", "expected string");
    return result;
  }

  /** getl2stateroot at a specific batch height. */
  async getStateRootAt(batchNumber: bigint): Promise<string> {
    const result = await this.call("getl2stateroot", [this.chainId, batchNumber.toString()]);
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
    return hexToBytes(result);
  }

  /** getl2messageproof — Merkle proof bytes for a cross-chain message; null if unknown. */
  async getMessageProof(messageHash: string): Promise<Uint8Array | null> {
    const result = await this.call("getl2messageproof", [this.chainId, messageHash]);
    if (result === null) return null;
    if (typeof result !== "string")
      throw new L2RpcProtocolError("getl2messageproof", "expected hex string");
    return hexToBytes(result);
  }

  /** getl1depositstatus — has an L1 deposit (sourceChain, nonce) been consumed? null if untracked. */
  async getDepositStatus(sourceChainId: number, nonce: bigint): Promise<DepositStatusResponse | null> {
    const result = await this.call("getl1depositstatus", [sourceChainId, nonce.toString()]);
    if (result === null) return null;
    if (typeof result !== "object")
      throw new L2RpcProtocolError("getl1depositstatus", "expected object response");
    const r = result as Record<string, unknown>;
    // Cross-check the requested sourceChainId matches what the server returned — a
    // misbehaving server returning another chain's deposit would otherwise sail through.
    const respChain = Number(r.sourceChainId);
    if (respChain !== sourceChainId)
      throw new L2RpcMismatchedChainIdError("getl1depositstatus", sourceChainId, respChain);
    return {
      sourceChainId: respChain,
      nonce: BigInt(r.nonce as number | string),
      consumedOnL2: Boolean(r.consumedOnL2),
      includedInBatch: r.includedInBatch === null || r.includedInBatch === undefined
        ? null
        : BigInt(r.includedInBatch as number | string),
    };
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
    const result = await this.call("getbridgedasset", [l1Asset]);
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
      chainId: Number(r.chainId),
      level: Number(r.level) as SecurityLevel,
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
      chainId: Number(r.chainId),
      securityLevel: Number(r.securityLevel) as SecurityLevel,
      daMode: Number(r.daMode) as DAMode,
      gatewayEnabled: Boolean(r.gatewayEnabled),
      sequencer: Number(r.sequencer) as SequencerModel,
      exit: Number(r.exit) as ExitModel,
    };
  }

  private assertChainId(obj: object, method: string): void {
    const r = obj as Record<string, unknown>;
    if (r.chainId !== undefined && Number(r.chainId) !== this.chainId) {
      throw new L2RpcMismatchedChainIdError(method, this.chainId, Number(r.chainId));
    }
  }

  private async call(method: string, params: unknown[]): Promise<unknown> {
    const id = ++this.nextId;
    const body = JSON.stringify({ jsonrpc: "2.0", method, params, id });

    const controller = new AbortController();
    const timeoutHandle = setTimeout(() => controller.abort(), this.timeoutMs);

    let response: Response;
    try {
      response = await this.fetchImpl(this.endpoint.toString(), {
        method: "POST",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body,
        signal: controller.signal,
      });
    } catch (e) {
      const err = e as Error;
      if (err.name === "AbortError") {
        throw new L2RpcTransportError(method, `timeout after ${this.timeoutMs}ms`);
      }
      throw new L2RpcTransportError(method, `fetch failed: ${err.message}`);
    } finally {
      clearTimeout(timeoutHandle);
    }

    if (!response.ok) {
      const text = await response.text();
      const snippet = text.length <= 200 ? text : text.slice(0, 200);
      throw new L2RpcTransportError(method, `http ${response.status}: ${snippet}`);
    }

    let parsed: unknown;
    try {
      parsed = await response.json();
    } catch (e) {
      throw new L2RpcProtocolError(method, `parse error: ${(e as Error).message}`);
    }

    if (typeof parsed !== "object" || parsed === null) {
      throw new L2RpcProtocolError(method, "non-object response");
    }
    const env = parsed as Record<string, unknown>;
    const responseId = typeof env.id === "number" ? env.id : Number(env.id);
    if (responseId !== id) {
      throw new L2RpcProtocolError(method, `response id ${responseId} does not match request id ${id}`);
    }
    if (env.error !== undefined && env.error !== null) {
      const err = env.error as Record<string, unknown>;
      const code = typeof err.code === "number" ? err.code : -32603;
      const msg = typeof err.message === "string" ? err.message : "rpc error";
      throw new L2RpcServerError(method, code, msg);
    }
    return env.result ?? null;
  }
}

/* ─────────────────────────── helpers ────────────────────────────── */

function parseBatchView(r: Record<string, unknown>): L2BatchView {
  return {
    chainId: Number(r.chainId),
    batchNumber: BigInt(r.batchNumber as number | string),
    firstBlock: BigInt(r.firstBlock as number | string),
    lastBlock: BigInt(r.lastBlock as number | string),
    preStateRoot: String(r.preStateRoot),
    postStateRoot: String(r.postStateRoot),
    txRoot: String(r.txRoot),
    receiptRoot: String(r.receiptRoot),
    withdrawalRoot: String(r.withdrawalRoot),
    l2ToL1MessageRoot: String(r.l2ToL1MessageRoot),
    l2ToL2MessageRoot: String(r.l2ToL2MessageRoot),
    daCommitment: String(r.daCommitment),
    publicInputHash: String(r.publicInputHash),
    proofType: Number(r.proofType) as ProofType,
    proof: String(r.proof),
    encoded: String(r.encoded),
  };
}

function hexToBytes(hex: string): Uint8Array {
  const clean = hex.startsWith("0x") ? hex.slice(2) : hex;
  if (clean.length % 2 !== 0)
    throw new Error(`invalid hex string length: ${clean.length}`);
  const bytes = new Uint8Array(clean.length / 2);
  for (let i = 0; i < bytes.length; i++) {
    bytes[i] = parseInt(clean.substring(i * 2, i * 2 + 2), 16);
  }
  return bytes;
}
