// The Raw view's unit rows: the view shows the answer document itself, whole and unabridged, for every
// answer shape the transport hands it — SELECT bindings, an ASK verdict, an empty solution set, and a
// failure document with its diagnostics. A row here fails the moment the projection starts telling a story
// of its own instead of serializing what the engine answered.

import { describe, expect, it } from 'vitest';
import { rawErrorText, rawResultsText } from './raw-view';
import type { SparqlResultsDocument } from './components/sparql-results';

/** A two-solution SELECT answer, as the transport parses it off any tier. */
const selectDocument: SparqlResultsDocument = {
  head: { vars: ['firstName', 'lastName'] },
  results: {
    bindings: [
      { firstName: { type: 'literal', value: 'Frank' }, lastName: { type: 'literal', value: 'Berg' } },
      { firstName: { type: 'literal', value: 'Gita' }, lastName: { type: 'literal', value: 'Patel' } }
    ]
  }
};

describe('rawResultsText', () => {
  it('serializes a SELECT answer with its variables and every solution', () => {
    const text = rawResultsText(selectDocument);
    expect(JSON.parse(text)).toEqual(selectDocument);
    expect(text).toContain('"firstName"');
    expect(text).toContain('Gita');
  });

  it('indents the document so the view reads as a document, not one line', () => {
    expect(rawResultsText(selectDocument)).toContain('\n  "head"');
  });

  it('serializes an ASK verdict as the boolean the engine answered', () => {
    expect(JSON.parse(rawResultsText({ head: { vars: [] }, boolean: false }))).toEqual({ head: { vars: [] }, boolean: false });
  });

  it('serializes an empty solution set as an empty bindings array', () => {
    const text = rawResultsText({ head: { vars: ['s'] }, results: { bindings: [] } });
    expect(JSON.parse(text)).toEqual({ head: { vars: ['s'] }, results: { bindings: [] } });
  });
});

describe('rawErrorText', () => {
  it('serializes a failure document with its span-bearing diagnostics', () => {
    const error = {
      error: 'Undefined prefix bat:',
      diagnostics: [{ code: 'SP0012', severity: 'error', message: 'prefix not declared', startLine: 3, startColumn: 7 }]
    };
    const text = rawErrorText(error);
    expect(JSON.parse(text)).toEqual(error);
    expect(text).toContain('SP0012');
  });

  it('serializes a failure that carried no diagnostics as an empty array, never a narrative', () => {
    expect(JSON.parse(rawErrorText({ error: 'HTTP 500', diagnostics: [] }))).toEqual({ error: 'HTTP 500', diagnostics: [] });
  });
});
