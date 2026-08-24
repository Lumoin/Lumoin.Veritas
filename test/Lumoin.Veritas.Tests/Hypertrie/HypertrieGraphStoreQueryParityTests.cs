using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

/// <summary>
/// Parity tests for <see cref="HypertrieGraphStore.Query"/>
/// versus a reference BGP evaluator built on
/// <see cref="HypertrieGraphStore.Match"/>. The reference is a
/// straight nested-loop hash-join over per-pattern match results
/// — slow, but obviously correct. Whenever the two disagree, the
/// engine has a bug.
/// </summary>
[TestClass]
internal sealed class HypertrieGraphStoreQueryParityTests
{
    public TestContext TestContext { get; set; } = null!;

    private static EncodedTriple[] SampleTriples { get; } =
    [
        EncodedTriple.FromEncoded(1, 100, 2),
        EncodedTriple.FromEncoded(2, 100, 3),
        EncodedTriple.FromEncoded(1, 200, 10),
        EncodedTriple.FromEncoded(2, 200, 10),
        EncodedTriple.FromEncoded(3, 200, 20),
        EncodedTriple.FromEncoded(1, 300, 999),
        EncodedTriple.FromEncoded(4, 100, 5),
    ];

    [TestMethod]
    public async Task SinglePatternQueryMatchesReference()
    {
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");

        TriplePattern pattern = new(
            PatternPosition.OfVariable(s),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.OfVariable(o));

        BasicGraphPattern bgp = new([pattern], registry);

        await AssertParityAsync(bgp).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task TwoPatternJoinMatchesReference()
    {
        VariableRegistry registry = new();
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");

        TriplePattern p1 = new(
            PatternPosition.OfVariable(x),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.OfVariable(y));

        TriplePattern p2 = new(
            PatternPosition.OfVariable(y),
            PatternPosition.Bound(TermId.FromEncoded(200)),
            PatternPosition.Bound(TermId.FromEncoded(10)));

        BasicGraphPattern bgp = new([p1, p2], registry);

        await AssertParityAsync(bgp).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ChainOfThreePatternsMatchesReference()
    {
        VariableRegistry registry = new();
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");

        TriplePattern p1 = new(
            PatternPosition.OfVariable(a),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.OfVariable(b));

        TriplePattern p2 = new(
            PatternPosition.OfVariable(b),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.OfVariable(c));

        BasicGraphPattern bgp = new([p1, p2], registry);

        await AssertParityAsync(bgp).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task DisjointVariablesCrossProductMatchesReference()
    {
        //Two patterns sharing no variables — the join is a
        //cartesian product. A genuine test of the driver because
        //leapfrog with no shared variables degenerates: each
        //iterator runs independently.
        VariableRegistry registry = new();
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");

        TriplePattern p1 = new(
            PatternPosition.OfVariable(x),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.Bound(TermId.FromEncoded(2)));

        TriplePattern p2 = new(
            PatternPosition.OfVariable(y),
            PatternPosition.Bound(TermId.FromEncoded(200)),
            PatternPosition.Bound(TermId.FromEncoded(10)));

        BasicGraphPattern bgp = new([p1, p2], registry);

        await AssertParityAsync(bgp).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PatternsWithFullyBoundConstraintMatchReference()
    {
        VariableRegistry registry = new();
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");

        TriplePattern variableBearing = new(
            PatternPosition.OfVariable(x),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.OfVariable(y));

        TriplePattern existsCheck = new(
            PatternPosition.Bound(TermId.FromEncoded(1)),
            PatternPosition.Bound(TermId.FromEncoded(200)),
            PatternPosition.Bound(TermId.FromEncoded(10)));

        BasicGraphPattern bgp = new([variableBearing, existsCheck], registry);

        await AssertParityAsync(bgp).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task EmptyResultSetMatchesReference()
    {
        VariableRegistry registry = new();
        Variable x = registry.GetOrAdd("x");

        //Nothing in the graph has predicate 9999, so this yields nothing.
        TriplePattern pattern = new(
            PatternPosition.OfVariable(x),
            PatternPosition.Bound(TermId.FromEncoded(9999)),
            PatternPosition.Bound(TermId.FromEncoded(0)));

        BasicGraphPattern bgp = new([pattern], registry);

        await AssertParityAsync(bgp).ConfigureAwait(false);
    }

    private async Task AssertParityAsync(BasicGraphPattern bgp)
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        HashSet<string> queryResults = await CollectKeyedAsync(store.QueryAsync(bgp, VeritasClock.System, cancellationToken: TestContext.CancellationToken), bgp).ConfigureAwait(false);
        HashSet<string> referenceResults = ReferenceEvaluate(SampleTriples, bgp);

        Assert.IsTrue(referenceResults.SetEquals(queryResults),
            $"Engine and reference evaluator disagree.\nEngine: [{string.Join(", ", queryResults.OrderBy(x => x))}]\nReference: [{string.Join(", ", referenceResults.OrderBy(x => x))}]");
    }

    private static async Task<HashSet<string>> CollectKeyedAsync(IAsyncEnumerable<Solution> source, BasicGraphPattern bgp)
    {
        HashSet<string> results = [];

        await foreach(Solution solution in source.ConfigureAwait(false))
        {
            results.Add(KeyForSolution(solution, bgp));
        }

        return results;
    }

    //Builds a string key from a solution — variable id and value
    //pairs in BGP variable order — for set comparison.
    private static string KeyForSolution(Solution solution, BasicGraphPattern bgp)
    {
        List<string> parts = [];

        foreach(Variable variable in bgp.Variables)
        {
            if(solution.TryGetValue(variable, out TermId value))
            {
                parts.Add($"{variable.Id}={value.Encoded}");
            }
        }

        return string.Join("|", parts);
    }

    //The reference BGP evaluator — nested-loop hash-join over
    //InMemoryGraphStore.Match results. Slow, allocation-heavy,
    //obviously correct. Used only by these parity tests.
    private static HashSet<string> ReferenceEvaluate(EncodedTriple[] triples, BasicGraphPattern bgp)
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(triples);

        if(bgp.Patterns.Count == 0)
        {
            //Empty BGP yields one empty solution (universal truth).
            return [string.Empty];
        }

        //Start with the bindings produced by the first pattern.
        List<Dictionary<Variable, long>> bindings = MatchPattern(store, bgp.Patterns[0]);

        //For each subsequent pattern, extend each existing binding
        //by every match consistent with shared variables.
        for(int i = 1; i < bgp.Patterns.Count; i++)
        {
            List<Dictionary<Variable, long>> next = [];
            List<Dictionary<Variable, long>> newMatches = MatchPattern(store, bgp.Patterns[i]);

            foreach(Dictionary<Variable, long> existing in bindings)
            {
                foreach(Dictionary<Variable, long> candidate in newMatches)
                {
                    Dictionary<Variable, long>? merged = TryMerge(existing, candidate);

                    if(merged is not null)
                    {
                        next.Add(merged);
                    }
                }
            }

            bindings = next;

            if(bindings.Count == 0)
            {
                break;
            }
        }

        HashSet<string> keys = [];

        foreach(Dictionary<Variable, long> binding in bindings)
        {
            List<string> parts = [];

            foreach(Variable variable in bgp.Variables)
            {
                if(binding.TryGetValue(variable, out long value))
                {
                    parts.Add($"{variable.Id}={value}");
                }
            }

            keys.Add(string.Join("|", parts));
        }

        return keys;
    }

    //Returns every binding produced by a single pattern's matches.
    //A pattern with no variables produces one empty binding (when
    //it matches) or zero bindings (when it does not).
    private static List<Dictionary<Variable, long>> MatchPattern(InMemoryGraphStore store, TriplePattern pattern)
    {
        TermId subject = pattern.Subject.IsBound ? pattern.Subject.BoundTerm : TermId.None;
        TermId predicate = pattern.Predicate.IsBound ? pattern.Predicate.BoundTerm : TermId.None;
        TermId obj = pattern.Object.IsBound ? pattern.Object.BoundTerm : TermId.None;

        List<Dictionary<Variable, long>> results = [];

        foreach(EncodedTriple triple in store.Match(subject, predicate, obj))
        {
            Dictionary<Variable, long> binding = [];

            if(pattern.Subject.IsVariable)
            {
                binding[pattern.Subject.Variable] = triple.Subject.Encoded;
            }

            if(pattern.Predicate.IsVariable)
            {
                if(!TryAssign(binding, pattern.Predicate.Variable, triple.Predicate.Encoded))
                {
                    continue;
                }
            }

            if(pattern.Object.IsVariable)
            {
                if(!TryAssign(binding, pattern.Object.Variable, triple.Object.Encoded))
                {
                    continue;
                }
            }

            results.Add(binding);
        }

        return results;
    }

    //Within a single pattern: handles the case where the same
    //variable appears in two positions (a self-join). The
    //production engine rejects self-joins so this code path is
    //only exercised by the reference if a future test introduces
    //one. Returns false when the value would conflict with an
    //already-bound assignment.
    private static bool TryAssign(Dictionary<Variable, long> binding, Variable variable, long value)
    {
        if(binding.TryGetValue(variable, out long existing))
        {
            return existing == value;
        }

        binding[variable] = value;

        return true;
    }

    //Merges two bindings. Returns the merged binding when every
    //shared variable agrees on its value, null otherwise.
    private static Dictionary<Variable, long>? TryMerge(Dictionary<Variable, long> left, Dictionary<Variable, long> right)
    {
        Dictionary<Variable, long> merged = new(left);

        foreach(KeyValuePair<Variable, long> entry in right)
        {
            if(merged.TryGetValue(entry.Key, out long existing))
            {
                if(existing != entry.Value)
                {
                    return null;
                }
            }
            else
            {
                merged[entry.Key] = entry.Value;
            }
        }

        return merged;
    }
}
