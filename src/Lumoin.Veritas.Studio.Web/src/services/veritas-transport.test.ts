// The transport seam's unit rows: every branch of the never-throw degrade ladders, exercised
// through the injectable fetch and stub bridge/engine surfaces the seam was built with. The two
// wrong-type-diagnostics normalizations (an arbitrary endpoint or a failure document putting a
// non-array in the diagnostics field must never flow into the renderer's unconditional map) are
// pinned here as regressions.

import { describe, expect, it } from 'vitest';
import {
  type FetchLike,
  HttpVeritasTransport,
  NativeVeritasTransport,
  type VeritasBridge,
  type VeritasWasmEngine,
  WasmVeritasTransport,
  serverEngineAvailable
} from './veritas-transport';

/** A canned fetch answering every request with one fixed response. */
const fetchAnswering = (status: number, body: string, contentType = 'application/json'): FetchLike =>
  () => Promise.resolve(new Response(body, { status, headers: { 'content-type': contentType } }));

/** A canned fetch whose every request rejects, the network-fault shape. */
const fetchFaulting: FetchLike = () => Promise.reject(new TypeError('Failed to fetch'));

/** A minimal SPARQL-results-JSON document. */
const resultsDocument = JSON.stringify({ head: { vars: ['s'] }, results: { bindings: [{ s: { type: 'uri', value: 'https://example.org/a' } }] } });

describe('HttpVeritasTransport.runSparql', () => {
  it('returns the parsed results document on an OK SPARQL-results-JSON answer', async () => {
    const transport = new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, resultsDocument) });
    const outcome = await transport.runSparql('SELECT * WHERE { ?s ?p ?o }');
    expect(outcome.ok).toBe(true);
    if (outcome.ok) {
      expect(outcome.results.results?.bindings).toHaveLength(1);
    }
  });

  it('degrades an OK non-JSON body to an error naming the body, never a throw', async () => {
    const transport = new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, '<html>surprise</html>', 'text/html') });
    const outcome = await transport.runSparql('SELECT * WHERE { ?s ?p ?o }');
    expect(outcome.ok).toBe(false);
    if (!outcome.ok) {
      expect(outcome.error.error).toContain('not SPARQL-results-JSON');
      expect(outcome.error.diagnostics).toEqual([]);
    }
  });

  it('passes an engine failure document through with its diagnostics array', async () => {
    const failure = JSON.stringify({ error: 'Undefined prefix', diagnostics: [{ message: 'x', start: 0, length: 1 }] });
    const transport = new HttpVeritasTransport({ fetchImpl: fetchAnswering(400, failure) });
    const outcome = await transport.runSparql('SELECT * WHERE { ?s ?p ?o }');
    expect(outcome.ok).toBe(false);
    if (!outcome.ok) {
      expect(outcome.error.error).toBe('Undefined prefix');
      expect(outcome.error.diagnostics).toHaveLength(1);
    }
  });

  it('normalizes a wrong-type diagnostics field to an empty array', async () => {
    const failure = JSON.stringify({ error: 'boom', diagnostics: { not: 'an array' } });
    const transport = new HttpVeritasTransport({ fetchImpl: fetchAnswering(500, failure) });
    const outcome = await transport.runSparql('SELECT * WHERE { ?s ?p ?o }');
    expect(outcome.ok).toBe(false);
    if (!outcome.ok) {
      expect(outcome.error.error).toBe('boom');
      expect(outcome.error.diagnostics).toEqual([]);
    }
  });

  it('degrades a non-OK non-JSON body to the status line with a bounded snippet', async () => {
    const longBody = `<html>${'x'.repeat(500)}</html>`;
    const transport = new HttpVeritasTransport({ fetchImpl: fetchAnswering(502, longBody, 'text/html') });
    const outcome = await transport.runSparql('SELECT * WHERE { ?s ?p ?o }');
    expect(outcome.ok).toBe(false);
    if (!outcome.ok) {
      expect(outcome.error.error).toContain('HTTP 502');
      expect(outcome.error.error.length).toBeLessThan(220);
      expect(outcome.error.error.endsWith('…')).toBe(true);
    }
  });

  it('states a bare status line when the non-OK body is empty', async () => {
    const transport = new HttpVeritasTransport({ fetchImpl: fetchAnswering(503, '') });
    const outcome = await transport.runSparql('SELECT * WHERE { ?s ?p ?o }');
    expect(outcome.ok).toBe(false);
    if (!outcome.ok) {
      expect(outcome.error.error).toBe('The endpoint answered HTTP 503');
    }
  });

  it('degrades a transport-level fault to an error outcome naming it, never a throw', async () => {
    const transport = new HttpVeritasTransport({ fetchImpl: fetchFaulting });
    const outcome = await transport.runSparql('SELECT * WHERE { ?s ?p ?o }');
    expect(outcome.ok).toBe(false);
    if (!outcome.ok) {
      expect(outcome.error.error).toContain('could not be reached');
    }
  });
});

describe('HttpVeritasTransport faces', () => {
  it('answers null from describeLiteral without a request when the endpoint has no diagnostics face', async () => {
    let calls = 0;
    const counting: FetchLike = () => {
      calls += 1;

      return Promise.resolve(new Response('{}', { status: 200 }));
    };
    const transport = new HttpVeritasTransport({ fetchImpl: counting, literalDiagnosticsEndpoint: null });
    expect(await transport.describeLiteral('https://example.org/dt', 'body')).toBeNull();
    expect(calls).toBe(0);
  });

  it('reads a four-state diagnosis document', async () => {
    const transport = new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, JSON.stringify({ status: 'invalid', kind: 'grammar', byteOffset: 3, datatype: 'https://example.org/dt' })) });
    const diagnosis = await transport.describeLiteral('https://example.org/dt', 'body');
    expect(diagnosis?.status).toBe('invalid');
    expect(diagnosis?.byteOffset).toBe(3);
  });

  it('answers null for a diagnosis whose status is outside the four states', async () => {
    const transport = new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, JSON.stringify({ status: 'exploded', datatype: 'https://example.org/dt' })) });
    expect(await transport.describeLiteral('https://example.org/dt', 'body')).toBeNull();
  });

  it('answers null for a non-OK diagnosis answer and for a faulted request', async () => {
    const nonOk = new HttpVeritasTransport({ fetchImpl: fetchAnswering(500, '{}') });
    expect(await nonOk.describeLiteral('https://example.org/dt', 'body')).toBeNull();
    const faulted = new HttpVeritasTransport({ fetchImpl: fetchFaulting });
    expect(await faulted.describeLiteral('https://example.org/dt', 'body')).toBeNull();
  });

  it('reads a structurally valid completion context and refuses a half-shaped one', async () => {
    const valid = JSON.stringify({ caret: 4, expectedTokens: ['VAR'], enclosingProductions: ['SelectQuery'], inScopeVariables: [], variablePredicates: [] });
    const validTransport = new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, valid) });
    expect((await validTransport.describeCompletion('SELECT', 4))?.caret).toBe(4);

    const missingArrays = JSON.stringify({ caret: 4, expectedTokens: ['VAR'], enclosingProductions: ['SelectQuery'] });
    const invalidTransport = new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, missingArrays) });
    expect(await invalidTransport.describeCompletion('SELECT', 4)).toBeNull();
  });

  it('reads a vocabulary corpus only when it is an array of strings', async () => {
    const corpus = new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, JSON.stringify(['rdf:type', '<https://example.org/dt>'])) });
    expect(await corpus.editorVocabulary()).toEqual(['rdf:type', '<https://example.org/dt>']);

    const wrongElements = new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, JSON.stringify(['rdf:type', 7])) });
    expect(await wrongElements.editorVocabulary()).toBeNull();
  });

  it('reads traceAvailable from the trace endpoint: the default carries it, an explicit null does not', () => {
    expect(new HttpVeritasTransport({ fetchImpl: fetchFaulting }).traceAvailable).toBe(true);
    expect(new HttpVeritasTransport({ fetchImpl: fetchFaulting, traceEndpoint: null }).traceAvailable).toBe(false);
  });
});

describe('WasmVeritasTransport', () => {
  /** A stub engine whose every face answers the given documents. */
  const engineAnswering = (sparqlDocument: string): VeritasWasmEngine => ({
    runSparql: () => Promise.resolve(sparqlDocument),
    loadTurtle: () => Promise.resolve(),
    validateShacl: () => Promise.resolve('{}'),
    describeCompletion: () => Promise.resolve('{}'),
    editorVocabulary: () => Promise.resolve('[]'),
    describeTurtleCompletion: () => Promise.resolve('{}'),
    describeGeoLiteral: () => Promise.resolve('{}'),
    listWorldsJson: () => Promise.resolve(JSON.stringify({ worlds: [{ name: 'main', stateId: '00ff00ff00ff00ff', parent: null }] })),
    forkWorld: () => Promise.resolve('{"outcome":"forked"}'),
    dropWorld: () => Promise.resolve('{"outcome":"dropped"}'),
    runSparqlIn: () => Promise.resolve(sparqlDocument),
    runUpdateIn: () => Promise.resolve('{"outcome":"updated"}'),
    diffWorldsJson: () => Promise.resolve(JSON.stringify({ outcome: 'diffed', cap: 1000, totalTransitions: 0, totalTriples: 0, truncated: false, transitions: [] })),
    onTrace: () => () => undefined
  });

  it('passes a results document through', async () => {
    const outcome = await new WasmVeritasTransport(engineAnswering(resultsDocument)).runSparql('SELECT * WHERE { ?s ?p ?o }');
    expect(outcome.ok).toBe(true);
  });

  it('normalizes a failure document with a wrong-type diagnostics field to an empty array', async () => {
    const failure = JSON.stringify({ error: 'boom', diagnostics: 'not-an-array' });
    const outcome = await new WasmVeritasTransport(engineAnswering(failure)).runSparql('SELECT * WHERE { ?s ?p ?o }');
    expect(outcome.ok).toBe(false);
    if (!outcome.ok) {
      expect(outcome.error.diagnostics).toEqual([]);
    }
  });
});

describe('NativeVeritasTransport', () => {
  it('answers null from every optional face the injected bridge does not carry', async () => {
    const bareBridge = { runSparql: () => Promise.resolve({ ok: true, results: {} }), onTrace: () => () => undefined } as unknown as VeritasBridge;
    const transport = new NativeVeritasTransport(bareBridge);
    expect(await transport.describeLiteral('https://example.org/dt', 'body')).toBeNull();
    expect(await transport.describeCompletion('SELECT', 0)).toBeNull();
    expect(await transport.describeTurtleCompletion('@prefix', 0, 'turtle')).toBeNull();
    expect(await transport.editorVocabulary()).toBeNull();
  });

  it('answers null when a bridge face faults instead of surfacing the failure', async () => {
    const faultingBridge = {
      runSparql: () => Promise.resolve({ ok: true, results: {} }),
      onTrace: () => () => undefined,
      describeLiteral: () => Promise.reject(new Error('channel fault'))
    } as unknown as VeritasBridge;
    expect(await new NativeVeritasTransport(faultingBridge).describeLiteral('https://example.org/dt', 'body')).toBeNull();
  });
});

describe('serverEngineAvailable', () => {
  it('reads the CLI marker as a server engine', async () => {
    expect(await serverEngineAvailable(fetchAnswering(200, JSON.stringify({ engine: 'http' })))).toBe(true);
  });

  it('reads an SPA-fallback HTML answer as no server engine', async () => {
    expect(await serverEngineAvailable(fetchAnswering(200, '<!doctype html><title>app</title>', 'text/html'))).toBe(false);
  });

  it('reads a 404, a foreign marker, and a network fault all as no server engine', async () => {
    expect(await serverEngineAvailable(fetchAnswering(404, ''))).toBe(false);
    expect(await serverEngineAvailable(fetchAnswering(200, JSON.stringify({ engine: 'other' })))).toBe(false);
    expect(await serverEngineAvailable(fetchFaulting)).toBe(false);
  });
});

describe('HttpVeritasTransport worlds face', () => {
  const listingDocument = JSON.stringify({ worlds: [{ name: 'main', stateId: '00ff00ff00ff00ff', parent: null }, { name: 'what-if', stateId: 'aa11aa11aa11aa11', parent: 'main' }] });
  const diffDocument = JSON.stringify({
    outcome: 'diffed',
    cap: 1000,
    totalTransitions: 1,
    totalTriples: 1,
    truncated: false,
    transitions: [{ graph: null, totalAdditions: 1, totalRemovals: 0, additions: [{ s: '<https://example.org/a>', p: '<https://example.org/p>', o: '"x"' }], removals: [] }]
  });

  it('reads worldsAvailable from the worlds endpoint: the default carries it, an explicit null does not', () => {
    expect(new HttpVeritasTransport({ fetchImpl: fetchFaulting }).worldsAvailable).toBe(true);
    expect(new HttpVeritasTransport({ fetchImpl: fetchFaulting, worldsEndpoint: null }).worldsAvailable).toBe(false);
  });

  it('degrades every worlds member without a request when the endpoint has no worlds face', async () => {
    let calls = 0;
    const counting: FetchLike = () => {
      calls += 1;

      return Promise.resolve(new Response('{}', { status: 200 }));
    };
    const transport = new HttpVeritasTransport({ fetchImpl: counting, worldsEndpoint: null });
    expect(await transport.listWorlds()).toBeNull();
    expect(await transport.forkWorld('main', 'what-if')).toBeNull();
    expect(await transport.dropWorld('what-if')).toBeNull();
    expect(await transport.diffWorlds('main', 'what-if')).toBeNull();
    const query = await transport.runSparqlIn('what-if', 'SELECT * WHERE { ?s ?p ?o }');
    expect(query.ok).toBe(false);
    const update = await transport.updateIn('what-if', 'INSERT DATA { <a:s> <a:p> <a:o> }');
    expect(update.ok).toBe(false);
    expect(calls).toBe(0);
  });

  it('reads a structurally valid worlds listing and refuses a half-shaped one', async () => {
    const valid = new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, listingDocument) });
    const worlds = await valid.listWorlds();
    expect(worlds).toHaveLength(2);
    expect(worlds?.[1]).toEqual({ name: 'what-if', stateId: 'aa11aa11aa11aa11', parent: 'main' });

    const missingStateId = JSON.stringify({ worlds: [{ name: 'main', parent: null }] });
    expect(await new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, missingStateId) }).listWorlds()).toBeNull();
  });

  it('reads fork and drop outcome tokens and refuses a foreign token', async () => {
    expect(await new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, '{"outcome":"duplicateName"}') }).forkWorld('main', 'taken')).toBe('duplicateName');
    expect(await new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, '{"outcome":"primaryWorld"}') }).dropWorld('main')).toBe('primaryWorld');
    expect(await new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, '{"outcome":"exploded"}') }).forkWorld('main', 'x')).toBeNull();
    expect(await new HttpVeritasTransport({ fetchImpl: fetchAnswering(404, '') }).dropWorld('x')).toBeNull();
  });

  it('reads a bounded diff document, passes the unknown-world outcome through, and refuses a half-shaped diff', async () => {
    const diff = await new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, diffDocument) }).diffWorlds('main', 'what-if');
    expect(diff?.outcome).toBe('diffed');
    if (diff?.outcome === 'diffed') {
      expect(diff.totalTriples).toBe(1);
      expect(diff.transitions[0]?.additions[0]?.s).toBe('<https://example.org/a>');
    }

    expect(await new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, '{"outcome":"unknownWorld"}') }).diffWorlds('main', 'missing')).toEqual({ outcome: 'unknownWorld' });

    const missingTotals = JSON.stringify({ outcome: 'diffed', transitions: [] });
    expect(await new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, missingTotals) }).diffWorlds('main', 'what-if')).toBeNull();
  });

  it('answers world-scoped queries with the same shapes the protocol route answers', async () => {
    const ok = await new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, resultsDocument) }).runSparqlIn('what-if', 'SELECT * WHERE { ?s ?p ?o }');
    expect(ok.ok).toBe(true);

    const failed = await new HttpVeritasTransport({ fetchImpl: fetchAnswering(400, JSON.stringify({ error: 'no such world' })) }).runSparqlIn('missing', 'SELECT * WHERE { ?s ?p ?o }');
    expect(failed.ok).toBe(false);
    if (!failed.ok) {
      expect(failed.error.error).toBe('no such world');
    }
  });

  it('answers world-scoped updates value-based on every path', async () => {
    expect(await new HttpVeritasTransport({ fetchImpl: fetchAnswering(200, '{"outcome":"updated"}') }).updateIn('what-if', 'INSERT DATA { <a:s> <a:p> <a:o> }')).toEqual({ ok: true });

    const refused = await new HttpVeritasTransport({ fetchImpl: fetchAnswering(400, JSON.stringify({ error: 'did not parse' })) }).updateIn('what-if', 'INSERT nonsense');
    expect(refused).toEqual({ ok: false, error: 'did not parse' });

    const faulted = await new HttpVeritasTransport({ fetchImpl: fetchFaulting }).updateIn('what-if', 'INSERT DATA { <a:s> <a:p> <a:o> }');
    expect(faulted.ok).toBe(false);
  });
});

describe('WasmVeritasTransport worlds face', () => {
  /** A stub engine carrying the worlds face with the given documents. */
  const worldsEngine = (): VeritasWasmEngine => ({
    runSparql: () => Promise.resolve(resultsDocument),
    loadTurtle: () => Promise.resolve(),
    validateShacl: () => Promise.resolve('{}'),
    describeCompletion: () => Promise.resolve('{}'),
    editorVocabulary: () => Promise.resolve('[]'),
    describeTurtleCompletion: () => Promise.resolve('{}'),
    describeGeoLiteral: () => Promise.resolve('{}'),
    listWorldsJson: () => Promise.resolve(JSON.stringify({ worlds: [{ name: 'main', stateId: '00ff00ff00ff00ff', parent: null }] })),
    forkWorld: () => Promise.resolve('{"outcome":"forked"}'),
    dropWorld: () => Promise.resolve('{"outcome":"unknownWorld"}'),
    runSparqlIn: () => Promise.resolve(resultsDocument),
    runUpdateIn: () => Promise.resolve(JSON.stringify({ error: 'did not parse' })),
    diffWorldsJson: () => Promise.resolve('{"outcome":"unknownWorld"}'),
    onTrace: () => () => undefined
  });

  it('answers the worlds face through the engine documents', async () => {
    const transport = new WasmVeritasTransport(worldsEngine());
    expect(transport.worldsAvailable).toBe(true);
    expect(await transport.listWorlds()).toEqual([{ name: 'main', stateId: '00ff00ff00ff00ff', parent: null }]);
    expect(await transport.forkWorld('main', 'what-if')).toBe('forked');
    expect(await transport.dropWorld('missing')).toBe('unknownWorld');
    expect(await transport.diffWorlds('main', 'missing')).toEqual({ outcome: 'unknownWorld' });
    const query = await transport.runSparqlIn('what-if', 'SELECT * WHERE { ?s ?p ?o }');
    expect(query.ok).toBe(true);
    expect(await transport.updateIn('what-if', 'INSERT nonsense')).toEqual({ ok: false, error: 'did not parse' });
  });

  it('degrades every worlds member on an engine surface that predates the face', async () => {
    const stale = {
      runSparql: () => Promise.resolve(resultsDocument),
      onTrace: () => () => undefined
    } as unknown as VeritasWasmEngine;
    const transport = new WasmVeritasTransport(stale);
    expect(transport.worldsAvailable).toBe(false);
    expect(await transport.listWorlds()).toBeNull();
    expect(await transport.forkWorld('main', 'what-if')).toBeNull();
    expect(await transport.dropWorld('what-if')).toBeNull();
    expect(await transport.diffWorlds('main', 'what-if')).toBeNull();
    const query = await transport.runSparqlIn('what-if', 'SELECT * WHERE { ?s ?p ?o }');
    expect(query.ok).toBe(false);
    const update = await transport.updateIn('what-if', 'INSERT DATA { <a:s> <a:p> <a:o> }');
    expect(update.ok).toBe(false);
  });
});

describe('NativeVeritasTransport worlds face', () => {
  it('reads the whole worlds face as unavailable on a bridge that does not carry it', async () => {
    const bareBridge = { runSparql: () => Promise.resolve({ ok: true, results: {} }), onTrace: () => () => undefined } as unknown as VeritasBridge;
    const transport = new NativeVeritasTransport(bareBridge);
    expect(transport.worldsAvailable).toBe(false);
    expect(await transport.listWorlds()).toBeNull();
    expect(await transport.forkWorld('main', 'what-if')).toBeNull();
    expect(await transport.dropWorld('what-if')).toBeNull();
    expect(await transport.diffWorlds('main', 'what-if')).toBeNull();
    const query = await transport.runSparqlIn('what-if', 'SELECT * WHERE { ?s ?p ?o }');
    expect(query.ok).toBe(false);
    const update = await transport.updateIn('what-if', 'INSERT DATA { <a:s> <a:p> <a:o> }');
    expect(update.ok).toBe(false);
  });

  it('forwards the worlds face and absorbs a channel fault value-based', async () => {
    const bridge = {
      runSparql: () => Promise.resolve({ ok: true, results: {} }),
      onTrace: () => () => undefined,
      listWorlds: () => Promise.resolve([{ name: 'main', stateId: '00ff00ff00ff00ff', parent: null }]),
      forkWorld: () => Promise.reject(new Error('channel fault')),
      updateIn: () => Promise.reject(new Error('channel fault'))
    } as unknown as VeritasBridge;
    const transport = new NativeVeritasTransport(bridge);
    expect(transport.worldsAvailable).toBe(true);
    expect(await transport.listWorlds()).toEqual([{ name: 'main', stateId: '00ff00ff00ff00ff', parent: null }]);
    expect(await transport.forkWorld('main', 'what-if')).toBeNull();
    const update = await transport.updateIn('what-if', 'INSERT DATA { <a:s> <a:p> <a:o> }');
    expect(update.ok).toBe(false);
  });
});
