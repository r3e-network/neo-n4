import test from 'node:test';
import assert from 'node:assert/strict';

import {
  createInitialState,
  getScenario,
  listScenarios,
  runScenario,
} from '../../docs/interactive-runtime/simulator.js';

test('lists the core Neo N4 learning scenarios in stable order', () => {
  assert.deepEqual(
    listScenarios().map((scenario) => scenario.id),
    ['deposit', 'batch', 'proof', 'withdrawal', 'external', 'challenge'],
  );
});

test('deposit scenario locks L1 value before minting L2 credit', () => {
  const result = runScenario('deposit');

  assert.equal(result.ledger.l1Escrow, 100);
  assert.equal(result.ledger.l2Credit, 100);
  assert.equal(result.timeline.at(-1).label, 'L2BridgeContract mints wrapped asset');
  assert.equal(result.timeline.at(-1).packet.to, 'L2 native contracts');
});

test('batch scenario advances batch number and changes the state root', () => {
  const before = createInitialState();
  const after = runScenario('batch');

  assert.equal(after.batch.number, before.batch.number + 1);
  assert.notEqual(after.batch.stateRoot, before.batch.stateRoot);
  assert.equal(after.batch.status, 'sealed');
});

test('proof scenario routes through gateway aggregation before settlement', () => {
  const result = runScenario('proof');

  assert.equal(result.proof.status, 'accepted');
  assert.equal(result.gateway.queue, 0);
  assert.match(result.timeline.map((event) => event.label).join(' -> '), /Gateway folds proof -> SettlementManager accepts batch/);
});

test('every scenario has complete node endpoints and non-empty events', () => {
  for (const scenario of listScenarios()) {
    const loaded = getScenario(scenario.id);

    assert.ok(loaded.events.length >= 4, `${scenario.id} should teach at least four runtime steps`);
    for (const event of loaded.events) {
      assert.ok(event.packet.from, `${scenario.id}/${event.id} missing packet.from`);
      assert.ok(event.packet.to, `${scenario.id}/${event.id} missing packet.to`);
      assert.ok(event.label, `${scenario.id}/${event.id} missing label`);
    }
  }
});
