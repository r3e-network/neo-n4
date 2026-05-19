# Neo N4 统一体验中心第一期实施计划

> **给 agentic workers:** REQUIRED SUB-SKILL: 使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实施本计划。步骤使用 checkbox（`- [ ]`）语法追踪。

**目标:** 构建第一版可运行的 Neo N4 统一体验中心基础：报告 schema、仓库 manifest、静态 app shell、中英文文档入口和聚焦测试。

**架构:** 第一期是在 `docs/experience-hub/` 下实现一个静态、本地优先的文档应用。它读取版本化报告 fixture 和生成的仓库 manifest，把现有 runtime theater 作为可链接的工作流模块复用，并保持有权限操作仍然位于 CLI、钱包和节点边界。

**技术栈:** 原生 HTML/CSS/ES modules、Node 内置 test runner、不新增 npm 依赖、浏览器不签名、不引入框架构建步骤。

---

## 范围

本计划只实现基础层。不实现实时 devnet 编排、真实部署执行、.NET CLI 内的 report export 命令或真实公网 testnet 证据。这些在 app 和 report 边界稳定后由后续计划处理。

第一版 app shell 视觉参考：`docs/experience-hub/concepts/neo-n4-experience-hub-concept.png`。

设计规范：`docs/superpowers/specs/2026-05-19-neo-n4-unified-experience-hub-design.md`。

英文计划：`docs/superpowers/plans/2026-05-19-neo-n4-experience-hub-phase-1.md`。

## 文件结构

新建：

- `docs/experience-hub/index.html` - 静态 app 入口。
- `docs/experience-hub/app.js` - DOM 渲染和交互。
- `docs/experience-hub/hubState.js` - 纯状态、tab、status 和数据 helper。
- `docs/experience-hub/styles.css` - app 布局和响应式视觉系统。
- `docs/experience-hub/package.json` - 将目录标记为 ESM。
- `docs/experience-hub/data/reportSchemas.js` - 报告类型定义和验证器。
- `docs/experience-hub/data/sampleReports.js` - 脱敏的确定性示例报告。
- `docs/experience-hub/data/neo-n4.manifest.json` - UI 消费的生成式仓库 manifest。
- `tools/experience-hub/generate-manifest.mjs` - 确定性 manifest 生成器。
- `tests/experience-hub/report-schemas.test.mjs` - schema 和脱敏测试。
- `tests/experience-hub/manifest-generator.test.mjs` - manifest 生成器测试。
- `tests/experience-hub/hub-state.test.mjs` - app 状态测试。
- `docs/experience-hub.md` - 英文文档入口。
- `docs/zh/experience-hub.md` - 中文文档入口。

修改：

- `docs/SUMMARY.md` - 添加英文文档链接。
- `docs/zh/SUMMARY.md` - 添加中文文档链接。

## 任务 1：报告 Schema 和示例报告

**文件:**

- 新建：`docs/experience-hub/data/reportSchemas.js`
- 新建：`docs/experience-hub/data/sampleReports.js`
- 测试：`tests/experience-hub/report-schemas.test.mjs`

- [ ] **步骤 1：写失败的 schema 测试**

创建 `tests/experience-hub/report-schemas.test.mjs`，测试内容与英文计划任务 1 中的代码一致。测试必须验证：

- 每种第一期报告类型都有脱敏示例报告。
- 错误 type 和缺少 envelope metadata 会失败。
- private key、mnemonic、secret-like key/value 会被拒绝。

- [ ] **步骤 2：运行测试并确认失败**

运行：

```powershell
node --test tests\experience-hub\report-schemas.test.mjs
```

期望：FAIL，因为 `docs/experience-hub/data/reportSchemas.js` 尚不存在。

- [ ] **步骤 3：实现 schema helper 和 fixture**

创建 `docs/experience-hub/data/reportSchemas.js`，导出：

- `reportTypes`
- `validateReportEnvelope(report, expectedType)`
- `assertRedactedReport(report)`

创建 `docs/experience-hub/data/sampleReports.js`，每个报告使用统一 envelope：

```js
function envelope(type, tool, summary, payload) {
  return {
    type,
    schemaVersion: '1.0.0',
    repoCommit: '966f4ac',
    generatedAt: '2026-05-19T00:00:00Z',
    tool,
    network: { name: 'devnet-n4', kind: 'private' },
    redaction: { secrets: 'removed', credentials: 'omitted' },
    summary,
    payload,
  };
}
```

fixture 必须覆盖：

- `chain-config-report`: `chainId`, `proofMode`, `daMode: 'NeoFS'`, `vmProfile: 'NeoVM2/RISC-V'`。
- `deployment-plan`: `contracts`, `requiresWitnesses`, `nativeAccelerator`。
- `deployment-receipt`: deployed contract hash 和 block height。
- `devnet-report`: L1、L2、NeoFS、batcher、prover、gateway、bridge service 状态。
- `neofs-da-report`: object id、commitment、read/write check result、retention。
- `bridge-drill-report`: deposit、inclusion、settlement、withdrawal、replay check。
- `validation-report`: total、passed、failed、skipped、success rate、evidence paths。

- [ ] **步骤 4：运行 schema 测试**

运行：

```powershell
node --test tests\experience-hub\report-schemas.test.mjs
```

期望：PASS。

- [ ] **步骤 5：提交**

```powershell
git add docs\experience-hub\data\reportSchemas.js docs\experience-hub\data\sampleReports.js tests\experience-hub\report-schemas.test.mjs
git commit -m "feat: add experience hub report schemas"
```

## 任务 2：仓库 Manifest 生成器

**文件:**

- 新建：`tools/experience-hub/generate-manifest.mjs`
- 新建：`docs/experience-hub/data/neo-n4.manifest.json`
- 测试：`tests/experience-hub/manifest-generator.test.mjs`

- [ ] **步骤 1：写失败的 manifest 测试**

创建 `tests/experience-hub/manifest-generator.test.mjs`，测试内容与英文计划任务 2 中的代码一致。测试必须验证：

- 能发现 `contracts/NeoHub.*`。
- 能发现 `tools/*`。
- 能发现 docs。
- 固定包含 deposit、batch、proof、withdrawal、external、challenge 工作流。
- 默认 DA 是 `NeoFS`。
- 默认 VM profile 是 `NeoVM2/RISC-V`。

- [ ] **步骤 2：运行测试并确认失败**

```powershell
node --test tests\experience-hub\manifest-generator.test.mjs
```

期望：FAIL，因为 generator 尚不存在。

- [ ] **步骤 3：实现 manifest generator**

创建 `tools/experience-hub/generate-manifest.mjs`。它必须导出：

- `buildManifest(root)`
- `writeManifest(root, outFile)`

要求：

- 只使用 Node 内置模块。
- 所有数组按 name/path 排序。
- root object 包含 `schemaVersion`、`generatedAt`、`contracts`、`tools`、`docs`、`workflows`、`da`、`vm`、`boundaries`。
- `da.defaultProvider` 必须是 `NeoFS`。
- `vm.defaultProfile` 必须是 `NeoVM2/RISC-V`。
- `boundaries.neohub` 必须是 `deployable L1 contracts`。

- [ ] **步骤 4：生成仓库 manifest**

```powershell
node tools\experience-hub\generate-manifest.mjs --root . --out docs\experience-hub\data\neo-n4.manifest.json
```

期望：文件包含全部 24 个 `contracts/NeoHub.*` 目录和已有 `tools/*` 目录。

- [ ] **步骤 5：运行 manifest 测试**

```powershell
node --test tests\experience-hub\manifest-generator.test.mjs
```

期望：PASS。

- [ ] **步骤 6：提交**

```powershell
git add tools\experience-hub\generate-manifest.mjs docs\experience-hub\data\neo-n4.manifest.json tests\experience-hub\manifest-generator.test.mjs
git commit -m "feat: generate experience hub manifest"
```

## 任务 3：静态 Experience Hub App Shell

**文件:**

- 新建：`docs/experience-hub/package.json`
- 新建：`docs/experience-hub/index.html`
- 新建：`docs/experience-hub/styles.css`
- 新建：`docs/experience-hub/hubState.js`
- 新建：`docs/experience-hub/app.js`
- 测试：`tests/experience-hub/hub-state.test.mjs`

- [ ] **步骤 1：写失败的状态测试**

创建 `tests/experience-hub/hub-state.test.mjs`，测试内容与英文计划任务 3 中的代码一致。测试必须验证：

- 四个 workspace 是 `learn`、`build`、`operate`、`verify`。
- 架构节点保留 NeoHub 可部署 L1 合约边界。
- `NeoVM2 / RISC-V` 是默认 L2 执行节点。
- evidence summary 区分 private devnet evidence 和 public network evidence。

- [ ] **步骤 2：运行测试并确认失败**

```powershell
node --test tests\experience-hub\hub-state.test.mjs
```

期望：FAIL，因为 `hubState.js` 尚不存在。

- [ ] **步骤 3：实现纯状态模块**

创建 `docs/experience-hub/hubState.js`，导出：

- `workspaceIds`
- `architectureNodes`
- `createHubState({ reports, manifest })`
- `selectWorkspace(state, workspaceId)`
- `summarizeEvidence(reports)`

`architectureNodes` 必须包含：

- `neohub`
- `native-zk`
- `zk-accelerator`
- `shared-bridge`
- `gateway`
- `neofs-da`
- `l2-riscv`
- `optional-vm`

- [ ] **步骤 4：创建静态 app 文件**

创建：

- `docs/experience-hub/package.json`，内容为 `{ "type": "module" }`。
- `docs/experience-hub/index.html`，包含语义结构、Learn/Build/Operate/Verify 导航、架构 canvas、证据 inspector、report timeline，以及指向 `../interactive-runtime/index.html` 的链接。
- `docs/experience-hub/app.js`，导入 `hubState.js` 和 `sampleReports.js` 并渲染架构节点、证据摘要、workspace 内容和点击切换状态。
- `docs/experience-hub/styles.css`，遵循视觉概念：白色/graphite 外壳、绿色表示 healthy/NeoFS、蓝色表示 L2、琥珀色表示 proof/DA、紫色表示 optional VM profile、不使用装饰 blobs、不使用嵌套卡片、移动端不溢出。

- [ ] **步骤 5：运行状态测试**

```powershell
node --test tests\experience-hub\hub-state.test.mjs
```

期望：PASS。

- [ ] **步骤 6：手动打开 app**

运行：

```powershell
python -m http.server 8088
```

打开：

```text
http://localhost:8088/docs/experience-hub/
```

期望：

- Learn/Build/Operate/Verify 导航会改变可见内容。
- 架构 canvas 包含 NeoHub、NativeZkVerifier、L1 Native ZK Accelerator、NeoFS DA 和 NeoVM2/RISC-V。
- 页面明确说明 sample report 是 private devnet evidence，不是 public network evidence。

- [ ] **步骤 7：提交**

```powershell
git add docs\experience-hub\package.json docs\experience-hub\index.html docs\experience-hub\styles.css docs\experience-hub\hubState.js docs\experience-hub\app.js tests\experience-hub\hub-state.test.mjs
git commit -m "feat: add static experience hub shell"
```

## 任务 4：文档入口和中英文目录链接

**文件:**

- 新建：`docs/experience-hub.md`
- 新建：`docs/zh/experience-hub.md`
- 修改：`docs/SUMMARY.md`
- 修改：`docs/zh/SUMMARY.md`

- [ ] **步骤 1：创建英文入口**

创建 `docs/experience-hub.md`，内容与英文计划任务 4 的示例一致。

- [ ] **步骤 2：创建中文入口**

创建 `docs/zh/experience-hub.md`，内容与英文计划任务 4 的示例一致。

- [ ] **步骤 3：添加目录链接**

在 `docs/SUMMARY.md` 的 Architecture 下添加：

```md
- [Neo N4 Experience Hub](./experience-hub.md)
```

在 `docs/zh/SUMMARY.md` 的架构下添加：

```md
- [Neo N4 统一体验中心](./experience-hub.md)
```

- [ ] **步骤 4：提交**

```powershell
git add docs\experience-hub.md docs\zh\experience-hub.md docs\SUMMARY.md docs\zh\SUMMARY.md
git commit -m "docs: add experience hub entry"
```

## 任务 5：验证和推送

**文件:**

- 验证任务 1-4 的全部文件。

- [ ] **步骤 1：运行 Node 测试**

```powershell
node --test tests\interactive-runtime\simulator.test.mjs tests\experience-hub\*.test.mjs
```

期望：所有 simulator 和 experience-hub 测试 PASS。

- [ ] **步骤 2：检查 Git 空白问题**

```powershell
git diff --check
```

期望：无错误。

- [ ] **步骤 3：浏览器验证**

启动：

```powershell
python -m http.server 8088
```

打开：

```text
http://localhost:8088/docs/experience-hub/
```

验证：

- 桌面视图架构 canvas 非空。
- 移动宽度没有横向溢出。
- Learn/Build/Operate/Verify 按钮能更新内容。
- evidence inspector 明确标注 sample evidence 是 private devnet evidence。
- UI 不要求或保存 private key。

- [ ] **步骤 4：如验证产生修复则提交**

```powershell
git add docs tests tools
git commit -m "fix: polish experience hub phase 1"
```

- [ ] **步骤 5：只推送到 r3e-network**

确认：

```powershell
git remote -v
```

期望：`origin` 只有 `https://github.com/r3e-network/neo-n4.git`。

推送：

```powershell
git push origin master
```

期望：`master -> master`。

## 自审

规格覆盖：

- Learn/Build/Operate/Verify shell：任务 3。
- 报告 schema 和脱敏：任务 1。
- Repo manifest：任务 2。
- NeoFS DA 一等信号：任务 1、2、3。
- NeoVM2/RISC-V 默认执行层和 optional VM profile 表述：任务 2、3。
- 浏览器只读安全边界：任务 1、3、4。
- 中英文文档一致：任务 4。
- 验证：任务 5。

本期范围外：

- 实时 devnet start/stop 编排。
- 真实 NeoFS 写入/读取探测。
- .NET CLI report export 命令。
- 真实合约部署 receipt。
- 公网 testnet 证据。

占位扫描：没有故意留下未解决的实现步骤。后续阶段工作被明确列为范围外，而不是遗漏步骤。
