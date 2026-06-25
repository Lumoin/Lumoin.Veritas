// Sample data the shell renders before a live engine is attached. Domain: a cotton→garment supply chain —
// UNTP product passports joined to facilities, fibre materials, river basins and WRI Aqueduct water-risk
// scores. It exercises the story Studio is built for: regulated ROLES (manufacturer, auditor, authority)
// with DIFFERENTIATED ACCESS, data FEDERATED across operators, reached by a SPARQL SERVICE query ACROSS A
// TRUST BOUNDARY under access governance. Replaced by real transport results once an engine is present.
// (Battery passports are the same shape with a different vocabulary — both ship as selectable datasets.)

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
  scale: { triples: 123, nodes: 30, rendered: 30, clusters: 6, regions: 2 },

  types: [
    { key: 'facility', label: 'Facilities', hue: 150, c: 0.13 },
    { key: 'product', label: 'Products / passports', hue: 95, c: 0.14 },
    { key: 'material', label: 'Materials (fibre)', hue: 300, c: 0.12 },
    { key: 'basin', label: 'River basins', hue: 230, c: 0.12 },
    { key: 'risk', label: 'Water-risk ratings', hue: 25, c: 0.14 },
    { key: 'party', label: 'Parties (brand)', hue: 290, c: 0.13 }
  ],

  clusters: [
    { id: 'farm', type: 'facility', label: 'Cotton farm', count: 1, x: -0.72, y: -0.15 },
    { id: 'gin', type: 'facility', label: 'Ginning works', count: 1, x: -0.4, y: 0.35 },
    { id: 'spinner', type: 'facility', label: 'Spinning mill', count: 1, x: -0.02, y: -0.5 },
    { id: 'mill', type: 'facility', label: 'Fabric · dye house', count: 1, x: 0.4, y: 0.28 },
    { id: 'garment', type: 'facility', label: 'Garment plant', count: 1, x: 0.72, y: -0.2 },
    { id: 'basin', type: 'basin', label: 'Indus / Karnaphuli basins', count: 3, x: 0.05, y: 0.62 }
  ],

  links: [
    ['garment', 'mill'], ['mill', 'spinner'], ['spinner', 'gin'], ['gin', 'farm'],
    ['farm', 'basin'], ['gin', 'basin'], ['spinner', 'basin'], ['mill', 'basin']
  ],

  bindings: {
    vars: ['garment', 'facility', 'basin', 'riskScore', 'risk'],
    rows: [
      ["Men's woven shirt", 'Faisalabad Spinning Mill', '261034', '4.6', 'Extremely High'],
      ["Men's woven shirt", 'Jhang Cotton Growers', '261034', '4.6', 'Extremely High'],
      ["Men's woven shirt", 'Multan Ginning Works', '261034', '4.6', 'Extremely High'],
      ["Men's woven shirt", 'Chittagong Dye House', '352007', '4.4', 'Extremely High']
    ],
    total: 4
  },

  trace: [
    { kind: 'chose', term: 'untp:Product (6205)', why: 'Bound ?garment — the finished men’s woven shirt SKU matched the product category.', matches: 1 },
    { kind: 'chose', term: 'untp:upstreamInput+', why: 'Walked the traceability chain transitively to every upstream facility — fabric, yarn, lint, seed cotton.', matches: 4 },
    { kind: 'chose', term: 'SERVICE registry.example', why: 'Federated across a trust boundary to the brand’s facility/water registry; the auditor role credential authorised the view.', matches: 4 },
    { kind: 'excluded', term: 'untp:facilityWorkforce', why: 'Authority-restricted attribute — access denied for the auditor role (labour inspectorate only).', matches: 0 },
    { kind: 'chose', term: 'vw:baselineWaterStress', why: 'Required by the FILTER ( ≥ 4.0 ); the Aqueduct basin score pulled from the federated registry.', matches: 4 },
    { kind: 'note', term: 'governance · permit', why: 'The cross-boundary SERVICE call was permitted by policy; an agent can re-run this water-risk sweep across every supplier facility.', matches: 0 }
  ],

  cost: { rows: 4, ms: 18, ops: 640, plan: 'leapfrog · bound SERVICE' }
};
