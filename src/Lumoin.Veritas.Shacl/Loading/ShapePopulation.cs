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

                ConstraintComponent component = info.Factory(bag);
                builder.Constraints.Add(component);
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
        ShaclPopulationIds ids = context.ShaclIds;

        if(value.Equals((TermId)ids.Violation))
        {
            return Severity.Violation;
        }

        if(value.Equals((TermId)ids.Warning))
        {
            return Severity.Warning;
        }

        if(value.Equals((TermId)ids.Info))
        {
            return Severity.Info;
        }

        throw new FormatException(
            $"Unknown sh:severity value {context.Dictionary.Resolve(value)}; expected sh:Violation, sh:Warning, or sh:Info.");
    }

    private static bool ParseBoolLiteral(TermId value, TermDictionary dictionary)
    {
        if(dictionary.Resolve(value) is Literal literal)
        {
            //The xsd:boolean lexical space is exactly {true, false, 1, 0},
            //case-sensitive.
            ReadOnlySpan<byte> lexical = literal.Value.Span;
            if(lexical.SequenceEqual("true"u8) || lexical.SequenceEqual("1"u8))
            {
                return true;
            }

            if(lexical.SequenceEqual("false"u8) || lexical.SequenceEqual("0"u8))
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
