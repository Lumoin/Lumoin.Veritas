// The page half of the native bridge. When Studio runs inside the desktop shell, the host exposes a
// message channel (`window.external.sendMessage` / `receiveMessage`, the Photino-style surface). This
// module adapts that raw channel into the typed `window.veritas` bridge the transport seam resolves —
// request/response correlated by id, trace events fanned out. Importing it is a no-op in a plain
// browser (no `window.external`), so the same bundle runs in the shell and on the web unchanged.

import type { SparqlQueryError, SparqlResultsDocument } from '../components/sparql-results';
import type {
  CompletionContextDto,
  LiteralDiagnosisDto,
  SparqlOutcome,
  TraceEvent,
  TurtleCompletionContextDto,
  VeritasBridge
} from './veritas-transport';

interface PhotinoExternal {
  sendMessage(message: string): void;
  receiveMessage(callback: (message: string) => void): void;
}

// One reply variant per request method, each carrying its own payload field, so a reply discriminates on the
// field it carries; a request the host cannot answer comes back as the error envelope.
type HostReply =
  | { id: string; ok: true; results: SparqlResultsDocument }
  | { id: string; ok: true; diagnosis: LiteralDiagnosisDto }
  | { id: string; ok: true; sparqlContext: CompletionContextDto }
  | { id: string; ok: true; turtleContext: TurtleCompletionContextDto }
  | { id: string; ok: true; vocabulary: string[] }
  | { id: string; ok: false; error: SparqlQueryError }
  | { kind: 'trace'; event: TraceEvent };

const external = (globalThis as { external?: Partial<PhotinoExternal> }).external;

if (external?.sendMessage !== undefined && external.receiveMessage !== undefined) {
  const pending = new Map<string, (reply: HostReply) => void>();
  const traceHandlers = new Set<(event: TraceEvent) => void>();
  const send = external.sendMessage.bind(external);
  let sequence = 0;

  external.receiveMessage((raw: string) => {
    const reply = JSON.parse(raw) as HostReply;
    if ('id' in reply) {
      pending.get(reply.id)?.(reply);
      pending.delete(reply.id);

      return;
    }

    for (const handler of traceHandlers) {
      handler(reply.event);
    }
  });

  const bridge: VeritasBridge = {
    runSparql(query: string): Promise<SparqlOutcome> {
      const id = `q${++sequence}`;

      return new Promise<HostReply>((resolve) => {
        pending.set(id, resolve);
        send(JSON.stringify({ id, method: 'runSparql', query }));
      }).then((reply): SparqlOutcome => {
        if ('results' in reply) {
          return { ok: true, results: reply.results };
        }

        if ('error' in reply) {
          return { ok: false, error: reply.error };
        }

        return { ok: false, error: { error: 'Unexpected host reply.', diagnostics: [] } };
      });
    },

    describeLiteral(datatypeIri: string, body: string): Promise<LiteralDiagnosisDto | null> {
      const id = `d${++sequence}`;

      return new Promise<HostReply>((resolve) => {
        pending.set(id, resolve);
        send(JSON.stringify({ id, method: 'describeLiteral', datatype: datatypeIri, body }));
      }).then((reply): LiteralDiagnosisDto | null => ('diagnosis' in reply ? reply.diagnosis : null));
    },

    describeCompletion(query: string, caret: number): Promise<CompletionContextDto | null> {
      const id = `c${++sequence}`;

      return new Promise<HostReply>((resolve) => {
        pending.set(id, resolve);
        send(JSON.stringify({ id, method: 'describeCompletion', query, caret }));
      }).then((reply): CompletionContextDto | null => ('sparqlContext' in reply ? reply.sparqlContext : null));
    },

    describeTurtleCompletion(source: string, caret: number, syntax: string): Promise<TurtleCompletionContextDto | null> {
      const id = `t${++sequence}`;

      return new Promise<HostReply>((resolve) => {
        pending.set(id, resolve);
        send(JSON.stringify({ id, method: 'describeTurtleCompletion', source, caret, syntax }));
      }).then((reply): TurtleCompletionContextDto | null => ('turtleContext' in reply ? reply.turtleContext : null));
    },

    editorVocabulary(): Promise<string[] | null> {
      const id = `v${++sequence}`;

      return new Promise<HostReply>((resolve) => {
        pending.set(id, resolve);
        send(JSON.stringify({ id, method: 'editorVocabulary' }));
      }).then((reply): string[] | null => ('vocabulary' in reply ? reply.vocabulary : null));
    },

    onTrace(handler: (event: TraceEvent) => void): () => void {
      traceHandlers.add(handler);
      send(JSON.stringify({ method: 'subscribeTrace' }));

      return () => {
        traceHandlers.delete(handler);
      };
    }
  };

  (globalThis as { veritas?: VeritasBridge }).veritas = bridge;
}
