using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using System.Globalization;

namespace Lumoin.Veritas.ParserTests.Infrastructure;

/// <summary>
/// Fluent builder for constructing small SHACL shape graphs in tests.
/// Assembles <see cref="Quad"/> objects under the hood, then produces a
/// populated <see cref="InMemoryGraphStore"/> and the
/// <see cref="TermDictionary"/> that was used to encode the quads.
/// </summary>
/// <remarks>
/// <para>
/// The builder is deliberately narrow: it knows how to construct node
/// shapes and property shapes with metadata/constraint/target triples,
/// and how to assemble an RDF list from a sequence of member terms. It
/// does not attempt to be a general-purpose RDF graph builder.
/// </para>
/// <para>
/// A single dictionary is shared across every quad added to the
/// builder, so IRIs that repeat across shapes (e.g., vocabulary terms)
/// get the same <see cref="Core.Encoding.TermId"/>. Callers can inspect
/// the dictionary after <see cref="Finish"/> to resolve shape IRIs to
/// their <see cref="Core.Encoding.TermId"/> for registry lookups.
/// </para>
/// </remarks>
internal sealed class ShapeGraphBuilder
{
    private readonly List<Quad> quads = [];
    private int blankCounter;

    /// <summary>
    /// The term dictionary into which this builder's quads will be
    /// encoded. Exposed so callers can resolve IRIs to term ids after
    /// <see cref="Finish"/>.
    /// </summary>
    public TermDictionary Dictionary { get; } = new();

    /// <summary>Constructs a named-node term from an IRI string.</summary>
    public static RdfTerm Iri(string iri) => new NamedNode(Utf8Strings.From(iri));

    /// <summary>Constructs an <c>xsd:integer</c> literal.</summary>
    public static RdfTerm IntLiteral(int value)
        => new Literal(
            Utf8Strings.From(value.ToString(CultureInfo.InvariantCulture)),
            new NamedNode(Vocabulary.Xsd.Integer));

    /// <summary>Constructs an <c>xsd:boolean</c> literal.</summary>
    public static RdfTerm BoolLiteral(bool value)
        => new Literal(
            Utf8Strings.From(value ? "true" : "false"),
            new NamedNode(Vocabulary.Xsd.Boolean));

    /// <summary>Constructs an <c>xsd:string</c> literal.</summary>
    public static RdfTerm StringLiteral(string text)
        => new Literal(Utf8Strings.From(text), new NamedNode(Vocabulary.Xsd.String));

    /// <summary>Constructs an <c>rdf:langString</c> literal with the given language tag.</summary>
    public static RdfTerm LangString(string text, string lang)
        => new Literal(
            Utf8Strings.From(text),
            new NamedNode(Vocabulary.Rdf.LangString),
            Utf8Strings.From(lang));

    /// <summary>
    /// Builds an RDF list from the given members and returns the list
    /// head. The empty list is <c>rdf:nil</c>; non-empty lists are
    /// chained blank-node cons cells. Each call allocates fresh blanks
    /// so multiple lists in the same graph do not collide.
    /// </summary>
    public RdfTerm List(params RdfTerm[] members)
    {
        NamedNode first = new(RdfVocabulary.Rdf.First);
        NamedNode rest = new(RdfVocabulary.Rdf.Rest);
        NamedNode nil = new(RdfVocabulary.Rdf.Nil);

        if(members.Length == 0)
        {
            return nil;
        }

        //Build tail-first so each cons cell threads into its successor.
        RdfTerm currentRest = nil;
        for(int i = members.Length - 1; i >= 0; i--)
        {
            BlankNode cell = FreshBlank();
            quads.Add(new Quad(cell, first, members[i]));
            quads.Add(new Quad(cell, rest, currentRest));
            currentRest = cell;
        }

        return currentRest;
    }

    /// <summary>
    /// Declares a node shape and returns a <see cref="ShapeContext"/>
    /// for attaching constraints, targets, and metadata to it.
    /// </summary>
    public ShapeContext NodeShape(string iri)
    {
        NamedNode shape = new(Utf8Strings.From(iri));
        NamedNode rdfType = new(Vocabulary.Rdf.Type);
        NamedNode shNodeShape = new(ShaclCoreVocabulary.NodeShape);
        quads.Add(new Quad(shape, rdfType, shNodeShape));
        return new ShapeContext(this, shape);
    }

    /// <summary>
    /// Declares a property shape with the given path IRI and returns a
    /// <see cref="ShapeContext"/> for attaching constraints, targets,
    /// and metadata to it.
    /// </summary>
    public ShapeContext PropertyShape(string iri, string pathIri)
    {
        NamedNode shape = new(Utf8Strings.From(iri));
        NamedNode shPath = new(ShaclCoreVocabulary.Path);
        NamedNode path = new(Utf8Strings.From(pathIri));
        quads.Add(new Quad(shape, shPath, path));
        return new ShapeContext(this, shape);
    }

    /// <summary>
    /// Encodes every accumulated quad into the dictionary and returns
    /// a populated <see cref="InMemoryGraphStore"/> backed by the
    /// encoded triples, along with the dictionary itself.
    /// </summary>
    public (InMemoryGraphStore Store, TermDictionary Dictionary) Finish()
    {
        List<EncodedTriple> encoded = new(quads.Count);
        foreach(Quad q in quads)
        {
            encoded.Add(q.Encode(Dictionary).AsTriple());
        }

        return (InMemoryGraphStore.Build(encoded), Dictionary);
    }

    internal void AddQuad(RdfTerm subject, string predicateIri, RdfTerm @object)
    {
        NamedNode pred = new(Utf8Strings.From(predicateIri));
        quads.Add(new Quad(subject, pred, @object));
    }

    private BlankNode FreshBlank() => new(Utf8Strings.From($"b{blankCounter++}"));

    /// <summary>
    /// Cursor for attaching triples to a specific shape declared by
    /// <see cref="ShapeGraphBuilder.NodeShape"/> or
    /// <see cref="ShapeGraphBuilder.PropertyShape"/>.
    /// </summary>
    internal sealed class ShapeContext
    {
        private readonly ShapeGraphBuilder owner;
        private readonly RdfTerm subject;

        internal ShapeContext(ShapeGraphBuilder owner, RdfTerm subject)
        {
            this.owner = owner;
            this.subject = subject;
        }

        /// <summary>
        /// Adds a triple <c>(this-shape, predicate, object)</c> and
        /// returns this cursor for fluent chaining.
        /// </summary>
        public ShapeContext With(string predicateIri, RdfTerm @object)
        {
            owner.AddQuad(subject, predicateIri, @object);
            return this;
        }
    }
}
