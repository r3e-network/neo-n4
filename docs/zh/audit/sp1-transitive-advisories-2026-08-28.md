# SP1 6.2.1 传递依赖通报评估 — 2026-08-28

当前有两条 GitHub Advisory Database 通报命中被钉扎的 SP1 6.2.1 工具链的传递依赖。
截至本日期,两条均无对应的 RUSTSEC 记录,因此 CI 的 `cargo audit` 门禁(RUSTSEC 数据源)
对全部生产 lockfile 保持绿色。两条 Dependabot 安全更新作业在 master 提交上失败,原因是
被钉扎的依赖图无法表达修复。本笔记记录评估结论与既定修复路径。

## GHSA-vj64-rjf3-w3v7 / CVE-2026-46654 — p3-challenger(high)

- **通报内容**:Plonky3 `MultiField32Challenger` 转录可塑性与挑战熵损失——Fiat-Shamir
  海绵未将挑战严格绑定到观测的域元素流(吸收/挤出单射性、被吸收比特的覆盖性)。
- **锁定版本**:`p3-challenger 0.3.3-succinct`(SP1 的 Plonky3 分支线),命中
  `>= 0.4.3` → 被标记。上游修复版本:0.4.3 / 0.5.3。
- **可达性**:仅经 SP1 证明器栈
  (`slop-whir → sp1-hypercube → sp1-core-* → sp1-prover → sp1-sdk 6.2.1`)引入,消费方为
  `bridge/neo-zkvm-host`(运营商侧证明器)及网关宿主的 build-dependency。链上代码不链接
  这些 crate。
- **影响**:削弱运营商侧证明器产出的 SP1 证明的理论可靠性;L1 的
  `NeoHub.Sp1Groth16Verifier` 在针对钉扎 VK 验证时信任证明系统的可靠性。该漏洞赋能的
  威胁行为者与 Stage-2 证明已置于运营商信任边界上的行为者相同。SP1 的 `0.3.3-succinct`
  分支是否回移上游修复无公开记录;保守立场将其视为受影响。
- **修复路径**:协调性 SP1 工具链升级,至依赖图钉扎 `p3-challenger >= 0.4.3` 的版本
  (或带回移修复的 SP1 线)。这是 VK 重钉 + guest 重建 + `SP1_STATEFUL_NEO_VM_V1`
  语义 ID 轮换,不是 lockfile 级别的 bump。先例:Dependabot 的 sp1 6.3.1 尝试
  (PR #23)因冲突及 4 项检查失败被关闭;6.2.1 的钉扎是刻意的(见 AGENTS.md /
  IMPLEMENTATION_STATUS.md Phase 4)。在迁移落地前,本项按运营商信任模型记录为
  已接受风险。

## GHSA-qqmc-hwqp-8g2w — lru(high,0.12 线无已修补版本)

- **通报内容**:用后释放——持有缓存迭代器的同时调用 `pop()` 会释放迭代器仍引用的条目。
  Dependabot 另按未修补的 `>= 0.9.0, < 0.16.3` 区间匹配了同类模式。
- **锁定版本**:`lru 0.12.5`,仅经 `sp1-prover 6.2.1 → sp1-sdk` 可达。
- **影响**:运营商侧证明器进程内的内存安全缺陷;不在链上,不在验证代码中。需要易受攻击
  的迭代器 + `pop` 交错调用才可触发;上游未发布可迁移的 0.12.x 修复版本。
- **修复路径**:钉扎工具链版本下无可行动作;随上述协调性 SP1 升级一并解决。

## 已采取的行动

1. `dependabot.yml` 对 `lru` + `p3-challenger`(cargo 生态)设置 ignore 并指向本笔记,
   使安全更新作业停止在每个 master 提交上失败,升级协调期间不再产生噪音。该 ignore
   仅覆盖这两个名称;其余所有通报路径保持激活。
2. CI 的 `cargo audit` 门禁不变:当 RUSTSEC 发布匹配记录时,门禁将大声失败。届时要么
   添加有依据的 `--ignore`(`.github/workflows/build.yml` 中 RUSTSEC-2026-0258/h2 的
   先例),要么完成 SP1 升级——失败本身就是提醒,不应预先压制。

## 验证快照(2026-08-28,master @ 0a40ee5e)

- `cargo audit --file Cargo.lock --ignore RUSTSEC-2026-0258` → 0 漏洞,
  9 个允许的 warning(仅 yanked crate)。
- `cargo tree -i lru` / `cargo tree -i p3-challenger` → 仅经上述 sp1-sdk 6.2.1 路径可达。
