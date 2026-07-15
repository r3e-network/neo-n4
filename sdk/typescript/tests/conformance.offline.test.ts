import { createHash, createPublicKey, verify } from "node:crypto";
import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import {
  BatchStatus,
  L2RpcClient,
  L2RpcMismatchedChainIdError,
  L2RpcProtocolError,
  L2RpcServerError,
  SecurityLevel,
} from "../src/index.js";

interface RpcCase {
  name: string;
  method: string;
  params: unknown[];
  result: unknown;
}

interface ErrorCase {
  name: string;
  jsonrpc: string;
  idOffset?: number;
  idAsString?: boolean;
  result?: unknown;
  error?: { code: number; message: string };
  expected: "server" | "protocol";
}

const vectors = JSON.parse(
  readFileSync(new URL("../../conformance/vectors/v1.json", import.meta.url), "utf8"),
) as Record<string, any>;

function response(body: object): Response {
  return {
    ok: true,
    status: 200,
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as unknown as Response;
}

async function invokeCase(client: L2RpcClient, testCase: RpcCase): Promise<void> {
  switch (testCase.name) {
    case "batch-missing-max-u64":
      expect(await client.getBatch((1n << 64n) - 1n)).toBeNull();
      break;
    case "batch-complete-large-u64": {
      const batch = await client.getBatch(9_007_199_254_740_993n);
      expect(batch?.batchNumber).toBe(9_007_199_254_740_993n);
      expect(batch?.proof).toBe("AABBCC");
      break;
    }
    case "batch-status": {
      const status = await client.getBatchStatus(9_007_199_254_740_993n);
      expect(status.batchNumber).toBe(9_007_199_254_740_993n);
      expect(status.status).toBe(BatchStatus.Finalized);
      break;
    }
    case "latest-state-root":
      expect(await client.getLatestStateRoot()).toBe(testCase.result);
      break;
    case "state-root-max-u64":
      expect(await client.getStateRootAt((1n << 64n) - 1n)).toBe(testCase.result);
      break;
    case "withdrawal-proof":
      expect(await client.getWithdrawalProof(testCase.params[1] as string)).toEqual(
        new Uint8Array([0xca, 0xfe, 0xba, 0xbe]),
      );
      break;
    case "message-proof":
      expect(await client.getMessageProof(testCase.params[1] as string)).toEqual(
        new Uint8Array([0xde, 0xad, 0xbe, 0xef]),
      );
      break;
    case "deposit-status-max-u64": {
      const status = await client.getDepositStatus(1, (1n << 64n) - 1n);
      expect(status?.nonce).toBe((1n << 64n) - 1n);
      expect(status?.includedInBatch).toBe(9_007_199_254_740_993n);
      break;
    }
    case "canonical-asset":
      expect(await client.getCanonicalAsset(testCase.params[0] as string)).toBe(testCase.result);
      break;
    case "bridged-asset":
      expect(await client.getBridgedAsset(testCase.params[0] as string)).toBe(testCase.result);
      break;
    case "security-level":
      expect((await client.getSecurityLevel()).level).toBe(SecurityLevel.Validity);
      break;
    case "security-label":
      expect((await client.getSecurityLabel()).securityLevel).toBe(SecurityLevel.Validium);
      break;
    default:
      throw new Error(`unknown conformance case ${testCase.name}`);
  }
}

async function invokeResponseErrorCase(client: L2RpcClient, name: string): Promise<unknown> {
  switch (name) {
    case "mismatched-chain-id":
      return client.getSecurityLabel();
    case "invalid-withdrawal-proof-hex":
      return client.getWithdrawalProof(`0x${"4".repeat(64)}`);
    case "wrong-state-root-type":
      return client.getLatestStateRoot();
    case "numeric-u64":
    case "mismatched-deposit-source-chain":
    case "mismatched-deposit-nonce":
      return client.getDepositStatus(1, 42n);
    case "mismatched-batch-number":
      return client.getBatchStatus(7n);
    default:
      throw new Error(`unknown response-error conformance case ${name}`);
  }
}

describe("shared SDK conformance vectors", () => {
  it("uses canonical method shapes and lossless u64 serialization", async () => {
    for (const testCase of vectors.rpc.cases as RpcCase[]) {
      const client = new L2RpcClient({
        endpoint: "http://node.example:30332",
        chainId: vectors.rpc.chainId,
        fetch: async (_input: RequestInfo | URL, init?: RequestInit) => {
          const request = JSON.parse(init!.body as string) as Record<string, unknown>;
          expect(request.jsonrpc).toBe("2.0");
          expect(request.method).toBe(testCase.method);
          expect(request.params).toEqual(testCase.params);
          return response({jsonrpc: "2.0", id: request.id, result: testCase.result});
        },
      });

      await invokeCase(client, testCase);
    }
  });

  it("maps little-endian UInt256 bytes to the canonical RPC display", () => {
    const wire = Buffer.from(vectors.hash.wireLittleEndianHex, "hex");
    expect(`0x${Buffer.from(wire).reverse().toString("hex")}`).toBe(vectors.hash.rpcDisplay);
  });

  it("maps server, id, and JSON-RPC version failures canonically", async () => {
    for (const errorCase of vectors.rpc.errors as ErrorCase[]) {
      const client = new L2RpcClient({
        endpoint: "http://node.example:30332",
        chainId: vectors.rpc.chainId,
        fetch: async (_input: RequestInfo | URL, init?: RequestInit) => {
          const request = JSON.parse(init!.body as string) as {id: number};
          const numericId = request.id + (errorCase.idOffset ?? 0);
          const id = errorCase.idAsString ? String(numericId) : numericId;
          return response(errorCase.error
            ? {jsonrpc: errorCase.jsonrpc, id, error: errorCase.error}
            : {jsonrpc: errorCase.jsonrpc, id, result: errorCase.result});
        },
      });

      if (errorCase.expected === "server")
        await expect(client.getLatestStateRoot()).rejects.toBeInstanceOf(L2RpcServerError);
      else
        await expect(client.getLatestStateRoot()).rejects.toBeInstanceOf(L2RpcProtocolError);
    }
  });

  it("fails closed on mismatched chain, malformed result shape, and invalid hex", async () => {
    for (const errorCase of vectors.rpc.responseErrors as RpcCase[]) {
      const client = new L2RpcClient({
        endpoint: "http://node.example:30332",
        chainId: vectors.rpc.chainId,
        fetch: async (_input: RequestInfo | URL, init?: RequestInit) => {
          const request = JSON.parse(init!.body as string) as {id: number};
          return response({jsonrpc: "2.0", id: request.id, result: errorCase.result});
        },
      });
      const errorType = (errorCase as RpcCase & {expected: string}).expected === "chain"
        ? L2RpcMismatchedChainIdError
        : L2RpcProtocolError;
      await expect(invokeResponseErrorCase(client, errorCase.name)).rejects.toBeInstanceOf(errorType);
    }
  });

  it("binds the L1 reservation, L2 chain id, and Neo network-magic domain", () => {
    const domain = vectors.domain;
    const network = Buffer.alloc(4);
    network.writeUInt32LE(domain.networkMagic);
    expect(domain.l1ReservedChainId).toBe(0);
    expect(domain.l2ChainId).toBe(vectors.rpc.chainId);
    expect(domain.networkMagic).toBe(vectors.transaction.network);
    expect(network.toString("hex")).toBe(domain.networkMagicLittleEndianHex);
    expect(vectors.transaction.signDataHex).toMatch(new RegExp(`^${domain.networkMagicLittleEndianHex}`));
  });

  it("round-trips pagination cursors and u64 values without loss", () => {
    const roundTrip = JSON.parse(JSON.stringify(vectors.pagination)) as typeof vectors.pagination;
    const batchNumbers = roundTrip.pages.flatMap((page: any) =>
      page.items.map((item: any) => item.batchNumber));
    expect(batchNumbers).toEqual(vectors.pagination.expectedBatchNumbers);
    expect(roundTrip.pages[0].nextCursor).toBe("batch:9007199254740994");
    expect(roundTrip.pages[1].nextCursor).toBeNull();
  });

  it("round-trips a signed Neo N3 transaction and verifies its signature", () => {
    const vector = vectors.transaction;
    const raw = Buffer.from(vector.rawTransactionHex, "hex");
    const unsigned = Buffer.from(vector.unsignedTransactionHex, "hex");
    const digest = createHash("sha256").update(unsigned).digest();
    expect(raw.subarray(0, unsigned.length)).toEqual(unsigned);
    expect(`0x${Buffer.from(digest).reverse().toString("hex")}`).toBe(vector.txid);

    const network = Buffer.alloc(4);
    network.writeUInt32LE(vector.network);
    const signData = Buffer.concat([network, digest]);
    expect(signData.toString("hex")).toBe(vector.signDataHex);

    const invocationLength = raw[unsigned.length + 1];
    const invocationStart = unsigned.length + 2;
    const invocationEnd = invocationStart + invocationLength;
    expect(raw[unsigned.length]).toBe(1);
    expect(invocationLength).toBe(66);
    expect(raw.subarray(invocationStart, invocationStart + 2).toString("hex")).toBe("0c40");
    expect(raw.subarray(invocationStart + 2, invocationEnd).toString("hex")).toBe(vector.signatureHex);
    expect(raw[invocationEnd]).toBe(40);
    expect(raw.subarray(invocationEnd + 1).toString("hex")).toBe(vector.verificationScriptHex);

    const spkiPrefix = Buffer.from("3059301306072a8648ce3d020106082a8648ce3d030107034200", "hex");
    const publicKey = createPublicKey({
      key: Buffer.concat([spkiPrefix, Buffer.from(vector.publicKeyUncompressedHex, "hex")]),
      format: "der",
      type: "spki",
    });
    expect(verify(
      "sha256",
      signData,
      {key: publicKey, dsaEncoding: "ieee-p1363"},
      Buffer.from(vector.signatureHex, "hex"),
    )).toBe(true);
  });
});
