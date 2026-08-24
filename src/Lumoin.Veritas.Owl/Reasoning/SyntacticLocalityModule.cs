using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// Syntactic ⊥-locality module extraction (Modular Reuse of Ontologies: Theory
/// and Practice, JAIR 2008): from a seed axiom set, the smallest axiom set the
/// syntactic test certifies as preserving every entailment over the seeds'
/// signature.
/// </summary>
/// <remarks>
/// <para>
/// <b>The test.</b> An axiom is ⊥-local with respect to a signature when
/// interpreting every class and property name outside the signature as
/// empty makes the axiom a tautology, by the standard syntactic
/// approximation: a class expression evaluates to bottom, top, or neither
/// (an out-of-signature class is bottom, an out-of-signature property
/// makes its existential restrictions bottom and its universal
/// restrictions top, and the connectives propagate), and each axiom form
/// has its tautology condition over those values — a subclass axiom is
/// local when its subclass is bottom or its superclass top, a domain
/// axiom when its property is empty or its domain top, and so on.
/// Extraction is the fixpoint: every non-local axiom joins the module and
/// its symbols join the signature, until nothing more joins. Superclass
/// chains of signature classes come in; subclass branches below the
/// signature stay out.
/// </para>
/// <para>
/// <b>Assertions join by signature relevance, not by the faithful rule.</b>
/// Under faithful ⊥-locality every positive assertion is non-local — a
/// property assertion is no tautology under any replacement — so a
/// faithful module swallows the entire ABox. Here a positive assertion
/// (class, property, same, different) joins only when it mentions a
/// signature symbol, growing the signature with its individuals so
/// assertion chains follow; negative assertions keep the faithful rule
/// (an out-of-signature property makes the negation hold vacuously). The
/// module therefore preserves entailments over the seed signature; an
/// inconsistency living entirely outside it is the materializer's to
/// find, not the module consumer's.
/// </para>
/// <para>
/// <b>Self-containment.</b> Declarations whose entity ends up in the
/// module signature are appended, so a consumer serialising the module
/// for an external reasoner has the declarations it needs. Annotation
/// axioms and imports carry no logical content and never join.
/// </para>
/// </remarks>
public static class SyntacticLocalityModule
{
    /// <summary>
    /// Extracts the ⊥-locality module of <paramref name="document"/> for
    /// the seed axioms.
    /// </summary>
    /// <param name="document">The ontology document the module draws from.</param>
    /// <param name="seeds">The seed axioms; always part of the module.</param>
    /// <returns>The module: the seeds, every axiom the fixpoint pulled in (in document order), and the declarations of module-signature entities.</returns>
    /// <exception cref="System.ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static IReadOnlyList<OwlAxiom> Extract(OwlOntologyDocument document, IReadOnlyCollection<OwlAxiom> seeds)
    {
        System.ArgumentNullException.ThrowIfNull(document);
        System.ArgumentNullException.ThrowIfNull(seeds);

        HashSet<Utf8String> signature = [];
        HashSet<OwlAxiom> inModule = [];
        foreach(OwlAxiom seed in seeds)
        {
            if(inModule.Add(seed))
            {
                CollectSignature(seed, signature);
            }
        }

        //The fixpoint: every round scans the axioms still outside the
        //module; a non-local one joins and grows the signature, which can
        //make further axioms non-local on the next round.
        bool changed = true;
        while(changed)
        {
            changed = false;
            foreach(OwlAxiom axiom in document.Axioms)
            {
                if(inModule.Contains(axiom) || IsLocal(axiom, signature))
                {
                    continue;
                }

                inModule.Add(axiom);
                CollectSignature(axiom, signature);
                changed = true;
            }
        }

        //The module in document order, with the declarations of
        //module-signature entities appended for self-containment.
        List<OwlAxiom> module = [];
        foreach(OwlAxiom axiom in document.Axioms)
        {
            bool include = axiom switch
            {
                OwlDeclarationAxiom declaration => inModule.Contains(axiom) || signature.Contains(declaration.Entity.Iri),
                _ => inModule.Contains(axiom)
            };

            if(include)
            {
                module.Add(axiom);
                inModule.Remove(axiom);
            }
        }

        //Seeds that did not come from the document's axiom list (an
        //already-extracted subset, say) still belong to the module.
        foreach(OwlAxiom seed in seeds)
        {
            if(inModule.Remove(seed))
            {
                module.Add(seed);
            }
        }

        return module;
    }

    /// <summary>The three-valued result of the syntactic locality evaluation of a class expression.</summary>
    private enum LocalityValue
    {
        /// <summary>Neither provably empty nor provably universal under the ⊥-replacement.</summary>
        Neither = 0,

        /// <summary>Provably empty under the ⊥-replacement.</summary>
        Bottom = 1,

        /// <summary>Provably universal under the ⊥-replacement.</summary>
        Top = 2,
    }

    /// <summary>
    /// Whether the axiom is ⊥-local with respect to the signature — a
    /// tautology once out-of-signature names are read as empty, by the
    /// per-form conditions, with positive assertions softened to
    /// signature relevance.
    /// </summary>
    /// <param name="axiom">The axiom to test.</param>
    /// <param name="signature">The current module signature.</param>
    /// <returns><see langword="true"/> when the axiom stays outside the module.</returns>
    private static bool IsLocal(OwlAxiom axiom, HashSet<Utf8String> signature)
    {
        return axiom switch
        {
            OwlDeclarationAxiom => true,
            OwlImportAxiom => true,
            OwlAnnotationAssertionAxiom => true,
            OwlSubAnnotationPropertyOfAxiom => true,
            OwlAnnotationPropertyDomainAxiom => true,
            OwlAnnotationPropertyRangeAxiom => true,
            OwlSubClassOfAxiom subClass =>
                Evaluate(subClass.SubClass, signature) == LocalityValue.Bottom || Evaluate(subClass.SuperClass, signature) == LocalityValue.Top,
            OwlEquivalentClassesAxiom equivalent => IsLocalEquivalence(equivalent, signature),
            OwlDisjointClassesAxiom disjoint => AtMostOneNonBottom(disjoint.Operands, signature),
            OwlDisjointUnionAxiom disjointUnion =>
                IsBottomClassName(disjointUnion.Class.Iri, signature) && AllBottom(disjointUnion.Operands, signature),
            OwlSubObjectPropertyOfAxiom subProperty =>
                IsBottomRole(subProperty.SubProperty, signature) || IsTopRole(subProperty.SuperProperty),
            OwlPropertyChainAxiom chain => AnyBottomRole(chain.Chain, signature) || IsTopRole(chain.SuperProperty),
            OwlEquivalentObjectPropertiesAxiom equivalentProperties =>
                IsBottomRole(equivalentProperties.First, signature) && IsBottomRole(equivalentProperties.Second, signature),
            OwlDisjointObjectPropertiesAxiom disjointProperties => AtMostOneNonBottomRole(disjointProperties.Operands, signature),
            OwlInverseObjectPropertiesAxiom inverse =>
                IsBottomRole(inverse.First, signature) && IsBottomRole(inverse.Second, signature),
            OwlObjectPropertyDomainAxiom domain =>
                IsBottomRole(domain.Property, signature) || Evaluate(domain.Domain, signature) == LocalityValue.Top,
            OwlObjectPropertyRangeAxiom range =>
                IsBottomRole(range.Property, signature) || Evaluate(range.Range, signature) == LocalityValue.Top,
            //An empty role is functional, transitive, symmetric,
            //asymmetric, and irreflexive vacuously — but never reflexive.
            OwlObjectPropertyCharacteristicAxiom characteristic =>
                characteristic.Characteristic != OwlPropertyCharacteristic.Reflexive && IsBottomRole(characteristic.Property, signature),
            OwlSubDataPropertyOfAxiom subData =>
                IsBottomDataProperty(subData.SubProperty.Iri, signature) || IsTopDataProperty(subData.SuperProperty.Iri),
            OwlEquivalentDataPropertiesAxiom equivalentData =>
                IsBottomDataProperty(equivalentData.First.Iri, signature) && IsBottomDataProperty(equivalentData.Second.Iri, signature),
            OwlDisjointDataPropertiesAxiom disjointData => AtMostOneNonBottomDataProperty(disjointData.Operands, signature),
            OwlDataPropertyDomainAxiom dataDomain =>
                IsBottomDataProperty(dataDomain.Property.Iri, signature) || Evaluate(dataDomain.Domain, signature) == LocalityValue.Top,
            OwlDataPropertyRangeAxiom dataRange => IsBottomDataProperty(dataRange.Property.Iri, signature),
            OwlFunctionalDataPropertyAxiom functionalData => IsBottomDataProperty(functionalData.Property.Iri, signature),
            OwlDatatypeDefinitionAxiom datatypeDefinition => !signature.Contains(datatypeDefinition.Datatype.Iri),
            OwlHasKeyAxiom hasKey => Evaluate(hasKey.Class, signature) == LocalityValue.Bottom,
            OwlClassAssertionAxiom classAssertion =>
                Evaluate(classAssertion.Class, signature) == LocalityValue.Top || !MentionsSignature(classAssertion, signature),
            OwlObjectPropertyAssertionAxiom or OwlDataPropertyAssertionAxiom or OwlSameIndividualAxiom or OwlDifferentIndividualsAxiom =>
                !MentionsSignature(axiom, signature),
            OwlNegativeObjectPropertyAssertionAxiom negative => IsBottomRole(negative.Property, signature),
            OwlNegativeDataPropertyAssertionAxiom negativeData => IsBottomDataProperty(negativeData.Property.Iri, signature),
            _ => false
        };
    }

    /// <summary>An equivalence is local when both sides are bottom or both sides are top.</summary>
    /// <param name="equivalent">The equivalence axiom.</param>
    /// <param name="signature">The current module signature.</param>
    /// <returns><see langword="true"/> when the equivalence is a tautology under the ⊥-replacement.</returns>
    private static bool IsLocalEquivalence(OwlEquivalentClassesAxiom equivalent, HashSet<Utf8String> signature)
    {
        LocalityValue first = Evaluate(equivalent.First, signature);
        LocalityValue second = Evaluate(equivalent.Second, signature);

        return (first == LocalityValue.Bottom && second == LocalityValue.Bottom)
            || (first == LocalityValue.Top && second == LocalityValue.Top);
    }

    /// <summary>Whether at most one operand fails to evaluate to bottom — pairwise disjointness of empties is vacuous.</summary>
    /// <param name="operands">The disjointness operands.</param>
    /// <param name="signature">The current module signature.</param>
    /// <returns><see langword="true"/> when the disjointness is a tautology under the ⊥-replacement.</returns>
    private static bool AtMostOneNonBottom(IReadOnlyList<OwlClassExpression> operands, HashSet<Utf8String> signature)
    {
        int nonBottom = 0;
        foreach(OwlClassExpression operand in operands)
        {
            if(Evaluate(operand, signature) != LocalityValue.Bottom && ++nonBottom > 1)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether every operand evaluates to bottom.</summary>
    /// <param name="operands">The expressions to evaluate.</param>
    /// <param name="signature">The current module signature.</param>
    /// <returns><see langword="true"/> when all operands are bottom.</returns>
    private static bool AllBottom(IReadOnlyList<OwlClassExpression> operands, HashSet<Utf8String> signature)
    {
        foreach(OwlClassExpression operand in operands)
        {
            if(Evaluate(operand, signature) != LocalityValue.Bottom)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether at most one property is non-empty under the ⊥-replacement.</summary>
    /// <param name="operands">The disjointness operands.</param>
    /// <param name="signature">The current module signature.</param>
    /// <returns><see langword="true"/> when the disjointness is vacuous.</returns>
    private static bool AtMostOneNonBottomRole(IReadOnlyList<OwlObjectPropertyExpression> operands, HashSet<Utf8String> signature)
    {
        int nonBottom = 0;
        foreach(OwlObjectPropertyExpression operand in operands)
        {
            if(!IsBottomRole(operand, signature) && ++nonBottom > 1)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether at most one data property is non-empty under the ⊥-replacement.</summary>
    /// <param name="operands">The disjointness operands.</param>
    /// <param name="signature">The current module signature.</param>
    /// <returns><see langword="true"/> when the disjointness is vacuous.</returns>
    private static bool AtMostOneNonBottomDataProperty(IReadOnlyList<NamedNode> operands, HashSet<Utf8String> signature)
    {
        int nonBottom = 0;
        foreach(NamedNode operand in operands)
        {
            if(!IsBottomDataProperty(operand.Iri, signature) && ++nonBottom > 1)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether any chain link is empty — an empty link empties the whole composition.</summary>
    /// <param name="chain">The chain links.</param>
    /// <param name="signature">The current module signature.</param>
    /// <returns><see langword="true"/> when the chain composes to the empty role.</returns>
    private static bool AnyBottomRole(IReadOnlyList<OwlObjectPropertyExpression> chain, HashSet<Utf8String> signature)
    {
        foreach(OwlObjectPropertyExpression link in chain)
        {
            if(IsBottomRole(link, signature))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The syntactic locality evaluation of a class expression: post-order
    /// over the expression tree with an explicit frame stack, memoised per
    /// call (the signature differs between calls).
    /// </summary>
    /// <param name="root">The expression to evaluate.</param>
    /// <param name="signature">The current module signature.</param>
    /// <returns>The expression's locality value.</returns>
    private static LocalityValue Evaluate(OwlClassExpression root, HashSet<Utf8String> signature)
    {
        Dictionary<OwlClassExpression, LocalityValue> results = [];
        Stack<(OwlClassExpression Node, bool ChildrenDone)> work = new();
        work.Push((root, false));

        while(work.Count > 0)
        {
            (OwlClassExpression node, bool childrenDone) = work.Pop();
            if(results.ContainsKey(node))
            {
                continue;
            }

            if(!childrenDone)
            {
                work.Push((node, true));
                foreach(OwlClassExpression child in ChildrenOf(node))
                {
                    work.Push((child, false));
                }

                continue;
            }

            results[node] = node switch
            {
                OwlClassReference reference => EvaluateReference(reference, signature),
                OwlObjectIntersectionOf intersection => EvaluateIntersection(intersection.Operands, results),
                OwlObjectUnionOf union => EvaluateUnion(union.Operands, results),
                OwlObjectComplementOf complement => results[complement.Operand] switch
                {
                    LocalityValue.Bottom => LocalityValue.Top,
                    LocalityValue.Top => LocalityValue.Bottom,
                    _ => LocalityValue.Neither
                },
                OwlObjectOneOf => LocalityValue.Neither,
                OwlObjectSomeValuesFrom some =>
                    IsBottomRole(some.Property, signature) || results[some.Filler] == LocalityValue.Bottom
                        ? LocalityValue.Bottom
                        : LocalityValue.Neither,
                OwlObjectAllValuesFrom all =>
                    IsBottomRole(all.Property, signature) || results[all.Filler] == LocalityValue.Top
                        ? LocalityValue.Top
                        : LocalityValue.Neither,
                OwlObjectHasValue hasValue => IsBottomRole(hasValue.Property, signature) ? LocalityValue.Bottom : LocalityValue.Neither,
                OwlObjectHasSelf hasSelf => IsBottomRole(hasSelf.Property, signature) ? LocalityValue.Bottom : LocalityValue.Neither,
                OwlObjectCardinality cardinality => EvaluateCardinality(
                    cardinality.Kind,
                    cardinality.Cardinality,
                    IsBottomRole(cardinality.Property, signature) || (cardinality.Filler is not null && results[cardinality.Filler] == LocalityValue.Bottom)),
                OwlDataSomeValuesFrom dataSome => AnyBottomDataProperty(dataSome.Properties, signature) ? LocalityValue.Bottom : LocalityValue.Neither,
                OwlDataAllValuesFrom dataAll => AnyBottomDataProperty(dataAll.Properties, signature) ? LocalityValue.Top : LocalityValue.Neither,
                OwlDataHasValue dataHas => IsBottomDataProperty(dataHas.Property.Iri, signature) ? LocalityValue.Bottom : LocalityValue.Neither,
                OwlDataCardinality dataCardinality => EvaluateCardinality(
                    dataCardinality.Kind,
                    dataCardinality.Cardinality,
                    IsBottomDataProperty(dataCardinality.Property.Iri, signature)),
                _ => LocalityValue.Neither
            };
        }

        return results[root];
    }

    /// <summary>The child expressions the post-order walk descends into.</summary>
    /// <param name="node">The expression node.</param>
    /// <returns>The children; empty for leaves.</returns>
    private static IEnumerable<OwlClassExpression> ChildrenOf(OwlClassExpression node)
    {
        return node switch
        {
            OwlObjectIntersectionOf intersection => intersection.Operands,
            OwlObjectUnionOf union => union.Operands,
            OwlObjectComplementOf complement => [complement.Operand],
            OwlObjectSomeValuesFrom some => [some.Filler],
            OwlObjectAllValuesFrom all => [all.Filler],
            OwlObjectCardinality { Filler: not null } cardinality => [cardinality.Filler!],
            _ => []
        };
    }

    /// <summary>A named class is bottom outside the signature; the built-ins keep their fixed reading.</summary>
    /// <param name="reference">The class reference.</param>
    /// <param name="signature">The current module signature.</param>
    /// <returns>The reference's locality value.</returns>
    private static LocalityValue EvaluateReference(OwlClassReference reference, HashSet<Utf8String> signature)
    {
        Utf8String iri = reference.Class.Iri;

        return iri.Equals(OwlVocabulary.Nothing)
            ? LocalityValue.Bottom
            : iri.Equals(OwlVocabulary.Thing)
                ? LocalityValue.Top
                : signature.Contains(iri) ? LocalityValue.Neither : LocalityValue.Bottom;
    }

    /// <summary>An intersection is bottom with any bottom operand and top with all-top operands.</summary>
    /// <param name="operands">The intersection operands.</param>
    /// <param name="results">The memoised child evaluations.</param>
    /// <returns>The intersection's locality value.</returns>
    private static LocalityValue EvaluateIntersection(IReadOnlyList<OwlClassExpression> operands, Dictionary<OwlClassExpression, LocalityValue> results)
    {
        bool allTop = true;
        foreach(OwlClassExpression operand in operands)
        {
            LocalityValue value = results[operand];
            if(value == LocalityValue.Bottom)
            {
                return LocalityValue.Bottom;
            }

            allTop &= value == LocalityValue.Top;
        }

        return allTop ? LocalityValue.Top : LocalityValue.Neither;
    }

    /// <summary>A union is top with any top operand and bottom with all-bottom operands.</summary>
    /// <param name="operands">The union operands.</param>
    /// <param name="results">The memoised child evaluations.</param>
    /// <returns>The union's locality value.</returns>
    private static LocalityValue EvaluateUnion(IReadOnlyList<OwlClassExpression> operands, Dictionary<OwlClassExpression, LocalityValue> results)
    {
        bool allBottom = true;
        foreach(OwlClassExpression operand in operands)
        {
            LocalityValue value = results[operand];
            if(value == LocalityValue.Top)
            {
                return LocalityValue.Top;
            }

            allBottom &= value == LocalityValue.Bottom;
        }

        return allBottom ? LocalityValue.Bottom : LocalityValue.Neither;
    }

    /// <summary>
    /// A cardinality restriction over an empty base: at-least-zero is
    /// top, at-least-n is bottom, at-most-n is top, and exactly-n splits
    /// on whether n is zero.
    /// </summary>
    /// <param name="kind">The cardinality flavour.</param>
    /// <param name="cardinality">The bound.</param>
    /// <param name="emptyBase">Whether the property is empty or the filler bottom under the ⊥-replacement.</param>
    /// <returns>The restriction's locality value.</returns>
    private static LocalityValue EvaluateCardinality(OwlCardinalityKind kind, int cardinality, bool emptyBase)
    {
        return kind switch
        {
            OwlCardinalityKind.Min when cardinality == 0 => LocalityValue.Top,
            OwlCardinalityKind.Min => emptyBase ? LocalityValue.Bottom : LocalityValue.Neither,
            OwlCardinalityKind.Max => emptyBase ? LocalityValue.Top : LocalityValue.Neither,
            OwlCardinalityKind.Exact when cardinality == 0 => emptyBase ? LocalityValue.Top : LocalityValue.Neither,
            OwlCardinalityKind.Exact => emptyBase ? LocalityValue.Bottom : LocalityValue.Neither,
            _ => LocalityValue.Neither
        };
    }

    /// <summary>An object property expression is empty when its named property is outside the signature; the built-in top and bottom keep their fixed reading. Inversion preserves emptiness.</summary>
    /// <param name="property">The property expression.</param>
    /// <param name="signature">The current module signature.</param>
    /// <returns><see langword="true"/> when the expression reads as the empty role.</returns>
    private static bool IsBottomRole(OwlObjectPropertyExpression property, HashSet<Utf8String> signature)
    {
        Utf8String iri = property.Property.Iri;

        return iri.Equals(OwlVocabulary.BottomObjectProperty)
            || (!iri.Equals(OwlVocabulary.TopObjectProperty) && !signature.Contains(iri));
    }

    /// <summary>Whether the expression is the built-in top object property — every inclusion into it is a tautology.</summary>
    /// <param name="property">The property expression.</param>
    /// <returns><see langword="true"/> for <c>owl:topObjectProperty</c>.</returns>
    private static bool IsTopRole(OwlObjectPropertyExpression property)
    {
        return property.Property.Iri.Equals(OwlVocabulary.TopObjectProperty) && !property.IsInverse;
    }

    /// <summary>A data property is empty when outside the signature; the built-in top and bottom keep their fixed reading.</summary>
    /// <param name="iri">The data property IRI.</param>
    /// <param name="signature">The current module signature.</param>
    /// <returns><see langword="true"/> when the property reads as empty.</returns>
    private static bool IsBottomDataProperty(Utf8String iri, HashSet<Utf8String> signature)
    {
        return iri.Equals(OwlVocabulary.BottomDataProperty)
            || (!iri.Equals(OwlVocabulary.TopDataProperty) && !signature.Contains(iri));
    }

    /// <summary>Whether the IRI is the built-in top data property.</summary>
    /// <param name="iri">The data property IRI.</param>
    /// <returns><see langword="true"/> for <c>owl:topDataProperty</c>.</returns>
    private static bool IsTopDataProperty(Utf8String iri)
    {
        return iri.Equals(OwlVocabulary.TopDataProperty);
    }

    /// <summary>A named class is bottom outside the signature, mirroring <see cref="EvaluateReference"/> for bare class names.</summary>
    /// <param name="iri">The class IRI.</param>
    /// <param name="signature">The current module signature.</param>
    /// <returns><see langword="true"/> when the class reads as empty.</returns>
    private static bool IsBottomClassName(Utf8String iri, HashSet<Utf8String> signature)
    {
        return iri.Equals(OwlVocabulary.Nothing)
            || (!iri.Equals(OwlVocabulary.Thing) && !signature.Contains(iri));
    }

    /// <summary>Whether any of the n-ary restriction's data properties is empty under the ⊥-replacement.</summary>
    /// <param name="properties">The restricted data properties.</param>
    /// <param name="signature">The current module signature.</param>
    /// <returns><see langword="true"/> when any property reads as empty.</returns>
    private static bool AnyBottomDataProperty(IReadOnlyList<NamedNode> properties, HashSet<Utf8String> signature)
    {
        foreach(NamedNode property in properties)
        {
            if(IsBottomDataProperty(property.Iri, signature))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the axiom's signature intersects the module signature — the relevance test positive assertions join by.</summary>
    /// <param name="axiom">The assertion axiom.</param>
    /// <param name="signature">The current module signature.</param>
    /// <returns><see langword="true"/> when any symbol of the axiom is in the signature.</returns>
    private static bool MentionsSignature(OwlAxiom axiom, HashSet<Utf8String> signature)
    {
        HashSet<Utf8String> mentioned = [];
        CollectSignature(axiom, mentioned);
        foreach(Utf8String symbol in mentioned)
        {
            if(signature.Contains(symbol))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Collects the axiom's signature — class, property, datatype, and
    /// individual names in logical positions; annotations are not logical
    /// content and the built-in top and bottom entities never enter.
    /// </summary>
    /// <param name="axiom">The axiom to collect from.</param>
    /// <param name="signature">The set receiving the symbols.</param>
    private static void CollectSignature(OwlAxiom axiom, HashSet<Utf8String> signature)
    {
        switch(axiom)
        {
            case OwlDeclarationAxiom declaration:
                AddName(declaration.Entity.Iri, signature);

                break;
            case OwlSubClassOfAxiom subClass:
                CollectExpression(subClass.SubClass, signature);
                CollectExpression(subClass.SuperClass, signature);

                break;
            case OwlEquivalentClassesAxiom equivalent:
                CollectExpression(equivalent.First, signature);
                CollectExpression(equivalent.Second, signature);

                break;
            case OwlDisjointClassesAxiom disjoint:
                CollectExpressions(disjoint.Operands, signature);

                break;
            case OwlDisjointUnionAxiom disjointUnion:
                AddName(disjointUnion.Class.Iri, signature);
                CollectExpressions(disjointUnion.Operands, signature);

                break;
            case OwlSubObjectPropertyOfAxiom subProperty:
                AddRole(subProperty.SubProperty, signature);
                AddRole(subProperty.SuperProperty, signature);

                break;
            case OwlPropertyChainAxiom chain:
                foreach(OwlObjectPropertyExpression link in chain.Chain)
                {
                    AddRole(link, signature);
                }

                AddRole(chain.SuperProperty, signature);

                break;
            case OwlEquivalentObjectPropertiesAxiom equivalentProperties:
                AddRole(equivalentProperties.First, signature);
                AddRole(equivalentProperties.Second, signature);

                break;
            case OwlDisjointObjectPropertiesAxiom disjointProperties:
                foreach(OwlObjectPropertyExpression operand in disjointProperties.Operands)
                {
                    AddRole(operand, signature);
                }

                break;
            case OwlInverseObjectPropertiesAxiom inverse:
                AddRole(inverse.First, signature);
                AddRole(inverse.Second, signature);

                break;
            case OwlObjectPropertyDomainAxiom domain:
                AddRole(domain.Property, signature);
                CollectExpression(domain.Domain, signature);

                break;
            case OwlObjectPropertyRangeAxiom range:
                AddRole(range.Property, signature);
                CollectExpression(range.Range, signature);

                break;
            case OwlObjectPropertyCharacteristicAxiom characteristic:
                AddRole(characteristic.Property, signature);

                break;
            case OwlSubDataPropertyOfAxiom subData:
                AddName(subData.SubProperty.Iri, signature);
                AddName(subData.SuperProperty.Iri, signature);

                break;
            case OwlEquivalentDataPropertiesAxiom equivalentData:
                AddName(equivalentData.First.Iri, signature);
                AddName(equivalentData.Second.Iri, signature);

                break;
            case OwlDisjointDataPropertiesAxiom disjointData:
                foreach(NamedNode operand in disjointData.Operands)
                {
                    AddName(operand.Iri, signature);
                }

                break;
            case OwlDataPropertyDomainAxiom dataDomain:
                AddName(dataDomain.Property.Iri, signature);
                CollectExpression(dataDomain.Domain, signature);

                break;
            case OwlDataPropertyRangeAxiom dataRange:
                AddName(dataRange.Property.Iri, signature);
                CollectDataRange(dataRange.Range, signature);

                break;
            case OwlFunctionalDataPropertyAxiom functionalData:
                AddName(functionalData.Property.Iri, signature);

                break;
            case OwlDatatypeDefinitionAxiom datatypeDefinition:
                AddName(datatypeDefinition.Datatype.Iri, signature);
                CollectDataRange(datatypeDefinition.Range, signature);

                break;
            case OwlHasKeyAxiom hasKey:
                CollectExpression(hasKey.Class, signature);
                foreach(OwlObjectPropertyExpression objectProperty in hasKey.ObjectProperties)
                {
                    AddRole(objectProperty, signature);
                }

                foreach(NamedNode dataProperty in hasKey.DataProperties)
                {
                    AddName(dataProperty.Iri, signature);
                }

                break;
            case OwlClassAssertionAxiom classAssertion:
                CollectExpression(classAssertion.Class, signature);
                AddIndividual(classAssertion.Individual, signature);

                break;
            case OwlObjectPropertyAssertionAxiom assertion:
                AddName(assertion.Property.Iri, signature);
                AddIndividual(assertion.Source, signature);
                AddIndividual(assertion.Target, signature);

                break;
            case OwlNegativeObjectPropertyAssertionAxiom negative:
                AddRole(negative.Property, signature);
                AddIndividual(negative.Source, signature);
                AddIndividual(negative.Target, signature);

                break;
            case OwlDataPropertyAssertionAxiom dataAssertion:
                AddName(dataAssertion.Property.Iri, signature);
                AddIndividual(dataAssertion.Source, signature);

                break;
            case OwlNegativeDataPropertyAssertionAxiom negativeData:
                AddName(negativeData.Property.Iri, signature);
                AddIndividual(negativeData.Source, signature);

                break;
            case OwlSameIndividualAxiom same:
                AddIndividual(same.First, signature);
                AddIndividual(same.Second, signature);

                break;
            case OwlDifferentIndividualsAxiom different:
                foreach(RdfTerm individual in different.Individuals)
                {
                    AddIndividual(individual, signature);
                }

                break;
            default:
                break;
        }
    }

    /// <summary>Collects the signatures of several class expressions.</summary>
    /// <param name="expressions">The expressions to collect from.</param>
    /// <param name="signature">The set receiving the symbols.</param>
    private static void CollectExpressions(IReadOnlyList<OwlClassExpression> expressions, HashSet<Utf8String> signature)
    {
        foreach(OwlClassExpression expression in expressions)
        {
            CollectExpression(expression, signature);
        }
    }

    /// <summary>Collects a class expression's signature with an explicit work stack.</summary>
    /// <param name="root">The expression to collect from.</param>
    /// <param name="signature">The set receiving the symbols.</param>
    private static void CollectExpression(OwlClassExpression root, HashSet<Utf8String> signature)
    {
        Stack<OwlClassExpression> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            OwlClassExpression node = work.Pop();
            switch(node)
            {
                case OwlClassReference reference:
                    AddClassName(reference.Class.Iri, signature);

                    break;
                case OwlObjectIntersectionOf intersection:
                    foreach(OwlClassExpression operand in intersection.Operands)
                    {
                        work.Push(operand);
                    }

                    break;
                case OwlObjectUnionOf union:
                    foreach(OwlClassExpression operand in union.Operands)
                    {
                        work.Push(operand);
                    }

                    break;
                case OwlObjectComplementOf complement:
                    work.Push(complement.Operand);

                    break;
                case OwlObjectOneOf oneOf:
                    foreach(RdfTerm individual in oneOf.Individuals)
                    {
                        AddIndividual(individual, signature);
                    }

                    break;
                case OwlObjectSomeValuesFrom some:
                    AddRole(some.Property, signature);
                    work.Push(some.Filler);

                    break;
                case OwlObjectAllValuesFrom all:
                    AddRole(all.Property, signature);
                    work.Push(all.Filler);

                    break;
                case OwlObjectHasValue hasValue:
                    AddRole(hasValue.Property, signature);
                    AddIndividual(hasValue.Individual, signature);

                    break;
                case OwlObjectHasSelf hasSelf:
                    AddRole(hasSelf.Property, signature);

                    break;
                case OwlObjectCardinality cardinality:
                    AddRole(cardinality.Property, signature);
                    if(cardinality.Filler is not null)
                    {
                        work.Push(cardinality.Filler);
                    }

                    break;
                case OwlDataSomeValuesFrom dataSome:
                    AddNames(dataSome.Properties, signature);
                    CollectDataRange(dataSome.Range, signature);

                    break;
                case OwlDataAllValuesFrom dataAll:
                    AddNames(dataAll.Properties, signature);
                    CollectDataRange(dataAll.Range, signature);

                    break;
                case OwlDataHasValue dataHas:
                    AddName(dataHas.Property.Iri, signature);

                    break;
                case OwlDataCardinality dataCardinality:
                    AddName(dataCardinality.Property.Iri, signature);
                    if(dataCardinality.Range is not null)
                    {
                        CollectDataRange(dataCardinality.Range, signature);
                    }

                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>Collects a data range's datatype names with an explicit work stack.</summary>
    /// <param name="root">The range to collect from.</param>
    /// <param name="signature">The set receiving the symbols.</param>
    private static void CollectDataRange(OwlDataRange root, HashSet<Utf8String> signature)
    {
        Stack<OwlDataRange> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            OwlDataRange node = work.Pop();
            switch(node)
            {
                case OwlDatatypeReference reference:
                    AddName(reference.Datatype.Iri, signature);

                    break;
                case OwlDataIntersectionOf intersection:
                    foreach(OwlDataRange range in intersection.Ranges)
                    {
                        work.Push(range);
                    }

                    break;
                case OwlDataUnionOf union:
                    foreach(OwlDataRange range in union.Ranges)
                    {
                        work.Push(range);
                    }

                    break;
                case OwlDataComplementOf complement:
                    work.Push(complement.Range);

                    break;
                case OwlDatatypeRestriction restriction:
                    AddName(restriction.Datatype.Iri, signature);

                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>Adds a class name, excluding the built-ins whose reading is fixed.</summary>
    /// <param name="iri">The class IRI.</param>
    /// <param name="signature">The set receiving the symbol.</param>
    private static void AddClassName(Utf8String iri, HashSet<Utf8String> signature)
    {
        if(!iri.Equals(OwlVocabulary.Thing) && !iri.Equals(OwlVocabulary.Nothing))
        {
            signature.Add(iri);
        }
    }

    /// <summary>Adds an object property expression's named property, excluding the built-ins whose reading is fixed.</summary>
    /// <param name="property">The property expression.</param>
    /// <param name="signature">The set receiving the symbol.</param>
    private static void AddRole(OwlObjectPropertyExpression property, HashSet<Utf8String> signature)
    {
        Utf8String iri = property.Property.Iri;
        if(!iri.Equals(OwlVocabulary.TopObjectProperty) && !iri.Equals(OwlVocabulary.BottomObjectProperty))
        {
            signature.Add(iri);
        }
    }

    /// <summary>Adds a named symbol, excluding the built-in top and bottom data properties.</summary>
    /// <param name="iri">The symbol IRI.</param>
    /// <param name="signature">The set receiving the symbol.</param>
    private static void AddName(Utf8String iri, HashSet<Utf8String> signature)
    {
        if(!iri.Equals(OwlVocabulary.TopDataProperty) && !iri.Equals(OwlVocabulary.BottomDataProperty))
        {
            signature.Add(iri);
        }
    }

    /// <summary>Adds several named symbols.</summary>
    /// <param name="names">The symbols to add.</param>
    /// <param name="signature">The set receiving the symbols.</param>
    private static void AddNames(IReadOnlyList<NamedNode> names, HashSet<Utf8String> signature)
    {
        foreach(NamedNode name in names)
        {
            AddName(name.Iri, signature);
        }
    }

    /// <summary>Adds an individual: a named individual by IRI, an anonymous one by label; literals carry no signature.</summary>
    /// <param name="individual">The individual term.</param>
    /// <param name="signature">The set receiving the symbol.</param>
    private static void AddIndividual(RdfTerm individual, HashSet<Utf8String> signature)
    {
        switch(individual)
        {
            case NamedNode named:
                signature.Add(named.Iri);

                break;
            case BlankNode blank:
                signature.Add(blank.Label);

                break;
            default:
                break;
        }
    }
}
