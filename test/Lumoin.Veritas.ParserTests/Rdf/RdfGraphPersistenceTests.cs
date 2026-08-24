using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Algebra;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Rdf;

/// <summary>
/// Tests for <see cref="RdfGraphPersistence"/> covering the three
/// term kinds (NamedNode, BlankNode, Literal) with and without
/// language tag, and the canonical literal escapes. Tests use the
/// <see cref="Stream"/> convenience overload, which wraps a
/// <see cref="System.IO.Pipelines.PipeWriter"/> internally; the
/// PipeWriter-direct path is a lower-level surface exercised
/// indirectly through the same code path.
/// </summary>
[TestClass]
internal sealed class RdfGraphPersistenceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task NamedNodeSubjectObjectFormatsWithAngleBrackets()
    {
        TermDictionary dictionary = new();
        TermId alice = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/alice"))).Value;
        TermId bob = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/bob"))).Value;
        IriId knows = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/knows")));

        LabeledGraphSource<TermId, IriId> source = SingleEdgeSource(alice, knows, bob);

        using MemoryStream stream = new();
        await RdfGraphPersistence.WriteNTriplesAsync(source, dictionary, stream, TestContext.CancellationToken).ConfigureAwait(false);

        string nt = Encoding.UTF8.GetString(stream.ToArray());
        Assert.AreEqual(
            "<http://example.org/alice> <http://example.org/knows> <http://example.org/bob> .\n",
            nt);
    }

    [TestMethod]
    public async Task BlankNodeFormatsWithLabelPrefix()
    {
        TermDictionary dictionary = new();
        TermId blank = dictionary.GetOrAdd(new BlankNode(Utf8Strings.From("b1"))).Value;
        IriId knows = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/knows")));
        TermId alice = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/alice"))).Value;

        LabeledGraphSource<TermId, IriId> source = SingleEdgeSource(blank, knows, alice);

        using MemoryStream stream = new();
        await RdfGraphPersistence.WriteNTriplesAsync(source, dictionary, stream, TestContext.CancellationToken).ConfigureAwait(false);

        string nt = Encoding.UTF8.GetString(stream.ToArray());
        Assert.AreEqual("_:b1 <http://example.org/knows> <http://example.org/alice> .\n", nt);
    }

    [TestMethod]
    public async Task DatatypedLiteralFormatsWithDoubleCaretAndDatatypeIri()
    {
        TermDictionary dictionary = new();
        TermId alice = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/alice"))).Value;
        IriId age = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/age")));
        TermId thirty = dictionary.GetOrAdd(
            new Literal(Utf8Strings.From("30"), new NamedNode(Vocabulary.Xsd.Integer))).Value;

        LabeledGraphSource<TermId, IriId> source = SingleEdgeSource(alice, age, thirty);

        using MemoryStream stream = new();
        await RdfGraphPersistence.WriteNTriplesAsync(source, dictionary, stream, TestContext.CancellationToken).ConfigureAwait(false);

        string nt = Encoding.UTF8.GetString(stream.ToArray());
        Assert.AreEqual(
            "<http://example.org/alice> <http://example.org/age> \"30\"^^<http://www.w3.org/2001/XMLSchema#integer> .\n",
            nt);
    }

    [TestMethod]
    public async Task LanguageTaggedLiteralFormatsWithAtSign()
    {
        TermDictionary dictionary = new();
        TermId alice = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/alice"))).Value;
        IriId name = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/name")));
        TermId hello = dictionary.GetOrAdd(
            new Literal(
                Utf8Strings.From("Hello"),
                new NamedNode(Vocabulary.Rdf.LangString),
                Utf8Strings.From("en"))).Value;

        LabeledGraphSource<TermId, IriId> source = SingleEdgeSource(alice, name, hello);

        using MemoryStream stream = new();
        await RdfGraphPersistence.WriteNTriplesAsync(source, dictionary, stream, TestContext.CancellationToken).ConfigureAwait(false);

        string nt = Encoding.UTF8.GetString(stream.ToArray());
        Assert.AreEqual("<http://example.org/alice> <http://example.org/name> \"Hello\"@en .\n", nt);
    }

    [TestMethod]
    public async Task LiteralWithControlCharactersIsEscaped()
    {
        TermDictionary dictionary = new();
        TermId alice = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/alice"))).Value;
        IriId comment = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/comment")));
        //Embed every escape: backslash, double-quote, newline, CR, tab.
        TermId tricky = dictionary.GetOrAdd(
            new Literal(
                Utf8Strings.From("a\\b\"c\nd\re\tf"),
                new NamedNode(Vocabulary.Xsd.String))).Value;

        LabeledGraphSource<TermId, IriId> source = SingleEdgeSource(alice, comment, tricky);

        using MemoryStream stream = new();
        await RdfGraphPersistence.WriteNTriplesAsync(source, dictionary, stream, TestContext.CancellationToken).ConfigureAwait(false);

        string nt = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains(@"""a\\b\""c\nd\re\tf""", nt);
    }

    //Builds a one-edge LabeledGraphSource over a fixed (subject,
    //predicate, object) triple. The helper takes IDs by value and
    //returns the source; OneEdgeSource holds them as fields and
    //exposes adjacency/edges via method-group binding.
    private static LabeledGraphSource<TermId, IriId> SingleEdgeSource(TermId subject, IriId predicate, TermId @object)
    {
        OneEdgeSource state = new(subject, predicate, @object);
        return new LabeledGraphSource<TermId, IriId>(
            Adjacency: state.AdjacencyAsync,
            Edges: state.EdgesAsync,
            KnownOrder: 2,
            KnownSize: 1);
    }

    private sealed record OneEdgeSource(TermId Subject, IriId Predicate, TermId Object)
    {
        public async IAsyncEnumerable<TermId> AdjacencyAsync(
            TermId source, IriId label, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if(source.Equals(Subject) && label.Equals(Predicate))
            {
                yield return Object;
            }
        }

        public async IAsyncEnumerable<(TermId Source, IriId Label, TermId Target)> EdgesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return (Subject, Predicate, Object);
        }
    }
}
