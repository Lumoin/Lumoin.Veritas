using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Turtle.Completion;
using Lumoin.Veritas.Turtle.Lexer;
using Lumoin.Veritas.Turtle.Parser;

namespace Lumoin.Veritas.ParserTests.Turtle;

/// <summary>
/// Soundness gate tying the completion FIRST sets in <see cref="TurtleCaretExpectations"/> to the parser's
/// acceptance predicates: every token a FIRST set proposes must be one <see cref="TurtleParser"/> actually
/// accepts at that production, so completion never offers a grammatically-impossible token. The relation is
/// containment, not equality — the parser additionally accepts lexer-internal tokens (a bare prefix namespace,
/// the error token) and the <c>a</c> shorthand in term position, which completion intentionally omits as not
/// worth proposing. Those exclusions are pinned below, so widening either side without revisiting the other
/// trips the gate.
/// </summary>
[TestClass]
internal sealed class TurtleCaretExpectationsTests
{
    /// <summary>Each completion FIRST set paired with the parser predicate that defines what is acceptable at that production.</summary>
    private static IReadOnlyList<(string Name, ImmutableArray<TurtleTokenKind> Set, Func<TurtleTokenKind, bool> Accepts)> FirstSets { get; } =
    [
        (nameof(TurtleCaretExpectations.VerbStart), TurtleCaretExpectations.VerbStart, TurtleParser.CanStartVerb),
        (nameof(TurtleCaretExpectations.SubjectStart), TurtleCaretExpectations.SubjectStart, TurtleParser.CanStartTerm),
        (nameof(TurtleCaretExpectations.ObjectStart), TurtleCaretExpectations.ObjectStart, TurtleParser.CanStartTerm),
        (nameof(TurtleCaretExpectations.StatementStart), TurtleCaretExpectations.StatementStart, TurtleParser.CanStartStatement),
        (nameof(TurtleCaretExpectations.TriGStatementStart), TurtleCaretExpectations.TriGStatementStart, TurtleParser.CanStartStatement),
        (nameof(TurtleCaretExpectations.NamedTermStart), TurtleCaretExpectations.NamedTermStart, TurtleParser.CanStartTerm),
    ];

    /// <summary>Every token a completion FIRST set proposes is one the parser accepts at that production (soundness).</summary>
    [TestMethod]
    public void FirstSetsAreAcceptedByTheParser()
    {
        foreach((string name, ImmutableArray<TurtleTokenKind> set, Func<TurtleTokenKind, bool> accepts) in FirstSets)
        {
            foreach(TurtleTokenKind kind in set)
            {
                Assert.IsTrue(accepts(kind), $"{name} proposes {kind}, which the parser rejects at that production.");
            }
        }
    }

    /// <summary>Every completion FIRST set is non-empty and free of duplicate token kinds.</summary>
    [TestMethod]
    public void FirstSetsAreNonEmptyAndDistinct()
    {
        foreach((string name, ImmutableArray<TurtleTokenKind> set, Func<TurtleTokenKind, bool> _) in FirstSets)
        {
            Assert.IsNotEmpty(set, $"{name} is empty.");
            Assert.HasCount(set.Length, new HashSet<TurtleTokenKind>(set), $"{name} contains a duplicate token kind.");
        }
    }

    /// <summary>
    /// The verb tokens the parser accepts but completion omits are exactly the bare prefix namespace (a partial
    /// prefixed name the lexer emits mid-token, not worth proposing). If this drifts, revisit <see cref="TurtleCaretExpectations.VerbStart"/>.
    /// </summary>
    [TestMethod]
    public void VerbExclusionsArePinned()
    {
        HashSet<TurtleTokenKind> acceptedNotProposed = AcceptedNotProposed(TurtleParser.CanStartVerb, TurtleCaretExpectations.VerbStart);

        Assert.IsTrue(acceptedNotProposed.SetEquals([TurtleTokenKind.PrefixNamespace]), $"Verb exclusions drifted: {Format(acceptedNotProposed)}.");
    }

    /// <summary>
    /// The term tokens the parser accepts but completion omits from its widest term set (<see cref="TurtleCaretExpectations.ObjectStart"/>)
    /// are exactly the bare prefix namespace, the <c>a</c> shorthand (a verb, not a term in practice), and the
    /// error token. If this drifts, revisit the object FIRST set.
    /// </summary>
    [TestMethod]
    public void TermExclusionsArePinned()
    {
        HashSet<TurtleTokenKind> acceptedNotProposed = AcceptedNotProposed(TurtleParser.CanStartTerm, TurtleCaretExpectations.ObjectStart);

        Assert.IsTrue(
            acceptedNotProposed.SetEquals([TurtleTokenKind.PrefixNamespace, TurtleTokenKind.A, TurtleTokenKind.Error]),
            $"Term exclusions drifted: {Format(acceptedNotProposed)}.");
    }

    /// <summary>The token kinds the predicate accepts but the FIRST set does not propose.</summary>
    /// <param name="accepts">The parser acceptance predicate.</param>
    /// <param name="proposed">The completion FIRST set.</param>
    /// <returns>The accepted-but-not-proposed token kinds.</returns>
    private static HashSet<TurtleTokenKind> AcceptedNotProposed(Func<TurtleTokenKind, bool> accepts, ImmutableArray<TurtleTokenKind> proposed)
    {
        HashSet<TurtleTokenKind> result = [];
        foreach(TurtleTokenKind kind in Enum.GetValues<TurtleTokenKind>())
        {
            if(accepts(kind) && !proposed.Contains(kind))
            {
                result.Add(kind);
            }
        }

        return result;
    }

    /// <summary>A readable rendering of a token-kind set for an assertion message.</summary>
    /// <param name="kinds">The token kinds to render.</param>
    /// <returns>The comma-separated kind names.</returns>
    private static string Format(IEnumerable<TurtleTokenKind> kinds)
        => string.Join(", ", kinds);
}
