using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// The survey's verdict over one module: the admission bit and the nominal
/// census. The census bits are the survey's contribution to the reasoner's
/// assembled statistics and are meaningful on an axiom-admissible module only —
/// an axiom-level rejection short-circuits before the census scan and leaves
/// them unset.
/// </summary>
/// <param name="Admitted">Whether the context-saturation engine admits the module: every axiom lies within the surveyed slice and neither nominal co-occurrence guard tripped.</param>
/// <param name="MentionsNominals">Whether a nominal construct (<c>ObjectOneOf</c> or <c>ObjectHasValue</c>) occurs on a surveyed class-expression surface — the census face of the clausifier's jurisdiction bit: a nominal-bearing module routes its ABox through the root context, a nominal-free module is untouched by the nominal machinery.</param>
/// <param name="NominalCountingInverseCooccurrence">Whether nominals, object number restrictions, and inverse roles co-occur in the module — the Nom rule's trigger census (the rule cannot fire without all three), surfaced on the reasoner's assembled statistics for calibration against measured trigger populations.</param>
/// <param name="EnumerationHabitat">The enumeration-CSP habitat class the census-first recognizer assigned from axiom shapes (<see cref="ContextHabitatRecognizer"/>) — a census label the reasoner's assembled statistics and trace records carry on every context-arm decision and abstention; <see cref="EnumerationHabitatClass.None"/> on an axiom-level rejection, whose short-circuit precedes the census scan.</param>
internal readonly record struct ContextModuleSurveyResult(bool Admitted, bool MentionsNominals, bool NominalCountingInverseCooccurrence, EnumerationHabitatClass EnumerationHabitat);

/// <summary>
/// The context-saturation engine's admission gate over the disjunctive slice of
/// the consequence-based SRIQ calculus
/// (KR 2016; <see href="https://arxiv.org/abs/1602.04498"/>) and its
/// pay-as-you-go nominal extension (<see href="https://arxiv.org/abs/1805.01396"/>):
/// ALCHOIQ with regular role inclusions, self restrictions, the
/// negative-constraint role characteristics — role disjointness and asymmetry —
/// the DL-safe ground key layer (named-class keys with their key-scoped
/// data-value facts, decided by the ground key join rather than by clauses),
/// and the nominal layer (one-of and has-value over named individuals, routed
/// through the distinguished root context), excluding the data-side shapes
/// beyond the surveyed demand tier. The survey is syntactic, conservative, and
/// all-or-nothing: one inadmissible axiom — or a tripped nominal co-occurrence
/// guard — rejects the whole module. An admitted verdict
/// promises the module clausifies to clauses in the context grammar the engine's
/// ordered-resolution rules consume: canonical heads of any length whose
/// disjunctive literals are central concept atoms, the pairwise counting
/// equalities, or the enumeration equalities against constants, whose only
/// (in)equality heads lie over the neighbour/function/constant
/// grammar the second gate admits, and whose only fresh roles are the DL4
/// counting auxiliaries; role automata from property chains and transitivity are
/// permitted and their chain elimination stays within the grammar over fresh
/// state concepts. Regularity, simple-role, reserved-role, ground-counting-edge,
/// and loop-concept obligations are clausifier knowledge (they need the RBox
/// closure, the ABox scan, or the interned IRIs), so a survey-admitted module
/// that trips a clausifier guard delegates honestly at the reasoner's second
/// gate; a module the survey cannot vouch for is delegated whole to the fallback
/// oracle.
/// </summary>
/// <remarks>
/// The survey admits: <c>SubClassOf</c> (a negative-admissible subclass under a
/// positive-admissible superclass), <c>EquivalentClasses</c> (each side
/// admissible in both polarities), <c>DisjointClasses</c> (every operand
/// negative-admissible), object-property domain and range (a positive-admissible
/// class over a named or inverse role), the role-hierarchy axioms
/// (<c>SubObjectPropertyOf</c>, <c>EquivalentObjectProperties</c>,
/// <c>InverseObjectProperties</c> in any spelling), property chains (every link
/// and the super role named or inverse), <c>DisjointObjectProperties</c> (every
/// operand named or inverse), the <c>Symmetric</c>, <c>Transitive</c>,
/// <c>Reflexive</c>, <c>Irreflexive</c>, <c>Asymmetric</c>, <c>Functional</c>,
/// and <c>InverseFunctional</c> characteristics (the property named or inverse),
/// and the declaration, annotation, and import no-ops.
/// Class-expression admissibility is polarity-tracked (the subclass side is
/// negative, flipped under complement): named class / <c>owl:Thing</c> /
/// <c>owl:Nothing</c>, intersection, union, complement, existential over a named
/// or inverse role, self over a named or inverse role, and min-, max-, and
/// exact-cardinality of any bound over a named or inverse role are admissible in
/// any polarity — union operands and a min filler inherit their parent's
/// polarity, a complement operand and a max filler flip it, and an exact filler
/// is surveyed at both (the exact split sends it through min and max); a
/// negative-position cardinality lowers through the clausifier's coded
/// contrapositive duals (min into max minus one, max into min plus one) into a
/// positive union. Universal restriction over a named or inverse role is
/// likewise admissible in any polarity with its filler inheriting — the
/// negative position lowers through the faithful rewrite into an existential
/// over the complemented filler under a positive union. One-of admits in any
/// position with any member count, and has-value admits in any position over a
/// named or inverse role — the nominal layer, lowered through the root context.
/// Two whole-module co-occurrence guards reject at the survey exactly as the
/// clausifier's belt re-checks them: a <c>HasKey</c> axiom beside a nominal
/// construct (the key readback runs over ground contexts the nominal
/// jurisdiction bypasses), and an anonymous individual in a nominal position
/// (a blank node is existential, not a constant). A single-property data
/// existential and data has-value over a named data property admit at either
/// polarity: the positive (superclass) position lowers to a value-forcing
/// demand marker the datatype sidecar decides, and the negative (subclass)
/// position lowers to its NNF dual — a universal demand marker over the
/// complemented range on a disjunctive head. A single-property data universal
/// and a positive data min-cardinality admit only in positive position (the
/// universal's dual is a value-forcing disjunct, and a negated counting bound is
/// the NNF dual no demand kind represents), and so does a data max- or
/// exact-cardinality of bound one or above, ranged or range-less — the sidecar's
/// per-property max slot decides it, an exact bound riding its minimum and
/// maximum halves. A range-less min-cardinality of bound one and a range-less
/// max- or exact-cardinality of bound zero admit through the per-property
/// value-existence atom. The n-ary data shapes, a ranged bound-zero cardinality,
/// and a negative-position counting bound outside the {0,1} value-existence
/// shapes reject. Data ranges are opaque to the survey — an undecidable
/// range surfaces as an oracle undecided verdict at saturation, not a survey
/// rejection. The data-property RBox axioms admit for named data properties:
/// domain (a positive-admissible class over the property), range, functional,
/// sub-, equivalent-, and disjoint-data-property. The object-side ABox admits:
/// a class assertion whose asserted class is superclass-position admissible, and
/// object-property assertions, negative object-property assertions, same- and
/// different-individuals axioms per axiom — their compositions (a counting role
/// carrying an asserted edge, a reserved role in an assertion position) are the
/// clausifier's guards, delegated at the second gate. Disjoint unions admit when
/// every operand is admissible at both polarities (the lowering emits the
/// covering inclusion positively, the member inclusions and pairwise
/// disjointness negatively). A <c>HasKey</c> axiom admits when its keyed class
/// is a named class (<c>owl:Thing</c> included), its key list is non-empty, its
/// object key properties are named or inverse, and its data key properties are
/// named — the ground key join decides its named-individual consequences; the
/// degenerate shapes (empty key list, non-atomic keyed class) delegate whole. A
/// positive data-property assertion over a named data property and a non-literal
/// subject admits as a ground key-value fact — the clausifier's key-scoped belt
/// delegates any module whose asserted data property is entangled beyond key
/// lists. The negative data-property assertion rejects.
/// The reasoner's second gate over the clausification result is the
/// belt-and-suspenders check that turns any survey/clausifier drift into an honest
/// delegation rather than an unsound verdict.
/// </remarks>
internal static class ContextModuleSurvey
{
    /// <summary>Surveys the module for the context-saturation engine at the production default: the admission verdict over every axiom plus the two nominal co-occurrence guards, and the nominal census the reasoner's assembled statistics surface. The vr key-join lift's switch is on (the production default), so a <c>HasKey</c> axiom beside a nominal is admitted for the root key join; a caller that wants the dark face passes <c>rootKeyJoinEnabled: false</c> to the threaded overload explicitly.</summary>
    /// <param name="module">The module to survey.</param>
    /// <returns>The survey result. The census bits are scanned only when every axiom is individually admissible; an axiom-level rejection returns them unset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    public static ContextModuleSurveyResult Survey(ReasoningModule module)
    {
        return Survey(module, rootKeyJoinEnabled: true);
    }

    /// <summary>Surveys the module for the context-saturation engine with the vr key-join lift's switch threaded: when the switch is on (the production default), a <c>HasKey</c> axiom beside a nominal construct is ADMITTED (the belt-and-suspenders key-beside-nominal guard steps aside for the root key join, matching the clausifier's routing), while the anonymous-in-nominal guard stays governing. Off, the key-beside-nominal guard whole-rejects the module.</summary>
    /// <param name="module">The module to survey.</param>
    /// <param name="rootKeyJoinEnabled">Whether the vr key-join lift is armed, admitting a key-beside-nominal module past the survey's belt guard into the root key join.</param>
    /// <returns>The survey result. The census bits are scanned only when every axiom is individually admissible; an axiom-level rejection returns them unset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    public static ContextModuleSurveyResult Survey(ReasoningModule module, bool rootKeyJoinEnabled)
    {
        ArgumentNullException.ThrowIfNull(module);

        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(!AdmitsAxiom(axiom))
            {
                return new ContextModuleSurveyResult(Admitted: false, MentionsNominals: false, NominalCountingInverseCooccurrence: false, EnumerationHabitatClass.None);
            }
        }

        NominalCensus census = ScanNominalCensus(module);

        //The two whole-module co-occurrence guards, re-checked by the clausifier
        //(the belt-and-suspenders discipline): a key axiom beside a nominal
        //construct delegates because the key readback runs over ground contexts
        //the nominal jurisdiction bypasses — UNLESS the vr key-join lift is armed,
        //which routes the key readback onto the root tier — and an anonymous
        //individual in a nominal position delegates because a blank node is
        //existential, not a constant.
        bool admitted = !census.MentionsNominals || ((!census.MentionsHasKey || rootKeyJoinEnabled) && !census.AnonymousInNominal);

        return new ContextModuleSurveyResult(
            admitted,
            census.MentionsNominals,
            census.MentionsNominals && census.MentionsCounting && census.MentionsInverse,
            ContextHabitatRecognizer.Classify(module, census.MentionsNominals, census.MentionsCounting));
    }

    /// <summary>
    /// The census seam behind the admission gate: replays the survey's
    /// axiom-admissibility gate and, where every axiom is individually
    /// admissible, runs the shipping census scan and reports the two bits
    /// <see cref="ContextHabitatRecognizer.Classify"/> receives on the
    /// production path. The seam is additive and unreached by production —
    /// battery rows and corpus instruments read the passed census through it,
    /// so what they assert is the shipping scan's own answer and never a
    /// re-implementation's.
    /// </summary>
    /// <param name="module">The module to survey.</param>
    /// <param name="mentionsNominals">The census's nominal-mention bit exactly as the classification walk receives it; <see langword="false"/> on an axiom-level rejection.</param>
    /// <param name="mentionsCounting">The census's counting-mention bit exactly as the classification walk receives it; <see langword="false"/> on an axiom-level rejection.</param>
    /// <returns><see langword="true"/> when every axiom is individually admissible and the census scan ran; <see langword="false"/> on the axiom-level rejection whose short-circuit precedes the census scan, leaving the classification walk unreached.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    internal static bool TryCensusFor(ReasoningModule module, out bool mentionsNominals, out bool mentionsCounting)
    {
        ArgumentNullException.ThrowIfNull(module);

        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(!AdmitsAxiom(axiom))
            {
                mentionsNominals = false;
                mentionsCounting = false;

                return false;
            }
        }

        NominalCensus census = ScanNominalCensus(module);
        mentionsNominals = census.MentionsNominals;
        mentionsCounting = census.MentionsCounting;

        return true;
    }

    /// <summary>The mutable accumulator of the census scan: the nominal, counting, and inverse mentions the Nom-trigger statistic composes, and the anonymous-in-nominal and key-presence bits the co-occurrence guards read.</summary>
    private struct NominalCensus
    {
        /// <summary>Whether a nominal construct (one-of or has-value) occurs on a surveyed class-expression surface.</summary>
        public bool MentionsNominals;

        /// <summary>Whether an object number restriction occurs — a cardinality restriction of any bound, or a functional or inverse-functional characteristic (each lowers to a counting clause).</summary>
        public bool MentionsCounting;

        /// <summary>Whether an inverse role occurs — an inverse property expression in any surveyed role position, an inverse-object-properties axiom, or an inverse-functional characteristic.</summary>
        public bool MentionsInverse;

        /// <summary>Whether an anonymous individual occupies a nominal position (a one-of member or a has-value filler).</summary>
        public bool AnonymousInNominal;

        /// <summary>Whether a <c>HasKey</c> axiom occurs in the module.</summary>
        public bool MentionsHasKey;
    }

    /// <summary>Scans the module for the nominal census: the construct mentions behind the survey's co-occurrence guards and the Nom-trigger co-occurrence statistic (the Nom rule cannot fire without nominals, number restrictions, and inverse roles together). Runs only on a module whose every axiom is individually admissible.</summary>
    /// <param name="module">The module to scan.</param>
    /// <returns>The accumulated census.</returns>
    private static NominalCensus ScanNominalCensus(ReasoningModule module)
    {
        NominalCensus census = default;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            ScanAxiomCensus(axiom, ref census);
        }

        return census;
    }

    /// <summary>Scans one axiom's class-expression and role surfaces into the census — the same surfaces the admission walk surveys.</summary>
    /// <param name="axiom">The axiom.</param>
    /// <param name="census">The accumulator.</param>
    private static void ScanAxiomCensus(OwlAxiom axiom, ref NominalCensus census)
    {
        switch(axiom)
        {
            case(OwlSubClassOfAxiom subClass):
            {
                ScanExpressionCensus(subClass.SubClass, ref census);
                ScanExpressionCensus(subClass.SuperClass, ref census);
                break;
            }
            case(OwlDisjointUnionAxiom disjointUnion):
            {
                ScanExpressionListCensus(disjointUnion.Operands, ref census);
                break;
            }
            case(OwlEquivalentClassesAxiom equivalent):
            {
                ScanExpressionCensus(equivalent.First, ref census);
                ScanExpressionCensus(equivalent.Second, ref census);
                break;
            }
            case(OwlDisjointClassesAxiom disjoint):
            {
                ScanExpressionListCensus(disjoint.Operands, ref census);
                break;
            }
            case(OwlObjectPropertyDomainAxiom domain):
            {
                ScanRoleCensus(domain.Property, ref census);
                ScanExpressionCensus(domain.Domain, ref census);
                break;
            }
            case(OwlObjectPropertyRangeAxiom range):
            {
                ScanRoleCensus(range.Property, ref census);
                ScanExpressionCensus(range.Range, ref census);
                break;
            }
            case(OwlSubObjectPropertyOfAxiom subRole):
            {
                ScanRoleCensus(subRole.SubProperty, ref census);
                ScanRoleCensus(subRole.SuperProperty, ref census);
                break;
            }
            case(OwlEquivalentObjectPropertiesAxiom equivalentRoles):
            {
                ScanRoleCensus(equivalentRoles.First, ref census);
                ScanRoleCensus(equivalentRoles.Second, ref census);
                break;
            }
            case(OwlInverseObjectPropertiesAxiom):
            {
                census.MentionsInverse = true;
                break;
            }
            case(OwlPropertyChainAxiom chain):
            {
                for(int i = 0; i < chain.Chain.Count; i++)
                {
                    ScanRoleCensus(chain.Chain[i], ref census);
                }

                ScanRoleCensus(chain.SuperProperty, ref census);
                break;
            }
            case(OwlDisjointObjectPropertiesAxiom disjointRoles):
            {
                for(int i = 0; i < disjointRoles.Operands.Count; i++)
                {
                    ScanRoleCensus(disjointRoles.Operands[i], ref census);
                }

                break;
            }
            case(OwlObjectPropertyCharacteristicAxiom characteristic):
            {
                ScanRoleCensus(characteristic.Property, ref census);
                if(characteristic.Characteristic is OwlPropertyCharacteristic.Functional or OwlPropertyCharacteristic.InverseFunctional)
                {
                    census.MentionsCounting = true;
                }

                if(characteristic.Characteristic == OwlPropertyCharacteristic.InverseFunctional)
                {
                    census.MentionsInverse = true;
                }

                break;
            }
            case(OwlDataPropertyDomainAxiom dataDomain):
            {
                ScanExpressionCensus(dataDomain.Domain, ref census);
                break;
            }
            case(OwlHasKeyAxiom hasKey):
            {
                census.MentionsHasKey = true;
                ScanExpressionCensus(hasKey.Class, ref census);
                for(int i = 0; i < hasKey.ObjectProperties.Count; i++)
                {
                    ScanRoleCensus(hasKey.ObjectProperties[i], ref census);
                }

                break;
            }
            case(OwlClassAssertionAxiom classAssertion):
            {
                ScanExpressionCensus(classAssertion.Class, ref census);
                break;
            }
            case(OwlNegativeObjectPropertyAssertionAxiom negativeAssertion):
            {
                ScanRoleCensus(negativeAssertion.Property, ref census);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Scans every expression in the list into the census.</summary>
    /// <param name="expressions">The expressions.</param>
    /// <param name="census">The accumulator.</param>
    private static void ScanExpressionListCensus(IReadOnlyList<OwlClassExpression> expressions, ref NominalCensus census)
    {
        for(int i = 0; i < expressions.Count; i++)
        {
            ScanExpressionCensus(expressions[i], ref census);
        }
    }

    /// <summary>Scans a class expression for the census mentions with an explicit stack — the admission walk's traversal, census-recording.</summary>
    /// <param name="root">The class expression.</param>
    /// <param name="census">The accumulator.</param>
    private static void ScanExpressionCensus(OwlClassExpression root, ref NominalCensus census)
    {
        Stack<OwlClassExpression> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            OwlClassExpression expression = work.Pop();
            switch(expression)
            {
                case(OwlObjectOneOf oneOf):
                {
                    census.MentionsNominals = true;
                    for(int i = 0; i < oneOf.Individuals.Count; i++)
                    {
                        if(oneOf.Individuals[i] is not NamedNode)
                        {
                            census.AnonymousInNominal = true;
                        }
                    }

                    break;
                }
                case(OwlObjectHasValue hasValue):
                {
                    census.MentionsNominals = true;
                    ScanRoleCensus(hasValue.Property, ref census);
                    if(hasValue.Individual is not NamedNode)
                    {
                        census.AnonymousInNominal = true;
                    }

                    break;
                }
                case(OwlObjectCardinality cardinality):
                {
                    census.MentionsCounting = true;
                    ScanRoleCensus(cardinality.Property, ref census);
                    if(cardinality.Filler is not null)
                    {
                        work.Push(cardinality.Filler);
                    }

                    break;
                }
                case(OwlObjectSomeValuesFrom existential):
                {
                    ScanRoleCensus(existential.Property, ref census);
                    work.Push(existential.Filler);
                    break;
                }
                case(OwlObjectAllValuesFrom universal):
                {
                    ScanRoleCensus(universal.Property, ref census);
                    work.Push(universal.Filler);
                    break;
                }
                case(OwlObjectHasSelf self):
                {
                    ScanRoleCensus(self.Property, ref census);
                    break;
                }
                case(OwlObjectIntersectionOf intersection):
                {
                    for(int i = 0; i < intersection.Operands.Count; i++)
                    {
                        work.Push(intersection.Operands[i]);
                    }

                    break;
                }
                case(OwlObjectUnionOf union):
                {
                    for(int i = 0; i < union.Operands.Count; i++)
                    {
                        work.Push(union.Operands[i]);
                    }

                    break;
                }
                case(OwlObjectComplementOf complement):
                {
                    work.Push(complement.Operand);
                    break;
                }
                default:
                {
                    break;
                }
            }
        }
    }

    /// <summary>Records an inverse-role mention when the property expression is an inverse.</summary>
    /// <param name="property">The object-property expression.</param>
    /// <param name="census">The accumulator.</param>
    private static void ScanRoleCensus(OwlObjectPropertyExpression property, ref NominalCensus census)
    {
        if(property is OwlInverseObjectProperty)
        {
            census.MentionsInverse = true;
        }
    }

    /// <summary>Whether one axiom lies within the disjunctive SRIQ slice, with each class-expression position surveyed at its polarity and each role position required named or inverse.</summary>
    /// <param name="axiom">The axiom.</param>
    /// <returns><see langword="true"/> when the axiom is admissible.</returns>
    private static bool AdmitsAxiom(OwlAxiom axiom)
    {
        return axiom switch
        {
            OwlSubClassOfAxiom subClass => IsAdmissible(subClass.SubClass, negativePolarity: true) && IsAdmissible(subClass.SuperClass, negativePolarity: false),
            OwlDisjointUnionAxiom disjointUnion => AllAdmissible(disjointUnion.Operands, negativePolarity: true) && AllAdmissible(disjointUnion.Operands, negativePolarity: false),
            OwlEquivalentClassesAxiom equivalent =>
                IsAdmissible(equivalent.First, negativePolarity: true) && IsAdmissible(equivalent.First, negativePolarity: false)
                && IsAdmissible(equivalent.Second, negativePolarity: true) && IsAdmissible(equivalent.Second, negativePolarity: false),
            OwlDisjointClassesAxiom disjoint => AllAdmissible(disjoint.Operands, negativePolarity: true),
            OwlObjectPropertyDomainAxiom domain => IsNamedOrInverse(domain.Property) && IsAdmissible(domain.Domain, negativePolarity: false),
            OwlObjectPropertyRangeAxiom range => IsNamedOrInverse(range.Property) && IsAdmissible(range.Range, negativePolarity: false),
            OwlSubObjectPropertyOfAxiom subRole => IsNamedOrInverse(subRole.SubProperty) && IsNamedOrInverse(subRole.SuperProperty),
            OwlEquivalentObjectPropertiesAxiom equivalentRoles => IsNamedOrInverse(equivalentRoles.First) && IsNamedOrInverse(equivalentRoles.Second),
            OwlInverseObjectPropertiesAxiom inverse => IsNamedOrInverse(inverse.First) && IsNamedOrInverse(inverse.Second),
            OwlPropertyChainAxiom chain => AdmitsChain(chain),
            OwlDisjointObjectPropertiesAxiom disjointRoles => AdmitsDisjointRoles(disjointRoles),
            OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Symmetric or OwlPropertyCharacteristic.Transitive or OwlPropertyCharacteristic.Reflexive or OwlPropertyCharacteristic.Irreflexive or OwlPropertyCharacteristic.Asymmetric or OwlPropertyCharacteristic.Functional or OwlPropertyCharacteristic.InverseFunctional } characteristic => IsNamedOrInverse(characteristic.Property),
            OwlDataPropertyDomainAxiom dataDomain => IsNamedDataProperty(dataDomain.Property.Iri) && IsAdmissible(dataDomain.Domain, negativePolarity: false),
            OwlDataPropertyRangeAxiom dataRange => IsNamedDataProperty(dataRange.Property.Iri),
            OwlSubDataPropertyOfAxiom subData => IsNamedDataProperty(subData.SubProperty.Iri) && IsNamedDataProperty(subData.SuperProperty.Iri),
            OwlEquivalentDataPropertiesAxiom equivalentData => IsNamedDataProperty(equivalentData.First.Iri) && IsNamedDataProperty(equivalentData.Second.Iri),
            OwlFunctionalDataPropertyAxiom functionalData => IsNamedDataProperty(functionalData.Property.Iri),
            OwlDisjointDataPropertiesAxiom disjointData => AllNamedDataProperties(disjointData.Operands),
            OwlHasKeyAxiom hasKey => AdmitsHasKey(hasKey),
            OwlClassAssertionAxiom classAssertion => IsAdmissible(classAssertion.Class, negativePolarity: false),
            OwlObjectPropertyAssertionAxiom or OwlNegativeObjectPropertyAssertionAxiom or OwlSameIndividualAxiom or OwlDifferentIndividualsAxiom => true,
            OwlDataPropertyAssertionAxiom dataAssertion => dataAssertion.Source is not Literal && IsNamedDataProperty(dataAssertion.Property.Iri),
            OwlDeclarationAxiom or OwlImportAxiom or OwlAnnotationAssertionAxiom or OwlSubAnnotationPropertyOfAxiom
                or OwlAnnotationPropertyDomainAxiom or OwlAnnotationPropertyRangeAxiom => true,
            _ => false,
        };
    }

    /// <summary>
    /// Whether a <c>HasKey</c> axiom lies within the ground key decider's admission
    /// grammar: the keyed class is a named class (<c>owl:Thing</c> included), the
    /// key list is non-empty, every object key property is named or inverse, and
    /// every data key property is a named data property. The degenerate shapes
    /// delegate whole — an empty key list forces every named instance of the
    /// keyed class equal, which the value join cannot express, and a non-atomic
    /// keyed class has no membership readout under the atom-only ground join —
    /// so neither ever risks a silently-missed forced merge.
    /// </summary>
    /// <param name="hasKey">The key axiom.</param>
    /// <returns><see langword="true"/> when the ground key join owns the axiom.</returns>
    private static bool AdmitsHasKey(OwlHasKeyAxiom hasKey)
    {
        if(hasKey.Class is not OwlClassReference)
        {
            return false;
        }

        if(hasKey.ObjectProperties.Count == 0 && hasKey.DataProperties.Count == 0)
        {
            return false;
        }

        for(int i = 0; i < hasKey.ObjectProperties.Count; i++)
        {
            if(!IsNamedOrInverse(hasKey.ObjectProperties[i]))
            {
                return false;
            }
        }

        for(int i = 0; i < hasKey.DataProperties.Count; i++)
        {
            if(!IsNamedDataProperty(hasKey.DataProperties[i].Iri))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether every operand of a disjoint-data-properties axiom is a named (non-reserved) data property.</summary>
    /// <param name="operands">The mutually disjoint data properties.</param>
    /// <returns><see langword="true"/> when all are named data properties.</returns>
    private static bool AllNamedDataProperties(IReadOnlyList<NamedNode> operands)
    {
        for(int i = 0; i < operands.Count; i++)
        {
            if(!IsNamedDataProperty(operands[i].Iri))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a data-property IRI is a named data property the context arm lowers — any property other than the reserved <c>owl:topDataProperty</c> / <c>owl:bottomDataProperty</c>, whose fixed universal/empty extension the context path does not interpret.</summary>
    /// <param name="property">The data-property IRI.</param>
    /// <returns><see langword="true"/> for a non-reserved named data property.</returns>
    private static bool IsNamedDataProperty(Utf8String property)
    {
        return !property.Equals(OwlVocabulary.TopDataProperty) && !property.Equals(OwlVocabulary.BottomDataProperty);
    }

    /// <summary>Whether a property-chain axiom is admissible: every chain link and the super role is named or inverse.</summary>
    /// <param name="chain">The property-chain axiom.</param>
    /// <returns><see langword="true"/> when every link and the super role is named or inverse.</returns>
    private static bool AdmitsChain(OwlPropertyChainAxiom chain)
    {
        for(int i = 0; i < chain.Chain.Count; i++)
        {
            if(!IsNamedOrInverse(chain.Chain[i]))
            {
                return false;
            }
        }

        return IsNamedOrInverse(chain.SuperProperty);
    }

    /// <summary>Whether a disjoint-object-properties axiom is admissible: every operand is a named role or its inverse.</summary>
    /// <param name="disjointRoles">The disjoint-object-properties axiom.</param>
    /// <returns><see langword="true"/> when every operand is named or inverse.</returns>
    private static bool AdmitsDisjointRoles(OwlDisjointObjectPropertiesAxiom disjointRoles)
    {
        for(int i = 0; i < disjointRoles.Operands.Count; i++)
        {
            if(!IsNamedOrInverse(disjointRoles.Operands[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether every expression in the list is admissible at the given polarity — the disjoint-classes reduction keeps each operand on the subclass (negative) side.</summary>
    /// <param name="expressions">The expressions.</param>
    /// <param name="negativePolarity">The polarity every operand is surveyed at.</param>
    /// <returns><see langword="true"/> when all are admissible.</returns>
    private static bool AllAdmissible(IReadOnlyList<OwlClassExpression> expressions, bool negativePolarity)
    {
        for(int i = 0; i < expressions.Count; i++)
        {
            if(!IsAdmissible(expressions[i], negativePolarity))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a class expression is admissible at the given polarity: an
    /// explicit-stack walk that pushes each subexpression with its polarity.
    /// Named class / <c>owl:Thing</c> / <c>owl:Nothing</c>, intersection, union,
    /// complement, existential, universal, and self over a named or inverse role,
    /// min-, max-, and exact-cardinality of any bound over a named or inverse
    /// role, one-of of any member count, and has-value over a named or inverse
    /// role are admissible in any polarity (the nominal leaves are childless —
    /// their members and fillers are individuals, vetted by the census scan's
    /// anonymous-individual guard); a single-property data existential and data
    /// has-value over a named data property at either polarity (the negative
    /// position lowers to the NNF-dual universal marker over the complemented
    /// range), a single-property data universal and a positive data
    /// min-cardinality in positive position, a data max- or exact-cardinality of
    /// bound one or above — ranged or range-less — in positive position (the
    /// counting shapes the datatype sidecar's per-property max slot decides, an
    /// exact bound riding its two halves), and, over a named data property, a
    /// range-less min-cardinality of bound one at negative polarity and a
    /// range-less max- or exact-cardinality of bound zero at either polarity (the
    /// {0,1} value-existence shapes, lowered through the per-property HasValueOf
    /// value-existence marker; their ranges opaque, their leaves childless); a
    /// ranged max- or exact-cardinality of bound zero, a negative-position
    /// counting bound outside the {0,1} value-existence shapes, and everything
    /// else, rejects.
    /// Polarity flows by the
    /// lowering each shape takes:
    /// intersection and union operands and an existential, universal, or min
    /// filler inherit (the min witness head keeps its side, and the negative
    /// universal's faithful rewrite complements its filler under a flipped
    /// position, preserving the inherited polarity); a complement operand and a
    /// max filler flip (the max filler lands in the counting aux clause body, and
    /// the negative-position duals swap min and max); an exact filler descends at
    /// both polarities (the exact split sends it through min and max).
    /// </summary>
    /// <param name="root">The class expression.</param>
    /// <param name="negativePolarity">The polarity the root is surveyed at (<see langword="true"/> on the subclass side, flipped under complement).</param>
    /// <returns><see langword="true"/> when the expression is admissible.</returns>
    private static bool IsAdmissible(OwlClassExpression root, bool negativePolarity)
    {
        Stack<(OwlClassExpression Expression, bool Negative)> work = new();
        work.Push((root, negativePolarity));

        while(work.Count > 0)
        {
            (OwlClassExpression expression, bool negative) = work.Pop();
            (OwlClassExpression Expression, bool Negative)[]? children = expression switch
            {
                OwlClassReference => [],
                OwlObjectIntersectionOf intersection => WithPolarity(intersection.Operands, negative),
                OwlObjectSomeValuesFrom existential when IsNamedOrInverse(existential.Property) => [(existential.Filler, negative)],
                OwlObjectHasSelf self when IsNamedOrInverse(self.Property) => [],
                OwlObjectCardinality { Kind: OwlCardinalityKind.Min, Filler: not null } min when IsNamedOrInverse(min.Property) => [(min.Filler, negative)],
                OwlObjectCardinality { Kind: OwlCardinalityKind.Min } minUnqualified when IsNamedOrInverse(minUnqualified.Property) => [],
                OwlObjectCardinality { Kind: OwlCardinalityKind.Max, Filler: not null } max when IsNamedOrInverse(max.Property) => [(max.Filler, !negative)],
                OwlObjectCardinality { Kind: OwlCardinalityKind.Max } maxUnqualified when IsNamedOrInverse(maxUnqualified.Property) => [],
                OwlObjectCardinality { Kind: OwlCardinalityKind.Exact, Filler: not null } exact when IsNamedOrInverse(exact.Property) => [(exact.Filler, false), (exact.Filler, true)],
                OwlObjectCardinality { Kind: OwlCardinalityKind.Exact } exactUnqualified when IsNamedOrInverse(exactUnqualified.Property) => [],
                OwlObjectAllValuesFrom universal when IsNamedOrInverse(universal.Property) => [(universal.Filler, negative)],
                OwlObjectComplementOf complement => [(complement.Operand, !negative)],
                OwlObjectUnionOf union => WithPolarity(union.Operands, negative),
                OwlObjectOneOf => [],
                OwlObjectHasValue hasValue when IsNamedOrInverse(hasValue.Property) => [],
                OwlDataSomeValuesFrom { Properties.Count: 1 } dataSome when IsNamedDataProperty(dataSome.Properties[0].Iri) => [],
                OwlDataAllValuesFrom { Properties.Count: 1 } dataAll when !negative && IsNamedDataProperty(dataAll.Properties[0].Iri) => [],
                OwlDataHasValue dataHas when IsNamedDataProperty(dataHas.Property.Iri) => [],
                OwlDataCardinality { Kind: OwlCardinalityKind.Min } dataMin when !negative && IsNamedDataProperty(dataMin.Property.Iri) => [],
                OwlDataCardinality { Kind: OwlCardinalityKind.Min, Cardinality: 1, Range: null } dataMinOne when negative && IsNamedDataProperty(dataMinOne.Property.Iri) => [],
                OwlDataCardinality { Kind: OwlCardinalityKind.Max, Cardinality: 0, Range: null } dataMaxZero when IsNamedDataProperty(dataMaxZero.Property.Iri) => [],
                OwlDataCardinality { Kind: OwlCardinalityKind.Exact, Cardinality: 0, Range: null } dataExactZero when IsNamedDataProperty(dataExactZero.Property.Iri) => [],
                OwlDataCardinality { Kind: OwlCardinalityKind.Max, Cardinality: >= 1 } dataMax when !negative && IsNamedDataProperty(dataMax.Property.Iri) => [],
                OwlDataCardinality { Kind: OwlCardinalityKind.Exact, Cardinality: >= 1 } dataExact when !negative && IsNamedDataProperty(dataExact.Property.Iri) => [],
                _ => null,
            };

            if(children is null)
            {
                return false;
            }

            for(int i = 0; i < children.Length; i++)
            {
                work.Push(children[i]);
            }
        }

        return true;
    }

    /// <summary>Tags each operand with the polarity it inherits from its parent (intersection and union operands both preserve their parent's polarity).</summary>
    /// <param name="operands">The operand expressions.</param>
    /// <param name="negative">The parent's polarity.</param>
    /// <returns>The operands paired with the inherited polarity.</returns>
    private static (OwlClassExpression Expression, bool Negative)[] WithPolarity(IReadOnlyList<OwlClassExpression> operands, bool negative)
    {
        (OwlClassExpression Expression, bool Negative)[] tagged = new (OwlClassExpression, bool)[operands.Count];
        for(int i = 0; i < operands.Count; i++)
        {
            tagged[i] = (operands[i], negative);
        }

        return tagged;
    }

    /// <summary>Whether an object-property expression is a named role or its inverse — every spelling the slice's role positions admit.</summary>
    /// <param name="property">The object-property expression.</param>
    /// <returns><see langword="true"/> for a named or inverse role.</returns>
    private static bool IsNamedOrInverse(OwlObjectPropertyExpression property)
    {
        return property is OwlObjectPropertyReference or OwlInverseObjectProperty;
    }
}
