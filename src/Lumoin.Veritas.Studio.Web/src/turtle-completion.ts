// Turtle / SHACL intellisense proposals: the Turtle keywords plus the fixed RDF vocabulary (the prefixed
// names and bracketed full IRIs the active source's vocabulary face answers) and the buffer's declared
// prefixes. The peer of query-completion.ts for the SPARQL editor: a token + prefix heuristic for the
// buffers no parser describes, and the parser-driven mapping for the contexts a source does describe.
import type { Completion, VocabularyView } from './query-completion';
import type { TurtleCompletionContextDto } from './services/veritas-transport';

/** The Turtle keywords proposed at a token boundary (`@prefix`/`@base` trigger once a Turtle-aware token scan lands). */
export const TURTLE_KEYWORDS: readonly string[] = ['@prefix', '@base', 'PREFIX', 'BASE', 'a', 'true', 'false'];

/**
 * Ranks Turtle/SHACL proposals for a partial token: the Turtle keywords, the buffer's declared prefixes,
 * and the supplied vocabulary (the fixed sh:/owl:/rdf:/rdfs:/xsd: terms plus any terms from the loaded data) — a
 * case-insensitive prefix match, keywords first, then prefixes, then vocabulary terms and classes.
 * @param token The partial token being typed.
 * @param prefixes The buffer's prefix map.
 * @param vocabulary The proposal vocabulary (fixed grammar terms + data terms).
 * @returns Up to a dozen proposals.
 */
export function turtleCompletionsFor(token: string, prefixes: Map<string, string>, vocabulary: VocabularyView): Completion[] {
  if (token.length === 0) {
    return [];
  }

  const lower = token.toLowerCase();
  const matches = (candidate: string): boolean => candidate.toLowerCase().startsWith(lower);
  const proposals: Completion[] = [];

  for (const keyword of TURTLE_KEYWORDS) {
    if (matches(keyword)) {
      proposals.push({ insert: keyword, label: keyword, kind: 'keyword' });
    }
  }

  for (const prefix of prefixes.keys()) {
    if (matches(`${prefix}:`)) {
      proposals.push({ insert: `${prefix}:`, label: `${prefix}:`, kind: 'prefix' });
    }
  }

  for (const term of vocabulary.predicates) {
    if (matches(term)) {
      proposals.push({ insert: term, label: term, kind: 'term' });
    }
  }

  for (const klass of vocabulary.classes) {
    if (matches(klass)) {
      proposals.push({ insert: klass, label: klass, kind: 'class' });
    }
  }

  return proposals.slice(0, 12);
}

/** The literal text of each Turtle keyword token kind that has a fixed insertion. */
const TURTLE_TOKEN_TEXT: Readonly<Record<string, string>> = {
  A: 'a',
  PrefixKeyword: '@prefix',
  BaseKeyword: '@base',
  VersionKeyword: '@version',
  GraphKeyword: 'GRAPH'
};

/**
 * Maps a parser-driven Turtle completion context to proposals: the keyword tokens the grammar admits next
 * (e.g. `a`, `@prefix`), and — where an IRI or prefixed name is admissible (a verb or object position) — the
 * supplied vocabulary (the fixed sh:/owl:/rdf:/rdfs:/xsd: terms plus any data terms). Filtered by the partial token, or
 * all of them when nothing is typed yet.
 * @param context The Turtle completion context from the engine.
 * @param token The partial token being typed, or an empty string.
 * @param vocabulary The proposal vocabulary.
 * @returns Up to a dozen proposals, in suggestion order.
 */
export function turtleParserCompletions(context: TurtleCompletionContextDto, token: string, vocabulary: VocabularyView): Completion[] {
  const lower = token.toLowerCase();
  const matches = (candidate: string): boolean => token.length === 0 || candidate.toLowerCase().startsWith(lower);
  const expected = new Set(context.expectedTokens);
  const proposals: Completion[] = [];

  for (const kind of context.expectedTokens) {
    const text = TURTLE_TOKEN_TEXT[kind];
    if (text !== undefined && matches(text)) {
      proposals.push({ insert: text, label: text, kind: 'keyword' });
    }
  }

  if (expected.has('Iri') || expected.has('PrefixedName')) {
    for (const term of vocabulary.predicates) {
      if (matches(term)) {
        proposals.push({ insert: term, label: term, kind: 'term' });
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
