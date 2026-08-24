using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// The <c>EXISTS</c>/<c>NOT EXISTS</c> battery for the compile-once-per-evaluation plan machinery: on/off
/// answer parity across negation, the active-graph flow under <c>GRAPH</c>, outer-binding compatibility
/// (absent and unbound shared variables), the no-seeding inner shapes (<c>OPTIONAL</c>/<c>MINUS</c>/
/// sub-<c>SELECT</c>), nesting, <c>BIND</c> and <c>OPTIONAL</c>-condition sites, the per-row cache
/// correctness pin, BOTH type-expansion seeding divergence pins (the object and predicate variants the
/// mechanical rewrite-set diff must decline), and the uniform nesting cap's two arms (the parser diagnostic
/// and the runtime defensive check).
/// </summary>
[TestClass]
internal sealed class ExistsBatteryTests
{
    /// <summary>The example-namespace prefix the test queries and data share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The <c>rdf:type</c> IRI for the expansion-ladder data.</summary>
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The on-mode policy under test.</summary>
    private static SparqlEnginePolicy StreamingOn { get; } = new(PreferStreamingOperators: true);

    /// <summary>The shared chain graph: a knows b knows c, one name on a, one name on c.</summary>
    private static DataTriple[] ChainGraph { get; } =
    [
        new DataTriple(Iri("a"), Iri("knows"), Iri("b")),
        new DataTriple(Iri("b"), Iri("knows"), Iri("c")),
        new DataTriple(Iri("a"), Iri("name"), Iri("aname")),
        new DataTriple(Iri("c"), Iri("name"), Iri("cname")),
    ];

    /// <summary><c>NOT EXISTS</c> negates the same primitive: per-row answers and survivor counts agree across modes.</summary>
    [TestMethod]
    public async Task NotExistsNegationAgreesAcrossModes()
    {
        await AssertParityAsync(ChainGraph, "SELECT * WHERE { ?s :knows ?o FILTER NOT EXISTS { ?o :knows ?z } }", expectedCount: 1).ConfigureAwait(false);
        await AssertParityAsync(ChainGraph, "SELECT * WHERE { ?s :knows ?o FILTER EXISTS { ?o :knows ?z } }", expectedCount: 1).ConfigureAwait(false);
    }

    /// <summary><c>EXISTS</c> under a <c>GRAPH</c> form evaluates in the enclosing active graph: an inner pattern present only in the named graph answers true, one present only in the default graph answers false.</summary>
    [TestMethod]
    public async Task ExistsUnderGraphFlowsTheActiveGraph()
    {
        DataTriple[] defaultGraph = [new DataTriple(Iri("a"), Iri("p"), Iri("b"))];
        (RdfTerm Name, IEnumerable<DataTriple> Triples)[] named =
        [
            (Iri("g"), new[] { new DataTriple(Iri("a"), Iri("q"), Iri("c")) }),
        ];

        SparqlQueryEngine off = await SparqlQueryEngine.BuildDatasetAsync(defaultGraph, named, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        SparqlQueryEngine on = await SparqlQueryEngine.BuildDatasetAsync(defaultGraph, named, enginePolicy: StreamingOn, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        AlgebraOperator inNamed = Translate("ASK { GRAPH :g { ?s :q ?o FILTER EXISTS { ?s :q ?o2 } } }", pool);
        AlgebraOperator inDefaultOnly = Translate("ASK { GRAPH :g { ?s :q ?o FILTER EXISTS { ?s :p ?x } } }", pool);

        Assert.IsTrue(await off.EvaluateAskAsync(inNamed, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsTrue(await on.EvaluateAskAsync(inNamed, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await off.EvaluateAskAsync(inDefaultOnly, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await on.EvaluateAskAsync(inDefaultOnly, TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>Outer bindings act by compatibility, never constraint: a pre-binding variable absent from the inner pattern constrains nothing, and an UNBOUND shared variable is compatible with every inner row.</summary>
    [TestMethod]
    public async Task OuterBindingsApplyByCompatibility()
    {
        //?o is absent from the inner pattern: only the outer rows whose ?s has a name survive.
        await AssertParityAsync(ChainGraph, "SELECT * WHERE { ?s :knows ?o FILTER EXISTS { ?s :name ?n } }", expectedCount: 1).ConfigureAwait(false);

        //Row (b,c) leaves ?n unbound through the OPTIONAL; unbound shared variables are compatible with
        //every inner row, so both rows survive against an inner pattern with any name row at all.
        await AssertParityAsync(ChainGraph, "SELECT * WHERE { ?s :knows ?o OPTIONAL { ?s :name ?n } FILTER EXISTS { ?x :name ?n } }", expectedCount: 2).ConfigureAwait(false);

        //No :nick rows exist, so the inner is empty and even the unbound-?n rows fail.
        await AssertParityAsync(ChainGraph, "SELECT * WHERE { ?s :knows ?o OPTIONAL { ?s :nick ?n } FILTER EXISTS { ?x :nick ?n } }", expectedCount: 0).ConfigureAwait(false);
    }

    /// <summary>The no-seeding inner shapes — <c>OPTIONAL</c>, <c>MINUS</c>, and a sub-<c>SELECT</c> — take the compatibility path and agree with off-mode.</summary>
    [TestMethod]
    public async Task NoSeedingInnerShapesAgreeAcrossModes()
    {
        await AssertParityAsync(ChainGraph, "SELECT * WHERE { ?s :knows ?o FILTER EXISTS { ?s :knows ?m OPTIONAL { ?m :name ?mn } } }", expectedCount: 2).ConfigureAwait(false);

        //MINUS removes inner rows whose ?m carries a name: a knows b (b nameless — kept), b knows c
        //(c named — removed), so only the (a,b) outer row finds a compatible inner row.
        await AssertParityAsync(ChainGraph, "SELECT * WHERE { ?s :knows ?o FILTER EXISTS { ?s :knows ?m MINUS { ?m :name ?x2 } } }", expectedCount: 1).ConfigureAwait(false);

        await AssertParityAsync(ChainGraph, "SELECT * WHERE { ?s :knows ?o FILTER EXISTS { { SELECT ?m WHERE { ?m :knows ?z } } } }", expectedCount: 2).ConfigureAwait(false);
    }

    /// <summary>
    /// The probe re-arm crosses bindings in BOTH directions: the second outer binding needs a left row the
    /// first binding's early-stopped pull already consumed, so a Reset that fails to re-arm the streamed
    /// (left) side answers false where true is required. The blind-certified killer for the
    /// Reset-retains-streamed-state mutation family.
    /// </summary>
    [TestMethod]
    public async Task ResetRearmsTheOptionalProbeAcrossBindings()
    {
        //THREE outer bindings with a repeated subject: whatever order the filter processes them in, some
        //later binding needs a probe left row an earlier binding's early-stopped pull already consumed, so
        //a Reset that fails to re-arm the streamed (left) side answers false somewhere — order-robust by
        //construction (a monotone stream cannot serve an interleaved requirement without rewinding).
        DataTriple[] data =
        [
            new DataTriple(Iri("a"), Iri("probe"), Iri("x")),
            new DataTriple(Iri("b"), Iri("probe"), Iri("y")),
            new DataTriple(Iri("a"), Iri("probe"), Iri("z")),
            new DataTriple(Iri("a"), Iri("knows"), Iri("b")),
            new DataTriple(Iri("b"), Iri("knows"), Iri("c")),
            new DataTriple(Iri("b"), Iri("name"), Iri("nb")),
        ];

        await AssertParityAsync(data, "SELECT * WHERE { ?s :probe ?o FILTER EXISTS { ?s :knows ?m OPTIONAL { ?m :name ?n } } }", expectedCount: 3).ConfigureAwait(false);
    }

    /// <summary>Nested <c>EXISTS</c> resolves through per-level registries and agrees across modes.</summary>
    [TestMethod]
    public async Task NestedExistsAgreesAcrossModes()
    {
        await AssertParityAsync(ChainGraph, "SELECT * WHERE { ?s :knows ?o FILTER EXISTS { ?o :knows ?z FILTER EXISTS { ?z :name ?zn } } }", expectedCount: 1).ConfigureAwait(false);
    }

    /// <summary><c>EXISTS</c> in a <c>BIND</c> expression and in an <c>OPTIONAL</c>'s lifted condition agree across modes.</summary>
    [TestMethod]
    public async Task ExistsInBindAndLeftJoinConditionAgreesAcrossModes()
    {
        SparqlQueryEngine off = await SparqlQueryEngine.BuildAsync(ChainGraph, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(ChainGraph, enginePolicy: StreamingOn, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        AlgebraOperator bind = Translate("SELECT * WHERE { ?s :knows ?o BIND(EXISTS { ?o :knows ?z } AS ?e) }", pool);
        IReadOnlyList<SparqlSolution> offBind = await off.EvaluateAsync(bind, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> onBind = await on.EvaluateAsync(bind, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(2, offBind);
        Assert.HasCount(offBind.Count, onBind);
        Assert.AreEqual(BoundLiteral(offBind, "a", "e"), BoundLiteral(onBind, "a", "e"));
        Assert.AreEqual(BoundLiteral(offBind, "b", "e"), BoundLiteral(onBind, "b", "e"));
        Assert.AreEqual("true", BoundLiteral(onBind, "a", "e"));
        Assert.AreEqual("false", BoundLiteral(onBind, "b", "e"));

        AlgebraOperator optionalCondition = Translate("SELECT * WHERE { ?s :knows ?o OPTIONAL { ?o :knows ?z FILTER EXISTS { ?z :name ?zn } } }", pool);
        IReadOnlyList<SparqlSolution> offOptional = await off.EvaluateAsync(optionalCondition, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> onOptional = await on.EvaluateAsync(optionalCondition, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(offOptional.Count, onOptional);
    }

    /// <summary>The per-row cache correctness pin: one compiled site answers M rows with DIFFERENT truths — the compile-once plan must not leak one row's pre-binding into the next (off-mode: the rebuilt trailing-VALUES query; on-mode: the ResetAsync re-arm).</summary>
    [TestMethod]
    public async Task CompileOncePlanAnswersEachRowIndependently()
    {
        //Row (a,b): b knows c -> EXISTS true; row (b,c): c knows nothing -> false. The survivor set pins
        //per-row independence in both modes; a leaked pre-binding or stale reset flips one of them.
        SparqlQueryEngine off = await SparqlQueryEngine.BuildAsync(ChainGraph, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(ChainGraph, enginePolicy: StreamingOn, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("SELECT * WHERE { ?s :knows ?o FILTER EXISTS { ?o :knows ?z } }", pool);
        IReadOnlyList<SparqlSolution> offRows = await off.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> onRows = await on.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, offRows);
        Assert.HasCount(1, onRows);
        Assert.AreEqual(Ex + "a", ValueIri(offRows[0], "s"));
        Assert.AreEqual(Ex + "a", ValueIri(onRows[0], "s"));
    }

    /// <summary>
    /// The type-expansion OBJECT divergence pin (the seeding carve-out's keystone case): with the ladder
    /// active and the pre-binding naming the SUPERCLASS, seeding the inner <c>rdf:type</c> object would
    /// activate the subclass ladder today's trailing-VALUES path never triggers — the mechanical
    /// rewrite-set diff must decline, keeping the answer FALSE in both modes. The subclass-named binding is
    /// the positive control that exercises the seeded probe.
    /// </summary>
    [TestMethod]
    public async Task SeedingDeclinesTheTypeExpansionObjectFlip()
    {
        DataTriple[] data =
        [
            new DataTriple(Iri("rex"), new NamedNode(Utf8Strings.From(RdfType)), Iri("Dog")),
            new DataTriple(Iri("Dog"), Iri("subClassOf"), Iri("Animal")),
            new DataTriple(Iri("probeA"), Iri("points"), Iri("Animal")),
            new DataTriple(Iri("probeD"), Iri("points"), Iri("Dog")),
        ];
        SparqlQueryEngine off = await SparqlQueryEngine.BuildAsync(data, typeExpansion: ExpandAnimalToDog, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(data, typeExpansion: ExpandAnimalToDog, enginePolicy: StreamingOn, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        AlgebraOperator superclassBinding = Translate("ASK { :probeA :points ?t FILTER EXISTS { ?x a ?t } }", pool);
        AlgebraOperator subclassBinding = Translate("ASK { :probeD :points ?t FILTER EXISTS { ?x a ?t } }", pool);

        //No instance is typed :Animal directly; the uncorrected seeded design would answer true via the ladder.
        Assert.IsFalse(await off.EvaluateAskAsync(superclassBinding, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await on.EvaluateAskAsync(superclassBinding, TestContext.CancellationToken).ConfigureAwait(false));

        Assert.IsTrue(await off.EvaluateAskAsync(subclassBinding, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsTrue(await on.EvaluateAskAsync(subclassBinding, TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// The type-expansion PREDICATE divergence pin (the carve-out rule is general over positions): seeding
    /// a predicate variable to <c>rdf:type</c> over an already-bound class object would likewise activate
    /// the ladder — the diff must decline, keeping FALSE in both modes; the subclass-object sibling is the
    /// positive control (its patched encoding expands to nothing, so seeding proceeds).
    /// </summary>
    [TestMethod]
    public async Task SeedingDeclinesTheTypeExpansionPredicateFlip()
    {
        DataTriple[] data =
        [
            new DataTriple(Iri("rex"), new NamedNode(Utf8Strings.From(RdfType)), Iri("Dog")),
            new DataTriple(Iri("Dog"), Iri("subClassOf"), Iri("Animal")),
            new DataTriple(Iri("probeT"), Iri("points"), new NamedNode(Utf8Strings.From(RdfType))),
        ];
        SparqlQueryEngine off = await SparqlQueryEngine.BuildAsync(data, typeExpansion: ExpandAnimalToDog, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(data, typeExpansion: ExpandAnimalToDog, enginePolicy: StreamingOn, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        AlgebraOperator boundSuperclassObject = Translate("ASK { :probeT :points ?p FILTER EXISTS { ?x ?p :Animal } }", pool);
        AlgebraOperator boundSubclassObject = Translate("ASK { :probeT :points ?p FILTER EXISTS { ?x ?p :Dog } }", pool);

        Assert.IsFalse(await off.EvaluateAskAsync(boundSuperclassObject, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await on.EvaluateAskAsync(boundSuperclassObject, TestContext.CancellationToken).ConfigureAwait(false));

        Assert.IsTrue(await off.EvaluateAskAsync(boundSubclassObject, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsTrue(await on.EvaluateAskAsync(boundSubclassObject, TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// The compile-cache determinism pin: the cached-plan construction — normalize the
    /// synthetic <c>SELECT * { pattern }</c> ONCE, rebuild with the row's <c>VALUES</c>, translate — yields
    /// algebra structurally identical to a fresh per-row synthesize/normalize/translate, and both evaluate
    /// to the same rows. The deterministic normalizer/translator is what the per-site cache leans on.
    /// </summary>
    [TestMethod]
    public async Task CompileCacheDeterminismPin()
    {
        using Utf8StringPool pool = new();
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes("PREFIX : <http://example.org/> ASK { ?s :knows ?o FILTER EXISTS { ?o :knows ?z . ?o :name ?n } }"), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery outer = (SparqlQuery)parser.ParseRequest();
        ExistsExpression exists = FindExists(outer);

        //The pre-binding a mid-evaluation row would carry.
        ValuesClause values = new(SourceSpan.None, [new SparqlVariable(Utf8Strings.From("o"))], [[Iri("b")]]);
        SparqlQuery synthetic = new(
            SourceSpan.None,
            new Prologue(SourceSpan.None, [], [], []),
            new SelectQuery(SourceSpan.None, IsDistinct: false, IsReduced: false, IsStar: true, []),
            new DatasetClause(SourceSpan.None, [], []),
            new WhereClause(SourceSpan.None, exists.Inner),
            new SolutionModifier(SourceSpan.None, null, null, null, null, null),
            values);

        //Fresh per-row construction: normalize the WHOLE values-bearing query, then translate.
        AlgebraOperator fresh = SparqlTranslator.Translate((SparqlQuery)new SparqlNormalizer(pool).Normalize(synthetic));

        //Cached-plan construction: normalize WITHOUT the values once, rebuild per row, translate.
        SparqlQuery normalizedBase = (SparqlQuery)new SparqlNormalizer(pool).Normalize(synthetic with { Values = null });
        AlgebraOperator cached = SparqlTranslator.Translate(normalizedBase with { Values = values });

        AssertStructurallyIdentical(fresh, cached);

        DataTriple[] data =
        [
            new DataTriple(Iri("a"), Iri("knows"), Iri("b")),
            new DataTriple(Iri("b"), Iri("knows"), Iri("c")),
            new DataTriple(Iri("b"), Iri("name"), Iri("nb")),
        ];
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(data, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> freshRows = await engine.EvaluateAsync(fresh, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> cachedRows = await engine.EvaluateAsync(cached, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(1, freshRows);
        Assert.HasCount(freshRows.Count, cachedRows);
    }

    /// <summary>Finds the query's first <c>EXISTS</c> expression node.</summary>
    /// <param name="query">The parsed query.</param>
    /// <returns>The EXISTS node.</returns>
    private static ExistsExpression FindExists(SparqlQuery query)
    {
        GroupGraphPattern group = (GroupGraphPattern)query.Where.Pattern;
        foreach(GraphPattern member in group.Members)
        {
            if(member is FilterPattern { Expression: ExistsExpression exists })
            {
                return exists;
            }
        }

        throw new InvalidOperationException("The query carries no FILTER EXISTS member.");
    }

    /// <summary>Asserts two algebra trees are structurally identical: the same operator type at every position of a lockstep pre-order walk, with equal child counts, equal BGP pattern counts, and equal inline-table shapes.</summary>
    /// <param name="expected">The fresh-construction tree.</param>
    /// <param name="actual">The cached-construction tree.</param>
    private static void AssertStructurallyIdentical(AlgebraOperator expected, AlgebraOperator actual)
    {
        List<AlgebraOperator> expectedWalk = [.. AlgebraWalker.Traverse(expected)];
        List<AlgebraOperator> actualWalk = [.. AlgebraWalker.Traverse(actual)];
        Assert.HasCount(expectedWalk.Count, actualWalk, "the trees differ in size");
        for(int i = 0; i < expectedWalk.Count; i++)
        {
            Assert.AreEqual(expectedWalk[i].GetType(), actualWalk[i].GetType(), $"operator kind at walk position {i}");
            Assert.HasCount(expectedWalk[i].Children.Count, actualWalk[i].Children, $"child count at walk position {i}");
            if(expectedWalk[i] is Bgp expectedBgp && actualWalk[i] is Bgp actualBgp)
            {
                Assert.HasCount(expectedBgp.Patterns.Count, actualBgp.Patterns, $"BGP pattern count at walk position {i}");
            }

            if(expectedWalk[i] is Table expectedTable && actualWalk[i] is Table actualTable)
            {
                Assert.HasCount(expectedTable.Data.Variables.Count, actualTable.Data.Variables, $"table variable count at walk position {i}");
                Assert.HasCount(expectedTable.Data.Rows.Count, actualTable.Data.Rows, $"table row count at walk position {i}");
            }
        }
    }

    /// <summary>The parser arm of the uniform nesting cap: nesting exactly to the cap parses clean; one level past records <c>SP0053</c> and recovers.</summary>
    [TestMethod]
    public void ParserRecordsTheNestingDiagnosticPastTheCap()
    {
        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> atCap = SparqlParser.ParseRequest(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(NestedExistsQuery(SparqlTranslator.MaxExistsNestingDepth))), pool);
        Assert.IsFalse(atCap.HasErrors, "EXISTS nested exactly to the cap is legal and must parse clean.");
        Assert.IsFalse(HasCode(atCap, WellKnownDiagnostics.Sparql.ExistsNestingTooDeep));

        ParseResult<SparqlRequest> pastCap = SparqlParser.ParseRequest(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(NestedExistsQuery(SparqlTranslator.MaxExistsNestingDepth + 1))), pool);
        Assert.IsTrue(pastCap.HasErrors);
        Assert.IsTrue(HasCode(pastCap, WellKnownDiagnostics.Sparql.ExistsNestingTooDeep), "EXISTS nested past the cap must record the nesting diagnostic and recover.");
    }

    /// <summary>The runtime arm of the uniform nesting cap: programmatically-constructed algebra nested past the cap (which never passed the parser) raises the clean <see cref="NotSupportedException"/> contract in both modes instead of exhausting the stack.</summary>
    [TestMethod]
    public async Task RuntimeCapRefusesOverdeepProgrammaticNesting()
    {
        SparqlQueryEngine off = await SparqlQueryEngine.BuildAsync(ChainGraph, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(ChainGraph, enginePolicy: StreamingOn, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes("PREFIX : <http://example.org/> ASK { ?s :knows ?o }"), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery baseQuery = (SparqlQuery)parser.ParseRequest();
        GroupGraphPattern baseGroup = (GroupGraphPattern)baseQuery.Where.Pattern;

        //One EXISTS level past the cap, built directly on the AST: the innermost group is the plain triples
        //group, each wrapper is a group holding only FILTER(EXISTS { previous }), and the outermost filter
        //sits beside the triples so it evaluates over a real row.
        GraphPattern current = baseGroup;
        for(int depth = 0; depth < SparqlTranslator.MaxExistsNestingDepth; depth++)
        {
            current = new GroupGraphPattern(SourceSpan.None, [new FilterPattern(SourceSpan.None, new ExistsExpression(SourceSpan.None, current))]);
        }

        SparqlQuery overdeep = baseQuery with
        {
            Where = new WhereClause(SourceSpan.None, new GroupGraphPattern(SourceSpan.None, [baseGroup.Members[0], new FilterPattern(SourceSpan.None, new ExistsExpression(SourceSpan.None, current))])),
        };
        AlgebraOperator algebra = SparqlTranslator.Translate((SparqlQuery)new SparqlNormalizer(pool).Normalize(overdeep));

        await AssertNestingRefusedAsync(off, algebra).ConfigureAwait(false);
        await AssertNestingRefusedAsync(on, algebra).ConfigureAwait(false);
    }

    /// <summary>Asserts the over-deep algebra raises the clean nesting refusal.</summary>
    /// <param name="engine">The engine to evaluate on.</param>
    /// <param name="algebra">The over-deep algebra.</param>
    private async Task AssertNestingRefusedAsync(SparqlQueryEngine engine, AlgebraOperator algebra)
    {
        NotSupportedException? refused = null;
        try
        {
            await engine.EvaluateAskAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
        }
        catch(NotSupportedException ex)
        {
            refused = ex;
        }

        Assert.IsNotNull(refused, "the over-deep nesting must be refused, not evaluated");
        Assert.IsTrue(refused.Message.Contains("nesting", StringComparison.Ordinal), refused.Message);
    }

    /// <summary>Builds an <c>ASK</c> whose <c>FILTER EXISTS</c> chain nests to the given depth.</summary>
    /// <param name="depth">The EXISTS nesting depth.</param>
    /// <returns>The query text.</returns>
    private static string NestedExistsQuery(int depth)
    {
        StringBuilder builder = new("PREFIX : <http://example.org/> ASK { ?s :knows ?o ");
        for(int level = 0; level < depth; level++)
        {
            builder.Append("FILTER(EXISTS { ?s :knows ?o ");
        }

        for(int level = 0; level < depth; level++)
        {
            builder.Append("}) ");
        }

        builder.Append('}');

        return builder.ToString();
    }

    /// <summary>Runs one SELECT under both modes and asserts the survivor count and cross-mode agreement.</summary>
    /// <param name="data">The data graph.</param>
    /// <param name="query">The SELECT query (without the shared prefix).</param>
    /// <param name="expectedCount">The expected survivor count.</param>
    private async Task AssertParityAsync(DataTriple[] data, string query, int expectedCount)
    {
        SparqlQueryEngine off = await SparqlQueryEngine.BuildAsync(data, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(data, enginePolicy: StreamingOn, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate(query, pool);
        IReadOnlyList<SparqlSolution> offRows = await off.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> onRows = await on.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(expectedCount, offRows, $"Off-mode count for '{query}'.");
        Assert.HasCount(offRows.Count, onRows, $"On/off count divergence for '{query}'.");
    }

    /// <summary>Returns the lexical value the named variable is bound to in the solution whose <c>?s</c> is the given local name.</summary>
    /// <param name="solutions">The solutions.</param>
    /// <param name="subjectLocalName">The subject's example-namespace local name selecting the row.</param>
    /// <param name="variableName">The variable to read.</param>
    /// <returns>The bound literal's lexical value.</returns>
    private static string BoundLiteral(IReadOnlyList<SparqlSolution> solutions, string subjectLocalName, string variableName)
    {
        foreach(SparqlSolution solution in solutions)
        {
            if(solution.TryGetValue(new SparqlVariable(Utf8Strings.From("s")), out RdfTerm subject) && subject is NamedNode node && node.Iri.ToString() == Ex + subjectLocalName)
            {
                Assert.IsTrue(solution.TryGetValue(new SparqlVariable(Utf8Strings.From(variableName)), out RdfTerm value), $"Expected ?{variableName} bound on the {subjectLocalName} row.");

                return ((Literal)value).Value.ToString();
            }
        }

        throw new InvalidOperationException($"No solution with ?s = :{subjectLocalName}.");
    }

    /// <summary>Returns the IRI string the named variable is bound to, asserting it is bound to a named node.</summary>
    /// <param name="solution">The solution to read.</param>
    /// <param name="variableName">The variable name.</param>
    /// <returns>The bound IRI as a string.</returns>
    private static string ValueIri(SparqlSolution solution, string variableName)
    {
        Assert.IsTrue(solution.TryGetValue(new SparqlVariable(Utf8Strings.From(variableName)), out RdfTerm value), $"Expected ?{variableName} to be bound.");

        return ((NamedNode)value).Iri.ToString();
    }

    /// <summary>Whether the parse recorded a diagnostic with the given code.</summary>
    /// <param name="result">The parse result.</param>
    /// <param name="code">The diagnostic code.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool HasCode(ParseResult<SparqlRequest> result, Utf8String code)
    {
        foreach(Diagnostic diagnostic in result.Diagnostics)
        {
            if(diagnostic.Code.Equals(code))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The type-expansion seam mapping the Animal class onto itself plus Dog (the subclass ladder), and every other class onto itself.</summary>
    /// <param name="classIri">The bound class IRI.</param>
    /// <returns>The expansion classes.</returns>
    private static IReadOnlyCollection<Utf8String> ExpandAnimalToDog(Utf8String classIri)
    {
        return classIri.Equals(Utf8Strings.From(Ex + "Animal"))
            ? [Utf8Strings.From(Ex + "Animal"), Utf8Strings.From(Ex + "Dog")]
            : [classIri];
    }

    /// <summary>Builds an example-namespace IRI term from a local name.</summary>
    /// <param name="localName">The local name appended to the example prefix.</param>
    /// <returns>The named-node term.</returns>
    private static NamedNode Iri(string localName)
    {
        return new NamedNode(Utf8Strings.From(Ex + localName));
    }

    /// <summary>Parses, normalizes, and translates a query (the shared example prefix prepended) to its algebra.</summary>
    /// <param name="text">The query text without the prefix.</param>
    /// <param name="pool">The pool the parse interns into.</param>
    /// <returns>The translated algebra.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes("PREFIX : <http://example.org/> " + text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
    }
}
