// The engine-source selection the topbar picker persists: which engine this page queries — the in-browser
// WASM engine, this origin's CLI-served engine, or a user-entered custom SPARQL Protocol endpoint. The
// selection lives in localStorage so a reload (and the next visit) keeps it; it ships EMPTY — no endpoint
// is ever pre-filled. The desktop shell's bridge is not a selectable source: it wins outright at boot and
// the picker is hidden under it.

/** The selectable engine sources: the in-browser engine, this origin's server, or a custom endpoint. */
export type EngineSourceKind = 'wasm' | 'server' | 'custom';

/** A persisted engine-source selection; `endpoint` carries the custom URL and is empty otherwise. */
export interface EngineSourceSelection {
  kind: EngineSourceKind;
  endpoint: string;
}

/** The localStorage key the selection persists under. */
const STORAGE_KEY = 'veritas-studio-engine-source';

/**
 * The persisted engine-source selection, or null when none is stored (the first visit, a cleared browser,
 * or storage unavailable — private modes may deny access, which reads as no selection).
 */
export function loadEngineSourceSelection(): EngineSourceSelection | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw === null) {
      return null;
    }

    const parsed = JSON.parse(raw) as Partial<EngineSourceSelection>;
    if (parsed.kind !== 'wasm' && parsed.kind !== 'server' && parsed.kind !== 'custom') {
      return null;
    }

    return { kind: parsed.kind, endpoint: typeof parsed.endpoint === 'string' ? parsed.endpoint : '' };
  } catch {
    return null;
  }
}

/** Persists an engine-source selection; storage denial (private modes) is tolerated as session-only choice. */
export function saveEngineSourceSelection(selection: EngineSourceSelection): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(selection));
  } catch {
    // Storage unavailable: the selection still applies for this page's lifetime.
  }
}

/**
 * Whether a candidate custom-endpoint URL is usable: absolute, and an HTTP(S) scheme — the schemes fetch
 * can POST a SPARQL query to.
 * @param candidate The user-entered URL text.
 */
export function isValidEndpointUrl(candidate: string): boolean {
  try {
    const url = new URL(candidate);

    return url.protocol === 'http:' || url.protocol === 'https:';
  } catch {
    return false;
  }
}
