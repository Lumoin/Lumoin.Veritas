using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>
/// Parses the XSD 1.1 (Appendix F) regular-expression dialect into a
/// <see cref="RegexProgram"/>. The dialect is pinned: whole-string anchoring is implicit
/// and <c>^</c>/<c>$</c> are ordinary characters; there are no backreferences or
/// lookaround; <c>.</c> excludes only line feed and carriage return; <c>\d</c>, <c>\s</c>,
/// <c>\w</c>, <c>\i</c>, <c>\c</c> and their upper-case complements have the fixed dialect
/// meanings; <c>\p{...}</c>/<c>\P{...}</c> resolve general categories and groups only, with
/// <c>\p{Is...}</c> block names rejected. Character classes support ranges, negation, and
/// nested subtraction <c>[a-z-[b-e-[c]]]</c>. Every malformed input is a value-based
/// <see cref="RegexParseOutcome"/> failure; the parser never throws for bad patterns and
/// never recurses.
/// </summary>
internal static class XsdPatternParser
{
    /// <summary>The most code points a pattern may contain before it is rejected.</summary>
    public const int MaxPatternLength = 8192;

    /// <summary>The largest quantifier bound a brace quantifier may name before it is rejected.</summary>
    public const int MaxQuantifierBound = 1024;

    /// <summary>Parses a UTF-8 XSD-dialect pattern.</summary>
    /// <param name="pattern">The pattern bytes.</param>
    /// <returns>The parse outcome.</returns>
    public static RegexParseOutcome Parse(ReadOnlySpan<byte> pattern)
    {
        if(!TryDecode(pattern, out int[] codePoints, out int badPosition))
        {
            return RegexParseOutcome.Fail(RegexParseError.InvalidUtf8, badPosition);
        }

        if(codePoints.Length > MaxPatternLength)
        {
            return RegexParseOutcome.Fail(RegexParseError.PatternTooLong, MaxPatternLength);
        }

        Parser parser = new(codePoints);

        return parser.Run();
    }

    /// <summary>Decodes UTF-8 bytes into a code-point array, reporting the byte offset of the first invalid sequence.</summary>
    /// <param name="pattern">The pattern bytes.</param>
    /// <param name="codePoints">The decoded code points.</param>
    /// <param name="badPosition">The byte offset of the first decode failure, on failure.</param>
    /// <returns><see langword="true"/> when the whole input decoded.</returns>
    private static bool TryDecode(ReadOnlySpan<byte> pattern, out int[] codePoints, out int badPosition)
    {
        List<int> decoded = [];
        int offset = 0;
        ReadOnlySpan<byte> remaining = pattern;
        while(!remaining.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf8(remaining, out Rune rune, out int consumed);
            if(status != OperationStatus.Done)
            {
                codePoints = [];
                badPosition = offset;

                return false;
            }

            decoded.Add(rune.Value);
            remaining = remaining[consumed..];
            offset += consumed;
        }

        codePoints = [.. decoded];
        badPosition = -1;

        return true;
    }

    /// <summary>The operator markers the shunting-yard operator stack carries.</summary>
    private enum OperatorMarker
    {
        /// <summary>An implicit concatenation operator.</summary>
        Concatenation,

        /// <summary>An alternation operator.</summary>
        Alternation,

        /// <summary>An open-group marker that a matching close-group reduces down to.</summary>
        GroupOpen,
    }

    /// <summary>The outcome of scanning a single backslash escape.</summary>
    /// <param name="IsSet">Whether the escape denotes a code-point set rather than one code point.</param>
    /// <param name="CodePoint">The code point, when the escape is a single character.</param>
    /// <param name="Set">The set, when the escape is a class escape.</param>
    private readonly record struct EscapeResult(bool IsSet, int CodePoint, CodePointSet Set);

    /// <summary>A character-class scan frame: the positive group accumulated so far, whether it is negated, and whether its one subtraction has been applied.</summary>
    private sealed class ClassFrame
    {
        /// <summary>The accumulated positive group (or the resolved value after a subtraction is applied).</summary>
        public CodePointSet Accumulated { get; set; } = CodePointSet.Empty;

        /// <summary>Whether a leading caret negated the group.</summary>
        public bool Negated { get; set; }

        /// <summary>Whether the frame's single subtraction has been applied, after which only the closing bracket is allowed.</summary>
        public bool SubtractionDone { get; set; }
    }

    /// <summary>The single-pattern iterative parse worker: a shunting-yard driver over the code points with an explicit operand and operator stack.</summary>
    private sealed class Parser
    {
        /// <summary>The pattern code points.</summary>
        private int[] CodePoints { get; }

        /// <summary>The current scan position.</summary>
        private int Pos { get; set; }

        /// <summary>The syntax-tree nodes built so far.</summary>
        private List<RegexNode> Nodes { get; } = [];

        /// <summary>The atom code-point-set table.</summary>
        private List<CodePointSet> Sets { get; } = [];

        /// <summary>The operand stack of node indices.</summary>
        private Stack<int> Operands { get; } = new();

        /// <summary>The operator stack.</summary>
        private Stack<OperatorMarker> Operators { get; } = new();

        /// <summary>Whether the token just processed completed an operand, so the next atom needs an implicit concatenation.</summary>
        private bool PrevOperand { get; set; }

        /// <summary>Creates a worker over the given code points.</summary>
        /// <param name="codePoints">The pattern code points.</param>
        public Parser(int[] codePoints)
        {
            CodePoints = codePoints;
        }

        /// <summary>Runs the parse to completion.</summary>
        /// <returns>The parse outcome.</returns>
        public RegexParseOutcome Run()
        {
            while(Pos < CodePoints.Length)
            {
                int c = CodePoints[Pos];
                RegexParseError error = Step(c);
                if(error != RegexParseError.None)
                {
                    return RegexParseOutcome.Fail(error, Pos);
                }
            }

            if(!PrevOperand)
            {
                Operands.Push(AddEmpty());
            }

            RegexParseError closeError = ReduceAll();
            if(closeError != RegexParseError.None)
            {
                return RegexParseOutcome.Fail(closeError, Pos);
            }

            if(Operands.Count != 1)
            {
                return RegexParseOutcome.Fail(RegexParseError.UnbalancedParenthesis, Pos);
            }

            RegexProgram program = new([.. Nodes], Operands.Pop(), [.. Sets]);

            return RegexParseOutcome.Ok(program);
        }

        /// <summary>Processes one token starting at the current position.</summary>
        /// <param name="c">The code point at the current position.</param>
        /// <returns>The parse error, or <see cref="RegexParseError.None"/>.</returns>
        private RegexParseError Step(int c)
        {
            if(c == '|')
            {
                if(!PrevOperand)
                {
                    Operands.Push(AddEmpty());
                }

                PushOperator(OperatorMarker.Alternation);
                PrevOperand = false;
                Pos++;

                return RegexParseError.None;
            }

            if(c == '(')
            {
                if(PrevOperand)
                {
                    PushOperator(OperatorMarker.Concatenation);
                }

                Operators.Push(OperatorMarker.GroupOpen);
                PrevOperand = false;
                Pos++;

                return RegexParseError.None;
            }

            if(c == ')')
            {
                if(!PrevOperand)
                {
                    Operands.Push(AddEmpty());
                }

                RegexParseError error = ReduceUntilGroup();
                if(error != RegexParseError.None)
                {
                    return error;
                }

                PrevOperand = true;
                Pos++;

                return RegexParseError.None;
            }

            if(c == '*' || c == '+' || c == '?')
            {
                if(!PrevOperand)
                {
                    return RegexParseError.QuantifierWithoutAtom;
                }

                (int min, int max) = c switch
                {
                    '*' => (0, -1),
                    '+' => (1, -1),
                    _ => (0, 1)
                };

                ApplyRepeat(min, max);
                Pos++;

                return RegexParseError.None;
            }

            if(c == '{')
            {
                return StepBrace();
            }

            if(c == '[')
            {
                RegexParseError error = TryScanCharClass(out CodePointSet set);
                if(error != RegexParseError.None)
                {
                    return error;
                }

                EmitAtom(set);

                return RegexParseError.None;
            }

            if(c == '.')
            {
                EmitAtom(XsdCharacterClasses.Dot);
                Pos++;

                return RegexParseError.None;
            }

            if(c == '\\')
            {
                RegexParseError error = TryScanEscape(out EscapeResult escape);
                if(error != RegexParseError.None)
                {
                    return error;
                }

                EmitAtom(escape.IsSet ? escape.Set : XmlCharAlphabet.Bound(CodePointSet.Single(escape.CodePoint)));

                return RegexParseError.None;
            }

            if(c == ']')
            {
                return RegexParseError.InvalidCharacterClass;
            }

            EmitAtom(XmlCharAlphabet.Bound(CodePointSet.Single(c)));
            Pos++;

            return RegexParseError.None;
        }

        /// <summary>Processes a brace token as either a quantifier (after an atom) or a literal open brace.</summary>
        /// <returns>The parse error, or <see cref="RegexParseError.None"/>.</returns>
        private RegexParseError StepBrace()
        {
            if(PrevOperand)
            {
                RegexParseError error = TryScanBraceQuantifier(out int min, out int max, out bool recognized);
                if(error != RegexParseError.None)
                {
                    return error;
                }

                if(recognized)
                {
                    ApplyRepeat(min, max);

                    return RegexParseError.None;
                }
            }

            EmitAtom(XmlCharAlphabet.Bound(CodePointSet.Single('{')));
            Pos++;

            return RegexParseError.None;
        }

        /// <summary>Emits an atom operand, inserting an implicit concatenation when an operand precedes it.</summary>
        /// <param name="set">The atom's code-point set.</param>
        private void EmitAtom(CodePointSet set)
        {
            if(PrevOperand)
            {
                PushOperator(OperatorMarker.Concatenation);
            }

            Operands.Push(AddAtom(set));
            PrevOperand = true;
        }

        /// <summary>Wraps the top operand in a repetition node.</summary>
        /// <param name="min">The lower bound.</param>
        /// <param name="max">The upper bound, or -1 for unbounded.</param>
        private void ApplyRepeat(int min, int max)
        {
            int child = Operands.Pop();
            Operands.Push(AddRepeat(child, min, max));
        }

        /// <summary>Pushes an operator, first reducing any operators of at least its precedence (left associativity).</summary>
        /// <param name="op">The operator to push.</param>
        private void PushOperator(OperatorMarker op)
        {
            while(Operators.Count > 0 && Operators.Peek() != OperatorMarker.GroupOpen && Precedence(Operators.Peek()) >= Precedence(op))
            {
                ReduceTop();
            }

            Operators.Push(op);
        }

        /// <summary>Reduces the top binary operator, combining the top two operands.</summary>
        private void ReduceTop()
        {
            OperatorMarker op = Operators.Pop();
            int right = Operands.Pop();
            int left = Operands.Pop();
            Operands.Push(op == OperatorMarker.Concatenation ? AddConcat(left, right) : AddAlt(left, right));
        }

        /// <summary>Reduces operators down to the nearest open-group marker and pops it.</summary>
        /// <returns>The parse error when no open group is found, else <see cref="RegexParseError.None"/>.</returns>
        private RegexParseError ReduceUntilGroup()
        {
            while(Operators.Count > 0 && Operators.Peek() != OperatorMarker.GroupOpen)
            {
                ReduceTop();
            }

            if(Operators.Count == 0)
            {
                return RegexParseError.UnbalancedParenthesis;
            }

            Operators.Pop();

            return RegexParseError.None;
        }

        /// <summary>Reduces every remaining operator, rejecting a left-open group.</summary>
        /// <returns>The parse error when a group is still open, else <see cref="RegexParseError.None"/>.</returns>
        private RegexParseError ReduceAll()
        {
            while(Operators.Count > 0)
            {
                if(Operators.Peek() == OperatorMarker.GroupOpen)
                {
                    return RegexParseError.UnbalancedParenthesis;
                }

                ReduceTop();
            }

            return RegexParseError.None;
        }

        /// <summary>The binding precedence of an operator.</summary>
        /// <param name="op">The operator.</param>
        /// <returns>The precedence; higher binds tighter.</returns>
        private static int Precedence(OperatorMarker op)
        {
            return op switch
            {
                OperatorMarker.Concatenation => 2,
                OperatorMarker.Alternation => 1,
                _ => 0
            };
        }

        /// <summary>Adds an atom node for a code-point set.</summary>
        /// <param name="set">The set.</param>
        /// <returns>The node index.</returns>
        private int AddAtom(CodePointSet set)
        {
            Sets.Add(set);
            Nodes.Add(new RegexNode(RegexNodeKind.Atom, Sets.Count - 1, -1, -1, 0, 0));

            return Nodes.Count - 1;
        }

        /// <summary>Adds an empty-word node.</summary>
        /// <returns>The node index.</returns>
        private int AddEmpty()
        {
            Nodes.Add(new RegexNode(RegexNodeKind.Empty, -1, -1, -1, 0, 0));

            return Nodes.Count - 1;
        }

        /// <summary>Adds a concatenation node.</summary>
        /// <param name="left">The left child.</param>
        /// <param name="right">The right child.</param>
        /// <returns>The node index.</returns>
        private int AddConcat(int left, int right)
        {
            Nodes.Add(new RegexNode(RegexNodeKind.Concatenation, -1, left, right, 0, 0));

            return Nodes.Count - 1;
        }

        /// <summary>Adds an alternation node.</summary>
        /// <param name="left">The left child.</param>
        /// <param name="right">The right child.</param>
        /// <returns>The node index.</returns>
        private int AddAlt(int left, int right)
        {
            Nodes.Add(new RegexNode(RegexNodeKind.Alternation, -1, left, right, 0, 0));

            return Nodes.Count - 1;
        }

        /// <summary>Adds a repetition node.</summary>
        /// <param name="child">The child.</param>
        /// <param name="min">The lower bound.</param>
        /// <param name="max">The upper bound, or -1 for unbounded.</param>
        /// <returns>The node index.</returns>
        private int AddRepeat(int child, int min, int max)
        {
            Nodes.Add(new RegexNode(RegexNodeKind.Repeat, -1, child, -1, min, max));

            return Nodes.Count - 1;
        }

        /// <summary>Scans a brace quantifier <c>{m}</c>, <c>{m,}</c>, or <c>{m,n}</c> at the current position.</summary>
        /// <param name="min">The lower bound, on recognition.</param>
        /// <param name="max">The upper bound (or -1 unbounded), on recognition.</param>
        /// <param name="recognized">Whether the brace opened a quantifier; when false the brace is a literal and the position is restored.</param>
        /// <returns>The parse error, or <see cref="RegexParseError.None"/>.</returns>
        private RegexParseError TryScanBraceQuantifier(out int min, out int max, out bool recognized)
        {
            min = 0;
            max = 0;
            recognized = false;
            int save = Pos;
            Pos++;

            if(Pos >= CodePoints.Length || !IsDigit(CodePoints[Pos]))
            {
                Pos = save;

                return RegexParseError.None;
            }

            RegexParseError error = ReadBound(out min);
            if(error != RegexParseError.None)
            {
                return error;
            }

            if(Pos < CodePoints.Length && CodePoints[Pos] == '}')
            {
                Pos++;
                max = min;
                recognized = true;

                return RegexParseError.None;
            }

            if(Pos < CodePoints.Length && CodePoints[Pos] == ',')
            {
                Pos++;
                if(Pos < CodePoints.Length && CodePoints[Pos] == '}')
                {
                    Pos++;
                    max = -1;
                    recognized = true;

                    return RegexParseError.None;
                }

                if(Pos < CodePoints.Length && IsDigit(CodePoints[Pos]))
                {
                    RegexParseError upperError = ReadBound(out max);
                    if(upperError != RegexParseError.None)
                    {
                        return upperError;
                    }

                    if(Pos < CodePoints.Length && CodePoints[Pos] == '}')
                    {
                        Pos++;
                        if(max < min)
                        {
                            return RegexParseError.InvalidQuantifier;
                        }

                        recognized = true;

                        return RegexParseError.None;
                    }
                }
            }

            return RegexParseError.InvalidQuantifier;
        }

        /// <summary>Reads a decimal bound, guarding against the quantifier-bound ceiling.</summary>
        /// <param name="value">The parsed bound.</param>
        /// <returns>The parse error, or <see cref="RegexParseError.None"/>.</returns>
        private RegexParseError ReadBound(out int value)
        {
            value = 0;
            while(Pos < CodePoints.Length && IsDigit(CodePoints[Pos]))
            {
                value = (value * 10) + (CodePoints[Pos] - '0');
                if(value > MaxQuantifierBound)
                {
                    return RegexParseError.QuantifierBoundTooLarge;
                }

                Pos++;
            }

            return RegexParseError.None;
        }

        /// <summary>Scans a backslash escape at the current position.</summary>
        /// <param name="result">The escape result.</param>
        /// <returns>The parse error, or <see cref="RegexParseError.None"/>.</returns>
        private RegexParseError TryScanEscape(out EscapeResult result)
        {
            result = default;
            Pos++;
            if(Pos >= CodePoints.Length)
            {
                return RegexParseError.TrailingBackslash;
            }

            int e = CodePoints[Pos];
            Pos++;

            switch(e)
            {
                case 'n':
                    result = new EscapeResult(false, 0xA, CodePointSet.Empty);

                    return RegexParseError.None;

                case 'r':
                    result = new EscapeResult(false, 0xD, CodePointSet.Empty);

                    return RegexParseError.None;

                case 't':
                    result = new EscapeResult(false, 0x9, CodePointSet.Empty);

                    return RegexParseError.None;

                case '\\':
                case '.':
                case '*':
                case '+':
                case '?':
                case '(':
                case ')':
                case '{':
                case '}':
                case '[':
                case ']':
                case '|':
                case '-':
                case '^':
                    result = new EscapeResult(false, e, CodePointSet.Empty);

                    return RegexParseError.None;

                case 'd':
                    result = AsSet(XsdCharacterClasses.Digit);

                    return RegexParseError.None;

                case 'D':
                    result = AsSet(XsdCharacterClasses.NonDigit);

                    return RegexParseError.None;

                case 's':
                    result = AsSet(XsdCharacterClasses.Space);

                    return RegexParseError.None;

                case 'S':
                    result = AsSet(XsdCharacterClasses.NonSpace);

                    return RegexParseError.None;

                case 'w':
                    result = AsSet(XsdCharacterClasses.Word);

                    return RegexParseError.None;

                case 'W':
                    result = AsSet(XsdCharacterClasses.NonWord);

                    return RegexParseError.None;

                case 'i':
                    result = AsSet(XsdCharacterClasses.InitialName);

                    return RegexParseError.None;

                case 'I':
                    result = AsSet(XsdCharacterClasses.NonInitialName);

                    return RegexParseError.None;

                case 'c':
                    result = AsSet(XsdCharacterClasses.Name);

                    return RegexParseError.None;

                case 'C':
                    result = AsSet(XsdCharacterClasses.NonName);

                    return RegexParseError.None;

                case 'p':
                    return TryScanCategory(false, out result);

                case 'P':
                    return TryScanCategory(true, out result);

                default:
                    return RegexParseError.UnknownEscape;
            }
        }

        /// <summary>Scans a <c>\p{...}</c> or <c>\P{...}</c> category escape body at the current position.</summary>
        /// <param name="negate">Whether the escape is the complement form.</param>
        /// <param name="result">The escape result.</param>
        /// <returns>The parse error, or <see cref="RegexParseError.None"/>.</returns>
        private RegexParseError TryScanCategory(bool negate, out EscapeResult result)
        {
            result = default;
            if(Pos >= CodePoints.Length || CodePoints[Pos] != '{')
            {
                return RegexParseError.MalformedCategory;
            }

            Pos++;
            int start = Pos;
            while(Pos < CodePoints.Length && CodePoints[Pos] != '}')
            {
                Pos++;
            }

            if(Pos >= CodePoints.Length)
            {
                return RegexParseError.MalformedCategory;
            }

            int length = Pos - start;
            Pos++;

            if(length == 0)
            {
                return RegexParseError.MalformedCategory;
            }

            if(length >= 2 && CodePoints[start] == 'I' && CodePoints[start + 1] == 's')
            {
                return RegexParseError.BlockEscapeUnsupported;
            }

            if(length > 2)
            {
                return RegexParseError.UnknownCategory;
            }

            Span<byte> name = stackalloc byte[2];
            for(int i = 0; i < length; i++)
            {
                int ch = CodePoints[start + i];
                if(ch > 0x7F)
                {
                    return RegexParseError.UnknownCategory;
                }

                name[i] = (byte)ch;
            }

            if(!UnicodeCategoryTables.TryGetCategorySet(name[..length], out CodePointSet set))
            {
                return RegexParseError.UnknownCategory;
            }

            result = AsSet(negate ? XmlCharAlphabet.Complement(set) : set);

            return RegexParseError.None;
        }

        /// <summary>Scans a bracket character class, including nested subtraction, into one code-point set.</summary>
        /// <param name="result">The class set.</param>
        /// <returns>The parse error, or <see cref="RegexParseError.None"/>.</returns>
        private RegexParseError TryScanCharClass(out CodePointSet result)
        {
            result = CodePointSet.Empty;
            Stack<ClassFrame> frames = new();
            Pos++;
            OpenClassFrame(frames);

            while(true)
            {
                if(Pos >= CodePoints.Length)
                {
                    return RegexParseError.UnbalancedBracket;
                }

                int c = CodePoints[Pos];
                ClassFrame frame = frames.Peek();

                if(c == ']')
                {
                    Pos++;
                    CodePointSet value = frame.SubtractionDone
                        ? frame.Accumulated
                        : (frame.Negated ? XmlCharAlphabet.Complement(frame.Accumulated) : frame.Accumulated);
                    frames.Pop();
                    if(frames.Count == 0)
                    {
                        result = value;

                        return RegexParseError.None;
                    }

                    ClassFrame parent = frames.Peek();
                    CodePointSet parentBase = parent.Negated ? XmlCharAlphabet.Complement(parent.Accumulated) : parent.Accumulated;
                    parent.Accumulated = CodePointSet.Subtract(parentBase, value);
                    parent.Negated = false;
                    parent.SubtractionDone = true;

                    continue;
                }

                if(frame.SubtractionDone)
                {
                    return RegexParseError.InvalidCharacterClass;
                }

                if(c == '-' && Pos + 1 < CodePoints.Length && CodePoints[Pos + 1] == '[')
                {
                    Pos += 2;
                    OpenClassFrame(frames);

                    continue;
                }

                if(c == '-')
                {
                    frame.Accumulated = CodePointSet.Union(frame.Accumulated, XmlCharAlphabet.Bound(CodePointSet.Single('-')));
                    Pos++;

                    continue;
                }

                if(c == '[')
                {
                    return RegexParseError.InvalidCharacterClass;
                }

                RegexParseError itemError = ScanClassItem(frame, c);
                if(itemError != RegexParseError.None)
                {
                    return itemError;
                }
            }
        }

        /// <summary>Scans one class item (a single code point or a class escape) and any range it begins.</summary>
        /// <param name="frame">The current frame.</param>
        /// <param name="c">The code point at the current position.</param>
        /// <returns>The parse error, or <see cref="RegexParseError.None"/>.</returns>
        private RegexParseError ScanClassItem(ClassFrame frame, int c)
        {
            int primary;
            if(c == '\\')
            {
                RegexParseError escapeError = TryScanEscape(out EscapeResult escape);
                if(escapeError != RegexParseError.None)
                {
                    return escapeError;
                }

                if(escape.IsSet)
                {
                    frame.Accumulated = CodePointSet.Union(frame.Accumulated, escape.Set);

                    return RegexParseError.None;
                }

                primary = escape.CodePoint;
            }
            else
            {
                primary = c;
                Pos++;
            }

            bool startsRange = Pos < CodePoints.Length
                && CodePoints[Pos] == '-'
                && Pos + 1 < CodePoints.Length
                && CodePoints[Pos + 1] != ']'
                && CodePoints[Pos + 1] != '[';
            if(!startsRange)
            {
                frame.Accumulated = CodePointSet.Union(frame.Accumulated, XmlCharAlphabet.Bound(CodePointSet.Single(primary)));

                return RegexParseError.None;
            }

            Pos++;
            int end;
            if(CodePoints[Pos] == '\\')
            {
                RegexParseError endError = TryScanEscape(out EscapeResult endEscape);
                if(endError != RegexParseError.None)
                {
                    return endError;
                }

                if(endEscape.IsSet)
                {
                    return RegexParseError.InvalidRange;
                }

                end = endEscape.CodePoint;
            }
            else
            {
                end = CodePoints[Pos];
                Pos++;
            }

            if(end < primary)
            {
                return RegexParseError.InvalidRange;
            }

            frame.Accumulated = CodePointSet.Union(frame.Accumulated, XmlCharAlphabet.Bound(CodePointSet.Range(primary, end)));

            return RegexParseError.None;
        }

        /// <summary>Pushes a fresh class frame, consuming a leading caret as negation.</summary>
        /// <param name="frames">The frame stack.</param>
        private void OpenClassFrame(Stack<ClassFrame> frames)
        {
            ClassFrame frame = new();
            if(Pos < CodePoints.Length && CodePoints[Pos] == '^')
            {
                frame.Negated = true;
                Pos++;
            }

            frames.Push(frame);
        }

        /// <summary>Wraps a set as an escape result.</summary>
        /// <param name="set">The set.</param>
        /// <returns>The escape result.</returns>
        private static EscapeResult AsSet(CodePointSet set)
        {
            return new EscapeResult(true, 0, set);
        }

        /// <summary>Whether a code point is an ASCII decimal digit.</summary>
        /// <param name="c">The code point.</param>
        /// <returns><see langword="true"/> for <c>0</c> through <c>9</c>.</returns>
        private static bool IsDigit(int c)
        {
            return c >= '0' && c <= '9';
        }
    }
}
