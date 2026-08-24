// Studio shell controller: editor language tabs, result view tabs, the gutter, the streaming status
// readout, theme, the graph, the trace feed, the engine-source picker, the worlds strip — and the wiring
// of Run to the VeritasTransport in the active world (queries through the query face, updates through the
// worlds face). Every panel answers for the attached engine, the loaded dataset, and the active world alone. There is one
// dataset-loading path — applyLoadedDataset — and startup (which reads the `dataset` link parameter, either a
// distributed dataset's id or an absolute https URL to fetch), the picker, the file opener and the host bridge
// all take it: the Turtle goes into the engine, the address bar is replaced so it always shares what is
// loaded, the graph query and the showcase query run, the
// data is validated against the shapes the dataset ships, and the table, raw, graph, conformance, trace and
// status surfaces paint from those runs. Where a source cannot answer, a panel says so; nothing is ever
// painted from content that did not come through the engine. Run re-executes the active buffer against the
// resolved transport (the native bridge, the in-browser engine, this origin's CLI server, or a user-entered
// custom SPARQL endpoint). Framework-free; the components register on import.

import './services/host-bridge';
import './components/sparql-results';
import type { StudioData } from './data';
import { buildGraphData } from './graph-data';
import { loadManifest, fetchDataset, datasetFromFile, type StudioDatasetEntry, type LoadedDataset } from './datasets';
import { installCompletion } from './completion-popup';
import { completionsFor, loadVocabulary, parsePrefixes, parserCompletions, type VocabularyView } from './query-completion';
import { turtleCompletionsFor, turtleParserCompletions } from './turtle-completion';
import { installLiteralDiagnostics, type LiteralDiagnosticsView } from './literal-diagnostics';
import { bindGraphViewControls, createGraphView, detachedGraphCanvas, replaceGraphCanvas, type GraphMode, type GraphView } from './graph';
import { HttpVeritasTransport, WasmVeritasTransport, resolveTransport, serverEngineAvailable, type CompletionContextDto, type LiteralDiagnosisDto, type SparqlOutcome, type TraceEvent, type VeritasTransport, type VeritasWasmEngine, type WorldDto, type WorldsDiffDto } from './services/veritas-transport';
import { SCENARIO_LEVER_QUERY, diffSummary, dropVerdict, forkVerdict, resolveActiveWorld, scenarioLeversFrom, scenarioUpdateText, sparqlOperationKind, worldsDiffRows, worldsStripView, type ScenarioLeverSetting, type ScenarioLeverView } from './worlds-strip';
import { isValidEndpointUrl, loadEngineSourceSelection, saveEngineSourceSelection, type EngineSourceKind } from './services/engine-source';
import { shaclReportRows, type ShaclReportView } from './shacl-report';
import { rawErrorText, rawResultsText } from './raw-view';
import { datasetLinkHref, isRemoteDatasetUrl, linkedDataset, remoteDatasetName } from './dataset-link';
import type { SparqlResultsElement } from './components/sparql-results';

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

/** The SPARQL buffer's geometry-literal diagnostics overlay; null until the buffer is wired at init. */
let literalDiagnostics: LiteralDiagnosticsView | null = null;

/**
 * Diagnoses one geometry literal through the active transport — resolved per call, so a source switch
 * takes effect on the next scan; a source with no diagnostics face answers null and nothing is painted.
 * @param datatypeIri The literal's datatype IRI.
 * @param body The literal's value (escapes already resolved).
 * @returns The diagnosis, or null.
 */
function describeLiteralOnTransport(datatypeIri: string, body: string): Promise<LiteralDiagnosisDto | null> {
  return transport.describeLiteral(datatypeIri, body);
}

/**
 * Describes the SPARQL completion context at a caret through the active transport — resolved per call, so a
 * source switch takes effect on the next keystroke; a source with no completion face answers null and the
 * popup falls back to its token heuristic.
 * @param query The buffer's full text.
 * @param caretOffset The caret's UTF-16 index into that text.
 * @returns The completion context, or null.
 */
function describeCompletionOnTransport(query: string, caretOffset: number): Promise<CompletionContextDto | null> {
  return transport.describeCompletion(query, caretOffset);
}

/** The world Run, updates, the graph, the vocabulary, and validation execute in; null is the primary world (the plain faces). */
let activeWorld: string | null = null;

/** The worlds the active source last listed; empty when the source carries no worlds face. */
let worldsListing: readonly WorldDto[] = [];

/** The worlds-listing generation: bumped per strip refresh, so a listing from a superseded source or refresh never paints. */
let worldsGen = 0;

/** The base world the diff panel compares against; null is the primary world. */
let diffBase: string | null = null;

/** The diff generation: a diff document paints only while it is the latest asked for. */
let diffGen = 0;

/**
 * Runs a SPARQL query in the active world through the active transport: the plain protocol face for the
 * primary world, the world-scoped face otherwise — so a source without the worlds face keeps answering
 * everything it always answered, and a fork answers the same surfaces the primary does.
 * @param query The query text.
 * @param signal An abort signal for the request.
 * @returns The query outcome.
 */
function runSparqlOnActiveWorld(query: string, signal?: AbortSignal): Promise<SparqlOutcome> {
  return activeWorld === null ? transport.runSparql(query, signal) : transport.runSparqlIn(activeWorld, query, signal);
}

/** The source the shell is actually querying, reflected in the picker; the browser boot default is this origin's HTTP front door. */
let activeSourceKind: EngineSourceKind = 'server';

/** The custom endpoint currently attached; empty when the active source is not a custom endpoint. */
let activeCustomEndpoint = '';

/** The source the user explicitly chose in the picker, or null while the boot-time automatic selection stands. */
let chosenSource: EngineSourceKind | null = null;

/** The distributed datasets (from datasets/manifest.json); populated on init. */
let datasetManifest: StudioDatasetEntry[] = [];

/** The dataset currently loaded into the engine and reflected in the editors; null before the first load. */
let activeDataset: LoadedDataset | null = null;

/** The absolute https URL the loaded dataset was fetched from; null when the dataset came from elsewhere. */
let activeRemoteUrl: string | null = null;

/** Loads the dataset manifest and fills the picker; tolerates a missing manifest (Open file… still works). */
async function initDatasets(): Promise<void> {
  try {
    datasetManifest = await loadManifest();
  } catch (error) {
    // No manifest reachable: the dataset picker is empty, but the file picker remains usable.
    console.error('dataset manifest failed to load', error);
    datasetManifest = [];
  }

  populateDatasetSelect();
}

/**
 * Selects the engine at boot — one codebase, one build, hosted anywhere. The desktop shell's bridge
 * (already resolved by `resolveTransport`) wins outright. Otherwise an explicit `?engine=wasm` forces the
 * in-browser engine; then a persisted picker selection applies (a custom endpoint attaches directly, with
 * no probe and no WASM boot); with no override the origin is probed (`serverEngineAvailable`): a CLI-served
 * page answers `GET /config` and keeps the HTTP transport, while a static host (GitHub Pages / offline)
 * has no such endpoint, so the in-browser WASM engine is booted. The manifest must already be loaded
 * (initDatasets).
 */
async function selectEngine(): Promise<void> {
  if (window.veritas !== undefined) {
    // The desktop shell injected its bridge; resolveTransport already chose it — never probe or boot WASM.
    return;
  }

  const forceWasm = new URLSearchParams(location.search).get('engine') === 'wasm';
  if (forceWasm) {
    await bootInBrowserEngineAtStartup();

    return;
  }

  const persisted = loadEngineSourceSelection();
  if (persisted?.kind === 'custom' && isValidEndpointUrl(persisted.endpoint)) {
    attachCustomEndpoint(persisted.endpoint);

    return;
  }

  if (persisted?.kind === 'wasm') {
    await bootInBrowserEngineAtStartup();

    return;
  }

  if (await serverEngineAvailable()) {
    // A server-side engine answered at this origin: keep the HTTP transport resolveTransport already chose,
    // and settle its surfaces through the one chokepoint (this branch establishes the live source).
    activeSourceKind = 'server';
    reflectEngineSource('server', '');
    onTransportChanged();

    return;
  }

  await bootInBrowserEngineAtStartup();
}

/**
 * A boot-time in-browser boot with its failure surfaced: on success the picker reads the in-browser
 * engine; on failure the picker reverts to the source actually attached (the boot never changed the
 * transport) and the results panel says what happened — the picker must never claim an engine that is
 * not there.
 */
async function bootInBrowserEngineAtStartup(): Promise<void> {
  reflectEngineSource('wasm', '');
  await bootInBrowserEngine();
  if (window.veritasEngine === undefined) {
    reflectEngineSource(activeSourceKind, activeCustomEndpoint);
    (byId('results') as unknown as SparqlResultsElement)
      .showMessage('The in-browser engine failed to boot — no engine is attached.', 'error');
  }
}

/** The in-flight in-browser boot, shared so overlapping boot requests await one boot; null when none is in flight. */
let wasmBootInFlight: Promise<void> | null = null;

/**
 * Boots the in-browser WASM engine, deduplicating overlapping requests onto one in-flight boot: the picker
 * can ask for the engine while a boot started at page load is still running, and two concurrent runtime
 * creations must never race. The slot clears on completion either way, so a failed boot can be retried.
 */
function bootInBrowserEngine(): Promise<void> {
  wasmBootInFlight ??= bootInBrowserEngineOnce().finally(() => {
    wasmBootInFlight = null;
  });

  return wasmBootInFlight;
}

/**
 * Boots the in-browser WASM engine on an empty graph, re-resolves the transport to it, and opens the first
 * distributed dataset through the one dataset-loading path — the very path the picker takes, so startup is
 * an ordinary dataset switch and nothing reaches the panels any other way. With no datasets distributed the
 * engine simply stands empty until the picker or a file supplies one. A boot failure leaves the default
 * transport in place and is surfaced for diagnosis.
 */
async function bootInBrowserEngineOnce(): Promise<void> {
  try {
    const { bootWasmEngine } = await import('./services/wasm-engine');
    // Base-relative so the runtime loads from this deployment's base, not the apex: a GitHub Pages project
    // page serves the app (and its copied _framework) under e.g. /Repo/. import.meta.env.BASE_URL ends in '/'.
    const frameworkUrl = `${import.meta.env.BASE_URL}_framework/dotnet.js`;
    await bootWasmEngine(frameworkUrl);
    if (sourceRedirectedDuringBoot()) {
      return;
    }

    transport = resolveTransport();
    activeSourceKind = 'wasm';
    onTransportChanged();
    const startupEntry = datasetManifest[0];
    if (startupEntry !== undefined) {
      // A shared link picks the dataset; everything else about the load is the ordinary path.
      const linked = linkedDataset(location.search, datasetManifest.map((entry) => entry.id));
      if (linked !== null && isRemoteDatasetUrl(linked)) {
        await loadRemoteDataset(linked, startupEntry.id);
      } else {
        await loadDataset(linked ?? startupEntry.id);
      }
    }
  } catch (error) {
    // Stay on the default transport if the engine fails to boot; surface it for diagnosis.
    console.error('In-browser WASM engine failed to boot', error);
  }
}

/**
 * Whether the user pointed the shell at another source while an in-browser boot was in flight; the booted
 * engine then stays available (the editors' parser-driven completion uses it) but the transport — and the
 * dataset reflection — belong to the user's choice, so the boot must not clobber them.
 */
function sourceRedirectedDuringBoot(): boolean {
  return chosenSource !== null && chosenSource !== 'wasm';
}

/**
 * Attaches a custom SPARQL Protocol endpoint: queries POST there, and none of the first-party faces are
 * attached — no trace stream, no literal diagnostics, no completion, no vocabulary, and no worlds — so the
 * trace panel disables, the editor paints no marks, the popup proposes from its token heuristic and the
 * corpus already loaded, and the worlds strip hides.
 * @param endpoint The user-entered SPARQL Protocol endpoint.
 */
function attachCustomEndpoint(endpoint: string): void {
  transport = new HttpVeritasTransport({
    endpoint,
    traceEndpoint: null,
    literalDiagnosticsEndpoint: null,
    completionEndpoint: null,
    turtleCompletionEndpoint: null,
    editorVocabularyEndpoint: null,
    worldsEndpoint: null
  });
  activeSourceKind = 'custom';
  activeCustomEndpoint = endpoint;
  lastQueryText = '';
  reflectEngineSource('custom', endpoint);
  onTransportChanged();
}

/** Reflects an engine source in the picker: the select value, and the endpoint input shown only for custom. */
function reflectEngineSource(kind: EngineSourceKind, endpoint: string): void {
  const select = document.querySelector<HTMLSelectElement>('[data-testid="engine-select"]');
  if (select !== null) {
    select.value = kind;
  }

  const input = document.querySelector<HTMLInputElement>('[data-testid="endpoint-input"]');
  if (input !== null) {
    input.hidden = kind !== 'custom';
    if (kind === 'custom' && endpoint.length > 0) {
      input.value = endpoint;
    }
  }
}

/** Refreshes the transport-derived surfaces after a source switch: the completion vocabulary, the assist terms, and the graph. */
async function refreshTransportSurfaces(): Promise<void> {
  await refreshVocabulary();
  renderAssistTerms();
  await deriveAndMountGraph();
}

/**
 * Switches the engine source on the picker's word: persists the choice, re-attaches the transport, and
 * refreshes the transport-derived surfaces. A custom selection with no usable URL only reveals the endpoint
 * input; a server selection whose origin answers no `/config` marker reverts to the current source with a
 * message rather than attaching a dead transport.
 */
async function switchEngineSource(kind: EngineSourceKind, endpoint: string): Promise<void> {
  const results = byId('results') as unknown as SparqlResultsElement;

  if (kind === 'custom') {
    if (!isValidEndpointUrl(endpoint)) {
      // No usable URL yet: show the input and wait for one; nothing is persisted or attached.
      reflectEngineSource('custom', endpoint);

      return;
    }

    if (chosenSource === 'custom' && endpoint === activeCustomEndpoint) {
      return;
    }

    chosenSource = 'custom';
    attachCustomEndpoint(endpoint);
    saveEngineSourceSelection({ kind: 'custom', endpoint });
    results.showMessage(`Querying ${endpoint} — Run ▸ to query.`, 'idle');
    await refreshTransportSurfaces();

    return;
  }

  if (kind === 'server') {
    if (!(await serverEngineAvailable())) {
      results.showMessage('No server-side engine answers at this origin — staying on the current source.', 'idle');
      reflectEngineSource(activeSourceKind, activeCustomEndpoint);

      return;
    }

    chosenSource = 'server';
    transport = new HttpVeritasTransport();
    activeSourceKind = 'server';
    activeCustomEndpoint = '';
    lastQueryText = '';
    saveEngineSourceSelection({ kind: 'server', endpoint: '' });
    reflectEngineSource('server', '');
    onTransportChanged();
    results.showMessage("Querying this origin's server engine — Run ▸ to query.", 'idle');
    await refreshTransportSurfaces();

    return;
  }

  chosenSource = 'wasm';
  activeCustomEndpoint = '';
  lastQueryText = '';
  saveEngineSourceSelection({ kind: 'wasm', endpoint: '' });
  reflectEngineSource('wasm', '');
  const engine = window.veritasEngine;
  if (engine !== undefined) {
    transport = new WasmVeritasTransport(engine);
    activeSourceKind = 'wasm';
    onTransportChanged();
  } else {
    results.showMessage('Booting the in-browser engine…', 'loading');
    await bootInBrowserEngine();
    if (window.veritasEngine === undefined) {
      // The boot failed (already surfaced for diagnosis): the previous transport stands, so say so.
      results.showMessage('The in-browser engine failed to boot — staying on the current source.', 'error');
      reflectEngineSource(activeSourceKind, activeCustomEndpoint);

      return;
    }
  }

  results.showMessage('Querying the in-browser engine — Run ▸ to query.', 'idle');
  await refreshTransportSurfaces();
}

/** Wires the engine-source picker; hidden outright under the desktop shell's bridge, which wins at boot. */
function initEnginePicker(): void {
  const group = document.querySelector<HTMLElement>('.tb-engine');
  const select = document.querySelector<HTMLSelectElement>('[data-testid="engine-select"]');
  const input = document.querySelector<HTMLInputElement>('[data-testid="endpoint-input"]');
  if (group === null || select === null || input === null) {
    return;
  }

  if (window.veritas !== undefined) {
    group.hidden = true;

    return;
  }

  select.addEventListener('change', () => {
    const kind = select.value as EngineSourceKind;
    if (kind === 'custom') {
      input.hidden = false;
      if (isValidEndpointUrl(input.value.trim())) {
        void switchEngineSource('custom', input.value.trim());
      } else {
        input.focus();
      }

      return;
    }

    void switchEngineSource(kind, '');
  });

  input.addEventListener('change', () => {
    const endpoint = input.value.trim();
    if (isValidEndpointUrl(endpoint)) {
      void switchEngineSource('custom', endpoint);
    }
  });
  input.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') {
      const endpoint = input.value.trim();
      if (isValidEndpointUrl(endpoint)) {
        void switchEngineSource('custom', endpoint);
      }
    }
  });
}

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

/**
 * Reflects a dataset in the shell: the active-dataset readout, the picker selection, the address bar, and the
 * editors. The address is replaced, never pushed — a dataset switch is what the page is showing, not a place
 * the reader navigated to, so Back still leaves the app rather than walking the switches.
 */
function applyDataset(dataset: LoadedDataset): void {
  const chip = document.querySelector<HTMLElement>('[data-testid="active-dataset"]');
  if (chip !== null) {
    chip.textContent = dataset.label;
    chip.title = dataset.description;
  }

  const distributed = datasetManifest.some((entry) => entry.id === dataset.id);
  const select = document.querySelector<HTMLSelectElement>('[data-testid="dataset-select"]');
  if (select !== null && distributed) {
    select.value = dataset.id;
  }

  history.replaceState(null, '', datasetLinkHref(location.href, distributed ? dataset.id : activeRemoteUrl));

  setEditorBlock('sparql', dataset.query);
  setEditorBlock('shacl', dataset.shapes);
  setEditorBlock('turtle', dataset.turtle);
}

/**
 * The one dataset-loading path, taken by startup, the picker, the file opener, and the host bridge alike:
 * the Turtle goes into the engine, the editors and the readout reflect the documents, the trace feed drops
 * to idle (the previous dataset's decisions describe nothing here), the graph and completion vocabulary are
 * re-derived, the data is validated against the shapes the dataset ships, and the dataset's showcase query
 * runs — so every panel shows this dataset, from this engine, and nothing else ever reaches them.
 * @param dataset The fetched dataset.
 */
async function applyLoadedDataset(dataset: LoadedDataset): Promise<void> {
  activeDataset = dataset;
  const engine = (globalThis as { veritasEngine?: VeritasWasmEngine }).veritasEngine;
  if (engine !== undefined) {
    await engine.loadTurtle(dataset.turtle);
  }

  applyDataset(dataset);
  resetTraceFeed();
  // A load starts a fresh primary world and forks do not survive it: the strip re-lists before the panels derive.
  activeWorld = null;
  await syncWorldsStrip();
  await deriveAndMountGraph();
  await refreshVocabulary();
  renderAssistTerms();
  await syncShaclTab();
  await runSparqlIntoPanels(dataset.query, false);
}

/** Opens a distributed dataset by id: fetches its documents and takes them down the one loading path. */
async function loadDataset(id: string): Promise<void> {
  const entry = datasetManifest.find((dataset) => dataset.id === id) ?? datasetManifest[0];
  if (entry === undefined) {
    return;
  }

  activeRemoteUrl = null;
  await applyLoadedDataset(await fetchDataset(entry));
}

/**
 * Opens a remote Turtle document a shared link addresses: a plain cross-origin fetch with no credentials,
 * subject to the browser's CORS rules, taken down the same loading path an opened file takes — a generic
 * explore query and no shapes, named by the URL's last path segment. A document the browser will not hand
 * over leaves the shell on the dataset it would otherwise have opened.
 * @param url The remote document's absolute https URL.
 * @param fallbackId The distributed dataset to open instead when the fetch does not succeed.
 */
async function loadRemoteDataset(url: string, fallbackId: string): Promise<void> {
  try {
    const response = await fetch(url, { credentials: 'omit' });
    if (!response.ok) {
      throw new Error(`remote dataset fetch failed (${url}): ${response.status}`);
    }

    const turtle = await response.text();
    activeRemoteUrl = url;
    await applyLoadedDataset(datasetFromFile(remoteDatasetName(url), turtle));
  } catch (error) {
    console.error('Remote dataset could not be loaded', error);
    await loadDataset(fallbackId);
  }
}

/** Opens a user-chosen RDF file: the same loading path, with a generic explore query and no shapes. */
async function openFile(file: File): Promise<void> {
  activeRemoteUrl = null;
  await applyLoadedDataset(datasetFromFile(file.name, await file.text()));
}

/** Fills the dataset picker from the manifest (in display order). */
function populateDatasetSelect(): void {
  const select = document.querySelector<HTMLSelectElement>('[data-testid="dataset-select"]');
  if (select === null) {
    return;
  }

  select.innerHTML = datasetManifest
    .map((entry) => `<option value="${entry.id}">${entry.label}</option>`)
    .join('');
}

/** The loaded data's vocabulary the intellisense proposes from; reloaded per dataset from the active source. */
let completionVocab: VocabularyView = { predicates: [], classes: [] };

/** The fixed vocabulary corpus (the source's term roster) every editor proposes from; reloaded per source. */
let editorVocab: string[] = [];

/** The live-source generation: bumped at every transport change, so an answer from a superseded source never overwrites either corpus. */
let transportGen = 0;

/** Every editor's proposal vocabulary: the fixed grammar terms (sh:/owl:/rdf:/rdfs:/xsd: and the geo:/geof:/sf:/gml: rosters, plus the bracketed full IRIs) and the loaded data's predicates and classes. */
const proposalVocabulary = (): VocabularyView => ({
  predicates: [...editorVocab, ...completionVocab.predicates],
  classes: completionVocab.classes
});

/** Reloads the completion vocabulary (predicates + classes) for the active dataset and world; a source switch mid-flight discards the answer. */
async function refreshVocabulary(): Promise<void> {
  const generation = transportGen;
  try {
    const loaded = await loadVocabulary(runSparqlOnActiveWorld, parsePrefixes(activeCode()));
    if (generation === transportGen) {
      completionVocab = loaded;
    }
  } catch {
    // No engine attached — keep the current vocabulary; keywords + prefixes still propose.
  }
}

/** The cap on terms the assist strip offers at once. */
const ASSIST_TERM_CAP = 8;

/** The assist strip's note when the active source reports no vocabulary for the loaded data. */
const ASSIST_EMPTY_NOTE = 'No terms yet — the loaded data’s classes and predicates appear here.';

/**
 * Renders the assist strip from the loaded data's own vocabulary — the classes and predicates the active
 * source reports for it — so every term offered is a term the data carries. The click that adds one is
 * delegated on the strip, so a re-render never re-binds it.
 */
function renderAssistTerms(): void {
  const strip = document.querySelector<HTMLElement>('#facets');
  if (strip === null) {
    return;
  }

  const terms = [...completionVocab.classes, ...completionVocab.predicates].slice(0, ASSIST_TERM_CAP);
  if (terms.length === 0) {
    const note = document.createElement('span');
    note.className = 'facet-note';
    note.textContent = ASSIST_EMPTY_NOTE;
    strip.replaceChildren(note);

    return;
  }

  const buttons: HTMLElement[] = [];
  for (const term of terms) {
    const button = document.createElement('button');
    button.className = 'facet';
    button.type = 'button';
    button.dataset.term = term;
    const label = document.createElement('span');
    label.textContent = term;
    const plus = document.createElement('span');
    plus.className = 'plus';
    plus.textContent = '+';
    button.append(label, plus);
    buttons.push(button);
  }

  strip.replaceChildren(...buttons);
}

/**
 * Writes a term into the SPARQL buffer at the caret — at the end when the caret is elsewhere — and fires the
 * input event the live re-query, the gutter, and the literal marks all listen on.
 * @param term The term to insert.
 */
function insertIntoSparqlBuffer(term: string): void {
  const editor = document.querySelector<HTMLElement>('[data-testid="editor-sparql"]');
  if (editor === null) {
    return;
  }

  const selection = window.getSelection();
  const range = selection !== null && selection.rangeCount > 0 ? selection.getRangeAt(0) : null;
  if (selection !== null && range !== null && editor.contains(range.startContainer) && range.startContainer.nodeType === Node.TEXT_NODE) {
    const node = range.startContainer;
    const offset = range.startOffset;
    const text = node.textContent ?? '';
    node.textContent = text.slice(0, offset) + term + text.slice(offset);
    range.setStart(node, offset + term.length);
    range.collapse(true);
    selection.removeAllRanges();
    selection.addRange(range);
  } else {
    editor.textContent = `${editor.textContent ?? ''}${term}`;
  }

  editor.dispatchEvent(new Event('input', { bubbles: true }));
}

/**
 * Reloads the fixed vocabulary corpus from the active source. A source switch mid-flight discards the
 * answer, and a source that answers null leaves the corpus in place: the corpus is static authoring
 * vocabulary the editors offer regardless of what the attached source can do.
 */
async function reloadEditorVocabulary(): Promise<void> {
  const generation = transportGen;
  try {
    const corpus = await transport.editorVocabulary();
    if (generation === transportGen && corpus !== null) {
      editorVocab = corpus;
    }
  } catch {
    // The source's vocabulary face faulted: the editors keep the corpus already loaded.
  }
}

/** The active graph view (the 2D placeholder, then the BabylonJS view); null before init. */
let graph: GraphView | null = null;

/** The in-flight streaming-animation frame handle. */
let streamRaf = 0;

/** The text of the active editor language buffer (the run target). */
function activeCode(): string {
  const editor = document.querySelector<HTMLElement>('.editor');
  const lang = editor?.dataset.lang ?? 'sparql';
  const code = document.querySelector<HTMLElement>(`.code-only.${lang}`);

  return code?.textContent ?? '';
}

/** The graph data currently mounted, or null when the canvas holds no picture; the legend and HUD read it. */
let graphData: StudioData | null = null;

/** The graph HUD's note while the canvas holds no picture. */
const GRAPH_EMPTY_NOTE = 'no graph yet — the loaded dataset has no edges to draw.';

/** Renders the node-kind colour legend from the mounted graph — each kind's swatch, label and node count — empty when nothing is mounted. */
function renderLegend(): void {
  byId('legend').innerHTML = (graphData?.types ?? [])
    .map((t) => `<span class="li"><span class="sw" style="background:oklch(0.62 ${t.c} ${t.hue})"></span>${t.label} · ${fmt(t.count)}</span>`)
    .join('');
}

/** Renders the graph HUD readout: the mounted picture's shape, or the empty note. */
function renderGraphHud(): void {
  const pill = byId('graph-hud-pill');
  if (graphData === null) {
    pill.textContent = GRAPH_EMPTY_NOTE;

    return;
  }

  pill.textContent = `showing ${fmt(graphData.clusters.length)} nodes · ${fmt(graphData.links.length)} edges · ${fmt(graphData.types.length)} kinds`;
}

/** The current graph mount generation; guards a slow BabylonJS upgrade from overwriting a newer mount. */
let graphGen = 0;

/** Unbinds the graph surface controls (mode switcher, zoom, fit) from the active view, or null when none are bound. */
let unbindGraphControls: (() => void) | null = null;

/**
 * Mounts a graph dataset: the instant 2D placeholder, upgraded to the BabylonJS view when its chunk
 * loads. The upgrade builds the WebGL view on a DETACHED fresh canvas (the placeholder's canvas is
 * bound to its 2d context and can never host WebGL) and swaps it in only once construction succeeded,
 * so a failed upgrade leaves the painted placeholder standing. `data-engine` on the canvas names the
 * view actually mounted; the surface controls rebind to whichever view is live.
 */
function mountGraph(data: StudioData): void {
  const theme: Theme = (document.documentElement.getAttribute('data-theme') as Theme) ?? 'light';
  const generation = ++graphGen;
  unbindGraphControls?.();
  unbindGraphControls = null;
  graph?.dispose();
  const canvas = replaceGraphCanvas(byId<HTMLCanvasElement>('graph-canvas'));
  canvas.dataset.engine = 'placeholder';
  graph = createGraphView(canvas, data);
  graph.setTheme(theme);
  unbindGraphControls = bindGraphViewControls(graph, canvas);
  graphData = data;
  renderLegend();
  renderGraphHud();

  // Upgrade to the BabylonJS view once its code-split chunk arrives; skip if a newer mount superseded this.
  void import('./graph-babylon')
    .then(({ createBabylonGraphView }) => {
      if (generation !== graphGen) {
        return;
      }

      const upgraded = detachedGraphCanvas(byId<HTMLCanvasElement>('graph-canvas'));
      const view = createBabylonGraphView(upgraded, data);
      byId<HTMLCanvasElement>('graph-canvas').replaceWith(upgraded);
      upgraded.dataset.engine = 'babylon';
      unbindGraphControls?.();
      graph?.dispose();
      graph = view;
      graph.setTheme(theme);
      unbindGraphControls = bindGraphViewControls(graph, upgraded);
      graph.resize();
      graph.render();
    })
    .catch((error: unknown) => {
      // The chunk did not load or the WebGL view could not be built: the 2D placeholder stands, and the
      // failure is surfaced rather than silently read as an upgrade.
      console.error('The BabylonJS graph view failed to mount; the 2D placeholder stands.', error);
    });
}

/**
 * Drops the mounted graph: the controls unbind (disabling the surface buttons), the view is disposed, the
 * drawing buffer emptied (sizing it to zero discards the pixels for a 2D and a WebGL context alike, and the
 * next mount sizes it again), and the HUD says the canvas holds nothing — a picture of data the engine no
 * longer holds must not survive on screen.
 */
function clearGraph(): void {
  graphGen++;
  unbindGraphControls?.();
  unbindGraphControls = null;
  graph?.dispose();
  graph = null;
  graphData = null;
  byId<HTMLCanvasElement>('graph-canvas').width = 0;
  renderLegend();
  renderGraphHud();
}

/** The graph-derivation generation; a result mounts only if it is still the latest, so a slow response from a superseded transport never overwrites a newer mount. */
let graphQueryGen = 0;

/** Runs the active dataset's graph query in the active world and remounts the graph from the result; a query that yields no edges clears it. */
async function deriveAndMountGraph(): Promise<void> {
  if (activeDataset === null) {
    clearGraph();

    return;
  }

  const generation = ++graphQueryGen;
  try {
    const outcome = await runSparqlOnActiveWorld(activeDataset.graphQuery);
    if (generation !== graphQueryGen) {
      return;
    }

    const data = outcome.ok ? buildGraphData(outcome.results.results?.bindings ?? []) : null;
    if (data !== null && data.clusters.length > 0) {
      mountGraph(data);

      return;
    }

    clearGraph();
  } catch {
    if (generation !== graphQueryGen) {
      return;
    }

    // No engine attached, or the graph query cannot run here: the canvas says so rather than keeping a picture.
    clearGraph();
  }
}

/** The live trace subscription's unsubscribe handle, or null when none is open. */
let traceUnsubscribe: (() => void) | null = null;

/** The transport the open trace subscription belongs to; a source switch invalidates it. */
let traceSubscribedTransport: VeritasTransport | null = null;

/** The rendered trace rows for the current run; bounded by the cap, with the excess folded into one overflow row. */
let traceRowCount = 0;

/** The cap on rendered trace rows per run. */
const TRACE_ROW_CAP = 500;

/** The live trace events folded into the overflow row past the cap for the current run. */
let traceOverflow = 0;

/** The correlation id of the run the panel is showing, or null right after a clear; a different id starts a fresh view (latest run wins). */
let traceViewCorrelation: string | null = null;

/** The trace feed's idle placeholder: the panel is bound to the source's stream and waiting for a run. */
const TRACE_IDLE_NOTE = 'Run ▸ to stream the engine decisions behind a query.';

/**
 * Builds a trace-feed note row — the placeholder the panel shows while it holds no run's decisions.
 * @param text The note text.
 * @returns The row element.
 */
function traceNoteRow(text: string): HTMLElement {
  const row = document.createElement('div');
  row.className = 'tr note';
  const mark = document.createElement('span');
  mark.className = 'mark';
  mark.textContent = '·';
  const body = document.createElement('div');
  const why = document.createElement('div');
  why.className = 'why';
  why.textContent = text;
  body.append(why);
  row.append(mark, body);

  return row;
}

/**
 * Reflects the active source's trace capability in the panel: a first-party engine streams live trace, so
 * the feed drops to its listening placeholder, while a generic SPARQL endpoint offers none, so the panel
 * DISABLES — never errors.
 */
function syncTracePanel(): void {
  const panel = document.querySelector<HTMLElement>('.panel.trace');
  const status = document.querySelector<HTMLElement>('[data-testid="trace-status"]');
  if (panel === null || status === null) {
    return;
  }

  if (transport.traceAvailable) {
    panel.removeAttribute('data-trace');
    resetTraceFeed();

    return;
  }

  panel.setAttribute('data-trace', 'off');
  status.textContent = 'not available for this source';
  byId('trace-list').replaceChildren(traceNoteRow('This engine source offers no trace stream.'));
}

/** Drops the open trace subscription, if any. */
function resetTraceSubscription(): void {
  traceUnsubscribe?.();
  traceUnsubscribe = null;
  traceSubscribedTransport = null;
}

/**
 * The one transport-change chokepoint, on the path of every source establishment: it opens the new
 * generation (so an in-flight vocabulary answer from the superseded source is discarded), drops the stale
 * subscription, re-syncs the trace panel and the worlds strip, re-diagnoses the buffer, reloads the editor
 * corpus, re-answers the conformance tab for the new source, and drops the run readouts — so neither a
 * superseded source's stream, nor its marks, nor its terms, nor its verdicts, nor its worlds, nor its
 * answers outlive it.
 */
function onTransportChanged(): void {
  transportGen++;
  queryGen++;
  resetTraceSubscription();
  syncTracePanel();
  literalDiagnostics?.rescan();
  void reloadEditorVocabulary();
  void syncShaclTab();
  activeWorld = null;
  void syncWorldsStrip();
  showRaw(RAW_IDLE_NOTE, 'idle');
  resetStatusReadout();
}

/** Opens the live trace subscription on the active transport, or re-binds it after a source switch; a no-op on a source with no trace stream. */
function ensureTraceSubscription(): void {
  if (!transport.traceAvailable) {
    return;
  }

  if (traceSubscribedTransport === transport && traceUnsubscribe !== null) {
    return;
  }

  resetTraceSubscription();
  traceSubscribedTransport = transport;
  traceUnsubscribe = transport.subscribeTrace(appendTraceRow);
}

/**
 * Drops the trace feed to its idle state — the listening placeholder and the listening status — for a fresh
 * run, a dataset switch, or a source switch; a no-op on a source with no trace stream, whose panel keeps its
 * disabled placeholder. The first streamed event of a run replaces the placeholder.
 */
function resetTraceFeed(): void {
  if (!transport.traceAvailable) {
    return;
  }

  byId('trace-list').replaceChildren(traceNoteRow(TRACE_IDLE_NOTE));
  traceRowCount = 0;
  traceOverflow = 0;
  traceViewCorrelation = null;
  traceOperations = 0;
  tracedOperators.length = 0;
  const status = document.querySelector<HTMLElement>('[data-testid="trace-status"]');
  if (status !== null) {
    status.innerHTML = '<span class="dot live"></span>listening';
  }
}

/** Settles the trace status after a run's outcome rendered: the number of events the run streamed. */
function settleTraceStatus(): void {
  const status = document.querySelector<HTMLElement>('[data-testid="trace-status"]');
  if (status !== null && transport.traceAvailable) {
    status.textContent = `${fmt(traceRowCount + traceOverflow)} events`;
  }
}

/** The operator evaluations the current run streamed; the status readout's operation count. */
let traceOperations = 0;

/** The operators the current run evaluated, in first-seen order and without repeats; the plan chip reads them. */
const tracedOperators: string[] = [];

/** The cap on operators the plan chip names before eliding the rest. */
const PLAN_OPERATOR_CAP = 4;

/** The operator evaluations the current run streamed, or null on a source that streams no trace and reports none. */
function tracedOperationCount(): number | null {
  return transport.traceAvailable ? traceOperations : null;
}

/**
 * The plan the current run executed, read off its own trace: the operators it evaluated in order, the tail
 * elided past the cap. A source that streams no trace — or a run the engine answered without evaluating an
 * operator — claims no plan.
 */
function tracedPlan(): string {
  if (tracedOperators.length === 0) {
    return UNREPORTED;
  }

  const named = tracedOperators.slice(0, PLAN_OPERATOR_CAP).join(' · ');

  return tracedOperators.length > PLAN_OPERATOR_CAP ? `${named} · …` : named;
}

/**
 * Folds one trace event into the run's plan readout: operator evaluations are counted, and each operator is
 * named once, in the order the engine reached it.
 * @param event The streamed event.
 */
function foldTraceIntoPlan(event: TraceEvent): void {
  if (event.kind !== 'operator') {
    return;
  }

  traceOperations++;
  const operator = event.term;
  if (operator !== undefined && operator.length > 0 && !tracedOperators.includes(operator) && tracedOperators.length <= PLAN_OPERATOR_CAP) {
    tracedOperators.push(operator);
  }
}

/**
 * Renders one live trace event as a feed row. Rewrite verdicts take the chose/excluded marks (a rule that
 * applied vs one that matched and declined); operator evaluations and interceptions are notes. Rows are
 * DOM-built with textContent so event text never renders as markup.
 */
function appendTraceRow(event: TraceEvent): void {
  const list = byId('trace-list');

  // Latest run wins: a new correlation id starts a fresh view, so an edit-driven live re-query — or, on
  // the served tier, another client's run arriving on the shared stream — replaces the panel rather than
  // silently mixing into what reads as one run's decisions.
  if (traceViewCorrelation !== event.correlationId) {
    traceViewCorrelation = event.correlationId;
    list.innerHTML = '';
    traceRowCount = 0;
    traceOverflow = 0;
    traceOperations = 0;
    tracedOperators.length = 0;
  }

  // Counted before the render cap: the readouts describe the run, not the rows the panel had room for.
  foldTraceIntoPlan(event);
  if (traceRowCount >= TRACE_ROW_CAP) {
    traceOverflow++;
    const tail = list.querySelector<HTMLElement>('.tr.overflow .why');
    if (tail !== null) {
      tail.textContent = `+${fmt(traceOverflow)} more events`;

      return;
    }

    const overflowRow = document.createElement('div');
    overflowRow.className = 'tr note overflow';
    const overflowMark = document.createElement('span');
    overflowMark.className = 'mark';
    overflowMark.textContent = '·';
    const overflowBody = document.createElement('div');
    const overflowWhy = document.createElement('div');
    overflowWhy.className = 'why';
    overflowWhy.textContent = `+${fmt(traceOverflow)} more events`;
    overflowBody.append(overflowWhy);
    overflowRow.append(overflowMark, overflowBody);
    list.append(overflowRow);

    return;
  }

  traceRowCount++;
  const kindClass = event.kind === 'rewrite-applied' ? 'chose' : event.kind === 'rewrite-abstained' ? 'excluded' : 'note';
  const mark = kindClass === 'chose' ? '✓' : kindClass === 'excluded' ? '✕' : '·';
  const row = document.createElement('div');
  row.className = `tr ${kindClass}`;
  const markSpan = document.createElement('span');
  markSpan.className = 'mark';
  markSpan.textContent = mark;
  const body = document.createElement('div');
  const termDiv = document.createElement('div');
  termDiv.className = 'term';
  termDiv.textContent = event.term ?? event.kind;
  const whyDiv = document.createElement('div');
  whyDiv.className = 'why';
  whyDiv.textContent = event.detail;
  body.append(termDiv, whyDiv);
  row.append(markSpan, body);
  list.append(row);
}

/** The Raw view's note before any run has answered on the active source. */
const RAW_IDLE_NOTE = 'No run yet — Run ▸ and the answer document lands here.';

/**
 * Paints the Raw view: the text of the last run's answer document, or a note when there is none.
 * @param text The document text, or the note.
 * @param state The state the text stands for (`idle`, `loading`, `resulted`, or `error`).
 */
function showRaw(text: string, state: string): void {
  const pre = byId('raw-pre');
  pre.dataset.state = state;
  pre.textContent = text;
}

/** The conformance tab's note when the shell is not querying the in-browser engine, which validates. */
const SHACL_ENGINE_NOTE = 'SHACL validation runs against the in-browser engine — switch the engine source to it.';

/** The conformance tab's note while no dataset is loaded. */
const SHACL_NO_DATASET_NOTE = 'No dataset is loaded — load one to validate it against its shapes.';

/** The conformance tab's note for a dataset that ships no shapes graph (an opened file carries none). */
const SHACL_NO_SHAPES_NOTE = 'This dataset ships no SHACL shapes — author them in the SHACL editor tab and press Run ▸.';

/** The validation generation: a report paints only while it is the latest, so a superseded dataset's or source's verdict never lands. */
let shaclGen = 0;

/** The in-browser engine when the shell is querying it, or null — SHACL validation is that engine's capability. */
function inBrowserEngineSource(): VeritasWasmEngine | null {
  const engine = (globalThis as { veritasEngine?: VeritasWasmEngine }).veritasEngine;

  return activeSourceKind === 'wasm' && engine !== undefined ? engine : null;
}

/**
 * Paints one note row into the conformance tab — what the tab says instead of a report.
 * @param text The note text.
 * @param state The state the note stands for (`idle`, `loading`, or `error`), reflected on the tab body.
 */
function showShaclNote(text: string, state: string): void {
  const body = byId('shacl-body');
  const row = document.createElement('div');
  row.className = 'shacl-row';
  const mark = document.createElement('span');
  mark.className = 'ck';
  mark.textContent = '·';
  const message = document.createElement('span');
  message.className = 'msg';
  message.textContent = text;
  row.append(mark, message);
  body.dataset.state = state;
  body.replaceChildren(row);
}

/**
 * Paints a validation report into the conformance tab: the verdict, then one row per result. Rows are
 * DOM-built with textContent, so report text never renders as markup.
 * @param report The validation report.
 */
function showShaclReport(report: ShaclReportView): void {
  const rows: HTMLElement[] = [];
  for (const row of shaclReportRows(report)) {
    const element = document.createElement('div');
    element.className = row.tone === 'ok' ? 'shacl-row' : 'shacl-row warn';
    const mark = document.createElement('span');
    mark.className = `ck ${row.tone}`;
    mark.textContent = row.mark;
    const message = document.createElement('span');
    message.className = 'msg';
    message.textContent = row.message;
    const focus = document.createElement('span');
    focus.className = 'focus';
    focus.textContent = row.focus;
    element.append(mark, message, focus);
    rows.push(element);
  }

  const body = byId('shacl-body');
  body.dataset.state = report.conforms ? 'conforms' : 'violations';
  body.replaceChildren(...rows);
}

/**
 * Validates a shapes graph against the active world's data on the in-browser engine and paints the
 * conformance tab with the outcome. A superseded validation — a newer dataset, source, world, or run
 * started meanwhile — never paints.
 * @param engine The in-browser engine.
 * @param shapes The SHACL shapes graph (Turtle).
 */
async function validateShapesIntoTab(engine: VeritasWasmEngine, shapes: string): Promise<void> {
  const generation = ++shaclGen;
  showShaclNote('Validating…', 'loading');
  try {
    const report = JSON.parse(await engine.validateShacl(shapes, activeWorld)) as ShaclReportView;
    if (generation !== shaclGen) {
      return;
    }

    showShaclReport(report);
  } catch (error) {
    if (generation !== shaclGen) {
      return;
    }

    showShaclNote(`Validation failed: ${String(error)}`, 'error');
  }
}

/**
 * Answers the conformance tab for the shell's current state, on every dataset load and every source switch:
 * against the in-browser engine the loaded dataset is validated with the shapes it ships and the report is
 * painted; every other source, and a dataset that ships no shapes, gets the note that says what to do. The
 * tab therefore never holds a verdict about data the shell no longer has.
 */
async function syncShaclTab(): Promise<void> {
  // Supersede any in-flight validation: whatever this call paints is the tab's answer.
  shaclGen++;
  const engine = inBrowserEngineSource();
  if (engine === null) {
    showShaclNote(SHACL_ENGINE_NOTE, 'idle');

    return;
  }

  const dataset = activeDataset;
  if (dataset === null) {
    showShaclNote(SHACL_NO_DATASET_NOTE, 'idle');

    return;
  }

  if (dataset.shapes.trim().length === 0) {
    showShaclNote(SHACL_NO_SHAPES_NOTE, 'idle');

    return;
  }

  await validateShapesIntoTab(engine, dataset.shapes);
}

/**
 * Re-derives the world-scoped panels — the graph, the vocabulary and assist terms, the conformance
 * verdict — so each answers for the active world alone.
 */
async function rederiveWorldPanels(): Promise<void> {
  await deriveAndMountGraph();
  await refreshVocabulary();
  renderAssistTerms();
  await syncShaclTab();
}

/**
 * Lands a world change in the shell: an in-flight run of the previous world never paints, the run
 * readouts drop (they answered another world), the trace feed resets, and the world-scoped panels
 * re-derive.
 * @param message What the results panel says about the change.
 */
function reflectWorldSwitch(message: string): void {
  queryGen++;
  lastQueryText = '';
  showRaw(RAW_IDLE_NOTE, 'idle');
  resetStatusReadout();
  resetTraceFeed();
  (byId('results') as unknown as SparqlResultsElement).showMessage(message, 'idle');
  void rederiveWorldPanels();
}

/**
 * Paints one worlds listing into the strip and the diff surfaces: the picker, the active world's state id
 * and lineage, the drop affordance (the primary is never droppable), and the diff-base picker. An empty
 * listing hides the strip and the Diff view — the degrade a source without the worlds face presents.
 * @param worlds The listing, primary first; empty for no worlds face.
 */
function applyWorldsListing(worlds: readonly WorldDto[]): void {
  worldsListing = worlds;
  activeWorld = resolveActiveWorld(worlds, activeWorld);
  if (diffBase !== null && !worlds.some((world) => world.name === diffBase)) {
    diffBase = null;
  }

  const strip = byId('worlds-strip');
  const diffTab = document.querySelector<HTMLElement>('.res-tab[data-tab="diff"]');
  if (worlds.length === 0) {
    strip.hidden = true;
    if (diffTab !== null) {
      diffTab.hidden = true;
      // A hidden Diff tab must not stay the selected view.
      if (diffTab.getAttribute('aria-selected') === 'true') {
        selectResultTab('graph');
      }
    }

    return;
  }

  const view = worldsStripView(worlds, activeWorld);
  strip.hidden = false;
  if (diffTab !== null) {
    diffTab.hidden = false;
  }

  const select = byId<HTMLSelectElement>('world-select');
  select.replaceChildren(...view.options.map((option) => new Option(option.name, option.name, false, option.selected)));
  const state = byId<HTMLOutputElement>('world-state');
  state.textContent = view.stateId;
  state.title = view.lineage;
  byId<HTMLButtonElement>('world-drop').disabled = !view.dropEnabled;
  const base = byId<HTMLSelectElement>('diff-base-select');
  const baseName = diffBase ?? worlds[0].name;
  base.replaceChildren(...worlds.map((world) => new Option(world.name, world.name, false, world.name === baseName)));
}

/**
 * Reflects the active source's worlds capability: a first-party engine lists its worlds and the strip
 * presents them, while a source without the face hides the strip and the Diff view — the same degrade
 * class as the trace panel. A listing from a superseded refresh or source never paints.
 */
async function syncWorldsStrip(): Promise<void> {
  const generation = ++worldsGen;
  if (!transport.worldsAvailable) {
    applyWorldsListing([]);

    return;
  }

  const worlds = await transport.listWorlds();
  if (generation !== worldsGen) {
    return;
  }

  applyWorldsListing(worlds ?? []);
}

/** Switches the active world on the picker's word and lands the change; re-picking the active world repaints only. */
function switchActiveWorld(name: string): void {
  const next = resolveActiveWorld(worldsListing, name);
  if (next === activeWorld) {
    applyWorldsListing(worldsListing);

    return;
  }

  activeWorld = next;
  applyWorldsListing(worldsListing);
  const label = activeWorld ?? worldsListing[0]?.name ?? '';
  reflectWorldSwitch(`Querying world '${label}' — Run ▸ to query.`);
}

/**
 * Runs a query in a named world through the active transport: the plain face for the primary world,
 * the world-scoped face otherwise.
 * @param world The registered world the query runs in.
 * @param query The query text.
 * @returns The query outcome.
 */
function runSparqlInNamedWorld(world: string, query: string): Promise<SparqlOutcome> {
  return worldsListing.length > 0 && world === worldsListing[0].name ? transport.runSparql(query) : transport.runSparqlIn(world, query);
}

/** The levers the open scenario dialog presents; the created world's settings are read against these. */
let scenarioDialogLevers: ScenarioLeverView[] = [];

/** The lever-load generation: a load superseded by a newer one (or a closed dialog) never paints. */
let scenarioLeverGen = 0;

/**
 * Shows a refusal inside the scenario dialog, or clears it.
 * @param message The message, or null to clear.
 */
function showScenarioError(message: string | null): void {
  const error = byId('scenario-error');
  error.hidden = message === null;
  error.textContent = message ?? '';
}

/**
 * Paints the dialog's knobs from the base world's declared levers — each knob starts at that world's
 * actual value. A dataset that declares no levers hides the section, and the scenario is then a plain
 * fork the buffer can update by hand.
 * @param levers The declared levers, in presentation order.
 */
function renderScenarioLevers(levers: ScenarioLeverView[]): void {
  scenarioDialogLevers = levers;
  const section = byId<HTMLFieldSetElement>('scenario-levers');
  for (const row of [...section.querySelectorAll('.lever')]) {
    row.remove();
  }

  section.hidden = levers.length === 0;
  for (let index = 0; index < levers.length; index++) {
    const lever = levers[index];
    const row = document.createElement('p');
    row.className = 'lever';
    const label = document.createElement('label');
    label.htmlFor = `scenario-lever-${index}`;
    label.textContent = lever.label;
    const input = document.createElement('input');
    input.type = 'range';
    input.id = `scenario-lever-${index}`;
    input.min = String(lever.min);
    input.max = String(lever.max);
    input.step = String(lever.step);
    input.value = String(lever.value);
    const output = document.createElement('output');
    output.setAttribute('for', input.id);
    output.textContent = String(lever.value);
    row.append(label, input, output);
    section.append(row);
  }
}

/**
 * Loads a base world's declared levers into the open dialog; a load superseded by a newer one never
 * paints, and a source that answers none (or errors) presents no knobs rather than a failure.
 * @param base The base world whose levers and current values the knobs present.
 */
async function loadScenarioLevers(base: string): Promise<void> {
  const generation = ++scenarioLeverGen;
  const outcome = await runSparqlInNamedWorld(base, SCENARIO_LEVER_QUERY);
  if (generation !== scenarioLeverGen) {
    return;
  }

  renderScenarioLevers(outcome.ok ? scenarioLeversFrom(outcome.results.results?.bindings ?? []) : []);
}

/** Opens the create-a-scenario dialog over the current listing, the active world as the base. */
async function openScenarioDialog(): Promise<void> {
  if (worldsListing.length === 0) {
    return;
  }

  const baseSelect = byId<HTMLSelectElement>('scenario-base');
  const activeName = activeWorld ?? worldsListing[0].name;
  baseSelect.replaceChildren(...worldsListing.map((world) => new Option(world.name, world.name, false, world.name === activeName)));
  byId<HTMLInputElement>('scenario-name').value = '';
  showScenarioError(null);
  renderScenarioLevers([]);
  byId<HTMLDialogElement>('scenario-dialog').showModal();
  await loadScenarioLevers(baseSelect.value);
}

/** Reads the dialog's knob positions against the levers it presents. */
function readScenarioSettings(): ScenarioLeverSetting[] {
  const settings: ScenarioLeverSetting[] = [];
  for (let index = 0; index < scenarioDialogLevers.length; index++) {
    const input = document.getElementById(`scenario-lever-${index}`) as HTMLInputElement | null;
    const lever = scenarioDialogLevers[index];
    settings.push({ lever, value: input === null ? lever.value : Number(input.value) });
  }

  return settings;
}

/**
 * Creates the scenario world from the dialog's entries: fork the base under the name, commit the moved
 * knobs as the new world's first update, and switch the shell into it. Expected refusals (a taken
 * name, a vanished base) stay inside the dialog; a created world lands like any world switch, the
 * primary untouched.
 */
async function createScenarioFromDialog(): Promise<void> {
  const name = byId<HTMLInputElement>('scenario-name').value.trim();
  if (name.length === 0) {
    return;
  }

  const base = byId<HTMLSelectElement>('scenario-base').value;
  const settings = readScenarioSettings();
  const outcome = await transport.forkWorld(base, name);
  const verdict = forkVerdict(name, outcome);
  if (!verdict.ok) {
    showScenarioError(verdict.message);

    return;
  }

  let message = verdict.message;
  const update = scenarioUpdateText(settings);
  if (update.length > 0) {
    const committed = await transport.updateIn(name, update);
    if (!committed.ok) {
      message = `Scenario world '${name}' was created, but moving the assumptions failed: ${committed.error}`;
    }
  }

  byId<HTMLDialogElement>('scenario-dialog').close();
  await syncWorldsStrip();
  activeWorld = resolveActiveWorld(worldsListing, name);
  applyWorldsListing(worldsListing);
  reflectWorldSwitch(message);
}

/** Drops the active world; the strip falls back to the primary world and the panels re-derive there. */
async function dropActiveWorld(): Promise<void> {
  if (activeWorld === null) {
    return;
  }

  const name = activeWorld;
  const outcome = await transport.dropWorld(name);
  const verdict = dropVerdict(name, outcome);
  await syncWorldsStrip();
  if (verdict.ok) {
    reflectWorldSwitch(verdict.message);
  } else {
    (byId('results') as unknown as SparqlResultsElement).showMessage(verdict.message, 'error');
  }
}

/**
 * Builds one note row of the diff table — what the panel says instead of triples.
 * @param label The note text.
 * @returns The row element.
 */
function diffNoteRow(label: string): HTMLElement {
  const row = document.createElement('tr');
  row.className = 'diff-note';
  const cell = document.createElement('td');
  cell.colSpan = 4;
  cell.textContent = label;
  row.append(cell);

  return row;
}

/**
 * Paints a diff document into the panel: the summary states the document's exact totals (the truth even
 * when the listings are capped), and the table renders per-graph headers, the listed triples, and an
 * elision note wherever the cap cut a listing. Rows are DOM-built with textContent, so term text never
 * renders as markup.
 * @param diff The diff document, or null when the source answered none.
 */
function renderWorldsDiff(diff: WorldsDiffDto | null): void {
  const summary = byId('diff-summary');
  const body = byId('diff-rows');
  if (diff === null) {
    summary.textContent = 'no diff answered';
    body.replaceChildren(diffNoteRow('The source answered no diff document.'));

    return;
  }

  summary.textContent = diffSummary(diff);
  const rows: HTMLElement[] = [];
  for (const row of worldsDiffRows(diff)) {
    if (row.kind === 'graph') {
      const header = document.createElement('tr');
      header.className = 'diff-graph';
      const cell = document.createElement('th');
      cell.scope = 'colgroup';
      cell.colSpan = 4;
      cell.textContent = row.label;
      header.append(cell);
      rows.push(header);
    } else if (row.kind === 'triple') {
      const triple = document.createElement('tr');
      triple.className = row.sign === '+' ? 'diff-add' : 'diff-remove';
      const sign = document.createElement('td');
      sign.className = 'sign';
      sign.textContent = row.sign;
      const subject = document.createElement('td');
      subject.textContent = row.subject;
      const predicate = document.createElement('td');
      predicate.textContent = row.predicate;
      const object = document.createElement('td');
      object.textContent = row.object;
      triple.append(sign, subject, predicate, object);
      rows.push(triple);
    } else {
      rows.push(diffNoteRow(row.label));
    }
  }

  body.replaceChildren(...rows);
}

/**
 * Diffs the active world against the picked base (the primary by default), showing the Diff view and
 * painting the bounded document the engine answered. A diff superseded by a newer one never paints.
 */
async function runWorldsDiff(): Promise<void> {
  if (worldsListing.length === 0) {
    return;
  }

  selectResultTab('diff');
  const from = diffBase ?? worldsListing[0].name;
  const to = activeWorld ?? worldsListing[0].name;
  byId('diff-target').textContent = to;
  byId('diff-summary').textContent = 'diffing…';
  const generation = ++diffGen;
  const diff = await transport.diffWorlds(from, to);
  if (generation !== diffGen) {
    return;
  }

  renderWorldsDiff(diff);
}

/** Wires the worlds strip and the create-a-scenario dialog: the world picker, scenario creation, diff, and drop. */
function initWorldsStrip(): void {
  const select = byId<HTMLSelectElement>('world-select');
  select.addEventListener('change', () => {
    switchActiveWorld(select.value);
  });

  byId('world-create').addEventListener('click', () => void openScenarioDialog());
  const scenarioBase = byId<HTMLSelectElement>('scenario-base');
  scenarioBase.addEventListener('change', () => void loadScenarioLevers(scenarioBase.value));
  // One delegated listener keeps each knob's readout current however many levers a dataset declares.
  byId('scenario-levers').addEventListener('input', (event) => {
    const knob = event.target as HTMLInputElement;
    const readout = knob.nextElementSibling;
    if (knob.type === 'range' && readout instanceof HTMLOutputElement) {
      readout.textContent = knob.value;
    }
  });
  byId('scenario-cancel').addEventListener('click', () => byId<HTMLDialogElement>('scenario-dialog').close());
  byId<HTMLFormElement>('scenario-form').addEventListener('submit', (event) => {
    event.preventDefault();
    void createScenarioFromDialog();
  });

  byId('world-diff').addEventListener('click', () => void runWorldsDiff());
  byId('world-drop').addEventListener('click', () => void dropActiveWorld());
  const base = byId<HTMLSelectElement>('diff-base-select');
  base.addEventListener('change', () => {
    diffBase = base.value;
    void runWorldsDiff();
  });
}

/** Repaints the gutter line numbers for the active editor language buffer. */
function updateGutter(): void {
  const lang = document.querySelector<HTMLElement>('.editor')?.dataset.lang ?? 'sparql';
  const code = document.querySelector<HTMLElement>(`.code-only.${lang}`);
  const lines = (code?.textContent ?? '').replace(/\n$/, '').split('\n').length;
  byId('gutter').innerHTML = Array.from({ length: Math.max(lines, 1) }, (_, i) => `<span>${i + 1}</span>`).join('');
}

/** Wires the editor language tabs: select one, switch the visible buffer, repaint the gutter. */
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

/**
 * Selects a result view tab as a click on it would, so an outcome lands on screen in the view that renders it.
 * @param tab The tab's `data-tab` value (`graph`, `table`, `raw`, `shacl`, or `diff`).
 */
function selectResultTab(tab: string): void {
  document.querySelector<HTMLElement>(`.res-tab[data-tab="${tab}"]`)?.click();
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

/** The status readout's reading for a count the active source cannot report. */
const UNREPORTED = '—';

/**
 * Animates the status readout to a run's measured counts; the easing is cosmetic, the targets are the run's
 * own numbers. A source that streams no trace reports no operation count, which reads as unreported rather
 * than as zero.
 * @param rows The run's solution count.
 * @param ms The run's measured round trip, in milliseconds.
 * @param ops The operator evaluations the run streamed, or null when the source reports none.
 * @param durationMs The animation's duration.
 */
function runStream(rows: number, ms: number, ops: number | null, durationMs: number): void {
  const studio = document.querySelector<HTMLElement>('.studio');
  studio?.setAttribute('data-streaming', 'live');
  byId('stream-label').firstChild!.textContent = 'streaming… ';
  const start = performance.now();
  cancelAnimationFrame(streamRaf);
  if (ops === null) {
    byId('ops').textContent = UNREPORTED;
  }

  const step = (now: number): void => {
    const k = Math.min(1, (now - start) / durationMs);
    const e = 1 - Math.pow(1 - k, 3);
    byId('row-count').textContent = fmt(Math.round(rows * e));
    byId('ms').textContent = fmt(Math.round(ms * e));
    if (ops !== null) {
      byId('ops').textContent = fmt(Math.round(ops * e));
    }

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

/**
 * Drops the status readout to idle: no rows, no timing, nothing claimed about a plan. Runs whenever the
 * shell changes what it is querying, so a superseded source's completion never stands as this one's.
 */
function resetStatusReadout(): void {
  cancelAnimationFrame(streamRaf);
  document.querySelector<HTMLElement>('.studio')?.setAttribute('data-streaming', 'idle');
  byId('stream-label').firstChild!.textContent = 'idle · ';
  byId('row-count').textContent = '0';
  byId('ms').textContent = '0';
  byId('ops').textContent = UNREPORTED;
  byId('res-count').textContent = `${UNREPORTED} rows`;
  byId('plan-chip').textContent = `plan: ${UNREPORTED}`;
}

/** The query generation; a live re-query renders only if it is still the latest (latest-wins / switch-to-newest). */
let queryGen = 0;

/** The last query text run, so an unchanged buffer does not re-run (distinct-until-changed). */
let lastQueryText = '';

/** The debounce handle for the live re-query. */
let liveQueryTimer = 0;

/** The note the result panels carry when the active source could not be reached at all. */
const SOURCE_UNREACHABLE_NOTE = 'The engine source did not answer — check the engine picker, then Run ▸.';

/**
 * Runs one SPARQL text against the transport and paints every result panel from that run alone: the table,
 * the raw answer document, the status readout (rows and the measured round trip, with the operation count
 * and plan drawn from the run's own trace), and the trace feed. A superseded run — a newer dataset, source,
 * or Run started meanwhile — never paints. In live mode (a debounced re-query while editing) a mid-edit
 * parse error keeps the last good results rather than flashing, and no transient status overwrites them.
 * @param query The SPARQL text to run.
 * @param live Whether this is a debounced edit-driven re-query rather than a Run or a dataset load.
 */
async function runSparqlIntoPanels(query: string, live: boolean): Promise<void> {
  const results = byId('results') as unknown as SparqlResultsElement;
  lastQueryText = query;
  const generation = ++queryGen;
  if (!live) {
    results.showMessage('Running…', 'loading');
    showRaw('Running…', 'loading');
    // A run starts a fresh trace view: subscribe on the active source (a no-op without a trace stream) and
    // reset the feed, so the panel shows this run's engine decisions as they stream in.
    ensureTraceSubscription();
    resetTraceFeed();
  }

  byId('plan-chip').textContent = 'plan: …';
  const started = performance.now();
  try {
    const outcome = await runSparqlOnActiveWorld(query);
    const elapsedMs = Math.round(performance.now() - started);
    if (generation !== queryGen) {
      return;
    }

    if (outcome.ok) {
      results.results = outcome.results;
      showRaw(rawResultsText(outcome.results), 'resulted');
      runStream(outcome.results.results?.bindings.length ?? 0, elapsedMs, tracedOperationCount(), 700);
      byId('plan-chip').textContent = `plan: ${tracedPlan()}`;
      if (!live) {
        settleTraceStatus();
      }
    } else if (live) {
      // Mid-edit the buffer may not parse; keep the last good results rather than flashing the error.
      byId('plan-chip').textContent = 'plan: editing…';
    } else {
      results.showError(outcome.error);
      showRaw(rawErrorText(outcome.error), 'error');
      runStream(0, elapsedMs, tracedOperationCount(), 300);
      byId('plan-chip').textContent = `plan: ${tracedPlan()}`;
      settleTraceStatus();
    }
  } catch {
    if (generation !== queryGen || live) {
      return;
    }

    // The source faulted below its own degrade ladder: say so, and claim nothing about the run.
    results.showMessage(SOURCE_UNREACHABLE_NOTE, 'error');
    showRaw(SOURCE_UNREACHABLE_NOTE, 'error');
    resetStatusReadout();
  }
}

/** The note the results panel carries when the active source accepts no SPARQL Update. */
const UPDATE_UNAVAILABLE_NOTE = 'This source accepts no SPARQL Update — updates commit through a first-party engine’s worlds face.';

/**
 * Commits the buffer's SPARQL Update into the active world and reflects the outcome: the acknowledgement
 * (or the engine's own failure) in the results and Raw panels, then — the world's state advanced — the
 * strip's state id and every world-scoped panel re-derive. Only the explicit Run reaches here; the
 * debounced live path never writes.
 * @param update The update text.
 */
async function runUpdateIntoPanels(update: string): Promise<void> {
  const results = byId('results') as unknown as SparqlResultsElement;
  lastQueryText = update;
  const world = worldsListing.length > 0 ? activeWorld ?? worldsListing[0].name : null;
  if (!transport.worldsAvailable || world === null) {
    results.showMessage(UPDATE_UNAVAILABLE_NOTE, 'idle');

    return;
  }

  const generation = ++queryGen;
  results.showMessage('Updating…', 'loading');
  showRaw('Updating…', 'loading');
  const outcome = await transport.updateIn(world, update);
  if (generation !== queryGen) {
    return;
  }

  if (!outcome.ok) {
    const error = { error: outcome.error, diagnostics: [] };
    results.showError(error);
    showRaw(rawErrorText(error), 'error');

    return;
  }

  const committed = `Update committed to world '${world}'.`;
  results.showMessage(committed, 'idle');
  showRaw(committed, 'resulted');
  await syncWorldsStrip();
  await rederiveWorldPanels();
}

/**
 * Runs the active editor buffer: a SPARQL query against the transport in the active world, a SPARQL
 * Update through the worlds face (explicit Run only — the live path never writes), SHACL against the
 * in-browser engine, and nothing for a buffer this shell does not execute.
 * @param options.live Whether this is a debounced edit-driven re-query rather than an explicit Run.
 */
async function runQuery(options?: { live?: boolean }): Promise<void> {
  const lang = editorLangOf();
  if (lang === 'shacl') {
    if (!options?.live) {
      await runShaclValidation();
    }

    return;
  }

  if (lang !== 'sparql') {
    if (!options?.live) {
      (byId('results') as unknown as SparqlResultsElement)
        .showMessage('Only SPARQL and SHACL run against the engine in this shell.', 'idle');
    }

    return;
  }

  const text = activeCode();
  if (sparqlOperationKind(text) === 'update') {
    if (options?.live !== true) {
      await runUpdateIntoPanels(text);
    }

    return;
  }

  await runSparqlIntoPanels(text, options?.live === true);
}

/**
 * Schedules the debounced live path once the SPARQL buffer settles: the geometry literals are re-diagnosed
 * (every scan repaints from scratch, so an edited literal's mark follows the edit) and the query re-runs,
 * the latter skipped for an unchanged buffer.
 */
function scheduleLiveQuery(): void {
  window.clearTimeout(liveQueryTimer);
  liveQueryTimer = window.setTimeout(() => {
    if (editorLangOf() !== 'sparql') {
      return;
    }

    literalDiagnostics?.rescan();
    if (activeCode() !== lastQueryText) {
      void runQuery({ live: true });
    }
  }, 250);
}

/**
 * Validates the loaded dataset against the SHACL editor's current shapes — the buffer as the user has
 * authored it, which is why Run is offered on a dataset the shell already validated against its shipped
 * shapes — and shows the report in the conformance tab, where every report this shell renders lands.
 */
async function runShaclValidation(): Promise<void> {
  selectResultTab('shacl');
  const engine = inBrowserEngineSource();
  if (engine === null) {
    shaclGen++;
    showShaclNote(SHACL_ENGINE_NOTE, 'idle');

    return;
  }

  await validateShapesIntoTab(engine, activeCode());
}

/** The active editor language (`sparql`, `shacl`, …). */
function editorLangOf(): string {
  return document.querySelector<HTMLElement>('.editor')?.dataset.lang ?? 'sparql';
}

/** Sets the colour theme and repaints the theme-dependent surfaces (graph, legend). */
function setTheme(theme: Theme): void {
  document.documentElement.setAttribute('data-theme', theme);
  graph?.setTheme(theme);
  renderLegend();
}

/** Wires the top-bar theme toggle. */
function initThemeButton(): void {
  byId('theme-btn').addEventListener('click', () => {
    const next: Theme = document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
    setTheme(next);
  });
}

/**
 * Installs the Turtle-family editor behaviour on a buffer: a gutter repaint on edit, and parser-driven
 * completion — the grammar's admissible next tokens plus the fixed vocabulary corpus (and the loaded data's
 * terms) — described by the active source, resolved per call so a source switch takes effect on the next
 * keystroke; a source that describes no context leaves the token heuristic proposing. Shared by the SHACL,
 * Turtle, OWL, and TriG tabs, which are all Turtle-family.
 * @param editor The contenteditable Turtle / SHACL / OWL / TriG buffer.
 * @param syntax The grammar flavour to parse as: `trig` for TriG (named graph blocks), otherwise Turtle.
 */
function installTurtleEditor(editor: HTMLElement, syntax: string): void {
  editor.addEventListener('input', updateGutter);
  installCompletion(
    editor,
    proposalVocabulary,
    (source, caretOffset) => transport.describeTurtleCompletion(source, caretOffset, syntax),
    turtleCompletionsFor,
    turtleParserCompletions
  );
}

/**
 * Registers the offline service worker for the static (production) deployment, after the page has loaded so it
 * never competes with the initial boot. A no-op in dev (the dev server ships no worker) and where unsupported.
 * `?sw=off` is a kill switch — it unregisters the worker and clears its caches (so a reused origin, or a build
 * shipped without a worker, can evict a stuck one). The worker is base-relative so a subpath project page resolves it.
 */
function registerServiceWorker(): void {
  if (!('serviceWorker' in navigator)) {
    return;
  }

  if (new URLSearchParams(location.search).get('sw') === 'off') {
    void navigator.serviceWorker.getRegistrations().then((registrations) => Promise.all(registrations.map((registration) => registration.unregister())));
    void caches.keys().then((names) => Promise.all(names.filter((name) => name.startsWith('veritas-studio-')).map((name) => caches.delete(name))));

    return;
  }

  if (!import.meta.env.PROD) {
    return;
  }

  const base = import.meta.env.BASE_URL;
  window.addEventListener('load', () => {
    void navigator.serviceWorker.register(`${base}service-worker.js`, { scope: base, updateViaCache: 'none' })
      .then((registration) => {
        // register() resolves even when the install (the atomic precache) fails; surface that so a missing or
        // 404-ing precache asset does not leave a silently non-functioning offline app.
        const installing = registration.installing;
        installing?.addEventListener('statechange', () => {
          if (installing.state === 'redundant') {
            console.error('Service worker install failed (precache incomplete); offline support is unavailable.');
          }
        });
      })
      .catch((error) => {
        console.error('Service worker registration failed', error);
      });
  });
}

document.addEventListener('DOMContentLoaded', () => {
  // Load the dataset manifest (fills the picker), then attach an engine — which opens the first distributed
  // dataset down the one loading path, the same path the picker takes.
  void (async () => {
    await initDatasets();
    await selectEngine();
  })();
  registerServiceWorker();
  renderLegend();
  renderGraphHud();
  renderAssistTerms();
  initEditorTabs();
  initResultTabs();
  initThemeButton();
  initEnginePicker();
  initWorldsStrip();
  updateGutter();
  (byId('results') as unknown as SparqlResultsElement).showMessage('Attaching an engine…', 'loading');

  byId('run-btn').addEventListener('click', () => void runQuery());
  // The SPARQL buffer is editable: live re-query on edit (debounced, latest-wins) + intellisense proposals.
  const sparqlEditor = document.querySelector<HTMLElement>('[data-testid="editor-sparql"]');
  if (sparqlEditor !== null) {
    sparqlEditor.addEventListener('input', () => {
      updateGutter();
      scheduleLiveQuery();
    });
    // Parser-driven proposals from the active source, resolved per keystroke (the source is picked at boot
    // and can change); the popup falls back to the token heuristic whenever the source describes no context.
    installCompletion(sparqlEditor, proposalVocabulary, describeCompletionOnTransport, completionsFor, parserCompletions);
    // Offset-precise marks under the buffer's geometry literals, painted from the active source's
    // diagnostics face on the same debounced cadence as the live re-query.
    literalDiagnostics = installLiteralDiagnostics(sparqlEditor, describeLiteralOnTransport);
  }
  // The SHACL, Turtle, OWL, and TriG buffers are editable and Turtle-family: install the same parser-driven
  // completion on each (the grammar's admissible next tokens + the fixed vocabulary corpus), falling back to
  // the token heuristic when the source describes no context. TriG parses as named-graph syntax, so its
  // statement boundary additionally offers GRAPH / '{'.
  const turtleFamilyEditors: readonly [string, string][] = [
    ['editor-shacl', 'turtle'],
    ['editor-turtle', 'turtle'],
    ['editor-owl', 'turtle'],
    ['editor-trig', 'trig']
  ];
  for (const [testid, syntax] of turtleFamilyEditors) {
    const editor = document.querySelector<HTMLElement>(`[data-testid="${testid}"]`);
    if (editor !== null) {
      installTurtleEditor(editor, syntax);
    }
  }

  // The boot transport (the native bridge, or this origin's front door until the boot-time selection lands)
  // is live and the editors are wired: run the chokepoint once so the trace panel, the conformance tab, the
  // buffer marks, and the editor corpus all reflect the source the shell starts on.
  onTransportChanged();

  // One delegated listener over the assist strip: the terms are re-rendered per dataset, the binding is not.
  document.querySelector<HTMLElement>('#facets')?.addEventListener('click', (event) => {
    const term = (event.target as HTMLElement).closest<HTMLElement>('.facet')?.dataset.term;
    if (term !== undefined) {
      insertIntoSparqlBuffer(term);
    }
  });

  // The picker opens a distributed dataset; Open file… opens an arbitrary local RDF document. Both route
  // through the same loading path as the native host's window.studio.loadDataset.
  const datasetSelect = document.querySelector<HTMLSelectElement>('[data-testid="dataset-select"]');
  datasetSelect?.addEventListener('change', () => void loadDataset(datasetSelect.value));

  const openFileButton = document.querySelector<HTMLElement>('[data-testid="open-file"]');
  const fileInput = document.querySelector<HTMLInputElement>('#file-input');
  openFileButton?.addEventListener('click', () => fileInput?.click());
  fileInput?.addEventListener('change', () => {
    const file = fileInput.files?.[0];
    if (file !== undefined) {
      void openFile(file);
    }

    // Clear so re-opening the same file fires change again.
    fileInput.value = '';
  });

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
