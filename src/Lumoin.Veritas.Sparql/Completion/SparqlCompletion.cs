using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Analysis;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;

namespace Lumoin.Veritas.Sparql.Completion;

/// <summary>
/// Caret-aware SPARQL completion: given a query buffer and a byte offset, produces the
/// <see cref="CompletionContext"/> at that caret — the token kinds the grammar admits next and the open
/// production chain enclosing the caret. The context is built store-free from the lexer and parser; the
/// in-scope variables and their datatypes are filled by later stages (scope analysis, then a store-backed
/// datatype resolver at the consumer), so for now <see cref="CompletionContext.InScopeVariables"/> is empty
/// and datatypes are <see cref="DatatypeSource.Unknown"/>.
/// </summary>
public static class SparqlCompletion
{
    /// <summary>
    /// Describes the completion context at <paramref name="caretByteOffset"/> in <paramref name="source"/>.
    /// The query text up to the caret is lexed and driven to that point, suspending the parser with its work
    /// stack intact; the open productions fix the expected next tokens — the innermost one widened by the
    /// continuations of the enclosing productions it may close into — and give the enclosing-production
    /// chain from outermost to innermost.
    /// </summary>
    /// <param name="source">The UTF-8 query buffer.</param>
    /// <param name="caretByteOffset">The caret position as a byte offset into <paramref name="source"/>; clamped to the buffer.</param>
    /// <param name="pool">The pool to intern parser-allocated values into; a private pool is created when <see langword="null"/>.</param>
    /// <param name="baseIri">The external base IRI relative references resolve against before any in-query <c>BASE</c>, or <see langword="null"/>.</param>
    /// <returns>The completion context at the caret.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The private pool backs the interned values the returned CompletionContext exposes (later: variable names and datatype IRIs); it is kept alive by the context and collected with it rather than disposed here, matching the SparqlParser.ParseRequest facade.")]
    public static CompletionContext Describe(ReadOnlyMemory<byte> source, int caretByteOffset, Utf8StringPool? pool = null, Utf8String? baseIri = null)
    {
        int caret = Math.Clamp(caretByteOffset, 0, source.Length);
        ReadOnlyMemory<byte> prefix = source[..caret];

        Utf8StringPool effectivePool = pool ?? new Utf8StringPool();
        SparqlLexer lexer = new(effectivePool);
        SparqlParser parser = new(effectivePool, baseIri);
        lexer.FeedDecodedSource(prefix.Span, isFinal: true);

        SparqlTokenKind lastKind = SparqlTokenKind.EndOfInput;
        SparqlTokenKind secondLastKind = SparqlTokenKind.EndOfInput;
        while(true)
        {
            SparqlLexStatus status = lexer.TryLexNext(out SparqlToken token);
            if(status == SparqlLexStatus.NeedMore)
            {
                break;
            }

            if(token.Kind != SparqlTokenKind.EndOfInput)
            {
                secondLastKind = lastKind;
                lastKind = token.Kind;
            }

            parser.FeedToken(token);
            if(token.Kind == SparqlTokenKind.EndOfInput)
            {
                break;
            }
        }

        IReadOnlyList<(ParseFrameKind Kind, int Stage)> openFrames = parser.SuspendOpenFramesAtEndOfInput();

        ImmutableArray<SparqlTokenKind> expectedTokens = SparqlCaretExpectations.ExpectedTokensAcross(openFrames);

        //An RDF literal's datatype position is invisible to the frame map: the literal leaf consumes the '^^'
        //and recovers within one parser step when the input ends there, so the suspended frames report the
        //surrounding position. The token stream still carries the position exactly — a caret directly after a
        //string literal's '^^' admits precisely an IRI or a prefixed name.
        if(lastKind == SparqlTokenKind.TypeMarker
            && secondLastKind is SparqlTokenKind.StringLiteral or SparqlTokenKind.LongStringLiteral)
        {
            expectedTokens = SparqlCaretExpectations.NamedTermStart;
        }

        //OpenFrames lists the frames innermost-first; EnclosingProductions runs outermost-to-innermost.
        ParseFrameKind[] enclosing = new ParseFrameKind[openFrames.Count];
        for(int i = 0; i < openFrames.Count; i++)
        {
            enclosing[i] = openFrames[openFrames.Count - 1 - i].Kind;
        }

        //The variables in scope are those bound in the query parsed up to the caret. Because the prefix ends
        //at the caret, the §18.2.1 in-scope set of its WHERE pattern is exactly the preceding bindings a
        //FILTER or BIND at the caret may reference; a recovering parse yields the pattern even mid-edit. The
        //same pattern gives the variable→predicate pairs a store-backed resolver turns into datatypes.
        IReadOnlyList<ScopeVariable> inScopeVariables = [];
        IReadOnlyList<VariablePredicate> variablePredicates = [];
        if(SparqlParser.ParseRequest(prefix, effectivePool, baseIri).Tree is SparqlQuery query)
        {
            HashSet<SparqlVariable> bound = SparqlScopeAnalyzer.InScopeVariables(query.Where.Pattern);
            ScopeVariable[] scope = new ScopeVariable[bound.Count];
            int slot = 0;
            foreach(SparqlVariable variable in bound)
            {
                scope[slot] = new ScopeVariable(variable, Datatype: null, DatatypeSource.Unknown);
                slot++;
            }

            //The set comes from a hash set, so order it by name for a deterministic completion surface.
            Array.Sort(scope, static (left, right) => left.Variable.Name.CompareTo(right.Variable.Name));
            inScopeVariables = scope;
            variablePredicates = CollectVariablePredicates(query.Where.Pattern);
        }

        return new CompletionContext(caret, inScopeVariables, expectedTokens, enclosing, variablePredicates);
    }

    /// <summary>
    /// Collects the variable→predicate pairs of a graph pattern: for each triple whose predicate is a
    /// constant IRI, the subject and object variables paired with that predicate and their position. A
    /// property-path or variable predicate identifies no single property and contributes none; a sub-SELECT
    /// is its own scope. Duplicate pairs are collapsed, first-seen order kept.
    /// </summary>
    /// <param name="root">The graph pattern to walk.</param>
    /// <returns>The distinct variable→predicate pairs.</returns>
    private static List<VariablePredicate> CollectVariablePredicates(GraphPattern root)
    {
        List<VariablePredicate> predicates = [];
        HashSet<VariablePredicate> seen = [];
        Stack<GraphPattern> patterns = new();
        patterns.Push(root);

        while(patterns.Count > 0)
        {
            GraphPattern pattern = patterns.Pop();
            if(pattern is BasicGraphPatternBlock block)
            {
                foreach(TriplePattern triple in block.Triples)
                {
                    AddTriplePredicates(triple, predicates, seen);
                }

                continue;
            }

            //The patterns that nest triples; a sub-SELECT is its own scope and MINUS / FILTER / BIND /
            //VALUES bind no variable→predicate pair, so both contribute no children.
            IReadOnlyList<GraphPattern> children = pattern switch
            {
                GroupGraphPattern group => group.Members,
                OptionalPattern optional => [optional.Inner],
                UnionPattern union => [union.Left, union.Right],
                GraphGraphPattern graph => [graph.Inner],
                ServicePattern service => [service.Inner],
                _ => []
            };
            foreach(GraphPattern child in children)
            {
                patterns.Push(child);
            }
        }

        return predicates;
    }

    /// <summary>
    /// Adds the subject and object variable→predicate pairs of a triple whose predicate is a constant IRI,
    /// skipping any already collected. A path or variable predicate is skipped — it names no single property.
    /// </summary>
    /// <param name="triple">The triple pattern.</param>
    /// <param name="predicates">The accumulating pair list.</param>
    /// <param name="seen">The pairs already collected, to collapse duplicates.</param>
    private static void AddTriplePredicates(TriplePattern triple, List<VariablePredicate> predicates, HashSet<VariablePredicate> seen)
    {
        if(triple.Predicate is not ConstantTerm { Term: NamedNode predicate })
        {
            return;
        }

        if(triple.Subject is VariableTerm subject)
        {
            VariablePredicate pair = new(subject.Variable, predicate.Iri, TermPosition.Subject);
            if(seen.Add(pair))
            {
                predicates.Add(pair);
            }
        }

        //An object may carry an RDF 1.2 annotation wrapper; the bound variable is its inner object.
        TriplePatternTerm objectTerm = triple.Object is AnnotatedObject annotated ? annotated.Object : triple.Object;
        if(objectTerm is VariableTerm boundObject)
        {
            VariablePredicate pair = new(boundObject.Variable, predicate.Iri, TermPosition.Object);
            if(seen.Add(pair))
            {
                predicates.Add(pair);
            }
        }
    }
}
