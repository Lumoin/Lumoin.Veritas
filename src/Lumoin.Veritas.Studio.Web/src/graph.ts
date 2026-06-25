// The results graph behind a seam. GraphView is the contract the results panel drives; the runnable
// shell ships CanvasGraphView (a compact 2D placeholder over the clusters + inter-cluster links), and
// the real view — BabylonGraphView, force/cluster/3D over @babylonjs/core — implements the same
// interface, so swapping it is a one-line factory change, not a rewrite. No external deps here.

import type { Cluster, NodeType, StudioData } from './data';

export type GraphMode = 'force' | 'cluster' | 'depth';
export type GraphTheme = 'light' | 'dark';

/** The results-graph contract the results panel drives; both CanvasGraphView and BabylonGraphView satisfy it. */
export interface GraphView {
  /** Sets the layout treatment (force / cluster / 3D). */
  setMode(mode: GraphMode): void;

  /** Sets the colour theme. */
  setTheme(theme: GraphTheme): void;

  /** Re-reads the canvas size after a layout change. */
  resize(): void;

  /** Paints one frame. */
  render(): void;

  /** Releases the view's resources (the animation loop, GPU/canvas). */
  dispose(): void;
}

const TAU = Math.PI * 2;
const clamp = (v: number, lo: number, hi: number): number => Math.min(hi, Math.max(lo, v));

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
  private readonly loop: (ts: number) => void;

  /** Builds the deterministic cluster layout and starts the render loop. */
  constructor(private readonly canvas: HTMLCanvasElement, private readonly data: StudioData) {
    this.ctx = canvas.getContext('2d')!;
    this.typeMap = Object.fromEntries(data.types.map((d) => [d.key, d]));
    const rnd = mulberry32(42);
    this.supers = data.clusters.map((c) => ({ ...c, z: (rnd() - 0.5) * 1.4, r: 10 + Math.sqrt(c.count) / 38 }));
    this.superById = Object.fromEntries(this.supers.map((s) => [s.id, s]));
    this.loop = this.step.bind(this);
    this.resize();
    this.render();
    this.raf = requestAnimationFrame(this.loop);
  }

  /** @inheritdoc */
  setMode(mode: GraphMode): void {
    this.mode = mode;
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
  dispose(): void {
    cancelAnimationFrame(this.raf);
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

  /** The edge (link) colour at the given alpha, in the current theme. */
  private edge(alpha: number): string {
    return this.theme === 'dark' ? `oklch(0.62 0.02 230 / ${alpha})` : `oklch(0.55 0.01 90 / ${alpha})`;
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
    const sx = this.width / 2 - pad;
    const sy = this.height / 2 - pad;
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
      const pa = this.project(this.superById[a], pad);
      const pb = this.project(this.superById[b], pad);
      ctx.strokeStyle = this.edge(this.mode === 'cluster' ? 0.5 : 0.32);
      ctx.beginPath();
      ctx.moveTo(pa.x, pa.y);
      ctx.lineTo(pb.x, pb.y);
      ctx.stroke();
    }

    const ordered = [...this.supers].sort((a, b) => this.project(a, pad).depth - this.project(b, pad).depth);
    for (const s of ordered) {
      const p = this.project(s, pad);
      const radius = (this.mode === 'cluster' ? s.r * 1.35 : s.r * 0.7) * p.s;

      const halo = ctx.createRadialGradient(p.x, p.y, radius * 0.3, p.x, p.y, radius * 2.4);
      halo.addColorStop(0, this.fill(s.type, 0.06, 0.22));
      halo.addColorStop(1, this.fill(s.type, 0.06, 0));
      ctx.fillStyle = halo;
      ctx.beginPath();
      ctx.arc(p.x, p.y, radius * 2.4, 0, TAU);
      ctx.fill();

      ctx.fillStyle = this.fill(s.type, 0.04, 1);
      ctx.beginPath();
      ctx.arc(p.x, p.y, radius, 0, TAU);
      ctx.fill();
      ctx.lineWidth = 2;
      ctx.strokeStyle = this.theme === 'dark' ? 'oklch(0.16 0.008 230)' : 'oklch(1 0 0)';
      ctx.stroke();

      ctx.fillStyle = this.ink(false);
      ctx.font = "600 12px ui-sans-serif, system-ui, sans-serif";
      ctx.textAlign = 'center';
      ctx.fillText(s.label, p.x, p.y - radius - 9);
      if (this.mode === 'cluster') {
        ctx.fillStyle = this.ink(true);
        ctx.font = "500 11px ui-monospace, monospace";
        ctx.fillText(`${s.count.toLocaleString('en-US')} elems`, p.x, p.y + 4);
      }
    }
  }
}

/** Builds the active graph view. Swap to BabylonGraphView here when the @babylonjs/core view lands. */
export function createGraphView(canvas: HTMLCanvasElement, data: StudioData): GraphView {
  return new CanvasGraphView(canvas, data);
}
