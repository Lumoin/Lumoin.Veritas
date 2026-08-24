// The seam between the Studio app and the Veritas engine. The app never reaches the engine directly:
// it codes against VeritasTransport, and the right implementation is resolved once at boot —
//   • NativeVeritasTransport — the desktop shell injects `window.veritas`, a typed message channel
//     over the host's WebMessageReceived/SendWebMessage bridge into the IN-PROCESS engine (no HTTP).
//   • HttpVeritasTransport — a plain browser / dev front door talks to a SPARQL Protocol endpoint:
//     this origin's CLI server by default (the Vite dev server proxies /sparql, /trace to it), or any
//     conformant endpoint the user points the Studio at through the engine-source picker.
// Both return discriminated unions (never throw a string). A new surface (a WASM-in-worker engine,
// say) is a third implementation, not a change to any caller.

import type { SparqlQueryError, SparqlResultsDocument } from '../components/sparql-results';

export type FetchLike = typeof globalThis.fetch;

export type SparqlOutcome =
  | { ok: true; results: SparqlResultsDocument }
  | { ok: false; error: SparqlQueryError };

/** A trace event the "why these terms" panel renders; the engine's trace bus, projected to the wire. */
export interface TraceEvent {
  correlationId: string;
  sequence: number;
  kind: string;
  term?: string;
  detail: string;
}

/**
 * One literal's diagnosis, as the engine's literal-diagnostics face answers it. The four states are
 * structural: `unsupported` is a datatype the face does not diagnose, `valid` a body the engine reads,
 * `invalid` a body that breaks its datatype's grammar, and `warning` a body the datatype tolerates yet the
 * engine cannot evaluate. `kind` (a refusal-kind token) and `byteOffset` (UTF-8 bytes into the literal
 * value, -1 when the refusal is unlocated) come with the latter two.
 */
export interface LiteralDiagnosisDto {
  status: 'unsupported' | 'valid' | 'warning' | 'invalid';
  kind?: string;
  byteOffset?: number;
  datatype: string;
}

/**
 * The parser-driven SPARQL completion context at a caret: the caret the source described (a UTF-16 index),
 * the token kinds the grammar admits there, the productions enclosing that position, the variables in scope
 * (each with its resolved datatype when the data determines one), and the predicates each variable is used
 * with. The wire shape of the engine's SPARQL completion document.
 */
export interface CompletionContextDto {
  readonly caret: number;
  readonly expectedTokens: readonly string[];
  readonly enclosingProductions: readonly string[];
  readonly inScopeVariables: readonly { readonly name: string; readonly datatype: string | null; readonly datatypeSource: string }[];
  readonly variablePredicates: readonly { readonly variable: string; readonly predicate: string; readonly position: string }[];
}

/**
 * The parser-driven Turtle-family completion context at a caret: the caret the source described (a UTF-16
 * index), the token kinds the grammar admits there, and the productions enclosing that position. The wire
 * shape of the engine's Turtle completion document.
 */
export interface TurtleCompletionContextDto {
  readonly caret: number;
  readonly expectedTokens: readonly string[];
  readonly enclosingProductions: readonly string[];
}

/** Whether a parsed value is an array of strings — the shape a completion document's token arrays and the vocabulary corpus both take. */
function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((entry) => typeof entry === 'string');
}

/** Whether a parsed document carries the fields every completion context has: the number caret and the two token arrays. */
function hasCompletionCore(document: Partial<TurtleCompletionContextDto>): boolean {
  return typeof document.caret === 'number'
    && isStringArray(document.expectedTokens)
    && isStringArray(document.enclosingProductions);
}

/**
 * Reads a SPARQL completion document, structurally validated: the number caret, the two token arrays, and
 * the two variable arrays. Null on anything else — an unparseable body, or a document from a source that
 * answers something other than a completion context — so the editor degrades to its token heuristic rather
 * than proposing from a half-read document.
 * @param json The document text.
 * @returns The context, or null.
 */
function readCompletionContext(json: string): CompletionContextDto | null {
  try {
    const document = JSON.parse(json) as Partial<CompletionContextDto>;

    return hasCompletionCore(document) && Array.isArray(document.inScopeVariables) && Array.isArray(document.variablePredicates)
      ? (document as CompletionContextDto)
      : null;
  } catch {
    // Not a JSON document: this source proposes nothing for the position.
    return null;
  }
}

/**
 * Reads a Turtle-family completion document, structurally validated: the number caret and the two token
 * arrays. Null on anything else, so the editor degrades to its token heuristic.
 * @param json The document text.
 * @returns The context, or null.
 */
function readTurtleCompletionContext(json: string): TurtleCompletionContextDto | null {
  try {
    const document = JSON.parse(json) as Partial<TurtleCompletionContextDto>;

    return hasCompletionCore(document) ? (document as TurtleCompletionContextDto) : null;
  } catch {
    // Not a JSON document: this source proposes nothing for the position.
    return null;
  }
}

/**
 * Reads an editor-vocabulary document: the corpus as an array of prefixed names and bracketed full IRIs.
 * Null on anything else, so the editor keeps the corpus it already has.
 * @param json The document text.
 * @returns The corpus, or null.
 */
function readEditorVocabulary(json: string): string[] | null {
  try {
    const document: unknown = JSON.parse(json);

    return isStringArray(document) ? document : null;
  } catch {
    // Not a JSON document: this source carries no vocabulary corpus.
    return null;
  }
}

/**
 * One world in the worlds listing: its name, its content-addressed state identifier (the revision token
 * a caching or streaming consumer scopes by — sixteen hex digits crossing as text), and the name of the
 * world it was forked from (null for the primary world). The parent is fork lineage recorded at fork
 * time; it stands even after the parent's name is dropped.
 */
export interface WorldDto {
  readonly name: string;
  readonly stateId: string;
  readonly parent: string | null;
}

/** The fork outcome tokens the worlds wire carries; an unknown source and a taken name are expected conditions, never faults. */
export type WorldForkOutcomeDto = 'forked' | 'unknownSource' | 'duplicateName';

/** The drop outcome tokens the worlds wire carries; the primary world is never droppable. */
export type WorldDropOutcomeDto = 'dropped' | 'unknownWorld' | 'primaryWorld';

/** One triple of a worlds diff, each term in its lexical form (`<iri>`, `_:label`, `"value"^^<datatype>`), decoded engine-side. */
export interface WorldTripleDto {
  readonly s: string;
  readonly p: string;
  readonly o: string;
}

/**
 * One graph's part of a worlds diff: the graph's name term in lexical form (null for the default
 * graph), the exact addition and removal totals, and the listed triples — capped by the document's
 * triple budget, so the totals are the truth and the listings may be cut.
 */
export interface WorldTransitionDto {
  readonly graph: string | null;
  readonly totalAdditions: number;
  readonly totalRemovals: number;
  readonly additions: readonly WorldTripleDto[];
  readonly removals: readonly WorldTripleDto[];
}

/**
 * The bounded worlds diff document: on `diffed` the cap it was written under, the exact transition and
 * triple totals, whether any triple was omitted, and the per-graph transitions; an unknown world on
 * either side is the bare outcome. Truncated-at-N-of-M truth, never an unbounded dump.
 */
export type WorldsDiffDto =
  | {
      readonly outcome: 'diffed';
      readonly cap: number;
      readonly totalTransitions: number;
      readonly totalTriples: number;
      readonly truncated: boolean;
      readonly transitions: readonly WorldTransitionDto[];
    }
  | { readonly outcome: 'unknownWorld' };

/** The outcome of a world-scoped update: committed, or the failure message the engine named. */
export type WorldUpdateOutcome = { ok: true } | { ok: false; error: string };

/** Whether a parsed value is one world entry: the two strings and the nullable parent. */
function isWorldDto(value: unknown): value is WorldDto {
  const entry = value as Partial<WorldDto>;

  return typeof entry === 'object' && entry !== null
    && typeof entry.name === 'string'
    && typeof entry.stateId === 'string'
    && (entry.parent === null || typeof entry.parent === 'string');
}

/**
 * Reads a worlds listing document, structurally validated entry by entry. Null on anything else — an
 * unparseable body, or a document from a source that answers something other than a worlds listing — so
 * the caller degrades to no worlds rather than presenting a half-read list.
 * @param json The document text.
 * @returns The worlds, or null.
 */
function readWorldsList(json: string): WorldDto[] | null {
  try {
    const document = JSON.parse(json) as { worlds?: unknown };

    return Array.isArray(document.worlds) && document.worlds.every(isWorldDto)
      ? (document.worlds as WorldDto[])
      : null;
  } catch {
    // Not a JSON document: this source answers no worlds listing.
    return null;
  }
}

/**
 * Reads a fork outcome document. Null on anything that is not one of the three tokens, so the caller
 * degrades value-based on one signal.
 * @param json The document text.
 * @returns The outcome token, or null.
 */
function readForkOutcome(json: string): WorldForkOutcomeDto | null {
  try {
    const document = JSON.parse(json) as { outcome?: unknown };

    return document.outcome === 'forked' || document.outcome === 'unknownSource' || document.outcome === 'duplicateName'
      ? document.outcome
      : null;
  } catch {
    // Not a JSON document: this source answers no fork outcome.
    return null;
  }
}

/**
 * Reads a drop outcome document. Null on anything that is not one of the three tokens.
 * @param json The document text.
 * @returns The outcome token, or null.
 */
function readDropOutcome(json: string): WorldDropOutcomeDto | null {
  try {
    const document = JSON.parse(json) as { outcome?: unknown };

    return document.outcome === 'dropped' || document.outcome === 'unknownWorld' || document.outcome === 'primaryWorld'
      ? document.outcome
      : null;
  } catch {
    // Not a JSON document: this source answers no drop outcome.
    return null;
  }
}

/** Whether a parsed value is one diff transition: the nullable graph, the two exact totals, and the two triple arrays. */
function isWorldTransitionDto(value: unknown): value is WorldTransitionDto {
  const transition = value as Partial<WorldTransitionDto>;

  return typeof transition === 'object' && transition !== null
    && (transition.graph === null || typeof transition.graph === 'string')
    && typeof transition.totalAdditions === 'number'
    && typeof transition.totalRemovals === 'number'
    && Array.isArray(transition.additions)
    && Array.isArray(transition.removals);
}

/**
 * Reads a worlds diff document, structurally validated: the bare unknown-world outcome passes through,
 * and a diffed document must carry the cap, the exact totals, the truncation flag, and well-shaped
 * transitions. Null on anything else, so the caller degrades rather than rendering a half-read diff.
 * @param json The document text.
 * @returns The diff document, or null.
 */
function readWorldsDiff(json: string): WorldsDiffDto | null {
  try {
    const document = JSON.parse(json) as Partial<WorldsDiffDto> & { outcome?: unknown; transitions?: unknown };
    if (document.outcome === 'unknownWorld') {
      return { outcome: 'unknownWorld' };
    }

    return document.outcome === 'diffed'
      && typeof document.cap === 'number'
      && typeof document.totalTransitions === 'number'
      && typeof document.totalTriples === 'number'
      && typeof document.truncated === 'boolean'
      && Array.isArray(document.transitions) && document.transitions.every(isWorldTransitionDto)
      ? (document as WorldsDiffDto)
      : null;
  } catch {
    // Not a JSON document: this source answers no diff.
    return null;
  }
}

/** The error outcome a world-scoped query answers on a source that carries no worlds face. */
function noWorldsFaceOutcome(): SparqlOutcome {
  return { ok: false, error: { error: 'This source carries no worlds face.', diagnostics: [] } };
}

export interface VeritasTransport {
  /** Runs a SPARQL query, returning W3C SPARQL-results-JSON or a span-bearing error. */
  runSparql(query: string, signal?: AbortSignal): Promise<SparqlOutcome>;
  /** Subscribes to the source's live trace stream (correlation-keyed, spanning query runs); returns an unsubscribe handle. Inert when the source offers no stream — see traceAvailable. */
  subscribeTrace(onEvent: (event: TraceEvent) => void): () => void;
  /** Whether this source offers a live trace stream — a capability of the first-party engines, not of the SPARQL Protocol, so a generic endpoint reads false and the trace panel disables. */
  readonly traceAvailable: boolean;
  /** Diagnoses one literal body against its datatype IRI. Null means this source carries no diagnostics face, so the editor degrades to no marks — never an error. */
  describeLiteral(datatypeIri: string, body: string): Promise<LiteralDiagnosisDto | null>;
  /** Describes the SPARQL completion context at a caret (a UTF-16 index into the query). Null means this source carries no completion face, so the editor degrades to its token heuristic — never an error. */
  describeCompletion(query: string, caret: number): Promise<CompletionContextDto | null>;
  /** Describes the completion context at a caret (a UTF-16 index) in a Turtle-family buffer; `syntax` is `trig` for TriG, otherwise Turtle. Null degrades to the token heuristic. */
  describeTurtleCompletion(source: string, caret: number, syntax: string): Promise<TurtleCompletionContextDto | null>;
  /** The fixed vocabulary corpus every editor proposes from (prefixed names plus bracketed full IRIs). Null means this source carries no vocabulary face, so the editor keeps the corpus it has. */
  editorVocabulary(): Promise<string[] | null>;
  /** Whether this source offers the worlds face — a capability of the first-party engines, not of the SPARQL Protocol, so a generic endpoint reads false and the worlds strip hides. */
  readonly worldsAvailable: boolean;
  /** Lists the source's worlds (name, state id, fork parent; the primary world first). Null means this source carries no worlds face or answered something else — the strip then presents no worlds, never an error. */
  listWorlds(): Promise<WorldDto[] | null>;
  /** Forks a world's current committed state under a new name, answering the outcome token. Null degrades exactly as listWorlds does. */
  forkWorld(source: string, name: string): Promise<WorldForkOutcomeDto | null>;
  /** Drops a world's name, answering the outcome token. Null degrades exactly as listWorlds does. */
  dropWorld(name: string): Promise<WorldDropOutcomeDto | null>;
  /** Runs a SPARQL query in a registered world, answering the same discriminated union runSparql does; a source without the worlds face answers an error outcome naming that. */
  runSparqlIn(world: string, query: string, signal?: AbortSignal): Promise<SparqlOutcome>;
  /** Commits a SPARQL Update into a registered world; a failed parse, an unknown world, and a source without the worlds face all answer the error arm. */
  updateIn(world: string, update: string): Promise<WorldUpdateOutcome>;
  /** Diffs two worlds (`from` the baseline, `to` the diffed world), answering the bounded diff document. Null degrades exactly as listWorlds does. */
  diffWorlds(from: string, to: string): Promise<WorldsDiffDto | null>;
}

/** The bridge the native shell injects onto the page (over WebMessageReceived/SendWebMessage). */
export interface VeritasBridge {
  runSparql(query: string): Promise<SparqlOutcome>;
  onTrace(handler: (event: TraceEvent) => void): () => void;
  /** Diagnoses one literal body in the host's in-process engine; null when the host answers an error reply. */
  describeLiteral(datatypeIri: string, body: string): Promise<LiteralDiagnosisDto | null>;
  /** Describes the SPARQL completion context at a caret in the host's in-process engine; null when the host answers an error reply. */
  describeCompletion(query: string, caret: number): Promise<CompletionContextDto | null>;
  /** Describes a Turtle-family completion context at a caret in the host's in-process engine; null when the host answers an error reply. */
  describeTurtleCompletion(source: string, caret: number, syntax: string): Promise<TurtleCompletionContextDto | null>;
  /** The host's fixed vocabulary corpus; null when the host answers an error reply. */
  editorVocabulary(): Promise<string[] | null>;
  /** The host's worlds listing; null when the host answers an error reply. The worlds members are optional as one block: no shipped shell carries them yet, and the transport reads the whole face as unavailable while they are absent — a shell that ships worlds ships all six. */
  listWorlds?(): Promise<WorldDto[] | null>;
  /** Forks a world in the host's in-process engine; null when the host answers an error reply. */
  forkWorld?(source: string, name: string): Promise<WorldForkOutcomeDto | null>;
  /** Drops a world in the host's in-process engine; null when the host answers an error reply. */
  dropWorld?(name: string): Promise<WorldDropOutcomeDto | null>;
  /** Runs a query in a registered world of the host's in-process engine. */
  runSparqlIn?(world: string, query: string): Promise<SparqlOutcome>;
  /** Commits an update into a registered world of the host's in-process engine. */
  updateIn?(world: string, update: string): Promise<WorldUpdateOutcome>;
  /** Diffs two worlds in the host's in-process engine; null when the host answers an error reply. */
  diffWorlds?(from: string, to: string): Promise<WorldsDiffDto | null>;
}

declare global {
  interface Window {
    /** The bridge the desktop shell injects (over the host message channel). */
    veritas?: VeritasBridge;

    /** The in-browser WASM engine, once booted; present in the no-scaffolding web deployment. */
    veritasEngine?: VeritasWasmEngine;
  }
}

/** Desktop-shell transport: forwards to the in-process engine through the host bridge. */
export class NativeVeritasTransport implements VeritasTransport {
  /** The desktop host is a first-party engine, so its tier carries the trace capability. */
  readonly traceAvailable = true;

  /** @param bridge The host bridge the requests forward to. */
  constructor(private readonly bridge: VeritasBridge) {}

  /** Runs a query through the host bridge. */
  runSparql(query: string): Promise<SparqlOutcome> {
    return this.bridge.runSparql(query);
  }

  /** Subscribes to the engine trace stream through the host bridge. */
  subscribeTrace(onEvent: (event: TraceEvent) => void): () => void {
    return this.bridge.onTrace(onEvent);
  }

  /**
   * Diagnoses one literal body through the host bridge. Null when the host answers an error reply, when its
   * channel faults, or when the injected bridge carries no such method — the editor then paints no marks
   * rather than surfacing a failure.
   * @param datatypeIri The literal's datatype IRI.
   * @param body The literal's value (escapes already resolved).
   * @returns The diagnosis, or null.
   */
  async describeLiteral(datatypeIri: string, body: string): Promise<LiteralDiagnosisDto | null> {
    if (typeof this.bridge.describeLiteral !== 'function') {
      return null;
    }

    try {
      return await this.bridge.describeLiteral(datatypeIri, body);
    } catch {
      // The host channel faulted: no diagnosis for this literal.
      return null;
    }
  }

  /**
   * Describes the SPARQL completion context at a caret through the host bridge; null on an error reply, a
   * faulted channel, or a bridge without the method, so the editor degrades to its token heuristic.
   * @param query The buffer's full text.
   * @param caret The caret's UTF-16 index into that text.
   * @returns The context, or null.
   */
  async describeCompletion(query: string, caret: number): Promise<CompletionContextDto | null> {
    if (typeof this.bridge.describeCompletion !== 'function') {
      return null;
    }

    try {
      return await this.bridge.describeCompletion(query, caret);
    } catch {
      // The host channel faulted: no parser-driven proposals for this position.
      return null;
    }
  }

  /**
   * Describes a Turtle-family completion context at a caret through the host bridge; null on an error reply,
   * a faulted channel, or a bridge without the method.
   * @param source The buffer's full text.
   * @param caret The caret's UTF-16 index into that text.
   * @param syntax The grammar flavour: `trig` for TriG, otherwise Turtle.
   * @returns The context, or null.
   */
  async describeTurtleCompletion(source: string, caret: number, syntax: string): Promise<TurtleCompletionContextDto | null> {
    if (typeof this.bridge.describeTurtleCompletion !== 'function') {
      return null;
    }

    try {
      return await this.bridge.describeTurtleCompletion(source, caret, syntax);
    } catch {
      // The host channel faulted: no parser-driven proposals for this position.
      return null;
    }
  }

  /**
   * Reads the host's fixed vocabulary corpus through the bridge; null on an error reply, a faulted channel,
   * or a bridge without the method, so the editor keeps the corpus it has.
   * @returns The corpus, or null.
   */
  async editorVocabulary(): Promise<string[] | null> {
    if (typeof this.bridge.editorVocabulary !== 'function') {
      return null;
    }

    try {
      return await this.bridge.editorVocabulary();
    } catch {
      // The host channel faulted: this source contributes no corpus.
      return null;
    }
  }

  /** Whether the injected bridge carries the worlds face; a shell that predates it reads false, so the worlds strip hides until a shell ships the members. */
  get worldsAvailable(): boolean {
    return typeof this.bridge.listWorlds === 'function';
  }

  /**
   * Lists the host engine's worlds through the bridge; null on an error reply, a faulted channel, or a
   * bridge without the face, so the strip presents no worlds rather than surfacing a failure.
   * @returns The worlds, or null.
   */
  async listWorlds(): Promise<WorldDto[] | null> {
    if (typeof this.bridge.listWorlds !== 'function') {
      return null;
    }

    try {
      return await this.bridge.listWorlds();
    } catch {
      // The host channel faulted: this source answers no worlds listing.
      return null;
    }
  }

  /**
   * Forks a world through the bridge; null on an error reply, a faulted channel, or a bridge without
   * the face.
   * @param source The world to fork from.
   * @param name The new world's name.
   * @returns The outcome token, or null.
   */
  async forkWorld(source: string, name: string): Promise<WorldForkOutcomeDto | null> {
    if (typeof this.bridge.forkWorld !== 'function') {
      return null;
    }

    try {
      return await this.bridge.forkWorld(source, name);
    } catch {
      // The host channel faulted: this source answers no fork outcome.
      return null;
    }
  }

  /**
   * Drops a world through the bridge; null on an error reply, a faulted channel, or a bridge without
   * the face.
   * @param name The world's name.
   * @returns The outcome token, or null.
   */
  async dropWorld(name: string): Promise<WorldDropOutcomeDto | null> {
    if (typeof this.bridge.dropWorld !== 'function') {
      return null;
    }

    try {
      return await this.bridge.dropWorld(name);
    } catch {
      // The host channel faulted: this source answers no drop outcome.
      return null;
    }
  }

  /**
   * Runs a query in a registered world through the bridge; a bridge without the face and a faulted
   * channel both answer an error outcome, honouring the seam's never-throw contract.
   * @param world The registered world the query runs in.
   * @param query The query text.
   * @returns The query outcome.
   */
  async runSparqlIn(world: string, query: string): Promise<SparqlOutcome> {
    if (typeof this.bridge.runSparqlIn !== 'function') {
      return noWorldsFaceOutcome();
    }

    try {
      return await this.bridge.runSparqlIn(world, query);
    } catch (error) {
      return { ok: false, error: { error: `The host channel faulted: ${String(error)}`, diagnostics: [] } };
    }
  }

  /**
   * Commits an update into a registered world through the bridge; a bridge without the face and a
   * faulted channel both answer the error arm.
   * @param world The registered world the update commits into.
   * @param update The update text.
   * @returns The update outcome.
   */
  async updateIn(world: string, update: string): Promise<WorldUpdateOutcome> {
    if (typeof this.bridge.updateIn !== 'function') {
      return { ok: false, error: 'This source carries no worlds face.' };
    }

    try {
      return await this.bridge.updateIn(world, update);
    } catch (error) {
      return { ok: false, error: `The host channel faulted: ${String(error)}` };
    }
  }

  /**
   * Diffs two worlds through the bridge; null on an error reply, a faulted channel, or a bridge
   * without the face.
   * @param from The baseline world.
   * @param to The diffed world.
   * @returns The diff document, or null.
   */
  async diffWorlds(from: string, to: string): Promise<WorldsDiffDto | null> {
    if (typeof this.bridge.diffWorlds !== 'function') {
      return null;
    }

    try {
      return await this.bridge.diffWorlds(from, to);
    } catch {
      // The host channel faulted: this source answers no diff.
      return null;
    }
  }
}

/** The leading slice of an arbitrary response body, bounded so an endpoint's error page never floods the error view. */
function bodySnippet(text: string): string {
  const flattened = text.trim().replace(/\s+/g, ' ');

  return flattened.length > 160 ? `${flattened.slice(0, 160)}…` : flattened;
}

/** The endpoints and injection points an {@link HttpVeritasTransport} is built from; every field has a this-origin default. */
export interface HttpVeritasTransportOptions {
  /** The SPARQL Protocol query endpoint the queries POST to; the default is this deployment's own `sparql` path, base-relative so a subpath deployment resolves it inside its base. */
  readonly endpoint?: string;

  /** The Server-Sent-Events trace endpoint, or null when the endpoint offers no trace stream — every user-entered custom endpoint, since trace is a capability of the first-party hosts, not of the SPARQL Protocol. */
  readonly traceEndpoint?: string | null;

  /** The literal-diagnostics endpoint, or null when the endpoint offers no diagnostics face — again every custom endpoint, the face being a first-party one. */
  readonly literalDiagnosticsEndpoint?: string | null;

  /** The SPARQL completion endpoint, or null when the endpoint offers no completion face — every custom endpoint, the face being a first-party one. */
  readonly completionEndpoint?: string | null;

  /** The Turtle-family completion endpoint, or null when the endpoint offers no such face. */
  readonly turtleCompletionEndpoint?: string | null;

  /** The editor-vocabulary endpoint, or null when the endpoint offers no vocabulary face. */
  readonly editorVocabularyEndpoint?: string | null;

  /** The worlds endpoint (the listing route; the fork, drop, query, update, and diff routes live under it), or null when the endpoint offers no worlds face — every custom endpoint, the face being a first-party one. */
  readonly worldsEndpoint?: string | null;

  /** The fetch used for requests; injectable so tests pass a canned delegate. */
  readonly fetchImpl?: FetchLike;
}

/** Browser/dev transport: the SPARQL Protocol against this origin's CLI server or any conformant endpoint, results as SPARQL-JSON. */
export class HttpVeritasTransport implements VeritasTransport {
  /** The SPARQL Protocol query endpoint the queries POST to. */
  private readonly endpoint: string;

  /** The Server-Sent-Events trace endpoint, or null when this endpoint offers no trace stream. */
  private readonly traceEndpoint: string | null;

  /** The literal-diagnostics endpoint, or null when this endpoint offers no diagnostics face. */
  private readonly literalDiagnosticsEndpoint: string | null;

  /** The SPARQL completion endpoint, or null when this endpoint offers no completion face. */
  private readonly completionEndpoint: string | null;

  /** The Turtle-family completion endpoint, or null when this endpoint offers no such face. */
  private readonly turtleCompletionEndpoint: string | null;

  /** The editor-vocabulary endpoint, or null when this endpoint offers no vocabulary face. */
  private readonly editorVocabularyEndpoint: string | null;

  /** The worlds endpoint, or null when this endpoint offers no worlds face. */
  private readonly worldsEndpoint: string | null;

  /**
   * The fetch used for requests. The default is the global fetch bound to the global: invoked as a method
   * (`this.fetchImpl(...)`) the browser's native fetch rejects a non-global receiver ("Illegal
   * invocation"), so the binding is required.
   */
  private readonly fetchImpl: FetchLike;

  /**
   * @param options The endpoints and the fetch to use; each omitted field takes this deployment's own
   *   base-relative default. A field given as null is honoured as "this endpoint has no such face" — the
   *   shape a custom endpoint attaches with — so the defaults never creep back in.
   */
  constructor(options: HttpVeritasTransportOptions = {}) {
    this.endpoint = options.endpoint ?? `${import.meta.env.BASE_URL}sparql`;
    this.traceEndpoint = options.traceEndpoint !== undefined ? options.traceEndpoint : `${import.meta.env.BASE_URL}trace`;
    this.literalDiagnosticsEndpoint = options.literalDiagnosticsEndpoint !== undefined
      ? options.literalDiagnosticsEndpoint
      : `${import.meta.env.BASE_URL}literal-diagnostics`;
    this.completionEndpoint = options.completionEndpoint !== undefined
      ? options.completionEndpoint
      : `${import.meta.env.BASE_URL}completion`;
    this.turtleCompletionEndpoint = options.turtleCompletionEndpoint !== undefined
      ? options.turtleCompletionEndpoint
      : `${import.meta.env.BASE_URL}turtle-completion`;
    this.editorVocabularyEndpoint = options.editorVocabularyEndpoint !== undefined
      ? options.editorVocabularyEndpoint
      : `${import.meta.env.BASE_URL}editor-vocabulary`;
    this.worldsEndpoint = options.worldsEndpoint !== undefined
      ? options.worldsEndpoint
      : `${import.meta.env.BASE_URL}worlds`;
    this.fetchImpl = options.fetchImpl ?? globalThis.fetch.bind(globalThis);
  }

  /**
   * Runs a query over the SPARQL Protocol, returning SPARQL-results-JSON or an error outcome — never a thrown
   * exception, honouring the seam's discriminated-union contract. The engine's own failure document is
   * `{"error":…}` with optional span-bearing diagnostics; an arbitrary endpoint answers whatever it likes
   * (HTML, plain text, an empty body), so the error path degrades: the parsed engine document when the body
   * is one, otherwise the HTTP status line with a bounded body snippet, and a transport-level failure (a
   * network fault, a CORS block, an abort) becomes an error outcome naming it.
   */
  async runSparql(query: string, signal?: AbortSignal): Promise<SparqlOutcome> {
    let response: Response;
    let text: string;
    try {
      response = await this.fetchImpl(this.endpoint, {
        method: 'POST',
        headers: { 'content-type': 'application/sparql-query', accept: 'application/sparql-results+json' },
        body: query,
        signal
      });
      text = await response.text();
    } catch (error) {
      return { ok: false, error: { error: `The endpoint could not be reached: ${String(error)}`, diagnostics: [] } };
    }

    return HttpVeritasTransport.sparqlOutcomeFrom(response, text);
  }

  /**
   * Decodes a query response body into the seam's outcome — shared by the protocol route and the
   * world-scoped route, which answer the same shapes: parsed SPARQL-results-JSON on OK, the engine's
   * `{"error":…}` document on a failure that carries one, otherwise the HTTP status line with a
   * bounded body snippet.
   * @param response The HTTP response.
   * @param text The response body.
   * @returns The query outcome.
   */
  private static sparqlOutcomeFrom(response: Response, text: string): SparqlOutcome {
    if (response.ok) {
      try {
        return { ok: true, results: JSON.parse(text) as SparqlResultsDocument };
      } catch {
        return { ok: false, error: { error: `The endpoint answered with a body that is not SPARQL-results-JSON: ${bodySnippet(text)}`, diagnostics: [] } };
      }
    }

    try {
      const parsed = JSON.parse(text) as Partial<SparqlQueryError>;
      if (typeof parsed.error === 'string') {
        // Array.isArray, not ??: an arbitrary endpoint may put any JSON value in a diagnostics field, and
        // the renderer maps over the array unconditionally — a wrong-type truthy value must not flow through.
        return { ok: false, error: { error: parsed.error, diagnostics: Array.isArray(parsed.diagnostics) ? parsed.diagnostics : [] } };
      }
    } catch {
      // Not a JSON body — the status-line error below carries the snippet instead.
    }

    return { ok: false, error: { error: `The endpoint answered HTTP ${response.status}${text.length > 0 ? `: ${bodySnippet(text)}` : ''}`, diagnostics: [] } };
  }

  /** Whether this endpoint carries the first-party trace stream; false for every user-entered custom endpoint. */
  get traceAvailable(): boolean {
    return this.traceEndpoint !== null;
  }

  /**
   * Diagnoses one literal body over the diagnostics route (POST JSON, JSON back). Null on every path that
   * is not a diagnosis document — no such endpoint on this source, a transport fault, a non-OK status, an
   * unparseable body, or a body whose status is not one of the four — so the editor degrades to no marks.
   * @param datatypeIri The literal's datatype IRI.
   * @param body The literal's value (escapes already resolved).
   * @returns The diagnosis, or null.
   */
  async describeLiteral(datatypeIri: string, body: string): Promise<LiteralDiagnosisDto | null> {
    if (this.literalDiagnosticsEndpoint === null) {
      return null;
    }

    try {
      const response = await this.fetchImpl(this.literalDiagnosticsEndpoint, {
        method: 'POST',
        headers: { 'content-type': 'application/json', accept: 'application/json' },
        body: JSON.stringify({ datatype: datatypeIri, body })
      });
      if (!response.ok) {
        return null;
      }

      const diagnosis = JSON.parse(await response.text()) as Partial<LiteralDiagnosisDto>;

      return diagnosis.status === 'unsupported' || diagnosis.status === 'valid'
        || diagnosis.status === 'warning' || diagnosis.status === 'invalid'
        ? (diagnosis as LiteralDiagnosisDto)
        : null;
    } catch {
      // Unreachable endpoint, CORS block, or a body that is not JSON: no diagnosis for this literal.
      return null;
    }
  }

  /**
   * Reads one of the first-party faces as text. Null on every path that is not an answer — this endpoint
   * carries no such face, the request faulted (unreachable, CORS), or the status is not OK — so each caller
   * degrades value-based on one signal.
   * @param endpoint The face's endpoint, or null when this source carries none.
   * @param request The JSON request body to POST, or null to GET the face.
   * @returns The response text, or null.
   */
  private async faceText(endpoint: string | null, request: string | null): Promise<string | null> {
    if (endpoint === null) {
      return null;
    }

    const init: RequestInit = request === null
      ? { headers: { accept: 'application/json' } }
      : { method: 'POST', headers: { 'content-type': 'application/json', accept: 'application/json' }, body: request };

    try {
      const response = await this.fetchImpl(endpoint, init);

      return response.ok ? await response.text() : null;
    } catch {
      // Unreachable endpoint or a CORS block: this source answers nothing for the face.
      return null;
    }
  }

  /**
   * Describes the SPARQL completion context at a caret over the completion route (POST JSON, JSON back).
   * Null when this source carries no completion face or answers anything but a completion document.
   * @param query The buffer's full text.
   * @param caret The caret's UTF-16 index into that text.
   * @returns The context, or null.
   */
  async describeCompletion(query: string, caret: number): Promise<CompletionContextDto | null> {
    const answer = await this.faceText(this.completionEndpoint, JSON.stringify({ query, caret }));

    return answer !== null ? readCompletionContext(answer) : null;
  }

  /**
   * Describes a Turtle-family completion context at a caret over the Turtle completion route. Null when this
   * source carries no such face or answers anything but a completion document.
   * @param source The buffer's full text.
   * @param caret The caret's UTF-16 index into that text.
   * @param syntax The grammar flavour: `trig` for TriG, otherwise Turtle.
   * @returns The context, or null.
   */
  async describeTurtleCompletion(source: string, caret: number, syntax: string): Promise<TurtleCompletionContextDto | null> {
    const answer = await this.faceText(this.turtleCompletionEndpoint, JSON.stringify({ source, caret, syntax }));

    return answer !== null ? readTurtleCompletionContext(answer) : null;
  }

  /**
   * Reads the fixed vocabulary corpus over the editor-vocabulary route (GET, JSON array back). Null when
   * this source carries no vocabulary face or answers anything but an array of terms.
   * @returns The corpus, or null.
   */
  async editorVocabulary(): Promise<string[] | null> {
    const answer = await this.faceText(this.editorVocabularyEndpoint, null);

    return answer !== null ? readEditorVocabulary(answer) : null;
  }

  /** Whether this endpoint carries the first-party worlds face; false for every user-entered custom endpoint, so the worlds strip hides there. */
  get worldsAvailable(): boolean {
    return this.worldsEndpoint !== null;
  }

  /**
   * Lists the source's worlds over the worlds route (GET, JSON back). Null on every path that is not a
   * worlds listing — no worlds face on this source, a transport fault, a non-OK status, or a body that
   * is not the listing document — so the strip presents no worlds rather than surfacing a failure.
   * @returns The worlds, or null.
   */
  async listWorlds(): Promise<WorldDto[] | null> {
    const answer = await this.faceText(this.worldsEndpoint, null);

    return answer !== null ? readWorldsList(answer) : null;
  }

  /**
   * Forks a world over the fork route (POST JSON, the outcome document back). Null on every path that
   * is not an outcome, exactly as listWorlds degrades.
   * @param source The world to fork from.
   * @param name The new world's name.
   * @returns The outcome token, or null.
   */
  async forkWorld(source: string, name: string): Promise<WorldForkOutcomeDto | null> {
    const answer = await this.faceText(this.worldsEndpoint === null ? null : `${this.worldsEndpoint}/fork`, JSON.stringify({ source, name }));

    return answer !== null ? readForkOutcome(answer) : null;
  }

  /**
   * Drops a world over the drop route (POST JSON, the outcome document back). Null on every path that
   * is not an outcome, exactly as listWorlds degrades.
   * @param name The world's name.
   * @returns The outcome token, or null.
   */
  async dropWorld(name: string): Promise<WorldDropOutcomeDto | null> {
    const answer = await this.faceText(this.worldsEndpoint === null ? null : `${this.worldsEndpoint}/drop`, JSON.stringify({ world: name }));

    return answer !== null ? readDropOutcome(answer) : null;
  }

  /**
   * Runs a query in a registered world over the world-scoped query route (POST JSON, the same response
   * shapes the protocol route answers). A source without the worlds face answers an error outcome
   * naming that; every other failure degrades exactly as runSparql does.
   * @param world The registered world the query runs in.
   * @param query The query text.
   * @param signal An abort signal for the request.
   * @returns The query outcome.
   */
  async runSparqlIn(world: string, query: string, signal?: AbortSignal): Promise<SparqlOutcome> {
    if (this.worldsEndpoint === null) {
      return noWorldsFaceOutcome();
    }

    let response: Response;
    let text: string;
    try {
      response = await this.fetchImpl(`${this.worldsEndpoint}/query`, {
        method: 'POST',
        headers: { 'content-type': 'application/json', accept: 'application/sparql-results+json' },
        body: JSON.stringify({ world, query }),
        signal
      });
      text = await response.text();
    } catch (error) {
      return { ok: false, error: { error: `The endpoint could not be reached: ${String(error)}`, diagnostics: [] } };
    }

    return HttpVeritasTransport.sparqlOutcomeFrom(response, text);
  }

  /**
   * Commits an update into a registered world over the world-scoped update route (POST JSON, the
   * acknowledgement document back). The error arm carries the engine's own failure message when the
   * body is its `{"error":…}` document, otherwise the HTTP status line with a bounded snippet; a
   * source without the worlds face and a transport fault answer it too.
   * @param world The registered world the update commits into.
   * @param update The update text.
   * @returns The update outcome.
   */
  async updateIn(world: string, update: string): Promise<WorldUpdateOutcome> {
    if (this.worldsEndpoint === null) {
      return { ok: false, error: 'This source carries no worlds face.' };
    }

    let response: Response;
    let text: string;
    try {
      response = await this.fetchImpl(`${this.worldsEndpoint}/update`, {
        method: 'POST',
        headers: { 'content-type': 'application/json', accept: 'application/json' },
        body: JSON.stringify({ world, update })
      });
      text = await response.text();
    } catch (error) {
      return { ok: false, error: `The endpoint could not be reached: ${String(error)}` };
    }

    if (response.ok) {
      return { ok: true };
    }

    try {
      const parsed = JSON.parse(text) as { error?: unknown };
      if (typeof parsed.error === 'string') {
        return { ok: false, error: parsed.error };
      }
    } catch {
      // Not a JSON body — the status-line error below carries the snippet instead.
    }

    return { ok: false, error: `The endpoint answered HTTP ${response.status}${text.length > 0 ? `: ${bodySnippet(text)}` : ''}` };
  }

  /**
   * Diffs two worlds over the diff route (GET with `from` and `to` parameters, the bounded diff
   * document back). Null on every path that is not a diff document, exactly as listWorlds degrades.
   * @param from The baseline world.
   * @param to The diffed world.
   * @returns The diff document, or null.
   */
  async diffWorlds(from: string, to: string): Promise<WorldsDiffDto | null> {
    const answer = await this.faceText(
      this.worldsEndpoint === null ? null : `${this.worldsEndpoint}/diff?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
      null
    );

    return answer !== null ? readWorldsDiff(answer) : null;
  }

  /** Subscribes to the engine trace stream, projected as Server-Sent Events; inert when this endpoint has no trace stream. */
  subscribeTrace(onEvent: (event: TraceEvent) => void): () => void {
    if (this.traceEndpoint === null) {
      return () => undefined;
    }

    const source = new EventSource(this.traceEndpoint);
    const listener = (message: MessageEvent<string>): void => onEvent(JSON.parse(message.data) as TraceEvent);
    source.addEventListener('trace', listener as EventListener);

    return () => source.close();
  }
}

/**
 * The engine compiled to WebAssembly, running fully client-side — no .NET host, no server. It exposes a
 * JS-interop surface (the WASM module's exported methods) over the dataset the page loads into it.
 * Booted lazily on the page's main thread; this adapts its surface to the transport seam.
 */
export interface VeritasWasmEngine {
  runSparql(query: string): Promise<string>;
  /** Loads (replacing) a Turtle dataset into the engine, so subsequent queries run over it. */
  loadTurtle(turtle: string): Promise<void>;
  /** Validates a world's dataset (null for the primary world) against a SHACL shapes graph (Turtle); returns the report JSON. */
  validateShacl(shapes: string, world: string | null): Promise<string>;
  /** Describes the parser-driven completion context at a caret (a UTF-16 index into the query); returns the context JSON. Store-free, so it needs no loaded dataset. */
  describeCompletion(query: string, caretOffset: number): Promise<string>;
  /** Returns the fixed vocabulary corpus — the prefixed-name rosters plus the bracketed full IRI of every registered datatype no roster covers — as a JSON array of terms, for completing a Turtle / SHACL / OWL buffer. */
  editorVocabulary(): Promise<string>;
  /** Describes the parser-driven completion context at a caret (a UTF-16 index) in a Turtle / SHACL / TriG buffer; `syntax` is `trig` for TriG, otherwise Turtle. Returns the context JSON. Store-free. */
  describeTurtleCompletion(source: string, caretOffset: number, syntax: string): Promise<string>;
  /** Diagnoses one geometry literal body against its datatype IRI; returns the diagnosis JSON (the four-state document). Store-free. */
  describeGeoLiteral(datatypeIri: string, body: string): Promise<string>;
  /** Lists the engine's worlds; returns the worlds listing JSON (name, state id, fork parent; the primary world first). */
  listWorldsJson(): Promise<string>;
  /** Forks a world's current committed state under a new name; returns the fork outcome JSON. */
  forkWorld(source: string, name: string): Promise<string>;
  /** Drops a world's name; returns the drop outcome JSON. */
  dropWorld(name: string): Promise<string>;
  /** Runs a query in a registered world; returns the same SPARQL-results-JSON or `{"error":…}` document runSparql returns. */
  runSparqlIn(world: string, query: string): Promise<string>;
  /** Commits an update into a registered world; returns the acknowledgement JSON or the `{"error":…}` document. */
  runUpdateIn(world: string, update: string): Promise<string>;
  /** Diffs two worlds (`from` the baseline, `to` the diffed world); returns the bounded diff JSON. */
  diffWorldsJson(from: string, to: string): Promise<string>;
  onTrace(handler: (event: TraceEvent) => void): () => void;
}

/** Browser transport over the in-browser WASM engine; results are the engine's SPARQL-results-JSON. */
export class WasmVeritasTransport implements VeritasTransport {
  /** The in-browser engine is "the server" of its tier and bridges its trace to the page. */
  readonly traceAvailable = true;

  /** @param engine The booted in-browser WASM engine surface. */
  constructor(private readonly engine: VeritasWasmEngine) {}

  /** Runs a query on the in-browser engine and decodes its SPARQL-results-JSON, or its `{"error":…}` failure document. */
  async runSparql(query: string): Promise<SparqlOutcome> {
    return WasmVeritasTransport.outcomeFromDocument(await this.engine.runSparql(query));
  }

  /**
   * Decodes an engine answer into the seam's outcome — shared by the primary and world-scoped query
   * faces, which answer the same documents.
   * @param json The engine's document text.
   * @returns The query outcome.
   */
  private static outcomeFromDocument(json: string): SparqlOutcome {
    const document = JSON.parse(json) as SparqlResultsDocument | Partial<SparqlQueryError>;

    if (typeof (document as Partial<SparqlQueryError>).error === 'string') {
      const failure = document as Partial<SparqlQueryError>;
      // Array.isArray, exactly as the HTTP transport normalizes: the engine's failure document may omit
      // diagnostics entirely, and the renderer maps over the array unconditionally — an undefined or
      // wrong-type value must not flow through.
      return { ok: false, error: { error: failure.error as string, diagnostics: Array.isArray(failure.diagnostics) ? failure.diagnostics : [] } };
    }

    return { ok: true, results: document as SparqlResultsDocument };
  }

  /**
   * Diagnoses one literal body on the in-browser engine's diagnostics face, decoding its four-state
   * document. Null when the face is unreachable or answers a body that is not a diagnosis, so the editor
   * degrades to no marks rather than surfacing an error.
   * @param datatypeIri The literal's datatype IRI.
   * @param body The literal's value (escapes already resolved).
   * @returns The diagnosis, or null.
   */
  async describeLiteral(datatypeIri: string, body: string): Promise<LiteralDiagnosisDto | null> {
    try {
      const diagnosis = JSON.parse(await this.engine.describeGeoLiteral(datatypeIri, body)) as Partial<LiteralDiagnosisDto>;

      return diagnosis.status === 'unsupported' || diagnosis.status === 'valid'
        || diagnosis.status === 'warning' || diagnosis.status === 'invalid'
        ? (diagnosis as LiteralDiagnosisDto)
        : null;
    } catch {
      // The engine surface carries no diagnostics face, or answered a body that is not JSON.
      return null;
    }
  }

  /**
   * Describes the SPARQL completion context at a caret on the in-browser engine, decoding its completion
   * document. Null when the face is unreachable or answers a body that is not a context, so the editor
   * degrades to its token heuristic.
   * @param query The buffer's full text.
   * @param caret The caret's UTF-16 index into that text.
   * @returns The context, or null.
   */
  async describeCompletion(query: string, caret: number): Promise<CompletionContextDto | null> {
    try {
      return readCompletionContext(await this.engine.describeCompletion(query, caret));
    } catch {
      // The engine surface carries no completion face, or the call faulted.
      return null;
    }
  }

  /**
   * Describes a Turtle-family completion context at a caret on the in-browser engine. Null when the face is
   * unreachable or answers a body that is not a context.
   * @param source The buffer's full text.
   * @param caret The caret's UTF-16 index into that text.
   * @param syntax The grammar flavour: `trig` for TriG, otherwise Turtle.
   * @returns The context, or null.
   */
  async describeTurtleCompletion(source: string, caret: number, syntax: string): Promise<TurtleCompletionContextDto | null> {
    try {
      return readTurtleCompletionContext(await this.engine.describeTurtleCompletion(source, caret, syntax));
    } catch {
      // The engine surface carries no Turtle completion face, or the call faulted.
      return null;
    }
  }

  /**
   * Reads the in-browser engine's fixed vocabulary corpus. Null when the face is unreachable or answers a
   * body that is not an array of terms, so the editor keeps the corpus it has.
   * @returns The corpus, or null.
   */
  async editorVocabulary(): Promise<string[] | null> {
    try {
      return readEditorVocabulary(await this.engine.editorVocabulary());
    } catch {
      // The engine surface carries no vocabulary face, or the call faulted.
      return null;
    }
  }

  /** Whether the booted engine surface carries the worlds face; a stale cached surface without it reads false, so the worlds strip hides rather than faulting. */
  get worldsAvailable(): boolean {
    return typeof this.engine.listWorldsJson === 'function';
  }

  /**
   * Lists the in-browser engine's worlds, decoding the listing document. Null when the surface carries
   * no worlds face, the call faults, or the body is not a listing, so the strip presents no worlds.
   * @returns The worlds, or null.
   */
  async listWorlds(): Promise<WorldDto[] | null> {
    if (typeof this.engine.listWorldsJson !== 'function') {
      return null;
    }

    try {
      return readWorldsList(await this.engine.listWorldsJson());
    } catch {
      // The engine surface faulted: this source answers no worlds listing.
      return null;
    }
  }

  /**
   * Forks a world on the in-browser engine, decoding the outcome document. Null degrades exactly as
   * listWorlds does.
   * @param source The world to fork from.
   * @param name The new world's name.
   * @returns The outcome token, or null.
   */
  async forkWorld(source: string, name: string): Promise<WorldForkOutcomeDto | null> {
    if (typeof this.engine.forkWorld !== 'function') {
      return null;
    }

    try {
      return readForkOutcome(await this.engine.forkWorld(source, name));
    } catch {
      // The engine surface faulted: this source answers no fork outcome.
      return null;
    }
  }

  /**
   * Drops a world on the in-browser engine, decoding the outcome document. Null degrades exactly as
   * listWorlds does.
   * @param name The world's name.
   * @returns The outcome token, or null.
   */
  async dropWorld(name: string): Promise<WorldDropOutcomeDto | null> {
    if (typeof this.engine.dropWorld !== 'function') {
      return null;
    }

    try {
      return readDropOutcome(await this.engine.dropWorld(name));
    } catch {
      // The engine surface faulted: this source answers no drop outcome.
      return null;
    }
  }

  /**
   * Runs a query in a registered world on the in-browser engine, decoding the same documents runSparql
   * decodes; a surface without the worlds face and a faulted call both answer an error outcome.
   * @param world The registered world the query runs in.
   * @param query The query text.
   * @returns The query outcome.
   */
  async runSparqlIn(world: string, query: string): Promise<SparqlOutcome> {
    if (typeof this.engine.runSparqlIn !== 'function') {
      return noWorldsFaceOutcome();
    }

    try {
      return WasmVeritasTransport.outcomeFromDocument(await this.engine.runSparqlIn(world, query));
    } catch (error) {
      return { ok: false, error: { error: `The engine surface faulted: ${String(error)}`, diagnostics: [] } };
    }
  }

  /**
   * Commits an update into a registered world on the in-browser engine; the error arm carries the
   * engine's own failure message from its `{"error":…}` document.
   * @param world The registered world the update commits into.
   * @param update The update text.
   * @returns The update outcome.
   */
  async updateIn(world: string, update: string): Promise<WorldUpdateOutcome> {
    if (typeof this.engine.runUpdateIn !== 'function') {
      return { ok: false, error: 'This source carries no worlds face.' };
    }

    try {
      const document = JSON.parse(await this.engine.runUpdateIn(world, update)) as { outcome?: unknown; error?: unknown };
      if (document.outcome === 'updated') {
        return { ok: true };
      }

      return { ok: false, error: typeof document.error === 'string' ? document.error : 'The engine answered a body that is not an update outcome.' };
    } catch (error) {
      return { ok: false, error: `The engine surface faulted: ${String(error)}` };
    }
  }

  /**
   * Diffs two worlds on the in-browser engine, decoding the bounded diff document. Null degrades
   * exactly as listWorlds does.
   * @param from The baseline world.
   * @param to The diffed world.
   * @returns The diff document, or null.
   */
  async diffWorlds(from: string, to: string): Promise<WorldsDiffDto | null> {
    if (typeof this.engine.diffWorldsJson !== 'function') {
      return null;
    }

    try {
      return readWorldsDiff(await this.engine.diffWorldsJson(from, to));
    } catch {
      // The engine surface faulted: this source answers no diff.
      return null;
    }
  }

  /** Subscribes to the in-browser engine trace stream. */
  subscribeTrace(onEvent: (event: TraceEvent) => void): () => void {
    return this.engine.onTrace(onEvent);
  }
}

/**
 * Picks the transport at boot: the native bridge when the desktop shell injected it; otherwise the
 * in-browser WASM engine once it is booted (`window.veritasEngine`); otherwise the HTTP front door to
 * the CLI server. One seam, three deployments — desktop, no-scaffolding web, and dev/server — with the
 * same app code above the line.
 */
export function resolveTransport(): VeritasTransport {
  if (window.veritas !== undefined) {
    return new NativeVeritasTransport(window.veritas);
  }

  if (window.veritasEngine !== undefined) {
    return new WasmVeritasTransport(window.veritasEngine);
  }

  return new HttpVeritasTransport();
}

/**
 * Probes the origin for a CLI-served, server-side engine. The CLI answers `GET /config` with the marker
 * `{"engine":"http"}` — read as "a server engine is answering here, keep the HTTP transport". A static
 * host (GitHub Pages / offline) has no such endpoint: a 404, an SPA-fallback HTML page, a non-JSON body,
 * or a network/timeout error are all read as "no server engine", so the caller boots the in-browser WASM
 * engine instead. This is the one-build model-A/model-B discriminator; it never throws (any failure is
 * a negative answer). A blunt build-mode check cannot stand in for it: the CLI-served page is also a
 * production build, and a Pages SPA fallback can make a `/sparql` probe a false positive.
 * @param fetchImpl The fetch used for the probe; injectable so tests pass a canned delegate.
 * @returns Whether a server-side engine is reachable over the HTTP transport at this origin.
 */
export async function serverEngineAvailable(fetchImpl: FetchLike = globalThis.fetch.bind(globalThis)): Promise<boolean> {
  try {
    // Base-relative (import.meta.env.BASE_URL ends in '/') so the probe targets this deployment's origin+base:
    // a subpath GitHub Pages project page serves the app — and any /config — under e.g. /Repo/, not the apex.
    const response = await fetchImpl(`${import.meta.env.BASE_URL}config`, { headers: { accept: 'application/json' }, signal: AbortSignal.timeout(4_000) });
    if (!response.ok || !(response.headers.get('content-type') ?? '').includes('application/json')) {
      return false;
    }

    const config = JSON.parse(await response.text()) as { engine?: string };

    return config.engine === 'http';
  } catch {
    // A 404, an HTML SPA fallback, an unparseable body, or a network/timeout error: no server engine here.
    return false;
  }
}
