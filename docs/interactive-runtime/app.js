import {
  applyEvent,
  createInitialState,
  getScenario,
  listScenarios,
  nodes,
  routeForEvent,
  runScenario,
} from './simulator.js';

const els = {
  scenarioList: document.querySelector('[data-scenario-list]'),
  scene: document.querySelector('[data-scene]'),
  pathLayer: document.querySelector('[data-path-layer]'),
  nodeLayer: document.querySelector('[data-node-layer]'),
  packet: document.querySelector('[data-packet]'),
  eventTitle: document.querySelector('[data-event-title]'),
  eventBody: document.querySelector('[data-event-body]'),
  scenarioTitle: document.querySelector('[data-scenario-title]'),
  scenarioSummary: document.querySelector('[data-scenario-summary]'),
  scenarioObjective: document.querySelector('[data-scenario-objective]'),
  timeline: document.querySelector('[data-timeline]'),
  stateGrid: document.querySelector('[data-state-grid]'),
  play: document.querySelector('[data-action="play"]'),
  step: document.querySelector('[data-action="step"]'),
  reset: document.querySelector('[data-action="reset"]'),
  speed: document.querySelector('[data-speed]'),
  language: document.querySelector('[data-language]'),
  reduced: document.querySelector('[data-reduced]'),
};

let activeScenarioId = 'deposit';
let activeScenario = getScenario(activeScenarioId);
let state = createInitialState();
let eventIndex = 0;
let progress = 0;
let isPlaying = false;
let lastFrame = 0;
let language = 'en';

renderShell();
selectScenario(activeScenarioId);
requestAnimationFrame(tick);

function renderShell() {
  els.scenarioList.replaceChildren(...listScenarios().map((scenario, index) => {
    const button = document.createElement('button');
    button.className = 'scenario-button';
    button.type = 'button';
    button.dataset.scenarioId = scenario.id;
    button.innerHTML = `
      <span class="scenario-index">${String(index + 1).padStart(2, '0')}</span>
      <span>
        <strong>${scenario.title}</strong>
        <small>${scenario.zhTitle}</small>
      </span>
    `;
    button.addEventListener('click', () => selectScenario(scenario.id));
    return button;
  }));

  els.nodeLayer.replaceChildren(...nodes.map((node) => {
    const group = svg('g', { class: `map-node zone-${node.zone}`, transform: `translate(${node.x} ${node.y})`, tabindex: '0' });
    group.append(
      svg('circle', { r: 5.8 }),
      svg('text', { x: 0, y: -8.8, 'text-anchor': 'middle' }, node.shortLabel ?? node.label),
      svg('text', { x: 0, y: 9.8, 'text-anchor': 'middle', class: 'node-subtitle' }, node.shortZh ?? node.zh),
    );
    group.addEventListener('mouseenter', () => showNode(node));
    group.addEventListener('focus', () => showNode(node));
    return group;
  }));

  els.play.addEventListener('click', () => setPlaying(!isPlaying));
  els.step.addEventListener('click', () => advanceStep());
  els.reset.addEventListener('click', () => resetScenario());
  els.speed.addEventListener('input', () => {});
  els.language.addEventListener('change', () => {
    language = els.language.value;
    renderScenarioText();
    renderTimeline();
  });
  els.reduced.addEventListener('change', () => {
    if (els.reduced.checked) {
      progress = 1;
      setPlaying(false);
    }
  });
}

function selectScenario(id) {
  activeScenarioId = id;
  activeScenario = getScenario(id);
  state = createInitialState();
  eventIndex = 0;
  progress = 0;
  setPlaying(false);

  for (const button of els.scenarioList.querySelectorAll('button')) {
    button.classList.toggle('is-active', button.dataset.scenarioId === id);
  }

  renderScenarioText();
  renderPaths();
  renderState();
  renderTimeline();
  renderEvent();
  positionPacket();
}

function resetScenario() {
  selectScenario(activeScenarioId);
}

function setPlaying(next) {
  isPlaying = next;
  els.play.textContent = isPlaying ? 'Pause' : 'Play';
  els.play.setAttribute('aria-pressed', String(isPlaying));
}

function advanceStep() {
  const event = activeScenario.events[eventIndex];
  if (!event) {
    state = runScenario(activeScenarioId);
    renderState();
    setPlaying(false);
    return;
  }

  state = applyEvent(state, event);
  eventIndex += 1;
  progress = 0;
  if (eventIndex >= activeScenario.events.length) setPlaying(false);
  renderState();
  renderTimeline();
  renderEvent();
  renderPaths();
  positionPacket();
}

function tick(timestamp) {
  const delta = lastFrame ? timestamp - lastFrame : 0;
  lastFrame = timestamp;
  if (isPlaying && !els.reduced.checked) {
    const speed = Number(els.speed.value);
    progress += delta / (1500 / speed);
    if (progress >= 1) {
      progress = 1;
      positionPacket();
      advanceStep();
    } else {
      positionPacket();
    }
  }
  requestAnimationFrame(tick);
}

function renderScenarioText() {
  els.scenarioTitle.textContent = language === 'zh' ? activeScenario.zhTitle : activeScenario.title;
  els.scenarioSummary.textContent = language === 'zh' ? activeScenario.zhSummary : activeScenario.summary;
  els.scenarioObjective.textContent = activeScenario.objective;
}

function renderPaths() {
  els.pathLayer.replaceChildren(...activeScenario.events.map((event, index) => {
    const { from, to } = routeForEvent(event);
    const active = index === eventIndex;
    const done = index < eventIndex;
    const path = svg('path', {
      class: `route ${active ? 'is-active' : ''} ${done ? 'is-done' : ''}`,
      d: curve(from, to),
    });
    return path;
  }));
}

function renderEvent() {
  const event = activeScenario.events[eventIndex] ?? activeScenario.events.at(-1);
  if (!event) return;
  const prefix = eventIndex >= activeScenario.events.length ? 'Complete' : `Step ${eventIndex + 1}`;
  els.eventTitle.textContent = `${prefix}: ${event.label}`;
  els.eventBody.textContent = event.description;
}

function renderTimeline() {
  const rows = activeScenario.events.map((event, index) => {
    const row = document.createElement('li');
    row.className = index < eventIndex ? 'is-done' : index === eventIndex ? 'is-active' : '';
    row.innerHTML = `
      <span>${String(index + 1).padStart(2, '0')}</span>
      <strong>${event.label}</strong>
      <small>${event.packet.type}: ${event.packet.from} -> ${event.packet.to}</small>
    `;
    return row;
  });
  els.timeline.replaceChildren(...rows);
}

function renderState() {
  const items = [
    ['L1 escrow', `${state.ledger.l1Escrow}`],
    ['L2 credit', `${state.ledger.l2Credit}`],
    ['Withdrawal queue', `${state.ledger.withdrawalQueue}`],
    ['Foreign escrow', `${state.ledger.foreignEscrow}`],
    ['Platform assets', state.assets.platform.join(', ')],
    ['Decimal policy', Object.entries(state.assets.decimals).map(([asset, policy]) => `${asset} ${policy}`).join(' / ')],
    ['Batch', `#${state.batch.number} / ${state.batch.status}`],
    ['State root', state.batch.stateRoot],
    ['Proof', `${state.proof.status} (${state.proof.artifacts} artifacts)`],
    ['Gateway', `${state.gateway.queue} queued / ${state.gateway.aggregateRoot}`],
    ['Security', `${state.security.challengeWindow}, ${state.security.finality}`],
    ['Messages', `${state.counters.messages}`],
  ];

  els.stateGrid.replaceChildren(...items.flatMap(([label, value]) => {
    const labelEl = document.createElement('dt');
    labelEl.textContent = label;
    const valueEl = document.createElement('dd');
    valueEl.textContent = value;
    return [labelEl, valueEl];
  }));
}

function showNode(node) {
  els.eventTitle.textContent = node.label;
  els.eventBody.textContent = `${node.zh}. ${node.note}`;
}

function positionPacket() {
  const event = activeScenario.events[Math.min(eventIndex, activeScenario.events.length - 1)];
  if (!event) return;
  const { from, to } = routeForEvent(event);
  const eased = ease(progress);
  const x = from.x + (to.x - from.x) * eased;
  const y = from.y + (to.y - from.y) * eased;
  els.packet.style.left = `clamp(66px, ${x}%, calc(100% - 66px))`;
  els.packet.style.top = `clamp(38px, ${y}%, calc(100% - 38px))`;
  els.packet.querySelector('strong').textContent = event.packet.type;
  els.packet.querySelector('span').textContent = event.packet.payload;
}

function curve(from, to) {
  const midX = (from.x + to.x) / 2;
  const midY = (from.y + to.y) / 2 - 10;
  return `M ${from.x} ${from.y} Q ${midX} ${midY} ${to.x} ${to.y}`;
}

function ease(t) {
  return t < 0.5 ? 2 * t * t : 1 - ((-2 * t + 2) ** 2) / 2;
}

function svg(name, attrs = {}, text = undefined) {
  const el = document.createElementNS('http://www.w3.org/2000/svg', name);
  for (const [key, value] of Object.entries(attrs)) el.setAttribute(key, value);
  if (text !== undefined) el.textContent = text;
  return el;
}
