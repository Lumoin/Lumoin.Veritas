using System;

namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>The kind of an XSD-dialect regular-expression syntax-tree node.</summary>
internal enum RegexNodeKind
{
    /// <summary>A single matchable unit whose code points are a set (a literal, a class escape, or a bracket class).</summary>
    Atom,

    /// <summary>The empty word (an empty branch, an empty group, or a zero-count repetition).</summary>
    Empty,

    /// <summary>The concatenation of a left then a right child.</summary>
    Concatenation,

    /// <summary>The alternation of a left or a right child.</summary>
    Alternation,

    /// <summary>A bounded or unbounded repetition of a child.</summary>
    Repeat,
}

/// <summary>
/// One node of the parsed regular-expression tree, stored flat in a
/// <see cref="RegexProgram"/> with children referenced by index. Fields are interpreted
/// per <see cref="Kind"/>: an atom reads <see cref="SetIndex"/>; a concatenation or
/// alternation reads <see cref="Left"/> and <see cref="Right"/>; a repetition reads
/// <see cref="Left"/>, <see cref="Min"/>, and <see cref="Max"/> (with -1 for an unbounded
/// upper bound).
/// </summary>
/// <param name="Kind">The node kind.</param>
/// <param name="SetIndex">The atom's code-point set index, or -1.</param>
/// <param name="Left">The first (or only) child index, or -1.</param>
/// <param name="Right">The second child index, or -1.</param>
/// <param name="Min">The repetition lower bound, or 0.</param>
/// <param name="Max">The repetition upper bound, or -1 for unbounded, or 0.</param>
internal readonly record struct RegexNode(RegexNodeKind Kind, int SetIndex, int Left, int Right, int Min, int Max);

/// <summary>
/// A parsed XSD-dialect regular expression: the flat node array, the root node index, and
/// the code-point-set table the atoms reference by index. Consumed by
/// <see cref="ThompsonBuilder"/> to compile the automaton.
/// </summary>
internal sealed class RegexProgram
{
    /// <summary>The flat node array.</summary>
    public RegexNode[] Nodes { get; }

    /// <summary>The root node index.</summary>
    public int Root { get; }

    /// <summary>The code-point-set table the atoms index into.</summary>
    public CodePointSet[] Sets { get; }

    /// <summary>Wraps the parsed components.</summary>
    /// <param name="nodes">The flat node array.</param>
    /// <param name="root">The root node index.</param>
    /// <param name="sets">The code-point-set table.</param>
    public RegexProgram(RegexNode[] nodes, int root, CodePointSet[] sets)
    {
        Nodes = nodes;
        Root = root;
        Sets = sets;
    }
}

/// <summary>Whether an XSD-dialect pattern parse succeeded.</summary>
internal enum RegexParseStatus
{
    /// <summary>The pattern parsed into a program.</summary>
    Ok,

    /// <summary>The pattern was rejected; the outcome carries the reason and position.</summary>
    Error,
}

/// <summary>The reason an XSD-dialect pattern parse was rejected.</summary>
internal enum RegexParseError
{
    /// <summary>No error.</summary>
    None,

    /// <summary>The pattern length exceeds the parser guard.</summary>
    PatternTooLong,

    /// <summary>The pattern bytes are not valid UTF-8.</summary>
    InvalidUtf8,

    /// <summary>A backslash ended the pattern with no escape body.</summary>
    TrailingBackslash,

    /// <summary>An escape letter is not a recognized XSD-dialect escape.</summary>
    UnknownEscape,

    /// <summary>A <c>\p{...}</c> name is not a nameable general category or group.</summary>
    UnknownCategory,

    /// <summary>A <c>\p{Is...}</c> block-name escape was used; blocks are not supported.</summary>
    BlockEscapeUnsupported,

    /// <summary>A <c>\p{...}</c> escape is malformed (missing braces or empty name).</summary>
    MalformedCategory,

    /// <summary>A parenthesis is unbalanced.</summary>
    UnbalancedParenthesis,

    /// <summary>A character-class bracket is unbalanced or malformed.</summary>
    UnbalancedBracket,

    /// <summary>A quantifier followed no atom.</summary>
    QuantifierWithoutAtom,

    /// <summary>A brace quantifier is malformed or has a lower bound above its upper bound.</summary>
    InvalidQuantifier,

    /// <summary>A quantifier bound exceeds the parser guard.</summary>
    QuantifierBoundTooLarge,

    /// <summary>A character range has an upper endpoint below its lower endpoint.</summary>
    InvalidRange,

    /// <summary>A character class is malformed (for example an unescaped bracket or a class-escape range endpoint).</summary>
    InvalidCharacterClass,
}

/// <summary>
/// The value-based result of parsing an XSD-dialect pattern: on success the compiled
/// <see cref="Program"/>; on failure a structured <see cref="Error"/> and the code-point
/// <see cref="Position"/> at which parsing stopped. Parsing never throws for malformed input.
/// </summary>
internal readonly record struct RegexParseOutcome
{
    /// <summary>Whether the parse succeeded.</summary>
    public RegexParseStatus Status { get; }

    /// <summary>The rejection reason, or <see cref="RegexParseError.None"/> on success.</summary>
    public RegexParseError Error { get; }

    /// <summary>The code-point position parsing stopped at, on failure.</summary>
    public int Position { get; }

    /// <summary>The compiled program, on success.</summary>
    public RegexProgram? Program { get; }

    /// <summary>Creates an outcome.</summary>
    /// <param name="status">The status.</param>
    /// <param name="error">The rejection reason.</param>
    /// <param name="position">The stop position.</param>
    /// <param name="program">The compiled program.</param>
    private RegexParseOutcome(RegexParseStatus status, RegexParseError error, int position, RegexProgram? program)
    {
        Status = status;
        Error = error;
        Position = position;
        Program = program;
    }

    /// <summary>A successful parse.</summary>
    /// <param name="program">The compiled program.</param>
    /// <returns>The outcome.</returns>
    public static RegexParseOutcome Ok(RegexProgram program)
    {
        return new RegexParseOutcome(RegexParseStatus.Ok, RegexParseError.None, -1, program);
    }

    /// <summary>A rejected parse.</summary>
    /// <param name="error">The rejection reason.</param>
    /// <param name="position">The stop position.</param>
    /// <returns>The outcome.</returns>
    public static RegexParseOutcome Fail(RegexParseError error, int position)
    {
        return new RegexParseOutcome(RegexParseStatus.Error, error, position, null);
    }
}
