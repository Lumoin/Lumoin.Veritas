// The graph builder's unit rows: every kind in the picture gets its own colour — also the kinds only a
// dataset's own graph query names — the legend labels stay readable, the kind counts match the nodes,
// and the picture is the same whatever order the query returned its rows in.

import { describe, expect, it } from 'vitest';
import { buildGraphData } from './graph-data';

/** One typed-edge row as the graph query binds it. */
function edge(fromKind: string, fromLabel: string, toKind: string, toLabel: string): Record<string, { value: string }> {
  return {
    fromKind: { value: fromKind },
    fromLabel: { value: fromLabel },
    toKind: { value: toKind },
    toLabel: { value: toLabel }
  };
}

describe('buildGraphData', () => {
  it('assigns every kind its own colour, including kinds no label table lists', () => {
    const data = buildGraphData([
      edge('mystery', 'A', 'enigma', 'B'),
      edge('riddle', 'C', 'mystery', 'A')
    ]);
    const hues = data.types.map((t) => t.hue);
    expect(hues).toHaveLength(3);
    expect(new Set(hues).size).toBe(hues.length);
  });

  it('paints an unlisted kind at full chroma rather than a washed-out fallback', () => {
    const data = buildGraphData([edge('mystery', 'A', 'enigma', 'B')]);
    for (const type of data.types) {
      expect(type.c).toBeGreaterThanOrEqual(0.1);
    }
  });

  it('labels a curated kind by its display name', () => {
    const data = buildGraphData([edge('battery', 'Pack', 'cathode', 'NMC 811')]);
    expect(data.types.find((t) => t.key === 'battery')?.label).toBe('Battery models');
  });

  it('capitalises the label of a kind no table lists', () => {
    const data = buildGraphData([edge('mystery', 'A', 'mystery', 'B')]);
    expect(data.types[0]?.label).toBe('Mystery');
  });

  it('counts the nodes behind each kind for the legend', () => {
    const data = buildGraphData([
      edge('measure', 'Wall', 'segment', 'North bank'),
      edge('measure', 'Basin', 'segment', 'North bank')
    ]);
    expect(data.types.find((t) => t.key === 'measure')?.count).toBe(2);
    expect(data.types.find((t) => t.key === 'segment')?.count).toBe(1);
  });

  it('keeps curated column precedence ahead of unlisted kinds', () => {
    const data = buildGraphData([edge('aardvark', 'A', 'battery', 'Pack')]);
    expect(data.types.map((t) => t.key)).toEqual(['battery', 'aardvark']);
  });

  it('draws the same picture whatever order the rows arrive in', () => {
    const rows = [
      edge('regulator', 'Agency', 'measure', 'Wall'),
      edge('community', 'Eastside', 'segment', 'North bank'),
      edge('measure', 'Wall', 'segment', 'North bank')
    ];
    const forward = buildGraphData(rows);
    const reversed = buildGraphData([...rows].reverse());
    expect(reversed.types).toEqual(forward.types);
  });
});
