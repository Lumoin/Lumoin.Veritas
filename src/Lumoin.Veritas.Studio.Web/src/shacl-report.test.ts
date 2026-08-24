// The conformance projection's unit rows: what the results-SHACL tab renders for a report. The verdict row
// is always first and always carries the report's own conformance flag, every result becomes its own warning
// row carrying the failing node and the constraint that produced it, and a result whose shape ships no
// message still reads as something — the tab never renders a blank row and never renders a narrative of its
// own, so a row here fails the moment the projection stops following the report.

import { describe, expect, it } from 'vitest';
import { shaclReportRows, type ShaclReportView } from './shacl-report';

/** The social network's person/email report the in-browser engine answers with: one profile carries no email. */
const socialReport: ShaclReportView = {
  conforms: false,
  results: [{
    focusNode: 'p10',
    severity: 'Violation',
    constraint: 'MinCountConstraintComponent',
    message: 'Every person profile must carry an email address.'
  }]
};

describe('shaclReportRows', () => {
  it('renders a conforming report as a single ok verdict row', () => {
    const rows = shaclReportRows({ conforms: true, results: [] });
    expect(rows).toHaveLength(1);
    expect(rows[0].tone).toBe('ok');
    expect(rows[0].message).toContain('Conforms');
    expect(rows[0].focus).toBe('sh:conforms true');
  });

  it('renders a failing report as a warning verdict counting the results, then one row per result', () => {
    const rows = shaclReportRows(socialReport);
    expect(rows).toHaveLength(2);
    expect(rows[0].tone).toBe('warn');
    expect(rows[0].message).toContain('1 result(s)');
    expect(rows[0].focus).toBe('sh:conforms false');
    expect(rows[1].tone).toBe('warn');
    expect(rows[1].message).toBe('Every person profile must carry an email address.');
    expect(rows[1].focus).toBe('p10 · MinCountConstraintComponent');
  });

  it('carries every result of a multi-result report, in order', () => {
    const rows = shaclReportRows({
      conforms: false,
      results: [
        { focusNode: 'a', severity: 'Violation', constraint: 'MinCountConstraintComponent', message: 'first' },
        { focusNode: 'b', severity: 'Violation', constraint: 'MinInclusiveConstraintComponent', message: 'second' },
        { focusNode: 'c', severity: 'Warning', constraint: 'DatatypeConstraintComponent', message: 'third' }
      ]
    });
    expect(rows.map((row) => row.message)).toEqual(['Does not conform — 3 result(s).', 'first', 'second', 'third']);
    expect(rows.map((row) => row.focus)).toEqual([
      'sh:conforms false',
      'a · MinCountConstraintComponent',
      'b · MinInclusiveConstraintComponent',
      'c · DatatypeConstraintComponent'
    ]);
  });

  it('falls back to the severity and constraint for a result whose shape ships no message', () => {
    const rows = shaclReportRows({
      conforms: false,
      results: [{ focusNode: 'p10', severity: 'Violation', constraint: 'MinCountConstraintComponent', message: '' }]
    });
    expect(rows[1].message).toBe('Violation · MinCountConstraintComponent');
  });

  it('keeps a conforming verdict ok while still rendering the non-violating results it carries', () => {
    const rows = shaclReportRows({
      conforms: true,
      results: [{ focusNode: 'p3', severity: 'Warning', constraint: 'PatternConstraintComponent', message: 'advisory' }]
    });
    expect(rows[0].tone).toBe('ok');
    expect(rows).toHaveLength(2);
    expect(rows[1].message).toBe('advisory');
  });
});
