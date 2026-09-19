# 链配置 spec 对应性模型 — 2026-09-18

doc.md §3.2 的 `L2ChainConfig` 结构与 91 字节线格式序列化器
（`src/Neo.L2.Abstractions/Models/L2ChainConfigSerializer.cs`）之间的
spec-to-implementation 对应性模型。这是手写抽象，**不是** C#/NeoVM 验证器。

## 已建立的性质

doc.md §3.2 声明的 12 个字段（chainId、operatorManager、verifier、bridgeAdapter、
messageAdapter、securityLevel、daMode、gatewayEnabled、permissionlessExit、
sequencerModel、exitModel、active）**精确划分** 91 字节线域：

- 偏移连续、无间隙、无重叠，按 doc.md 字段顺序严格递增，且末字段恰好止于
  `ConfigSize == 91 == sum(字段宽度)`。
- 编码是**注入的**：两个字段相等的 91 字节编码必是相等的 wire（字段覆盖整个缓冲区），
  因此仅凭 spec 字段清单即确定编码；`chainId` 不同必然改变 wire。
- `active` 位位于偏移 90，在缓冲区内（合约在该处读取）。
- 三个负向对照在划分被破坏时构造反例：未被覆盖的尾部字节（90 字节布局使末字节无法由
  字段清单确定）、重叠字段对（写第二个破坏第一个）、间隙字节（字段相同但间隙字节不同
  的两条 wire 无法由字段清单区分）。

`verify_config_spec.py` 共 12 项义务（5 UNSAT 静态/字段事实、2 UNSAT 注入性、
2 SAT 可达、3 SAT 负向对照），全部通过。`config-spec-result.json` 记录求解器、
源码/规范/脚本哈希和信任假设。

## 边界与限制

本模型覆盖 doc.md 结构与序列化器之间的**线格式对应性**——声明的字段分解即是编码，
wire 上不搭载其他内容。它**不**建模合约的存储语义、chainId-0 保留守卫、枚举范围守卫
（securityLevel 0..4、daMode 0..3）或小数缩放；这些在合约与序列化器逻辑中，由测试覆盖。
`L2ChainConfigSerializer.cs` 与 `doc.md` 的摘要均已记录；序列化器漂移 fail-closed。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_config_spec.py
```

Windows 使用 `.venv-formal/Scripts/python`。四项自测覆盖源码漂移、换行、UNKNOWN 与
模型/负向对照。