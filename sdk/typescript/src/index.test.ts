import { describe, it, expect } from "vitest";
import {
  L2RpcClient,
  L2RpcServerError,
  L2RpcTransportError,
  L2RpcProtocolError,
  L2RpcMismatchedChainIdError,
  SecurityLevel,
  DAMode,
  SequencerModel,
  ExitModel,
} from "./index.js";

/**
 * Stub fetch: captures requests + returns canned JSON-RPC responses.
 * Mirrors the .NET StubHttpHandler tests in tests/Neo.L2.Sdk.UnitTests.
 */
function stubFetch(handler: (method: string, params: unknown[]) => unknown): typeof fetch {
  return async (input: RequestInfo | URL, init?: RequestInit) => {
    const body = JSON.parse(init!.body as string) as { method: string; params: unknown[]; id: number };
    const result = handler(body.method, body.params);
    return {
      ok: true,
      status: 200,
      json: async () => ({ jsonrpc: "2.0", id: body.id, result }),
      text: async () => "",
    } as unknown as Response;
  };
}

function rejectingFetch(status: number, body: string): typeof fetch {
  return async () => {
    return {
      ok: false,
      status,
      statusText: "Bad Gateway",
      text: async () => body,
      json: async () => ({}),
    } as unknown as Response;
  };
}

describe("L2RpcClient", () => {
  const ENDPOINT = "http://node.example:30332";
  const CHAIN_ID = 1099;

  describe("construction", () => {
    it("rejects non-http endpoint", () => {
      expect(() => new L2RpcClient({ endpoint: "ftp://example.com", chainId: 1 })).toThrow();
    });

    it("rejects invalid URL", () => {
      expect(() => new L2RpcClient({ endpoint: "not-a-url", chainId: 1 })).toThrow();
    });

    it("rejects chainId 0 (reserved for L1)", () => {
      expect(() => new L2RpcClient({ endpoint: ENDPOINT, chainId: 0 })).toThrow();
    });
  });

  describe("getLatestStateRoot", () => {
    it("returns the string result", async () => {
      const expected = "0x" + "a".repeat(64);
      const client = new L2RpcClient({
        endpoint: ENDPOINT,
        chainId: CHAIN_ID,
        fetch: stubFetch((method, params) => {
          expect(method).toBe("getl2stateroot");
          expect(params).toEqual([CHAIN_ID]);
          return expected;
        }),
      });
      expect(await client.getLatestStateRoot()).toBe(expected);
    });
  });

  describe("getStateRootAt", () => {
    it("includes the batch number in params as a string (u64 precision-safe)", async () => {
      const expected = "0x" + "b".repeat(64);
      const client = new L2RpcClient({
        endpoint: ENDPOINT,
        chainId: CHAIN_ID,
        fetch: stubFetch((method, params) => {
          expect(method).toBe("getl2stateroot");
          // Wire-format: the SDK serializes the u64 batch number as a JSON string so
          // that values above 2^53 are not silently truncated by JS Number. Server's
          // L2RpcMethods.ReadULong accepts JString via ulong.Parse.
          expect(params).toEqual([CHAIN_ID, "42"]);
          return expected;
        }),
      });
      expect(await client.getStateRootAt(42n)).toBe(expected);
    });

    it("preserves precision for batch numbers above 2^53", async () => {
      const expected = "0x" + "c".repeat(64);
      const big = (1n << 60n) + 17n; // > 2^53; Number(big) would silently truncate
      const client = new L2RpcClient({
        endpoint: ENDPOINT,
        chainId: CHAIN_ID,
        fetch: stubFetch((method, params) => {
          expect(method).toBe("getl2stateroot");
          expect(params).toEqual([CHAIN_ID, big.toString()]);
          return expected;
        }),
      });
      expect(await client.getStateRootAt(big)).toBe(expected);
    });
  });

  describe("getWithdrawalProof", () => {
    it("decodes hex bytes", async () => {
      const client = new L2RpcClient({
        endpoint: ENDPOINT,
        chainId: CHAIN_ID,
        fetch: stubFetch(() => "CAFEBABE"),
      });
      const got = await client.getWithdrawalProof("0x" + "1".repeat(64));
      expect(got).toEqual(new Uint8Array([0xca, 0xfe, 0xba, 0xbe]));
    });

    it("returns null for unknown leaf", async () => {
      const client = new L2RpcClient({
        endpoint: ENDPOINT,
        chainId: CHAIN_ID,
        fetch: stubFetch(() => null),
      });
      expect(await client.getWithdrawalProof("0x" + "1".repeat(64))).toBeNull();
    });
  });

  describe("getDepositStatus", () => {
    it("decodes nullable includedInBatch", async () => {
      const client = new L2RpcClient({
        endpoint: ENDPOINT,
        chainId: CHAIN_ID,
        fetch: stubFetch(() => ({
          sourceChainId: 1,
          nonce: "42",
          consumedOnL2: false,
          includedInBatch: null,
        })),
      });
      const got = await client.getDepositStatus(1, 42n);
      expect(got).toEqual({
        sourceChainId: 1,
        nonce: 42n,
        consumedOnL2: false,
        includedInBatch: null,
      });
    });

    it("returns null when server reports untracked", async () => {
      const client = new L2RpcClient({
        endpoint: ENDPOINT,
        chainId: CHAIN_ID,
        fetch: stubFetch(() => null),
      });
      expect(await client.getDepositStatus(1, 42n)).toBeNull();
    });
  });

  describe("getSecurityLabel", () => {
    it("decodes all five §16.2 dimensions", async () => {
      const client = new L2RpcClient({
        endpoint: ENDPOINT,
        chainId: CHAIN_ID,
        fetch: stubFetch(() => ({
          chainId: CHAIN_ID,
          securityLevel: SecurityLevel.Validium,
          daMode: DAMode.NeoFS,
          gatewayEnabled: true,
          sequencer: SequencerModel.DbftCommittee,
          exit: ExitModel.Delayed,
        })),
      });
      const got = await client.getSecurityLabel();
      expect(got.securityLevel).toBe(SecurityLevel.Validium);
      expect(got.daMode).toBe(DAMode.NeoFS);
      expect(got.gatewayEnabled).toBe(true);
      expect(got.sequencer).toBe(SequencerModel.DbftCommittee);
      expect(got.exit).toBe(ExitModel.Delayed);
    });
  });

  describe("error taxonomy", () => {
    it("server-side JSON-RPC error → L2RpcServerError with code", async () => {
      const client = new L2RpcClient({
        endpoint: ENDPOINT,
        chainId: CHAIN_ID,
        fetch: async (_input: RequestInfo | URL, init?: RequestInit) => {
          const body = JSON.parse(init!.body as string) as { id: number };
          return {
            ok: true,
            status: 200,
            json: async () => ({ jsonrpc: "2.0", id: body.id, error: { code: -32000, message: "node not synced" } }),
            text: async () => "",
          } as unknown as Response;
        },
      });
      try {
        await client.getLatestStateRoot();
        expect.fail("should have thrown");
      } catch (e) {
        expect(e).toBeInstanceOf(L2RpcServerError);
        expect((e as L2RpcServerError).code).toBe(-32000);
      }
    });

    it("HTTP non-2xx → L2RpcTransportError", async () => {
      const client = new L2RpcClient({
        endpoint: ENDPOINT,
        chainId: CHAIN_ID,
        fetch: rejectingFetch(502, "upstream unavailable"),
      });
      await expect(client.getLatestStateRoot()).rejects.toThrow(L2RpcTransportError);
    });

    it("malformed JSON → L2RpcProtocolError", async () => {
      const client = new L2RpcClient({
        endpoint: ENDPOINT,
        chainId: CHAIN_ID,
        fetch: async () => ({
          ok: true,
          status: 200,
          json: async () => { throw new Error("unexpected token"); },
          text: async () => "{not json",
        } as unknown as Response),
      });
      await expect(client.getLatestStateRoot()).rejects.toThrow(L2RpcProtocolError);
    });

    it("mismatched chainId → L2RpcMismatchedChainIdError", async () => {
      const client = new L2RpcClient({
        endpoint: ENDPOINT,
        chainId: CHAIN_ID,
        fetch: stubFetch(() => ({
          chainId: 9999,  // wrong!
          level: SecurityLevel.Optimistic,
        })),
      });
      await expect(client.getSecurityLevel()).rejects.toThrow(L2RpcMismatchedChainIdError);
    });

    it("response id mismatch → L2RpcProtocolError", async () => {
      const client = new L2RpcClient({
        endpoint: ENDPOINT,
        chainId: CHAIN_ID,
        fetch: async () => ({
          ok: true,
          status: 200,
          json: async () => ({ jsonrpc: "2.0", id: 99999, result: "0x" + "a".repeat(64) }),
          text: async () => "",
        } as unknown as Response),
      });
      await expect(client.getLatestStateRoot()).rejects.toThrow(L2RpcProtocolError);
    });
  });
});
