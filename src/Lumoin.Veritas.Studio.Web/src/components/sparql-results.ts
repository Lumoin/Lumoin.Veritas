// The <sparql-results> web component: renders a W3C SPARQL results-JSON document —
// an ASK boolean verdict, or the SELECT variables as a table of bindings (each cell shows the bound
// value with its term type). Query errors carry the parser's span-bearing diagnostics. Light DOM,
// uhtml via the thunk form. Framework-free (custom element), the Verifable.Web pattern.

import { html, render } from 'uhtml';

export interface SparqlDiagnosticView {
  code: string;
  severity: string;
  message: string;
  startLine: number;
  startColumn: number;
}

export interface SparqlTermView {
  type: string;
  value: string;
  datatype?: string;
  'xml:lang'?: string;
}

export interface SparqlResultsDocument {
  head: { vars: string[] };
  results?: { bindings: Record<string, SparqlTermView>[] };
  boolean?: boolean;
}

export interface SparqlQueryError {
  error: string;
  diagnostics: SparqlDiagnosticView[];
}

/** One SHACL validation result row, as the report view renders it. */
export interface ShaclResultView {
  /** The focus node that failed (a short label). */
  focusNode: string;

  /** The result severity (Violation / Warning / Info). */
  severity: string;

  /** The constraint component that produced the result (a short name). */
  constraint: string;

  /** The human-readable message attached to the result. */
  message: string;
}

/** A SHACL validation report: a conformance verdict and the result rows. */
export interface ShaclReportView {
  /** Whether the data conforms (no violation-severity results). */
  conforms: boolean;

  /** The validation results (violations, warnings, info). */
  results: ShaclResultView[];
}

type Renderable = ReturnType<typeof html>;

/** Renders a bound term as its value plus a term-type annotation (for non-IRI terms). */
function renderTerm(term: SparqlTermView | undefined): Renderable {
  if (term === undefined) {
    return html``;
  }

  const annotation = term.type === 'uri' ? null : html`<span class="term-type"> (${term.type})</span>`;

  return html`<code>${term.value}</code>${annotation}`;
}

class SparqlResultsElement extends HTMLElement {
  /** Renders the results: an ASK verdict, or the SELECT variables as a bindings table. */
  set results(document: SparqlResultsDocument) {
    if (document.boolean !== undefined) {
      render(this, () => html`<p class=${document.boolean ? 'ask-true' : 'ask-false'}>ASK → ${String(document.boolean)}</p>`);
      this.dataset.state = 'resulted';

      return;
    }

    const vars = document.head.vars;
    const bindings = document.results?.bindings ?? [];

    render(this, () => html`
      <p class="result-count">${bindings.length.toLocaleString('en-US')} solution(s).</p>
      ${bindings.length > 0 ? html`
        <table class="tbl" aria-label="Query solutions">
          <thead><tr>${vars.map((name) => html`<th>?${name}</th>`)}</tr></thead>
          <tbody>${bindings.map((row) => html`
            <tr>${vars.map((name) => html`<td>${renderTerm(row[name])}</td>`)}</tr>`)}</tbody>
        </table>` : html`<span hidden></span>`}
    `);
    this.dataset.state = 'resulted';
  }

  /** Renders a query error and its span-bearing diagnostics. */
  showError(error: SparqlQueryError): void {
    render(this, () => html`
      <p class="result-error">${error.error}</p>
      ${error.diagnostics.length > 0 ? html`
        <ul class="result-diagnostics">${error.diagnostics.map((diagnostic) => html`
          <li class=${`diagnostic diagnostic-${diagnostic.severity}`}>
            <code>${diagnostic.code}</code> ${diagnostic.message}
            <span class="term-type">(line ${diagnostic.startLine + 1}, col ${diagnostic.startColumn + 1})</span>
          </li>`)}</ul>` : html`<span hidden></span>`}
    `);
    this.dataset.state = 'error';
  }

  /** Renders a transient status message (e.g. loading) and tags the element with the given state. */
  showMessage(text: string, state: string): void {
    render(this, () => html`${text}`);
    this.dataset.state = state;
  }

  /** Renders a SHACL validation report: a conformance verdict and a table of results. */
  showReport(report: ShaclReportView): void {
    render(this, () => html`
      <p class=${report.conforms ? 'ask-true' : 'ask-false'}>${report.conforms
        ? '✓ Conforms — no violations.'
        : `✗ Does not conform — ${report.results.length.toLocaleString('en-US')} result(s).`}</p>
      ${report.results.length > 0 ? html`
        <table class="tbl" aria-label="Validation results">
          <thead><tr><th>focus node</th><th>severity</th><th>constraint</th><th>message</th></tr></thead>
          <tbody>${report.results.map((result) => html`
            <tr>
              <td><code>${result.focusNode}</code></td>
              <td>${result.severity}</td>
              <td><code>${result.constraint}</code></td>
              <td>${result.message}</td>
            </tr>`)}</tbody>
        </table>` : html`<span hidden></span>`}
    `);
    this.dataset.state = report.conforms ? 'resulted' : 'error';
  }
}

customElements.define('sparql-results', SparqlResultsElement);

export type { SparqlResultsElement };
