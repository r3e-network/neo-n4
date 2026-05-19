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
