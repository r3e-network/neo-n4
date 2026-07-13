# Operator signer-command protocol

`neo-stack` signs L1 and L2 operator transactions through
`INeoTransactionSigner` (see `doc.md` §14.2). The built-in WIF path is useful
for an isolated devnet, but production operators often keep the key in a
wallet service, HSM, or KMS. This protocol lets the CLI call a reviewed local
adapter without loading a private key or provider SDK into the CLI process.

## CLI contract

Use the command signer instead of `--wif-env`:

```bash
neo-stack register-chain ... --broadcast \
  --rpc https://l1.example/rpc --expected-network 123456 \
  --signer-command /opt/neo/bin/kms-signer \
  --signer-account 0x0123456789abcdef0123456789abcdef01234567 \
  --signer-verification-script <hex> \
  --signer-placeholder-invocation-script <hex>
```

`--signer-command` is executed directly with `ProcessStartInfo.ArgumentList`;
the CLI never invokes a shell. It must name a reviewed local executable (or a
reviewed `.dll`, which the CLI starts through `dotnet`). Use a small wrapper
when a cloud-KMS client needs provider-specific flags or credentials.

The caller must supply all of the following:

| Option | Purpose |
| --- | --- |
| `--signer-command` | Local adapter executable. |
| `--signer-account` | Non-zero `UInt160` that pays the transaction fee. |
| `--signer-verification-script` | Hex verification script whose script hash must equal `--signer-account`. |
| `--signer-placeholder-invocation-script` | Hex witness invocation script with the final witness's exact serialized size, used only for network-fee estimation. |
| `--signer-timeout-seconds` | Optional adapter deadline, from 1 to 300 seconds (default: 60). |

For `deploy-bridge-adapter --side both`, use the same prefixed options for
each chain: `--l1-signer-command`, `--l1-signer-account`, and so on; likewise
for `--l2-*`. An explicitly supplied `--wif-env` and a signer command are
mutually exclusive. If neither signer is configured, the existing
`NEO_N4_OPERATOR_WIF` fallback remains the devnet-only path.

## Request and response

For every final signing request, `neo-stack` writes exactly one JSON object to
the adapter's standard input and closes it. The adapter must write exactly one
JSON object to standard output and reserve standard error for diagnostics.

```json
{
  "version": 1,
  "network": 123456,
  "account": "0x0123456789abcdef0123456789abcdef01234567",
  "scope": "CalledByEntry",
  "signData": "<base64 Neo IVerifiable.GetSignData(network) bytes>",
  "transaction": "<base64 serialized transaction with its fee-estimation witness>"
}
```

The adapter returns the final invocation script as base64:

```json
{
  "invocationScript": "<base64 witness invocation-script bytes>"
}
```

The CLI pins the verification script from its own command-line configuration;
an adapter cannot replace it. The final transaction is built from the same
canonical `signData` that Neo verifies. A standard secp256r1 signer signs the
provided bytes with Neo's SHA-256 signing convention and returns the 64-byte
`r || s` signature wrapped in the canonical Neo invocation script (not a DER
signature). Contract and multisig adapters may return their own valid
invocation script, provided it matches the configured verification script and
the transaction's fee-estimation shape.

## Failure behavior

The adapter is fail-closed. A missing option, account/script-hash mismatch,
invalid hex or JSON, empty response script, non-zero child exit, timeout, or
caller cancellation stops before `sendrawtransaction`. The CLI still performs
the normal `getversion` network check, `invokescript` preflight, exact
network-fee calculation, broadcast, and HALT confirmation after the signer
returns successfully.

Do not log `signData`, serialized transactions, provider tokens, or response
scripts in a shared shell history. The adapter process is an operator-owned
trust boundary: pin its path and deployment provenance alongside the KMS key
policy, then rehearse cancellation and failed-signature behavior on a devnet
before production use.
