import test from 'node:test';
import assert from 'node:assert/strict';

import {
  architectureNodes,
  bottomStatus,
  createHubState,
  reportTimeline,
  selectWorkspace,
  summarizeEvidence,
  topMetrics,
  validationEvidence,
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
    'contract-zk',
    'proof-verifier',
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
  assert.equal(summary.validationStatus, 'valid');
});

test('top and bottom status bars expose concept-grade devnet evidence', () => {
  const top = topMetrics(sampleReports);
  assert.equal(top.length, 10);
  assert.deepEqual(top.map((metric) => metric.label), [
    'Private Devnet',
    'L2 Block Height',
    'L2 TPS (avg)',
    'Prover Status',
    'DA (NeoFS)',
    'Gateway',
    'SharedBridge',
    'Network',
    'Environment',
    'Unix Time',
  ]);
  assert.equal(top.find((metric) => metric.label === 'DA (NeoFS)').value, 'healthy');
  assert.equal(top.find((metric) => metric.label === 'Network').value, 'devnet-n4');

  const bottom = bottomStatus(sampleReports);
  assert.equal(bottom.find((item) => item.label === 'Nodes').value, '4 / 4');
  assert.equal(bottom.find((item) => item.label === 'NeoFS Peers').value, '6');
});

test('validation evidence keeps NeoFS DA and onchain verification visible', () => {
  const groups = validationEvidence(sampleReports);
  assert.deepEqual(groups.map((group) => group.title), [
    'Latest Proof',
    'Onchain Verification',
    'Data Availability (NeoFS)',
  ]);
  assert.equal(groups[0].rows.find(([label]) => label === 'Status')[1], 'Verified on L1');
  assert.equal(groups[2].rows.find(([label]) => label === 'Replication')[1], '3/3');

  const timeline = reportTimeline(sampleReports);
  assert.equal(timeline.length, 7);
  assert.equal(timeline.at(-1).label, 'Verified');
});
