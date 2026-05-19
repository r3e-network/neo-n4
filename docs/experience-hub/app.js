import { sampleReports } from './data/sampleReports.js';
import {
  activeWorkspace,
  architectureLinks,
  architectureNodes,
  bottomStatus,
  createHubState,
  reportTimeline,
  selectNode,
  selectWorkspace,
  selectedNode,
  summarizeEvidence,
  topMetrics,
  validationEvidence,
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
  bottomStatus: document.querySelector('[data-bottom-status]'),
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
  renderBottomStatus();
}

function renderNavigation() {
  els.nav.replaceChildren(...workspaces.map((workspace) => {
    const group = document.createElement('section');
    group.className = 'workspace-group';

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'workspace-button';
    button.dataset.workspaceId = workspace.id;
    button.setAttribute('aria-pressed', String(workspace.id === state.activeWorkspace));
    button.innerHTML = `
      <span class="workspace-key" aria-hidden="true">${workspace.label.slice(0, 1)}</span>
      <span>
        <strong>${workspace.label}</strong>
      </span>
    `;
    button.addEventListener('click', () => {
      state = selectWorkspace(state, workspace.id);
      renderNavigation();
      renderWorkspace();
    });

    const subnav = document.createElement('div');
    subnav.className = 'workspace-subnav';
    subnav.replaceChildren(...workspace.subItems.map((item, index) => {
      const span = document.createElement('span');
      span.textContent = item;
      if (workspace.id === state.activeWorkspace && index === 0) span.className = 'active';
      return span;
    }));

    group.replaceChildren(button, subnav);
    return group;
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
  const linkLayer = svg('svg', {
    class: 'link-layer',
    viewBox: '0 0 100 100',
    'aria-hidden': 'true',
  });
  const defs = svg('defs');
  const marker = svg('marker', {
    id: 'arrowhead',
    markerWidth: '5',
    markerHeight: '5',
    refX: '4',
    refY: '2.5',
    orient: 'auto',
  });
  marker.append(svg('path', { d: 'M0,0 L5,2.5 L0,5 Z', class: 'arrowhead' }));
  defs.append(marker);
  linkLayer.append(defs, ...architectureLinks.flatMap(([fromId, toId, label]) => {
    const from = architectureNodes.find((node) => node.id === fromId);
    const to = architectureNodes.find((node) => node.id === toId);
    const [x1, y1, x2, y2] = linkEndpoint(from, to);
    const line = svg('line', {
      x1,
      y1,
      x2,
      y2,
      class: `data-link ${from.kind} ${to.kind}`,
      'marker-end': 'url(#arrowhead)',
    });
    line.append(svg('title', {}, label));
    const text = svg('text', {
      x: (x1 + x2) / 2,
      y: (y1 + y2) / 2 - 1,
      class: 'link-label',
    }, label);
    return [line, text];
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
      <ul>${node.details.map((item) => `<li>${item}</li>`).join('')}</ul>
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

function linkEndpoint(from, to) {
  const nodeWidth = 16;
  const nodeHeight = 13;
  const fromCenterX = from.x + nodeWidth / 2;
  const fromCenterY = from.y + nodeHeight / 2;
  const toCenterX = to.x + nodeWidth / 2;
  const toCenterY = to.y + nodeHeight / 2;
  const dx = toCenterX - fromCenterX;
  const dy = toCenterY - fromCenterY;

  if (Math.abs(dx) > Math.abs(dy)) {
    return dx > 0
      ? [from.x + nodeWidth, fromCenterY, to.x, toCenterY]
      : [from.x, fromCenterY, to.x + nodeWidth, toCenterY];
  }

  return dy > 0
    ? [fromCenterX, from.y + nodeHeight, toCenterX, to.y]
    : [fromCenterX, from.y, toCenterX, to.y + nodeHeight];
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
    <span>Selected node</span>
    <strong>${node.label}</strong>
    <small>${node.boundary}</small>
  `;

  els.evidenceList.replaceChildren(...validationEvidence(state.reports).map((group) => {
    const section = document.createElement('section');
    section.className = 'evidence-group';
    section.innerHTML = `<h3>${group.title}</h3>`;
    const list = document.createElement('dl');
    list.replaceChildren(...group.rows.flatMap(([label, value]) => {
      const dt = document.createElement('dt');
      dt.textContent = label;
      const dd = document.createElement('dd');
      dd.textContent = value;
      if (/success|valid|verified|healthy|online|synced/i.test(String(value))) dd.className = 'good-value';
      return [dt, dd];
    }));
    section.append(list);
    return section;
  }));
}

function renderTimeline() {
  els.reportTimeline.replaceChildren(...reportTimeline(state.reports).map((item) => {
    const row = document.createElement('li');
    row.className = item.tone ?? '';
    row.innerHTML = `<strong>${item.label}</strong><span>${item.detail}</span><small>${item.time}</small>`;
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

function renderBottomStatus() {
  els.bottomStatus.replaceChildren(...bottomStatus(state.reports).map((item) => {
    const row = document.createElement('span');
    row.className = `footer-status-item ${item.tone ?? ''}`;
    row.innerHTML = `<span>${item.label}</span><strong>${item.value}</strong>`;
    return row;
  }));
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
