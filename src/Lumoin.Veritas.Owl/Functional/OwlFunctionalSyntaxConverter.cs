using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Functional;

/// <summary>
/// Converts completed functional-syntax constructor groups into the
/// structural model, one ontology child at a time.
/// </summary>
/// <remarks>
/// The converter is the streaming back half of the functional-syntax reader:
/// the reader hands it each prefix declaration and each direct child of the
/// <c>Ontology(…)</c> group as its closing parenthesis arrives, and the
/// converter folds the subtree into axioms in post-order over an explicit
/// stack. Conversion state — the prefix table, the running axiom list, the
/// declaration census — persists across children, so the converter sees the
/// same document order a whole-buffer pass would.
/// </remarks>
internal sealed class OwlFunctionalSyntaxConverter
{
    /// <summary>Gets the bag every lexical, structural, and conversion diagnostic of the parse accumulates into.</summary>
    public DiagnosticBag Diagnostics { get; } = new();

    /// <summary>The source extent of the node currently converting — the span every conversion diagnostic carries.</summary>
    private SourceSpan CurrentSpan { get; set; }

    /// <summary>Gets the axioms converted so far, in document order.</summary>
    public ImmutableArray<OwlAxiom>.Builder Axioms { get; } = ImmutableArray.CreateBuilder<OwlAxiom>();

    /// <summary>Gets the ontology IRI, once the ontology header has supplied one.</summary>
    public NamedNode? OntologyIri { get; private set; }

    /// <summary>Gets the IRIs declared as classes.</summary>
    public HashSet<Utf8String> DeclaredClasses { get; } = [];

    /// <summary>Gets the IRIs declared as object properties.</summary>
    public HashSet<Utf8String> DeclaredObjectProperties { get; } = [];

    /// <summary>Gets the IRIs declared as data properties.</summary>
    public HashSet<Utf8String> DeclaredDataProperties { get; } = [];

    /// <summary>Gets the IRIs declared as annotation properties.</summary>
    public HashSet<Utf8String> DeclaredAnnotationProperties { get; } = [];

    /// <summary>Gets the IRIs declared as datatypes.</summary>
    public HashSet<Utf8String> DeclaredDatatypes { get; } = [];

    /// <summary>The prefix table the document's prefixed names resolve through; keys carry an eager hash for lookup.</summary>
    private Dictionary<Utf8String, Utf8String> Prefixes { get; } = [];

    /// <summary>Converted values per tree node, filled in post-order and cleared after each ontology child.</summary>
    private Dictionary<OwlFunctionalNode, object?> Converted { get; } = [];

    /// <summary>Whether the leading bare-IRI ontology header has ended for the current ontology group.</summary>
    private bool HeaderDone { get; set; }

    /// <summary>The synthetic origin every functional-syntax axiom carries — the syntax has no triples to anchor to.</summary>
    private Quad Origin { get; set; } = new(
        new NamedNode(Utf8Strings.From("urn:veritas:functional-syntax")),
        new NamedNode(Utf8Strings.From("urn:veritas:functional-syntax")),
        new NamedNode(Utf8Strings.From("urn:veritas:functional-syntax")),
        Graph: null);

    /// <summary>Registers a completed top-level <c>Prefix(…)</c> group into the prefix table.</summary>
    /// <param name="prefixGroup">The completed prefix group.</param>
    public void RegisterPrefix(OwlFunctionalNode prefixGroup)
    {
        //Prefix(p:=<iri>) tokenizes as Name(p:) Equals Iri.
        if(prefixGroup.Children.Count >= 3
            && prefixGroup.Children[0] is { IsAtom: true, Atom: { Kind: OwlFunctionalTokenKind.Name } prefixToken }
            && prefixGroup.Children[2] is { IsAtom: true, Atom: { Kind: OwlFunctionalTokenKind.Iri } iriToken })
        {
            Prefixes[TrimTrailingColons(prefixToken.Text)] = InternTerm(iriToken.Text.Span);
        }
    }

    /// <summary>Begins a top-level <c>Ontology(…)</c> group: the leading bare-IRI header is open again.</summary>
    public void BeginOntology()
    {
        HeaderDone = false;
    }

    /// <summary>
    /// Converts one direct child of the open <c>Ontology(…)</c> group: a
    /// header IRI, an import, an ontology annotation, or an axiom frame.
    /// </summary>
    /// <param name="child">The completed child node.</param>
    public void AcceptOntologyChild(OwlFunctionalNode child)
    {
        CurrentSpan = child.Span;

        //The leading bare IRIs are the ontology IRI and version IRI.
        if(!HeaderDone && child.IsAtom && child.Atom.Kind == OwlFunctionalTokenKind.Iri)
        {
            OntologyIri ??= new NamedNode(InternTerm(child.Atom.Text.Span));

            return;
        }

        HeaderDone = true;

        if(child.Head is Utf8String importHead && OwlFunctionalKeywords.IsImport(importHead)
            && child.Children.Count == 1 && child.Children[0].Atom.Kind == OwlFunctionalTokenKind.Iri)
        {
            Axioms.Add(new OwlImportAxiom(new NamedNode(InternTerm(child.Children[0].Atom.Text.Span))) { Origin = Origin });

            return;
        }

        if(child.Head is Utf8String annotationHead && OwlFunctionalKeywords.IsAnnotation(annotationHead))
        {
            //An ontology annotation surfaces as an annotation
            //assertion on the ontology IRI — the same triple the RDF
            //mapping carries it as. An anonymous ontology has no
            //subject to carry it, so the annotation is consumed.
            ConvertSubtree(child);
            if(OntologyIri is NamedNode ontologyIri && Converted[child] is OwlAnnotation ontologyAnnotation)
            {
                Axioms.Add(new OwlAnnotationAssertionAxiom(ontologyIri, ontologyAnnotation.Property, ontologyAnnotation.Value)
                {
                    Origin = Origin,
                    Annotations = ontologyAnnotation.Annotations
                });
            }

            Converted.Clear();

            return;
        }

        if(child.Head is null)
        {
            Diagnostics.Add(new Diagnostic(
                WellKnownDiagnostics.Owl.MalformedAxiomStructure,
                DiagnosticSeverity.Error,
                CurrentSpan,
                Utf8Strings.From("A bare group is not an axiom.")));

            return;
        }

        ConvertSubtree(child);
        ConvertAxiom(child);
        Converted.Clear();
    }

    //A declaration constructor's payload: the entity kind plus the IRI.
    private sealed record DeclarationValue(OwlEntityKind Kind, Utf8String Iri);

    //Converts a constructor subtree in post-order over an explicit
    //stack: a group constructs once every child has its value.
    private void ConvertSubtree(OwlFunctionalNode root)
    {
        Stack<OwlFunctionalNode> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            OwlFunctionalNode node = work.Peek();
            if(Converted.ContainsKey(node))
            {
                work.Pop();

                continue;
            }

            if(node.IsAtom)
            {
                CurrentSpan = node.Span;
                Converted[node] = ConvertAtom(node.Atom);
                work.Pop();

                continue;
            }

            bool ready = true;
            foreach(OwlFunctionalNode child in node.Children)
            {
                if(!Converted.ContainsKey(child))
                {
                    ready = false;
                    work.Push(child);
                }
            }

            if(!ready)
            {
                continue;
            }

            CurrentSpan = node.Span;
            Converted[node] = ConstructValue(node);
            work.Pop();
        }
    }

    private object? ConvertAtom(OwlFunctionalToken token)
    {
        return token.Kind switch
        {
            OwlFunctionalTokenKind.Iri => InternTerm(token.Text.Span),
            OwlFunctionalTokenKind.Name => ResolveName(token.Text) is Utf8String resolvedName ? resolvedName : null,
            OwlFunctionalTokenKind.BlankNode => new BlankNode(InternTerm(token.Text.Span)),
            OwlFunctionalTokenKind.Number => ParseNumber(token.Text.Span),
            OwlFunctionalTokenKind.Literal => MakeLiteral(token),
            _ => null
        };
    }

    private Utf8String? ResolveName(Utf8String name)
    {
        int colon = name.Span.IndexOf((byte)':');
        if(colon < 0)
        {
            //A bare name in atom position is a constructor misuse; report it.
            Diagnostics.Add(new Diagnostic(
                WellKnownDiagnostics.Owl.MalformedAxiomStructure,
                DiagnosticSeverity.Error,
                CurrentSpan,
                Utf8Strings.From($"Unresolvable name '{name}'.")));

            return null;
        }

        Utf8String prefix = new(name.Memory[..colon]);
        ReadOnlyMemory<byte> local = name.Memory[(colon + 1)..];

        if(Prefixes.TryGetValue(prefix, out Utf8String expansion))
        {
            return Concat(expansion.Span, local.Span);
        }

        Diagnostics.Add(new Diagnostic(
            WellKnownDiagnostics.Owl.MalformedAxiomStructure,
            DiagnosticSeverity.Error,
            CurrentSpan,
            Utf8Strings.From($"Undeclared prefix '{prefix}:'.")));

        return null;
    }

    private Literal MakeLiteral(OwlFunctionalToken token)
    {
        if(token.LiteralLanguage is Utf8String language)
        {
            return new Literal(token.Text, new NamedNode(Vocabulary.Rdf.LangString), InternTerm(language.Span));
        }

        Utf8String datatype;
        if(token.LiteralDatatype is not Utf8String datatypeText)
        {
            datatype = Vocabulary.Xsd.String;
        }
        else if(datatypeText.Length > 0 && datatypeText.Span[0] == (byte)'<')
        {
            //The lexer marks a <…> datatype IRI with a leading '<'; strip it.
            datatype = InternTerm(datatypeText.Span[1..]);
        }
        else
        {
            datatype = ResolveName(datatypeText) is Utf8String resolved ? resolved : Vocabulary.Xsd.String;
        }

        return new Literal(token.Text, new NamedNode(datatype));
    }

    //Constructs the value of a headed (or bare) group from its
    //already-converted children.
    private object? ConstructValue(OwlFunctionalNode node)
    {
        List<object?> children = new(node.Children.Count);
        foreach(OwlFunctionalNode child in node.Children)
        {
            children.Add(Converted[child]);
        }

        if(node.Head is not Utf8String headValue)
        {
            return children;
        }

        OwlFunctionalKeyword keyword = OwlFunctionalKeywords.Resolve(headValue);

        return keyword switch
        {
            OwlFunctionalKeyword.Annotation => BuildAnnotation(children),
            OwlFunctionalKeyword.Class when children is [Utf8String classIri] => new DeclarationValue(OwlEntityKind.Class, classIri),
            OwlFunctionalKeyword.Datatype when children is [Utf8String datatypeIri] => new DeclarationValue(OwlEntityKind.Datatype, datatypeIri),
            OwlFunctionalKeyword.ObjectProperty when children is [Utf8String objectPropertyIri] => new DeclarationValue(OwlEntityKind.ObjectProperty, objectPropertyIri),
            OwlFunctionalKeyword.DataProperty when children is [Utf8String dataPropertyIri] => new DeclarationValue(OwlEntityKind.DataProperty, dataPropertyIri),
            OwlFunctionalKeyword.AnnotationProperty when children is [Utf8String annotationPropertyIri] => new DeclarationValue(OwlEntityKind.AnnotationProperty, annotationPropertyIri),
            OwlFunctionalKeyword.NamedIndividual when children is [Utf8String individualIri] => new DeclarationValue(OwlEntityKind.NamedIndividual, individualIri),
            OwlFunctionalKeyword.ObjectInverseOf when AsObjectProperty(children[0]) is OwlObjectPropertyReference inverted => new OwlInverseObjectProperty(inverted.Named),
            OwlFunctionalKeyword.ObjectIntersectionOf => new OwlObjectIntersectionOf(AsClassList(children)),
            OwlFunctionalKeyword.ObjectUnionOf => new OwlObjectUnionOf(AsClassList(children)),
            OwlFunctionalKeyword.ObjectComplementOf => new OwlObjectComplementOf(AsClass(children[0])),
            OwlFunctionalKeyword.ObjectOneOf => new OwlObjectOneOf(AsIndividualList(children)),
            OwlFunctionalKeyword.ObjectSomeValuesFrom => new OwlObjectSomeValuesFrom(AsObjectProperty(children[0]), AsClass(children[1])),
            OwlFunctionalKeyword.ObjectAllValuesFrom => new OwlObjectAllValuesFrom(AsObjectProperty(children[0]), AsClass(children[1])),
            OwlFunctionalKeyword.ObjectHasValue => new OwlObjectHasValue(AsObjectProperty(children[0]), AsIndividual(children[1])),
            OwlFunctionalKeyword.ObjectHasSelf => new OwlObjectHasSelf(AsObjectProperty(children[0])),
            OwlFunctionalKeyword.ObjectMinCardinality or OwlFunctionalKeyword.ObjectMaxCardinality or OwlFunctionalKeyword.ObjectExactCardinality => BuildObjectCardinality(keyword, children),
            OwlFunctionalKeyword.DataSomeValuesFrom => new OwlDataSomeValuesFrom([AsDataProperty(children[0])], AsDataRange(children[1])),
            OwlFunctionalKeyword.DataAllValuesFrom => new OwlDataAllValuesFrom([AsDataProperty(children[0])], AsDataRange(children[1])),
            OwlFunctionalKeyword.DataHasValue when children[1] is Literal dataValue => new OwlDataHasValue(AsDataProperty(children[0]), dataValue),
            OwlFunctionalKeyword.DataMinCardinality or OwlFunctionalKeyword.DataMaxCardinality or OwlFunctionalKeyword.DataExactCardinality => BuildDataCardinality(keyword, children),
            OwlFunctionalKeyword.DataIntersectionOf => new OwlDataIntersectionOf(AsDataRangeList(children)),
            OwlFunctionalKeyword.DataUnionOf => new OwlDataUnionOf(AsDataRangeList(children)),
            OwlFunctionalKeyword.DataComplementOf => new OwlDataComplementOf(AsDataRange(children[0])),
            OwlFunctionalKeyword.DataOneOf => BuildDataOneOf(children),
            OwlFunctionalKeyword.DatatypeRestriction => BuildDatatypeRestriction(children),
            OwlFunctionalKeyword.ObjectPropertyChain => BuildObjectPropertyChain(children),

            //Axiom heads construct in ConvertAxiom; leave the
            //converted children accessible through the group node.
            _ => children
        };
    }

    /// <summary>Builds an annotation frame: nested annotations, then the annotation property and value.</summary>
    /// <param name="children">The frame's converted children.</param>
    /// <returns>The <see cref="OwlAnnotation"/>, or <see langword="null"/> when the parts do not form one.</returns>
    private OwlAnnotation? BuildAnnotation(List<object?> children)
    {
        ImmutableArray<OwlAnnotation>.Builder? nested = null;
        List<object?> parts = new(children.Count);
        foreach(object? childValue in children)
        {
            if(childValue is OwlAnnotation nestedAnnotation)
            {
                nested ??= ImmutableArray.CreateBuilder<OwlAnnotation>();
                nested.Add(nestedAnnotation);
            }
            else
            {
                parts.Add(childValue);
            }
        }

        return parts is [Utf8String annotationProperty, var annotationValue]
            ? new OwlAnnotation(new NamedNode(annotationProperty), AsAnnotationValue(annotationValue)) { Annotations = nested?.ToImmutable() ?? [] }
            : null;
    }

    /// <summary>Builds an object cardinality from the bound, property, and optional qualifying class.</summary>
    /// <param name="keyword">The min/max/exact cardinality keyword.</param>
    /// <param name="children">The expression's converted children.</param>
    /// <returns>The constructed cardinality.</returns>
    private OwlObjectCardinality BuildObjectCardinality(OwlFunctionalKeyword keyword, List<object?> children)
    {
        OwlCardinalityKind kind = keyword switch
        {
            OwlFunctionalKeyword.ObjectMinCardinality => OwlCardinalityKind.Min,
            OwlFunctionalKeyword.ObjectMaxCardinality => OwlCardinalityKind.Max,
            _ => OwlCardinalityKind.Exact
        };

        return new OwlObjectCardinality(
            kind,
            children[0] is int bound ? bound : 0,
            AsObjectProperty(children[1]),
            children.Count > 2 ? AsClass(children[2]) : null);
    }

    /// <summary>Builds a data cardinality from the bound, property, and optional qualifying data range.</summary>
    /// <param name="keyword">The min/max/exact cardinality keyword.</param>
    /// <param name="children">The expression's converted children.</param>
    /// <returns>The constructed cardinality.</returns>
    private OwlDataCardinality BuildDataCardinality(OwlFunctionalKeyword keyword, List<object?> children)
    {
        OwlCardinalityKind kind = keyword switch
        {
            OwlFunctionalKeyword.DataMinCardinality => OwlCardinalityKind.Min,
            OwlFunctionalKeyword.DataMaxCardinality => OwlCardinalityKind.Max,
            _ => OwlCardinalityKind.Exact
        };

        return new OwlDataCardinality(
            kind,
            children[0] is int bound ? bound : 0,
            AsDataProperty(children[1]),
            children.Count > 2 ? AsDataRange(children[2]) : null);
    }

    /// <summary>Collects the literal members of a <c>DataOneOf</c> enumeration.</summary>
    /// <param name="children">The enumeration's converted children.</param>
    /// <returns>The constructed data enumeration.</returns>
    private static OwlDataOneOf BuildDataOneOf(List<object?> children)
    {
        List<Literal> literals = [];
        foreach(object? child in children)
        {
            if(child is Literal literal)
            {
                literals.Add(literal);
            }
        }

        return new OwlDataOneOf(literals);
    }

    /// <summary>Builds a datatype restriction from the base datatype IRI and the facet/value pairs.</summary>
    /// <param name="children">The restriction's converted children.</param>
    /// <returns>The constructed datatype restriction.</returns>
    private OwlDatatypeRestriction BuildDatatypeRestriction(List<object?> children)
    {
        List<OwlFacetRestriction> facets = [];
        for(int i = 1; i + 1 < children.Count; i += 2)
        {
            if(children[i] is Utf8String facet && children[i + 1] is Literal value)
            {
                facets.Add(new OwlFacetRestriction(new NamedNode(facet), value));
            }
        }

        return new OwlDatatypeRestriction(new NamedNode(AsIri(children[0])), facets);
    }

    /// <summary>Collects an object-property chain's links.</summary>
    /// <param name="children">The chain's converted children.</param>
    /// <returns>The ordered property-expression links.</returns>
    private List<OwlObjectPropertyExpression> BuildObjectPropertyChain(List<object?> children)
    {
        List<OwlObjectPropertyExpression> links = [];
        foreach(object? child in children)
        {
            links.Add(AsObjectProperty(child));
        }

        return links;
    }

    //Constructs the axiom for a top-level constructor group whose
    //subtree is converted.
    private void ConvertAxiom(OwlFunctionalNode node)
    {
        CurrentSpan = node.Span;

        if(Converted[node] is DeclarationValue || node.Head is null)
        {
            //A bare Class(:A) at axiom position would be malformed; the
            //Declaration head handles the well-formed case below.
            return;
        }

        List<object?> raw = Converted[node] is List<object?> converted ? converted : [Converted[node]];

        //Axiom-frame annotations precede the logical arguments; they
        //attach to every axiom the frame produces (a pairwise expansion
        //carries the frame's annotations on each pair).
        ImmutableArray<OwlAnnotation>.Builder? annotations = null;
        List<object?> children = new(raw.Count);
        foreach(object? value in raw)
        {
            if(value is OwlAnnotation annotation)
            {
                annotations ??= ImmutableArray.CreateBuilder<OwlAnnotation>();
                annotations.Add(annotation);
            }
            else
            {
                children.Add(value);
            }
        }

        int firstAxiom = Axioms.Count;

        OwlFunctionalKeyword keyword = OwlFunctionalKeywords.Resolve(node.Head.GetValueOrDefault());

        switch(keyword)
        {
            case OwlFunctionalKeyword.Declaration when children is [DeclarationValue declaration]:
            {
                AddDeclaration(declaration);
                break;
            }
            case OwlFunctionalKeyword.SubClassOf when children.Count == 2:
            {
                Axioms.Add(new OwlSubClassOfAxiom(AsClass(children[0]), AsClass(children[1])) { Origin = Origin });
                break;
            }
            case(OwlFunctionalKeyword.EquivalentClasses):
            {
                foreach((object? first, object? second) in Pairs(children))
                {
                    Axioms.Add(new OwlEquivalentClassesAxiom(AsClass(first), AsClass(second)) { Origin = Origin });
                }

                break;
            }
            case(OwlFunctionalKeyword.DisjointClasses):
            {
                Axioms.Add(new OwlDisjointClassesAxiom(AsClassList(children)) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.DisjointUnion when children.Count >= 2 && children[0] is Utf8String unionClass:
            {
                Axioms.Add(new OwlDisjointUnionAxiom(new NamedNode(unionClass), AsClassList(children[1..])) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.SubObjectPropertyOf when children.Count == 2:
            {
                if(children[0] is List<OwlObjectPropertyExpression> chain)
                {
                    Axioms.Add(new OwlPropertyChainAxiom(chain, AsObjectProperty(children[1])) { Origin = Origin });
                }
                else
                {
                    Axioms.Add(new OwlSubObjectPropertyOfAxiom(AsObjectProperty(children[0]), AsObjectProperty(children[1])) { Origin = Origin });
                }

                break;
            }
            case(OwlFunctionalKeyword.EquivalentObjectProperties):
            {
                foreach((object? first, object? second) in Pairs(children))
                {
                    Axioms.Add(new OwlEquivalentObjectPropertiesAxiom(AsObjectProperty(first), AsObjectProperty(second)) { Origin = Origin });
                }

                break;
            }
            case(OwlFunctionalKeyword.DisjointObjectProperties):
            {
                List<OwlObjectPropertyExpression> properties = [];
                foreach(object? child in children)
                {
                    properties.Add(AsObjectProperty(child));
                }

                Axioms.Add(new OwlDisjointObjectPropertiesAxiom(properties) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.InverseObjectProperties when children.Count == 2:
            {
                Axioms.Add(new OwlInverseObjectPropertiesAxiom(AsObjectProperty(children[0]), AsObjectProperty(children[1])) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.ObjectPropertyDomain when children.Count == 2:
            {
                Axioms.Add(new OwlObjectPropertyDomainAxiom(AsObjectProperty(children[0]), AsClass(children[1])) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.ObjectPropertyRange when children.Count == 2:
            {
                Axioms.Add(new OwlObjectPropertyRangeAxiom(AsObjectProperty(children[0]), AsClass(children[1])) { Origin = Origin });
                break;
            }
            case(OwlFunctionalKeyword.FunctionalObjectProperty):
            case(OwlFunctionalKeyword.InverseFunctionalObjectProperty):
            case(OwlFunctionalKeyword.TransitiveObjectProperty):
            case(OwlFunctionalKeyword.SymmetricObjectProperty):
            case(OwlFunctionalKeyword.AsymmetricObjectProperty):
            case(OwlFunctionalKeyword.ReflexiveObjectProperty):
            case(OwlFunctionalKeyword.IrreflexiveObjectProperty):
            {
                OwlPropertyCharacteristic characteristic = keyword switch
                {
                    OwlFunctionalKeyword.FunctionalObjectProperty => OwlPropertyCharacteristic.Functional,
                    OwlFunctionalKeyword.InverseFunctionalObjectProperty => OwlPropertyCharacteristic.InverseFunctional,
                    OwlFunctionalKeyword.TransitiveObjectProperty => OwlPropertyCharacteristic.Transitive,
                    OwlFunctionalKeyword.SymmetricObjectProperty => OwlPropertyCharacteristic.Symmetric,
                    OwlFunctionalKeyword.AsymmetricObjectProperty => OwlPropertyCharacteristic.Asymmetric,
                    OwlFunctionalKeyword.ReflexiveObjectProperty => OwlPropertyCharacteristic.Reflexive,
                    _ => OwlPropertyCharacteristic.Irreflexive
                };

                Axioms.Add(new OwlObjectPropertyCharacteristicAxiom(characteristic, AsObjectProperty(children[0])) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.SubDataPropertyOf when children.Count == 2:
            {
                Axioms.Add(new OwlSubDataPropertyOfAxiom(AsDataProperty(children[0]), AsDataProperty(children[1])) { Origin = Origin });
                break;
            }
            case(OwlFunctionalKeyword.EquivalentDataProperties):
            {
                foreach((object? first, object? second) in Pairs(children))
                {
                    Axioms.Add(new OwlEquivalentDataPropertiesAxiom(AsDataProperty(first), AsDataProperty(second)) { Origin = Origin });
                }

                break;
            }
            case(OwlFunctionalKeyword.DisjointDataProperties):
            {
                List<NamedNode> properties = [];
                foreach(object? child in children)
                {
                    properties.Add(AsDataProperty(child));
                }

                Axioms.Add(new OwlDisjointDataPropertiesAxiom(properties) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.DataPropertyDomain when children.Count == 2:
            {
                Axioms.Add(new OwlDataPropertyDomainAxiom(AsDataProperty(children[0]), AsClass(children[1])) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.DataPropertyRange when children.Count == 2:
            {
                Axioms.Add(new OwlDataPropertyRangeAxiom(AsDataProperty(children[0]), AsDataRange(children[1])) { Origin = Origin });
                break;
            }
            case(OwlFunctionalKeyword.FunctionalDataProperty):
            {
                Axioms.Add(new OwlFunctionalDataPropertyAxiom(AsDataProperty(children[0])) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.DatatypeDefinition when children.Count == 2:
            {
                Axioms.Add(new OwlDatatypeDefinitionAxiom(new NamedNode(AsIri(children[0])), AsDataRange(children[1])) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.HasKey when children.Count >= 1:
            {
                //HasKey(CE (opes…) (dpes…)): the bare groups carry the
                //key components. An IRI key with no declaration basis
                //counts as an object property.
                List<OwlObjectPropertyExpression> objectKeys = [];
                List<NamedNode> dataKeys = [];
                for(int i = 1; i < children.Count; i++)
                {
                    if(children[i] is not List<object?> group)
                    {
                        continue;
                    }

                    foreach(object? key in group)
                    {
                        if(key is Utf8String keyIri && DeclaredDataProperties.Contains(keyIri))
                        {
                            dataKeys.Add(new NamedNode(keyIri));
                        }
                        else
                        {
                            objectKeys.Add(AsObjectProperty(key));
                        }
                    }
                }

                Axioms.Add(new OwlHasKeyAxiom(AsClass(children[0]), objectKeys, dataKeys) { Origin = Origin });
                break;
            }
            case(OwlFunctionalKeyword.SameIndividual):
            {
                foreach((object? first, object? second) in Pairs(children))
                {
                    Axioms.Add(new OwlSameIndividualAxiom(AsIndividual(first), AsIndividual(second)) { Origin = Origin });
                }

                break;
            }
            case(OwlFunctionalKeyword.DifferentIndividuals):
            {
                Axioms.Add(new OwlDifferentIndividualsAxiom(AsIndividualList(children)) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.ClassAssertion when children.Count == 2:
            {
                Axioms.Add(new OwlClassAssertionAxiom(AsClass(children[0]), AsIndividual(children[1])) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.ObjectPropertyAssertion when children.Count == 3 && AsObjectProperty(children[0]) is OwlObjectPropertyReference assertedProperty:
            {
                Axioms.Add(new OwlObjectPropertyAssertionAxiom(AsIndividual(children[1]), assertedProperty.Named, AsIndividual(children[2])) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.NegativeObjectPropertyAssertion when children.Count == 3:
            {
                Axioms.Add(new OwlNegativeObjectPropertyAssertionAxiom(AsIndividual(children[1]), AsObjectProperty(children[0]), AsIndividual(children[2])) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.DataPropertyAssertion when children.Count == 3 && children[2] is Literal dataValue:
            {
                Axioms.Add(new OwlDataPropertyAssertionAxiom(AsIndividual(children[1]), AsDataProperty(children[0]), dataValue) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.NegativeDataPropertyAssertion when children.Count == 3 && children[2] is Literal negativeValue:
            {
                Axioms.Add(new OwlNegativeDataPropertyAssertionAxiom(AsIndividual(children[1]), AsDataProperty(children[0]), negativeValue) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.AnnotationAssertion when children.Count == 3:
            {
                Axioms.Add(new OwlAnnotationAssertionAxiom(AsIndividual(children[1]), new NamedNode(AsIri(children[0])), AsAnnotationValue(children[2])) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.SubAnnotationPropertyOf when children.Count == 2:
            {
                Axioms.Add(new OwlSubAnnotationPropertyOfAxiom(new NamedNode(AsIri(children[0])), new NamedNode(AsIri(children[1]))) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.AnnotationPropertyDomain when children.Count == 2:
            {
                Axioms.Add(new OwlAnnotationPropertyDomainAxiom(new NamedNode(AsIri(children[0])), new NamedNode(AsIri(children[1]))) { Origin = Origin });
                break;
            }
            case OwlFunctionalKeyword.AnnotationPropertyRange when children.Count == 2:
            {
                Axioms.Add(new OwlAnnotationPropertyRangeAxiom(new NamedNode(AsIri(children[0])), new NamedNode(AsIri(children[1]))) { Origin = Origin });
                break;
            }
            default:
            {
                Diagnostics.Add(new Diagnostic(
                    WellKnownDiagnostics.Owl.UnsupportedConstruct,
                    DiagnosticSeverity.Error,
                    CurrentSpan,
                    Utf8Strings.From($"Unrecognised axiom constructor '{node.Head.GetValueOrDefault()}'.")));
                break;
            }
        }

        if(annotations is not null)
        {
            ImmutableArray<OwlAnnotation> attached = annotations.ToImmutable();
            for(int i = firstAxiom; i < Axioms.Count; i++)
            {
                Axioms[i] = Axioms[i] with { Annotations = attached };
            }
        }
    }

    private void AddDeclaration(DeclarationValue declaration)
    {
        Axioms.Add(new OwlDeclarationAxiom(declaration.Kind, new NamedNode(declaration.Iri)) { Origin = Origin });

        HashSet<Utf8String> target = declaration.Kind switch
        {
            OwlEntityKind.Class => DeclaredClasses,
            OwlEntityKind.Datatype => DeclaredDatatypes,
            OwlEntityKind.ObjectProperty => DeclaredObjectProperties,
            OwlEntityKind.DataProperty => DeclaredDataProperties,
            OwlEntityKind.AnnotationProperty => DeclaredAnnotationProperties,
            _ => DeclaredClasses
        };

        if(declaration.Kind != OwlEntityKind.NamedIndividual)
        {
            target.Add(declaration.Iri);
        }
    }

    private static IEnumerable<(object? First, object? Second)> Pairs(List<object?> values)
    {
        for(int i = 0; i < values.Count; i++)
        {
            for(int j = i + 1; j < values.Count; j++)
            {
                yield return (values[i], values[j]);
            }
        }
    }

    /// <summary>Copies bytes into an eager-hash term value detached from the reader's buffer.</summary>
    /// <param name="bytes">The UTF-8 bytes of the term.</param>
    /// <returns>A <see cref="Utf8String"/> over a fresh copy, with its hash precomputed for dictionary use.</returns>
    private static Utf8String InternTerm(ReadOnlySpan<byte> bytes)
    {
        return new Utf8String(bytes.ToArray());
    }

    /// <summary>Joins a prefix expansion and a local name into one eager-hash term value.</summary>
    /// <param name="expansion">The namespace expansion bytes.</param>
    /// <param name="local">The local-name bytes.</param>
    /// <returns>A <see cref="Utf8String"/> over the concatenation.</returns>
    private static Utf8String Concat(ReadOnlySpan<byte> expansion, ReadOnlySpan<byte> local)
    {
        byte[] joined = new byte[expansion.Length + local.Length];
        expansion.CopyTo(joined);
        local.CopyTo(joined.AsSpan(expansion.Length));

        return new Utf8String(joined);
    }

    /// <summary>Drops any trailing <c>:</c> bytes from a prefix name, yielding an eager-hash lookup key.</summary>
    /// <param name="name">The prefix token's text, including its trailing colon.</param>
    /// <returns>The prefix without trailing colons, with its hash precomputed for dictionary use.</returns>
    private static Utf8String TrimTrailingColons(Utf8String name)
    {
        ReadOnlyMemory<byte> memory = name.Memory;
        int end = memory.Length;
        while(end > 0 && memory.Span[end - 1] == (byte)':')
        {
            end--;
        }

        return new Utf8String(memory[..end]);
    }

    /// <summary>Parses a nonnegative integer cardinality bound from its UTF-8 digits.</summary>
    /// <param name="text">The token's digit bytes.</param>
    /// <returns>The parsed value, or zero if it does not fit.</returns>
    private static int ParseNumber(ReadOnlySpan<byte> text)
    {
        return Utf8Parser.TryParse(text, out int value, out _) ? value : 0;
    }

    //Coercions: atoms resolve per the requesting context.

    private Utf8String AsIri(object? value)
    {
        if(value is Utf8String iri)
        {
            return iri;
        }

        Diagnostics.Add(new Diagnostic(
            WellKnownDiagnostics.Owl.MalformedAxiomStructure,
            DiagnosticSeverity.Error,
            CurrentSpan,
            Utf8Strings.From("An IRI was expected.")));

        return Utf8Strings.From("urn:veritas:invalid");
    }

    private OwlClassExpression AsClass(object? value)
    {
        return value switch
        {
            OwlClassExpression expression => expression,
            Utf8String iri => new OwlClassReference(new NamedNode(iri)),
            _ => Invalid()
        };

        OwlClassExpression Invalid()
        {
            Diagnostics.Add(new Diagnostic(
                WellKnownDiagnostics.Owl.MalformedClassExpression,
                DiagnosticSeverity.Error,
                CurrentSpan,
                Utf8Strings.From("A class expression was expected.")));

            return new OwlClassReference(new NamedNode(Utf8Strings.From("urn:veritas:invalid")));
        }
    }

    private List<OwlClassExpression> AsClassList(List<object?> values)
    {
        List<OwlClassExpression> expressions = new(values.Count);
        foreach(object? value in values)
        {
            expressions.Add(AsClass(value));
        }

        return expressions;
    }

    private List<OwlDataRange> AsDataRangeList(List<object?> values)
    {
        List<OwlDataRange> ranges = new(values.Count);
        foreach(object? value in values)
        {
            ranges.Add(AsDataRange(value));
        }

        return ranges;
    }

    private OwlObjectPropertyExpression AsObjectProperty(object? value)
    {
        return value switch
        {
            OwlObjectPropertyExpression expression => expression,
            Utf8String iri => new OwlObjectPropertyReference(new NamedNode(iri)),
            _ => Invalid()
        };

        OwlObjectPropertyExpression Invalid()
        {
            Diagnostics.Add(new Diagnostic(
                WellKnownDiagnostics.Owl.MalformedAxiomStructure,
                DiagnosticSeverity.Error,
                CurrentSpan,
                Utf8Strings.From("An object property expression was expected.")));

            return new OwlObjectPropertyReference(new NamedNode(Utf8Strings.From("urn:veritas:invalid")));
        }
    }

    private NamedNode AsDataProperty(object? value)
    {
        return new NamedNode(AsIri(value));
    }

    private OwlDataRange AsDataRange(object? value)
    {
        return value switch
        {
            OwlDataRange range => range,
            Utf8String iri => new OwlDatatypeReference(new NamedNode(iri)),
            _ => Invalid()
        };

        OwlDataRange Invalid()
        {
            Diagnostics.Add(new Diagnostic(
                WellKnownDiagnostics.Owl.MalformedClassExpression,
                DiagnosticSeverity.Error,
                CurrentSpan,
                Utf8Strings.From("A data range was expected.")));

            return new OwlDatatypeReference(new NamedNode(Utf8Strings.From("urn:veritas:invalid")));
        }
    }

    private RdfTerm AsIndividual(object? value)
    {
        return value switch
        {
            BlankNode blank => blank,
            Utf8String iri => new NamedNode(iri),
            _ => InvalidIndividual()
        };

        RdfTerm InvalidIndividual()
        {
            Diagnostics.Add(new Diagnostic(
                WellKnownDiagnostics.Owl.MalformedAxiomStructure,
                DiagnosticSeverity.Error,
                CurrentSpan,
                Utf8Strings.From("An individual was expected.")));

            return new NamedNode(Utf8Strings.From("urn:veritas:invalid"));
        }
    }

    private RdfTerm AsAnnotationValue(object? value)
    {
        return value switch
        {
            Literal literal => literal,
            BlankNode blank => blank,
            Utf8String iri => new NamedNode(iri),
            _ => AsIndividual(value)
        };
    }

    private List<RdfTerm> AsIndividualList(List<object?> values)
    {
        List<RdfTerm> individuals = new(values.Count);
        foreach(object? value in values)
        {
            individuals.Add(AsIndividual(value));
        }

        return individuals;
    }
}
