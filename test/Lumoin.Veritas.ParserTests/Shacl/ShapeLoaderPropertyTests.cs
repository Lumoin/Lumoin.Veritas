using CsCheck;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.ParserTests.Infrastructure;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// CsCheck-driven property tests for <see cref="ShapeLoader.LoadAsync"/>.
/// Each property exercises an invariant that should hold across many
/// randomly-generated shape graphs: the count of loaded shapes equals
/// the count of declared shapes, list-valued shape references don't
/// leak list-cell blanks into the registry, permuting triple order
/// produces the same registry, and so on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Async sampling.</b> The loader's <c>LoadAsync</c> returns a
/// <see cref="Task{T}"/>, so each property drives CsCheck's
/// <c>SampleAsync</c> with an async lambda that awaits <c>LoadAsync</c>
/// once per iteration.
/// </para>
/// </remarks>
[TestClass]
internal sealed class ShapeLoaderPropertyTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task PropertyNodeShapeCountRoundTrips()
    {
        //For any N node shapes declared with distinct IRIs, the loaded
        //registry contains exactly N shapes and each declared IRI
        //resolves to a shape.
        await Gen.Int[0, 20].SampleAsync(async n =>
        {
            ShapeGraphBuilder builder = new();
            for(int i = 0; i < n; i++)
            {
                builder.NodeShape($"http://example.org/Shape{i}")
                    .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));
            }

            (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

            ShapeRegistry registry = await ShapeLoader.LoadAsync(
                store.AsMatchDelegate(),
                dictionary,
                ShaclBuiltInComponents.All,
                cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);

            Assert.HasCount(n, registry.AllShapes);

            for(int i = 0; i < n; i++)
            {
                TermId id = dictionary.GetOrAdd(new NamedNode(
                    Utf8Strings.From($"http://example.org/Shape{i}")));
                Assert.IsTrue(registry.TryGetShape(id, out _),
                    $"Shape {i} should be resolvable.");
            }
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyShAndWithListOfAnyLengthDoesNotLeakListCells()
    {
        //Given an outer shape with sh:and pointing at a list of N
        //member shapes, the registry contains exactly N + 1 shapes
        //(the members plus the outer). No list-cell blank nodes
        //leak in as bogus node shapes. Pins the list-head fix.
        await Gen.Int[0, 10].SampleAsync(async memberCount =>
        {
            ShapeGraphBuilder builder = new();

            //Declare the member shapes.
            RdfTerm[] memberTerms = new RdfTerm[memberCount];
            for(int i = 0; i < memberCount; i++)
            {
                string iri = $"http://example.org/Member{i}";
                builder.NodeShape(iri)
                    .With(ShaclConstraintVocabulary.MinCount.ToString(),
                        ShapeGraphBuilder.IntLiteral(1));
                memberTerms[i] = ShapeGraphBuilder.Iri(iri);
            }

            //Declare the outer shape with sh:and pointing at the list.
            RdfTerm listHead = builder.List(memberTerms);
            builder.NodeShape("http://example.org/Outer")
                .With(ShaclConstraintVocabulary.And.ToString(), listHead);

            (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

            ShapeRegistry registry = await ShapeLoader.LoadAsync(
                store.AsMatchDelegate(),
                dictionary,
                ShaclBuiltInComponents.All,
                cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);

            Assert.HasCount(memberCount + 1, registry.AllShapes);

            //The outer shape's sh:and has exactly memberCount references.
            TermId outerId = dictionary.GetOrAdd(new NamedNode(
                Utf8Strings.From("http://example.org/Outer")));
            Assert.IsTrue(registry.TryGetShape(outerId, out Shape? outer));
            Assert.IsNotNull(outer);

            AndConstraint and = (AndConstraint)outer.Constraints.Single();
            Assert.HasCount(memberCount, and.MemberShapeIds);
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyConstraintCountEqualsDeclaredPrimaryCount()
    {
        //For each shape, the number of constraints in the loaded
        //Shape.Constraints equals the number of distinct constraint
        //primary parameters declared on it. Uses a small set of
        //primaries to keep the graph meaningful.
        await Gen.Int[0, 4].Array[1, 8].SampleAsync(async primariesPerShape =>
        {
            //primariesPerShape.Length shapes; shape i gets
            //primariesPerShape[i] distinct primary constraint parameters.
            //We pick from a fixed palette of 5 scalar primaries so any
            //value 0..4 maps to a distinct parameter on that shape.
            string[] palette =
            [
                ShaclConstraintVocabulary.MinCount.ToString(),
                ShaclConstraintVocabulary.MaxCount.ToString(),
                ShaclConstraintVocabulary.MinExclusive.ToString(),
                ShaclConstraintVocabulary.MaxExclusive.ToString(),
                ShaclConstraintVocabulary.MinLength.ToString(),
            ];

            ShapeGraphBuilder builder = new();

            for(int shapeIdx = 0; shapeIdx < primariesPerShape.Length; shapeIdx++)
            {
                int k = primariesPerShape[shapeIdx];  //0..4
                ShapeGraphBuilder.ShapeContext ctx = builder.NodeShape(
                    $"http://example.org/Shape{shapeIdx}");
                for(int p = 0; p <= k; p++)
                {
                    ctx.With(palette[p], ShapeGraphBuilder.IntLiteral(1));
                }
            }

            (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

            ShapeRegistry registry = await ShapeLoader.LoadAsync(
                store.AsMatchDelegate(),
                dictionary,
                ShaclBuiltInComponents.All,
                cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);

            Assert.HasCount(primariesPerShape.Length, registry.AllShapes);

            for(int shapeIdx = 0; shapeIdx < primariesPerShape.Length; shapeIdx++)
            {
                TermId id = dictionary.GetOrAdd(new NamedNode(
                    Utf8Strings.From($"http://example.org/Shape{shapeIdx}")));
                Assert.IsTrue(registry.TryGetShape(id, out Shape? shape));
                Assert.IsNotNull(shape);

                //k in palette is 0-based index, so count of primaries
                //declared on this shape is k+1.
                Assert.HasCount(primariesPerShape[shapeIdx] + 1, shape.Constraints,
                    $"Shape {shapeIdx} constraint count");
            }
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyLoaderIsOrderIndependent()
    {
        //Declaring the same set of shapes in different orders produces
        //the same set of loaded shape IRIs. Shape graph semantics are
        //triple-set semantics; any ordering sensitivity would be a bug,
        //and especially important for future graph stores (hypertrie)
        //whose match ordering may differ from the current sorted-array
        //backend.
        await Gen.Int[1, 10].SampleAsync(async n =>
        {
            HashSet<string> firstPass = await BuildAndLoad(n, reverseOrder: false).ConfigureAwait(false);
            HashSet<string> secondPass = await BuildAndLoad(n, reverseOrder: true).ConfigureAwait(false);

            Assert.IsTrue(firstPass.SetEquals(secondPass),
                "Shape IRI set should not depend on declaration order.");
        }).ConfigureAwait(false);

        async Task<HashSet<string>> BuildAndLoad(int n, bool reverseOrder)
        {
            ShapeGraphBuilder builder = new();
            IEnumerable<int> indices = reverseOrder
                ? Enumerable.Range(0, n).Reverse()
                : Enumerable.Range(0, n);

            foreach(int i in indices)
            {
                builder.NodeShape($"http://example.org/Shape{i}")
                    .With(ShaclConstraintVocabulary.MinCount.ToString(),
                        ShapeGraphBuilder.IntLiteral(1));
            }

            (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

            ShapeRegistry registry = await ShapeLoader.LoadAsync(
                store.AsMatchDelegate(),
                dictionary,
                ShaclBuiltInComponents.All,
                cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);

            HashSet<string> iris = [];
            foreach(Shape s in registry.AllShapes)
            {
                NamedNode named = (NamedNode)dictionary.Resolve(s.Id);
                iris.Add(named.Iri.ToString());
            }

            return iris;
        }
    }

    [TestMethod]
    public async Task PropertyInConstraintPreservesListLength()
    {
        //An sh:in list of N members produces an InConstraint whose
        //AllowedValues has exactly N entries. Exercises RDF list
        //walking across varied lengths (0 through many).
        await Gen.Int[0, 15].SampleAsync(async memberCount =>
        {
            ShapeGraphBuilder builder = new();

            RdfTerm[] members = new RdfTerm[memberCount];
            for(int i = 0; i < memberCount; i++)
            {
                members[i] = ShapeGraphBuilder.StringLiteral($"value{i}");
            }

            RdfTerm listHead = builder.List(members);
            builder.NodeShape("http://example.org/Shape")
                .With(ShaclConstraintVocabulary.In.ToString(), listHead);

            (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

            ShapeRegistry registry = await ShapeLoader.LoadAsync(
                store.AsMatchDelegate(),
                dictionary,
                ShaclBuiltInComponents.All,
                cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);

            Shape shape = registry.AllShapes.Single();
            InConstraint inConstraint = (InConstraint)shape.Constraints.Single();
            Assert.HasCount(memberCount, inConstraint.AllowedValues);
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyShapeKindRoundTripsForMixedDeclarations()
    {
        //For any mixed sequence of node-shape and property-shape
        //declarations, every loaded shape's kind matches what was
        //declared. Probes classification across all four discovery
        //rules that touch the isPropertyShape flag: sh:path (rule 1
        //sets true), rdf:type sh:NodeShape / sh:PropertyShape (rule 2
        //respects priors), constraint-parameter subject (rule 4
        //defaults to node), sh:property object (rule 5 sets true).
        await Gen.Bool.Array[1, 12].SampleAsync(async kinds =>
        {
            ShapeGraphBuilder builder = new();

            for(int i = 0; i < kinds.Length; i++)
            {
                string iri = $"http://example.org/Shape{i}";
                if(kinds[i])
                {
                    builder.PropertyShape(iri, $"http://example.org/prop{i}")
                        .With(ShaclConstraintVocabulary.MinCount.ToString(),
                            ShapeGraphBuilder.IntLiteral(1));
                }
                else
                {
                    builder.NodeShape(iri)
                        .With(ShaclConstraintVocabulary.MinCount.ToString(),
                            ShapeGraphBuilder.IntLiteral(1));
                }
            }

            (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

            ShapeRegistry registry = await ShapeLoader.LoadAsync(
                store.AsMatchDelegate(),
                dictionary,
                ShaclBuiltInComponents.All,
                cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);

            Assert.HasCount(kinds.Length, registry.AllShapes);

            int expectedNodeShapes = kinds.Count(k => !k);
            int expectedPropertyShapes = kinds.Count(k => k);

            Assert.HasCount(expectedNodeShapes, registry.NodeShapes);
            Assert.HasCount(expectedPropertyShapes, registry.PropertyShapes);

            //Per-shape kind check.
            for(int i = 0; i < kinds.Length; i++)
            {
                TermId id = dictionary.GetOrAdd(new NamedNode(
                    Utf8Strings.From($"http://example.org/Shape{i}")));
                Assert.IsTrue(registry.TryGetShape(id, out Shape? shape));
                Assert.IsNotNull(shape);

                if(kinds[i])
                {
                    Assert.IsInstanceOfType<PropertyShape>(shape,
                        $"Shape {i} declared with sh:path must be PropertyShape.");
                }
                else
                {
                    Assert.IsInstanceOfType<NodeShape>(shape,
                        $"Shape {i} without sh:path must be NodeShape.");
                }
            }
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyAllReferencedShapeIdsResolveInTheRegistry()
    {
        //For every shape in the registry and every TermId in its
        //ReferencedShapeIds, the target id must itself be a loadable
        //shape in the registry. This is the soundness condition for
        //shape-tree walking — the validator relies on being able to
        //follow references by id. If discovery fails to pick up a
        //transitively-referenced shape, this catches it.
        await Gen.Int[1, 10].SampleAsync(async n =>
        {
            ShapeGraphBuilder builder = new();

            //Declare n shapes, each with a sh:not referencing shape
            //((i + 1) mod n). Produces a cycle of references for n > 1;
            //for n == 1 the shape references itself, which is legal
            //SHACL (vacuously unreachable validation but syntactically
            //valid).
            for(int i = 0; i < n; i++)
            {
                int target = (i + 1) % n;
                builder.NodeShape($"http://example.org/Shape{i}")
                    .With(ShaclConstraintVocabulary.Not.ToString(),
                        ShapeGraphBuilder.Iri($"http://example.org/Shape{target}"));
            }

            (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

            ShapeRegistry registry = await ShapeLoader.LoadAsync(
                store.AsMatchDelegate(),
                dictionary,
                ShaclBuiltInComponents.All,
                cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);

            Assert.HasCount(n, registry.AllShapes);

            //Soundness: every referenced id resolves.
            foreach(Shape shape in registry.AllShapes)
            {
                foreach(TermId referencedId in shape.ReferencedShapeIds)
                {
                    Assert.IsTrue(registry.TryGetShape(referencedId, out _),
                        $"Shape {shape.Id} references {referencedId}, which must be in the registry.");
                }
            }
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyTypedAndDynamicConstraintsCoexist()
    {
        //A registry containing both built-in typed components and
        //custom dynamic components can load shapes that use either
        //kind, and each shape gets a constraint of the expected type.
        //Probes the dispatch path where the loader chooses between
        //pre-registered primary-parameter-id → ConstraintComponentInfo
        //entries without special-casing dynamic components.
        await Gen.Bool.Array[1, 10].SampleAsync(async useDynamic =>
        {
            const string CustomComponentIri = "http://example.org/ns#CustomComponent";
            const string CustomParamIri = "http://example.org/ns#customParam";

            ConstraintComponentInfo customDynamic = ConstraintComponentInfo.CreateDynamic(
                componentIri: Utf8Strings.From(CustomComponentIri),
                primaryParameter: Utf8Strings.From(CustomParamIri),
                shapeTypedParameters: []);

            List<ConstraintComponentInfo> registered =
            [
                .. ShaclBuiltInComponents.All,
                customDynamic,
            ];

            ShapeGraphBuilder builder = new();

            for(int i = 0; i < useDynamic.Length; i++)
            {
                string iri = $"http://example.org/Shape{i}";
                if(useDynamic[i])
                {
                    builder.NodeShape(iri)
                        .With(CustomParamIri, ShapeGraphBuilder.IntLiteral(i));
                }
                else
                {
                    builder.NodeShape(iri)
                        .With(ShaclConstraintVocabulary.MinCount.ToString(),
                            ShapeGraphBuilder.IntLiteral(i));
                }
            }

            (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

            ShapeRegistry registry = await ShapeLoader.LoadAsync(
                store.AsMatchDelegate(),
                dictionary,
                registered,
                cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);

            Assert.HasCount(useDynamic.Length, registry.AllShapes);

            for(int i = 0; i < useDynamic.Length; i++)
            {
                TermId id = dictionary.GetOrAdd(new NamedNode(
                    Utf8Strings.From($"http://example.org/Shape{i}")));
                Assert.IsTrue(registry.TryGetShape(id, out Shape? shape));
                Assert.IsNotNull(shape);

                ConstraintComponent constraint = shape.Constraints.Single();
                if(useDynamic[i])
                {
                    Assert.IsInstanceOfType<DynamicConstraint>(constraint,
                        $"Shape {i} with custom parameter should have DynamicConstraint.");
                }
                else
                {
                    Assert.IsInstanceOfType<MinCountConstraint>(constraint,
                        $"Shape {i} with sh:minCount should have typed MinCountConstraint.");
                }
            }
        }).ConfigureAwait(false);
    }
}
