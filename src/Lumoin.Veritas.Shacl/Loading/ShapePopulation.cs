using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Shacl.Targets;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Populates a <see cref="ShapeBuilder"/> from the triples of a single
/// shape in the shape graph. One invocation per discovered shape; the
/// orchestration happens in <see cref="ShapeLoader"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Single-pass population.</b> Because shape-referencing constraints
/// hold <see cref="TermId"/> values rather than live <see cref="Shape"/>
/// references (R-4), every constraint factory can run during one pass
/// regardless of what other shapes its output refers to. The routine
/// reads all triples on the shape, classifies each by predicate (path,
/// target, metadata, or constraint parameter), walks any RDF lists
/// reachable from constraint-parameter values, then invokes the
/// matching constraint factory for each primary-parameter occurrence.
/// </para>
/// <para>
/// <b>Storage-agnostic.</b> The only storage touchpoint is the
/// <see cref="StorageDelegates.MatchTriplesAsync"/> delegate supplied
/// by the caller. Population does not assume any particular index
/// layout; it works identically against sorted-array stores and
/// against hypertrie-backed stores. The only requirement is that the
/// delegate honors the three-position access pattern
/// (<c>(subject, predicate, object)</c> with any of the three bound).
/// </para>
/// <para>
/// <b>Companion-parameter scope.</b> Companion parameters (e.g.,
/// <c>sh:flags</c> for <c>sh:pattern</c>, <c>sh:qualifiedValueShape</c>
/// for <c>sh:qualifiedMinCount</c>) are shape-scoped: they are read
/// once per shape and seen identically by every factory invocation on
/// that shape. SHACL semantics do not pair companions to specific
/// primary occurrences; they apply to the shape as a whole.
/// </para>
/// <para>
/// <b>RDF list resolution.</b> Any constraint-parameter value that is
/// the head of an RDF list (identifiable by a <c>rdf:first</c>
/// outgoing edge, or by being <c>rdf:nil</c>) is walked into an
/// <see cref="ImmutableArray{T}"/> of member term ids before factory
/// invocation. Factories see only pre-resolved lists. The walker
/// tolerates shapes of list data across different parameters — mixed
/// list and non-list — without requiring components to declare which
/// parameters are list-typed. Nested lists are not resolved.
/// </para>
/// </remarks>
internal static class ShapePopulation
{
    /// <summary>
    /// Populates the builder in place from the shape's triples.
    /// </summary>
    /// <param name="builder">The builder to populate. Modified in place.</param>
    /// <param name="shapeGraphMatch">The shape-graph triple source.</param>
    /// <param name="context">Loader-scoped vocabulary, registry, and configuration.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task PopulateAsync(
        ShapeBuilder builder,
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        ShapePopulationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(shapeGraphMatch);
        ArgumentNullException.ThrowIfNull(context);

        Dictionary<IriId, List<TermId>> parameterValues = [];
        TermId? pathValue = null;
        List<TermId> sparqlConstraintNodes = [];
        bool isClass = false;

        await foreach(EncodedTriple triple in shapeGraphMatch(builder.Id, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            IriId predicate = IriId.FromUnchecked(triple.Predicate);
            TermId value = triple.Object;

            if(predicate.Equals(context.ShaclIds.Path))
            {
                if(pathValue.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Property shape {builder.Id} has multiple sh:path triples; SHACL requires exactly one.");
                }

                pathValue = value;
                continue;
            }

            //sh:sparql links to a SPARQL-based constraint whose query/message/prefixes live in a sub-graph on
            //the linked node — like sh:path, this is parsed structurally (after the scan), not as a parameter
            //scalar through the constraint-factory mechanism.
            if(predicate.Equals(context.ShaclIds.Sparql))
            {
                sparqlConstraintNodes.Add(value);
                continue;
            }

            //A shape that is also an rdfs:Class implicitly targets every instance of that class (§2.1.3.3).
            if(predicate.Equals(context.RdfsVocabulary.RdfType) && value.Equals((TermId)context.ShaclIds.RdfsClass))
            {
                isClass = true;
            }

            if(TryHandleMetadata(builder, predicate, value, context))
            {
                continue;
            }

            if(TryHandleTarget(builder, predicate, value, context))
            {
                continue;
            }

            if(context.KnownParameterIds.Contains(predicate))
            {
                if(!parameterValues.TryGetValue(predicate, out List<TermId>? values))
                {
                    values = [];
                    parameterValues[predicate] = values;
                }

                values.Add(value);
                continue;
            }

            //Other predicates are silently ignored — rdf:type for the
            //shape class, custom metadata, extensions not registered
            //with this loader.
        }

        //A shape that is also an rdfs:Class targets every instance of that class — an implicit class target.
        if(isClass)
        {
            builder.Targets.Add(new ImplicitClassTarget(
                IriId.FromUnchecked(builder.Id),
                context.RdfsVocabulary.RdfType,
                context.RdfsVocabulary.RdfsSubClassOf));
        }

        if(builder.IsPropertyShape)
        {
            if(!pathValue.HasValue)
            {
                throw new InvalidOperationException(
                    $"Discovered property shape {builder.Id} has no sh:path triple during population; discovery bug.");
            }

            builder.Path = await ShapePathParser.ParseAsync(
                pathValue.Value,
                shapeGraphMatch,
                context.Dictionary,
                context.PathVocabulary,
                context.RdfListIds,
                cancellationToken).ConfigureAwait(false);
        }

        Dictionary<TermId, ImmutableArray<TermId>> resolvedLists = await ResolveReferencedListsAsync(
            parameterValues,
            shapeGraphMatch,
            context.RdfListIds,
            cancellationToken).ConfigureAwait(false);

        foreach((IriId parameterIri, List<TermId> occurrences) in parameterValues)
        {
            if(!context.ComponentsByPrimaryParameter.TryGetValue(parameterIri, out ConstraintComponentInfo info))
            {
                //Not a primary; this parameter is a companion of some
                //other component and will be consumed alongside that
                //component's primary invocation.
                continue;
            }

            IReadOnlyDictionary<IriId, IReadOnlyList<TermId>> companions = BuildCompanionView(
                parameterIri,
                parameterValues,
                context);

            foreach(TermId primaryValue in occurrences)
            {
                ParameterBag bag = new(
                    parameterIri,
                    primaryValue,
                    companions,
                    resolvedLists,
                    context.Dictionary,
                    context.RdfsVocabulary,
                    context.Options,
                    context.PatternMemo);

                //A null return means the component declined to instantiate
                //because a mandatory companion parameter is absent (e.g. a
                //sh:qualifiedMinCount with no sh:qualifiedValueShape) — skip it
                //rather than recording a constraint that cannot be evaluated.
                ConstraintComponent? component = info.Factory(bag);
                if(component is not null)
                {
                    builder.Constraints.Add(component);
                }
            }
        }

        foreach(TermId sparqlConstraintNode in sparqlConstraintNodes)
        {
            SparqlConstraint? sparqlConstraint = await SparqlConstraintParser.ParseAsync(
                sparqlConstraintNode,
                shapeGraphMatch,
                context.Dictionary,
                context.ShaclIds,
                cancellationToken).ConfigureAwait(false);

            //A null result is a not-yet-enforceable constraint (deactivated, ask-only, or unparseable); skip it
            //rather than add a constraint with no query.
            if(sparqlConstraint is not null)
            {
                builder.Constraints.Add(sparqlConstraint);
            }
        }
    }

    private static bool TryHandleMetadata(
        ShapeBuilder builder,
        IriId predicate,
        TermId value,
        ShapePopulationContext context)
    {
        ShaclPopulationIds ids = context.ShaclIds;

        if(predicate.Equals(ids.Severity))
        {
            builder.Severity = ResolveSeverity(value, context);
            return true;
        }

        if(predicate.Equals(ids.Deactivated))
        {
            builder.Deactivated = ParseBoolLiteral(value, context.Dictionary);
            return true;
        }

        if(predicate.Equals(ids.Message))
        {
            if(context.Dictionary.Resolve(value) is Literal literal)
            {
                string lang = literal.Language is { } tag ? tag.ToString() : string.Empty;
                builder.Messages[lang] = literal.Value.ToString();
            }

            return true;
        }

        return false;
    }

    private static bool TryHandleTarget(
        ShapeBuilder builder,
        IriId predicate,
        TermId value,
        ShapePopulationContext context)
    {
        ShaclPopulationIds ids = context.ShaclIds;

        if(predicate.Equals(ids.TargetClass))
        {
            builder.Targets.Add(new TargetClass(
                IriId.FromUnchecked(value),
                context.RdfsVocabulary.RdfType,
                context.RdfsVocabulary.RdfsSubClassOf));
            return true;
        }

        if(predicate.Equals(ids.TargetNode))
        {
            builder.Targets.Add(new TargetNode(value));
            return true;
        }

        if(predicate.Equals(ids.TargetSubjectsOf))
        {
            builder.Targets.Add(new TargetSubjectsOf(IriId.FromUnchecked(value)));
            return true;
        }

        if(predicate.Equals(ids.TargetObjectsOf))
        {
            builder.Targets.Add(new TargetObjectsOf(IriId.FromUnchecked(value)));
            return true;
        }

        return false;
    }

    private static Severity ResolveSeverity(TermId value, ShapePopulationContext context)
    {
        //SHACL §4.6: sh:severity is an IRI. The three standard levels are the
        //common case, but any IRI is permitted and carried through verbatim to
        //sh:resultSeverity. So the severity is simply the referenced IRI.
        if(context.Dictionary.Resolve(value) is NamedNode named)
        {
            return new Severity(named.Iri);
        }

        throw new FormatException(
            $"sh:severity value {context.Dictionary.Resolve(value)} is not an IRI.");
    }

    private static bool ParseBoolLiteral(TermId value, TermDictionary dictionary)
    {
        if(dictionary.Resolve(value) is Literal literal)
        {
            //SHACL activation switch (sh:deactivated): on only for the literal
            //"true". "1"^^xsd:boolean is a valid xsd:boolean whose value is
            //true, but does not activate the switch — see ParameterBag.ParseBool
            //and W3C core/property/uniqueLang-002.
            ReadOnlySpan<byte> lexical = literal.Value.Span;
            if(lexical.SequenceEqual("true"u8))
            {
                return true;
            }

            if(lexical.SequenceEqual("false"u8) || lexical.SequenceEqual("0"u8) || lexical.SequenceEqual("1"u8))
            {
                return false;
            }
        }

        throw new FormatException(
            $"Expected xsd:boolean literal, got {dictionary.Resolve(value)}.");
    }

    private static IReadOnlyDictionary<IriId, IReadOnlyList<TermId>> BuildCompanionView(
        IriId primaryParameterId,
        Dictionary<IriId, List<TermId>> allValues,
        ShapePopulationContext context)
    {
        if(!context.CompanionParametersByPrimaryParameter.TryGetValue(primaryParameterId, out ImmutableArray<IriId> companionIds)
            || companionIds.IsDefaultOrEmpty)
        {
            return EmptyCompanions;
        }

        Dictionary<IriId, IReadOnlyList<TermId>> view = [];
        foreach(IriId companionId in companionIds)
        {
            if(allValues.TryGetValue(companionId, out List<TermId>? values))
            {
                view[companionId] = values;
            }
        }

        return view;
    }

    private static async Task<Dictionary<TermId, ImmutableArray<TermId>>> ResolveReferencedListsAsync(
        Dictionary<IriId, List<TermId>> parameterValues,
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        RdfListIds rdfListIds,
        CancellationToken cancellationToken)
    {
        Dictionary<TermId, ImmutableArray<TermId>> resolved = [];

        foreach(List<TermId> values in parameterValues.Values)
        {
            foreach(TermId candidate in values)
            {
                if(resolved.ContainsKey(candidate))
                {
                    continue;
                }

                if(candidate.Equals((TermId)rdfListIds.RdfNil))
                {
                    resolved[candidate] = ImmutableArray<TermId>.Empty;
                    continue;
                }

                ImmutableArray<TermId>? members = await TryWalkListAsync(
                    candidate,
                    shapeGraphMatch,
                    rdfListIds,
                    cancellationToken).ConfigureAwait(false);

                if(members is { } walked)
                {
                    resolved[candidate] = walked;
                }
            }
        }

        return resolved;
    }

    private static async Task<ImmutableArray<TermId>?> TryWalkListAsync(
        TermId head,
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        RdfListIds rdfListIds,
        CancellationToken cancellationToken)
    {
        ImmutableArray<TermId>.Builder builder = ImmutableArray.CreateBuilder<TermId>();
        TermId current = head;

        while(!current.Equals((TermId)rdfListIds.RdfNil))
        {
            TermId? first = null;
            TermId? rest = null;

            await foreach(EncodedTriple triple in shapeGraphMatch(current, rdfListIds.RdfFirst, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                first = triple.Object;
                break;
            }

            if(first is null)
            {
                //Not a list head.
                return null;
            }

            await foreach(EncodedTriple triple in shapeGraphMatch(current, rdfListIds.RdfRest, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                rest = triple.Object;
                break;
            }

            if(rest is null)
            {
                throw new InvalidOperationException($"Malformed RDF list: term {current} has rdf:first but no rdf:rest.");
            }

            builder.Add(first.Value);
            current = rest.Value;
        }

        return builder.ToImmutable();
    }

    private static IReadOnlyDictionary<IriId, IReadOnlyList<TermId>> EmptyCompanions { get; }
        = new Dictionary<IriId, IReadOnlyList<TermId>>();
}
