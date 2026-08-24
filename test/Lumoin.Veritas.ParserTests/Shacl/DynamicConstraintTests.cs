using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Constraints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// Round-trip equivalence tests between <see cref="DynamicConstraint"/>
/// produced through <see cref="ConstraintComponentInfo.CreateDynamic"/>
/// and a compile-time-typed equivalent produced through the ordinary
/// factory path. These tests anchor the graduation guarantee: a
/// dynamic constraint and its compiled twin agree on component IRI
/// and on captured parameter values.
/// </summary>
[TestClass]
internal sealed class DynamicConstraintTests
{
    [TestMethod]
    public void DynamicMinCountCapturesSamePrimaryValueAsBuiltIn()
    {
        TermDictionary dictionary = new();
        Utf8String minCountIri = Utf8Strings.From("http://www.w3.org/ns/shacl#minCount");
        IriId minCountId = dictionary.GetOrAdd(new NamedNode(minCountIri));
        TermId three = dictionary.GetOrAdd(new Literal(
            Utf8Strings.From("3"),
            new NamedNode(Vocabulary.Xsd.Integer)));

        ParameterBag bag = CreateBag(
            primaryParameter: minCountId,
            primaryValue: three,
            companions: EmptyCompanions,
            resolvedLists: EmptyResolvedLists,
            dictionary: dictionary);

        ConstraintComponent builtIn = ShaclBuiltInComponents.MinCount.Factory(bag)!;
        MinCountConstraint typed = (MinCountConstraint)builtIn;

        ConstraintComponentInfo dynamicInfo = ConstraintComponentInfo.CreateDynamic(
            componentIri: Utf8Strings.From("http://www.w3.org/ns/shacl#MinCountConstraintComponent"),
            primaryParameter: minCountIri,
            shapeTypedParameters: []);
        ConstraintComponent dynamic = dynamicInfo.Factory(bag)!;
        DynamicConstraint dyn = (DynamicConstraint)dynamic;

        Assert.AreEqual(builtIn.ConstraintComponentIri, dyn.ConstraintComponentIri);

        Assert.IsTrue(dyn.TryGetScalar(minCountId, out TermId dynPrimary));
        Assert.AreEqual(three, dynPrimary);

        Literal lit = (Literal)dictionary.Resolve(dynPrimary);
        Assert.AreEqual("3", lit.Value.ToString());
        Assert.AreEqual(3, typed.MinCount);

        Assert.IsEmpty(builtIn.ReferencedShapeIds);
        Assert.IsEmpty(dyn.ReferencedShapeIds);
    }

    [TestMethod]
    public void DynamicCapturesCompanionParameters()
    {
        TermDictionary dictionary = new();
        Utf8String patternIri = Utf8Strings.From("http://www.w3.org/ns/shacl#pattern");
        Utf8String flagsIri = Utf8Strings.From("http://www.w3.org/ns/shacl#flags");
        IriId patternId = dictionary.GetOrAdd(new NamedNode(patternIri));
        IriId flagsId = dictionary.GetOrAdd(new NamedNode(flagsIri));

        TermId patternValue = dictionary.GetOrAdd(new Literal(
            Utf8Strings.From("^[a-z]+$"),
            new NamedNode(Vocabulary.Xsd.String)));
        TermId flagsValue = dictionary.GetOrAdd(new Literal(
            Utf8Strings.From("i"),
            new NamedNode(Vocabulary.Xsd.String)));

        Dictionary<IriId, IReadOnlyList<TermId>> companions = new()
        {
            [flagsId] = new[] { flagsValue },
        };

        ParameterBag bag = CreateBag(
            primaryParameter: patternId,
            primaryValue: patternValue,
            companions: companions,
            resolvedLists: EmptyResolvedLists,
            dictionary: dictionary);

        ConstraintComponentInfo dynamicInfo = ConstraintComponentInfo.CreateDynamic(
            componentIri: Utf8Strings.From("http://www.w3.org/ns/shacl#PatternConstraintComponent"),
            primaryParameter: patternIri,
            shapeTypedParameters: [],
            optionalCompanions: flagsIri);

        DynamicConstraint dyn = (DynamicConstraint)dynamicInfo.Factory(bag)!;

        Assert.IsTrue(dyn.TryGetScalar(patternId, out TermId capturedPattern));
        Assert.IsTrue(dyn.TryGetScalar(flagsId, out TermId capturedFlags));
        Assert.AreEqual(patternValue, capturedPattern);
        Assert.AreEqual(flagsValue, capturedFlags);

        Assert.HasCount(2, dyn.ScalarParameters);
        Assert.IsEmpty(dyn.ListParameters);
        Assert.IsEmpty(dyn.ReferencedShapeIds);
    }

    [TestMethod]
    public void DynamicCapturesShapeReferencesFromShapeTypedPrimary()
    {
        TermDictionary dictionary = new();
        Utf8String nodeIri = Utf8Strings.From("http://www.w3.org/ns/shacl#node");
        IriId nodeId = dictionary.GetOrAdd(new NamedNode(nodeIri));

        TermId referencedShape = dictionary.GetOrAdd(new NamedNode(
            Utf8Strings.From("http://example.org/SomeNodeShape")));

        ParameterBag bag = CreateBag(
            primaryParameter: nodeId,
            primaryValue: referencedShape,
            companions: EmptyCompanions,
            resolvedLists: EmptyResolvedLists,
            dictionary: dictionary);

        ConstraintComponentInfo dynamicInfo = ConstraintComponentInfo.CreateDynamic(
            componentIri: Utf8Strings.From("http://www.w3.org/ns/shacl#NodeConstraintComponent"),
            primaryParameter: nodeIri,
            shapeTypedParameters: [nodeIri]);
        DynamicConstraint dyn = (DynamicConstraint)dynamicInfo.Factory(bag)!;

        List<TermId> refs = [];
        foreach(TermId refId in dyn.ReferencedShapeIds)
        {
            refs.Add(refId);
        }

        Assert.HasCount(1, refs);
        Assert.AreEqual(referencedShape, refs[0]);
    }

    [TestMethod]
    public void DynamicCapturesListPrimary()
    {
        TermDictionary dictionary = new();
        Utf8String andIri = Utf8Strings.From("http://www.w3.org/ns/shacl#and");
        IriId andId = dictionary.GetOrAdd(new NamedNode(andIri));

        TermId listHead = dictionary.GetOrAdd(new BlankNode(Utf8Strings.From("b0")));
        TermId shapeA = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/A")));
        TermId shapeB = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/B")));

        Dictionary<TermId, ImmutableArray<TermId>> resolvedLists = new()
        {
            [listHead] = [shapeA, shapeB],
        };

        ParameterBag bag = CreateBag(
            primaryParameter: andId,
            primaryValue: listHead,
            companions: EmptyCompanions,
            resolvedLists: resolvedLists,
            dictionary: dictionary);

        ConstraintComponentInfo dynamicInfo = ConstraintComponentInfo.CreateDynamic(
            componentIri: Utf8Strings.From("http://www.w3.org/ns/shacl#AndConstraintComponent"),
            primaryParameter: andIri,
            shapeTypedParameters: [andIri]);
        DynamicConstraint dyn = (DynamicConstraint)dynamicInfo.Factory(bag)!;

        Assert.IsEmpty(dyn.ScalarParameters);
        Assert.HasCount(1, dyn.ListParameters);
        Assert.IsTrue(dyn.TryGetList(andId, out ImmutableArray<TermId> members));
        Assert.HasCount(2, members);
        Assert.AreEqual(shapeA, members[0]);
        Assert.AreEqual(shapeB, members[1]);

        List<TermId> refs = [];
        foreach(TermId refId in dyn.ReferencedShapeIds)
        {
            refs.Add(refId);
        }

        Assert.HasCount(2, refs);
        Assert.Contains(shapeA, refs);
        Assert.Contains(shapeB, refs);
    }

    private static Dictionary<IriId, IReadOnlyList<TermId>> EmptyCompanions { get; } = new();

    private static Dictionary<TermId, ImmutableArray<TermId>> EmptyResolvedLists { get; } = new();

    private static ParameterBag CreateBag(
        IriId primaryParameter,
        TermId primaryValue,
        IReadOnlyDictionary<IriId, IReadOnlyList<TermId>> companions,
        IReadOnlyDictionary<TermId, ImmutableArray<TermId>> resolvedLists,
        TermDictionary dictionary)
    {
        RdfsVocabularyIds rdfsVocab = default;
        ShapeLoaderOptions options = new();
        ConcurrentDictionary<(string, string?, bool), Regex> patternMemo = new();

        return new ParameterBag(
            primaryParameter,
            primaryValue,
            companions,
            resolvedLists,
            dictionary,
            rdfsVocab,
            options,
            patternMemo);
    }
}
