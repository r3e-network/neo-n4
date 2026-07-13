# SDK 共享一致性向量

`vectors/v1.json` 是由 .NET、Rust、TypeScript 和 Python SDK 一致性测试共同消费的语言无关契约。任何 RPC 线上协议语义变更，都必须在同一次审查中更新该文件及四种语言的消费者。

这些向量固定了：

- 全部十个公开 L2 RPC 方法，以及历史状态根的重载形式；
- 所有 `u64` 值（包括 `u64::MAX`）的 JSON 字符串编码；
- 严格的 JSON-RPC 2.0 信封和通用错误分类；
- 数值响应 ID 的原样回显，以及链域和请求身份绑定；
- Neo `UInt256` 的小端字节与反转后的 RPC 显示文本之间的对应关系；
- 超出 JavaScript 安全整数范围的游标与分页值的无损序列化；
- 一笔完整的 Neo N3 P-256 单签名交易，包括未签名字节、交易 ID、绑定网络的签名数据、见证脚本和原始交易；
- 明确的 L1/L2 链域值，以及对畸形响应执行关闭式失败的测试用例。

真实节点测试还会读取一份由运营方持有、并与 [`live-fixture.example.json`](./live-fixture.example.json) 匹配的夹具。该夹具固定 N3/N4 网络 Magic、创世区块哈希，以及批次、证明、桥接、消息、状态和安全查询的精确非空结果。它属于部署证据，因此仓库中的示例仅是结构模板，绝不能被描述为一次成功的真实环境运行。

交易使用私钥标量 `1` 对应的公开测试密钥。它只是确定性的互操作夹具，不是用于保管资金的账户。

通过 [`scripts/ci/run_sdk_conformance.py`](../../scripts/ci/run_sdk_conformance.py) 运行完整矩阵。

## 命令

```bash
python3 scripts/ci/run_sdk_conformance.py \
  --mode offline \
  --require-all-languages \
  --output artifacts/sdk-conformance-offline.json
```

真实环境运行必须显式启用，并提供两个节点端点、N4 链 ID 和运营方持有的部署夹具。仓库中的示例不会被自动选用：

```bash
export NEO_SDK_LIVE=1
export NEO_N3_RPC_URL='https://n3-node.example'
export NEO_N4_RPC_URL='https://n4-node.example'
export NEO_N4_CHAIN_ID='1099'
export NEO_SDK_LIVE_FIXTURE='/secure/path/neo-sdk-live-fixture.json'

python3 scripts/ci/run_sdk_conformance.py \
  --mode live \
  --require-live \
  --require-all-languages \
  --output artifacts/sdk-conformance-live.json
```

如果环境配置不完整，本地真实环境测试会把所有已发现测试报告为跳过；`--require-live` 会将缺失配置转换为非零退出失败。`.github/workflows/sdk-conformance.yml` 中的定时运行、版本标签运行和手动触发运行始终使用必需真实环境模式，并从仓库机密生成 `NEO_SDK_LIVE_FIXTURE_JSON`。

## 规范对齐

这些向量现在固定了 `doc.md` §14.1 的规范接口：可选的历史状态根选择器、绑定链域的证明查询、以 `(sourceChainId, nonce)` 表示的充值身份、绑定链域的桥接资产查询，以及 §16.2 的 `getsecuritylabel` 方法。四套 SDK 发送相同参数，要求 `u64` 字段使用十进制字符串，并在 JSON-RPC 版本或 ID、链 ID、批次号、充值身份不匹配时执行关闭式失败。详见 [`docs/audit/p1-1-rpc-sdk-abi.md`](../../docs/audit/p1-1-rpc-sdk-abi.md)。
