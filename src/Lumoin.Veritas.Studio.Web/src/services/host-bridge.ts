// The page half of the native bridge. When Studio runs inside the desktop shell, the host exposes a
// message channel (`window.external.sendMessage` / `receiveMessage`, the Photino-style surface). This
// module adapts that raw channel into the typed `window.veritas` bridge the transport seam resolves —
// request/response correlated by id, trace events fanned out. Importing it is a no-op in a plain
// browser (no `window.external`), so the same bundle runs in the shell and on the web unchanged.

import type { SparqlQueryError, SparqlResultsDocument } from '../components/sparql-results';
import type { SparqlOutcome, TraceEvent, VeritasBridge } from './veritas-transport';

interface PhotinoExternal {
  sendMessage(message: string): void;
  receiveMessage(callback: (message: string) => void): void;
}

type HostReply =
  | { id: string; ok: true; results: SparqlResultsDocument }
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
        if ('ok' in reply && reply.ok) {
          return { ok: true, results: reply.results };
        }

        if ('ok' in reply) {
          return { ok: false, error: reply.error };
        }

        return { ok: false, error: { error: 'Unexpected host reply.', diagnostics: [] } };
      });
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
