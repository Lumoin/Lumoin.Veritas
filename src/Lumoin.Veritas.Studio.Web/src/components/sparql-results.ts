// The <sparql-results> web component: renders a W3C SPARQL results-JSON document —
// an ASK boolean verdict, or the SELECT variables as a table of bindings (each cell shows the bound
// value with its term type). Query errors carry the parser's span-bearing diagnostics. Light DOM,
// uhtml via the thunk form. Framework-free (custom element).

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
  /**
   * Paints content fresh into the element, replacing prior content. Renders into a throwaway node and swaps
   * the children in, so each update is a first render — uhtml's stateful re-render diffing (which fails when
   * a results table shrinks between queries) never runs. Results re-render per query, not per frame, so the
   * fresh render costs nothing meaningful.
   * @param content A thunk returning the content to render.
   */
  private paint(content: () => Renderable): void {
    const host = document.createElement('div');
    render(host, content);
    this.replaceChildren(...host.childNodes);
  }

  /** Renders the results: an ASK verdict, or the SELECT variables as a bindings table. */
  set results(document: SparqlResultsDocument) {
    if (document.boolean !== undefined) {
      this.paint(() => html`<p class=${document.boolean ? 'ask-true' : 'ask-false'}>ASK → ${String(document.boolean)}</p>`);
      this.dataset.state = 'resulted';

      return;
    }

    const vars = document.head.vars;
    const bindings = document.results?.bindings ?? [];

    this.paint(() => html`
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
    this.paint(() => html`
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
    this.paint(() => html`${text}`);
    this.dataset.state = state;
  }
}

customElements.define('sparql-results', SparqlResultsElement);

export type { SparqlResultsElement };
