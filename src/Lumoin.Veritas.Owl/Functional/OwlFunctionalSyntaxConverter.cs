using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
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

    /// <summary>The prefix table the document's prefixed names resolve through.</summary>
    private Dictionary<string, string> Prefixes { get; } = [];

    /// <summary>Converted values per tree node, filled in post-order and cleared after each ontology child.</summary>
    private Dictionary<OwlFunctionalNode, object?> Converted { get; } = [];

    /// <summary>Whether the leading bare-IRI ontology header has ended for the current ontology group.</summary>
    private bool HeaderDone { get; set; }

    /// <summary>The synthetic origin every functional-syntax axiom carries — the syntax has no triples to anchor to.</summary>
    private Quad Origin { get; set; } = new(
        new NamedNode(Utf8String.FromString("urn:veritas:functional-syntax")),
        new NamedNode(Utf8String.FromString("urn:veritas:functional-syntax")),
        new NamedNode(Utf8String.FromString("urn:veritas:functional-syntax")),
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
            Prefixes[prefixToken.Text.TrimEnd(':')] = iriToken.Text;
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
            OntologyIri ??= new NamedNode(Utf8String.FromString(child.Atom.Text));

            return;
        }

        HeaderDone = true;

        if(child.Head == "Import" && child.Children.Count == 1 && child.Children[0].Atom.Kind == OwlFunctionalTokenKind.Iri)
        {
            Axioms.Add(new OwlImportAxiom(new NamedNode(Utf8String.FromString(child.Children[0].Atom.Text))) { Origin = Origin });

            return;
        }

        if(child.Head == "Annotation")
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
                Utf8String.FromString("A bare group is not an axiom.")));

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
            OwlFunctionalTokenKind.Iri => Utf8String.FromString(token.Text),
            OwlFunctionalTokenKind.Name => ResolveName(token.Text) is Utf8String resolvedName ? resolvedName : null,
            OwlFunctionalTokenKind.BlankNode => new BlankNode(Utf8String.FromString(token.Text)),
            OwlFunctionalTokenKind.Number => int.Parse(token.Text, NumberStyles.None, CultureInfo.InvariantCulture),
            OwlFunctionalTokenKind.Literal => MakeLiteral(token),
            _ => null
        };
    }

    private Utf8String? ResolveName(string name)
    {
        int colon = name.IndexOf(':', StringComparison.Ordinal);
        if(colon < 0)
        {
            //A bare name in atom position is a constructor misuse; report it.
            Diagnostics.Add(new Diagnostic(
                WellKnownDiagnostics.Owl.MalformedAxiomStructure,
                DiagnosticSeverity.Error,
                CurrentSpan,
                Utf8String.FromString($"Unresolvable name '{name}'.")));

            return null;
        }

        string prefix = name[..colon];
        string local = name[(colon + 1)..];

        if(Prefixes.TryGetValue(prefix, out string? expansion))
        {
            return Utf8String.FromString(expansion + local);
        }

        Diagnostics.Add(new Diagnostic(
            WellKnownDiagnostics.Owl.MalformedAxiomStructure,
            DiagnosticSeverity.Error,
            CurrentSpan,
            Utf8String.FromString($"Undeclared prefix '{prefix}:'.")));

        return null;
    }

    private Literal MakeLiteral(OwlFunctionalToken token)
    {
        if(token.LiteralLanguage is string language)
        {
            return new Literal(Utf8String.FromString(token.Text), new NamedNode(Vocabulary.Rdf.LangString), Utf8String.FromString(language));
        }

        Utf8String datatype = token.LiteralDatatype switch
        {
            null => Vocabulary.Xsd.String,
            ['<', .. var full] => Utf8String.FromString(full),
            _ => ResolveName(token.LiteralDatatype) is Utf8String resolved ? resolved : Vocabulary.Xsd.String
        };

        return new Literal(Utf8String.FromString(token.Text), new NamedNode(datatype));
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

        switch(node.Head)
        {
            case null:
            {
                return children;
            }
            case "Annotation":
            {
                //An annotation frame: optional nested annotations, then
                //the annotation property and the value.
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
            case "Class" when children is [Utf8String classIri]:
            {
                return new DeclarationValue(OwlEntityKind.Class, classIri);
            }
            case "Datatype" when children is [Utf8String datatypeIri]:
            {
                return new DeclarationValue(OwlEntityKind.Datatype, datatypeIri);
            }
            case "ObjectProperty" when children is [Utf8String objectPropertyIri]:
            {
                return new DeclarationValue(OwlEntityKind.ObjectProperty, objectPropertyIri);
            }
            case "DataProperty" when children is [Utf8String dataPropertyIri]:
            {
                return new DeclarationValue(OwlEntityKind.DataProperty, dataPropertyIri);
            }
            case "AnnotationProperty" when children is [Utf8String annotationPropertyIri]:
            {
                return new DeclarationValue(OwlEntityKind.AnnotationProperty, annotationPropertyIri);
            }
            case "NamedIndividual" when children is [Utf8String individualIri]:
            {
                return new DeclarationValue(OwlEntityKind.NamedIndividual, individualIri);
            }
            case "ObjectInverseOf" when AsObjectProperty(children[0]) is OwlObjectPropertyReference inverted:
            {
                return new OwlInverseObjectProperty(inverted.Named);
            }
            case "ObjectIntersectionOf":
            {
                return new OwlObjectIntersectionOf(AsClassList(children));
            }
            case "ObjectUnionOf":
            {
                return new OwlObjectUnionOf(AsClassList(children));
            }
            case "ObjectComplementOf":
            {
                return new OwlObjectComplementOf(AsClass(children[0]));
            }
            case "ObjectOneOf":
            {
                return new OwlObjectOneOf(AsIndividualList(children));
            }
            case "ObjectSomeValuesFrom":
            {
                return new OwlObjectSomeValuesFrom(AsObjectProperty(children[0]), AsClass(children[1]));
            }
            case "ObjectAllValuesFrom":
            {
                return new OwlObjectAllValuesFrom(AsObjectProperty(children[0]), AsClass(children[1]));
            }
            case "ObjectHasValue":
            {
                return new OwlObjectHasValue(AsObjectProperty(children[0]), AsIndividual(children[1]));
            }
            case "ObjectHasSelf":
            {
                return new OwlObjectHasSelf(AsObjectProperty(children[0]));
            }
            case "ObjectMinCardinality" or "ObjectMaxCardinality" or "ObjectExactCardinality":
            {
                OwlCardinalityKind kind = node.Head switch
                {
                    "ObjectMinCardinality" => OwlCardinalityKind.Min,
                    "ObjectMaxCardinality" => OwlCardinalityKind.Max,
                    _ => OwlCardinalityKind.Exact
                };

                return new OwlObjectCardinality(
                    kind,
                    children[0] is int bound ? bound : 0,
                    AsObjectProperty(children[1]),
                    children.Count > 2 ? AsClass(children[2]) : null);
            }
            case "DataSomeValuesFrom":
            {
                return new OwlDataSomeValuesFrom([AsDataProperty(children[0])], AsDataRange(children[1]));
            }
            case "DataAllValuesFrom":
            {
                return new OwlDataAllValuesFrom([AsDataProperty(children[0])], AsDataRange(children[1]));
            }
            case "DataHasValue" when children[1] is Literal dataValue:
            {
                return new OwlDataHasValue(AsDataProperty(children[0]), dataValue);
            }
            case "DataMinCardinality" or "DataMaxCardinality" or "DataExactCardinality":
            {
                OwlCardinalityKind kind = node.Head switch
                {
                    "DataMinCardinality" => OwlCardinalityKind.Min,
                    "DataMaxCardinality" => OwlCardinalityKind.Max,
                    _ => OwlCardinalityKind.Exact
                };

                return new OwlDataCardinality(
                    kind,
                    children[0] is int bound ? bound : 0,
                    AsDataProperty(children[1]),
                    children.Count > 2 ? AsDataRange(children[2]) : null);
            }
            case "DataIntersectionOf":
            {
                return new OwlDataIntersectionOf(AsDataRangeList(children));
            }
            case "DataUnionOf":
            {
                return new OwlDataUnionOf(AsDataRangeList(children));
            }
            case "DataComplementOf":
            {
                return new OwlDataComplementOf(AsDataRange(children[0]));
            }
            case "DataOneOf":
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
            case "DatatypeRestriction":
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
            case "ObjectPropertyChain":
            {
                List<OwlObjectPropertyExpression> links = [];
                foreach(object? child in children)
                {
                    links.Add(AsObjectProperty(child));
                }

                return links;
            }
            default:
            {
                //Axiom heads construct in ConvertAxiom; leave the
                //converted children accessible through the group node.
                return children;
            }
        }
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

        switch(node.Head)
        {
            case "Declaration" when children is [DeclarationValue declaration]:
            {
                AddDeclaration(declaration);
                break;
            }
            case "SubClassOf" when children.Count == 2:
            {
                Axioms.Add(new OwlSubClassOfAxiom(AsClass(children[0]), AsClass(children[1])) { Origin = Origin });
                break;
            }
            case "EquivalentClasses":
            {
                ForEachPair(children, (first, second) =>
                    Axioms.Add(new OwlEquivalentClassesAxiom(AsClass(first), AsClass(second)) { Origin = Origin }));
                break;
            }
            case "DisjointClasses":
            {
                Axioms.Add(new OwlDisjointClassesAxiom(AsClassList(children)) { Origin = Origin });
                break;
            }
            case "DisjointUnion" when children.Count >= 2 && children[0] is Utf8String unionClass:
            {
                Axioms.Add(new OwlDisjointUnionAxiom(new NamedNode(unionClass), AsClassList(children[1..])) { Origin = Origin });
                break;
            }
            case "SubObjectPropertyOf" when children.Count == 2:
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
            case "EquivalentObjectProperties":
            {
                ForEachPair(children, (first, second) =>
                    Axioms.Add(new OwlEquivalentObjectPropertiesAxiom(AsObjectProperty(first), AsObjectProperty(second)) { Origin = Origin }));
                break;
            }
            case "DisjointObjectProperties":
            {
                List<OwlObjectPropertyExpression> properties = [];
                foreach(object? child in children)
                {
                    properties.Add(AsObjectProperty(child));
                }

                Axioms.Add(new OwlDisjointObjectPropertiesAxiom(properties) { Origin = Origin });
                break;
            }
            case "InverseObjectProperties" when children.Count == 2:
            {
                Axioms.Add(new OwlInverseObjectPropertiesAxiom(AsObjectProperty(children[0]), AsObjectProperty(children[1])) { Origin = Origin });
                break;
            }
            case "ObjectPropertyDomain" when children.Count == 2:
            {
                Axioms.Add(new OwlObjectPropertyDomainAxiom(AsObjectProperty(children[0]), AsClass(children[1])) { Origin = Origin });
                break;
            }
            case "ObjectPropertyRange" when children.Count == 2:
            {
                Axioms.Add(new OwlObjectPropertyRangeAxiom(AsObjectProperty(children[0]), AsClass(children[1])) { Origin = Origin });
                break;
            }
            case "FunctionalObjectProperty" or "InverseFunctionalObjectProperty" or "TransitiveObjectProperty"
                or "SymmetricObjectProperty" or "AsymmetricObjectProperty" or "ReflexiveObjectProperty" or "IrreflexiveObjectProperty":
            {
                OwlPropertyCharacteristic characteristic = node.Head switch
                {
                    "FunctionalObjectProperty" => OwlPropertyCharacteristic.Functional,
                    "InverseFunctionalObjectProperty" => OwlPropertyCharacteristic.InverseFunctional,
                    "TransitiveObjectProperty" => OwlPropertyCharacteristic.Transitive,
                    "SymmetricObjectProperty" => OwlPropertyCharacteristic.Symmetric,
                    "AsymmetricObjectProperty" => OwlPropertyCharacteristic.Asymmetric,
                    "ReflexiveObjectProperty" => OwlPropertyCharacteristic.Reflexive,
                    _ => OwlPropertyCharacteristic.Irreflexive
                };

                Axioms.Add(new OwlObjectPropertyCharacteristicAxiom(characteristic, AsObjectProperty(children[0])) { Origin = Origin });
                break;
            }
            case "SubDataPropertyOf" when children.Count == 2:
            {
                Axioms.Add(new OwlSubDataPropertyOfAxiom(AsDataProperty(children[0]), AsDataProperty(children[1])) { Origin = Origin });
                break;
            }
            case "EquivalentDataProperties":
            {
                ForEachPair(children, (first, second) =>
                    Axioms.Add(new OwlEquivalentDataPropertiesAxiom(AsDataProperty(first), AsDataProperty(second)) { Origin = Origin }));
                break;
            }
            case "DisjointDataProperties":
            {
                List<NamedNode> properties = [];
                foreach(object? child in children)
                {
                    properties.Add(AsDataProperty(child));
                }

                Axioms.Add(new OwlDisjointDataPropertiesAxiom(properties) { Origin = Origin });
                break;
            }
            case "DataPropertyDomain" when children.Count == 2:
            {
                Axioms.Add(new OwlDataPropertyDomainAxiom(AsDataProperty(children[0]), AsClass(children[1])) { Origin = Origin });
                break;
            }
            case "DataPropertyRange" when children.Count == 2:
            {
                Axioms.Add(new OwlDataPropertyRangeAxiom(AsDataProperty(children[0]), AsDataRange(children[1])) { Origin = Origin });
                break;
            }
            case "FunctionalDataProperty":
            {
                Axioms.Add(new OwlFunctionalDataPropertyAxiom(AsDataProperty(children[0])) { Origin = Origin });
                break;
            }
            case "DatatypeDefinition" when children.Count == 2:
            {
                Axioms.Add(new OwlDatatypeDefinitionAxiom(new NamedNode(AsIri(children[0])), AsDataRange(children[1])) { Origin = Origin });
                break;
            }
            case "HasKey" when children.Count >= 1:
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
            case "SameIndividual":
            {
                ForEachPair(children, (first, second) =>
                    Axioms.Add(new OwlSameIndividualAxiom(AsIndividual(first), AsIndividual(second)) { Origin = Origin }));
                break;
            }
            case "DifferentIndividuals":
            {
                Axioms.Add(new OwlDifferentIndividualsAxiom(AsIndividualList(children)) { Origin = Origin });
                break;
            }
            case "ClassAssertion" when children.Count == 2:
            {
                Axioms.Add(new OwlClassAssertionAxiom(AsClass(children[0]), AsIndividual(children[1])) { Origin = Origin });
                break;
            }
            case "ObjectPropertyAssertion" when children.Count == 3 && AsObjectProperty(children[0]) is OwlObjectPropertyReference assertedProperty:
            {
                Axioms.Add(new OwlObjectPropertyAssertionAxiom(AsIndividual(children[1]), assertedProperty.Named, AsIndividual(children[2])) { Origin = Origin });
                break;
            }
            case "NegativeObjectPropertyAssertion" when children.Count == 3:
            {
                Axioms.Add(new OwlNegativeObjectPropertyAssertionAxiom(AsIndividual(children[1]), AsObjectProperty(children[0]), AsIndividual(children[2])) { Origin = Origin });
                break;
            }
            case "DataPropertyAssertion" when children.Count == 3 && children[2] is Literal dataValue:
            {
                Axioms.Add(new OwlDataPropertyAssertionAxiom(AsIndividual(children[1]), AsDataProperty(children[0]), dataValue) { Origin = Origin });
                break;
            }
            case "NegativeDataPropertyAssertion" when children.Count == 3 && children[2] is Literal negativeValue:
            {
                Axioms.Add(new OwlNegativeDataPropertyAssertionAxiom(AsIndividual(children[1]), AsDataProperty(children[0]), negativeValue) { Origin = Origin });
                break;
            }
            case "AnnotationAssertion" when children.Count == 3:
            {
                Axioms.Add(new OwlAnnotationAssertionAxiom(AsIndividual(children[1]), new NamedNode(AsIri(children[0])), AsAnnotationValue(children[2])) { Origin = Origin });
                break;
            }
            case "SubAnnotationPropertyOf" when children.Count == 2:
            {
                Axioms.Add(new OwlSubAnnotationPropertyOfAxiom(new NamedNode(AsIri(children[0])), new NamedNode(AsIri(children[1]))) { Origin = Origin });
                break;
            }
            case "AnnotationPropertyDomain" when children.Count == 2:
            {
                Axioms.Add(new OwlAnnotationPropertyDomainAxiom(new NamedNode(AsIri(children[0])), new NamedNode(AsIri(children[1]))) { Origin = Origin });
                break;
            }
            case "AnnotationPropertyRange" when children.Count == 2:
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
                    Utf8String.FromString($"Unrecognised axiom constructor '{node.Head}'.")));
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

    private static void ForEachPair(List<object?> values, Action<object?, object?> emit)
    {
        for(int i = 0; i < values.Count; i++)
        {
            for(int j = i + 1; j < values.Count; j++)
            {
                emit(values[i], values[j]);
            }
        }
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
            Utf8String.FromString("An IRI was expected.")));

        return Utf8String.FromString("urn:veritas:invalid");
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
                Utf8String.FromString("A class expression was expected.")));

            return new OwlClassReference(new NamedNode(Utf8String.FromString("urn:veritas:invalid")));
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
                Utf8String.FromString("An object property expression was expected.")));

            return new OwlObjectPropertyReference(new NamedNode(Utf8String.FromString("urn:veritas:invalid")));
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
                Utf8String.FromString("A data range was expected.")));

            return new OwlDatatypeReference(new NamedNode(Utf8String.FromString("urn:veritas:invalid")));
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
                Utf8String.FromString("An individual was expected.")));

            return new NamedNode(Utf8String.FromString("urn:veritas:invalid"));
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
