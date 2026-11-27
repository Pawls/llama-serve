// Run with: node --test web/model-finder.test.mjs   (test-model-finder.ps1 runs it too)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as finder from './model-finder.js';

const { filterModels, sortModels, defaultFilters, whyHidden, parseContext, formatContext, loadedIds, thinkingName,
  scoreOf, benchmarkColumns } = finder;

const entries = [
  { id: 'big-vision', engine: 'router', context: 262144, vision: true, gpus: 'both', thinking: 'on', speed: { code: 57, prose: 50 } },
  { id: 'short-vision', engine: 'router', context: 65536, vision: true, gpus: 'both', thinking: 'off', speed: { code: 36, prose: 36 } },
  { id: 'single-text', engine: 'router', context: 131072, vision: false, gpus: 'gpu1', thinking: 'on', speed: { code: 39, prose: 30 } },
  { id: 'coder-dflash', engine: 'router', context: 262144, vision: false, gpus: 'both', thinking: 'none', codeOnly: true, speed: { code: 190, prose: 56 } },
  { id: 'strata-big', engine: 'strata', context: 262144, vision: true, gpus: 'both', thinking: 'on', speed: { code: 126, prose: 94 } },
  { id: 'ninfer-untimed', engine: 'ninfer', context: 786432, vision: true, gpus: 'both', thinking: 'on' },
];
const ids = list => list.map(e => e.id);
const filters = overrides => ({ ...defaultFilters(), ...overrides });

test('32K shows every entry', () => {
  assert.deepEqual(ids(filterModels(entries, filters({ minContext: 32768 }))), ids(entries));
});

test('an entry shows when its context equals the value, and drops one token above', () => {
  assert.ok(ids(filterModels(entries, filters({ minContext: 131072 }))).includes('single-text'));
  assert.ok(!ids(filterModels(entries, filters({ minContext: 131073 }))).includes('single-text'));
});

test('262K drops the short ones', () => {
  assert.deepEqual(ids(filterModels(entries, filters({ minContext: 262144 }))),
    ['big-vision', 'coder-dflash', 'strata-big', 'ninfer-untimed']);
});

test('images required keeps only vision entries', () => {
  assert.deepEqual(ids(filterModels(entries, filters({ vision: true }))),
    ['big-vision', 'short-vision', 'strata-big', 'ninfer-untimed']);
});

test('GPU 1 only keeps the single-card entries', () => {
  assert.deepEqual(ids(filterModels(entries, filters({ gpu1Only: true }))), ['single-text']);
});

test('engine filter', () => {
  assert.deepEqual(ids(filterModels(entries, filters({ engines: ['strata', 'ninfer'] }))), ['strata-big', 'ninfer-untimed']);
});

test('thinking filter: thinks by default, and answers without thinking', () => {
  assert.deepEqual(ids(filterModels(entries, filters({ thinking: 'thinks' }))),
    ['big-vision', 'single-text', 'strata-big', 'ninfer-untimed']);
  assert.deepEqual(ids(filterModels(entries, filters({ thinking: 'direct' }))), ['short-vision', 'coder-dflash']);
});

test('hide code-only drafter lanes', () => {
  assert.ok(!ids(filterModels(entries, filters({ hideCodeOnly: true }))).includes('coder-dflash'));
});

test('a minimum speed hides slower and unmeasured entries; zero hides none', () => {
  assert.deepEqual(ids(filterModels(entries, filters({ minSpeed: 100 }))), ['coder-dflash', 'strata-big']);
  assert.equal(filterModels(entries, filters({ minSpeed: 0 })).length, entries.length);
});

test('sort by code speed, fastest first, unmeasured last', () => {
  assert.deepEqual(ids(sortModels(entries, 'speed')),
    ['coder-dflash', 'strata-big', 'big-vision', 'single-text', 'short-vision', 'ninfer-untimed']);
});

test('sort by context, largest first, ties in id order', () => {
  assert.deepEqual(ids(sortModels(entries, 'context')),
    ['ninfer-untimed', 'big-vision', 'coder-dflash', 'strata-big', 'single-text', 'short-vision']);
});

test('sort by name', () => {
  assert.deepEqual(ids(sortModels(entries, 'name')),
    ['big-vision', 'coder-dflash', 'ninfer-untimed', 'short-vision', 'single-text', 'strata-big']);
});

test('sorting does not reorder the input', () => {
  const before = ids(entries);
  sortModels(entries, 'speed');
  assert.deepEqual(ids(entries), before);
});

test('whyHidden names the first filter an entry fails, or null', () => {
  assert.equal(whyHidden(entries[1], filters({ minContext: 131072 })), 'context');
  assert.equal(whyHidden(entries[2], filters({ vision: true })), 'images');
  assert.equal(whyHidden(entries[0], filters({ gpu1Only: true })), 'gpus');
  assert.equal(whyHidden(entries[0], filters()), null);
});

// The docs say both "262k" and "256K" for 262144, so a typed K is read as 1000: the threshold then never drops a
// model whose context someone wrote either way.
test('parseContext reads what people type, K and M as decimal thresholds', () => {
  assert.equal(parseContext('32k'), 32000);
  assert.equal(parseContext('262K'), 262000);
  assert.equal(parseContext('256K'), 256000);
  assert.equal(parseContext('1M'), 1000000);
  assert.equal(parseContext(' 131072 '), 131072);
  assert.equal(parseContext('1.5m'), 1500000);
  assert.equal(parseContext('32,768'), 32768);
  assert.equal(parseContext('abc'), null);
  assert.equal(parseContext(''), null);
});

test('formatContext writes tokens in binary K and M, as the tray menu does', () => {
  assert.equal(formatContext(32768), '32K');
  assert.equal(formatContext(262144), '256K');
  assert.equal(formatContext(8192), '8K');
  assert.equal(formatContext(65536), '64K');
  assert.equal(formatContext(368640), '360K');
  assert.equal(formatContext(1048576), '1M');
  assert.equal(formatContext(786432), '768K');
  assert.equal(formatContext(188416), '184K');
});

const running = [
  { id: 'qwen3.8-27b', engine: 'router', modelId: 'qwen3.8-27b' },
  { id: 'swift-iq3_s', engine: 'strata', modelId: 'swift-1.5-iq3_s' },
  { id: 'sq-20', engine: 'strata', modelId: 'inkling-sq' },
  { id: 'sq-20-gpu1', engine: 'strata', modelId: 'inkling-sq' },
  { id: 'inkling2-mini', engine: 'ninfer', modelId: 'inkling-2-mini' },
];

test('loadedIds: the router model only once loaded, Strata when Loaded, ninfer when Ready', () => {
  const ids = loadedIds({
    router: { state: 'Loaded', model: 'qwen3.8-27b', model_status: 'loaded' },
    strata: { state: 'Unloaded', model: 'swift-1.5-iq3_s' },
    ninfer: { state: 'Ready', model: 'inkling-2-mini' },
  }, running);
  assert.deepEqual([...ids].sort(), ['inkling2-mini', 'qwen3.8-27b']);
  const loading = loadedIds({ router: { model: 'qwen3.8-27b', model_status: 'loading' },
    strata: { state: 'Loaded', model: 'swift-1.5-iq3_s' }, ninfer: { state: 'Stopped' } }, running);
  assert.deepEqual([...loading], ['swift-iq3_s']);
  assert.equal(loadedIds(null, running).size, 0);
});

test('loadedIds: a Strata size the tray started marks only that size, not every size with its model id', () => {
  const status = size => ({ strata: { state: 'Loaded', model: 'inkling-sq', size } });
  assert.deepEqual([...loadedIds(status('sq-20'), running)], ['sq-20']);
  assert.deepEqual([...loadedIds(status(null), running)], ['sq-20', 'sq-20-gpu1']);
});

test('thinkingName: a value the page has no name for is shown as it is', () => {
  assert.equal(thinkingName('on'), 'Thinks');
  assert.equal(thinkingName('budgeted'), 'budgeted');
});

// ---- Benchmark scores ----

const families = {
  alpha: { scores: { swe: { value: 70.1 }, gpqa: { value: 80 } } },
  beta: { scores: { swe: { value: 75 }, gpqa: { value: 80 }, rare: { value: 9 } } },
  gamma: { scores: { swe: { value: 70.1 } } },
  delta: { scores: { gpqa: { value: 0 } } },
};
const scored = [
  { id: 'zeta', family: 'alpha' },
  { id: 'alpha-lane', family: 'alpha' },
  { id: 'beta-one', family: 'beta' },
  { id: 'gamma-one', family: 'gamma' },
  { id: 'finetune', family: 'no-such-family' },
  { id: 'delta-one', family: 'delta' },
];

test('sort by a benchmark: highest first, ties in name order, missing scores last in name order', () => {
  assert.deepEqual(ids(sortModels(scored, 'bench:swe', families)),
    ['beta-one', 'alpha-lane', 'gamma-one', 'zeta', 'delta-one', 'finetune']);
});

test('sort by a benchmark: a score of 0 is a score, not a missing one', () => {
  assert.deepEqual(ids(sortModels(scored, 'bench:gpqa', families)),
    ['alpha-lane', 'beta-one', 'zeta', 'delta-one', 'finetune', 'gamma-one']);
});

test('sort by a benchmark nobody reports, or with no family table, is name order', () => {
  const byName = ['alpha-lane', 'beta-one', 'delta-one', 'finetune', 'gamma-one', 'zeta'];
  assert.deepEqual(ids(sortModels(scored, 'bench:nope', families)), byName);
  assert.deepEqual(ids(sortModels(scored, 'bench:swe')), byName);
});

test('scoreOf reads the family score an entry shares with its lanes', () => {
  assert.equal(scoreOf(scored[0], 'swe', families).value, 70.1);
  assert.equal(scoreOf(scored[4], 'swe', families), undefined);
  assert.equal(scoreOf(scored[3], 'gpqa', families), undefined);
});

test('benchmarkColumns: a column needs three families; the most reported comes first, ties by id', () => {
  const table = structuredClone(families);
  assert.deepEqual(benchmarkColumns(table), ['gpqa', 'swe']);
  table.gamma.scores.extra = { value: 1 };
  table.alpha.scores.extra = { value: 1 };
  assert.deepEqual(benchmarkColumns(table), ['gpqa', 'swe']);
  table.delta.scores.swe = { value: 1 };
  assert.deepEqual(benchmarkColumns(table), ['swe', 'gpqa']);
  assert.deepEqual(benchmarkColumns({}), []);
});

// The shipped table (model-catalog.json): the rules the page and the plan rely on.
const shipped = JSON.parse(readFileSync(new URL('../model-catalog.json', import.meta.url), 'utf8'));

test('model-catalog.json: every scored family names its source page and the model measured, every score a described benchmark', () => {
  const entryFamilies = new Set(Object.values(shipped.entries).map(e => e.family));
  assert.ok(Object.keys(shipped.families).length > 0, 'no families');
  for (const [family, row] of Object.entries(shipped.families)) {
    assert.ok(entryFamilies.has(family), family + ' is no entry\'s family');
    assert.match(row.source, /^https:\/\/\S+$/, family + ': source');
    assert.ok(row.measuredOn, family + ': measuredOn');
    assert.ok(Object.keys(row.scores).length > 0, family + ': no scores');
    for (const [bench, score] of Object.entries(row.scores)) {
      const where = family + ' / ' + bench;
      assert.ok(shipped.benchmarks[bench], where + ': benchmark not described');
      assert.equal(typeof score.value, 'number', where + ': value');
    }
  }
});

test('model-catalog.json: every benchmark with a column says what it tests and what higher means', () => {
  const columns = benchmarkColumns(shipped.families);
  assert.ok(columns.length > 0, 'no benchmark has three families');
  assert.deepEqual(Object.keys(shipped.benchmarks).sort(), [...columns].sort(), 'a described benchmark has no column');
  for (const bench of columns) {
    const b = shipped.benchmarks[bench];
    assert.ok(b.name && b.name.length <= 24, bench + ': name');
    assert.ok(b.tests && b.higher, bench + ': tests and higher');
  }
});
