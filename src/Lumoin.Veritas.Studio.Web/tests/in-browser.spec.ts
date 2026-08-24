// End-to-end coverage of the in-browser Studio. Most tests cover the no-scaffolding (WASM) deployment — the
// Veritas engine running fully in the browser, booted under ?engine=wasm (which opens the startup dataset)
// and driven through window.veritasEngine / window.studio (the same surfaces the shell uses) — asserting the
// query, dataset-switch, and SHACL-validation paths. A second group covers the boot-time transport selection
// (one build, hosted anywhere): auto-booting WASM on a static host versus keeping the HTTP transport when the
// origin advertises a server-side engine via /config. Chromium by default; ALL_BROWSERS=1 runs the full matrix.
import { test, expect, type Page } from '@playwright/test';

const BELOW_THRESHOLD_QUERY = `PREFIX bat: <https://veritas.app/ns/battery#>
SELECT ?battery ?recycledCobalt WHERE {
  ?battery a bat:Battery ; bat:recycledCobalt ?recycledCobalt .
  FILTER (?recycledCobalt < 10)
}`;

const BATTERY_SHAPES = `@prefix sh: <http://www.w3.org/ns/shacl#> .
@prefix bat: <https://veritas.app/ns/battery#> .
@prefix ex: <https://veritas.app/shapes/battery#> .
ex:RecycledContentShape a sh:NodeShape ;
    sh:targetClass bat:Battery ;
    sh:property [ sh:path bat:recycledCobalt ; sh:minInclusive 10 ;
        sh:message "Recycled cobalt content must be at least 10%." ] .`;

const SOCIAL_FOF_QUERY = `PREFIX soc: <https://veritas.app/ns/social#>
PREFIX sn: <https://veritas.app/data/social/>
SELECT DISTINCT ?candidate WHERE {
  sn:p1 soc:knows ?friend . ?friend soc:knows ?candidate .
  FILTER (?candidate != sn:p1)
  FILTER NOT EXISTS { sn:p1 soc:knows ?candidate }
}`;

const SOCIAL_SHAPES = `@prefix sh: <http://www.w3.org/ns/shacl#> .
@prefix soc: <https://veritas.app/ns/social#> .
@prefix ex: <https://veritas.app/shapes/social#> .
ex:PersonProfileShape a sh:NodeShape ;
    sh:targetClass soc:Person ;
    sh:property [ sh:path soc:email ; sh:minCount 1 ;
        sh:message "Every person profile must carry an email address." ] .`;

const CAMPUS_PROF_QUERY = `PREFIX cmp: <https://veritas.app/ns/campus#>
SELECT ?name WHERE {
  ?professor a cmp:Professor ; cmp:name ?name ; cmp:worksFor ?dept .
}`;

const CAMPUS_SHAPES = `@prefix sh: <http://www.w3.org/ns/shacl#> .
@prefix cmp: <https://veritas.app/ns/campus#> .
@prefix ex: <https://veritas.app/shapes/campus#> .
ex:GraduateAdvisorShape a sh:NodeShape ;
    sh:targetClass cmp:GraduateStudent ;
    sh:property [ sh:path cmp:advisor ; sh:minCount 1 ;
        sh:message "Every graduate student must have an advisor." ] .`;

// Joins the local battery graph with a federated SERVICE to the dev stub registry (the cross-trust-boundary
// hop): each pack's cathode chemistry gets its certifier from the remote endpoint, over fetch. The SERVICE
// endpoint is the dev server's own stub middleware, so the URL derives from the page origin rather than
// naming a port.
const federatedQuery = (origin: string): string => `PREFIX bat: <https://veritas.app/ns/battery#>
PREFIX mat: <https://veritas.app/ns/materials#>
SELECT ?battery ?certifier WHERE {
  ?battery a bat:Battery ; bat:cathode ?cathode .
  SERVICE <${origin}/federated/sparql> {
    ?cathode mat:certifiedBy ?certifier .
  }
}`;

// The geometry-literal diagnostics rows drive the served tier: the shell lexes the SPARQL buffer for
// geometry-typed literals, posts each literal's unescaped value to /literal-diagnostics, and paints the
// four-state answer over the offending character. The endpoint is stubbed with a fixed answer per row, so
// the rows pin the shell's half — the scan, the escape walk from the wire's UTF-8 value offset to the
// buffer's UTF-16 source index, and the overlay.
const GEO_PREFIX = 'PREFIX geo: <http://www.opengis.net/ont/geosparql#>\n';

/** The query text up to (and including) the opening quote of the one geometry literal an escape row diagnoses. */
const ESCAPE_ROW_HEAD = `${GEO_PREFIX}SELECT * WHERE { ?s ?p "`;

/** The same head for the long-string (`"""…"""`) quote form. */
const LONG_STRING_ROW_HEAD = `${GEO_PREFIX}SELECT * WHERE { ?s ?p """`;

/** The long-string row's literal body: a real newline (no ECHAR requirement) and multi-byte characters. */
const LONG_STRING_BODY = `line1
ä 中`;

/** The literal-diagnostics document a stubbed served tier answers for one posted literal. */
type DiagnosisStub = (posted: { datatype: string; body: string }) => Record<string, unknown>;

// Fragments of the battery dataset's story. Every panel answers for the data the engine actually holds, or
// says why it cannot, so once another dataset is loaded none of these may appear — their presence is the
// signature of a panel painting a narrative of its own instead of following the dataset.
const BATTERY_CONFORMANCE_FRAGMENTS = ['Recycled cobalt content', 'bat:recycledCobalt', 'PackA_E09'];
const BATTERY_TRACE_FRAGMENTS = ['SERVICE registry.example', 'bat:cathode', 'NMC811'];

/** The minimal window surface the in-browser engine and shell expose to the page. */
interface StudioWindow {
  veritasEngine: { runSparql(query: string): Promise<string>; validateShacl(shapes: string, world: string | null): Promise<string> };
  studio: { loadDataset(id: string): void };
}

/** A W3C SPARQL results document, as the engine returns it. */
interface ResultsDocument {
  results?: { bindings: Record<string, { value: string }>[] };
  boolean?: boolean;
}

/** A SHACL report, as the interop returns it. */
interface ReportDocument {
  conforms: boolean;
  results: { focusNode: string; severity: string; constraint: string; message: string }[];
}

/**
 * Waits until the startup dataset is in the engine. The engine is installed on an empty graph and the startup
 * dataset then goes down the ordinary loading path (there is only the one), so the readout naming the loaded
 * dataset — not the engine's presence — is the signal that its Turtle is loaded and queryable.
 */
async function waitForStartupDataset(page: Page): Promise<void> {
  await expect(page.locator('[data-testid="active-dataset"]')).not.toBeEmpty({ timeout: 90_000 });
}

/**
 * Navigates to the WASM deployment and waits for the engine and its startup dataset to be ready.
 * @param page The page under test.
 * @param url The address to open; the default forces the in-browser engine and lets startup pick its dataset.
 */
async function bootEngine(page: Page, url = '/?engine=wasm'): Promise<void> {
  await page.goto(url, { waitUntil: 'load' });
  await page.waitForFunction(() => (globalThis as unknown as Partial<StudioWindow>).veritasEngine !== undefined, undefined, { timeout: 90_000 });
  await waitForStartupDataset(page);
}

/** Runs a SPARQL query on the in-browser engine and parses its results document. */
async function runSparql(page: Page, query: string): Promise<ResultsDocument> {
  const json = await page.evaluate((q) => (globalThis as unknown as StudioWindow).veritasEngine.runSparql(q), query);

  return JSON.parse(json) as ResultsDocument;
}

/** Validates the loaded dataset's primary world against shapes on the in-browser engine and parses the report. */
async function validate(page: Page, shapes: string): Promise<ReportDocument> {
  const json = await page.evaluate((s) => (globalThis as unknown as StudioWindow).veritasEngine.validateShacl(s, null), shapes);

  return JSON.parse(json) as ReportDocument;
}

/** The number of solution bindings in a results document. */
function rowCount(document: ResultsDocument): number {
  return document.results?.bindings.length ?? 0;
}

/** Sets the editable SPARQL buffer's text and fires the input event the shell's live path listens on. */
async function setSparqlBuffer(page: Page, text: string): Promise<void> {
  await page.evaluate((value) => {
    const editor = document.querySelector('[data-testid="editor-sparql"]');
    if (editor !== null) {
      editor.textContent = value;
      editor.dispatchEvent(new Event('input', { bubbles: true }));
    }
  }, text);
}

/**
 * Stubs a served-tier origin: the `/config` engine marker (so the shell keeps the HTTP transport and never
 * boots WASM), an empty SPARQL answer for the live re-query, and the literal-diagnostics route answering
 * whatever the row's stub returns for the posted literal.
 */
async function stubServedTier(page: Page, diagnose: DiagnosisStub): Promise<void> {
  await page.route((url) => url.pathname === '/config', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: '{"engine":"http"}' }));
  await page.route((url) => url.pathname === '/sparql', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/sparql-results+json',
      body: JSON.stringify({ head: { vars: [] }, results: { bindings: [] } })
    }));
  await page.route((url) => url.pathname === '/literal-diagnostics', (route) => {
    const posted = JSON.parse(route.request().postData() ?? '{}') as { datatype: string; body: string };

    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(diagnose(posted)) });
  });
}

test.describe('in-browser (WASM) engine', () => {
  test('boots and loads the default battery dataset', async ({ page }) => {
    await bootEngine(page);
    const packs = await runSparql(page, 'PREFIX bat: <https://veritas.app/ns/battery#> SELECT ?b WHERE { ?b a bat:Battery }');
    expect(rowCount(packs)).toBe(5);
  });

  test('finds packs below the recycled-cobalt threshold (FILTER)', async ({ page }) => {
    await bootEngine(page);
    const result = await runSparql(page, BELOW_THRESHOLD_QUERY);
    expect(rowCount(result)).toBe(3);
  });

  test('flags battery packs below the recycled-cobalt threshold (SHACL)', async ({ page }) => {
    await bootEngine(page);
    const report = await validate(page, BATTERY_SHAPES);
    expect(report.conforms).toBe(false);
    expect(report.results.length).toBe(3);
  });

  test('switches to the social network and recommends friends-of-friends', async ({ page }) => {
    await bootEngine(page);
    await page.evaluate(() => (globalThis as unknown as StudioWindow).studio.loadDataset('social'));
    await expect
      .poll(async () => rowCount(await runSparql(page, SOCIAL_FOF_QUERY)))
      .toBe(3);

    const report = await validate(page, SOCIAL_SHAPES);
    expect(report.conforms).toBe(false);
    expect(report.results.length).toBe(1);
  });

  test('switches to the campus dataset and finds professors via the class hierarchy', async ({ page }) => {
    await bootEngine(page);
    await page.evaluate(() => (globalThis as unknown as StudioWindow).studio.loadDataset('campus'));
    await expect
      .poll(async () => rowCount(await runSparql(page, CAMPUS_PROF_QUERY)))
      .toBe(3);

    const report = await validate(page, CAMPUS_SHAPES);
    expect(report.conforms).toBe(false);
    expect(report.results.length).toBe(1);
  });

  test('opens a local RDF file via the file picker and queries it', async ({ page }) => {
    await bootEngine(page);
    const turtle = [
      '@prefix ex: <https://example.org/> .',
      'ex:alice a ex:Person ; ex:knows ex:bob .',
      'ex:bob a ex:Person .'
    ].join('\n');
    // The picker's hidden input is the same one Open file… clicks; setting files drives openFile().
    await page.setInputFiles('#file-input', { name: 'people.ttl', mimeType: 'text/turtle', buffer: Buffer.from(turtle) });
    await expect
      .poll(async () => rowCount(await runSparql(page, 'PREFIX ex: <https://example.org/> SELECT ?p WHERE { ?p a ex:Person }')))
      .toBe(2);
  });

  test('federates a SERVICE sub-query across a trust boundary and joins the result', async ({ page }) => {
    await bootEngine(page);
    const result = await runSparql(page, federatedQuery(new URL(page.url()).origin));
    const certifiers = (result.results?.bindings ?? []).map((row) => row.certifier?.value).filter(Boolean);
    expect(rowCount(result)).toBe(4);
    expect(certifiers.length).toBe(4);
    // Only the two chemistries the registry certifies join; the LFP pack's cathode has no remote row.
  });

  test('live re-queries the editable SPARQL buffer as it is edited (debounced, latest-wins)', async ({ page }) => {
    await bootEngine(page);
    const rows = page.locator('[data-testid="results"] tbody tr');
    const setEditor = (text: string): Promise<void> =>
      page.evaluate((value) => {
        const editor = document.querySelector('[data-testid="editor-sparql"]');
        if (editor !== null) {
          editor.textContent = value;
          editor.dispatchEvent(new Event('input', { bubbles: true }));
        }
      }, text);

    await setEditor('PREFIX bat: <https://veritas.app/ns/battery#> SELECT ?p WHERE { ?p a bat:Battery }');
    await expect.poll(() => rows.count()).toBe(5);

    await setEditor('PREFIX bat: <https://veritas.app/ns/battery#> SELECT ?p WHERE { ?p a bat:Battery ; bat:recycledCobalt 11 }');
    await expect.poll(() => rows.count()).toBe(1);
  });

  test('surfaces the CONSTRUCT refusal as a rendered query error (in-browser engine)', async ({ page }) => {
    await bootEngine(page);
    await page.evaluate((value) => {
      const editor = document.querySelector('[data-testid="editor-sparql"]');
      if (editor !== null) {
        editor.textContent = value;
        editor.dispatchEvent(new Event('input', { bubbles: true }));
      }
    }, 'PREFIX bat: <https://veritas.app/ns/battery#> CONSTRUCT { ?p a bat:Battery } WHERE { ?p a bat:Battery }');

    // The in-browser results wire carries SELECT/ASK documents only; a CONSTRUCT answers the engine's
    // value-based error document, rendered like any query error — never a rejected promise. The debounced
    // live path deliberately keeps the last good results, so the error renders on an explicit Run.
    await page.locator('[data-testid="run"]').click();
    await page.locator('.res-tab[data-tab="table"]').click();
    const error = page.locator('[data-testid="results"] .result-error');
    await expect(error).toBeVisible();
    await expect(error).toContainText('CONSTRUCT and DESCRIBE results are not rendered in the Studio yet');
  });

  test('proposes completions as the SPARQL buffer is typed and inserts the chosen one (intellisense)', async ({ page }) => {
    await bootEngine(page);
    const editor = page.locator('[data-testid="editor-sparql"]');
    await editor.click();
    await page.keyboard.press('Control+A');
    await page.keyboard.type('SEL');

    const popup = page.locator('.completion-popup:visible');
    await expect(popup).toBeVisible();
    await expect(popup).toContainText('SELECT');

    await page.keyboard.press('Enter');
    await expect.poll(async () => (await editor.textContent())?.trim()).toBe('SELECT');
  });

  test('proposes in-scope variables as the SPARQL buffer is typed (intellisense)', async ({ page }) => {
    await bootEngine(page);
    const editor = page.locator('[data-testid="editor-sparql"]');
    const popup = page.locator('.completion-popup:visible');
    // A fresh triple subject admits a variable; the variables bound earlier in the WHERE are offered, filtered
    // by the partial `?g` — proving variable completion surfaces (it describes the position before the token).
    await expect
      .poll(async () => {
        await editor.click();
        await page.keyboard.press('Control+A');
        await page.keyboard.type('SELECT * WHERE { ?battery a ?type . ?b');

        return (await popup.allTextContents()).join(' ');
      })
      .toContain('?battery');
  });

  test('proposes WHERE where the loaded query had it, and accepting restores it (intellisense)', async ({ page }) => {
    // The writer deletes WHERE from the loaded query and types it again, the rest of the query still standing
    // after the caret. The described context at that caret carries no proposal — it names the continuations of
    // the select-variable list and not the tokens that close it — so the popup must fall through to the token
    // heuristic rather than treat "nothing described" as "nothing admissible", and offer the keyword the
    // writer is plainly typing. Accepting it puts the query back exactly as it was.
    await bootEngine(page);
    const editor = page.locator('[data-testid="editor-sparql"]');
    const loaded = (await editor.textContent()) ?? '';
    const keywordAt = loaded.indexOf('WHERE');
    expect(keywordAt).toBeGreaterThan(0);

    // Remove the keyword and put the caret exactly where it stood, as deleting it by hand leaves things.
    await editor.click();
    await page.evaluate((cut) => {
      const buffer = document.querySelector('[data-testid="editor-sparql"]');
      if (buffer === null || buffer.firstChild === null) {
        return;
      }

      buffer.textContent = `${cut.text.slice(0, cut.at)}${cut.text.slice(cut.at + 'WHERE'.length)}`;
      const range = document.createRange();
      range.setStart(buffer.firstChild, cut.at);
      range.collapse(true);
      const selection = window.getSelection();
      selection?.removeAllRanges();
      selection?.addRange(range);
    }, { text: loaded, at: keywordAt });

    await page.keyboard.type('WH');

    const popup = page.locator('.completion-popup:visible');
    await expect(popup).toContainText('WHERE');

    await page.keyboard.press('Enter');
    await expect.poll(async () => (await editor.textContent()) ?? '').toBe(loaded);
  });

  test('proposes the OWL vocabulary in the Turtle-authored SHACL editor (intellisense)', async ({ page }) => {
    await bootEngine(page);
    // The SHACL tab shares the parser-driven Turtle completion; switch to it so its buffer is visible.
    await page.locator('.lang-tab[data-lang="shacl"]').click();
    const editor = page.locator('[data-testid="editor-shacl"]');
    await expect(editor).toBeVisible();

    const popup = page.locator('.completion-popup:visible');
    // Retype each poll: the fixed-vocabulary corpus loads asynchronously after the engine boots, and the
    // popup only re-evaluates on input — so re-fire until the owl: term appears (or the timeout trips). An
    // object position (after `a`) admits a prefixed name, so the corpus is proposed there.
    await expect
      .poll(async () => {
        await editor.click();
        await page.keyboard.press('Control+A');
        await page.keyboard.type('ex:Widget a owl:Cl');

        return (await popup.allTextContents()).join(' ');
      })
      .toContain('owl:Class');
  });

  test('proposes the GRAPH keyword at a TriG statement boundary (intellisense)', async ({ page }) => {
    await bootEngine(page);
    await page.locator('.lang-tab[data-lang="trig"]').click();
    const editor = page.locator('[data-testid="editor-trig"]');
    await expect(editor).toBeVisible();

    const popup = page.locator('.completion-popup:visible');
    // Typing a partial `GR` at a boundary: the popup describes the position before the token, where TriG
    // admits a graph block, so GRAPH is proposed (a plain Turtle tab would not offer it).
    await expect
      .poll(async () => {
        await editor.click();
        await page.keyboard.press('Control+A');
        await page.keyboard.type('GR');

        return (await popup.allTextContents()).join(' ');
      })
      .toContain('GRAPH');
  });

  test('proposes vocabulary inside a TriG graph block (intellisense)', async ({ page }) => {
    await bootEngine(page);
    await page.locator('.lang-tab[data-lang="trig"]').click();
    const editor = page.locator('[data-testid="editor-trig"]');
    await expect(editor).toBeVisible();

    const popup = page.locator('.completion-popup:visible');
    // An object position inside a graph block admits a prefixed name, so the fixed vocabulary is proposed —
    // proving the `{ … }` block parses as TriG and the corpus flows through.
    await expect
      .poll(async () => {
        await editor.click();
        await page.keyboard.press('Control+A');
        await page.keyboard.type('{ ex:s ex:p sh');

        return (await popup.allTextContents()).join(' ');
      })
      .toContain('sh:NodeShape');
  });

  test('auto-boots the in-browser engine on a static host (no ?engine=wasm, no /config)', async ({ page }) => {
    // A static host (GitHub Pages / offline) has no /config endpoint. Stub the 404 so the test is
    // deterministic regardless of whether a CLI happens to be listening locally; the page must then boot
    // the in-browser WASM engine on its own — the headline "host anywhere" (model-A) behaviour.
    await page.route((url) => url.pathname === '/config', (route) => route.fulfill({ status: 404 }));
    await page.goto('/', { waitUntil: 'load' });
    await page.waitForFunction(() => (globalThis as unknown as Partial<StudioWindow>).veritasEngine !== undefined, undefined, { timeout: 90_000 });
    await waitForStartupDataset(page);

    const packs = await runSparql(page, 'PREFIX bat: <https://veritas.app/ns/battery#> SELECT ?p WHERE { ?p a bat:Battery }');
    expect(rowCount(packs)).toBeGreaterThan(0);
  });

  test('selects the HTTP transport when the origin advertises a server engine (no WASM boot)', async ({ page }) => {
    // A CLI-served origin (model B) answers /config with the {"engine":"http"} marker. The page must keep
    // the HTTP transport — so Run fires a POST /sparql over HTTP — and must never boot the in-browser WASM
    // engine. The assertion is the request firing (proving the HTTP transport was selected), not its result,
    // so it needs no live CLI; /sparql is stubbed only so the page does not error on the dead dev proxy.
    await page.route((url) => url.pathname === '/config', (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: '{"engine":"http"}' }));
    await page.route((url) => url.pathname === '/sparql', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/sparql-results+json',
        body: JSON.stringify({ head: { vars: ['s'] }, results: { bindings: [{ s: { type: 'uri', value: 'https://veritas.app/data/x' } }] } })
      }));
    // The WASM runtime is fetched only when the engine boots. Tracking it makes "no WASM boot" a proven
    // negative: HTTP is the boot-default transport, so an instant veritasEngine===undefined alone could pass
    // even if the probe were ignored and WASM booted a few seconds later — but that boot would fetch _framework.
    let frameworkRequested = false;
    page.on('request', (request) => {
      if (request.url().includes('/_framework/')) {
        frameworkRequested = true;
      }
    });
    await page.goto('/', { waitUntil: 'load' });

    // Edit the SPARQL buffer: the shell live-re-queries it over the selected transport (debounced). With
    // /config advertising a server engine, that transport is HTTP, so a POST /sparql fires over the wire.
    const sparqlRequest = page.waitForRequest((request) => request.method() === 'POST' && request.url().endsWith('/sparql'), { timeout: 30_000 });
    await page.evaluate(() => {
      const editor = document.querySelector('[data-testid="editor-sparql"]');
      if (editor !== null) {
        editor.textContent = 'SELECT * WHERE { ?s ?p ?o } LIMIT 1';
        editor.dispatchEvent(new Event('input', { bubbles: true }));
      }
    });
    await sparqlRequest;
    await page.waitForTimeout(1_000);
    expect(frameworkRequested).toBe(false);
    expect(await page.evaluate(() => (globalThis as unknown as Partial<StudioWindow>).veritasEngine === undefined)).toBe(true);
  });

  test('boots the in-browser engine when /config is an HTML SPA fallback (GitHub Pages)', async ({ page }) => {
    // A subpath static host (e.g. GitHub Pages) may answer an unknown /config with its index.html (200) via
    // SPA fallback. The probe must read that non-JSON 200 as "no server engine" and boot WASM, rather than
    // mistake the 200 for a CLI-served engine and wrongly stay on a (dead) HTTP transport.
    await page.route((url) => url.pathname === '/config', (route) =>
      route.fulfill({ status: 200, contentType: 'text/html', body: '<!doctype html><title>app</title>' }));
    await page.goto('/', { waitUntil: 'load' });
    await page.waitForFunction(() => (globalThis as unknown as Partial<StudioWindow>).veritasEngine !== undefined, undefined, { timeout: 90_000 });
    await waitForStartupDataset(page);

    const packs = await runSparql(page, 'PREFIX bat: <https://veritas.app/ns/battery#> SELECT ?p WHERE { ?p a bat:Battery }');
    expect(rowCount(packs)).toBeGreaterThan(0);
  });

  test('?engine=wasm forces the in-browser engine even when the origin advertises a server engine', async ({ page }) => {
    // The explicit override beats the /config marker: a developer can force the in-browser engine on a
    // CLI-served page. So WASM boots despite /config saying engine:http (selectEngine short-circuits the probe).
    await page.route((url) => url.pathname === '/config', (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: '{"engine":"http"}' }));
    await page.goto('/?engine=wasm', { waitUntil: 'load' });
    await page.waitForFunction(() => (globalThis as unknown as Partial<StudioWindow>).veritasEngine !== undefined, undefined, { timeout: 90_000 });
    await waitForStartupDataset(page);

    const packs = await runSparql(page, 'PREFIX bat: <https://veritas.app/ns/battery#> SELECT ?p WHERE { ?p a bat:Battery }');
    expect(rowCount(packs)).toBeGreaterThan(0);
  });

  test('the desktop-shell bridge wins outright — no /config probe, no WASM boot', async ({ page }) => {
    // The native bridge (window.veritas) is the top precedence rung: resolveTransport picks it and selectEngine
    // returns before probing /config or booting WASM. Inject a minimal bridge before any page script runs, then
    // assert /config is never fetched and the in-browser engine never boots.
    let configProbed = false;
    await page.route((url) => url.pathname === '/config', (route) => {
      configProbed = true;

      return route.fulfill({ status: 200, contentType: 'application/json', body: '{"engine":"http"}' });
    });
    await page.addInitScript(() => {
      (globalThis as { veritas?: unknown }).veritas = {
        runSparql: () => Promise.resolve({ ok: true, results: { head: { vars: [] }, results: { bindings: [] } } }),
        onTrace: () => () => undefined
      };
    });
    await page.goto('/', { waitUntil: 'load' });
    await page.waitForTimeout(3_000);

    expect(configProbed).toBe(false);
    expect(await page.evaluate(() => (globalThis as unknown as Partial<StudioWindow>).veritasEngine === undefined)).toBe(true);
  });

  test('routes queries to a user-entered custom SPARQL endpoint (engine-source picker)', async ({ page }) => {
    // A generic conformant endpoint, stubbed at an absolute cross-origin URL: entering it in the picker must
    // route Run's POST there over the SPARQL Protocol (application/sparql-query body, SPARQL-results-JSON
    // accept) and render the endpoint's bindings — no /config marker, no in-browser boot needed first.
    await page.route((url) => url.pathname === '/config', (route) => route.fulfill({ status: 404 }));
    await page.route('https://endpoint.example/sparql', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/sparql-results+json',
        body: JSON.stringify({ head: { vars: ['who'] }, results: { bindings: [{ who: { type: 'uri', value: 'https://veritas.app/data/remote' } }] } })
      }));
    await page.goto('/', { waitUntil: 'load' });

    await page.selectOption('[data-testid="engine-select"]', 'custom');
    const input = page.locator('[data-testid="endpoint-input"]');
    await expect(input).toBeVisible();

    const posted = page.waitForRequest((request) => request.method() === 'POST' && request.url() === 'https://endpoint.example/sparql', { timeout: 30_000 });
    await input.fill('https://endpoint.example/sparql');
    await input.press('Enter');
    await page.locator('[data-testid="run"]').click();
    const request = await posted;
    expect(request.headers()['accept']).toBe('application/sparql-results+json');

    const rows = page.locator('[data-testid="results"] tbody tr');
    await expect.poll(() => rows.count()).toBe(1);
  });

  test('persists the custom-endpoint choice across reload — reattached with no probe and no WASM boot', async ({ page }) => {
    // The picker's choice survives the reload: the persisted custom endpoint reattaches on its own, without
    // re-probing /config and without booting the in-browser engine (the _framework fetch is the proven
    // negative, as in the server-marker row) — even though this origin advertises a server engine.
    let configProbes = 0;
    await page.route((url) => url.pathname === '/config', (route) => {
      configProbes += 1;

      return route.fulfill({ status: 200, contentType: 'application/json', body: '{"engine":"http"}' });
    });
    await page.route('https://endpoint.example/sparql', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/sparql-results+json',
        body: JSON.stringify({ head: { vars: ['who'] }, results: { bindings: [{ who: { type: 'uri', value: 'https://veritas.app/data/remote' } }] } })
      }));
    let frameworkRequested = false;
    page.on('request', (request) => {
      if (request.url().includes('/_framework/')) {
        frameworkRequested = true;
      }
    });

    await page.goto('/', { waitUntil: 'load' });
    await page.selectOption('[data-testid="engine-select"]', 'custom');
    const input = page.locator('[data-testid="endpoint-input"]');
    const attached = page.waitForRequest((request) => request.method() === 'POST' && request.url() === 'https://endpoint.example/sparql', { timeout: 30_000 });
    await input.fill('https://endpoint.example/sparql');
    await input.press('Enter');
    // The vocabulary refresh fires the first POST at the endpoint, proving the choice attached (and persisted).
    await attached;

    const probesBeforeReload = configProbes;
    await page.reload({ waitUntil: 'load' });

    await expect(page.locator('[data-testid="engine-select"]')).toHaveValue('custom');
    await expect(input).toHaveValue('https://endpoint.example/sparql');

    const posted = page.waitForRequest((request) => request.method() === 'POST' && request.url() === 'https://endpoint.example/sparql', { timeout: 30_000 });
    await page.locator('[data-testid="run"]').click();
    await posted;
    await page.waitForTimeout(1_000);
    expect(configProbes).toBe(probesBeforeReload);
    expect(frameworkRequested).toBe(false);
    expect(await page.evaluate(() => (globalThis as unknown as Partial<StudioWindow>).veritasEngine === undefined)).toBe(true);
  });

  test('degrades an arbitrary endpoint error body to a rendered query error, never a fabricated success', async ({ page }) => {
    // A generic endpoint fails with an HTML error page, not the engine's {"error":…} document. The shell
    // must render a query error carrying the HTTP status (the transport's degrade ladder) rather than
    // presenting any content as if the query had succeeded.
    await page.route((url) => url.pathname === '/config', (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: '{"engine":"http"}' }));
    await page.route('https://endpoint.example/sparql', (route) =>
      route.fulfill({ status: 500, contentType: 'text/html', body: '<!doctype html><title>error</title><h1>Internal error</h1>' }));
    await page.goto('/', { waitUntil: 'load' });

    await page.selectOption('[data-testid="engine-select"]', 'custom');
    const input = page.locator('[data-testid="endpoint-input"]');
    await input.fill('https://endpoint.example/sparql');
    await input.press('Enter');
    await page.locator('[data-testid="run"]').click();

    // The error renders in the results element, which lives in the Table view — activate it to see it.
    await page.locator('.res-tab[data-tab="table"]').click();
    const error = page.locator('[data-testid="results"] .result-error');
    await expect(error).toBeVisible();
    await expect(error).toContainText('HTTP 500');
  });

  test('renders an endpoint error document whose diagnostics field is not an array, never a fabricated success', async ({ page }) => {
    // An arbitrary endpoint may answer the engine's {"error":…} shape but put any JSON value in the
    // diagnostics field. The transport must normalize a non-array to no diagnostics, so the renderer's
    // array mapping never throws — a throw there would land the run on the unreachable-source note instead
    // of the endpoint's own diagnosis.
    await page.route((url) => url.pathname === '/config', (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: '{"engine":"http"}' }));
    await page.route('https://endpoint.example/sparql', (route) =>
      route.fulfill({ status: 400, contentType: 'application/json', body: '{"error":"Malformed query near token 7","diagnostics":"see the server log"}' }));
    await page.goto('/', { waitUntil: 'load' });

    await page.selectOption('[data-testid="engine-select"]', 'custom');
    const input = page.locator('[data-testid="endpoint-input"]');
    await input.fill('https://endpoint.example/sparql');
    await input.press('Enter');
    await page.locator('[data-testid="run"]').click();

    await page.locator('.res-tab[data-tab="table"]').click();
    const error = page.locator('[data-testid="results"] .result-error');
    await expect(error).toBeVisible();
    await expect(error).toContainText('Malformed query near token 7');
  });

  test('surfaces a boot-time in-browser engine failure and reverts the picker (static host)', async ({ page }) => {
    // A static host with no /config boots the in-browser engine by default. When that boot fails (the
    // runtime unreachable), the picker must not keep claiming the in-browser engine — it reverts to the
    // source actually attached and the results panel says the boot failed, instead of failing silently.
    await page.route((url) => url.pathname === '/config', (route) => route.fulfill({ status: 404 }));
    await page.route((url) => url.pathname.includes('/_framework/'), (route) => route.abort());
    await page.goto('/', { waitUntil: 'load' });

    await page.locator('.res-tab[data-tab="table"]').click();
    const message = page.locator('[data-testid="results"]');
    await expect(message).toContainText('The in-browser engine failed to boot', { timeout: 90_000 });
    await expect(page.locator('[data-testid="engine-select"]')).toHaveValue('server');
    expect(await page.evaluate(() => (globalThis as unknown as Partial<StudioWindow>).veritasEngine === undefined)).toBe(true);
  });

  test('streams the live execution trace into the panel on Run (in-browser engine)', async ({ page }) => {
    // The in-browser engine is "the server" of its tier: Run subscribes the shell to the engine's per-event
    // trace bridge and the panel renders the run's decisions live — operator evaluations with their strategy
    // and row shape. The settled status counts the run's events.
    await bootEngine(page);
    await page.locator('[data-testid="run"]').click();

    await expect(page.locator('[data-testid="trace-status"]')).toContainText('events', { timeout: 30_000 });
    const rows = page.locator('[data-testid="trace"] .tr');
    await expect.poll(() => rows.count()).toBeGreaterThan(0);
    await expect(page.locator('[data-testid="trace"] .tr .why').first()).toContainText(/(columnar|row|streaming):/);
  });

  test('disables the trace panel on a generic custom endpoint — trace is a first-party capability', async ({ page }) => {
    // A user-entered conformant endpoint answers queries but offers no trace stream (the SPARQL Protocol has
    // none). The panel must disable — status text, no listening dot, no error — and Run must leave it disabled.
    await page.route((url) => url.pathname === '/config', (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: '{"engine":"http"}' }));
    await page.route('https://endpoint.example/sparql', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/sparql-results+json',
        body: JSON.stringify({ head: { vars: ['who'] }, results: { bindings: [] } })
      }));
    await page.goto('/', { waitUntil: 'load' });

    await page.selectOption('[data-testid="engine-select"]', 'custom');
    const input = page.locator('[data-testid="endpoint-input"]');
    await input.fill('https://endpoint.example/sparql');
    await input.press('Enter');

    await expect(page.locator('[data-testid="trace-status"]')).toHaveText('not available for this source');
    await expect(page.locator('.panel.trace')).toHaveAttribute('data-trace', 'off');

    await page.locator('[data-testid="run"]').click();
    await expect(page.locator('[data-testid="trace-status"]')).toHaveText('not available for this source');
    // The disabled placeholder survives the Run — the feed-clearing path is capability-guarded.
    await expect(page.locator('[data-testid="trace"]')).toContainText('offers no trace stream');
  });

  test('renders server-tier trace frames delivered over Server-Sent Events', async ({ page }) => {
    // The server tier's trace rides GET /trace as SSE. Stub the stream with two frames (the EventSource
    // dispatches them before the closed connection retries), run a query, and the panel renders them with
    // the kind-mapped marks: a rewrite that applied reads as chosen, an operator evaluation as a note.
    await page.route((url) => url.pathname === '/config', (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: '{"engine":"http"}' }));
    await page.route((url) => url.pathname === '/sparql', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/sparql-results+json',
        body: JSON.stringify({ head: { vars: ['s'] }, results: { bindings: [] } })
      }));
    const frames = [
      'event: trace',
      'data: {"correlationId":"11112222-3333-4444-5555-666677778888","sequence":1,"kind":"operator","term":"Bgp","detail":"columnar: 4 rows"}',
      '',
      'event: trace',
      'data: {"correlationId":"11112222-3333-4444-5555-666677778888","sequence":2,"kind":"rewrite-applied","term":"JoinReorder","detail":"pass 0 at Join"}',
      '',
      ''
    ].join('\n');
    await page.route((url) => url.pathname === '/trace', (route) =>
      route.fulfill({ status: 200, contentType: 'text/event-stream', body: frames }));
    await page.goto('/', { waitUntil: 'load' });

    await page.locator('[data-testid="run"]').click();

    const rows = page.locator('[data-testid="trace"] .tr');
    await expect.poll(() => rows.count(), { timeout: 30_000 }).toBeGreaterThanOrEqual(2);
    await expect(page.locator('[data-testid="trace"] .tr.note .term').first()).toHaveText('Bgp');
    await expect(page.locator('[data-testid="trace"] .tr.chose .term').first()).toHaveText('JoinReorder');
    await expect(page.locator('[data-testid="trace"] .tr.note .why').first()).toContainText('columnar: 4 rows');
  });

  test('squiggles a broken geometry literal at the offending byte and clears the mark when it is fixed', async ({ page }) => {
    // The served tier refuses the unclosed WKT body at byte 6 (the `1`); the shell must paint an invalid
    // mark carrying the refusal kind and offset over exactly that character, and repaint from scratch on
    // the next debounced scan — so correcting the literal (the tier then answers valid) leaves no mark.
    await stubServedTier(page, (posted) => posted.body.includes(')')
      ? { status: 'valid', datatype: posted.datatype }
      : { status: 'invalid', kind: 'MalformedDocument', byteOffset: 6, datatype: posted.datatype });
    await page.goto('/', { waitUntil: 'load' });

    await setSparqlBuffer(page, `${ESCAPE_ROW_HEAD}POINT(1"^^geo:wktLiteral }`);
    const invalid = page.locator('[data-testid="literal-diagnostics"] [data-status="invalid"]');
    await expect.poll(() => invalid.count()).toBe(1);
    await expect(invalid.first()).toHaveAttribute('data-kind', 'MalformedDocument');
    await expect(invalid.first()).toHaveAttribute('data-offset', '6');
    await expect(invalid.first()).toHaveAttribute('title', 'MalformedDocument at byte 6');
    await expect(invalid.first()).toHaveAttribute('data-source-index', String(ESCAPE_ROW_HEAD.length + 6));

    await setSparqlBuffer(page, `${ESCAPE_ROW_HEAD}POINT(1 2)"^^geo:wktLiteral }`);
    await expect.poll(() => page.locator('[data-testid="literal-diagnostics"] span').count()).toBe(0);
  });

  test('marks a literal its datatype tolerates but the engine cannot evaluate as a warning', async ({ page }) => {
    // The four states are structural: a body the validator accepts yet the codec reader refuses answers
    // warning, and the overlay must render it as its own kind of mark — never as datatype invalidity.
    await stubServedTier(page, (posted) => posted.body.startsWith('LINESTRING')
      ? { status: 'warning', kind: 'StructuralViolation', byteOffset: 0, datatype: posted.datatype }
      : { status: 'valid', datatype: posted.datatype });
    await page.goto('/', { waitUntil: 'load' });

    await setSparqlBuffer(page, `${GEO_PREFIX}SELECT * WHERE { ?s ?p "LINESTRING(1 2)"^^geo:wktLiteral }`);
    const warning = page.locator('[data-testid="literal-diagnostics"] [data-status="warning"]');
    await expect.poll(() => warning.count()).toBe(1);
    await expect(warning.first()).toHaveAttribute('data-kind', 'StructuralViolation');
    await expect(warning.first()).toHaveAttribute('title', 'StructuralViolation at byte 0');
    await expect(page.locator('[data-testid="literal-diagnostics"] [data-status="invalid"]')).toHaveCount(0);
  });

  test('places the mark past an ECHAR escape, the byte offset being into the unescaped value', async ({ page }) => {
    // The tier diagnoses the VALUE `<gml:Point srsName="bad"/>`, whose byte 20 is the `b` of `bad`. The
    // buffer spells the quote before it as the two-unit escape \", so the mark lands at source index 21.
    await stubServedTier(page, (posted) => ({ status: 'invalid', kind: 'MalformedDocument', byteOffset: 20, datatype: posted.datatype }));
    await page.goto('/', { waitUntil: 'load' });

    await setSparqlBuffer(page, `${ESCAPE_ROW_HEAD}<gml:Point srsName=\\"bad\\"/>"^^geo:gmlLiteral }`);
    const invalid = page.locator('[data-testid="literal-diagnostics"] [data-status="invalid"]');
    await expect.poll(() => invalid.count()).toBe(1);
    await expect(invalid.first()).toHaveAttribute('data-source-index', String(ESCAPE_ROW_HEAD.length + 21));
  });

  test('places the mark past UCHAR escapes, including an astral code point', async ({ page }) => {
    // The value is `ä😀Z`: two bytes, then four (the astral code point the buffer spells as \U0001F600),
    // so byte 6 is the `Z`. In the source that sits past 6 + 10 units of escape, at source index 16.
    await stubServedTier(page, (posted) => ({ status: 'invalid', kind: 'MalformedDocument', byteOffset: 6, datatype: posted.datatype }));
    await page.goto('/', { waitUntil: 'load' });

    await setSparqlBuffer(page, `${ESCAPE_ROW_HEAD}\\u00E4\\U0001F600Z"^^geo:wktLiteral }`);
    const invalid = page.locator('[data-testid="literal-diagnostics"] [data-status="invalid"]');
    await expect.poll(() => invalid.count()).toBe(1);
    await expect(invalid.first()).toHaveAttribute('data-source-index', String(ESCAPE_ROW_HEAD.length + 16));
  });

  test('places the mark inside a long-string literal, whose newlines and multi-byte characters are plain', async ({ page }) => {
    // The long form takes real newlines and needs no escapes: byte 9 of `line1\nä 中` is the `中` (the `ä`
    // spends two bytes), which sits at source index 8 — the walk counts bytes, the buffer counts units.
    await stubServedTier(page, (posted) => ({ status: 'invalid', kind: 'MalformedDocument', byteOffset: 9, datatype: posted.datatype }));
    await page.goto('/', { waitUntil: 'load' });

    await setSparqlBuffer(page, `${LONG_STRING_ROW_HEAD}${LONG_STRING_BODY}"""^^geo:wktLiteral }`);
    const invalid = page.locator('[data-testid="literal-diagnostics"] [data-status="invalid"]');
    await expect.poll(() => invalid.count()).toBe(1);
    await expect(invalid.first()).toHaveAttribute('data-source-index', String(LONG_STRING_ROW_HEAD.length + 8));
  });

  test('proposes the geof: function vocabulary in the SPARQL buffer (intellisense)', async ({ page }) => {
    await bootEngine(page);
    const editor = page.locator('[data-testid="editor-sparql"]');
    const popup = page.locator('.completion-popup:visible');
    // Retype each poll: the fixed-vocabulary corpus loads asynchronously after the engine boots, and the
    // popup only re-evaluates on input. A FILTER's parentheses admit an expression primary — an IRI or
    // prefixed name among them — so the geo rosters are proposed there, filtered by the partial token.
    await expect
      .poll(async () => {
        await editor.click();
        await page.keyboard.press('Control+A');
        await page.keyboard.type('SELECT * WHERE { ?s ?p ?o . FILTER(geof:dist');

        return (await popup.allTextContents()).join(' ');
      })
      .toContain('geof:distance');
  });

  test('proposes a registered datatype with no prefix pairing as a bracketed full IRI (intellisense)', async ({ page }) => {
    await bootEngine(page);
    await page.locator('.lang-tab[data-lang="turtle"]').click();
    const editor = page.locator('[data-testid="editor-turtle"]');
    await expect(editor).toBeVisible();

    const popup = page.locator('.completion-popup:visible');
    // The a5 grid datatype is house-namespaced and pairs with no prefix, so it rides the corpus as a
    // bracketed full IRI. Typing an unclosed IRIREF makes the whole `<https://lu` run one token, which is
    // what the bracketed candidate prefix-matches against. Retype each poll: the corpus loads asynchronously
    // after the engine boots, and the popup only re-evaluates on input.
    await expect
      .poll(async () => {
        await editor.click();
        await page.keyboard.press('Control+A');
        await page.keyboard.type('ex:s ex:p "x"^^<https://lu');

        return (await popup.allTextContents()).join(' ');
      })
      .toContain('<https://lumoin.com/veritas/dggs/a5Literal>');

    await page.keyboard.press('Enter');
    // Accepting replaces the whole unclosed run, so the buffer holds ONE bracketed IRI — not the typed
    // prefix followed by a second copy of it.
    await expect
      .poll(async () => ((await editor.textContent()) ?? '').split('<https://lumoin.com/veritas/dggs/a5Literal>').length - 1)
      .toBe(1);
    await expect
      .poll(async () => ((await editor.textContent()) ?? '').trim())
      .toBe('ex:s ex:p "x"^^<https://lumoin.com/veritas/dggs/a5Literal>');
  });

  test('drives completion through the transport seam and re-binds it on a source switch', async ({ page }) => {
    // The served tier answers completion over the seam: the shell posts the buffer and the caret to the
    // completion route and maps the context to proposals — here an in-scope variable no token heuristic
    // could invent, so the proposal proves the request left the page. The popup binds to the transport per
    // call, so switching the source re-binds it: a generic endpoint carries no completion face, and the
    // proposals keep coming from the heuristic instead.
    await page.route((url) => url.pathname === '/config', (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: '{"engine":"http"}' }));
    await page.route((url) => url.pathname === '/sparql', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/sparql-results+json',
        body: JSON.stringify({ head: { vars: [] }, results: { bindings: [] } })
      }));
    await page.route((url) => url.pathname === '/completion', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          caret: 0,
          expectedTokens: ['Variable'],
          enclosingProductions: ['TriplesBlock'],
          inScopeVariables: [{ name: 'battery', datatype: null, datatypeSource: 'unresolved' }],
          variablePredicates: []
        })
      }));
    await page.route('https://endpoint.example/sparql', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/sparql-results+json',
        body: JSON.stringify({ head: { vars: [] }, results: { bindings: [] } })
      }));
    await page.goto('/', { waitUntil: 'load' });

    const editor = page.locator('[data-testid="editor-sparql"]');
    const popup = page.locator('.completion-popup:visible');
    await expect
      .poll(async () => {
        await editor.click();
        await page.keyboard.press('Control+A');
        await page.keyboard.type('SELECT * WHERE { ?b');

        return (await popup.allTextContents()).join(' ');
      })
      .toContain('?battery');

    await page.selectOption('[data-testid="engine-select"]', 'custom');
    const input = page.locator('[data-testid="endpoint-input"]');
    await input.fill('https://endpoint.example/sparql');
    await input.press('Enter');

    await expect
      .poll(async () => {
        await editor.click();
        await page.keyboard.press('Control+A');
        await page.keyboard.type('SEL');

        return (await popup.allTextContents()).join(' ');
      })
      .toContain('SELECT');
  });

  test('fills every result panel from the startup dataset’s own run, with nothing bundled', async ({ page }) => {
    // Startup is an ordinary dataset switch: the first distributed dataset goes down the one loading path and
    // its showcase query runs, so the table, the raw document, the graph HUD, the assist terms and the status
    // readout all describe THAT run — the battery dataset's five pack rows, the answer document as
    // SPARQL results JSON (never Turtle), a node count the query produced, and a completion with a measured
    // timing and a plan read off the run's own trace.
    await bootEngine(page);

    await expect(page.locator('#stream-label')).toContainText('complete', { timeout: 30_000 });
    await expect(page.locator('[data-testid="res-count"]')).toHaveText('5 rows');
    await expect(page.locator('[data-testid="results"] tbody tr')).toHaveCount(5);
    await expect(page.locator('#plan-chip')).not.toHaveText('plan: —');
    await expect(page.locator('#ops')).not.toHaveText('—');

    const raw = page.locator('[data-testid="raw-view"]');
    await expect(raw).toContainText('"head"');
    await expect(raw).toContainText('"bindings"');
    // The Raw view is the answer document; the Turtle snippet that used to sit there is not an answer.
    await expect(raw).not.toContainText('@prefix');

    const hud = page.locator('[data-testid="graph-hud"]');
    await expect(hud).toContainText(/\d+ nodes · \d+ edges/);
    await expect(hud).not.toContainText('1,284,006');

    const assist = page.locator('[data-testid="assist-terms"]');
    await expect(assist.locator('.facet').first()).toBeVisible();
    await expect(assist).not.toContainText('SERVICE');
  });

  test('opens the dataset a shared ?dataset link names, down the same loading path', async ({ page }) => {
    // The link parameter only picks the id; the load itself is the ordinary path, so the linked dataset
    // arrives complete — picker selection, readout, and every panel painted from its own run (Ada's three
    // friend-of-friend recommendations), not from the dataset startup would otherwise have opened.
    await bootEngine(page, '/?engine=wasm&dataset=social');

    await expect(page.locator('[data-testid="dataset-select"]')).toHaveValue('social');
    await expect(page.locator('[data-testid="active-dataset"]')).toHaveText('Social network');
    await expect(page.locator('[data-testid="res-count"]')).toHaveText('3 rows');
    await expect(page.locator('[data-testid="results"]')).toContainText('Frank');
    await expect(page.locator('[data-testid="raw-view"]')).toContainText('"firstName"');
    await expect(page.locator('[data-testid="results"]')).not.toContainText('NMC811');
  });

  test('falls back to the first dataset for an id no deployment carries, and the address self-corrects', async ({ page }) => {
    // A hand-typed or outlived link must not error or leave the shell empty: an id naming nothing addresses
    // nothing, startup opens what it always opens, and the address is rewritten to the dataset actually
    // loaded — so the link the reader copies next is one that works.
    await bootEngine(page, '/?engine=wasm&dataset=nonsense');

    await expect(page.locator('[data-testid="active-dataset"]')).toHaveText('EV battery passports');
    await expect(page.locator('[data-testid="res-count"]')).toHaveText('5 rows');
    await expect.poll(() => new URL(page.url()).searchParams.get('dataset')).toBe('battery');
  });

  test('shares a picker switch in the address without pushing a history entry', async ({ page }) => {
    // Copying the address at any moment shares the session, so a switch rewrites the parameter — with
    // replaceState, since a dataset switch is what the page shows rather than a place the reader navigated
    // to: Back must still leave the app, not walk back through the switches. The engine override survives.
    await bootEngine(page);
    const entriesBeforeSwitch = await page.evaluate(() => history.length);

    await page.selectOption('[data-testid="dataset-select"]', 'campus');

    await expect(page.locator('[data-testid="active-dataset"]')).toHaveText('University campus');
    await expect.poll(() => new URL(page.url()).searchParams.get('dataset')).toBe('campus');
    expect(new URL(page.url()).searchParams.get('engine')).toBe('wasm');
    expect(await page.evaluate(() => history.length)).toBe(entriesBeforeSwitch);
  });

  test('validates each loaded dataset against the shapes it ships and follows the dataset switch (conformance tab)', async ({ page }) => {
    // The conformance tab is a report about the data the engine holds: booting on the battery dataset
    // validates bat:Battery against the shipped RecycledContentShape (3 packs fall below the threshold), and
    // switching to the social network re-validates against ITS shapes. social.ttl gives every soc:Person a
    // soc:email except sn:p10 (Jonas Holm), so PersonProfileShape's sh:minCount 1 fails on exactly that one
    // node — the tab must read the person/email story and carry no trace of the battery one. The trace panel
    // drops back to its listening placeholder on the same switch: the previous dataset's decisions describe
    // nothing about this one.
    await bootEngine(page);
    await page.locator('.res-tab[data-tab="shacl"]').click();
    const conformance = page.locator('[data-testid="shacl-report"]');
    await expect(conformance).toContainText('Does not conform — 3 result(s).');
    await expect(conformance).toContainText('Recycled cobalt content');

    await page.evaluate(() => (globalThis as unknown as StudioWindow).studio.loadDataset('social'));

    await expect(conformance).toContainText('Does not conform — 1 result(s).');
    await expect(conformance).toContainText('Every person profile must carry an email address.');
    await expect(conformance).toContainText('p10 · MinCountConstraintComponent');
    await expect(conformance).toHaveAttribute('data-state', 'violations');
    for (const fragment of BATTERY_CONFORMANCE_FRAGMENTS) {
      await expect(conformance).not.toContainText(fragment);
    }

    const trace = page.locator('[data-testid="trace"]');
    for (const fragment of BATTERY_TRACE_FRAGMENTS) {
      await expect(trace).not.toContainText(fragment);
    }

    // The switch is a load, so it re-runs: the table, the raw document and the graph carry the social
    // network's friend-of-friend answer (three recommendations for Ada), not the battery dataset's.
    await expect(page.locator('[data-testid="res-count"]')).toHaveText('3 rows');
    const table = page.locator('[data-testid="results"]');
    await expect(table).toContainText('Frank');
    await expect(table).not.toContainText('NMC811');
    const raw = page.locator('[data-testid="raw-view"]');
    await expect(raw).toContainText('"firstName"');
    await expect(raw).not.toContainText('recycledCobalt');
    await expect(page.locator('[data-testid="graph-hud"]')).toContainText(/\d+ nodes/);
  });

  test('says a dataset ships no shapes rather than holding the previous verdict (conformance tab)', async ({ page }) => {
    // An arbitrary RDF file carries no shapes graph, so there is nothing to validate it against. The tab must
    // say so — an idle state naming what to do — instead of keeping the report of the dataset it replaced.
    await bootEngine(page);
    await page.locator('.res-tab[data-tab="shacl"]').click();
    const conformance = page.locator('[data-testid="shacl-report"]');
    await expect(conformance).toContainText('Does not conform');

    const turtle = ['@prefix ex: <https://example.org/> .', 'ex:alice a ex:Person ; ex:knows ex:bob .'].join('\n');
    await page.setInputFiles('#file-input', { name: 'people.ttl', mimeType: 'text/turtle', buffer: Buffer.from(turtle) });

    await expect(conformance).toContainText('ships no SHACL shapes');
    await expect(conformance).toHaveAttribute('data-state', 'idle');
    for (const fragment of BATTERY_CONFORMANCE_FRAGMENTS) {
      await expect(conformance).not.toContainText(fragment);
    }
  });

  test('drops the conformance verdict when the engine source leaves the in-browser engine', async ({ page }) => {
    // SHACL validation is the in-browser engine's capability: a generic SPARQL endpoint cannot be asked for a
    // report. Switching to one must leave the tab saying which source validates — never a verdict about data
    // the shell is no longer querying.
    await page.route('https://endpoint.example/sparql', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/sparql-results+json',
        body: JSON.stringify({ head: { vars: [] }, results: { bindings: [] } })
      }));
    await bootEngine(page);
    await page.locator('.res-tab[data-tab="shacl"]').click();
    const conformance = page.locator('[data-testid="shacl-report"]');
    await expect(conformance).toContainText('Does not conform');

    await page.selectOption('[data-testid="engine-select"]', 'custom');
    const input = page.locator('[data-testid="endpoint-input"]');
    await input.fill('https://endpoint.example/sparql');
    await input.press('Enter');

    await expect(conformance).toContainText('runs against the in-browser engine');
    await expect(conformance).toHaveAttribute('data-state', 'idle');
    await expect(conformance).not.toContainText('Recycled cobalt content');
    for (const fragment of BATTERY_CONFORMANCE_FRAGMENTS) {
      await expect(conformance).not.toContainText(fragment);
    }

    // The answer surfaces drop with it: the previous source's run described data this one does not hold.
    const raw = page.locator('[data-testid="raw-view"]');
    await expect(raw).toContainText('No run yet');
    await expect(raw).not.toContainText('"bindings"');
    await expect(page.locator('#stream-label')).toContainText('idle');
    await expect(page.locator('[data-testid="res-count"]')).toHaveText('— rows');
    await expect(page.locator('#plan-chip')).toHaveText('plan: —');
  });

  test('runs the what-if flow through the worlds strip: fork, update in the fork, diff, primary untouched, drop', async ({ page }) => {
    // The whole many-worlds story, driven through the UI: the strip forks the primary, Run commits the
    // hypothetical into the fork (never the primary — its state id is the proof), the question answers the
    // hypothetical there, the Diff view shows exactly the committed delta, and the drop returns the shell
    // to the untouched primary. The state id pill also pins zero-copy convergence: a fresh fork's id IS the
    // primary's, because identical content addresses identically.
    await bootEngine(page);
    const strip = page.locator('[data-testid="worlds-strip"]');
    await expect(strip).toBeVisible();
    const worldSelect = page.locator('[data-testid="world-select"]');
    await expect(worldSelect).toHaveValue('main');
    const statePill = page.locator('[data-testid="world-state"]');
    await expect(statePill).toHaveText(/^[0-9a-f]{16}$/);
    const mainState = (await statePill.innerText()).trim();
    await expect(page.locator('[data-testid="world-drop"]')).toBeDisabled();

    // Create the scenario world through the dialog; the battery dataset declares no levers, so the
    // dialog offers none and the scenario is a plain fork — which, holding identical content, keeps
    // the primary's state id.
    await page.locator('[data-testid="world-create"]').click();
    await expect(page.locator('#scenario-dialog')).toBeVisible();
    await expect(page.locator('[data-testid="scenario-levers"]')).toBeHidden();
    await page.locator('[data-testid="scenario-name"]').fill('whatif');
    await page.locator('[data-testid="scenario-create"]').click();
    await expect(worldSelect).toHaveValue('whatif');
    await expect(worldSelect.locator('option')).toHaveCount(2);
    await expect(statePill).toHaveText(mainState);
    await expect(page.locator('[data-testid="world-drop"]')).toBeEnabled();

    // Typing the update fires the debounced live path, which must never write: the fork's state id stands.
    await setSparqlBuffer(page, `PREFIX bat: <https://veritas.app/ns/battery#>
INSERT DATA { bat:PackX_W01 a bat:Battery ; bat:recycledCobalt 5 . }`);
    await page.waitForTimeout(600);
    await expect(statePill).toHaveText(mainState);

    // The explicit Run commits the hypothetical into the fork, and only then does its state id advance.
    await page.locator('[data-testid="run"]').click();
    await page.locator('.res-tab[data-tab="table"]').click();
    await expect(page.locator('[data-testid="results"]')).toContainText("Update committed to world 'whatif'");
    await expect(statePill).not.toHaveText(mainState);

    // The question, asked in the fork, answers the hypothetical: a fourth pack below the threshold.
    await setSparqlBuffer(page, BELOW_THRESHOLD_QUERY);
    await page.locator('[data-testid="run"]').click();
    await expect.poll(() => page.locator('[data-testid="results"] tbody tr').count()).toBe(4);

    // The diff against the primary is exactly the committed delta: one transition, two added triples.
    await page.locator('[data-testid="world-diff"]').click();
    await expect(page.locator('[data-testid="diff-summary"]')).toHaveText('1 transition · 2 triples');
    const diffTable = page.locator('[data-testid="worlds-diff"]');
    await expect(diffTable.locator('tr.diff-add')).toHaveCount(2);
    await expect(diffTable.locator('tr.diff-remove')).toHaveCount(0);
    await expect(diffTable).toContainText('PackX_W01');
    await expect(diffTable).toContainText('recycledCobalt');

    // The primary is untouched throughout: its state id never moved, and the question answers what it always did.
    await worldSelect.selectOption('main');
    await expect(statePill).toHaveText(mainState);
    await page.locator('.res-tab[data-tab="table"]').click();
    await page.locator('[data-testid="run"]').click();
    await expect.poll(() => page.locator('[data-testid="results"] tbody tr').count()).toBe(3);

    // Dropping the fork ends the what-if: the strip returns to the primary alone.
    await worldSelect.selectOption('whatif');
    await page.locator('[data-testid="world-drop"]').click();
    await expect(worldSelect).toHaveValue('main');
    await expect(worldSelect.locator('option')).toHaveCount(1);
    await expect(page.locator('[data-testid="world-drop"]')).toBeDisabled();
  });

  test('validates in the active world (conformance tab): the fork carries the hypothetical, the primary keeps its verdict', async ({ page }) => {
    // SHACL validation follows the active world: committing a below-threshold pack into a fork adds a
    // fourth violation THERE, and switching back to the primary re-validates to the shipped three — a
    // verdict about the fork must never stand as the primary's, in either direction.
    await bootEngine(page);
    await page.locator('.res-tab[data-tab="shacl"]').click();
    const conformance = page.locator('[data-testid="shacl-report"]');
    await expect(conformance).toContainText('Does not conform — 3 result(s).');

    await page.locator('[data-testid="world-create"]').click();
    await page.locator('[data-testid="scenario-name"]').fill('audit');
    await page.locator('[data-testid="scenario-create"]').click();
    await expect(page.locator('[data-testid="world-select"]')).toHaveValue('audit');

    await setSparqlBuffer(page, `PREFIX bat: <https://veritas.app/ns/battery#>
INSERT DATA { bat:PackX_W02 a bat:Battery ; bat:recycledCobalt 4 . }`);
    await page.locator('[data-testid="run"]').click();
    await page.locator('.res-tab[data-tab="shacl"]').click();
    await expect(conformance).toContainText('Does not conform — 4 result(s).');

    await page.locator('[data-testid="world-select"]').selectOption('main');
    await expect(conformance).toContainText('Does not conform — 3 result(s).');
  });

  test('creates a scenario world with the dataset’s declared levers and reads the consequence (adaptation)', async ({ page }) => {
    // The adaptation dataset declares its levers as data; the create-a-scenario dialog reads them from
    // the base world, a moved knob becomes the new world's first update, and the plan's answer moves
    // with the assumption — while the primary keeps the baseline answer.
    await bootEngine(page, '/?engine=wasm&dataset=adaptation');
    await expect(page.locator('[data-testid="active-dataset"]')).toHaveText('Water adaptation pathways');
    await expect(page.locator('[data-testid="res-count"]')).toHaveText('3 rows');

    // The governance gaps the shapes flag: an undefended segment and an ownerless measure.
    await page.locator('.res-tab[data-tab="shacl"]').click();
    const conformance = page.locator('[data-testid="shacl-report"]');
    await expect(conformance).toContainText('Does not conform — 2 result(s).');
    await expect(conformance).toContainText('weakest interface');

    await page.locator('[data-testid="world-create"]').click();
    const levers = page.locator('[data-testid="scenario-levers"]');
    await expect(levers).toBeVisible();
    await expect(levers.locator('.lever')).toHaveCount(4);
    await page.locator('[data-testid="scenario-name"]').fill('high-sea');
    const seaLevelRow = levers.locator('.lever', { hasText: 'Sea-level rise' });
    await seaLevelRow.locator('input').evaluate((element, value) => {
      const knob = element as HTMLInputElement;
      knob.value = value;
      knob.dispatchEvent(new Event('input', { bubbles: true }));
      knob.dispatchEvent(new Event('change', { bubbles: true }));
    }, '60');
    await expect(seaLevelRow.locator('output')).toHaveText('60');
    await page.locator('[data-testid="scenario-create"]').click();
    await expect(page.locator('[data-testid="world-select"]')).toHaveValue('high-sea');

    // The question re-asked in the scenario world calls for the quay raise as well.
    await page.locator('[data-testid="run"]').click();
    await page.locator('.res-tab[data-tab="table"]').click();
    await expect.poll(() => page.locator('[data-testid="results"] tbody tr').count()).toBe(4);
    await expect(page.locator('[data-testid="results"]')).toContainText('Raise the harbour quays');

    // The diff against the primary is exactly the moved assumption.
    await page.locator('[data-testid="world-diff"]').click();
    await expect(page.locator('[data-testid="diff-summary"]')).toHaveText('1 transition · 2 triples');
    const diffTable = page.locator('[data-testid="worlds-diff"]');
    await expect(diffTable.locator('tr.diff-add')).toHaveCount(1);
    await expect(diffTable.locator('tr.diff-remove')).toHaveCount(1);
    await expect(diffTable).toContainText('seaLevelRiseCm');
    await expect(diffTable).toContainText('60');

    // The primary keeps the baseline answer.
    await page.locator('[data-testid="world-select"]').selectOption('main');
    await page.locator('.res-tab[data-tab="table"]').click();
    await page.locator('[data-testid="run"]').click();
    await expect.poll(() => page.locator('[data-testid="results"] tbody tr').count()).toBe(3);
  });

  test('upgrades the graph to the BabylonJS WebGL view once its chunk arrives', async ({ page }) => {
    // A canvas is permanently bound to its first rendering context, so the upgrade swaps a fresh canvas
    // in for the WebGL view; the data-engine marker names the view actually mounted — this row fails if
    // the upgrade silently keeps the 2D placeholder.
    await bootEngine(page);
    await expect(page.locator('#graph-canvas')).toHaveAttribute('data-engine', 'babylon', { timeout: 30_000 });
  });

  test('the BabylonJS view labels nodes on screen, groups kinds, and answers the layout controls', async ({ page }) => {
    // The interactive view: collision-aware screen labels drawn from a DOM pool (scaled and faded by
    // camera distance in 3D), per-kind group tags in the grouped layouts, a keyboard-reachable selection
    // channel into the accessible inspector, and working mode/zoom controls. This row fails on a scaffold
    // view that draws unlabeled spheres and answers no control.
    await bootEngine(page);
    const canvas = page.locator('#graph-canvas');
    await expect(canvas).toHaveAttribute('data-engine', 'babylon', { timeout: 30_000 });
    await expect(canvas).toHaveAttribute('data-graph-label-renderer', 'screen-space');
    await expect(canvas).toHaveAttribute('data-graph-edge-style', 'directed');
    await expect.poll(async () => Number(await canvas.getAttribute('data-graph-visible-labels'))).toBeGreaterThan(0);

    // The grouped layout raises the per-kind guides and tags.
    await page.locator('button[data-graph-mode="cluster"]').click();
    await expect(canvas).toHaveAttribute('data-graph-guides', 'clusters');
    await expect.poll(async () => Number(await canvas.getAttribute('data-graph-visible-cluster-labels'))).toBeGreaterThan(0);

    // The 3D layout hands the camera to the user.
    await page.locator('button[data-graph-mode="depth"]').click();
    await expect(canvas).toHaveAttribute('data-graph-navigation', 'orbit');

    // Zoom is a real control: the reported zoom moves.
    const zoomBefore = await canvas.getAttribute('data-graph-zoom');
    await page.locator('button[data-graph-action="zoom-in"]').click();
    await expect.poll(() => canvas.getAttribute('data-graph-zoom')).not.toBe(zoomBefore);

    // Keyboard selection reaches the accessible inspector.
    await canvas.focus();
    await page.keyboard.press('ArrowRight');
    await expect(page.locator('#graph-inspector')).toHaveAttribute('data-state', 'selected');
  });

  test('carries RDF 1.2 reified edge names, queryable through rdf:reifies (adaptation)', async ({ page }) => {
    // The defence relations are annotated in the dataset's Turtle: the annotation reifies each edge and
    // its name rides the reifier — five named defence connections, including two distinctly named edges
    // between the surge barrier and each segment it shares with another measure.
    await bootEngine(page, '/?engine=wasm&dataset=adaptation');
    const result = await runSparql(page, `PREFIX adp: <https://veritas.app/ns/adaptation#>
PREFIX rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#>
PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
SELECT ?edge WHERE { ?r rdf:reifies <<( ?m adp:defends ?seg )>> ; rdfs:label ?edge } ORDER BY ?edge`);
    const edges = (result.results?.bindings ?? []).map((row) => row.edge?.value);
    expect(edges).toHaveLength(5);
    expect(edges).toContain('shields the old town shore');
    expect(edges).toContain('raises the quay line');
  });

  test('paints the worlds strip from the served tier and hides it on a generic custom endpoint', async ({ page }) => {
    // Worlds are a first-party capability: a CLI-served origin answers GET /worlds and the strip presents
    // the listing (with the Diff view beside the other result tabs), while a user-entered generic endpoint
    // carries no worlds face — the strip and the Diff tab must hide, never error.
    await page.route((url) => url.pathname === '/config', (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: '{"engine":"http"}' }));
    await page.route((url) => url.pathname === '/sparql', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/sparql-results+json',
        body: JSON.stringify({ head: { vars: [] }, results: { bindings: [] } })
      }));
    await page.route((url) => url.pathname === '/worlds', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ worlds: [{ name: 'main', stateId: '00e1b2c3d4e5f607', parent: null }] })
      }));
    await page.route('https://endpoint.example/sparql', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/sparql-results+json',
        body: JSON.stringify({ head: { vars: [] }, results: { bindings: [] } })
      }));
    await page.goto('/', { waitUntil: 'load' });

    const strip = page.locator('[data-testid="worlds-strip"]');
    await expect(strip).toBeVisible();
    await expect(page.locator('[data-testid="world-select"]')).toHaveValue('main');
    await expect(page.locator('[data-testid="world-state"]')).toHaveText('00e1b2c3d4e5f607');
    await expect(page.locator('.res-tab[data-tab="diff"]')).toBeVisible();

    await page.selectOption('[data-testid="engine-select"]', 'custom');
    const input = page.locator('[data-testid="endpoint-input"]');
    await input.fill('https://endpoint.example/sparql');
    await input.press('Enter');

    await expect(strip).toBeHidden();
    await expect(page.locator('.res-tab[data-tab="diff"]')).toBeHidden();
  });
});
