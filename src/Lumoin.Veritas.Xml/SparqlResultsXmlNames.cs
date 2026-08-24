namespace Lumoin.Veritas.Xml;

/// <summary>
/// The well-known element and attribute local names of the
/// <see href="https://www.w3.org/TR/sparql12-results-xml/">SPARQL Query Results XML Format</see>, referenced by
/// both <see cref="SparqlResultsXmlWriter"/> and <see cref="SparqlResultsXmlReader"/> so the two sides share one
/// source of truth (the enclosing namespace is held separately as the results namespace IRI).
/// </summary>
internal static class SparqlResultsXmlNames
{
    /// <summary>The document root element <c>sparql</c>.</summary>
    public const string Sparql = "sparql";

    /// <summary>The <c>head</c> element listing the result variables.</summary>
    public const string Head = "head";

    /// <summary>The <c>variable</c> element naming one projected variable.</summary>
    public const string Variable = "variable";

    /// <summary>The <c>results</c> element wrapping the solution rows.</summary>
    public const string Results = "results";

    /// <summary>The <c>result</c> element of one solution row.</summary>
    public const string Result = "result";

    /// <summary>The <c>binding</c> element of one variable's value in a row.</summary>
    public const string Binding = "binding";

    /// <summary>The <c>boolean</c> element carrying an <c>ASK</c> result.</summary>
    public const string Boolean = "boolean";

    /// <summary>The <c>triple</c> element of an RDF 1.2 quoted-triple binding value.</summary>
    public const string Triple = "triple";

    /// <summary>The <c>subject</c> component element of a quoted triple.</summary>
    public const string Subject = "subject";

    /// <summary>The <c>predicate</c> component element of a quoted triple.</summary>
    public const string Predicate = "predicate";

    /// <summary>The <c>object</c> component element of a quoted triple.</summary>
    public const string Object = "object";

    /// <summary>The <c>uri</c> element of an IRI binding value.</summary>
    public const string Uri = "uri";

    /// <summary>The <c>bnode</c> element of a blank-node binding value.</summary>
    public const string Bnode = "bnode";

    /// <summary>The <c>literal</c> element of a literal binding value.</summary>
    public const string Literal = "literal";

    /// <summary>The <c>name</c> attribute of a <c>variable</c> or <c>binding</c> element.</summary>
    public const string Name = "name";

    /// <summary>The <c>datatype</c> attribute of a typed <c>literal</c> element.</summary>
    public const string Datatype = "datatype";
}
