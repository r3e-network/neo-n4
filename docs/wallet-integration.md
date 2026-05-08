# Wallet integration patterns

This page describes how operators integrate Neo wallets (NEP-6 keystores,
Ledger devices, NeoLine browser extension, custom HSM tooling) with the
Neo Elastic Network's L1 transaction surface.

## Why this is operator-territory

Wallet integration spans hundreds of products with conflicting UX, security,
and key-storage models. Pinning the framework to any one product (NeoLine,
Neon Wallet, NEP-6 plain JSON, hardware Ledger, AWS KMS, etc.) forces every
downstream operator into that choice. Every L2 framework — including ZKsync,
Polygon zkEVM, Optimism — defers this layer for the same reason.

What `neo4` ships instead: **canonical paste-into-wallet hex** for every
L1 invocation operators need, plus **a delegate signing pattern** for
production hot-wallet flows.

## Pattern 1 — paste-into-wallet hex (manual signing)

Every CLI in this repo emits canonical Neo VM invocation scripts as hex.
The operator pastes the hex into the wallet of their choice; the wallet
asks for confirmation, signs, and broadcasts.

```bash
# Bridge: deposit GAS from L1 to L2.
neo-bridge deposit \
  --bridge   0xaaaa...aaaa \
  --asset    0xbbbb...bbbb \
  --target-chain 1099 \
  --recipient 0xcccc...cccc \
  --amount   100000000

# Output: 116-byte hex blob.
# Paste into NeoLine / Neon / your wallet's "send invocation" form.

# Bridge: finalize a withdrawal with a Merkle proof.
neo-bridge withdraw \
  --bridge ... --chain-id 1099 --batch 7 \
  --leaf 0x...... --leaf-index 3 \
  --asset 0x.... --recipient 0x.... --amount 50000000 \
  --proof-endpoint http://l2-rpc:30332

# Faucet: rate-limited drip with the same paste-flow.
neo-l2-faucet drip --bridge ... --asset ... --target-chain ... \
                   --recipient ... --amount 100000000

# Hub deploy: full L1 contract suite.
neo-hub-deploy plan --plan ./deploy-plan.json --output ./bundle.json
# Then operator signs each step in their wallet of choice.
```

This pattern is **safest** for cold-key deployments — the L1 keys never
touch the operator's CI / web app / shared host. The wallet's hardware
or KMS handles signing; the framework only ever sees the canonical
unsigned invocation hex.

## Pattern 2 — delegate signing (hot-wallet automation)

For automated flows (devnet runners, batch settlers, faucet servers)
that need to sign + broadcast without manual steps, the framework's
production code accepts a **signing delegate** the operator wires:

```csharp
using Neo.L2.Settlement.Rpc;

// RpcSettlementClient takes a SignAndSendAsync delegate. Operator
// implements it however they want (NEP-6 file load, KMS signer,
// Ledger HID, etc.). The framework never sees the private key.
var settlement = new RpcSettlementClient(
    rpc: new JsonRpcClient("http://l1-rpc:20332"),
    settlementManagerHash: UInt160.Parse("0x..."),
    signAndSend: async (contractHash, callData, ct) =>
    {
        // Build the Neo Transaction with the operator's signer + witness.
        var tx = BuildAndSignTransaction(contractHash, callData);
        // Broadcast via Neo RPC, return the tx hash.
        return await BroadcastViaJsonRpc(tx, ct);
    });
```

Same pattern for the other CLIs that need to sign — they all expose a
delegate hook so the operator wires their preferred signing path
(`AWS-KMS` / `Azure Key Vault` / `Ledger` / NEP-6 / etc.) without the
framework needing to know.

## Specific wallet products

### NeoLine (browser extension)

Drop the canonical hex into NeoLine's "Send" → "Smart Contract" form.
NeoLine handles witness-script construction and broadcast.

### Neon Wallet (desktop)

`File → Open invocation script → paste hex`. Same flow as NeoLine; the
desktop client handles the signing UX.

### NEP-6 (programmatic)

For automated signers using a NEP-6 keystore:

```csharp
using Neo.Wallets.NEP6;

var wallet = new NEP6Wallet("operator-keystore.json");
wallet.Unlock(operatorPassword);
var account = wallet.GetAccounts().First(a => a.IsDefault);

// Wire to RpcSettlementClient via the delegate above.
async ValueTask<UInt256> SignAndSend(UInt160 hash, byte[] script, CancellationToken ct) {
    var tx = BuildTransaction(account, hash, script);
    var ctx = new ContractParametersContext(snapshot, tx, network);
    if (account.Sign(ctx)) tx.Witnesses = ctx.GetWitnesses();
    var rawTx = tx.ToArray();
    return await rpc.CallAsync("sendrawtransaction", new JArray { Convert.ToBase64String(rawTx) }, ct);
}
```

### Ledger (hardware)

Ledger's Neo app supports the same `signTransaction` flow. The operator
wires their HID / WebHID transport, swaps it into the same delegate
shape, and the rest of the framework is unchanged.

### Custom HSM / KMS

For production deployments with KMS-backed keys (AWS-KMS, GCP-KMS,
Azure Key Vault, HashiCorp Vault), the integration is identical: the
delegate calls into the KMS's signing API + returns the broadcast tx hash.
No changes anywhere else.

## Why no built-in signer

A built-in signer would force every operator into one of these mutually
exclusive choices:

- A **specific keystore format** (NEP-6 vs PKCS#11 vs raw JSON vs custom)
- A **specific signing mechanism** (cold-key paste vs hardware vs KMS)
- A **specific wallet UX** (CLI prompt vs browser popup vs auto-sign)

The framework intentionally ships zero of these so operators are free
to wire whichever fits their compliance model. Every CLI emits
paste-into-wallet hex; every production-hot-path takes a signing
delegate.

## Reference

Each CLI's exact wallet-pastable output:

| CLI | Output | Wallet step |
|-----|--------|-------------|
| `neo-bridge deposit` | `SharedBridge.Deposit` invocation hex | Paste into wallet → sign |
| `neo-bridge withdraw` | `SharedBridge.FinalizeWithdrawalWithProof` invocation hex | Paste → sign |
| `neo-l2-faucet drip` | `SharedBridge.Deposit` (rate-limited) | Paste → sign |
| `neo-hub-deploy plan` | 13 sequential contract-deploy invocations | Sign each in order |
| `neo-stack register-chain` | `ChainRegistry.Register` invocation + 91-byte configBytes hex | Paste → sign |
