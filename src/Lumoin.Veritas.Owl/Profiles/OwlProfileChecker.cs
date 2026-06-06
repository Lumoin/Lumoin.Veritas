using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Owl.Profiles;

/// <summary>
/// Checks an OWL 2 structural document against the EL, QL, and RL profile
/// grammars of <see href="https://www.w3.org/TR/owl2-profiles/">OWL 2 Profiles</see>,
/// reporting every violation with its origin triple.
/// </summary>
/// <remarks>
/// <para>
/// Each profile is a grammar restriction over the structural syntax: which
/// axioms may appear, and which class expressions may appear in which
/// position (EL's expression grammar is positionally uniform; QL and RL
/// distinguish subclass, superclass, and — for RL — equivalence positions).
/// Expression trees are walked with an explicit stack of positioned nodes;
/// the no-recursion discipline holds.
/// </para>
/// <para>
/// A document whose mapping recorded errors is not structurally a
/// well-formed OWL 2 ontology and is therefore outside every profile; the
/// report carries one violation per profile saying so.
/// </para>
/// </remarks>
public static class OwlProfileChecker
{
    /// <summary>The expression position a profile grammar distinguishes.</summary>
    private enum Position
    {
        /// <summary>Subclass side (the left of <c>SubClassOf</c> and the positions that inherit its grammar).</summary>
        Sub = 0,

        /// <summary>Superclass side (the right of <c>SubClassOf</c>, domains, ranges, assertions).</summary>
        Super = 1,

        /// <summary>An operand of <c>EquivalentClasses</c> (RL has a dedicated grammar for it).</summary>
        Equivalence = 2,
    }

    /// <summary>The datatypes OWL 2 EL and QL admit (Profiles §2.4 / §3.3 — the shared list whose value spaces are infinite or trivial).</summary>
    private static IReadOnlySet<Utf8String> ElQlDatatypes { get; } = OwlDatatypeMap.ElQl;

    /// <summary>The datatypes OWL 2 RL admits (Profiles §4.3 — the full datatype map except <c>owl:real</c> and <c>owl:rational</c>).</summary>
    private static IReadOnlySet<Utf8String> RlDatatypes { get; } = OwlDatatypeMap.Rl;

    /// <summary>
    /// Checks the document against all three profiles.
    /// </summary>
    /// <param name="document">The mapped ontology document.</param>
    /// <returns>The membership report with per-profile violations.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static OwlProfileReport Check(OwlOntologyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<OwlProfileViolation> violations = [];

        //A graph the mapping could not read as structural OWL 2 is outside
        //every profile — the profiles are defined over OWL 2 ontologies.
        if(document.Diagnostics.HasErrors)
        {
            string detail = DescribeFirstError(document);
            violations.Add(new OwlProfileViolation(OwlProfiles.El, Origin: null, $"Not structurally a well-formed OWL 2 ontology: {detail}"));
            violations.Add(new OwlProfileViolation(OwlProfiles.Ql, Origin: null, $"Not structurally a well-formed OWL 2 ontology: {detail}"));
            violations.Add(new OwlProfileViolation(OwlProfiles.Rl, Origin: null, $"Not structurally a well-formed OWL 2 ontology: {detail}"));

            return new OwlProfileReport(OwlProfiles.None, violations);
        }

        //Anonymous individuals that appear as object-property-assertion
        //targets are rolled-up existentials; the individual rules below
        //distinguish them from free-floating anonymous roots.
        HashSet<RdfTerm> anonymousTargets = [];
        foreach(OwlAxiom axiom in document.Axioms)
        {
            if(axiom is OwlObjectPropertyAssertionAxiom { Target: BlankNode target })
            {
                anonymousTargets.Add(target);
            }
        }

        foreach(OwlAxiom axiom in document.Axioms)
        {
            CheckEl(axiom, violations);
            CheckQl(axiom, violations);
            CheckRl(axiom, violations);
            CheckIndividuals(axiom, anonymousTargets, violations);
        }

        OwlProfiles memberships = OwlProfiles.El | OwlProfiles.Ql | OwlProfiles.Rl;
        foreach(OwlProfileViolation violation in violations)
        {
            memberships &= ~violation.Profile;
        }

        return new OwlProfileReport(memberships, violations);
    }

    //EL (Profiles §2): a positionally uniform expression grammar; the
    //axiom set excludes everything inverse-flavoured, universal, cardinal,
    //and disjunctive.

    private static void CheckEl(OwlAxiom axiom, List<OwlProfileViolation> violations)
    {
        switch(axiom)
        {
            case OwlSubClassOfAxiom subClass:
            {
                RequireElExpression(subClass.SubClass, axiom, violations);
                RequireElExpression(subClass.SuperClass, axiom, violations);
                break;
            }
            case OwlEquivalentClassesAxiom equivalent:
            {
                RequireElExpression(equivalent.First, axiom, violations);
                RequireElExpression(equivalent.Second, axiom, violations);
                break;
            }
            case OwlDisjointClassesAxiom disjoint:
            {
                foreach(OwlClassExpression operand in disjoint.Operands)
                {
                    RequireElExpression(operand, axiom, violations);
                }

                break;
            }
            case OwlDisjointUnionAxiom:
            {
                violations.Add(new OwlProfileViolation(OwlProfiles.El, axiom.Origin, "DisjointUnion is not in EL."));
                break;
            }
            case OwlSubObjectPropertyOfAxiom subProperty:
            {
                RequireElProperty(subProperty.SubProperty, axiom, violations);
                RequireElProperty(subProperty.SuperProperty, axiom, violations);
                break;
            }
            case OwlPropertyChainAxiom chain:
            {
                foreach(OwlObjectPropertyExpression link in chain.Chain)
                {
                    RequireElProperty(link, axiom, violations);
                }

                RequireElProperty(chain.SuperProperty, axiom, violations);
                break;
            }
            case OwlEquivalentObjectPropertiesAxiom equivalentProperties:
            {
                RequireElProperty(equivalentProperties.First, axiom, violations);
                RequireElProperty(equivalentProperties.Second, axiom, violations);
                break;
            }
            case OwlDisjointObjectPropertiesAxiom:
            {
                violations.Add(new OwlProfileViolation(OwlProfiles.El, axiom.Origin, "DisjointObjectProperties is not in EL."));
                break;
            }
            case OwlInverseObjectPropertiesAxiom:
            {
                violations.Add(new OwlProfileViolation(OwlProfiles.El, axiom.Origin, "InverseObjectProperties is not in EL."));
                break;
            }
            case OwlObjectPropertyCharacteristicAxiom characteristic:
            {
                if(characteristic.Characteristic is not (OwlPropertyCharacteristic.Transitive or OwlPropertyCharacteristic.Reflexive))
                {
                    violations.Add(new OwlProfileViolation(OwlProfiles.El, axiom.Origin, $"{characteristic.Characteristic}ObjectProperty is not in EL."));
                }

                RequireElProperty(characteristic.Property, axiom, violations);
                break;
            }
            case OwlObjectPropertyDomainAxiom domain:
            {
                RequireElProperty(domain.Property, axiom, violations);
                RequireElExpression(domain.Domain, axiom, violations);
                break;
            }
            case OwlObjectPropertyRangeAxiom range:
            {
                RequireElProperty(range.Property, axiom, violations);
                RequireElExpression(range.Range, axiom, violations);
                break;
            }
            case OwlDataPropertyDomainAxiom dataDomain:
            {
                RequireElExpression(dataDomain.Domain, axiom, violations);
                break;
            }
            case OwlDataPropertyRangeAxiom dataRange:
            {
                RequireElDataRange(dataRange.Range, axiom, violations);
                break;
            }
            case OwlDatatypeDefinitionAxiom definition:
            {
                RequireElDataRange(definition.Range, axiom, violations);
                break;
            }
            case OwlHasKeyAxiom hasKey:
            {
                RequireElExpression(hasKey.Class, axiom, violations);
                foreach(OwlObjectPropertyExpression key in hasKey.ObjectProperties)
                {
                    RequireElProperty(key, axiom, violations);
                }

                break;
            }
            case OwlClassAssertionAxiom assertion:
            {
                RequireElExpression(assertion.Class, axiom, violations);
                break;
            }
            case OwlNegativeObjectPropertyAssertionAxiom negativeObject:
            {
                RequireElProperty(negativeObject.Property, axiom, violations);
                break;
            }
            case OwlDisjointDataPropertiesAxiom:
            {
                violations.Add(new OwlProfileViolation(OwlProfiles.El, axiom.Origin, "DisjointDataProperties is not in EL."));
                break;
            }
            default:
            {
                break;
            }
        }
    }

    private static void RequireElProperty(OwlObjectPropertyExpression property, OwlAxiom axiom, List<OwlProfileViolation> violations)
    {
        if(property.IsInverse)
        {
            violations.Add(new OwlProfileViolation(OwlProfiles.El, axiom.Origin, "ObjectInverseOf is not in EL."));
        }
    }

    private static void RequireElExpression(OwlClassExpression root, OwlAxiom axiom, List<OwlProfileViolation> violations)
    {
        Stack<OwlClassExpression> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            OwlClassExpression expression = work.Pop();

            switch(expression)
            {
                case OwlClassReference:
                {
                    break;
                }
                case OwlObjectIntersectionOf intersection:
                {
                    foreach(OwlClassExpression operand in intersection.Operands)
                    {
                        work.Push(operand);
                    }

                    break;
                }
                case OwlObjectSomeValuesFrom someValues:
                {
                    RequireElProperty(someValues.Property, axiom, violations);
                    work.Push(someValues.Filler);
                    break;
                }
                case OwlObjectHasValue hasValue:
                {
                    RequireElProperty(hasValue.Property, axiom, violations);
                    break;
                }
                case OwlObjectHasSelf hasSelf:
                {
                    RequireElProperty(hasSelf.Property, axiom, violations);
                    break;
                }
                case OwlObjectOneOf oneOf:
                {
                    if(oneOf.Individuals.Count != 1)
                    {
                        violations.Add(new OwlProfileViolation(OwlProfiles.El, axiom.Origin, "ObjectOneOf with more than one individual is not in EL."));
                    }

                    break;
                }
                case OwlDataSomeValuesFrom dataSome:
                {
                    RequireElDataRange(dataSome.Range, axiom, violations);
                    break;
                }
                case OwlDataHasValue:
                {
                    break;
                }
                default:
                {
                    violations.Add(new OwlProfileViolation(OwlProfiles.El, axiom.Origin, $"{expression.GetType().Name} is not an EL class expression."));
                    break;
                }
            }
        }
    }

    private static void RequireElDataRange(OwlDataRange root, OwlAxiom axiom, List<OwlProfileViolation> violations)
    {
        Stack<OwlDataRange> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            OwlDataRange range = work.Pop();

            switch(range)
            {
                case OwlDatatypeReference datatype:
                {
                    if(!ElQlDatatypes.Contains(datatype.Datatype.Iri))
                    {
                        violations.Add(new OwlProfileViolation(OwlProfiles.El, axiom.Origin, $"Datatype {datatype.Datatype} is not in the EL datatype list."));
                    }

                    break;
                }
                case OwlDataIntersectionOf intersection:
                {
                    foreach(OwlDataRange operand in intersection.Ranges)
                    {
                        work.Push(operand);
                    }

                    break;
                }
                case OwlDataOneOf oneOf:
                {
                    if(oneOf.Literals.Count != 1)
                    {
                        violations.Add(new OwlProfileViolation(OwlProfiles.El, axiom.Origin, "DataOneOf with more than one literal is not in EL."));
                    }

                    break;
                }
                default:
                {
                    violations.Add(new OwlProfileViolation(OwlProfiles.El, axiom.Origin, $"{range.GetType().Name} is not an EL data range."));
                    break;
                }
            }
        }
    }

    //QL (Profiles §3): a sub/super positional grammar without
    //existence-creating constructs on the left; the axiom set excludes
    //equality, keys, chains, transitivity, and functionality.

    private static void CheckQl(OwlAxiom axiom, List<OwlProfileViolation> violations)
    {
        switch(axiom)
        {
            case OwlSubClassOfAxiom subClass:
            {
                RequireQlExpression(subClass.SubClass, Position.Sub, axiom, violations);
                RequireQlExpression(subClass.SuperClass, Position.Super, axiom, violations);
                break;
            }
            case OwlEquivalentClassesAxiom equivalent:
            {
                RequireQlExpression(equivalent.First, Position.Sub, axiom, violations);
                RequireQlExpression(equivalent.Second, Position.Sub, axiom, violations);
                break;
            }
            case OwlDisjointClassesAxiom disjoint:
            {
                foreach(OwlClassExpression operand in disjoint.Operands)
                {
                    RequireQlExpression(operand, Position.Sub, axiom, violations);
                }

                break;
            }
            case OwlDisjointUnionAxiom:
            {
                violations.Add(new OwlProfileViolation(OwlProfiles.Ql, axiom.Origin, "DisjointUnion is not in QL."));
                break;
            }
            case OwlPropertyChainAxiom:
            {
                violations.Add(new OwlProfileViolation(OwlProfiles.Ql, axiom.Origin, "Property chains are not in QL."));
                break;
            }
            case OwlObjectPropertyCharacteristicAxiom characteristic:
            {
                //The corpus annotations fix QL's characteristic set at
                //symmetric, asymmetric, and reflexive (see
                //New-Feature-IrreflexiveProperty-001, RL-only).
                bool allowed = characteristic.Characteristic is OwlPropertyCharacteristic.Symmetric
                    or OwlPropertyCharacteristic.Asymmetric
                    or OwlPropertyCharacteristic.Reflexive;
                if(!allowed)
                {
                    violations.Add(new OwlProfileViolation(OwlProfiles.Ql, axiom.Origin, $"{characteristic.Characteristic}ObjectProperty is not in QL."));
                }

                break;
            }
            case OwlFunctionalDataPropertyAxiom:
            {
                violations.Add(new OwlProfileViolation(OwlProfiles.Ql, axiom.Origin, "FunctionalDataProperty is not in QL."));
                break;
            }
            case OwlObjectPropertyDomainAxiom domain:
            {
                RequireQlExpression(domain.Domain, Position.Super, axiom, violations);
                break;
            }
            case OwlObjectPropertyRangeAxiom range:
            {
                RequireQlExpression(range.Range, Position.Super, axiom, violations);
                break;
            }
            case OwlDataPropertyDomainAxiom dataDomain:
            {
                RequireQlExpression(dataDomain.Domain, Position.Super, axiom, violations);
                break;
            }
            case OwlDataPropertyRangeAxiom dataRange:
            {
                RequireQlDataRange(dataRange.Range, axiom, violations);
                break;
            }
            case OwlHasKeyAxiom:
            {
                violations.Add(new OwlProfileViolation(OwlProfiles.Ql, axiom.Origin, "HasKey is not in QL."));
                break;
            }
            case OwlSameIndividualAxiom:
            {
                violations.Add(new OwlProfileViolation(OwlProfiles.Ql, axiom.Origin, "SameIndividual is not in QL."));
                break;
            }
            case OwlNegativeObjectPropertyAssertionAxiom or OwlNegativeDataPropertyAssertionAxiom:
            {
                violations.Add(new OwlProfileViolation(OwlProfiles.Ql, axiom.Origin, "Negative property assertions are not in QL."));
                break;
            }
            case OwlClassAssertionAxiom assertion:
            {
                if(assertion.Class is not OwlClassReference)
                {
                    violations.Add(new OwlProfileViolation(OwlProfiles.Ql, axiom.Origin, "ClassAssertion in QL requires a named class."));
                }

                break;
            }
            case OwlDatatypeDefinitionAxiom:
            {
                violations.Add(new OwlProfileViolation(OwlProfiles.Ql, axiom.Origin, "DatatypeDefinition is not in QL."));
                break;
            }
            default:
            {
                break;
            }
        }
    }

    private static void RequireQlExpression(OwlClassExpression root, Position position, Position positionForComplement, OwlAxiom axiom, List<OwlProfileViolation> violations)
    {
        //The complement position parameter exists so a complemented operand
        //checks under the SUB grammar regardless of where the complement sat.
        _ = positionForComplement;
        RequireQlExpression(root, position, axiom, violations);
    }

    private static void RequireQlExpression(OwlClassExpression root, Position position, OwlAxiom axiom, List<OwlProfileViolation> violations)
    {
        Stack<(OwlClassExpression Expression, Position Position)> work = new();
        work.Push((root, position));

        while(work.Count > 0)
        {
            (OwlClassExpression expression, Position at) = work.Pop();

            switch(expression)
            {
                case OwlClassReference:
                {
                    break;
                }
                case OwlObjectSomeValuesFrom someValues:
                {
                    if(at == Position.Sub)
                    {
                        //subClassExpression admits only existentials over
                        //owl:Thing.
                        if(someValues.Filler is not OwlClassReference filler || filler.Class.Iri != OwlVocabulary.Thing)
                        {
                            violations.Add(new OwlProfileViolation(OwlProfiles.Ql, axiom.Origin, "A subclass-side ObjectSomeValuesFrom in QL must have owl:Thing as its filler."));
                        }
                    }
                    else if(someValues.Filler is not OwlClassReference)
                    {
                        violations.Add(new OwlProfileViolation(OwlProfiles.Ql, axiom.Origin, "A superclass-side ObjectSomeValuesFrom in QL requires a named class filler."));
                    }

                    break;
                }
                case OwlDataSomeValuesFrom dataSome:
                {
                    RequireQlDataRange(dataSome.Range, axiom, violations);
                    break;
                }
                case OwlObjectIntersectionOf intersection when at == Position.Super:
                {
                    foreach(OwlClassExpression operand in intersection.Operands)
                    {
                        work.Push((operand, Position.Super));
                    }

                    break;
                }
                case OwlObjectComplementOf complement when at == Position.Super:
                {
                    work.Push((complement.Operand, Position.Sub));
                    break;
                }
                default:
                {
                    violations.Add(new OwlProfileViolation(OwlProfiles.Ql, axiom.Origin, $"{expression.GetType().Name} is not a QL class expression in {(at == Position.Sub ? "subclass" : "superclass")} position."));
                    break;
                }
            }
        }
    }

    private static void RequireQlDataRange(OwlDataRange root, OwlAxiom axiom, List<OwlProfileViolation> violations)
    {
        Stack<OwlDataRange> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            OwlDataRange range = work.Pop();

            switch(range)
            {
                case OwlDatatypeReference datatype:
                {
                    if(!ElQlDatatypes.Contains(datatype.Datatype.Iri))
                    {
                        violations.Add(new OwlProfileViolation(OwlProfiles.Ql, axiom.Origin, $"Datatype {datatype.Datatype} is not in the QL datatype list."));
                    }

                    break;
                }
                case OwlDataIntersectionOf intersection:
                {
                    foreach(OwlDataRange operand in intersection.Ranges)
                    {
                        work.Push(operand);
                    }

                    break;
                }
                default:
                {
                    violations.Add(new OwlProfileViolation(OwlProfiles.Ql, axiom.Origin, $"{range.GetType().Name} is not a QL data range."));
                    break;
                }
            }
        }
    }

    //RL (Profiles §4): three positional grammars (sub, super,
    //equivalence); owl:Thing is excluded as a class expression; the axiom
    //set excludes reflexivity, disjoint unions, and datatype definitions.

    private static void CheckRl(OwlAxiom axiom, List<OwlProfileViolation> violations)
    {
        switch(axiom)
        {
            case OwlSubClassOfAxiom subClass:
            {
                RequireRlExpression(subClass.SubClass, Position.Sub, axiom, violations);
                RequireRlExpression(subClass.SuperClass, Position.Super, axiom, violations);
                break;
            }
            case OwlEquivalentClassesAxiom equivalent:
            {
                RequireRlExpression(equivalent.First, Position.Equivalence, axiom, violations);
                RequireRlExpression(equivalent.Second, Position.Equivalence, axiom, violations);
                break;
            }
            case OwlDisjointClassesAxiom disjoint:
            {
                foreach(OwlClassExpression operand in disjoint.Operands)
                {
                    RequireRlExpression(operand, Position.Sub, axiom, violations);
                }

                break;
            }
            case OwlDisjointUnionAxiom:
            {
                violations.Add(new OwlProfileViolation(OwlProfiles.Rl, axiom.Origin, "DisjointUnion is not in RL."));
                break;
            }
            case OwlObjectPropertyCharacteristicAxiom characteristic:
            {
                //All characteristics are RL-admissible per the corpus
                //annotations (New-Feature-ReflexiveProperty-001 is RL-marked).
                RequireRlProperty(characteristic.Property, axiom, violations);
                break;
            }
            case OwlSubObjectPropertyOfAxiom subProperty:
            {
                RequireRlProperty(subProperty.SubProperty, axiom, violations);
                RequireRlProperty(subProperty.SuperProperty, axiom, violations);
                break;
            }
            case OwlPropertyChainAxiom chain:
            {
                foreach(OwlObjectPropertyExpression link in chain.Chain)
                {
                    RequireRlProperty(link, axiom, violations);
                }

                RequireRlProperty(chain.SuperProperty, axiom, violations);
                break;
            }
            case OwlInverseObjectPropertiesAxiom inverse:
            {
                RequireRlProperty(inverse.First, axiom, violations);
                RequireRlProperty(inverse.Second, axiom, violations);
                break;
            }
            case OwlEquivalentObjectPropertiesAxiom equivalentProperties:
            {
                RequireRlProperty(equivalentProperties.First, axiom, violations);
                RequireRlProperty(equivalentProperties.Second, axiom, violations);
                break;
            }
            case OwlObjectPropertyDomainAxiom domain:
            {
                RequireRlProperty(domain.Property, axiom, violations);
                RequireRlExpression(domain.Domain, Position.Super, axiom, violations);
                break;
            }
            case OwlObjectPropertyRangeAxiom range:
            {
                RequireRlProperty(range.Property, axiom, violations);
                RequireRlExpression(range.Range, Position.Super, axiom, violations);
                break;
            }
            case OwlDataPropertyDomainAxiom dataDomain:
            {
                RequireRlExpression(dataDomain.Domain, Position.Super, axiom, violations);
                break;
            }
            case OwlDataPropertyRangeAxiom dataRange:
            {
                RequireRlDataRange(dataRange.Range, axiom, violations);
                break;
            }
            case OwlDatatypeDefinitionAxiom:
            {
                violations.Add(new OwlProfileViolation(OwlProfiles.Rl, axiom.Origin, "DatatypeDefinition is not in RL."));
                break;
            }
            case OwlHasKeyAxiom hasKey:
            {
                RequireRlExpression(hasKey.Class, Position.Sub, axiom, violations);
                break;
            }
            case OwlClassAssertionAxiom assertion:
            {
                RequireRlExpression(assertion.Class, Position.Super, axiom, violations);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    //The top properties cannot be axiomatized by RL rules; the corpus marks
    //their uses out of RL (New-Feature-TopObjectProperty-001).
    private static void RequireRlProperty(OwlObjectPropertyExpression property, OwlAxiom axiom, List<OwlProfileViolation> violations)
    {
        if(property.Property.Iri == OwlVocabulary.TopObjectProperty || property.Property.Iri == OwlVocabulary.TopDataProperty)
        {
            violations.Add(new OwlProfileViolation(OwlProfiles.Rl, axiom.Origin, "The top properties are not in RL."));
        }
    }

    private static void RequireRlExpression(OwlClassExpression root, Position position, OwlAxiom axiom, List<OwlProfileViolation> violations)
    {
        Stack<(OwlClassExpression Expression, Position Position)> work = new();
        work.Push((root, position));

        while(work.Count > 0)
        {
            (OwlClassExpression expression, Position at) = work.Pop();

            switch(expression)
            {
                case OwlClassReference reference:
                {
                    if(reference.Class.Iri == OwlVocabulary.Thing)
                    {
                        violations.Add(new OwlProfileViolation(OwlProfiles.Rl, axiom.Origin, "owl:Thing is not an RL class expression."));
                    }

                    break;
                }
                case OwlObjectIntersectionOf intersection:
                {
                    foreach(OwlClassExpression operand in intersection.Operands)
                    {
                        work.Push((operand, at));
                    }

                    break;
                }
                case OwlObjectUnionOf union when at == Position.Sub:
                {
                    foreach(OwlClassExpression operand in union.Operands)
                    {
                        work.Push((operand, Position.Sub));
                    }

                    break;
                }
                case OwlObjectOneOf when at == Position.Sub:
                {
                    break;
                }
                case OwlObjectSomeValuesFrom someValues when at == Position.Sub:
                {
                    //The subclass grammar admits existentials whose filler is
                    //a subclass expression or owl:Thing.
                    RequireRlProperty(someValues.Property, axiom, violations);
                    if(someValues.Filler is not OwlClassReference { Class.Iri: var fillerIri } || fillerIri != OwlVocabulary.Thing)
                    {
                        work.Push((someValues.Filler, Position.Sub));
                    }

                    break;
                }
                case OwlObjectAllValuesFrom allValues when at == Position.Super:
                {
                    RequireRlProperty(allValues.Property, axiom, violations);
                    work.Push((allValues.Filler, Position.Super));
                    break;
                }
                case OwlObjectHasValue hasValue:
                {
                    RequireRlProperty(hasValue.Property, axiom, violations);
                    break;
                }
                case OwlObjectCardinality { Kind: OwlCardinalityKind.Max } cardinality when at == Position.Super:
                {
                    RequireRlProperty(cardinality.Property, axiom, violations);
                    if(cardinality.Cardinality > 1)
                    {
                        violations.Add(new OwlProfileViolation(OwlProfiles.Rl, axiom.Origin, "A superclass-side max cardinality in RL must be 0 or 1."));
                    }

                    if(cardinality.Filler is OwlClassExpression qualifier
                        && !(qualifier is OwlClassReference { Class.Iri: var qualifierIri } && qualifierIri == OwlVocabulary.Thing))
                    {
                        work.Push((qualifier, Position.Sub));
                    }

                    break;
                }
                case OwlObjectComplementOf complement when at == Position.Super:
                {
                    work.Push((complement.Operand, Position.Sub));
                    break;
                }
                case OwlDataSomeValuesFrom dataSome when at == Position.Sub:
                {
                    RequireRlDataRange(dataSome.Range, axiom, violations);
                    break;
                }
                case OwlDataAllValuesFrom dataAll when at == Position.Super:
                {
                    RequireRlDataRange(dataAll.Range, axiom, violations);
                    break;
                }
                case OwlDataHasValue:
                {
                    break;
                }
                case OwlDataCardinality { Kind: OwlCardinalityKind.Max } dataCardinality when at == Position.Super:
                {
                    if(dataCardinality.Cardinality > 1)
                    {
                        violations.Add(new OwlProfileViolation(OwlProfiles.Rl, axiom.Origin, "A superclass-side data max cardinality in RL must be 0 or 1."));
                    }

                    break;
                }
                default:
                {
                    string positionName = at switch
                    {
                        Position.Sub => "subclass",
                        Position.Super => "superclass",
                        _ => "equivalence"
                    };
                    violations.Add(new OwlProfileViolation(OwlProfiles.Rl, axiom.Origin, $"{expression.GetType().Name} is not an RL class expression in {positionName} position."));
                    break;
                }
            }
        }
    }

    private static void RequireRlDataRange(OwlDataRange root, OwlAxiom axiom, List<OwlProfileViolation> violations)
    {
        Stack<OwlDataRange> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            OwlDataRange range = work.Pop();

            switch(range)
            {
                case OwlDatatypeReference datatype:
                {
                    if(!RlDatatypes.Contains(datatype.Datatype.Iri))
                    {
                        violations.Add(new OwlProfileViolation(OwlProfiles.Rl, axiom.Origin, $"Datatype {datatype.Datatype} is not in the RL datatype list."));
                    }

                    break;
                }
                case OwlDataIntersectionOf intersection:
                {
                    foreach(OwlDataRange operand in intersection.Ranges)
                    {
                        work.Push(operand);
                    }

                    break;
                }
                default:
                {
                    violations.Add(new OwlProfileViolation(OwlProfiles.Rl, axiom.Origin, $"{range.GetType().Name} is not an RL data range."));
                    break;
                }
            }
        }
    }

    //Cross-profile individual restrictions: anonymous individuals are
    //admissible everywhere as rolled-up existentials (objects of property
    //assertions), but a FREE-FLOATING anonymous node typed with a built-in
    //class (owl:Thing, owl:NamedIndividual) — never the target of an object
    //property assertion — is the OWL-1-era idiom the corpus marks out of EL
    //and QL (owl2-rl-anonymous-individual, the WebOnt-I5.3 family, and
    //WebOnt-AnnotationProperty-002, against WebOnt-someValuesFrom-003 whose
    //anonymous chain hangs off a named root and stays in EL).

    private static void CheckIndividuals(OwlAxiom axiom, HashSet<RdfTerm> anonymousTargets, List<OwlProfileViolation> violations)
    {
        if(axiom is OwlClassAssertionAxiom { Individual: BlankNode anonymous, Class: OwlClassReference reference }
            && (reference.Class.Iri == OwlVocabulary.NamedIndividual || reference.Class.Iri == OwlVocabulary.Thing)
            && !anonymousTargets.Contains(anonymous))
        {
            violations.Add(new OwlProfileViolation(OwlProfiles.El, axiom.Origin, $"A free-floating anonymous individual typed {reference.Class} is not in EL."));
            violations.Add(new OwlProfileViolation(OwlProfiles.Ql, axiom.Origin, $"A free-floating anonymous individual typed {reference.Class} is not in QL."));
        }
    }

    private static string DescribeFirstError(OwlOntologyDocument document)
    {
        foreach(Diagnostic diagnostic in document.Diagnostics.Diagnostics)
        {
            if(diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return diagnostic.Message.ToString();
            }
        }

        return "(unspecified mapping error)";
    }

}
