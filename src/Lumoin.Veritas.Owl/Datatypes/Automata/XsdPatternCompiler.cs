using System;

namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>Whether an XSD-dialect pattern compiled to an automaton.</summary>
internal enum PatternCompileStatus
{
    /// <summary>The pattern parsed and compiled within budget.</summary>
    Compiled,

    /// <summary>The pattern was rejected during parsing.</summary>
    ParseError,

    /// <summary>The construction crossed the NFA state ceiling; the caller consumes this as an abstention.</summary>
    BudgetExceeded,
}

/// <summary>
/// The value-based result of compiling an XSD-dialect pattern: the built automaton on success,
/// or a structured parse error, or a budget-exceeded abstention.
/// </summary>
internal readonly record struct PatternCompileResult
{
    /// <summary>The compile status.</summary>
    public PatternCompileStatus Status { get; }

    /// <summary>The compiled automaton, on success.</summary>
    public NondeterministicAutomaton? Automaton { get; }

    /// <summary>The parse error, when the status is a parse error.</summary>
    public RegexParseError ParseError { get; }

    /// <summary>The code-point position of a parse error.</summary>
    public int Position { get; }

    /// <summary>Creates a result.</summary>
    /// <param name="status">The status.</param>
    /// <param name="automaton">The compiled automaton.</param>
    /// <param name="parseError">The parse error.</param>
    /// <param name="position">The parse-error position.</param>
    private PatternCompileResult(PatternCompileStatus status, NondeterministicAutomaton? automaton, RegexParseError parseError, int position)
    {
        Status = status;
        Automaton = automaton;
        ParseError = parseError;
        Position = position;
    }

    /// <summary>A compiled result.</summary>
    /// <param name="automaton">The compiled automaton.</param>
    /// <returns>The result.</returns>
    public static PatternCompileResult Compiled(NondeterministicAutomaton automaton)
    {
        return new PatternCompileResult(PatternCompileStatus.Compiled, automaton, RegexParseError.None, -1);
    }

    /// <summary>A parse-error result.</summary>
    /// <param name="error">The parse error.</param>
    /// <param name="position">The parse-error position.</param>
    /// <returns>The result.</returns>
    public static PatternCompileResult Failed(RegexParseError error, int position)
    {
        return new PatternCompileResult(PatternCompileStatus.ParseError, null, error, position);
    }

    /// <summary>A budget-exceeded result.</summary>
    /// <returns>The result.</returns>
    public static PatternCompileResult BudgetExceeded()
    {
        return new PatternCompileResult(PatternCompileStatus.BudgetExceeded, null, RegexParseError.None, -1);
    }
}

/// <summary>
/// Compiles an XSD-dialect pattern to a nondeterministic automaton by parsing it with
/// <see cref="XsdPatternParser"/> and constructing it with <see cref="ThompsonBuilder"/>, threading
/// the shared <see cref="AutomatonBudgets"/> NFA ceiling. This is the entry the pattern-facet and
/// pattern-datatype consumers build their automata through.
/// </summary>
internal static class XsdPatternCompiler
{
    /// <summary>Compiles a UTF-8 pattern within the given budgets.</summary>
    /// <param name="pattern">The pattern bytes.</param>
    /// <param name="budgets">The automaton budgets.</param>
    /// <returns>The compile result.</returns>
    public static PatternCompileResult Compile(ReadOnlySpan<byte> pattern, AutomatonBudgets budgets)
    {
        RegexParseOutcome parse = XsdPatternParser.Parse(pattern);
        if(parse.Status == RegexParseStatus.Error)
        {
            return PatternCompileResult.Failed(parse.Error, parse.Position);
        }

        ThompsonResult built = ThompsonBuilder.Build(parse.Program!, budgets.MaxNfaStates);

        return built.Status switch
        {
            ThompsonStatus.Compiled => PatternCompileResult.Compiled(built.Automaton!),
            _ => PatternCompileResult.BudgetExceeded()
        };
    }
}
