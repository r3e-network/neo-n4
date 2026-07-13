# 四语言 SDK 一致性

Neo N4 的 SDK 共同执行一份协议契约，而不是分别维护四套手写预期：

| SDK | 生产客户端 | 离线测试 | 真实环境测试 |
|---|---|---|---|
| .NET | `src/Neo.L2.Sdk/` | MSTest 分类 `SdkConformanceOffline` | MSTest 分类 `SdkConformanceLive` |
| Rust | `sdk/rust/` | `conformance_offline` | ignored target `conformance_live` |
| TypeScript | `sdk/typescript/` | Vitest `tests/conformance.offline.test.ts` | Vitest `tests/conformance.live.test.ts` |
| Python | `sdk/python/` | `unittest` `test_conformance_offline.py` | `unittest` `test_conformance_live.py` |

所有测试都读取 [`sdk/conformance/vectors/v1.json`](../../sdk/conformance/vectors/v1.json)。CI 运行器会拒绝空选择、部分执行、任何被跳过的离线测试，以及配置了真实环境却没有执行全部已发现测试的情况。它输出 `neo-n4-sdk-conformance-summary/v1` JSON，记录每种语言的发现、执行、通过、失败与跳过数量，并包含向量/夹具的 SHA-256 摘要，以及可用时的 GitHub 提交 SHA。

## 覆盖矩阵

| 范围 | 规范断言 |
|---|---|
| RPC 方法形态 | 十个规范 L2 方法和历史状态根重载使用一致的方法名、参数顺序与返回语义。 |
| 整数编码 | 每个 `u64` 请求值使用十进制 JSON 字符串；`u64::MAX` 和大于 `2^53` 的值验证无损响应解码，并拒绝不安全的 JSON number。 |
| 规范批次 | 完整 batch commitment 会被解码并重新编码；真实环境夹具校验器拒绝与结构化字段不一致的 `encoded` 值。 |
| 哈希字节序 | `UInt256` 线上字节为小端，RPC 展示为反转后的 32 字节小写十六进制并带 `0x`。 |
| 错误映射 | 服务端错误、响应 ID 不匹配和非 2.0 信封映射到统一的 server/protocol 错误分类。 |
| 分页/序列化 | 游标可空性、页面顺序和大于 `2^53` 的标识符可无损 JSON 往返。当前十方法接口没有分页；该向量固定未来分页方法的通用信封契约。 |
| 签名/交易 | 真实 secp256r1 签名在 `networkMagicLE || txHash` 上验证；未签名字节、txid、调用脚本、验证脚本与完整原始交易均可往返。 |
| 真实 N3/N4 | 两个节点都必须满足 `getversion`、`getblockcount` 和 `getblockhash(0)` 的响应形态；N4 还必须满足类型化安全标签/状态根查询及错误链域的 server-error 映射。 |

真实环境测试只读，不会广播交易夹具，也不会调用 `sendrawtransaction`。

## 本地命令

先安装 TypeScript 与 Python 测试依赖：

```bash
npm --prefix sdk/typescript ci --no-audit --no-fund
python3 -m pip install --editable 'sdk/python[test]'
```

运行全部离线测试并写入机器可读报告：

```bash
python3 scripts/ci/run_sdk_conformance.py \
  --mode offline \
  --require-all-languages \
  --output artifacts/sdk-conformance-offline.json
```

.NET 项目通常解析固定的 `external/neo` 子模块。已经检出 r3e Neo fork 的审查者可以把 `NEO_CORE_PATH` 指向其 `src` 目录，无需修改项目文件。

## 真实节点契约

真实环境执行必须显式启用，并提供两个节点端点、N4 链 ID，以及基于 `sdk/conformance/live-fixture.example.json` 的运营方夹具：

```bash
export NEO_SDK_LIVE='1'
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

如果环境不完整，四种语言都会把三个真实环境测试报告为跳过，汇总状态为 `skipped`；`--require-live` 会把该状态转换成失败。配置完整时，每种语言都会验证夹具中的网络 Magic、创世哈希、最低高度、全部十一个 N4 RPC 结果，以及类型化客户端/错误路径。这样可防止发布门禁在执行零个真实断言或连接错误部署时仍显示绿色。

## CI 与发布门禁

`.github/workflows/sdk-conformance.yml` 始终运行离线任务。普通拉取请求和分支推送不会创建真实环境任务；定时运行、`v*` 标签运行和手动触发始终要求 `NEO_N3_RPC_URL`、`NEO_N4_RPC_URL`、`NEO_N4_CHAIN_ID` 机密及 `NEO_SDK_LIVE_FIXTURE_JSON`，其内容必须符合示例夹具结构。工作流将其写入私有临时文件，并要求四种 SDK 全部真实执行；缺少机密会失败，而不是产生空的绿色任务。

发布必须同时受两个工作流检查保护，并保留离线和真实环境 JSON 汇总作为证据。运行器验证测试发现与执行数量，因此工作流工件证明实际执行过测试，而不只是测试进程返回了零退出码。

## 规范对齐

权威 `doc.md` §14.1、`Neo.Plugins.L2Rpc`、官方 RpcServer 注册适配器和四套 SDK 现在共享同一套十方法 ABI。该 ABI 使用可选历史状态根选择器、绑定链域的证明查询、以 `(sourceChainId, nonce)` 表示的充值身份、绑定链域的桥接资产查询，以及 §16.2 的 `getsecuritylabel` 方法。共享向量在参数顺序、十进制字符串 `u64` 编码、JSON-RPC 信封/ID、链域、批次和充值身份漂移时执行关闭式失败。决策矩阵与本地真实 HTTP 证据记录在 [`audit/p1-1-rpc-sdk-abi.md`](./audit/p1-1-rpc-sdk-abi.md)。
