using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using Lumoin.Veritas.ParserTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// Shared DAG generators for class-hierarchy property tests. Builds
/// a class hierarchy and value-type assignments from CsCheck-supplied
/// bitmaps; produces both an in-memory <see cref="InMemoryGraphStore"/>
/// (for direct helper tests) and a SHACL pipeline data state (for
/// evaluator tests).
/// </summary>
/// <remarks>
/// <para>
/// <b>DAG construction.</b> Classes are integer-indexed. Edges go
/// from higher-indexed (more specific) to lower-indexed (more
/// general): if bit <c>edgeBit(child, parent)</c> is set, the data
/// graph contains <c>class_child rdfs:subClassOf class_parent</c>.
/// The constraint <c>parent &lt; child</c> is enforced by indexing,
/// so the result is acyclic by construction.
/// </para>
/// <para>
/// <b>Value-type assignments.</b> For each <c>(value, class)</c>
/// pair, a single bit in the type bitmap controls whether
/// <c>value rdf:type class</c> is asserted. Multiple types per value
/// are allowed.
/// </para>
/// </remarks>
internal static class ClassHierarchyTestDag
{
    public const string RdfTypeIri = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    public const string RdfsSubClassOfIri = "http://www.w3.org/2000/01/rdf-schema#subClassOf";

    public static string ClassIri(int index) => $"http://example.org/class{index}";
    public static string ValueIri(int index) => $"http://example.org/value{index}";

    //Number of edge bits needed for a class hierarchy with classCount
    //classes. Only edges with parent < child are allowed; the count
    //is C(classCount, 2).
    public static int EdgeBitCount(int classCount) => classCount * (classCount - 1) / 2;

    //Maps a (child, parent) pair with parent < child to a flat bit
    //index. The mapping is consistent with the bitmap layout:
    //bits for child=1 come first (parent=0), then child=2 (parent
    //in 0..1), etc.
    public static int EdgeBitIndex(int child, int parent)
        => (child * (child - 1) / 2) + parent;

    //Maps a (value, class) pair to a flat type-bitmap index.
    public static int TypeBitIndex(int valueIndex, int classIndex, int classCount)
        => (valueIndex * classCount) + classIndex;

    //Decodes the edge bitmap into a parent-child adjacency list,
    //keyed on child class index.
    public static Dictionary<int, List<int>> DecodeEdges(bool[] edgeBits, int classCount)
    {
        Dictionary<int, List<int>> parentsByChild = [];
        for(int child = 1; child < classCount; child++)
        {
            List<int> parents = [];
            for(int parent = 0; parent < child; parent++)
            {
                if(edgeBits[EdgeBitIndex(child, parent)])
                {
                    parents.Add(parent);
                }
            }

            parentsByChild[child] = parents;
        }

        return parentsByChild;
    }

    //Decodes the type bitmap into per-value direct-type lists.
    public static Dictionary<int, List<int>> DecodeTypes(
        bool[] typeBits, int valueCount, int classCount)
    {
        Dictionary<int, List<int>> typesByValue = [];
        for(int v = 0; v < valueCount; v++)
        {
            List<int> types = [];
            for(int c = 0; c < classCount; c++)
            {
                if(typeBits[TypeBitIndex(v, c, classCount)])
                {
                    types.Add(c);
                }
            }

            typesByValue[v] = types;
        }

        return typesByValue;
    }

    //Reference: transitive closure of the subClassOf graph from a
    //given starting class, excluding the start itself.
    public static HashSet<int> StrictSuperclasses(int classIndex, Dictionary<int, List<int>> parentsByChild)
    {
        HashSet<int> result = [];
        Queue<int> queue = new();
        if(parentsByChild.TryGetValue(classIndex, out List<int>? directParents))
        {
            foreach(int p in directParents)
            {
                if(result.Add(p))
                {
                    queue.Enqueue(p);
                }
            }
        }

        while(queue.Count > 0)
        {
            int current = queue.Dequeue();
            if(parentsByChild.TryGetValue(current, out List<int>? parents))
            {
                foreach(int p in parents)
                {
                    if(result.Add(p))
                    {
                        queue.Enqueue(p);
                    }
                }
            }
        }

        return result;
    }

    //Reference: per the spec definition, value is a SHACL instance
    //of target iff some direct type t of value satisfies either
    //t == target or target is in StrictSuperclasses(t).
    public static bool IsInstanceOf(
        int valueIndex,
        int targetClass,
        Dictionary<int, List<int>> typesByValue,
        Dictionary<int, List<int>> parentsByChild)
    {
        if(!typesByValue.TryGetValue(valueIndex, out List<int>? directTypes))
        {
            return false;
        }

        foreach(int t in directTypes)
        {
            if(t == targetClass)
            {
                return true;
            }

            HashSet<int> closure = StrictSuperclasses(t, parentsByChild);
            if(closure.Contains(targetClass))
            {
                return true;
            }
        }

        return false;
    }

    //Reference: per the RootClassEvaluator implementation, value
    //conforms iff (a) it is an instance of ClassId AND (b) none of
    //its direct types and none of their strict superclasses lies in
    //the strict-superclass set of ClassId.
    public static bool RootClassConforms(
        int valueIndex,
        int classId,
        Dictionary<int, List<int>> typesByValue,
        Dictionary<int, List<int>> parentsByChild)
    {
        if(!IsInstanceOf(valueIndex, classId, typesByValue, parentsByChild))
        {
            return false;
        }

        HashSet<int> superclassesOfRoot = StrictSuperclasses(classId, parentsByChild);

        if(!typesByValue.TryGetValue(valueIndex, out List<int>? directTypes))
        {
            return true;
        }

        foreach(int t in directTypes)
        {
            if(superclassesOfRoot.Contains(t))
            {
                return false;
            }

            HashSet<int> tSupers = StrictSuperclasses(t, parentsByChild);
            if(tSupers.Overlaps(superclassesOfRoot))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// CsCheck-driven property tests for
/// <see cref="ClassHierarchyHelpers.IsInstanceOfAsync"/>. The helper
/// is a pure function over (value, target class, data graph),
/// testable directly without the SHACL pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reference implementation.</b>
/// <see cref="ClassHierarchyTestDag.IsInstanceOf"/> is a literal
/// transcription of the spec definition. The reference operates on
/// integer indices; the property test materialises the same graph
/// in the data store and asserts that the helper returns the same
/// answer for every (value, class) pair.
/// </para>
/// <para>
/// <b>No pipeline.</b> Unlike the other property test files, this
/// one bypasses <see cref="TestShaclPipeline"/> entirely. The helper
/// takes a <see cref="StorageDelegates.MatchTriplesAsync"/> delegate
/// directly via
/// <see cref="RdfAdjacencyAdapter"/>. Building an
/// <see cref="InMemoryGraphStore"/> with the encoded triples is
/// sufficient.
/// </para>
/// </remarks>
[TestClass]
internal sealed class ClassHierarchyHelpersPropertyTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task PropertyIsInstanceOfMatchesSpecDefinition()
    {
        //4 classes, 3 values. Edge bitmap controls subClassOf edges
        //(only parent<child allowed; 6 edges possible). Type bitmap
        //controls direct rdf:type assignments (12 cells). Total
        //bitmap length 18.
        const int ClassCount = 4;
        const int ValueCount = 3;
        int edgeBits = ClassHierarchyTestDag.EdgeBitCount(ClassCount);
        int typeBits = ValueCount * ClassCount;
        int total = edgeBits + typeBits;

        await Gen.Bool.Array[total, total].SampleAsync(async bits =>
        {
            bool[] edges = new bool[edgeBits];
            bool[] types = new bool[typeBits];
            System.Array.Copy(bits, 0, edges, 0, edgeBits);
            System.Array.Copy(bits, edgeBits, types, 0, typeBits);

            Dictionary<int, List<int>> parentsByChild =
                ClassHierarchyTestDag.DecodeEdges(edges, ClassCount);
            Dictionary<int, List<int>> typesByValue =
                ClassHierarchyTestDag.DecodeTypes(types, ValueCount, ClassCount);

            //For every (value, target) pair, expected = reference;
            //actual = helper's decision via direct call.
            for(int v = 0; v < ValueCount; v++)
            {
                for(int target = 0; target < ClassCount; target++)
                {
                    bool expected = ClassHierarchyTestDag.IsInstanceOf(
                        v, target, typesByValue, parentsByChild);
                    bool actual = await RunIsInstanceOfAsync(
                        v, target, parentsByChild, typesByValue, ClassCount, ValueCount).ConfigureAwait(false);

                    Assert.AreEqual(expected, actual,
                        $"IsInstanceOf(v{v}, c{target}) reference={expected} helper={actual}. "
                        + $"parentsByChild={FormatAdjacency(parentsByChild)} "
                        + $"typesByValue={FormatAdjacency(typesByValue)}.");
                }
            }
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyIsInstanceOfReflexiveOnDirectType()
    {
        //If value has direct type T, then IsInstanceOf(value, T)
        //must be true regardless of the rest of the hierarchy. This
        //is the reflexive case of the spec definition: the asserted
        //type IS the target. The test is a corollary of the main
        //property but pinning it independently catches regressions
        //that affect only the equality short-circuit path.
        const int ClassCount = 3;
        const int ValueCount = 2;
        int edgeBits = ClassHierarchyTestDag.EdgeBitCount(ClassCount);
        int typeBits = ValueCount * ClassCount;

        await Gen.Bool.Array[edgeBits + typeBits, edgeBits + typeBits].SampleAsync(async bits =>
        {
            bool[] edges = new bool[edgeBits];
            bool[] types = new bool[typeBits];
            System.Array.Copy(bits, 0, edges, 0, edgeBits);
            System.Array.Copy(bits, edgeBits, types, 0, typeBits);

            Dictionary<int, List<int>> parentsByChild =
                ClassHierarchyTestDag.DecodeEdges(edges, ClassCount);
            Dictionary<int, List<int>> typesByValue =
                ClassHierarchyTestDag.DecodeTypes(types, ValueCount, ClassCount);

            for(int v = 0; v < ValueCount; v++)
            {
                if(!typesByValue.TryGetValue(v, out List<int>? directTypes))
                {
                    continue;
                }

                foreach(int directType in directTypes)
                {
                    bool actual = await RunIsInstanceOfAsync(
                        v, directType, parentsByChild, typesByValue, ClassCount, ValueCount).ConfigureAwait(false);

                    Assert.IsTrue(actual,
                        $"IsInstanceOf(v{v}, c{directType}) must be true when v{v} has direct type c{directType}.");
                }
            }
        }).ConfigureAwait(false);
    }

    //Materialises the generated DAG and value-type assignments into
    //an InMemoryGraphStore, builds an RdfAdjacencyAdapter over the
    //store, and calls IsInstanceOfAsync directly. No SHACL pipeline
    //involved.
    private async Task<bool> RunIsInstanceOfAsync(
        int valueIndex,
        int targetClass,
        Dictionary<int, List<int>> parentsByChild,
        Dictionary<int, List<int>> typesByValue,
        int classCount,
        int valueCount)
    {
        TermDictionary dictionary = new();

        NamedNode rdfTypeNode = new(Utf8Strings.From(ClassHierarchyTestDag.RdfTypeIri));
        NamedNode rdfsSubClassOfNode = new(Utf8Strings.From(ClassHierarchyTestDag.RdfsSubClassOfIri));

        IriId rdfTypeId = dictionary.GetOrAdd(rdfTypeNode);
        IriId rdfsSubClassOfId = dictionary.GetOrAdd(rdfsSubClassOfNode);

        NamedNode[] classNodes = new NamedNode[classCount];
        TermId[] classIds = new TermId[classCount];
        for(int c = 0; c < classCount; c++)
        {
            classNodes[c] = new NamedNode(Utf8Strings.From(ClassHierarchyTestDag.ClassIri(c)));
            classIds[c] = dictionary.GetOrAdd(classNodes[c]).Value;
        }

        NamedNode[] valueNodes = new NamedNode[valueCount];
        TermId[] valueIds = new TermId[valueCount];
        for(int v = 0; v < valueCount; v++)
        {
            valueNodes[v] = new NamedNode(Utf8Strings.From(ClassHierarchyTestDag.ValueIri(v)));
            valueIds[v] = dictionary.GetOrAdd(valueNodes[v]).Value;
        }

        List<EncodedTriple> triples = [];

        foreach((int child, List<int> parents) in parentsByChild)
        {
            foreach(int parent in parents)
            {
                triples.Add(new Quad(classNodes[child], rdfsSubClassOfNode, classNodes[parent])
                    .Encode(dictionary).AsTriple());
            }
        }

        foreach((int v, List<int> directTypes) in typesByValue)
        {
            foreach(int t in directTypes)
            {
                triples.Add(new Quad(valueNodes[v], rdfTypeNode, classNodes[t])
                    .Encode(dictionary).AsTriple());
            }
        }

        InMemoryGraphStore store = InMemoryGraphStore.Build(triples);
        RdfAdjacencyAdapter adapter = new(store.AsMatchDelegate());

        IriId targetClassId = IriId.FromUnchecked(classIds[targetClass]);

        ClassMembershipCache membershipCache = new();
        SubclassClosureCache closureCache = new();

        return await ClassHierarchyHelpers.IsInstanceOfAsync(
            valueIds[valueIndex],
            targetClassId,
            rdfTypeId,
            rdfsSubClassOfId,
            adapter,
            membershipCache,
            closureCache,
            TestContext.CancellationToken).ConfigureAwait(false);
    }

    private static string FormatAdjacency(Dictionary<int, List<int>> adj)
    {
        List<string> entries = [];
        foreach((int k, List<int> v) in adj)
        {
            entries.Add($"{k}->[{string.Join(",", v)}]");
        }

        return "{" + string.Join(", ", entries) + "}";
    }
}

/// <summary>
/// CsCheck-driven property tests for <see cref="RootClassEvaluator"/>.
/// Per SHACL 1.2 Core §6.3.4: value conforms iff it is a SHACL
/// instance of the declared root class AND no proper superclass of
/// that root class is also a SHACL type of the value.
/// </summary>
/// <remarks>
/// <para>
/// Reference implementation in
/// <see cref="ClassHierarchyTestDag.RootClassConforms"/>. The
/// property test runs the evaluator through the SHACL pipeline with
/// a NodeShape targeting a single value as focus, materialises the
/// generated DAG and rdf:type assignments, and compares the
/// per-iteration violation outcome to the reference's conformance
/// outcome.
/// </para>
/// <para>
/// One value per iteration keeps the pipeline construction simple;
/// CsCheck's many iterations exercise different combinations of
/// (hierarchy shape, value type assignments, root-class choice)
/// across the bitmap-driven generator.
/// </para>
/// </remarks>
[TestClass]
internal sealed class RootClassEvaluatorPropertyTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExShape = "http://example.org/S";

    [TestMethod]
    public async Task PropertyRootClassConformanceMatchesSpec()
    {
        //3 classes, 1 value (the focus). Edge bitmap (3 bits) for
        //subClassOf, type bitmap (3 bits) for rdf:type assignments
        //of the single focus. Root class fixed at index 0 (the most
        //general by index ordering). Total 6 bits.
        const int ClassCount = 3;
        const int RootClass = 0;
        int edgeBits = ClassHierarchyTestDag.EdgeBitCount(ClassCount);
        int typeBits = ClassCount;
        int total = edgeBits + typeBits;

        await Gen.Bool.Array[total, total].SampleAsync(async bits =>
        {
            bool[] edges = new bool[edgeBits];
            bool[] types = new bool[typeBits];
            System.Array.Copy(bits, 0, edges, 0, edgeBits);
            System.Array.Copy(bits, edgeBits, types, 0, typeBits);

            Dictionary<int, List<int>> parentsByChild =
                ClassHierarchyTestDag.DecodeEdges(edges, ClassCount);

            //Single value (index 0); reuse the typesByValue shape with
            //one entry.
            List<int> directTypes = [];
            for(int c = 0; c < ClassCount; c++)
            {
                if(types[c])
                {
                    directTypes.Add(c);
                }
            }

            Dictionary<int, List<int>> typesByValue = new()
            {
                [0] = directTypes,
            };

            bool expectedConforms = ClassHierarchyTestDag.RootClassConforms(
                0, RootClass, typesByValue, parentsByChild);

            int actualViolationCount = await RunRootClassAsync(
                RootClass, parentsByChild, directTypes, ClassCount).ConfigureAwait(false);

            int expectedViolationCount = expectedConforms ? 0 : 1;

            Assert.AreEqual(expectedViolationCount, actualViolationCount,
                $"Conforms reference={expectedConforms}, expected violations={expectedViolationCount}, "
                + $"got {actualViolationCount}. parents={FormatAdjacency(parentsByChild)} "
                + $"directTypes=[{string.Join(",", directTypes)}].");
        }).ConfigureAwait(false);
    }

    //Materialises the DAG and rdf:type assignments into the SHACL
    //pipeline. The single focus is the value; the shape is a
    //NodeShape with sh:rootClass.
    private async Task<int> RunRootClassAsync(
        int rootClass,
        Dictionary<int, List<int>> parentsByChild,
        List<int> directTypes,
        int classCount)
    {
        const string Focus = "http://example.org/value0";

        TestShaclPipelineShapeState shapeState = TestShaclPipeline
            .BeginWithFocus(Focus)
            .WithNodeShapeTargetingPipelineFocus(ExShape)
            .With(ShaclConstraintVocabulary.RootClass.ToString(),
                ShapeGraphBuilder.Iri(ClassHierarchyTestDag.ClassIri(rootClass)))
            .Done();

        TestShaclPipelineDataState dataState = await shapeState
            .BuildAsync(TestContext.CancellationToken).ConfigureAwait(false);

        //Emit subClassOf edges across the class hierarchy.
        foreach((int child, List<int> parents) in parentsByChild)
        {
            foreach(int parent in parents)
            {
                dataState = dataState.WithExplicitTriple(
                    subjectIri: ClassHierarchyTestDag.ClassIri(child),
                    predicateIri: ClassHierarchyTestDag.RdfsSubClassOfIri,
                    @object: ShapeGraphBuilder.Iri(ClassHierarchyTestDag.ClassIri(parent)));
            }
        }

        //Emit the focus's rdf:type assertions.
        foreach(int t in directTypes)
        {
            dataState = dataState.WithTripleOnFocus(
                ClassHierarchyTestDag.RdfTypeIri,
                ShapeGraphBuilder.Iri(ClassHierarchyTestDag.ClassIri(t)));
        }

        (ValidationReport report, ValidationTrace _) = await dataState
            .WithEvaluator(ShaclComponentVocabulary.RootClass, RootClassEvaluator.EvaluateAsync)
            .RunWithTraceAsync(TestContext.CancellationToken).ConfigureAwait(false);

        int count = 0;
        foreach(ValidationResult result in report.Results)
        {
            if(result.Severity == Severity.Violation)
            {
                count++;
            }
        }

        return count;
    }

    private static string FormatAdjacency(Dictionary<int, List<int>> adj)
    {
        List<string> entries = [];
        foreach((int k, List<int> v) in adj)
        {
            entries.Add($"{k}->[{string.Join(",", v)}]");
        }

        return "{" + string.Join(", ", entries) + "}";
    }
}
