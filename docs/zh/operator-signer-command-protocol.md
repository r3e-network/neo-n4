# Operator signer-command 协议

`neo-stack` 通过 `INeoTransactionSigner` 为 L1 与 L2 运维交易签名（见 `doc.md`
§14.2）。内置 WIF 路径只适合隔离 devnet；生产环境通常把密钥保存在钱包服务、
HSM 或 KMS 中。本协议允许 CLI 调用经审计的本地适配器，CLI 进程无需加载私钥或
云厂商 SDK。

## CLI 合约

生产环境使用 command signer，不使用 `--wif-env`：

```bash
neo-stack register-chain ... --broadcast \
  --rpc https://l1.example/rpc --expected-network 123456 \
  --signer-command /opt/neo/bin/kms-signer \
  --signer-account 0x0123456789abcdef0123456789abcdef01234567 \
  --signer-verification-script <hex> \
  --signer-placeholder-invocation-script <hex>
```

`--signer-command` 由 `ProcessStartInfo.ArgumentList` 直接执行，CLI 不会启动 shell。
该参数必须指向经审计的本地可执行文件；也可指向经审计的 `.dll`，此时 CLI 通过
`dotnet` 启动。云 KMS 客户端需要厂商参数或凭据时，应使用最小职责的本地包装器。

调用者必须提供以下参数：

| 参数 | 用途 |
| --- | --- |
| `--signer-command` | 本地签名适配器可执行文件。 |
| `--signer-account` | 支付交易费用的非零 `UInt160`。 |
| `--signer-verification-script` | 十六进制 verification script；其 script hash 必须等于 `--signer-account`。 |
| `--signer-placeholder-invocation-script` | 只用于网络费估算的十六进制占位 invocation script；序列化长度必须与最终 witness 完全一致。 |
| `--signer-timeout-seconds` | 可选适配器超时，范围 1 至 300 秒，默认 60 秒。 |

`deploy-bridge-adapter --side both` 对两条链使用同样的前缀参数，例如
`--l1-signer-command`、`--l1-signer-account` 以及对应的 `--l2-*` 参数。显式
`--wif-env` 与 signer command 互斥。两者都未配置时，`NEO_N4_OPERATOR_WIF`
回退路径仅用于 devnet。

## 请求与响应

每次最终签名时，`neo-stack` 向适配器标准输入写入一个 JSON 对象并关闭输入。
适配器必须向标准输出写入一个 JSON 对象，诊断信息只能写到标准错误。

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

适配器以 base64 返回最终 invocation script：

```json
{
  "invocationScript": "<base64 witness invocation-script bytes>"
}
```

verification script 由 CLI 命令行配置固定，适配器不能替换。最终交易使用 Neo
验证的同一份 canonical `signData`。标准 secp256r1 适配器按 Neo 的 SHA-256
签名约定签署该字节串，返回由 64 字节 `r || s` 签名封装的 canonical Neo
invocation script，而不是 DER 签名。合约账户或多签适配器可返回自己的有效
invocation script，但必须匹配已配置的 verification script 与费用估算 witness 形状。

## 失败行为

该边界始终 fail closed。缺少参数、account/script-hash 不匹配、无效十六进制或
JSON、空响应脚本、子进程非零退出、超时或调用方取消，都会在
`sendrawtransaction` 前终止。签名成功后，CLI 仍执行正常的 `getversion` network
校验、`invokescript` 预执行、精确网络费计算、广播与 HALT 确认。

不要把 `signData`、序列化交易、provider token 或响应脚本写入共享 shell 历史或
日志。适配器进程属于运维者控制的信任边界：应把二进制路径和来源与 KMS key policy
一起固定，并在生产启用前于 devnet 演练取消、超时和无效签名路径。
