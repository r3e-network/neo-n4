# Neo N4 Experience Hub Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first working Neo N4 Experience Hub foundation: report schemas, repository manifest, static app shell, bilingual docs entry, and focused tests.

**Architecture:** Phase 1 is a static, local-first documentation app under `docs/experience-hub/`. It reads versioned report fixtures and a generated repository manifest, reuses the existing runtime theater as a linked workflow module, and keeps privileged actions in CLI/wallet/node boundaries.

**Tech Stack:** Plain HTML/CSS/ES modules, Node built-in test runner, no new npm dependency, no browser-side signing, no framework build step.

---

## Scope

This plan implements the foundation only. It does not implement live devnet orchestration, real deployment execution, report export commands inside .NET CLIs, or live public testnet evidence. Those belong to later plans after this app and report boundary exist.

Visual reference for the first app shell: `docs/experience-hub/concepts/neo-n4-experience-hub-concept.png`.

Design spec: `docs/superpowers/specs/2026-05-19-neo-n4-unified-experience-hub-design.md`.

Chinese plan mirror: `docs/zh/superpowers/plans/2026-05-19-neo-n4-experience-hub-phase-1.md`.

## File Structure

Create:

- `docs/experience-hub/index.html` - static app entry point.
- `docs/experience-hub/app.js` - DOM rendering and interactions.
- `docs/experience-hub/hubState.js` - pure state, tab, status, and data helpers.
- `docs/experience-hub/styles.css` - app layout and responsive visual system.
- `docs/experience-hub/package.json` - marks the folder as ESM for tests and local static development.
- `docs/experience-hub/data/reportSchemas.js` - report type definitions and validators.
- `docs/experience-hub/data/sampleReports.js` - redacted deterministic sample reports.
- `docs/experience-hub/data/neo-n4.manifest.json` - generated repository manifest consumed by the UI.
- `tools/experience-hub/generate-manifest.mjs` - deterministic manifest generator.
- `tests/experience-hub/report-schemas.test.mjs` - schema and redaction tests.
- `tests/experience-hub/manifest-generator.test.mjs` - manifest generator tests.
- `tests/experience-hub/hub-state.test.mjs` - app state tests.
- `docs/experience-hub.md` - English docs entry.
- `docs/zh/experience-hub.md` - Chinese docs entry.

Modify:

- `docs/SUMMARY.md` - add English docs link.
- `docs/zh/SUMMARY.md` - add Chinese docs link.

## Task 1: Report Schemas and Sample Reports

**Files:**

- Create: `docs/experience-hub/data/reportSchemas.js`
- Create: `docs/experience-hub/data/sampleReports.js`
- Test: `tests/experience-hub/report-schemas.test.mjs`

- [ ] **Step 1: Write failing schema tests**

Create `tests/experience-hub/report-schemas.test.mjs`:

```js
import test from 'node:test';
import assert from 'node:assert/strict';

import {
  reportTypes,
  validateReportEnvelope,
  assertRedactedReport,
} from '../../docs/experience-hub/data/reportSchemas.js';
import { sampleReports } from '../../docs/experience-hub/data/sampleReports.js';

test('all required phase-1 report types have redacted sample reports', () => {
  assert.deepEqual(Object.keys(sampleReports).sort(), reportTypes.toSorted());
  for (const type of reportTypes) {
    const result = validateReportEnvelope(sampleReports[type], type);
    assert.equal(result.ok, true, `${type}: ${result.errors.join(', ')}`);
    assert.doesNotThrow(() => assertRedactedReport(sampleReports[type]));
  }
});

test('report validation rejects wrong type and missing metadata', () => {
  const report = { ...sampleReports['devnet-report'], type: 'validation-report' };
  const result = validateReportEnvelope(report, 'devnet-report');
  assert.equal(result.ok, false);
  assert.match(result.errors.join('\n'), /type must be devnet-report/);

  const incomplete = { type: 'devnet-report' };
  const missing = validateReportEnvelope(incomplete, 'devnet-report');
  assert.equal(missing.ok, false);
  assert.match(missing.errors.join('\n'), /schemaVersion/);
  assert.match(missing.errors.join('\n'), /repoCommit/);
});

test('redaction guard rejects secret-like keys and values', () => {
  assert.throws(
    () => assertRedactedReport({
      ...sampleReports['deployment-receipt'],
      payload: { privateKey: 'Kx1234567890' },
    }),
    /secret-like key/i,
  );

  assert.throws(
    () => assertRedactedReport({
      ...sampleReports['deployment-receipt'],
      payload: { note: 'mnemonic phrase should never be present' },
    }),
    /secret-like value/i,
  );
});
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
node --test tests\experience-hub\report-schemas.test.mjs
```

Expected: FAIL because `docs/experience-hub/data/reportSchemas.js` does not exist.

- [ ] **Step 3: Implement schema helpers and fixtures**

Create `docs/experience-hub/data/reportSchemas.js` with:

```js
export const reportTypes = Object.freeze([
  'chain-config-report',
  'deployment-plan',
  'deployment-receipt',
  'devnet-report',
  'neofs-da-report',
  'bridge-drill-report',
  'validation-report',
]);

const requiredEnvelopeFields = Object.freeze([
  'schemaVersion',
  'repoCommit',
  'generatedAt',
  'tool',
  'network',
  'redaction',
  'type',
  'summary',
  'payload',
]);

const secretKeyPattern = /(private|secret|mnemonic|seed|password|token|credential|signingKey)/i;
const secretValuePattern = /(mnemonic phrase|private key|BEGIN PRIVATE KEY|Kx[0-9A-Za-z]{20,}|L[0-9A-Za-z]{20,})/i;

export function validateReportEnvelope(report, expectedType) {
  const errors = [];
  if (!report || typeof report !== 'object' || Array.isArray(report)) {
    return { ok: false, errors: ['report must be an object'] };
  }
  for (const field of requiredEnvelopeFields) {
    if (!(field in report)) errors.push(`${field} is required`);
  }
  if (report.type !== expectedType) errors.push(`type must be ${expectedType}`);
  if (!reportTypes.includes(expectedType)) errors.push(`unknown report type ${expectedType}`);
  if (typeof report.schemaVersion !== 'string' || !report.schemaVersion.match(/^1\./)) {
    errors.push('schemaVersion must be a v1 string');
  }
  if (typeof report.repoCommit !== 'string' || report.repoCommit.length < 7) {
    errors.push('repoCommit must identify the source commit');
  }
  if (report.redaction?.secrets !== 'removed') {
    errors.push('redaction.secrets must be removed');
  }
  return { ok: errors.length === 0, errors };
}

export function assertRedactedReport(report) {
  walk(report, []);
}

function walk(value, path) {
  if (Array.isArray(value)) {
    value.forEach((item, index) => walk(item, [...path, String(index)]));
    return;
  }
  if (!value || typeof value !== 'object') {
    if (typeof value === 'string' && secretValuePattern.test(value)) {
      throw new Error(`secret-like value at ${path.join('.')}`);
    }
    return;
  }
  for (const [key, child] of Object.entries(value)) {
    if (secretKeyPattern.test(key)) {
      throw new Error(`secret-like key at ${[...path, key].join('.')}`);
    }
    walk(child, [...path, key]);
  }
}
```

Create `docs/experience-hub/data/sampleReports.js` with one deterministic report per type. Every report must use this envelope shape:

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

The fixture payloads must include:

- `chain-config-report`: `chainId`, `proofMode`, `daMode: 'NeoFS'`, `vmProfile: 'NeoVM2/RISC-V'`.
- `deployment-plan`: `contracts`, `requiresWitnesses`, `nativeAccelerator`.
- `deployment-receipt`: deployed contract hashes and block heights.
- `devnet-report`: L1, L2, NeoFS, batcher, prover, gateway, bridge service status.
- `neofs-da-report`: object id, commitment, read/write check result, retention.
- `bridge-drill-report`: deposit, inclusion, settlement, withdrawal, replay check.
- `validation-report`: total, passed, failed, skipped, success rate, evidence paths.

- [ ] **Step 4: Run schema tests**

Run:

```powershell
node --test tests\experience-hub\report-schemas.test.mjs
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add docs\experience-hub\data\reportSchemas.js docs\experience-hub\data\sampleReports.js tests\experience-hub\report-schemas.test.mjs
git commit -m "feat: add experience hub report schemas"
```

## Task 2: Repository Manifest Generator

**Files:**

- Create: `tools/experience-hub/generate-manifest.mjs`
- Create: `docs/experience-hub/data/neo-n4.manifest.json`
- Test: `tests/experience-hub/manifest-generator.test.mjs`

- [ ] **Step 1: Write failing manifest tests**

Create `tests/experience-hub/manifest-generator.test.mjs`:

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { buildManifest, writeManifest } from '../../tools/experience-hub/generate-manifest.mjs';

test('manifest generator discovers NeoHub contracts, tools, docs, and workflows', async () => {
  const root = await mkdtemp(join(tmpdir(), 'neo-n4-manifest-'));
  await mkdir(join(root, 'contracts', 'NeoHub.SharedBridge'), { recursive: true });
  await mkdir(join(root, 'tools', 'Neo.Stack.Cli'), { recursive: true });
  await mkdir(join(root, 'docs'), { recursive: true });
  await writeFile(join(root, 'docs', 'interactive-runtime.md'), '# Interactive Runtime');

  const manifest = await buildManifest(root);
  assert.deepEqual(manifest.contracts.map((item) => item.name), ['NeoHub.SharedBridge']);
  assert.deepEqual(manifest.tools.map((item) => item.name), ['Neo.Stack.Cli']);
  assert.equal(manifest.docs.some((item) => item.path === 'docs/interactive-runtime.md'), true);
  assert.equal(manifest.workflows.some((item) => item.id === 'deposit'), true);
  assert.equal(manifest.da.defaultProvider, 'NeoFS');
  assert.equal(manifest.vm.defaultProfile, 'NeoVM2/RISC-V');
});

test('writeManifest creates deterministic JSON', async () => {
  const root = await mkdtemp(join(tmpdir(), 'neo-n4-manifest-write-'));
  await mkdir(join(root, 'contracts', 'NeoHub.TokenRegistry'), { recursive: true });
  await mkdir(join(root, 'tools', 'Neo.Hub.Deploy'), { recursive: true });
  await mkdir(join(root, 'docs'), { recursive: true });

  const out = join(root, 'docs', 'experience-hub', 'data', 'neo-n4.manifest.json');
  await writeManifest(root, out);
  const json = JSON.parse(await readFile(out, 'utf8'));

  assert.equal(json.schemaVersion, '1.0.0');
  assert.equal(json.contracts[0].name, 'NeoHub.TokenRegistry');
  assert.equal(json.tools[0].name, 'Neo.Hub.Deploy');
});
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
node --test tests\experience-hub\manifest-generator.test.mjs
```

Expected: FAIL because the generator does not exist.

- [ ] **Step 3: Implement manifest generator**

Create `tools/experience-hub/generate-manifest.mjs`. It must export `buildManifest(root)` and `writeManifest(root, outFile)`, use only Node built-ins, sort all arrays by name/path, and include fixed workflow ids:

```js
const workflows = Object.freeze([
  { id: 'deposit', label: 'L1 to L2 deposit' },
  { id: 'batch', label: 'Batch settlement' },
  { id: 'proof', label: 'ZK proof and native accelerator' },
  { id: 'withdrawal', label: 'L2 to L1 withdrawal' },
  { id: 'external', label: 'External bridge' },
  { id: 'challenge', label: 'Challenge and recovery' },
]);
```

The manifest root object must contain:

- `schemaVersion: '1.0.0'`
- `generatedAt`
- `contracts`
- `tools`
- `docs`
- `workflows`
- `da: { defaultProvider: 'NeoFS' }`
- `vm: { defaultProfile: 'NeoVM2/RISC-V', optionalProfiles: ['EVM', 'WASM', 'Move', 'Custom'] }`
- `boundaries: { neohub: 'deployable L1 contracts', browser: 'read-only' }`

- [ ] **Step 4: Generate repository manifest**

Run:

```powershell
node tools\experience-hub\generate-manifest.mjs --root . --out docs\experience-hub\data\neo-n4.manifest.json
```

Expected: file is written and contains all 24 `contracts/NeoHub.*` directories and all existing `tools/*` directories.

- [ ] **Step 5: Run manifest tests**

Run:

```powershell
node --test tests\experience-hub\manifest-generator.test.mjs
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add tools\experience-hub\generate-manifest.mjs docs\experience-hub\data\neo-n4.manifest.json tests\experience-hub\manifest-generator.test.mjs
git commit -m "feat: generate experience hub manifest"
```

## Task 3: Static Experience Hub App Shell

**Files:**

- Create: `docs/experience-hub/package.json`
- Create: `docs/experience-hub/index.html`
- Create: `docs/experience-hub/styles.css`
- Create: `docs/experience-hub/hubState.js`
- Create: `docs/experience-hub/app.js`
- Test: `tests/experience-hub/hub-state.test.mjs`

- [ ] **Step 1: Write failing state tests**

Create `tests/experience-hub/hub-state.test.mjs`:

```js
import test from 'node:test';
import assert from 'node:assert/strict';

import {
  architectureNodes,
  createHubState,
  selectWorkspace,
  summarizeEvidence,
  workspaceIds,
} from '../../docs/experience-hub/hubState.js';
import { sampleReports } from '../../docs/experience-hub/data/sampleReports.js';

test('hub exposes the four approved workspaces', () => {
  assert.deepEqual(workspaceIds, ['learn', 'build', 'operate', 'verify']);
  const state = createHubState({ reports: sampleReports });
  assert.equal(state.activeWorkspace, 'learn');
  assert.equal(selectWorkspace(state, 'operate').activeWorkspace, 'operate');
});

test('architecture nodes preserve approved protocol boundaries', () => {
  const ids = architectureNodes.map((node) => node.id);
  assert.deepEqual(ids, [
    'neohub',
    'native-zk',
    'zk-accelerator',
    'shared-bridge',
    'gateway',
    'neofs-da',
    'l2-riscv',
    'optional-vm',
  ]);
  assert.equal(architectureNodes.find((node) => node.id === 'neohub').boundary, 'deployable L1 contracts');
  assert.equal(architectureNodes.find((node) => node.id === 'l2-riscv').label, 'NeoVM2 / RISC-V');
});

test('evidence summary separates local private evidence from public network evidence', () => {
  const summary = summarizeEvidence(sampleReports);
  assert.equal(summary.networkKind, 'private');
  assert.equal(summary.hasPublicNetworkEvidence, false);
  assert.equal(summary.daProvider, 'NeoFS');
  assert.equal(summary.validationStatus, 'attention');
});
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
node --test tests\experience-hub\hub-state.test.mjs
```

Expected: FAIL because `hubState.js` does not exist.

- [ ] **Step 3: Implement pure hub state**

Create `docs/experience-hub/hubState.js` with:

```js
export const workspaceIds = Object.freeze(['learn', 'build', 'operate', 'verify']);

export const architectureNodes = Object.freeze([
  { id: 'neohub', label: 'NeoHub L1', kind: 'l1', boundary: 'deployable L1 contracts' },
  { id: 'native-zk', label: 'NativeZkVerifier', kind: 'verifier', boundary: 'deployable verifier adapter' },
  { id: 'zk-accelerator', label: 'L1 Native ZK Accelerator', kind: 'accelerator', boundary: 'native accelerator hook' },
  { id: 'shared-bridge', label: 'SharedBridge', kind: 'bridge', boundary: 'L1 asset custody' },
  { id: 'gateway', label: 'Gateway', kind: 'gateway', boundary: 'optional proof aggregation' },
  { id: 'neofs-da', label: 'NeoFS DA', kind: 'da', boundary: 'data availability' },
  { id: 'l2-riscv', label: 'NeoVM2 / RISC-V', kind: 'l2', boundary: 'canonical L2 execution profile' },
  { id: 'optional-vm', label: 'Optional VM Profiles', kind: 'extension', boundary: 'pluggable N4 L2 execution profiles' },
]);

export function createHubState({ reports = {}, manifest = undefined } = {}) {
  return {
    activeWorkspace: 'learn',
    reports,
    manifest,
    selectedNodeId: 'neohub',
  };
}

export function selectWorkspace(state, workspaceId) {
  if (!workspaceIds.includes(workspaceId)) throw new Error(`Unknown workspace ${workspaceId}`);
  return { ...state, activeWorkspace: workspaceId };
}

export function summarizeEvidence(reports) {
  const validation = reports['validation-report']?.payload ?? {};
  const da = reports['neofs-da-report']?.payload ?? {};
  const network = reports['devnet-report']?.network ?? {};
  return {
    networkKind: network.kind ?? 'unknown',
    hasPublicNetworkEvidence: network.kind === 'testnet' || network.kind === 'mainnet',
    daProvider: da.provider ?? 'NeoFS',
    validationStatus: validation.failed > 0 ? 'attention' : 'valid',
  };
}
```

- [ ] **Step 4: Create static app files**

Create:

- `docs/experience-hub/package.json` with `{ "type": "module" }`.
- `docs/experience-hub/index.html` with semantic landmarks, nav buttons for Learn/Build/Operate/Verify, architecture canvas, evidence inspector, report timeline, and link to `../interactive-runtime/index.html`.
- `docs/experience-hub/app.js` that imports `hubState.js`, `sampleReports.js`, and renders:
  - architecture nodes from `architectureNodes`;
  - report summary from `summarizeEvidence(sampleReports)`;
  - workspace-specific body copy;
  - selected state when a workspace button is clicked.
- `docs/experience-hub/styles.css` using the concept's restrained console visual system:
  - white/graphite shell;
  - green for healthy/NeoFS;
  - blue for L2;
  - amber for proof/DA;
  - violet for optional VM profiles;
  - no decorative blobs;
  - no card nesting;
  - mobile layout without text overlap.

- [ ] **Step 5: Run state tests**

Run:

```powershell
node --test tests\experience-hub\hub-state.test.mjs
```

Expected: PASS.

- [ ] **Step 6: Manually open the app**

Run:

```powershell
python -m http.server 8088
```

Open:

```text
http://localhost:8088/docs/experience-hub/
```

Expected:

- Learn/Build/Operate/Verify navigation changes visible content.
- Architecture canvas includes NeoHub, NativeZkVerifier, L1 Native ZK Accelerator, NeoFS DA, and NeoVM2/RISC-V.
- The page states that public network evidence is not present when sample reports are private devnet reports.

- [ ] **Step 7: Commit**

```powershell
git add docs\experience-hub\package.json docs\experience-hub\index.html docs\experience-hub\styles.css docs\experience-hub\hubState.js docs\experience-hub\app.js tests\experience-hub\hub-state.test.mjs
git commit -m "feat: add static experience hub shell"
```

## Task 4: Documentation Entry and Bilingual Summary Links

**Files:**

- Create: `docs/experience-hub.md`
- Create: `docs/zh/experience-hub.md`
- Modify: `docs/SUMMARY.md`
- Modify: `docs/zh/SUMMARY.md`

- [ ] **Step 1: Create English entry**

Create `docs/experience-hub.md` with:

```md
# Neo N4 Experience Hub

Open the local-first Experience Hub:

[Launch the Neo N4 Experience Hub](../../experience-hub/index.html)

The hub is a read-only product surface for understanding, building, operating,
and verifying Neo N4. It visualizes NeoHub deployed L1 contracts, NeoFS DA,
NativeZkVerifier, the L1 native ZK accelerator, Gateway, SharedBridge, L2
NeoVM2/RISC-V execution, optional N4 L2 VM profiles, and validation evidence.

The browser does not hold private keys or sign deployment/governance actions.
Privileged actions remain in CLIs, wallets, node processes, and contracts.
```

- [ ] **Step 2: Create Chinese entry**

Create `docs/zh/experience-hub.md` with:

```md
# Neo N4 统一体验中心

打开本地优先的体验中心：

[启动 Neo N4 统一体验中心](../../experience-hub/index.html)

体验中心是一个只读产品界面，用于理解、构建、运维和验证 Neo N4。它可视化
NeoHub 可部署 L1 合约、NeoFS DA、NativeZkVerifier、L1 原生 ZK 加速器、
Gateway、SharedBridge、L2 NeoVM2/RISC-V 执行、可选 N4 L2 VM profile，
以及验证证据。

浏览器不保存私钥，也不签署部署或治理操作。有权限操作仍然留在 CLI、钱包、节点进程和合约中。
```

- [ ] **Step 3: Add summary links**

Add to `docs/SUMMARY.md` under Architecture:

```md
- [Neo N4 Experience Hub](../../experience-hub.md)
```

Add to `docs/zh/SUMMARY.md` under Architecture:

```md
- [Neo N4 统一体验中心](../../experience-hub.md)
```

- [ ] **Step 4: Commit**

```powershell
git add docs\experience-hub.md docs\zh\experience-hub.md docs\SUMMARY.md docs\zh\SUMMARY.md
git commit -m "docs: add experience hub entry"
```

## Task 5: Verification and Push

**Files:**

- Verify all files from Tasks 1-4.

- [ ] **Step 1: Run Node tests**

Run:

```powershell
node --test tests\interactive-runtime\simulator.test.mjs tests\experience-hub\*.test.mjs
```

Expected: PASS for all simulator and experience-hub tests.

- [ ] **Step 2: Check git whitespace**

Run:

```powershell
git diff --check
```

Expected: no errors.

- [ ] **Step 3: Browser verification**

Serve:

```powershell
python -m http.server 8088
```

Open:

```text
http://localhost:8088/docs/experience-hub/
```

Verify:

- Desktop view has no blank architecture canvas.
- Mobile-width view has no horizontal overflow.
- Learn/Build/Operate/Verify buttons update content.
- Evidence inspector clearly labels the sample evidence as private devnet evidence.
- No UI asks for or stores a private key.

- [ ] **Step 4: Final commit if verification changes were needed**

If verification required fixes:

```powershell
git add docs tests tools
git commit -m "fix: polish experience hub phase 1"
```

- [ ] **Step 5: Push only to r3e-network**

Confirm:

```powershell
git remote -v
```

Expected: only `https://github.com/r3e-network/neo-n4.git` for `origin`.

Push:

```powershell
git push origin master
```

Expected: `master -> master`.

## Self-Review

Spec coverage:

- Learn/Build/Operate/Verify shell: Task 3.
- Report schemas and redaction: Task 1.
- Repo manifest: Task 2.
- NeoFS DA as first-class signal: Tasks 1, 2, 3.
- NeoVM2/RISC-V default and optional VM profile framing: Tasks 2, 3.
- Browser read-only security boundary: Tasks 1, 3, 4.
- English/Chinese doc parity: Task 4.
- Verification: Task 5.

Out of scope for this phase:

- Live devnet start/stop orchestration.
- Real NeoFS write/read probes.
- .NET CLI report export commands.
- Real contract deployment receipts.
- Public testnet evidence.

Completion scan: no intentionally unresolved implementation steps are included. Later-phase work is explicitly out of scope, not a missing step.
