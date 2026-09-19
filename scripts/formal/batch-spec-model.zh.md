# 批次承诺 spec 对应性模型 — 2026-09-18

doc.md §3.2 的 `L2BatchCommitment` 结构与实现 wire 布局（BatchSerializer.cs +
RollupHubContract.cs）之间的 spec-to-implementation 对应性模型。这是手写抽象，**不是**
C#/NeoVM 验证器。

## 已建立的性质

按声明顺序以标准 C# wire 宽度（uint32=4、ulong=8、UInt256=32、byte=1）遍历 doc.md §3.2
声明的字段（chainId、batchNumber、firstBlock、lastBlock、preStateRoot、postStateRoot、
txRoot、receiptRoot、withdrawalRoot、l2ToL1MessageRoot、l2ToL2MessageRoot、daCommitment、
publicInputHash、proofType），**恰好**推导出实现使用的偏移 chainId@0 .. proofType@316
——spec 字段清单与实现完全同步。

- proof 区域紧随其后：[317,321) 的 4 字节长度前缀与 [321,321+proofLen) 的 proof 字节；
  固定头恰为 321 字节（HeaderMinLength）。
- 头部字段连续、无间隙、偏移严格递增。
- **public-inputs 域恰为 352 字节**：header[0..252) + l1MessageHash(32) + daCommitment(32)
  + blockContextHash(32) + forcedInclusionCount(4)，前缀止于 l2ToL2MessageRoot 末尾。
- **注入性**：[0,321) 的 15 字段划分（14 个固定字段 + proof 长度前缀，即 doc.md 可变
  `byte[] proof` 的编码）意味着字段相等 ⇒ 头部相同——spec 字段清单决定整个头部编码；
  chainId 不同必然改变它。
- 三个负向对照在划分被破坏时构造反例：丢弃 publicInputHash 使 [284,316) 无字段覆盖
  （结算摘要歧义）、省略 forcedInclusionCount 使 fic@348..352 自由（强制计数歧义）、
  省略 proofLen 前缀使 proof 载荷长度欠定。

`verify_batch_spec.py` 共 13 项义务（7 UNSAT 静态/布局事实、2 UNSAT 注入性、2 SAT 可达、
3 SAT 负向对照），全部通过。`batch-spec-result.json` 记录求解器、规范/脚本哈希和信任假设。

## 边界与限制

本模型覆盖 doc.md 结构与实现布局之间的**线格式对应性**——声明的字段分解即是编码。
它**不**建模执行语义、proof 验证逻辑、DA 模式门控或 L1 结算状态机（独立义务；结算模型
与 VmTests 覆盖行为）。Hash256 抗碰撞是独立的信任假设。Fail-closed 通过源码锚点实现
（序列化器的 321/352 布局文档与合约的 OffsetProofType=316 / HeaderMinLength=321 声明），
而非摘要钉死，因此与注释无关的布局漂移会被捕获，而文档格式化变更不会。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_batch_spec.py
```

Windows 使用 `.venv-formal/Scripts/python`。四项自测覆盖锚点漂移、UNKNOWN、反例拒绝
以及模型/负向对照。