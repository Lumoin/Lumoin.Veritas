// Turns a graph query's typed-edge rows (?fromLabel ?fromKind ?toLabel ?toKind) into the StudioData the
// graph view renders, so the picture reflects whichever dataset the engine currently holds rather than a
// hardcoded structure. Distinct endpoints become nodes laid out in per-kind columns (left to right by a
// fixed precedence), the rows become links, and every kind gets its own colour from a golden-angle hue
// walk over the kind list — a dataset whose graph query binds kinds no table here lists still paints
// each kind distinctly.
import type { Cluster, NodeType, StudioData } from './data';

/** The legend labels for the kinds the distributed datasets' graph queries bind; an unlisted kind shows its capitalised kind string. */
const KIND_LABELS: Record<string, string> = {
  battery: 'Battery models',
  cathode: 'Cathode materials',
  department: 'Departments',
  faculty: 'Faculty',
  student: 'Students',
  person: 'People',
  regulator: 'Regulators',
  contract: 'Contracts',
  owner: 'Owners',
  measure: 'Measures',
  segment: 'Segments',
  community: 'Communities',
  ngo: 'NGOs',
  channel: 'Channels',
  programme: 'Programmes',
  observation: 'Observations',
  case: 'Cases',
  evidence: 'Evidence',
  counterclaim: 'Counterclaims',
  record: 'Declarations',
  passport: 'Product passports',
  supply: 'Material supplies',
  supplier: 'Suppliers',
  region: 'Regions',
  greenwater: 'Green-water flows',
  authority: 'Authorities',
  media: 'Media'
};

// Left-to-right column order; kinds not listed sort after these, alphabetically.
const KIND_ORDER = [
  'battery', 'cathode', 'department', 'faculty', 'student', 'person',
  'regulator', 'contract', 'owner', 'measure', 'segment', 'community', 'ngo', 'channel',
  'programme', 'observation', 'case', 'evidence', 'counterclaim', 'record',
  'passport', 'supply', 'supplier', 'region', 'greenwater', 'authority', 'media'
];

/** The hue step between consecutive kinds: the golden angle spreads any number of kinds over the wheel without neighbouring columns sharing a colour. */
const KIND_HUE_STEP = 137.508;

/** The first kind's hue — the blue the shell's accent palette anchors on. */
const KIND_HUE_BASE = 245;

/** The chroma every kind's colour carries; the views choose lightness per theme. */
const KIND_CHROMA = 0.14;

/** The column precedence (and so the colour-walk position) of a kind: listed kinds keep the curated order, the rest follow alphabetically. */
function kindRank(kind: string): number {
  const index = KIND_ORDER.indexOf(kind);

  return index === -1 ? KIND_ORDER.length : index;
}

/** One typed-edge row from the graph query; an unbound variable is absent. */
type EdgeRow = Record<string, { value: string } | undefined>;

/** A graph node accumulated while scanning the edge rows. */
interface BuiltNode {
  /** The node's stable id (kind + label, slugified), used by clusters and links. */
  id: string;

  /** The node's kind, mapped to a colour. */
  kind: string;

  /** The node's display label. */
  label: string;
}

/**
 * Builds the graph view's {@link StudioData} from typed-edge query rows.
 * @param rows The graph query's solution bindings (?fromLabel ?fromKind ?toLabel ?toKind).
 * @returns The StudioData the graph view renders.
 */
export function buildGraphData(rows: ReadonlyArray<EdgeRow>): StudioData {
  const nodes = new Map<string, BuiltNode>();
  const linkKeys = new Set<string>();
  const links: [string, string][] = [];

  const idOf = (kind: string, label: string): string => `${kind}::${label}`.replace(/[^a-zA-Z0-9]+/g, '-');
  const addNode = (kind: string, label: string): string => {
    const id = idOf(kind, label);
    if (!nodes.has(id)) {
      nodes.set(id, { id, kind, label });
    }

    return id;
  };

  for (const row of rows) {
    const fromLabel = row.fromLabel?.value;
    const fromKind = row.fromKind?.value;
    const toLabel = row.toLabel?.value;
    const toKind = row.toKind?.value;
    if (fromLabel === undefined || fromKind === undefined || toLabel === undefined || toKind === undefined) {
      continue;
    }

    const from = addNode(fromKind, fromLabel);
    const to = addNode(toKind, toLabel);
    const key = `${from} ${to}`;
    if (from !== to && !linkKeys.has(key)) {
      linkKeys.add(key);
      links.push([from, to]);
    }
  }

  const allNodes = [...nodes.values()];
  const kindCounts = new Map<string, number>();
  for (const node of allNodes) {
    kindCounts.set(node.kind, (kindCounts.get(node.kind) ?? 0) + 1);
  }

  const kinds = [...kindCounts.keys()].sort(
    (p, q) => (kindRank(p) - kindRank(q)) || (p < q ? -1 : p > q ? 1 : 0));

  const types: NodeType[] = kinds.map((kind, index) => ({
    key: kind,
    label: KIND_LABELS[kind] ?? `${kind.charAt(0).toUpperCase()}${kind.slice(1)}`,
    hue: (KIND_HUE_BASE + index * KIND_HUE_STEP) % 360,
    c: KIND_CHROMA,
    count: kindCounts.get(kind) ?? 0
  }));

  const clusters: Cluster[] = [];
  kinds.forEach((kind, column) => {
    const inKind = allNodes.filter((node) => node.kind === kind);
    const x = kinds.length === 1 ? 0 : -0.78 + (1.56 * column) / (kinds.length - 1);
    inKind.forEach((node, index) => {
      const y = inKind.length === 1 ? 0 : -0.72 + (1.44 * index) / (inKind.length - 1);
      clusters.push({ id: node.id, type: node.kind, label: node.label, count: 1, x, y });
    });
  });

  return { types, clusters, links };
}
