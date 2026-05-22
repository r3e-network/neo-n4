import {
  FIELD_PRIME,
  buildMerkleTree,
  lessonOrder,
  listLessons,
  makeLearningSnapshot,
  merkleProof,
  pipelineStages,
  sampleNeoVmProgram,
  sampleRiscVProgram,
  verifyMerkleProof,
} from './mathModel.js';

const copy = {
  zh: {
    title: 'Neo N4 数学动态教程',
    subtitle: '用可交互模型学习 NeoVM 语义、NeoVM2/RISC-V 执行、zkVM 证明、有限域、承诺、聚合和 L1 验证。',
    railTitle: '学习目标',
    railBody: '先理解“要证明什么”，再看它如何被执行轨迹、代数约束和 L1 验证器逐层压缩。',
    lessonEyebrow: '当前章节',
    coreClaim: '核心声明',
    pipelineTitle: '从执行到证明的完整链路',
    pipelineBody: '这条链路展示 Neo N4 如何把 L2 状态转换变成 L1 可验证的 public inputs。',
    fieldTitle: '有限域算术实验',
    fieldBody: 'ZK 约束通常工作在质数域上。拖动 a 和 b，观察加法、乘法和逆元如何取模。',
    commitTitle: '承诺与 Merkle Root',
    commitBody: '状态、交易和消息不会全部放进 L1，而是绑定成 root；任何叶子变化都会改变 root。',
    neovmTitle: 'NeoVM 栈语义 Stepper',
    neovmBody: '逐步执行一个小程序，看 opcode 如何改变 stack、pc 和 gas，并形成可证明的 transition。',
    riscvTitle: 'RISC-V 周期 Stepper',
    riscvBody: 'RISC-V 把执行拆成寄存器、内存和 pc 的小步转换，适合 zkVM 和 PolkaVM 类宿主处理。',
    proofTitle: 'zkVM 证明成本直觉',
    proofBody: '这不是 benchmark，而是教学模型：trace 越长、memory access 越多，约束和证明工作越多。',
    principleTitle: '本章技术原则',
    conceptNote: '阅读方式：先看左侧章节，再拖动实验控件；每个数字都是为了说明结构，不代表生产参数。',
    stepAll: '全部前进一步',
    reset: '重置教程',
    resetShort: '重置',
    step: 'Step',
    cycle: 'Cycle',
    tamper: '篡改叶子',
    teachingModel: '教学模型',
    cycles: 'cycles',
    memoryOps: 'memory ops',
    publicInputs: 'public inputs',
    aggregation: 'aggregation',
    selectedLeaf: '选中叶子',
    merkleRoot: 'Merkle root',
    proofValid: '路径验证',
    traceRows: 'trace rows',
    constraints: 'constraints',
    commitments: 'commitments',
    verifierChecks: 'verifier checks',
    proofBytes: 'proof bytes',
    gas: 'gas',
    result: 'result',
    halted: 'halted',
  },
  en: {
    title: 'Neo N4 Math Lab',
    subtitle: 'Interactive models for NeoVM semantics, NeoVM2/RISC-V execution, zkVM proving, finite fields, commitments, aggregation, and L1 verification.',
    railTitle: 'Learning goal',
    railBody: 'Understand what is being proved before studying traces, algebraic constraints, and the L1 verifier boundary.',
    lessonEyebrow: 'Current lesson',
    coreClaim: 'Core claim',
    pipelineTitle: 'Execution-to-proof pipeline',
    pipelineBody: 'This pipeline shows how Neo N4 turns an L2 transition into L1-verifiable public inputs.',
    fieldTitle: 'Finite-field arithmetic lab',
    fieldBody: 'ZK constraints usually live over a prime field. Move a and b to see modular addition, multiplication, and inverse checks.',
    commitTitle: 'Commitments and Merkle roots',
    commitBody: 'State, transactions, and messages are bound into roots; one changed leaf changes the root.',
    neovmTitle: 'NeoVM stack semantics stepper',
    neovmBody: 'Step through a tiny program and inspect how each opcode changes stack, pc, gas, and transition constraints.',
    riscvTitle: 'RISC-V cycle stepper',
    riscvBody: 'RISC-V execution decomposes into register, memory, and pc transitions that zkVMs and PolkaVM-style hosts can handle.',
    proofTitle: 'zkVM proof cost intuition',
    proofBody: 'This is a teaching model, not a benchmark: longer traces and more memory accesses increase constraints and proving work.',
    principleTitle: 'Technical principle',
    conceptNote: 'How to read this page: pick a lesson, then move the controls. Numbers are structural teaching values, not production parameters.',
    stepAll: 'Step all',
    reset: 'Reset lab',
    resetShort: 'Reset',
    step: 'Step',
    cycle: 'Cycle',
    tamper: 'Tamper leaf',
    teachingModel: 'teaching model',
    cycles: 'cycles',
    memoryOps: 'memory ops',
    publicInputs: 'public inputs',
    aggregation: 'aggregation',
    selectedLeaf: 'selected leaf',
    merkleRoot: 'Merkle root',
    proofValid: 'path verifies',
    traceRows: 'trace rows',
    constraints: 'constraints',
    commitments: 'commitments',
    verifierChecks: 'verifier checks',
    proofBytes: 'proof bytes',
    gas: 'gas',
    result: 'result',
    halted: 'halted',
  },
};

const els = {
  nav: document.querySelector('[data-lesson-nav]'),
  language: document.querySelector('[data-language]'),
  lessonTitle: document.querySelector('[data-lesson-title]'),
  lessonSummary: document.querySelector('[data-lesson-summary]'),
  lessonFormula: document.querySelector('[data-lesson-formula]'),
  lessonPrinciple: document.querySelector('[data-lesson-principle]'),
  checkpoints: document.querySelector('[data-checkpoints]'),
  pipeline: document.querySelector('[data-pipeline]'),
  fieldA: document.querySelector('[data-field-a]'),
  fieldB: document.querySelector('[data-field-b]'),
  fieldPrime: document.querySelector('[data-field-prime]'),
  fieldRuler: document.querySelector('[data-field-ruler]'),
  fieldOutput: document.querySelector('[data-field-output]'),
  merkleTree: document.querySelector('[data-merkle-tree]'),
  merkleOutput: document.querySelector('[data-merkle-output]'),
  neovmProgram: document.querySelector('[data-neovm-program]'),
  neovmStack: document.querySelector('[data-neovm-stack]'),
  neovmTrace: document.querySelector('[data-neovm-trace]'),
  riscvRegisters: document.querySelector('[data-riscv-registers]'),
  riscvTrace: document.querySelector('[data-riscv-trace]'),
  proofCycles: document.querySelector('[data-proof-cycles]'),
  proofMemory: document.querySelector('[data-proof-memory]'),
  proofInputs: document.querySelector('[data-proof-inputs]'),
  proofAggregation: document.querySelector('[data-proof-aggregation]'),
  proofOutput: document.querySelector('[data-proof-output]'),
};

let state = {
  language: 'zh',
  activeLessonId: 'stack-map',
  fieldA: 5,
  fieldB: 9,
  merkleLeaves: ['tx:deposit', 'tx:mint', 'tx:swap', 'tx:withdraw'],
  merkleIndex: 1,
  neoVmStep: 0,
  riscvStep: 0,
  proofCycles: 64,
  proofMemoryOps: 12,
  proofPublicInputs: 6,
  proofAggregation: 2,
};

renderAll();

els.language.addEventListener('change', () => {
  state.language = els.language.value;
  document.documentElement.lang = state.language;
  renderAll();
});

for (const input of [els.fieldA, els.fieldB, els.proofCycles, els.proofMemory, els.proofInputs, els.proofAggregation]) {
  input.addEventListener('input', () => {
    syncStateFromInputs();
    renderDynamic();
  });
}

document.querySelector('[data-action="step-all"]').addEventListener('click', () => {
  state.neoVmStep = Math.min(sampleNeoVmProgram.length, state.neoVmStep + 1);
  state.riscvStep = Math.min(sampleRiscVProgram.length, state.riscvStep + 1);
  renderDynamic();
});

document.querySelector('[data-action="reset"]').addEventListener('click', () => {
  state = {
    ...state,
    fieldA: 5,
    fieldB: 9,
    merkleLeaves: ['tx:deposit', 'tx:mint', 'tx:swap', 'tx:withdraw'],
    merkleIndex: 1,
    neoVmStep: 0,
    riscvStep: 0,
    proofCycles: 64,
    proofMemoryOps: 12,
    proofPublicInputs: 6,
    proofAggregation: 2,
  };
  syncInputsFromState();
  renderAll();
});

document.querySelector('[data-action="step-neovm"]').addEventListener('click', () => {
  state.neoVmStep = Math.min(sampleNeoVmProgram.length, state.neoVmStep + 1);
  renderDynamic();
});

document.querySelector('[data-action="reset-neovm"]').addEventListener('click', () => {
  state.neoVmStep = 0;
  renderDynamic();
});

document.querySelector('[data-action="step-riscv"]').addEventListener('click', () => {
  state.riscvStep = Math.min(sampleRiscVProgram.length, state.riscvStep + 1);
  renderDynamic();
});

document.querySelector('[data-action="reset-riscv"]').addEventListener('click', () => {
  state.riscvStep = 0;
  renderDynamic();
});

document.querySelector('[data-action="tamper"]').addEventListener('click', () => {
  const next = [...state.merkleLeaves];
  next[state.merkleIndex] = next[state.merkleIndex].endsWith(':tampered')
    ? next[state.merkleIndex].replace(':tampered', '')
    : `${next[state.merkleIndex]}:tampered`;
  state.merkleLeaves = next;
  renderDynamic();
});

function renderAll() {
  renderCopy();
  renderNavigation();
  renderPipeline();
  renderStaticPrograms();
  renderDynamic();
}

function renderCopy() {
  const dict = copy[state.language];
  for (const el of document.querySelectorAll('[data-i18n]')) {
    el.textContent = dict[el.dataset.i18n] ?? el.dataset.i18n;
  }
}

function renderNavigation() {
  const lessons = listLessons();
  els.nav.replaceChildren(...lessons.map((lesson, index) => {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = `lesson-button ${lesson.id === state.activeLessonId ? 'is-active' : ''}`;
    button.dataset.lessonId = lesson.id;
    button.innerHTML = `
      <span>${String(index + 1).padStart(2, '0')}</span>
      <strong>${state.language === 'zh' ? lesson.zhTitle : lesson.title}</strong>
      <small>${lesson.formula}</small>
    `;
    button.addEventListener('click', () => {
      state.activeLessonId = lesson.id;
      renderAll();
    });
    return button;
  }));
}

function renderPipeline() {
  const activeIndex = Math.max(0, lessonOrder.indexOf(state.activeLessonId));
  els.pipeline.replaceChildren(...pipelineStages.map((stage, index) => {
    const item = document.createElement('section');
    item.className = `pipeline-stage ${index <= activeIndex ? 'is-lit' : ''}`;
    item.innerHTML = `
      <span>${String(index + 1).padStart(2, '0')}</span>
      <strong>${state.language === 'zh' ? stage.zhLabel : stage.label}</strong>
      <small>${state.language === 'zh' ? stage.zhDetail : stage.detail}</small>
    `;
    return item;
  }));
}

function renderStaticPrograms() {
  els.neovmProgram.replaceChildren(...sampleNeoVmProgram.map((instruction, index) => {
    const row = document.createElement('li');
    row.className = index < state.neoVmStep ? 'is-done' : index === state.neoVmStep ? 'is-active' : '';
    row.innerHTML = `<code>${index}: ${instruction.op}${instruction.value === undefined ? '' : ` ${instruction.value}`}</code><span>${instruction.doc}</span>`;
    return row;
  }));
}

function renderDynamic() {
  const snapshot = makeLearningSnapshot(state);
  const lesson = snapshot.activeLesson;
  els.lessonTitle.textContent = state.language === 'zh' ? lesson.zhTitle : lesson.title;
  els.lessonSummary.textContent = state.language === 'zh' ? lesson.zhSummary : lesson.summary;
  els.lessonFormula.textContent = lesson.formula;
  els.lessonPrinciple.textContent = state.language === 'zh' ? lesson.zhPrinciple : lesson.principle;
  els.checkpoints.replaceChildren(...(state.language === 'zh' ? lesson.zhCheckpoints : lesson.checkpoints).map((item) => {
    const li = document.createElement('li');
    li.textContent = item;
    return li;
  }));

  renderField(snapshot);
  renderMerkle(snapshot);
  renderNeoVm(snapshot);
  renderRiscV(snapshot);
  renderProof(snapshot);
  renderPipeline();
  renderStaticPrograms();
}

function renderField(snapshot) {
  const dict = copy[state.language];
  els.fieldPrime.textContent = `F_${FIELD_PRIME}`;
  els.fieldRuler.replaceChildren(...snapshot.field.ruler.map((value) => {
    const item = document.createElement('span');
    item.className = value === snapshot.field.a ? 'is-a' : value === snapshot.field.b ? 'is-b' : '';
    item.textContent = value;
    return item;
  }));
  renderDefinitionList(els.fieldOutput, [
    ['a', String(snapshot.field.a)],
    ['b', String(snapshot.field.b)],
    ['a + b', `${snapshot.field.add} mod ${FIELD_PRIME}`],
    ['a * b', `${snapshot.field.mul} mod ${FIELD_PRIME}`],
    ['a^-1', snapshot.field.inverse === null ? 'undefined' : String(snapshot.field.inverse)],
    ['a * a^-1', snapshot.field.inverseCheck === null ? 'n/a' : String(snapshot.field.inverseCheck)],
    ['(a-b)^2', String(snapshot.field.equalityPolynomial)],
    [dict.proofValid, snapshot.field.equalityPolynomial === 0 ? 'a == b' : 'a != b'],
  ]);
}

function renderMerkle(snapshot) {
  const { tree } = snapshot.merkle;
  const rows = tree.levels.map((level, levelIndex) => {
    const group = document.createElement('div');
    group.className = 'merkle-level';
    group.dataset.level = levelIndex;
    group.replaceChildren(...level.map((hash, index) => {
      const node = document.createElement('button');
      node.type = 'button';
      node.className = levelIndex === 0 && index === state.merkleIndex ? 'is-selected' : '';
      node.textContent = String(hash);
      if (levelIndex === 0) {
        node.title = tree.leaves[index];
        node.addEventListener('click', () => {
          state.merkleIndex = index;
          renderDynamic();
        });
      }
      return node;
    }));
    return group;
  });
  els.merkleTree.replaceChildren(...rows);

  const proof = merkleProof(buildMerkleTree(state.merkleLeaves), state.merkleIndex);
  const verifies = verifyMerkleProof(state.merkleLeaves[state.merkleIndex], proof, tree.root, tree.prime);
  const dict = copy[state.language];
  renderDefinitionList(els.merkleOutput, [
    [dict.selectedLeaf, state.merkleLeaves[state.merkleIndex]],
    [dict.merkleRoot, String(tree.root)],
    [dict.proofValid, verifies ? 'true' : 'false'],
    ['path length', String(proof.length)],
  ]);
}

function renderNeoVm(snapshot) {
  const dict = copy[state.language];
  const stackItems = snapshot.neovm.stack.length === 0 ? ['empty'] : snapshot.neovm.stack.map(String).reverse();
  els.neovmStack.replaceChildren(
    createMetricBlock(dict.gas, String(snapshot.neovm.gas)),
    createMetricBlock(dict.result, String(snapshot.neovm.result ?? 'n/a')),
    createMetricBlock(dict.halted, String(snapshot.neovm.halted)),
    ...stackItems.map((item) => {
      const el = document.createElement('span');
      el.className = 'stack-item';
      el.textContent = item;
      return el;
    }),
  );
  els.neovmTrace.replaceChildren(...snapshot.neovm.trace.map((row) => {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${row.pc}</td>
      <td><code>${row.op}${row.value === undefined ? '' : ` ${row.value}`}</code></td>
      <td>${formatArray(row.before)}</td>
      <td>${formatArray(row.after)}</td>
      <td>${row.constraint}</td>
    `;
    return tr;
  }));
}

function renderRiscV(snapshot) {
  els.riscvRegisters.replaceChildren(...Object.entries(snapshot.riscv.regs).map(([name, value]) => {
    const item = document.createElement('div');
    item.className = name === 'x0' ? 'register zero' : 'register';
    item.innerHTML = `<span>${name}</span><strong>${value}</strong>`;
    return item;
  }));
  els.riscvTrace.replaceChildren(...snapshot.riscv.trace.map((row) => {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${row.cycle}</td>
      <td>${row.pc}</td>
      <td><code>${row.instruction}</code></td>
      <td>${row.constraint}</td>
    `;
    return tr;
  }));
}

function renderProof(snapshot) {
  const dict = copy[state.language];
  const estimate = snapshot.proofEstimate;
  renderDefinitionList(els.proofOutput, [
    [dict.traceRows, String(estimate.traceRows)],
    [dict.constraints, String(estimate.constraints)],
    [dict.commitments, String(estimate.commitmentCount)],
    [dict.verifierChecks, String(estimate.verifierChecks)],
    [dict.proofBytes, `${estimate.proofBytes} B`],
    ['note', estimate.note],
  ]);
}

function syncStateFromInputs() {
  state.fieldA = Number(els.fieldA.value);
  state.fieldB = Number(els.fieldB.value);
  state.proofCycles = Number(els.proofCycles.value);
  state.proofMemoryOps = Number(els.proofMemory.value);
  state.proofPublicInputs = Number(els.proofInputs.value);
  state.proofAggregation = Number(els.proofAggregation.value);
}

function syncInputsFromState() {
  els.fieldA.value = String(state.fieldA);
  els.fieldB.value = String(state.fieldB);
  els.proofCycles.value = String(state.proofCycles);
  els.proofMemory.value = String(state.proofMemoryOps);
  els.proofInputs.value = String(state.proofPublicInputs);
  els.proofAggregation.value = String(state.proofAggregation);
}

function renderDefinitionList(target, rows) {
  target.replaceChildren(...rows.flatMap(([label, value]) => {
    const dt = document.createElement('dt');
    dt.textContent = label;
    const dd = document.createElement('dd');
    dd.textContent = value;
    return [dt, dd];
  }));
}

function createMetricBlock(label, value) {
  const block = document.createElement('div');
  block.className = 'stack-metric';
  block.innerHTML = `<span>${label}</span><strong>${value}</strong>`;
  return block;
}

function formatArray(values) {
  return `[${values.join(', ')}]`;
}
