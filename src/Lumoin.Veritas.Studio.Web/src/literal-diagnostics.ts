// Offset-precise diagnostics for the geometry literals in the editable SPARQL buffer. The buffer is lexed
// for typed literals whose datatype resolves — prefixed or full-IRI — to one of the six geometry datatypes;
// each literal's unescaped value goes through the transport seam's describeLiteral, and a warning or
// invalid answer is painted as a squiggle over the offending character. The wire offset counts UTF-8 bytes
// into the literal VALUE, so it is walked back through the source escapes to a UTF-16 index into the
// buffer. The editable's own DOM is never touched: the squiggles are absolutely positioned children of the
// scrolling .editor-body container, so native scrolling carries them. Vanilla TS + the DOM Range API.

import { parsePrefixes } from './query-completion';
import type { LiteralDiagnosisDto } from './services/veritas-transport';

/** The six geometry datatype IRIs the diagnostics face answers for; a literal of any other datatype is left alone. */
export const GEO_DATATYPE_IRIS: readonly string[] = [
  'http://www.opengis.net/ont/geosparql#wktLiteral',
  'http://www.opengis.net/ont/geosparql#gmlLiteral',
  'http://www.opengis.net/ont/geosparql#geoJSONLiteral',
  'http://www.opengis.net/ont/geosparql#kmlLiteral',
  'http://www.opengis.net/ont/geosparql#dggsLiteral',
  'https://lumoin.com/veritas/dggs/a5Literal'
];

/** Diagnoses one literal body against its datatype; null when the active source offers no diagnostics. */
export type LiteralDiagnosisProbe = (datatypeIri: string, body: string) => Promise<LiteralDiagnosisDto | null>;

/** The installed overlay: a re-scan on the caller's cadence, and the teardown that removes it. */
export interface LiteralDiagnosticsView {
  /** Re-scans the buffer and repaints the overlay from the fresh answers, dropping every earlier one. */
  rescan(): void;

  /** Removes the overlay layer and stops observing the container. */
  dispose(): void;
}

/** One typed geometry literal found in the buffer: where its body sits, how the buffer spells it, and its value. */
export interface GeoLiteralOccurrence {
  /** The datatype IRI the literal's `^^` suffix resolves to — one of {@link GEO_DATATYPE_IRIS}. */
  readonly datatypeIri: string;

  /** The index into the buffer text of the body's first character (just past the opening quote). */
  readonly bodyStart: number;

  /** The index into the buffer text one past the body's last character (the closing quote). */
  readonly bodyEnd: number;

  /** The body exactly as the buffer spells it, escapes unresolved — the text the offset walk addresses. */
  readonly bodySource: string;

  /** The literal's value: the body with its escapes resolved — the form the diagnostics face reads. */
  readonly value: string;
}

/** One value character read out of a literal's source text: the code point it denotes and how much source it spans. */
interface ValueCharacter {
  /** The Unicode code point the source spells at that position. */
  readonly codePoint: number;

  /** The number of UTF-16 source units the spelling occupies (1 or 2 plain, 2 for an ECHAR, 6/10/12 for a UCHAR). */
  readonly sourceLength: number;
}

/** A diagnosed literal held for painting: the buffer span to underline and the wire facts the squiggle carries. */
interface PaintedDiagnosis {
  /** The answer's severity — the only two the overlay paints. */
  readonly status: 'warning' | 'invalid';

  /** The refusal kind token, verbatim from the wire. */
  readonly kind: string;

  /** The refusal's UTF-8 byte offset into the literal value, or -1 when the refusal is unlocated. */
  readonly byteOffset: number;

  /** The index into the buffer text where the underline starts. */
  readonly start: number;

  /** The index into the buffer text one past the underline's last character. */
  readonly end: number;
}

/** The single-character ECHAR escapes: the source marker after the backslash mapped to the code point it denotes. */
const ECHAR_CODE_POINTS: Readonly<Record<string, number>> = {
  t: 0x09, b: 0x08, n: 0x0a, r: 0x0d, f: 0x0c, '"': 0x22, "'": 0x27, '\\': 0x5c
};

/** A prefixed-name datatype suffix (`geo:wktLiteral`), matched exactly where the `^^` leaves off. */
const PREFIXED_DATATYPE = /([A-Za-z][\w-]*):([A-Za-z_][\w.-]*)/y;

/** The number of UTF-8 bytes a code point encodes to — the wire offset's unit. */
function utf8ByteLength(codePoint: number): number {
  if (codePoint < 0x80) {
    return 1;
  }

  if (codePoint < 0x800) {
    return 2;
  }

  return codePoint < 0x10000 ? 3 : 4;
}

/** The hexadecimal value of `digits` source units starting at `index`, or -1 when they are not all hex digits. */
function hexValue(source: string, index: number, digits: number): number {
  if (index + digits > source.length) {
    return -1;
  }

  let value = 0;
  for (let scan = index; scan < index + digits; scan++) {
    const code = source.charCodeAt(scan);
    const digit = code >= 0x30 && code <= 0x39 ? code - 0x30
      : code >= 0x41 && code <= 0x46 ? code - 0x37
        : code >= 0x61 && code <= 0x66 ? code - 0x57
          : -1;
    if (digit < 0) {
      return -1;
    }

    value = value * 16 + digit;
  }

  return value;
}

/**
 * Reads the one value character the literal source spells at `index`: a plain character (one code point,
 * a surrogate pair counting as one), an ECHAR (`\n`, `\"`, …), or a UCHAR (`\uXXXX`, `\UXXXXXXXX`, and the
 * `\uXXXX\uXXXX` surrogate pair that spells one astral code point). A backslash that opens none of those
 * is itself the value character, so a malformed spelling never desynchronizes the walk.
 * @param source The literal body as the buffer spells it.
 * @param index The source index to read at.
 * @returns The code point and the source units it spans.
 */
function readValueCharacter(source: string, index: number): ValueCharacter {
  if (source[index] !== '\\' || index + 1 >= source.length) {
    const codePoint = source.codePointAt(index) ?? 0;

    return { codePoint, sourceLength: codePoint > 0xffff ? 2 : 1 };
  }

  const marker = source[index + 1];
  const escaped = ECHAR_CODE_POINTS[marker];
  if (escaped !== undefined) {
    return { codePoint: escaped, sourceLength: 2 };
  }

  if (marker === 'u') {
    const value = hexValue(source, index + 2, 4);
    if (value < 0) {
      return { codePoint: 0x5c, sourceLength: 1 };
    }

    // A high surrogate spelled as \uXXXX pairs with a following low-surrogate escape into one code point.
    if (value >= 0xd800 && value <= 0xdbff && source[index + 6] === '\\' && source[index + 7] === 'u') {
      const low = hexValue(source, index + 8, 4);
      if (low >= 0xdc00 && low <= 0xdfff) {
        return { codePoint: (value - 0xd800) * 0x400 + (low - 0xdc00) + 0x10000, sourceLength: 12 };
      }
    }

    return { codePoint: value, sourceLength: 6 };
  }

  if (marker === 'U') {
    const value = hexValue(source, index + 2, 8);

    return value < 0 ? { codePoint: 0x5c, sourceLength: 1 } : { codePoint: value, sourceLength: 10 };
  }

  return { codePoint: 0x5c, sourceLength: 1 };
}

/**
 * Resolves a literal body's escapes into the value the engine diagnoses. A body with no backslash is its
 * own value, so the common case allocates nothing.
 * @param source The literal body as the buffer spells it (the text between its quotes).
 * @returns The literal's value.
 */
export function decodeLiteralValue(source: string): string {
  if (!source.includes('\\')) {
    return source;
  }

  const characters: string[] = [];
  for (let index = 0; index < source.length;) {
    const character = readValueCharacter(source, index);
    characters.push(String.fromCodePoint(character.codePoint));
    index += character.sourceLength;
  }

  return characters.join('');
}

/**
 * Maps a wire byte offset — UTF-8, into the literal's UNESCAPED value — to the UTF-16 index of the
 * character carrying that byte in the ESCAPED source text. Covers the ECHAR forms (one escape spells one
 * value character), the UCHAR forms (`\uXXXX`, `\UXXXXXXXX`, and the surrogate-pair spelling of an astral
 * code point), and plain multi-byte characters, whose UTF-8 lengths the walk counts per code point. A long
 * string's body needs no special case: its real newlines and quotes are plain characters.
 * @param sourceLiteralText The literal body as the buffer spells it (the text between its quotes).
 * @param byteOffset The UTF-8 byte offset into the literal's value.
 * @returns The source index of the character the byte falls in; the text's length when the offset is past its end.
 */
export function literalValueOffsetToSourceIndex(sourceLiteralText: string, byteOffset: number): number {
  if (byteOffset <= 0) {
    return 0;
  }

  let bytes = 0;
  for (let index = 0; index < sourceLiteralText.length;) {
    const character = readValueCharacter(sourceLiteralText, index);
    const length = utf8ByteLength(character.codePoint);
    if (bytes + length > byteOffset) {
      return index;
    }

    bytes += length;
    index += character.sourceLength;
  }

  return sourceLiteralText.length;
}

/** The index one past a `<…>` IRI reference opening at `index`, or -1 when that `<` opens none (a comparison, say). */
function iriReferenceEnd(text: string, index: number): number {
  for (let scan = index + 1; scan < text.length; scan++) {
    const character = text[scan];
    if (character === '>') {
      return scan + 1;
    }

    // The characters an IRI reference cannot contain: past one of them the `<` was an operator, not an IRI.
    if (character === '<' || character === '"' || character === "'" || character === '{' || character === '}'
      || character === ' ' || character === '\t' || character === '\n' || character === '\r') {
      return -1;
    }
  }

  return -1;
}

/** The index of the first character at or after `index` that is not inline whitespace. */
function skipWhitespace(text: string, index: number): number {
  let scan = index;
  while (scan < text.length && (text[scan] === ' ' || text[scan] === '\t' || text[scan] === '\n' || text[scan] === '\r')) {
    scan++;
  }

  return scan;
}

/** The index of the literal body's closing delimiter starting the search at `bodyStart`, or -1 when the literal is unterminated. */
function literalBodyEnd(text: string, bodyStart: number, delimiter: string, long: boolean): number {
  for (let scan = bodyStart; scan < text.length;) {
    const character = text[scan];
    if (character === '\\') {
      scan += 2;
      continue;
    }

    if (!long && character === '\n') {
      return -1;
    }

    if (text.startsWith(delimiter, scan)) {
      return scan;
    }

    scan++;
  }

  return -1;
}

/**
 * The datatype IRI of the `^^` suffix at `index`, and the index past it — the full-IRI (`^^<http://…>`)
 * and prefixed (`^^geo:wktLiteral`) forms both, the latter resolved against the buffer's prefixes.
 * @param text The buffer text.
 * @param index The index of the `^^` marker.
 * @param prefixes The buffer's prefix map.
 * @returns The resolved datatype IRI (empty when the suffix names none) and the index past the suffix.
 */
function readDatatypeSuffix(text: string, index: number, prefixes: Map<string, string>): { iri: string; next: number } {
  const start = skipWhitespace(text, index + 2);
  if (text[start] === '<') {
    const end = iriReferenceEnd(text, start);

    return end < 0 ? { iri: '', next: start } : { iri: text.slice(start + 1, end - 1), next: end };
  }

  PREFIXED_DATATYPE.lastIndex = start;
  const match = PREFIXED_DATATYPE.exec(text);
  if (match === null) {
    return { iri: '', next: start };
  }

  const namespace = prefixes.get(match[1]);

  return { iri: namespace === undefined ? '' : `${namespace}${match[2]}`, next: start + match[0].length };
}

/**
 * Lexes a buffer for typed literals whose datatype is one of the six geometry datatypes. Comments and IRI
 * references are consumed as such, so a `#` or a quote inside either never opens a literal; the short
 * (`"…"`, `'…'`) and long (`"""…"""`, `'''…'''`) quote forms are both recognized, and an unterminated
 * literal ends the literal's own scan without swallowing the rest of the buffer.
 * @param text The buffer text.
 * @param prefixes The buffer's prefix map, parsed from the same text.
 * @returns The geometry literals, in buffer order.
 */
export function scanGeoLiterals(text: string, prefixes: Map<string, string>): GeoLiteralOccurrence[] {
  const found: GeoLiteralOccurrence[] = [];
  let index = 0;
  while (index < text.length) {
    const character = text[index];
    if (character === '#') {
      const newline = text.indexOf('\n', index);
      index = newline < 0 ? text.length : newline + 1;
      continue;
    }

    if (character === '<') {
      const end = iriReferenceEnd(text, index);
      index = end < 0 ? index + 1 : end;
      continue;
    }

    if (character !== '"' && character !== "'") {
      index++;
      continue;
    }

    const long = text.startsWith(character.repeat(3), index);
    const delimiter = long ? character.repeat(3) : character;
    const bodyStart = index + delimiter.length;
    const bodyEnd = literalBodyEnd(text, bodyStart, delimiter, long);
    if (bodyEnd < 0) {
      index = bodyStart;
      continue;
    }

    index = bodyEnd + delimiter.length;
    const suffix = skipWhitespace(text, index);
    if (!text.startsWith('^^', suffix)) {
      continue;
    }

    const datatype = readDatatypeSuffix(text, suffix, prefixes);
    index = datatype.next;
    if (!GEO_DATATYPE_IRIS.includes(datatype.iri)) {
      continue;
    }

    const bodySource = text.slice(bodyStart, bodyEnd);
    found.push({ datatypeIri: datatype.iri, bodyStart, bodyEnd, bodySource, value: decodeLiteralValue(bodySource) });
  }

  return found;
}

/**
 * The buffer span a diagnosis underlines: the source spelling of the character carrying the offending byte
 * (a whole escape sequence when that is how the buffer spells it), or the whole body when the refusal is
 * unlocated. A span that would be empty — an offset past the body's end, or an empty body — falls back to
 * the body's last character, and failing that to the opening quote, so every painted answer has a
 * rectangle to draw.
 * @param literal The diagnosed literal.
 * @param byteOffset The refusal's UTF-8 byte offset into the value, or -1 when it is unlocated.
 * @returns The start and end indices into the buffer text.
 */
function squiggleSpan(literal: GeoLiteralOccurrence, byteOffset: number): { start: number; end: number } {
  const wholeBodyOrQuote = literal.bodyEnd > literal.bodyStart
    ? { start: literal.bodyStart, end: literal.bodyEnd }
    : { start: Math.max(0, literal.bodyStart - 1), end: literal.bodyStart };
  if (byteOffset < 0) {
    return wholeBodyOrQuote;
  }

  const relative = literalValueOffsetToSourceIndex(literal.bodySource, byteOffset);
  if (relative >= literal.bodySource.length) {
    return literal.bodyEnd > literal.bodyStart
      ? { start: literal.bodyEnd - 1, end: literal.bodyEnd }
      : wholeBodyOrQuote;
  }

  const located = literal.bodyStart + relative;

  return { start: located, end: Math.min(literal.bodyEnd, located + readValueCharacter(literal.bodySource, relative).sourceLength) };
}

/**
 * A DOM Range over `[start, end)` of an element's text, walked over its text nodes — the same offsets the
 * completion popup addresses, and read-only: the editable's own DOM is never touched.
 * @param editor The contenteditable buffer.
 * @param start The start index into the element's text.
 * @param end The end index into the element's text.
 * @returns The range, or null when the span falls outside the current text.
 */
function rangeOver(editor: HTMLElement, start: number, end: number): Range | null {
  const walker = document.createTreeWalker(editor, NodeFilter.SHOW_TEXT);
  const range = document.createRange();
  let consumed = 0;
  let placed = false;
  for (let node = walker.nextNode(); node !== null; node = walker.nextNode()) {
    const length = node.textContent?.length ?? 0;
    if (!placed && start <= consumed + length) {
      range.setStart(node, start - consumed);
      placed = true;
    }

    if (placed && end <= consumed + length) {
      range.setEnd(node, end - consumed);

      return range;
    }

    consumed += length;
  }

  return null;
}

/**
 * Installs the diagnostics overlay on an editable buffer. Each `rescan` lexes the buffer for geometry
 * literals and sends every one through the probe under that scan's ticket, so an answer from a superseded
 * scan never paints over a newer one; warning and invalid answers become squiggles positioned over the
 * offending character, and every other answer paints nothing. The layer is an absolutely positioned child
 * of the scrolling `.editor-body` container, so the marks travel with the content; a container resize
 * repaints them from the current rectangles.
 * @param editor The contenteditable buffer to diagnose.
 * @param probe The diagnosis call (the transport seam's describeLiteral, bound to the active source).
 * @returns The installed view.
 */
export function installLiteralDiagnostics(editor: HTMLElement, probe: LiteralDiagnosisProbe): LiteralDiagnosticsView {
  const container = editor.closest<HTMLElement>('.editor-body');
  if (container === null) {
    return { rescan: () => undefined, dispose: () => undefined };
  }

  const layer = document.createElement('div');
  layer.className = 'literal-diagnostics';
  layer.dataset.testid = 'literal-diagnostics';
  container.append(layer);

  let sequence = 0;
  let painted: PaintedDiagnosis[] = [];

  /**
   * Repaints the layer from the held answers, converting each range rectangle from viewport coordinates to
   * the container's content coordinates — its padding-box origin (the border widths are `clientLeft` /
   * `clientTop`) plus the current scroll, which is what an absolutely positioned child is placed against.
   */
  const paint = (): void => {
    const containerRect = container.getBoundingClientRect();
    const originLeft = containerRect.left + container.clientLeft - container.scrollLeft;
    const originTop = containerRect.top + container.clientTop - container.scrollTop;
    const marks: HTMLElement[] = [];
    for (const diagnosis of painted) {
      const range = rangeOver(editor, diagnosis.start, diagnosis.end);
      if (range === null) {
        continue;
      }

      for (const rect of range.getClientRects()) {
        const mark = document.createElement('span');
        mark.className = diagnosis.status === 'invalid' ? 'literal-squiggle is-invalid' : 'literal-squiggle is-warning';
        mark.dataset.status = diagnosis.status;
        mark.dataset.kind = diagnosis.kind;
        mark.dataset.offset = String(diagnosis.byteOffset);
        mark.dataset.sourceIndex = String(diagnosis.start);
        mark.title = `${diagnosis.kind} at byte ${diagnosis.byteOffset}`;
        mark.style.left = `${rect.left - originLeft}px`;
        mark.style.top = `${rect.top - originTop}px`;
        mark.style.width = `${rect.width}px`;
        mark.style.height = `${rect.height}px`;
        marks.push(mark);
      }
    }

    layer.replaceChildren(...marks);
  };

  /** Diagnoses one literal under a scan's ticket and paints the answer, unless a newer scan superseded it. */
  const diagnose = async (ticket: number, literal: GeoLiteralOccurrence): Promise<void> => {
    let diagnosis: LiteralDiagnosisDto | null = null;
    try {
      diagnosis = await probe(literal.datatypeIri, literal.value);
    } catch {
      // The source offers no diagnostics, or the call failed: this literal simply carries no mark.
      diagnosis = null;
    }

    if (ticket !== sequence || diagnosis === null) {
      return;
    }

    if (diagnosis.status !== 'warning' && diagnosis.status !== 'invalid') {
      return;
    }

    const byteOffset = diagnosis.byteOffset ?? -1;
    const span = squiggleSpan(literal, byteOffset);
    painted.push({ status: diagnosis.status, kind: diagnosis.kind ?? '', byteOffset, start: span.start, end: span.end });
    paint();
  };

  /** Clears the layer, lexes the buffer afresh, and sends each geometry literal out under a new ticket. */
  const rescan = (): void => {
    const ticket = ++sequence;
    const text = editor.textContent ?? '';
    painted = [];
    layer.replaceChildren();
    for (const literal of scanGeoLiterals(text, parsePrefixes(text))) {
      void diagnose(ticket, literal);
    }
  };

  const observer = new ResizeObserver(paint);
  observer.observe(container);

  return {
    rescan,
    dispose: () => {
      observer.disconnect();
      layer.remove();
    }
  };
}
