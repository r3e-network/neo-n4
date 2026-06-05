# neo-n4-sdk Python 版

这是 Neo Elastic Network L2 RPC 表面（`doc.md` §14.1）的类型化 Python
客户端。它与 .NET、TypeScript、Rust SDK 保持同一形状：同样 10 个
JSON-RPC 方法、同样四类错误、同样的 chainId 交叉校验。

运行时只依赖 Python 标准库，方便运维把它直接放进部署脚本、健康检查和冒烟测试，
不用额外携带依赖栈。

## 安装

```bash
cd sdk/python
python3 -m pip install .
python3 -m unittest discover -s tests
```

源码树内开发可直接运行：

```bash
PYTHONPATH=sdk/python python3 -m unittest discover -s sdk/python/tests
```

## 使用

```python
from neo_n4_sdk import L2RpcClient

client = L2RpcClient("http://node.example:30332", 1099)

root = client.get_latest_state_root()
batch = client.get_batch(7)
label = client.get_security_label()
proof = client.get_withdrawal_proof("0x...")
```

## RPC 表面

所有方法都是同步方法，并直接映射到 `doc.md` §14.1：

- `get_batch(batch_number)` → `getl2batch`
- `get_batch_status(batch_number)` → `getl2batchstatus`
- `get_latest_state_root()` → `getl2stateroot`
- `get_state_root_at(batch_number)` → `getl2stateroot`
- `get_withdrawal_proof(leaf)` → `getl2withdrawalproof`
- `get_message_proof(message_hash)` → `getl2messageproof`
- `get_deposit_status(source_chain_id, nonce)` → `getl1depositstatus`
- `get_canonical_asset(l2_asset)` → `getcanonicalasset`
- `get_bridged_asset(l1_asset)` → `getbridgedasset`
- `get_security_level()` / `get_security_label()`

批次号和 nonce 在 wire format 中编码为 JSON 字符串，用来保留完整 u64 精度，
并与 TypeScript SDK 的精度安全格式保持一致。

## 错误处理

每次调用只会抛出以下四类错误之一：

| 类型 | 是否适合重试 | 场景 |
|------|:------------:|------|
| `L2RpcTransportError` | 是 | HTTP 超时、连接失败、非 2xx |
| `L2RpcProtocolError` | 否 | JSON 损坏、envelope 错误、response id 不匹配 |
| `L2RpcServerError` | 视情况 | 服务端返回 JSON-RPC `error` 字段 |
| `L2RpcMismatchedChainIdError` | 否 | 服务端返回了其他链的数据 |

测试通过可注入 transport 执行，因此生产 HTTP 请求和进程内单元测试共享同一套
JSON-RPC envelope 校验路径。
