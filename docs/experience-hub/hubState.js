export const workspaceIds = Object.freeze(['learn', 'build', 'operate', 'verify']);

export const workspaces = Object.freeze([
  {
    id: 'learn',
    label: 'Learn',
    title: 'Architecture and workflow map',
    summary: 'Read the Neo N4 stack through the same boundaries used by deployment, settlement, bridge, DA, and proof verification.',
    actions: ['Open Runtime Theater', 'Inspect NeoHub contracts', 'Trace deposit to withdrawal'],
  },
  {
    id: 'build',
    label: 'Build',
    title: 'Developer integration surface',
    summary: 'Use shared report schemas, asset routes, wire formats, and SDK examples without confusing Neo Stack execution profiles with NeoX.',
    actions: ['Review asset catalog', 'Check VM profile model', 'Open SDK examples'],
  },
  {
    id: 'operate',
    label: 'Operate',
    title: 'Private devnet cockpit',
    summary: 'Track L1, L2, NeoFS DA, Gateway, bridge, batcher, prover, and watcher status from generated reports.',
    actions: ['Review devnet status', 'Inspect NeoFS DA', 'Run bridge drills'],
  },
  {
    id: 'verify',
    label: 'Verify',
    title: 'Validation evidence center',
    summary: 'Separate local private-network evidence from real public network evidence and keep redacted report data inspectable.',
    actions: ['Review validation report', 'Check redaction', 'Export audit packet'],
  },
]);

export const architectureNodes = Object.freeze([
  {
    id: 'neohub',
    label: 'NeoHub L1',
    subtitle: 'Deployed contracts',
    kind: 'l1',
    boundary: 'deployable L1 contracts',
    x: 8,
    y: 15,
    details: ['ChainRegistry', 'TokenRegistry', 'SharedBridge', 'SettlementManager'],
  },
  {
    id: 'native-zk',
    label: 'NativeZkVerifier',
    subtitle: 'Verifier adapter',
    kind: 'proof',
    boundary: 'deployable verifier adapter',
    x: 42,
    y: 15,
    details: ['Proof envelope', 'VK id', 'Public input hash', 'Accelerator ABI'],
  },
  {
    id: 'zk-accelerator',
    label: 'L1 Native ZK Accelerator',
    subtitle: 'Native proof math',
    kind: 'proof',
    boundary: 'native accelerator hook',
    x: 68,
    y: 15,
    details: ['BN254', 'Pairing checks', 'Curve ops', 'Optimized verifier'],
  },
  {
    id: 'shared-bridge',
    label: 'SharedBridge',
    subtitle: 'L1 <-> L2 bridge',
    kind: 'bridge',
    boundary: 'L1 asset custody',
    x: 8,
    y: 48,
    details: ['Lock or mint', 'Burn or unlock', 'Message relay', 'Replay protection'],
  },
  {
    id: 'gateway',
    label: 'Gateway',
    subtitle: 'Aggregation and routing',
    kind: 'gateway',
    boundary: 'optional proof aggregation',
    x: 32,
    y: 48,
    details: ['JSON-RPC', 'WebSocket', 'Indexer feed', 'Health checks'],
  },
  {
    id: 'neofs-da',
    label: 'NeoFS DA',
    subtitle: 'Data availability',
    kind: 'da',
    boundary: 'data availability',
    x: 42,
    y: 76,
    details: ['Object id', 'DA commitment', 'Read check', 'Retention policy'],
  },
  {
    id: 'l2-riscv',
    label: 'NeoVM2 / RISC-V',
    subtitle: 'N4 L2 execution',
    kind: 'l2',
    boundary: 'canonical L2 execution profile',
    x: 57,
    y: 48,
    details: ['Execution engine', 'L2 native contracts', 'State manager', 'Receipts'],
  },
  {
    id: 'optional-vm',
    label: 'Optional VM Profiles',
    subtitle: 'Extension points',
    kind: 'extension',
    boundary: 'pluggable N4 L2 execution profiles',
    x: 75,
    y: 64,
    details: ['EVM profile', 'WASM profile', 'Move profile', 'Custom profile'],
  },
]);

export const architectureLinks = Object.freeze([
  ['neohub', 'native-zk', 'State root commitment'],
  ['native-zk', 'zk-accelerator', 'Proof dispatch'],
  ['zk-accelerator', 'l2-riscv', 'Verified proof'],
  ['neohub', 'shared-bridge', 'Messages and events'],
  ['shared-bridge', 'gateway', 'Bridge route'],
  ['gateway', 'l2-riscv', 'Transactions and queries'],
  ['l2-riscv', 'neofs-da', 'Witness and data commitments'],
  ['neofs-da', 'l2-riscv', 'Available data'],
  ['optional-vm', 'l2-riscv', 'Profile adapter'],
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

export function selectNode(state, nodeId) {
  if (!architectureNodes.some((node) => node.id === nodeId)) throw new Error(`Unknown architecture node ${nodeId}`);
  return { ...state, selectedNodeId: nodeId };
}

export function activeWorkspace(state) {
  return workspaces.find((workspace) => workspace.id === state.activeWorkspace) ?? workspaces[0];
}

export function selectedNode(state) {
  return architectureNodes.find((node) => node.id === state.selectedNodeId) ?? architectureNodes[0];
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
    testsTotal: validation.total ?? 0,
    testsPassed: validation.passed ?? 0,
    testsFailed: validation.failed ?? 0,
    successRate: validation.successRate ?? 0,
  };
}

export function topMetrics(reports) {
  const devnet = reports['devnet-report']?.payload ?? {};
  const validation = summarizeEvidence(reports);
  return [
    { label: 'Private Devnet', value: 'Running', tone: 'good' },
    { label: 'L2 Block Height', value: String(devnet.latestL2Block ?? '-') },
    { label: 'Prover Status', value: serviceStatus(devnet, 'Prover'), tone: 'good' },
    { label: 'DA (NeoFS)', value: serviceStatus(devnet, 'NeoFS DA service'), tone: 'good' },
    { label: 'Gateway', value: serviceStatus(devnet, 'Gateway'), tone: 'good' },
    { label: 'Evidence', value: validation.validationStatus === 'valid' ? 'Valid' : 'Attention', tone: validation.validationStatus === 'valid' ? 'good' : 'warn' },
  ];
}

export function reportTimeline(reports) {
  const receipt = reports['deployment-receipt']?.payload?.receipts ?? [];
  const da = reports['neofs-da-report']?.payload ?? {};
  return [
    { label: 'Contracts planned', detail: 'NeoHub deployment bundle' },
    { label: 'Contracts deployed', detail: `${receipt.length} receipt records` },
    { label: 'DA published', detail: `${da.provider ?? 'NeoFS'} ${da.readCheck ?? 'unknown'}` },
    { label: 'Proof verified', detail: 'NativeZkVerifier path' },
    { label: 'Reports checked', detail: 'Redacted v1 schema' },
  ];
}

function serviceStatus(devnet, serviceName) {
  const service = devnet.services?.find((item) => item.name === serviceName);
  return service?.status ?? 'unknown';
}
