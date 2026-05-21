# 第 7 章：学习路径

本章按读者角色组织继续阅读路线。它的目标是让不同背景的读者都能从本书进入代码库。

## 7.1 如果你是第一次了解 Neo N4

阅读顺序：

1. 本书 [README](./README.md)
2. [系统模型](./01-system-model.md)
3. [总体架构](./02-architecture.md)
4. [`../visual-guide.md`](../visual-guide.md)
5. [`../experience-hub.md`](../experience-hub.md)

目标：能画出 L1 NeoHub、Gateway、L2、DA、Prover、Bridge 的关系。

## 7.2 如果你要实现或修改代码

阅读顺序：

1. [协议与数据结构](./03-protocol-data.md)
2. [实现导读](./04-implementation.md)
3. `src/Neo.L2.Abstractions`
4. 与任务相关的 `src/Neo.L2.*` 模块
5. 对应 `tests/Neo.L2.*.UnitTests`

修改原则：

- 先找共享模型，不要重新定义协议对象；
- 修改协议对象时同步文档、wire format 和测试；
- 修改 L2 native contracts 时同步 `external/neo` tests；
- 修改 NeoHub deploy plan 时同步 `Neo.Hub.Deploy.UnitTests`；
- 修改用户可见英文文档时同步中文 counterpart。

## 7.3 如果你是 L2 运营者

阅读顺序：

1. [`../getting-started.md`](../getting-started.md)
2. [`../launching-an-l2.md`](../launching-an-l2.md)
3. [运维、部署与运行](./05-operations.md)
4. [`../private-network-testing.md`](../private-network-testing.md)
5. [`../telemetry.md`](../telemetry.md)
6. [`../release-readiness-checklist.md`](../release-readiness-checklist.md)

你需要能回答：

- 使用哪种 `DAMode`？
- 是否启用 Gateway？
- 使用哪种 proof mode？
- sequencer / prover / DA / settlement 谁运行？
- emergency owner 和 governance owner 是谁？
- 用户如何退出？

## 7.4 如果你是合约审计者

阅读顺序：

1. [`../neohub-architecture-and-workflows.md`](../neohub-architecture-and-workflows.md)
2. `contracts/NeoHub.*`
3. `tools/Neo.Hub.Deploy/ScaffoldPlan.cs`
4. `tests/Neo.Hub.Deploy.UnitTests`
5. [安全、测试与审计](./06-security-testing.md)

重点检查：

- 每个合约的 owner / governance / pause 权限；
- bridge escrow 的重放保护；
- withdrawal proof 的 spent 状态；
- token decimals 缩放；
- `ContractZkVerifier` 是否 safe-by-default；
- stub verifier 是否不会进入生产 deploy plan。

## 7.5 如果你是证明系统工程师

阅读顺序：

1. [协议与数据结构](./03-protocol-data.md)
2. `src/Neo.L2.Proving`
3. `bridge/neo-execution-core`
4. `bridge/neo-zkvm-host`
5. `bridge/neo-zkvm-guest`
6. `contracts/NeoHub.ContractZkVerifier`

关键问题：

- public input hash 是否唯一绑定 batch commitment？
- guest ELF 是否可复现？
- host-mode execution 和 zkVM execution 是否一致？
- malformed proof 是否被拒绝？
- verification key 如何注册和升级？

## 7.6 如果你是应用开发者

阅读顺序：

1. [`../wallet-integration.md`](../wallet-integration.md)
2. `sdk/typescript`
3. `src/Neo.L2.Sdk`
4. `sdk/rust`
5. `sdk/web-explorer`

建议先跑：

```powershell
Push-Location sdk\typescript
npm test
npm run build
Pop-Location
```

然后用本地 devnet 测：

```powershell
dotnet run --project tools\Neo.L2.Devnet\Neo.L2.Devnet.csproj `
  -c Release -- 3 --config samples\general-rollup.config.json
```

## 7.7 如果你要写新章节

新增文档必须满足：

| 规则 | 说明 |
| --- | --- |
| 有中文 counterpart | 英文文档新增时必须同步中文 |
| 有图表 counterpart | 英文 SVG/PNG/图表新增时必须同步中文 |
| 链接真实代码 | 架构结论必须能指向实现或测试 |
| 不夸大生产状态 | 没有真实证据的公网部署不能写成已完成 |
| 避免旧路线 | 当前路线是 deployable NeoHub + ContractZkVerifier |
| 保持 VM 命名 | 默认 NeoVM2/RISC-V；额外 VM 是 execution profile |

## 7.8 最终检查清单

读完整本书后，读者应能做到：

- 解释 Neo N4 为什么分 L1 NeoHub、Gateway 和多条 L2；
- 描述 deposit、batch settlement、withdrawal、cross-L2 message 的数据流；
- 找到每个核心组件的代码目录；
- 运行本地 devnet；
- 生成 NeoHub deploy plan；
- 说明 NeoFS DA 在系统中的角色；
- 解释 `ContractZkVerifier` 与 verifier registry 的关系；
- 区分 testnet 演练证据和 mainnet 生产门槛；
- 给一个新 VM profile 设计正确接入边界。

