// The model finder's filter and sort rules, and the page that uses them. Plan: plans/model-finder.md § S1.
// The rules are pure exports so web/model-finder.test.mjs can run them under node; the page code at the end runs
// only in a browser.

export const ENGINES = ['router', 'strata', 'ninfer'];

export function defaultFilters() {
  return { minContext: 0, vision: false, gpu1Only: false, engines: [...ENGINES],
    thinking: 'any', hideCodeOnly: false, minSpeed: 0 };
}

const THINKS = new Set(['on', 'always']);

// The first filter an entry fails, or null when it fits. The order is the order of the filter panel.
export function whyHidden(e, filters) {
  if (e.context < filters.minContext) return 'context';
  if (filters.vision && !e.vision) return 'images';
  if (filters.gpu1Only && e.gpus !== 'gpu1') return 'gpus';
  if (!filters.engines.includes(e.engine)) return 'engine';
  if (filters.thinking !== 'any' && (filters.thinking === 'thinks') !== THINKS.has(e.thinking)) return 'thinking';
  if (filters.hideCodeOnly && e.codeOnly) return 'codeOnly';
  if (filters.minSpeed && (e.speed?.code ?? 0) < filters.minSpeed) return 'speed';
  return null;
}

export function filterModels(entries, filters) {
  return entries.filter(e => whyHidden(e, filters) === null);
}

// Each key reads one number, larger first; a missing number sorts last. Name sorts by id.
const SORT_VALUES = {
  speed: e => e.speed?.code,
  context: e => e.context,
};

export function sortModels(entries, key) {
  const byId = (a, b) => a.id.localeCompare(b.id);
  const value = SORT_VALUES[key];
  if (!value) return [...entries].sort(byId);
  return [...entries].sort((a, b) => {
    const va = value(a), vb = value(b);
    if (va == null || vb == null) return (va == null) - (vb == null) || byId(a, b);
    return vb - va || byId(a, b);
  });
}

// A typed K or M is read as 1000 or 1,000,000: the docs write 262144 both as "262k" and "256K", and a decimal
// threshold keeps the model either way.
export function parseContext(text) {
  const m = String(text).trim().replace(/[,_\s]/g, '').match(/^(\d+(?:\.\d+)?)([km]?)$/i);
  if (!m) return null;
  const scale = { '': 1, k: 1000, m: 1000000 }[m[2].toLowerCase()];
  return Math.round(parseFloat(m[1]) * scale);
}

export function formatContext(tokens) {
  const trim = n => String(Math.round(n * 10) / 10);
  if (tokens >= 1048576) return trim(tokens / 1048576) + 'M';
  return trim(tokens / 1024) + 'K';
}

// "engine:modelId" for each model an engine is serving now, from the tray's status.json.
export function loadedIds(status) {
  const ids = new Set();
  if (!status) return ids;
  const { router, strata, ninfer } = status;
  if (router?.model && router.model_status === 'loaded') ids.add('router:' + router.model);
  if (strata?.model && strata.state === 'Loaded') ids.add('strata:' + strata.model);
  if (ninfer?.model && ninfer.state === 'Ready') ids.add('ninfer:' + ninfer.model);
  return ids;
}

// ---- The page ----

const STORE_KEY = 'model-finder.filters';
const CONTEXT_MIN_LOG = 13, CONTEXT_MAX_LOG = 20;   // the slider runs 8K to 1M on a log2 scale
const ENGINE_NAMES = { router: 'Router', strata: 'Strata', ninfer: 'ninfer' };
const THINKING_NAMES = { on: 'Thinks', always: 'Always reasons', off: 'Off by default', none: 'Never' };
const HIDDEN_REASONS = {
  context: e => 'context ' + formatContext(e.context),
  images: () => 'no images',
  gpus: () => 'needs both GPUs',
  engine: e => ENGINE_NAMES[e.engine] + ' is unticked',
  thinking: e => THINKING_NAMES[e.thinking].toLowerCase(),
  codeOnly: () => 'code-only drafter',
  speed: e => e.speed?.code ? e.speed.code + ' t/s on code' : 'speed not measured',
};

function sliderToContext(position) {
  const log = CONTEXT_MIN_LOG + (CONTEXT_MAX_LOG - CONTEXT_MIN_LOG) * position / 1000;
  return Math.round(2 ** log / 1024) * 1024;   // whole K, so the label reads cleanly
}

function contextToSlider(tokens) {
  if (!tokens) return 0;
  const log = Math.log2(Math.min(Math.max(tokens, 2 ** CONTEXT_MIN_LOG), 2 ** CONTEXT_MAX_LOG));
  return Math.round((log - CONTEXT_MIN_LOG) / (CONTEXT_MAX_LOG - CONTEXT_MIN_LOG) * 1000);
}

function startPage() {
  const $ = id => document.getElementById(id);
  const saved = JSON.parse(localStorage.getItem(STORE_KEY) || 'null');
  const state = { filters: { ...defaultFilters(), ...(saved?.filters ?? {}) }, sort: saved?.sort ?? 'speed',
    gpuChosen: saved?.gpuChosen ?? false, entries: [], loaded: new Set(), status: null };

  function save() {
    localStorage.setItem(STORE_KEY, JSON.stringify({ filters: state.filters, sort: state.sort, gpuChosen: state.gpuChosen }));
  }

  function cell(tag, text, className) {
    const el = document.createElement(tag);
    if (text != null) el.textContent = text;
    if (className) el.className = className;
    return el;
  }

  function row(e, reason) {
    const tr = cell('tr');
    const name = cell('td', null, 'name');
    const title = cell('div', null, 'id');
    title.append(cell('span', e.id));
    if (state.loaded.has(e.engine + ':' + e.modelId)) title.append(cell('span', 'loaded', 'loaded'));
    name.append(title, cell('div', e.description, 'desc'));
    if (reason) name.append(cell('div', HIDDEN_REASONS[reason](e), 'reason'));
    tr.append(name,
      cell('td', ENGINE_NAMES[e.engine]),
      cell('td', formatContext(e.context), 'num'),
      cell('td', e.vision ? 'yes' : 'no', e.vision ? '' : 'muted'),
      cell('td', e.gpus === 'gpu1' ? 'GPU 1' : 'both'),
      cell('td', THINKING_NAMES[e.thinking]),
      cell('td', e.speed?.code ?? '–', 'num'),
      cell('td', e.speed?.prose ?? '–', 'num'));
    if (e.codeOnly) tr.classList.add('code-only');
    return tr;
  }

  function render() {
    const f = state.filters;
    // What was typed stays as typed ("262k"); the slider writes its own label.
    const typed = $('context-value').value.trim();
    if ((parseContext(typed) ?? 0) !== f.minContext) $('context-value').value = f.minContext ? formatContext(f.minContext) : '';
    $('context-slider').value = contextToSlider(f.minContext);
    $('vision').checked = f.vision;
    $('gpu-any').checked = !f.gpu1Only;
    $('gpu-1').checked = f.gpu1Only;
    for (const engine of ENGINES) $('engine-' + engine).checked = f.engines.includes(engine);
    $('thinking-' + f.thinking).checked = true;
    $('code-only').checked = f.hideCodeOnly;
    $('min-speed').value = f.minSpeed || '';
    $('sort').value = state.sort;
    $('reserved').hidden = !state.status?.reserve_gpu0;

    const fitting = sortModels(filterModels(state.entries, f), state.sort);
    const rest = sortModels(state.entries.filter(e => whyHidden(e, f) !== null), state.sort);
    $('fit').replaceChildren(...fitting.map(e => row(e)));
    $('nofit').replaceChildren(...rest.map(e => row(e, whyHidden(e, f))));
    $('count').textContent = fitting.length + ' of ' + state.entries.length + ' fit';
    $('empty').hidden = fitting.length > 0;
    $('nofit-section').hidden = rest.length === 0;
    $('nofit-count').textContent = rest.length;
  }

  function update(change) {
    change(state.filters);
    save();
    render();
  }

  $('context-slider').addEventListener('input', ev => {
    const position = Number(ev.target.value);
    update(f => { f.minContext = position === 0 ? 0 : sliderToContext(position); });
  });
  $('context-value').addEventListener('change', ev => {
    const text = ev.target.value.trim();
    const tokens = text === '' || /^any$/i.test(text) ? 0 : parseContext(text);
    if (tokens === null) { ev.target.setCustomValidity('Type a size such as 32k, 262144 or 1M.'); ev.target.reportValidity(); return; }
    ev.target.setCustomValidity('');
    update(f => { f.minContext = tokens; });
  });
  $('vision').addEventListener('change', ev => update(f => { f.vision = ev.target.checked; }));
  for (const id of ['gpu-any', 'gpu-1']) {
    $(id).addEventListener('change', () => { state.gpuChosen = true; update(f => { f.gpu1Only = $('gpu-1').checked; }); });
  }
  for (const engine of ENGINES) {
    $('engine-' + engine).addEventListener('change', () =>
      update(f => { f.engines = ENGINES.filter(name => $('engine-' + name).checked); }));
  }
  for (const value of ['any', 'thinks', 'direct']) {
    $('thinking-' + value).addEventListener('change', () => update(f => { f.thinking = value; }));
  }
  $('code-only').addEventListener('change', ev => update(f => { f.hideCodeOnly = ev.target.checked; }));
  $('min-speed').addEventListener('input', ev => update(f => { f.minSpeed = Math.max(0, Number(ev.target.value) || 0); }));
  $('sort').addEventListener('change', ev => { state.sort = ev.target.value; save(); render(); });
  $('reset').addEventListener('click', () => {
    state.gpuChosen = false;
    update(f => Object.assign(f, defaultFilters(), { gpu1Only: Boolean(state.status?.reserve_gpu0) }));
  });

  async function poll() {
    try {
      const response = await fetch('status.json', { cache: 'no-store' });
      state.status = await response.json();
      state.loaded = loadedIds(state.status);
      // While GPU 0 is reserved only GPU 1 models can start, until the GPU choice is made by hand.
      if (!state.gpuChosen) state.filters.gpu1Only = Boolean(state.status.reserve_gpu0);
      $('offline').hidden = true;
    } catch {
      $('offline').hidden = false;
    }
    render();
  }

  async function loadCatalog() {
    try {
      const response = await fetch('catalog.json', { cache: 'no-store' });
      if (!response.ok) throw new Error(await response.text());
      const catalog = await response.json();
      state.entries = catalog.entries;
      $('error').hidden = true;
    } catch (err) {
      $('error').textContent = 'The model list could not be loaded: ' + err.message;
      $('error').hidden = false;
    }
    render();
  }

  render();
  loadCatalog();
  poll();
  setInterval(poll, 5000);
}

if (typeof document !== 'undefined') startPage();
