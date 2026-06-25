// The intellisense popup over the editable SPARQL buffer: it proposes completions as the writer types and
// inserts the chosen one at the caret. Self-contained (creates and owns its popup element); the caller
// supplies the vocabulary via a getter, refreshed per dataset. Vanilla DOM + the Selection API, no framework.
import { completionsFor, parsePrefixes, tokenBefore, type Completion, type VocabularyView } from './query-completion';

/**
 * Installs the completion popup on an editable element. As the writer types a token the popup proposes
 * matching keywords, in-scope prefixes, and vocabulary; arrow keys move the selection, Enter/Tab accept,
 * Escape dismisses, and a click accepts. Accepting replaces the token at the caret and re-fires input.
 * @param editor The contenteditable SPARQL buffer.
 * @param vocabulary A getter for the loaded dataset's vocabulary (kept current by the caller).
 */
export function installCompletion(editor: HTMLElement, vocabulary: () => VocabularyView): void {
  const popup = document.createElement('div');
  popup.className = 'completion-popup';
  popup.hidden = true;
  document.body.append(popup);

  let proposals: Completion[] = [];
  let selected = 0;
  let suppressNext = false;

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

  const update = (): void => {
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
    const token = tokenBefore((range.startContainer.textContent ?? '').slice(0, range.startOffset));
    if (token.length === 0) {
      hide();

      return;
    }

    proposals = completionsFor(token, parsePrefixes(editor.textContent ?? ''), vocabulary());
    if (proposals.length === 0) {
      hide();

      return;
    }

    selected = 0;
    paint();
    const rect = range.getBoundingClientRect();
    popup.style.left = `${rect.left}px`;
    popup.style.top = `${rect.bottom + 4}px`;
    popup.hidden = false;
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

  editor.addEventListener('input', update);
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
