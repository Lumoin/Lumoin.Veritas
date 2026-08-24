using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Owl.Rl;

/// <summary>
/// The encoded identifiers of every vocabulary term the OWL 2 RL/RDF rules
/// consume, resolved once through the caller's term dictionary — the same
/// dictionary-free-reasoner convention as <see cref="RdfsVocabularyTerms"/>,
/// sized for the full rule set.
/// </summary>
public sealed class OwlRlTerms
{
    /// <summary>The encoded <c>rdf:type</c> term.</summary>
    public TermId Type { get; }

    /// <summary>The encoded <c>rdf:first</c> term.</summary>
    public TermId First { get; }

    /// <summary>The encoded <c>rdf:rest</c> term.</summary>
    public TermId Rest { get; }

    /// <summary>The encoded <c>rdf:nil</c> term.</summary>
    public TermId Nil { get; }

    /// <summary>The encoded <c>rdfs:subClassOf</c> term.</summary>
    public TermId SubClassOf { get; }

    /// <summary>The encoded <c>rdfs:subPropertyOf</c> term.</summary>
    public TermId SubPropertyOf { get; }

    /// <summary>The encoded <c>rdfs:domain</c> term.</summary>
    public TermId Domain { get; }

    /// <summary>The encoded <c>rdfs:range</c> term.</summary>
    public TermId Range { get; }

    /// <summary>The encoded <c>owl:sameAs</c> term.</summary>
    public TermId SameAs { get; }

    /// <summary>The encoded <c>owl:differentFrom</c> term.</summary>
    public TermId DifferentFrom { get; }

    /// <summary>The encoded <c>owl:AllDifferent</c> term.</summary>
    public TermId AllDifferent { get; }

    /// <summary>The encoded <c>owl:members</c> term.</summary>
    public TermId Members { get; }

    /// <summary>The encoded <c>owl:distinctMembers</c> term.</summary>
    public TermId DistinctMembers { get; }

    /// <summary>The encoded <c>owl:Thing</c> term.</summary>
    public TermId Thing { get; }

    /// <summary>The encoded <c>owl:Nothing</c> term.</summary>
    public TermId Nothing { get; }

    /// <summary>The encoded <c>owl:Class</c> term.</summary>
    public TermId ClassTerm { get; }

    /// <summary>The encoded <c>rdfs:Class</c> term — the metaclass the RDF-Based semantics merges with <c>owl:Class</c> under <see cref="OwlAxiomaticVocabulary.MetaclassMerged"/>.</summary>
    public TermId RdfsClass { get; }

    /// <summary>The encoded <c>owl:FunctionalProperty</c> term.</summary>
    public TermId FunctionalProperty { get; }

    /// <summary>The encoded <c>owl:InverseFunctionalProperty</c> term.</summary>
    public TermId InverseFunctionalProperty { get; }

    /// <summary>The encoded <c>owl:IrreflexiveProperty</c> term.</summary>
    public TermId IrreflexiveProperty { get; }

    /// <summary>The encoded <c>owl:ReflexiveProperty</c> term.</summary>
    public TermId ReflexiveProperty { get; }

    /// <summary>The encoded <c>owl:SymmetricProperty</c> term.</summary>
    public TermId SymmetricProperty { get; }

    /// <summary>The encoded <c>owl:AsymmetricProperty</c> term.</summary>
    public TermId AsymmetricProperty { get; }

    /// <summary>The encoded <c>owl:TransitiveProperty</c> term.</summary>
    public TermId TransitiveProperty { get; }

    /// <summary>The encoded <c>owl:propertyChainAxiom</c> term.</summary>
    public TermId PropertyChainAxiom { get; }

    /// <summary>The encoded <c>owl:equivalentProperty</c> term.</summary>
    public TermId EquivalentProperty { get; }

    /// <summary>The encoded <c>owl:propertyDisjointWith</c> term.</summary>
    public TermId PropertyDisjointWith { get; }

    /// <summary>The encoded <c>owl:AllDisjointProperties</c> term.</summary>
    public TermId AllDisjointProperties { get; }

    /// <summary>The encoded <c>owl:inverseOf</c> term.</summary>
    public TermId InverseOf { get; }

    /// <summary>The encoded <c>owl:hasKey</c> term.</summary>
    public TermId HasKey { get; }

    /// <summary>The encoded <c>owl:sourceIndividual</c> term.</summary>
    public TermId SourceIndividual { get; }

    /// <summary>The encoded <c>owl:assertionProperty</c> term.</summary>
    public TermId AssertionProperty { get; }

    /// <summary>The encoded <c>owl:targetIndividual</c> term.</summary>
    public TermId TargetIndividual { get; }

    /// <summary>The encoded <c>owl:targetValue</c> term.</summary>
    public TermId TargetValue { get; }

    /// <summary>The encoded <c>owl:NegativePropertyAssertion</c> term.</summary>
    public TermId NegativePropertyAssertion { get; }

    /// <summary>The encoded <c>owl:intersectionOf</c> term.</summary>
    public TermId IntersectionOf { get; }

    /// <summary>The encoded <c>owl:unionOf</c> term.</summary>
    public TermId UnionOf { get; }

    /// <summary>The encoded <c>owl:complementOf</c> term.</summary>
    public TermId ComplementOf { get; }

    /// <summary>The encoded <c>owl:oneOf</c> term.</summary>
    public TermId OneOf { get; }

    /// <summary>The encoded <c>owl:someValuesFrom</c> term.</summary>
    public TermId SomeValuesFrom { get; }

    /// <summary>The encoded <c>owl:allValuesFrom</c> term.</summary>
    public TermId AllValuesFrom { get; }

    /// <summary>The encoded <c>owl:hasValue</c> term.</summary>
    public TermId HasValue { get; }

    /// <summary>The encoded <c>owl:onProperty</c> term.</summary>
    public TermId OnProperty { get; }

    /// <summary>The encoded <c>owl:onClass</c> term.</summary>
    public TermId OnClass { get; }

    /// <summary>The encoded <c>owl:maxCardinality</c> term.</summary>
    public TermId MaxCardinality { get; }

    /// <summary>The encoded <c>owl:maxQualifiedCardinality</c> term.</summary>
    public TermId MaxQualifiedCardinality { get; }

    /// <summary>The encoded <c>owl:minCardinality</c> term.</summary>
    public TermId MinCardinality { get; }

    /// <summary>The encoded <c>owl:cardinality</c> term.</summary>
    public TermId Cardinality { get; }

    /// <summary>The encoded <c>owl:equivalentClass</c> term.</summary>
    public TermId EquivalentClass { get; }

    /// <summary>The encoded <c>owl:disjointWith</c> term.</summary>
    public TermId DisjointWith { get; }

    /// <summary>The encoded <c>owl:AllDisjointClasses</c> term.</summary>
    public TermId AllDisjointClasses { get; }

    /// <summary>The encoded <c>owl:NamedIndividual</c> term — the individuals the reflexive-property rule instantiates over.</summary>
    public TermId NamedIndividual { get; }

    /// <summary>The encoded <c>rdfs:Datatype</c> term.</summary>
    public TermId RdfsDatatype { get; }

    /// <summary>The encoded <c>rdf:Property</c> term.</summary>
    public TermId RdfProperty { get; }

    /// <summary>The encoded <c>owl:AnnotationProperty</c> term.</summary>
    public TermId AnnotationProperty { get; }

    /// <summary>The encoded <c>owl:ObjectProperty</c> term.</summary>
    public TermId ObjectPropertyTerm { get; }

    /// <summary>The encoded <c>owl:DatatypeProperty</c> term.</summary>
    public TermId DatatypePropertyTerm { get; }

    /// <summary>The encoded <c>owl:Ontology</c> term.</summary>
    public TermId Ontology { get; }

    /// <summary>The encoded <c>owl:imports</c> term.</summary>
    public TermId Imports { get; }

    /// <summary>
    /// The built-in annotation properties the RDF-Based semantics types
    /// <c>owl:AnnotationProperty</c> axiomatically; the closure seeds one
    /// typing triple per member.
    /// </summary>
    public IReadOnlyList<TermId> BuiltInAnnotationProperties { get; }

    /// <summary>
    /// The property-characteristic classes the RDF-Based semantics
    /// subsumes under <c>owl:ObjectProperty</c> axiomatically; the closure
    /// seeds one subsumption triple per member.
    /// </summary>
    public IReadOnlyList<TermId> PropertyCharacteristicClasses { get; }

    /// <summary>The encodings of a zero cardinality bound across the integer datatypes fixtures use; the max-cardinality falsity rules match any of them.</summary>
    public IReadOnlySet<TermId> ZeroBounds { get; }

    /// <summary>The encodings of a one cardinality bound across the integer datatypes fixtures use; the max-cardinality sameAs rules match any of them.</summary>
    public IReadOnlySet<TermId> OneBounds { get; }

    /// <summary>
    /// The built-in XSD numeric and string datatype hierarchy as
    /// (subtype, supertype) pairs, seeded into the closure base so the
    /// schema rules derive datatype-range subsumptions
    /// (<c>xsd:byte ⊑ xsd:short ⊑ … ⊑ xsd:decimal</c>, the unsigned and
    /// signed branches, and the string family).
    /// </summary>
    public IReadOnlyList<(TermId SubType, TermId SuperType)> DatatypeHierarchy { get; }

    /// <summary>
    /// Every term the rule set reads by identifier — the resolved
    /// vocabulary, the axiomatic-seed terms, and the cardinality-bound
    /// encodings. A canonicalized view of the closure must keep each of
    /// these as its equivalence clique's representative, or bridge the
    /// term back explicitly; a representative choice that rewrites one
    /// away silently disables its fixed-identifier reads.
    /// </summary>
    public IReadOnlySet<TermId> IdentityReadTerms { get; }

    /// <summary>The dictionary the vocabulary resolved through; the extension rules mint their deterministic structure nodes with it, and the entailment surface fail-fasts when a caller pairs the vocabulary with a different dictionary — term identifiers from independent dictionaries collide by construction.</summary>
    internal TermDictionary Dictionary { get; }

    /// <summary>
    /// The deterministic list node at <paramref name="position"/> of the
    /// transitivity-chain extension structure for
    /// <paramref name="property"/>. The content-keyed engine mint makes the
    /// derivation idempotent across rounds — the same property always
    /// yields the same nodes, so the fixpoint terminates — and no parsed
    /// term can occupy the node, so an input document cannot pre-load facts
    /// onto the chain structure.
    /// </summary>
    /// <param name="property">The transitive property the chain is for.</param>
    /// <param name="position">The zero-based position in the two-link chain list.</param>
    /// <returns>The minted node identifier.</returns>
    internal TermId TransitivityChainNode(TermId property, int position)
    {
        return Dictionary.GetOrAdd((RdfTerm)new EngineNode(EngineNodeFamily.TransitivityChain, property.Encoded, (uint)position));
    }

    /// <summary>
    /// The deterministic existential-witness node for one instance of a
    /// some-values-from restriction under one of its semantic equations.
    /// Every dimension rides the label: each (someValuesFrom, onProperty)
    /// triple pair on a restriction node states its own independent
    /// existential, so witnesses are never shared across fillers or
    /// properties — a shared node would assert an unentailed coincidence.
    /// The content-keyed engine mint makes the minting idempotent across
    /// rounds, and no parsed term can occupy the witness node.
    /// </summary>
    /// <param name="instance">The restriction's member the witness serves.</param>
    /// <param name="restriction">The restriction node.</param>
    /// <param name="property">The <c>owl:onProperty</c> edge the equation reads.</param>
    /// <param name="filler">The <c>owl:someValuesFrom</c> filler the witness is typed with.</param>
    /// <returns>The minted witness identifier.</returns>
    internal TermId SomeValuesFromWitnessNode(TermId instance, TermId restriction, TermId property, TermId filler)
    {
        return Dictionary.GetOrAdd((RdfTerm)new EngineNode(EngineNodeFamily.SomeValuesFromWitness, instance.Encoded, restriction.Encoded, property.Encoded, filler.Encoded));
    }

    /// <summary>
    /// Resolves the full RL vocabulary through the dictionary.
    /// </summary>
    /// <param name="dictionary">The term dictionary the triples were encoded with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <see langword="null"/>.</exception>
    public OwlRlTerms(TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        Dictionary = dictionary;
        Type = dictionary.GetOrAdd(new NamedNode(Vocabulary.Rdf.Type));
        First = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.First));
        Rest = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.Rest));
        Nil = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.Nil));
        SubClassOf = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.SubClassOf));
        SubPropertyOf = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.SubPropertyOf));
        Domain = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.Domain));
        Range = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.Range));
        SameAs = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.SameAs));
        DifferentFrom = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.DifferentFrom));
        AllDifferent = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.AllDifferent));
        Members = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.Members));
        DistinctMembers = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.DistinctMembers));
        Thing = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.Thing));
        Nothing = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.Nothing));
        ClassTerm = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.ClassTerm));
        RdfsClass = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.Class));
        FunctionalProperty = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.FunctionalProperty));
        InverseFunctionalProperty = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.InverseFunctionalProperty));
        IrreflexiveProperty = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.IrreflexiveProperty));
        ReflexiveProperty = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.ReflexiveProperty));
        SymmetricProperty = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.SymmetricProperty));
        AsymmetricProperty = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.AsymmetricProperty));
        TransitiveProperty = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.TransitiveProperty));
        PropertyChainAxiom = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.PropertyChainAxiom));
        EquivalentProperty = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.EquivalentProperty));
        PropertyDisjointWith = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.PropertyDisjointWith));
        AllDisjointProperties = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.AllDisjointProperties));
        InverseOf = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.InverseOf));
        HasKey = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.HasKey));
        SourceIndividual = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.SourceIndividual));
        AssertionProperty = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.AssertionProperty));
        TargetIndividual = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.TargetIndividual));
        TargetValue = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.TargetValue));
        NegativePropertyAssertion = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.NegativePropertyAssertion));
        IntersectionOf = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.IntersectionOf));
        UnionOf = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.UnionOf));
        ComplementOf = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.ComplementOf));
        OneOf = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.OneOf));
        SomeValuesFrom = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.SomeValuesFrom));
        AllValuesFrom = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.AllValuesFrom));
        HasValue = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.HasValue));
        OnProperty = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.OnProperty));
        OnClass = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.OnClass));
        MaxCardinality = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.MaxCardinality));
        MaxQualifiedCardinality = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.MaxQualifiedCardinality));
        MinCardinality = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.MinCardinality));
        Cardinality = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.Cardinality));
        EquivalentClass = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.EquivalentClass));
        DisjointWith = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.DisjointWith));
        AllDisjointClasses = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.AllDisjointClasses));
        NamedIndividual = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.NamedIndividual));
        RdfsDatatype = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.Datatype));
        RdfProperty = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.Property));
        AnnotationProperty = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.AnnotationPropertyTerm));
        ObjectPropertyTerm = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.ObjectPropertyTerm));
        DatatypePropertyTerm = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.DatatypeProperty));
        Ontology = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.Ontology));
        Imports = dictionary.GetOrAdd(new NamedNode(OwlVocabulary.Imports));
        BuiltInAnnotationProperties =
        [
            dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.Label)),
            dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.Comment)),
            dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.SeeAlso)),
            dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.IsDefinedBy)),
            dictionary.GetOrAdd(new NamedNode(OwlVocabulary.VersionInfo)),
            dictionary.GetOrAdd(new NamedNode(OwlVocabulary.Deprecated)),
            dictionary.GetOrAdd(new NamedNode(OwlVocabulary.PriorVersion)),
            dictionary.GetOrAdd(new NamedNode(OwlVocabulary.BackwardCompatibleWith)),
            dictionary.GetOrAdd(new NamedNode(OwlVocabulary.IncompatibleWith)),
        ];
        PropertyCharacteristicClasses =
        [
            FunctionalProperty,
            InverseFunctionalProperty,
            IrreflexiveProperty,
            ReflexiveProperty,
            SymmetricProperty,
            AsymmetricProperty,
            TransitiveProperty,
        ];
        ZeroBounds = BoundEncodings(dictionary, "0");
        OneBounds = BoundEncodings(dictionary, "1");

        TermId Datatype(Utf8String iri)
        {
            return dictionary.GetOrAdd(new NamedNode(iri));
        }

        TermId integer = Datatype(Vocabulary.Xsd.Integer);
        TermId nonNegative = Datatype(Vocabulary.Xsd.NonNegativeInteger);
        TermId nonPositive = Datatype(Vocabulary.Xsd.NonPositiveInteger);
        TermId longType = Datatype(Vocabulary.Xsd.Long);
        TermId intType = Datatype(Vocabulary.Xsd.Int);
        TermId shortType = Datatype(Vocabulary.Xsd.Short);
        TermId unsignedLong = Datatype(Vocabulary.Xsd.UnsignedLong);
        TermId unsignedInt = Datatype(Vocabulary.Xsd.UnsignedInt);
        TermId unsignedShort = Datatype(Vocabulary.Xsd.UnsignedShort);
        TermId normalizedString = Datatype(Vocabulary.Xsd.NormalizedString);

        DatatypeHierarchy =
        [
            (Datatype(Vocabulary.Xsd.ByteValue), shortType),
            (shortType, intType),
            (intType, longType),
            (longType, integer),
            (integer, Datatype(Vocabulary.Xsd.Decimal)),
            (nonNegative, integer),
            (nonPositive, integer),
            (Datatype(Vocabulary.Xsd.PositiveInteger), nonNegative),
            (Datatype(Vocabulary.Xsd.NegativeInteger), nonPositive),
            (Datatype(Vocabulary.Xsd.UnsignedByte), unsignedShort),
            (unsignedShort, unsignedInt),
            (unsignedInt, unsignedLong),
            (unsignedLong, nonNegative),
            (Datatype(Vocabulary.Xsd.Token), normalizedString),
            (normalizedString, Datatype(Vocabulary.Xsd.String)),
        ];

        HashSet<TermId> identityRead =
        [
            Type, First, Rest, Nil, SubClassOf, SubPropertyOf, Domain, Range,
            SameAs, DifferentFrom, AllDifferent, Members, DistinctMembers,
            Thing, Nothing, ClassTerm, RdfsClass,
            FunctionalProperty, InverseFunctionalProperty, IrreflexiveProperty, ReflexiveProperty,
            SymmetricProperty, AsymmetricProperty, TransitiveProperty,
            PropertyChainAxiom, EquivalentProperty, PropertyDisjointWith, AllDisjointProperties,
            InverseOf, HasKey,
            SourceIndividual, AssertionProperty, TargetIndividual, TargetValue, NegativePropertyAssertion,
            IntersectionOf, UnionOf, ComplementOf, OneOf,
            SomeValuesFrom, AllValuesFrom, HasValue, OnProperty, OnClass,
            MaxCardinality, MaxQualifiedCardinality, MinCardinality, Cardinality,
            EquivalentClass, DisjointWith, AllDisjointClasses, NamedIndividual,
            RdfsDatatype, RdfProperty, AnnotationProperty, ObjectPropertyTerm, DatatypePropertyTerm,
            Ontology, Imports,
        ];
        identityRead.UnionWith(BuiltInAnnotationProperties);
        identityRead.UnionWith(PropertyCharacteristicClasses);
        identityRead.UnionWith(ZeroBounds);
        identityRead.UnionWith(OneBounds);
        foreach((TermId subType, TermId superType) in DatatypeHierarchy)
        {
            identityRead.Add(subType);
            identityRead.Add(superType);
        }

        IdentityReadTerms = identityRead;
    }

    //The integer datatypes a cardinality bound appears under in practice;
    //the bound matches by encoded literal identity, so each variant is
    //minted once here.
    private static HashSet<TermId> BoundEncodings(TermDictionary dictionary, string lexical)
    {
        Utf8String value = Utf8Strings.From(lexical);

        return
        [
            dictionary.GetOrAdd(new Literal(value, new NamedNode(Vocabulary.Xsd.NonNegativeInteger))),
            dictionary.GetOrAdd(new Literal(value, new NamedNode(Vocabulary.Xsd.Integer))),
            dictionary.GetOrAdd(new Literal(value, new NamedNode(Vocabulary.Xsd.Int))),
        ];
    }
}
