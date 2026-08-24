using System;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.Json.Stj;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Geo;

/// <summary>
/// The shared query-side builders of the Geo SPARQL tests: term and literal construction, the
/// parse-normalize-translate chain, and the typed-literal solution assertion.
/// </summary>
internal static class GeoSparqlQueries
{
    /// <summary>Builds the module-composed extension-function registry, asserting every catalog registration is accepted.</summary>
    /// <returns>The registry.</returns>
    public static SparqlFunctionRegistry BuildModuleFunctions()
    {
        SparqlFunctionRegistryBuilder builder = new();
        GeoExtensionModule.RegisterFunctions(builder, GeoJsonGeometryReader.TryRead);
        foreach(SparqlFunctionRegistration outcome in builder.Outcomes)
        {
            Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, outcome.Kind, $"{outcome.FunctionIri}: the module must register cleanly.");
        }

        return builder.Build();
    }

    /// <summary>Builds the module-composed value-datatype registry.</summary>
    /// <returns>The registry.</returns>
    public static ValueDatatypeRegistry BuildModuleDatatypes()
    {
        ValueDatatypeRegistryBuilder builder = new();
        GeoExtensionModule.RegisterValueDatatypes(builder);

        return builder.Build();
    }

    /// <summary>Builds an IRI term.</summary>
    /// <param name="iri">The IRI text.</param>
    /// <returns>The named-node term.</returns>
    public static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>Builds a <c>geo:wktLiteral</c> data literal.</summary>
    /// <param name="lexicalForm">The literal's lexical form.</param>
    /// <returns>The literal term.</returns>
    public static Literal Wkt(string lexicalForm)
    {
        return new Literal(Utf8Strings.From(lexicalForm), new NamedNode(GeoVocabulary.Geo.WktLiteral));
    }

    /// <summary>Builds an <c>xsd:string</c> data literal.</summary>
    /// <param name="lexicalForm">The literal's lexical form.</param>
    /// <returns>The literal term.</returns>
    public static Literal Text(string lexicalForm)
    {
        return new Literal(Utf8Strings.From(lexicalForm), new NamedNode(Vocabulary.Xsd.String));
    }

    /// <summary>Parses, normalizes, and translates a query to algebra under the pure-SPARQL posture.</summary>
    /// <param name="text">The query text.</param>
    /// <param name="pool">The parse pool.</param>
    /// <returns>The algebra.</returns>
    public static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
    }

    /// <summary>Parses, normalizes, and translates a query to algebra under a registry's declared aggregate profile.</summary>
    /// <param name="text">The query text.</param>
    /// <param name="pool">The parse pool.</param>
    /// <param name="functions">The registry whose <see cref="SparqlFunctionRegistry.AggregateIris"/> the translator lifts against.</param>
    /// <returns>The algebra.</returns>
    public static AlgebraOperator Translate(string text, Utf8StringPool pool, SparqlFunctionRegistry functions)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query, functions.AggregateIris);
    }

    /// <summary>Builds a SPARQL variable from its name (without the leading marker).</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable.</returns>
    public static SparqlVariable Variable(string name)
    {
        return new SparqlVariable(Utf8Strings.From(name));
    }

    /// <summary>Asserts a bound literal with the expected lexical form and datatype IRI.</summary>
    /// <param name="solution">The solution to read.</param>
    /// <param name="variableName">The variable name.</param>
    /// <param name="expectedLexical">The expected lexical form.</param>
    /// <param name="expectedDatatype">The expected datatype IRI.</param>
    public static void AssertTypedLiteral(SparqlSolution solution, string variableName, string expectedLexical, Utf8String expectedDatatype)
    {
        Assert.IsTrue(solution.TryGetValue(Variable(variableName), out RdfTerm value), $"Expected ?{variableName} to be bound.");
        Assert.IsInstanceOfType<Literal>(value);
        Literal literal = (Literal)value;
        Assert.AreEqual(expectedLexical, literal.Value.ToString());
        Assert.IsTrue(literal.Datatype.Iri.Span.SequenceEqual(expectedDatatype.Span), $"?{variableName}: expected the datatype '{expectedDatatype}', found '{literal.Datatype.Iri}'.");
    }
}
