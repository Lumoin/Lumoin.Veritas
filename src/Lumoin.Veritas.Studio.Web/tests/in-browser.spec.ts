// End-to-end coverage of the no-scaffolding (WASM) deployment: the Veritas engine running fully in the
// browser. Each test boots the engine under ?engine=wasm (which loads the default dataset), then drives it
// through window.veritasEngine / window.studio — the same surfaces the shell uses — asserting the query,
// dataset-switch, and SHACL-validation paths. Chromium by default; ALL_BROWSERS=1 runs the full matrix.
import { test, expect, type Page } from '@playwright/test';

const WATER_RISK_QUERY = `PREFIX schema: <https://schema.org/>
PREFIX untp: <https://vocabulary.uncefact.org/untp/>
PREFIX vu: <https://veritas.app/ns/untp#>
PREFIX vw: <https://veritas.app/ns/water#>
SELECT ?garmentName ?facilityName ?riskScore WHERE {
  ?garment a untp:Product ; schema:name ?garmentName ; untp:productCategory "6205" .
  ?garment vu:upstreamInput+ ?upstreamProduct .
  ?upstreamProduct untp:producedAtFacility ?facility .
  ?facility schema:name ?facilityName ; vw:locatedInBasin ?basin .
  ?risk vw:assessedBasin ?basin ; vw:score ?riskScore .
  FILTER (?riskScore >= 4.0)
}`;

const TEXTILE_SHAPES = `@prefix sh: <http://www.w3.org/ns/shacl#> .
@prefix untp: <https://vocabulary.uncefact.org/untp/> .
@prefix ex: <https://veritas.app/shapes/textile#> .
ex:ProductTraceabilityShape a sh:NodeShape ;
    sh:targetClass untp:Product ;
    sh:property [ sh:path untp:cottonSpecies ; sh:minCount 1 ;
        sh:message "Every product must declare its cotton species." ] .`;

const BATTERY_SHAPES = `@prefix sh: <http://www.w3.org/ns/shacl#> .
@prefix bat: <https://veritas.app/ns/battery#> .
@prefix ex: <https://veritas.app/shapes/battery#> .
ex:RecycledContentShape a sh:NodeShape ;
    sh:targetClass bat:Battery ;
    sh:property [ sh:path bat:recycledCobalt ; sh:minInclusive 10 ;
        sh:message "Recycled cobalt content must be at least 10%." ] .`;

/** The minimal window surface the in-browser engine and shell expose to the page. */
interface StudioWindow {
  veritasEngine: { runSparql(query: string): Promise<string>; validateShacl(shapes: string): Promise<string> };
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

/** Navigates to the WASM deployment and waits for the engine (and its default dataset) to be ready. */
async function bootEngine(page: Page): Promise<void> {
  await page.goto('/?engine=wasm', { waitUntil: 'load' });
  await page.waitForFunction(() => (globalThis as unknown as Partial<StudioWindow>).veritasEngine !== undefined, undefined, { timeout: 90_000 });
}

/** Runs a SPARQL query on the in-browser engine and parses its results document. */
async function runSparql(page: Page, query: string): Promise<ResultsDocument> {
  const json = await page.evaluate((q) => (globalThis as unknown as StudioWindow).veritasEngine.runSparql(q), query);

  return JSON.parse(json) as ResultsDocument;
}

/** Validates the loaded dataset against shapes on the in-browser engine and parses the report. */
async function validate(page: Page, shapes: string): Promise<ReportDocument> {
  const json = await page.evaluate((s) => (globalThis as unknown as StudioWindow).veritasEngine.validateShacl(s), shapes);

  return JSON.parse(json) as ReportDocument;
}

/** The number of solution bindings in a results document. */
function rowCount(document: ResultsDocument): number {
  return document.results?.bindings.length ?? 0;
}

test.describe('in-browser (WASM) engine', () => {
  test('boots and loads the default cotton→garment dataset', async ({ page }) => {
    await bootEngine(page);
    const products = await runSparql(page, 'PREFIX untp: <https://vocabulary.uncefact.org/untp/> SELECT ?p WHERE { ?p a untp:Product }');
    expect(rowCount(products)).toBeGreaterThan(0);
  });

  test('traces garments to high-water-stress basins (property path + FILTER)', async ({ page }) => {
    await bootEngine(page);
    const result = await runSparql(page, WATER_RISK_QUERY);
    expect(rowCount(result)).toBe(4);
  });

  test('flags textile products missing a cotton-species declaration (SHACL)', async ({ page }) => {
    await bootEngine(page);
    const report = await validate(page, TEXTILE_SHAPES);
    expect(report.conforms).toBe(false);
    expect(report.results.length).toBe(4);
  });

  test('switches to battery and flags packs below the recycled-cobalt threshold', async ({ page }) => {
    await bootEngine(page);
    await page.evaluate(() => (globalThis as unknown as StudioWindow).studio.loadDataset('battery'));
    await expect
      .poll(async () => rowCount(await runSparql(page, 'PREFIX bat: <https://veritas.app/ns/battery#> SELECT ?b WHERE { ?b a bat:Battery }')))
      .toBe(5);

    const report = await validate(page, BATTERY_SHAPES);
    expect(report.conforms).toBe(false);
    expect(report.results.length).toBe(3);
  });
});
