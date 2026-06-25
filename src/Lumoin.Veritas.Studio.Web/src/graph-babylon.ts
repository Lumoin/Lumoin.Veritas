// The real results graph on BabylonJS, implementing the GraphView seam from graph.ts. It is imported
// only dynamically (from studio.ts), so @babylonjs/core is code-split into its own chunk — the shell
// paints with the 2D placeholder first, then upgrades when this chunk arrives. Clusters are spheres
// (sized by element count), inter-cluster relations are lines, colour follows the node-type hue.

import {
  ArcRotateCamera,
  Color3,
  Color4,
  Engine,
  HemisphericLight,
  MeshBuilder,
  Scene,
  StandardMaterial,
  Vector3
} from '@babylonjs/core';
import type { StudioData } from './data';
import type { GraphMode, GraphTheme, GraphView } from './graph';

class BabylonGraphView implements GraphView {
  private readonly engine: Engine;
  private readonly scene: Scene;
  private readonly camera: ArcRotateCamera;
  private mode: GraphMode = 'force';

  /** Builds the scene: an arc-rotate camera, a light, a sphere per cluster (sized by count), lines for links. */
  constructor(canvas: HTMLCanvasElement, data: StudioData) {
    this.engine = new Engine(canvas, true, { adaptToDeviceRatio: true, antialias: true });
    this.scene = new Scene(this.engine);
    this.scene.clearColor = new Color4(0, 0, 0, 0);

    this.camera = new ArcRotateCamera('cam', Math.PI / 2, Math.PI / 2.6, 3.4, Vector3.Zero(), this.scene);
    this.camera.attachControl(canvas, true);
    this.camera.lowerRadiusLimit = 1.6;
    this.camera.upperRadiusLimit = 8;
    this.camera.wheelDeltaPercentage = 0.02;
    new HemisphericLight('light', new Vector3(0.3, 1, 0.2), this.scene);

    const typeColor = new Map(data.types.map((t) => [t.key, Color3.FromHSV((t.hue + 360) % 360, 0.45, 0.85)]));
    const at = new Map<string, Vector3>();
    for (const [i, cluster] of data.clusters.entries()) {
      const z = ((i % 3) - 1) * 0.5;
      const position = new Vector3(cluster.x * 1.6, cluster.y * 1.2, z);
      at.set(cluster.id, position);

      const sphere = MeshBuilder.CreateSphere(cluster.id, { diameter: 0.12 + Math.sqrt(cluster.count) / 900, segments: 16 }, this.scene);
      sphere.position = position;
      const material = new StandardMaterial(`m-${cluster.id}`, this.scene);
      material.diffuseColor = typeColor.get(cluster.type) ?? Color3.Gray();
      material.emissiveColor = (typeColor.get(cluster.type) ?? Color3.Gray()).scale(0.35);
      sphere.material = material;
    }

    for (const [a, b] of data.links) {
      const from = at.get(a);
      const to = at.get(b);
      if (from !== undefined && to !== undefined) {
        const line = MeshBuilder.CreateLines(`l-${a}-${b}`, { points: [from, to] }, this.scene);
        line.color = new Color3(0.5, 0.5, 0.55);
        line.alpha = 0.4;
      }
    }

    this.engine.runRenderLoop(() => this.scene.render());
  }

  /** @inheritdoc */
  setMode(mode: GraphMode): void {
    this.mode = mode;
    this.camera.useAutoRotationBehavior = mode === 'depth';
  }

  /** @inheritdoc */
  setTheme(_theme: GraphTheme): void {
    // The canvas is transparent over the themed panel, so the clear colour stays clear in both themes;
    // the mode is what the camera reflects.
    void this.mode;
  }

  /** @inheritdoc */
  resize(): void {
    this.engine.resize();
  }

  /** @inheritdoc */
  render(): void {
    this.scene.render();
  }

  /** @inheritdoc */
  dispose(): void {
    this.engine.stopRenderLoop();
    this.scene.dispose();
    this.engine.dispose();
  }
}

/** Builds the BabylonJS graph view; called via dynamic import so its chunk loads on demand. */
export function createBabylonGraphView(canvas: HTMLCanvasElement, data: StudioData): GraphView {
  return new BabylonGraphView(canvas, data);
}
