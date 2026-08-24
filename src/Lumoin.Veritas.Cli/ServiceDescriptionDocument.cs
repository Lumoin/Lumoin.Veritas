using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;

namespace Lumoin.Veritas.Cli;

/// <summary>
/// Builds the endpoint's SPARQL 1.1 Service Description — the RDF document a protocol client receives
/// when it dereferences the endpoint IRI with no query parameter — from LIVE state: the served endpoint
/// address and the engine options' extension-function registry, so the whole registered function surface
/// (the GeoSPARQL <c>geof:</c> catalog under the default CLI composition) names itself without a
/// hand-maintained list. The document describes capabilities only — the query language, the result
/// formats, and the extension functions and aggregates — and never enumerates dataset content.
/// </summary>
internal static class ServiceDescriptionDocument
{
    /// <summary>The <c>rdf:type</c> predicate.</summary>
    private static NamedNode RdfType { get; } = new(Utf8Strings.From("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"));

    /// <summary>The <c>sd:Service</c> class.</summary>
    private static NamedNode SdService { get; } = new(Utf8Strings.From("http://www.w3.org/ns/sparql-service-description#Service"));

    /// <summary>The <c>sd:endpoint</c> predicate.</summary>
    private static NamedNode SdEndpoint { get; } = new(Utf8Strings.From("http://www.w3.org/ns/sparql-service-description#endpoint"));

    /// <summary>The <c>sd:supportedLanguage</c> predicate.</summary>
    private static NamedNode SdSupportedLanguage { get; } = new(Utf8Strings.From("http://www.w3.org/ns/sparql-service-description#supportedLanguage"));

    /// <summary>The <c>sd:SPARQL11Query</c> language individual.</summary>
    private static NamedNode SdSparql11Query { get; } = new(Utf8Strings.From("http://www.w3.org/ns/sparql-service-description#SPARQL11Query"));

    /// <summary>The <c>sd:resultFormat</c> predicate.</summary>
    private static NamedNode SdResultFormat { get; } = new(Utf8Strings.From("http://www.w3.org/ns/sparql-service-description#resultFormat"));

    /// <summary>The <c>sd:extensionFunction</c> predicate.</summary>
    private static NamedNode SdExtensionFunction { get; } = new(Utf8Strings.From("http://www.w3.org/ns/sparql-service-description#extensionFunction"));

    /// <summary>The <c>sd:extensionAggregate</c> predicate.</summary>
    private static NamedNode SdExtensionAggregate { get; } = new(Utf8Strings.From("http://www.w3.org/ns/sparql-service-description#extensionAggregate"));

    /// <summary>The W3C format-registry individual for SPARQL Results XML.</summary>
    private static NamedNode FormatResultsXml { get; } = new(Utf8Strings.From("http://www.w3.org/ns/formats/SPARQL_Results_XML"));

    /// <summary>The W3C format-registry individual for SPARQL Results JSON.</summary>
    private static NamedNode FormatResultsJson { get; } = new(Utf8Strings.From("http://www.w3.org/ns/formats/SPARQL_Results_JSON"));

    /// <summary>The W3C format-registry individual for SPARQL Results CSV.</summary>
    private static NamedNode FormatResultsCsv { get; } = new(Utf8Strings.From("http://www.w3.org/ns/formats/SPARQL_Results_CSV"));

    /// <summary>The W3C format-registry individual for SPARQL Results TSV.</summary>
    private static NamedNode FormatResultsTsv { get; } = new(Utf8Strings.From("http://www.w3.org/ns/formats/SPARQL_Results_TSV"));

    /// <summary>The W3C format-registry individual for N-Triples.</summary>
    private static NamedNode FormatNTriples { get; } = new(Utf8Strings.From("http://www.w3.org/ns/formats/N-Triples"));

    /// <summary>The W3C format-registry individual for Turtle.</summary>
    private static NamedNode FormatTurtle { get; } = new(Utf8Strings.From("http://www.w3.org/ns/formats/Turtle"));

    /// <summary>Renders the service description for the endpoint at <paramref name="endpointUrl"/> as a Turtle document.</summary>
    /// <param name="endpointUrl">The absolute URL the endpoint answers at; it doubles as the service subject.</param>
    /// <param name="options">The engine options whose live registries the description enumerates.</param>
    /// <returns>The Turtle document.</returns>
    public static string Render(string endpointUrl, VeritasEngineOptions options)
    {
        NamedNode service = new(Utf8Strings.From(endpointUrl));
        List<Quad> quads =
        [
            new Quad(service, RdfType, SdService),
            new Quad(service, SdEndpoint, service),
            new Quad(service, SdSupportedLanguage, SdSparql11Query),
            new Quad(service, SdResultFormat, FormatResultsXml),
            new Quad(service, SdResultFormat, FormatResultsJson),
            new Quad(service, SdResultFormat, FormatResultsCsv),
            new Quad(service, SdResultFormat, FormatResultsTsv),
            new Quad(service, SdResultFormat, FormatNTriples),
            new Quad(service, SdResultFormat, FormatTurtle)
        ];

        foreach(Utf8String functionIri in options.ExtensionFunctions.FunctionIris)
        {
            quads.Add(new Quad(service, SdExtensionFunction, new NamedNode(functionIri)));
        }

        foreach(Utf8String aggregateIri in options.ExtensionFunctions.AggregateIris)
        {
            quads.Add(new Quad(service, SdExtensionAggregate, new NamedNode(aggregateIri)));
        }

        return VeritasOperations.RenderTurtle(quads);
    }
}
