// The intellisense popup over the editable SPARQL buffer: it proposes completions as the writer types and
// inserts the chosen one at the caret. Self-contained (creates and owns its popup element); the caller
// supplies the vocabulary via a getter, refreshed per dataset. Vanilla DOM + the Selection API, no framework.
import { completionsFor, parsePrefixes, tokenBefore, type Completion, type VocabularyView } from './query-completion';

/**
 * Installs the completion popup on an editable element. As the writer types a token the popup proposes the
 * grammar's admissible next tokens — keywords, in-scope variables, and vocabulary — and falls back to a
 * token heuristic when the source describes no context, or describes one that leaves nothing to propose at
 * the caret; arrow keys move the selection, Enter/Tab accept, Escape dismisses, and a click accepts.
 * Accepting replaces the token at the caret and re-fires input.
 * The context type binds `describeCompletion` to `parseProposals`: the popup carries the context typed,
 * never a JSON document — the transport seam owns the parse.
 * @param editor The contenteditable buffer (SPARQL, or a Turtle-family buffer with a Turtle proposer).
 * @param vocabulary A getter for the buffer's proposal vocabulary (kept current by the caller).
 * @param describeCompletion The parser-driven completion context at a caret, or null from a source that describes none; when omitted, the heuristic `proposer` drives the proposals.
 * @param proposer The token + prefix heuristic that ranks proposals (defaults to the SPARQL proposer; pass the Turtle proposer for a Turtle-family buffer).
 * @param parseProposals Maps a described context to proposals (the SPARQL mapping for a SPARQL buffer, the Turtle mapping for a Turtle-family one); without it the heuristic drives the proposals alone.
 */
export function installCompletion<TContext>(
  editor: HTMLElement,
  vocabulary: () => VocabularyView,
  describeCompletion?: (query: string, caretOffset: number) => Promise<TContext | null>,
  proposer: (token: string, prefixes: Map<string, string>, vocabulary: VocabularyView) => Completion[] = completionsFor,
  parseProposals?: (context: TContext, token: string, vocabulary: VocabularyView) => Completion[]
): void {
  const popup = document.createElement('div');
  popup.className = 'completion-popup';
  popup.hidden = true;
  document.body.append(popup);

  let proposals: Completion[] = [];
  let selected = 0;
  let suppressNext = false;
  let sequence = 0;

  const hide = (): void => {
    popup.hidden = true;
    proposals = [];
  };

  const paint = (): void => {
    popup.replaceChildren(...proposals.map((proposal, index) => {
      const item = document.createElement('div');
      item.className = index === selected ? 'completion-item is-selected' : 'completion-item';
      const label = document.createElement('span');
      label.className = 'ci-label';
      label.textContent = proposal.label;
      const kind = document.createElement('span');
      kind.className = 'ci-kind';
      kind.textContent = proposal.kind;
      item.append(label, kind);
      // mousedown (not click) so it fires before the editor's blur hides the popup.
      item.addEventListener('mousedown', (event) => {
        event.preventDefault();
        accept(proposal);
      });

      return item;
    }));
  };

  /** The absolute caret offset (UTF-16 code units) in the editor's full text, the index the parser addresses. */
  const caretOffset = (range: Range): number => {
    const measured = range.cloneRange();
    measured.selectNodeContents(editor);
    measured.setEnd(range.startContainer, range.startOffset);

    return measured.toString().length;
  };

  /** Paints and positions the popup at the caret rectangle, or hides it when there are no proposals. */
  const show = (rect: DOMRect): void => {
    if (proposals.length === 0) {
      hide();

      return;
    }

    selected = 0;
    paint();
    popup.style.left = `${rect.left}px`;
    popup.style.top = `${rect.bottom + 4}px`;
    popup.hidden = false;
  };

  const update = async (): Promise<void> => {
    if (suppressNext) {
      suppressNext = false;

      return;
    }

    const selection = window.getSelection();
    if (selection === null || selection.rangeCount === 0) {
      hide();

      return;
    }

    const range = selection.getRangeAt(0);
    const fullText = editor.textContent ?? '';
    const offset = caretOffset(range);
    const token = tokenBefore(fullText.slice(0, offset));
    if (token.length === 0) {
      hide();

      return;
    }

    // The caret rectangle is captured now; the range may be invalidated by the await below.
    const rect = range.getBoundingClientRect();

    if (describeCompletion === undefined) {
      proposals = proposer(token, parsePrefixes(fullText), vocabulary());
      show(rect);

      return;
    }

    const ticket = ++sequence;
    let next: Completion[] | null = null;
    try {
      // Describe the position at the START of the partial token, not the caret: the parser would otherwise
      // consume the partial term (e.g. `owl:Cl`, or a bare `GR`) and report the continuation, hiding the very
      // vocabulary or keyword the writer is typing. The returned proposals are then filtered by `token`.
      const context = await describeCompletion(fullText, offset - token.length);
      if (ticket !== sequence) {
        // A newer keystroke superseded this request; its own handler paints the result.
        return;
      }

      // A source that describes no context — and a buffer installed without a mapping — fall through to the
      // heuristic below rather than through an error path.
      next = context !== null && parseProposals !== undefined ? parseProposals(context, token, vocabulary()) : null;
    } catch {
      next = null;
    }

    if (ticket !== sequence) {
      return;
    }

    // A described context that proposes nothing at this caret takes the same rung of the degrade ladder as a
    // source that describes no context at all, because an empty proposal set does NOT mean "nothing may be
    // written here". The described token set is the continuation of the production the caret sits in, and it
    // can be empty or narrower than the grammar admits — a repetition reports what continues it while
    // omitting the tokens that close it and carry on the enclosing production — so a writer typing one of
    // those would otherwise be offered nothing. The set can also map to no proposal on its own terms (a
    // position admitting only a variable, with none yet in scope). The grammar still leads wherever it
    // proposes anything at all: the heuristic runs only where the description leaves the writer with nothing.
    proposals = next !== null && next.length > 0 ? next : proposer(token, parsePrefixes(fullText), vocabulary());
    show(rect);
  };

  const accept = (proposal: Completion): void => {
    const selection = window.getSelection();
    if (selection === null || selection.rangeCount === 0) {
      hide();

      return;
    }

    const range = selection.getRangeAt(0);
    const node = range.startContainer;
    if (node.nodeType !== Node.TEXT_NODE) {
      hide();

      return;
    }

    const offset = range.startOffset;
    const text = node.textContent ?? '';
    const token = tokenBefore(text.slice(0, offset));
    node.textContent = text.slice(0, offset - token.length) + proposal.insert + text.slice(offset);
    const caret = offset - token.length + proposal.insert.length;
    range.setStart(node, caret);
    range.collapse(true);
    selection.removeAllRanges();
    selection.addRange(range);
    hide();
    // Re-fire input for the live re-query + gutter, but skip the completion's own re-show of the just-inserted token.
    suppressNext = true;
    editor.dispatchEvent(new Event('input', { bubbles: true }));
  };

  editor.addEventListener('input', () => { void update(); });
  editor.addEventListener('keydown', (event) => {
    if (popup.hidden || proposals.length === 0) {
      return;
    }

    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        selected = (selected + 1) % proposals.length;
        paint();
        break;
      case 'ArrowUp':
        event.preventDefault();
        selected = (selected - 1 + proposals.length) % proposals.length;
        paint();
        break;
      case 'Enter':
      case 'Tab':
        event.preventDefault();
        accept(proposals[selected]);
        break;
      case 'Escape':
        event.preventDefault();
        hide();
        break;
      default:
        break;
    }
  });
  // Hide on blur, but after a tick so a mousedown-accept on a popup item runs first.
  editor.addEventListener('blur', () => window.setTimeout(hide, 120));
}
