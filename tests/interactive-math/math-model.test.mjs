import test from 'node:test';
import assert from 'node:assert/strict';

import {
  FIELD_PRIME,
  buildFieldSnapshot,
  buildFieldVisualization,
  buildJourneyVisualization,
  buildMerkleTree,
  buildMerkleVisualization,
  buildProofTranscript,
  buildTraceConstraintMatrix,
  buildZkpMathVisualization,
  estimateProofPipeline,
  evaluatePolynomial,
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
  vanishingPolynomial,
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

test('journey visualization maps the full execution proof route with active stages', () => {
  const journey = buildJourneyVisualization({ activeLessonId: 'arithmetization', neoVmStep: 3, riscvStep: 2 });

  assert.deepEqual(
    journey.stages.map((stage) => stage.id),
    ['neovm', 'riscv', 'trace', 'constraints', 'commitment', 'proof', 'settlement'],
  );
  assert.equal(journey.activeStageId, 'constraints');
  assert.equal(journey.packet.stageId, 'constraints');
  assert.ok(journey.stages.filter((stage) => stage.state === 'done').length >= 3);
  assert.match(journey.packet.payload, /trace|constraint/i);
});

test('field visualization exposes ring coordinates, addition arc, multiplication hops, and inverse link', () => {
  const visual = buildFieldVisualization(5, 9, FIELD_PRIME);

  assert.equal(visual.points.length, FIELD_PRIME);
  assert.equal(visual.addition.start, 5);
  assert.equal(visual.addition.end, 14);
  assert.equal(visual.multiplication.end, 11);
  assert.equal(visual.inverse.value, 7);
  assert.equal(visual.inverse.check, 1);
  assert.ok(visual.points.every((point) => Number.isFinite(point.x) && Number.isFinite(point.y)));
});

test('Merkle visualization marks selected leaf, sibling proof path, and root path', () => {
  const visual = buildMerkleVisualization(['tx:deposit', 'tx:mint', 'tx:swap', 'tx:withdraw'], 2);

  assert.equal(visual.root.hash, visual.levels.at(-1)[0].hash);
  assert.equal(visual.selectedLeaf.leaf, 'tx:swap');
  assert.equal(visual.path.length, 3);
  assert.equal(visual.path.at(-1).role, 'root');
  assert.ok(visual.edges.some((edge) => edge.kind === 'proof'));
  assert.equal(visual.verifies, true);
});

test('trace constraint matrix and proof transcript explain arithmetization and verifier dialogue', () => {
  const neovm = runNeoVmTrace(sampleNeoVmProgram, 4);
  const riscv = runRiscVTrace(sampleRiscVProgram, 3);
  const proofEstimate = estimateProofPipeline({ cycles: 64, memoryOps: 12, publicInputs: 6, aggregation: 2 });
  const matrix = buildTraceConstraintMatrix({ neovmTrace: neovm.trace, riscvTrace: riscv.trace, proofEstimate });
  const transcript = buildProofTranscript({ proofEstimate, publicInputRoot: '52', aggregateCount: 2 });

  assert.deepEqual(matrix.columns, ['row', 'pc', 'operation', 'state delta', 'constraint', 'status']);
  assert.ok(matrix.rows.some((row) => row.constraint.includes('pc')));
  assert.ok(matrix.rows.some((row) => row.status === 'active'));
  assert.deepEqual(transcript.steps.map((step) => step.actor), ['L2 executor', 'Prover', 'Verifier', 'NeoHub']);
  assert.equal(transcript.publicInputs.root, '52');
});

test('ZKP math visualization explains domain vanishing, quotient checks, and transcript challenges', () => {
  const proofEstimate = estimateProofPipeline({ cycles: 96, memoryOps: 16, publicInputs: 5, aggregation: 3 });
  const visual = buildZkpMathVisualization({ proofEstimate, publicInputRoot: '52', prime: FIELD_PRIME });

  assert.equal(visual.domain.length, 8);
  assert.equal(new Set(visual.domain.map((point) => point.x)).size, visual.domain.length);
  assert.ok(visual.domain.every((point) => vanishingPolynomial(point.x, visual.domainSize, FIELD_PRIME) === 0));
  assert.ok(visual.gates.length >= 4);
  assert.ok(visual.gates.every((gate) => gate.residual === 0));
  assert.equal(visual.challenge.inDomain, false);
  assert.equal(visual.verifierCheck.pass, true);
  assert.equal(
    visual.verifierCheck.left,
    fieldMul(
      evaluatePolynomial(visual.quotient.coefficients, visual.challenge.zeta, FIELD_PRIME),
      vanishingPolynomial(visual.challenge.zeta, visual.domainSize, FIELD_PRIME),
      FIELD_PRIME,
    ),
  );
  assert.ok(visual.transcript.some((step) => step.action.includes('challenge')));
  assert.ok(visual.publicInputs.root === '52');
});
