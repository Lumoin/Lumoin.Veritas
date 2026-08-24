// The shape of the graph view's data: the node kinds it colours by, the nodes it lays out, and the edges it
// draws between them. Built from a dataset's graph query by buildGraphData — the only producer — so the
// picture always describes the data the engine currently holds.

export interface NodeType {
  key: string;
  label: string;
  hue: number;
  c: number;
  count: number;
}

export interface Cluster {
  id: string;
  type: string;
  label: string;
  count: number;
  x: number;
  y: number;
}

export interface StudioData {
  types: NodeType[];
  clusters: Cluster[];
  links: [string, string][];
}
