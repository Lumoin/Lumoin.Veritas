using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Completion;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Database.Completion;

/// <summary>
/// Fills the RDF datatypes of a store-free <see cref="CompletionContext"/> (produced by
/// <see cref="SparqlCompletion.Describe"/>) by querying a live <see cref="VeritasEngine"/>. For each in-scope
/// variable bound in an object position, it runs a three-tier resolution ladder — a SHACL property shape's
/// <c>sh:datatype</c>, an <c>rdfs:range</c> declaration, then a sampled <c>DATATYPE()</c> over the data —
/// strongest intent first, and records the datatype IRI together with the source that produced it. It is glue
/// over the query path: no engine internals, no parser, no change to the SPARQL layer (which never references the
/// store). Resolution degrades gracefully — a tier whose data is absent simply misses, a variable with no object
/// predicate or no resolvable datatype stays <see cref="DatatypeSource.Unknown"/>, and a query that fails never
/// propagates out of the resolver, so a completion request can never fault the editor.
/// </summary>
/// <remarks>
/// Against an immutable engine the three tier queries observe one fixed, content-addressed snapshot — repeatable
/// reads for free, with no caller-held pin. A mutable engine re-snapshots per query, so a concurrent commit can
/// shift results between tiers; prefer an immutable engine for completion when strict cross-tier isolation matters.
/// </remarks>
public static class SparqlCompletionDatatypes
{
    /// <summary>The result variable every tier query binds the datatype into; its name carries no leading <c>?</c>, matching <see cref="SparqlVariable"/>.</summary>
    private static SparqlVariable DatatypeVariable { get; } = new(Utf8Strings.From("dt"));

    /// <summary>
    /// Resolves each in-scope variable's RDF datatype against <paramref name="engine"/> and returns
    /// <paramref name="context"/> with <see cref="ScopeVariable.Datatype"/> and
    /// <see cref="ScopeVariable.DatatypeSource"/> filled where a datatype could be determined; the other context
    /// fields pass through unchanged. A variable bound by more than one object predicate resolves against the
    /// first; a variable with no object-position predicate, or none of whose tiers hit, stays
    /// <see cref="DatatypeSource.Unknown"/>.
    /// </summary>
    /// <param name="engine">The live engine the resolution queries run against.</param>
    /// <param name="context">The store-free completion context whose variables are to be datatyped.</param>
    /// <param name="accessContext">The opaque access-control context threaded through every resolution query, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the resolution; an in-flight tier query observes it.</param>
    /// <returns>The context with each resolvable variable's datatype filled.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="engine"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    public static async ValueTask<CompletionContext> ResolveAsync(
        VeritasEngine engine,
        CompletionContext context,
        AccessContext? accessContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(context);

        if(context.InScopeVariables.Count == 0)
        {
            return context;
        }

        ScopeVariable[] enriched = new ScopeVariable[context.InScopeVariables.Count];
        for(int i = 0; i < context.InScopeVariables.Count; i++)
        {
            enriched[i] = await ResolveVariableAsync(engine, context, context.InScopeVariables[i], accessContext, cancellationToken).ConfigureAwait(false);
        }

        return context with { InScopeVariables = enriched };
    }

    /// <summary>
    /// Resolves one variable's datatype through the three-tier ladder, returning it enriched with the first tier
    /// that hit, or unchanged when no object predicate binds it or no tier resolves a datatype.
    /// </summary>
    /// <param name="engine">The engine the tier queries run against.</param>
    /// <param name="context">The completion context the variable→predicate pairs are read from.</param>
    /// <param name="scopeVariable">The variable to datatype.</param>
    /// <param name="accessContext">The access-control context threaded through each tier query.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>The variable with its datatype filled, or unchanged when none could be resolved.</returns>
    private static async ValueTask<ScopeVariable> ResolveVariableAsync(
        VeritasEngine engine,
        CompletionContext context,
        ScopeVariable scopeVariable,
        AccessContext? accessContext,
        CancellationToken cancellationToken)
    {
        if(FindObjectPredicate(context, scopeVariable.Variable) is not { } predicate)
        {
            return scopeVariable;
        }

        //Strongest intent first, stop at the first hit: a declared SHACL datatype, then a declared rdfs:range
        //(materialized OWL data-property ranges land here too), then an observed DATATYPE() sample of the data.
        Utf8String? shacl = await QueryDatatypeAsync(engine, ShaclDatatypeQuery(predicate), accessContext, cancellationToken).ConfigureAwait(false);
        if(shacl is not null)
        {
            return scopeVariable with { Datatype = shacl, DatatypeSource = DatatypeSource.ShaclShape };
        }

        Utf8String? range = await QueryDatatypeAsync(engine, RangeQuery(predicate), accessContext, cancellationToken).ConfigureAwait(false);
        if(range is not null)
        {
            return scopeVariable with { Datatype = range, DatatypeSource = DatatypeSource.RdfsRange };
        }

        Utf8String? sample = await QueryDatatypeAsync(engine, SampleQuery(predicate), accessContext, cancellationToken).ConfigureAwait(false);
        if(sample is not null)
        {
            return scopeVariable with { Datatype = sample, DatatypeSource = DatatypeSource.DataSample };
        }

        return scopeVariable;
    }

    /// <summary>
    /// Finds the predicate IRI that binds <paramref name="variable"/> in an object position — the binding a literal
    /// datatype can be inferred from — taking the first such pair (a subject position identifies a resource, not a
    /// literal datatype) and requiring a predicate IRI usable inside a SPARQL <c>&lt;...&gt;</c> term.
    /// </summary>
    /// <param name="context">The completion context whose variable→predicate pairs are scanned.</param>
    /// <param name="variable">The variable to find an object-position predicate for.</param>
    /// <returns>The predicate IRI, or <see langword="null"/> when the variable binds no usable object predicate.</returns>
    private static Utf8String? FindObjectPredicate(CompletionContext context, SparqlVariable variable)
    {
        for(int i = 0; i < context.VariablePredicates.Count; i++)
        {
            VariablePredicate pair = context.VariablePredicates[i];
            if(pair.Position == TermPosition.Object && pair.Variable == variable && IsUsableIriReference(pair.Predicate))
            {
                return pair.Predicate;
            }
        }

        return null;
    }

    /// <summary>
    /// Runs one resolution query and reads the single bound datatype IRI from its first solution, returning
    /// <see langword="null"/> on no row, a non-IRI binding, or any query failure: completion is best-effort, so a
    /// tier whose query cannot be answered simply misses rather than surfacing the failure into the editor.
    /// </summary>
    /// <param name="engine">The engine the query runs against.</param>
    /// <param name="sparql">The tier query selecting <c>?dt</c>.</param>
    /// <param name="accessContext">The access-control context threaded into the query.</param>
    /// <param name="cancellationToken">Cancels the query; cancellation propagates rather than being swallowed as a miss.</param>
    /// <returns>The resolved datatype IRI, or <see langword="null"/> on a miss or failure.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Datatype resolution for editor completion is best-effort: a tier query that cannot be answered (an unsupported feature, an access denial, malformed data) is a miss, never a fault surfaced into the editor. Cancellation is rethrown so it still propagates.")]
    private static async ValueTask<Utf8String?> QueryDatatypeAsync(VeritasEngine engine, string sparql, AccessContext? accessContext, CancellationToken cancellationToken)
    {
        VeritasQueryResult result;
        try
        {
            result = await engine.QueryAsync(Utf8Strings.From(sparql), accessContext: accessContext, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException)
        {
            throw;
        }
        catch(Exception)
        {
            return null;
        }

        SparqlResultSet? rows = result.Bindings;
        if(rows is null || rows.Solutions.Count == 0)
        {
            return null;
        }

        return rows.Solutions[0].TryGetValue(DatatypeVariable, out RdfTerm term) && term is NamedNode named
            ? named.Iri
            : null;
    }

    /// <summary>Builds the tier-1 query: a SHACL property shape whose <c>sh:path</c> is the predicate and which declares an <c>sh:datatype</c>.</summary>
    /// <param name="predicateIri">The object-position predicate IRI, already validated for a <c>&lt;...&gt;</c> term.</param>
    /// <returns>The SHACL datatype query.</returns>
    private static string ShaclDatatypeQuery(Utf8String predicateIri)
    {
        return $"SELECT ?dt WHERE {{ ?shape <{ShaclCoreVocabulary.Path}> <{predicateIri}> ; <{ShaclConstraintVocabulary.Datatype}> ?dt }} LIMIT 1";
    }

    /// <summary>Builds the tier-2 query: the predicate's declared <c>rdfs:range</c> (a materialized OWL data-property range answers here too).</summary>
    /// <param name="predicateIri">The object-position predicate IRI, already validated for a <c>&lt;...&gt;</c> term.</param>
    /// <returns>The range query.</returns>
    private static string RangeQuery(Utf8String predicateIri)
    {
        return $"SELECT ?dt WHERE {{ <{predicateIri}> <{RdfVocabulary.Rdfs.Range}> ?dt }} LIMIT 1";
    }

    /// <summary>Builds the tier-3 query: the datatype of one observed object of the predicate. An IRI or blank-node object yields no datatype (a miss, not a wrong answer); an untyped literal yields <c>xsd:string</c>.</summary>
    /// <param name="predicateIri">The object-position predicate IRI, already validated for a <c>&lt;...&gt;</c> term.</param>
    /// <returns>The data-sample query.</returns>
    private static string SampleQuery(Utf8String predicateIri)
    {
        return $"SELECT ?dt WHERE {{ ?s <{predicateIri}> ?o . BIND(DATATYPE(?o) AS ?dt) }} LIMIT 1";
    }

    /// <summary>
    /// Whether an IRI is usable verbatim inside a SPARQL <c>&lt;...&gt;</c> term — non-empty and free of every
    /// character the SPARQL <c>IRIREF</c> production forbids (the controls and space below <c>0x21</c> and the
    /// set <c>&lt; &gt; " { } | ^ ` \</c>). A constant predicate from the parser always satisfies this; the guard
    /// also keeps a malformed or crafted IRI from breaking out of the term.
    /// </summary>
    /// <param name="iri">The predicate IRI to validate.</param>
    /// <returns><see langword="true"/> when the IRI can be embedded directly in a <c>&lt;...&gt;</c> term.</returns>
    private static bool IsUsableIriReference(Utf8String iri)
    {
        ReadOnlySpan<byte> bytes = iri.Span;
        if(bytes.IsEmpty)
        {
            return false;
        }

        foreach(byte value in bytes)
        {
            bool forbidden = value <= 0x20 || value switch
            {
                (byte)'<' or (byte)'>' or (byte)'"' or (byte)'{' or (byte)'}' or (byte)'|' or (byte)'^' or (byte)'`' or (byte)'\\' => true,
                _ => false
            };
            if(forbidden)
            {
                return false;
            }
        }

        return true;
    }
}
