using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using AstTriplePattern = Lumoin.Veritas.Sparql.Ast.TriplePattern;
using EncodedTriplePattern = Lumoin.Veritas.Core.Hypertrie.Query.TriplePattern;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Evaluates a translated SPARQL algebra tree (the output of <see cref="Translation.SparqlTranslator"/>) against a
/// hypertrie-backed data graph, producing decoded <see cref="SparqlSolution"/>s. This is the Milestone-C executor:
/// it bridges the SPARQL term world (<see cref="SparqlVariable"/> / <see cref="RdfTerm"/>) to the backend's encoded
/// world (<see cref="Variable"/> / <see cref="TermId"/>) and drives the worst-case-optimal join engine in
/// <see cref="HypertrieGraphStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Slice scope.</b> The executor covers basic graph patterns (a <see cref="Bgp"/>, the translator already merges
/// consecutive plain triples into one), the expression-free relational operators (<see cref="Join"/>,
/// <see cref="Union"/>, <see cref="Minus"/>), sub-<c>SELECT</c> (<see cref="ToMultiSet"/> / <see cref="ToList"/>),
/// the expression-gated operators (<see cref="Filter"/>, <c>BIND</c>/<see cref="Extend"/>, <c>OPTIONAL</c>'s
/// <see cref="LeftJoin"/> — via <see cref="SparqlExpressionEvaluator"/>), aggregation (<see cref="Group"/> +
/// <see cref="AggregateJoin"/>), the inline <c>VALUES</c> <see cref="Table"/>, and the projection/sequence
/// modifiers (<see cref="Project"/>, <see cref="Distinct"/>, <see cref="Reduced"/>, <see cref="OrderBy"/>,
/// <see cref="Slice"/>, and the <see cref="UnitTable"/> identity), and property-path <see cref="Path"/> leaves
/// (via <see cref="PropertyPathEvaluator"/>; negated property sets excepted). Named graphs (<c>GRAPH</c>),
/// <c>SERVICE</c>, and <c>EXISTS</c>/<c>NOT EXISTS</c> raise <see cref="NotSupportedException"/> until their later
/// slice lands. The public surface (entry naming, result
/// model, streaming) is provisional and follows codebase convention; the W3C SPARQL 1.2 specification governs the
/// evaluation semantics.
/// </para>
/// <para>
/// <b>No recursion.</b> The algebra is evaluated over an explicit two-phase (expand/combine) work stack, mirroring
/// the translator's iterative discipline, so a deep algebra tree cannot overflow the stack. Results are
/// materialized per operator; streaming evaluation is a later concern.
/// </para>
/// <para>SPARQL 1.2 §18.5 / §18.6 [SPARQL Algebra evaluation].</para>
/// </remarks>
public sealed class SparqlQueryEngine
{
    /// <summary>A solution sequence holding the single empty solution — the result of the <see cref="UnitTable"/> identity.</summary>
    private static IReadOnlyList<SparqlSolution> SingleEmptySolution { get; } = [new SparqlSolution([])];

    private readonly HypertrieGraphStore store;

    private readonly TermDictionary dictionary;

    private readonly SparqlExpressionContext expressionContext;

    /// <summary>Constructs an engine over an already-built store and the term dictionary that encoded it.</summary>
    /// <param name="store">The hypertrie store holding the data graph.</param>
    /// <param name="dictionary">The term dictionary that encoded <paramref name="store"/>; query constants are resolved against it.</param>
    /// <param name="expressionContext">The seams (randomness, digests, the query timestamp) the non-pure expression functions consume; <see langword="null"/> uses <see cref="SparqlExpressionContext.CreateDefault"/>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SparqlQueryEngine(HypertrieGraphStore store, TermDictionary dictionary, SparqlExpressionContext? expressionContext = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dictionary);

        this.store = store;
        this.dictionary = dictionary;
        this.expressionContext = expressionContext ?? SparqlExpressionContext.CreateDefault();
    }

    /// <summary>
    /// Builds an engine over a data graph given as in-memory triples: the terms are encoded into a fresh term
    /// dictionary and indexed into a new hypertrie store.
    /// </summary>
    /// <param name="triples">The data-graph triples.</param>
    /// <param name="expressionContext">The seams (randomness, digests, the query timestamp) the non-pure expression functions consume; <see langword="null"/> uses <see cref="SparqlExpressionContext.CreateDefault"/>.</param>
    /// <param name="cancellationToken">A token that aborts the build.</param>
    /// <returns>An engine ready to evaluate queries against the graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> is <see langword="null"/>.</exception>
    public static async ValueTask<SparqlQueryEngine> BuildAsync(IEnumerable<DataTriple> triples, SparqlExpressionContext? expressionContext = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triples);

        TermDictionary dictionary = new();
        List<EncodedTriple> encoded = [];
        foreach(DataTriple triple in triples)
        {
            uint subject = dictionary.GetOrAdd(triple.Subject).Encoded;
            uint predicate = dictionary.GetOrAdd(triple.Predicate).Encoded;
            uint @object = dictionary.GetOrAdd(triple.Object).Encoded;
            encoded.Add(EncodedTriple.FromEncoded(subject, predicate, @object));
        }

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(encoded, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);

        return new SparqlQueryEngine(store, dictionary, expressionContext);
    }

    /// <summary>
    /// Evaluates an algebra tree to its solution sequence. A <c>SELECT</c> consumes the sequence directly; an
    /// <c>ASK</c> is satisfied when the sequence is non-empty.
    /// </summary>
    /// <param name="algebra">The translated algebra (from <see cref="Translation.SparqlTranslator.Translate"/>).</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The materialized solution sequence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="algebra"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The algebra uses an operator this slice does not yet execute.</exception>
    public async ValueTask<IReadOnlyList<SparqlSolution>> EvaluateAsync(AlgebraOperator algebra, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(algebra);

        GuardSupported(algebra);

        //Two-phase expand/combine over an explicit stack (no recursion): each operator's children are evaluated
        //and recorded before the operator combines them. Results are keyed by reference — a parsed algebra is a
        //tree, so each operator instance appears exactly once as a key.
        Dictionary<AlgebraOperator, IReadOnlyList<SparqlSolution>> results = new(ReferenceEqualityComparer.Instance);
        Stack<(AlgebraOperator Node, bool Combine)> work = new();
        work.Push((algebra, Combine: false));

        while(work.Count > 0)
        {
            (AlgebraOperator node, bool combine) = work.Pop();
            if(combine)
            {
                results[node] = Combine(node, results, expressionContext);

                continue;
            }

            IReadOnlyList<AlgebraOperator> children = node.Children;
            if(children.Count == 0)
            {
                results[node] = await EvaluateLeafAsync(node, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                work.Push((node, Combine: true));
                for(int i = children.Count - 1; i >= 0; i--)
                {
                    work.Push((children[i], Combine: false));
                }
            }
        }

        return results[algebra];
    }

    /// <summary>Evaluates a leaf operator (one with no algebra children): a <see cref="Bgp"/> against the store, or the <see cref="UnitTable"/> identity.</summary>
    /// <param name="op">The leaf operator.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The leaf's solution sequence.</returns>
    private async ValueTask<IReadOnlyList<SparqlSolution>> EvaluateLeafAsync(AlgebraOperator op, CancellationToken cancellationToken)
    {
        if(op is Bgp bgp)
        {
            return await EvaluateBgpAsync(bgp, cancellationToken).ConfigureAwait(false);
        }

        if(op is UnitTable)
        {
            return SingleEmptySolution;
        }

        if(op is Table table)
        {
            return BuildTableSolutions(table.Data);
        }

        if(op is Path pathOperator)
        {
            return await EvaluatePathAsync(pathOperator, cancellationToken).ConfigureAwait(false);
        }

        //The supported-operator guard runs first, so any other leaf reaching here is an executor bug, not input.
        throw new InvalidOperationException($"Leaf operator '{op.GetType().Name}' has no evaluation rule in the executor.");
    }

    /// <summary>Combines an operator's already-evaluated child sequences (read from <paramref name="results"/>) into its own sequence.</summary>
    /// <param name="op">The operator to combine.</param>
    /// <param name="results">The map of already-evaluated operators to their solution sequences.</param>
    /// <param name="context">The seams the expression-gated operators' non-pure functions consume.</param>
    /// <returns>The operator's solution sequence.</returns>
    private static IReadOnlyList<SparqlSolution> Combine(AlgebraOperator op, Dictionary<AlgebraOperator, IReadOnlyList<SparqlSolution>> results, SparqlExpressionContext context)
    {
        return op switch
        {
            Project project => ApplyProject(project, results[project.Input]),
            Distinct distinct => ApplyDistinct(results[distinct.Input]),

            //REDUCED permits but does not require duplicate elimination; passing the sequence through is conformant.
            Reduced reduced => results[reduced.Input],
            Slice slice => ApplySlice(slice, results[slice.Input]),
            Join join => JoinSolutions(results[join.Left], results[join.Right]),
            Union union => UnionSolutions(results[union.Left], results[union.Right]),
            Minus minus => MinusSolutions(results[minus.Left], results[minus.Right]),
            Filter filter => ApplyFilter(filter, results[filter.Input], context),
            Extend extend => ApplyExtend(extend, results[extend.Input], context),
            LeftJoin leftJoin => LeftJoinSolutions(results[leftJoin.Left], results[leftJoin.Right], leftJoin.Condition, context),
            OrderBy orderBy => ApplyOrderBy(orderBy, results[orderBy.Input], context),

            //ToMultiSet / ToList are multiset / sequence conversions; with no ORDER BY in scope they pass through.
            ToMultiSet toMultiSet => results[toMultiSet.Input],
            ToList toList => results[toList.Input],

            //Group leaves the multiset unchanged here; the partitioning into groups happens in the AggregateJoin
            //that wraps it (which reads the Group's keys directly).
            Group group => results[group.Input],
            AggregateJoin aggregateJoin => EvaluateAggregateJoin(aggregateJoin, results[aggregateJoin.Input], context),
            _ => throw new InvalidOperationException($"Operator '{op.GetType().Name}' has children but no combine rule in the executor.")
        };
    }

    /// <summary>Evaluates a basic graph pattern: encodes it to the backend, runs the worst-case-optimal join engine, and decodes each solution.</summary>
    /// <param name="bgp">The basic graph pattern.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The BGP's solution sequence; empty when a constant in the pattern is absent from the data graph.</returns>
    private async ValueTask<IReadOnlyList<SparqlSolution>> EvaluateBgpAsync(Bgp bgp, CancellationToken cancellationToken)
    {
        VariableRegistry registry = new();
        Dictionary<SparqlVariable, Variable> toBackend = [];
        Dictionary<Variable, SparqlVariable> toSparql = [];
        List<EncodedTriplePattern> patterns = new(bgp.Patterns.Count);

        foreach(AstTriplePattern triple in bgp.Patterns)
        {
            //A constant absent from the data graph can never match, so the whole BGP yields nothing.
            if(!TryEncodePosition(triple.Subject, registry, toBackend, toSparql, out PatternPosition subject)
                || !TryEncodePosition(triple.Predicate, registry, toBackend, toSparql, out PatternPosition predicate)
                || !TryEncodePosition(triple.Object, registry, toBackend, toSparql, out PatternPosition @object))
            {
                return [];
            }

            patterns.Add(new EncodedTriplePattern(subject, predicate, @object));
        }

        BasicGraphPattern query = new(patterns, registry);
        List<SparqlSolution> solutions = [];
        await foreach(Solution solution in store.Query(query, TimeProvider.System, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            solutions.Add(Decode(solution, toSparql));
        }

        return solutions;
    }

    /// <summary>
    /// Encodes one triple-pattern term to a backend <see cref="PatternPosition"/>: a variable to a registered
    /// <see cref="Variable"/>, a constant to its bound <see cref="TermId"/>.
    /// </summary>
    /// <param name="term">The term to encode (a constant or a variable).</param>
    /// <param name="registry">The registry minting backend variables for this pattern.</param>
    /// <param name="toBackend">The accumulating SPARQL-variable to backend-variable map.</param>
    /// <param name="toSparql">The accumulating backend-variable to SPARQL-variable map (for decoding).</param>
    /// <param name="position">Receives the encoded position on success.</param>
    /// <returns><see langword="true"/> when the term encodes to a matchable position; <see langword="false"/> when a constant is absent from the data graph.</returns>
    /// <exception cref="NotSupportedException">The term is neither a constant nor a variable (a nested triple term / RDF-star quoted pattern is a later slice).</exception>
    private bool TryEncodePosition(
        TriplePatternTerm term,
        VariableRegistry registry,
        Dictionary<SparqlVariable, Variable> toBackend,
        Dictionary<Variable, SparqlVariable> toSparql,
        out PatternPosition position)
    {
        switch(term)
        {
            case VariableTerm variableTerm:
            {
                position = PatternPosition.OfVariable(GetOrAddVariable(variableTerm.Variable, registry, toBackend, toSparql));

                return true;
            }

            case ConstantTerm constantTerm:
            {
                TermId id = dictionary.GetIdOrDefault(constantTerm.Term);
                if(id.IsNone)
                {
                    position = default;

                    return false;
                }

                position = PatternPosition.Bound(id);

                return true;
            }

            default:
            {
                throw new NotSupportedException($"Triple-pattern term '{term.GetType().Name}' is not yet executable; this slice handles constants and variables. Nested triple terms / RDF-star quoted patterns are a later slice.");
            }
        }
    }

    /// <summary>Returns the backend variable for a SPARQL variable, minting and recording it (both map directions) on first sight.</summary>
    /// <param name="variable">The SPARQL variable.</param>
    /// <param name="registry">The registry minting backend variables.</param>
    /// <param name="toBackend">The SPARQL-variable to backend-variable map.</param>
    /// <param name="toSparql">The backend-variable to SPARQL-variable map.</param>
    /// <returns>The backend variable for <paramref name="variable"/>.</returns>
    private static Variable GetOrAddVariable(
        SparqlVariable variable,
        VariableRegistry registry,
        Dictionary<SparqlVariable, Variable> toBackend,
        Dictionary<Variable, SparqlVariable> toSparql)
    {
        if(toBackend.TryGetValue(variable, out Variable existing))
        {
            return existing;
        }

        Variable backend = registry.GetOrAdd(variable.Name.ToString());
        toBackend[variable] = backend;
        toSparql[backend] = variable;

        return backend;
    }

    /// <summary>Decodes a backend solution to a SPARQL solution, mapping each backend variable to its SPARQL variable and resolving each encoded value to its RDF term.</summary>
    /// <param name="solution">The backend solution.</param>
    /// <param name="toSparql">The backend-variable to SPARQL-variable map for this pattern.</param>
    /// <returns>The decoded SPARQL solution.</returns>
    private SparqlSolution Decode(Solution solution, Dictionary<Variable, SparqlVariable> toSparql)
    {
        List<SparqlBinding> bindings = new(solution.Bindings.Count);
        foreach(VariableBinding binding in solution.Bindings)
        {
            if(toSparql.TryGetValue(binding.Variable, out SparqlVariable variable))
            {
                bindings.Add(new SparqlBinding(variable, dictionary.Resolve(binding.Value)));
            }
        }

        return new SparqlSolution(bindings);
    }

    /// <summary>Restricts each solution to the projected variables, in projection order, dropping bindings for variables a solution does not bind.</summary>
    /// <param name="project">The projection.</param>
    /// <param name="input">The input solution sequence.</param>
    /// <returns>The projected solution sequence.</returns>
    private static List<SparqlSolution> ApplyProject(Project project, IReadOnlyList<SparqlSolution> input)
    {
        List<SparqlSolution> projected = new(input.Count);
        foreach(SparqlSolution solution in input)
        {
            List<SparqlBinding> bindings = new(project.Variables.Count);
            foreach(SparqlVariable variable in project.Variables)
            {
                if(solution.TryGetValue(variable, out RdfTerm value))
                {
                    bindings.Add(new SparqlBinding(variable, value));
                }
            }

            projected.Add(new SparqlSolution(bindings));
        }

        return projected;
    }

    /// <summary>Eliminates duplicate solutions, comparing the (order-stable, post-projection) binding sequences by value.</summary>
    /// <param name="input">The input solution sequence.</param>
    /// <returns>The distinct solution sequence, in first-appearance order.</returns>
    private static List<SparqlSolution> ApplyDistinct(IReadOnlyList<SparqlSolution> input)
    {
        List<SparqlSolution> distinct = new(input.Count);
        HashSet<SparqlSolution> seen = new(SolutionComparer.Instance);
        foreach(SparqlSolution solution in input)
        {
            if(seen.Add(solution))
            {
                distinct.Add(solution);
            }
        }

        return distinct;
    }

    /// <summary>Applies the <c>OFFSET</c>/<c>LIMIT</c> window over the solution sequence.</summary>
    /// <param name="slice">The slice.</param>
    /// <param name="input">The input solution sequence.</param>
    /// <returns>The windowed solution sequence.</returns>
    private static IReadOnlyList<SparqlSolution> ApplySlice(Slice slice, IReadOnlyList<SparqlSolution> input)
    {
        IEnumerable<SparqlSolution> windowed = input;
        if(slice.Offset > 0)
        {
            windowed = windowed.Skip(slice.Offset);
        }

        if(slice.Limit is int limit)
        {
            windowed = windowed.Take(limit);
        }

        return [.. windowed];
    }

    /// <summary>Orders the solution sequence by the <c>ORDER BY</c> conditions (§15.1), each ascending or descending; ties keep input order (a stable sort by original index).</summary>
    /// <param name="orderBy">The order operator.</param>
    /// <param name="input">The input solution sequence.</param>
    /// <param name="context">The seams the order expressions' non-pure functions consume.</param>
    /// <returns>The ordered solution sequence.</returns>
    private static List<SparqlSolution> ApplyOrderBy(OrderBy orderBy, IReadOnlyList<SparqlSolution> input, SparqlExpressionContext context)
    {
        List<SparqlSolution> source = [.. input];
        int[] order = new int[source.Count];
        for(int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (left, right) =>
        {
            int comparison = CompareByConditions(source[left], source[right], orderBy.Conditions, context);

            //A stable tiebreak on the original index keeps ordering deterministic where the keys are equal.
            return comparison != 0 ? comparison : left.CompareTo(right);
        });

        List<SparqlSolution> ordered = new(source.Count);
        foreach(int index in order)
        {
            ordered.Add(source[index]);
        }

        return ordered;
    }

    /// <summary>Compares two solutions by the order conditions in priority order, applying each condition's direction.</summary>
    /// <param name="left">The first solution.</param>
    /// <param name="right">The second solution.</param>
    /// <param name="conditions">The order conditions, in priority order.</param>
    /// <param name="context">The seams the order expressions' non-pure functions consume.</param>
    /// <returns>A negative, zero, or positive value as <paramref name="left"/> orders before, equal to, or after <paramref name="right"/>.</returns>
    private static int CompareByConditions(SparqlSolution left, SparqlSolution right, IReadOnlyList<OrderCondition> conditions, SparqlExpressionContext context)
    {
        foreach(OrderCondition condition in conditions)
        {
            (ExpressionNode expression, bool descending) = condition switch
            {
                OrderAscending ascending => (ascending.Expression, false),
                OrderDescending descendingCondition => (descendingCondition.Expression, true),
                _ => throw new InvalidOperationException($"Unexpected order-condition kind {condition.GetType().Name} during SPARQL evaluation.")
            };

            RdfTerm? leftKey = SparqlExpressionEvaluator.TryEvaluate(expression, left, context, out RdfTerm leftValue) ? leftValue : null;
            RdfTerm? rightKey = SparqlExpressionEvaluator.TryEvaluate(expression, right, context, out RdfTerm rightValue) ? rightValue : null;
            int comparison = SparqlExpressionEvaluator.CompareForOrdering(leftKey, rightKey);
            if(comparison != 0)
            {
                return descending ? -comparison : comparison;
            }
        }

        return 0;
    }

    /// <summary>Builds the solution sequence of an inline <c>VALUES</c> block: one solution per row, binding each variable to its row value and leaving <c>UNDEF</c> (a <see langword="null"/> entry) unbound.</summary>
    /// <param name="data">The inline-data block.</param>
    /// <returns>The block's solution sequence.</returns>
    private static List<SparqlSolution> BuildTableSolutions(ValuesClause data)
    {
        List<SparqlSolution> solutions = new(data.Rows.Count);
        foreach(IReadOnlyList<RdfTerm?> row in data.Rows)
        {
            List<SparqlBinding> bindings = new(data.Variables.Count);
            for(int i = 0; i < data.Variables.Count && i < row.Count; i++)
            {
                if(row[i] is RdfTerm term)
                {
                    bindings.Add(new SparqlBinding(data.Variables[i], term));
                }
            }

            solutions.Add(new SparqlSolution(bindings));
        }

        return solutions;
    }

    /// <summary>
    /// Evaluates a property-path <see cref="Path"/> leaf (§18.6) by resolving the AST path to an
    /// <see cref="Rdf.PropertyPath"/> over encoded ids and delegating to <see cref="PropertyPathEvaluator"/>
    /// (forward reachability over the store's match operations). It enumerates the matching subject/object pairs
    /// per binding mode and binds the variable endpoints: a bound subject enumerates objects; a bound object
    /// enumerates subjects via the inverse path; both bound is an existence check; both variable seeds from every
    /// graph node (the per-seed enumeration — the native pair-aware closure is a later optimization).
    /// </summary>
    /// <param name="path">The path operator.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The path's solution sequence.</returns>
    private async ValueTask<IReadOnlyList<SparqlSolution>> EvaluatePathAsync(Path path, CancellationToken cancellationToken)
    {
        PropertyPath rdfPath = ConvertPath(path.PathExpression);
        GraphMatchOps ops = store.AsMatchOps();
        (TermId? subjectId, SparqlVariable? subjectVariable) = ResolveEndpoint(path.Subject);
        (TermId? objectId, SparqlVariable? objectVariable) = ResolveEndpoint(path.Object);
        List<SparqlSolution> solutions = [];

        if(subjectId is TermId subject)
        {
            //Bound subject: enumerate reachable objects (filtering to the object when it too is bound).
            if(subject.IsNone)
            {
                return solutions;
            }

            await foreach(TermId reached in PropertyPathEvaluator.EvaluateAsync(subject, rdfPath, ops, cancellationToken).ConfigureAwait(false))
            {
                if(objectId is TermId fixedObject && reached != fixedObject)
                {
                    continue;
                }

                BindPathPair(solutions, subjectVariable, subject, objectVariable, reached);
            }
        }
        else if(objectId is TermId @object)
        {
            //Bound object, variable subject: enumerate reachable subjects via the inverse path.
            if(@object.IsNone)
            {
                return solutions;
            }

            await foreach(TermId reached in PropertyPathEvaluator.EvaluateAsync(@object, new InversePath(rdfPath), ops, cancellationToken).ConfigureAwait(false))
            {
                BindPathPair(solutions, subjectVariable, reached, objectVariable, @object);
            }
        }
        else
        {
            //Both endpoints variable: seed forward reachability from every node in the graph.
            foreach(TermId seed in AllNodes())
            {
                await foreach(TermId reached in PropertyPathEvaluator.EvaluateAsync(seed, rdfPath, ops, cancellationToken).ConfigureAwait(false))
                {
                    BindPathPair(solutions, subjectVariable, seed, objectVariable, reached);
                }
            }
        }

        return solutions;
    }

    /// <summary>Resolves a path endpoint to its bound term id (a constant) or the variable it binds.</summary>
    /// <param name="term">The endpoint term (a constant or a variable after normalization).</param>
    /// <returns>The bound term id (a constant — possibly <see cref="TermId.None"/> when absent from the data) and no variable, or no id and the variable.</returns>
    /// <exception cref="NotSupportedException">The endpoint is neither a constant nor a variable.</exception>
    private (TermId? Id, SparqlVariable? Variable) ResolveEndpoint(TriplePatternTerm term)
    {
        return term switch
        {
            ConstantTerm constant => (dictionary.GetIdOrDefault(constant.Term), (SparqlVariable?)null),
            VariableTerm variable => ((TermId?)null, variable.Variable),
            _ => throw new NotSupportedException($"Property-path endpoint '{term.GetType().Name}' is not supported by the executor.")
        };
    }

    /// <summary>Emits a solution for one matched subject/object pair, binding whichever endpoints are variables and decoding their values; a pair with the same variable on both ends matches only when the two values coincide.</summary>
    /// <param name="solutions">The accumulating solution list.</param>
    /// <param name="subjectVariable">The subject variable, or <see langword="null"/> when the subject is a constant.</param>
    /// <param name="subjectValue">The matched subject term id.</param>
    /// <param name="objectVariable">The object variable, or <see langword="null"/> when the object is a constant.</param>
    /// <param name="objectValue">The matched object term id.</param>
    private void BindPathPair(List<SparqlSolution> solutions, SparqlVariable? subjectVariable, TermId subjectValue, SparqlVariable? objectVariable, TermId objectValue)
    {
        //The same variable on both ends (e.g. ?x path ?x) matches only the pairs where the endpoints coincide.
        if(subjectVariable is SparqlVariable both && objectVariable == both && subjectValue != objectValue)
        {
            return;
        }

        List<SparqlBinding> bindings = new(2);
        if(subjectVariable is SparqlVariable subject)
        {
            bindings.Add(new SparqlBinding(subject, dictionary.Resolve(subjectValue)));
        }

        if(objectVariable is SparqlVariable @object && @object != subjectVariable)
        {
            bindings.Add(new SparqlBinding(@object, dictionary.Resolve(objectValue)));
        }

        solutions.Add(new SparqlSolution(bindings));
    }

    /// <summary>Collects every distinct node appearing as a subject or object in the graph — the seed set for a both-variable path (so <c>*</c> is reflexive over all nodes).</summary>
    /// <returns>The distinct nodes.</returns>
    private HashSet<TermId> AllNodes()
    {
        HashSet<TermId> nodes = [];
        foreach(EncodedTriple triple in store.Match(TermId.None, TermId.None, TermId.None))
        {
            nodes.Add(triple.Subject);
            nodes.Add(triple.Object);
        }

        return nodes;
    }

    /// <summary>
    /// Converts an AST property-path expression to the encoded <see cref="Rdf.PropertyPath"/> the
    /// <see cref="PropertyPathEvaluator"/> consumes, over an explicit post-order stack (no recursion). Predicate
    /// IRIs resolve through the term dictionary; one absent from the data becomes a <see cref="TermId.None"/>
    /// predicate that matches nothing.
    /// </summary>
    /// <param name="root">The AST path expression.</param>
    /// <returns>The encoded property path.</returns>
    /// <exception cref="NotSupportedException">The path uses a negated property set (no encoded-path representation yet).</exception>
    private PropertyPath ConvertPath(PropertyPathExpression root)
    {
        Dictionary<PropertyPathExpression, PropertyPath> results = new(ReferenceEqualityComparer.Instance);
        Stack<(PropertyPathExpression Node, bool Combine)> work = new();
        work.Push((root, Combine: false));

        while(work.Count > 0)
        {
            (PropertyPathExpression node, bool combine) = work.Pop();
            if(combine)
            {
                results[node] = CombinePath(node, results);

                continue;
            }

            IReadOnlyList<PropertyPathExpression> children = PathChildren(node);
            if(children.Count == 0)
            {
                results[node] = ConvertLeafPath(node);
            }
            else
            {
                work.Push((node, Combine: true));
                for(int i = children.Count - 1; i >= 0; i--)
                {
                    work.Push((children[i], Combine: false));
                }
            }
        }

        return results[root];
    }

    /// <summary>Returns the sub-paths of an AST path expression; empty for a single predicate or a negated set.</summary>
    /// <param name="node">The path expression.</param>
    /// <returns>The sub-paths.</returns>
    private static IReadOnlyList<PropertyPathExpression> PathChildren(PropertyPathExpression node)
    {
        return node switch
        {
            PathInverse inverse => [inverse.Inner],
            PathSequence sequence => sequence.Steps,
            PathAlternative alternative => alternative.Alternatives,
            PathZeroOrMore zeroOrMore => [zeroOrMore.Inner],
            PathOneOrMore oneOrMore => [oneOrMore.Inner],
            PathZeroOrOne zeroOrOne => [zeroOrOne.Inner],
            _ => []
        };
    }

    /// <summary>Converts a leaf path expression: a single predicate to a <see cref="PredicatePath"/> over its encoded id.</summary>
    /// <param name="node">The leaf path expression.</param>
    /// <returns>The encoded path.</returns>
    /// <exception cref="NotSupportedException">The leaf is a negated property set.</exception>
    private PredicatePath ConvertLeafPath(PropertyPathExpression node)
    {
        return node switch
        {
            PathPredicate predicate => new PredicatePath(IriId.FromUnchecked(dictionary.GetIdOrDefault(new NamedNode(predicate.Predicate.Value)))),
            PathNegatedSet => throw new NotSupportedException("SPARQL negated property sets are not yet executable; the encoded property-path representation has no negated-set form."),
            _ => throw new InvalidOperationException($"Path expression '{node.GetType().Name}' has sub-paths but reached the leaf converter.")
        };
    }

    /// <summary>Combines a composite path expression from its already-converted sub-paths.</summary>
    /// <param name="node">The composite path expression.</param>
    /// <param name="results">The map of already-converted sub-paths.</param>
    /// <returns>The encoded path.</returns>
    private static PropertyPath CombinePath(PropertyPathExpression node, Dictionary<PropertyPathExpression, PropertyPath> results)
    {
        return node switch
        {
            PathInverse inverse => new InversePath(results[inverse.Inner]),
            PathSequence sequence => new SequencePath(ToPathArray(sequence.Steps, results)),
            PathAlternative alternative => new AlternativePath(ToPathArray(alternative.Alternatives, results)),
            PathZeroOrMore zeroOrMore => new ZeroOrMorePath(results[zeroOrMore.Inner]),
            PathOneOrMore oneOrMore => new OneOrMorePath(results[oneOrMore.Inner]),
            PathZeroOrOne zeroOrOne => new ZeroOrOnePath(results[zeroOrOne.Inner]),
            _ => throw new InvalidOperationException($"Path expression '{node.GetType().Name}' has no combine rule.")
        };
    }

    /// <summary>Collects the converted sub-paths of a sequence/alternative into an immutable array, in order.</summary>
    /// <param name="steps">The AST sub-paths.</param>
    /// <param name="results">The map of already-converted sub-paths.</param>
    /// <returns>The converted sub-paths.</returns>
    private static ImmutableArray<PropertyPath> ToPathArray(IReadOnlyList<PropertyPathExpression> steps, Dictionary<PropertyPathExpression, PropertyPath> results)
    {
        ImmutableArray<PropertyPath>.Builder builder = ImmutableArray.CreateBuilder<PropertyPath>(steps.Count);
        foreach(PropertyPathExpression step in steps)
        {
            builder.Add(results[step]);
        }

        return builder.ToImmutable();
    }

    /// <summary>Joins two solution sequences (§18.6 Join): the merge of every pair of compatible solutions, one from each side.</summary>
    /// <param name="left">The left solution sequence.</param>
    /// <param name="right">The right solution sequence.</param>
    /// <returns>The joined solution sequence.</returns>
    private static List<SparqlSolution> JoinSolutions(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right)
    {
        List<SparqlSolution> joined = [];
        foreach(SparqlSolution outer in left)
        {
            foreach(SparqlSolution inner in right)
            {
                if(AreCompatible(outer, inner))
                {
                    joined.Add(Merge(outer, inner));
                }
            }
        }

        return joined;
    }

    /// <summary>Unions two solution sequences (§18.6 Union): the multiset of all solutions of either side, left before right.</summary>
    /// <param name="left">The left solution sequence.</param>
    /// <param name="right">The right solution sequence.</param>
    /// <returns>The unioned solution sequence.</returns>
    private static List<SparqlSolution> UnionSolutions(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right)
    {
        List<SparqlSolution> all = new(left.Count + right.Count);
        all.AddRange(left);
        all.AddRange(right);

        return all;
    }

    /// <summary>
    /// Subtracts the right sequence from the left (§18.6 Minus): a left solution is removed only when some right
    /// solution is compatible with it <em>and</em> shares at least one variable. The shared-variable requirement is
    /// the spec's disjoint-domain exception — a left solution with no variable in common with any right solution is
    /// kept even though disjoint mappings are technically compatible.
    /// </summary>
    /// <param name="left">The left (kept) solution sequence.</param>
    /// <param name="right">The right (subtracting) solution sequence.</param>
    /// <returns>The left solutions that survive the subtraction.</returns>
    private static List<SparqlSolution> MinusSolutions(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right)
    {
        List<SparqlSolution> kept = new(left.Count);
        foreach(SparqlSolution candidate in left)
        {
            bool removed = false;
            foreach(SparqlSolution subtractor in right)
            {
                if(SharesVariable(candidate, subtractor) && AreCompatible(candidate, subtractor))
                {
                    removed = true;

                    break;
                }
            }

            if(!removed)
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }

    /// <summary>Keeps the solutions whose <c>FILTER</c> condition has effective boolean value true (§18.6 Filter); an error or non-boolean value drops the solution.</summary>
    /// <param name="filter">The filter.</param>
    /// <param name="input">The input solution sequence.</param>
    /// <param name="context">The seams the condition's non-pure functions consume.</param>
    /// <returns>The surviving solutions.</returns>
    private static List<SparqlSolution> ApplyFilter(Filter filter, IReadOnlyList<SparqlSolution> input, SparqlExpressionContext context)
    {
        List<SparqlSolution> kept = new(input.Count);
        foreach(SparqlSolution solution in input)
        {
            if(SparqlExpressionEvaluator.Satisfies(filter.Condition, solution, context))
            {
                kept.Add(solution);
            }
        }

        return kept;
    }

    /// <summary>Binds an expression's value to a variable on each solution (§18.6 Extend / <c>BIND</c>); a solution whose expression errs is kept with the variable left unbound.</summary>
    /// <param name="extend">The extend.</param>
    /// <param name="input">The input solution sequence.</param>
    /// <param name="context">The seams the expression's non-pure functions consume.</param>
    /// <returns>The extended solution sequence.</returns>
    private static List<SparqlSolution> ApplyExtend(Extend extend, IReadOnlyList<SparqlSolution> input, SparqlExpressionContext context)
    {
        List<SparqlSolution> extended = new(input.Count);
        foreach(SparqlSolution solution in input)
        {
            if(!SparqlExpressionEvaluator.TryEvaluate(extend.Expression, solution, context, out RdfTerm value))
            {
                extended.Add(solution);

                continue;
            }

            List<SparqlBinding> bindings = new(solution.Bindings.Count + 1);
            bindings.AddRange(solution.Bindings);
            bindings.Add(new SparqlBinding(extend.Variable, value));
            extended.Add(new SparqlSolution(bindings));
        }

        return extended;
    }

    /// <summary>
    /// Evaluates an <c>OPTIONAL</c> (§18.6 LeftJoin): each left solution is extended by every compatible right
    /// solution that also satisfies the optional condition, and a left solution with no such extension is kept
    /// unchanged.
    /// </summary>
    /// <param name="left">The required (left) solution sequence.</param>
    /// <param name="right">The optional (right) solution sequence.</param>
    /// <param name="condition">The optional join condition (the lifted inner <c>FILTER</c>), or <see langword="null"/> when there is none.</param>
    /// <param name="context">The seams the condition's non-pure functions consume.</param>
    /// <returns>The left-joined solution sequence.</returns>
    private static List<SparqlSolution> LeftJoinSolutions(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right, ExpressionNode? condition, SparqlExpressionContext context)
    {
        List<SparqlSolution> result = [];
        foreach(SparqlSolution outer in left)
        {
            bool extended = false;
            foreach(SparqlSolution inner in right)
            {
                if(!AreCompatible(outer, inner))
                {
                    continue;
                }

                SparqlSolution merged = Merge(outer, inner);
                if(condition is null || SparqlExpressionEvaluator.Satisfies(condition, merged, context))
                {
                    result.Add(merged);
                    extended = true;
                }
            }

            if(!extended)
            {
                result.Add(outer);
            }
        }

        return result;
    }

    /// <summary>
    /// Evaluates an <c>AggregateJoin</c> (§18.5): partitions the input solutions into groups by the wrapped
    /// <see cref="Group"/>'s keys, then emits one solution per group binding the named grouping keys and each
    /// aggregate's per-group result. An empty <c>GROUP BY</c> (implicit grouping) always yields exactly one group,
    /// even over no input solutions, so <c>COUNT(*)</c> over an empty pattern is 0.
    /// </summary>
    /// <param name="aggregateJoin">The aggregate-join operator (its input is the grouping operator).</param>
    /// <param name="input">The grouped operand's solutions (the Group passthrough).</param>
    /// <param name="context">The seams the grouping-key and aggregate expressions' non-pure functions consume.</param>
    /// <returns>One solution per group.</returns>
    private static List<SparqlSolution> EvaluateAggregateJoin(AggregateJoin aggregateJoin, IReadOnlyList<SparqlSolution> input, SparqlExpressionContext context)
    {
        IReadOnlyList<GroupCondition> keys = aggregateJoin.Input is Group group ? group.Keys : [];
        List<AggregateGroup> groups = Partition(keys, input, context);

        //Implicit grouping (no keys) is always a single group, even with no input rows.
        if(keys.Count == 0 && groups.Count == 0)
        {
            groups.Add(new AggregateGroup([], []));
        }

        List<SparqlSolution> result = new(groups.Count);
        foreach(AggregateGroup aggregateGroup in groups)
        {
            List<SparqlBinding> bindings = new(aggregateGroup.NamedKeys);
            foreach(AggregateBinding aggregation in aggregateJoin.Aggregations)
            {
                if(SparqlExpressionEvaluator.EvaluateAggregate(aggregation.Aggregate, aggregateGroup.Members, context) is RdfTerm value)
                {
                    bindings.Add(new SparqlBinding(aggregation.Variable, value));
                }
            }

            result.Add(new SparqlSolution(bindings));
        }

        return result;
    }

    /// <summary>Partitions solutions into groups by their grouping-key values, preserving first-appearance order of groups.</summary>
    /// <param name="keys">The grouping conditions.</param>
    /// <param name="input">The solutions to partition.</param>
    /// <param name="context">The seams the grouping-key expressions' non-pure functions consume.</param>
    /// <returns>The groups, each carrying its named-key bindings and member solutions.</returns>
    private static List<AggregateGroup> Partition(IReadOnlyList<GroupCondition> keys, IReadOnlyList<SparqlSolution> input, SparqlExpressionContext context)
    {
        List<AggregateGroup> groups = [];
        foreach(SparqlSolution solution in input)
        {
            RdfTerm?[] keyValues = new RdfTerm?[keys.Count];
            List<SparqlBinding> namedKeys = [];
            for(int i = 0; i < keys.Count; i++)
            {
                (RdfTerm? value, SparqlVariable? named) = EvaluateGroupKey(keys[i], solution, context);
                keyValues[i] = value;
                if(named is SparqlVariable variable && value is RdfTerm term)
                {
                    namedKeys.Add(new SparqlBinding(variable, term));
                }
            }

            AggregateGroup? match = null;
            foreach(AggregateGroup candidate in groups)
            {
                if(KeysEqual(candidate.KeyValues, keyValues))
                {
                    match = candidate;

                    break;
                }
            }

            if(match is null)
            {
                groups.Add(new AggregateGroup(keyValues, namedKeys) { Members = { solution } });
            }
            else
            {
                match.Members.Add(solution);
            }
        }

        return groups;
    }

    /// <summary>Evaluates one grouping condition over a solution, returning the key value and the variable it names (if any).</summary>
    /// <param name="condition">The grouping condition.</param>
    /// <param name="solution">The solution.</param>
    /// <param name="context">The seams the grouping expression's non-pure functions consume.</param>
    /// <returns>The key value (or <see langword="null"/> when unbound/errored) and the named variable the group binds, or <see langword="null"/> for a bare grouping expression.</returns>
    private static (RdfTerm? Value, SparqlVariable? Named) EvaluateGroupKey(GroupCondition condition, SparqlSolution solution, SparqlExpressionContext context)
    {
        switch(condition)
        {
            case GroupVariable variable:
            {
                return (solution.TryGetValue(variable.Variable, out RdfTerm value) ? value : null, variable.Variable);
            }

            case GroupExpressionAs expressionAs:
            {
                return (SparqlExpressionEvaluator.TryEvaluate(expressionAs.Expression, solution, context, out RdfTerm value) ? value : null, expressionAs.AsVariable);
            }

            case GroupExpression expression:
            {
                return (SparqlExpressionEvaluator.TryEvaluate(expression.Expression, solution, context, out RdfTerm value) ? value : null, null);
            }

            default:
            {
                throw new InvalidOperationException($"Unexpected grouping-condition kind {condition.GetType().Name} during SPARQL evaluation.");
            }
        }
    }

    /// <summary>Returns whether two grouping-key tuples are equal position-by-position (unbound matches unbound; bound matches an equal term).</summary>
    /// <param name="left">The first key tuple.</param>
    /// <param name="right">The second key tuple.</param>
    /// <returns><see langword="true"/> when the tuples are equal.</returns>
    private static bool KeysEqual(RdfTerm?[] left, RdfTerm?[] right)
    {
        if(left.Length != right.Length)
        {
            return false;
        }

        for(int i = 0; i < left.Length; i++)
        {
            if(left[i] is null != (right[i] is null))
            {
                return false;
            }

            if(left[i] is RdfTerm leftTerm && !leftTerm.Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns whether two solutions are compatible (§18.1): every variable bound in both maps to the same term.</summary>
    /// <param name="left">The first solution.</param>
    /// <param name="right">The second solution.</param>
    /// <returns><see langword="true"/> when the solutions agree on every shared variable.</returns>
    private static bool AreCompatible(SparqlSolution left, SparqlSolution right)
    {
        foreach(SparqlBinding binding in left.Bindings)
        {
            if(right.TryGetValue(binding.Variable, out RdfTerm value) && !value.Equals(binding.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns whether two solutions bind at least one variable in common (their domains intersect).</summary>
    /// <param name="left">The first solution.</param>
    /// <param name="right">The second solution.</param>
    /// <returns><see langword="true"/> when some variable is bound in both.</returns>
    private static bool SharesVariable(SparqlSolution left, SparqlSolution right)
    {
        foreach(SparqlBinding binding in left.Bindings)
        {
            if(right.TryGetValue(binding.Variable, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Merges two compatible solutions: every binding of <paramref name="left"/>, plus each binding of <paramref name="right"/> for a variable the left does not bind.</summary>
    /// <param name="left">The left solution.</param>
    /// <param name="right">The right solution.</param>
    /// <returns>The merged solution.</returns>
    private static SparqlSolution Merge(SparqlSolution left, SparqlSolution right)
    {
        List<SparqlBinding> merged = new(left.Bindings.Count + right.Bindings.Count);
        merged.AddRange(left.Bindings);
        foreach(SparqlBinding binding in right.Bindings)
        {
            if(!left.TryGetValue(binding.Variable, out _))
            {
                merged.Add(binding);
            }
        }

        return new SparqlSolution(merged);
    }

    /// <summary>One group during aggregation: its grouping-key values (for partitioning), the named-key bindings it contributes to the output solution, and its member solutions.</summary>
    private sealed class AggregateGroup
    {
        /// <summary>Constructs a group with its key tuple and named-key bindings; members start empty.</summary>
        /// <param name="keyValues">The grouping-key values, in condition order (an entry is <see langword="null"/> for an unbound/errored key).</param>
        /// <param name="namedKeys">The bindings the group's named keys contribute to its output solution.</param>
        public AggregateGroup(RdfTerm?[] keyValues, List<SparqlBinding> namedKeys)
        {
            KeyValues = keyValues;
            NamedKeys = namedKeys;
        }

        /// <summary>The grouping-key values used to match solutions into this group.</summary>
        public RdfTerm?[] KeyValues { get; }

        /// <summary>The named grouping-key bindings the group contributes to its output solution.</summary>
        public List<SparqlBinding> NamedKeys { get; }

        /// <summary>The solutions partitioned into this group.</summary>
        public List<SparqlSolution> Members { get; } = [];
    }

    /// <summary>Verifies every operator in the algebra is one this slice can execute, throwing a descriptive <see cref="NotSupportedException"/> on the first that is not.</summary>
    /// <param name="algebra">The algebra to check.</param>
    /// <exception cref="NotSupportedException">An operator outside this slice's supported set is present.</exception>
    private static void GuardSupported(AlgebraOperator algebra)
    {
        foreach(AlgebraOperator op in AlgebraWalker.Traverse(algebra))
        {
            bool supported = op is Bgp or UnitTable or Table or Project or Distinct or Reduced or Slice or OrderBy or Join or Union or Minus or ToMultiSet or ToList or Filter or Extend or LeftJoin or Group or AggregateJoin or Path;
            if(!supported)
            {
                throw new NotSupportedException($"SPARQL algebra operator '{op.GetType().Name}' is not yet executable. This executor covers basic graph patterns, property paths, JOIN/UNION/MINUS, FILTER, BIND (Extend), OPTIONAL (LeftJoin), inline VALUES, ORDER BY, aggregation (GROUP BY / HAVING), sub-SELECT (ToMultiSet), and the projection/DISTINCT/REDUCED/OFFSET/LIMIT modifiers; named graphs (GRAPH), SERVICE, and EXISTS land in later slices.");
            }
        }
    }

    /// <summary>Value equality over solutions by their binding sequences — used to deduplicate under <see cref="Distinct"/> (where projection has already fixed the binding order).</summary>
    private sealed class SolutionComparer : IEqualityComparer<SparqlSolution>
    {
        /// <summary>The shared comparer instance.</summary>
        public static SolutionComparer Instance { get; } = new();

        /// <summary>Returns whether two solutions have identical binding sequences.</summary>
        /// <param name="x">The first solution.</param>
        /// <param name="y">The second solution.</param>
        /// <returns><see langword="true"/> when the binding sequences are equal.</returns>
        public bool Equals(SparqlSolution? x, SparqlSolution? y)
        {
            if(ReferenceEquals(x, y))
            {
                return true;
            }

            if(x is null || y is null || x.Bindings.Count != y.Bindings.Count)
            {
                return false;
            }

            for(int i = 0; i < x.Bindings.Count; i++)
            {
                if(!x.Bindings[i].Equals(y.Bindings[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Computes a binding-sequence hash for a solution.</summary>
        /// <param name="solution">The solution.</param>
        /// <returns>The hash code.</returns>
        public int GetHashCode(SparqlSolution solution)
        {
            HashCode hash = new();
            foreach(SparqlBinding binding in solution.Bindings)
            {
                hash.Add(binding);
            }

            return hash.ToHashCode();
        }
    }
}
