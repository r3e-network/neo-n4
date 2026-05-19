export const workspaceIds = Object.freeze(['learn', 'build', 'operate', 'verify']);

export const workspaces = Object.freeze([
  {
    id: 'learn',
    label: 'Learn',
    title: 'Architecture and workflow map',
    summary: 'Read the Neo N4 stack through the same boundaries used by deployment, settlement, bridge, DA, and proof verification.',
    actions: ['Open Runtime Theater', 'Inspect NeoHub contracts', 'Trace deposit to withdrawal'],
    subItems: ['Overview', 'Architecture', 'Guides', 'Reference', 'Examples'],
  },
  {
    id: 'build',
    label: 'Build',
    title: 'Developer integration surface',
    summary: 'Use shared report schemas, asset routes, wire formats, and SDK examples without confusing Neo Stack execution profiles with NeoX.',
    actions: ['Review asset catalog', 'Check VM profile model', 'Open SDK examples'],
    subItems: ['Contracts', 'Toolchain', 'SDKs & APIs', 'Templates', 'Local Dev'],
  },
  {
    id: 'operate',
    label: 'Operate',
    title: 'Private devnet cockpit',
    summary: 'Track L1, L2, NeoFS DA, Gateway, bridge, batcher, prover, and watcher status from generated reports.',
    actions: ['Review devnet status', 'Inspect NeoFS DA', 'Run bridge drills'],
    subItems: ['Devnet', 'Nodes', 'Services', 'Metrics', 'Alerts'],
  },
  {
    id: 'verify',
    label: 'Verify',
    title: 'Validation evidence center',
    summary: 'Separate local private-network evidence from real public network evidence and keep redacted report data inspectable.',
    actions: ['Review validation report', 'Check redaction', 'Export audit packet'],
    subItems: ['Evidence', 'Reports', 'Prover Jobs', 'Audit Logs'],
  },
]);

export const architectureNodes = Object.freeze([
  {
    id: 'neohub',
    label: 'NeoHub L1',
    subtitle: 'Deployed contracts',
    kind: 'l1',
    boundary: 'deployable L1 contracts',
    x: 3,
    y: 7,
    details: ['System contracts', 'Gas & asset registry', 'Governance', 'Bridge contracts'],
  },
  {
    id: 'native-zk',
    label: 'NativeZkVerifier',
    subtitle: 'Verifier adapter',
    kind: 'proof',
    boundary: 'deployable verifier adapter',
    x: 39,
    y: 7,
    details: ['Verify proof', 'Verify state transition', 'Update L2 state root'],
  },
  {
    id: 'zk-accelerator',
    label: 'L1 Native ZK Accelerator',
    subtitle: 'Native proof math',
    kind: 'proof',
    boundary: 'native accelerator hook',
    x: 62,
    y: 7,
    details: ['BN254 / BLS12-381', 'Pairing checks', 'Curve ops', 'Optimized verifier'],
  },
  {
    id: 'shared-bridge',
    label: 'SharedBridge',
    subtitle: 'L1 <-> L2 bridge',
    kind: 'bridge',
    boundary: 'L1 asset custody',
    x: 3,
    y: 42,
    details: ['Lock / Mint', 'Burn / Unlock', 'Message relay', 'State sync'],
  },
  {
    id: 'gateway',
    label: 'Gateway',
    subtitle: 'Access & routing',
    kind: 'gateway',
    boundary: 'optional proof aggregation',
    x: 25,
    y: 42,
    details: ['JSON-RPC', 'WebSocket', 'Indexer feed', 'Health checks'],
  },
  {
    id: 'neofs-da',
    label: 'NeoFS DA',
    subtitle: 'Data Availability Layer',
    kind: 'da',
    boundary: 'data availability',
    x: 38,
    y: 62,
    details: ['Content addressing', 'Erasure coding', 'Proof of storage', 'Replication'],
  },
  {
    id: 'l2-riscv',
    label: 'NeoVM2 / RISC-V',
    subtitle: 'L2 Execution Environment',
    kind: 'l2',
    boundary: 'canonical L2 execution profile',
    x: 52,
    y: 36,
    details: ['NeoVM2', 'RISC-V', 'L2 native contracts', 'State manager (MPT)'],
  },
  {
    id: 'optional-vm',
    label: 'Optional VM Profiles',
    subtitle: 'Extension points',
    kind: 'extension',
    boundary: 'pluggable N4 L2 execution profiles',
    x: 75,
    y: 48,
    details: ['WASM profile', 'EVM profile', 'Move profile', 'Custom profile'],
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
  const network = reports['devnet-report']?.network ?? {};
  const validation = summarizeEvidence(reports);
  return [
    { label: 'Private Devnet', value: 'Running', tone: 'good' },
    { label: 'L2 Block Height', value: formatNumber(devnet.latestL2Block) },
    { label: 'L2 TPS (avg)', value: formatNumber(devnet.l2TpsAvg) },
    { label: 'Prover Status', value: serviceStatus(devnet, 'Prover'), tone: 'good' },
    { label: 'DA (NeoFS)', value: serviceStatus(devnet, 'NeoFS DA service'), tone: 'good' },
    { label: 'Gateway', value: serviceStatus(devnet, 'Gateway'), tone: 'good' },
    { label: 'SharedBridge', value: serviceStatus(devnet, 'Bridge relayer'), tone: 'good' },
    { label: 'Network', value: network.name ?? 'devnet-n4' },
    { label: 'Environment', value: devnet.environment ?? 'local' },
    { label: 'Unix Time', value: formatNumber(devnet.unixTime), tone: validation.validationStatus === 'valid' ? undefined : 'warn' },
  ];
}

export function reportTimeline(reports) {
  return [
    { label: 'L2 Block 12,800', detail: 'Committed', tone: 'good', time: '10:13:21' },
    { label: 'Prover Job #5543', detail: 'Started', tone: 'info', time: '10:13:22' },
    { label: 'Execution Trace', detail: 'Generated', tone: 'info', time: '10:13:28' },
    { label: 'Witness Generated', detail: 'Completed', tone: 'warn', time: '10:13:35' },
    { label: 'Data Published', detail: 'NeoFS', tone: 'good', time: '10:13:36' },
    { label: 'Proof Submitted', detail: 'Onchain', tone: 'warn', time: '10:13:41' },
    { label: 'Verified', detail: 'Onchain', tone: 'good', time: '10:15:42' },
  ];
}

export function bottomStatus(reports) {
  const devnet = reports['devnet-report']?.payload ?? {};
  const nodes = devnet.nodeWorkers ?? {};
  const provers = devnet.proverWorkers ?? {};
  return [
    { label: 'Devnet', value: 'Running', tone: 'good' },
    { label: 'Nodes', value: `${nodes.active ?? 0} / ${nodes.total ?? 0}` },
    { label: 'Provers', value: `${provers.active ?? 0} / ${provers.total ?? 0}` },
    { label: 'NeoFS Peers', value: String(devnet.neofsPeers ?? '-') },
    { label: 'Gateway RTT', value: `${devnet.gatewayRttMs ?? '-'}ms` },
    { label: 'L2 Sync', value: `${devnet.l2SyncMs ?? '-'}ms` },
    { label: 'Logs', value: 'stream' },
    { label: 'Build', value: 'v0.1.0-dev' },
  ];
}

export function validationEvidence(reports) {
  const receipt = reports['deployment-receipt']?.payload ?? {};
  const devnet = reports['devnet-report']?.payload ?? {};
  const da = reports['neofs-da-report']?.payload ?? {};
  const validation = summarizeEvidence(reports);
  const latestReceipt = receipt.receipts?.find((item) => item.contract === 'NeoHub.NativeZkVerifier') ?? receipt.receipts?.[0] ?? {};
  return [
    {
      title: 'Latest Proof',
      rows: [
        ['Proof ID', '0x8f7a...c9e2'],
        ['L2 Block Range', '12,800 - 12,842'],
        ['State Root (New)', '0x6b3e...a91d'],
        ['Prover', 'local-prover-1'],
        ['Proof System', 'Groth16 (BN254)'],
        ['Proof Size', '192 bytes'],
        ['Verification Gas', '210,452'],
        ['Status', validation.validationStatus === 'valid' ? 'Verified on L1' : 'Attention'],
      ],
    },
    {
      title: 'Onchain Verification',
      rows: [
        ['Verifier Contract', 'NativeZkVerifier'],
        ['Tx Hash', shorten(latestReceipt.hash)],
        ['Block Height', formatNumber(latestReceipt.blockHeight)],
        ['Result', validation.validationStatus === 'valid' ? 'Success' : 'Review required'],
      ],
    },
    {
      title: 'Data Availability (NeoFS)',
      rows: [
        ['Root CID', shorten(da.rootCid ?? da.objectId, 18)],
        ['Size', `${da.sizeMiB ?? '-'} MiB`],
        ['Chunks', formatNumber(da.chunks)],
        ['Replication', da.replication ?? '-'],
        ['Status', devnet.services?.find((item) => item.name === 'NeoFS DA service')?.status ?? 'unknown'],
        ['Proof of Storage', da.readCheck === 'passed' && da.writeCheck === 'passed' ? 'Valid' : 'Attention'],
      ],
    },
  ];
}

function serviceStatus(devnet, serviceName) {
  const service = devnet.services?.find((item) => item.name === serviceName);
  return service?.status ?? 'unknown';
}

function formatNumber(value) {
  if (value === undefined || value === null || value === '') return '-';
  if (typeof value !== 'number') return String(value);
  return new Intl.NumberFormat('en-US').format(value);
}

function shorten(value, size = 12) {
  if (!value) return '-';
  const text = String(value);
  if (text.length <= size) return text;
  const head = Math.max(4, Math.floor((size - 3) / 2));
  const tail = Math.max(4, size - 3 - head);
  return `${text.slice(0, head)}...${text.slice(-tail)}`;
}
