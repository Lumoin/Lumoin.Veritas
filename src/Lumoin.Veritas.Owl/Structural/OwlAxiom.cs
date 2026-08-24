using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl.Structural;

/// <summary>
/// One OWL 2 axiom mapped out of an RDF graph. Every axiom carries the
/// <see cref="Origin"/> triple it was rooted at, so editor surfaces and
/// profile reports can point back into the source graph.
/// </summary>
public abstract record OwlAxiom
{
    /// <summary>The graph triple this axiom was mapped from — the axiom's root triple, not its whole structural extent.</summary>
    public required Quad Origin { get; init; }

    /// <summary>The axiom's annotations: functional-syntax axiom-frame annotations, or RDF <c>owl:Axiom</c> reification annotations.</summary>
    public ImmutableArray<OwlAnnotation> Annotations { get; init; } = [];

    /// <summary>
    /// Appends this axiom's direct individual-position terms to
    /// <paramref name="individualsToAppendTo"/> and pushes its direct class
    /// expressions onto <paramref name="expressionsToAppendTo"/>. Individual
    /// positions are the terms denoting individuals — class-assertion subjects,
    /// property-assertion sources and targets, individual (in)equality members,
    /// and negative-assertion sources and targets — never property or datatype
    /// IRIs and never literals. The member touches only the axiom's own direct
    /// terms and never descends: a caller reaches the individuals nested inside
    /// the pushed class expressions by draining the worklist, invoking
    /// <see cref="OwlClassExpression.AppendMentionedIndividuals"/> on each popped
    /// expression until the worklist empties.
    /// </summary>
    /// <param name="individualsToAppendTo">The list this axiom's direct individual-position terms are appended to.</param>
    /// <param name="expressionsToAppendTo">The worklist this axiom's direct class expressions are pushed onto for a caller to drain.</param>
    public abstract void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo);
}

/// <summary>
/// One annotation carried by an axiom, by an ontology header, or — nested —
/// by another annotation.
/// </summary>
/// <param name="Property">The annotation property node.</param>
/// <param name="Value">The annotation value: an IRI node, an anonymous individual, or a literal.</param>
[DebuggerDisplay("Annotation {Property}")]
public sealed record OwlAnnotation(NamedNode Property, RdfTerm Value)
{
    /// <summary>The annotations on this annotation itself, when nested.</summary>
    public ImmutableArray<OwlAnnotation> Annotations { get; init; } = [];
}

/// <summary>The kind of entity a declaration introduces.</summary>
public enum OwlEntityKind
{
    /// <summary>An <c>owl:Class</c> declaration.</summary>
    Class = 0,

    /// <summary>An <c>rdfs:Datatype</c> declaration.</summary>
    Datatype = 1,

    /// <summary>An <c>owl:ObjectProperty</c> declaration.</summary>
    ObjectProperty = 2,

    /// <summary>An <c>owl:DatatypeProperty</c> declaration.</summary>
    DataProperty = 3,

    /// <summary>An <c>owl:AnnotationProperty</c> declaration.</summary>
    AnnotationProperty = 4,

    /// <summary>An <c>owl:NamedIndividual</c> declaration.</summary>
    NamedIndividual = 5,
}

/// <summary>The object-property characteristic an axiom asserts.</summary>
public enum OwlPropertyCharacteristic
{
    /// <summary><c>FunctionalObjectProperty</c>.</summary>
    Functional = 0,

    /// <summary><c>InverseFunctionalObjectProperty</c>.</summary>
    InverseFunctional = 1,

    /// <summary><c>TransitiveObjectProperty</c>.</summary>
    Transitive = 2,

    /// <summary><c>SymmetricObjectProperty</c>.</summary>
    Symmetric = 3,

    /// <summary><c>AsymmetricObjectProperty</c>.</summary>
    Asymmetric = 4,

    /// <summary><c>ReflexiveObjectProperty</c>.</summary>
    Reflexive = 5,

    /// <summary><c>IrreflexiveObjectProperty</c>.</summary>
    Irreflexive = 6,
}

/// <summary>An entity declaration.</summary>
/// <param name="Kind">The declared entity kind.</param>
/// <param name="Entity">The declared entity IRI node.</param>
[DebuggerDisplay("Declaration {Kind} {Entity}")]
public sealed record OwlDeclarationAxiom(OwlEntityKind Kind, NamedNode Entity): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: a declaration introduces an entity and mentions no individual and no class expression.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>A <c>SubClassOf</c> axiom.</summary>
/// <param name="SubClass">The subclass expression.</param>
/// <param name="SuperClass">The superclass expression.</param>
[DebuggerDisplay("SubClassOf")]
public sealed record OwlSubClassOfAxiom(OwlClassExpression SubClass, OwlClassExpression SuperClass): OwlAxiom
{
    /// <summary>Pushes the subclass and superclass expressions; a subclass axiom mentions no direct individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        expressionsToAppendTo.Push(SubClass);
        expressionsToAppendTo.Push(SuperClass);
    }
}

/// <summary>An <c>EquivalentClasses</c> axiom over one asserted pair.</summary>
/// <param name="First">The first expression.</param>
/// <param name="Second">The second expression.</param>
[DebuggerDisplay("EquivalentClasses")]
public sealed record OwlEquivalentClassesAxiom(OwlClassExpression First, OwlClassExpression Second): OwlAxiom
{
    /// <summary>Pushes both equivalent class expressions; an equivalence axiom mentions no direct individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        expressionsToAppendTo.Push(First);
        expressionsToAppendTo.Push(Second);
    }
}

/// <summary>A <c>DisjointClasses</c> axiom (a pairwise <c>owl:disjointWith</c> yields two operands; an <c>owl:AllDisjointClasses</c> yields its member list).</summary>
/// <param name="Operands">The mutually disjoint expressions.</param>
[DebuggerDisplay("DisjointClasses ({Operands.Count})")]
public sealed record OwlDisjointClassesAxiom(IReadOnlyList<OwlClassExpression> Operands): OwlAxiom
{
    /// <summary>Pushes each disjoint class expression; a disjointness axiom mentions no direct individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        foreach(OwlClassExpression operand in Operands)
        {
            expressionsToAppendTo.Push(operand);
        }
    }
}

/// <summary>A <c>DisjointUnion</c> axiom.</summary>
/// <param name="Class">The class defined as the disjoint union.</param>
/// <param name="Operands">The pairwise-disjoint union members.</param>
[DebuggerDisplay("DisjointUnion {Class}")]
public sealed record OwlDisjointUnionAxiom(NamedNode Class, IReadOnlyList<OwlClassExpression> Operands): OwlAxiom
{
    /// <summary>Pushes each union-member class expression; the defined class is a named class, so no direct individual is mentioned.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        foreach(OwlClassExpression operand in Operands)
        {
            expressionsToAppendTo.Push(operand);
        }
    }
}

/// <summary>A <c>SubObjectPropertyOf</c> axiom between property expressions.</summary>
/// <param name="SubProperty">The subproperty expression.</param>
/// <param name="SuperProperty">The superproperty expression.</param>
[DebuggerDisplay("SubObjectPropertyOf")]
public sealed record OwlSubObjectPropertyOfAxiom(OwlObjectPropertyExpression SubProperty, OwlObjectPropertyExpression SuperProperty): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: a sub-object-property axiom holds only property expressions.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>A <c>SubObjectPropertyOf</c> axiom whose subproperty is a property chain (<c>owl:propertyChainAxiom</c>).</summary>
/// <param name="Chain">The chain links, in composition order.</param>
/// <param name="SuperProperty">The superproperty expression the chain is included in.</param>
[DebuggerDisplay("SubObjectPropertyOf(chain {Chain.Count})")]
public sealed record OwlPropertyChainAxiom(IReadOnlyList<OwlObjectPropertyExpression> Chain, OwlObjectPropertyExpression SuperProperty): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: a property-chain axiom holds only property expressions.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>An <c>EquivalentObjectProperties</c> axiom over one asserted pair.</summary>
/// <param name="First">The first property expression.</param>
/// <param name="Second">The second property expression.</param>
[DebuggerDisplay("EquivalentObjectProperties")]
public sealed record OwlEquivalentObjectPropertiesAxiom(OwlObjectPropertyExpression First, OwlObjectPropertyExpression Second): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: an object-property equivalence holds only property expressions.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>A <c>DisjointObjectProperties</c> axiom.</summary>
/// <param name="Operands">The mutually disjoint property expressions.</param>
[DebuggerDisplay("DisjointObjectProperties ({Operands.Count})")]
public sealed record OwlDisjointObjectPropertiesAxiom(IReadOnlyList<OwlObjectPropertyExpression> Operands): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: an object-property disjointness holds only property expressions.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>An <c>InverseObjectProperties</c> axiom.</summary>
/// <param name="First">The first property expression.</param>
/// <param name="Second">The second property expression.</param>
[DebuggerDisplay("InverseObjectProperties")]
public sealed record OwlInverseObjectPropertiesAxiom(OwlObjectPropertyExpression First, OwlObjectPropertyExpression Second): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: an inverse-object-properties axiom holds only property expressions.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>An <c>ObjectPropertyDomain</c> axiom.</summary>
/// <param name="Property">The property expression.</param>
/// <param name="Domain">The domain class expression.</param>
[DebuggerDisplay("ObjectPropertyDomain")]
public sealed record OwlObjectPropertyDomainAxiom(OwlObjectPropertyExpression Property, OwlClassExpression Domain): OwlAxiom
{
    /// <summary>Pushes the domain class expression; the property is a property expression, so no direct individual is mentioned.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        expressionsToAppendTo.Push(Domain);
    }
}

/// <summary>An <c>ObjectPropertyRange</c> axiom.</summary>
/// <param name="Property">The property expression.</param>
/// <param name="Range">The range class expression.</param>
[DebuggerDisplay("ObjectPropertyRange")]
public sealed record OwlObjectPropertyRangeAxiom(OwlObjectPropertyExpression Property, OwlClassExpression Range): OwlAxiom
{
    /// <summary>Pushes the range class expression; the property is a property expression, so no direct individual is mentioned.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        expressionsToAppendTo.Push(Range);
    }
}

/// <summary>An object-property characteristic axiom (functional, transitive, …).</summary>
/// <param name="Characteristic">The asserted characteristic.</param>
/// <param name="Property">The property expression.</param>
[DebuggerDisplay("{Characteristic}ObjectProperty")]
public sealed record OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic Characteristic, OwlObjectPropertyExpression Property): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: a characteristic axiom holds only a property expression.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>A <c>SubDataPropertyOf</c> axiom.</summary>
/// <param name="SubProperty">The data subproperty.</param>
/// <param name="SuperProperty">The data superproperty.</param>
[DebuggerDisplay("SubDataPropertyOf")]
public sealed record OwlSubDataPropertyOfAxiom(NamedNode SubProperty, NamedNode SuperProperty): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: a sub-data-property axiom holds only data properties.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>An <c>EquivalentDataProperties</c> axiom over one asserted pair.</summary>
/// <param name="First">The first data property.</param>
/// <param name="Second">The second data property.</param>
[DebuggerDisplay("EquivalentDataProperties")]
public sealed record OwlEquivalentDataPropertiesAxiom(NamedNode First, NamedNode Second): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: a data-property equivalence holds only data properties.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>A <c>DisjointDataProperties</c> axiom.</summary>
/// <param name="Operands">The mutually disjoint data properties.</param>
[DebuggerDisplay("DisjointDataProperties ({Operands.Count})")]
public sealed record OwlDisjointDataPropertiesAxiom(IReadOnlyList<NamedNode> Operands): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: a data-property disjointness holds only data properties.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>A <c>DataPropertyDomain</c> axiom.</summary>
/// <param name="Property">The data property.</param>
/// <param name="Domain">The domain class expression.</param>
[DebuggerDisplay("DataPropertyDomain")]
public sealed record OwlDataPropertyDomainAxiom(NamedNode Property, OwlClassExpression Domain): OwlAxiom
{
    /// <summary>Pushes the domain class expression; the data property mentions no individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        expressionsToAppendTo.Push(Domain);
    }
}

/// <summary>A <c>DataPropertyRange</c> axiom.</summary>
/// <param name="Property">The data property.</param>
/// <param name="Range">The range data range.</param>
[DebuggerDisplay("DataPropertyRange")]
public sealed record OwlDataPropertyRangeAxiom(NamedNode Property, OwlDataRange Range): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: a data-property range holds a data property and a data range.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>A <c>FunctionalDataProperty</c> axiom.</summary>
/// <param name="Property">The functional data property.</param>
[DebuggerDisplay("FunctionalDataProperty")]
public sealed record OwlFunctionalDataPropertyAxiom(NamedNode Property): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: a functional-data-property axiom holds only a data property.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>A <c>DatatypeDefinition</c> axiom.</summary>
/// <param name="Datatype">The defined datatype.</param>
/// <param name="Range">The defining data range.</param>
[DebuggerDisplay("DatatypeDefinition {Datatype}")]
public sealed record OwlDatatypeDefinitionAxiom(NamedNode Datatype, OwlDataRange Range): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: a datatype definition holds a datatype and a data range.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>A <c>HasKey</c> axiom.</summary>
/// <param name="Class">The keyed class expression.</param>
/// <param name="ObjectProperties">The object-property key components.</param>
/// <param name="DataProperties">The data-property key components.</param>
[DebuggerDisplay("HasKey")]
public sealed record OwlHasKeyAxiom(OwlClassExpression Class, IReadOnlyList<OwlObjectPropertyExpression> ObjectProperties, IReadOnlyList<NamedNode> DataProperties): OwlAxiom
{
    /// <summary>Pushes the keyed class expression; the key components are property references and mention no individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        expressionsToAppendTo.Push(Class);
    }
}

/// <summary>A <c>ClassAssertion</c> axiom.</summary>
/// <param name="Class">The asserted class expression.</param>
/// <param name="Individual">The individual (named or anonymous).</param>
[DebuggerDisplay("ClassAssertion")]
public sealed record OwlClassAssertionAxiom(OwlClassExpression Class, RdfTerm Individual): OwlAxiom
{
    /// <summary>Appends the asserted individual and pushes the asserted class expression.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        individualsToAppendTo.Add(Individual);
        expressionsToAppendTo.Push(Class);
    }
}

/// <summary>An <c>ObjectPropertyAssertion</c> axiom.</summary>
/// <param name="Source">The source individual.</param>
/// <param name="Property">The asserted property.</param>
/// <param name="Target">The target individual.</param>
[DebuggerDisplay("ObjectPropertyAssertion {Property}")]
public sealed record OwlObjectPropertyAssertionAxiom(RdfTerm Source, NamedNode Property, RdfTerm Target): OwlAxiom
{
    /// <summary>Appends the source and target individuals; the property mentions no individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        individualsToAppendTo.Add(Source);
        individualsToAppendTo.Add(Target);
    }
}

/// <summary>A <c>NegativeObjectPropertyAssertion</c> axiom.</summary>
/// <param name="Source">The source individual.</param>
/// <param name="Property">The denied property expression.</param>
/// <param name="Target">The target individual.</param>
[DebuggerDisplay("NegativeObjectPropertyAssertion")]
public sealed record OwlNegativeObjectPropertyAssertionAxiom(RdfTerm Source, OwlObjectPropertyExpression Property, RdfTerm Target): OwlAxiom
{
    /// <summary>Appends the source and target individuals; the property is a property expression and mentions no individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        individualsToAppendTo.Add(Source);
        individualsToAppendTo.Add(Target);
    }
}

/// <summary>A <c>DataPropertyAssertion</c> axiom.</summary>
/// <param name="Source">The source individual.</param>
/// <param name="Property">The asserted data property.</param>
/// <param name="Target">The target literal.</param>
[DebuggerDisplay("DataPropertyAssertion {Property}")]
public sealed record OwlDataPropertyAssertionAxiom(RdfTerm Source, NamedNode Property, Literal Target): OwlAxiom
{
    /// <summary>Appends the source individual; the target is a literal and the property mentions no individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        individualsToAppendTo.Add(Source);
    }
}

/// <summary>A <c>NegativeDataPropertyAssertion</c> axiom.</summary>
/// <param name="Source">The source individual.</param>
/// <param name="Property">The denied data property.</param>
/// <param name="Target">The denied literal value.</param>
[DebuggerDisplay("NegativeDataPropertyAssertion")]
public sealed record OwlNegativeDataPropertyAssertionAxiom(RdfTerm Source, NamedNode Property, Literal Target): OwlAxiom
{
    /// <summary>Appends the source individual; the target is a literal and the property mentions no individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        individualsToAppendTo.Add(Source);
    }
}

/// <summary>A <c>SameIndividual</c> axiom over one asserted pair.</summary>
/// <param name="First">The first individual.</param>
/// <param name="Second">The second individual.</param>
[DebuggerDisplay("SameIndividual")]
public sealed record OwlSameIndividualAxiom(RdfTerm First, RdfTerm Second): OwlAxiom
{
    /// <summary>Appends both asserted-equal individuals.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        individualsToAppendTo.Add(First);
        individualsToAppendTo.Add(Second);
    }
}

/// <summary>A <c>DifferentIndividuals</c> axiom (a pairwise <c>owl:differentFrom</c> yields two operands; an <c>owl:AllDifferent</c> yields its member list).</summary>
/// <param name="Individuals">The mutually distinct individuals.</param>
[DebuggerDisplay("DifferentIndividuals ({Individuals.Count})")]
public sealed record OwlDifferentIndividualsAxiom(IReadOnlyList<RdfTerm> Individuals): OwlAxiom
{
    /// <summary>Appends every asserted-distinct individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        individualsToAppendTo.AddRange(Individuals);
    }
}

/// <summary>An <c>AnnotationAssertion</c> axiom.</summary>
/// <param name="Subject">The annotated subject.</param>
/// <param name="Property">The annotation property.</param>
/// <param name="Value">The annotation value (IRI, anonymous individual, or literal).</param>
[DebuggerDisplay("AnnotationAssertion {Property}")]
public sealed record OwlAnnotationAssertionAxiom(RdfTerm Subject, NamedNode Property, RdfTerm Value): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: an annotation assertion is non-logical, so its subject and value are not logical individual positions.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>A <c>SubAnnotationPropertyOf</c> axiom.</summary>
/// <param name="SubProperty">The annotation subproperty.</param>
/// <param name="SuperProperty">The annotation superproperty.</param>
[DebuggerDisplay("SubAnnotationPropertyOf")]
public sealed record OwlSubAnnotationPropertyOfAxiom(NamedNode SubProperty, NamedNode SuperProperty): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: a sub-annotation-property axiom is non-logical and holds only annotation properties.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>An <c>AnnotationPropertyDomain</c> axiom.</summary>
/// <param name="Property">The annotation property.</param>
/// <param name="Domain">The domain IRI node.</param>
[DebuggerDisplay("AnnotationPropertyDomain")]
public sealed record OwlAnnotationPropertyDomainAxiom(NamedNode Property, NamedNode Domain): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: an annotation-property domain is non-logical and holds only IRIs.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>An <c>AnnotationPropertyRange</c> axiom.</summary>
/// <param name="Property">The annotation property.</param>
/// <param name="Range">The range IRI node.</param>
[DebuggerDisplay("AnnotationPropertyRange")]
public sealed record OwlAnnotationPropertyRangeAxiom(NamedNode Property, NamedNode Range): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: an annotation-property range is non-logical and holds only IRIs.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>An ontology import (<c>owl:imports</c>).</summary>
/// <param name="Imported">The imported ontology IRI node.</param>
[DebuggerDisplay("Import {Imported}")]
public sealed record OwlImportAxiom(NamedNode Imported): OwlAxiom
{
    /// <summary>Appends nothing and pushes nothing: an import references an ontology IRI and mentions no individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}
