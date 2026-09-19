# 注册状态机模型 — 2026-09-18

原子 `RegisterChain` 的归纳安全模型。这是手写抽象，**不是** C#/NeoVM 程序验证。
根为 256 位向量，布尔量建模 config 存储、创世根存储、OffsetActive 位与可观测 active。

## 证明的命题

在被治理、单入口注册的链上，状态机满足：

- **无部分初始化**：config 与创世根的存储总是同时存在或同时缺失
  （`registered == genesis_set`）。允许"只配置未锚根"或"只锚根未配置"的旧两条入口
  已被移除，因此 `IsChainActive`（现 `IsActive`）在创世根缺失时永不为真。
- **创世根非零且不可变**：已注册链必有非零创世根；重复注册仅在根不变时允许刷新
  config，任何转移都不能改写已设置的根。
- **active 定义健全**：`IsActive(chainId) = config[OffsetActive]==1 且创世根已存在`，
  结构性排除"无根却 active"。

四个转移（`register`/`update`/`resume`/`pause`）各自验证三条性质（保持不变量、
active 蕴含已锚根、根不可变），外加初始状态和 active 定义共 13 条 UNSAT 安全查询、
4 条可达性 SAT（防空前提冒充证据）与 3 条负向对照（去掉守卫即可构造坏状态）。

义务清单：`verify_registration.py` 21 项，全部通过（13 unsat / 8 sat）。
结果写入 `registration-result.json`；求解器版本、源码/脚本/规范哈希与信任假设随报告落盘。

## 信任假设与边界

模型信任：手写 C#/NeoVM 对应性（非抽取的转移关系）、单一原子注册入口、幂等重注册
仅刷新 config、原子故障回滚无重入、active 语义与 `IsActive` 一致、无治理回滚或并发转移。
它**不**代替 VM 测试（`RegisterChain_IsAtomic_ActiveAndIdempotent` 等钉住字节码行为）——
模型证明状态机性质，测试证明编译产物行为，二者互补。

文本锚与 `RegisterChain` 的零根断言绑定，源码摘要变动即 fail-closed 要求重审对应性。
本模型提供结算模型所假设的"链已注册且创世根存在"前提；结算转移本身见 settlement-model.md。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_registration.py
```

Windows 将解释器路径换成 `.venv-formal/Scripts/python`。
自测 5 项覆盖源码守卫变更、换行格式、UNKNOWN 拒绝、反例拒绝与正常模型/负向对照。