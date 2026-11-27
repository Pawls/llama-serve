// The model finder's filter and sort rules, and the page that uses them. Usage: README.md § Using the model finder.
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

// A model's lanes share its family's published scores (model-catalog.json "families"); a fine-tune nobody scored
// has none.
export function scoreOf(e, bench, families) {
  return families?.[e.family]?.scores?.[bench];
}

// The benchmarks that get a column: reported by at least three families, the most reported first.
export function benchmarkColumns(families) {
  const counts = {};
  for (const row of Object.values(families ?? {})) {
    for (const bench of Object.keys(row.scores ?? {})) counts[bench] = (counts[bench] ?? 0) + 1;
  }
  return Object.keys(counts).filter(bench => counts[bench] >= 3)
    .sort((a, b) => counts[b] - counts[a] || a.localeCompare(b));
}

// Each key reads one number, larger first; a missing number sorts last. Name sorts by id.
const SORT_VALUES = {
  speed: e => e.speed?.code,
  context: e => e.context,
};
const BENCH_SORT = 'bench:';   // a sort key for a benchmark is this plus its id

// The benchmark a sort key names, or null for the other sorts.
export function benchOf(key) {
  return key?.startsWith(BENCH_SORT) ? key.slice(BENCH_SORT.length) : null;
}

export function sortModels(entries, key, families) {
  const byId = (a, b) => a.id.localeCompare(b.id);
  const bench = benchOf(key);
  const value = bench ? e => scoreOf(e, bench, families)?.value : SORT_VALUES[key];
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

// The ids of the entries an engine is serving now, from the tray's status.json. Strata sizes can share a model id,
// so a size the tray reports picks one of them; without it every size with that id is marked.
export function loadedIds(status, entries) {
  const ids = new Set();
  if (!status) return ids;
  const { router, strata, ninfer } = status;
  const serving = {
    router: router?.model_status === 'loaded' ? router.model : null,
    strata: strata?.state === 'Loaded' ? strata.model : null,
    ninfer: ninfer?.state === 'Ready' ? ninfer.model : null,
  };
  for (const e of entries) {
    if (!serving[e.engine] || e.modelId !== serving[e.engine]) continue;
    if (e.engine === 'strata' && strata.size && e.id !== strata.size) continue;
    ids.add(e.id);
  }
  return ids;
}

const THINKING_NAMES = { on: 'Thinks', always: 'Always reasons', off: 'Off by default', none: 'Never' };

// The label for an entry's thinking value; one the page does not know is shown as written.
export function thinkingName(value) {
  return THINKING_NAMES[value] ?? value;
}

// ---- The page ----

const STORE_KEY = 'model-finder.filters';
const CONTEXT_MIN_LOG = 13, CONTEXT_MAX_LOG = 20;   // the slider runs 8K to 1M on a log2 scale
const ENGINE_NAMES = { router: 'Router', strata: 'Strata', ninfer: 'ninfer' };
const HIDDEN_REASONS = {
  context: e => 'context ' + formatContext(e.context),
  images: () => 'no images',
  gpus: () => 'needs both GPUs',
  engine: e => ENGINE_NAMES[e.engine] + ' is unticked',
  thinking: e => 'thinking: ' + thinkingName(e.thinking).toLowerCase(),
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
  // Until a sort is picked by hand the list follows the default: the benchmark the most families report.
  // State saved before benchmarks existed has no sortChosen: its sort counts as picked unless it is the old default.
  const sortChosen = saved?.sortChosen ?? (saved?.sort != null && saved.sort !== 'speed');
  const state = { filters: { ...defaultFilters(), ...(saved?.filters ?? {}) }, sort: sortChosen ? saved.sort : 'speed',
    sortChosen, gpuChosen: saved?.gpuChosen ?? false, entries: [], families: {}, benchmarks: {}, columns: [],
    loaded: new Set(), status: null };

  function save() {
    localStorage.setItem(STORE_KEY, JSON.stringify({ filters: state.filters, sort: state.sort,
      sortChosen: state.sortChosen, gpuChosen: state.gpuChosen }));
  }

  function cell(tag, text, className) {
    const el = document.createElement(tag);
    if (text != null) el.textContent = text;
    if (className) el.className = className;
    return el;
  }

  // A score links to the publisher's page it was read from; its tooltip has the full number, the model and the setting.
  function scoreCell(e, bench) {
    const score = scoreOf(e, bench, state.families);
    const sorted = benchOf(state.sort) === bench ? ' sorted' : '';
    if (!score) return cell('td', '–', 'num bench muted' + sorted);
    const td = cell('td', null, 'num bench' + sorted);
    const family = state.families[e.family];
    const link = cell('a', score.value.toFixed(1));
    link.href = family.source;
    link.target = '_blank';
    link.rel = 'noopener';
    link.title = score.value + ' measured on ' + family.measuredOn + (score.setting ? '; ' + score.setting : '') + '. Opens the publisher\'s page.';
    td.append(link);
    return td;
  }

  // The benchmark columns and their sort options follow the catalog, so a new benchmark needs no page edit.
  function showBenchmarks() {
    const name = bench => state.benchmarks[bench]?.name ?? bench;
    for (const head of document.querySelectorAll('thead tr')) {
      head.querySelectorAll('th.bench').forEach(th => th.remove());
      head.append(...state.columns.map(bench => {
        const th = cell('th', name(bench), 'num bench');
        th.dataset.bench = bench;
        return th;
      }));
    }
    $('sort').querySelectorAll('option.bench').forEach(option => option.remove());
    $('sort').prepend(...state.columns.map(bench => {
      const option = cell('option', name(bench), 'bench');
      option.value = BENCH_SORT + bench;
      return option;
    }));
    const known = [...$('sort').options].some(option => option.value === state.sort);
    if (!state.sortChosen || !known) state.sort = state.columns.length ? BENCH_SORT + state.columns[0] : 'speed';
  }

  // Marks the column the list is sorted by and explains its benchmark.
  function explainSort() {
    const bench = benchOf(state.sort);
    for (const th of document.querySelectorAll('th.bench')) {
      const active = th.dataset.bench === bench;
      th.classList.toggle('sorted', active);
      if (active) th.setAttribute('aria-sort', 'descending'); else th.removeAttribute('aria-sort');
    }
    const b = bench ? state.benchmarks[bench] : null;
    $('explain').hidden = !b;
    if (!b) return;
    $('explain-name').textContent = b.name;
    $('explain-text').textContent = b.tests + ' ' + b.higher;
    $('explain-about').hidden = !b.about;
    if (b.about) $('explain-about').href = b.about;
  }

  function row(e, reason) {
    const tr = cell('tr');
    const name = cell('td', null, 'name');
    const title = cell('div', null, 'id');
    title.append(cell('span', e.id));
    if (state.loaded.has(e.id)) title.append(cell('span', 'loaded', 'loaded'));
    name.append(title, cell('div', e.description, 'desc'));
    if (reason) name.append(cell('div', HIDDEN_REASONS[reason](e), 'reason'));
    tr.append(name,
      cell('td', ENGINE_NAMES[e.engine]),
      cell('td', formatContext(e.context), 'num'),
      cell('td', e.vision ? 'yes' : 'no', e.vision ? '' : 'muted'),
      cell('td', e.gpus === 'gpu1' ? 'GPU 1' : 'both'),
      cell('td', thinkingName(e.thinking)),
      cell('td', e.speed?.code ?? '–', 'num'),
      cell('td', e.speed?.prose ?? '–', 'num'),
      ...state.columns.map(bench => scoreCell(e, bench)));
    if (e.codeOnly) tr.classList.add('code-only');
    return tr;
  }

  function render() {
    const f = state.filters;
    state.loaded = loadedIds(state.status, state.entries);
    // What was typed stays as typed ("262k"); the slider writes its own label.
    const typed = $('context-value').value.trim();
    if ((parseContext(typed) ?? 0) !== f.minContext) {
      $('context-value').value = f.minContext ? formatContext(f.minContext) : '';
      $('context-value').setCustomValidity('');   // the text it replaces may have been invalid
    }
    $('context-slider').value = contextToSlider(f.minContext);
    $('vision').checked = f.vision;
    $('gpu-any').checked = !f.gpu1Only;
    $('gpu-1').checked = f.gpu1Only;
    for (const engine of ENGINES) $('engine-' + engine).checked = f.engines.includes(engine);
    $('thinking-' + f.thinking).checked = true;
    $('code-only').checked = f.hideCodeOnly;
    $('min-speed').value = f.minSpeed || '';
    $('sort').value = state.sort;
    // A saved benchmark sort has no option until the catalog arrives; the list is in name order meanwhile.
    if ($('sort').selectedIndex < 0) $('sort').value = 'name';
    $('reserved').hidden = !state.status?.reserve_gpu0;

    explainSort();

    const fitting = sortModels(filterModels(state.entries, f), state.sort, state.families);
    const rest = sortModels(state.entries.filter(e => whyHidden(e, f) !== null), state.sort, state.families);
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
  $('sort').addEventListener('change', ev => { state.sort = ev.target.value; state.sortChosen = true; save(); render(); });
  $('reset').addEventListener('click', () => {
    state.gpuChosen = false;
    update(f => Object.assign(f, defaultFilters(), { gpu1Only: Boolean(state.status?.reserve_gpu0) }));
  });

  async function poll() {
    try {
      const response = await fetch('status.json', { cache: 'no-store' });
      state.status = await response.json();
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
      state.families = catalog.families ?? {};
      state.benchmarks = catalog.benchmarks ?? {};
      state.columns = benchmarkColumns(state.families);
      showBenchmarks();
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
