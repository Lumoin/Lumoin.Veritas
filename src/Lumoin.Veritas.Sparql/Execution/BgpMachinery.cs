using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Results;
using AstTriplePattern = Lumoin.Veritas.Sparql.Ast.TriplePattern;
using AstTripleTerm = Lumoin.Veritas.Sparql.Ast.TripleTerm;
using EncodedTriplePattern = Lumoin.Veritas.Core.Hypertrie.Query.TriplePattern;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The basic-graph-pattern leaf machinery shared by the materialising executor and the streaming operator
/// pipeline: pattern encoding into the backend query space, the type-expansion plan, the per-solution rewrites
/// (self-join equalities, triple-term destructuring), backend source selection (batched columns or per-row
/// solutions, default-graph or named-graph routed), the columnar result builder, solution decoding, and the
/// access-guarded match operations. One instance is constructed per <see cref="SparqlQueryEngine"/> and carries
/// exactly the engine state the BGP path reads.
/// </summary>
internal sealed class BgpMachinery
{
    private readonly SparqlDataset dataset;

    private readonly TermDictionary dictionary;

    private readonly AccessControlDelegate? accessControl;

    private readonly AccessContext? accessContext;

    private readonly TimeProvider timeProvider;

    /// <summary>The query-time type-expansion seam, or <c>null</c> for no expansion.</summary>
    private TypeExpansionDelegate? TypeExpansion { get; }

    /// <summary>The <c>rdf:type</c> predicate node the expansion seam keys off.</summary>
    private static NamedNode RdfTypeNode { get; } = new(Vocabulary.Rdf.Type);

    /// <summary>The term dictionary the machinery encodes and decodes through.</summary>
    internal TermDictionary Dictionary => dictionary;

    /// <summary>The dataset whose graphs the machinery queries.</summary>
    internal SparqlDataset Dataset => dataset;

    /// <summary>The clock threaded into every backend source and trace sink this machinery opens.</summary>
    internal TimeProvider Time => timeProvider;

    /// <summary>Constructs the machinery over the owning engine's dataset, dictionary, and seams.</summary>
    /// <param name="dataset">The dataset the BGP leaves query.</param>
    /// <param name="dictionary">The term dictionary that encoded the dataset's graphs.</param>
    /// <param name="accessControl">The access-control policy consulted per candidate triple, or <see langword="null"/> to allow every triple.</param>
    /// <param name="accessContext">The opaque "who is asking" context handed to <paramref name="accessControl"/>; <see langword="null"/> when the evaluation carries no access context.</param>
    /// <param name="typeExpansion">The query-time type-expansion seam, or <see langword="null"/> for no expansion.</param>
    /// <param name="timeProvider">The caller-provided clock threaded into every backend source this machinery opens.</param>
    public BgpMachinery(
        SparqlDataset dataset,
        TermDictionary dictionary,
        AccessControlDelegate? accessControl,
        AccessContext? accessContext,
        TypeExpansionDelegate? typeExpansion,
        TimeProvider timeProvider)
    {
        this.dataset = dataset;
        this.dictionary = dictionary;
        this.accessControl = accessControl;
        this.accessContext = accessContext;
        TypeExpansion = typeExpansion;
        this.timeProvider = timeProvider;
    }

    /// <summary>
    /// The backend encoding of a BGP leaf: the encoded patterns and their registry (the backend query), the
    /// backend→SPARQL projection map, and the per-solution rewrites the evaluation must apply — triple-term
    /// destructurings and per-pattern self-join equalities. <see cref="Encodable"/> is <see langword="false"/>
    /// when a constant term is absent from the data graph, where the BGP yields nothing.
    /// </summary>
    internal sealed class EncodedBgp
    {
        /// <summary>Whether every constant term encoded; <see langword="false"/> means the BGP yields nothing.</summary>
        public bool Encodable { get; init; }

        /// <summary>The encoded patterns, in AST order.</summary>
        public List<EncodedTriplePattern> Patterns { get; init; } = [];

        /// <summary>The backend variable registry the patterns are registered in.</summary>
        public VariableRegistry Registry { get; init; } = new();

        /// <summary>The backend-variable to SPARQL-variable projection map, in encounter order.</summary>
        public Dictionary<Variable, SparqlVariable> ToSparql { get; init; } = [];

        /// <summary>The variable-bearing triple-term destructurings applied per solution; empty in the common case.</summary>
        public List<TripleTermMatch> TripleTermMatches { get; init; } = [];

        /// <summary>The within-pattern variable-repeat equalities checked per solution; empty in the common case.</summary>
        public List<(Variable Original, Variable Fresh)> SelfJoinEqualities { get; init; } = [];
    }

    /// <summary>
    /// Encodes a BGP's AST patterns into the backend query space: constant terms to ids, SPARQL variables to
    /// backend variables, blank nodes to non-distinguished (existential) join variables absent from the
    /// projection map, variable-bearing triple-term positions to fresh variables plus destructuring tasks, and
    /// within-pattern variable repeats (<c>?x :q ?x</c>) to fresh variables plus post-join equalities (the
    /// <c>#</c> prefix cannot collide with a SPARQL variable name). Not encodable when a constant term is
    /// absent from the data graph — such a pattern can never match, so the BGP yields nothing.
    /// </summary>
    /// <param name="bgp">The BGP leaf.</param>
    /// <returns>The encoded form.</returns>
    internal EncodedBgp EncodeBgp(Bgp bgp)
    {
        VariableRegistry registry = new();
        Dictionary<SparqlVariable, Variable> toBackend = [];
        Dictionary<Variable, SparqlVariable> toSparql = [];
        Dictionary<Utf8String, Variable> blankToBackend = [];
        List<EncodedTriplePattern> patterns = new(bgp.Patterns.Count);
        List<TripleTermMatch> tripleTermMatches = [];
        List<(Variable Original, Variable Fresh)> selfJoinEqualities = [];

        foreach(AstTriplePattern triple in bgp.Patterns)
        {
            if(!TryEncodePosition(triple.Subject, registry, toBackend, toSparql, blankToBackend, tripleTermMatches, out PatternPosition subject)
                || !TryEncodePosition(triple.Predicate, registry, toBackend, toSparql, blankToBackend, tripleTermMatches, out PatternPosition predicate)
                || !TryEncodePosition(triple.Object, registry, toBackend, toSparql, blankToBackend, tripleTermMatches, out PatternPosition @object))
            {
                return new EncodedBgp { Encodable = false, Registry = registry };
            }

            if(predicate.IsVariable && subject.IsVariable && predicate.Variable == subject.Variable)
            {
                predicate = PatternPosition.OfVariable(FreshSelfJoinVariable(registry, selfJoinEqualities, predicate.Variable));
            }

            if(@object.IsVariable && subject.IsVariable && @object.Variable == subject.Variable)
            {
                @object = PatternPosition.OfVariable(FreshSelfJoinVariable(registry, selfJoinEqualities, @object.Variable));
            }
            else if(@object.IsVariable && predicate.IsVariable && @object.Variable == predicate.Variable)
            {
                @object = PatternPosition.OfVariable(FreshSelfJoinVariable(registry, selfJoinEqualities, @object.Variable));
            }

            patterns.Add(new EncodedTriplePattern(subject, predicate, @object));
        }

        return new EncodedBgp
        {
            Encodable = true,
            Patterns = patterns,
            Registry = registry,
            ToSparql = toSparql,
            TripleTermMatches = tripleTermMatches,
            SelfJoinEqualities = selfJoinEqualities,
        };
    }

    /// <summary>Registers the internal stand-in for a repeated variable and records the equality the post-join filter checks.</summary>
    /// <param name="registry">The registry minting backend variables.</param>
    /// <param name="equalities">The accumulating equality list; the new pair is appended.</param>
    /// <param name="original">The repeated variable the stand-in must equal.</param>
    /// <returns>The fresh stand-in variable.</returns>
    private static Variable FreshSelfJoinVariable(VariableRegistry registry, List<(Variable Original, Variable Fresh)> equalities, Variable original)
    {
        Variable fresh = registry.GetOrAdd($"#self{equalities.Count}");
        equalities.Add((original, fresh));

        return fresh;
    }

    /// <summary>
    /// Encodes one triple-pattern term to a backend <see cref="PatternPosition"/>: a variable to a registered
    /// <see cref="Variable"/>, a constant to its bound <see cref="TermId"/>, a ground quoted triple term to its one
    /// interned <see cref="TermId"/>, and a variable-bearing quoted triple term to a fresh join variable plus a
    /// destructuring task appended to <paramref name="tripleTermMatches"/>.
    /// </summary>
    /// <param name="term">The term to encode.</param>
    /// <param name="registry">The registry minting backend variables for this pattern.</param>
    /// <param name="toBackend">The accumulating SPARQL-variable to backend-variable map.</param>
    /// <param name="toSparql">The accumulating backend-variable to SPARQL-variable map (for decoding).</param>
    /// <param name="blankToBackend">The accumulating blank-node-label to backend-variable map (blank nodes are non-projected join variables).</param>
    /// <param name="tripleTermMatches">The accumulating list of post-join destructuring tasks; a variable-bearing quoted triple term appends one.</param>
    /// <param name="position">Receives the encoded position on success.</param>
    /// <returns><see langword="true"/> when the term encodes to a matchable position; <see langword="false"/> when a constant (or ground triple term) is absent from the data graph.</returns>
    /// <exception cref="NotSupportedException">The term is none of a constant, a variable, or a quoted triple term (e.g. a property path reached this position).</exception>
    private bool TryEncodePosition(
        TriplePatternTerm term,
        VariableRegistry registry,
        Dictionary<SparqlVariable, Variable> toBackend,
        Dictionary<Variable, SparqlVariable> toSparql,
        Dictionary<Utf8String, Variable> blankToBackend,
        List<TripleTermMatch> tripleTermMatches,
        out PatternPosition position)
    {
        switch(term)
        {
            case VariableTerm variableTerm:
            {
                position = PatternPosition.OfVariable(GetOrAddVariable(variableTerm.Variable, registry, toBackend, toSparql));

                return true;
            }

            //A blank node in a query pattern is a non-distinguished join variable, not a ground value: occurrences
            //of the same label join, but it is never projected (it is not added to `toSparql`).
            case ConstantTerm { Term: BlankNode blank }:
            {
                position = PatternPosition.OfVariable(GetOrAddBlankVariable(blank.Label, registry, blankToBackend));

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

            case AstTripleTerm tripleTerm when TryBuildGroundTripleTerm(tripleTerm, out Core.TripleTerm ground):
            {
                //A fully-ground quoted triple term (no variables inside) is a single RDF 1.2 triple term value:
                //encode it as one bound TermId, exactly as the data-graph triple term was interned, so it matches by
                //value.
                TermId id = dictionary.GetIdOrDefault(ground);
                if(id.IsNone)
                {
                    position = default;

                    return false;
                }

                position = PatternPosition.Bound(id);

                return true;
            }

            case AstTripleTerm tripleTerm:
            {
                //A quoted triple term with a variable (or blank-node join variable) inside cannot encode to one
                //bound value. Encode the position as a fresh internal join variable the leapfrog binds to whatever
                //triple-term value occupies it, and record a destructuring task that unifies the term's components
                //against the solution after the join. The target is never recorded in `toSparql`, so it is a join
                //variable that is not projected.
                Variable target = registry.GetOrAdd("<<tt " + tripleTermMatches.Count.ToString(CultureInfo.InvariantCulture) + ">>");
                TripleComponent pattern = BuildTripleTermComponent(tripleTerm, registry, toBackend, toSparql, blankToBackend);
                tripleTermMatches.Add(new TripleTermMatch(target, pattern));
                position = PatternPosition.OfVariable(target);

                return true;
            }

            default:
            {
                throw new NotSupportedException($"Triple-pattern term '{term.GetType().Name}' is not executable; this engine handles constants, variables, and quoted triple terms.");
            }
        }
    }

    /// <summary>
    /// Builds the RDF 1.2 <see cref="Core.TripleTerm"/> value of a quoted triple term whose every position is a
    /// constant (its subject may itself be a ground quoted triple term), walking the nesting over an explicit stack
    /// (no recursion). Returns <see langword="false"/> when any position is a variable or otherwise non-constant —
    /// such a pattern needs structural matching rather than a single encoded value.
    /// </summary>
    /// <param name="tripleTerm">The quoted triple term.</param>
    /// <param name="ground">Receives the ground triple-term value on success.</param>
    /// <returns><see langword="true"/> when the quoted triple term is fully ground.</returns>
    private static bool TryBuildGroundTripleTerm(AstTripleTerm tripleTerm, out Core.TripleTerm ground)
    {
        ground = null!;

        //Post-order build: a position is either a constant (its RdfTerm taken directly), or a nested quoted triple
        //term (built first, then used as this term's subject/object). A predicate must resolve to a NamedNode.
        Dictionary<AstTripleTerm, Core.TripleTerm> built = new(ReferenceEqualityComparer.Instance);
        Stack<(AstTripleTerm Term, bool Build, int Depth)> work = new();
        work.Push((tripleTerm, Build: false, Depth: 1));

        while(work.Count > 0)
        {
            (AstTripleTerm term, bool build, int depth) = work.Pop();
            AstTriplePattern inner = term.Inner;

            if(!build)
            {
                if(depth > QuotedTripleLimits.MaxNestingDepth)
                {
                    throw new TripleTermDepthLimitException(depth, QuotedTripleLimits.MaxNestingDepth);
                }

                work.Push((term, Build: true, depth));

                //Only a nested quoted triple term needs to be built before this one; constants are read in place.
                if(inner.Subject is AstTripleTerm nestedSubject)
                {
                    work.Push((nestedSubject, Build: false, depth + 1));
                }

                if(inner.Object is AstTripleTerm nestedObject)
                {
                    work.Push((nestedObject, Build: false, depth + 1));
                }

                continue;
            }

            if(!TryGroundPosition(inner.Subject, built, out RdfTerm subjectTerm)
                || inner.Predicate is not ConstantTerm { Term: NamedNode predicateNode }
                || !TryGroundPosition(inner.Object, built, out RdfTerm objectTerm))
            {
                return false;
            }

            built[term] = new Core.TripleTerm(subjectTerm, predicateNode, objectTerm);
        }

        ground = built[tripleTerm];

        return true;
    }

    /// <summary>Resolves a quoted-triple-term inner position to its ground RDF term: a constant's term, or an already-built nested triple term; fails on a variable or other non-ground term.</summary>
    /// <param name="position">The inner position.</param>
    /// <param name="built">The map of already-built nested triple terms.</param>
    /// <param name="term">Receives the ground term on success.</param>
    /// <returns><see langword="true"/> when the position is ground.</returns>
    private static bool TryGroundPosition(TriplePatternTerm position, Dictionary<AstTripleTerm, Core.TripleTerm> built, out RdfTerm term)
    {
        term = position switch
        {
            //A blank node in a query triple term is a non-distinguished join variable, not a ground value, so a
            //triple term containing one is NOT ground — it falls through to structural (destructuring) matching.
            ConstantTerm { Term: BlankNode } => null!,
            ConstantTerm constant => constant.Term,
            AstTripleTerm nested => built[nested],
            _ => null!
        };

        return term is not null;
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

    /// <summary>
    /// Returns the backend join variable for a query blank node, minting one per distinct label on first sight.
    /// Unlike <see cref="GetOrAddVariable"/> it records no entry in the backend→SPARQL map, so the blank node joins
    /// across its occurrences but is never decoded into the output solution (a blank node is non-distinguished).
    /// </summary>
    /// <param name="label">The blank node's label.</param>
    /// <param name="registry">The registry minting backend variables.</param>
    /// <param name="blankToBackend">The blank-label to backend-variable map.</param>
    /// <returns>The backend variable for the blank node.</returns>
    private static Variable GetOrAddBlankVariable(Utf8String label, VariableRegistry registry, Dictionary<Utf8String, Variable> blankToBackend)
    {
        if(blankToBackend.TryGetValue(label, out Variable existing))
        {
            return existing;
        }

        //A label that can never collide with a real SPARQL variable name (which never contains a space).
        Variable backend = registry.GetOrAdd("_:bnode " + label.ToString());
        blankToBackend[label] = backend;

        return backend;
    }

    /// <summary>
    /// Builds the destructuring pattern for a variable-bearing quoted triple term: a tree of
    /// <see cref="TripleTermComponent"/> mirroring the term, with each leaf a backend variable (for a query variable
    /// or blank node) or a constant RDF term. Walks the nesting over an explicit stack (no recursion), reusing the
    /// pattern's backend-variable maps so a component variable joins its other occurrences.
    /// </summary>
    /// <param name="root">The quoted triple term (known to contain at least one variable).</param>
    /// <param name="registry">The registry minting backend variables.</param>
    /// <param name="toBackend">The SPARQL-variable to backend-variable map.</param>
    /// <param name="toSparql">The backend-variable to SPARQL-variable map.</param>
    /// <param name="blankToBackend">The blank-node-label to backend-variable map.</param>
    /// <returns>The root triple-term component.</returns>
    private static TripleComponent BuildTripleTermComponent(
        AstTripleTerm root,
        VariableRegistry registry,
        Dictionary<SparqlVariable, Variable> toBackend,
        Dictionary<Variable, SparqlVariable> toSparql,
        Dictionary<Utf8String, Variable> blankToBackend)
    {
        Dictionary<AstTripleTerm, TripleComponent> built = new(ReferenceEqualityComparer.Instance);
        Stack<(AstTripleTerm Term, bool Build)> work = new();
        work.Push((root, Build: false));

        while(work.Count > 0)
        {
            (AstTripleTerm term, bool build) = work.Pop();
            AstTriplePattern inner = term.Inner;

            if(!build)
            {
                work.Push((term, Build: true));

                //Only a nested quoted triple term must be built before this one; the other positions read in place.
                if(inner.Subject is AstTripleTerm nestedSubject)
                {
                    work.Push((nestedSubject, Build: false));
                }

                if(inner.Object is AstTripleTerm nestedObject)
                {
                    work.Push((nestedObject, Build: false));
                }

                continue;
            }

            TripleTermComponent subject = ToTripleTermComponent(inner.Subject, built, registry, toBackend, toSparql, blankToBackend);
            TripleTermComponent predicate = ToTripleTermComponent(inner.Predicate, built, registry, toBackend, toSparql, blankToBackend);
            TripleTermComponent objectComponent = ToTripleTermComponent(inner.Object, built, registry, toBackend, toSparql, blankToBackend);
            built[term] = new TripleComponent(subject, predicate, objectComponent);
        }

        return built[root];
    }

    /// <summary>Maps one quoted-triple-term inner position to a <see cref="TripleTermComponent"/>: a variable / blank node to a (join) variable component, a constant to a constant component, a nested triple term to its already-built component.</summary>
    /// <param name="position">The inner position.</param>
    /// <param name="built">The map of already-built nested triple-term components.</param>
    /// <param name="registry">The registry minting backend variables.</param>
    /// <param name="toBackend">The SPARQL-variable to backend-variable map.</param>
    /// <param name="toSparql">The backend-variable to SPARQL-variable map.</param>
    /// <param name="blankToBackend">The blank-node-label to backend-variable map.</param>
    /// <returns>The component.</returns>
    /// <exception cref="NotSupportedException">The position is a property path (not valid inside a quoted triple term).</exception>
    private static TripleTermComponent ToTripleTermComponent(
        TriplePatternTerm position,
        Dictionary<AstTripleTerm, TripleComponent> built,
        VariableRegistry registry,
        Dictionary<SparqlVariable, Variable> toBackend,
        Dictionary<Variable, SparqlVariable> toSparql,
        Dictionary<Utf8String, Variable> blankToBackend)
    {
        return position switch
        {
            VariableTerm variableTerm => new VariableComponent(GetOrAddVariable(variableTerm.Variable, registry, toBackend, toSparql)),
            ConstantTerm { Term: BlankNode blank } => new VariableComponent(GetOrAddBlankVariable(blank.Label, registry, blankToBackend)),
            ConstantTerm constant => new ConstantComponent(constant.Term),
            AstTripleTerm nested => built[nested],
            _ => throw new NotSupportedException($"Quoted-triple-term component '{position.GetType().Name}' is not supported.")
        };
    }

    /// <summary>A post-join destructuring task for a variable-bearing quoted triple term: the fresh join variable the leapfrog binds to the matched triple-term value, and the component pattern to unify against that value.</summary>
    /// <param name="Target">The fresh backend join variable bound to the matched triple-term value.</param>
    /// <param name="Pattern">The triple-term component pattern to unify against the resolved value.</param>
    internal sealed record TripleTermMatch(Variable Target, TripleComponent Pattern);

    /// <summary>A node in a quoted-triple-term destructuring pattern: a constant term, a (join) variable, or a nested triple term.</summary>
    internal abstract record TripleTermComponent;

    /// <summary>A constant position in a triple-term pattern, matched by RDF-term equality.</summary>
    /// <param name="Term">The constant RDF term.</param>
    internal sealed record ConstantComponent(RdfTerm Term): TripleTermComponent;

    /// <summary>A variable (or blank-node join variable) position in a triple-term pattern, unified against the actual term.</summary>
    /// <param name="Variable">The backend join variable.</param>
    internal sealed record VariableComponent(Variable Variable): TripleTermComponent;

    /// <summary>A nested quoted triple term inside a triple-term pattern; the actual term must itself be a triple term, matched component-wise.</summary>
    /// <param name="Subject">The subject component.</param>
    /// <param name="Predicate">The predicate component.</param>
    /// <param name="Object">The object component.</param>
    internal sealed record TripleComponent(TripleTermComponent Subject, TripleTermComponent Predicate, TripleTermComponent Object): TripleTermComponent;

    /// <summary>Applies every triple-term destructuring task to a candidate solution: resolves each target's matched value and unifies its components, extending <paramref name="bindings"/> with fresh component bindings. Returns <see langword="false"/> (dropping the solution) when any target is unbound, is not a triple term, or has a component that does not unify.</summary>
    /// <param name="matches">The destructuring tasks for this pattern.</param>
    /// <param name="bindings">The candidate solution's backend bindings, extended in place on success.</param>
    /// <returns><see langword="true"/> when every task unifies.</returns>
    internal bool TryApplyTripleTermMatches(List<TripleTermMatch> matches, List<VariableBinding> bindings)
    {
        foreach(TripleTermMatch match in matches)
        {
            if(!TryLookupBinding(bindings, match.Target, out TermId boundId))
            {
                return false;
            }

            if(!TryUnifyTripleTermComponent(match.Pattern, dictionary.Resolve(boundId), bindings))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Unifies a <see cref="TripleTermComponent"/> pattern against an actual RDF term, binding fresh variable components and checking already-bound ones, descending nested triple terms over an explicit stack (no recursion). Returns <see langword="false"/> on the first mismatch.</summary>
    /// <param name="root">The component pattern.</param>
    /// <param name="rootActual">The actual term to unify against.</param>
    /// <param name="bindings">The bindings, extended in place when a variable component binds.</param>
    /// <returns><see langword="true"/> when the pattern unifies with the term.</returns>
    /// <remarks>The descent is bounded by the <see cref="TripleTermComponent"/> pattern's depth (it advances only where the pattern is a nested component), and the pattern is built from a parser-produced AST whose quoted-triple nesting is capped at <see cref="QuotedTripleLimits.MaxNestingDepth"/>; an over-deep actual term mismatches against the bottomed-out pattern rather than driving the stack, so no independent depth cap is required here.</remarks>
    private bool TryUnifyTripleTermComponent(TripleTermComponent root, RdfTerm rootActual, List<VariableBinding> bindings)
    {
        Stack<(TripleTermComponent Component, RdfTerm Actual)> work = new();
        work.Push((root, rootActual));

        while(work.Count > 0)
        {
            (TripleTermComponent component, RdfTerm actual) = work.Pop();
            bool unified = component switch
            {
                ConstantComponent constant => constant.Term.Equals(actual),
                VariableComponent variable => TryBindOrCheck(variable.Variable, actual, bindings),
                TripleComponent triple => TryDescendTripleComponent(triple, actual, work),
                _ => throw new InvalidOperationException($"Unexpected triple-term component '{component.GetType().Name}'.")
            };
            if(!unified)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Binds a variable component to the actual term's id when unbound, or checks equality with its existing binding. Interns the actual term (a no-op when already interned); equal RDF terms share an id, so the comparison is by value.</summary>
    /// <param name="variable">The backend variable.</param>
    /// <param name="actual">The actual term.</param>
    /// <param name="bindings">The bindings, extended in place when the variable is unbound.</param>
    /// <returns><see langword="true"/> when the variable was unbound (now bound) or already equals the actual term.</returns>
    private bool TryBindOrCheck(Variable variable, RdfTerm actual, List<VariableBinding> bindings)
    {
        TermId actualId = dictionary.GetOrAdd(actual);
        if(TryLookupBinding(bindings, variable, out TermId existing))
        {
            return existing == actualId;
        }

        bindings.Add(new VariableBinding(variable, actualId));

        return true;
    }

    /// <summary>Descends a nested triple-term component: when the actual term is itself a triple term, pushes its three component/term pairs onto <paramref name="work"/> for unification; otherwise the match fails.</summary>
    /// <param name="triple">The nested triple-term component.</param>
    /// <param name="actual">The actual term, which must be a <see cref="Core.TripleTerm"/> to match.</param>
    /// <param name="work">The unification work stack to push the component pairs onto.</param>
    /// <returns><see langword="true"/> when the actual term is a triple term (children pushed); <see langword="false"/> otherwise.</returns>
    private static bool TryDescendTripleComponent(TripleComponent triple, RdfTerm actual, Stack<(TripleTermComponent Component, RdfTerm Actual)> work)
    {
        if(actual is not Core.TripleTerm tripleTerm)
        {
            return false;
        }

        work.Push((triple.Subject, tripleTerm.Subject));
        work.Push((triple.Predicate, tripleTerm.Predicate));
        work.Push((triple.Object, tripleTerm.Object));

        return true;
    }

    /// <summary>Looks up a backend variable's bound value in a binding list.</summary>
    /// <param name="bindings">The bindings to scan.</param>
    /// <param name="variable">The backend variable.</param>
    /// <param name="value">Receives the bound value when found.</param>
    /// <returns><see langword="true"/> when the variable is bound.</returns>
    private static bool TryLookupBinding(List<VariableBinding> bindings, Variable variable, out TermId value)
    {
        foreach(VariableBinding binding in bindings)
        {
            if(binding.Variable == variable)
            {
                value = binding.Value;

                return true;
            }
        }

        value = default;

        return false;
    }

    /// <summary>
    /// The Seam Q expansion plan: for each pattern whose predicate is a
    /// bound <c>rdf:type</c> and whose object is a bound named class, the
    /// expansion classes that exist in this engine's dictionary (an absent
    /// class can match nothing). The delegate is consulted once per such
    /// pattern; an expansion of one class leaves the pattern alone.
    /// </summary>
    /// <param name="patterns">The encoded patterns.</param>
    /// <returns>The per-pattern alternative lists; empty when nothing expands.</returns>
    internal List<(int PatternIndex, List<TermId> Alternatives)> ComputeTypeExpansions(List<EncodedTriplePattern> patterns)
    {
        List<(int PatternIndex, List<TermId> Alternatives)> expansions = [];
        if(TypeExpansion is null)
        {
            return expansions;
        }

        TermId rdfType = dictionary.GetIdOrDefault(RdfTypeNode);
        if(rdfType == TermId.None)
        {
            return expansions;
        }

        for(int i = 0; i < patterns.Count; i++)
        {
            EncodedTriplePattern pattern = patterns[i];
            if(!pattern.Predicate.IsBound || pattern.Predicate.BoundTerm != rdfType || !pattern.Object.IsBound)
            {
                continue;
            }

            if(dictionary.Resolve(pattern.Object.BoundTerm) is not NamedNode boundClass)
            {
                continue;
            }

            List<TermId> alternatives = [];
            foreach(Utf8String expansionIri in TypeExpansion(boundClass.Iri))
            {
                TermId expanded = expansionIri == boundClass.Iri
                    ? pattern.Object.BoundTerm
                    : dictionary.GetIdOrDefault(new NamedNode(expansionIri));
                if(expanded != TermId.None && !alternatives.Contains(expanded))
                {
                    alternatives.Add(expanded);
                }
            }

            if(alternatives.Count > 1)
            {
                expansions.Add((i, alternatives));
            }
        }

        return expansions;
    }

    /// <summary>A deterministic identity key for a backend solution, for deduplication across expansion variants.</summary>
    /// <param name="solution">The backend solution.</param>
    /// <returns>The order-insensitive binding key.</returns>
    internal static string SolutionKey(Solution solution)
    {
        List<string> parts = new(solution.Bindings.Count);
        foreach(VariableBinding binding in solution.Bindings)
        {
            parts.Add($"{binding.Variable}={binding.Value.Encoded}");
        }

        parts.Sort(StringComparer.Ordinal);

        return string.Join("|", parts);
    }

    /// <summary>
    /// Builds the columnar backing of a basic graph pattern's result: one encoded-id column per projected variable,
    /// filled row by row from the backend solutions without decoding any term. The projected variables (and their
    /// column order) are taken from the pattern's backend→SPARQL map, in encounter order; a backend join variable
    /// the pattern does not project is dropped, and a row that does not bind a projected variable stores <c>0</c>
    /// (unbound) in its column.
    /// </summary>
    internal sealed class BgpColumnBuilder
    {
        private readonly List<SparqlVariable> schema;

        private readonly Dictionary<Variable, int> backendToColumn;

        private readonly List<uint>[] columns;

        private readonly uint[] rowScratch;

        private readonly TermDictionary dictionary;

        private int rowCount;

        /// <summary>The number of accepted rows so far — the leaf row cap's progress measure.</summary>
        public int RowCount => rowCount;

        /// <summary>Constructs a builder over the pattern's projected variables.</summary>
        /// <param name="toSparql">The backend-variable to SPARQL-variable map; its values, in iteration (encounter) order, are the result schema and its keys are the columns' backend variables.</param>
        /// <param name="dictionary">The term dictionary the encoded ids decode through at the boundary.</param>
        public BgpColumnBuilder(Dictionary<Variable, SparqlVariable> toSparql, TermDictionary dictionary)
        {
            schema = new List<SparqlVariable>(toSparql.Count);
            backendToColumn = new Dictionary<Variable, int>(toSparql.Count);
            foreach(KeyValuePair<Variable, SparqlVariable> entry in toSparql)
            {
                backendToColumn[entry.Key] = schema.Count;
                schema.Add(entry.Value);
            }

            columns = new List<uint>[schema.Count];
            for(int i = 0; i < columns.Length; i++)
            {
                columns[i] = [];
            }

            rowScratch = new uint[schema.Count];
            this.dictionary = dictionary;
        }

        /// <summary>Appends one row, writing each projected binding's encoded id into its column and <c>0</c> into the columns the row does not bind.</summary>
        /// <param name="bindings">The backend solution's bindings (possibly extended by triple-term destructuring); non-projected variables are ignored.</param>
        public void AppendRow(IReadOnlyList<VariableBinding> bindings)
        {
            Array.Clear(rowScratch, 0, rowScratch.Length);
            foreach(VariableBinding binding in bindings)
            {
                if(backendToColumn.TryGetValue(binding.Variable, out int column))
                {
                    rowScratch[column] = binding.Value.Encoded;
                }
            }

            for(int column = 0; column < columns.Length; column++)
            {
                columns[column].Add(rowScratch[column]);
            }

            rowCount++;
        }

        /// <summary>
        /// Appends a whole Core <see cref="SolutionBatch"/> column-wise, routing each batch column to its projected
        /// column by backend variable and leaving the columns the batch does not carry at <c>0</c> (unbound). The
        /// batched scan never materialised a per-row <see cref="Solution"/>, so no heap solution is allocated per
        /// scanned row.
        /// </summary>
        /// <param name="batch">The Core batch (its schema's variables are this BGP's backend variables).</param>
        public void AppendBatch(SolutionBatch batch)
        {
            int rows = batch.Count;
            if(rows == 0)
            {
                return;
            }

            int baseRow = rowCount;
            for(int column = 0; column < columns.Length; column++)
            {
                List<uint> target = columns[column];
                for(int row = 0; row < rows; row++)
                {
                    target.Add(0);
                }
            }

            IReadOnlyList<Variable> batchSchema = batch.Schema;
            for(int source = 0; source < batchSchema.Count; source++)
            {
                if(backendToColumn.TryGetValue(batchSchema[source], out int column))
                {
                    ReadOnlySpan<uint> values = batch.ColumnOf(source);
                    List<uint> target = columns[column];
                    for(int row = 0; row < rows; row++)
                    {
                        target[baseRow + row] = values[row];
                    }
                }
            }

            rowCount += rows;
        }

        /// <summary>Freezes the accumulated columns into a columnar <see cref="SolutionTable"/>.</summary>
        /// <returns>The columnar table; a schema-less table of <see cref="rowCount"/> empty rows for an all-constant pattern.</returns>
        public SolutionTable Build()
        {
            uint[][] frozen = new uint[columns.Length][];
            for(int column = 0; column < columns.Length; column++)
            {
                frozen[column] = [.. columns[column]];
            }

            return SolutionTable.Columnar(schema, frozen, rowCount, dictionary);
        }
    }

    /// <summary>Evaluates a basic graph pattern against the active graph: encodes it to the backend, runs the worst-case-optimal join engine over <paramref name="graphStore"/> — through <paramref name="rendezvous"/> when one applies, pinning the store so the engine choice never outruns this snapshot — and decodes each solution. Draining stops once <paramref name="maxRows"/> accepted rows are gathered (a LIMIT pushed to the leaf; the slice above trims the exact window), possibly overshooting by less than one batch on the batched path.</summary>
    /// <param name="bgp">The basic graph pattern.</param>
    /// <param name="graphStore">The active graph's store.</param>
    /// <param name="rendezvous">The default-graph rendezvous when the active graph is the default graph, else <see langword="null"/>.</param>
    /// <param name="activeGraph">The active graph, or <see cref="TermId.None"/> for the default graph.</param>
    /// <param name="maxRows">The accepted-row cap; <see cref="int.MaxValue"/> drains fully.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The BGP's solution sequence; empty when a constant in the pattern is absent from the active graph.</returns>
    internal async ValueTask<SolutionTable> EvaluateBgpAsync(Bgp bgp, HypertrieGraphStore? graphStore, QueryEngineRendezvous? rendezvous, TermId activeGraph, int maxRows, CancellationToken cancellationToken)
    {
        EncodedBgp encoded = EncodeBgp(bgp);
        if(!encoded.Encodable)
        {
            return SolutionTable.Empty;
        }

        List<EncodedTriplePattern> patterns = encoded.Patterns;
        BasicGraphPattern query = new(patterns, encoded.Registry);

        //Seam Q: a bound rdf:type pattern expands to its closure classes —
        //the BGP evaluates once per combination of per-pattern expansion
        //classes and the solutions union, deduplicated across variants
        //(an instance typed under two expansion classes matches twice).
        List<(int PatternIndex, List<TermId> Alternatives)> typeExpansions = ComputeTypeExpansions(patterns);

        //The BGP is the columnar island's leaf: each accepted backend solution is written as a row of encoded term
        //ids into one column per projected variable, never decoded here. Decoding to RDF terms happens once, at the
        //boundary that finally needs rows (see SolutionTable.AsRows).
        BgpColumnBuilder builder = new(encoded.ToSparql, dictionary);
        HashSet<string>? seenAcrossVariants = typeExpansions.Count > 0 ? [] : null;

        if(typeExpansions.Count == 0)
        {
            //Batched-scan fast path (default graph only): when the columnar pipeline applies, consume its
            //column-major batches straight into the builder, so no per-row backend Solution is materialised. The
            //per-solution rewrites (self-join equality, triple-term destructuring) have no columnar form, so a BGP
            //carrying either stays on the per-row path. A null result declines to that same per-row path.
            if(TryOpenBatchedColumns(query, graphStore, rendezvous, encoded, typeExpansions) is IEnumerable<SolutionBatch> batches)
            {
                foreach(SolutionBatch batch in batches)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    builder.AppendBatch(batch);

                    if(builder.RowCount >= maxRows)
                    {
                        break;
                    }
                }

                return builder.Build();
            }

            await DrainVariantAsync(query, graphStore, rendezvous, activeGraph, encoded, builder, seenAcrossVariants, maxRows, cancellationToken).ConfigureAwait(false);

            return builder.Build();
        }

        //The cartesian product over the per-pattern alternatives, walked
        //with explicit counters.
        int[] cursors = new int[typeExpansions.Count];

        while(true)
        {
            List<EncodedTriplePattern> variantPatterns = [.. patterns];
            for(int i = 0; i < typeExpansions.Count; i++)
            {
                (int patternIndex, List<TermId> alternatives) = typeExpansions[i];
                EncodedTriplePattern original = variantPatterns[patternIndex];
                variantPatterns[patternIndex] = new EncodedTriplePattern(original.Subject, original.Predicate, PatternPosition.Bound(alternatives[cursors[i]]));
            }

            await DrainVariantAsync(new BasicGraphPattern(variantPatterns, encoded.Registry), graphStore, rendezvous, activeGraph, encoded, builder, seenAcrossVariants, maxRows, cancellationToken).ConfigureAwait(false);

            if(builder.RowCount >= maxRows)
            {
                return builder.Build();
            }

            int advance = typeExpansions.Count - 1;
            while(advance >= 0 && ++cursors[advance] == typeExpansions[advance].Alternatives.Count)
            {
                cursors[advance] = 0;
                advance--;
            }

            if(advance < 0)
            {
                return builder.Build();
            }
        }
    }

    /// <summary>Drains one pattern variant's backend solutions into the builder, applying the per-solution rewrites in evaluation order: the accepted-row cap, the self-join equality filter, cross-variant deduplication, and triple-term destructuring.</summary>
    /// <param name="variant">The (possibly expansion-substituted) backend query.</param>
    /// <param name="graphStore">The active graph's store.</param>
    /// <param name="rendezvous">The default-graph rendezvous when the active graph is the default graph, else <see langword="null"/>.</param>
    /// <param name="activeGraph">The active graph, or <see cref="TermId.None"/> for the default graph.</param>
    /// <param name="encoded">The encoded BGP carrying the rewrites this drain applies.</param>
    /// <param name="builder">The columnar builder accepted rows are appended to.</param>
    /// <param name="seenAcrossVariants">The cross-variant dedup keys, or <see langword="null"/> when a single variant runs.</param>
    /// <param name="maxRows">The accepted-row cap; <see cref="int.MaxValue"/> drains fully.</param>
    /// <param name="cancellationToken">A token that aborts the drain.</param>
    /// <returns>A task completing when the variant is drained or the cap is met.</returns>
    private async ValueTask DrainVariantAsync(
        BasicGraphPattern variant,
        HypertrieGraphStore? graphStore,
        QueryEngineRendezvous? rendezvous,
        TermId activeGraph,
        EncodedBgp encoded,
        BgpColumnBuilder builder,
        HashSet<string>? seenAcrossVariants,
        int maxRows,
        CancellationToken cancellationToken)
    {
        List<(Variable Original, Variable Fresh)> selfJoinEqualities = encoded.SelfJoinEqualities;
        List<TripleTermMatch> tripleTermMatches = encoded.TripleTermMatches;
        bool hasTripleTermMatches = tripleTermMatches.Count > 0;

        await foreach(Solution solution in OpenRowSource(variant, graphStore, rendezvous, activeGraph, cancellationToken).ConfigureAwait(false))
        {
            if(builder.RowCount >= maxRows)
            {
                return;
            }

            if(selfJoinEqualities.Count > 0 && !SelfJoinHolds(solution, selfJoinEqualities))
            {
                continue;
            }

            if(seenAcrossVariants is not null && !seenAcrossVariants.Add(SolutionKey(solution)))
            {
                continue;
            }

            if(!hasTripleTermMatches)
            {
                builder.AppendRow(solution.Bindings);

                continue;
            }

            //Destructure each variable-bearing triple-term position: resolve the matched triple-term value and unify
            //its components against the solution (binding fresh component variables, checking already-bound ones).
            //A non-triple-term value or a component mismatch drops the solution.
            List<VariableBinding> bindings = [.. solution.Bindings];
            if(TryApplyTripleTermMatches(tripleTermMatches, bindings))
            {
                builder.AppendRow(bindings);
            }
        }
    }

    /// <summary>Whether every recorded self-join equality holds in the solution: both variables bound to the same term.</summary>
    /// <param name="solution">The backend solution.</param>
    /// <param name="equalities">The recorded within-pattern equalities.</param>
    /// <returns><see langword="true"/> when every equality holds.</returns>
    internal static bool SelfJoinHolds(Solution solution, List<(Variable Original, Variable Fresh)> equalities)
    {
        foreach((Variable original, Variable fresh) in equalities)
        {
            TermId originalValue = TermId.None;
            TermId freshValue = TermId.None;
            foreach(VariableBinding binding in solution.Bindings)
            {
                if(binding.Variable == original)
                {
                    originalValue = binding.Value;
                }
                else if(binding.Variable == fresh)
                {
                    freshValue = binding.Value;
                }
            }

            if(originalValue != freshValue || originalValue == TermId.None)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Opens the per-row backend solution source for one query variant against the active graph: the default
    /// graph routes through <paramref name="rendezvous"/> (which pins this snapshot's store and serves the warm
    /// view or materialises the trie on demand), a named graph routes through the graph-set rendezvous, and a
    /// named graph without a rendezvous queries its concrete store directly. Access control is consulted per
    /// candidate on every route.
    /// </summary>
    /// <param name="query">The backend query.</param>
    /// <param name="graphStore">The active graph's store; <see langword="null"/> only for the deferred default graph, where <paramref name="rendezvous"/> is non-null and answers around the (null) pinned store.</param>
    /// <param name="rendezvous">The default-graph rendezvous when the active graph is the default graph, else <see langword="null"/>.</param>
    /// <param name="activeGraph">The active graph, or <see cref="TermId.None"/> for the default graph.</param>
    /// <param name="cancellationToken">A token that aborts the enumeration.</param>
    /// <returns>The backend solutions.</returns>
    internal IAsyncEnumerable<Solution> OpenRowSource(BasicGraphPattern query, HypertrieGraphStore? graphStore, QueryEngineRendezvous? rendezvous, TermId activeGraph, CancellationToken cancellationToken)
    {
        //graphStore is null only for the deferred default graph, where rendezvous is non-null and graphStore is
        //the (null) pinned store the rendezvous answers around. The named-graph branches are reached only when
        //rendezvous is null, which happens only for a named graph — never deferred — so graphStore is non-null.
        return rendezvous is not null
            ? rendezvous.QueryAsync(graphStore, query, timeProvider, accessControl: accessControl, accessContext: accessContext, cancellationToken: cancellationToken)
            : !activeGraph.IsNone
                ? dataset.NamedGraphRendezvous.QueryAsync(
                    dataset.NamedGraphGeneration, activeGraph, graphStore!, query, timeProvider,
                    accessControl: accessControl, accessContext: accessContext, cancellationToken: cancellationToken)
                : graphStore!.QueryAsync(query, timeProvider, accessControl: accessControl, accessContext: accessContext, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Opens the batched column-major source for a query when the columnar pipeline applies: default graph only
    /// (a rendezvous is present), no per-solution rewrites (self-join equality and triple-term destructuring
    /// have no columnar form), no type expansion (each variant re-encodes the pattern), and the rendezvous
    /// accepts the shape. Returns <see langword="null"/> to decline to the per-row path.
    /// </summary>
    /// <param name="query">The backend query.</param>
    /// <param name="graphStore">The active graph's store (the pinned snapshot the rendezvous answers around).</param>
    /// <param name="rendezvous">The default-graph rendezvous, or <see langword="null"/> for a named graph (always declines).</param>
    /// <param name="encoded">The encoded BGP whose rewrites gate eligibility.</param>
    /// <param name="typeExpansions">The type-expansion plan; a non-empty plan declines.</param>
    /// <returns>The lazy batch sequence, or <see langword="null"/> when the batched path does not apply.</returns>
    internal IEnumerable<SolutionBatch>? TryOpenBatchedColumns(
        BasicGraphPattern query,
        HypertrieGraphStore? graphStore,
        QueryEngineRendezvous? rendezvous,
        EncodedBgp encoded,
        List<(int PatternIndex, List<TermId> Alternatives)> typeExpansions)
    {
        if(rendezvous is null
            || encoded.SelfJoinEqualities.Count > 0
            || encoded.TripleTermMatches.Count > 0
            || typeExpansions.Count > 0)
        {
            return null;
        }

        return rendezvous.TryQueryBatchedColumns(graphStore, query, timeProvider, accessControl) is (_, IEnumerable<SolutionBatch> batches)
            ? batches
            : null;
    }

    /// <summary>
    /// Decodes one streamed BGP solution into a SELECT solution: maps each backend variable to its SPARQL variable,
    /// resolves its term through the dictionary, drops unbound and non-distinguished bindings, and — when a
    /// projection is given — keeps only the projected variables.
    /// </summary>
    /// <param name="solution">The encoded BGP solution.</param>
    /// <param name="toSparql">The backend-variable to SPARQL-variable map (distinguished variables only).</param>
    /// <param name="keep">The projected variables to retain, or <see langword="null"/> to keep all distinguished variables.</param>
    /// <returns>The decoded solution.</returns>
    internal SparqlSolution DecodeStreamedSolution(Solution solution, Dictionary<Variable, SparqlVariable> toSparql, HashSet<SparqlVariable>? keep)
    {
        List<SparqlBinding> bindings = new(solution.Bindings.Count);
        foreach(VariableBinding binding in solution.Bindings)
        {
            if(binding.Value == TermId.None || !toSparql.TryGetValue(binding.Variable, out SparqlVariable sparqlVariable))
            {
                continue;
            }

            if(keep is not null && !keep.Contains(sparqlVariable))
            {
                continue;
            }

            bindings.Add(new SparqlBinding(sparqlVariable, dictionary.Resolve(binding.Value)));
        }

        return new SparqlSolution(bindings);
    }

    /// <summary>Decodes the bindings a triple-term-destructured (or otherwise rewritten) backend binding list produced, mirroring <see cref="DecodeStreamedSolution"/> over an explicit binding list.</summary>
    /// <param name="bindings">The backend bindings (possibly extended by destructuring).</param>
    /// <param name="toSparql">The backend-variable to SPARQL-variable map (distinguished variables only).</param>
    /// <returns>The decoded solution.</returns>
    internal SparqlSolution DecodeBindings(IReadOnlyList<VariableBinding> bindings, Dictionary<Variable, SparqlVariable> toSparql)
    {
        List<SparqlBinding> decoded = new(bindings.Count);
        foreach(VariableBinding binding in bindings)
        {
            if(binding.Value == TermId.None || !toSparql.TryGetValue(binding.Variable, out SparqlVariable sparqlVariable))
            {
                continue;
            }

            decoded.Add(new SparqlBinding(sparqlVariable, dictionary.Resolve(binding.Value)));
        }

        return new SparqlSolution(decoded);
    }

    /// <summary>Builds the solution sequence of an inline <c>VALUES</c> block: one solution per row, binding each variable to its row value and leaving <c>UNDEF</c> (a <see langword="null"/> entry) unbound.</summary>
    /// <param name="data">The inline-data block.</param>
    /// <returns>The block's solution sequence.</returns>
    internal static List<SparqlSolution> BuildTableSolutions(ValuesClause data)
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
    /// Wraps a store's match operations with the engine's access-control policy so the read paths that go through
    /// <see cref="GraphMatchOps"/> rather than the BGP <c>Query</c> — property paths
    /// (<see cref="PropertyPathEvaluator"/>) and <c>DESCRIBE</c> — are gated identically: each matched triple is
    /// consulted and only an <see cref="AccessDecision.Allow"/> keeps it (<see cref="AccessDecision.Deny"/> and
    /// <see cref="AccessDecision.NotFound"/> drop it, exactly as in a basic graph pattern). With no policy the
    /// operations are returned unchanged, so the common path pays nothing.
    /// </summary>
    /// <param name="ops">The store's unguarded match operations.</param>
    /// <returns>The same operations when there is no policy, otherwise an access-filtered wrapper.</returns>
    internal GraphMatchOps GuardMatchOps(GraphMatchOps ops)
    {
        if(accessControl is null)
        {
            return ops;
        }

        AccessGuardedMatchOps guarded = new(this, ops);

        return new GraphMatchOps(guarded.MatchTriples, guarded.MatchTriplesBySubjects, guarded.MatchTriplesByObjects);
    }

    /// <summary>
    /// Access-filters a store's three match operations, carrying the machinery and the unguarded operations as
    /// explicit state so the wrapped <see cref="GraphMatchOps"/> delegates are bound method groups rather than
    /// lambdas closing over the enclosing machinery and operations.
    /// </summary>
    /// <param name="machinery">The machinery whose access policy filters each matched triple.</param>
    /// <param name="inner">The store's unguarded match operations.</param>
    private sealed class AccessGuardedMatchOps(BgpMachinery machinery, GraphMatchOps inner)
    {
        /// <summary>The machinery whose access policy filters each matched triple.</summary>
        private BgpMachinery Machinery { get; } = machinery;

        /// <summary>The store's unguarded match operations.</summary>
        private GraphMatchOps Inner { get; } = inner;

        /// <summary>Matches triples through the inner operations, keeping only those the access policy allows.</summary>
        /// <param name="subject">The subject to match, or <see cref="TermId.None"/> for any subject.</param>
        /// <param name="predicate">The predicate to match, or <see cref="TermId.None"/> for any predicate.</param>
        /// <param name="object">The object to match, or <see cref="TermId.None"/> for any object.</param>
        /// <param name="cancellationToken">A token to cancel the enumeration.</param>
        /// <returns>The allowed matching triples.</returns>
        public IAsyncEnumerable<EncodedTriple> MatchTriples(TermId subject, TermId predicate, TermId @object, CancellationToken cancellationToken)
        {
            return Machinery.FilterByAccessAsync(Inner.MatchTriples(subject, predicate, @object, cancellationToken), cancellationToken);
        }

        /// <summary>Matches triples for a subject set under a bound predicate, keeping only those the access policy allows.</summary>
        /// <param name="subjects">The encoded subject identifiers to look up under <paramref name="predicate"/>.</param>
        /// <param name="predicate">The bound predicate to match.</param>
        /// <param name="object">The object to match, or <see cref="TermId.None"/> for any object.</param>
        /// <param name="cancellationToken">A token to cancel the enumeration.</param>
        /// <returns>The allowed matching triples.</returns>
        public IAsyncEnumerable<EncodedTriple> MatchTriplesBySubjects(ReadOnlyMemory<TermId> subjects, TermId predicate, TermId @object, CancellationToken cancellationToken)
        {
            return Machinery.FilterByAccessAsync(Inner.MatchTriplesBySubjects(subjects, predicate, @object, cancellationToken), cancellationToken);
        }

        /// <summary>Matches triples for an object set under a bound predicate, keeping only those the access policy allows.</summary>
        /// <param name="subject">The subject to match, or <see cref="TermId.None"/> for any subject.</param>
        /// <param name="predicate">The bound predicate to match.</param>
        /// <param name="objects">The encoded object identifiers to look up under <paramref name="predicate"/>.</param>
        /// <param name="cancellationToken">A token to cancel the enumeration.</param>
        /// <returns>The allowed matching triples.</returns>
        public IAsyncEnumerable<EncodedTriple> MatchTriplesByObjects(TermId subject, TermId predicate, ReadOnlyMemory<TermId> objects, CancellationToken cancellationToken)
        {
            return Machinery.FilterByAccessAsync(Inner.MatchTriplesByObjects(subject, predicate, objects, cancellationToken), cancellationToken);
        }
    }

    /// <summary>Yields only the matched triples the access-control policy allows; a <c>Deny</c> or <c>NotFound</c> drops the triple, mirroring the BGP gate.</summary>
    /// <param name="triples">The unguarded matched triples.</param>
    /// <param name="cancellationToken">A token that aborts enumeration and the access consultation.</param>
    /// <returns>The allowed triples.</returns>
    private async IAsyncEnumerable<EncodedTriple> FilterByAccessAsync(IAsyncEnumerable<EncodedTriple> triples, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach(EncodedTriple triple in triples.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            AccessDecision decision = await accessControl!(new AccessRequest(triple, accessContext!), cancellationToken).ConfigureAwait(false);
            if(decision == AccessDecision.Allow)
            {
                yield return triple;
            }
        }
    }
}
