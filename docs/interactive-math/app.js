import {
  FIELD_PRIME,
  buildMerkleTree,
  getLessonIdForJourneyStage,
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
    mathTheaterTitle: 'Proof Journey 动态剧场',
    mathTheaterBody: '观察一个执行声明如何从 opcode 和 RISC-V 周期，逐步变成 trace、约束、承诺、证明和 L1 验证。',
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
    constraintMatrixTitle: 'Trace -> Constraint Matrix',
    proofTranscriptTitle: 'Prover / Verifier 对话',
    zkpMathTitle: 'ZKP 数学原理动画',
    zkpMathBody: '把 witness、评价域、约束多项式、vanishing polynomial、quotient 和随机挑战连成一个可观察的验证流程。',
    clickToExplore: '点击图中的节点、卡片或步骤可以切换讲解焦点。',
    fieldPickA: '点击圆环上的数字来设置 a。',
    fieldPickB: '点击圆环上的数字来设置 b。',
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
    mathTheaterTitle: 'Proof Journey Theater',
    mathTheaterBody: 'Watch an execution claim move from opcode and RISC-V cycles into traces, constraints, commitments, proofs, and L1 verification.',
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
    constraintMatrixTitle: 'Trace -> Constraint Matrix',
    proofTranscriptTitle: 'Prover / Verifier Dialogue',
    zkpMathTitle: 'ZKP Math Animation',
    zkpMathBody: 'Connect witness values, the evaluation domain, constraint polynomials, the vanishing polynomial, quotient openings, and random challenges in one verifier flow.',
    clickToExplore: 'Click diagram nodes, cards, or steps to change the explanation focus.',
    fieldPickA: 'Click a number on the field clock to set a.',
    fieldPickB: 'Click a number on the field clock to set b.',
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
  journeyCanvas: document.querySelector('[data-journey-canvas]'),
  journeyNarration: document.querySelector('[data-journey-narration]'),
  fieldA: document.querySelector('[data-field-a]'),
  fieldB: document.querySelector('[data-field-b]'),
  fieldPickState: document.querySelector('[data-field-pick-state]'),
  fieldPrime: document.querySelector('[data-field-prime]'),
  fieldClock: document.querySelector('[data-field-clock]'),
  fieldRuler: document.querySelector('[data-field-ruler]'),
  fieldOutput: document.querySelector('[data-field-output]'),
  merkleTree: document.querySelector('[data-merkle-tree]'),
  merkleProofPath: document.querySelector('[data-merkle-proof-path]'),
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
  constraintMatrix: document.querySelector('[data-constraint-matrix]'),
  proofTranscript: document.querySelector('[data-proof-transcript]'),
  proofOutput: document.querySelector('[data-proof-output]'),
  proofLiveValues: document.querySelector('[data-proof-live-values]'),
  zkpCanvas: document.querySelector('[data-zkp-canvas]'),
  zkpEquation: document.querySelector('[data-zkp-equation]'),
  zkpFocus: document.querySelector('[data-zkp-focus]'),
  zkpGates: document.querySelector('[data-zkp-gates]'),
  zkpTranscript: document.querySelector('[data-zkp-transcript]'),
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
  nextFieldTarget: 'a',
  zkpFocusId: 'layer:quotient',
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
    nextFieldTarget: 'a',
    zkpFocusId: 'layer:quotient',
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
    makeClickable(row, () => {
      state.activeLessonId = 'neovm';
      state.neoVmStep = index + 1;
      renderDynamic();
    });
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

  renderJourney(snapshot);
  renderField(snapshot);
  renderMerkle(snapshot);
  renderNeoVm(snapshot);
  renderRiscV(snapshot);
  renderProof(snapshot);
  renderZkpMath(snapshot.zkpMath);
  renderPipeline();
  renderStaticPrograms();
}

function renderJourney(snapshot) {
  const { journey } = snapshot;
  const stageById = Object.fromEntries(journey.stages.map((stage) => [stage.id, stage]));
  const svgEl = svg('svg', {
    viewBox: '0 0 100 100',
    role: 'img',
    'aria-label': 'Execution to proof journey',
  });
  const defs = svg('defs');
  const marker = svg('marker', {
    id: 'journey-arrow',
    markerWidth: '7',
    markerHeight: '7',
    refX: '6',
    refY: '3.5',
    orient: 'auto',
  });
  marker.append(svg('path', { d: 'M0,0 L7,3.5 L0,7 Z', class: 'journey-arrow' }));
  defs.append(marker);
  svgEl.append(defs);

  for (const link of journey.links) {
    const from = stageById[link.from].position;
    const to = stageById[link.to].position;
    const midX = (from.x + to.x) / 2;
    const midY = (from.y + to.y) / 2 - 12;
    svgEl.append(svg('path', {
      class: `journey-link ${link.state}`,
      d: `M ${from.x} ${from.y} Q ${midX} ${midY} ${to.x} ${to.y}`,
      'marker-end': 'url(#journey-arrow)',
    }));
  }

  for (const stage of journey.stages) {
    const group = svg('g', {
      class: `journey-node ${stage.state}`,
      transform: `translate(${stage.position.x} ${stage.position.y})`,
      'data-journey-stage': stage.id,
    });
    group.append(
      svg('circle', { r: stage.state === 'active' ? 5.9 : 4.9 }),
      svg('text', { x: 0, y: -8.2, 'text-anchor': 'middle' }, journeyCanvasLabel(stage)),
    );
    makeClickable(group, () => {
      state.activeLessonId = getLessonIdForJourneyStage(stage.id);
      renderAll();
    });
    svgEl.append(group);
  }

  const packet = journey.packet;
  const packetGroup = svg('g', {
    class: 'journey-packet',
    transform: `translate(${packet.x} ${packet.y})`,
  });
  packetGroup.append(
    svg('circle', { r: 2.7 }),
    svg('text', { x: 0, y: 5.8, 'text-anchor': 'middle' }, 'claim'),
  );
  svgEl.append(packetGroup);
  els.journeyCanvas.replaceChildren(svgEl);

  els.journeyNarration.replaceChildren(...journey.stages.map((stage, index) => {
    const item = document.createElement('section');
    item.className = `journey-note ${stage.state}`;
    item.innerHTML = `
      <span>${String(index + 1).padStart(2, '0')}</span>
      <strong>${state.language === 'zh' ? stage.zhLabel : stage.label}</strong>
      <small>${stage.payload}</small>
    `;
    makeClickable(item, () => {
      state.activeLessonId = getLessonIdForJourneyStage(stage.id);
      renderAll();
    });
    return item;
  }));
}

function renderField(snapshot) {
  const dict = copy[state.language];
  els.fieldPickState.textContent = state.nextFieldTarget === 'a' ? dict.fieldPickA : dict.fieldPickB;
  renderFieldClock(snapshot.fieldVisual);
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

function renderFieldClock(fieldVisual) {
  const pointByValue = Object.fromEntries(fieldVisual.points.map((point) => [point.value, point]));
  const svgEl = svg('svg', {
    viewBox: '0 0 100 100',
    role: 'img',
    'aria-label': 'Finite-field modular arithmetic clock',
  });
  svgEl.append(svg('circle', {
    cx: fieldVisual.center.x,
    cy: fieldVisual.center.y,
    r: fieldVisual.radius,
    class: 'field-ring',
  }));
  for (const hop of fieldVisual.multiplication.hops) {
    const from = pointByValue[hop.from];
    const to = pointByValue[hop.to];
    svgEl.append(svg('line', {
      x1: from.x,
      y1: from.y,
      x2: to.x,
      y2: to.y,
      class: 'field-hop',
    }));
  }
  const start = pointByValue[fieldVisual.addition.start];
  const end = pointByValue[fieldVisual.addition.end];
  svgEl.append(svg('path', {
    d: `M ${start.x} ${start.y} A ${fieldVisual.radius} ${fieldVisual.radius} 0 0 1 ${end.x} ${end.y}`,
    class: 'field-add-arc',
  }));
  if (fieldVisual.inverse.value !== null) {
    const inverse = pointByValue[fieldVisual.inverse.value];
    svgEl.append(svg('line', {
      x1: start.x,
      y1: start.y,
      x2: inverse.x,
      y2: inverse.y,
      class: 'field-inverse-link',
    }));
  }
  for (const point of fieldVisual.points) {
    const group = svg('g', { class: `field-point ${point.role}`, transform: `translate(${point.x} ${point.y})` });
    group.append(svg('circle', { r: point.role === 'field' ? 2 : 3.5 }), svg('text', { y: 0.9, 'text-anchor': 'middle' }, String(point.value)));
    makeClickable(group, () => {
      if (state.nextFieldTarget === 'a') {
        state.fieldA = point.value;
        state.nextFieldTarget = 'b';
      } else {
        state.fieldB = point.value;
        state.nextFieldTarget = 'a';
      }
      syncInputsFromState();
      renderDynamic();
    });
    svgEl.append(group);
  }
  svgEl.append(
    svg('text', { x: 50, y: 51, 'text-anchor': 'middle', class: 'field-clock-label' }, fieldVisual.addition.equation),
    svg('text', { x: 50, y: 58, 'text-anchor': 'middle', class: 'field-clock-label muted' }, fieldVisual.inverse.equation),
  );
  els.fieldClock.replaceChildren(svgEl);
}

function renderMerkle(snapshot) {
  const { tree, visual } = snapshot.merkle;
  const pathKeys = new Set(visual.path.map((node) => `${node.level}:${node.index}`));
  const rows = tree.levels.map((level, levelIndex) => {
    const group = document.createElement('div');
    group.className = 'merkle-level';
    group.dataset.level = levelIndex;
    group.replaceChildren(...level.map((hash, index) => {
      const node = document.createElement('button');
      node.type = 'button';
      node.className = [
        levelIndex === 0 && index === state.merkleIndex ? 'is-selected' : '',
        pathKeys.has(`${levelIndex}:${index}`) ? 'is-proof-path' : '',
      ].filter(Boolean).join(' ');
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
  els.merkleProofPath.replaceChildren(...visual.path.map((node) => {
    const item = document.createElement('span');
    item.className = node.role;
    item.textContent = `${node.role}: ${node.hash}`;
    return item;
  }));

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
  renderConstraintMatrix(snapshot.constraintMatrix);
  renderProofTranscript(snapshot.proofTranscript);
  els.proofLiveValues.replaceChildren(...[
    [dict.cycles, state.proofCycles],
    [dict.memoryOps, state.proofMemoryOps],
    [dict.publicInputs, state.proofPublicInputs],
    [dict.aggregation, state.proofAggregation],
  ].map(([label, value]) => {
    const item = document.createElement('span');
    item.textContent = `${label}: ${value}`;
    return item;
  }));
  renderDefinitionList(els.proofOutput, [
    [dict.traceRows, String(estimate.traceRows)],
    [dict.constraints, String(estimate.constraints)],
    [dict.commitments, String(estimate.commitmentCount)],
    [dict.verifierChecks, String(estimate.verifierChecks)],
    [dict.proofBytes, `${estimate.proofBytes} B`],
    ['note', estimate.note],
  ]);
}

function renderZkpMath(zkp) {
  const isZh = state.language === 'zh';
  const svgEl = svg('svg', {
    viewBox: '0 0 100 70',
    role: 'img',
    'aria-label': 'ZKP polynomial and quotient animation',
  });
  const defs = svg('defs');
  const marker = svg('marker', {
    id: 'zkp-arrow',
    markerWidth: '7',
    markerHeight: '7',
    refX: '6',
    refY: '3.5',
    orient: 'auto',
  });
  marker.append(svg('path', { d: 'M0,0 L7,3.5 L0,7 Z', class: 'zkp-arrow' }));
  defs.append(marker);
  svgEl.append(defs);

  for (let index = 0; index < zkp.layers.length - 1; index += 1) {
    const from = zkp.layers[index];
    const to = zkp.layers[index + 1];
    svgEl.append(svg('path', {
      class: 'zkp-flow-link',
      d: `M ${from.x + 3.8} ${from.y} C ${from.x + 11} ${from.y - 10}, ${to.x - 11} ${to.y + 10}, ${to.x - 3.8} ${to.y}`,
      'marker-end': 'url(#zkp-arrow)',
    }));
  }

  for (const layer of zkp.layers) {
    const group = svg('g', {
      class: `zkp-layer-node ${layer.state}`,
      transform: `translate(${layer.x} ${layer.y})`,
      'data-zkp-focus-target': `layer:${layer.id}`,
    });
    group.append(
      svg('circle', { r: layer.state === 'active' ? 4.9 : 4.1 }),
      svg('text', { x: 0, y: -7.2, 'text-anchor': 'middle' }, isZh ? layer.zhLabel : layer.label),
    );
    makeClickable(group, () => {
      state.zkpFocusId = `layer:${layer.id}`;
      state.activeLessonId = layer.id === 'constraints' ? 'arithmetization' : 'zk-proofs';
      renderDynamic();
    });
    svgEl.append(group);
  }

  const domainGroup = svg('g', { class: 'zkp-domain' });
  domainGroup.append(
    svg('circle', { cx: 22, cy: 50, r: 13.8, class: 'zkp-domain-ring' }),
    svg('text', { x: 22, y: 31, 'text-anchor': 'middle', class: 'zkp-svg-title' }, 'H: Z_H(x)=0'),
  );
  for (const point of zkp.domain) {
    const x = 22 + (point.px - 50) * 0.34;
    const y = 50 + (point.py - 50) * 0.34;
    domainGroup.append(
      svg('circle', { cx: roundSvg(x), cy: roundSvg(y), r: 1.7, class: 'zkp-domain-point' }),
      svg('text', { x: roundSvg(x), y: roundSvg(y + 4.4), 'text-anchor': 'middle' }, String(point.x)),
    );
  }
  svgEl.append(domainGroup);

  const sampleGroup = svg('g', { class: 'zkp-samples' });
  const sampleBaseY = 58;
  zkp.quotient.samples.slice(0, 8).forEach((sample, index) => {
    const x = 43 + index * 3.3;
    sampleGroup.append(svg('line', {
      x1: roundSvg(x),
      y1: sampleBaseY,
      x2: roundSvg(x),
      y2: roundSvg(sampleBaseY - Math.max(1.8, sample.c + 1)),
      class: 'zkp-zero-bar',
    }));
  });
  sampleGroup.append(
    svg('text', { x: 55, y: 64, 'text-anchor': 'middle', class: 'zkp-svg-note' }, 'C(x)=0 on H'),
    svg('circle', { cx: 78, cy: 51, r: 3.1, class: 'zkp-zeta-point' }),
    svg('text', { x: 78, y: 44, 'text-anchor': 'middle', class: 'zkp-svg-title' }, `ζ=${zkp.challenge.zeta}`),
    svg('text', { x: 78, y: 58, 'text-anchor': 'middle', class: 'zkp-svg-note' }, `C(ζ)=${zkp.quotient.cAtZeta}`),
  );
  svgEl.append(sampleGroup);

  svgEl.append(
    svg('rect', { x: 40, y: 3.5, width: 45, height: 8.8, rx: 2.8, class: 'zkp-equation-ribbon' }),
    svg('text', { x: 62.5, y: 9.6, 'text-anchor': 'middle', class: 'zkp-equation-text' }, zkp.verifierCheck.equation),
  );
  els.zkpCanvas.replaceChildren(svgEl);

  renderDefinitionList(els.zkpEquation, [
    ['public root', zkp.publicInputs.root],
    ['domain', `|H| = ${zkp.domainSize}, F_${zkp.prime}`],
    ['Z_H(ζ)', String(zkp.quotient.zAtZeta)],
    ['Q(ζ)', String(zkp.quotient.qAtZeta)],
    ['C(ζ)', String(zkp.quotient.cAtZeta)],
    ['verifier', `${zkp.verifierCheck.left} == ${zkp.verifierCheck.right} (${zkp.verifierCheck.pass ? 'pass' : 'fail'})`],
  ]);
  els.zkpFocus.innerHTML = `
    <strong>${isZh ? zkp.focus.zhLabel : zkp.focus.label}</strong>
    ${zkp.focus.equation ? `<code>${zkp.focus.equation}</code>` : ''}
    <small>${isZh ? zkp.focus.zhDetail : zkp.focus.detail}</small>
    <em>${copy[state.language].clickToExplore}</em>
  `;

  els.zkpGates.replaceChildren(...zkp.gates.map((gate) => {
    const item = document.createElement('section');
    item.className = `zkp-gate ${gate.residual === 0 ? 'valid' : 'invalid'} ${gate.state}`;
    item.innerHTML = `
      <strong>${isZh ? gate.zhLabel : gate.label}</strong>
      <code>${gate.equation}</code>
      <small>${isZh ? gate.zhRole : gate.role}; residual = ${gate.residual}</small>
    `;
    makeClickable(item, () => {
      state.zkpFocusId = `gate:${gate.id}`;
      state.activeLessonId = gate.id === 'public-input' ? 'n4-security' : 'arithmetization';
      renderDynamic();
    });
    return item;
  }));

  els.zkpTranscript.replaceChildren(...zkp.transcript.map((step, index) => {
    const item = document.createElement('section');
    item.className = `zkp-transcript-step ${step.state}`;
    item.innerHTML = `
      <span>${String(index + 1).padStart(2, '0')}</span>
      <div>
        <strong>${step.actor}</strong>
        <em>${step.action}</em>
        <small>${step.detail}</small>
      </div>
    `;
    makeClickable(item, () => {
      state.zkpFocusId = `transcript:${step.id}`;
      state.activeLessonId = 'zk-proofs';
      renderDynamic();
    });
    return item;
  }), createSoundnessNote(isZh ? zkp.soundness.zhStatement : zkp.soundness.statement));
}

function renderConstraintMatrix(matrix) {
  const rows = matrix.rows.length === 0
    ? [
      { row: 'N0', operation: 'NeoVM step', constraint: 'Step NeoVM to materialize opcode constraints.', status: 'pending' },
      { row: 'R0', operation: 'RISC-V cycle', constraint: 'Step RISC-V to materialize register and memory constraints.', status: 'pending' },
    ]
    : matrix.rows.slice(-6);
  els.constraintMatrix.replaceChildren(...rows.map((row) => {
    const item = document.createElement('div');
    item.className = `constraint-row ${row.status}`;
    item.innerHTML = `
      <span>${row.row}</span>
      <strong>${row.operation}</strong>
      <small>${row.constraint}</small>
    `;
    return item;
  }));
}

function renderProofTranscript(transcript) {
  els.proofTranscript.replaceChildren(...transcript.steps.map((step, index) => {
    const item = document.createElement('section');
    item.className = 'proof-step';
    item.innerHTML = `
      <span>${String(index + 1).padStart(2, '0')}</span>
      <div>
        <strong>${step.actor}</strong>
        <em>${step.action}</em>
        <small>${step.payload}</small>
      </div>
    `;
    return item;
  }));
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

function createSoundnessNote(text) {
  const note = document.createElement('section');
  note.className = 'zkp-soundness-note';
  note.textContent = text;
  return note;
}

function makeClickable(el, handler) {
  el.setAttribute('role', 'button');
  el.setAttribute('tabindex', '0');
  el.addEventListener('click', handler);
  el.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      handler(event);
    }
  });
  return el;
}

function formatArray(values) {
  return `[${values.join(', ')}]`;
}

function roundSvg(value) {
  return Math.round(value * 10) / 10;
}

function journeyCanvasLabel(stage) {
  const zh = {
    neovm: 'NeoVM',
    riscv: 'RISC-V',
    trace: 'Trace',
    constraints: '约束',
    commitment: '承诺',
    proof: 'Proof',
    settlement: 'L1',
  };
  const en = {
    neovm: 'NeoVM',
    riscv: 'RISC-V',
    trace: 'Trace',
    constraints: 'Constraints',
    commitment: 'Commit',
    proof: 'Proof',
    settlement: 'L1',
  };
  return state.language === 'zh' ? zh[stage.id] : en[stage.id];
}

function svg(name, attrs = {}, text = undefined) {
  const el = document.createElementNS('http://www.w3.org/2000/svg', name);
  for (const [key, value] of Object.entries(attrs)) el.setAttribute(key, value);
  if (text !== undefined) el.textContent = text;
  return el;
}
