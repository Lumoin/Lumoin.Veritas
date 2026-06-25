// The sample datasets the in-browser engine can load. In the no-scaffolding (WASM) deployment the user
// switches between them at runtime — each is parsed into the engine via the interop's LoadTurtleAsync — so
// the same shell demonstrates more than one supply-chain story. Each dataset pairs its Turtle with a
// showcase query that exercises it.
import { BATTERY_SAMPLE, BATTERY_QUERY } from './battery-sample';
import { TEXTILE_SAMPLE, TEXTILE_QUERY } from './textile-sample';

/** A loadable sample dataset: a Turtle document and a query that showcases it. */
export interface StudioDataset {
  /** Stable identifier used by the dataset switch. */
  readonly id: string;

  /** Short human label shown in the dataset switch and the active-dataset readout. */
  readonly label: string;

  /** One-line description of the supply-chain story the dataset tells. */
  readonly description: string;

  /** The Turtle document loaded into the engine when the dataset is selected. */
  readonly turtle: string;

  /** A SPARQL query that exercises the dataset (placed in the editor on selection). */
  readonly query: string;
}

/** The available sample datasets, in display order. */
export const DATASETS: readonly StudioDataset[] = [
  {
    id: 'textile',
    label: 'Cotton → garment chain',
    description: 'A UNTP cotton-to-garment supply chain with WRI Aqueduct water-risk per river basin.',
    turtle: TEXTILE_SAMPLE,
    query: TEXTILE_QUERY
  },
  {
    id: 'battery',
    label: 'EV battery passports',
    description: 'EU Battery Regulation digital product passports with recycled-cobalt shares.',
    turtle: BATTERY_SAMPLE,
    query: BATTERY_QUERY
  }
];

/** The dataset loaded on first boot (the cotton→garment chain — the richer supply-chain story). */
export const DEFAULT_DATASET: StudioDataset = DATASETS[0];

/**
 * Looks up a dataset by id.
 * @param id The dataset identifier.
 * @returns The matching dataset, or undefined when no dataset has that id.
 */
export function datasetById(id: string): StudioDataset | undefined {
  return DATASETS.find((dataset) => dataset.id === id);
}
