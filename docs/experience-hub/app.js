import { sampleReports } from './data/sampleReports.js';
import {
  activeWorkspace,
  architectureLinks,
  architectureNodes,
  createHubState,
  reportTimeline,
  selectNode,
  selectWorkspace,
  selectedNode,
  summarizeEvidence,
  topMetrics,
  workspaces,
} from './hubState.js';

const els = {
  nav: document.querySelector('[data-workspace-nav]'),
  topStatus: document.querySelector('[data-top-status]'),
  workspaceTitle: document.querySelector('[data-workspace-title]'),
  workspaceSummary: document.querySelector('[data-workspace-summary]'),
  publicBoundary: document.querySelector('[data-public-boundary]'),
  architectureMap: document.querySelector('[data-architecture-map]'),
  nodeInspector: document.querySelector('[data-node-inspector]'),
  evidenceList: document.querySelector('[data-evidence-list]'),
  evidenceScope: document.querySelector('[data-evidence-scope]'),
  evidenceStatus: document.querySelector('[data-evidence-status]'),
  reportTimeline: document.querySelector('[data-report-timeline]'),
  schemaSummary: document.querySelector('[data-schema-summary]'),
  testSummary: document.querySelector('[data-test-summary]'),
  resetNode: document.querySelector('[data-action="reset-node"]'),
};

let state = createHubState({ reports: sampleReports });

const manifest = await loadManifest();
state = { ...state, manifest };

render();

els.resetNode.addEventListener('click', () => {
  state = selectNode(state, 'neohub');
  renderArchitecture();
  renderInspector();
});

async function loadManifest() {
  try {
    const response = await fetch('./data/neo-n4.manifest.json');
    if (!response.ok) return undefined;
    return response.json();
  } catch {
    return undefined;
  }
}

function render() {
  renderNavigation();
  renderTopStatus();
  renderWorkspace();
  renderArchitecture();
  renderInspector();
  renderTimeline();
  renderSummaries();
}

function renderNavigation() {
  els.nav.replaceChildren(...workspaces.map((workspace) => {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'workspace-button';
    button.dataset.workspaceId = workspace.id;
    button.setAttribute('aria-pressed', String(workspace.id === state.activeWorkspace));
    button.innerHTML = `
      <span class="workspace-key">${workspace.label.slice(0, 1)}</span>
      <span>
        <strong>${workspace.label}</strong>
        <small>${workspace.title}</small>
      </span>
    `;
    button.addEventListener('click', () => {
      state = selectWorkspace(state, workspace.id);
      renderNavigation();
      renderWorkspace();
    });
    return button;
  }));
}

function renderTopStatus() {
  els.topStatus.replaceChildren(...topMetrics(state.reports).map((metric) => {
    const item = document.createElement('div');
    item.className = `metric ${metric.tone ?? ''}`;
    item.innerHTML = `<span>${metric.label}</span><strong>${metric.value}</strong>`;
    return item;
  }));
}

function renderWorkspace() {
  const workspace = activeWorkspace(state);
  els.workspaceTitle.textContent = workspace.title;
  els.workspaceSummary.textContent = workspace.summary;
  const evidence = summarizeEvidence(state.reports);
  els.publicBoundary.textContent = evidence.hasPublicNetworkEvidence
    ? 'Public network evidence present'
    : 'Private devnet evidence only';
}

function renderArchitecture() {
  const linkLayer = document.createElement('svg');
  linkLayer.className = 'link-layer';
  linkLayer.setAttribute('viewBox', '0 0 100 100');
  linkLayer.setAttribute('aria-hidden', 'true');
  linkLayer.append(...architectureLinks.map(([fromId, toId, label]) => {
    const from = architectureNodes.find((node) => node.id === fromId);
    const to = architectureNodes.find((node) => node.id === toId);
    const line = svg('line', {
      x1: from.x + 7,
      y1: from.y + 6,
      x2: to.x + 7,
      y2: to.y + 6,
      class: `data-link ${from.kind} ${to.kind}`,
    });
    line.append(svg('title', {}, label));
    return line;
  }));

  const nodes = architectureNodes.map((node) => {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = `architecture-node ${node.kind}`;
    button.style.left = `${node.x}%`;
    button.style.top = `${node.y}%`;
    button.dataset.nodeId = node.id;
    button.setAttribute('aria-pressed', String(node.id === state.selectedNodeId));
    button.innerHTML = `
      <strong>${node.label}</strong>
      <span>${node.subtitle}</span>
    `;
    button.addEventListener('click', () => {
      state = selectNode(state, node.id);
      renderArchitecture();
      renderInspector();
    });
    return button;
  });

  els.architectureMap.replaceChildren(linkLayer, ...nodes);
}

function renderInspector() {
  const node = selectedNode(state);
  const evidence = summarizeEvidence(state.reports);
  els.evidenceScope.textContent = evidence.hasPublicNetworkEvidence
    ? 'Public evidence is loaded.'
    : 'Loaded evidence is scoped to a local private network.';
  els.evidenceStatus.textContent = evidence.validationStatus === 'valid' ? 'Valid' : 'Attention';
  els.evidenceStatus.className = `status-badge ${evidence.validationStatus}`;

  els.nodeInspector.innerHTML = `
    <h3>${node.label}</h3>
    <p>${node.boundary}</p>
    <ul>${node.details.map((item) => `<li>${item}</li>`).join('')}</ul>
  `;

  const reports = [
    ['Latest proof', 'NativeZkVerifier path through L1 native accelerator'],
    ['Data Availability (NeoFS)', `${state.reports['neofs-da-report'].payload.readCheck} read, ${state.reports['neofs-da-report'].payload.writeCheck} write`],
    ['Deployment boundary', 'NeoHub is represented as deployed L1 contracts'],
    ['Browser boundary', 'Read-only report and RPC surface'],
  ];

  els.evidenceList.replaceChildren(...reports.map(([label, value]) => {
    const item = document.createElement('div');
    item.className = 'evidence-item';
    item.innerHTML = `<span>${label}</span><strong>${value}</strong>`;
    return item;
  }));
}

function renderTimeline() {
  els.reportTimeline.replaceChildren(...reportTimeline(state.reports).map((item) => {
    const row = document.createElement('li');
    row.innerHTML = `<strong>${item.label}</strong><span>${item.detail}</span>`;
    return row;
  }));
}

function renderSummaries() {
  const manifestSummary = state.manifest
    ? `${state.manifest.contracts.length} NeoHub contracts / ${state.manifest.tools.length} tools`
    : 'manifest unavailable';
  renderDefinitionList(els.schemaSummary, [
    ['Schema version', '1.0.0'],
    ['Manifest', manifestSummary],
    ['DA provider', state.reports['neofs-da-report'].payload.provider],
    ['Default VM', state.reports['chain-config-report'].payload.vmProfile],
  ]);

  const evidence = summarizeEvidence(state.reports);
  renderDefinitionList(els.testSummary, [
    ['Total tests', String(evidence.testsTotal)],
    ['Passed', String(evidence.testsPassed)],
    ['Failed', String(evidence.testsFailed)],
    ['Success rate', `${evidence.successRate}%`],
  ]);
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

function svg(name, attrs = {}, text = undefined) {
  const el = document.createElementNS('http://www.w3.org/2000/svg', name);
  for (const [key, value] of Object.entries(attrs)) el.setAttribute(key, value);
  if (text !== undefined) el.textContent = text;
  return el;
}
