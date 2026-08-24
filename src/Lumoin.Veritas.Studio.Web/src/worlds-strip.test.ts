// The worlds strip's state-machine rows: which world the shell executes in as listings change under it,
// what the strip presents, how the diff document projects into table rows, and how a buffer routes between
// the query and update faces. Every claimed behavior has a row that fails the moment the projection stops
// honoring it — the capped-listing rows in particular fail if a cut listing is ever presented as complete.

import { describe, expect, it } from 'vitest';
import type { WorldDto, WorldsDiffDto } from './services/veritas-transport';
import { diffSummary, dropVerdict, forkVerdict, resolveActiveWorld, scenarioLeversFrom, scenarioUpdateText, sparqlOperationKind, worldsDiffRows, worldsStripView, type ScenarioLeverView } from './worlds-strip';

/** A two-world listing: the primary and one fork, the shape the what-if flow produces. */
const twoWorlds: readonly WorldDto[] = [
  { name: 'main', stateId: '00e1b2c3d4e5f607', parent: null },
  { name: 'whatif', stateId: 'a1b2c3d4e5f60718', parent: 'main' }
];

/** A diff document with one default-graph transition listing everything it counts. */
const oneTransition: WorldsDiffDto = {
  outcome: 'diffed',
  cap: 1000,
  totalTransitions: 1,
  totalTriples: 2,
  truncated: false,
  transitions: [{
    graph: null,
    totalAdditions: 2,
    totalRemovals: 0,
    additions: [
      { s: '<https://veritas.app/data/battery/PackX>', p: '<http://www.w3.org/1999/02/22-rdf-syntax-ns#type>', o: '<https://veritas.app/ns/battery#Battery>' },
      { s: '<https://veritas.app/data/battery/PackX>', p: '<https://veritas.app/ns/battery#recycledCobalt>', o: '"5"^^<http://www.w3.org/2001/XMLSchema#integer>' }
    ],
    removals: []
  }]
};

describe('resolveActiveWorld', () => {
  it('answers the primary for an empty listing, no pick, and the primary picked by name', () => {
    expect(resolveActiveWorld([], 'whatif')).toBeNull();
    expect(resolveActiveWorld(twoWorlds, null)).toBeNull();
    expect(resolveActiveWorld(twoWorlds, 'main')).toBeNull();
  });

  it('keeps a picked world while the listing carries it', () => {
    expect(resolveActiveWorld(twoWorlds, 'whatif')).toBe('whatif');
  });

  it('falls back to the primary when the picked world left the listing', () => {
    expect(resolveActiveWorld([twoWorlds[0]], 'whatif')).toBeNull();
  });
});

describe('worldsStripView', () => {
  it('presents the primary with its state id, its lineage, and drop disabled', () => {
    const view = worldsStripView(twoWorlds, null);
    expect(view.activeName).toBe('main');
    expect(view.stateId).toBe('00e1b2c3d4e5f607');
    expect(view.lineage).toContain('primary world');
    expect(view.dropEnabled).toBe(false);
    expect(view.options.map((option) => option.name)).toEqual(['main', 'whatif']);
    expect(view.options[0].selected).toBe(true);
    expect(view.options[1].selected).toBe(false);
  });

  it('presents a fork with its own state id, its parent lineage, and drop enabled', () => {
    const view = worldsStripView(twoWorlds, 'whatif');
    expect(view.activeName).toBe('whatif');
    expect(view.stateId).toBe('a1b2c3d4e5f60718');
    expect(view.lineage).toContain("forked from 'main'");
    expect(view.dropEnabled).toBe(true);
    expect(view.options[1].selected).toBe(true);
  });
});

describe('forkVerdict and dropVerdict', () => {
  it('maps every fork outcome token, and the null degrade, to a verdict', () => {
    expect(forkVerdict('whatif', 'forked')).toEqual({ ok: true, message: "Created scenario world 'whatif' — Run and updates now execute in it." });
    expect(forkVerdict('whatif', 'duplicateName').ok).toBe(false);
    expect(forkVerdict('whatif', 'unknownSource').ok).toBe(false);
    expect(forkVerdict('whatif', null).ok).toBe(false);
  });

  it('maps every drop outcome token, and the null degrade, to a verdict', () => {
    expect(dropVerdict('whatif', 'dropped')).toEqual({ ok: true, message: "Dropped scenario world 'whatif' — back on the primary world." });
    expect(dropVerdict('main', 'primaryWorld').ok).toBe(false);
    expect(dropVerdict('gone', 'unknownWorld').ok).toBe(false);
    expect(dropVerdict('whatif', null).ok).toBe(false);
  });
});

describe('diffSummary', () => {
  it('states the exact totals', () => {
    expect(diffSummary(oneTransition)).toBe('1 transition · 2 triples');
  });

  it('names the cap whenever a triple was omitted', () => {
    expect(diffSummary({ ...oneTransition, totalTriples: 1200, truncated: true })).toBe('1 transition · 1200 triples · listing the first 1000');
  });

  it('says when a diffed world is not registered', () => {
    expect(diffSummary({ outcome: 'unknownWorld' })).toBe('one of the diffed worlds is not registered');
  });
});

describe('worldsDiffRows', () => {
  it('renders a transition as its graph header and one row per listed triple', () => {
    const rows = worldsDiffRows(oneTransition);
    expect(rows).toHaveLength(3);
    expect(rows[0]).toEqual({ kind: 'graph', label: 'default graph · +2 −0' });
    expect(rows[1]).toMatchObject({ kind: 'triple', sign: '+', predicate: '<http://www.w3.org/1999/02/22-rdf-syntax-ns#type>' });
    expect(rows[2]).toMatchObject({ kind: 'triple', sign: '+', object: '"5"^^<http://www.w3.org/2001/XMLSchema#integer>' });
  });

  it('appends an elision note when the cap cut a listing, so cut never reads as complete', () => {
    const capped: WorldsDiffDto = {
      outcome: 'diffed',
      cap: 1,
      totalTransitions: 1,
      totalTriples: 3,
      truncated: true,
      transitions: [{
        graph: '<https://veritas.app/graphs/audit>',
        totalAdditions: 2,
        totalRemovals: 1,
        additions: [{ s: '<s>', p: '<p>', o: '<o>' }],
        removals: []
      }]
    };
    const rows = worldsDiffRows(capped);
    expect(rows[0]).toEqual({ kind: 'graph', label: '<https://veritas.app/graphs/audit> · +2 −1' });
    expect(rows[2]).toEqual({ kind: 'note', label: '1 more addition not listed' });
    expect(rows[3]).toEqual({ kind: 'note', label: '1 more removal not listed' });
  });

  it('renders removals with the removal sign', () => {
    const removal: WorldsDiffDto = {
      outcome: 'diffed',
      cap: 1000,
      totalTransitions: 1,
      totalTriples: 1,
      truncated: false,
      transitions: [{ graph: null, totalAdditions: 0, totalRemovals: 1, additions: [], removals: [{ s: '<s>', p: '<p>', o: '<o>' }] }]
    };
    expect(worldsDiffRows(removal)[1]).toMatchObject({ kind: 'triple', sign: '−' });
  });

  it('says an empty diff holds no differences and an unknown world is not registered', () => {
    expect(worldsDiffRows({ ...oneTransition, totalTransitions: 0, totalTriples: 0, transitions: [] })).toEqual([
      { kind: 'note', label: 'No differences — the worlds hold the same content.' }
    ]);
    expect(worldsDiffRows({ outcome: 'unknownWorld' })).toEqual([
      { kind: 'note', label: 'One of the diffed worlds is not registered.' }
    ]);
  });
});

/** The sea-level lever as the lever query answers it for the adaptation dataset's primary world. */
const seaLevelLever: ScenarioLeverView = {
  label: 'Sea-level rise by 2100 (cm)',
  target: 'https://veritas.app/data/riverton/Climate',
  property: 'https://veritas.app/ns/adaptation#seaLevelRiseCm',
  min: 0,
  max: 120,
  step: 10,
  value: 30
};

describe('scenarioLeversFrom', () => {
  it('reads a lever row into a knob view', () => {
    const levers = scenarioLeversFrom([{
      label: { value: 'Sea-level rise by 2100 (cm)' },
      target: { value: 'https://veritas.app/data/riverton/Climate' },
      property: { value: 'https://veritas.app/ns/adaptation#seaLevelRiseCm' },
      min: { value: '0' },
      max: { value: '120' },
      step: { value: '10' },
      value: { value: '30' }
    }]);
    expect(levers).toEqual([seaLevelLever]);
  });

  it('drops a row missing a field or carrying a non-numeric range, never rendering a broken knob', () => {
    expect(scenarioLeversFrom([{ label: { value: 'x' } }])).toEqual([]);
    expect(scenarioLeversFrom([{
      label: { value: 'x' },
      target: { value: 't' },
      property: { value: 'p' },
      min: { value: 'low' },
      max: { value: '10' },
      step: { value: '1' },
      value: { value: '5' }
    }])).toEqual([]);
    expect(scenarioLeversFrom([])).toEqual([]);
  });
});

describe('scenarioUpdateText', () => {
  it('compiles a moved knob into a delete-insert of the targeted value', () => {
    const update = scenarioUpdateText([{ lever: seaLevelLever, value: 60 }]);
    expect(update).toContain('DELETE { <https://veritas.app/data/riverton/Climate> <https://veritas.app/ns/adaptation#seaLevelRiseCm> ?prior0 . }');
    expect(update).toContain('INSERT { <https://veritas.app/data/riverton/Climate> <https://veritas.app/ns/adaptation#seaLevelRiseCm> 60 . }');
    expect(update).toContain('WHERE { <https://veritas.app/data/riverton/Climate> <https://veritas.app/ns/adaptation#seaLevelRiseCm> ?prior0 . }');
  });

  it('contributes nothing for an unmoved knob and answers empty when nothing moved', () => {
    expect(scenarioUpdateText([{ lever: seaLevelLever, value: 30 }])).toBe('');
    expect(scenarioUpdateText([])).toBe('');
  });

  it('joins several moved knobs into one update with distinct prior variables', () => {
    const rainfall: ScenarioLeverView = { ...seaLevelLever, label: 'Mean annual rainfall change (%)', property: 'https://veritas.app/ns/adaptation#rainfallChangePercent', min: -30, max: 40, step: 5, value: 0 };
    const update = scenarioUpdateText([{ lever: seaLevelLever, value: 60 }, { lever: rainfall, value: 20 }]);
    expect(update).toContain('?prior0');
    expect(update).toContain('?prior1');
    expect(update).toContain(' ;');
    expect(update).toContain('rainfallChangePercent> 20 . }');
  });

  it('routes as an update through the buffer classifier, so Run would commit it', () => {
    expect(sparqlOperationKind(scenarioUpdateText([{ lever: seaLevelLever, value: 60 }]))).toBe('update');
  });
});

describe('sparqlOperationKind', () => {
  it('routes query forms to the query face', () => {
    expect(sparqlOperationKind('SELECT * WHERE { ?s ?p ?o }')).toBe('query');
    expect(sparqlOperationKind('ASK { ?s ?p ?o }')).toBe('query');
    expect(sparqlOperationKind('# a comment\nPREFIX ex: <https://example.org/>\nSELECT ?s WHERE { ?s a ex:T }')).toBe('query');
    expect(sparqlOperationKind('BASE <https://example.org/>\nDESCRIBE <thing>')).toBe('query');
  });

  it('routes update forms to the update face, prologue and comments notwithstanding', () => {
    expect(sparqlOperationKind('INSERT DATA { <s> <p> <o> }')).toBe('update');
    expect(sparqlOperationKind('PREFIX ex: <https://example.org/>\n# hypothetical\nDELETE WHERE { ?s ex:p ?o }')).toBe('update');
    expect(sparqlOperationKind('WITH <https://example.org/g> DELETE { ?s ?p ?o } WHERE { ?s ?p ?o }')).toBe('update');
    expect(sparqlOperationKind('clear default')).toBe('update');
  });

  it('routes empty and mid-edit text to the query face, whose parser stays the authority', () => {
    expect(sparqlOperationKind('')).toBe('query');
    expect(sparqlOperationKind('   # only a comment')).toBe('query');
    expect(sparqlOperationKind('PREFIX ex: <https://exa')).toBe('query');
    expect(sparqlOperationKind('SELEC')).toBe('query');
  });
});
