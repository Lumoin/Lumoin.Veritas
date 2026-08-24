// SPARQL intellisense proposals: keywords, the buffer's in-scope prefixes, and the loaded dataset's
// vocabulary (predicates + classes), read through the transport seam. Two producers: parserCompletions maps
// a described completion context to proposals, and the token + prefix heuristic proposes wherever the
// active source describes no context.
import type { CompletionContextDto, SparqlOutcome } from './services/veritas-transport';

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

/**
 * The partial token immediately before the caret (the term being typed), or an empty string when none. Two
 * token shapes: an unclosed IRIREF run — a `<` the text has not closed — is one token, the bracket included,
 * so a partial IRI (`<https://lu`) prefix-matches a bracketed candidate and accepting one replaces the whole
 * run; otherwise the token is the word run, whose leading `@` lets a Turtle directive (`@prefix`, `@base`)
 * be a token.
 */
export function tokenBefore(textBeforeCaret: string): string {
  const iri = textBeforeCaret.match(/<[^<>"\s]*$/);
  if (iri !== null) {
    return iri[0];
  }

  const match = textBeforeCaret.match(/[@A-Za-z_:?][\w:?-]*$/);

  return match !== null ? match[0] : '';
}

/** Parses the buffer's prefix declarations — SPARQL `PREFIX` or Turtle `@prefix` — into a prefix → namespace-IRI map. */
export function parsePrefixes(text: string): Map<string, string> {
  const prefixes = new Map<string, string>();
  const declaration = /(?:@prefix|PREFIX)\s+([A-Za-z][\w-]*):\s*<([^>]*)>/gi;
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

/** The literal text of each keyword token kind the parser can name in `expectedTokens`. */
const KEYWORD_TEXT: Readonly<Record<string, string>> = {
  SelectKeyword: 'SELECT', ConstructKeyword: 'CONSTRUCT', AskKeyword: 'ASK', DescribeKeyword: 'DESCRIBE',
  BaseKeyword: 'BASE', PrefixKeyword: 'PREFIX', VersionKeyword: 'VERSION', WhereKeyword: 'WHERE',
  FromKeyword: 'FROM', NamedKeyword: 'NAMED', OptionalKeyword: 'OPTIONAL', UnionKeyword: 'UNION',
  MinusKeyword: 'MINUS', FilterKeyword: 'FILTER', BindKeyword: 'BIND', AsKeyword: 'AS',
  ServiceKeyword: 'SERVICE', SilentKeyword: 'SILENT', GraphKeyword: 'GRAPH', ValuesKeyword: 'VALUES',
  UndefKeyword: 'UNDEF', GroupKeyword: 'GROUP BY', HavingKeyword: 'HAVING', OrderKeyword: 'ORDER BY',
  LimitKeyword: 'LIMIT', OffsetKeyword: 'OFFSET', DistinctKeyword: 'DISTINCT', ReducedKeyword: 'REDUCED',
  AscKeyword: 'ASC', DescKeyword: 'DESC', InsertKeyword: 'INSERT', DeleteKeyword: 'DELETE',
  DataKeyword: 'DATA', LoadKeyword: 'LOAD', ClearKeyword: 'CLEAR', DropKeyword: 'DROP',
  CreateKeyword: 'CREATE', AddKeyword: 'ADD', MoveKeyword: 'MOVE', CopyKeyword: 'COPY',
  IntoKeyword: 'INTO', ToKeyword: 'TO', WithKeyword: 'WITH', UsingKeyword: 'USING',
  DefaultKeyword: 'DEFAULT', AllKeyword: 'ALL', A: 'a'
};

/** The local name of an IRI for a compact datatype hint: the part after the last `#` or `/`. */
function localName(iri: string): string {
  const cut = Math.max(iri.lastIndexOf('#'), iri.lastIndexOf('/'));

  return cut >= 0 && cut < iri.length - 1 ? iri.slice(cut + 1) : iri;
}

/**
 * Maps a parser-driven completion context to proposals: the keyword tokens the grammar admits next, the
 * in-scope variables when a variable is admissible (annotated with their datatype when resolved), and the
 * loaded vocabulary when an IRI / prefixed name is admissible. Filtered by the partial token (a prefix
 * match), or all of them when nothing is typed yet — the "what can I write here" list the parser enables.
 * @param context The completion context from the engine.
 * @param token The partial token being typed, or an empty string.
 * @param vocabulary The loaded dataset's vocabulary.
 * @returns Up to a dozen proposals, in suggestion order.
 */
export function parserCompletions(context: CompletionContextDto, token: string, vocabulary: VocabularyView): Completion[] {
  const lower = token.toLowerCase();
  const matches = (candidate: string): boolean => token.length === 0 || candidate.toLowerCase().startsWith(lower);
  const expected = new Set(context.expectedTokens);
  const proposals: Completion[] = [];

  for (const kind of context.expectedTokens) {
    const text = KEYWORD_TEXT[kind];
    if (text !== undefined && matches(text)) {
      proposals.push({ insert: text, label: text, kind: 'keyword' });
    }
  }

  if (expected.has('Variable')) {
    for (const variable of context.inScopeVariables) {
      const insert = `?${variable.name}`;
      if (matches(insert)) {
        const label = variable.datatype !== null ? `${insert} : ${localName(variable.datatype)}` : insert;
        proposals.push({ insert, label, kind: 'variable' });
      }
    }
  }

  if (expected.has('Iri') || expected.has('PrefixedName')) {
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
  }

  return proposals.slice(0, 12);
}

/** Runs one vocabulary query; the shell supplies the active transport and world, so the terms describe what is actually queried. */
export type VocabularyQueryRunner = (query: string) => Promise<SparqlOutcome>;

/** Reads the distinct terms bound to `?t` by a query, abbreviated against the prefixes. */
async function distinctTerms(runSparql: VocabularyQueryRunner, query: string, prefixes: Map<string, string>): Promise<string[]> {
  const outcome = await runSparql(query);
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
 * proposals reflect the data actually loaded and queried, abbreviated against the buffer's prefixes.
 * @param runSparql The query runner (the shell's active transport and world).
 * @param prefixes The buffer's prefix map (for abbreviation).
 * @returns The vocabulary view (empty when no engine answers).
 */
export async function loadVocabulary(runSparql: VocabularyQueryRunner, prefixes: Map<string, string>): Promise<VocabularyView> {
  const predicates = await distinctTerms(runSparql, 'SELECT DISTINCT ?t WHERE { ?s ?t ?o }', prefixes);
  const classes = await distinctTerms(runSparql, 'SELECT DISTINCT ?t WHERE { ?s a ?t }', prefixes);

  return { predicates, classes };
}
