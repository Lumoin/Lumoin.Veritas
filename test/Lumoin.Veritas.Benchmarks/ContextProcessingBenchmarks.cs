using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Measures the cost of the format-agnostic active-context processing
/// core (<see cref="ContextProcessing"/>). Used to establish a baseline
/// before deciding whether to invest in structural-sharing optimisations
/// (persistent map / HAMT for <see cref="LinkedDataContext"/>) or prefix-trie
/// indexing of term IRI mappings.
/// </summary>
/// <remarks>
/// <para>
/// Scenarios:
/// </para>
/// <list type="bullet">
/// <item><description><c>SmallInlineContext</c> — single inline context
/// with a handful of simple-string terms. The common case for hand-authored
/// JSON-LD documents.</description></item>
/// <item><description><c>ManyTermsRegistry</c> — inline context with 100
/// term definitions. Stresses the per-term loop in
/// <see cref="ContextProcessing.ApplyEmbeddedContextsAsync"/>.</description></item>
/// <item><description><c>NestedScopedContexts</c> — depth-4 chain of
/// scoped contexts. Stresses the recursive scoped-context
/// pre-validation path inside
/// <see cref="ContextProcessing.CreateTermDefinitionAsync"/>.</description></item>
/// </list>
/// <para>
/// All benchmarks use the no-op fetcher / parser pair from
/// <see cref="ContextBenchmarkSupport"/> so no real I/O happens; the
/// measurement reflects pure algorithm cost.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class ContextProcessingBenchmarks
{
    private IReadOnlyList<LinkedDataContextEntry> smallContext = null!;
    private IReadOnlyList<LinkedDataContextEntry> manyTermsContext = null!;
    private IReadOnlyList<LinkedDataContextEntry> nestedContext = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        smallContext = BuildSmallContext();
        manyTermsContext = BuildManyTermsContext(termCount: 100);
        nestedContext = BuildNestedContext(depth: 4);
    }

    [Benchmark]
    public ValueTask<LinkedDataContext> SmallInlineContext()
    {
        return ContextProcessing.ApplyEmbeddedContextsAsync(
            LinkedDataContext.Empty,
            smallContext,
            baseUrl: null,
            ContextBenchmarkSupport.ThrowingFetcher,
            ContextBenchmarkSupport.ThrowingParser,
            cache: null,
            cancellationToken: CancellationToken.None);
    }

    [Benchmark]
    public ValueTask<LinkedDataContext> ManyTermsContext()
    {
        return ContextProcessing.ApplyEmbeddedContextsAsync(
            LinkedDataContext.Empty,
            manyTermsContext,
            baseUrl: null,
            ContextBenchmarkSupport.ThrowingFetcher,
            ContextBenchmarkSupport.ThrowingParser,
            cache: null,
            cancellationToken: CancellationToken.None);
    }

    [Benchmark]
    public ValueTask<LinkedDataContext> NestedScopedContexts()
    {
        return ContextProcessing.ApplyEmbeddedContextsAsync(
            LinkedDataContext.Empty,
            nestedContext,
            baseUrl: null,
            ContextBenchmarkSupport.ThrowingFetcher,
            ContextBenchmarkSupport.ThrowingParser,
            cache: null,
            cancellationToken: CancellationToken.None);
    }

    private static LinkedDataContextEntry[] BuildSmallContext()
    {
        Dictionary<string, LinkedDataTermSource> terms = new()
        {
            ["name"] = new("k-name") { Iri = "http://schema.org/name", IsSimpleString = true },
            ["age"] = new("k-age") { Iri = "http://schema.org/age", IsSimpleString = true },
            ["email"] = new("k-email") { Iri = "http://schema.org/email", IsSimpleString = true },
            ["address"] = new("k-address") { Iri = "http://schema.org/address", IsSimpleString = true },
            ["birthDate"] = new("k-birthDate") { Iri = "http://schema.org/birthDate", IsSimpleString = true },
        };
        return new[] { new LinkedDataContextEntry(terms, baseUrl: null, syntheticKey: "ctx-small") };
    }

    private static LinkedDataContextEntry[] BuildManyTermsContext(int termCount)
    {
        Dictionary<string, LinkedDataTermSource> terms = new(termCount);
        for(int i = 0; i < termCount; i++)
        {
            string name = string.Create(CultureInfo.InvariantCulture, $"term{i:000}");
            string iri = string.Create(CultureInfo.InvariantCulture, $"http://example.org/{name}");
            string key = string.Create(CultureInfo.InvariantCulture, $"k-{name}");
            terms[name] = new LinkedDataTermSource(key) { Iri = iri, IsSimpleString = true };
        }
        return new[] { new LinkedDataContextEntry(terms, baseUrl: null, syntheticKey: "ctx-many") };
    }

    private static IReadOnlyList<LinkedDataContextEntry> BuildNestedContext(int depth)
    {
        //Build a term whose scoped context contains a term whose scoped
        //context contains a term... nested `depth` levels deep.
        LinkedDataTermSource innermost = new("k-innermost") { Iri = "http://example.org/innermost", IsSimpleString = true };
        IReadOnlyList<LinkedDataContextEntry> current = new[]
        {
            new LinkedDataContextEntry(
                new Dictionary<string, LinkedDataTermSource> { ["innermost"] = innermost },
                baseUrl: null,
                syntheticKey: "ctx-innermost")
        };

        for(int level = depth - 1; level > 0; level--)
        {
            string name = string.Create(CultureInfo.InvariantCulture, $"level{level}");
            string iri = string.Create(CultureInfo.InvariantCulture, $"http://example.org/{name}");
            LinkedDataTermSource scopedTerm = new(string.Create(CultureInfo.InvariantCulture, $"k-{name}"))
            {
                Iri = iri,
                ScopedContext = current
            };
            current = new[]
            {
                new LinkedDataContextEntry(
                    new Dictionary<string, LinkedDataTermSource> { [name] = scopedTerm },
                    baseUrl: null,
                    syntheticKey: string.Create(CultureInfo.InvariantCulture, $"ctx-{name}"))
            };
        }

        return current;
    }
}

internal static class ContextBenchmarkSupport
{
    public static FetchRemoteResourceDelegate ThrowingFetcher { get; } =
        (url, baseUrl, ct) => throw new System.InvalidOperationException(
            "Benchmark contexts must be inline; no remote-context fetching is supported in this corpus.");

    public static ParseRemoteContextDelegate ThrowingParser { get; } =
        (resource, ct) => throw new System.InvalidOperationException(
            "Benchmark contexts must be inline; no remote-context parsing is supported in this corpus.");
}
