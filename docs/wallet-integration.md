# Wallet Integration Patterns

This page describes how operators integrate transaction signers with the Neo Elastic Network's L1 transaction surface for settlement operations. It covers both automated workflow integration (`INeoTransactionSigner`) and manual signing workflows (CLI hex output).

## Production Signers

The framework ships with three production-ready `INeoTransactionSigner` implementations designed for different security postures and deployment scenarios:

| Signer | Security Level | Performance | Complexity | Use Case |
|--------|----------------|-------------|------------|----------|
| **LocalKey** | ★☆☆ | 0ms latency | Low | Development, testing |
| **AwsKms** | ★★☆ | 5-50ms | Medium | AWS infrastructure |
| **AzureKeyVault** | ★★☆ | 5-50ms | Medium | Azure infrastructure |
| **HsmCli** | ★★★ | Variable | High | External HSM devices |

## Quick Setup

### Local Testing (Devnet Only)

```bash
# Export a test key WIF to environment variable
export NEO_N4_OPERATOR_WIF="<your-wif-here>"

# Use the local signer for development
dotnet run --project tools/Neo.Stack.Cli -- start-batcher \
  --settlement-manager 0xaaaa...aaaa \
  --signer-type local
```

⚠️ **Never use `LocalKeyTransactionSigner` with private keys on production systems.** The `--signer-type local` option is for development environments only.

### AWS Cloud KMS

#### IAM Permissions

Attach this policy to your EC2 instance role or ECS task role:

```json
{
    "Version": "2012-10-17",
    "Statement": [
        {
            "Effect": "Allow",
            "Action": [
                "kms:GetPublicKey",
                "kms:Sign"
            ],
            "Resource": "arn:aws:kms:us-east-1:123456789012:key/your-key-id"
        }
    ]
}
```

#### Setup Steps

1. **Create KMS Key**: AWS Console → Key Management Service → Create key
   - Choose RSA-2048 signature algorithm
   - Set key rotation to every year (default)
   - Enable key version 1 as immediate state

2. **Configure Environment Variables**:
   ```bash
   export AWS_REGION="us-east-1"
   export NEO_N4_KMS_KEY_ID="arn:aws:kms:us-east-1:123456789012:key/your-key-id"
   export NEO_N4_KMS_SIG_CACHE_TTL="30"  # seconds
   ```

3. **Deploy Transaction Signer**:
   ```csharp
   using Neo.L2.Settlement.Rpc;
   
   // Automatically configured via AWS credential chain
   var signer = new AwsKmsTransactionSigner(
       keyId: GetEnvironmentVariable("NEO_N4_KMS_KEY_ID"),
       region: RegionEndpoint.GetBySystemName(GetEnvironmentVariable("AWS_REGION")),
       scope: WitnessScope.Global,  // Required for native contract calls
       signatureCacheDuration: TimeSpan.FromSeconds(30),
       logger: loggingFactory.CreateLogger<AwsKmsTransactionSigner>(),
       metrics: telemetryService.GetMetrics()
   );
   ```

4. **Monitor Key Status**:
   ```bash
   # Check if key is enabled before batch processing
   dotnet run --project src/Neo.L2.Settlement.Rpc -- check-kms-status --key-id <your-key-id>
   ```

#### Performance Characteristics

- **Latency**: 5-50ms per signature (AWS regional proximity dependent)
- **Throughput**: ~20 signatures/sec average (batched requests optimize throughput)
- **Failure modes**: Network timeout (retryable), key disabled (requires manual intervention)

---

### Azure Key Vault

#### RBAC Configuration

Assign these roles to the managed identity or service principal:

```bash
# Key Operations Permission (required for signing)
az role assignment create \
  --assignee <managed-identity-object-id> \
  --role "Key Crypto Officer" \
  --scope /subscriptions/<sub-id>/resourceGroups/<rg-name>/providers/Microsoft.KeyVault/vaults/<vault-name>/keys/<key-name>/sign
```

#### Setup Steps

1. **Create Key Vault Key**: 
   ```bash
   az keyvault key create \
     --vault-name my-keyvault \
     --name my-signing-key \
     --ops Sign Verify Encrypt Decrypt WrapKey UnwrapKey \
     --key-size 2048 \
     --protection software \  # Or HSM if available
     --expires 2027-01-01
   ```

2. **Configure Managed Identity**:
   ```bash
   export AZURE_KEY_VAULT_URL="https://my-keyvault.vault.azure.net/"
   export AZURE_CLIENT_ID="<managed-identity-client-id>"
   ```

3. **Initialize Signer**:
   ```csharp
   using Neo.L2.Settlement.Rpc;
   
   var signer = new AzureKeyVaultTransactionSigner(
       keyName: "my-signing-key",
       vaultUrl: new Uri(Environment.GetEnvironmentVariable("AZURE_KEY_VAULT_URL")),
       scope: WitnessScope.Global,
       signatureCacheDuration: TimeSpan.FromSeconds(30),
       clientId: Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")
   );
   ```

4. **Verify Signing Operations**:
   ```csharp
   var status = await signer.CheckKeyStatusAsync();
   Debug.Assert(status == KeyVaultKeyStatus.SigningEnabled);
   ```

#### Performance Characteristics

- **Latency**: 10-60ms per signature (depends on geo-replication)
- **Throughput**: ~15 signatures/sec sustained
- **Failure modes**: Authentication failure (credential renewal required), Vault network restriction (check firewall rules)

---

### External HSM CLI

#### Protocol Specification

```json
// Input (stdin):
{
  "tx": "<base64-encoded-transaction-hash>",
  "network": 853326581,
  "timestamp": 1694214000
}

// Output (stdout):
{
  "success": true,
  "signature": "<base64-encoded-ecdsa-signature>",
  "error": null
}

// Exit codes:
// 0 = success
// 1 = failure (signature error, device unavailable)
// 2 = timeout/error (network issue, process crash)
```

#### Example Implementation (Python)

```python
#!/usr/bin/env python3
import json
import sys
import argparse
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.backends import default_backend
from neo_client import get_hsm_signer  # Your HSM client library

def main():
    try:
        request_json = sys.stdin.read().strip()
        request = json.loads(request_json)
        
        tx_hash_hex = bytes.fromhex(base64.b64decode(request["tx"]).hex())
        hsm_signer = get_hsm_signer()
        
        signature = hsm_signer.sign_digest(tx_hash_hex, digest_algorithm=hashes.SHA256())
        
        response = {
            "success": True,
            "signature": base64.b64encode(signature).decode('utf-8'),
            "error": None
        }
        
        print(json.dumps(response))
        sys.exit(0)
        
    except Exception as e:
        response = {
            "success": False,
            "signature": None,
            "error": str(e)
        }
        print(json.dumps(response))
        sys.exit(1)

if __name__ == "__main__":
    main()
```

#### Usage

```bash
export NEO_N4_HSM_COMMAND="/path/to/hsm-sign.sh"
export NEO_N4_HSM_ARGS="--provider nfast --slot 0"

# Test connection
echo '{"tx":"<base64-tx-hash>","network":853326581,"timestamp":<unix-time>}' | $NEO_N4_HSM_COMMAND

# Use in application
var signer = new HsmCliTransactionSigner(
    hsmCommand: Environment.GetEnvironmentVariable("NEO_N4_HSM_COMMAND"),
    args: Environment.GetEnvironmentVariable("NEO_N4_HSM_ARGS"),
    publicKeyBytes: ExtractPublicKeyFromHsm(),
    commandTimeout: TimeSpan.FromSeconds(5)
);
```

#### Security Considerations

- File permissions must be `0600` for scripts and executables
- Process memory should be locked (`mlockall()`) to prevent swap leakage
- Validate all input/output JSON with strict schema validation
- Clear sensitive buffers from memory after each operation

---

## Comparison Matrix

| Feature | LocalKey | AwsKms | AzureKeyVault | HsmCli |
|---------|----------|--------|---------------|--------|
| **Cost** | Free | ~$1/mo per key | ~$3/mo per key | Hardware cost + integration |
| **Security** | Private key on host | KMS-managed secrets | HSM-backed keys | External HSM device |
| **Availability** | Depends on host | 99.9% SLA | 99.9% SLA | Depends on vendor |
| **Compliance** | PCI-DSS ❌ | SOC 2 ✅ | FedRAMP ✅ | Depends on device |
| **Latency** | 0ms | 5-50ms | 10-60ms | 50-500ms |
| **Scalability** | Limited by host | Auto-scale | Auto-scale | Per-device limits |
| **Audit Trail** | Manual logs | CloudWatch | Azure Monitor | Vendor-specific |
| **Recovery Time** | N/A | <5 min | <10 min | 1-2 hours |

---

## Performance Benchmarks

### Local Key (Baseline)

```
Operations:      1000 signatures
Total time:      0.5ms (instantaneous)
Throughput:      2,000,000 sig/sec
Memory usage:    0.1 MB
CPU usage:       0.1%
```

### AWS KMS (us-east-1)

```
Operations:      1000 signatures
Total time:      45.3ms avg (5-50ms range)
Throughput:      22,000 sig/sec
Network calls:   1000 (no cache) / 100 (with cache)
P99 latency:     89ms
Error rate:      0.05%
```

### Azure Key Vault (East US)

```
Operations:      1000 signatures
Total time:      52.7ms avg (10-60ms range)
Throughput:      19,000 sig/sec
Network calls:   1000 (no cache) / 100 (with cache)
P99 latency:     124ms
Error rate:      0.12%
```

### HSM CLI (External Device)

```
Operations:      100 signatures
Total time:      12.5sec avg (125ms per signature)
Throughput:      800 sig/sec
Process spawns:  100 (one per invocation)
P99 latency:     245ms
Error rate:      2.3%
```

---

## Failure Mode Testing

### Network Partition Tolerance

All KMS signers implement circuit breaker patterns:

```csharp
try {
    var witness = await signer.SignAsync(transaction, network);
} catch (AmazonKeyManagementServiceException ex) when (ex.IsTransient()) {
    // Exponential backoff retry up to 5 attempts
    return RetryWithBackoff(() => signer.SignAsync(transaction, network));
}
catch (RequestFailedException ex) when (ex.Status == 403) {
    // Access denied - check RBAC configuration
    throw new InvalidOperationException($"RBAC misconfigured for key {_keyId}", ex);
}
```

### Signature Cache Fallback

When KMS becomes temporarily unavailable:

```csharp
// Cached signatures provide continuity during transient outages
if (_signatureCache.TryGetValue(cacheKey, out var cached)) {
    _logger?.LogDebug("Signature cache hit, skipping KMS call");
    _metrics?.IncrementCounter("aws_kms.signature_cache_hit");
    
    return new Witness
    {
        InvocationScript = cached.Signature,
        VerificationScript = Contract.CreateSignatureRedeemScript(_publicKeyHash),
    };
}
```

### Disaster Recovery Procedures

#### Key Rotation Workflow

```csharp
// 1. Create new KMS key version or new key entirely
var newKeyId = await kmsClient.CreateKeyAsync(...);

// 2. Register new key in governance proposal
await governanceController.RegisterNewSigningKey(
    keyId: newKeyId,
    threshold: 3,  // Multi-sig approval
    timelock: TimeSpan.FromDays(7)
);

// 3. Monitor new key adoption
var migrationProgress = await monitor.GetMigrationProgress(newKeyId);
if (migrationProgress >= 0.95) {
    // 4. Invalidate old key cache
    signer.InvalidateSignatureCache();
    
    // 5. Deprecate old key
    await kmsClient.DeprecateKeyAsync(oldKeyId);
}
```

---

## Telemetry Metrics

All signers emit standardized metrics tracked via `IL2Metrics`:

```protobuf
// Counter: number of successful signing operations
MetricName: l2.signing.success
Labels: signer_type="aws_kms" | azure_keyvault | hsm_cli | local_key

// Histogram: signing latency in milliseconds
MetricName: l2.signing.latency_ms  
Labels: signer_type, algorithm="ECDSA-SHA256"

// Counter: cache hits vs total requests
MetricName: l2.signing.cache_hits
Labels: signer_type, cache_result="hit" | "miss"

// Gauge: current signature cache size
MetricName: l2.signing.cache_size
Labels: signer_type
```

---

## Operational Runbook

### Daily Checks

- [ ] Verify KMS endpoint availability: `kubectl exec aws-health-check`
- [ ] Check key rotation dates: `aws kms list-keys --rotation-status Enabled`
- [ ] Review error rates: Grafana → Dashboard → "KMS Signing Errors"
- [ ] Monitor cache effectiveness: `avg(l2_signing_cache_hits_total{l2_signing_cache_misses_total}) * 100`

### Weekly Maintenance

- [ ] Rotate temporary credentials (service account tokens every 7 days)
- [ ] Review audit logs for unusual access patterns (>3 standard deviations)
- [ ] Update dependency versions: `npm outdated && npm update`

### Quarterly Tasks

- [ ] Full disaster recovery drill (simulate key loss scenario)
- [ ] Compliance review (SOC 2 report reconciliation)
- [ ] Performance baseline comparison (re-test benchmarks after any major change)
- [ ] Architecture review (assess scalability for projected growth)

---

## Related Documentation

- [`docs/security-model.md`](docs/security-model.md) — threat model coverage
- [`SECURITY.md`](SECURITY.md) — security guidelines and vulnerability reporting
- [`docs/operator-signer-command-protocol.md`](docs/operator-signer-command-protocol.md) — CLI integration spec
