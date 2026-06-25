// SPARQL intellisense proposals: keywords, the buffer's in-scope prefixes, and the loaded dataset's
// vocabulary (predicates + classes), fed by the engine for the vocabulary. No parser dependency yet — a
// light token + context heuristic drives the proposals; the incremental parser refines this later.
import type { VeritasTransport } from './services/veritas-transport';

/** A completion proposal shown in the popup. */
export interface Completion {
  /** The text inserted when the proposal is accepted. */
  readonly insert: string;

  /** The label shown in the popup (usually the same as the inserted text). */
  readonly label: string;

  /** A short kind tag: keyword / prefix / predicate / class. */
  readonly kind: string;
}

/** The loaded dataset's vocabulary, abbreviated against the buffer's prefixes. */
export interface VocabularyView {
  /** The predicate terms in the data (abbreviated or `<iri>`). */
  readonly predicates: readonly string[];

  /** The class terms in the data (objects of `rdf:type`). */
  readonly classes: readonly string[];
}

/** The SPARQL keywords proposed at a clause boundary. */
export const SPARQL_KEYWORDS: readonly string[] = [
  'SELECT', 'WHERE', 'PREFIX', 'FILTER', 'OPTIONAL', 'UNION', 'SERVICE', 'BIND', 'ORDER BY', 'GROUP BY',
  'LIMIT', 'OFFSET', 'DISTINCT', 'ASK', 'CONSTRUCT', 'DESCRIBE', 'FROM', 'GRAPH', 'VALUES', 'HAVING', 'MINUS'
];

/** The partial token immediately before the caret (the word being typed), or an empty string when none. */
export function tokenBefore(textBeforeCaret: string): string {
  const match = textBeforeCaret.match(/[A-Za-z_:?][\w:?-]*$/);

  return match !== null ? match[0] : '';
}

/** Parses the buffer's PREFIX declarations into a prefix → namespace-IRI map. */
export function parsePrefixes(text: string): Map<string, string> {
  const prefixes = new Map<string, string>();
  const declaration = /PREFIX\s+([A-Za-z][\w-]*):\s*<([^>]*)>/gi;
  for (let match = declaration.exec(text); match !== null; match = declaration.exec(text)) {
    prefixes.set(match[1], match[2]);
  }

  return prefixes;
}

/** Abbreviates an IRI against the prefixes (`ns:local`), or wraps it as `<iri>` when no prefix matches. */
function abbreviate(iri: string, prefixes: Map<string, string>): string {
  for (const [prefix, namespace] of prefixes) {
    if (namespace.length > 0 && iri.startsWith(namespace)) {
      return `${prefix}:${iri.slice(namespace.length)}`;
    }
  }

  return `<${iri}>`;
}

/**
 * Ranks proposals for a partial token against the keywords, the in-scope prefixes, and the vocabulary —
 * a prefix-match (case-insensitive), keywords first, then prefixes, then predicates and classes.
 * @param token The partial token being typed.
 * @param prefixes The buffer's prefix map.
 * @param vocabulary The loaded dataset's vocabulary.
 * @returns Up to a dozen proposals.
 */
export function completionsFor(token: string, prefixes: Map<string, string>, vocabulary: VocabularyView): Completion[] {
  if (token.length === 0) {
    return [];
  }

  const lower = token.toLowerCase();
  const matches = (candidate: string): boolean => candidate.toLowerCase().startsWith(lower);
  const proposals: Completion[] = [];

  for (const keyword of SPARQL_KEYWORDS) {
    if (matches(keyword)) {
      proposals.push({ insert: keyword, label: keyword, kind: 'keyword' });
    }
  }

  for (const prefix of prefixes.keys()) {
    if (matches(`${prefix}:`)) {
      proposals.push({ insert: `${prefix}:`, label: `${prefix}:`, kind: 'prefix' });
    }
  }

  for (const predicate of vocabulary.predicates) {
    if (matches(predicate)) {
      proposals.push({ insert: predicate, label: predicate, kind: 'predicate' });
    }
  }

  for (const klass of vocabulary.classes) {
    if (matches(klass)) {
      proposals.push({ insert: klass, label: klass, kind: 'class' });
    }
  }

  return proposals.slice(0, 12);
}

/** Reads the distinct terms bound to `?t` by a query, abbreviated against the prefixes. */
async function distinctTerms(transport: VeritasTransport, query: string, prefixes: Map<string, string>): Promise<string[]> {
  const outcome = await transport.runSparql(query);
  if (!outcome.ok) {
    return [];
  }

  const iris = (outcome.results.results?.bindings ?? [])
    .map((row) => row.t?.value)
    .filter((value): value is string => value !== undefined);

  return [...new Set(iris.map((iri) => abbreviate(iri, prefixes)))];
}

/**
 * Loads the loaded dataset's vocabulary from the engine — the distinct predicates and classes — so the
 * proposals reflect the data actually loaded, abbreviated against the buffer's prefixes.
 * @param transport The engine transport.
 * @param prefixes The buffer's prefix map (for abbreviation).
 * @returns The vocabulary view (empty when no engine answers).
 */
export async function loadVocabulary(transport: VeritasTransport, prefixes: Map<string, string>): Promise<VocabularyView> {
  const predicates = await distinctTerms(transport, 'SELECT DISTINCT ?t WHERE { ?s ?t ?o }', prefixes);
  const classes = await distinctTerms(transport, 'SELECT DISTINCT ?t WHERE { ?s a ?t }', prefixes);

  return { predicates, classes };
}
