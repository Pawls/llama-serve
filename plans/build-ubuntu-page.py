"""Renders the Ubuntu plan and guide into one HTML page for the claude.ai artifact named in the plan.

Usage: python plans/build-ubuntu-page.py <out.html>   (then publish <out.html> to the artifact URL)
"""
import html
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
OUT = Path(sys.argv[1])

plan = (REPO / "plans" / "ubuntu-dual-boot.md").read_text(encoding="utf-8")
guide = (REPO / "docs" / "ubuntu-setup.md").read_text(encoding="utf-8")

PAGE = """<title>Ubuntu Dual Boot Plan</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Atkinson+Hyperlegible:ital,wght@0,400;0,700;1,400&family=JetBrains+Mono:wght@400;600&display=swap">
<style>
/* Layout: one reading column, a sticky tab bar, slice status chips under the plan header. */
:root {
  --bg: #faf8fb; --surface: #ffffff; --fg: #231c26; --muted: #6a5f6e; --rule: #e4dde7;
  --accent: #772953; --accent-soft: #f3e6ee; --done: #2f7d4f; --code-bg: #f1edf3;
  --body: "Atkinson Hyperlegible", system-ui, sans-serif;
  --mono: "JetBrains Mono", ui-monospace, Consolas, monospace;
}
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) {
  --bg: #17121a; --surface: #201a24; --fg: #ece6ef; --muted: #a99fae; --rule: #3a3140;
  --accent: #e08bb8; --accent-soft: #3a2232; --done: #6fcf97; --code-bg: #2a2230; color-scheme: dark } }
:root[data-theme="dark"] {
  --bg: #17121a; --surface: #201a24; --fg: #ece6ef; --muted: #a99fae; --rule: #3a3140;
  --accent: #e08bb8; --accent-soft: #3a2232; --done: #6fcf97; --code-bg: #2a2230; color-scheme: dark }
body { background: var(--bg); color: var(--fg); font: 16px/1.6 var(--body); padding-inline: 16px; }
.wrap { max-width: 760px; margin: 0 auto; padding-block: 8px 64px; }
header.top { position: sticky; top: env(safe-area-inset-top, 0px); background: var(--bg); z-index: 2;
  border-bottom: 1px solid var(--rule); padding-block: 12px 0; }
.eyebrow { font: 600 12px/1 var(--mono); letter-spacing: .08em; text-transform: uppercase; color: var(--muted); }
.tabs { display: flex; gap: 4px; margin-top: 10px; }
.tabs button { font: 700 15px var(--body); color: var(--muted); background: none; border: 0;
  border-bottom: 3px solid transparent; padding: 8px 12px; cursor: pointer; }
.tabs button[aria-selected="true"] { color: var(--accent); border-bottom-color: var(--accent); }
.tabs button:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.chips { display: flex; flex-wrap: wrap; gap: 8px; margin: 18px 0 4px; }
.chip { font: 600 13px var(--mono); padding: 4px 10px; border-radius: 999px; border: 1px solid var(--rule);
  color: var(--muted); text-decoration: none; }
.chip.done { color: var(--done); border-color: var(--done); }
.chip.next { color: var(--accent); border-color: var(--accent); background: var(--accent-soft); }
article { min-width: 0; }
article h1 { font-size: 1.6rem; line-height: 1.25; text-wrap: balance; margin: 24px 0 8px; }
article h2 { font-size: 1.25rem; margin: 36px 0 8px; padding-top: 8px; border-top: 1px solid var(--rule); }
article h3 { font-size: 1.1rem; margin: 32px 0 6px; text-wrap: balance; scroll-margin-top: 110px; }
article h3 del { color: var(--done); }
article blockquote { margin: 16px 0; padding: 4px 16px; background: var(--surface); border: 1px solid var(--rule);
  border-radius: 8px; }
article code { font: 0.88em var(--mono); background: var(--code-bg); padding: 1px 5px; border-radius: 4px;
  overflow-wrap: anywhere; }
article pre { background: var(--code-bg); padding: 12px 14px; border-radius: 8px; overflow-x: auto; }
article pre code { background: none; padding: 0; overflow-wrap: normal; }
.table-scroll { overflow-x: auto; margin: 12px 0; }
article table { border-collapse: collapse; font-size: 14px; min-width: 100%; }
article th, article td { border-bottom: 1px solid var(--rule); padding: 6px 10px; text-align: left; vertical-align: top; }
article th { color: var(--muted); font-weight: 700; }
article ul { padding-left: 1.3em; }
article li.task { list-style: none; margin-left: -1.3em; }
article input[type=checkbox] { accent-color: var(--accent); margin-right: 8px; }
article a { color: var(--accent); }
.src { margin-top: 40px; font-size: 13px; color: var(--muted); }
@media (prefers-reduced-motion: no-preference) { html { scroll-behavior: smooth; } }
</style>

<div class="wrap">
  <header class="top">
    <div class="eyebrow">llama-serve &middot; Ubuntu 26.04 on disk 3 (477 GB)</div>
    <nav class="tabs" role="tablist">
      <button id="tab-plan" role="tab" aria-selected="true" data-pane="plan">Plan</button>
      <button id="tab-guide" role="tab" aria-selected="false" data-pane="guide">Setup guide</button>
    </nav>
  </header>
  <section id="pane-plan" role="tabpanel">
    <div class="chips" id="chips"></div>
    <article id="plan"></article>
  </section>
  <section id="pane-guide" role="tabpanel" hidden>
    <article id="guide"></article>
  </section>
  <p class="src">Source of truth: <code>plans/ubuntu-dual-boot.md</code> and <code>docs/ubuntu-setup.md</code>
  in <code>F:\\AI\\llama-serve</code> (<code>/mnt/f/AI/llama-serve</code> from Linux). This page is a copy; it is
  republished when they change.</p>
</div>

<script type="text/markdown" id="md-plan">__PLAN__</script>
<script type="text/markdown" id="md-guide">__GUIDE__</script>
<script src="https://cdnjs.cloudflare.com/ajax/libs/marked/12.0.2/marked.min.js"></script>
<script>
function source(id) {
  const t = document.createElement("textarea");
  t.innerHTML = document.getElementById(id).textContent;
  return t.value;
}
// Headings carry explicit ids as "{#u1}"; marked doesn't know that syntax, so lift them into the HTML.
function render(md, target) {
  const ids = [];
  md = md.replace(/^(#{1,6} .*?)\\s*\\{#([a-z0-9-]+)\\}\\s*$/gm, (m, h, id) => { ids.push(id); return h + " \\u2063" + id; });
  const el = document.getElementById(target);
  el.innerHTML = marked.parse(md, { gfm: true });
  el.querySelectorAll("h1,h2,h3,h4").forEach(h => {
    const m = h.innerHTML.match(/\\u2063([a-z0-9-]+)$/);
    if (m) { h.id = m[1]; h.innerHTML = h.innerHTML.replace(/\\s*\\u2063[a-z0-9-]+$/, ""); }
  });
  el.querySelectorAll("table").forEach(t => {
    const box = document.createElement("div"); box.className = "table-scroll";
    t.replaceWith(box); box.appendChild(t);
  });
  el.querySelectorAll("li").forEach(li => { if (li.querySelector(":scope > input[type=checkbox]")) li.classList.add("task"); });
  // Links between the plan and the guide switch tabs instead of leaving the page.
  el.querySelectorAll("a[href]").forEach(a => {
    const href = a.getAttribute("href");
    if (/ubuntu-setup\\.md/.test(href)) { a.href = "#guide"; a.removeAttribute("target"); }
    else if (/ubuntu-dual-boot\\.md/.test(href)) { a.href = "#plan"; a.removeAttribute("target"); }
  });
}
function chips(md) {
  const next = (md.match(/\\*\\*Next:\\*\\*\\s*([A-Z]\\d+)/) || [])[1];
  const box = document.getElementById("chips");
  for (const m of md.matchAll(/^### (~~)?([A-Z]\\d+) \\u2014 (.*?)(?:~~)?\\s\\u00b7.*\\{#([a-z0-9]+)\\}/gm)) {
    const a = document.createElement("a");
    a.className = "chip" + (m[1] ? " done" : m[2] === next ? " next" : "");
    a.href = "#" + m[4];
    a.textContent = m[2] + (m[1] ? " shipped" : m[2] === next ? " next" : "");
    a.title = m[3];
    box.appendChild(a);
  }
}
function show(pane) {
  document.querySelectorAll(".tabs button").forEach(b => b.setAttribute("aria-selected", String(b.dataset.pane === pane)));
  document.getElementById("pane-plan").hidden = pane !== "plan";
  document.getElementById("pane-guide").hidden = pane !== "guide";
}
const planMd = source("md-plan"), guideMd = source("md-guide");
render(planMd, "plan"); render(guideMd, "guide"); chips(planMd);
document.querySelectorAll(".tabs button").forEach(b => b.addEventListener("click", () => { show(b.dataset.pane); history.replaceState(null, "", "#" + b.dataset.pane); }));
function route() {
  const h = location.hash.slice(1);
  if (h === "guide") show("guide");
  else if (h === "plan" || !h) show("plan");
  else { const el = document.getElementById(h); if (el) { show(el.closest("#pane-guide") ? "guide" : "plan"); el.scrollIntoView(); } }
}
window.addEventListener("hashchange", route); route();
</script>
"""

OUT.write_text(PAGE.replace("__PLAN__", html.escape(plan)).replace("__GUIDE__", html.escape(guide)),
               encoding="utf-8", newline="\n")
print(OUT)
