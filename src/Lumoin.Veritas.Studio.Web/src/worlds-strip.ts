// The worlds strip's state machine and the diff panel's view models — pure projections the shell renders
// verbatim, kept DOM-free so the strip's behavior is unit-tested where it is decided. The active world is
// null for the primary world (the listing's first entry): Run, updates, the graph, the vocabulary, and
// SHACL validation route through the plain faces there and through the world-scoped faces everywhere else,
// so a source without the worlds face still runs everything it always ran.

import type { WorldDto, WorldDropOutcomeDto, WorldForkOutcomeDto, WorldsDiffDto } from './services/veritas-transport';

/** One picker option of the worlds strip. */
export interface WorldOptionView {
  readonly name: string;
  readonly selected: boolean;
}

/** What the worlds strip presents for one listing: the picker options, the active world's identity, and the drop affordance. */
export interface WorldsStripView {
  readonly options: readonly WorldOptionView[];
  readonly activeName: string;
  readonly stateId: string;
  readonly lineage: string;
  readonly dropEnabled: boolean;
}

/** The verdict a strip operation paints: whether it took, and the message that says what happened. */
export interface WorldOperationVerdict {
  readonly ok: boolean;
  readonly message: string;
}

/** One row of the diff panel's table: a per-graph header, a listed triple, or a note (an empty diff, a capped listing's elision, an unregistered world). */
export type WorldsDiffRow =
  | { readonly kind: 'graph'; readonly label: string }
  | { readonly kind: 'triple'; readonly sign: '+' | '−'; readonly subject: string; readonly predicate: string; readonly object: string }
  | { readonly kind: 'note'; readonly label: string };

/**
 * Resolves the active world against a fresh listing: a picked world stands while the listing carries it,
 * and everything else — an empty listing, no pick, a dropped name, or the primary picked by name — is the
 * primary world, expressed as null so the shell routes through the plain (non-world-scoped) faces there.
 * @param worlds The listing, primary first; empty when the source carries no worlds face.
 * @param current The world picked so far, or null for the primary.
 * @returns The world the shell executes in, or null for the primary.
 */
export function resolveActiveWorld(worlds: readonly WorldDto[], current: string | null): string | null {
  if (worlds.length === 0 || current === null || current === worlds[0].name) {
    return null;
  }

  return worlds.some((world) => world.name === current) ? current : null;
}

/**
 * Projects one listing into what the strip presents. The caller guarantees a non-empty listing — an empty
 * one hides the strip instead of rendering it.
 * @param worlds The listing, primary first (non-empty).
 * @param active The active world, or null for the primary.
 * @returns The strip view.
 */
export function worldsStripView(worlds: readonly WorldDto[], active: string | null): WorldsStripView {
  const activeName = active ?? worlds[0].name;
  const entry = worlds.find((world) => world.name === activeName) ?? worlds[0];

  return {
    options: worlds.map((world) => ({ name: world.name, selected: world.name === activeName })),
    activeName,
    stateId: entry.stateId,
    lineage: entry.parent === null ? `'${entry.name}' is the primary world` : `'${entry.name}' was forked from '${entry.parent}'`,
    dropEnabled: active !== null
  };
}

/**
 * The verdict a fork answers: the outcome tokens are expected conditions, and a null answer is the
 * transport's degrade (no worlds face, or a source that answered something else).
 * @param name The requested fork name.
 * @param outcome The wire's outcome token, or null.
 * @returns The verdict.
 */
export function forkVerdict(name: string, outcome: WorldForkOutcomeDto | null): WorldOperationVerdict {
  switch (outcome) {
    case 'forked':
      return { ok: true, message: `Created scenario world '${name}' — Run and updates now execute in it.` };
    case 'duplicateName':
      return { ok: false, message: `A world named '${name}' already exists.` };
    case 'unknownSource':
      return { ok: false, message: 'The world to fork from is no longer registered.' };
    default:
      return { ok: false, message: 'The source answered no fork outcome.' };
  }
}

/**
 * The verdict a drop answers, under the same degrade contract the fork verdict states.
 * @param name The dropped world's name.
 * @param outcome The wire's outcome token, or null.
 * @returns The verdict.
 */
export function dropVerdict(name: string, outcome: WorldDropOutcomeDto | null): WorldOperationVerdict {
  switch (outcome) {
    case 'dropped':
      return { ok: true, message: `Dropped scenario world '${name}' — back on the primary world.` };
    case 'primaryWorld':
      return { ok: false, message: 'The primary world is never droppable.' };
    case 'unknownWorld':
      return { ok: false, message: `No world named '${name}' is registered.` };
    default:
      return { ok: false, message: 'The source answered no drop outcome.' };
  }
}

/** A count with its unit, pluralized on anything but one. */
function counted(count: number, unit: string): string {
  return count === 1 ? `${count} ${unit}` : `${count} ${unit}s`;
}

/**
 * The diff panel's summary line: the document's exact totals — the truth even when the listings are
 * capped — with the cap named whenever a triple was omitted.
 * @param diff The diff document.
 * @returns The summary text.
 */
export function diffSummary(diff: WorldsDiffDto): string {
  if (diff.outcome === 'unknownWorld') {
    return 'one of the diffed worlds is not registered';
  }

  const totals = `${counted(diff.totalTransitions, 'transition')} · ${counted(diff.totalTriples, 'triple')}`;

  return diff.truncated ? `${totals} · listing the first ${diff.cap}` : totals;
}

/**
 * Projects a diff document into the panel's table rows: per graph a header carrying the exact totals, one
 * row per listed triple, and an elision note wherever the cap cut a listing — so the panel never presents
 * a capped listing as the whole difference.
 * @param diff The diff document.
 * @returns The rows, in render order.
 */
export function worldsDiffRows(diff: WorldsDiffDto): WorldsDiffRow[] {
  if (diff.outcome === 'unknownWorld') {
    return [{ kind: 'note', label: 'One of the diffed worlds is not registered.' }];
  }

  if (diff.transitions.length === 0) {
    return [{ kind: 'note', label: 'No differences — the worlds hold the same content.' }];
  }

  const rows: WorldsDiffRow[] = [];
  for (const transition of diff.transitions) {
    rows.push({ kind: 'graph', label: `${transition.graph ?? 'default graph'} · +${transition.totalAdditions} −${transition.totalRemovals}` });
    for (const triple of transition.additions) {
      rows.push({ kind: 'triple', sign: '+', subject: triple.s, predicate: triple.p, object: triple.o });
    }

    if (transition.additions.length < transition.totalAdditions) {
      rows.push({ kind: 'note', label: `${counted(transition.totalAdditions - transition.additions.length, 'more addition')} not listed` });
    }

    for (const triple of transition.removals) {
      rows.push({ kind: 'triple', sign: '−', subject: triple.s, predicate: triple.p, object: triple.o });
    }

    if (transition.removals.length < transition.totalRemovals) {
      rows.push({ kind: 'note', label: `${counted(transition.totalRemovals - transition.removals.length, 'more removal')} not listed` });
    }
  }

  return rows;
}

/**
 * The fixed query the create-a-scenario dialog reads a dataset's declared levers with — run in the
 * base world, so each knob starts at that world's actual value. A dataset that declares no levers
 * answers no rows and the dialog shows none.
 */
export const SCENARIO_LEVER_QUERY = `PREFIX scn: <https://veritas.app/ns/scenario#>
SELECT ?label ?target ?property ?min ?max ?step ?value WHERE {
  ?lever a scn:Lever ; scn:label ?label ; scn:target ?target ; scn:property ?property ;
         scn:min ?min ; scn:max ?max ; scn:step ?step .
  ?target ?property ?value .
}
ORDER BY ?label`;

/** One row of a SPARQL results document, as the transport parses it. */
type ResultBinding = Record<string, { readonly value: string } | undefined>;

/** One scenario lever the loaded data declares: the knob's label and range, the value it moves, and the world's current value. */
export interface ScenarioLeverView {
  readonly label: string;
  readonly target: string;
  readonly property: string;
  readonly min: number;
  readonly max: number;
  readonly step: number;
  readonly value: number;
}

/** One knob's chosen setting when a scenario world is created. */
export interface ScenarioLeverSetting {
  readonly lever: ScenarioLeverView;
  readonly value: number;
}

/**
 * Reads the lever query's rows into lever views, structurally validated: a row missing a field or
 * carrying a non-numeric range or value is dropped rather than rendered as a broken knob.
 * @param bindings The lever query's solution bindings.
 * @returns The levers, in the query's order.
 */
export function scenarioLeversFrom(bindings: readonly ResultBinding[]): ScenarioLeverView[] {
  const levers: ScenarioLeverView[] = [];
  for (const row of bindings) {
    const label = row.label?.value;
    const target = row.target?.value;
    const property = row.property?.value;
    const min = Number(row.min?.value);
    const max = Number(row.max?.value);
    const step = Number(row.step?.value);
    const value = Number(row.value?.value);
    if (label === undefined || target === undefined || property === undefined
      || !Number.isFinite(min) || !Number.isFinite(max) || !Number.isFinite(step) || !Number.isFinite(value)) {
      continue;
    }

    levers.push({ label, target, property, min, max, step, value });
  }

  return levers;
}

/**
 * Compiles the changed knobs into the one SPARQL Update the new scenario world commits: per
 * changed lever a delete-insert of the targeted value. Unchanged knobs contribute nothing, and no
 * change at all answers the empty string — the scenario is then a plain fork.
 * @param settings The dialog's knob settings.
 * @returns The update text, or empty when nothing moved.
 */
export function scenarioUpdateText(settings: readonly ScenarioLeverSetting[]): string {
  const operations: string[] = [];
  for (const setting of settings) {
    if (setting.value === setting.lever.value) {
      continue;
    }

    const at = operations.length;
    operations.push(`DELETE { <${setting.lever.target}> <${setting.lever.property}> ?prior${at} . }
INSERT { <${setting.lever.target}> <${setting.lever.property}> ${setting.value} . }
WHERE { <${setting.lever.target}> <${setting.lever.property}> ?prior${at} . }`);
  }

  return operations.join(' ;\n');
}

/** The keywords that open a SPARQL Update operation once the prologue is behind. */
const UPDATE_OPENERS = new Set(['insert', 'delete', 'load', 'clear', 'create', 'drop', 'copy', 'move', 'add', 'with']);

/**
 * Classifies a SPARQL buffer for routing: a text whose first post-prologue keyword opens an update
 * operation commits through the update face, everything else — queries, partial edits, text the grammar
 * refuses — runs through the query face, whose parser stays the authority on what the text actually is.
 * The prologue (comments, PREFIX and BASE declarations) is skipped, never interpreted.
 * @param text The buffer's full text.
 * @returns `update` when the first operation keyword is an update opener, otherwise `query`.
 */
export function sparqlOperationKind(text: string): 'query' | 'update' {
  let at = 0;
  const end = text.length;
  while (at < end) {
    const character = text[at];
    if (character === '#') {
      while (at < end && text[at] !== '\n') {
        at++;
      }

      continue;
    }

    if (/\s/.test(character)) {
      at++;
      continue;
    }

    let wordEnd = at;
    while (wordEnd < end && /[A-Za-z]/.test(text[wordEnd])) {
      wordEnd++;
    }

    const word = text.slice(at, wordEnd).toLowerCase();
    if (word === 'prefix' || word === 'base') {
      // The declaration ends at its IRIREF's closing bracket; an unclosed one is a mid-edit buffer.
      const close = text.indexOf('>', wordEnd);
      if (close === -1) {
        return 'query';
      }

      at = close + 1;
      continue;
    }

    return UPDATE_OPENERS.has(word) ? 'update' : 'query';
  }

  return 'query';
}
