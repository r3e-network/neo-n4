export const FIELD_PRIME = 17;

export const lessonOrder = [
  'stack-map',
  'finite-fields',
  'commitments',
  'neovm',
  'riscv',
  'arithmetization',
  'zk-proofs',
  'aggregation',
  'n4-security',
];

const lessons = [
  {
    id: 'stack-map',
    title: 'Full Stack Map',
    zhTitle: '全栈地图',
    summary: 'Neo N4 turns program execution into compact public commitments that L1 can verify.',
    zhSummary: 'Neo N4 把程序执行变成 L1 可以验证的紧凑公开承诺。',
    principle: 'Every layer preserves the same transition claim: pre-state + input -> post-state.',
    zhPrinciple: '每一层都保护同一个状态转换声明：前状态 + 输入 -> 后状态。',
    formula: 'F(pre_state, txs) = post_state',
    checkpoints: ['Deterministic execution', 'Canonical public inputs', 'Retrievable DA payloads', 'Verifier-friendly proof'],
    zhCheckpoints: ['确定性执行', '规范 public inputs', '可取回的 DA 数据', '适合验证器处理的证明'],
  },
  {
    id: 'finite-fields',
    title: 'Finite Fields',
    zhTitle: '有限域',
    summary: 'ZK systems replace ordinary arithmetic with arithmetic modulo a prime field.',
    zhSummary: 'ZK 系统把普通算术换成质数域上的取模算术。',
    principle: 'A finite field keeps addition, multiplication, subtraction, division, and equality closed.',
    zhPrinciple: '有限域让加法、乘法、减法、除法和相等判断都保持封闭。',
    formula: '(a + b) mod p, (a * b) mod p, a * a^-1 = 1 mod p',
    checkpoints: ['Modulo wraparound', 'Multiplicative inverse', 'No floating point', 'Constraint-friendly equality'],
    zhCheckpoints: ['取模回绕', '乘法逆元', '没有浮点数', '适合约束的相等关系'],
  },
  {
    id: 'commitments',
    title: 'Commitments',
    zhTitle: '承诺与状态根',
    summary: 'Hashes and Merkle roots compress large state into a small value while keeping tamper evidence.',
    zhSummary: '哈希和 Merkle root 把大状态压缩成小值，同时保留篡改证据。',
    principle: 'A commitment hides volume, not truth: changing one leaf changes the root.',
    zhPrinciple: '承诺隐藏的是体积，不是真相：一个叶子变化会改变根。',
    formula: 'root = Merkle(Hash(leaf_0), ..., Hash(leaf_n))',
    checkpoints: ['Leaf hashing', 'Sibling path', 'Root recomputation', 'Public root comparison'],
    zhCheckpoints: ['叶子哈希', '兄弟路径', '根重算', '公开根比较'],
  },
  {
    id: 'neovm',
    title: 'NeoVM Semantics',
    zhTitle: 'NeoVM 语义',
    summary: 'NeoVM is a deterministic stack machine: each opcode transforms stack, instruction pointer, and gas.',
    zhSummary: 'NeoVM 是确定性的栈机器：每个 opcode 转换栈、指令指针和 gas。',
    principle: 'To prove NeoVM, prove every opcode transition and prove that the trace is chained correctly.',
    zhPrinciple: '要证明 NeoVM，就要证明每个 opcode 转换，并证明执行轨迹首尾相接。',
    formula: 'T_i = (pc_i, stack_i, gas_i), T_{i+1} = step(op_i, T_i)',
    checkpoints: ['Opcode metadata', 'Stack transition', 'Gas accounting', 'HALT/FAULT result'],
    zhCheckpoints: ['Opcode 元数据', '栈转换', 'Gas 计量', 'HALT/FAULT 结果'],
  },
  {
    id: 'riscv',
    title: 'RISC-V VM Execution',
    zhTitle: 'RISC-V VM 执行',
    summary: 'RISC-V execution exposes a simple register/memory cycle that zkVMs and PolkaVM-style hosts can reason about.',
    zhSummary: 'RISC-V 执行暴露简单的寄存器/内存周期，便于 zkVM 和 PolkaVM 类宿主推理。',
    principle: 'Each cycle fetches an instruction, reads registers/memory, writes at most a small state delta, then advances pc.',
    zhPrinciple: '每个周期取指、读寄存器/内存、写入小范围状态变化，然后推进 pc。',
    formula: '(pc, regs, mem) -> decode(inst) -> (pc\', regs\', mem\')',
    checkpoints: ['Fetch/decode', 'Register file', 'Memory access', 'Control-flow constraint'],
    zhCheckpoints: ['取指/解码', '寄存器文件', '内存访问', '控制流约束'],
  },
  {
    id: 'arithmetization',
    title: 'Arithmetization',
    zhTitle: '算术化',
    summary: 'Execution traces become algebraic constraints, so invalid execution cannot satisfy the equations.',
    zhSummary: '执行轨迹会变成代数约束，因此错误执行无法满足这些方程。',
    principle: 'The prover commits to a trace; the verifier checks low-degree equations over sampled points.',
    zhPrinciple: '证明者承诺执行轨迹；验证者在采样点上检查低阶多项式方程。',
    formula: 'C(trace_i, trace_{i+1}, public_input) = 0',
    checkpoints: ['Trace columns', 'Boundary constraints', 'Transition constraints', 'Permutation/lookups'],
    zhCheckpoints: ['轨迹列', '边界约束', '状态转移约束', '排列/查表约束'],
  },
  {
    id: 'zk-proofs',
    title: 'Zero-Knowledge Proofs',
    zhTitle: '零知识证明',
    summary: 'A proof convinces the verifier that a valid witness exists without replaying the full computation.',
    zhSummary: '证明让验证者相信存在有效 witness，而不需要重放完整计算。',
    principle: 'N4 needs validity more than secrecy: succinct verification is the operational win.',
    zhPrinciple: 'N4 更需要有效性而不是保密性：简洁验证才是运维价值。',
    formula: 'Verify(vk, public_inputs, proof) = true',
    checkpoints: ['Completeness', 'Soundness', 'Zero knowledge when enabled', 'Succinct verifier work'],
    zhCheckpoints: ['完备性', '可靠性', '可选零知识', '简洁验证成本'],
  },
  {
    id: 'aggregation',
    title: 'Aggregation and Recursion',
    zhTitle: '聚合与递归',
    summary: 'Many L2 proofs can be folded or wrapped so L1 verifies a smaller number of proofs.',
    zhSummary: '多条 L2 的证明可以折叠或包装，让 L1 验证更少的证明。',
    principle: 'Aggregation trades prover cost for lower L1 verification and cleaner settlement.',
    zhPrinciple: '聚合用更高证明者成本换取更低 L1 验证成本和更清晰结算。',
    formula: 'Aggregate(proof_1, ..., proof_n) -> proof_aggregate',
    checkpoints: ['Same public-input schema', 'Verifier-key registry', 'Batch root binding', 'Gateway settlement'],
    zhCheckpoints: ['统一 public-input schema', '验证密钥注册表', '批次根绑定', 'Gateway 结算'],
  },
  {
    id: 'n4-security',
    title: 'Neo N4 Security Model',
    zhTitle: 'Neo N4 安全模型',
    summary: 'The system is only as strong as execution determinism, DA retrievability, proof verification, and bridge accounting.',
    zhSummary: '系统强度取决于执行确定性、DA 可取回性、证明验证和桥资产记账。',
    principle: 'A production N4 claim must bind execution, data availability, proof, and token accounting together.',
    zhPrinciple: '生产级 N4 声明必须把执行、数据可用性、证明和资产记账绑定在一起。',
    formula: 'settled_root = Verify(exec_trace, DA_commitment, proof, bridge_delta)',
    checkpoints: ['NeoFS DA availability', 'Verifier contract policy', 'No lossy decimal conversion', 'Replay-resistant messages'],
    zhCheckpoints: ['NeoFS DA 可用性', '验证器合约策略', '无损 decimal 转换', '防重放消息'],
  },
];

export const pipelineStages = [
  {
    id: 'neovm',
    label: 'NeoVM semantics',
    zhLabel: 'NeoVM 语义',
    detail: 'Stack opcode behavior defines the source-level transition contract.',
    zhDetail: '栈式 opcode 行为定义源层状态转换契约。',
  },
  {
    id: 'riscv',
    label: 'NeoVM2 / RISC-V profile',
    zhLabel: 'NeoVM2 / RISC-V profile',
    detail: 'The canonical N4 L2 execution profile exposes deterministic cycles for execution hosts.',
    zhDetail: 'N4 L2 默认执行 profile 暴露确定性的执行周期给宿主。',
  },
  {
    id: 'trace',
    label: 'Execution trace',
    zhLabel: '执行轨迹',
    detail: 'Every VM step becomes rows: pc, registers/stack, memory deltas, gas, and roots.',
    zhDetail: '每个 VM step 变成 pc、寄存器/栈、内存变化、gas 和根的轨迹行。',
  },
  {
    id: 'constraints',
    label: 'Algebraic constraints',
    zhLabel: '代数约束',
    detail: 'Trace rows are checked by equations over a finite field.',
    zhDetail: '轨迹行会被有限域上的方程检查。',
  },
  {
    id: 'proof',
    label: 'zkVM proof',
    zhLabel: 'zkVM 证明',
    detail: 'The prover produces compact evidence for the public inputs.',
    zhDetail: '证明者为 public inputs 生成紧凑证据。',
  },
  {
    id: 'settlement',
    label: 'L1 verification',
    zhLabel: 'L1 验证',
    detail: 'NeoHub verifier policy accepts or rejects state-root updates.',
    zhDetail: 'NeoHub 验证策略接受或拒绝 state-root 更新。',
  },
];

export const journeyStages = [
  {
    id: 'neovm',
    label: 'NeoVM semantics',
    zhLabel: 'NeoVM 语义',
    position: { x: 9, y: 46 },
    payload: 'opcode transition',
  },
  {
    id: 'riscv',
    label: 'NeoVM2/RISC-V cycles',
    zhLabel: 'NeoVM2/RISC-V 周期',
    position: { x: 24, y: 28 },
    payload: 'pc + registers + memory',
  },
  {
    id: 'trace',
    label: 'Execution trace',
    zhLabel: '执行轨迹',
    position: { x: 39, y: 58 },
    payload: 'ordered trace rows',
  },
  {
    id: 'constraints',
    label: 'Field constraints',
    zhLabel: '有限域约束',
    position: { x: 54, y: 30 },
    payload: 'trace constraints = 0',
  },
  {
    id: 'commitment',
    label: 'Commitments',
    zhLabel: '承诺',
    position: { x: 68, y: 60 },
    payload: 'root + public inputs',
  },
  {
    id: 'proof',
    label: 'zkVM proof',
    zhLabel: 'zkVM 证明',
    position: { x: 82, y: 34 },
    payload: 'succinct proof bytes',
  },
  {
    id: 'settlement',
    label: 'L1 verify',
    zhLabel: 'L1 验证',
    position: { x: 94, y: 52 },
    payload: 'accept state root',
  },
];

const lessonToJourneyStage = {
  'stack-map': 'neovm',
  'finite-fields': 'constraints',
  commitments: 'commitment',
  neovm: 'neovm',
  riscv: 'riscv',
  arithmetization: 'constraints',
  'zk-proofs': 'proof',
  aggregation: 'proof',
  'n4-security': 'settlement',
};

const journeyStageToLesson = {
  neovm: 'neovm',
  riscv: 'riscv',
  trace: 'arithmetization',
  constraints: 'arithmetization',
  commitment: 'commitments',
  proof: 'zk-proofs',
  settlement: 'aggregation',
};

export function getLessonIdForJourneyStage(stageId) {
  return journeyStageToLesson[stageId] ?? 'stack-map';
}

export const sampleNeoVmProgram = [
  { op: 'PUSH', value: 2, doc: 'Push first operand.' },
  { op: 'PUSH', value: 3, doc: 'Push second operand.' },
  { op: 'ADD', doc: 'Add top two stack values in the field.' },
  { op: 'PUSH', value: 5, doc: 'Push expected result.' },
  { op: 'EQ', doc: 'Compare actual result with expected result.' },
  { op: 'HALT', doc: 'Stop with top-of-stack result.' },
];

export const sampleRiscVProgram = [
  { op: 'ADDI', rd: 'x1', rs1: 'x0', imm: 5, doc: 'x1 = 5' },
  { op: 'ADDI', rd: 'x2', rs1: 'x0', imm: 7, doc: 'x2 = 7' },
  { op: 'ADD', rd: 'x3', rs1: 'x1', rs2: 'x2', doc: 'x3 = x1 + x2' },
  { op: 'SW', rs1: 'x0', rs2: 'x3', imm: 0, doc: 'mem[0] = x3' },
  { op: 'LW', rd: 'x4', rs1: 'x0', imm: 0, doc: 'x4 = mem[0]' },
  { op: 'BEQ', rs1: 'x3', rs2: 'x4', imm: 8, doc: 'branch if stored value round-trips' },
];

export function listLessons() {
  return lessons.map((lesson) => ({ ...lesson }));
}

export function getLesson(id) {
  const lesson = lessons.find((item) => item.id === id);
  if (!lesson) throw new Error(`Unknown lesson: ${id}`);
  return { ...lesson };
}

export function mod(value, prime = FIELD_PRIME) {
  return ((Number(value) % prime) + prime) % prime;
}

export function fieldAdd(a, b, prime = FIELD_PRIME) {
  return mod(a + b, prime);
}

export function fieldSub(a, b, prime = FIELD_PRIME) {
  return mod(a - b, prime);
}

export function fieldMul(a, b, prime = FIELD_PRIME) {
  return mod(a * b, prime);
}

export function fieldPow(base, exponent, prime = FIELD_PRIME) {
  let result = 1;
  let b = mod(base, prime);
  let e = Number(exponent);
  while (e > 0) {
    if (e & 1) result = fieldMul(result, b, prime);
    b = fieldMul(b, b, prime);
    e >>= 1;
  }
  return result;
}

export function evaluatePolynomial(coefficients, x, prime = FIELD_PRIME) {
  let result = 0;
  let power = 1;
  for (const coefficient of coefficients) {
    result = fieldAdd(result, fieldMul(coefficient, power, prime), prime);
    power = fieldMul(power, x, prime);
  }
  return result;
}

export function vanishingPolynomial(x, domainSize, prime = FIELD_PRIME) {
  return fieldSub(fieldPow(x, domainSize, prime), 1, prime);
}

export function fieldInverse(value, prime = FIELD_PRIME) {
  const normalized = mod(value, prime);
  if (normalized === 0) return null;
  return fieldPow(normalized, prime - 2, prime);
}

export function buildFieldSnapshot(a, b, prime = FIELD_PRIME) {
  const left = mod(a, prime);
  const right = mod(b, prime);
  const inverse = fieldInverse(left, prime);
  return {
    prime,
    a: left,
    b: right,
    add: fieldAdd(left, right, prime),
    sub: fieldSub(left, right, prime),
    mul: fieldMul(left, right, prime),
    inverse,
    inverseCheck: inverse === null ? null : fieldMul(left, inverse, prime),
    equalityPolynomial: fieldMul(fieldSub(left, right, prime), fieldSub(left, right, prime), prime),
    ruler: Array.from({ length: prime }, (_, value) => value),
  };
}

export function buildJourneyVisualization({ activeLessonId = 'stack-map', neoVmStep = 0, riscvStep = 0 } = {}) {
  const activeStageId = lessonToJourneyStage[activeLessonId] ?? 'neovm';
  const activeIndex = journeyStages.findIndex((stage) => stage.id === activeStageId);
  const stages = journeyStages.map((stage, index) => ({
    ...stage,
    state: index < activeIndex ? 'done' : index === activeIndex ? 'active' : 'pending',
  }));
  const packetStage = stages[activeIndex] ?? stages[0];
  return {
    stages,
    links: stages.slice(0, -1).map((stage, index) => ({
      from: stage.id,
      to: stages[index + 1].id,
      state: index < activeIndex ? 'done' : index === activeIndex ? 'active' : 'pending',
    })),
    activeStageId: packetStage.id,
    packet: {
      stageId: packetStage.id,
      payload: `${packetStage.payload}; NeoVM step ${neoVmStep}; RISC-V cycle ${riscvStep}`,
      x: packetStage.position.x,
      y: packetStage.position.y,
    },
  };
}

export function buildFieldVisualization(a, b, prime = FIELD_PRIME) {
  const field = buildFieldSnapshot(a, b, prime);
  const center = { x: 50, y: 50 };
  const radius = 42;
  const points = field.ruler.map((value) => {
    const angle = ((value / prime) * Math.PI * 2) - Math.PI / 2;
    return {
      value,
      x: round(center.x + Math.cos(angle) * radius),
      y: round(center.y + Math.sin(angle) * radius),
      angle,
      role: value === field.a ? 'a' : value === field.b ? 'b' : value === field.add ? 'sum' : value === field.mul ? 'product' : 'field',
    };
  });
  const multiplicationHops = [];
  let cursor = 0;
  for (let i = 0; i < field.b; i += 1) {
    const next = fieldAdd(cursor, field.a, prime);
    multiplicationHops.push({ from: cursor, to: next, addend: field.a });
    cursor = next;
  }
  return {
    prime,
    center,
    radius,
    points,
    addition: {
      start: field.a,
      addend: field.b,
      end: field.add,
      equation: `${field.a} + ${field.b} = ${field.add} mod ${prime}`,
    },
    multiplication: {
      start: 0,
      addend: field.a,
      times: field.b,
      end: field.mul,
      hops: multiplicationHops,
      equation: `${field.a} * ${field.b} = ${field.mul} mod ${prime}`,
    },
    inverse: {
      value: field.inverse,
      check: field.inverseCheck,
      equation: field.inverse === null ? '0 has no inverse' : `${field.a} * ${field.inverse} = ${field.inverseCheck} mod ${prime}`,
    },
  };
}

export function toyHash(values, prime = 257) {
  const bytes = Array.isArray(values) ? values : String(values).split('').map((char) => char.charCodeAt(0));
  let acc = 73;
  for (const value of bytes) {
    const numeric = typeof value === 'number' ? value : String(value).charCodeAt(0);
    acc = (acc * 131 + numeric + 17) % prime;
  }
  return acc;
}

export function toyHashPair(left, right, prime = 257) {
  return toyHash([11, Number(left), 23, Number(right), 37], prime);
}

export function buildMerkleTree(leaves, prime = 257) {
  if (!Array.isArray(leaves) || leaves.length === 0) throw new Error('Merkle tree requires at least one leaf');
  let level = leaves.map((leaf) => toyHash(String(leaf), prime));
  const levels = [level];
  while (level.length > 1) {
    const next = [];
    for (let index = 0; index < level.length; index += 2) {
      const left = level[index];
      const right = level[index + 1] ?? left;
      next.push(toyHashPair(left, right, prime));
    }
    level = next;
    levels.push(level);
  }
  return {
    leaves: [...leaves],
    levels,
    root: level[0],
    prime,
  };
}

export function merkleProof(tree, leafIndex) {
  const proof = [];
  let index = leafIndex;
  for (let levelIndex = 0; levelIndex < tree.levels.length - 1; levelIndex += 1) {
    const level = tree.levels[levelIndex];
    const siblingIndex = index % 2 === 0 ? index + 1 : index - 1;
    proof.push({
      sibling: level[siblingIndex] ?? level[index],
      side: index % 2 === 0 ? 'right' : 'left',
    });
    index = Math.floor(index / 2);
  }
  return proof;
}

export function verifyMerkleProof(leaf, proof, expectedRoot, prime = 257) {
  let hash = toyHash(String(leaf), prime);
  for (const item of proof) {
    hash = item.side === 'right'
      ? toyHashPair(hash, item.sibling, prime)
      : toyHashPair(item.sibling, hash, prime);
  }
  return hash === expectedRoot;
}

export function buildMerkleVisualization(leaves, leafIndex, prime = 257) {
  const tree = buildMerkleTree(leaves, prime);
  const proof = merkleProof(tree, leafIndex);
  const levels = tree.levels.map((level, levelIndex) => level.map((hash, index) => ({
    hash,
    index,
    level: levelIndex,
    leaf: levelIndex === 0 ? tree.leaves[index] : undefined,
    x: round(((index + 1) / (level.length + 1)) * 100),
    y: round(88 - levelIndex * 34),
    role: 'node',
  })));
  const path = [{ ...levels[0][leafIndex], role: 'selected' }];
  let cursor = leafIndex;
  for (let levelIndex = 0; levelIndex < tree.levels.length - 1; levelIndex += 1) {
    const siblingIndex = cursor % 2 === 0 ? cursor + 1 : cursor - 1;
    const sibling = levels[levelIndex][siblingIndex] ?? levels[levelIndex][cursor];
    path.push({ ...sibling, role: 'sibling' });
    cursor = Math.floor(cursor / 2);
  }
  path[path.length - 1] = { ...levels.at(-1)[0], role: 'root' };
  const pathKeys = new Set(path.map((node) => `${node.level}:${node.index}`));
  const edges = [];
  for (let levelIndex = 0; levelIndex < levels.length - 1; levelIndex += 1) {
    for (const child of levels[levelIndex]) {
      const parent = levels[levelIndex + 1][Math.floor(child.index / 2)];
      edges.push({
        from: `${child.level}:${child.index}`,
        to: `${parent.level}:${parent.index}`,
        kind: pathKeys.has(`${child.level}:${child.index}`) || pathKeys.has(`${parent.level}:${parent.index}`) ? 'proof' : 'tree',
      });
    }
  }
  return {
    levels,
    edges,
    path,
    selectedLeaf: levels[0][leafIndex],
    root: levels.at(-1)[0],
    verifies: verifyMerkleProof(tree.leaves[leafIndex], proof, tree.root, tree.prime),
  };
}

export function runNeoVmTrace(program = sampleNeoVmProgram, until = program.length, prime = FIELD_PRIME) {
  const stack = [];
  const trace = [];
  let pc = 0;
  let gas = 0;
  let halted = false;
  const limit = Math.max(0, Math.min(until, program.length));

  while (pc < limit && !halted) {
    const instruction = program[pc];
    const before = [...stack];
    const gasBefore = gas;
    const transition = executeNeoVmInstruction(stack, instruction, prime);
    gas += transition.gas;
    trace.push({
      step: trace.length,
      pc,
      op: instruction.op,
      value: instruction.value,
      before,
      after: [...stack],
      gasBefore,
      gasAfter: gas,
      constraint: transition.constraint,
      doc: instruction.doc,
    });
    halted = instruction.op === 'HALT';
    pc += 1;
  }

  return {
    pc,
    gas,
    halted,
    stack,
    result: stack.at(-1),
    trace,
  };
}

function executeNeoVmInstruction(stack, instruction, prime) {
  switch (instruction.op) {
    case 'PUSH':
      stack.push(mod(instruction.value, prime));
      return { gas: 1, constraint: `stack' = stack || ${instruction.value}; pc' = pc + 1` };
    case 'ADD': {
      const b = stack.pop();
      const a = stack.pop();
      stack.push(fieldAdd(a, b, prime));
      return { gas: 2, constraint: `top' = (a + b) mod ${prime}; height' = height - 1` };
    }
    case 'MUL': {
      const b = stack.pop();
      const a = stack.pop();
      stack.push(fieldMul(a, b, prime));
      return { gas: 3, constraint: `top' = (a * b) mod ${prime}; height' = height - 1` };
    }
    case 'EQ': {
      const b = stack.pop();
      const a = stack.pop();
      stack.push(mod(a, prime) === mod(b, prime) ? 1 : 0);
      return { gas: 1, constraint: '(a - b) * is_equal = 0, result in {0,1}' };
    }
    case 'HALT':
      return { gas: 0, constraint: 'halted = true; public_result = stack_top' };
    default:
      throw new Error(`Unsupported NeoVM instruction: ${instruction.op}`);
  }
}

export function runRiscVTrace(program = sampleRiscVProgram, until = program.length) {
  const regs = { x0: 0, x1: 0, x2: 0, x3: 0, x4: 0 };
  const memory = {};
  const trace = [];
  let pc = 0;
  const limit = Math.max(0, Math.min(until, program.length));

  while (pc / 4 < limit) {
    const instruction = program[pc / 4];
    const before = { regs: { ...regs }, memory: { ...memory }, pc };
    const nextPc = executeRiscVInstruction({ regs, memory, pc }, instruction);
    const after = { regs: { ...regs }, memory: { ...memory }, pc: nextPc };
    trace.push({
      cycle: trace.length,
      pc,
      op: instruction.op,
      instruction: formatRiscV(instruction),
      before,
      after,
      constraint: riscvConstraint(instruction),
      doc: instruction.doc,
    });
    pc = nextPc;
    if (pc / 4 >= program.length) break;
  }

  return {
    pc,
    regs,
    memory,
    trace,
    result: regs.x4,
  };
}

function executeRiscVInstruction(machine, instruction) {
  const { regs, memory, pc } = machine;
  switch (instruction.op) {
    case 'ADDI':
      writeReg(regs, instruction.rd, regs[instruction.rs1] + instruction.imm);
      return pc + 4;
    case 'ADD':
      writeReg(regs, instruction.rd, regs[instruction.rs1] + regs[instruction.rs2]);
      return pc + 4;
    case 'SW':
      memory[String(regs[instruction.rs1] + instruction.imm)] = regs[instruction.rs2];
      return pc + 4;
    case 'LW':
      writeReg(regs, instruction.rd, memory[String(regs[instruction.rs1] + instruction.imm)] ?? 0);
      return pc + 4;
    case 'BEQ':
      return regs[instruction.rs1] === regs[instruction.rs2] ? pc + instruction.imm : pc + 4;
    default:
      throw new Error(`Unsupported RISC-V instruction: ${instruction.op}`);
  }
}

function writeReg(regs, name, value) {
  if (name !== 'x0') regs[name] = value;
}

function formatRiscV(instruction) {
  switch (instruction.op) {
    case 'ADDI':
      return `ADDI ${instruction.rd}, ${instruction.rs1}, ${instruction.imm}`;
    case 'ADD':
      return `ADD ${instruction.rd}, ${instruction.rs1}, ${instruction.rs2}`;
    case 'SW':
      return `SW ${instruction.rs2}, ${instruction.imm}(${instruction.rs1})`;
    case 'LW':
      return `LW ${instruction.rd}, ${instruction.imm}(${instruction.rs1})`;
    case 'BEQ':
      return `BEQ ${instruction.rs1}, ${instruction.rs2}, ${instruction.imm}`;
    default:
      return instruction.op;
  }
}

function riscvConstraint(instruction) {
  switch (instruction.op) {
    case 'ADDI':
      return `${instruction.rd}' = ${instruction.rs1} + imm; pc' = pc + 4`;
    case 'ADD':
      return `${instruction.rd}' = ${instruction.rs1} + ${instruction.rs2}; pc' = pc + 4`;
    case 'SW':
      return `mem[${instruction.rs1} + imm]' = ${instruction.rs2}; pc' = pc + 4`;
    case 'LW':
      return `${instruction.rd}' = mem[${instruction.rs1} + imm]; pc' = pc + 4`;
    case 'BEQ':
      return `pc' = (${instruction.rs1} == ${instruction.rs2}) ? pc + imm : pc + 4`;
    default:
      return 'valid instruction transition';
  }
}

export function estimateProofPipeline({ cycles = 64, memoryOps = 12, publicInputs = 6, aggregation = 1 } = {}) {
  const normalizedCycles = Math.max(1, Number(cycles));
  const normalizedMemoryOps = Math.max(0, Number(memoryOps));
  const normalizedPublicInputs = Math.max(1, Number(publicInputs));
  const normalizedAggregation = Math.max(1, Number(aggregation));
  const traceRows = nextPowerOfTwo(normalizedCycles + normalizedMemoryOps + 8);
  const constraints = normalizedCycles * 19 + normalizedMemoryOps * 11 + normalizedPublicInputs * 7;
  const commitmentCount = Math.ceil(Math.log2(traceRows)) + 3;
  const verifierChecks = 4 + normalizedPublicInputs + Math.ceil(Math.log2(normalizedAggregation));
  const proofBytes = 192 + commitmentCount * 48 + Math.ceil(normalizedAggregation / 2) * 32;
  return {
    traceRows,
    constraints,
    commitmentCount,
    verifierChecks,
    proofBytes,
    note: 'Teaching estimate only; production proof size depends on the concrete backend and wrapper.',
  };
}

export function buildTraceConstraintMatrix({ neovmTrace = [], riscvTrace = [], proofEstimate = estimateProofPipeline() } = {}) {
  const neovmRows = neovmTrace.map((row) => ({
    row: `N${row.step}`,
    pc: row.pc,
    operation: row.value === undefined ? row.op : `${row.op} ${row.value}`,
    'state delta': `${formatStack(row.before)} -> ${formatStack(row.after)}`,
    constraint: row.constraint,
    status: row.step === neovmTrace.length - 1 ? 'active' : 'satisfied',
  }));
  const riscvRows = riscvTrace.map((row) => ({
    row: `R${row.cycle}`,
    pc: row.pc,
    operation: row.instruction,
    'state delta': changedRegisters(row.before.regs, row.after.regs).join(', ') || `pc ${row.before.pc}->${row.after.pc}`,
    constraint: row.constraint,
    status: row.cycle === riscvTrace.length - 1 ? 'active' : 'satisfied',
  }));
  return {
    columns: ['row', 'pc', 'operation', 'state delta', 'constraint', 'status'],
    rows: [...neovmRows, ...riscvRows],
    summary: {
      traceRows: proofEstimate.traceRows,
      constraints: proofEstimate.constraints,
      activeRows: [...neovmRows, ...riscvRows].filter((row) => row.status === 'active').length,
    },
  };
}

export function buildProofTranscript({ proofEstimate = estimateProofPipeline(), publicInputRoot = '0', aggregateCount = 1 } = {}) {
  return {
    publicInputs: {
      root: String(publicInputRoot),
      traceRows: proofEstimate.traceRows,
      constraints: proofEstimate.constraints,
      aggregateCount,
    },
    steps: [
      {
        actor: 'L2 executor',
        action: 'Commit public inputs',
        payload: `state root ${publicInputRoot}; ${proofEstimate.traceRows} trace rows`,
      },
      {
        actor: 'Prover',
        action: 'Prove witness satisfies constraints',
        payload: `${proofEstimate.constraints} constraints -> ${proofEstimate.proofBytes} bytes`,
      },
      {
        actor: 'Verifier',
        action: 'Check proof against vk and inputs',
        payload: `${proofEstimate.verifierChecks} verifier checks`,
      },
      {
        actor: 'NeoHub',
        action: 'Accept or reject settlement update',
        payload: aggregateCount > 1 ? `${aggregateCount} proofs aggregated before L1` : 'single L2 proof submitted',
      },
    ],
  };
}

export function buildZkpMathVisualization({
  proofEstimate = estimateProofPipeline(),
  publicInputRoot = '0',
  prime = FIELD_PRIME,
  focusId,
  witnessMode = 'valid',
  proofPhase = 2,
} = {}) {
  const domainSize = 8;
  const domainGenerator = 2;
  const mode = witnessMode === 'invalid' ? 'invalid' : 'valid';
  const domain = Array.from({ length: domainSize }, (_, index) => {
    const x = fieldPow(domainGenerator, index, prime);
    const angle = ((index / domainSize) * Math.PI * 2) - Math.PI / 2;
    return {
      index,
      x,
      vanish: vanishingPolynomial(x, domainSize, prime),
      px: round(50 + Math.cos(angle) * 34),
      py: round(50 + Math.sin(angle) * 34),
    };
  });
  const seed = mod(toyHash([publicInputRoot, proofEstimate.traceRows, proofEstimate.constraints], prime), prime);
  const step = mod((proofEstimate.verifierChecks % 5) + 2, prime);
  const witnessValues = domain.map((point) => ({
    ...point,
    value: fieldAdd(seed, fieldMul(point.index, step, prime), prime),
  }));
  const quotientCoefficients = [
    mod(3 + proofEstimate.commitmentCount, prime),
    mod(seed + 5, prime),
    mod(step + proofEstimate.verifierChecks, prime),
  ];
  const vanishingCoefficients = Array.from({ length: domainSize + 1 }, (_, index) => {
    if (index === 0) return prime - 1;
    if (index === domainSize) return 1;
    return 0;
  });
  const cleanConstraintCoefficients = polynomialMul(quotientCoefficients, vanishingCoefficients, prime);
  const invalidRow = 1;
  const invalidResidual = mode === 'invalid' ? nonZero(fieldAdd(step, 3, prime), prime) : 0;
  const invalidBasis = mode === 'invalid'
    ? lagrangeBasis(domain.map((point) => point.x), invalidRow, prime)
    : [0];
  const constraintCoefficients = mode === 'invalid'
    ? polynomialAdd(cleanConstraintCoefficients, polynomialScale(invalidBasis, invalidResidual, prime), prime)
    : cleanConstraintCoefficients;
  const challenge = buildChallenge({
    domain,
    publicInputRoot,
    proofEstimate,
    prime,
  });
  const qAtZeta = evaluatePolynomial(quotientCoefficients, challenge.zeta, prime);
  const zAtZeta = vanishingPolynomial(challenge.zeta, domainSize, prime);
  const cAtZeta = evaluatePolynomial(constraintCoefficients, challenge.zeta, prime);
  const right = fieldMul(qAtZeta, zAtZeta, prime);
  const phase = buildZkpPhase(proofPhase, mode, cAtZeta === right);
  const gates = [
    {
      id: 'boundary',
      label: 'Boundary',
      zhLabel: '边界约束',
      equation: 'w_0 - public_start = 0',
      residual: fieldSub(witnessValues[0].value, seed, prime),
      role: 'pins the initial state',
      zhRole: '绑定初始状态',
      detail: 'Boundary gates keep the first and last trace rows tied to public state commitments.',
      zhDetail: '边界约束把 trace 的第一行和最后一行绑定到公开状态承诺。',
    },
    {
      id: 'transition',
      label: 'Transition',
      zhLabel: '转移约束',
      equation: 'w_{i+1} - w_i - step = 0',
      residual: mode === 'invalid' ? invalidResidual : fieldSub(fieldSub(witnessValues[1].value, witnessValues[0].value, prime), step, prime),
      role: 'checks every VM cycle',
      zhRole: '检查每个 VM 周期',
      detail: mode === 'invalid'
        ? 'This VM cycle was tampered: the transition equation leaves a non-zero residual.'
        : 'Transition gates prove that every VM cycle follows the allowed next-state rule.',
      zhDetail: mode === 'invalid'
        ? '这个 VM 周期被篡改了：转移方程留下了非零 residual。'
        : '转移约束证明每个 VM 周期都遵守允许的下一状态规则。',
    },
    {
      id: 'boolean',
      label: 'Boolean',
      zhLabel: '布尔约束',
      equation: 'b * (b - 1) = 0',
      residual: 0,
      role: 'forces selector bits to be 0 or 1',
      zhRole: '确保 selector 只能是 0 或 1',
      detail: 'Boolean gates prevent selectors and flags from taking invalid field values.',
      zhDetail: '布尔约束防止 selector 和 flag 取到非法的有限域数值。',
    },
    {
      id: 'public-input',
      label: 'Public input',
      zhLabel: '公开输入',
      equation: 'root == committed_root',
      residual: 0,
      role: 'binds proof to the L1 state root',
      zhRole: '把证明绑定到 L1 状态根',
      detail: 'Public-input gates make the proof inseparable from the L1-visible state root.',
      zhDetail: '公开输入约束让证明无法脱离 L1 可见的状态根。',
    },
  ];
  const transcript = [
    {
      id: 'commit',
      phaseId: 'witness',
      actor: 'Prover',
      action: 'commit witness trace',
      detail: `${proofEstimate.traceRows} rows compressed into trace commitments`,
    },
    {
      id: 'alpha',
      phaseId: 'constraints',
      actor: 'Verifier',
      action: `sample alpha challenge ${challenge.alpha}`,
      detail: 'combine many constraints into one random linear combination',
    },
    {
      id: 'quotient',
      phaseId: 'quotient',
      actor: 'Prover',
      action: 'divide by vanishing polynomial',
      detail: 'produce quotient Q(x) only if C(x) vanishes on the trace domain',
    },
    {
      id: 'zeta',
      phaseId: 'challenge',
      actor: 'Verifier',
      action: `sample zeta challenge ${challenge.zeta}`,
      detail: 'check one outside-domain opening instead of replaying every row',
    },
    {
      id: 'opening',
      phaseId: 'verify',
      actor: 'Verifier',
      action: 'verify quotient opening',
      detail: `${cAtZeta} ${cAtZeta === right ? '==' : '!='} ${qAtZeta} * ${zAtZeta} = ${right} mod ${prime}`,
    },
  ];
  const layers = [
    { id: 'witness', label: 'Witness trace', zhLabel: 'Witness 轨迹', x: 8, y: 20, state: 'done', detail: 'The private witness is the full execution trace that should not be replayed on L1.', zhDetail: '私有 witness 是完整执行轨迹，L1 不应该重放它。' },
    { id: 'constraints', label: 'Gate constraints', zhLabel: '门约束', x: 28, y: 54, state: 'done', detail: 'Gate equations translate valid VM execution into finite-field equalities.', zhDetail: '门约束把有效 VM 执行翻译成有限域等式。' },
    { id: 'quotient', label: 'Quotient Q(x)', zhLabel: '商多项式 Q(x)', x: 50, y: 24, state: 'active', detail: 'A clean quotient means the combined constraint vanishes over the whole evaluation domain.', zhDetail: '能干净得到商多项式，说明组合约束在整个评价域上归零。' },
    { id: 'challenge', label: 'Random zeta', zhLabel: '随机 zeta', x: 72, y: 56, state: 'active', detail: 'Fiat-Shamir turns commitments into random verifier challenges without interaction.', zhDetail: 'Fiat-Shamir 把承诺变成无需交互的随机验证挑战。' },
    { id: 'verify', label: 'Verifier equality', zhLabel: '验证器等式', x: 92, y: 26, state: 'pending', detail: 'The verifier checks one outside-domain equation instead of every trace row.', zhDetail: '验证器检查一个评价域外等式，而不是检查每一行 trace。' },
  ];
  const effectiveFocusId = focusId ?? (mode === 'invalid' ? 'gate:transition' : `layer:${phase.id}`);
  const focus = resolveZkpFocus(effectiveFocusId, { gates, transcript, layers });
  return {
    prime,
    domainSize,
    publicInputs: {
      root: String(publicInputRoot),
      traceRows: proofEstimate.traceRows,
      constraints: proofEstimate.constraints,
      verifierChecks: proofEstimate.verifierChecks,
    },
    domain,
    witness: {
      mode,
      seed,
      step,
      invalidRow: mode === 'invalid' ? invalidRow : null,
      invalidResidual,
      values: witnessValues,
      equation: `w_i = ${seed} + ${step}*i mod ${prime}`,
    },
    gates: gates.map((gate) => ({ ...gate, state: focus.id === `gate:${gate.id}` ? 'selected' : 'idle' })),
    quotient: {
      coefficients: quotientCoefficients,
      constraintCoefficients,
      equation: 'C(x) = Q(x) * Z_H(x)',
      samples: domain.map((point) => ({
        x: point.x,
        c: evaluatePolynomial(constraintCoefficients, point.x, prime),
        z: point.vanish,
        state: evaluatePolynomial(constraintCoefficients, point.x, prime) === 0 ? 'zero' : 'broken',
      })),
      qAtZeta,
      zAtZeta,
      cAtZeta,
    },
    phase,
    challenge,
    verifierCheck: {
      equation: 'C(zeta) == Q(zeta) * Z_H(zeta)',
      left: cAtZeta,
      right,
      pass: cAtZeta === right,
    },
    transcript: transcript.map((step) => ({ ...step, state: focus.id === `transcript:${step.id}` ? 'selected' : 'idle' })),
    layers: layers.map((layer) => ({ ...layer, state: focus.id === `layer:${layer.id}` ? 'selected' : layer.state })),
    focus,
    soundness: {
      statement: 'If a cheating trace makes C(x) non-zero on the domain, C(x) will not divide cleanly by Z_H(x); a random zeta catches that with high probability.',
      zhStatement: '如果错误轨迹让 C(x) 在评价域上不为 0，C(x) 就不能被 Z_H(x) 干净整除；随机 zeta 会以高概率抓住它。',
    },
  };
}

export function makeLearningSnapshot({
  activeLessonId = 'stack-map',
  fieldA = 5,
  fieldB = 9,
  merkleLeaves = ['tx:deposit', 'tx:mint', 'tx:swap', 'tx:withdraw'],
  merkleIndex = 1,
  neoVmStep = sampleNeoVmProgram.length,
  riscvStep = sampleRiscVProgram.length,
  proofCycles = 64,
  proofMemoryOps = 12,
  proofPublicInputs = 6,
  proofAggregation = 2,
  zkpFocusId = undefined,
  zkpWitnessMode = 'valid',
  zkpProofPhase = 2,
} = {}) {
  const tree = buildMerkleTree(merkleLeaves);
  const proof = merkleProof(tree, merkleIndex);
  const neovm = runNeoVmTrace(sampleNeoVmProgram, neoVmStep);
  const riscv = runRiscVTrace(sampleRiscVProgram, riscvStep);
  const proofEstimate = estimateProofPipeline({
    cycles: proofCycles,
    memoryOps: proofMemoryOps,
    publicInputs: proofPublicInputs,
    aggregation: proofAggregation,
  });
  return {
    activeLesson: getLesson(activeLessonId),
    field: buildFieldSnapshot(fieldA, fieldB),
    fieldVisual: buildFieldVisualization(fieldA, fieldB),
    merkle: {
      tree,
      proof,
      verifies: verifyMerkleProof(merkleLeaves[merkleIndex], proof, tree.root, tree.prime),
      selectedLeaf: merkleLeaves[merkleIndex],
      visual: buildMerkleVisualization(merkleLeaves, merkleIndex),
    },
    neovm,
    riscv,
    proofEstimate,
    journey: buildJourneyVisualization({ activeLessonId, neoVmStep, riscvStep }),
    constraintMatrix: buildTraceConstraintMatrix({ neovmTrace: neovm.trace, riscvTrace: riscv.trace, proofEstimate }),
    proofTranscript: buildProofTranscript({ proofEstimate, publicInputRoot: String(tree.root), aggregateCount: proofAggregation }),
    zkpMath: buildZkpMathVisualization({
      proofEstimate,
      publicInputRoot: String(tree.root),
      focusId: zkpFocusId,
      witnessMode: zkpWitnessMode,
      proofPhase: zkpProofPhase,
    }),
  };
}

function nextPowerOfTwo(value) {
  let current = 1;
  while (current < value) current *= 2;
  return current;
}

function buildChallenge({ domain, publicInputRoot, proofEstimate, prime }) {
  const domainSet = new Set(domain.map((point) => point.x));
  let zeta = mod(toyHash([publicInputRoot, proofEstimate.proofBytes, proofEstimate.verifierChecks, 97], prime), prime);
  while (zeta === 0 || domainSet.has(zeta)) zeta = fieldAdd(zeta, 1, prime);
  return {
    alpha: mod(toyHash([publicInputRoot, proofEstimate.constraints, 31], prime), prime),
    beta: mod(toyHash([publicInputRoot, proofEstimate.traceRows, 43], prime), prime),
    gamma: mod(toyHash([publicInputRoot, proofEstimate.commitmentCount, 59], prime), prime),
    zeta,
    inDomain: domainSet.has(zeta),
  };
}

function buildZkpPhase(proofPhase, mode, verifierPass) {
  const phases = [
    {
      id: 'witness',
      label: 'Witness',
      zhLabel: 'Witness',
      detail: 'Start with private execution rows: stack/register/memory values are the witness.',
      zhDetail: '从私有执行行开始：stack、register、memory 的值就是 witness。',
      plain: 'The prover starts with the full execution table. L1 should not replay it, but the proof must still bind to it.',
      zhPlain: '证明者先拿到完整执行表。L1 不应该重放它，但证明必须和这张表绑定。',
      math: 'w_i = seed + step*i mod p',
      observable: 'Follow the green witness node: those private rows become the values checked by later gates.',
      zhObservable: '先看绿色 witness 节点：这些私有行会变成后续 gate 要检查的值。',
    },
    {
      id: 'constraints',
      label: 'Constraints',
      zhLabel: '约束',
      detail: mode === 'invalid'
        ? 'A tampered row leaves a non-zero residual, so the constraint polynomial no longer vanishes on H.'
        : 'Every gate residual is zero, so the combined constraint vanishes on the evaluation domain H.',
      zhDetail: mode === 'invalid'
        ? '被篡改的行留下非零 residual，所以约束多项式不再在 H 上归零。'
        : '每个 gate residual 都为 0，所以组合约束在评价域 H 上归零。',
      plain: mode === 'invalid'
        ? 'A bad execution row cannot hide: one gate leaves a visible non-zero residual.'
        : 'Each valid VM step becomes an equation whose residual is zero.',
      zhPlain: mode === 'invalid'
        ? '错误执行行藏不住：至少一个 gate 会留下非零 residual。'
        : '每个有效 VM step 都会变成 residual 为 0 的方程。',
      math: 'C(trace_i, trace_{i+1}, public_input) = 0 on H',
      observable: mode === 'invalid'
        ? 'The transition gate turns red and one domain sample rises above zero.'
        : 'All gate cards stay green and every domain sample stays at zero.',
      zhObservable: mode === 'invalid'
        ? '转移约束卡片会变红，并且有一个域采样柱抬离 0。'
        : '所有 gate 卡片保持绿色，所有域采样都停在 0。',
    },
    {
      id: 'quotient',
      label: 'Quotient',
      zhLabel: '商多项式',
      detail: mode === 'invalid'
        ? 'The prover can still claim a quotient, but a leftover remainder will surface at a random opening.'
        : 'Because C(x) vanishes on H, C(x) can be represented as Q(x) * Z_H(x).',
      zhDetail: mode === 'invalid'
        ? '证明者仍可声称有商多项式，但剩余项会在随机 opening 中暴露。'
        : '因为 C(x) 在 H 上归零，所以 C(x) 可以表示为 Q(x) * Z_H(x)。',
      plain: mode === 'invalid'
        ? 'A fake quotient may look plausible locally, but it leaves a remainder.'
        : 'If C(x) is zero on every point in H, it is divisible by the vanishing polynomial.',
      zhPlain: mode === 'invalid'
        ? '假的 quotient 局部看起来也许像真的，但会留下 remainder。'
        : '如果 C(x) 在 H 的每个点上都是 0，它就可以被 vanishing polynomial 整除。',
      math: 'C(x) = Q(x) * Z_H(x), where Z_H(x)=0 for every x in H',
      observable: 'The middle bars explain whether C(x) vanishes cleanly before the verifier samples zeta.',
      zhObservable: '中间的柱状采样展示 C(x) 在验证器抽 zeta 前是否干净归零。',
    },
    {
      id: 'challenge',
      label: 'Challenge',
      zhLabel: '随机挑战',
      detail: 'Fiat-Shamir samples zeta outside H, forcing the prover to open the committed polynomials at an unpredictable point.',
      zhDetail: 'Fiat-Shamir 在 H 外采样 zeta，迫使证明者在不可预测点打开承诺多项式。',
      plain: 'The verifier chooses an unpredictable outside-domain point so the prover cannot tune the proof after seeing the test.',
      zhPlain: '验证器选择一个不可预测的域外点，让证明者无法看见考题后再改证明。',
      math: 'zeta notin H; open C(zeta), Q(zeta), and Z_H(zeta)',
      observable: 'The orange zeta dot is the random audit point used instead of replaying every row.',
      zhObservable: '橙色 zeta 点就是随机抽查点，用它代替逐行重放。',
    },
    {
      id: 'verify',
      label: 'Verify',
      zhLabel: '验证',
      detail: verifierPass
        ? 'The equality holds at zeta, so this teaching verifier accepts the quotient relation.'
        : 'The equality fails at zeta, so the verifier catches the bad witness and rejects.',
      zhDetail: verifierPass
        ? 'zeta 点上的等式成立，所以这个教学验证器接受 quotient 关系。'
        : 'zeta 点上的等式失败，所以验证器抓住错误 witness 并拒绝。',
      plain: verifierPass
        ? 'The compact proof is accepted because the opened values agree with the quotient relation.'
        : 'The compact proof is rejected because the opened values contradict the quotient relation.',
      zhPlain: verifierPass
        ? '打开值符合 quotient 关系，所以这个紧凑证明被接受。'
        : '打开值和 quotient 关系矛盾，所以这个紧凑证明被拒绝。',
      math: 'Verify: C(zeta) == Q(zeta) * Z_H(zeta)',
      observable: verifierPass
        ? 'The verifier row shows pass and the final equality uses ==.'
        : 'The verifier row shows fail and the final equality uses !=.',
      zhObservable: verifierPass
        ? '验证器行显示 pass，最终等式使用 ==。'
        : '验证器行显示 fail，最终等式使用 !=。',
    },
  ];
  const index = Math.max(0, Math.min(phases.length - 1, Number(proofPhase)));
  return {
    index,
    count: phases.length,
    ...phases[index],
  };
}

function resolveZkpFocus(focusId, { gates, transcript, layers }) {
  const [kind, id] = String(focusId || 'layer:quotient').split(':');
  if (kind === 'gate') {
    const gate = gates.find((item) => item.id === id) ?? gates[0];
    return {
      id: `gate:${gate.id}`,
      kind: 'gate',
      label: gate.label,
      zhLabel: gate.zhLabel,
      detail: gate.detail,
      zhDetail: gate.zhDetail,
      equation: gate.equation,
    };
  }
  if (kind === 'transcript') {
    const step = transcript.find((item) => item.id === id) ?? transcript[0];
    return {
      id: `transcript:${step.id}`,
      kind: 'transcript',
      label: `${step.actor}: ${step.action}`,
      zhLabel: `${step.actor}: ${step.action}`,
      detail: step.detail,
      zhDetail: step.detail,
      equation: '',
    };
  }
  const layer = layers.find((item) => item.id === id) ?? layers.find((item) => item.id === 'quotient') ?? layers[0];
  return {
    id: `layer:${layer.id}`,
    kind: 'layer',
    label: layer.label,
    zhLabel: layer.zhLabel,
    detail: layer.detail,
    zhDetail: layer.zhDetail,
    equation: '',
  };
}

function nonZero(value, prime) {
  const normalized = mod(value, prime);
  return normalized === 0 ? 1 : normalized;
}

function lagrangeBasis(domain, targetIndex, prime) {
  let numerator = [1];
  let denominator = 1;
  const target = domain[targetIndex];
  for (let index = 0; index < domain.length; index += 1) {
    if (index === targetIndex) continue;
    numerator = polynomialMul(numerator, [fieldSub(0, domain[index], prime), 1], prime);
    denominator = fieldMul(denominator, fieldSub(target, domain[index], prime), prime);
  }
  return polynomialScale(numerator, fieldInverse(denominator, prime), prime);
}

function polynomialAdd(left, right, prime) {
  const length = Math.max(left.length, right.length);
  return Array.from({ length }, (_, index) => fieldAdd(left[index] ?? 0, right[index] ?? 0, prime));
}

function polynomialScale(coefficients, scalar, prime) {
  return coefficients.map((coefficient) => fieldMul(coefficient, scalar, prime));
}

function polynomialMul(left, right, prime) {
  const output = Array.from({ length: left.length + right.length - 1 }, () => 0);
  for (let i = 0; i < left.length; i += 1) {
    for (let j = 0; j < right.length; j += 1) {
      output[i + j] = fieldAdd(output[i + j], fieldMul(left[i], right[j], prime), prime);
    }
  }
  return output;
}

function changedRegisters(before, after) {
  return Object.keys(after)
    .filter((name) => before[name] !== after[name])
    .map((name) => `${name}: ${before[name]}->${after[name]}`);
}

function formatStack(values) {
  return `[${values.join(',')}]`;
}

function round(value) {
  return Math.round(value * 100) / 100;
}
