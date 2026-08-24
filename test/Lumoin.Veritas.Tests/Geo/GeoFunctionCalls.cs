using Lumoin.Veritas.Core;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Sparql.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The shared argument builders, invoker, and result assertions of the <c>geof:</c> catalog tests: a
/// catalog entry is invoked under its own function IRI over evaluated argument terms, and a bound result
/// is asserted by lexical form and datatype IRI.
/// </summary>
internal static class GeoFunctionCalls
{
    /// <summary>The shared default evaluation context; the catalog's functions consume no context seam.</summary>
    private static SparqlExpressionContext Context { get; } = SparqlExpressionContext.CreateDefault();

    /// <summary>Builds a <c>geo:wktLiteral</c> argument.</summary>
    /// <param name="lexicalForm">The literal's lexical form.</param>
    /// <returns>The literal term.</returns>
    public static Literal Wkt(string lexicalForm)
    {
        return new Literal(Utf8Strings.From(lexicalForm), new NamedNode(GeoVocabulary.Geo.WktLiteral));
    }

    /// <summary>Builds an <c>xsd:integer</c> argument.</summary>
    /// <param name="lexicalForm">The integer's lexical form.</param>
    /// <returns>The literal term.</returns>
    public static Literal Integer(string lexicalForm)
    {
        return new Literal(Utf8Strings.From(lexicalForm), new NamedNode(Vocabulary.Xsd.Integer));
    }

    /// <summary>Builds an <c>xsd:double</c> argument.</summary>
    /// <param name="lexicalForm">The double's lexical form.</param>
    /// <returns>The literal term.</returns>
    public static Literal Double(string lexicalForm)
    {
        return new Literal(Utf8Strings.From(lexicalForm), new NamedNode(Vocabulary.Xsd.Double));
    }

    /// <summary>Builds an <c>xsd:string</c> argument.</summary>
    /// <param name="lexicalForm">The string's lexical form.</param>
    /// <returns>The literal term.</returns>
    public static Literal Text(string lexicalForm)
    {
        return new Literal(Utf8Strings.From(lexicalForm), new NamedNode(Vocabulary.Xsd.String));
    }

    /// <summary>Invokes a catalog entry's scalar implementation under its own IRI over the given arguments.</summary>
    /// <param name="entry">The catalog entry; it must carry a scalar face.</param>
    /// <param name="arguments">The evaluated arguments, in call order.</param>
    /// <returns>The invocation result.</returns>
    public static SparqlFunctionResult Invoke(SparqlFunctionEntry entry, params RdfTerm[] arguments)
    {
        return entry.Scalar!(entry.FunctionIri, arguments, Context);
    }

    /// <summary>Invokes a catalog entry's aggregate implementation under its own IRI over a group's evaluated values.</summary>
    /// <param name="entry">The catalog entry; it must carry an aggregate face.</param>
    /// <param name="values">The group's evaluated values, in member order.</param>
    /// <returns>The fold result.</returns>
    public static SparqlFunctionResult InvokeAggregate(SparqlFunctionEntry entry, params RdfTerm[] values)
    {
        return entry.Aggregate!(entry.FunctionIri, new SparqlAggregateGroup(values), Context);
    }

    /// <summary>Asserts a bound literal result with the expected lexical form and datatype IRI.</summary>
    /// <param name="result">The invocation result.</param>
    /// <param name="expectedLexical">The expected lexical form.</param>
    /// <param name="expectedDatatype">The expected datatype IRI.</param>
    public static void AssertLexical(SparqlFunctionResult result, string expectedLexical, Utf8String expectedDatatype)
    {
        Assert.IsFalse(result.IsError, $"Expected the bound literal '{expectedLexical}', not the error value.");
        Assert.IsInstanceOfType<Literal>(result.Term);
        Literal literal = (Literal)result.Term;
        Assert.AreEqual(expectedLexical, literal.Value.ToString());
        Assert.IsTrue(literal.Datatype.Iri.Span.SequenceEqual(expectedDatatype.Span), $"Expected the datatype '{expectedDatatype}', found '{literal.Datatype.Iri}'.");
    }
}
