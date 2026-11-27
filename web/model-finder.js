// The model finder's filter and sort rules, and the page that uses them. Plan: plans/model-finder.md § S1.
// The rules are pure exports so web/model-finder.test.mjs can run them under node; the page code below them
// runs only in a browser.

export function defaultFilters() {
  return { minContext: 0, vision: false, gpu1Only: false, engines: ['router', 'strata', 'ninfer'],
    thinking: 'any', hideCodeOnly: false, minSpeed: 0 };
}

const THINKS = new Set(['on', 'always']);

export function filterModels(entries, filters) {
  const engines = new Set(filters.engines);
  return entries.filter(e =>
    e.context >= filters.minContext
    && (!filters.vision || e.vision)
    && (!filters.gpu1Only || e.gpus === 'gpu1')
    && engines.has(e.engine)
    && (filters.thinking === 'any' || (filters.thinking === 'thinks') === THINKS.has(e.thinking))
    && (!filters.hideCodeOnly || !e.codeOnly)
    && (!filters.minSpeed || (e.speed?.code ?? 0) >= filters.minSpeed));
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
