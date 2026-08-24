// The SHACL validation report as the results-SHACL tab consumes it: the report view the engine's interop
// answers with, and the projection of that report into the tab's conformance rows (the verdict, then one
// row per result). Pure — the shell assembles the DOM from these rows, so the projection stands on its own.

/** One SHACL validation result row, as the report view carries it. */
export interface ShaclResultView {
  /** The focus node that failed (a short label). */
  focusNode: string;

  /** The result severity (Violation / Warning / Info). */
  severity: string;

  /** The constraint component that produced the result (a short name). */
  constraint: string;

  /** The human-readable message attached to the result; empty when the shape carries none. */
  message: string;
}

/** A SHACL validation report: a conformance verdict and the result rows. */
export interface ShaclReportView {
  /** Whether the data conforms (no violation-severity results). */
  conforms: boolean;

  /** The validation results (violations, warnings, info). */
  results: ShaclResultView[];
}

/** One conformance row the results-SHACL tab renders. */
export interface ShaclRowView {
  /** The row's tone: `ok` for a conforming verdict, `warn` for a failing verdict and every result. */
  tone: 'ok' | 'warn';

  /** The check glyph. */
  mark: string;

  /** The row's message. */
  message: string;

  /** The row's annotation: the conformance flag on the verdict row, the focus node and its constraint on a result row. */
  focus: string;
}

/**
 * Projects a validation report into the tab's conformance rows: the verdict first, then one row per result.
 * A result whose shape carries no message reads as its severity and constraint component.
 * @param report The validation report.
 * @returns The rows, verdict first.
 */
export function shaclReportRows(report: ShaclReportView): ShaclRowView[] {
  const count = report.results.length.toLocaleString('en-US');
  const rows: ShaclRowView[] = [{
    tone: report.conforms ? 'ok' : 'warn',
    mark: report.conforms ? '✓' : '!',
    message: report.conforms ? 'Conforms — no violations.' : `Does not conform — ${count} result(s).`,
    focus: `sh:conforms ${String(report.conforms)}`
  }];

  for (const result of report.results) {
    rows.push({
      tone: 'warn',
      mark: '!',
      message: result.message.length > 0 ? result.message : `${result.severity} · ${result.constraint}`,
      focus: `${result.focusNode} · ${result.constraint}`
    });
  }

  return rows;
}
