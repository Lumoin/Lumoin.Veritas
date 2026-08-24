// The Raw result view's projection. The Raw tab sits in the result-view tab strip beside Graph and Table, so
// it shows the last run's answer as the document it is: the W3C SPARQL results JSON for a run that answered,
// the engine's span-bearing failure document for one that did not. The transport parses the wire text into
// these documents on every tier (the native bridge hands over an object, never text), so the view serializes
// the document back — one raw face, identical whichever source answered.

import type { SparqlQueryError, SparqlResultsDocument } from './components/sparql-results';

/**
 * Serializes a run's results document as the Raw view shows it.
 * @param document The SPARQL results document the run answered with.
 * @returns The indented JSON text.
 */
export function rawResultsText(document: SparqlResultsDocument): string {
  return JSON.stringify(document, null, 2);
}

/**
 * Serializes a failed run's error document as the Raw view shows it.
 * @param error The engine's error document, with whatever diagnostics it carried.
 * @returns The indented JSON text.
 */
export function rawErrorText(error: SparqlQueryError): string {
  return JSON.stringify(error, null, 2);
}
