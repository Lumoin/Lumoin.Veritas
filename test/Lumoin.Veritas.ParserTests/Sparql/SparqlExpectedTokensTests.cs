using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Sparql.Completion;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Equivalence tests that pin the declarative FIRST/FOLLOW sets in <see cref="SparqlExpectedTokens"/> to the
/// parser's hot-path predicates. Each FIRST set must agree with its <c>SparqlParser.CanStart*</c> predicate
/// over every <see cref="SparqlTokenKind"/>, and <see cref="SparqlExpectedTokens.ResyncTokens"/> must agree
/// with <c>SparqlParser.IsResyncToken</c> over every <see cref="ParseFrameKind"/>. The two encodings are kept
/// deliberately separate (a fast boolean form on the parse hot path, an enumerable form for completion); these
/// tests are the gate that fails if one is edited without the other.
/// </summary>
[TestClass]
internal sealed class SparqlExpectedTokensTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The named FIRST sets paired with the parser predicate each one mirrors.</summary>
    private static IReadOnlyList<(string Name, ImmutableArray<SparqlTokenKind> Set, Func<SparqlTokenKind, bool> Predicate)> FirstSets { get; } =
    [
        (nameof(SparqlExpectedTokens.TripleStart), SparqlExpectedTokens.TripleStart, SparqlParser.CanStartTriple),
        (nameof(SparqlExpectedTokens.VerbStart), SparqlExpectedTokens.VerbStart, SparqlParser.CanStartVerb),
        (nameof(SparqlExpectedTokens.PathStart), SparqlExpectedTokens.PathStart, SparqlParser.CanStartPath),
        (nameof(SparqlExpectedTokens.CompoundTermStart), SparqlExpectedTokens.CompoundTermStart, SparqlParser.CanStartCompoundTerm),
        (nameof(SparqlExpectedTokens.ReifierIdStart), SparqlExpectedTokens.ReifierIdStart, SparqlParser.CanStartReifierId),
        (nameof(SparqlExpectedTokens.DataBlockValueStart), SparqlExpectedTokens.DataBlockValueStart, SparqlParser.CanStartDataBlockValue),
        (nameof(SparqlExpectedTokens.BareExpressionConditionStart), SparqlExpectedTokens.BareExpressionConditionStart, SparqlParser.CanStartBareExpressionCondition),
        (nameof(SparqlExpectedTokens.GroupMemberStart), SparqlExpectedTokens.GroupMemberStart, SparqlParser.CanStartGroupMember),
    ];

    /// <summary>Each FIRST set admits exactly the token kinds its mirrored <c>CanStart*</c> predicate accepts.</summary>
    [TestMethod]
    public void FirstSetsAgreeWithPredicates()
    {
        foreach((string name, ImmutableArray<SparqlTokenKind> set, Func<SparqlTokenKind, bool> predicate) in FirstSets)
        {
            foreach(SparqlTokenKind kind in Enum.GetValues<SparqlTokenKind>())
            {
                Assert.AreEqual(predicate(kind), set.Contains(kind), $"{name} disagrees with its predicate for {kind}.");
            }
        }
    }

    /// <summary><see cref="SparqlExpectedTokens.ResyncTokens"/> admits exactly the tokens <c>IsResyncToken</c> accepts, for every frame kind.</summary>
    [TestMethod]
    public void ResyncTokensAgreeWithIsResyncToken()
    {
        foreach(ParseFrameKind frameKind in Enum.GetValues<ParseFrameKind>())
        {
            ImmutableArray<SparqlTokenKind> set = SparqlExpectedTokens.ResyncTokens(frameKind);

            foreach(SparqlTokenKind kind in Enum.GetValues<SparqlTokenKind>())
            {
                Assert.AreEqual(SparqlParser.IsResyncToken(frameKind, kind), set.Contains(kind), $"ResyncTokens({frameKind}) disagrees with IsResyncToken for {kind}.");
            }
        }
    }

    /// <summary>Every FIRST set is non-empty and free of duplicate token kinds.</summary>
    [TestMethod]
    public void FirstSetsAreNonEmptyAndDistinct()
    {
        foreach((string name, ImmutableArray<SparqlTokenKind> set, Func<SparqlTokenKind, bool> _) in FirstSets)
        {
            Assert.IsNotEmpty(set, $"{name} is empty.");
            Assert.HasCount(set.Length, new HashSet<SparqlTokenKind>(set), $"{name} contains a duplicate token kind.");
        }
    }

    /// <summary>Every frame kind's resync set is non-empty and free of duplicate token kinds.</summary>
    [TestMethod]
    public void ResyncSetsAreNonEmptyAndDistinct()
    {
        foreach(ParseFrameKind frameKind in Enum.GetValues<ParseFrameKind>())
        {
            ImmutableArray<SparqlTokenKind> set = SparqlExpectedTokens.ResyncTokens(frameKind);

            Assert.IsNotEmpty(set, $"ResyncTokens({frameKind}) is empty.");
            Assert.HasCount(set.Length, new HashSet<SparqlTokenKind>(set), $"ResyncTokens({frameKind}) contains a duplicate token kind.");
        }
    }
}
