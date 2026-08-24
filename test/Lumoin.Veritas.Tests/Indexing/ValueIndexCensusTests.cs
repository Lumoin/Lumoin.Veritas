using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.Rdf.Indexing;

namespace Lumoin.Veritas.Tests.Indexing;

/// <summary>
/// The Q8 census pins: the on-demand diagnostic partitions the graph's registered-datatype literal
/// entries into declared (probe-servable) and undeclared (invisible to every probe — the mis-encoded
/// host's visibility signal), ignores literals of unregistered datatypes and non-literal objects, and
/// returns zeros for an empty registry.
/// </summary>
[TestClass]
internal sealed class ValueIndexCensusTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The declared point-axis predicate.</summary>
    private static Utf8String At => Utf8Strings.From("http://example.org/at");

    /// <summary>The census partitions declared from undeclared temporal entries and skips unregistered datatypes and non-literals.</summary>
    [TestMethod]
    public async Task CensusPartitionsDeclaredFromUndeclared()
    {
        TermDictionary dictionary = new();
        TermId at = dictionary.GetOrAdd(new NamedNode(At));
        TermId elsewhere = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/recordedAt")));
        TermId s1 = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/s1")));
        TermId s2 = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/s2")));
        TermId declaredValue = dictionary.GetOrAdd(DateTimeLiteral("2020-01-01T00:00:00Z"));
        TermId undeclaredValue = dictionary.GetOrAdd(DateTimeLiteral("2021-06-01T00:00:00Z"));
        TermId integerValue = dictionary.GetOrAdd(new Literal(Utf8Strings.From("42"), new NamedNode(Vocabulary.Xsd.Integer)));
        TermId iriObject = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/notAValue")));

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            new EncodedTriple(s1, at, declaredValue),
            new EncodedTriple(s1, elsewhere, undeclaredValue),
            new EncodedTriple(s2, elsewhere, undeclaredValue),
            new EncodedTriple(s2, at, integerValue),
            new EncodedTriple(s2, at, iriObject),
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        ValueIndexCensusResult census = ValueIndexCensus.Compute(store, dictionary, TemporalRegistry());
        Assert.AreEqual(1L, census.DeclaredEntryCount, "One dateTime entry lives under the declared axis predicate.");
        Assert.AreEqual(2L, census.UndeclaredEntryCount, "Two dateTime entries live under an undeclared predicate — the mis-encoding signal.");

        ValueIndexCensusResult empty = ValueIndexCensus.Compute(store, dictionary, ValueIndexRegistry.Empty);
        Assert.AreEqual(default, empty, "An empty registry registers nothing, so nothing is countable.");
    }

    /// <summary>A registry holding one temporal point-axis registration over <see cref="At"/> (empty sample corpus — the C.1 battery certifies the method's semantics).</summary>
    /// <returns>The registry.</returns>
    private static ValueIndexRegistry TemporalRegistry()
    {
        ValueAxisDeclaration axis = ValueAxisDeclaration.PointAxis(At);

        return new ValueIndexRegistryBuilder()
            .Add(new ValueIndexRegistration(
                new TemporalIntervalAccessMethod(Vocabulary.Xsd.DateTime, axis, TimeSpan.Zero),
                axis,
                new EmptySource(),
                selfTestCases: []))
            .Build();
    }

    /// <summary>Builds an <c>xsd:dateTime</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal DateTimeLiteral(string lexical)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(Vocabulary.Xsd.DateTime));
    }

    /// <summary>An empty registrant sample corpus.</summary>
    private sealed class EmptySource: ValueSegmentSource
    {
        /// <summary>Enumerates nothing.</summary>
        /// <param name="predicateIri">The requested predicate.</param>
        /// <returns>No entries.</returns>
        public override IEnumerable<ValueSegmentEntry> EnumerateDeclared(Utf8String predicateIri)
        {
            return [];
        }
    }
}
