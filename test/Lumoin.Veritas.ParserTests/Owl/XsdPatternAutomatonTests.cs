using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Owl.Datatypes.Automata;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The stage-A datatype-registry battery for the XSD-dialect pattern automaton module: the
/// certified NFA emptiness and dialect rows (NFA-*, DIA-*), the budget-abstention rows (BUD-*),
/// the automaton-level distinct-string counting rows (CNT-*), and the pinned-dialect corner cases
/// beyond the certified set. Verdicts are certified ground truths over the pinned dialect and the
/// checked-in version-pinned Unicode category tables.
/// </summary>
[TestClass]
internal sealed class XsdPatternAutomatonTests
{
    /// <summary>The automaton budgets the certified rows compile under.</summary>
    private static AutomatonBudgets Budgets => AutomatonBudgets.Default;

    //NFA emptiness rows.

    /// <summary>NFA-SINGLE-E: the language of an empty character class is empty.</summary>
    [TestMethod]
    public void NFASINGLEEEmptyClassLanguageIsEmpty()
    {
        Assert.IsTrue(Compile("[a-z-[a-z]]"u8).IsEmptyLanguage());
    }

    /// <summary>NFA-SINGLE-NE: the language of a literal string is non-empty and accepts exactly that string.</summary>
    [TestMethod]
    public void NFASINGLENELiteralLanguageIsNonEmpty()
    {
        NondeterministicAutomaton automaton = Compile("abc"u8);
        Assert.IsFalse(automaton.IsEmptyLanguage());
        Assert.IsTrue(automaton.Accepts(CodePoints("abc")));
        Assert.IsFalse(automaton.Accepts(CodePoints("ab")));
        Assert.IsFalse(automaton.Accepts(CodePoints("abcd")));
    }

    /// <summary>NFA-PRODUCT-E: the intersection of one-or-more a with one-or-more b is empty.</summary>
    [TestMethod]
    public void NFAPRODUCTEDisjointPlusIntersectionIsEmpty()
    {
        Assert.AreEqual(ProductEmptiness.Empty, Intersect("a+"u8, "b+"u8));
    }

    /// <summary>NFA-PRODUCT-NE: the intersection of a class-first pattern with a literal is non-empty.</summary>
    [TestMethod]
    public void NFAPRODUCTNEOverlappingIntersectionIsNonEmpty()
    {
        Assert.AreEqual(ProductEmptiness.NonEmpty, Intersect("[ab]bc"u8, "abc"u8));
    }

    /// <summary>NFA-PATLEN: three a's intersected with strings of length at most two is empty.</summary>
    [TestMethod]
    public void NFAPATLENPatternLengthConjunctionIsEmpty()
    {
        ProductEmptiness result = AutomatonProduct.IsIntersectionEmpty(Compile("a{3}"u8), LengthAutomaton.AtMost(2), Budgets.MaxProductStates);
        Assert.AreEqual(ProductEmptiness.Empty, result);
    }

    /// <summary>NFA-PATENUM: one-or-more a intersected with the single string "bbb" is empty.</summary>
    [TestMethod]
    public void NFAPATENUMPatternEnumerationConjunctionIsEmpty()
    {
        Assert.AreEqual(ProductEmptiness.Empty, Intersect("a+"u8, "bbb"u8));
    }

    //Dialect corner rows.

    /// <summary>DIA-W-USCORE: the word escape does not match the connector-punctuation underscore.</summary>
    [TestMethod]
    public void DIAWUSCOREWordEscapeExcludesUnderscore()
    {
        Assert.IsFalse(Matches("\\w"u8, 0x5F));
        Assert.IsTrue(Matches("\\w"u8, 'a'));
    }

    /// <summary>DIA-S-NBSP: the space escape does not match the no-break space.</summary>
    [TestMethod]
    public void DIASNBSPSpaceEscapeExcludesNoBreakSpace()
    {
        Assert.IsFalse(Matches("\\s"u8, 0xA0));
        Assert.IsTrue(Matches("\\s"u8, 0x20));
    }

    /// <summary>DIA-S-LSEP: the space escape does not match the line separator.</summary>
    [TestMethod]
    public void DIASLSEPSpaceEscapeExcludesLineSeparator()
    {
        Assert.IsFalse(Matches("\\s"u8, 0x2028));
    }

    /// <summary>DIA-DOT-CR: the dot metacharacter does not match a carriage return.</summary>
    [TestMethod]
    public void DIADOTCRDotExcludesCarriageReturn()
    {
        Assert.IsFalse(Matches("."u8, 0xD));
        Assert.IsTrue(Matches("."u8, 'a'));
    }

    /// <summary>DIA-SUBNEST: the nested-subtraction class matches "c" but not "d".</summary>
    [TestMethod]
    public void DIASUBNESTNestedSubtractionRetainsInnermost()
    {
        NondeterministicAutomaton automaton = Compile("[a-z-[b-e-[c]]]"u8);
        Assert.IsTrue(automaton.Accepts(CodePoints("c")));
        Assert.IsFalse(automaton.Accepts(CodePoints("d")));
    }

    /// <summary>DIA-IC: the name-start then name-char pattern matches "a-b" but not "-ab".</summary>
    [TestMethod]
    public void DIAICNameStartThenNameChars()
    {
        NondeterministicAutomaton automaton = Compile("\\i\\c*"u8);
        Assert.IsTrue(automaton.Accepts(CodePoints("a-b")));
        Assert.IsFalse(automaton.Accepts(CodePoints("-ab")));
    }

    /// <summary>DIA-X00: a zero-count quantifier removes its atom, so "ab" matches but "axb" does not.</summary>
    [TestMethod]
    public void DIAX00ZeroCountQuantifierIsEpsilon()
    {
        NondeterministicAutomaton automaton = Compile("ax{0,0}b"u8);
        Assert.IsTrue(automaton.Accepts(CodePoints("ab")));
        Assert.IsFalse(automaton.Accepts(CodePoints("axb")));
    }

    /// <summary>DIA-ANCHOR: caret and dollar are ordinary characters, so the pattern matches only the literal "^abc$".</summary>
    [TestMethod]
    public void DIAANCHORCaretAndDollarAreOrdinary()
    {
        NondeterministicAutomaton automaton = Compile("^abc$"u8);
        Assert.IsFalse(automaton.Accepts(CodePoints("abc")));
        Assert.IsTrue(automaton.Accepts(CodePoints("^abc$")));
    }

    /// <summary>DIA-BLOCK: a block-name category escape is a value-based parse error.</summary>
    [TestMethod]
    public void DIABLOCKBlockEscapeIsParseError()
    {
        RegexParseOutcome outcome = XsdPatternParser.Parse("\\p{IsBasicLatin}"u8);
        Assert.AreEqual(RegexParseStatus.Error, outcome.Status);
        Assert.AreEqual(RegexParseError.BlockEscapeUnsupported, outcome.Error);
    }

    //Budget-abstention rows.

    /// <summary>BUD-PRODUCT: a product that exceeds the pair-state ceiling abstains with a budget-exceeded outcome.</summary>
    [TestMethod]
    public void BUDPRODUCTProductCeilingBreachAbstains()
    {
        ProductEmptiness result = AutomatonProduct.IsIntersectionEmpty(Compile("abc"u8), Compile("abc"u8), 1);
        Assert.AreEqual(ProductEmptiness.BudgetExceeded, result);
    }

    /// <summary>BUD-SUBSET: a determinization that exceeds the subset ceiling abstains with a budget-exceeded outcome.</summary>
    [TestMethod]
    public void BUDSUBSETDeterminizeCeilingBreachAbstains()
    {
        DeterminizeResult result = SubsetConstruction.Determinize(Compile("abc"u8), 1);
        Assert.AreEqual(DeterminizeOutcome.BudgetExceeded, result.Outcome);
    }

    //Automaton-level distinct-string counting rows, counted after determinization.

    /// <summary>CNT-AA: the language of (a|a) has exactly one distinct string after determinization.</summary>
    [TestMethod]
    public void CNTAADuplicateAlternationCountsOne()
    {
        Assert.AreEqual(AutomatonCount.Finite(1), CountDistinct("(a|a)"u8));
    }

    /// <summary>CNT-FINITE: the language of (a|b)(c|d) has exactly four distinct strings.</summary>
    [TestMethod]
    public void CNTFINITECrossProductCountsFour()
    {
        Assert.AreEqual(AutomatonCount.Finite(4), CountDistinct("(a|b)(c|d)"u8));
    }

    /// <summary>CNT-INF: the language of one-or-more a is infinite (a cycle among productive states).</summary>
    [TestMethod]
    public void CNTINFUnboundedRepetitionIsInfinite()
    {
        Assert.AreEqual(AutomatonCountKind.Infinite, CountDistinct("a+"u8).Kind);
    }

    //Pinned-dialect corners beyond the certified rows.

    /// <summary>An escaped metacharacter is a literal, not its special meaning.</summary>
    [TestMethod]
    public void EscapedMetacharacterIsLiteral()
    {
        NondeterministicAutomaton dot = Compile("\\."u8);
        Assert.IsTrue(dot.Accepts(CodePoints(".")));
        Assert.IsFalse(dot.Accepts(CodePoints("a")));

        NondeterministicAutomaton paren = Compile("\\(a\\)"u8);
        Assert.IsTrue(paren.Accepts(CodePoints("(a)")));
        Assert.IsFalse(paren.Accepts(CodePoints("a")));
    }

    /// <summary>An in-class dash is literal at the first or last class position.</summary>
    [TestMethod]
    public void InClassDashIsLiteralAtEdges()
    {
        NondeterministicAutomaton leading = Compile("[-a]"u8);
        Assert.IsTrue(leading.Accepts(CodePoints("-")));
        Assert.IsTrue(leading.Accepts(CodePoints("a")));
        Assert.IsFalse(leading.Accepts(CodePoints("b")));

        NondeterministicAutomaton trailing = Compile("[a-]"u8);
        Assert.IsTrue(trailing.Accepts(CodePoints("a")));
        Assert.IsTrue(trailing.Accepts(CodePoints("-")));
    }

    /// <summary>An unbounded brace quantifier matches from its lower bound upward but not below it.</summary>
    [TestMethod]
    public void UnboundedBraceQuantifierMatchesFromLowerBound()
    {
        NondeterministicAutomaton automaton = Compile("a{2,}"u8);
        Assert.IsFalse(automaton.Accepts(CodePoints("a")));
        Assert.IsTrue(automaton.Accepts(CodePoints("aa")));
        Assert.IsTrue(automaton.Accepts(CodePoints("aaa")));
    }

    /// <summary>An empty branch matches the empty word.</summary>
    [TestMethod]
    public void EmptyBranchMatchesEmptyWord()
    {
        NondeterministicAutomaton automaton = Compile("a|"u8);
        Assert.IsTrue(automaton.Accepts(CodePoints("a")));
        Assert.IsTrue(automaton.Accepts(CodePoints(string.Empty)));
        Assert.IsFalse(automaton.Accepts(CodePoints("b")));
    }

    /// <summary>A quantifier with no preceding atom is a value-based parse error.</summary>
    [TestMethod]
    public void LeadingQuantifierIsParseError()
    {
        Assert.AreEqual(RegexParseError.QuantifierWithoutAtom, ParseError("*a"u8));
        Assert.AreEqual(RegexParseError.QuantifierWithoutAtom, ParseError("+"u8));
    }

    /// <summary>A category group matches every member category; a single category matches only its own.</summary>
    [TestMethod]
    public void CategoryGroupVersusSingleCategory()
    {
        NondeterministicAutomaton upper = Compile("\\p{Lu}"u8);
        Assert.IsTrue(upper.Accepts(CodePoints("A")));
        Assert.IsFalse(upper.Accepts(CodePoints("a")));

        NondeterministicAutomaton letter = Compile("\\p{L}"u8);
        Assert.IsTrue(letter.Accepts(CodePoints("A")));
        Assert.IsTrue(letter.Accepts(CodePoints("a")));

        NondeterministicAutomaton lower = Compile("\\p{Ll}"u8);
        Assert.IsFalse(lower.Accepts(CodePoints("A")));
        Assert.IsTrue(lower.Accepts(CodePoints("a")));
    }

    /// <summary>A complemented category excludes its members and admits everything else in the universe.</summary>
    [TestMethod]
    public void ComplementedCategoryExcludesMembers()
    {
        NondeterministicAutomaton nonDigit = Compile("\\P{Nd}"u8);
        Assert.IsFalse(nonDigit.Accepts(CodePoints("5")));
        Assert.IsTrue(nonDigit.Accepts(CodePoints("a")));
    }

    /// <summary>The Unicode category tables place the sentinel code points in their load-bearing categories.</summary>
    [TestMethod]
    public void UnicodeCategorySentinels()
    {
        Assert.IsTrue(Matches("\\p{Pc}"u8, 0x5F));
        Assert.IsTrue(Matches("\\p{Zs}"u8, 0xA0));
        Assert.IsTrue(Matches("\\p{Zl}"u8, 0x2028));
    }

    /// <summary>An unknown category name is a value-based parse error, distinct from a block-name error.</summary>
    [TestMethod]
    public void UnknownCategoryIsParseError()
    {
        Assert.AreEqual(RegexParseError.UnknownCategory, ParseError("\\p{Xx}"u8));
    }

    /// <summary>A descending character range is a value-based parse error.</summary>
    [TestMethod]
    public void DescendingRangeIsParseError()
    {
        Assert.AreEqual(RegexParseError.InvalidRange, ParseError("[z-a]"u8));
    }

    /// <summary>Unbalanced grouping and bracketing are value-based parse errors.</summary>
    [TestMethod]
    public void UnbalancedDelimitersAreParseErrors()
    {
        Assert.AreEqual(RegexParseError.UnbalancedParenthesis, ParseError("(a"u8));
        Assert.AreEqual(RegexParseError.UnbalancedParenthesis, ParseError("a)"u8));
        Assert.AreEqual(RegexParseError.UnbalancedBracket, ParseError("[a"u8));
    }

    /// <summary>A single-level class subtraction removes the subtrahend members.</summary>
    [TestMethod]
    public void SingleLevelSubtractionRemovesMembers()
    {
        NondeterministicAutomaton automaton = Compile("[a-z-[aeiou]]"u8);
        Assert.IsTrue(automaton.Accepts(CodePoints("b")));
        Assert.IsFalse(automaton.Accepts(CodePoints("a")));
        Assert.IsFalse(automaton.Accepts(CodePoints("e")));
    }

    /// <summary>Compiles a pattern, asserting it compiled within budget.</summary>
    /// <param name="pattern">The pattern bytes.</param>
    /// <returns>The compiled automaton.</returns>
    private static NondeterministicAutomaton Compile(scoped ReadOnlySpan<byte> pattern)
    {
        PatternCompileResult result = XsdPatternCompiler.Compile(pattern, Budgets);
        Assert.AreEqual(PatternCompileStatus.Compiled, result.Status);

        return result.Automaton!;
    }

    /// <summary>The intersection-emptiness verdict of two compiled patterns under the default product ceiling.</summary>
    /// <param name="first">The first pattern bytes.</param>
    /// <param name="second">The second pattern bytes.</param>
    /// <returns>The emptiness verdict.</returns>
    private static ProductEmptiness Intersect(scoped ReadOnlySpan<byte> first, scoped ReadOnlySpan<byte> second)
    {
        return AutomatonProduct.IsIntersectionEmpty(Compile(first), Compile(second), Budgets.MaxProductStates);
    }

    /// <summary>Whether a compiled single-code-point pattern accepts a code point.</summary>
    /// <param name="pattern">The pattern bytes.</param>
    /// <param name="codePoint">The code point.</param>
    /// <returns><see langword="true"/> when the pattern accepts the one-code-point string.</returns>
    private static bool Matches(scoped ReadOnlySpan<byte> pattern, int codePoint)
    {
        return Compile(pattern).Accepts([codePoint]);
    }

    /// <summary>The distinct-string count of a compiled pattern after determinization.</summary>
    /// <param name="pattern">The pattern bytes.</param>
    /// <returns>The count.</returns>
    private static AutomatonCount CountDistinct(scoped ReadOnlySpan<byte> pattern)
    {
        DeterminizeResult determinized = SubsetConstruction.Determinize(Compile(pattern), Budgets.MaxDfaStates);
        Assert.AreEqual(DeterminizeOutcome.Done, determinized.Outcome);

        return AutomatonCounting.CountDistinct(determinized.Automaton!);
    }

    /// <summary>The parse error of a rejected pattern.</summary>
    /// <param name="pattern">The pattern bytes.</param>
    /// <returns>The parse error.</returns>
    private static RegexParseError ParseError(scoped ReadOnlySpan<byte> pattern)
    {
        RegexParseOutcome outcome = XsdPatternParser.Parse(pattern);
        Assert.AreEqual(RegexParseStatus.Error, outcome.Status);

        return outcome.Error;
    }

    /// <summary>Decodes a test input string into its code points.</summary>
    /// <param name="input">The input string.</param>
    /// <returns>The code points.</returns>
    private static int[] CodePoints(string input)
    {
        List<int> codePoints = [];
        foreach(Rune rune in input.EnumerateRunes())
        {
            codePoints.Add(rune.Value);
        }

        return [.. codePoints];
    }
}
