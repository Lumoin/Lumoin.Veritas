using System;
using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Algebra.Rewriting;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// The GeoSPARQL Query Rewrite Extension as an algebra rewrite rule: a basic graph pattern whose predicate
/// is one of the twenty-four topological relation properties is expanded so the pattern also matches pairs
/// whose relation is derivable from their geometries, per the specification's transformation rules
/// (OGC 22-047r1, the query-rewrite requirements). This is a SEMANTIC rule, not a plan optimization: it
/// implements the extension's entailment-regime reading of BGP matching, so enabling it deliberately
/// changes answers to exactly what that extension specifies. It ships in no default pipeline; a host
/// composes it explicitly, beside the function catalog its derived branches call.
/// </summary>
/// <remarks>
/// <para>
/// <b>The replacement shape.</b> Each rewritten triple pattern <c>S rel O</c> becomes
/// <c>Distinct(Project(Union(asserted, derived), vars(S rel O)))</c>: the asserted branch is the original
/// single-triple pattern, and the derived branch unions the specification's four case rules —
/// feature-feature, feature-geometry, geometry-feature, and geometry-geometry — where a feature reaches its
/// geometry through <c>geo:hasDefaultGeometry</c>, a serialization is read through <c>geo:asWKT</c>, and
/// the case closes with a <c>FILTER</c> calling the relation's <c>geof:</c> predicate function on the two
/// serializations. The projection hides the internal join variables, and the surrounding
/// <c>Distinct</c> realizes the entailment regime's set answers: a pair counts once however many
/// witnesses — the asserted triple, several geometries, several cases — derive it.
/// </para>
/// <para>
/// <b>Bounds, stated.</b> Only constant-predicate triple patterns rewrite; a variable predicate and a
/// property path keep asserted-only matching (the specification leaves the unbound-predicate case to the
/// implementation). The serialization route is <c>geo:asWKT</c> — the WKT serialization parameter of the
/// claimed conformance classes. When the <c>geof:</c> functions are not registered in the evaluating
/// context, every derived-branch filter answers the expression error, so the pattern degrades to
/// asserted-only matching — silent, never wrong.
/// </para>
/// <para>
/// <b>The entry is one-shot by design</b> (no fixpoint participation): the replacement embeds the original
/// triple pattern as its asserted branch, so a further pass over the replacement would re-expand that
/// branch without bound. One bottom-up application rewrites every matching pattern in the tree exactly
/// once, and one entry covers all three relation families so a mixed pattern rewrites whole.
/// </para>
/// </remarks>
public static class GeoQueryRewrite
{
    /// <summary>The <c>geo:hasDefaultGeometry</c> predicate term of the derived branches' feature cases.</summary>
    private static ConstantTerm HasDefaultGeometryTerm { get; } = new(SourceSpan.None, new NamedNode(GeoVocabulary.Geo.HasDefaultGeometry));

    /// <summary>The <c>geo:asWKT</c> predicate term of the derived branches' serialization steps.</summary>
    private static ConstantTerm AsWktTerm { get; } = new(SourceSpan.None, new NamedNode(GeoVocabulary.Geo.AsWkt));

    /// <summary>
    /// The relation roster: each topological relation property IRI paired with the <c>geof:</c> predicate
    /// function that decides it. Authored from the requirement census; the order is the census order of
    /// the three families.
    /// </summary>
    private static (Utf8String Property, Utf8String Function)[] Relations { get; } =
    [
        (GeoVocabulary.Geo.SfEquals, GeoVocabulary.Geof.SfEquals),
        (GeoVocabulary.Geo.SfDisjoint, GeoVocabulary.Geof.SfDisjoint),
        (GeoVocabulary.Geo.SfIntersects, GeoVocabulary.Geof.SfIntersects),
        (GeoVocabulary.Geo.SfTouches, GeoVocabulary.Geof.SfTouches),
        (GeoVocabulary.Geo.SfCrosses, GeoVocabulary.Geof.SfCrosses),
        (GeoVocabulary.Geo.SfWithin, GeoVocabulary.Geof.SfWithin),
        (GeoVocabulary.Geo.SfContains, GeoVocabulary.Geof.SfContains),
        (GeoVocabulary.Geo.SfOverlaps, GeoVocabulary.Geof.SfOverlaps),
        (GeoVocabulary.Geo.EhEquals, GeoVocabulary.Geof.EhEquals),
        (GeoVocabulary.Geo.EhDisjoint, GeoVocabulary.Geof.EhDisjoint),
        (GeoVocabulary.Geo.EhMeet, GeoVocabulary.Geof.EhMeet),
        (GeoVocabulary.Geo.EhOverlap, GeoVocabulary.Geof.EhOverlap),
        (GeoVocabulary.Geo.EhCovers, GeoVocabulary.Geof.EhCovers),
        (GeoVocabulary.Geo.EhCoveredBy, GeoVocabulary.Geof.EhCoveredBy),
        (GeoVocabulary.Geo.EhInside, GeoVocabulary.Geof.EhInside),
        (GeoVocabulary.Geo.EhContains, GeoVocabulary.Geof.EhContains),
        (GeoVocabulary.Geo.Rcc8Eq, GeoVocabulary.Geof.Rcc8Eq),
        (GeoVocabulary.Geo.Rcc8Dc, GeoVocabulary.Geof.Rcc8Dc),
        (GeoVocabulary.Geo.Rcc8Ec, GeoVocabulary.Geof.Rcc8Ec),
        (GeoVocabulary.Geo.Rcc8Po, GeoVocabulary.Geof.Rcc8Po),
        (GeoVocabulary.Geo.Rcc8Tppi, GeoVocabulary.Geof.Rcc8Tppi),
        (GeoVocabulary.Geo.Rcc8Tpp, GeoVocabulary.Geof.Rcc8Tpp),
        (GeoVocabulary.Geo.Rcc8Ntpp, GeoVocabulary.Geof.Rcc8Ntpp),
        (GeoVocabulary.Geo.Rcc8Ntppi, GeoVocabulary.Geof.Rcc8Ntppi),
    ];

    /// <summary>
    /// The topological-relations rewrite entry, named <c>geo-topological-relations</c> in trace
    /// provenance. One-shot (no fixpoint participation) because its replacement embeds the original
    /// pattern as the asserted branch.
    /// </summary>
    public static AlgebraRewriteEntry TopologicalRelations { get; } = new("geo-topological-relations", ApplyTopologicalRelations, Fixpoint: false);

    /// <summary>Applies the topological-relations rewrite at one operator position.</summary>
    /// <param name="node">The operator position.</param>
    /// <param name="context">The rule context (unused — the rule is unconditional on its pattern).</param>
    /// <returns>The expanded pattern when the position is a BGP carrying at least one constant topological relation predicate, else not-applicable.</returns>
    private static AlgebraRewriteOutcome ApplyTopologicalRelations(AlgebraOperator node, in AlgebraRewriteContext context)
    {
        if(node is not Bgp bgp || bgp.Patterns.Count == 0)
        {
            return AlgebraRewriteOutcome.NotApplicable(node);
        }

        List<TriplePattern>? remainder = null;
        List<(TriplePattern Pattern, Utf8String Function)>? rewritten = null;
        foreach(TriplePattern pattern in bgp.Patterns)
        {
            if(TryMapRelation(pattern, out Utf8String function))
            {
                rewritten ??= [];
                rewritten.Add((pattern, function));
            }
            else
            {
                remainder ??= [];
                remainder.Add(pattern);
            }
        }

        if(rewritten is null)
        {
            return AlgebraRewriteOutcome.NotApplicable(node);
        }

        AlgebraOperator? result = remainder is null ? null : new Bgp(remainder);
        for(int i = 0; i < rewritten.Count; i++)
        {
            AlgebraOperator replacement = BuildRelationPattern(rewritten[i].Pattern, rewritten[i].Function, i);
            result = result is null ? replacement : new Join(result, replacement);
        }

        return AlgebraRewriteOutcome.Applied(result!);
    }

    /// <summary>Maps a triple pattern's constant predicate IRI to its deciding <c>geof:</c> function, when the predicate is a topological relation property.</summary>
    /// <param name="pattern">The triple pattern to inspect.</param>
    /// <param name="function">The deciding function IRI on success.</param>
    /// <returns><see langword="true"/> when the predicate is one of the twenty-four relation properties.</returns>
    private static bool TryMapRelation(TriplePattern pattern, out Utf8String function)
    {
        if(pattern.Predicate is ConstantTerm { Term: NamedNode named })
        {
            foreach((Utf8String property, Utf8String candidate) in Relations)
            {
                if(named.Iri.Span.SequenceEqual(property.Span))
                {
                    function = candidate;

                    return true;
                }
            }
        }

        function = default;

        return false;
    }

    /// <summary>
    /// Builds the replacement for one rewritten triple pattern: the union of the asserted branch and the
    /// four derived case rules, projected to the pattern's own variables and wrapped in the set-answer
    /// <c>Distinct</c>.
    /// </summary>
    /// <param name="pattern">The rewritten triple pattern.</param>
    /// <param name="function">The deciding <c>geof:</c> predicate function IRI.</param>
    /// <param name="ordinal">The pattern's zero-based ordinal among this BGP's rewritten patterns, folded into the internal variable names.</param>
    /// <returns>The replacement operator.</returns>
    private static Distinct BuildRelationPattern(TriplePattern pattern, Utf8String function, int ordinal)
    {
        //The '#' marker cannot appear in a parsed variable name, so the internal variables can never
        //collide with the query's own; the ordinal separates multiple rewritten patterns in one BGP.
        SparqlVariable subjectGeometry = FreshVariable("g1", ordinal);
        SparqlVariable objectGeometry = FreshVariable("g2", ordinal);
        SparqlVariable subjectSerialization = FreshVariable("w1", ordinal);
        SparqlVariable objectSerialization = FreshVariable("w2", ordinal);
        VariableTerm subjectGeometryTerm = new(SourceSpan.None, subjectGeometry);
        VariableTerm objectGeometryTerm = new(SourceSpan.None, objectGeometry);
        VariableTerm subjectSerializationTerm = new(SourceSpan.None, subjectSerialization);
        VariableTerm objectSerializationTerm = new(SourceSpan.None, objectSerialization);

        ExpressionNode call = new FunctionCallExpression(
            SourceSpan.None,
            new IriRef(function, SourceSpan.None),
            [new VariableExpression(SourceSpan.None, subjectSerialization), new VariableExpression(SourceSpan.None, objectSerialization)]);

        Bgp featureFeature = new(
        [
            Pattern(pattern.Subject, HasDefaultGeometryTerm, subjectGeometryTerm),
            Pattern(pattern.Object, HasDefaultGeometryTerm, objectGeometryTerm),
            Pattern(subjectGeometryTerm, AsWktTerm, subjectSerializationTerm),
            Pattern(objectGeometryTerm, AsWktTerm, objectSerializationTerm),
        ]);

        Bgp featureGeometry = new(
        [
            Pattern(pattern.Subject, HasDefaultGeometryTerm, subjectGeometryTerm),
            Pattern(subjectGeometryTerm, AsWktTerm, subjectSerializationTerm),
            Pattern(pattern.Object, AsWktTerm, objectSerializationTerm),
        ]);

        Bgp geometryFeature = new(
        [
            Pattern(pattern.Subject, AsWktTerm, subjectSerializationTerm),
            Pattern(pattern.Object, HasDefaultGeometryTerm, objectGeometryTerm),
            Pattern(objectGeometryTerm, AsWktTerm, objectSerializationTerm),
        ]);

        Bgp geometryGeometry = new(
        [
            Pattern(pattern.Subject, AsWktTerm, subjectSerializationTerm),
            Pattern(pattern.Object, AsWktTerm, objectSerializationTerm),
        ]);

        AlgebraOperator derived = new Union(
            new Union(new Filter(call, featureFeature), new Filter(call, featureGeometry)),
            new Union(new Filter(call, geometryFeature), new Filter(call, geometryGeometry)));

        Bgp asserted = new([pattern]);
        List<SparqlVariable> kept = [.. asserted.OutputVariables];

        return new Distinct(new Project(new Union(asserted, derived), kept));
    }

    /// <summary>Builds one internal variable, its name carrying the un-parseable <c>#</c> marker and the pattern ordinal.</summary>
    /// <param name="role">The variable's role tag within one rewritten pattern.</param>
    /// <param name="ordinal">The rewritten pattern's zero-based ordinal within its BGP.</param>
    /// <returns>The variable.</returns>
    private static SparqlVariable FreshVariable(string role, int ordinal)
    {
        return new SparqlVariable(Utf8Strings.From(string.Create(CultureInfo.InvariantCulture, $"geo#{role}#{ordinal}")));
    }

    /// <summary>Builds one synthesized triple pattern with the programmatic source span.</summary>
    /// <param name="subject">The subject term.</param>
    /// <param name="predicate">The predicate term.</param>
    /// <param name="object">The object term.</param>
    /// <returns>The triple pattern.</returns>
    private static TriplePattern Pattern(TriplePatternTerm subject, TriplePatternTerm predicate, TriplePatternTerm @object)
    {
        return new TriplePattern(SourceSpan.None, subject, predicate, @object);
    }
}
