# 结算状态机模型 — 2026-09-17

`scripts/formal/verify_settlement.py` 是本目录第二项 SMT 模型，针对已完成注册的单链结算抽象，
参照 `contracts/NeoHub.RollupHub/RollupHubContract.cs` 与 doc.md §3.2、§8.3。
**这不是 C#/NeoVM 实现验证，也不是全系统完成声明。**

## 证明内容

基态是链与非零不可变创世根都已注册、终局高度为零的状态，不是未注册状态。
状态包含任意高度索引的 pre/post/verified 数组、可选 pending 批次及规范根。

对 submit、两步 finalize、原子 submit-and-finalize、失败停等四种转移，分别检查
前提可满足，以及后继保持不变量、历史不变、高度不减。不变量包括首批连接创世根、
后续批次连接前批 post 根、终局历史包含验证标记、规范根等于最后终局 post 根、
pending 批次指向下一个高度且连接当前规范根。

共 21 项查询：13 项反例查询 UNSAT、5 项前提可满足性 SAT、3 项负向对照 SAT。
删掉 pre-root 守卫、遗漏规范根更新、终局化未验证批次三个变体均产生反例。
这是符号归纳检查，不是固定批次数目的枚举。

## 实现对应与可信假设

源码按规范化换行计算 SHA-256；任何变化都要求重新评审，但哈希绑定不证明模型
与源码语义等价。映射依据为 SubmitBatchCore、FinalizeBatch、FinalizeBatchInternal、
GetCanonicalStateRoot、GetLatestFinalizedBatchNumber 与 DA 记录去重守卫。
现有 RollupHub VM 测试 15/15 通过，是独立回归证据，不等于模型提取或编译器证明。

模型假设 C#/NeoVM 事务原子回滚、守卫可达、没有重入和隐藏存储写入；高度是受
uint64 上限约束的数学整数，终端高度不允许后继。proof_ok 表示外部验证器返回成功，
不是密码学安全性证明。模型不包含治理回滚、重配置、并发、DA 可用性及活性。

规范要求 config 与创世根原子注册，合约目前由 RegisterChain 与
RegisterGenesisStateRoot 分开完成。模型只覆盖两者均完成之后，不能证明注册符合规范。

## 实现问题 REG-ATOMIC-01

状态：已关闭（原子 ABI + 创世根守卫）。RegisterChain(uint chainId, byte[] configBytes,
UInt256 genesisStateRoot) 现为唯一注册入口，在单次调用内原子持久化配置与不可变非零
创世根（doc.md §3.2）。独立的 registerGenesisStateRoot ABI 已移除，因此不存在两条事务
路径。IsActive(chainId) 返回 config[OffsetActive]==1 且创世根已存在时才为真，部分初始化的
链永远不会被观测为 active。重复注册幂等：仅在与已注册创世根不变时允许刷新配置（不同根
被拒绝），零根在注册前即被拒绝。

由 VM 测试钉住：RegisterChain_IsAtomic_ActiveAndIdempotent（零根拒绝、同根幂等重注册、
不同根拒绝、pause/resume/update 均锚定在已注册根上）与 RegisterChain_Atomic_Success。
GetCanonicalStateRoot（900–906 行）在规范根与创世根均不存在时 fault，因此未锚定的首批
不会被接受。结算转移模型假设两个注册均已完成的语义，现由原子调用精确保证。

## 复现

先按 README.zh.md 安装固定版本 Z3，然后在仓库根执行：

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_settlement.py
```

Windows 将解释器路径换成 `.venv-formal/Scripts/python`。
结果保存在 settlement-result.json，包含求解器版本、源码/脚本/规范哈希、每项结果和假设。
检查器自测合计 7 项（含长度模型 2 项、结算模型 5 项），覆盖源码守卫变更、换行格式、
UNKNOWN 拒绝、反例拒绝与正常模型/负向对照。失败返回非零退出码。
