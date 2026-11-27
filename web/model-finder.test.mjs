// Run with: node --test web/model-finder.test.mjs   (test-model-finder.ps1 runs it too)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { filterModels, sortModels, defaultFilters } from './model-finder.js';

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
