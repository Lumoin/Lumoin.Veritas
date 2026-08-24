using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Parsing;
using AstTriplePattern = Lumoin.Veritas.Sparql.Ast.TriplePattern;
using AstTripleTerm = Lumoin.Veritas.Sparql.Ast.TripleTerm;
using CoreTripleTerm = Lumoin.Veritas.Core.TripleTerm;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Instantiates a <c>CONSTRUCT</c> graph template against a solution sequence (SPARQL 1.2 §16.2): each solution
/// substitutes the template's variables, yielding triples; the union of the well-formed, distinct instantiations is
/// the result graph. The same instantiation is the engine behind SPARQL Update's <c>INSERT … WHERE</c> /
/// <c>DELETE … WHERE</c> (which differ only in the sink), so this is shared, storage-independent machinery.
/// </summary>
/// <remarks>
/// <para>
/// <b>Well-formedness (§16.2.1).</b> An instantiated triple is emitted only when it is a legal RDF triple: a triple
/// with an unbound variable, a literal/triple-term in the predicate position, or a literal in the subject position
/// is silently dropped (not an error). A blank node in the template produces a <em>fresh</em> blank node per
/// solution — the same template label maps to one blank node within a row but distinct blank nodes across rows.
/// </para>
/// <para>
/// <b>No recursion.</b> A template position may be a nested RDF 1.2 quoted triple term; each position is resolved by
/// an explicit post-order walk over the term tree, never call-stack recursion.
/// </para>
/// </remarks>
public static class SparqlGraphConstruction
{
    /// <summary>The blank-label range reserved per update operation, so two operations' same-labelled template blank nodes mint distinct nodes (§4.1.2). One operation is assumed to mint fewer than this many blank nodes.</summary>
    private const int BlankScopeStride = 1_000_000;

    /// <summary>Instantiates the template against every solution and returns the distinct result triples (as default-graph quads).</summary>
    /// <param name="template">The normalized CONSTRUCT template (plain triple patterns; collections/blank-node lists already lowered).</param>
    /// <param name="solutions">The solution sequence the WHERE clause produced.</param>
    /// <returns>The distinct, well-formed instantiated triples.</returns>
    public static List<Quad> Construct(IReadOnlyList<AstTriplePattern> template, IReadOnlyList<SparqlSolution> solutions)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(solutions);

        List<Quad> quads = [];
        HashSet<Quad> seen = [];

        //One monotonic counter across the whole construction mints fresh blank-node labels; a per-solution map keeps
        //a template label consistent within a row but distinct across rows. The labels themselves are immaterial
        //(the result graph is compared up to blank-node isomorphism), so a deterministic counter — not entropy — is
        //the right source.
        int blankCounter = 0;
        foreach(SparqlSolution solution in solutions)
        {
            Dictionary<Utf8String, BlankNode> rowBlanks = [];
            foreach(AstTriplePattern pattern in template)
            {
                if(TryInstantiate(pattern, graph: null, solution, rowBlanks, ref blankCounter, out Quad quad) && seen.Add(quad))
                {
                    quads.Add(quad);
                }
            }
        }

        return quads;
    }

    /// <summary>
    /// Instantiates a SPARQL Update quad block (default-graph triples and <c>GRAPH</c> groups) against a solution
    /// sequence, yielding the distinct well-formed quads with their target graph. The same instantiation engine as
    /// <see cref="Construct"/>: per-solution blank nodes (a template label is one blank node within a row, distinct
    /// across rows), the §16.2.1 well-formedness filter. A <c>GRAPH</c> group whose graph designator is an unbound
    /// variable contributes nothing for that solution. For ground data (<c>INSERT</c>/<c>DELETE DATA</c>), pass a
    /// single empty solution.
    /// </summary>
    /// <param name="quads">The normalized quad block (sugar already lowered to plain triple patterns).</param>
    /// <param name="solutions">The solution sequence; a single empty solution for ground data.</param>
    /// <param name="operationScope">The update operation's ordinal in the request, which seeds a disjoint blank-node label range so the same template label in two different operations mints distinct blank nodes (SPARQL Update §4.1.2 — blank nodes are scoped per operation).</param>
    /// <returns>The distinct, well-formed instantiated quads (each carrying its target graph, or none for the default graph).</returns>
    public static List<Quad> InstantiateQuads(Ast.Quads quads, IReadOnlyList<SparqlSolution> solutions, int operationScope = 0)
    {
        ArgumentNullException.ThrowIfNull(quads);
        ArgumentNullException.ThrowIfNull(solutions);

        List<Quad> result = [];
        HashSet<Quad> seen = [];

        //Each operation gets a disjoint blank-label range (operationScope · stride): the same `_:b` label in two
        //operations must be two distinct blank nodes, while within one operation it stays one (the per-row map below).
        int blankCounter = operationScope * BlankScopeStride;
        foreach(SparqlSolution solution in solutions)
        {
            Dictionary<Utf8String, BlankNode> rowBlanks = [];
            foreach(AstTriplePattern pattern in quads.DefaultTriples)
            {
                if(TryInstantiate(pattern, null, solution, rowBlanks, ref blankCounter, out Quad quad) && seen.Add(quad))
                {
                    result.Add(quad);
                }
            }

            foreach(Ast.QuadsGraphGroup group in quads.GraphGroups)
            {
                if(ResolveGraph(group.Graph, solution) is not RdfTerm graph)
                {
                    //An unbound graph variable: the group instantiates to nothing for this solution.
                    continue;
                }

                foreach(AstTriplePattern pattern in group.Triples)
                {
                    if(TryInstantiate(pattern, graph, solution, rowBlanks, ref blankCounter, out Quad quad) && seen.Add(quad))
                    {
                        result.Add(quad);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Resolves a <c>GRAPH</c> designator to a concrete graph term: an IRI to itself, a variable to its binding (or <see langword="null"/> when unbound).</summary>
    /// <param name="graph">The graph designator.</param>
    /// <param name="solution">The solution supplying variable bindings.</param>
    /// <returns>The graph term, or <see langword="null"/> when an unbound variable.</returns>
    private static RdfTerm? ResolveGraph(Ast.GraphTerm graph, SparqlSolution solution)
        => graph switch
        {
            Ast.GraphIriTerm iri => new NamedNode(iri.Iri.Value),
            Ast.GraphVariableTerm variable => solution.TryGetValue(variable.Variable, out RdfTerm value) ? value : null,
            _ => null
        };

    /// <summary>Instantiates one template triple under a solution into the given target graph, applying the §16.2.1 well-formedness filter.</summary>
    /// <param name="pattern">The template triple pattern.</param>
    /// <param name="graph">The target graph term, or <see langword="null"/> for the default graph.</param>
    /// <param name="solution">The solution supplying variable bindings.</param>
    /// <param name="rowBlanks">The per-solution template-label to blank-node map.</param>
    /// <param name="blankCounter">The fresh-blank-node counter, advanced as labels are minted.</param>
    /// <param name="quad">Receives the instantiated quad on success.</param>
    /// <returns><see langword="true"/> when a well-formed triple was produced.</returns>
    private static bool TryInstantiate(AstTriplePattern pattern, RdfTerm? graph, SparqlSolution solution, Dictionary<Utf8String, BlankNode> rowBlanks, ref int blankCounter, out Quad quad)
    {
        quad = null!;
        if(Resolve(pattern.Subject, solution, rowBlanks, ref blankCounter) is not RdfTerm subject
            || Resolve(pattern.Predicate, solution, rowBlanks, ref blankCounter) is not RdfTerm predicate
            || Resolve(pattern.Object, solution, rowBlanks, ref blankCounter) is not RdfTerm @object)
        {
            return false;
        }

        //A legal RDF triple: subject is an IRI/blank node/triple term (not a literal); predicate is an IRI.
        if(subject is Literal || predicate is not NamedNode predicateIri)
        {
            return false;
        }

        quad = new Quad(subject, predicateIri, @object, graph);

        return true;
    }

    /// <summary>
    /// Resolves a template term to a concrete RDF term under a solution — a variable to its binding (or
    /// <see langword="null"/> when unbound), a template blank node to its per-row fresh blank node, a constant to
    /// itself, and a quoted triple term to a <see cref="CoreTripleTerm"/> built from its resolved components. Walks
    /// the term tree over an explicit post-order stack (no recursion). Returns <see langword="null"/> when any part
    /// is unbound or the term cannot form a legal RDF term (a property path, or a triple term with a non-IRI
    /// predicate / literal subject).
    /// </summary>
    /// <param name="root">The template term.</param>
    /// <param name="solution">The solution supplying variable bindings.</param>
    /// <param name="rowBlanks">The per-solution template-label to blank-node map.</param>
    /// <param name="blankCounter">The fresh-blank-node counter, advanced as labels are minted.</param>
    /// <returns>The resolved term, or <see langword="null"/>.</returns>
    private static RdfTerm? Resolve(Ast.TriplePatternTerm root, SparqlSolution solution, Dictionary<Utf8String, BlankNode> rowBlanks, ref int blankCounter)
    {
        Dictionary<Ast.TriplePatternTerm, RdfTerm?> resolved = new(ReferenceEqualityComparer.Instance);
        Stack<(Ast.TriplePatternTerm Term, bool Combine, int Depth)> work = new();
        work.Push((root, Combine: false, Depth: 1));

        while(work.Count > 0)
        {
            (Ast.TriplePatternTerm term, bool combine, int depth) = work.Pop();
            if(combine)
            {
                AstTripleTerm tripleTerm = (AstTripleTerm)term;
                resolved[term] = BuildTripleTerm(
                    resolved[tripleTerm.Inner.Subject],
                    resolved[tripleTerm.Inner.Predicate],
                    resolved[tripleTerm.Inner.Object]);

                continue;
            }

            switch(term)
            {
                case Ast.VariableTerm variable:
                {
                    resolved[term] = solution.TryGetValue(variable.Variable, out RdfTerm value) ? value : null;

                    break;
                }

                case Ast.ConstantTerm { Term: BlankNode templateBlank }:
                {
                    resolved[term] = ResolveBlank(rowBlanks, templateBlank.Label, ref blankCounter);

                    break;
                }

                case Ast.ConstantTerm constant:
                {
                    resolved[term] = constant.Term;

                    break;
                }

                case AstTripleTerm tripleTerm:
                {
                    if(depth > QuotedTripleLimits.MaxNestingDepth)
                    {
                        throw new TripleTermDepthLimitException(depth, QuotedTripleLimits.MaxNestingDepth);
                    }

                    //Resolve the three components first, then combine on the second visit.
                    work.Push((term, Combine: true, depth));
                    work.Push((tripleTerm.Inner.Object, Combine: false, depth + 1));
                    work.Push((tripleTerm.Inner.Predicate, Combine: false, depth + 1));
                    work.Push((tripleTerm.Inner.Subject, Combine: false, depth + 1));

                    break;
                }

                default:
                {
                    //A property path has no place in a CONSTRUCT template.
                    resolved[term] = null;

                    break;
                }
            }
        }

        return resolved[root];
    }

    /// <summary>Builds a quoted triple term from its resolved components, or <see langword="null"/> when they cannot form one (unbound, non-IRI predicate, or literal subject).</summary>
    /// <param name="subject">The resolved subject.</param>
    /// <param name="predicate">The resolved predicate.</param>
    /// <param name="object">The resolved object.</param>
    /// <returns>The triple term, or <see langword="null"/>.</returns>
    private static CoreTripleTerm? BuildTripleTerm(RdfTerm? subject, RdfTerm? predicate, RdfTerm? @object)
    {
        if(subject is null or Literal || predicate is not NamedNode predicateIri || @object is null)
        {
            return null;
        }

        return new CoreTripleTerm(subject, predicateIri, @object);
    }

    /// <summary>Returns the per-row blank node for a template label, minting a fresh one (deterministic label) on first use within the row.</summary>
    /// <param name="rowBlanks">The per-solution template-label to blank-node map.</param>
    /// <param name="templateLabel">The template's blank-node label.</param>
    /// <param name="blankCounter">The fresh-blank-node counter, advanced when a label is minted.</param>
    /// <returns>The row's blank node for the label.</returns>
    private static BlankNode ResolveBlank(Dictionary<Utf8String, BlankNode> rowBlanks, Utf8String templateLabel, ref int blankCounter)
    {
        if(rowBlanks.TryGetValue(templateLabel, out BlankNode? existing))
        {
            return existing;
        }

        BlankNode fresh = new(Utf8Strings.From("cb" + blankCounter.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        blankCounter++;
        rowBlanks[templateLabel] = fresh;

        return fresh;
    }
}
