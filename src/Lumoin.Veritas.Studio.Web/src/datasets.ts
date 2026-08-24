// The distributed datasets are static files under /datasets, listed in datasets/manifest.json, so the editor
// loads data by FETCHING (and the very same files ship for download / CLI bulk-load) rather than baking RDF
// into the bundle. They are ordinary datasets the editor opens, one of them at startup; the file picker opens
// an arbitrary local RDF document through the same path. All paths are relative, so they resolve against the
// page base — correct at the root and under a subpath host.

/** A dataset entry from datasets/manifest.json: metadata plus the relative URLs of its documents. */
export interface StudioDatasetEntry {
  /** Stable identifier used by the dataset switch. */
  readonly id: string;

  /** Short human label shown in the dataset switch and the active-dataset readout. */
  readonly label: string;

  /** One-line description of the story the dataset tells. */
  readonly description: string;

  /** Relative URL of the Turtle document loaded into the engine. */
  readonly turtle: string;

  /** Relative URL of the showcase SPARQL query. */
  readonly query: string;

  /** Relative URL of the graph-edge SPARQL query the graph view renders. */
  readonly graphQuery: string;

  /** Relative URL of the SHACL shapes graph (Turtle). */
  readonly shapes: string;
}

/** A dataset with its documents fetched: the content the shell loads into the engine and the editors. */
export interface LoadedDataset {
  /** Stable identifier (a `file:<name>` id for a user-opened document). */
  readonly id: string;

  /** Short human label shown in the active-dataset readout. */
  readonly label: string;

  /** One-line description of the dataset. */
  readonly description: string;

  /** The Turtle document content, loaded into the engine. */
  readonly turtle: string;

  /** The showcase SPARQL query content (placed in the SPARQL editor). */
  readonly query: string;

  /** The graph-edge SPARQL query content (?fromLabel ?fromKind ?toLabel ?toKind) the graph view runs. */
  readonly graphQuery: string;

  /** The SHACL shapes graph content (placed in the SHACL editor; empty for a user-opened file). */
  readonly shapes: string;
}

/**
 * Fetches a distributed text document, relative to the page base.
 * @param path The relative document path (e.g. `datasets/social.ttl`).
 * @returns The document text.
 */
async function fetchText(path: string): Promise<string> {
  const response = await fetch(path);
  if (!response.ok) {
    throw new Error(`dataset document fetch failed (${path}): ${response.status}`);
  }

  return await response.text();
}

/**
 * Fetches the dataset manifest — the distributed datasets, in display order.
 * @returns The manifest entries.
 */
export async function loadManifest(): Promise<StudioDatasetEntry[]> {
  const response = await fetch('datasets/manifest.json');
  if (!response.ok) {
    throw new Error(`dataset manifest fetch failed: ${response.status}`);
  }

  return await response.json() as StudioDatasetEntry[];
}

/**
 * Fetches a dataset's four documents into a LoadedDataset.
 * @param entry The manifest entry to fetch.
 * @returns The dataset with its documents' content.
 */
export async function fetchDataset(entry: StudioDatasetEntry): Promise<LoadedDataset> {
  const [turtle, query, graphQuery, shapes] = await Promise.all([
    fetchText(entry.turtle),
    fetchText(entry.query),
    fetchText(entry.graphQuery),
    fetchText(entry.shapes)
  ]);

  return { id: entry.id, label: entry.label, description: entry.description, turtle, query, graphQuery, shapes };
}

/** A generic explore query for a user-opened file (no showcase query ships with arbitrary RDF). */
const EXPLORE_QUERY = `SELECT ?s ?p ?o WHERE {
  ?s ?p ?o .
}
LIMIT 100`;

/** A generic graph query: every IRI-to-IRI edge, labelled by local name, so the graph view renders any file. */
const EXPLORE_GRAPH_QUERY = `SELECT ?fromLabel ?fromKind ?toLabel ?toKind WHERE {
  ?from ?p ?to .
  FILTER(isIRI(?to))
  BIND(REPLACE(STR(?from), "^.*[#/]", "") AS ?fromLabel)
  BIND(REPLACE(STR(?to), "^.*[#/]", "") AS ?toLabel)
  BIND("node" AS ?fromKind)
  BIND("node" AS ?toKind)
}
LIMIT 200`;

/**
 * Wraps a user-opened RDF document as a LoadedDataset with generic explore queries and no shapes.
 * @param name The file name, used as the label and (as `file:<name>`) the id.
 * @param turtle The document content.
 * @returns The loadable dataset.
 */
export function datasetFromFile(name: string, turtle: string): LoadedDataset {
  return {
    id: `file:${name}`,
    label: name,
    description: `Opened from ${name}`,
    turtle,
    query: EXPLORE_QUERY,
    graphQuery: EXPLORE_GRAPH_QUERY,
    shapes: ''
  };
}
