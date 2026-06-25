// Sample data the shell renders before a live engine is attached. Domain: a battery passport graph —
// EV/industrial battery models joined to cathode materials, suppliers, recyclers, and conformity
// credentials, the way the EU Battery Regulation (2023/1542) battery passport and the UNTP value-chain
// specs frame it. It exercises the story Studio is built for: regulated ROLES (manufacturer, recycler,
// authority) with DIFFERENTIATED ACCESS, data FEDERATED across operators, reached by a SPARQL SERVICE
// query ACROSS A TRUST BOUNDARY under access governance. Replaced by real transport results once an
// engine is present. (Construction-material passports are the same shape with a different vocabulary.)

export interface NodeType {
  key: string;
  label: string;
  hue: number;
  c: number;
}

export interface Cluster {
  id: string;
  type: string;
  label: string;
  count: number;
  x: number;
  y: number;
}

export interface TraceDecision {
  kind: 'chose' | 'excluded' | 'note';
  term: string;
  why: string;
  matches: number;
}

export interface StudioData {
  scale: { triples: number; nodes: number; rendered: number; clusters: number; regions: number };
  types: NodeType[];
  clusters: Cluster[];
  links: [string, string][];
  bindings: { vars: string[]; rows: string[][]; total: number };
  trace: TraceDecision[];
  cost: { rows: number; ms: number; ops: number; plan: string };
}

export const STUDIO_DATA: StudioData = {
  scale: { triples: 4_812_440, nodes: 1_284_006, rendered: 312, clusters: 6, regions: 2 },

  types: [
    { key: 'battery', label: 'Battery models', hue: 220, c: 0.14 },
    { key: 'material', label: 'Cathode materials', hue: 95, c: 0.14 },
    { key: 'cell', label: 'Cells / modules', hue: 150, c: 0.13 },
    { key: 'supplier', label: 'Suppliers', hue: 290, c: 0.15 },
    { key: 'recycler', label: 'Recyclers', hue: 190, c: 0.12 },
    { key: 'credential', label: 'Conformity credentials', hue: 25, c: 0.13 }
  ],

  clusters: [
    { id: 'batteries', type: 'battery', label: 'EV battery line', count: 184_200, x: -0.55, y: -0.35 },
    { id: 'materials', type: 'material', label: 'Cathode · DPP', count: 42_980, x: 0.1, y: -0.62 },
    { id: 'cells', type: 'cell', label: 'Cells / modules', count: 11_640, x: 0.62, y: -0.18 },
    { id: 'suppliers', type: 'supplier', label: 'Supply chain', count: 3_120, x: 0.48, y: 0.52 },
    { id: 'recyclers', type: 'recycler', label: 'Recyclers', count: 64_500, x: -0.62, y: 0.4 },
    { id: 'credentials', type: 'credential', label: 'Conformity credentials', count: 8_770, x: -0.05, y: 0.2 }
  ],

  links: [
    ['batteries', 'materials'], ['batteries', 'cells'], ['batteries', 'recyclers'],
    ['materials', 'suppliers'], ['materials', 'credentials'], ['cells', 'recyclers'],
    ['suppliers', 'credentials'], ['credentials', 'batteries'], ['recyclers', 'credentials']
  ],

  bindings: {
    vars: ['battery', 'cathode', 'recycledCobalt', 'passport'],
    rows: [
      ['bat:Pack_A·E12', 'mat:NMC-811', '11', 'dpp:ABF1…91c'],
      ['bat:Pack_A·E14', 'mat:NMC-811', '9', 'dpp:7C2A…04d'],
      ['bat:Pack_B·N03', 'mat:NMC-622', '14', 'dpp:11E9…77a'],
      ['bat:Pack_A·E09', 'mat:NMC-811', '7', 'dpp:9F40…b21'],
      ['bat:Pack_C·S21', 'mat:LFP-120', '0', 'dpp:5571…3ce'],
      ['bat:Pack_B·W07', 'mat:NMC-622', '13', 'dpp:0A8D…e60'],
      ['bat:Pack_A·E05', 'mat:NMC-811', '12', 'dpp:BD33…1f7']
    ],
    total: 1_284
  },

  trace: [
    { kind: 'chose', term: 'bat:Battery', why: 'Bound ?battery — 412 EV packs in the active line matched the class.', matches: 412 },
    { kind: 'chose', term: 'bat:cathode', why: 'One hop to the cathode material carrying the recycled-content claim.', matches: 388 },
    { kind: 'chose', term: 'SERVICE supplier.example', why: 'Federated across a trust boundary to the supplier passport endpoint; the recycler role credential authorised the legitimate-interest view.', matches: 388 },
    { kind: 'excluded', term: 'bat:cellManufacturingSite', why: 'Authority-restricted attribute — access denied for the recycler role (market-surveillance only).', matches: 0 },
    { kind: 'chose', term: 'dpp:recycledContent (Co)', why: 'Required by the FILTER ( < 16 ); the cobalt share pulled from the federated passport.', matches: 33 },
    { kind: 'note', term: 'governance · permit', why: 'The cross-boundary SERVICE call was permitted by policy; an agent can re-run this due-diligence sweep across every supplier.', matches: 0 }
  ],

  cost: { rows: 1_284, ms: 42, ops: 3_140, plan: 'hash-join · bound SERVICE' }
};
