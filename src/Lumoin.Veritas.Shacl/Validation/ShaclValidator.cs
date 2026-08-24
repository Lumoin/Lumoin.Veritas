using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Shacl.Diagnostics;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Targets;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Shacl.Validation;

/// <summary>
/// Orchestrator that validates a data graph against a loaded
/// <see cref="ShapeRegistry"/>, emitting a <see cref="ValidationReport"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Specification.</b> Implements the validation algorithm defined
/// in SHACL 1.2 Core §3.4
/// (<a href="https://www.w3.org/TR/shacl12-core/#validation-definition">Validation</a>).
/// Every behavioural choice in this orchestrator is pinned by a spec
/// section; references are inline at each decision point.
/// </para>
/// <para>
/// <b>Two surfaces.</b> <see cref="ValidateStreamAsync"/> is the core:
/// it yields each top-level <see cref="ValidationResult"/> lazily as the
/// fold produces it. <see cref="ValidateAsync"/> is a convenience
/// collector over that stream — it materialises the results, computes
/// the <see cref="ValidationReport.Conforms"/> fold, and applies the
/// <see cref="ShaclValidatorOptions.MaxResults"/> cap and
/// <see cref="ShaclValidatorOptions.FailFast"/> short-circuit.
/// </para>
/// <para>
/// <b>The algorithm.</b> Per §3.4 the validator iterates every
/// non-deactivated shape in the registry, expands its targets into
/// focus nodes per §2.1.3
/// (<a href="https://www.w3.org/TR/shacl12-core/#targets">Targets</a>),
/// and validates every <c>(shape, focus)</c> pair. A pair conforms
/// (§3.6,
/// <a href="https://www.w3.org/TR/shacl12-core/#results-conforms">Conformance</a>)
/// when no validation result of any severity is produced for it —
/// <c>sh:conforms</c> is the absence of results, not the absence of
/// violations; severity is advisory metadata on each result.
/// </para>
/// <para>
/// <b>Iteration, not recursion.</b> Shape-referencing constraints
/// (<c>sh:node</c>, <c>sh:property</c>, <c>sh:and</c>, <c>sh:or</c>,
/// <c>sh:not</c>, <c>sh:xone</c>, <c>sh:qualifiedValueShape</c>,
/// <c>sh:memberShape</c>) need the report of an inner <c>(shape, focus)</c>
/// pair to decide their own outcome. The orchestrator computes those
/// inner reports by a two-pass catamorphism over the
/// <c>(shape, focus)</c> dependency graph, driven by an explicit stack
/// — no method-call recursion. A discovery pass enumerates each pair's
/// dependency pairs (which inner pairs its constraints will read); a
/// reduction pass processes them in post-order so a pair's children are
/// always computed before the pair itself, and records each pair's
/// <see cref="ValidationReport"/> in a memo. An evaluator reaches an
/// inner report through <see cref="ValidationContext.ShapeValidator"/>,
/// which is a pure memo lookup rather than a re-entrant call.
/// </para>
/// <para>
/// <b>Recursion semantics over cycles.</b> SHACL 1.2 Core §3.5
/// (<a href="https://www.w3.org/TR/shacl12-core/#shape-recursion">Recursion</a>)
/// permits multiple interpretations of recursive shapes; this
/// orchestrator treats a re-entered <c>(shape, focus)</c> as conforming,
/// which guarantees termination and matches the behaviour the W3C
/// recursive-shape test cases exercise. In the iterative form a cycle is
/// a back-edge to a pair still on the current fold path: the discovery
/// pass does not descend into it, and the memo lookup returns the empty
/// report for it, so the cycle reads as conforming.
/// </para>
/// <para>
/// <b>Tracing.</b> When
/// <see cref="ShaclValidatorOptions.TraceHandler"/> is non-<c>null</c>
/// the orchestrator emits <see cref="ShaclTraceEvent"/>s as it reduces
/// each pair. Trace events from a single run share one correlation id.
/// </para>
/// </remarks>
public static class ShaclValidator
{
    /// <summary>
    /// Runs validation and collects the results into a
    /// <see cref="ValidationReport"/>. A convenience wrapper over
    /// <see cref="ValidateStreamAsync"/> that materialises the stream,
    /// computes the conformance fold, and applies the
    /// <see cref="ShaclValidatorOptions.MaxResults"/> cap and
    /// <see cref="ShaclValidatorOptions.FailFast"/> short-circuit.
    /// </summary>
    /// <param name="shapes">The loaded shape registry.</param>
    /// <param name="dataMatchOps">Match-op bundle over the data graph.</param>
    /// <param name="dictionary">
    /// The term dictionary shared by the shape graph and the data graph.
    /// </param>
    /// <param name="evaluators">The evaluator registry.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on emitted trace events. Pass <see cref="TimeProvider.System"/> in production; tests pinning trace timing pass a <c>FakeTimeProvider</c>.</param>
    /// <param name="shapesGraphMatchOps">Match-op bundle over the shapes graph, exposed to SPARQL constraints as the named graph their <c>$shapesGraph</c> designates; <c>null</c> to omit it (a validation with no SPARQL constraint never queries it).</param>
    /// <param name="shapesGraphIri">The IRI naming the shapes graph (pre-bound to <c>$shapesGraph</c>); <c>null</c> when <paramref name="shapesGraphMatchOps"/> is.</param>
    /// <param name="options">Tuning options; <c>null</c> uses <see cref="ShaclValidatorOptions.Default"/>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>A populated <see cref="ValidationReport"/>.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <c>null</c>.</exception>
    public static async Task<ValidationReport> ValidateAsync(
        ShapeRegistry shapes,
        GraphMatchOps dataMatchOps,
        TermDictionary dictionary,
        ConstraintEvaluatorRegistry evaluators,
        TimeProvider timeProvider,
        GraphMatchOps? shapesGraphMatchOps = null,
        RdfTerm? shapesGraphIri = null,
        ShaclValidatorOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(evaluators);
        ArgumentNullException.ThrowIfNull(timeProvider);

        ShaclValidatorOptions effectiveOptions = options ?? ShaclValidatorOptions.Default;

        List<ValidationResult> collected = [];
        bool conforms = true;

        await foreach(ValidationResult result in ValidateStreamAsync(
            shapes, dataMatchOps, dictionary, evaluators, timeProvider, shapesGraphMatchOps, shapesGraphIri, effectiveOptions, cancellationToken).ConfigureAwait(false))
        {
            //Cap stops collection (and, with it, the conformance scan) once
            //MaxResults is reached — matching the documented "diagnose the
            //first N" contract. Conformance runs use the default (no cap).
            if(effectiveOptions.MaxResults is int cap && collected.Count >= cap)
            {
                break;
            }

            //SHACL 1.2 Core §3.6: the report conforms iff the validation produced no validation results at all —
            //any result, of any severity (Violation, Warning, or Info), makes it non-conforming. Severity is
            //advisory metadata on each result and does not factor into sh:conforms.
            conforms = false;
            collected.Add(result);

            if(effectiveOptions.FailFast)
            {
                break;
            }
        }

        return new ValidationReport
        {
            Conforms = conforms,
            Results = [.. collected],
        };
    }

    /// <summary>
    /// Validates the data graph and yields each top-level
    /// <see cref="ValidationResult"/> lazily, in the order the fold
    /// produces it. This is the composable core; consumers that want a
    /// materialised report with a conformance flag use
    /// <see cref="ValidateAsync"/>.
    /// </summary>
    /// <param name="shapes">The loaded shape registry.</param>
    /// <param name="dataMatchOps">Match-op bundle over the data graph.</param>
    /// <param name="dictionary">The term dictionary shared by the shape graph and the data graph.</param>
    /// <param name="evaluators">The evaluator registry.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on emitted trace events.</param>
    /// <param name="shapesGraphMatchOps">Match-op bundle over the shapes graph, exposed to SPARQL constraints as the named graph their <c>$shapesGraph</c> designates; <c>null</c> to omit it.</param>
    /// <param name="shapesGraphIri">The IRI naming the shapes graph (pre-bound to <c>$shapesGraph</c>); <c>null</c> when <paramref name="shapesGraphMatchOps"/> is.</param>
    /// <param name="options">Tuning options; <c>null</c> uses <see cref="ShaclValidatorOptions.Default"/>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The validation results, streamed.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <c>null</c>.</exception>
    public static async IAsyncEnumerable<ValidationResult> ValidateStreamAsync(
        ShapeRegistry shapes,
        GraphMatchOps dataMatchOps,
        TermDictionary dictionary,
        ConstraintEvaluatorRegistry evaluators,
        TimeProvider timeProvider,
        GraphMatchOps? shapesGraphMatchOps = null,
        RdfTerm? shapesGraphIri = null,
        ShaclValidatorOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(evaluators);
        ArgumentNullException.ThrowIfNull(timeProvider);

        ShaclValidatorOptions effectiveOptions = options ?? ShaclValidatorOptions.Default;

        //Pre-resolve the RDF list vocabulary once per run so list-walking
        //evaluators and the sh:memberShape dependency walk consume the ids
        //directly via the context rather than repeating the dictionary
        //lookup on each invocation.
        IriId rdfFirstId = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.First));
        IriId rdfRestId = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.Rest));
        IriId rdfNilId = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.Nil));

        //All shared per-run state lives on RunState. The ShapeValidator
        //delegate installed on ValidationContext is method-group-converted
        //from RunState.LookupAsync, so its closure captures only the
        //runState local — never a parameter of this method, satisfying the
        //no-closure-over-parameters convention.
        RunState runState = new()
        {
            Shapes = shapes,
            DataMatchOps = dataMatchOps,
            Dictionary = dictionary,
            Evaluators = evaluators,
            Options = effectiveOptions,
            TraceHandler = effectiveOptions.TraceHandler,
            TimeProvider = timeProvider,
            CorrelationId = (effectiveOptions.Identifiers ?? VeritasIdentifiers.System)(new IdentifierRequest(IdentifierPurpose.Correlation, default)),
            RdfFirstId = rdfFirstId,
            RdfRestId = rdfRestId,
            RdfNilId = rdfNilId,
        };

        runState.Context = new ValidationContext
        {
            DataMatchOps = dataMatchOps,
            ShapesGraphMatchOps = shapesGraphMatchOps,
            ShapesGraphIri = shapesGraphIri,
            Shapes = shapes,
            Dictionary = dictionary,
            Options = effectiveOptions,
            RdfFirstId = rdfFirstId,
            RdfRestId = rdfRestId,
            RdfNilId = rdfNilId,
            ShapeValidator = runState.LookupAsync,
            SparqlEngines = new SparqlEngineCache(effectiveOptions.SparqlExecution, effectiveOptions.ValueDatatypes, effectiveOptions.ExtensionFunctions),
        };

        foreach(Shape shape in shapes.AllShapes)
        {
            if(shape.Deactivated)
            {
                continue;
            }

            //Expand targets into the focus-node set. A shape whose targets
            //match nothing is not validated — §3.4 iterates over focus
            //nodes, and an empty iteration produces no results. This is the
            //empty-focus-set case; it is distinct from the empty-VALUE-set
            //case, which ReduceAsync handles by dispatching constraints
            //regardless of value-node count (cardinality depends on it).
            HashSet<TermId> focusNodes = [];
            foreach(Target target in shape.Targets)
            {
                await foreach(TermId focus in target.ExpandAsync(dataMatchOps.MatchTriples, cancellationToken).ConfigureAwait(false))
                {
                    focusNodes.Add(focus);
                }
            }

            foreach(TermId focus in focusNodes)
            {
                ValidationReport report = await runState.EvaluateAsync((shape.Id, focus), shape, cancellationToken).ConfigureAwait(false);
                foreach(ValidationResult result in report.Results)
                {
                    yield return result;
                }
            }
        }
    }

    /// <summary>
    /// Computes the value nodes for a focus per SHACL 1.2 Core §2.4: a
    /// node shape's value-node set is the focus itself; a property shape's
    /// is the image of the focus under its path (§2.3). The path evaluator
    /// dedupes internally, so the result is the distinct value-node set.
    /// </summary>
    /// <param name="focus">The focus node whose value nodes are computed.</param>
    /// <param name="path">The property path to evaluate, or <c>null</c> for a node shape.</param>
    /// <param name="dataMatchOps">Match-op bundle over the data graph.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The distinct value-node set.</returns>
    private static async Task<ImmutableArray<TermId>> ComputeValueNodesAsync(
        TermId focus,
        PropertyPath? path,
        GraphMatchOps dataMatchOps,
        CancellationToken cancellationToken)
    {
        if(path is null)
        {
            return [focus];
        }

        List<TermId> values = [];
        await foreach(TermId value in PropertyPathEvaluator.EvaluateAsync(
            focus, path, dataMatchOps, cancellationToken).ConfigureAwait(false))
        {
            values.Add(value);
        }

        return [.. values];
    }

    /// <summary>
    /// Per-run mutable state for one <see cref="ValidateStreamAsync"/> call:
    /// the memo of reduced reports, the in-progress fold path, and the
    /// immutable collaborators the fold needs.
    /// </summary>
    /// <remarks>
    /// The <see cref="LookupAsync"/> method-group is installed on
    /// <see cref="ValidationContext.ShapeValidator"/>; because
    /// <see cref="RunState"/> is a local allocated inside
    /// <see cref="ValidateStreamAsync"/>, the delegate captures only a local
    /// reference — never a parameter — satisfying the
    /// no-closure-over-parameters convention.
    /// </remarks>
    private sealed class RunState
    {
        /// <summary>
        /// Memo of every reduced <c>(shape, focus)</c> pair's report, shared
        /// across top-level focuses so a pair reached from several seeds is
        /// reduced once. A pair absent during reduction is either a back-edge
        /// (present in <see cref="inProgress"/>) or a discovery defect.
        /// </summary>
        private readonly Dictionary<(TermId ShapeId, TermId Focus), ValidationReport> memo = new();

        /// <summary>
        /// The <c>(shape, focus)</c> pairs on the current fold path. A pair is
        /// added when its frame is pushed and removed when the frame reduces,
        /// so the set is exactly the ancestors of the frame on top of the
        /// stack — which is what makes "in <see cref="inProgress"/>"
        /// equivalent to "back-edge".
        /// </summary>
        private readonly HashSet<(TermId ShapeId, TermId Focus)> inProgress = [];

        /// <summary>The loaded shape registry being validated against.</summary>
        public required ShapeRegistry Shapes { get; init; }

        /// <summary>Match-op bundle over the data graph being validated.</summary>
        public required GraphMatchOps DataMatchOps { get; init; }

        /// <summary>
        /// The term dictionary shared by the shape graph and the data graph.
        /// </summary>
        public required TermDictionary Dictionary { get; init; }

        /// <summary>
        /// The pluggable registry of constraint-component evaluators, keyed
        /// by component IRI.
        /// </summary>
        public required ConstraintEvaluatorRegistry Evaluators { get; init; }

        /// <summary>The validator options in effect for this run.</summary>
        public required ShaclValidatorOptions Options { get; init; }

        /// <summary>
        /// Caller-supplied trace handler, or <c>null</c> when the run is
        /// untraced. Every emission site checks this for <c>null</c> before
        /// constructing the event payload.
        /// </summary>
        public required TraceHandler<ShaclTraceEvent>? TraceHandler { get; init; }

        /// <summary>
        /// Clock used to stamp <see cref="ShaclTraceEvent"/> timestamps. Pass
        /// <see cref="System.TimeProvider.System"/> in production; tests
        /// pinning trace timing pass a <c>FakeTimeProvider</c>.
        /// </summary>
        public required TimeProvider TimeProvider { get; init; }

        /// <summary>
        /// Correlation id stamped on every <see cref="ShaclTraceEvent"/>
        /// emitted by this run; consumers join a run's events by this value.
        /// </summary>
        public required Guid CorrelationId { get; init; }

        /// <summary>
        /// The pre-resolved <c>rdf:first</c> identifier, consumed by the
        /// <c>sh:memberShape</c> dependency walk.
        /// </summary>
        public required IriId RdfFirstId { get; init; }

        /// <summary>
        /// The pre-resolved <c>rdf:rest</c> identifier, consumed by the
        /// <c>sh:memberShape</c> dependency walk.
        /// </summary>
        public required IriId RdfRestId { get; init; }

        /// <summary>
        /// The pre-resolved <c>rdf:nil</c> terminator identifier, consumed by
        /// the <c>sh:memberShape</c> dependency walk.
        /// </summary>
        public required IriId RdfNilId { get; init; }

        /// <summary>
        /// Monotonically-increasing sequence stamped on each emitted
        /// <see cref="ShaclTraceEvent"/>; post-incremented by
        /// <see cref="NextSequence"/>.
        /// </summary>
        public long SequenceCounter { get; set; }

        /// <summary>
        /// The composed context handed to evaluators. Set after construction
        /// because its <see cref="ValidationContext.ShapeValidator"/> is the
        /// method-group-converted <see cref="LookupAsync"/>, which can only be
        /// referenced once this instance exists. Non-null throughout
        /// validation.
        /// </summary>
        public ValidationContext Context { get; set; } = null!;

        /// <summary>
        /// Post-increments <see cref="SequenceCounter"/> and returns the prior
        /// value, producing the monotonically-increasing sequence number
        /// stamped on every emitted trace event.
        /// </summary>
        /// <returns>The pre-increment sequence value.</returns>
        public long NextSequence() => SequenceCounter++;

        /// <summary>
        /// The <see cref="ValidationContext.ShapeValidator"/> delegate: a pure
        /// memo lookup returning the inner pair's results, never a re-entrant
        /// call.
        /// </summary>
        /// <remarks>
        /// Post-order guarantees an inner pair is reduced before any pair that
        /// depends on it, so a hit is the norm. A miss on an in-progress
        /// ancestor is a cycle, reported as conforming (empty). Any other miss
        /// means discovery failed to enumerate the dependency; that is
        /// surfaced loudly rather than silently read as conforming.
        /// </remarks>
        /// <param name="shape">The inner shape whose report is requested.</param>
        /// <param name="focus">The focus node the inner shape was evaluated against.</param>
        /// <param name="cancellationToken">Cancellation.</param>
        /// <returns>The inner pair's results, or empty for a cycle back-edge.</returns>
        /// <exception cref="InvalidOperationException">The pair was neither reduced nor an in-progress ancestor — a discovery defect.</exception>
        public ValueTask<ImmutableArray<ValidationResult>> LookupAsync(
            Shape shape,
            TermId focus,
            CancellationToken cancellationToken)
        {
            (TermId ShapeId, TermId Focus) key = (shape.Id, focus);

            if(memo.TryGetValue(key, out ValidationReport? report))
            {
                return ValueTask.FromResult(report.Results);
            }

            if(inProgress.Contains(key))
            {
                return ValueTask.FromResult(ImmutableArray<ValidationResult>.Empty);
            }

            throw new InvalidOperationException(
                $"SHACL validation requested an inner report for shape {shape.Id} at focus {focus} that dependency discovery did not enumerate.");
        }

        /// <summary>
        /// Folds the <c>(shape, focus)</c> dependency subgraph rooted at
        /// <paramref name="seed"/> and returns its report. Iterative
        /// post-order driven by an explicit stack: a frame reduces only once
        /// every dependency it declared is in the memo (or is a back-edge or
        /// unresolved reference, both of which are skipped).
        /// </summary>
        /// <param name="seed">The pair to evaluate.</param>
        /// <param name="seedShape">The shape resolved for <paramref name="seed"/>'s shape id.</param>
        /// <param name="cancellationToken">Cancellation.</param>
        /// <returns>The report for <paramref name="seed"/>.</returns>
        public async ValueTask<ValidationReport> EvaluateAsync(
            (TermId ShapeId, TermId Focus) seed,
            Shape seedShape,
            CancellationToken cancellationToken)
        {
            if(memo.TryGetValue(seed, out ValidationReport? cached))
            {
                return cached;
            }

            Stack<FoldFrame> stack = new();
            stack.Push(await BuildFrameAsync(seed, seedShape, cancellationToken).ConfigureAwait(false));
            inProgress.Add(seed);

            while(stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FoldFrame top = stack.Peek();

                if(top.NextIndex >= top.Dependencies.Count)
                {
                    ValidationReport report = await ReduceAsync(top, cancellationToken).ConfigureAwait(false);
                    memo[top.Node] = report;
                    inProgress.Remove(top.Node);
                    stack.Pop();
                    continue;
                }

                (TermId ShapeId, TermId Focus) dependency = top.Dependencies[top.NextIndex];
                top.NextIndex++;

                //Already reduced, or a back-edge to an ancestor still on the
                //path: nothing to descend into. The back-edge reads as the
                //empty (conforming) report through LookupAsync at reduction.
                if(memo.ContainsKey(dependency) || inProgress.Contains(dependency))
                {
                    continue;
                }

                //Unresolved reference: the evaluator that named it short-
                //circuits to an informational result without consulting the
                //memo, so there is nothing to reduce here.
                if(!Shapes.TryGetShape(dependency.ShapeId, out Shape? dependencyShape))
                {
                    continue;
                }

                inProgress.Add(dependency);
                stack.Push(await BuildFrameAsync(dependency, dependencyShape, cancellationToken).ConfigureAwait(false));
            }

            return memo[seed];
        }

        /// <summary>
        /// Builds a fold frame: computes the pair's value nodes once (reused
        /// at reduction) and enumerates the inner pairs its constraints depend
        /// on.
        /// </summary>
        /// <param name="node">The <c>(shape, focus)</c> pair the frame folds.</param>
        /// <param name="shape">The shape resolved for <paramref name="node"/>'s shape id.</param>
        /// <param name="cancellationToken">Cancellation.</param>
        /// <returns>The constructed frame.</returns>
        private async ValueTask<FoldFrame> BuildFrameAsync(
            (TermId ShapeId, TermId Focus) node,
            Shape shape,
            CancellationToken cancellationToken)
        {
            PropertyPath? path = shape is PropertyShape propertyShape ? propertyShape.Path : null;
            ImmutableArray<TermId> valueNodes = await ComputeValueNodesAsync(node.Focus, path, DataMatchOps, cancellationToken).ConfigureAwait(false);
            List<(TermId ShapeId, TermId Focus)> dependencies = await DiscoverDependenciesAsync(shape, node.Focus, valueNodes, cancellationToken).ConfigureAwait(false);

            return new FoldFrame(node, shape, path, valueNodes, dependencies);
        }

        /// <summary>
        /// Discovery pass: enumerates the inner <c>(shape, node)</c> pairs each
        /// shape-referencing constraint will read from the memo during
        /// reduction.
        /// </summary>
        /// <remarks>
        /// The node a referenced shape is validated against is the
        /// constraint's policy: at the focus for <c>sh:property</c>, at each
        /// value node for the node, logical, and qualified constraints, and at
        /// each list member for <c>sh:memberShape</c>. The pairs enumerated
        /// here must match the pairs the corresponding evaluator looks up;
        /// <see cref="LookupAsync"/> throws on any mismatch so a divergence
        /// cannot pass silently.
        /// </remarks>
        /// <param name="shape">The shape whose constraints are scanned.</param>
        /// <param name="focus">The focus node under evaluation.</param>
        /// <param name="valueNodes">The pair's value nodes.</param>
        /// <param name="cancellationToken">Cancellation.</param>
        /// <returns>The dependency pairs, in constraint order.</returns>
        private async ValueTask<List<(TermId ShapeId, TermId Focus)>> DiscoverDependenciesAsync(
            Shape shape,
            TermId focus,
            ImmutableArray<TermId> valueNodes,
            CancellationToken cancellationToken)
        {
            List<(TermId ShapeId, TermId Focus)> dependencies = [];

            foreach(ConstraintComponent constraint in shape.Constraints)
            {
                switch(constraint)
                {
                    case PropertyConstraint property:
                    {
                        //sh:property validates each value node, as a focus node,
                        //against the nested property shape (SHACL §4.8.1) — the
                        //same per-value-node descent as sh:node. On a node shape
                        //the value-node set is the focus itself, so this is one
                        //dependency at the focus; on a property shape it is one
                        //per path-reached node. Mirrors PropertyEvaluator.
                        AddAtValueNodes(dependencies, property.PropertyShapeId, valueNodes);
                        break;
                    }

                    case NodeConstraint node:
                    {
                        AddAtValueNodes(dependencies, node.NodeShapeId, valueNodes);
                        break;
                    }

                    case NotConstraint not:
                    {
                        AddAtValueNodes(dependencies, not.InnerShapeId, valueNodes);
                        break;
                    }

                    case AndConstraint and:
                    {
                        AddMembersAtValueNodes(dependencies, and.MemberShapeIds, valueNodes);
                        break;
                    }

                    case OrConstraint or:
                    {
                        AddMembersAtValueNodes(dependencies, or.MemberShapeIds, valueNodes);
                        break;
                    }

                    case XoneConstraint xone:
                    {
                        AddMembersAtValueNodes(dependencies, xone.MemberShapeIds, valueNodes);
                        break;
                    }

                    case QualifiedMinCountConstraint qualifiedMin:
                    {
                        AddQualifiedDependencies(dependencies, shape, qualifiedMin.ValueShapeId, qualifiedMin.Disjoint, valueNodes);
                        break;
                    }

                    case QualifiedMaxCountConstraint qualifiedMax:
                    {
                        AddQualifiedDependencies(dependencies, shape, qualifiedMax.ValueShapeId, qualifiedMax.Disjoint, valueNodes);
                        break;
                    }

                    case MemberShapeConstraint member:
                    {
                        await AddMemberShapeDependenciesAsync(dependencies, member.MemberShapeId, valueNodes, cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    default:
                    {
                        //Leaf constraint: no inner shapes to read.
                        break;
                    }
                }
            }

            return dependencies;
        }

        /// <summary>
        /// Adds a dependency on <paramref name="shapeId"/> at every value node
        /// — the node policy shared by the node, negation, and qualified
        /// constraints.
        /// </summary>
        /// <param name="dependencies">The dependency list being built.</param>
        /// <param name="shapeId">The referenced shape's id.</param>
        /// <param name="valueNodes">The value nodes to validate it against.</param>
        private static void AddAtValueNodes(
            List<(TermId ShapeId, TermId Focus)> dependencies,
            TermId shapeId,
            ImmutableArray<TermId> valueNodes)
        {
            foreach(TermId value in valueNodes)
            {
                dependencies.Add((shapeId, value));
            }
        }

        /// <summary>
        /// Adds a dependency on each member shape at every value node — the
        /// node policy shared by the <c>sh:and</c>, <c>sh:or</c>, and
        /// <c>sh:xone</c> constraints.
        /// </summary>
        /// <param name="dependencies">The dependency list being built.</param>
        /// <param name="memberShapeIds">The member shape ids.</param>
        /// <param name="valueNodes">The value nodes to validate them against.</param>
        private static void AddMembersAtValueNodes(
            List<(TermId ShapeId, TermId Focus)> dependencies,
            ImmutableArray<TermId> memberShapeIds,
            ImmutableArray<TermId> valueNodes)
        {
            foreach(TermId memberShapeId in memberShapeIds)
            {
                AddAtValueNodes(dependencies, memberShapeId, valueNodes);
            }
        }

        /// <summary>
        /// Adds the dependencies of a qualified-cardinality constraint: the
        /// inner shape at each value node, plus — when disjoint — each sibling
        /// qualified value shape at each value node.
        /// </summary>
        /// <remarks>
        /// The sibling set comes from
        /// <see cref="QualifiedValueShapeCounting.CollectSiblingValueShapes"/>,
        /// the same helper the counting code consumes, so discovery and
        /// counting cannot disagree about which siblings exist.
        /// </remarks>
        /// <param name="dependencies">The dependency list being built.</param>
        /// <param name="shape">The shape the constraint is declared on.</param>
        /// <param name="valueShapeId">The qualified value shape's id.</param>
        /// <param name="disjoint">Whether sibling-disjoint subtraction applies.</param>
        /// <param name="valueNodes">The value nodes to validate against.</param>
        private void AddQualifiedDependencies(
            List<(TermId ShapeId, TermId Focus)> dependencies,
            Shape shape,
            TermId valueShapeId,
            bool disjoint,
            ImmutableArray<TermId> valueNodes)
        {
            AddAtValueNodes(dependencies, valueShapeId, valueNodes);

            if(!disjoint)
            {
                return;
            }

            foreach(Shape sibling in QualifiedValueShapeCounting.CollectSiblingValueShapes(shape, valueShapeId, Context))
            {
                AddAtValueNodes(dependencies, sibling.Id, valueNodes);
            }
        }

        /// <summary>
        /// Adds a dependency on the member shape at each member of every value
        /// node that is a SHACL list. The list walk mirrors the evaluator's;
        /// non-list value nodes contribute no dependencies.
        /// </summary>
        /// <param name="dependencies">The dependency list being built.</param>
        /// <param name="memberShapeId">The shape each list member must conform to.</param>
        /// <param name="valueNodes">The value nodes whose list members are walked.</param>
        /// <param name="cancellationToken">Cancellation.</param>
        /// <returns>A task that completes when the dependencies are added.</returns>
        private async ValueTask AddMemberShapeDependenciesAsync(
            List<(TermId ShapeId, TermId Focus)> dependencies,
            TermId memberShapeId,
            ImmutableArray<TermId> valueNodes,
            CancellationToken cancellationToken)
        {
            foreach(TermId value in valueNodes)
            {
                RdfCollectionRead? read = await RdfCollection.TryReadAsync(
                    value,
                    RdfFirstId,
                    RdfRestId,
                    RdfNilId,
                    DataMatchOps.MatchTriples,
                    cancellationToken).ConfigureAwait(false);

                if(read is null)
                {
                    continue;
                }

                foreach(TermId member in read.Value.Members)
                {
                    dependencies.Add((memberShapeId, member));
                }
            }
        }

        /// <summary>
        /// Reduction pass for one <c>(shape, focus)</c> pair: dispatches every
        /// constraint, emits trace events, and folds the results into the
        /// pair's report.
        /// </summary>
        /// <remarks>
        /// Per §3.4 constraints are dispatched regardless of value-node count,
        /// so cardinality constraints (<c>sh:minCount</c> §4.5.1,
        /// <c>sh:qualifiedMinCount</c> §4.7.4) report violations precisely on
        /// the zero-value case. Shape-referencing evaluators read their inner
        /// reports through the memo-lookup
        /// <see cref="ValidationContext.ShapeValidator"/>; by post-order those
        /// reports are present.
        /// </remarks>
        /// <param name="frame">The frame whose pair is reduced.</param>
        /// <param name="cancellationToken">Cancellation.</param>
        /// <returns>The pair's report.</returns>
        private async ValueTask<ValidationReport> ReduceAsync(FoldFrame frame, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Shape shape = frame.Shape;
            TermId focus = frame.Node.Focus;
            PropertyPath? path = frame.Path;
            ImmutableArray<TermId> valueNodes = frame.ValueNodes;
            TraceHandler<ShaclTraceEvent>? traceHandler = TraceHandler;

            if(traceHandler is not null)
            {
                ShaclTraceEvent evt = ShaclTraceEvent.FocusNodeSelected(
                    NextSequence(),
                    TimeProvider.GetUtcNow().UtcTicks,
                    CorrelationId,
                    focus,
                    shape.Id);
                traceHandler(in evt);
            }

            ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

            foreach(ConstraintComponent constraint in shape.Constraints)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Utf8String componentIri = constraint.ConstraintComponentIri;

                //A SPARQL-based constraint component (SHACL-SPARQL §6) has a user-defined component IRI, so it is
                //dispatched by constraint type rather than by an IRI registered in the evaluator registry.
                bool isSparqlComponent = constraint is Constraints.SparqlComponentConstraint;
                bool isRegistered = isSparqlComponent || Evaluators.IsRegistered(componentIri);

                if(traceHandler is not null)
                {
                    string componentIriString = componentIri.ToString();
                    ShaclTraceEvent evt = isRegistered
                        ? ShaclTraceEvent.ConstraintStarted(
                            NextSequence(),
                            TimeProvider.GetUtcNow().UtcTicks,
                            CorrelationId,
                            focus,
                            shape.Id,
                            componentIriString)
                        : ShaclTraceEvent.ConstraintNotImplemented(
                            NextSequence(),
                            TimeProvider.GetUtcNow().UtcTicks,
                            CorrelationId,
                            focus,
                            shape.Id,
                            componentIriString);
                    traceHandler(in evt);
                }

                ConstraintEvaluator evaluator = isSparqlComponent
                    ? SparqlComponentConstraintEvaluator.EvaluateAsync
                    : Evaluators.GetOrDefault(componentIri);
                ImmutableArray<ValidationResult> results = await evaluator(
                    shape,
                    constraint,
                    focus,
                    valueNodes,
                    path,
                    Context,
                    cancellationToken).ConfigureAwait(false);

                foreach(ValidationResult result in results)
                {
                    if(traceHandler is not null)
                    {
                        ShaclTraceEvent evt = ShaclTraceEvent.ResultProduced(
                            NextSequence(),
                            TimeProvider.GetUtcNow().UtcTicks,
                            CorrelationId,
                            focus,
                            shape.Id,
                            componentIri.ToString(),
                            result.ValueNode ?? TermId.None,
                            result.Severity);
                        traceHandler(in evt);
                    }

                    builder.Add(result);
                }

                if(traceHandler is not null)
                {
                    ConstraintEvaluationStatus status = results.IsEmpty
                        ? ConstraintEvaluationStatus.Passed
                        : ConstraintEvaluationStatus.Failed;
                    ShaclTraceEvent evt = ShaclTraceEvent.ConstraintCompleted(
                        NextSequence(),
                        TimeProvider.GetUtcNow().UtcTicks,
                        CorrelationId,
                        focus,
                        shape.Id,
                        componentIri.ToString(),
                        status);
                    traceHandler(in evt);
                }
            }

            //§3.6: the pair conforms iff it produced no results (any severity), not only on violations.
            return new ValidationReport
            {
                Conforms = builder.Count == 0,
                Results = builder.ToImmutable(),
            };
        }

        /// <summary>
        /// One node of the <c>(shape, focus)</c> fold, holding the pair, its
        /// resolved shape and path, its value nodes, and the dependency pairs
        /// the driver descends into.
        /// </summary>
        /// <remarks>
        /// <see cref="NextIndex"/> advances through <see cref="Dependencies"/>
        /// as the driver descends; when it reaches the end every dependency is
        /// resolved and the frame reduces.
        /// </remarks>
        /// <param name="node">The <c>(shape, focus)</c> pair this frame folds.</param>
        /// <param name="shape">The shape resolved for <paramref name="node"/>'s shape id.</param>
        /// <param name="path">The path of <paramref name="shape"/> when it is a property shape; <c>null</c> otherwise.</param>
        /// <param name="valueNodes">The pair's value nodes, computed once and reused at reduction.</param>
        /// <param name="dependencies">The inner pairs this pair's constraints will read.</param>
        private sealed class FoldFrame(
            (TermId ShapeId, TermId Focus) node,
            Shape shape,
            PropertyPath? path,
            ImmutableArray<TermId> valueNodes,
            List<(TermId ShapeId, TermId Focus)> dependencies)
        {
            /// <summary>The <c>(shape, focus)</c> pair this frame folds.</summary>
            public (TermId ShapeId, TermId Focus) Node { get; } = node;

            /// <summary>The shape resolved for <see cref="Node"/>'s shape id.</summary>
            public Shape Shape { get; } = shape;

            /// <summary>
            /// The path of <see cref="Shape"/> when it is a property shape;
            /// <c>null</c> for a node shape.
            /// </summary>
            public PropertyPath? Path { get; } = path;

            /// <summary>
            /// The pair's value nodes, computed once at discovery and reused at
            /// reduction.
            /// </summary>
            public ImmutableArray<TermId> ValueNodes { get; } = valueNodes;

            /// <summary>
            /// The inner <c>(shape, focus)</c> pairs this pair's constraints
            /// will read during reduction.
            /// </summary>
            public List<(TermId ShapeId, TermId Focus)> Dependencies { get; } = dependencies;

            /// <summary>
            /// Cursor into <see cref="Dependencies"/> as the driver descends;
            /// reduction runs when it reaches the end.
            /// </summary>
            public int NextIndex { get; set; }
        }
    }
}
