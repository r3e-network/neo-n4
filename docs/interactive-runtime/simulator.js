export const nodes = [
  { id: 'wallet', label: 'User / Wallet', shortLabel: 'Wallet', zh: '用户 / 钱包', shortZh: '钱包', x: 9, y: 18, zone: 'entry', note: 'Signs L1 and L2 intent.' },
  { id: 'neohub', label: 'NeoHub L1', zh: 'NeoHub L1', x: 27, y: 46, zone: 'l1', note: 'Settlement, shared bridge, registries, governance.' },
  { id: 'gateway', label: 'Neo Gateway', zh: 'Neo Gateway', x: 47, y: 28, zone: 'gateway', note: 'Optional proof aggregation layer.' },
  { id: 'prover', label: 'Prover / DA', zh: '证明者 / DA', x: 48, y: 67, zone: 'proof', note: 'Multisig, optimistic, RISC-V, SP1, and DA publication.' },
  { id: 'l2node', label: 'L2 node', zh: 'L2 节点', x: 70, y: 34, zone: 'l2', note: 'Neo 4 execution kernel plus L2 plugins.' },
  { id: 'native', label: 'L2 native contracts', shortLabel: 'L2 native', zh: 'L2 原生合约', shortZh: '原生合约', x: 84, y: 58, zone: 'l2', note: 'Genesis-registered bridge, fee, message, paymaster, AA, interop.' },
  { id: 'watcher', label: 'Bridge watcher', shortLabel: 'Watcher', zh: '桥中继器', shortZh: '中继器', x: 23, y: 78, zone: 'watcher', note: 'Observed external-chain events, journals, committee proofs.' },
  { id: 'foreign', label: 'Foreign chain', shortLabel: 'External', zh: '外部链', shortZh: '外链', x: 8, y: 72, zone: 'foreign', note: 'EVM-family, Tron, or Solana router.' },
];

const nodeById = Object.fromEntries(nodes.map((node) => [node.id, node]));

const baseState = Object.freeze({
  ledger: {
    l1Escrow: 0,
    l2Credit: 0,
    withdrawalQueue: 0,
    foreignEscrow: 0,
  },
  batch: {
    number: 42,
    txs: 0,
    stateRoot: '0x8f1d...a40c',
    status: 'open',
  },
  proof: {
    mode: 'multisig / optimistic / zk',
    status: 'idle',
    artifacts: 0,
  },
  gateway: {
    queue: 0,
    aggregateRoot: '0x0000...0000',
  },
  security: {
    challengeWindow: 'idle',
    alarms: 0,
    finality: 'unsettled',
  },
  counters: {
    messages: 0,
    packets: 0,
  },
  timeline: [],
});

const scenarios = [
  {
    id: 'deposit',
    title: 'L1 to L2 deposit',
    zhTitle: 'L1 到 L2 充值',
    summary: 'A user locks value in NeoHub, a canonical message enters the L2 inbox, and the L2 native bridge mints credit at genesis-level contract state.',
    zhSummary: '用户在 NeoHub 锁定资产，规范消息进入 L2 inbox，L2 原生桥在 genesis 级合约状态里铸造信用。',
    objective: 'Move 100 units from L1 escrow to L2 credit without deploying an L2 contract.',
    events: [
      event('deposit-sign', 'Wallet signs deposit intent', 'wallet', 'neohub', 'Intent', 'NEP-17 amount=100', { counters: { messages: 1 } }),
      event('deposit-lock', 'SharedBridge locks asset', 'neohub', 'neohub', 'Escrow', 'l1Escrow +100', { ledger: { l1Escrow: 100 } }),
      event('deposit-inbox', 'MessageRouter emits L2 inbox payload', 'neohub', 'l2node', 'Inbox', 'chainId=1099 nonce=77', { counters: { messages: 2 } }),
      event('deposit-mint', 'L2BridgeContract mints wrapped asset', 'l2node', 'native', 'Mint', 'l2Credit +100', { ledger: { l2Credit: 100 }, batch: { txs: 1 } }),
    ],
  },
  {
    id: 'batch',
    title: 'Batch lifecycle',
    zhTitle: '批次生命周期',
    summary: 'The sequencer orders L2 transactions, native contracts update execution state, and a sealed batch emits a new state root.',
    zhSummary: '排序器给 L2 交易排序，原生合约更新执行状态，封闭批次产出新的 state root。',
    objective: 'Turn an open L2 batch into a sealed batch with a deterministic post-state root.',
    events: [
      event('batch-order', 'Sequencer orders L2 transactions', 'wallet', 'l2node', 'Tx list', '12 txs ordered', { batch: { txs: 12, status: 'executing' } }),
      event('batch-execute', 'Neo 4 execution kernel applies txs', 'l2node', 'native', 'Execution', 'native hooks + plugins', { counters: { messages: 3 } }),
      event('batch-da', 'Batch data is published to DA', 'l2node', 'prover', 'DA blob', 'calldata / NeoFS / DAC', { proof: { artifacts: 1 } }),
      event('batch-seal', 'Batch is sealed with post-state root', 'prover', 'l2node', 'State root', '0x43b7...9f22', { batch: { number: 43, stateRoot: '0x43b7...9f22', status: 'sealed' }, security: { finality: 'awaiting L1 settlement' } }),
    ],
  },
  {
    id: 'proof',
    title: 'Proof aggregation',
    zhTitle: '证明聚合',
    summary: 'A sealed batch becomes public inputs, the prover creates evidence, and Neo Gateway can aggregate many L2 proofs before L1 settlement.',
    zhSummary: '封闭批次转为 public inputs，证明者生成证据，Neo Gateway 可先聚合多条 L2 的证明再提交 L1。',
    objective: 'Aggregate and settle a proof without changing the L2 execution result.',
    events: [
      event('proof-input', 'Batch public inputs are hashed', 'l2node', 'prover', 'Public input', 'stateRoot + txRoot + msgRoot', { proof: { status: 'proving', artifacts: 1 } }),
      event('proof-create', 'Prover emits proof artifact', 'prover', 'gateway', 'Proof', 'multisig / optimistic / SP1', { gateway: { queue: 1 }, proof: { artifacts: 2 } }),
      event('proof-fold', 'Gateway folds proof', 'gateway', 'gateway', 'Aggregate', 'root=0x9ac0...53de', { gateway: { aggregateRoot: '0x9ac0...53de' } }),
      event('proof-settle', 'SettlementManager accepts batch', 'gateway', 'neohub', 'Settlement', 'batch #43 accepted', { gateway: { queue: 0 }, proof: { status: 'accepted' }, security: { finality: 'settled' } }),
    ],
  },
  {
    id: 'withdrawal',
    title: 'L2 to L1 withdrawal',
    zhTitle: 'L2 到 L1 提现',
    summary: 'The L2 native bridge burns or locks credit, the withdrawal root enters a settled batch, and NeoHub releases value on L1.',
    zhSummary: 'L2 原生桥销毁或锁定信用，提现 root 进入已结算批次，NeoHub 在 L1 释放资产。',
    objective: 'Prove an L2 exit and release value from the L1 shared bridge.',
    events: [
      event('withdraw-burn', 'L2BridgeContract burns withdrawal credit', 'wallet', 'native', 'Burn', 'l2Credit -40', { ledger: { l2Credit: -40, withdrawalQueue: 40 }, batch: { txs: 3 } }),
      event('withdraw-root', 'Withdrawal root enters sealed batch', 'native', 'prover', 'Merkle leaf', 'withdrawalRoot', { proof: { status: 'proving', artifacts: 1 } }),
      event('withdraw-finality', 'Proof finalizes exit on L1', 'prover', 'neohub', 'Exit proof', 'batch proof + inclusion path', { proof: { status: 'accepted' }, security: { finality: 'exit final' } }),
      event('withdraw-release', 'SharedBridge releases L1 asset', 'neohub', 'wallet', 'Release', 'l1Escrow -40', { ledger: { l1Escrow: -40, withdrawalQueue: -40 } }),
    ],
  },
  {
    id: 'external',
    title: 'Foreign-chain bridge',
    zhTitle: '外链桥',
    summary: 'Foreign routers emit lock events, watchers build committee proofs, and NeoHub routes the canonical message into the same L2 bridge model.',
    zhSummary: '外部链 router 发出锁定事件，watcher 构造委员会证明，NeoHub 把规范消息路由进同一个 L2 桥模型。',
    objective: 'Bring EVM/Tron/Solana value into NeoHub with replay-resistant committee evidence.',
    events: [
      event('foreign-lock', 'Foreign router locks asset', 'wallet', 'foreign', 'Lock', 'externalChainId=EVM-family', { ledger: { foreignEscrow: 75 } }),
      event('watcher-observe', 'Watcher journals confirmed event', 'foreign', 'watcher', 'Log event', 'min_confirmations satisfied', { counters: { messages: 1 } }),
      event('watcher-proof', 'Committee proof is assembled', 'watcher', 'neohub', 'Committee proof', 'threshold signatures', { proof: { status: 'verified', artifacts: 1 } }),
      event('external-route', 'NeoHub routes canonical bridge message', 'neohub', 'native', 'Bridge message', 'foreign lock -> L2 mint', { ledger: { l2Credit: 75 }, counters: { messages: 2 } }),
    ],
  },
  {
    id: 'challenge',
    title: 'Challenge and recovery',
    zhTitle: '挑战与恢复',
    summary: 'If a bad batch or committee proof appears, the challenge path freezes unsafe finality, verifies evidence, and either slashes or resumes.',
    zhSummary: '如果出现错误批次或委员会证明，挑战路径会冻结不安全 finality，验证证据，并执行罚没或恢复。',
    objective: 'Detect an invalid transition before finality and return the system to a safe state.',
    events: [
      event('challenge-alert', 'Audit detects inconsistent transition', 'l2node', 'prover', 'Alert', 'preRoot != previous postRoot', { security: { alarms: 1, challengeWindow: 'open' } }),
      event('challenge-file', 'Challenger files evidence on NeoHub', 'wallet', 'neohub', 'Challenge', 'fraud proof payload', { security: { alarms: 2 } }),
      event('challenge-verify', 'VerifierRegistry checks evidence', 'neohub', 'prover', 'Verification', 'bisection / verifier / committee fraud', { proof: { status: 'disputed' } }),
      event('challenge-resolve', 'EmergencyManager restores safe route', 'neohub', 'l2node', 'Recovery', 'unsafe batch rejected', { proof: { status: 'rejected' }, security: { challengeWindow: 'resolved', finality: 'safe route restored' } }),
    ],
  },
];

const scenarioById = Object.fromEntries(scenarios.map((scenario) => [scenario.id, scenario]));

export function listScenarios() {
  return scenarios.map(({ events, ...scenario }) => ({ ...scenario, eventCount: events.length }));
}

export function getScenario(id) {
  const scenario = scenarioById[id];
  if (!scenario) throw new Error(`Unknown scenario: ${id}`);
  return structuredClone(scenario);
}

export function createInitialState() {
  return structuredClone(baseState);
}

export function applyEvent(state, event) {
  const next = structuredClone(state);
  mergeEffects(next, event.effects ?? {});
  next.counters.packets += 1;
  next.timeline.push({
    id: event.id,
    label: event.label,
    description: event.description,
    packet: event.packet,
  });
  return next;
}

export function runScenario(id, untilIndex = undefined) {
  const scenario = getScenario(id);
  const limit = untilIndex === undefined ? scenario.events.length : Math.max(0, Math.min(untilIndex, scenario.events.length));
  let state = createInitialState();
  for (let i = 0; i < limit; i += 1) {
    state = applyEvent(state, scenario.events[i]);
  }
  return state;
}

export function routeForEvent(event) {
  const from = nodeById[event.fromId];
  const to = nodeById[event.toId];
  if (!from || !to) throw new Error(`Event ${event.id} has an unknown route`);
  return { from, to };
}

function event(id, label, fromId, toId, type, payload, effects) {
  const from = nodeById[fromId];
  const to = nodeById[toId];
  return {
    id,
    label,
    description: describe(label, payload),
    fromId,
    toId,
    packet: {
      from: from.label,
      to: to.label,
      type,
      payload,
    },
    effects,
  };
}

function describe(label, payload) {
  return `${label}. Payload: ${payload}.`;
}

function mergeEffects(target, effects) {
  for (const [section, values] of Object.entries(effects)) {
    if (!target[section]) target[section] = {};
    for (const [key, value] of Object.entries(values)) {
      if (isAdditiveSection(section) && typeof value === 'number' && typeof target[section][key] === 'number') {
        target[section][key] += value;
      } else {
        target[section][key] = value;
      }
    }
  }
}

function isAdditiveSection(section) {
  return section === 'ledger' || section === 'counters';
}
