namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// The canonical home for the RDF and XSD IRIs the JSON-LD ↔ RDF bridge
/// (<see cref="JsonLdRdfSerializer"/> toRdf and <see cref="JsonLdRdfDeserializer"/>
/// fromRdf) tests against: the <c>rdf:</c> list/typing vocabulary, the XSD
/// datatypes given native or canonical treatment, and the i18n datatype
/// namespace for base-direction round-tripping.
/// </summary>
internal static class JsonLdRdfTerms
{
    /// <summary>The <c>rdf:type</c> predicate IRI.</summary>
    public const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>The <c>rdf:first</c> predicate IRI (RDF list head).</summary>
    public const string RdfFirst = "http://www.w3.org/1999/02/22-rdf-syntax-ns#first";

    /// <summary>The <c>rdf:rest</c> predicate IRI (RDF list tail).</summary>
    public const string RdfRest = "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest";

    /// <summary>The <c>rdf:nil</c> IRI (the empty RDF list).</summary>
    public const string RdfNil = "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil";

    /// <summary>The <c>rdf:List</c> class IRI.</summary>
    public const string RdfList = "http://www.w3.org/1999/02/22-rdf-syntax-ns#List";

    /// <summary>The <c>rdf:JSON</c> datatype IRI (a JSON literal).</summary>
    public const string RdfJson = "http://www.w3.org/1999/02/22-rdf-syntax-ns#JSON";

    /// <summary>The <c>rdf:langString</c> datatype IRI (a language-tagged string).</summary>
    public const string RdfLangString = "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString";

    /// <summary>The <c>rdf:value</c> predicate IRI.</summary>
    public const string RdfValue = "http://www.w3.org/1999/02/22-rdf-syntax-ns#value";

    /// <summary>The <c>rdf:language</c> predicate IRI (compound-literal direction).</summary>
    public const string RdfLanguage = "http://www.w3.org/1999/02/22-rdf-syntax-ns#language";

    /// <summary>The <c>rdf:direction</c> predicate IRI (compound-literal direction).</summary>
    public const string RdfDirection = "http://www.w3.org/1999/02/22-rdf-syntax-ns#direction";

    /// <summary>The <c>xsd:string</c> datatype IRI.</summary>
    public const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    /// <summary>The <c>xsd:boolean</c> datatype IRI.</summary>
    public const string XsdBoolean = "http://www.w3.org/2001/XMLSchema#boolean";

    /// <summary>The <c>xsd:integer</c> datatype IRI.</summary>
    public const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    /// <summary>The <c>xsd:double</c> datatype IRI.</summary>
    public const string XsdDouble = "http://www.w3.org/2001/XMLSchema#double";

    /// <summary>The i18n datatype namespace (<c>https://www.w3.org/ns/i18n#</c>) for language+direction datatypes.</summary>
    public const string I18nNamespace = "https://www.w3.org/ns/i18n#";
}
