// The BabylonJS results graph, lazy-loaded behind the GraphView seam. The shell paints the compact 2D
// view immediately, then upgrades to this interactive view: shader-lit nodes, collision-aware screen labels
// that scale and fade with camera distance, working layouts/camera controls, and a selection channel shared
// with the accessible DOM inspector. Node colours come from StudioData.types — the same per-kind colours the
// legend and the 2D placeholder paint — converted from OKLCH to sRGB here, so every surface shows one palette.

import { ArcRotateCamera } from '@babylonjs/core/Cameras/arcRotateCamera';
import { Engine } from '@babylonjs/core/Engines/engine';
import { Color3, Color4 } from '@babylonjs/core/Maths/math.color';
import { Matrix, Vector3 } from '@babylonjs/core/Maths/math.vector';
import { ShaderMaterial } from '@babylonjs/core/Materials/shaderMaterial';
import { CreateLines } from '@babylonjs/core/Meshes/Builders/linesBuilder';
import { CreateSphere } from '@babylonjs/core/Meshes/Builders/sphereBuilder';
import { LinesMesh } from '@babylonjs/core/Meshes/linesMesh';
import { Mesh } from '@babylonjs/core/Meshes/mesh';
import { Scene } from '@babylonjs/core/scene';
import type { Cluster, StudioData } from './data';
import { reflectGraphMode, renderGraphSelection } from './graph';
import type { GraphMode, GraphSelectionListener, GraphTheme, GraphView } from './graph';

const NODE_VERTEX_SHADER = `
precision highp float;
attribute vec3 position;
attribute vec3 normal;
uniform mat4 world;
uniform mat4 worldViewProjection;
varying vec3 vNormalW;
varying vec3 vPositionW;

void main(void) {
  vec4 worldPosition = world * vec4(position, 1.0);
  vPositionW = worldPosition.xyz;
  vNormalW = normalize(mat3(world) * normal);
  gl_Position = worldViewProjection * vec4(position, 1.0);
}`;

const NODE_FRAGMENT_SHADER = `
precision highp float;
uniform vec3 baseColor;
uniform vec3 cameraPosition;
uniform float emphasis;
uniform float darkMode;
varying vec3 vNormalW;
varying vec3 vPositionW;

void main(void) {
  vec3 normal = normalize(vNormalW);
  vec3 viewDirection = normalize(cameraPosition - vPositionW);
  vec3 lightDirection = normalize(vec3(-0.35, 0.8, 0.55));
  float diffuse = 0.68 + max(dot(normal, lightDirection), 0.0) * 0.28;
  float fresnel = pow(1.0 - max(dot(normal, viewDirection), 0.0), 3.6);
  vec3 rim = mix(vec3(0.38, 0.6, 0.86), vec3(0.7, 0.92, 0.42), darkMode);
  float rimLight = fresnel * (0.08 + emphasis * 0.18);
  vec3 colour = baseColor * diffuse + rim * rimLight;
  colour = mix(colour, rim, emphasis * 0.1);
  gl_FragColor = vec4(colour, 1.0);
}`;

interface NodeRecord {
  cluster: Cluster;
  mesh: Mesh;
  material: ShaderMaterial;
  forcePosition: Vector3;
  clusterPosition: Vector3;
  depthPosition: Vector3;
}

interface EdgeRecord {
  line: LinesMesh;
  from: Mesh;
  to: Mesh;
  points: Vector3[];
}

interface GuideRecord {
  line: LinesMesh;
  mode: Extract<GraphMode, 'cluster' | 'depth'>;
}

interface TypeGroupRecord {
  type: string;
  nodes: NodeRecord[];
  label: HTMLElement;
}

interface ScreenRectangle {
  left: number;
  top: number;
  right: number;
  bottom: number;
}

interface LabelCandidate {
  index: number;
  x: number;
  y: number;
  z: number;
  scale: number;
  width: number;
  priority: number;
}

const clamp = (value: number, minimum: number, maximum: number): number => Math.min(maximum, Math.max(minimum, value));
const MAX_RENDERED_NODES = 1_500;
const MAX_RENDERED_EDGES = 3_000;
const MAX_LABEL_CANDIDATES = 160;
const MAX_MAP_LABELS = 48;
const MAX_DEPTH_LABELS = 24;

/** The lightness the node base colours carry; the shader's lighting shifts around it. */
const NODE_COLOUR_LIGHTNESS = 0.66;

/** The distance an edge arrowhead's tip keeps from the target node's centre, standing it off the sphere. */
const EDGE_ARROW_STANDOFF = 0.055;

/** The arrowhead's length from base to tip, in world units. */
const EDGE_ARROW_LENGTH = 0.05;

/** Half the arrowhead's wing spread, in world units. */
const EDGE_ARROW_HALF_WIDTH = 0.028;

/**
 * Converts an OKLCH colour to an sRGB Color3 (OKLab → linear sRGB → gamma-encoded), so the 3D nodes carry
 * exactly the per-kind hues the CSS legend and the 2D placeholder paint with oklch().
 */
function oklchToColor3(lightness: number, chroma: number, hueDegrees: number): Color3 {
  const hueRadians = hueDegrees * Math.PI / 180;
  const a = chroma * Math.cos(hueRadians);
  const b = chroma * Math.sin(hueRadians);
  const l = lightness + 0.3963377774 * a + 0.2158037573 * b;
  const m = lightness - 0.1055613458 * a - 0.0638541728 * b;
  const s = lightness - 0.0894841775 * a - 1.2914855480 * b;
  const l3 = l * l * l;
  const m3 = m * m * m;
  const s3 = s * s * s;
  const red = 4.0767416621 * l3 - 3.3077115913 * m3 + 0.2309699292 * s3;
  const green = -1.2684380046 * l3 + 2.6097574011 * m3 - 0.3413193965 * s3;
  const blue = -0.0041960863 * l3 - 0.7034186147 * m3 + 1.7076147010 * s3;
  const encode = (channel: number): number => {
    const bounded = clamp(channel, 0, 1);

    return bounded <= 0.0031308 ? 12.92 * bounded : 1.055 * Math.pow(bounded, 1 / 2.4) - 0.055;
  };

  return new Color3(encode(red), encode(green), encode(blue));
}

/** Whether two screen-space rectangles overlap, with a small readability gutter. */
function overlaps(left: ScreenRectangle, right: ScreenRectangle): boolean {
  const gutter = 4;

  return left.left < right.right + gutter && left.right + gutter > right.left
    && left.top < right.bottom + gutter && left.bottom + gutter > right.top;
}

/** Keeps a deterministic, type-inclusive subset when the current renderer receives more detail than it can draw responsibly. */
function renderableClusters(clusters: readonly Cluster[], maximum: number): Cluster[] {
  if (clusters.length <= maximum) {
    return [...clusters];
  }

  const indexes = new Set<number>();
  const seenTypes = new Set<string>();
  clusters.forEach((cluster, index) => {
    if (indexes.size < maximum && !seenTypes.has(cluster.type)) {
      indexes.add(index);
      seenTypes.add(cluster.type);
    }
  });
  for (let slot = 0; slot < maximum && indexes.size < maximum; slot += 1) {
    indexes.add(Math.min(clusters.length - 1, Math.floor(((slot + 0.5) * clusters.length) / maximum)));
  }
  for (let index = 0; indexes.size < maximum && index < clusters.length; index += 1) {
    indexes.add(index);
  }

  return [...indexes].sort((left, right) => left - right).slice(0, maximum).map((index) => clusters[index]);
}

/** Returns one flat frame around a set of planar positions; cluster mode should not look like stacked boxes. */
function framePath(points: readonly Vector3[], minimumSize: Vector3, padding: Vector3): Vector3[] {
  let lowX = points[0].x;
  let highX = points[0].x;
  let lowY = points[0].y;
  let highY = points[0].y;
  for (const point of points.slice(1)) {
    lowX = Math.min(lowX, point.x);
    highX = Math.max(highX, point.x);
    lowY = Math.min(lowY, point.y);
    highY = Math.max(highY, point.y);
  }
  const centreX = (lowX + highX) * 0.5;
  const centreY = (lowY + highY) * 0.5;
  const halfX = Math.max(minimumSize.x, highX - lowX + padding.x * 2) * 0.5;
  const halfY = Math.max(minimumSize.y, highY - lowY + padding.y * 2) * 0.5;
  const z = points.reduce((total, point) => total + point.z, 0) / points.length;

  return [
    new Vector3(centreX - halfX, centreY - halfY, z),
    new Vector3(centreX + halfX, centreY - halfY, z),
    new Vector3(centreX + halfX, centreY + halfY, z),
    new Vector3(centreX - halfX, centreY + halfY, z),
    new Vector3(centreX - halfX, centreY - halfY, z)
  ];
}

class BabylonGraphView implements GraphView {
  private readonly engine: Engine;
  private readonly scene: Scene;
  private readonly camera: ArcRotateCamera;
  private readonly nodes: NodeRecord[] = [];
  private readonly edges: EdgeRecord[] = [];
  private readonly guides: GuideRecord[] = [];
  private readonly typeGroups: TypeGroupRecord[] = [];
  private readonly labelSlots: HTMLElement[] = [];
  private readonly labelCandidateIndexes: number[] = [];
  private readonly identity = Matrix.Identity();
  private readonly scratchDirection = new Vector3();
  private readonly scratchView = new Vector3();
  private readonly scratchWing = new Vector3();
  private readonly scratchBase = new Vector3();
  private readonly typeLabels: Map<string, string>;
  private readonly typeColours: Map<string, Color3>;
  private readonly fallbackColour = Color3.FromHexString('#439AFF');
  private readonly nodeIndexByMesh = new Map<number, number>();
  private readonly tooltip: HTMLElement | null;
  private readonly labelLayer: HTMLElement | null;
  private readonly showAllLabels: boolean;
  private readonly lightEdgeColour = Color3.FromHexString('#395976');
  private readonly darkEdgeColour = Color3.FromHexString('#B8D4EE');
  private readonly lightFocusEdgeColour = Color3.FromHexString('#176FBF');
  private readonly darkFocusEdgeColour = Color3.FromHexString('#B4FF57');
  private readonly reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  private readonly keydown: (event: KeyboardEvent) => void;
  private readonly doubleClick: () => void;
  private mode: GraphMode = 'force';
  private theme: GraphTheme = 'light';
  private selectedIndex = -1;
  private hoveredIndex = -1;

  /** Builds the scene and starts its render loop. */
  constructor(
    private readonly canvas: HTMLCanvasElement,
    data: StudioData,
    private readonly selectionChanged: GraphSelectionListener
  ) {
    this.engine = new Engine(canvas, true, { adaptToDeviceRatio: true, antialias: true });
    this.scene = new Scene(this.engine);
    this.scene.clearColor = new Color4(0, 0, 0, 0);
    this.typeLabels = new Map(data.types.map((type) => [type.key, type.label]));
    this.typeColours = new Map(data.types.map((type) => [type.key, oklchToColor3(NODE_COLOUR_LIGHTNESS, type.c, type.hue)]));
    this.tooltip = document.getElementById('graph-tooltip');
    this.labelLayer = document.getElementById('graph-label-layer');
    this.labelLayer?.replaceChildren();
    const visibleClusters = renderableClusters(data.clusters, MAX_RENDERED_NODES);
    this.showAllLabels = data.clusters.length <= 80;
    this.canvas.dataset.graphLabelPolicy = this.showAllLabels ? 'all' : 'focus';
    this.canvas.dataset.graphLabelRenderer = 'screen-space';
    this.canvas.dataset.graphSourceNodes = String(data.clusters.length);
    this.canvas.dataset.graphRenderedNodes = String(visibleClusters.length);

    this.camera = new ArcRotateCamera('graph-camera', Math.PI / 2, Math.PI / 2.55, 4.6, Vector3.Zero(), this.scene);
    this.camera.attachControl(canvas, true);
    this.camera.lowerRadiusLimit = 2.2;
    this.camera.upperRadiusLimit = 10;
    this.camera.wheelDeltaPercentage = 0.035;
    this.camera.zoomToMouseLocation = true;
    this.camera.panningSensibility = 900;

    const byId = new Map<string, Mesh>();
    for (const [index, cluster] of visibleClusters.entries()) {
      const depth = ((index % 5) - 2) * 0.28;
      const forcePosition = new Vector3(cluster.x * 1.5, cluster.y * 1.42, depth * 0.28);
      const clusterPosition = new Vector3(cluster.x * 1.58, cluster.y * 1.55, 0);
      const depthPosition = new Vector3(cluster.x * 1.42, cluster.y * 1.24, depth);
      const diameter = 0.09 + Math.min(0.05, Math.log2(Math.max(1, cluster.count) + 1) * 0.008);
      const mesh = CreateSphere(`node-${cluster.id}`, { diameter, segments: 16 }, this.scene);
      mesh.position.copyFrom(forcePosition);
      mesh.isPickable = true;

      const material = new ShaderMaterial(
        `shader-${cluster.id}`,
        this.scene,
        { vertexSource: NODE_VERTEX_SHADER, fragmentSource: NODE_FRAGMENT_SHADER },
        {
          attributes: ['position', 'normal'],
          uniforms: ['world', 'worldViewProjection', 'emphasis', 'baseColor', 'cameraPosition', 'darkMode']
        }
      );
      material.setColor3('baseColor', this.typeColours.get(cluster.type) ?? this.fallbackColour);
      material.setFloat('emphasis', 0);
      material.setFloat('darkMode', 0);
      mesh.material = material;

      const record: NodeRecord = { cluster, mesh, material, forcePosition, clusterPosition, depthPosition };
      this.nodes.push(record);
      this.nodeIndexByMesh.set(mesh.uniqueId, index);
      byId.set(cluster.id, mesh);
    }

    for (const [fromId, toId] of data.links) {
      if (this.edges.length >= MAX_RENDERED_EDGES) {
        break;
      }
      const from = byId.get(fromId);
      const to = byId.get(toId);
      if (from !== undefined && to !== undefined) {
        const points = [new Vector3(), new Vector3(), new Vector3(), new Vector3(), new Vector3()];
        this.updateEdgeGeometry(from, to, points);
        const line = CreateLines(`edge-${fromId}-${toId}`, { points, updatable: true }, this.scene);
        line.color = Color3.FromHexString('#71879D');
        line.alpha = 0.82;
        line.isPickable = false;
        this.edges.push({ line, from, to, points });
      }
    }
    this.canvas.dataset.graphSourceEdges = String(data.links.length);
    this.canvas.dataset.graphRenderedEdges = String(this.edges.length);
    this.canvas.dataset.graphEdgeStyle = 'directed';
    if (visibleClusters.length < data.clusters.length || this.edges.length < data.links.length) {
      const hud = document.getElementById('graph-hud-pill');
      if (hud !== null) {
        hud.textContent = `showing ${visibleClusters.length.toLocaleString()} of ${data.clusters.length.toLocaleString()} nodes · ${this.edges.length.toLocaleString()} of ${data.links.length.toLocaleString()} edges · ${data.types.length.toLocaleString()} kinds`;
      }
    }

    const nodesByType = new Map<string, NodeRecord[]>();
    for (const node of this.nodes) {
      const typeNodes = nodesByType.get(node.cluster.type) ?? [];
      typeNodes.push(node);
      nodesByType.set(node.cluster.type, typeNodes);
    }
    for (const [type, typeNodes] of nodesByType) {
      const colour = this.typeColours.get(type) ?? this.fallbackColour;
      const clusterGuide = CreateLines(
        `cluster-frame-${type}`,
        { points: framePath(typeNodes.map((node) => node.clusterPosition), new Vector3(0.38, 0.38, 0), new Vector3(0.16, 0.16, 0)) },
        this.scene
      );
      clusterGuide.color = colour;
      clusterGuide.alpha = 0.72;
      clusterGuide.isPickable = false;
      clusterGuide.visibility = 0;
      this.guides.push({ line: clusterGuide, mode: 'cluster' });

      const groupLabel = document.createElement('span');
      groupLabel.className = 'graph-cluster-tag';
      groupLabel.dataset.kind = type;
      const itemCount = typeNodes.reduce((total, node) => total + node.cluster.count, 0);
      groupLabel.textContent = `${this.typeLabels.get(type) ?? type} · ${itemCount.toLocaleString()}`;
      groupLabel.hidden = true;
      this.labelLayer?.append(groupLabel);
      this.typeGroups.push({ type, nodes: typeNodes, label: groupLabel });
    }

    const candidateIndexes = new Set<number>();
    for (const group of this.typeGroups) {
      const representative = group.nodes.reduce((best, node) => node.cluster.count > best.cluster.count ? node : best);
      const index = this.nodeIndexByMesh.get(representative.mesh.uniqueId);
      if (index !== undefined) {
        candidateIndexes.add(index);
      }
    }
    const rankedIndexes = this.nodes.map((_node, index) => index).sort((left, right) => {
      const countDifference = this.nodes[right].cluster.count - this.nodes[left].cluster.count;

      return countDifference !== 0 ? countDifference : left - right;
    });
    for (const index of rankedIndexes) {
      if (candidateIndexes.size >= MAX_LABEL_CANDIDATES) {
        break;
      }
      candidateIndexes.add(index);
    }
    this.labelCandidateIndexes.push(...candidateIndexes);
    const labelSlotCount = Math.min(MAX_MAP_LABELS, this.nodes.length);
    for (let index = 0; index < labelSlotCount; index += 1) {
      const label = document.createElement('span');
      label.className = 'graph-node-label';
      label.hidden = true;
      this.labelLayer?.append(label);
      this.labelSlots.push(label);
    }

    const groundY = -1.04;
    for (let index = -4; index <= 4; index += 1) {
      const offset = index * 0.3;
      const gridLines: Array<[string, Vector3[]]> = [
        ['x', [new Vector3(-1.65, groundY, offset), new Vector3(1.65, groundY, offset)]],
        ['z', [new Vector3(offset, groundY, -1.65), new Vector3(offset, groundY, 1.65)]]
      ];
      for (const [axis, points] of gridLines) {
        const gridLine = CreateLines(`depth-grid-${axis}-${index}`, { points }, this.scene);
        gridLine.color = Color3.FromHexString('#71879D');
        gridLine.alpha = index === 0 ? 0.72 : 0.44;
        gridLine.isPickable = false;
        gridLine.visibility = 0;
        this.guides.push({ line: gridLine, mode: 'depth' });
      }
    }
    if (this.nodes.length <= 200) {
      for (const node of this.nodes) {
        const stem = CreateLines(
          `depth-stem-${node.cluster.id}`,
          { points: [node.depthPosition, new Vector3(node.depthPosition.x, groundY, node.depthPosition.z)] },
          this.scene
        );
        stem.color = this.typeColours.get(node.cluster.type) ?? this.fallbackColour;
        stem.alpha = 0.48;
        stem.isPickable = false;
        stem.visibility = 0;
        this.guides.push({ line: stem, mode: 'depth' });
      }
    }

    this.scene.onPointerMove = (_event, pickInfo) => {
      const next = pickInfo.hit && pickInfo.pickedMesh !== null
        ? (this.nodeIndexByMesh.get(pickInfo.pickedMesh.uniqueId) ?? -1)
        : -1;
      this.hoveredIndex = next;
      canvas.style.cursor = next >= 0 ? 'pointer' : 'grab';
      this.updateTooltip(_event, next);
    };
    this.scene.onPointerDown = (_event, pickInfo) => {
      const next = pickInfo.hit && pickInfo.pickedMesh !== null
        ? (this.nodeIndexByMesh.get(pickInfo.pickedMesh.uniqueId) ?? -1)
        : -1;
      if (next >= 0) {
        this.select(next);
      } else {
        this.selectedIndex = -1;
        this.selectionChanged(null);
      }
    };

    this.keydown = this.onKeyDown.bind(this);
    this.doubleClick = this.fit.bind(this);
    canvas.addEventListener('keydown', this.keydown);
    canvas.addEventListener('dblclick', this.doubleClick);
    this.setTheme('light');
    this.fit();
    this.canvas.dataset.graphRenderer = 'babylon';
    this.engine.runRenderLoop(() => this.renderFrame());
    this.selectionChanged(null);
  }

  /** @inheritdoc */
  setMode(mode: GraphMode): void {
    this.mode = mode;
    reflectGraphMode(this.canvas, mode);
    this.camera.lowerAlphaLimit = null;
    this.camera.upperAlphaLimit = null;
    this.camera.lowerBetaLimit = null;
    this.camera.upperBetaLimit = null;
    // Spatial orientation should remain under the user's control; an automatically moving frame makes
    // depth and label occlusion harder to understand.
    this.camera.useAutoRotationBehavior = false;
    if (mode === 'cluster') {
      this.camera.beta = Math.PI / 2.35;
      this.camera.radius = 4.8;
    } else if (mode === 'depth') {
      this.camera.beta = Math.PI / 3.25;
      this.camera.radius = 4.8;
    } else {
      this.camera.beta = Math.PI / 2.55;
      this.camera.radius = 4.6;
    }
    if (mode !== 'depth') {
      this.camera.lowerAlphaLimit = this.camera.alpha;
      this.camera.upperAlphaLimit = this.camera.alpha;
      this.camera.lowerBetaLimit = this.camera.beta;
      this.camera.upperBetaLimit = this.camera.beta;
    }
    for (const guide of this.guides) {
      guide.line.visibility = guide.mode === mode ? 1 : 0;
    }
    this.canvas.dataset.graphGuides = mode === 'cluster' ? 'clusters' : (mode === 'depth' ? 'depth' : 'none');
    this.canvas.dataset.graphZoom = (1 / this.camera.radius).toFixed(3);
  }

  /** @inheritdoc */
  setTheme(theme: GraphTheme): void {
    this.theme = theme;
    const dark = theme === 'dark';
    for (const node of this.nodes) {
      node.material.setFloat('darkMode', dark ? 1 : 0);
    }
    for (const guide of this.guides) {
      if (guide.line.name.startsWith('depth-grid-')) {
        guide.line.color = dark ? Color3.FromHexString('#9EBAD4') : Color3.FromHexString('#526B83');
        guide.line.alpha = /-0$/.test(guide.line.name) ? (dark ? 0.42 : 0.72) : (dark ? 0.24 : 0.44);
      } else if (guide.line.name.startsWith('depth-stem-')) {
        guide.line.alpha = dark ? 0.3 : 0.48;
      } else if (guide.line.name.startsWith('cluster-frame-')) {
        guide.line.alpha = dark ? 0.54 : 0.72;
      }
    }
  }

  /** @inheritdoc */
  resize(): void {
    this.engine.resize();
  }

  /** @inheritdoc */
  zoomBy(direction: number): void {
    this.camera.radius = clamp(this.camera.radius * (direction > 0 ? 0.82 : 1.2), this.camera.lowerRadiusLimit ?? 1.35, this.camera.upperRadiusLimit ?? 8);
    this.canvas.dataset.graphZoom = (1 / this.camera.radius).toFixed(3);
  }

  /** @inheritdoc */
  fit(): void {
    this.camera.setTarget(Vector3.Zero());
    this.camera.alpha = Math.PI / 2;
    this.camera.beta = this.mode === 'cluster' ? Math.PI / 2.35 : (this.mode === 'depth' ? Math.PI / 3.25 : Math.PI / 2.55);
    this.camera.radius = this.mode === 'cluster' ? 4.8 : (this.mode === 'depth' ? 4.8 : 4.6);
    this.canvas.dataset.graphZoom = (1 / this.camera.radius).toFixed(3);
  }

  /** @inheritdoc */
  render(): void {
    this.scene.render();
  }

  /** @inheritdoc */
  dispose(): void {
    this.engine.stopRenderLoop();
    this.canvas.removeEventListener('keydown', this.keydown);
    this.canvas.removeEventListener('dblclick', this.doubleClick);
    if (this.tooltip !== null) {
      this.tooltip.hidden = true;
    }
    this.labelLayer?.replaceChildren();
    this.selectionChanged(null);
    this.scene.dispose();
    this.engine.dispose();
  }

  /**
   * Projects a bounded candidate set into a fixed DOM-label pool. Priority + rectangle collision keeps labels
   * legible while orbiting, and depth mode scales/fades background labels instead of stacking billboard planes.
   */
  private updateScreenLabels(): void {
    if (this.labelLayer === null) {
      return;
    }

    const width = this.canvas.clientWidth;
    const height = this.canvas.clientHeight;
    if (width <= 0 || height <= 0) {
      return;
    }

    const viewport = this.camera.viewport.toGlobal(width, height);
    const transform = this.scene.getTransformMatrix();
    const project = (position: Vector3): Vector3 => Vector3.Project(position, this.identity, transform, viewport);
    const occupied: ScreenRectangle[] = [
      { left: 0, top: 0, right: Math.min(330, width * 0.62), bottom: 102 },
      { left: Math.max(0, width - 170), top: 0, right: width, bottom: 62 },
      { left: 0, top: Math.max(0, height - 94), right: Math.min(220, width), bottom: height },
      { left: Math.max(0, width - 160), top: Math.max(0, height - 62), right: width, bottom: height }
    ];

    let visibleGroupLabels = 0;
    for (const group of this.typeGroups) {
      if (this.mode === 'force') {
        group.label.hidden = true;
        continue;
      }
      const projected = group.nodes.map((node) => project(node.mesh.position)).filter((point) => point.z >= 0 && point.z <= 1);
      if (projected.length === 0) {
        group.label.hidden = true;
        continue;
      }
      const rawX = projected.reduce((total, point) => total + point.x, 0) / projected.length;
      const rawY = Math.min(...projected.map((point) => point.y)) - 18;
      const tagWidth = clamp(36 + (group.label.textContent?.length ?? 0) * 6.1, 92, 210);
      const x = clamp(rawX, tagWidth * 0.5 + 8, width - tagWidth * 0.5 - 8);
      const desiredY = clamp(rawY, 24, height - 76);
      let y = desiredY;
      let rectangle: ScreenRectangle | null = null;
      for (const offset of [0, 24, 48, -24]) {
        const candidateY = clamp(desiredY + offset, 24, height - 76);
        const candidate = { left: x - tagWidth * 0.5, top: candidateY - 19, right: x + tagWidth * 0.5, bottom: candidateY + 3 };
        if (!occupied.some((other) => overlaps(candidate, other))) {
          y = candidateY;
          rectangle = candidate;
          break;
        }
      }
      if (rectangle === null) {
        group.label.hidden = true;
        continue;
      }
      occupied.push(rectangle);
      group.label.style.transform = `translate3d(${x}px, ${y}px, 0) translate(-50%, -100%)`;
      group.label.style.opacity = this.mode === 'depth' ? '0.82' : '1';
      group.label.hidden = false;
      visibleGroupLabels += 1;
    }

    const candidateIndexes = new Set(this.labelCandidateIndexes);
    if (this.selectedIndex >= 0) {
      candidateIndexes.add(this.selectedIndex);
    }
    if (this.hoveredIndex >= 0) {
      candidateIndexes.add(this.hoveredIndex);
    }
    const candidates: LabelCandidate[] = [];
    for (const index of candidateIndexes) {
      const node = this.nodes[index];
      if (node === undefined) {
        continue;
      }
      const point = project(node.mesh.position);
      if (point.z < 0 || point.z > 1 || point.x < -80 || point.x > width + 80 || point.y < -40 || point.y > height + 40) {
        continue;
      }
      const selected = index === this.selectedIndex;
      const hovered = index === this.hoveredIndex;
      const cameraDistance = Vector3.Distance(this.camera.position, node.mesh.position);
      const scale = selected || hovered
        ? 1
        : (this.mode === 'depth' ? clamp(this.camera.radius / Math.max(cameraDistance, 0.1), 0.64, 0.94) : 0.92);
      const maximumWidth = selected || hovered ? 270 : 224;
      const labelWidth = clamp(20 + node.cluster.label.length * 6.25, 72, maximumWidth) * scale;
      const x = clamp(point.x, labelWidth * 0.5 + 8, width - labelWidth * 0.5 - 8);
      const y = clamp(point.y - 11 * scale, 26, height - 54);
      const focusPriority = selected ? 1_000_000 : (hovered ? 900_000 : 0);
      candidates.push({
        index,
        x,
        y,
        z: point.z,
        scale,
        width: labelWidth,
        priority: focusPriority + node.cluster.count * 100 + (1 - point.z) * 10 - index * 0.0001
      });
    }
    candidates.sort((left, right) => right.priority - left.priority);

    for (const label of this.labelSlots) {
      label.hidden = true;
    }
    const maximumLabels = this.mode === 'depth' ? MAX_DEPTH_LABELS : MAX_MAP_LABELS;
    let visibleLabels = 0;
    for (const candidate of candidates) {
      if (visibleLabels >= maximumLabels || visibleLabels >= this.labelSlots.length) {
        break;
      }
      const focused = candidate.index === this.selectedIndex || candidate.index === this.hoveredIndex;
      const rectangle = {
        left: candidate.x - candidate.width * 0.5,
        top: candidate.y - 22 * candidate.scale,
        right: candidate.x + candidate.width * 0.5,
        bottom: candidate.y + 2
      };
      if (!focused && occupied.some((other) => overlaps(rectangle, other))) {
        continue;
      }
      occupied.push(rectangle);
      const node = this.nodes[candidate.index];
      const label = this.labelSlots[visibleLabels];
      label.textContent = node.cluster.label;
      label.dataset.state = candidate.index === this.selectedIndex ? 'selected' : (candidate.index === this.hoveredIndex ? 'hovered' : 'context');
      label.style.transform = `translate3d(${candidate.x}px, ${candidate.y}px, 0) translate(-50%, -100%) scale(${candidate.scale.toFixed(3)})`;
      label.style.opacity = focused ? '1' : (this.mode === 'depth' ? String(clamp(0.34 + candidate.scale * 0.54, 0.58, 0.86)) : '0.9');
      label.style.zIndex = focused ? '3' : String(Math.round((1 - candidate.z) * 2));
      label.hidden = false;
      visibleLabels += 1;
    }
    this.canvas.dataset.graphVisibleLabels = String(visibleLabels);
    this.canvas.dataset.graphVisibleClusterLabels = String(visibleGroupLabels);
  }

  /** Places real node details next to the pointer without turning hover into selection. */
  private updateTooltip(event: { offsetX: number; offsetY: number }, index: number): void {
    if (this.tooltip === null) {
      return;
    }
    if (index < 0) {
      this.tooltip.hidden = true;
      return;
    }
    const node = this.nodes[index].cluster;
    const kind = this.typeLabels.get(node.type) ?? node.type;
    this.tooltip.textContent = node.count === 1 ? `${node.label} · ${kind}` : `${node.label} · ${kind} · ${node.count.toLocaleString()} records`;
    this.tooltip.style.left = `${clamp(event.offsetX + 14, 12, Math.max(12, this.canvas.clientWidth - 260))}px`;
    this.tooltip.style.top = `${clamp(event.offsetY + 14, 12, Math.max(12, this.canvas.clientHeight - 64))}px`;
    this.tooltip.hidden = false;
  }

  /** Moves the selection and announces it through the shared inspector listener. */
  private select(index: number): void {
    if (this.nodes.length === 0) {
      return;
    }
    this.selectedIndex = (index + this.nodes.length) % this.nodes.length;
    const node = this.nodes[this.selectedIndex].cluster;
    this.selectionChanged({
      id: node.id,
      label: node.label,
      type: node.type,
      typeLabel: this.typeLabels.get(node.type) ?? node.type,
      count: node.count
    });
  }

  /** Arrow keys traverse nodes; +/− zoom; Enter re-announces selection; 0 or Home restores the camera. */
  private onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
      event.preventDefault();
      this.select(this.selectedIndex + 1);
    } else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
      event.preventDefault();
      this.select(this.selectedIndex <= 0 ? this.nodes.length - 1 : this.selectedIndex - 1);
    } else if (event.key === 'Enter' && this.selectedIndex >= 0) {
      event.preventDefault();
      this.select(this.selectedIndex);
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

  /**
   * Writes a directed edge's polyline into its five-point buffer: source → tip, then the two arrowhead
   * wings with the tip repeated between them, the wings turned to face the camera so the arrow reads from
   * any orbit. The graph query rows' from/to order is the direction; an edge too short to point anywhere
   * collapses the arrow onto the target instead of drawing a degenerate one.
   */
  private updateEdgeGeometry(from: Mesh, to: Mesh, points: Vector3[]): void {
    points[0].copyFrom(from.position);
    to.position.subtractToRef(from.position, this.scratchDirection);
    const length = this.scratchDirection.length();
    if (length <= EDGE_ARROW_STANDOFF + EDGE_ARROW_LENGTH) {
      for (let index = 1; index < points.length; index += 1) {
        points[index].copyFrom(to.position);
      }

      return;
    }

    this.scratchDirection.scaleInPlace(1 / length);
    const tip = points[1];
    tip.copyFrom(this.scratchDirection).scaleInPlace(-EDGE_ARROW_STANDOFF).addInPlace(to.position);
    points[3].copyFrom(tip);
    this.camera.position.subtractToRef(tip, this.scratchView);
    Vector3.CrossToRef(this.scratchDirection, this.scratchView, this.scratchWing);
    if (this.scratchWing.lengthSquared() < 1e-8) {
      this.scratchView.set(0, 1, 0);
      Vector3.CrossToRef(this.scratchDirection, this.scratchView, this.scratchWing);
      if (this.scratchWing.lengthSquared() < 1e-8) {
        this.scratchWing.set(1, 0, 0);
      }
    }

    this.scratchWing.normalize();
    this.scratchBase.copyFrom(this.scratchDirection).scaleInPlace(-EDGE_ARROW_LENGTH).addInPlace(tip);
    points[2].copyFrom(this.scratchWing).scaleInPlace(EDGE_ARROW_HALF_WIDTH).addInPlace(this.scratchBase);
    points[4].copyFrom(this.scratchWing).scaleInPlace(-EDGE_ARROW_HALF_WIDTH).addInPlace(this.scratchBase);
  }

  /** Advances material uniforms, eases layout changes, updates edges and paints one frame. */
  private renderFrame(): void {
    this.nodes.forEach((node, index) => {
      const target = this.mode === 'cluster'
        ? node.clusterPosition
        : (this.mode === 'depth' ? node.depthPosition : node.forcePosition);
      Vector3.LerpToRef(node.mesh.position, target, this.reducedMotion ? 1 : 0.09, node.mesh.position);
      const emphasis = index === this.selectedIndex ? 1 : (index === this.hoveredIndex ? 0.55 : 0);
      const targetScale = emphasis === 1 ? 1.45 : (emphasis > 0 ? 1.2 : 1);
      const scale = node.mesh.scaling.x + (targetScale - node.mesh.scaling.x) * (this.reducedMotion ? 1 : 0.1);
      node.mesh.scaling.setAll(scale);
      node.material.setVector3('cameraPosition', this.camera.position);
      node.material.setFloat('emphasis', emphasis);
    });

    const focusIndex = this.selectedIndex >= 0 ? this.selectedIndex : this.hoveredIndex;
    const focusMesh = focusIndex >= 0 ? this.nodes[focusIndex]?.mesh : undefined;
    const edgeColour = this.theme === 'dark' ? this.darkEdgeColour : this.lightEdgeColour;
    const focusEdgeColour = this.theme === 'dark' ? this.darkFocusEdgeColour : this.lightFocusEdgeColour;
    let focusedEdges = 0;
    for (const edge of this.edges) {
      this.updateEdgeGeometry(edge.from, edge.to, edge.points);
      CreateLines(edge.line.name, { points: edge.points, instance: edge.line });
      const incident = focusMesh !== undefined && (edge.from === focusMesh || edge.to === focusMesh);
      const baseAlpha = this.theme === 'dark' ? 0.76 : 0.84;
      edge.line.alpha = focusMesh === undefined ? baseAlpha : (incident ? 1 : 0.14);
      edge.line.color = incident ? focusEdgeColour : edgeColour;
      if (incident) {
        focusedEdges += 1;
      }
    }
    this.scene.render();
    this.updateScreenLabels();
    this.canvas.dataset.graphFocusedEdges = String(focusedEdges);
  }
}

/**
 * Builds the BabylonJS graph view on the given canvas; called via dynamic import so its chunk loads on
 * demand. The studio hands it a detached fresh canvas (a canvas is bound to its first context family) and
 * swaps it into the DOM only after this construction succeeds, so a failed upgrade leaves the 2D
 * placeholder standing.
 */
export function createBabylonGraphView(
  canvas: HTMLCanvasElement,
  data: StudioData,
  selectionChanged: GraphSelectionListener = renderGraphSelection
): GraphView {
  return new BabylonGraphView(canvas, data, selectionChanged);
}
