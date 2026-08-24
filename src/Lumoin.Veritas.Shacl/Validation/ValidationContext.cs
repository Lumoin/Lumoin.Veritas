using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Sparql.Execution;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Shacl.Validation;

/// <summary>
/// Environment handed to a <see cref="ConstraintEvaluator"/> at each
/// invocation. Carries the data-graph match delegate, the shape
/// registry, the shared term dictionary, the validator options in
/// effect, a per-run class-membership cache, the pre-resolved RDF
/// list vocabulary identifiers, and — for recursion evaluators — a
/// shape-validator delegate that recursively validates an inner
/// shape at a specified focus node.
/// </summary>
/// <remarks>
/// <para>
/// Except for <see cref="ClassMembershipCache"/>,
/// <see cref="SubclassClosureCache"/>, and the recursion-guard set
/// that <see cref="ShapeValidator"/> carries internally, the context
/// is constant for the duration of a single
/// <see cref="ShaclValidator.ValidateAsync"/> call. Evaluators must
/// treat everything else as read-only; mutating anything reachable
/// through the context may corrupt concurrent evaluator invocations.
/// </para>
/// <para>
/// <b>Recursion.</b> The <see cref="ShapeValidator"/> delegate is the
/// mechanism by which recursion evaluators (<c>sh:node</c>,
/// <c>sh:property</c>, <c>sh:and</c>, <c>sh:or</c>, <c>sh:not</c>,
/// <c>sh:xone</c>, <c>sh:qualifiedValueShape</c>) delegate inner-shape
/// validation back to the orchestrator. The orchestrator sets this at
/// the start of a run and handles cycle detection internally — when a
/// <c>(shape, focus)</c> pair is already on the in-progress stack and
/// encountered again, the delegate short-circuits to "conforming" and
/// returns no results. This matches the accepted handling of recursive
/// shapes in SHACL 1.2 Core: a shape under evaluation is provisionally
/// treated as conforming to break the cycle.
/// </para>
/// <para>
/// <b>RDF list vocabulary identifiers.</b>
/// <see cref="RdfFirstId"/>, <see cref="RdfRestId"/>, and
/// <see cref="RdfNilId"/> are pre-resolved by the orchestrator at the
/// start of a run. List-walking evaluators
/// (<c>sh:minListLength</c>, <c>sh:maxListLength</c>,
/// <c>sh:uniqueMembers</c>, <c>sh:memberShape</c>) pass them directly
/// to <see cref="Lumoin.Veritas.Rdf.RdfCollection.ToListAsync"/> with
/// no per-invocation dictionary lookup.
/// </para>
/// </remarks>
public sealed record ValidationContext
{
    /// <summary>
    /// Match-op bundle against the data graph being validated. Carries
    /// the three primitive forms — single-pattern match, subject-set
    /// match, and object-set match — that the validator and property-path
    /// evaluator require. Constraint evaluators that only need
    /// single-pattern lookup read <c>DataMatchOps.MatchTriples</c>;
    /// path-evaluation consumers pass the whole bundle to
    /// <see cref="Lumoin.Veritas.Rdf.PropertyPathEvaluator"/>.
    /// </summary>
    public required GraphMatchOps DataMatchOps { get; init; }

    /// <summary>
    /// Match-op bundle against the shapes graph, made available to
    /// SPARQL-based constraints as the named graph their
    /// <c>$shapesGraph</c> variable designates (SHACL-SPARQL §5.2.1).
    /// <c>null</c> when the run supplies no shapes graph — a validation
    /// with no SPARQL constraint never queries it, and a SPARQL
    /// constraint that does will simply not match (under-validate).
    /// </summary>
    public GraphMatchOps? ShapesGraphMatchOps { get; init; }

    /// <summary>
    /// The IRI naming the shapes graph: pre-bound to <c>$shapesGraph</c>
    /// and used as the key of the shapes named graph in the SPARQL
    /// dataset. <c>null</c> when <see cref="ShapesGraphMatchOps"/> is.
    /// </summary>
    public RdfTerm? ShapesGraphIri { get; init; }

    /// <summary>The loaded shape registry.</summary>
    public required ShapeRegistry Shapes { get; init; }

    /// <summary>
    /// The term dictionary shared between the shape graph and the
    /// data graph.
    /// </summary>
    public required TermDictionary Dictionary { get; init; }

    /// <summary>The validator options in effect for this run.</summary>
    public required ShaclValidatorOptions Options { get; init; }

    /// <summary>
    /// The pre-resolved <c>rdf:first</c> predicate identifier.
    /// </summary>
    public required IriId RdfFirstId { get; init; }

    /// <summary>
    /// The pre-resolved <c>rdf:rest</c> predicate identifier.
    /// </summary>
    public required IriId RdfRestId { get; init; }

    /// <summary>
    /// The pre-resolved <c>rdf:nil</c> terminator identifier.
    /// </summary>
    public required IriId RdfNilId { get; init; }

    /// <summary>
    /// Per-run cache for <c>sh:class</c> membership decisions.
    /// </summary>
    public ClassMembershipCache ClassMembershipCache { get; init; } = new();

    /// <summary>
    /// Per-run cache for <c>rdfs:subClassOf*</c> transitive closures
    /// of class identifiers. Shared by class-hierarchy evaluators
    /// (<see cref="Evaluators.ClassEvaluator"/>,
    /// <see cref="Evaluators.RootClassEvaluator"/>) through the
    /// helpers in <see cref="Evaluators.ClassHierarchyHelpers"/>.
    /// </summary>
    public SubclassClosureCache SubclassClosureCache { get; init; } = new();

    /// <summary>
    /// Per-run cache for the SPARQL query engine backing <c>sh:sparql</c>
    /// constraint evaluation. The data graph is indexed into the engine at
    /// most once per run and re-used across every SPARQL constraint and
    /// focus node. The orchestrator wires the cache under the run's
    /// <see cref="ShaclValidatorOptions.SparqlExecution"/> policy with the
    /// run's value-datatype and extension-function registries; the
    /// hand-built default is the explicit off policy over the empty
    /// registries.
    /// </summary>
    public SparqlEngineCache SparqlEngines { get; init; } = new(SparqlEnginePolicy.Default, ValueDatatypeRegistry.Empty, SparqlFunctionRegistry.Empty);

    /// <summary>
    /// Recursive shape-validation delegate. <c>null</c> when the
    /// orchestrator has not wired it — evaluators that depend on it
    /// (the six recursion evaluators) must guard with a null check and
    /// fall back to a <see cref="NotImplementedEvaluator"/>-style
    /// informational result if it is missing. In practice the
    /// orchestrator always wires it; this null-permitting shape exists
    /// to support isolated unit tests of non-recursion evaluators
    /// that build a context by hand.
    /// </summary>
    public ShapeValidatorDelegate? ShapeValidator { get; init; }
}

/// <summary>
/// Recursively validates <paramref name="shape"/> against the data
/// graph at focus node <paramref name="focus"/>, returning the
/// accumulated results. Cycles over <c>(shape, focus)</c> are broken
/// by treating a re-entered pair as conforming, yielding an empty
/// array.
/// </summary>
/// <param name="shape">The inner shape to validate.</param>
/// <param name="focus">The focus node the inner shape is evaluated against.</param>
/// <param name="cancellationToken">Cancellation.</param>
/// <returns>The results produced by the inner validation.</returns>
public delegate ValueTask<ImmutableArray<ValidationResult>> ShapeValidatorDelegate(
    Shape shape,
    TermId focus,
    CancellationToken cancellationToken);
