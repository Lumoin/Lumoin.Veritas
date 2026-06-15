using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Database;

/// <summary>
/// The result of a query against a <see cref="VeritasEngine"/>: a SELECT's
/// solution bindings, or an ASK's boolean. Exactly one is present, per the
/// query's form, so a transport renders it to the requested SPARQL result
/// format without parsing the query itself to learn the form.
/// </summary>
public sealed record VeritasQueryResult
{
    /// <summary>Constructs a result holding exactly one of the bindings or the boolean.</summary>
    /// <param name="bindings">The SELECT bindings, or <c>null</c> for an ASK.</param>
    /// <param name="boolean">The ASK boolean, or <c>null</c> for a SELECT.</param>
    private VeritasQueryResult(SparqlResultSet? bindings, bool? boolean)
    {
        Bindings = bindings;
        Boolean = boolean;
    }

    /// <summary>The SELECT solution bindings, or <c>null</c> when the query was an ASK.</summary>
    public SparqlResultSet? Bindings { get; }

    /// <summary>The ASK boolean, or <c>null</c> when the query was a SELECT.</summary>
    public bool? Boolean { get; }

    /// <summary>Whether the result is an ASK boolean rather than SELECT bindings.</summary>
    public bool IsAsk => Boolean is not null;

    /// <summary>Builds a SELECT result over its bindings.</summary>
    /// <param name="bindings">The solution bindings.</param>
    /// <returns>The result.</returns>
    public static VeritasQueryResult ForSelect(SparqlResultSet bindings)
    {
        return new VeritasQueryResult(bindings, boolean: null);
    }

    /// <summary>Builds an ASK result over its boolean.</summary>
    /// <param name="boolean">The ASK answer.</param>
    /// <returns>The result.</returns>
    public static VeritasQueryResult ForAsk(bool boolean)
    {
        return new VeritasQueryResult(bindings: null, boolean);
    }
}
