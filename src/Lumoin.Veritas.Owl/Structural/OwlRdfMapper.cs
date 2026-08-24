using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Owl.Structural;

/// <summary>
/// Maps an RDF graph to OWL 2 structural form — the reverse direction of
/// <see href="https://www.w3.org/TR/owl2-mapping-to-rdf/">OWL 2 Mapping to RDF Graphs</see>.
/// </summary>
/// <remarks>
/// <para>
/// The mapping is value-based: malformed or unrecognised structure is
/// recorded in the document's <see cref="OwlOntologyDocument.Diagnostics"/>
/// and mapping continues; the caller decides what an error means (a profile
/// checker, for instance, treats a structurally ill-formed graph as outside
/// every profile).
/// </para>
/// <para>
/// Two passes over the graph: the axiom-root pass dispatches on the OWL and
/// RDFS axiom predicates (resolving class expressions, property expressions,
/// data ranges, and lists on demand, marking their structural triples
/// consumed), and the assertion pass maps every remaining plain triple to an
/// object-, data-, or annotation-property assertion using the declaration
/// index. Expression resolution walks with explicit stacks — class
/// expressions nest arbitrarily and the no-recursion discipline holds.
/// </para>
/// </remarks>
public static class OwlRdfMapper
{
    /// <summary>
    /// Maps the graph's default-graph triples into an OWL 2 structural document.
    /// </summary>
    /// <param name="quads">The graph triples.</param>
    /// <param name="declarationContext">
    /// An already-mapped document whose declarations seed this mapping's
    /// declaration index — the way a conclusion document of an entailment
    /// test, or any document interpreted within an importing ontology's
    /// context, uses entities its companion declared.
    /// </param>
    /// <returns>The mapped document: axioms, declaration index, and diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="quads"/> is <see langword="null"/>.</exception>
    public static OwlOntologyDocument Map(IReadOnlyList<Quad> quads, OwlOntologyDocument? declarationContext = null)
    {
        ArgumentNullException.ThrowIfNull(quads);

        MapperContext context = new(quads);

        if(declarationContext is not null)
        {
            context.SeedDeclarations(declarationContext);
        }

        context.CollectDeclarations();
        context.MapAxiomRoots();
        context.MapRemainingAssertions();
        context.AttachAnnotations();

        return new OwlOntologyDocument(
            context.Axioms.ToImmutable(),
            context.OntologyIri,
            context.Diagnostics,
            context.DeclaredClasses,
            context.DeclaredObjectProperties,
            context.DeclaredDataProperties,
            context.DeclaredAnnotationProperties,
            context.DeclaredDatatypes);
    }

    //The per-run mutable state: the subject index, the consumed-triple set,
    //the declaration index, the expression caches, and the outputs.
    private sealed class MapperContext
    {
        private static Utf8String RdfTypeIri { get; } = Vocabulary.Rdf.Type;

        /// <summary>The input triples in document order.</summary>
        private IReadOnlyList<Quad> Quads { get; }

        /// <summary>Per-subject triple index.</summary>
        private Dictionary<RdfTerm, List<Quad>> BySubject { get; } = [];

        /// <summary>Triples consumed as expression or reification structure; the assertion pass skips them.</summary>
        private HashSet<Quad> Consumed { get; } = [];

        /// <summary>Resolved class expressions per node; a <c>null</c> value records a failed resolution.</summary>
        private Dictionary<RdfTerm, OwlClassExpression?> ClassExpressionCache { get; } = [];

        /// <summary>Resolved data ranges per node; a <c>null</c> value records a failed resolution.</summary>
        private Dictionary<RdfTerm, OwlDataRange?> DataRangeCache { get; } = [];

        /// <summary>The mapped axioms.</summary>
        public ImmutableArray<OwlAxiom>.Builder Axioms { get; } = ImmutableArray.CreateBuilder<OwlAxiom>();

        /// <summary>The mapping diagnostics.</summary>
        public DiagnosticBag Diagnostics { get; } = new();

        /// <summary>The ontology IRI, when declared.</summary>
        public NamedNode? OntologyIri { get; private set; }

        /// <summary>The class declaration index (built-ins seeded).</summary>
        public HashSet<Utf8String> DeclaredClasses { get; } = [];

        /// <summary>The object-property declaration index (built-ins seeded).</summary>
        public HashSet<Utf8String> DeclaredObjectProperties { get; } = [];

        /// <summary>The data-property declaration index (built-ins seeded).</summary>
        public HashSet<Utf8String> DeclaredDataProperties { get; } = [];

        /// <summary>The annotation-property declaration index (built-ins seeded).</summary>
        public HashSet<Utf8String> DeclaredAnnotationProperties { get; } = [];

        /// <summary>The datatype declaration index.</summary>
        public HashSet<Utf8String> DeclaredDatatypes { get; } = [];

        public MapperContext(IReadOnlyList<Quad> quads)
        {
            Quads = quads;
            foreach(Quad quad in quads)
            {
                if(!BySubject.TryGetValue(quad.Subject, out List<Quad>? bucket))
                {
                    bucket = [];
                    BySubject[quad.Subject] = bucket;
                }

                bucket.Add(quad);
            }

            //The built-in vocabulary: annotation properties, the universal and
            //empty classes, and the top and bottom properties are declared in
            //every OWL 2 ontology without stating so.
            DeclaredAnnotationProperties.Add(RdfVocabulary.Rdfs.Label);
            DeclaredAnnotationProperties.Add(RdfVocabulary.Rdfs.Comment);
            DeclaredAnnotationProperties.Add(RdfVocabulary.Rdfs.SeeAlso);
            DeclaredAnnotationProperties.Add(RdfVocabulary.Rdfs.IsDefinedBy);
            DeclaredAnnotationProperties.Add(OwlVocabulary.VersionInfo);
            DeclaredAnnotationProperties.Add(OwlVocabulary.Deprecated);
            DeclaredAnnotationProperties.Add(OwlVocabulary.PriorVersion);
            DeclaredAnnotationProperties.Add(OwlVocabulary.BackwardCompatibleWith);
            DeclaredAnnotationProperties.Add(OwlVocabulary.IncompatibleWith);
            DeclaredClasses.Add(OwlVocabulary.Thing);
            DeclaredClasses.Add(OwlVocabulary.Nothing);
            DeclaredObjectProperties.Add(OwlVocabulary.TopObjectProperty);
            DeclaredObjectProperties.Add(OwlVocabulary.BottomObjectProperty);
            DeclaredDataProperties.Add(OwlVocabulary.TopDataProperty);
            DeclaredDataProperties.Add(OwlVocabulary.BottomDataProperty);
        }

        /// <summary>Seeds the declaration index from an already-mapped companion document.</summary>
        /// <param name="declarationContext">The document whose declarations carry over.</param>
        public void SeedDeclarations(OwlOntologyDocument declarationContext)
        {
            DeclaredClasses.UnionWith(declarationContext.DeclaredClasses);
            DeclaredObjectProperties.UnionWith(declarationContext.DeclaredObjectProperties);
            DeclaredDataProperties.UnionWith(declarationContext.DeclaredDataProperties);
            DeclaredAnnotationProperties.UnionWith(declarationContext.DeclaredAnnotationProperties);
            DeclaredDatatypes.UnionWith(declarationContext.DeclaredDatatypes);
        }

        /// <summary>Scans the typing triples into the declaration index — the disambiguation basis for every later pass.</summary>
        public void CollectDeclarations()
        {
            foreach(Quad quad in Quads)
            {
                if(quad.Predicate.Iri != RdfTypeIri || quad.Subject is not NamedNode subject || quad.Object is not NamedNode type)
                {
                    continue;
                }

                Utf8String iri = type.Iri;
                if(iri == OwlVocabulary.ClassTerm || iri == OwlVocabulary.DeprecatedClass)
                {
                    DeclaredClasses.Add(subject.Iri);
                }
                else if(iri == OwlVocabulary.ObjectPropertyTerm
                    || iri == OwlVocabulary.InverseFunctionalProperty
                    || iri == OwlVocabulary.TransitiveProperty
                    || iri == OwlVocabulary.SymmetricProperty
                    || iri == OwlVocabulary.AsymmetricProperty
                    || iri == OwlVocabulary.ReflexiveProperty
                    || iri == OwlVocabulary.IrreflexiveProperty)
                {
                    //The object-property characteristics type their subject
                    //as an object property (Mapping to RDF Graphs Table 11);
                    //owl:FunctionalProperty stays out — it admits either
                    //property kind and disambiguates nothing.
                    DeclaredObjectProperties.Add(subject.Iri);
                }
                else if(iri == OwlVocabulary.DatatypeProperty)
                {
                    DeclaredDataProperties.Add(subject.Iri);
                }
                else if(iri == OwlVocabulary.AnnotationPropertyTerm)
                {
                    DeclaredAnnotationProperties.Add(subject.Iri);
                }
                else if(iri == RdfVocabulary.Rdfs.Datatype)
                {
                    DeclaredDatatypes.Add(subject.Iri);
                }
            }
        }

        /// <summary>The axiom-root pass: dispatches every typing and axiom-predicate triple, resolving expressions on demand.</summary>
        public void MapAxiomRoots()
        {
            foreach(Quad quad in Quads)
            {
                if(Consumed.Contains(quad))
                {
                    continue;
                }

                Utf8String predicate = quad.Predicate.Iri;

                if(predicate == RdfTypeIri)
                {
                    MapTypeTriple(quad);
                }
                else if(quad.Subject is NamedNode namedExpressionSubject
                    && (predicate == OwlVocabulary.IntersectionOf || predicate == OwlVocabulary.UnionOf || predicate == OwlVocabulary.ComplementOf || predicate == OwlVocabulary.OneOf))
                {
                    //An expression constructor on a NAMED subject is an
                    //EquivalentClasses axiom between the name and the
                    //expression the constructor describes.
                    Consume(quad);
                    if(ConstructNamedSubjectExpression(quad) is OwlClassExpression namedExpression)
                    {
                        Axioms.Add(new OwlEquivalentClassesAxiom(new OwlClassReference(namedExpressionSubject), namedExpression) { Origin = quad });
                    }
                }
                else if(predicate == RdfVocabulary.Rdfs.SubClassOf)
                {
                    Consume(quad);
                    if(ResolveClassExpression(quad.Subject, quad) is OwlClassExpression sub
                        && ResolveClassExpression(quad.Object, quad) is OwlClassExpression super)
                    {
                        Axioms.Add(new OwlSubClassOfAxiom(sub, super) { Origin = quad });
                    }
                }
                else if(predicate == OwlVocabulary.EquivalentClass)
                {
                    Consume(quad);
                    MapEquivalentClass(quad);
                }
                else if(predicate == OwlVocabulary.DisjointWith)
                {
                    Consume(quad);
                    if(ResolveClassExpression(quad.Subject, quad) is OwlClassExpression first
                        && ResolveClassExpression(quad.Object, quad) is OwlClassExpression second)
                    {
                        Axioms.Add(new OwlDisjointClassesAxiom([first, second]) { Origin = quad });
                    }
                }
                else if(predicate == OwlVocabulary.DisjointUnionOf)
                {
                    Consume(quad);
                    if(quad.Subject is NamedNode unionClass && WalkList(quad.Object, quad) is List<RdfTerm> members)
                    {
                        List<OwlClassExpression> operands = ResolveAll(members, quad);
                        Axioms.Add(new OwlDisjointUnionAxiom(unionClass, operands) { Origin = quad });
                    }
                }
                else if(predicate == RdfVocabulary.Rdfs.SubPropertyOf)
                {
                    Consume(quad);
                    MapSubPropertyOf(quad);
                }
                else if(predicate == OwlVocabulary.PropertyChainAxiom)
                {
                    Consume(quad);
                    if(ResolvePropertyExpression(quad.Subject, quad) is OwlObjectPropertyExpression chainSuper
                        && WalkList(quad.Object, quad) is List<RdfTerm> links)
                    {
                        List<OwlObjectPropertyExpression> chain = ResolveAllProperties(links, quad);
                        Axioms.Add(new OwlPropertyChainAxiom(chain, chainSuper) { Origin = quad });
                    }
                }
                else if(predicate == OwlVocabulary.EquivalentProperty)
                {
                    Consume(quad);
                    MapEquivalentProperty(quad);
                }
                else if(predicate == OwlVocabulary.PropertyDisjointWith)
                {
                    Consume(quad);
                    MapPropertyDisjointWith(quad);
                }
                else if(predicate == OwlVocabulary.InverseOf && quad.Subject is NamedNode)
                {
                    //A blank-node subject is an inverse property EXPRESSION,
                    //consumed where it is used; only the named form is an axiom.
                    Consume(quad);
                    if(ResolvePropertyExpression(quad.Subject, quad) is OwlObjectPropertyExpression inverseFirst
                        && ResolvePropertyExpression(quad.Object, quad) is OwlObjectPropertyExpression inverseSecond)
                    {
                        Axioms.Add(new OwlInverseObjectPropertiesAxiom(inverseFirst, inverseSecond) { Origin = quad });
                    }
                }
                else if(predicate == RdfVocabulary.Rdfs.Domain)
                {
                    Consume(quad);
                    MapDomain(quad);
                }
                else if(predicate == RdfVocabulary.Rdfs.Range)
                {
                    Consume(quad);
                    MapRange(quad);
                }
                else if(predicate == OwlVocabulary.SameAs)
                {
                    Consume(quad);
                    Axioms.Add(new OwlSameIndividualAxiom(quad.Subject, quad.Object) { Origin = quad });
                }
                else if(predicate == OwlVocabulary.DifferentFrom)
                {
                    Consume(quad);
                    Axioms.Add(new OwlDifferentIndividualsAxiom([quad.Subject, quad.Object]) { Origin = quad });
                }
                else if(predicate == OwlVocabulary.HasKey)
                {
                    Consume(quad);
                    MapHasKey(quad);
                }
                else if(predicate == OwlVocabulary.Imports)
                {
                    Consume(quad);
                    if(quad.Object is NamedNode imported)
                    {
                        Axioms.Add(new OwlImportAxiom(imported) { Origin = quad });
                    }
                }
                else if(predicate == OwlVocabulary.VersionIri)
                {
                    Consume(quad);
                }
            }
        }

        /// <summary>The assertion pass: maps every remaining plain triple to an object-, data-, or annotation-property assertion.</summary>
        public void MapRemainingAssertions()
        {
            foreach(Quad quad in Quads)
            {
                if(Consumed.Contains(quad) || quad.Predicate.Iri == RdfTypeIri)
                {
                    continue;
                }

                Utf8String predicate = quad.Predicate.Iri;
                if(IsStructuralPredicate(predicate))
                {
                    //Structure never reached from an axiom root: a declared-
                    //but-unused expression (legal — it asserts nothing), so
                    //its triples are consumed silently.
                    ConsumeSubjectTriples(quad.Subject);

                    continue;
                }

                if(DeclaredObjectProperties.Contains(predicate))
                {
                    Axioms.Add(new OwlObjectPropertyAssertionAxiom(quad.Subject, quad.Predicate, quad.Object) { Origin = quad });
                }
                else if(DeclaredDataProperties.Contains(predicate))
                {
                    if(quad.Object is Literal dataValue)
                    {
                        Axioms.Add(new OwlDataPropertyAssertionAxiom(quad.Subject, quad.Predicate, dataValue) { Origin = quad });
                    }
                    else
                    {
                        Report(WellKnownDiagnostics.Owl.MalformedAxiomStructure, $"Data property assertion with a non-literal object: {Describe(quad)}.");
                    }
                }
                else if(DeclaredAnnotationProperties.Contains(predicate))
                {
                    Axioms.Add(new OwlAnnotationAssertionAxiom(quad.Subject, quad.Predicate, quad.Object) { Origin = quad });
                }
                else
                {
                    //Undeclared: the declaration-driven disambiguation has no
                    //basis, so the assertion maps as an annotation and the gap
                    //is reported.
                    Report(WellKnownDiagnostics.Owl.UndeclaredProperty, $"Undeclared property in assertion {Describe(quad)}; mapped as an annotation.");
                    Axioms.Add(new OwlAnnotationAssertionAxiom(quad.Subject, quad.Predicate, quad.Object) { Origin = quad });
                }
            }
        }

        //Maps one rdf:type triple: declarations, characteristics, reified
        //axiom structures, the ontology header, or a class assertion.
        private void MapTypeTriple(Quad quad)
        {
            if(quad.Object is not NamedNode type)
            {
                //rdf:type with a literal or blank object in type position is
                //a class assertion on a blank class expression — resolve it.
                Consume(quad);
                if(ResolveClassExpression(quad.Object, quad) is OwlClassExpression blankExpression)
                {
                    Axioms.Add(new OwlClassAssertionAxiom(blankExpression, quad.Subject) { Origin = quad });
                }

                return;
            }

            Utf8String iri = type.Iri;

            if(iri == OwlVocabulary.Ontology)
            {
                Consume(quad);
                if(quad.Subject is NamedNode ontology && OntologyIri is null)
                {
                    OntologyIri = ontology;
                }

                return;
            }

            if(iri == OwlVocabulary.ClassTerm || iri == OwlVocabulary.DeprecatedClass)
            {
                Consume(quad);
                if(quad.Subject is NamedNode declaredClass)
                {
                    Axioms.Add(new OwlDeclarationAxiom(OwlEntityKind.Class, declaredClass) { Origin = quad });
                }

                return;
            }

            if(iri == OwlVocabulary.ObjectPropertyTerm)
            {
                Consume(quad);
                if(quad.Subject is NamedNode objectProperty)
                {
                    Axioms.Add(new OwlDeclarationAxiom(OwlEntityKind.ObjectProperty, objectProperty) { Origin = quad });
                }

                return;
            }

            if(iri == OwlVocabulary.DatatypeProperty)
            {
                Consume(quad);
                if(quad.Subject is NamedNode dataProperty)
                {
                    Axioms.Add(new OwlDeclarationAxiom(OwlEntityKind.DataProperty, dataProperty) { Origin = quad });
                }

                return;
            }

            if(iri == OwlVocabulary.AnnotationPropertyTerm)
            {
                Consume(quad);
                if(quad.Subject is NamedNode annotationProperty)
                {
                    Axioms.Add(new OwlDeclarationAxiom(OwlEntityKind.AnnotationProperty, annotationProperty) { Origin = quad });
                }

                return;
            }

            if(iri == OwlVocabulary.NamedIndividual)
            {
                Consume(quad);
                if(quad.Subject is NamedNode individual)
                {
                    Axioms.Add(new OwlDeclarationAxiom(OwlEntityKind.NamedIndividual, individual) { Origin = quad });
                }
                else
                {
                    //owl:NamedIndividual typing on a blank node cannot be a
                    //declaration (declarations name IRIs); it surfaces as a
                    //class assertion so the profile checkers see it.
                    Axioms.Add(new OwlClassAssertionAxiom(new OwlClassReference(type), quad.Subject) { Origin = quad });
                }

                return;
            }

            if(iri == RdfVocabulary.Rdfs.Datatype)
            {
                Consume(quad);
                if(quad.Subject is NamedNode datatype)
                {
                    Axioms.Add(new OwlDeclarationAxiom(OwlEntityKind.Datatype, datatype) { Origin = quad });
                }

                return;
            }

            if(iri == OwlVocabulary.FunctionalProperty)
            {
                Consume(quad);

                //owl:FunctionalProperty applies to object AND data properties;
                //the declaration index disambiguates.
                if(quad.Subject is NamedNode functionalSubject && DeclaredDataProperties.Contains(functionalSubject.Iri))
                {
                    Axioms.Add(new OwlFunctionalDataPropertyAxiom(functionalSubject) { Origin = quad });
                }
                else if(ResolvePropertyExpression(quad.Subject, quad) is OwlObjectPropertyExpression functionalProperty)
                {
                    Axioms.Add(new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, functionalProperty) { Origin = quad });
                }

                return;
            }

            OwlPropertyCharacteristic? characteristic = iri switch
            {
                _ when iri == OwlVocabulary.InverseFunctionalProperty => OwlPropertyCharacteristic.InverseFunctional,
                _ when iri == OwlVocabulary.TransitiveProperty => OwlPropertyCharacteristic.Transitive,
                _ when iri == OwlVocabulary.SymmetricProperty => OwlPropertyCharacteristic.Symmetric,
                _ when iri == OwlVocabulary.AsymmetricProperty => OwlPropertyCharacteristic.Asymmetric,
                _ when iri == OwlVocabulary.ReflexiveProperty => OwlPropertyCharacteristic.Reflexive,
                _ when iri == OwlVocabulary.IrreflexiveProperty => OwlPropertyCharacteristic.Irreflexive,
                _ => null
            };

            if(characteristic is OwlPropertyCharacteristic resolvedCharacteristic)
            {
                Consume(quad);
                if(ResolvePropertyExpression(quad.Subject, quad) is OwlObjectPropertyExpression characteristicProperty)
                {
                    Axioms.Add(new OwlObjectPropertyCharacteristicAxiom(resolvedCharacteristic, characteristicProperty) { Origin = quad });
                }

                return;
            }

            if(iri == OwlVocabulary.AllDisjointClasses)
            {
                Consume(quad);
                if(MembersOf(quad) is List<RdfTerm> disjointMembers)
                {
                    Axioms.Add(new OwlDisjointClassesAxiom(ResolveAll(disjointMembers, quad)) { Origin = quad });
                }

                return;
            }

            if(iri == OwlVocabulary.AllDisjointProperties)
            {
                Consume(quad);
                if(MembersOf(quad) is List<RdfTerm> disjointProperties)
                {
                    MapDisjointPropertyMembers(quad, disjointProperties);
                }

                return;
            }

            if(iri == OwlVocabulary.AllDifferent)
            {
                Consume(quad);
                List<RdfTerm>? different = MembersOf(quad) ?? DistinctMembersOf(quad);
                if(different is not null)
                {
                    Axioms.Add(new OwlDifferentIndividualsAxiom(different) { Origin = quad });
                }

                return;
            }

            if(iri == OwlVocabulary.NegativePropertyAssertion)
            {
                Consume(quad);
                MapNegativePropertyAssertion(quad);

                return;
            }

            if(iri == OwlVocabulary.AxiomTerm || iri == OwlVocabulary.AnnotationTerm)
            {
                //Annotated-axiom reification: the annotated triple is asserted
                //separately and maps through the normal passes; the node's own
                //annotation triples attach to the matching axiom afterwards.
                Consume(quad);
                CollectReification(quad.Subject, annotatesAnnotation: iri == OwlVocabulary.AnnotationTerm);

                return;
            }

            if(iri == OwlVocabulary.Restriction || iri == OwlVocabulary.DataRange || iri == RdfVocabulary.Rdf.List)
            {
                //Expression structure (owl:DataRange is the OWL 1 spelling
                //of rdfs:Datatype structure); consumed when its using axiom
                //resolves.
                return;
            }

            if(iri == RdfVocabulary.Rdfs.Class)
            {
                //An optional rdfs:Class typing alongside owl:Class or
                //owl:Restriction adds nothing; it is consumed without an
                //axiom, so an entity typed ONLY rdfs:Class stays undeclared.
                Consume(quad);

                return;
            }

            //A plain class assertion: named or blank class expression.
            Consume(quad);
            if(ResolveClassExpression(type, quad) is OwlClassExpression assertedClass)
            {
                Axioms.Add(new OwlClassAssertionAxiom(assertedClass, quad.Subject) { Origin = quad });
            }
        }

        //owl:equivalentClass doubles as DatatypeDefinition when the subject is
        //a datatype or the object is data-range structure.
        private void MapEquivalentClass(Quad quad)
        {
            bool isDatatypeDefinition = quad.Subject is NamedNode named
                && (DeclaredDatatypes.Contains(named.Iri) || LooksLikeDataRange(quad.Object));

            if(isDatatypeDefinition)
            {
                if(quad.Subject is NamedNode datatype && ResolveDataRange(quad.Object, quad) is OwlDataRange range)
                {
                    Axioms.Add(new OwlDatatypeDefinitionAxiom(datatype, range) { Origin = quad });
                }

                return;
            }

            if(ResolveClassExpression(quad.Subject, quad) is OwlClassExpression first
                && ResolveClassExpression(quad.Object, quad) is OwlClassExpression second)
            {
                Axioms.Add(new OwlEquivalentClassesAxiom(first, second) { Origin = quad });
            }
        }

        private void MapSubPropertyOf(Quad quad)
        {
            PropertyKind kind = KindOfPropertyPair(quad.Subject, quad.Object);

            if(kind == PropertyKind.Object)
            {
                if(ResolvePropertyExpression(quad.Subject, quad) is OwlObjectPropertyExpression sub
                    && ResolvePropertyExpression(quad.Object, quad) is OwlObjectPropertyExpression super)
                {
                    Axioms.Add(new OwlSubObjectPropertyOfAxiom(sub, super) { Origin = quad });
                }
            }
            else if(kind == PropertyKind.Data)
            {
                if(quad.Subject is NamedNode sub && quad.Object is NamedNode super)
                {
                    Axioms.Add(new OwlSubDataPropertyOfAxiom(sub, super) { Origin = quad });
                }
            }
            else
            {
                if(quad.Subject is NamedNode sub && quad.Object is NamedNode super)
                {
                    Axioms.Add(new OwlSubAnnotationPropertyOfAxiom(sub, super) { Origin = quad });
                }
            }
        }

        private void MapEquivalentProperty(Quad quad)
        {
            PropertyKind kind = KindOfPropertyPair(quad.Subject, quad.Object);

            if(kind == PropertyKind.Data)
            {
                if(quad.Subject is NamedNode first && quad.Object is NamedNode second)
                {
                    Axioms.Add(new OwlEquivalentDataPropertiesAxiom(first, second) { Origin = quad });
                }
            }
            else if(ResolvePropertyExpression(quad.Subject, quad) is OwlObjectPropertyExpression first
                && ResolvePropertyExpression(quad.Object, quad) is OwlObjectPropertyExpression second)
            {
                Axioms.Add(new OwlEquivalentObjectPropertiesAxiom(first, second) { Origin = quad });
            }
        }

        private void MapPropertyDisjointWith(Quad quad)
        {
            PropertyKind kind = KindOfPropertyPair(quad.Subject, quad.Object);

            if(kind == PropertyKind.Data)
            {
                if(quad.Subject is NamedNode first && quad.Object is NamedNode second)
                {
                    Axioms.Add(new OwlDisjointDataPropertiesAxiom([first, second]) { Origin = quad });
                }
            }
            else if(ResolvePropertyExpression(quad.Subject, quad) is OwlObjectPropertyExpression first
                && ResolvePropertyExpression(quad.Object, quad) is OwlObjectPropertyExpression second)
            {
                Axioms.Add(new OwlDisjointObjectPropertiesAxiom([first, second]) { Origin = quad });
            }
        }

        private void MapDisjointPropertyMembers(Quad quad, List<RdfTerm> members)
        {
            bool isData = false;
            foreach(RdfTerm member in members)
            {
                if(member is NamedNode named && DeclaredDataProperties.Contains(named.Iri))
                {
                    isData = true;

                    break;
                }
            }

            if(isData)
            {
                List<NamedNode> properties = [];
                foreach(RdfTerm member in members)
                {
                    if(member is NamedNode named)
                    {
                        properties.Add(named);
                    }
                }

                Axioms.Add(new OwlDisjointDataPropertiesAxiom(properties) { Origin = quad });
            }
            else
            {
                Axioms.Add(new OwlDisjointObjectPropertiesAxiom(ResolveAllProperties(members, quad)) { Origin = quad });
            }
        }

        private void MapDomain(Quad quad)
        {
            if(quad.Subject is not NamedNode property)
            {
                return;
            }

            if(DeclaredDataProperties.Contains(property.Iri))
            {
                if(ResolveClassExpression(quad.Object, quad) is OwlClassExpression dataDomain)
                {
                    Axioms.Add(new OwlDataPropertyDomainAxiom(property, dataDomain) { Origin = quad });
                }
            }
            else if(DeclaredAnnotationProperties.Contains(property.Iri) && !DeclaredObjectProperties.Contains(property.Iri))
            {
                if(quad.Object is NamedNode annotationDomain)
                {
                    Axioms.Add(new OwlAnnotationPropertyDomainAxiom(property, annotationDomain) { Origin = quad });
                }
            }
            else if(ResolveClassExpression(quad.Object, quad) is OwlClassExpression objectDomain)
            {
                Axioms.Add(new OwlObjectPropertyDomainAxiom(new OwlObjectPropertyReference(property), objectDomain) { Origin = quad });
            }
        }

        private void MapRange(Quad quad)
        {
            if(quad.Subject is not NamedNode property)
            {
                return;
            }

            if(DeclaredDataProperties.Contains(property.Iri))
            {
                if(ResolveDataRange(quad.Object, quad) is OwlDataRange dataRange)
                {
                    Axioms.Add(new OwlDataPropertyRangeAxiom(property, dataRange) { Origin = quad });
                }
            }
            else if(DeclaredAnnotationProperties.Contains(property.Iri) && !DeclaredObjectProperties.Contains(property.Iri))
            {
                if(quad.Object is NamedNode annotationRange)
                {
                    Axioms.Add(new OwlAnnotationPropertyRangeAxiom(property, annotationRange) { Origin = quad });
                }
            }
            else if(ResolveClassExpression(quad.Object, quad) is OwlClassExpression objectRange)
            {
                Axioms.Add(new OwlObjectPropertyRangeAxiom(new OwlObjectPropertyReference(property), objectRange) { Origin = quad });
            }
        }

        private void MapHasKey(Quad quad)
        {
            if(ResolveClassExpression(quad.Subject, quad) is not OwlClassExpression keyedClass
                || WalkList(quad.Object, quad) is not List<RdfTerm> keyMembers)
            {
                return;
            }

            List<OwlObjectPropertyExpression> objectKeys = [];
            List<NamedNode> dataKeys = [];
            foreach(RdfTerm member in keyMembers)
            {
                if(member is NamedNode named && DeclaredDataProperties.Contains(named.Iri))
                {
                    dataKeys.Add(named);
                }
                else if(ResolvePropertyExpression(member, quad) is OwlObjectPropertyExpression objectKey)
                {
                    objectKeys.Add(objectKey);
                }
            }

            Axioms.Add(new OwlHasKeyAxiom(keyedClass, objectKeys, dataKeys) { Origin = quad });
        }

        private void MapNegativePropertyAssertion(Quad quad)
        {
            ConsumeSubjectTriples(quad.Subject);

            RdfTerm? source = ObjectOf(quad.Subject, OwlVocabulary.SourceIndividual);
            RdfTerm? property = ObjectOf(quad.Subject, OwlVocabulary.AssertionProperty);
            RdfTerm? targetIndividual = ObjectOf(quad.Subject, OwlVocabulary.TargetIndividual);
            RdfTerm? targetValue = ObjectOf(quad.Subject, OwlVocabulary.TargetValue);

            if(source is null || property is null || (targetIndividual is null && targetValue is null))
            {
                Report(WellKnownDiagnostics.Owl.MalformedAxiomStructure, $"Incomplete owl:NegativePropertyAssertion at {Describe(quad)}.");

                return;
            }

            if(targetValue is Literal literalTarget)
            {
                if(property is NamedNode dataProperty)
                {
                    Axioms.Add(new OwlNegativeDataPropertyAssertionAxiom(source, dataProperty, literalTarget) { Origin = quad });
                }
            }
            else if(targetIndividual is not null && ResolvePropertyExpression(property, quad) is OwlObjectPropertyExpression objectProperty)
            {
                Axioms.Add(new OwlNegativeObjectPropertyAssertionAxiom(source, objectProperty, targetIndividual) { Origin = quad });
            }
        }

        //Expression resolution.

        /// <summary>Resolves a node to a class expression with an explicit work stack (class expressions nest arbitrarily; no call-stack recursion).</summary>
        private OwlClassExpression? ResolveClassExpression(RdfTerm root, Quad origin)
        {
            if(ClassExpressionCache.TryGetValue(root, out OwlClassExpression? cached))
            {
                return cached;
            }

            //Post-order over the expression DAG: a frame is revisited once its
            //children resolve; the in-progress set breaks cycles.
            Stack<RdfTerm> work = new();
            HashSet<RdfTerm> inProgress = [];
            work.Push(root);

            while(work.Count > 0)
            {
                RdfTerm node = work.Peek();
                if(ClassExpressionCache.ContainsKey(node))
                {
                    work.Pop();

                    continue;
                }

                if(node is NamedNode named)
                {
                    ClassExpressionCache[node] = new OwlClassReference(named);
                    work.Pop();

                    continue;
                }

                if(node is not BlankNode)
                {
                    Report(WellKnownDiagnostics.Owl.MalformedClassExpression, $"A literal cannot be a class expression near {Describe(origin)}.");
                    ClassExpressionCache[node] = null;
                    work.Pop();

                    continue;
                }

                List<RdfTerm> children = PendingClassChildren(node, origin);
                bool allResolved = true;
                foreach(RdfTerm child in children)
                {
                    if(ClassExpressionCache.ContainsKey(child))
                    {
                        continue;
                    }

                    //A named child is a leaf reference and resolves at
                    //discovery: the same name may appear at several depths
                    //of one expression without being a structural cycle.
                    if(child is NamedNode namedChild)
                    {
                        ClassExpressionCache[child] = new OwlClassReference(namedChild);

                        continue;
                    }

                    allResolved = false;
                    if(!inProgress.Add(child))
                    {
                        Report(WellKnownDiagnostics.Owl.MalformedClassExpression, $"Cyclic class expression structure near {Describe(origin)}.");
                        ClassExpressionCache[child] = null;
                    }
                    else
                    {
                        work.Push(child);
                    }
                }

                if(allResolved)
                {
                    ClassExpressionCache[node] = ConstructClassExpression(node, origin);
                    work.Pop();
                }
            }

            return ClassExpressionCache[root];
        }

        //The blank-node class-expression children that must resolve before the
        //node constructs: boolean operands and class-valued fillers.
        private List<RdfTerm> PendingClassChildren(RdfTerm node, Quad origin)
        {
            List<RdfTerm> children = [];

            if(ObjectOf(node, OwlVocabulary.IntersectionOf) is RdfTerm intersection && WalkList(intersection, origin) is List<RdfTerm> intersectionMembers)
            {
                children.AddRange(intersectionMembers);
            }
            else if(ObjectOf(node, OwlVocabulary.UnionOf) is RdfTerm union && WalkList(union, origin) is List<RdfTerm> unionMembers)
            {
                children.AddRange(unionMembers);
            }
            else if(ObjectOf(node, OwlVocabulary.ComplementOf) is RdfTerm complement)
            {
                children.Add(complement);
            }
            else if(IsObjectRestriction(node))
            {
                RdfTerm? filler = ObjectOf(node, OwlVocabulary.SomeValuesFrom)
                    ?? ObjectOf(node, OwlVocabulary.AllValuesFrom)
                    ?? ObjectOf(node, OwlVocabulary.OnClass);
                if(filler is not null)
                {
                    children.Add(filler);
                }
            }

            return children;
        }

        //Constructs the EquivalentClasses-side expression for a NAMED subject
        //carrying an expression-constructor predicate: the members resolve
        //through the normal stack machine, and only the constructor's own
        //structure is consumed — the named class's other axioms stay live.
        private OwlClassExpression? ConstructNamedSubjectExpression(Quad quad)
        {
            Utf8String predicate = quad.Predicate.Iri;

            if(predicate == OwlVocabulary.ComplementOf)
            {
                return ResolveClassExpression(quad.Object, quad) is OwlClassExpression operand
                    ? new OwlObjectComplementOf(operand)
                    : null;
            }

            if(WalkList(quad.Object, quad) is not List<RdfTerm> members)
            {
                return null;
            }

            if(predicate == OwlVocabulary.IntersectionOf)
            {
                return Normalized(ResolveAll(members, quad), static operands => new OwlObjectIntersectionOf(operands));
            }

            if(predicate == OwlVocabulary.UnionOf)
            {
                return Normalized(ResolveAll(members, quad), static operands => new OwlObjectUnionOf(operands));
            }

            return new OwlObjectOneOf(members);
        }

        /// <summary>Constructs the n-ary class expression for two or more resolved operands.</summary>
        /// <param name="operands">The two-or-more operands.</param>
        /// <returns>The constructed n-ary class expression.</returns>
        private delegate OwlClassExpression NaryClassConstructor(List<OwlClassExpression> operands);

        //A singleton intersection or union denotes its operand — the corpus
        //uses one-membered owl:intersectionOf lists as plain class wrappers.
        private static OwlClassExpression? Normalized(List<OwlClassExpression> operands, NaryClassConstructor constructor)
        {
            return operands.Count switch
            {
                0 => null,
                1 => operands[0],
                _ => constructor(operands)
            };
        }

        //Constructs the expression for a blank node whose children are
        //resolved. Returns null (with a diagnostic) for unrecognised shapes.
        private OwlClassExpression? ConstructClassExpression(RdfTerm node, Quad origin)
        {
            if(node is BlankNode)
            {
                ConsumeSubjectTriples(node);
            }

            if(ObjectOf(node, OwlVocabulary.IntersectionOf) is RdfTerm intersection)
            {
                return WalkList(intersection, origin) is List<RdfTerm> members
                    ? Normalized(ResolveCached(members), static operands => new OwlObjectIntersectionOf(operands))
                    : null;
            }

            if(ObjectOf(node, OwlVocabulary.UnionOf) is RdfTerm union)
            {
                return WalkList(union, origin) is List<RdfTerm> members
                    ? Normalized(ResolveCached(members), static operands => new OwlObjectUnionOf(operands))
                    : null;
            }

            if(ObjectOf(node, OwlVocabulary.ComplementOf) is RdfTerm complement)
            {
                return ClassExpressionCache.GetValueOrDefault(complement) is OwlClassExpression operand
                    ? new OwlObjectComplementOf(operand)
                    : null;
            }

            if(ObjectOf(node, OwlVocabulary.OneOf) is RdfTerm enumeration)
            {
                if(WalkList(enumeration, origin) is not List<RdfTerm> members)
                {
                    return null;
                }

                //owl:oneOf with literal members is a data range misplaced in
                //class-expression position.
                foreach(RdfTerm member in members)
                {
                    if(member is Literal)
                    {
                        Report(WellKnownDiagnostics.Owl.MalformedClassExpression, $"owl:oneOf with literal members in class position near {Describe(origin)}.");

                        return null;
                    }
                }

                return new OwlObjectOneOf(members);
            }

            if(ObjectOf(node, OwlVocabulary.OnProperty) is RdfTerm onProperty)
            {
                return ConstructRestriction(node, onProperty, origin);
            }

            if(ObjectOf(node, OwlVocabulary.OnProperties) is not null)
            {
                Report(WellKnownDiagnostics.Owl.UnsupportedConstruct, $"N-ary owl:onProperties restriction near {Describe(origin)}.");

                return null;
            }

            Report(WellKnownDiagnostics.Owl.MalformedClassExpression, $"Unrecognised class expression structure near {Describe(origin)}.");

            return null;
        }

        private OwlClassExpression? ConstructRestriction(RdfTerm node, RdfTerm onProperty, Quad origin)
        {
            bool isDataProperty = onProperty is NamedNode namedProperty && DeclaredDataProperties.Contains(namedProperty.Iri);

            if(ObjectOf(node, OwlVocabulary.SomeValuesFrom) is RdfTerm someFiller)
            {
                if(isDataProperty || LooksLikeDataRange(someFiller))
                {
                    return onProperty is NamedNode dataProperty && ResolveDataRange(someFiller, origin) is OwlDataRange someRange
                        ? new OwlDataSomeValuesFrom([dataProperty], someRange)
                        : null;
                }

                return ResolvePropertyExpression(onProperty, origin) is OwlObjectPropertyExpression someProperty
                    && ClassExpressionCache.GetValueOrDefault(someFiller) is OwlClassExpression someClass
                    ? new OwlObjectSomeValuesFrom(someProperty, someClass)
                    : null;
            }

            if(ObjectOf(node, OwlVocabulary.AllValuesFrom) is RdfTerm allFiller)
            {
                if(isDataProperty || LooksLikeDataRange(allFiller))
                {
                    return onProperty is NamedNode dataProperty && ResolveDataRange(allFiller, origin) is OwlDataRange allRange
                        ? new OwlDataAllValuesFrom([dataProperty], allRange)
                        : null;
                }

                return ResolvePropertyExpression(onProperty, origin) is OwlObjectPropertyExpression allProperty
                    && ClassExpressionCache.GetValueOrDefault(allFiller) is OwlClassExpression allClass
                    ? new OwlObjectAllValuesFrom(allProperty, allClass)
                    : null;
            }

            if(ObjectOf(node, OwlVocabulary.HasValue) is RdfTerm hasValue)
            {
                if(hasValue is Literal literalValue)
                {
                    return onProperty is NamedNode dataProperty
                        ? new OwlDataHasValue(dataProperty, literalValue)
                        : null;
                }

                return ResolvePropertyExpression(onProperty, origin) is OwlObjectPropertyExpression valueProperty
                    ? new OwlObjectHasValue(valueProperty, hasValue)
                    : null;
            }

            if(ObjectOf(node, OwlVocabulary.HasSelf) is not null)
            {
                return ResolvePropertyExpression(onProperty, origin) is OwlObjectPropertyExpression selfProperty
                    ? new OwlObjectHasSelf(selfProperty)
                    : null;
            }

            (OwlCardinalityKind Kind, RdfTerm Value)? cardinality = CardinalityOf(node);
            if(cardinality is (OwlCardinalityKind kind, RdfTerm cardinalityValue))
            {
                if(cardinalityValue is not Literal cardinalityLiteral
                    || !int.TryParse(cardinalityLiteral.Value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int bound))
                {
                    Report(WellKnownDiagnostics.Owl.MalformedRestriction, $"Non-numeric cardinality near {Describe(origin)}.");

                    return null;
                }

                RdfTerm? onClass = ObjectOf(node, OwlVocabulary.OnClass);
                RdfTerm? onDataRange = ObjectOf(node, OwlVocabulary.OnDataRange);

                if(isDataProperty || onDataRange is not null)
                {
                    OwlDataRange? qualifier = onDataRange is null ? null : ResolveDataRange(onDataRange, origin);

                    return onProperty is NamedNode dataProperty
                        ? new OwlDataCardinality(kind, bound, dataProperty, qualifier)
                        : null;
                }

                OwlClassExpression? classQualifier = onClass is null ? null : ClassExpressionCache.GetValueOrDefault(onClass);

                return ResolvePropertyExpression(onProperty, origin) is OwlObjectPropertyExpression cardinalityProperty
                    ? new OwlObjectCardinality(kind, bound, cardinalityProperty, classQualifier)
                    : null;
            }

            Report(WellKnownDiagnostics.Owl.MalformedRestriction, $"owl:Restriction without a recognised restricting predicate near {Describe(origin)}.");

            return null;
        }

        private (OwlCardinalityKind Kind, RdfTerm Value)? CardinalityOf(RdfTerm node)
        {
            if(ObjectOf(node, OwlVocabulary.MinCardinality) is RdfTerm min)
            {
                return (OwlCardinalityKind.Min, min);
            }

            if(ObjectOf(node, OwlVocabulary.MaxCardinality) is RdfTerm max)
            {
                return (OwlCardinalityKind.Max, max);
            }

            if(ObjectOf(node, OwlVocabulary.Cardinality) is RdfTerm exact)
            {
                return (OwlCardinalityKind.Exact, exact);
            }

            if(ObjectOf(node, OwlVocabulary.MinQualifiedCardinality) is RdfTerm minQualified)
            {
                return (OwlCardinalityKind.Min, minQualified);
            }

            if(ObjectOf(node, OwlVocabulary.MaxQualifiedCardinality) is RdfTerm maxQualified)
            {
                return (OwlCardinalityKind.Max, maxQualified);
            }

            if(ObjectOf(node, OwlVocabulary.QualifiedCardinality) is RdfTerm exactQualified)
            {
                return (OwlCardinalityKind.Exact, exactQualified);
            }

            return null;
        }

        /// <summary>Resolves a node to an object property expression: a named property, or a blank <c>owl:inverseOf</c> node.</summary>
        private OwlObjectPropertyExpression? ResolvePropertyExpression(RdfTerm node, Quad origin)
        {
            if(node is NamedNode named)
            {
                return new OwlObjectPropertyReference(named);
            }

            if(node is BlankNode && ObjectOf(node, OwlVocabulary.InverseOf) is NamedNode inverted)
            {
                ConsumeSubjectTriples(node);

                return new OwlInverseObjectProperty(inverted);
            }

            Report(WellKnownDiagnostics.Owl.MalformedClassExpression, $"Unresolvable property expression near {Describe(origin)}.");

            return null;
        }

        /// <summary>Resolves a node to a data range with an explicit work stack.</summary>
        private OwlDataRange? ResolveDataRange(RdfTerm root, Quad origin)
        {
            if(DataRangeCache.TryGetValue(root, out OwlDataRange? cached))
            {
                return cached;
            }

            Stack<RdfTerm> work = new();
            work.Push(root);

            while(work.Count > 0)
            {
                RdfTerm node = work.Peek();
                if(DataRangeCache.ContainsKey(node))
                {
                    work.Pop();

                    continue;
                }

                if(node is NamedNode named)
                {
                    DataRangeCache[node] = new OwlDatatypeReference(named);
                    work.Pop();

                    continue;
                }

                if(node is not BlankNode)
                {
                    DataRangeCache[node] = null;
                    work.Pop();

                    continue;
                }

                List<RdfTerm> children = [];
                if(ObjectOf(node, OwlVocabulary.IntersectionOf) is RdfTerm intersection && WalkList(intersection, origin) is List<RdfTerm> intersectionMembers)
                {
                    children.AddRange(intersectionMembers);
                }
                else if(ObjectOf(node, OwlVocabulary.UnionOf) is RdfTerm union && WalkList(union, origin) is List<RdfTerm> unionMembers)
                {
                    children.AddRange(unionMembers);
                }
                else if(ObjectOf(node, OwlVocabulary.DatatypeComplementOf) is RdfTerm complement)
                {
                    children.Add(complement);
                }

                bool allResolved = true;
                foreach(RdfTerm child in children)
                {
                    if(!DataRangeCache.ContainsKey(child))
                    {
                        allResolved = false;
                        work.Push(child);
                    }
                }

                if(allResolved)
                {
                    DataRangeCache[node] = ConstructDataRange(node, origin);
                    work.Pop();
                }
            }

            return DataRangeCache[root];
        }

        private OwlDataRange? ConstructDataRange(RdfTerm node, Quad origin)
        {
            ConsumeSubjectTriples(node);

            if(ObjectOf(node, OwlVocabulary.OneOf) is RdfTerm enumeration)
            {
                if(WalkList(enumeration, origin) is not List<RdfTerm> members)
                {
                    return null;
                }

                List<Literal> literals = [];
                foreach(RdfTerm member in members)
                {
                    if(member is Literal literal)
                    {
                        literals.Add(literal);
                    }
                }

                return new OwlDataOneOf(literals);
            }

            if(ObjectOf(node, OwlVocabulary.OnDatatype) is NamedNode restrictedDatatype)
            {
                List<OwlFacetRestriction> facets = [];
                if(ObjectOf(node, OwlVocabulary.WithRestrictions) is RdfTerm restrictionList && WalkList(restrictionList, origin) is List<RdfTerm> facetNodes)
                {
                    foreach(RdfTerm facetNode in facetNodes)
                    {
                        if(BySubject.TryGetValue(facetNode, out List<Quad>? facetTriples))
                        {
                            foreach(Quad facetTriple in facetTriples)
                            {
                                Consume(facetTriple);
                                if(facetTriple.Object is Literal facetValue)
                                {
                                    facets.Add(new OwlFacetRestriction(facetTriple.Predicate, facetValue));
                                }
                            }
                        }
                    }
                }

                return new OwlDatatypeRestriction(restrictedDatatype, facets);
            }

            if(ObjectOf(node, OwlVocabulary.IntersectionOf) is RdfTerm intersection)
            {
                return WalkList(intersection, origin) is List<RdfTerm> members
                    ? new OwlDataIntersectionOf(ResolveCachedRanges(members))
                    : null;
            }

            if(ObjectOf(node, OwlVocabulary.UnionOf) is RdfTerm union)
            {
                return WalkList(union, origin) is List<RdfTerm> members
                    ? new OwlDataUnionOf(ResolveCachedRanges(members))
                    : null;
            }

            if(ObjectOf(node, OwlVocabulary.DatatypeComplementOf) is RdfTerm complement)
            {
                return DataRangeCache.GetValueOrDefault(complement) is OwlDataRange operand
                    ? new OwlDataComplementOf(operand)
                    : null;
            }

            Report(WellKnownDiagnostics.Owl.MalformedClassExpression, $"Unrecognised data range structure near {Describe(origin)}.");

            return null;
        }

        //Shared helpers.

        private List<OwlClassExpression> ResolveCached(List<RdfTerm> nodes)
        {
            List<OwlClassExpression> resolved = [];
            foreach(RdfTerm node in nodes)
            {
                if(ClassExpressionCache.GetValueOrDefault(node) is OwlClassExpression expression)
                {
                    resolved.Add(expression);
                }
            }

            return resolved;
        }

        private List<OwlDataRange> ResolveCachedRanges(List<RdfTerm> nodes)
        {
            List<OwlDataRange> resolved = [];
            foreach(RdfTerm node in nodes)
            {
                if(DataRangeCache.GetValueOrDefault(node) is OwlDataRange range)
                {
                    resolved.Add(range);
                }
            }

            return resolved;
        }

        private List<OwlClassExpression> ResolveAll(List<RdfTerm> nodes, Quad origin)
        {
            List<OwlClassExpression> resolved = [];
            foreach(RdfTerm node in nodes)
            {
                if(ResolveClassExpression(node, origin) is OwlClassExpression expression)
                {
                    resolved.Add(expression);
                }
            }

            return resolved;
        }

        private List<OwlObjectPropertyExpression> ResolveAllProperties(List<RdfTerm> nodes, Quad origin)
        {
            List<OwlObjectPropertyExpression> resolved = [];
            foreach(RdfTerm node in nodes)
            {
                if(ResolvePropertyExpression(node, origin) is OwlObjectPropertyExpression expression)
                {
                    resolved.Add(expression);
                }
            }

            return resolved;
        }

        /// <summary>Walks an RDF collection into its members, consuming the structure triples; <c>null</c> (with a diagnostic) on malformed chains.</summary>
        private List<RdfTerm>? WalkList(RdfTerm head, Quad origin)
        {
            List<RdfTerm> members = [];
            HashSet<RdfTerm> visited = [];
            RdfTerm current = head;

            while(true)
            {
                if(current is NamedNode named && named.Iri == RdfVocabulary.Rdf.Nil)
                {
                    return members;
                }

                if(!visited.Add(current))
                {
                    Report(WellKnownDiagnostics.Owl.MalformedList, $"Cyclic RDF list near {Describe(origin)}.");

                    return null;
                }

                RdfTerm? first = null;
                RdfTerm? rest = null;
                if(BySubject.TryGetValue(current, out List<Quad>? triples))
                {
                    foreach(Quad triple in triples)
                    {
                        if(triple.Predicate.Iri == RdfVocabulary.Rdf.First)
                        {
                            if(first is not null && !first.Equals(triple.Object))
                            {
                                Report(WellKnownDiagnostics.Owl.AmbiguousValue, $"Ambiguous rdf:first near {Describe(origin)}: a list cell carries several distinct values.");
                                Report(WellKnownDiagnostics.Owl.MalformedList, $"RDF list cell with several rdf:first values near {Describe(origin)}.");

                                return null;
                            }

                            first = triple.Object;
                            Consume(triple);
                        }
                        else if(triple.Predicate.Iri == RdfVocabulary.Rdf.Rest)
                        {
                            if(rest is not null && !rest.Equals(triple.Object))
                            {
                                Report(WellKnownDiagnostics.Owl.AmbiguousValue, $"Ambiguous rdf:rest near {Describe(origin)}: a list cell carries several distinct continuations.");
                                Report(WellKnownDiagnostics.Owl.MalformedList, $"RDF list cell with several rdf:rest values near {Describe(origin)}.");

                                return null;
                            }

                            rest = triple.Object;
                            Consume(triple);
                        }
                    }
                }

                if(first is null || rest is null)
                {
                    Report(WellKnownDiagnostics.Owl.MalformedList, $"RDF list node without rdf:first/rdf:rest near {Describe(origin)}.");

                    return null;
                }

                members.Add(first);
                current = rest;
            }
        }

        private List<RdfTerm>? MembersOf(Quad quad)
        {
            return ObjectOf(quad.Subject, OwlVocabulary.Members) is RdfTerm members ? WalkList(members, quad) : null;
        }

        private List<RdfTerm>? DistinctMembersOf(Quad quad)
        {
            return ObjectOf(quad.Subject, OwlVocabulary.DistinctMembers) is RdfTerm members ? WalkList(members, quad) : null;
        }

        //The exactly-one read of the reverse mapping: a position carrying
        //several distinct values matches no mapping pattern, so the read
        //reports the ambiguity and refuses, and the same triple set reads
        //identically in every quad order. An identical repeated row is one
        //fact and reads normally.
        private RdfTerm? ObjectOf(RdfTerm subject, Utf8String predicate)
        {
            RdfTerm? found = null;
            if(BySubject.TryGetValue(subject, out List<Quad>? triples))
            {
                foreach(Quad triple in triples)
                {
                    if(triple.Predicate.Iri != predicate)
                    {
                        continue;
                    }

                    if(found is null)
                    {
                        found = triple.Object;
                    }
                    else if(!found.Equals(triple.Object))
                    {
                        Report(WellKnownDiagnostics.Owl.AmbiguousValue, $"Ambiguous {predicate}: {subject} carries several distinct values.");

                        return null;
                    }
                }
            }

            return found;
        }

        //One owl:Axiom / owl:Annotation reification block, held until the
        //attachment pass: the annotated triple's terms and the node's own
        //annotation triples.
        private sealed record PendingReification(
            RdfTerm Node,
            bool AnnotatesAnnotation,
            RdfTerm? Source,
            RdfTerm? Property,
            RdfTerm? Target,
            List<OwlAnnotation> Direct);

        /// <summary>The reification blocks awaiting the attachment pass.</summary>
        private List<PendingReification> PendingReifications { get; } = [];

        private void CollectReification(RdfTerm node, bool annotatesAnnotation)
        {
            RdfTerm? source = null;
            RdfTerm? property = null;
            RdfTerm? target = null;
            bool ambiguous = false;
            List<OwlAnnotation> direct = [];

            if(BySubject.TryGetValue(node, out List<Quad>? triples))
            {
                foreach(Quad triple in triples)
                {
                    Consume(triple);
                    Utf8String predicate = triple.Predicate.Iri;
                    if(predicate == OwlVocabulary.AnnotatedSource)
                    {
                        ambiguous |= source is not null && !source.Equals(triple.Object);
                        source = triple.Object;
                    }
                    else if(predicate == OwlVocabulary.AnnotatedProperty)
                    {
                        ambiguous |= property is not null && !property.Equals(triple.Object);
                        property = triple.Object;
                    }
                    else if(predicate == OwlVocabulary.AnnotatedTarget)
                    {
                        ambiguous |= target is not null && !target.Equals(triple.Object);
                        target = triple.Object;
                    }
                    else if(predicate != RdfTypeIri)
                    {
                        direct.Add(new OwlAnnotation(triple.Predicate, triple.Object));
                    }
                }
            }

            //A reification member carrying several distinct values names no
            //one annotated triple; the block is refused whole.
            if(ambiguous)
            {
                Report(WellKnownDiagnostics.Owl.AmbiguousValue, $"Ambiguous reification: {node} carries several distinct values for an owl:annotatedSource/Property/Target member.");

                return;
            }

            PendingReifications.Add(new PendingReification(node, annotatesAnnotation, source, property, target, direct));
        }

        /// <summary>
        /// Attaches the collected reification blocks: nested
        /// <c>owl:Annotation</c> blocks fold into the annotation they
        /// annotate, leaf-first, so arbitrary nesting resolves without
        /// recursion; the <c>owl:Axiom</c> blocks then attach to every axiom
        /// whose origin is the annotated triple (a pairwise expansion shares
        /// its origin).
        /// </summary>
        public void AttachAnnotations()
        {
            if(PendingReifications.Count == 0)
            {
                return;
            }

            Dictionary<RdfTerm, PendingReification> byNode = [];
            List<PendingReification> pendingNested = [];
            foreach(PendingReification reification in PendingReifications)
            {
                byNode[reification.Node] = reification;
                if(reification.AnnotatesAnnotation)
                {
                    pendingNested.Add(reification);
                }
            }

            bool attachedAny = true;
            while(attachedAny && pendingNested.Count > 0)
            {
                attachedAny = false;
                for(int i = pendingNested.Count - 1; i >= 0; i--)
                {
                    PendingReification candidate = pendingNested[i];

                    //A block attaches once no remaining block annotates IT,
                    //so the deepest blocks fold first.
                    bool isAnnotated = false;
                    foreach(PendingReification other in pendingNested)
                    {
                        if(!ReferenceEquals(other, candidate) && Equals(other.Source, candidate.Node))
                        {
                            isAnnotated = true;

                            break;
                        }
                    }

                    if(isAnnotated)
                    {
                        continue;
                    }

                    if(candidate.Source is RdfTerm parentNode
                        && byNode.TryGetValue(parentNode, out PendingReification? parent)
                        && candidate.Property is NamedNode annotatedProperty)
                    {
                        for(int j = 0; j < parent.Direct.Count; j++)
                        {
                            OwlAnnotation entry = parent.Direct[j];
                            if(entry.Property == annotatedProperty && Equals(entry.Value, candidate.Target))
                            {
                                parent.Direct[j] = entry with { Annotations = entry.Annotations.AddRange(candidate.Direct) };

                                break;
                            }
                        }
                    }

                    pendingNested.RemoveAt(i);
                    attachedAny = true;
                }
            }

            Dictionary<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object), List<OwlAnnotation>> byTriple = [];
            foreach(PendingReification reification in PendingReifications)
            {
                if(reification.AnnotatesAnnotation
                    || reification.Source is not RdfTerm source
                    || reification.Property is not RdfTerm property
                    || reification.Target is not RdfTerm target)
                {
                    continue;
                }

                if(!byTriple.TryGetValue((source, property, target), out List<OwlAnnotation>? annotations))
                {
                    annotations = [];
                    byTriple[(source, property, target)] = annotations;
                }

                annotations.AddRange(reification.Direct);
            }

            for(int i = 0; i < Axioms.Count; i++)
            {
                OwlAxiom axiom = Axioms[i];
                if(byTriple.TryGetValue((axiom.Origin.Subject, axiom.Origin.Predicate, axiom.Origin.Object), out List<OwlAnnotation>? annotations)
                    && annotations.Count > 0)
                {
                    Axioms[i] = axiom with { Annotations = axiom.Annotations.AddRange(annotations) };
                }
            }
        }

        private void Consume(Quad quad)
        {
            Consumed.Add(quad);
        }

        private void ConsumeSubjectTriples(RdfTerm subject)
        {
            if(BySubject.TryGetValue(subject, out List<Quad>? triples))
            {
                foreach(Quad triple in triples)
                {
                    Consumed.Add(triple);
                }
            }
        }

        //A node looks like a data range when it is a known datatype IRI or a
        //blank node bearing datatype-restriction structure or literal-membered
        //owl:oneOf. The declaration index and IRI shape both feed the check.
        private bool LooksLikeDataRange(RdfTerm node)
        {
            if(node is NamedNode named)
            {
                return IsDatatypeIri(named.Iri);
            }

            if(node is not BlankNode)
            {
                return false;
            }

            if(ObjectOf(node, OwlVocabulary.OnDatatype) is not null || ObjectOf(node, OwlVocabulary.DatatypeComplementOf) is not null)
            {
                return true;
            }

            if(ObjectOf(node, RdfVocabulary.Rdfs.Datatype) is not null)
            {
                return true;
            }

            //A typing probe, not a single-valued read: rdf:type is
            //legitimately multi-valued, so the scan asks whether the
            //datatype typing is among the node's types.
            if(HasTypeTriple(node, RdfVocabulary.Rdfs.Datatype))
            {
                return true;
            }

            if(ObjectOf(node, OwlVocabulary.OneOf) is RdfTerm enumeration
                && ObjectOf(enumeration, RdfVocabulary.Rdf.First) is Literal)
            {
                return true;
            }

            return false;
        }

        //Whether the node carries an rdf:type triple naming the given type —
        //a membership probe over a multi-valued position, never an
        //exactly-one read.
        private bool HasTypeTriple(RdfTerm node, Utf8String typeIri)
        {
            if(BySubject.TryGetValue(node, out List<Quad>? triples))
            {
                foreach(Quad triple in triples)
                {
                    if(triple.Predicate.Iri == RdfTypeIri && triple.Object is NamedNode typeNode && typeNode.Iri == typeIri)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsDatatypeIri(Utf8String iri)
        {
            if(DeclaredDatatypes.Contains(iri))
            {
                return true;
            }

            string text = iri.ToString();

            return text.StartsWith(Vocabulary.Xsd.Namespace, StringComparison.Ordinal)
                || iri == RdfVocabulary.Rdfs.LiteralClass
                || iri == Vocabulary.Rdf.LangString
                || iri == Vocabulary.Rdf.XmlLiteral
                || iri == OwlVocabulary.Real
                || iri == OwlVocabulary.Rational
                || text == "http://www.w3.org/1999/02/22-rdf-syntax-ns#PlainLiteral";
        }

        private PropertyKind KindOfPropertyPair(RdfTerm first, RdfTerm second)
        {
            if(IsDeclaredKind(first, DeclaredObjectProperties) || IsDeclaredKind(second, DeclaredObjectProperties) || first is BlankNode || second is BlankNode)
            {
                return PropertyKind.Object;
            }

            if(IsDeclaredKind(first, DeclaredDataProperties) || IsDeclaredKind(second, DeclaredDataProperties))
            {
                return PropertyKind.Data;
            }

            if(IsDeclaredKind(first, DeclaredAnnotationProperties) || IsDeclaredKind(second, DeclaredAnnotationProperties))
            {
                return PropertyKind.Annotation;
            }

            Report(WellKnownDiagnostics.Owl.UndeclaredProperty, "Property axiom over undeclared properties; mapped as object properties.");

            return PropertyKind.Object;
        }

        private static bool IsDeclaredKind(RdfTerm term, HashSet<Utf8String> declared)
        {
            return term is NamedNode named && declared.Contains(named.Iri);
        }

        private bool IsObjectRestriction(RdfTerm node)
        {
            return ObjectOf(node, OwlVocabulary.OnProperty) is RdfTerm property
                && !(property is NamedNode named && DeclaredDataProperties.Contains(named.Iri));
        }

        private static bool IsStructuralPredicate(Utf8String predicate)
        {
            return predicate == OwlVocabulary.IntersectionOf
                || predicate == OwlVocabulary.UnionOf
                || predicate == OwlVocabulary.ComplementOf
                || predicate == OwlVocabulary.OneOf
                || predicate == OwlVocabulary.OnProperty
                || predicate == OwlVocabulary.OnProperties
                || predicate == OwlVocabulary.OnClass
                || predicate == OwlVocabulary.OnDataRange
                || predicate == OwlVocabulary.OnDatatype
                || predicate == OwlVocabulary.SomeValuesFrom
                || predicate == OwlVocabulary.AllValuesFrom
                || predicate == OwlVocabulary.HasValue
                || predicate == OwlVocabulary.HasSelf
                || predicate == OwlVocabulary.MinCardinality
                || predicate == OwlVocabulary.MaxCardinality
                || predicate == OwlVocabulary.Cardinality
                || predicate == OwlVocabulary.MinQualifiedCardinality
                || predicate == OwlVocabulary.MaxQualifiedCardinality
                || predicate == OwlVocabulary.QualifiedCardinality
                || predicate == OwlVocabulary.DatatypeComplementOf
                || predicate == OwlVocabulary.WithRestrictions
                || predicate == OwlVocabulary.Members
                || predicate == OwlVocabulary.DistinctMembers
                || predicate == OwlVocabulary.SourceIndividual
                || predicate == OwlVocabulary.AssertionProperty
                || predicate == OwlVocabulary.TargetIndividual
                || predicate == OwlVocabulary.TargetValue
                || predicate == OwlVocabulary.AnnotatedSource
                || predicate == OwlVocabulary.AnnotatedProperty
                || predicate == OwlVocabulary.AnnotatedTarget
                || predicate == OwlVocabulary.InverseOf
                || predicate == RdfVocabulary.Rdf.First
                || predicate == RdfVocabulary.Rdf.Rest;
        }

        private void Report(Utf8String code, string message)
        {
            Diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, default(SourceSpan), Utf8Strings.From(message)));
        }

        private static string Describe(Quad quad)
        {
            return $"({quad.Subject} {quad.Predicate} {quad.Object})";
        }

        private enum PropertyKind
        {
            Object = 0,
            Data = 1,
            Annotation = 2,
        }
    }
}
