using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Compares two SPARQL <see cref="SparqlResultSet"/>s for W3C evaluation-test equivalence: an <c>ASK</c> result by
/// its boolean, and a <c>SELECT</c> result as a solution multiset (a <em>bag</em> — duplicate rows are significant)
/// under blank-node isomorphism, or as an ordered sequence when the query carried an <c>ORDER BY</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two <c>SELECT</c> results are equivalent when there is a bijection between their solution rows, and a single
/// consistent bijection between the blank nodes appearing across all rows, that makes the rows pairwise equal.
/// Ground results (no blank nodes) reduce to a plain multiset comparison; results with blank nodes are matched by an
/// iterative backtracking search (no call-stack recursion), which is cheap because conformance result sets are small
/// and blank-node ambiguity is rare.
/// </para>
/// </remarks>
internal static class SparqlResultComparer
{
    /// <summary>The ASCII unit separator (U+001F), used to delimit binding renderings in a ground-solution key; it cannot occur in a variable name or a rendered term.</summary>
    private const char UnitSeparator = (char)0x1F;

    /// <summary>Returns whether the actual result set matches the expected, treating a <c>SELECT</c> as ordered when <paramref name="ordered"/> is set.</summary>
    /// <param name="actual">The result set produced by the engine.</param>
    /// <param name="expected">The result set parsed from the expected fixture.</param>
    /// <param name="ordered">Whether the query's <c>ORDER BY</c> makes row order significant.</param>
    /// <returns><see langword="true"/> when the result sets are equivalent.</returns>
    public static bool AreEquivalent(SparqlResultSet actual, SparqlResultSet expected, bool ordered)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);

        if(actual.IsBoolean || expected.IsBoolean)
        {
            return actual.Boolean == expected.Boolean;
        }

        if(actual.Solutions.Count != expected.Solutions.Count)
        {
            return false;
        }

        return ordered
            ? MatchOrdered(actual.Solutions, expected.Solutions)
            : MatchBag(actual.Solutions, expected.Solutions);
    }

    /// <summary>Matches two equal-length solution sequences position-by-position under one consistent blank-node mapping.</summary>
    /// <param name="actual">The actual solutions.</param>
    /// <param name="expected">The expected solutions.</param>
    /// <returns><see langword="true"/> when every aligned pair matches.</returns>
    private static bool MatchOrdered(IReadOnlyList<SparqlSolution> actual, IReadOnlyList<SparqlSolution> expected)
    {
        Dictionary<Utf8String, Utf8String> forward = [];
        Dictionary<Utf8String, Utf8String> backward = [];
        for(int i = 0; i < actual.Count; i++)
        {
            List<KeyValuePair<Utf8String, Utf8String>> added = [];
            if(!TryMatchSolution(actual[i], expected[i], forward, backward, added))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Matches two equal-size solution multisets under a single consistent blank-node bijection, via iterative backtracking.</summary>
    /// <param name="actual">The actual solutions.</param>
    /// <param name="expected">The expected solutions.</param>
    /// <returns><see langword="true"/> when a row bijection (with a consistent blank-node mapping) exists.</returns>
    private static bool MatchBag(IReadOnlyList<SparqlSolution> actual, IReadOnlyList<SparqlSolution> expected)
    {
        int n = actual.Count;

        //Ground fast path: with no blank nodes anywhere, the bag comparison is a plain multiset equality over
        //canonical row keys, avoiding the backtracking search entirely.
        if(!HasBlankNode(actual) && !HasBlankNode(expected))
        {
            return MultisetEqual(actual, expected);
        }

        Dictionary<Utf8String, Utf8String> forward = [];
        Dictionary<Utf8String, Utf8String> backward = [];
        bool[] used = new bool[n];

        //Explicit backtracking stack: each committed frame chose expected row `Chosen` for actual row `Depth`,
        //recording the blank-node bindings it added so a backtrack can undo exactly those.
        Stack<MatchFrame> stack = new();
        int nextCandidate = 0;

        while(true)
        {
            int depth = stack.Count;
            if(depth == n)
            {
                return true;
            }

            bool advanced = false;
            for(int candidate = nextCandidate; candidate < n; candidate++)
            {
                if(used[candidate])
                {
                    continue;
                }

                List<KeyValuePair<Utf8String, Utf8String>> added = [];
                if(TryMatchSolution(actual[depth], expected[candidate], forward, backward, added))
                {
                    used[candidate] = true;
                    stack.Push(new MatchFrame(candidate, added));
                    nextCandidate = 0;
                    advanced = true;

                    break;
                }
            }

            if(advanced)
            {
                continue;
            }

            if(stack.Count == 0)
            {
                return false;
            }

            MatchFrame frame = stack.Pop();
            Undo(frame.Added, forward, backward);
            used[frame.Chosen] = false;
            nextCandidate = frame.Chosen + 1;
        }
    }

    /// <summary>Attempts to match two solutions: the same bound variables, with each variable's terms equal under (and extending) the blank-node mapping.</summary>
    /// <param name="actual">The actual solution.</param>
    /// <param name="expected">The expected solution.</param>
    /// <param name="forward">The actual-to-expected blank-node map (extended on success).</param>
    /// <param name="backward">The expected-to-actual blank-node map (extended on success).</param>
    /// <param name="added">Receives the bindings this match added to <paramref name="forward"/>, for undo on backtrack.</param>
    /// <returns><see langword="true"/> when the solutions match; on <see langword="false"/> any partial additions are rolled back.</returns>
    private static bool TryMatchSolution(
        SparqlSolution actual,
        SparqlSolution expected,
        Dictionary<Utf8String, Utf8String> forward,
        Dictionary<Utf8String, Utf8String> backward,
        List<KeyValuePair<Utf8String, Utf8String>> added)
    {
        if(actual.Bindings.Count != expected.Bindings.Count)
        {
            return false;
        }

        foreach(SparqlBinding binding in actual.Bindings)
        {
            if(!expected.TryGetValue(binding.Variable, out RdfTerm expectedValue)
                || !TryMatchTerms(binding.Value, expectedValue, forward, backward, added))
            {
                Undo(added, forward, backward);
                added.Clear();

                return false;
            }
        }

        return true;
    }

    /// <summary>Matches two terms under the blank-node mapping, descending into triple terms over an explicit stack (no recursion); extends the mapping for newly-paired blank nodes.</summary>
    /// <param name="actualRoot">The actual term.</param>
    /// <param name="expectedRoot">The expected term.</param>
    /// <param name="forward">The actual-to-expected blank-node map.</param>
    /// <param name="backward">The expected-to-actual blank-node map.</param>
    /// <param name="added">Accumulates the blank-node bindings added.</param>
    /// <returns><see langword="true"/> when the terms match.</returns>
    private static bool TryMatchTerms(
        RdfTerm actualRoot,
        RdfTerm expectedRoot,
        Dictionary<Utf8String, Utf8String> forward,
        Dictionary<Utf8String, Utf8String> backward,
        List<KeyValuePair<Utf8String, Utf8String>> added)
    {
        Stack<(RdfTerm Actual, RdfTerm Expected)> work = new();
        work.Push((actualRoot, expectedRoot));

        while(work.Count > 0)
        {
            (RdfTerm a, RdfTerm e) = work.Pop();
            switch(a)
            {
                case BlankNode actualBlank when e is BlankNode expectedBlank:
                {
                    if(!BindBlank(actualBlank.Label, expectedBlank.Label, forward, backward, added))
                    {
                        return false;
                    }
                    break;
                }
                case BlankNode:
                {
                    return false;
                }
                case TripleTerm actualTriple when e is TripleTerm expectedTriple:
                {
                    work.Push((actualTriple.Subject, expectedTriple.Subject));
                    work.Push((actualTriple.Predicate, expectedTriple.Predicate));
                    work.Push((actualTriple.Object, expectedTriple.Object));
                    break;
                }
                case TripleTerm:
                {
                    return false;
                }
                default:
                {
                    //Ground terms (IRIs, literals) — and the IRI predicate of a triple term — match. Numeric and
                    //boolean literals match by VALUE (the W3C suite's expected files use non-canonical lexical forms
                    //like "2100" for an xsd:double, so a lexical compare would spuriously fail); everything else
                    //matches by RDF term identity.
                    if(e is BlankNode or TripleTerm || !TermsMatch(a, e))
                    {
                        return false;
                    }
                    break;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Matches two ground terms: two numeric literals by their numeric value under the SPARQL promotion lattice (so
    /// <c>"2100"^^xsd:double</c> equals the canonical <c>"2.1E3"^^xsd:double</c>, and <c>1</c> equals <c>1.0</c>
    /// only across compatible types is NOT asserted here — the W3C result comparison keeps datatype distinctions
    /// except for lexical-vs-canonical form, so values match only when both are numeric), and everything else by RDF
    /// term identity.
    /// </summary>
    /// <param name="actual">The actual term.</param>
    /// <param name="expected">The expected term.</param>
    /// <returns><see langword="true"/> when the terms match.</returns>
    private static bool TermsMatch(RdfTerm actual, RdfTerm expected)
    {
        if(actual is Literal actualLiteral && expected is Literal expectedLiteral
            && NumericValue.TryParse(actualLiteral.Value.ToString(), actualLiteral.Datatype.Iri, out NumericValue actualNumber)
            && NumericValue.TryParse(expectedLiteral.Value.ToString(), expectedLiteral.Datatype.Iri, out NumericValue expectedNumber))
        {
            //Same numeric kind (datatype family) AND equal value: a double result compares to a double expectation
            //regardless of canonical-vs-non-canonical lexical form, but xsd:integer 1 does not match xsd:double 1.0E0.
            return actualNumber.Kind == expectedNumber.Kind && NumericValue.Compare(actualNumber, expectedNumber) == ComparisonResult.Equal;
        }

        //Language-tagged literals match on the same lexical value, a CASE-INSENSITIVE language tag, and the same
        //base direction (RDF 1.2 — "ab"@en--ltr ≠ "ab"@en--rtl ≠ "ab"@en): BCP47 tags are case-insensitive (RDF
        //compares them so), and the W3C expected files often canonicalize a tag to lower-case (e.g.
        //STRLANG(?o,"en-US") expected as @en-us). Datatype + value must still match exactly.
        if(actual is Literal actualLang && actualLang.Language is { } al
            && expected is Literal expectedLang && expectedLang.Language is { } el)
        {
            return actualLang.Value.Equals(expectedLang.Value)
                && string.Equals(al.ToString(), el.ToString(), StringComparison.OrdinalIgnoreCase)
                && actualLang.BaseDirection == expectedLang.BaseDirection;
        }

        return actual.Equals(expected);
    }

    /// <summary>Binds an actual blank node to an expected one, consistent with prior bindings; records a new binding for undo.</summary>
    /// <param name="actualLabel">The actual blank-node label.</param>
    /// <param name="expectedLabel">The expected blank-node label.</param>
    /// <param name="forward">The actual-to-expected map.</param>
    /// <param name="backward">The expected-to-actual map.</param>
    /// <param name="added">Accumulates the binding when newly added.</param>
    /// <returns><see langword="true"/> when the pairing is consistent.</returns>
    private static bool BindBlank(
        Utf8String actualLabel,
        Utf8String expectedLabel,
        Dictionary<Utf8String, Utf8String> forward,
        Dictionary<Utf8String, Utf8String> backward,
        List<KeyValuePair<Utf8String, Utf8String>> added)
    {
        bool hasForward = forward.TryGetValue(actualLabel, out Utf8String mappedExpected);
        bool hasBackward = backward.TryGetValue(expectedLabel, out Utf8String mappedActual);
        if(hasForward || hasBackward)
        {
            return hasForward && hasBackward && mappedExpected.Equals(expectedLabel) && mappedActual.Equals(actualLabel);
        }

        forward[actualLabel] = expectedLabel;
        backward[expectedLabel] = actualLabel;
        added.Add(new KeyValuePair<Utf8String, Utf8String>(actualLabel, expectedLabel));

        return true;
    }

    /// <summary>Removes the blank-node bindings a tentative match added, restoring both maps.</summary>
    /// <param name="added">The bindings to remove.</param>
    /// <param name="forward">The actual-to-expected map.</param>
    /// <param name="backward">The expected-to-actual map.</param>
    private static void Undo(
        List<KeyValuePair<Utf8String, Utf8String>> added,
        Dictionary<Utf8String, Utf8String> forward,
        Dictionary<Utf8String, Utf8String> backward)
    {
        foreach(KeyValuePair<Utf8String, Utf8String> pair in added)
        {
            forward.Remove(pair.Key);
            backward.Remove(pair.Value);
        }
    }

    /// <summary>Compares two ground solution multisets by counting canonical row keys.</summary>
    /// <param name="actual">The actual solutions.</param>
    /// <param name="expected">The expected solutions.</param>
    /// <returns><see langword="true"/> when the multisets are equal.</returns>
    private static bool MultisetEqual(IReadOnlyList<SparqlSolution> actual, IReadOnlyList<SparqlSolution> expected)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach(SparqlSolution solution in actual)
        {
            string key = SolutionKey(solution);
            counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
        }

        foreach(SparqlSolution solution in expected)
        {
            string key = SolutionKey(solution);
            if(!counts.TryGetValue(key, out int c) || c == 0)
            {
                return false;
            }

            counts[key] = c - 1;
        }

        return true;
    }

    /// <summary>Builds an order-independent canonical key for a ground solution: its bindings sorted by variable name.</summary>
    /// <param name="solution">The solution.</param>
    /// <returns>The canonical key.</returns>
    private static string SolutionKey(SparqlSolution solution)
    {
        List<SparqlBinding> sorted = [.. solution.Bindings];
        sorted.Sort(static (left, right) => string.CompareOrdinal(left.Variable.Name.ToString(), right.Variable.Name.ToString()));

        StringBuilder builder = new();
        foreach(SparqlBinding binding in sorted)
        {
            builder.Append(binding.Variable.Name.ToString()).Append('=').Append(RenderTerm(binding.Value)).Append(UnitSeparator);
        }

        return builder.ToString();
    }

    /// <summary>Renders a ground term to a canonical string for key comparison, descending into triple terms over an explicit post-order stack (no recursion).</summary>
    /// <param name="root">The term (no blank node on the ground path).</param>
    /// <returns>The rendered term.</returns>
    private static string RenderTerm(RdfTerm root)
    {
        Dictionary<RdfTerm, string> rendered = new(ReferenceEqualityComparer.Instance);
        Stack<(RdfTerm Node, bool Combine)> work = new();
        work.Push((root, Combine: false));

        while(work.Count > 0)
        {
            (RdfTerm node, bool combine) = work.Pop();
            if(node is TripleTerm triple)
            {
                if(combine)
                {
                    rendered[node] = "<<" + rendered[triple.Subject] + " <" + triple.Predicate.Iri.ToString() + "> " + rendered[triple.Object] + ">>";
                }
                else
                {
                    work.Push((node, Combine: true));
                    work.Push((triple.Object, Combine: false));
                    work.Push((triple.Subject, Combine: false));
                }
            }
            else
            {
                rendered[node] = RenderLeaf(node);
            }
        }

        return rendered[root];
    }

    /// <summary>Renders a leaf ground term (an IRI or a literal) to its canonical string; a numeric literal renders in its kind plus canonical value so value-equal numbers (e.g. <c>"2100"</c> and <c>"2.1E3"</c> as <c>xsd:double</c>) share one key.</summary>
    /// <param name="term">The leaf term.</param>
    /// <returns>The rendered term.</returns>
    private static string RenderLeaf(RdfTerm term)
    {
        return term switch
        {
            NamedNode named => "<" + named.Iri.ToString() + ">",

            //Case-fold the BCP47 tag (ASCII by definition) so two rows differing only in tag case share one key,
            //matching the case-insensitive compare in TermsMatch; the base direction (RDF 1.2) is part of the key so
            //"ab"@en--ltr, "ab"@en--rtl, and "ab"@en stay distinct.
            Literal { Language: { } language } literal => "\"" + literal.Value.ToString() + "\"@" + FoldAsciiCase(language.ToString()) + DirectionSuffix(literal.BaseDirection),
            Literal literal when NumericValue.TryParse(literal.Value.ToString(), literal.Datatype.Iri, out NumericValue number) => "#" + number.Kind + ":" + number.ToCanonicalLexical(),
            Literal literal => "\"" + literal.Value.ToString() + "\"^^<" + literal.Datatype.Iri.ToString() + ">",
            _ => term.ToString() ?? string.Empty
        };
    }

    /// <summary>Renders an optional base direction as a canonical-key suffix: <c>--ltr</c>/<c>--rtl</c> (RDF 1.2), or the empty string when absent.</summary>
    /// <param name="direction">The literal's base direction, or <see langword="null"/>.</param>
    /// <returns>The direction suffix.</returns>
    private static string DirectionSuffix(TextDirection? direction)
    {
        return direction is TextDirection baseDirection ? "--" + TextDirections.ToText(baseDirection) : string.Empty;
    }

    /// <summary>Folds an ASCII string to upper case for use as a canonical key, without a culture-sensitive case transform (BCP47 language tags are ASCII letters, digits, and hyphens). Returns the input unchanged — allocating nothing — when it has no lower-case letter.</summary>
    /// <param name="value">The ASCII string.</param>
    /// <returns>The case-folded string.</returns>
    private static string FoldAsciiCase(string value)
    {
        ReadOnlySpan<char> span = value;
        int first = span.IndexOfAnyInRange('a', 'z');
        if(first < 0)
        {
            return value;
        }

        return string.Create(value.Length, (value, first), static (destination, state) =>
        {
            (string source, int from) = state;
            source.AsSpan().CopyTo(destination);
            for(int i = from; i < destination.Length; i++)
            {
                if(destination[i] is >= 'a' and <= 'z')
                {
                    destination[i] = (char)(destination[i] - 32);
                }
            }
        });
    }

    /// <summary>Returns whether any solution binds a term that is, or transitively contains, a blank node.</summary>
    /// <param name="solutions">The solutions to scan.</param>
    /// <returns><see langword="true"/> when a blank node is present.</returns>
    private static bool HasBlankNode(IReadOnlyList<SparqlSolution> solutions)
    {
        foreach(SparqlSolution solution in solutions)
        {
            foreach(SparqlBinding binding in solution.Bindings)
            {
                if(ContainsBlankNode(binding.Value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Returns whether a term is, or transitively contains (inside a triple term), a blank node, over an explicit stack.</summary>
    /// <param name="root">The term to scan.</param>
    /// <returns><see langword="true"/> when a blank node is present.</returns>
    private static bool ContainsBlankNode(RdfTerm root)
    {
        Stack<RdfTerm> work = new();
        work.Push(root);
        while(work.Count > 0)
        {
            switch(work.Pop())
            {
                case BlankNode:
                {
                    return true;
                }
                case TripleTerm triple:
                {
                    work.Push(triple.Subject);
                    work.Push(triple.Object);
                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        return false;
    }

    /// <summary>One committed choice on the backtracking stack.</summary>
    /// <param name="Chosen">The expected-row index chosen for this depth.</param>
    /// <param name="Added">The blank-node bindings this choice added, for undo.</param>
    private readonly record struct MatchFrame(int Chosen, List<KeyValuePair<Utf8String, Utf8String>> Added);
}
