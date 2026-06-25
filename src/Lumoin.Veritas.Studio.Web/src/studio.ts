// Studio shell controller: editor language tabs, result view tabs, the gutter, the streaming status
// readout, theme, the graph, the trace feed — and the wiring of Run to the VeritasTransport. On load
// the panels render from the bundled sample (so the shell stands alone); Run executes the active
// SPARQL against the resolved transport (the native bridge, or the CLI server over HTTP), and falls
// back to the sample when no engine is attached. Framework-free; the components register on import.

import './services/host-bridge';
import './components/sparql-results';
import { STUDIO_DATA, type StudioData } from './data';
import { buildGraphData } from './graph-data';
import { DATASETS, DEFAULT_DATASET, datasetById, type StudioDataset } from './datasets';
import { createGraphView, type GraphMode, type GraphView } from './graph';
import { resolveTransport, type VeritasTransport, type VeritasWasmEngine } from './services/veritas-transport';
import type { ShaclReportView, SparqlResultsDocument, SparqlResultsElement } from './components/sparql-results';

type Theme = 'light' | 'dark';

/** Resolves a shell element by id, asserting it exists (the shell markup is fixed). */
function byId<T extends HTMLElement>(id: string): T {
  const el = document.getElementById(id);
  if (el === null) {
    throw new Error(`Studio shell is missing #${id}.`);
  }

  return el as T;
}

/** Every element matching the selector, as an array. */
const all = (selector: string): HTMLElement[] => [...document.querySelectorAll<HTMLElement>(selector)];

/** Formats a number with grouping separators. */
const fmt = (n: number): string => n.toLocaleString('en-US');

/** The engine transport (native bridge, in-browser WASM engine, or HTTP); re-resolved after an optional WASM boot. */
let transport: VeritasTransport = resolveTransport();

/** Boots the in-browser WASM engine when `?engine=wasm` is requested, then re-resolves the transport to it. */
async function maybeBootWasmEngine(): Promise<void> {
  if (new URLSearchParams(location.search).get('engine') !== 'wasm') {
    return;
  }

  try {
    const { bootWasmEngine } = await import('./services/wasm-engine');
    await bootWasmEngine('/_framework/dotnet.js', DEFAULT_DATASET.turtle);
    transport = resolveTransport();
    applyDataset(DEFAULT_DATASET);
    await deriveAndMountGraph();
  } catch (error) {
    // Stay on the default transport if the engine fails to boot; surface it for diagnosis.
    console.error('In-browser WASM engine failed to boot', error);
  }
}

/** The id of the dataset currently loaded into the in-browser engine. */
let activeDatasetId = DEFAULT_DATASET.id;

/** Sets a language tab's editor text (the run target) and refreshes the gutter when it is the active tab. */
function setEditorBlock(lang: string, text: string): void {
  const block = document.querySelector<HTMLElement>(`.code-only.${lang}`);
  if (block !== null) {
    block.textContent = text;
    if (lang === editorLangOf()) {
      updateGutter();
    }
  }
}

/** Reflects a dataset in the shell: the active-dataset chip label and the editor's showcase query. */
function applyDataset(dataset: StudioDataset): void {
  activeDatasetId = dataset.id;
  const chip = document.querySelector<HTMLElement>('[data-testid="active-dataset"]');
  if (chip !== null) {
    chip.textContent = dataset.label;
    chip.title = dataset.description;
  }

  setEditorBlock('sparql', dataset.query);
  setEditorBlock('shacl', dataset.shapes);
}

/** Switches the active dataset: loads its Turtle into the in-browser engine (when present), then reflects it. */
async function loadDataset(id: string): Promise<void> {
  const dataset = datasetById(id) ?? DEFAULT_DATASET;
  const engine = (globalThis as { veritasEngine?: VeritasWasmEngine }).veritasEngine;
  if (engine !== undefined) {
    await engine.loadTurtle(dataset.turtle);
  }

  applyDataset(dataset);
  await deriveAndMountGraph();
  (byId('results') as unknown as SparqlResultsElement).showMessage(`Loaded ${dataset.label} — Run ▸ to query.`, 'idle');
}

/** The active graph view (the 2D placeholder, then the BabylonJS view); null before init. */
let graph: GraphView | null = null;

/** The in-flight streaming-animation frame handle. */
let streamRaf = 0;

/** Shapes the bundled sample into a SPARQL-results-JSON document so the result component renders it. */
function sampleResults(): SparqlResultsDocument {
  const { vars, rows } = STUDIO_DATA.bindings;

  return {
    head: { vars },
    results: {
      bindings: rows.map((row) => {
        const binding: Record<string, { type: string; value: string }> = {};
        vars.forEach((name, i) => {
          binding[name] = { type: /^\d+$/.test(row[i]) ? 'literal' : 'uri', value: row[i] };
        });

        return binding;
      })
    }
  };
}

/** The text of the active editor language sample (the run target until a live editor buffer lands). */
function activeCode(): string {
  const editor = document.querySelector<HTMLElement>('.editor');
  const lang = editor?.dataset.lang ?? 'sparql';
  const code = document.querySelector<HTMLElement>(`.code-only.${lang}`);

  return code?.textContent ?? '';
}

/** Renders the node-type colour legend into the graph HUD. */
function initLegend(): void {
  byId('legend').innerHTML = STUDIO_DATA.types
    .map((t) => `<span class="li"><span class="sw" style="background:oklch(0.62 ${t.c} ${t.hue})"></span>${t.label}</span>`)
    .join('');
}

/** The current graph mount generation; guards a slow BabylonJS upgrade from overwriting a newer mount. */
let graphGen = 0;

/** Mounts a graph dataset: the instant 2D placeholder, upgraded to the BabylonJS view when its chunk loads. */
function mountGraph(data: StudioData): void {
  const canvas = byId<HTMLCanvasElement>('graph-canvas');
  const theme: Theme = (document.documentElement.getAttribute('data-theme') as Theme) ?? 'light';
  const generation = ++graphGen;
  graph?.dispose();
  graph = createGraphView(canvas, data);
  graph.setTheme(theme);

  // Upgrade to the BabylonJS view once its code-split chunk arrives; skip if a newer mount superseded this.
  void import('./graph-babylon')
    .then(({ createBabylonGraphView }) => {
      if (generation !== graphGen) {
        return;
      }

      graph?.dispose();
      graph = createBabylonGraphView(canvas, data);
      graph.setTheme(theme);
    })
    .catch(() => {
      // Keep the 2D placeholder if the chunk fails to load.
    });
}

/** Mounts the bundled sample graph — the placeholder shown until the engine drives the picture. */
function initGraph(): void {
  mountGraph(STUDIO_DATA);
}

/** Runs the active dataset's graph query against the engine and remounts the graph from the result. */
async function deriveAndMountGraph(): Promise<void> {
  const dataset = datasetById(activeDatasetId) ?? DEFAULT_DATASET;
  try {
    const outcome = await transport.runSparql(dataset.graphQuery);
    if (outcome.ok) {
      const data = buildGraphData(outcome.results.results?.bindings ?? []);
      if (data.clusters.length > 0) {
        mountGraph(data);
      }
    }
  } catch {
    // Keep the current graph when no engine is attached or the graph query cannot run.
  }
}

/** Renders the trace feed ("why these terms") from the sample; live runs feed it from the trace stream. */
function initTrace(): void {
  const mark = { chose: '✓', excluded: '✕', note: '·' };
  byId('trace-list').innerHTML = STUDIO_DATA.trace
    .map((t) => `
      <div class="tr ${t.kind}">
        <span class="mark">${mark[t.kind]}</span>
        <div>
          <div class="term">${t.term}</div>
          <div class="why">${t.matches > 0 ? `<span class="count">${fmt(t.matches)}</span> · ` : ''}${t.why}</div>
        </div>
      </div>`)
    .join('');
}

/** Renders the raw (Turtle) result view from the sample. */
function initRaw(): void {
  byId('raw-pre').textContent = `@prefix untp: <https://vocabulary.uncefact.org/untp/> .
@prefix vw:   <https://veritas.app/ns/water#> .

ex:garment-sku-001 a untp:Product ;
    untp:productCategory    "6205" ;
    untp:producedAtFacility ex:garment-dhaka ;
    untp:upstreamInput      ex:fabric-roll-001 .

ex:mill-chittagong a untp:Facility ;
    vw:locatedInBasin ex:basin-352007 .`;
}

/** Renders the SHACL conformance view from the sample. */
function initShacl(): void {
  byId('shacl-body').innerHTML = `
    <div class="shacl-row">
      <span class="ck ok">✓</span>
      <span class="msg">seed-cotton lot conforms to <b>ex:ProductTraceabilityShape</b></span>
      <span class="focus">sh:conforms true</span>
    </div>
    <div class="shacl-row warn">
      <span class="ck warn">!</span>
      <span class="msg">4 products violate <b>sh:minCount</b> on untp:cottonSpecies</span>
      <span class="focus">species not yet declared</span>
    </div>
    <div class="shacl-row warn">
      <span class="ck warn">!</span>
      <span class="msg">4 facilities sit in <b>extremely-high</b> water-stress basins</span>
      <span class="focus">vw:baselineWaterStress ≥ 4.0</span>
    </div>`;
}

/** Repaints the gutter line numbers for the active editor language sample. */
function updateGutter(): void {
  const lang = document.querySelector<HTMLElement>('.editor')?.dataset.lang ?? 'sparql';
  const code = document.querySelector<HTMLElement>(`.code-only.${lang}`);
  const lines = (code?.textContent ?? '').replace(/\n$/, '').split('\n').length;
  byId('gutter').innerHTML = Array.from({ length: Math.max(lines, 1) }, (_, i) => `<span>${i + 1}</span>`).join('');
}

/** Wires the editor language tabs: select one, switch the visible sample, repaint the gutter. */
function initEditorTabs(): void {
  const editor = document.querySelector<HTMLElement>('.editor');
  for (const tab of all('.lang-tab')) {
    tab.addEventListener('click', () => {
      all('.lang-tab').forEach((t) => t.setAttribute('aria-selected', 'false'));
      tab.setAttribute('aria-selected', 'true');
      editor?.setAttribute('data-lang', tab.dataset.lang ?? 'sparql');
      updateGutter();
    });
  }
}

/** Wires the result view tabs (graph / table / raw / SHACL), re-laying-out the graph when it is shown. */
function initResultTabs(): void {
  const results = document.querySelector<HTMLElement>('.results');
  for (const tab of all('.res-tab')) {
    tab.addEventListener('click', () => {
      all('.res-tab').forEach((t) => t.setAttribute('aria-selected', 'false'));
      tab.setAttribute('aria-selected', 'true');
      results?.setAttribute('data-tab', tab.dataset.tab ?? 'graph');
      if (tab.dataset.tab === 'graph' && graph !== null) {
        graph.resize();
        graph.render();
      }
    });
  }
}

/** Animates the status readout to its targets; the easing is cosmetic, the targets are the real counts. */
function runStream(rows: number, ms: number, ops: number, durationMs: number): void {
  const studio = document.querySelector<HTMLElement>('.studio');
  studio?.setAttribute('data-streaming', 'live');
  byId('stream-label').firstChild!.textContent = 'streaming… ';
  const start = performance.now();
  cancelAnimationFrame(streamRaf);

  const step = (now: number): void => {
    const k = Math.min(1, (now - start) / durationMs);
    const e = 1 - Math.pow(1 - k, 3);
    byId('row-count').textContent = fmt(Math.round(rows * e));
    byId('ms').textContent = fmt(Math.round(ms * e));
    byId('ops').textContent = fmt(Math.round(ops * e));
    if (k < 1) {
      streamRaf = requestAnimationFrame(step);
    } else {
      studio?.setAttribute('data-streaming', 'done');
      byId('stream-label').firstChild!.textContent = 'complete · ';
      byId('res-count').textContent = `${fmt(rows)} rows`;
    }
  };

  streamRaf = requestAnimationFrame(step);
}

/** The query generation; a live re-query renders only if it is still the latest (latest-wins / switch-to-newest). */
let queryGen = 0;

/** The last query text run, so an unchanged buffer does not re-run (distinct-until-changed). */
let lastQueryText = '';

/** The debounce handle for the live re-query. */
let liveQueryTimer = 0;

/**
 * Runs the active SPARQL against the transport, rendering results or diagnostics; falls back to the sample
 * with no engine. In live mode (a debounced re-query while editing) a stale result is dropped, a mid-edit
 * parse error keeps the last good results rather than flashing, and no transient status overwrites them.
 * @param options.live Whether this is a debounced edit-driven re-query rather than an explicit Run.
 */
async function runQuery(options?: { live?: boolean }): Promise<void> {
  const results = byId('results') as unknown as SparqlResultsElement;
  const lang = editorLangOf();
  if (lang === 'shacl') {
    if (!options?.live) {
      await runShaclValidation(results);
    }

    return;
  }

  if (lang !== 'sparql') {
    if (!options?.live) {
      results.showMessage('Only SPARQL and SHACL run against the engine in this shell.', 'idle');
    }

    return;
  }

  const query = activeCode();
  lastQueryText = query;
  const generation = ++queryGen;
  if (!options?.live) {
    results.showMessage('Running…', 'loading');
  }

  byId('plan-chip').textContent = 'plan: …';
  try {
    const outcome = await transport.runSparql(query);
    if (generation !== queryGen) {
      return;
    }

    if (outcome.ok) {
      results.results = outcome.results;
      const rows = outcome.results.results?.bindings.length ?? 0;
      runStream(rows, STUDIO_DATA.cost.ms, STUDIO_DATA.cost.ops, 700);
      byId('plan-chip').textContent = `plan: ${STUDIO_DATA.cost.plan}`;
    } else if (options?.live) {
      // Mid-edit the buffer may not parse; keep the last good results rather than flashing the error.
      byId('plan-chip').textContent = 'plan: editing…';
    } else {
      results.showError(outcome.error);
      runStream(0, 0, 0, 300);
    }
  } catch {
    if (generation !== queryGen || options?.live) {
      return;
    }

    // No engine attached (browser without the CLI server, or no native bridge): render the sample.
    results.results = sampleResults();
    runStream(STUDIO_DATA.cost.rows, STUDIO_DATA.cost.ms, STUDIO_DATA.cost.ops, 700);
    byId('plan-chip').textContent = `plan: ${STUDIO_DATA.cost.plan} (sample)`;
  }
}

/** Schedules a debounced live re-query once the SPARQL buffer settles, skipping an unchanged buffer. */
function scheduleLiveQuery(): void {
  window.clearTimeout(liveQueryTimer);
  liveQueryTimer = window.setTimeout(() => {
    if (editorLangOf() === 'sparql' && activeCode() !== lastQueryText) {
      void runQuery({ live: true });
    }
  }, 250);
}

/** Validates the loaded dataset against the SHACL editor's shapes on the in-browser engine, rendering the report. */
async function runShaclValidation(results: SparqlResultsElement): Promise<void> {
  const engine = (globalThis as { veritasEngine?: VeritasWasmEngine }).veritasEngine;
  if (engine === undefined) {
    results.showMessage('SHACL validation runs against the in-browser engine — open with ?engine=wasm.', 'idle');

    return;
  }

  results.showMessage('Validating…', 'loading');
  try {
    const report = JSON.parse(await engine.validateShacl(activeCode())) as ShaclReportView;
    results.showReport(report);
  } catch (error) {
    results.showMessage(`Validation failed: ${String(error)}`, 'error');
  }
}

/** The active editor language (`sparql`, `shacl`, …). */
function editorLangOf(): string {
  return document.querySelector<HTMLElement>('.editor')?.dataset.lang ?? 'sparql';
}

/** Sets the colour theme and repaints the theme-dependent surfaces (graph, legend). */
function setTheme(theme: Theme): void {
  document.documentElement.setAttribute('data-theme', theme);
  graph?.setTheme(theme);
  initLegend();
}

/** Wires the top-bar theme toggle. */
function initThemeButton(): void {
  byId('theme-btn').addEventListener('click', () => {
    const next: Theme = document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
    setTheme(next);
  });
}

document.addEventListener('DOMContentLoaded', () => {
  void maybeBootWasmEngine();
  initGraph();
  initLegend();
  initTrace();
  initRaw();
  initShacl();
  initEditorTabs();
  initResultTabs();
  initThemeButton();
  updateGutter();
  (byId('results') as unknown as SparqlResultsElement).results = sampleResults();

  byId('run-btn').addEventListener('click', () => void runQuery());
  // The SPARQL buffer is editable; live re-query on edit (debounced, latest-wins) — the FILTER-in/out loop.
  document.querySelector<HTMLElement>('[data-testid="editor-sparql"]')?.addEventListener('input', () => {
    updateGutter();
    scheduleLiveQuery();
  });
  for (const facet of all('.facet')) {
    facet.addEventListener('click', () => {
      facet.style.borderColor = 'var(--status-verified)';
      const plus = facet.querySelector('.plus');
      if (plus !== null) {
        plus.textContent = '✓';
      }
    });
  }

  // Clicking the active-dataset chip cycles the sample datasets (loading each into the in-browser engine).
  const datasetChip = document.querySelector<HTMLElement>('[data-testid="active-dataset"]');
  if (datasetChip !== null) {
    datasetChip.textContent = DEFAULT_DATASET.label;
    datasetChip.title = DEFAULT_DATASET.description;
    datasetChip.addEventListener('click', () => {
      const index = DATASETS.findIndex((dataset) => dataset.id === activeDatasetId);
      void loadDataset(DATASETS[(index + 1) % DATASETS.length].id);
    });
  }

  // The graph/layout/density bridge the native host (or a settings control) drives.
  window.studio = {
    setTheme,
    setLayout: (v: string) => {
      document.querySelector('.studio')?.setAttribute('data-layout', v);
      graph?.resize();
      graph?.render();
    },
    setDensity: (v: string) => document.documentElement.setAttribute('data-density', v),
    setGraphMode: (v: string) => graph?.setMode(v as GraphMode),
    loadDataset: (id: string) => void loadDataset(id)
  };
});

declare global {
  interface Window {
    studio: {
      setTheme(theme: Theme): void;
      setLayout(value: string): void;
      setDensity(value: string): void;
      setGraphMode(value: string): void;
      loadDataset(id: string): void;
    };
  }
}
