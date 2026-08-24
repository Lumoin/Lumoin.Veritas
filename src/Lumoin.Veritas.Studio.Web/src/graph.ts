// The results graph behind a seam. GraphView is the contract the results panel drives; the runnable
// shell ships CanvasGraphView (a compact 2D placeholder over the clusters + inter-cluster links), and
// the real view — BabylonGraphView, force/cluster/3D over @babylonjs/core — implements the same
// interface, so swapping it is a one-line factory change, not a rewrite. The studio owns the control
// binding (bindGraphViewControls) and the canvas lifecycle (replaceGraphCanvas): a view draws on the
// canvas it is handed and never rebinds the surface itself. No external deps here.

import type { Cluster, NodeType, StudioData } from './data';

export type GraphMode = 'force' | 'cluster' | 'depth';
export type GraphTheme = 'light' | 'dark';

/** The human-readable node record a graph view reports to the Studio inspector. */
export interface GraphNodeSelection {
  id: string;
  label: string;
  type: string;
  typeLabel: string;
  count: number;
}

/** Receives the selected node, or null when the selection is cleared. */
export type GraphSelectionListener = (selection: GraphNodeSelection | null) => void;

/** The results-graph contract the results panel drives; both CanvasGraphView and BabylonGraphView satisfy it. */
export interface GraphView {
  /** Sets the layout treatment (force / cluster / 3D). */
  setMode(mode: GraphMode): void;

  /** Sets the colour theme. */
  setTheme(theme: GraphTheme): void;

  /** Re-reads the canvas size after a layout change. */
  resize(): void;

  /** Moves the camera closer (positive) or farther away (negative). */
  zoomBy(direction: number): void;

  /** Returns the complete graph to its default fitted view. */
  fit(): void;

  /** Paints one frame. */
  render(): void;

  /** Releases the view's resources (the animation loop, GPU/canvas). */
  dispose(): void;
}

const TAU = Math.PI * 2;
const clamp = (v: number, lo: number, hi: number): number => Math.min(hi, Math.max(lo, v));
const GRAPH_MODES: readonly GraphMode[] = ['force', 'cluster', 'depth'];
const numberFormat = new Intl.NumberFormat('en-US');

/** Returns a supported graph mode from DOM/host input, falling back to the map layout. */
function graphMode(value: string | undefined): GraphMode {
  return GRAPH_MODES.includes(value as GraphMode) ? value as GraphMode : 'force';
}

/** Keeps the visible layout control and the canvas's inspectable state aligned with the active renderer. */
export function reflectGraphMode(canvas: HTMLCanvasElement, mode: GraphMode): void {
  canvas.dataset.graphMode = mode;
  canvas.dataset.graphNavigation = mode === 'depth' ? 'orbit' : 'planar';
  document.querySelectorAll<HTMLButtonElement>('button[data-graph-mode]').forEach((button) => {
    button.setAttribute('aria-pressed', String(button.dataset.graphMode === mode));
  });
  const help = document.getElementById('graph-help');
  if (help !== null) {
    help.textContent = mode === 'depth'
      ? 'Drag to orbit · scroll or pinch to zoom · double-click to fit'
      : 'Scroll or pinch to zoom · use − / + · double-click to fit';
  }
}

/** Paints graph selection into the accessible DOM inspector without involving the Studio's data/transport lane. */
export function renderGraphSelection(selection: GraphNodeSelection | null): void {
  const inspector = document.getElementById('graph-inspector');
  const title = document.getElementById('graph-selection-title');
  const meta = document.getElementById('graph-selection-meta');
  if (inspector === null || title === null || meta === null) {
    return;
  }

  if (selection === null) {
    inspector.dataset.state = 'idle';
    delete inspector.dataset.selectionId;
    title.textContent = 'Choose a node';
    meta.textContent = 'Click a node or use ← and → while the graph is focused.';

    return;
  }

  inspector.dataset.state = 'selected';
  inspector.dataset.selectionId = selection.id;
  title.textContent = selection.label;
  meta.textContent = `${selection.typeLabel} · ${numberFormat.format(selection.count)} ${selection.count === 1 ? 'item' : 'items'}`;
}

/** One arrowhead marking a directed edge's target end: the tip and the two wing points, in screen space. */
export interface EdgeArrowhead {
  tipX: number;
  tipY: number;
  leftX: number;
  leftY: number;
  rightX: number;
  rightY: number;
}

/**
 * Computes the arrowhead marking a directed edge's target end in 2D screen space. The tip stands off the
 * target by the given distance (the target node's drawn radius), and the wings sweep back along the edge,
 * symmetric about it. An edge too short to point anywhere (endpoints closer than the standoff plus the
 * arrow length) yields null rather than a degenerate arrow.
 * @param fromX The source endpoint's x.
 * @param fromY The source endpoint's y.
 * @param toX The target endpoint's x.
 * @param toY The target endpoint's y.
 * @param standoff The distance the tip keeps from the target endpoint.
 * @param size The arrow's length from base to tip.
 * @returns The arrowhead points, or null for a too-short edge.
 */
export function edgeArrowhead(fromX: number, fromY: number, toX: number, toY: number, standoff: number, size: number): EdgeArrowhead | null {
  const deltaX = toX - fromX;
  const deltaY = toY - fromY;
  const length = Math.hypot(deltaX, deltaY);
  if (length <= standoff + size) {
    return null;
  }

  const unitX = deltaX / length;
  const unitY = deltaY / length;
  const tipX = toX - unitX * standoff;
  const tipY = toY - unitY * standoff;
  const baseX = tipX - unitX * size;
  const baseY = tipY - unitY * size;
  const halfWidth = size * 0.55;

  return {
    tipX,
    tipY,
    leftX: baseX - unitY * halfWidth,
    leftY: baseY + unitX * halfWidth,
    rightX: baseX + unitY * halfWidth,
    rightY: baseY - unitX * halfWidth
  };
}

/** Clones the graph canvas into a detached, renderer-clean twin: same markup attributes, no dataset marks from any earlier renderer. */
export function detachedGraphCanvas(canvas: HTMLCanvasElement): HTMLCanvasElement {
  const replacement = canvas.cloneNode(false) as HTMLCanvasElement;
  for (const key of Object.keys(replacement.dataset)) {
    delete replacement.dataset[key];
  }

  return replacement;
}

/**
 * Replaces the currently mounted graph canvas with an attribute-identical fresh element. A canvas can own only
 * one context family for its lifetime, so every 2D ↔ WebGL renderer handoff needs a new element. The caller may
 * hold a stale element from before an async upgrade; resolving by id keeps the swap on the live DOM surface.
 */
export function replaceGraphCanvas(canvas: HTMLCanvasElement): HTMLCanvasElement {
  const liveCanvas = canvas.id === ''
    ? canvas
    : (document.getElementById(canvas.id) as HTMLCanvasElement | null) ?? canvas;
  const replacement = detachedGraphCanvas(liveCanvas);
  liveCanvas.replaceWith(replacement);

  return replacement;
}

/**
 * Binds the controls owned by the graph surface to one active view and returns the unbind handle. The studio
 * unbinds the outgoing view before binding the incoming one, so the lazy upgrade never double-applies an action.
 */
export function bindGraphViewControls(view: GraphView, canvas: HTMLCanvasElement): () => void {
  const removers: Array<() => void> = [];
  const listen = (element: HTMLElement, type: string, listener: EventListener): void => {
    element.addEventListener(type, listener);
    removers.push(() => element.removeEventListener(type, listener));
  };
  const controls = Array.from(document.querySelectorAll<HTMLButtonElement>('button[data-graph-mode], button[data-graph-action]'));
  controls.forEach((button) => { button.disabled = false; });

  const selectedMode = graphMode(document.querySelector<HTMLButtonElement>('button[data-graph-mode][aria-pressed="true"]')?.dataset.graphMode);
  view.setMode(selectedMode);

  document.querySelectorAll<HTMLButtonElement>('button[data-graph-mode]').forEach((button) => {
    listen(button, 'click', () => view.setMode(graphMode(button.dataset.graphMode)));
  });
  document.querySelectorAll<HTMLButtonElement>('button[data-graph-action]').forEach((button) => {
    listen(button, 'click', () => {
      if (button.dataset.graphAction === 'zoom-in') {
        view.zoomBy(1);
      } else if (button.dataset.graphAction === 'zoom-out') {
        view.zoomBy(-1);
      } else if (button.dataset.graphAction === 'fit') {
        view.fit();
      }
    });
  });

  const host = canvas.parentElement;
  const resizeObserver = host === null ? null : new ResizeObserver(() => {
    view.resize();
    view.render();
  });
  if (host !== null) {
    resizeObserver?.observe(host);
  }

  return () => {
    resizeObserver?.disconnect();
    removers.forEach((remove) => remove());
    controls.forEach((button) => { button.disabled = true; });
  };
}

interface SuperNode extends Cluster {
  z: number;
  r: number;
}

interface Projected {
  x: number;
  y: number;
  s: number;
  depth: number;
}

/** Deterministic pseudo-random so the layout is stable across reloads. */
function mulberry32(seed: number): () => number {
  return () => {
    seed |= 0;
    seed = (seed + 0x6d2b79f5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;

    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** 2D-canvas placeholder graph: clusters as haloed nodes, inter-cluster relations as lines. */
class CanvasGraphView implements GraphView {
  private readonly ctx: CanvasRenderingContext2D;
  private readonly typeMap: Record<string, NodeType>;
  private readonly supers: SuperNode[];
  private readonly superById: Record<string, SuperNode>;
  private readonly reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  private mode: GraphMode = 'force';
  private theme: GraphTheme = 'light';
  private width = 0;
  private height = 0;
  private t = 0;
  private t0 = 0;
  private raf = 0;
  private zoomScale = 1;
  private selectedIndex = -1;
  private readonly loop: (ts: number) => void;
  private readonly click: (event: MouseEvent) => void;
  private readonly keydown: (event: KeyboardEvent) => void;
  private readonly doubleClick: () => void;

  /** Builds the deterministic cluster layout and starts the render loop. */
  constructor(
    private readonly canvas: HTMLCanvasElement,
    private readonly data: StudioData,
    private readonly selectionChanged: GraphSelectionListener
  ) {
    this.ctx = canvas.getContext('2d')!;
    this.typeMap = Object.fromEntries(data.types.map((d) => [d.key, d]));
    const rnd = mulberry32(42);
    this.supers = data.clusters.map((c) => ({ ...c, z: (rnd() - 0.5) * 1.4, r: 4.5 + Math.min(4.5, Math.sqrt(c.count) / 80) }));
    this.superById = Object.fromEntries(this.supers.map((s) => [s.id, s]));
    this.loop = this.step.bind(this);
    this.click = this.onClick.bind(this);
    this.keydown = this.onKeyDown.bind(this);
    this.doubleClick = this.fit.bind(this);
    canvas.addEventListener('click', this.click);
    canvas.addEventListener('keydown', this.keydown);
    canvas.addEventListener('dblclick', this.doubleClick);
    this.resize();
    this.render();
    this.raf = requestAnimationFrame(this.loop);
    this.canvas.dataset.graphRenderer = 'canvas';
    this.canvas.dataset.graphZoom = this.zoomScale.toFixed(3);
    this.canvas.dataset.graphEdgeStyle = 'directed';
    this.selectionChanged(null);
  }

  /** @inheritdoc */
  setMode(mode: GraphMode): void {
    this.mode = mode;
    reflectGraphMode(this.canvas, mode);
    this.render();
  }

  /** @inheritdoc */
  setTheme(theme: GraphTheme): void {
    this.theme = theme;
    this.render();
  }

  /** @inheritdoc */
  resize(): void {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const rect = this.canvas.getBoundingClientRect();
    this.width = rect.width;
    this.height = rect.height;
    this.canvas.width = Math.round(rect.width * dpr);
    this.canvas.height = Math.round(rect.height * dpr);
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  }

  /** @inheritdoc */
  zoomBy(direction: number): void {
    this.zoomScale = clamp(this.zoomScale * (direction > 0 ? 1.16 : 0.86), 0.62, 2.2);
    this.canvas.dataset.graphZoom = this.zoomScale.toFixed(3);
    this.render();
  }

  /** @inheritdoc */
  fit(): void {
    this.zoomScale = 1;
    this.canvas.dataset.graphZoom = this.zoomScale.toFixed(3);
    this.render();
  }

  /** @inheritdoc */
  dispose(): void {
    cancelAnimationFrame(this.raf);
    this.canvas.removeEventListener('click', this.click);
    this.canvas.removeEventListener('keydown', this.keydown);
    this.canvas.removeEventListener('dblclick', this.doubleClick);
    this.selectionChanged(null);
  }

  /** The animation tick: advances time and repaints when the 3D mode is rotating. */
  private step(ts: number): void {
    if (this.t0 === 0) {
      this.t0 = ts;
    }

    this.t = (ts - this.t0) / 1000;
    if (this.mode === 'depth' && !this.reduced) {
      this.render();
    }

    this.raf = requestAnimationFrame(this.loop);
  }

  /** The fill colour for a node type, lightness-shifted and alpha-scaled, in the current theme. */
  private fill(type: string, lightShift: number, alpha: number): string {
    const d = this.typeMap[type] ?? { hue: 220, c: 0.12 };
    const l = (this.theme === 'dark' ? 0.7 : 0.6) + lightShift;

    return `oklch(${clamp(l, 0.2, 0.92)} ${d.c} ${d.hue} / ${alpha})`;
  }

  /** The edge (link) colour at the given alpha, in the current theme; the light stroke is deep enough to read on the light panel. */
  private edge(alpha: number): string {
    return this.theme === 'dark' ? `oklch(0.62 0.02 230 / ${alpha})` : `oklch(0.45 0.02 250 / ${Math.min(1, alpha + 0.15)})`;
  }

  /** The label/text colour (primary or secondary) in the current theme. */
  private ink(secondary: boolean): string {
    return this.theme === 'dark'
      ? (secondary ? 'oklch(0.70 0.005 230)' : 'oklch(0.92 0.005 230)')
      : (secondary ? 'oklch(0.42 0.005 90)' : 'oklch(0.15 0.005 90)');
  }

  /** Projects a node's normalized [-1,1] position to screen pixels, with a perspective foreshorten in 3D mode. */
  private project(n: SuperNode, pad: number): Projected {
    const cx = this.width / 2;
    const cy = this.height / 2;
    const sx = (this.width / 2 - pad) * this.zoomScale;
    const sy = (this.height / 2 - pad) * this.zoomScale;
    if (this.mode === 'depth') {
      const ang = this.reduced ? 0.5 : this.t * 0.12;
      const x = n.x * Math.cos(ang) - n.z * Math.sin(ang);
      const z = n.x * Math.sin(ang) + n.z * Math.cos(ang);
      const persp = 1 / (1.7 - z * 0.55);

      return { x: cx + x * sx * persp, y: cy + n.y * sy * persp, s: persp, depth: z };
    }

    return { x: cx + n.x * sx, y: cy + n.y * sy, s: 1, depth: 0 };
  }

  /** @inheritdoc */
  render(): void {
    const ctx = this.ctx;
    const rect = this.canvas.getBoundingClientRect();
    if (Math.abs(rect.width - this.width) > 1 || Math.abs(rect.height - this.height) > 1) {
      this.resize();
    }

    ctx.clearRect(0, 0, this.width, this.height);
    const pad = 46;

    ctx.lineWidth = this.mode === 'cluster' ? 1.6 : 1;
    for (const [a, b] of this.data.links) {
      const target = this.superById[b];
      const pa = this.project(this.superById[a], pad);
      const pb = this.project(target, pad);
      ctx.strokeStyle = this.edge(this.mode === 'cluster' ? 0.5 : 0.32);
      ctx.beginPath();
      ctx.moveTo(pa.x, pa.y);
      ctx.lineTo(pb.x, pb.y);
      ctx.stroke();

      // The from/to order is the direction: an arrowhead marks the target end, standing off its disc.
      const targetRadius = (this.mode === 'cluster' ? target.r * 1.1 : target.r * 0.86) * pb.s;
      const arrow = edgeArrowhead(pa.x, pa.y, pb.x, pb.y, targetRadius + 2, 6.5 * clamp(pb.s, 0.6, 1));
      if (arrow !== null) {
        ctx.fillStyle = this.edge(this.mode === 'cluster' ? 0.7 : 0.55);
        ctx.beginPath();
        ctx.moveTo(arrow.tipX, arrow.tipY);
        ctx.lineTo(arrow.leftX, arrow.leftY);
        ctx.lineTo(arrow.rightX, arrow.rightY);
        ctx.closePath();
        ctx.fill();
      }
    }

    const ordered = [...this.supers].sort((a, b) => this.project(a, pad).depth - this.project(b, pad).depth);
    for (const s of ordered) {
      const p = this.project(s, pad);
      const radius = (this.mode === 'cluster' ? s.r * 1.1 : s.r * 0.86) * p.s;

      const halo = ctx.createRadialGradient(p.x, p.y, radius * 0.4, p.x, p.y, radius * 1.75);
      halo.addColorStop(0, this.fill(s.type, 0.06, 0.12));
      halo.addColorStop(1, this.fill(s.type, 0.06, 0));
      ctx.fillStyle = halo;
      ctx.beginPath();
      ctx.arc(p.x, p.y, radius * 1.75, 0, TAU);
      ctx.fill();

      ctx.fillStyle = this.fill(s.type, 0.04, 1);
      ctx.beginPath();
      ctx.arc(p.x, p.y, radius, 0, TAU);
      ctx.fill();
      ctx.lineWidth = 2;
      ctx.strokeStyle = this.theme === 'dark' ? 'oklch(0.16 0.008 230)' : 'oklch(1 0 0)';
      ctx.stroke();

      const selected = this.supers[this.selectedIndex]?.id === s.id;
      if (selected) {
        ctx.lineWidth = 2;
        ctx.strokeStyle = this.theme === 'dark' ? 'oklch(0.9 0.16 130.34)' : 'oklch(0.45 0.14 254.3)';
        ctx.beginPath();
        ctx.arc(p.x, p.y, radius + 6, 0, TAU);
        ctx.stroke();
      }

      const showDepthLabel = this.mode !== 'depth' || selected || p.s >= 0.64;
      if (showDepthLabel) {
        const labelScale = this.mode === 'depth' ? clamp(p.s, 0.62, 1) : 1;
        ctx.globalAlpha = this.mode === 'depth' && !selected ? clamp((p.s - 0.5) / 0.24, 0.24, 0.82) : 1;
        ctx.fillStyle = this.ink(false);
        ctx.font = `600 ${Math.round(12 * labelScale)}px 'IBM Plex Sans', ui-sans-serif, system-ui, sans-serif`;
        ctx.textAlign = 'center';
        ctx.fillText(s.label, p.x, p.y - radius - 9 * labelScale);
        ctx.globalAlpha = 1;
      }
      if (this.mode === 'cluster') {
        ctx.fillStyle = this.ink(true);
        ctx.font = "500 11px 'IBM Plex Mono', ui-monospace, monospace";
        ctx.fillText(`${s.count.toLocaleString('en-US')} elems`, p.x, p.y + 4);
      }
    }
  }

  /** Reports one node to the inspector. */
  private select(index: number): void {
    if (this.supers.length === 0) {
      return;
    }

    this.selectedIndex = (index + this.supers.length) % this.supers.length;
    const node = this.supers[this.selectedIndex];
    this.selectionChanged({
      id: node.id,
      label: node.label,
      type: node.type,
      typeLabel: this.typeMap[node.type]?.label ?? node.type,
      count: node.count
    });
    this.render();
  }

  /** Selects the node under a click, or clears selection when the background is clicked. */
  private onClick(event: MouseEvent): void {
    const rect = this.canvas.getBoundingClientRect();
    const x = event.clientX - rect.left;
    const y = event.clientY - rect.top;
    const pad = 46;
    let found = -1;
    let distance = Number.POSITIVE_INFINITY;
    this.supers.forEach((node, index) => {
      const p = this.project(node, pad);
      const radius = (this.mode === 'cluster' ? node.r * 1.1 : node.r * 0.86) * p.s + 8;
      const d = Math.hypot(x - p.x, y - p.y);
      if (d <= radius && d < distance) {
        found = index;
        distance = d;
      }
    });
    if (found >= 0) {
      this.select(found);
    } else {
      this.selectedIndex = -1;
      this.selectionChanged(null);
      this.render();
    }
  }

  /** Makes the canvas keyboard-operable: arrows move selection, +/− zoom, and 0 or Home restores the fit. */
  private onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
      event.preventDefault();
      this.select(this.selectedIndex + 1);
    } else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
      event.preventDefault();
      this.select(this.selectedIndex <= 0 ? this.supers.length - 1 : this.selectedIndex - 1);
    } else if (event.key === '+' || event.key === '=') {
      event.preventDefault();
      this.zoomBy(1);
    } else if (event.key === '-' || event.key === '_') {
      event.preventDefault();
      this.zoomBy(-1);
    } else if (event.key === '0' || event.key === 'Home') {
      event.preventDefault();
      this.fit();
    }
  }
}

/** Builds the 2D placeholder view on the given canvas; the studio replaces the canvas first and binds the surface controls after. */
export function createGraphView(canvas: HTMLCanvasElement, data: StudioData, selectionChanged: GraphSelectionListener = renderGraphSelection): GraphView {
  return new CanvasGraphView(canvas, data, selectionChanged);
}
