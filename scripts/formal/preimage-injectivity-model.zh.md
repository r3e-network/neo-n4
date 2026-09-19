# Preimage 注入性模型 — 2026-09-18

`src/Neo.L2.State/MessageHasher.cs` 中消息/提款 canonical preimage 的结构注入性模型。
这是手写抽象，**不是** C#/NeoVM 验证器。

## 已建立的性质

`EncodeMessage` / `EncodeWithdrawal` 将每个字段放入定长小端位位置，并用长度前缀标记
可变 payload。由于字段占据不相交偏移且 payload 边界由长度前缀明确，编码是**注入的**：

- 不同的 `chainId`、`targetChainId`、`nonce`、`sender`、`receiver` 或 `messageType`
  必然产生不同的 preimage 字节；
- 不同的 payload 长度或 payload 字节必然产生不同的 preimage 字节；
- chainId 字段是 load-bearing 的域分隔符：来自不同 L2 链、其余字段相同的两条消息会
  编码为不同 preimage，因此在 preimage 层面结构上阻止跨 L2 包含证明重放。

在信任 `Hash256`（双重 SHA-256）无碰撞的假设下，不同 preimage 蕴含不同叶哈希。这覆盖
"nonce/replay" 的 nonce/chainId 重放绑定一半，以及"消息哈希"的结构（preimage）一半；
SHA-256 本身的密码抗碰撞是独立的信任假设，不在此重推。

`verify_preimage.py` 共 13 项义务（9 UNSAT 字段绑定、1 SAT 不同消息可达性、3 SAT 负向
对照：移除长度前缀 / nonce / chainId 守卫）。全部通过。
`preimage-injectivity-result.json` 记录求解器、源码/脚本/规范哈希和信任假设。

## 边界与限制

本模型证明 **preimage 的结构注入性**，不证明 SHA-256 无碰撞、运行时 NEF 行为，也不证明
这些哈希在 Merkle/结算中的使用。提款与消息字段布局按编码器写入的宽度建模；实际的
`UInt160` 序列化与 `BigInteger.ToByteArray` 取自源码并被信任。长度前缀以有界 payload
切片建模以保持检查精确；解码器自身的长度守卫由测试单独覆盖。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_preimage.py
```

Windows 使用 `.venv-formal/Scripts/python`。五项自测覆盖源码漂移、换行、UNKNOWN、
反例拒绝以及正常模型/负向对照。