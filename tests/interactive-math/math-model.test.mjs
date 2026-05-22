import test from 'node:test';
import assert from 'node:assert/strict';

import {
  FIELD_PRIME,
  buildFieldSnapshot,
  buildMerkleTree,
  estimateProofPipeline,
  fieldInverse,
  fieldMul,
  lessonOrder,
  listLessons,
  makeLearningSnapshot,
  merkleProof,
  runNeoVmTrace,
  runRiscVTrace,
  sampleNeoVmProgram,
  sampleRiscVProgram,
  verifyMerkleProof,
} from '../../docs/interactive-math/mathModel.js';

test('lesson map covers the full NeoVM to zkVM to RISC-V learning path', () => {
  assert.deepEqual(listLessons().map((lesson) => lesson.id), lessonOrder);
  assert.ok(lessonOrder.includes('neovm'));
  assert.ok(lessonOrder.includes('riscv'));
  assert.ok(lessonOrder.includes('zk-proofs'));
});

test('finite-field snapshot keeps arithmetic closed and exposes inverse checks', () => {
  const snapshot = buildFieldSnapshot(5, 14, FIELD_PRIME);

  assert.equal(snapshot.add, 2);
  assert.equal(snapshot.mul, 2);
  assert.equal(fieldMul(5, fieldInverse(5, FIELD_PRIME), FIELD_PRIME), 1);
  assert.equal(snapshot.ruler.length, FIELD_PRIME);
});

test('Merkle commitment changes when a transaction leaf changes', () => {
  const leaves = ['tx:deposit', 'tx:mint', 'tx:swap', 'tx:withdraw'];
  const tree = buildMerkleTree(leaves);
  const proof = merkleProof(tree, 2);

  assert.equal(verifyMerkleProof(leaves[2], proof, tree.root, tree.prime), true);

  const tampered = buildMerkleTree(['tx:deposit', 'tx:mint', 'tx:swap:tampered', 'tx:withdraw']);
  assert.notEqual(tampered.root, tree.root);
  assert.equal(verifyMerkleProof('tx:swap:tampered', proof, tree.root, tree.prime), false);
});

test('NeoVM trace proves a chained stack transition for a simple equality program', () => {
  const trace = runNeoVmTrace(sampleNeoVmProgram);

  assert.equal(trace.halted, true);
  assert.equal(trace.result, 1);
  assert.equal(trace.gas, 6);
  assert.deepEqual(trace.trace.map((row) => row.op), ['PUSH', 'PUSH', 'ADD', 'PUSH', 'EQ', 'HALT']);
  assert.match(trace.trace[2].constraint, /a \+ b/);
});

test('RISC-V trace captures register, memory, and branch behavior', () => {
  const trace = runRiscVTrace(sampleRiscVProgram);

  assert.equal(trace.regs.x3, 12);
  assert.equal(trace.regs.x4, 12);
  assert.equal(trace.memory['0'], 12);
  assert.equal(trace.trace.at(-1).op, 'BEQ');
  assert.equal(trace.pc, 28);
});

test('proof estimate scales with cycles and aggregation while staying deterministic', () => {
  const small = estimateProofPipeline({ cycles: 16, memoryOps: 4, publicInputs: 3, aggregation: 1 });
  const large = estimateProofPipeline({ cycles: 128, memoryOps: 20, publicInputs: 6, aggregation: 4 });

  assert.ok(large.traceRows > small.traceRows);
  assert.ok(large.constraints > small.constraints);
  assert.ok(large.proofBytes >= small.proofBytes);
});

test('learning snapshot joins all interactive models into one stable state object', () => {
  const snapshot = makeLearningSnapshot({ activeLessonId: 'n4-security', merkleIndex: 0 });

  assert.equal(snapshot.activeLesson.id, 'n4-security');
  assert.equal(snapshot.merkle.verifies, true);
  assert.equal(snapshot.neovm.result, 1);
  assert.equal(snapshot.riscv.result, 12);
  assert.ok(snapshot.proofEstimate.constraints > 0);
});
