# 有限范围形式化验证

## 状态

**全系统形式化验证尚未完成。** 本目录包含二十个 Z3 模型与两个 CTL 模型——BatchSerializer.Decode
长度算术、原子 RegisterChain 注册状态机、强制包含 FIFO 队列、SharedBridge L1 escrow
守恒、L2 桥代币供给账本、GovernanceController 授权门、DA 写入侧失效切换策略、消息/提款
canonical preimage 结构注入性、Gateway 发布 outbox（归纳安全、崩溃恢复再水合、写入顺序
崩溃原子性、CTL 可达性活性）、乐观挑战二分游戏、doc.md §3.2 链配置与批次承诺线格式对应性、§3.2 结算方法面、§10/§11 桥接方法面、跨组件管道组合
（强制入列队列 × 批次生命周期）、批次算术不变量（单调性、不重叠、回滚约束）、状态
单调性（已终局状态根与 Gateway 水位永不回退）、算术溢出安全（ulong 递增与键构造无
碰撞），以及已注册链结算状态机的归纳安全模型——不是 C#/NeoVM 程序验证器。性质测试、变异测试与
SP1 执行证明属于不同证据，均不能替代全系统正确性证明。另见 settlement-model.md、
registration-model.md、forced-inclusion-model.md、bridge-conservation-model.md、
l2-bridge-model.md、governance-model.md、da-failover-model.md、
preimage-injectivity-model.md、gateway-outbox-model.md、gateway-outbox-recovery-model.md、
gateway-outbox-crashatomic-model.md、gateway-outbox-liveness-model.md、bisection-model.md、
config-spec-model.md、batch-spec-model.md、settlement-abi-model.md、bridge-abi-model.md、pipeline-liveness-model.md、batch-arithmetic-model.md、state-monotonicity-model.md、overflow-safety-model.md（中文要点见下文）与 mutation-results.zh.md。

结算模型（`verify_settlement.py`）对提交、两步终局化、原子终局化与故障停等
四种转移证明了基态满足与归纳保持：批次 N 的 pre 根等于批次 N-1 的 post 根
（批次 1 等于不可变创世根）、仅通过验证的批次可终局化、规范根始终等于最后
终局后根、高度连续且不回绕、已终局历史不可变；三项删改守卫的负向对照均能
产生反例。合约源码哈希被固定，改动即要求重新评审。模型信任 C#/NeoVM 原子性、
守卫可达性与无并发转移等假设。规范要求 config 与创世根原子注册，RollupHub 的
RegisterChain 现为单调用原子入口（REG-ATOMIC-01 已关闭），先前"合约暴露两个独立入口"
的差异已随该迁移消除，并有对应 VM 测试钉住。

## 复现

在仓库根目录使用 Python 3.13：

```sh
python -m venv .venv-formal
# Windows 使用 .venv-formal/Scripts/python；Unix 使用 .venv-formal/bin/python
.venv-formal/bin/python -m pip install -r scripts/formal/requirements.txt
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_batch_length.py
.venv-formal/bin/python scripts/formal/verify_registration.py
.venv-formal/bin/python scripts/formal/verify_forced_inclusion.py
.venv-formal/bin/python scripts/formal/verify_bridge.py
.venv-formal/bin/python scripts/formal/verify_governance.py
.venv-formal/bin/python scripts/formal/verify_da_failover.py
.venv-formal/bin/python scripts/formal/verify_preimage.py
.venv-formal/bin/python scripts/formal/verify_outbox.py
.venv-formal/bin/python scripts/formal/verify_outbox_recovery.py
.venv-formal/bin/python scripts/formal/verify_outbox_crashatomic.py
.venv-formal/bin/python scripts/formal/verify_bisection.py
.venv-formal/bin/python scripts/formal/verify_config_spec.py
.venv-formal/bin/python scripts/formal/verify_batch_spec.py
.venv-formal/bin/python scripts/formal/verify_settlement_abi.py
.venv-formal/bin/python scripts/formal/verify_bridge_abi.py
.venv-formal/bin/python scripts/formal/verify_pipeline_liveness.py
.venv-formal/bin/python scripts/formal/verify_batch_arithmetic.py
.venv-formal/bin/python scripts/formal/verify_state_monotonicity.py
.venv-formal/bin/python scripts/formal/verify_overflow_safety.py
.venv-formal/bin/python scripts/formal/verify_l2_bridge.py
.venv-formal/bin/python scripts/formal/verify_outbox_liveness.py
.venv-formal/bin/python scripts/formal/verify_settlement.py
```

各脚本在同目录写入对应证据文件（batch-length-result.txt / registration-result.json /
forced-inclusion-result.json / bridge-conservation-result.json / governance-result.json /
da-failover-result.json / preimage-injectivity-result.json / gateway-outbox-result.json /
gateway-outbox-recovery-result.json / gateway-outbox-crashatomic-result.json / bisection-result.json /
gateway-outbox-liveness-result.json / l2-bridge-result.json / settlement-result.json），
记录源码/脚本/规范 SHA-256、求解器版本、查询结果和退出码。每次修订应重新生成，已保存
的结果不是当前运行证据。缺少依赖、源码锚点变化、异常、非预期 SAT 或 UNKNOWN 均返回
退出码 1。自测会对临时源码副本执行故意失败的检查；其打印的 `FAILED` 是预期结果，
但 unittest 进程整体必须退出 0。

## 已建立的结论

模型使用有符号 32 位 proofLen 和缓冲区长度，假设
`0 <= proofLen <= 1048576`、缓冲区长度至少为 321、游标 `pos=321`：

- 合法证明长度加上固定头部不会导致有符号 int32 溢出。
- 模型的等值守卫只接受数学意义上恰为 `321 + proofLen` 的长度。
- 模型接受域排除负数及超限证明长度。
- 接受域非空，且零长度与最大长度均可接受。
- 弱化等值守卫后可以接受一个尾随字节（负向对照）。

三项反例查询必须返回 UNSAT；可满足性查询防止把空前提当作有效证据。
64 位符号扩展在这里代表精确加法，因为任何 int32 与 321 的和均落在 int64 内。

## 可信假设与限制

脚本检查 `src/Neo.L2.Batch/BatchSerializer.cs` 的守卫文本和长度上限。
这**不是 C# 解析器或符号执行器**。文本存在不能证明控制流、可达性、异常行为、
字段顺序、游标值或编译后二进制的拒绝行为；这些仍是可信的实现对应假设，
由独立生产测试辅助检查。例如保留守卫文本但修改 throw 内容不在漂移检查覆盖内。
长度相等和非法长度排除描述的是模型守卫本身，不能独立证明生产代码执行这些守卫。

`UT_BatchSerializer` 独立测试超限证明拒绝、最大长度接受、超限声明长度拒绝、
尾随字节拒绝。最近完整 Batch 测试为 136 项通过，只是回归证据。
PublicInputs 是 352 字节（ForcedInclusionCount 在偏移 348），不是这里建模的
321 字节 commitment 头部。

## 尚未完成的系统工作

不提供全系统完成百分比。注册、强制包含 FIFO/队列、桥接 L1 escrow 与 L2 供给守恒、
治理授权、DA 写入侧失效切换、canonical preimage 结构注入性、Gateway 发布 outbox
（归纳安全、崩溃恢复再水合、写入顺序崩溃原子性、CTL 可达性活性）、乐观挑战二分游戏
（rollback 收窄）、doc.md §3.2 链配置与批次承诺线格式对应性、§3.2 结算方法面、§10/§11 桥接方法面、跨组件管道组合、批次算术不变量、状态单调性、溢出安全与结算状态
机现由有限范围模型覆盖
（见 registration-model.md / forced-inclusion-model.md / bridge-conservation-model.md /
l2-bridge-model.md / governance-model.md / da-failover-model.md /
preimage-injectivity-model.md / gateway-outbox-model.md / gateway-outbox-recovery-model.md /
gateway-outbox-crashatomic-model.md / gateway-outbox-liveness-model.md /
bisection-model.md / config-spec-model.md / batch-spec-model.md /
settlement-model.md）。各模型的信任假设在
各自文档中如实记录，均不声称超出其所述范围。outbox 的 recovery/crashatomic 模型覆盖
协议层恢复再水合与写入顺序崩溃原子性；它们不建模 RocksDB 内部 WAL 或单次存储写的
原子性。config-spec、batch-spec、settlement-abi 与 bridge-abi 模型覆盖链配置、批次承诺
线格式、结算方法面与桥接方法面对应性切片；spec-to-implementation 对应义务已在
doc 声明的方法面层面关闭；pipeline-liveness 模型覆盖双组件排序器管道组合（队列 ×
批次生命周期），batch-arithmetic 模型覆盖执行语义算术层（批次/区块单调性、不重叠、
回滚约束），state-monotonicity 模型覆盖执行语义状态层（已终局状态根链连续性、
Gateway 水位单调），overflow-safety 模型覆盖算术溢出安全切片（ulong 递增与键构造
无碰撞），完整多组件网络组合与完整执行语义仍未关闭。未关闭的证明义务包括：方法面之外的
spec 对应（字段级/状态级语义）、完整多组件网络组合、执行语义、完整 nonce 重放绑定与 SHA-256 抗碰撞、
RocksDB 内部 WAL/持久化与 L1 核心 ChainMode 门控 GAS 钩子，以及明确网络假设下的
组合正确性和活性。每项均需要模型、审核后的假设、反例、实现关联和可复核 CI 证据。
SP1 发布检查保持独立：它证明固定程序的执行，不代表程序与外围系统满足所有预期性质。

早期失败/取消的尝试之后，现已完成两次 Stryker 实测：补充四项针对性回归后，
分数从 74.81% 提升到 91.60%。完整报告、剩余变异及分数排除项见
[mutation-results.zh.md](mutation-results.zh.md)。这不构成全系统验证完成，
也不认可历史认证报告的结论。
