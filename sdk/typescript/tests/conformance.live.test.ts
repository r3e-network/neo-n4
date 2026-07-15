import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { L2RpcClient, L2RpcServerError } from "../src/index.js";

const requiredVariables = [
  "NEO_SDK_LIVE",
  "NEO_N3_RPC_URL",
  "NEO_N4_RPC_URL",
  "NEO_N4_CHAIN_ID",
  "NEO_SDK_LIVE_FIXTURE",
] as const;
const missingVariables = requiredVariables.filter((name) => !process.env[name]?.trim());
const liveIt = missingVariables.length === 0 ? it : it.skip;

if (missingVariables.length > 0)
  console.warn(`SKIP live SDK conformance: missing ${missingVariables.join(", ")}`);

interface LiveConfiguration {
  n3: string;
  n4: string;
  chainId: number;
  n3Fixture: Record<string, any>;
  n4Fixture: Record<string, any>;
}

function configuration(): LiveConfiguration {
  if (process.env.NEO_SDK_LIVE !== "1")
    throw new Error("NEO_SDK_LIVE must equal 1");
  const chainId = Number(process.env.NEO_N4_CHAIN_ID);
  if (!Number.isInteger(chainId) || chainId <= 0 || chainId > 0xffff_ffff)
    throw new Error("NEO_N4_CHAIN_ID must be an unsigned non-zero integer");
  const fixture = JSON.parse(
    readFileSync(process.env.NEO_SDK_LIVE_FIXTURE!, "utf8"),
  ) as Record<string, any>;
  if (fixture.schema !== "neo-n4-sdk-live-fixture/v1")
    throw new Error("live fixture schema must be neo-n4-sdk-live-fixture/v1");
  if (fixture.n4?.chainId !== chainId)
    throw new Error("live fixture n4.chainId must match NEO_N4_CHAIN_ID");
  return {
    n3: process.env.NEO_N3_RPC_URL!,
    n4: process.env.NEO_N4_RPC_URL!,
    chainId,
    n3Fixture: fixture.n3,
    n4Fixture: fixture.n4,
  };
}

async function rawRpc(endpoint: string, method: string, params: unknown[]): Promise<unknown> {
  const response = await fetch(endpoint, {
    method: "POST",
    headers: {"Content-Type": "application/json", Accept: "application/json"},
    body: JSON.stringify({jsonrpc: "2.0", method, params, id: 1}),
  });
  expect(response.ok).toBe(true);
  const envelope = await response.json() as Record<string, unknown>;
  expect(envelope.jsonrpc).toBe("2.0");
  expect(envelope.id).toBe(1);
  expect(envelope.error ?? null).toBeNull();
  return envelope.result;
}

function isHash(value: unknown): boolean {
  return typeof value === "string" && /^0x[0-9a-fA-F]{64}$/.test(value);
}

async function assertBaseNode(endpoint: string, expected: Record<string, any>): Promise<void> {
  const version = await rawRpc(endpoint, "getversion", []) as Record<string, any>;
  expect(version.protocol?.network).toBe(expected.networkMagic);
  expect(Number(await rawRpc(endpoint, "getblockcount", []))).toBeGreaterThanOrEqual(
    expected.minimumBlockCount,
  );
  const genesis = await rawRpc(endpoint, "getblockhash", [0]);
  expect(isHash(genesis)).toBe(true);
  expect(genesis).toBe(expected.genesisHash);
}

function readU64(value: unknown): bigint {
  if (typeof value !== "string" || !/^(0|[1-9][0-9]*)$/.test(value))
    throw new Error("fixture u64 must be a canonical decimal string");
  const parsed = BigInt(value);
  if (parsed > 0xffff_ffff_ffff_ffffn) throw new Error("fixture u64 exceeds u64 range");
  return parsed;
}

async function assertTypedCase(client: L2RpcClient, testCase: Record<string, any>): Promise<void> {
  const params = testCase.params as unknown[];
  const expected = testCase.result as Record<string, any> | string;
  switch (testCase.name) {
    case "batch": {
      const batch = await client.getBatch(readU64(params[1]));
      expect(batch).not.toBeNull();
      expect({
        chainId: batch!.chainId,
        batchNumber: batch!.batchNumber.toString(),
        firstBlock: batch!.firstBlock.toString(),
        lastBlock: batch!.lastBlock.toString(),
        preStateRoot: batch!.preStateRoot,
        postStateRoot: batch!.postStateRoot,
        txRoot: batch!.txRoot,
        receiptRoot: batch!.receiptRoot,
        withdrawalRoot: batch!.withdrawalRoot,
        l2ToL1MessageRoot: batch!.l2ToL1MessageRoot,
        l2ToL2MessageRoot: batch!.l2ToL2MessageRoot,
        daCommitment: batch!.daCommitment,
        publicInputHash: batch!.publicInputHash,
        proofType: batch!.proofType,
        proof: batch!.proof,
        encoded: batch!.encoded,
      }).toEqual(expected);
      break;
    }
    case "batch-status": {
      const status = await client.getBatchStatus(readU64(params[1]));
      expect({
        chainId: status.chainId,
        batchNumber: status.batchNumber.toString(),
        status: status.status,
        statusName: status.statusName,
      }).toEqual(expected);
      break;
    }
    case "latest-state-root":
      expect(await client.getLatestStateRoot()).toBe(expected);
      break;
    case "historical-state-root":
      expect(await client.getStateRootAt(readU64(params[1]))).toBe(expected);
      break;
    case "withdrawal-proof":
      expect(Buffer.from((await client.getWithdrawalProof(params[1] as string))!).toString("hex").toUpperCase())
        .toBe(expected);
      break;
    case "message-proof":
      expect(Buffer.from((await client.getMessageProof(params[1] as string))!).toString("hex").toUpperCase())
        .toBe(expected);
      break;
    case "deposit-status": {
      const status = await client.getDepositStatus(params[0] as number, readU64(params[1]));
      expect(status).not.toBeNull();
      expect({
        sourceChainId: status!.sourceChainId,
        nonce: status!.nonce.toString(),
        consumedOnL2: status!.consumedOnL2,
        includedInBatch: status!.includedInBatch === null ? null : status!.includedInBatch.toString(),
      }).toEqual(expected);
      break;
    }
    case "canonical-asset":
      expect(await client.getCanonicalAsset(params[0] as string)).toBe(expected);
      break;
    case "bridged-asset":
      expect(await client.getBridgedAsset(params[0] as string)).toBe(expected);
      break;
    case "security-level": {
      const level = await client.getSecurityLevel();
      expect(level.chainId).toBe((expected as Record<string, any>).chainId);
      expect(level.level).toBe((expected as Record<string, any>).level);
      break;
    }
    case "security-label": {
      const label = await client.getSecurityLabel();
      expect(label.chainId).toBe((expected as Record<string, any>).chainId);
      expect(label.securityLevel).toBe((expected as Record<string, any>).securityLevel);
      expect(label.daMode).toBe((expected as Record<string, any>).daMode);
      expect(label.gatewayEnabled).toBe((expected as Record<string, any>).gatewayEnabled);
      expect(label.sequencer).toBe((expected as Record<string, any>).sequencer);
      expect(label.exit).toBe((expected as Record<string, any>).exit);
      break;
    }
    default:
      throw new Error(`unknown live conformance case ${testCase.name}`);
  }
}

describe("live SDK conformance", () => {
  liveIt("matches canonical Neo N3 network and genesis evidence", async () => {
    const current = configuration();
    await assertBaseNode(current.n3, current.n3Fixture);
  });

  liveIt("matches canonical Neo N4 network and genesis evidence", async () => {
    const current = configuration();
    await assertBaseNode(current.n4, current.n4Fixture);
  });

  liveIt("matches every typed N4 query and fails closed for the wrong chain", async () => {
    const current = configuration();
    const client = new L2RpcClient({endpoint: current.n4, chainId: current.chainId});
    for (const testCase of current.n4Fixture.cases as Record<string, any>[]) {
      expect(await rawRpc(current.n4, testCase.method, testCase.params)).toEqual(testCase.result);
      await assertTypedCase(client, testCase);
    }

    const wrongClient = new L2RpcClient({
      endpoint: current.n4,
      chainId: current.n4Fixture.wrongChainId,
    });
    await expect(wrongClient.getSecurityLabel()).rejects.toBeInstanceOf(L2RpcServerError);
  });
});
