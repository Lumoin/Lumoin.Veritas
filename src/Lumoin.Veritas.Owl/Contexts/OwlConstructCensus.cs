using System;
using System.Collections.Generic;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// A polarity-qualified, semantic census of the OWL constructs a
/// <see cref="ReasoningModule"/> uses, walked directly over the raw structural
/// axioms — nested class expressions, data ranges, and property expressions
/// included — with no clausification. The census is a planning instrument: its
/// keys name constructs at the granularity that predicts whether a module stays
/// Horn (and so which reasoning path decides it), so class-expression keys carry
/// the subclass/superclass polarity a structural transformation would assign.
/// </summary>
internal static class OwlConstructCensus
{
    /// <summary>
    /// Counts the semantic constructs the module uses, at the polarity each
    /// class-expression occurrence sits in, and returns them ordered by
    /// descending count then ascending ordinal key for a stable, deterministic
    /// result.
    /// </summary>
    /// <param name="module">The reasoning module whose axioms are surveyed; its violations are ignored.</param>
    /// <returns>The construct census as (key, count) pairs, most frequent first, ties broken by ordinal key.</returns>
    public static IReadOnlyList<(string Key, int Count)> Count(ReasoningModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach(OwlAxiom axiom in module.Axioms)
        {
            CountAxiom(axiom, counts);
        }

        List<(string Key, int Count)> result = new(counts.Count);
        foreach(KeyValuePair<string, int> pair in counts)
        {
            result.Add((pair.Key, pair.Value));
        }

        result.Sort(static (left, right) =>
            right.Count != left.Count
                ? right.Count.CompareTo(left.Count)
                : string.CompareOrdinal(left.Key, right.Key));

        return result;
    }

    /// <summary>
    /// Records the axiom's own construct key, then walks its nested class
    /// expressions, data ranges, and property expressions. Declaration,
    /// annotation, and import axioms are semantic no-ops and contribute no key.
    /// </summary>
    /// <param name="axiom">The axiom to census.</param>
    /// <param name="counts">The running key-to-count accumulator this method appends into.</param>
    private static void CountAxiom(OwlAxiom axiom, Dictionary<string, int> counts)
    {
        switch(axiom)
        {
            case(OwlSubClassOfAxiom subClass):
            {
                Bump(counts, Keys.SubClassOf);
                WalkClass(subClass.SubClass, CensusPolarity.Sub, counts);
                WalkClass(subClass.SuperClass, CensusPolarity.Super, counts);

                break;
            }
            case(OwlEquivalentClassesAxiom equivalent):
            {
                Bump(counts, Keys.EquivalentClasses);
                WalkClass(equivalent.First, CensusPolarity.Sub, counts);
                WalkClass(equivalent.First, CensusPolarity.Super, counts);
                WalkClass(equivalent.Second, CensusPolarity.Sub, counts);
                WalkClass(equivalent.Second, CensusPolarity.Super, counts);

                break;
            }
            case(OwlDisjointClassesAxiom disjoint):
            {
                Bump(counts, Keys.DisjointClasses);
                foreach(OwlClassExpression operand in disjoint.Operands)
                {
                    WalkClass(operand, CensusPolarity.Sub, counts);
                }

                break;
            }
            case(OwlDisjointUnionAxiom disjointUnion):
            {
                Bump(counts, Keys.DisjointUnion);
                foreach(OwlClassExpression operand in disjointUnion.Operands)
                {
                    WalkClass(operand, CensusPolarity.Sub, counts);
                }

                break;
            }
            case(OwlSubObjectPropertyOfAxiom subRole):
            {
                Bump(counts, Keys.SubObjectPropertyOf);
                CountObjectProperty(subRole.SubProperty, counts);
                CountObjectProperty(subRole.SuperProperty, counts);

                break;
            }
            case(OwlPropertyChainAxiom chain):
            {
                Bump(counts, Keys.SubObjectPropertyOfChain);
                foreach(OwlObjectPropertyExpression link in chain.Chain)
                {
                    CountObjectProperty(link, counts);
                }

                CountObjectProperty(chain.SuperProperty, counts);

                break;
            }
            case(OwlEquivalentObjectPropertiesAxiom equivalentRoles):
            {
                Bump(counts, Keys.EquivalentObjectProperties);
                CountObjectProperty(equivalentRoles.First, counts);
                CountObjectProperty(equivalentRoles.Second, counts);

                break;
            }
            case(OwlDisjointObjectPropertiesAxiom disjointRoles):
            {
                Bump(counts, Keys.DisjointObjectProperties);
                foreach(OwlObjectPropertyExpression operand in disjointRoles.Operands)
                {
                    CountObjectProperty(operand, counts);
                }

                break;
            }
            case(OwlInverseObjectPropertiesAxiom inverse):
            {
                Bump(counts, Keys.InverseObjectProperties);
                CountObjectProperty(inverse.First, counts);
                CountObjectProperty(inverse.Second, counts);

                break;
            }
            case(OwlObjectPropertyDomainAxiom domain):
            {
                Bump(counts, Keys.ObjectPropertyDomain);
                CountObjectProperty(domain.Property, counts);
                WalkClass(domain.Domain, CensusPolarity.Super, counts);

                break;
            }
            case(OwlObjectPropertyRangeAxiom range):
            {
                Bump(counts, Keys.ObjectPropertyRange);
                CountObjectProperty(range.Property, counts);
                WalkClass(range.Range, CensusPolarity.Super, counts);

                break;
            }
            case(OwlObjectPropertyCharacteristicAxiom characteristic):
            {
                Bump(counts, CharacteristicKey(characteristic.Characteristic));
                CountObjectProperty(characteristic.Property, counts);

                break;
            }
            case(OwlSubDataPropertyOfAxiom):
            {
                Bump(counts, Keys.SubDataPropertyOf);

                break;
            }
            case(OwlEquivalentDataPropertiesAxiom):
            {
                Bump(counts, Keys.EquivalentDataProperties);

                break;
            }
            case(OwlDisjointDataPropertiesAxiom):
            {
                Bump(counts, Keys.DisjointDataProperties);

                break;
            }
            case(OwlDataPropertyDomainAxiom dataDomain):
            {
                Bump(counts, Keys.DataPropertyDomain);
                WalkClass(dataDomain.Domain, CensusPolarity.Super, counts);

                break;
            }
            case(OwlDataPropertyRangeAxiom dataRange):
            {
                Bump(counts, Keys.DataPropertyRange);
                WalkDataRange(dataRange.Range, counts);

                break;
            }
            case(OwlFunctionalDataPropertyAxiom):
            {
                Bump(counts, Keys.FunctionalDataProperty);

                break;
            }
            case(OwlDatatypeDefinitionAxiom datatypeDefinition):
            {
                Bump(counts, Keys.DatatypeDefinition);
                WalkDataRange(datatypeDefinition.Range, counts);

                break;
            }
            case(OwlHasKeyAxiom hasKey):
            {
                Bump(counts, Keys.HasKey);
                WalkClass(hasKey.Class, CensusPolarity.Sub, counts);

                break;
            }
            case(OwlClassAssertionAxiom classAssertion):
            {
                Bump(counts, Keys.ClassAssertion);
                WalkClass(classAssertion.Class, CensusPolarity.Super, counts);

                break;
            }
            case(OwlObjectPropertyAssertionAxiom):
            {
                Bump(counts, Keys.ObjectPropertyAssertion);

                break;
            }
            case(OwlNegativeObjectPropertyAssertionAxiom negativeObject):
            {
                Bump(counts, Keys.NegativeObjectPropertyAssertion);
                CountObjectProperty(negativeObject.Property, counts);

                break;
            }
            case(OwlDataPropertyAssertionAxiom):
            {
                Bump(counts, Keys.DataPropertyAssertion);

                break;
            }
            case(OwlNegativeDataPropertyAssertionAxiom):
            {
                Bump(counts, Keys.NegativeDataPropertyAssertion);

                break;
            }
            case(OwlSameIndividualAxiom):
            {
                Bump(counts, Keys.SameIndividual);

                break;
            }
            case(OwlDifferentIndividualsAxiom differentIndividuals):
            {
                Bump(counts, DifferentIndividualsKey(differentIndividuals.Individuals.Count));

                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>
    /// Walks a class expression and its nested subexpressions with an explicit
    /// worklist, emitting a polarity-qualified key at each construct node. The
    /// stack carries each pending subexpression paired with the polarity it
    /// occupies: complement flips it, every other connective preserves it, and
    /// the filler of a max-cardinality restriction flips it (a max is a negated
    /// min). A bare named class contributes no key.
    /// </summary>
    /// <param name="root">The class expression to walk.</param>
    /// <param name="polarity">The polarity of <paramref name="root"/> in its axiom.</param>
    /// <param name="counts">The running key-to-count accumulator this method appends into.</param>
    private static void WalkClass(OwlClassExpression root, CensusPolarity polarity, Dictionary<string, int> counts)
    {
        Stack<(OwlClassExpression Expression, CensusPolarity Polarity)> work = new();
        work.Push((root, polarity));

        while(work.Count > 0)
        {
            (OwlClassExpression expression, CensusPolarity current) = work.Pop();
            switch(expression)
            {
                case(OwlClassReference):
                {
                    break;
                }
                case(OwlObjectIntersectionOf intersection):
                {
                    foreach(OwlClassExpression operand in intersection.Operands)
                    {
                        work.Push((operand, current));
                    }

                    break;
                }
                case(OwlObjectUnionOf union):
                {
                    Bump(counts, UnionKey(current));
                    foreach(OwlClassExpression operand in union.Operands)
                    {
                        work.Push((operand, current));
                    }

                    break;
                }
                case(OwlObjectComplementOf complement):
                {
                    Bump(counts, ComplementKey(current));
                    work.Push((complement.Operand, Flip(current)));

                    break;
                }
                case(OwlObjectOneOf oneOf):
                {
                    Bump(counts, OneOfKey(oneOf.Individuals.Count));

                    break;
                }
                case(OwlObjectSomeValuesFrom some):
                {
                    CountObjectProperty(some.Property, counts);
                    Bump(counts, Keys.ObjectSomeValuesFrom);
                    work.Push((some.Filler, current));

                    break;
                }
                case(OwlObjectAllValuesFrom all):
                {
                    CountObjectProperty(all.Property, counts);
                    Bump(counts, AllValuesKey(current));
                    work.Push((all.Filler, current));

                    break;
                }
                case(OwlObjectHasValue hasValue):
                {
                    CountObjectProperty(hasValue.Property, counts);
                    Bump(counts, Keys.ObjectHasValue);

                    break;
                }
                case(OwlObjectHasSelf hasSelf):
                {
                    CountObjectProperty(hasSelf.Property, counts);
                    Bump(counts, Keys.ObjectHasSelf);

                    break;
                }
                case(OwlObjectCardinality cardinality):
                {
                    CountObjectProperty(cardinality.Property, counts);
                    EmitObjectCardinality(cardinality, counts);
                    if(cardinality.Filler is not null)
                    {
                        work.Push((cardinality.Filler, CardinalityFillerPolarity(cardinality.Kind, current)));
                    }

                    break;
                }
                case(OwlDataSomeValuesFrom dataSome):
                {
                    Bump(counts, DataSomeKey(current));
                    WalkDataRange(dataSome.Range, counts);

                    break;
                }
                case(OwlDataAllValuesFrom dataAll):
                {
                    Bump(counts, DataAllKey(current));
                    WalkDataRange(dataAll.Range, counts);

                    break;
                }
                case(OwlDataHasValue):
                {
                    Bump(counts, DataHasValueKey(current));

                    break;
                }
                case(OwlDataCardinality dataCardinality):
                {
                    EmitDataCardinality(dataCardinality, current, counts);
                    if(dataCardinality.Range is not null)
                    {
                        WalkDataRange(dataCardinality.Range, counts);
                    }

                    break;
                }
                default:
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Walks a data range and its nested subranges with an explicit worklist,
    /// emitting a key at each data-range construct and one facet key per facet
    /// of a datatype restriction. A bare named datatype contributes no key.
    /// </summary>
    /// <param name="root">The data range to walk.</param>
    /// <param name="counts">The running key-to-count accumulator this method appends into.</param>
    private static void WalkDataRange(OwlDataRange root, Dictionary<string, int> counts)
    {
        Stack<OwlDataRange> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            switch(work.Pop())
            {
                case(OwlDatatypeReference):
                {
                    break;
                }
                case(OwlDataIntersectionOf intersection):
                {
                    Bump(counts, Keys.DataIntersectionOf);
                    foreach(OwlDataRange range in intersection.Ranges)
                    {
                        work.Push(range);
                    }

                    break;
                }
                case(OwlDataUnionOf union):
                {
                    Bump(counts, Keys.DataUnionOf);
                    foreach(OwlDataRange range in union.Ranges)
                    {
                        work.Push(range);
                    }

                    break;
                }
                case(OwlDataComplementOf complement):
                {
                    Bump(counts, Keys.DataComplementOf);
                    work.Push(complement.Range);

                    break;
                }
                case(OwlDataOneOf oneOf):
                {
                    Bump(counts, DataOneOfKey(oneOf.Literals.Count));

                    break;
                }
                case(OwlDatatypeRestriction restriction):
                {
                    Bump(counts, Keys.DatatypeRestriction);
                    for(int index = 0; index < restriction.Restrictions.Count; index++)
                    {
                        Bump(counts, Keys.DataFacet);
                    }

                    break;
                }
                default:
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Emits the cardinality key(s) for an object cardinality restriction: a
    /// min or max reading emits its one half, while an exact reading emits both
    /// the min and the max halves (an exact <c>n</c> is a min <c>n</c> conjoined
    /// with a max <c>n</c>). The <c>1</c> versus <c>n&gt;=2</c> bucket is chosen
    /// from the bound.
    /// </summary>
    /// <param name="cardinality">The object cardinality restriction.</param>
    /// <param name="counts">The running key-to-count accumulator this method appends into.</param>
    private static void EmitObjectCardinality(OwlObjectCardinality cardinality, Dictionary<string, int> counts)
    {
        switch(cardinality.Kind)
        {
            case(OwlCardinalityKind.Min):
            {
                Bump(counts, ObjectMinKey(cardinality.Cardinality));

                break;
            }
            case(OwlCardinalityKind.Max):
            {
                Bump(counts, ObjectMaxKey(cardinality.Cardinality));

                break;
            }
            case(OwlCardinalityKind.Exact):
            {
                Bump(counts, ObjectMinKey(cardinality.Cardinality));
                Bump(counts, ObjectMaxKey(cardinality.Cardinality));

                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>
    /// Emits the cardinality key(s) for a data cardinality restriction, mirroring
    /// <see cref="EmitObjectCardinality"/> with polarity qualification (the data
    /// tier's admission is polarity-split, so the census attributes it): a min
    /// or max reading emits its one half, an exact reading emits both halves, and
    /// the <c>1</c> versus <c>n&gt;=2</c> bucket is chosen from the bound.
    /// </summary>
    /// <param name="cardinality">The data cardinality restriction.</param>
    /// <param name="polarity">The polarity the restriction is censused at.</param>
    /// <param name="counts">The running key-to-count accumulator this method appends into.</param>
    private static void EmitDataCardinality(OwlDataCardinality cardinality, CensusPolarity polarity, Dictionary<string, int> counts)
    {
        switch(cardinality.Kind)
        {
            case(OwlCardinalityKind.Min):
            {
                Bump(counts, DataMinKey(cardinality.Cardinality, polarity));

                break;
            }
            case(OwlCardinalityKind.Max):
            {
                Bump(counts, DataMaxKey(cardinality.Cardinality, polarity));

                break;
            }
            case(OwlCardinalityKind.Exact):
            {
                Bump(counts, DataMinKey(cardinality.Cardinality, polarity));
                Bump(counts, DataMaxKey(cardinality.Cardinality, polarity));

                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Emits one inverse-object-property key when the property expression is an inverse (<c>ObjectInverseOf</c>).</summary>
    /// <param name="property">The object-property expression at any axiom or restriction position.</param>
    /// <param name="counts">The running key-to-count accumulator this method appends into.</param>
    private static void CountObjectProperty(OwlObjectPropertyExpression property, Dictionary<string, int> counts)
    {
        if(property.IsInverse)
        {
            Bump(counts, Keys.InverseObjectProperty);
        }
    }

    /// <summary>Increments the count for a census key, seeding it at one on first sight.</summary>
    /// <param name="counts">The running key-to-count accumulator to mutate.</param>
    /// <param name="key">The census key whose count to raise.</param>
    private static void Bump(Dictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out int existing);
        counts[key] = existing + 1;
    }

    /// <summary>The opposite polarity — complement is the only construct that flips it.</summary>
    /// <param name="polarity">The polarity to invert.</param>
    /// <returns><see cref="CensusPolarity.Super"/> for <see cref="CensusPolarity.Sub"/> and the reverse.</returns>
    private static CensusPolarity Flip(CensusPolarity polarity)
    {
        return polarity switch
        {
            CensusPolarity.Super => CensusPolarity.Sub,
            _ => CensusPolarity.Super,
        };
    }

    /// <summary>The polarity a cardinality restriction's filler occupies: a max filler flips it (a max is a negated min), a min or exact filler preserves it.</summary>
    /// <param name="kind">The cardinality flavour.</param>
    /// <param name="polarity">The polarity of the restriction itself.</param>
    /// <returns>The filler polarity.</returns>
    private static CensusPolarity CardinalityFillerPolarity(OwlCardinalityKind kind, CensusPolarity polarity)
    {
        return kind switch
        {
            OwlCardinalityKind.Max => Flip(polarity),
            _ => polarity,
        };
    }

    /// <summary>The polarity-qualified union key.</summary>
    /// <param name="polarity">The polarity of the union occurrence.</param>
    /// <returns>The super- or sub-polarity union key.</returns>
    private static string UnionKey(CensusPolarity polarity)
    {
        return polarity switch
        {
            CensusPolarity.Super => Keys.ObjectUnionOfSuper,
            _ => Keys.ObjectUnionOfSub,
        };
    }

    /// <summary>The polarity-qualified complement key.</summary>
    /// <param name="polarity">The polarity of the complement occurrence.</param>
    /// <returns>The super- or sub-polarity complement key.</returns>
    private static string ComplementKey(CensusPolarity polarity)
    {
        return polarity switch
        {
            CensusPolarity.Super => Keys.ObjectComplementOfSuper,
            _ => Keys.ObjectComplementOfSub,
        };
    }

    /// <summary>The polarity-qualified universal-restriction key.</summary>
    /// <param name="polarity">The polarity of the universal-restriction occurrence.</param>
    /// <returns>The super- or sub-polarity all-values key.</returns>
    private static string AllValuesKey(CensusPolarity polarity)
    {
        return polarity switch
        {
            CensusPolarity.Super => Keys.ObjectAllValuesFromSuper,
            _ => Keys.ObjectAllValuesFromSub,
        };
    }

    /// <summary>The object-enumeration key, split on singleton versus multi-individual enumeration.</summary>
    /// <param name="count">The number of enumerated individuals.</param>
    /// <returns>The singleton or multi-individual one-of key.</returns>
    private static string OneOfKey(int count)
    {
        return count == 1 ? Keys.ObjectOneOfOne : Keys.ObjectOneOfMany;
    }

    /// <summary>The data-enumeration key, split on singleton versus multi-literal enumeration.</summary>
    /// <param name="count">The number of enumerated literals.</param>
    /// <returns>The singleton or multi-literal one-of key.</returns>
    private static string DataOneOfKey(int count)
    {
        return count == 1 ? Keys.DataOneOfOne : Keys.DataOneOfMany;
    }

    /// <summary>The object min-cardinality key, split on the <c>1</c> versus <c>n&gt;=2</c> bound bucket.</summary>
    /// <param name="cardinality">The cardinality bound.</param>
    /// <returns>The min-1 or min-many key.</returns>
    private static string ObjectMinKey(int cardinality)
    {
        return cardinality == 1 ? Keys.ObjectCardinalityMinOne : Keys.ObjectCardinalityMinMany;
    }

    /// <summary>The object max-cardinality key, split on the <c>1</c> versus <c>n&gt;=2</c> bound bucket.</summary>
    /// <param name="cardinality">The cardinality bound.</param>
    /// <returns>The max-1 or max-many key.</returns>
    private static string ObjectMaxKey(int cardinality)
    {
        return cardinality == 1 ? Keys.ObjectCardinalityMaxOne : Keys.ObjectCardinalityMaxMany;
    }

    /// <summary>The polarity-split <c>DataSomeValuesFrom</c> key.</summary>
    /// <param name="polarity">The censused polarity.</param>
    /// <returns>The superclass- or subclass-polarity key.</returns>
    private static string DataSomeKey(CensusPolarity polarity)
    {
        return polarity switch
        {
            CensusPolarity.Super => Keys.DataSomeValuesFromSuper,
            _ => Keys.DataSomeValuesFromSub,
        };
    }

    /// <summary>The polarity-split <c>DataAllValuesFrom</c> key.</summary>
    /// <param name="polarity">The censused polarity.</param>
    /// <returns>The superclass- or subclass-polarity key.</returns>
    private static string DataAllKey(CensusPolarity polarity)
    {
        return polarity switch
        {
            CensusPolarity.Super => Keys.DataAllValuesFromSuper,
            _ => Keys.DataAllValuesFromSub,
        };
    }

    /// <summary>The polarity-split <c>DataHasValue</c> key.</summary>
    /// <param name="polarity">The censused polarity.</param>
    /// <returns>The superclass- or subclass-polarity key.</returns>
    private static string DataHasValueKey(CensusPolarity polarity)
    {
        return polarity switch
        {
            CensusPolarity.Super => Keys.DataHasValueSuper,
            _ => Keys.DataHasValueSub,
        };
    }

    /// <summary>The data min-cardinality key, split on the <c>1</c> versus <c>n&gt;=2</c> bound bucket and the censused polarity.</summary>
    /// <param name="cardinality">The cardinality bound.</param>
    /// <param name="polarity">The censused polarity.</param>
    /// <returns>The min-1 or min-many key at the polarity.</returns>
    private static string DataMinKey(int cardinality, CensusPolarity polarity)
    {
        return polarity switch
        {
            CensusPolarity.Super => cardinality == 1 ? Keys.DataCardinalityMinOneSuper : Keys.DataCardinalityMinManySuper,
            _ => cardinality == 1 ? Keys.DataCardinalityMinOneSub : Keys.DataCardinalityMinManySub,
        };
    }

    /// <summary>The data max-cardinality key, split on the <c>1</c> versus <c>n&gt;=2</c> bound bucket and the censused polarity.</summary>
    /// <param name="cardinality">The cardinality bound.</param>
    /// <param name="polarity">The censused polarity.</param>
    /// <returns>The max-1 or max-many key at the polarity.</returns>
    private static string DataMaxKey(int cardinality, CensusPolarity polarity)
    {
        return polarity switch
        {
            CensusPolarity.Super => cardinality == 1 ? Keys.DataCardinalityMaxOneSuper : Keys.DataCardinalityMaxManySuper,
            _ => cardinality == 1 ? Keys.DataCardinalityMaxOneSub : Keys.DataCardinalityMaxManySub,
        };
    }

    /// <summary>The characteristic-split key for an object-property characteristic axiom.</summary>
    /// <param name="characteristic">The asserted characteristic.</param>
    /// <returns>The census key naming that characteristic.</returns>
    private static string CharacteristicKey(OwlPropertyCharacteristic characteristic)
    {
        return characteristic switch
        {
            OwlPropertyCharacteristic.Functional => Keys.CharacteristicFunctional,
            OwlPropertyCharacteristic.InverseFunctional => Keys.CharacteristicInverseFunctional,
            OwlPropertyCharacteristic.Transitive => Keys.CharacteristicTransitive,
            OwlPropertyCharacteristic.Symmetric => Keys.CharacteristicSymmetric,
            OwlPropertyCharacteristic.Asymmetric => Keys.CharacteristicAsymmetric,
            OwlPropertyCharacteristic.Reflexive => Keys.CharacteristicReflexive,
            OwlPropertyCharacteristic.Irreflexive => Keys.CharacteristicIrreflexive,
            _ => throw new ArgumentOutOfRangeException(nameof(characteristic), characteristic, "Unknown object-property characteristic."),
        };
    }

    /// <summary>The different-individuals key carrying the operand count as a literal.</summary>
    /// <param name="count">The number of mutually distinct individuals.</param>
    /// <returns>The count-parameterized key.</returns>
    private static string DifferentIndividualsKey(int count)
    {
        return $"DifferentIndividuals({count})";
    }

    /// <summary>
    /// The polarity of a class-expression occurrence: subclass (negative) or
    /// superclass (positive) position, assigned exactly as a structural
    /// transformation would.
    /// </summary>
    private enum CensusPolarity
    {
        /// <summary>Negative, subclass-side position.</summary>
        Sub,

        /// <summary>Positive, superclass-side position.</summary>
        Super,
    }

    /// <summary>The canonical census key strings, the single home for every construct name the census emits.</summary>
    private static class Keys
    {
        /// <summary>Census key for the <c>SubClassOf</c> axiom.</summary>
        public const string SubClassOf = "SubClassOf";

        /// <summary>Census key for the <c>EquivalentClasses</c> axiom.</summary>
        public const string EquivalentClasses = "EquivalentClasses";

        /// <summary>Census key for the <c>DisjointClasses</c> axiom.</summary>
        public const string DisjointClasses = "DisjointClasses";

        /// <summary>Census key for the <c>DisjointUnion</c> axiom.</summary>
        public const string DisjointUnion = "DisjointUnion";

        /// <summary>Census key for the atomic <c>SubObjectPropertyOf</c> axiom.</summary>
        public const string SubObjectPropertyOf = "SubObjectPropertyOf";

        /// <summary>Census key for the property-chain <c>SubObjectPropertyOf</c> axiom.</summary>
        public const string SubObjectPropertyOfChain = "SubObjectPropertyOf(chain)";

        /// <summary>Census key for the <c>EquivalentObjectProperties</c> axiom.</summary>
        public const string EquivalentObjectProperties = "EquivalentObjectProperties";

        /// <summary>Census key for the <c>DisjointObjectProperties</c> axiom.</summary>
        public const string DisjointObjectProperties = "DisjointObjectProperties";

        /// <summary>Census key for the <c>InverseObjectProperties</c> axiom.</summary>
        public const string InverseObjectProperties = "InverseObjectProperties";

        /// <summary>Census key for the <c>ObjectPropertyDomain</c> axiom.</summary>
        public const string ObjectPropertyDomain = "ObjectPropertyDomain";

        /// <summary>Census key for the <c>ObjectPropertyRange</c> axiom.</summary>
        public const string ObjectPropertyRange = "ObjectPropertyRange";

        /// <summary>Census key for the <c>SubDataPropertyOf</c> axiom.</summary>
        public const string SubDataPropertyOf = "SubDataPropertyOf";

        /// <summary>Census key for the <c>EquivalentDataProperties</c> axiom.</summary>
        public const string EquivalentDataProperties = "EquivalentDataProperties";

        /// <summary>Census key for the <c>DisjointDataProperties</c> axiom.</summary>
        public const string DisjointDataProperties = "DisjointDataProperties";

        /// <summary>Census key for the <c>DataPropertyDomain</c> axiom.</summary>
        public const string DataPropertyDomain = "DataPropertyDomain";

        /// <summary>Census key for the <c>DataPropertyRange</c> axiom.</summary>
        public const string DataPropertyRange = "DataPropertyRange";

        /// <summary>Census key for the <c>FunctionalDataProperty</c> axiom.</summary>
        public const string FunctionalDataProperty = "FunctionalDataProperty";

        /// <summary>Census key for the <c>DatatypeDefinition</c> axiom.</summary>
        public const string DatatypeDefinition = "DatatypeDefinition";

        /// <summary>Census key for the <c>HasKey</c> axiom.</summary>
        public const string HasKey = "HasKey";

        /// <summary>Census key for the <c>ClassAssertion</c> axiom.</summary>
        public const string ClassAssertion = "ClassAssertion";

        /// <summary>Census key for the <c>ObjectPropertyAssertion</c> axiom.</summary>
        public const string ObjectPropertyAssertion = "ObjectPropertyAssertion";

        /// <summary>Census key for the <c>NegativeObjectPropertyAssertion</c> axiom.</summary>
        public const string NegativeObjectPropertyAssertion = "NegativeObjectPropertyAssertion";

        /// <summary>Census key for the <c>DataPropertyAssertion</c> axiom.</summary>
        public const string DataPropertyAssertion = "DataPropertyAssertion";

        /// <summary>Census key for the <c>NegativeDataPropertyAssertion</c> axiom.</summary>
        public const string NegativeDataPropertyAssertion = "NegativeDataPropertyAssertion";

        /// <summary>Census key for the <c>SameIndividual</c> axiom.</summary>
        public const string SameIndividual = "SameIndividual";

        /// <summary>Census key for the transitive object-property characteristic.</summary>
        public const string CharacteristicTransitive = "ObjectPropertyCharacteristic(Transitive)";

        /// <summary>Census key for the symmetric object-property characteristic.</summary>
        public const string CharacteristicSymmetric = "ObjectPropertyCharacteristic(Symmetric)";

        /// <summary>Census key for the functional object-property characteristic.</summary>
        public const string CharacteristicFunctional = "ObjectPropertyCharacteristic(Functional)";

        /// <summary>Census key for the inverse-functional object-property characteristic.</summary>
        public const string CharacteristicInverseFunctional = "ObjectPropertyCharacteristic(InverseFunctional)";

        /// <summary>Census key for the asymmetric object-property characteristic.</summary>
        public const string CharacteristicAsymmetric = "ObjectPropertyCharacteristic(Asymmetric)";

        /// <summary>Census key for the reflexive object-property characteristic.</summary>
        public const string CharacteristicReflexive = "ObjectPropertyCharacteristic(Reflexive)";

        /// <summary>Census key for the irreflexive object-property characteristic.</summary>
        public const string CharacteristicIrreflexive = "ObjectPropertyCharacteristic(Irreflexive)";

        /// <summary>Census key for an <c>ObjectComplementOf</c> in superclass polarity.</summary>
        public const string ObjectComplementOfSuper = "ObjectComplementOf(super)";

        /// <summary>Census key for an <c>ObjectComplementOf</c> in subclass polarity.</summary>
        public const string ObjectComplementOfSub = "ObjectComplementOf(sub)";

        /// <summary>Census key for an <c>ObjectUnionOf</c> in superclass polarity.</summary>
        public const string ObjectUnionOfSuper = "ObjectUnionOf(super)";

        /// <summary>Census key for an <c>ObjectUnionOf</c> in subclass polarity.</summary>
        public const string ObjectUnionOfSub = "ObjectUnionOf(sub)";

        /// <summary>Census key for a singleton <c>ObjectOneOf</c>.</summary>
        public const string ObjectOneOfOne = "ObjectOneOf(n=1)";

        /// <summary>Census key for a multi-individual <c>ObjectOneOf</c>.</summary>
        public const string ObjectOneOfMany = "ObjectOneOf(n>=2)";

        /// <summary>Census key for an <c>ObjectHasValue</c> restriction.</summary>
        public const string ObjectHasValue = "ObjectHasValue";

        /// <summary>Census key for an <c>ObjectHasSelf</c> restriction.</summary>
        public const string ObjectHasSelf = "ObjectHasSelf";

        /// <summary>Census key for an <c>ObjectSomeValuesFrom</c> restriction.</summary>
        public const string ObjectSomeValuesFrom = "ObjectSomeValuesFrom";

        /// <summary>Census key for an <c>ObjectAllValuesFrom</c> in superclass polarity.</summary>
        public const string ObjectAllValuesFromSuper = "ObjectAllValuesFrom(super)";

        /// <summary>Census key for an <c>ObjectAllValuesFrom</c> in subclass polarity.</summary>
        public const string ObjectAllValuesFromSub = "ObjectAllValuesFrom(sub)";

        /// <summary>Census key for an inverse object-property expression (<c>ObjectInverseOf</c>) at any position.</summary>
        public const string InverseObjectProperty = "InverseObjectProperty";

        /// <summary>Census key for a min-1 object cardinality restriction.</summary>
        public const string ObjectCardinalityMinOne = "ObjectCardinality(Min,1)";

        /// <summary>Census key for a min-<c>n&gt;=2</c> object cardinality restriction.</summary>
        public const string ObjectCardinalityMinMany = "ObjectCardinality(Min,n>=2)";

        /// <summary>Census key for a max-1 object cardinality restriction.</summary>
        public const string ObjectCardinalityMaxOne = "ObjectCardinality(Max,1)";

        /// <summary>Census key for a max-<c>n&gt;=2</c> object cardinality restriction.</summary>
        public const string ObjectCardinalityMaxMany = "ObjectCardinality(Max,n>=2)";

        /// <summary>Census key for a <c>DataSomeValuesFrom</c> restriction in superclass polarity.</summary>
        public const string DataSomeValuesFromSuper = "DataSomeValuesFrom(super)";

        /// <summary>Census key for a <c>DataSomeValuesFrom</c> restriction in subclass polarity.</summary>
        public const string DataSomeValuesFromSub = "DataSomeValuesFrom(sub)";

        /// <summary>Census key for a <c>DataAllValuesFrom</c> restriction in superclass polarity.</summary>
        public const string DataAllValuesFromSuper = "DataAllValuesFrom(super)";

        /// <summary>Census key for a <c>DataAllValuesFrom</c> restriction in subclass polarity.</summary>
        public const string DataAllValuesFromSub = "DataAllValuesFrom(sub)";

        /// <summary>Census key for a <c>DataHasValue</c> restriction in superclass polarity.</summary>
        public const string DataHasValueSuper = "DataHasValue(super)";

        /// <summary>Census key for a <c>DataHasValue</c> restriction in subclass polarity.</summary>
        public const string DataHasValueSub = "DataHasValue(sub)";

        /// <summary>Census key for a min-1 data cardinality restriction in superclass polarity.</summary>
        public const string DataCardinalityMinOneSuper = "DataCardinality(Min,1,super)";

        /// <summary>Census key for a min-1 data cardinality restriction in subclass polarity.</summary>
        public const string DataCardinalityMinOneSub = "DataCardinality(Min,1,sub)";

        /// <summary>Census key for a min-<c>n&gt;=2</c> data cardinality restriction in superclass polarity.</summary>
        public const string DataCardinalityMinManySuper = "DataCardinality(Min,n>=2,super)";

        /// <summary>Census key for a min-<c>n&gt;=2</c> data cardinality restriction in subclass polarity.</summary>
        public const string DataCardinalityMinManySub = "DataCardinality(Min,n>=2,sub)";

        /// <summary>Census key for a max-1 data cardinality restriction in superclass polarity.</summary>
        public const string DataCardinalityMaxOneSuper = "DataCardinality(Max,1,super)";

        /// <summary>Census key for a max-1 data cardinality restriction in subclass polarity.</summary>
        public const string DataCardinalityMaxOneSub = "DataCardinality(Max,1,sub)";

        /// <summary>Census key for a max-<c>n&gt;=2</c> data cardinality restriction in superclass polarity.</summary>
        public const string DataCardinalityMaxManySuper = "DataCardinality(Max,n>=2,super)";

        /// <summary>Census key for a max-<c>n&gt;=2</c> data cardinality restriction in subclass polarity.</summary>
        public const string DataCardinalityMaxManySub = "DataCardinality(Max,n>=2,sub)";

        /// <summary>Census key for a <c>DataComplementOf</c> range.</summary>
        public const string DataComplementOf = "DataComplementOf";

        /// <summary>Census key for a <c>DataUnionOf</c> range.</summary>
        public const string DataUnionOf = "DataUnionOf";

        /// <summary>Census key for a <c>DataIntersectionOf</c> range.</summary>
        public const string DataIntersectionOf = "DataIntersectionOf";

        /// <summary>Census key for a singleton <c>DataOneOf</c> range.</summary>
        public const string DataOneOfOne = "DataOneOf(n=1)";

        /// <summary>Census key for a multi-literal <c>DataOneOf</c> range.</summary>
        public const string DataOneOfMany = "DataOneOf(n>=2)";

        /// <summary>Census key for a <c>DatatypeRestriction</c> range.</summary>
        public const string DatatypeRestriction = "DatatypeRestriction";

        /// <summary>Census key for one facet of a <c>DatatypeRestriction</c>.</summary>
        public const string DataFacet = "DataFacet";
    }
}
