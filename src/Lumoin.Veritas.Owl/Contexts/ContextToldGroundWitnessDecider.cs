using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>The Shape W clash reason family — the told-ground-witness counterpart of the bijection-chain clash reasons: four stable leading identifiers the statistics assembly and the battery discriminate on.</summary>
internal static class ToldGroundWitnessClashReasons
{
    /// <summary>The complemented-membership clash: one told term is derived into a named class and, through a told complement conjunct, out of the same class.</summary>
    /// <param name="className">The class holding both the derived membership and its denial.</param>
    /// <returns>The named reason.</returns>
    public static string ComplementedMembership(Utf8String className)
    {
        return $"ToldGroundWitnessComplementedMembership({className})";
    }

    /// <summary>The empty-class assertion clash: a told class assertion types a term with <c>owl:Nothing</c>, or with a top-level complement of <c>owl:Thing</c>, whose extension is empty in every interpretation while the assertion demands a member.</summary>
    /// <param name="subject">The individual term the empty class was asserted on.</param>
    /// <returns>The named reason.</returns>
    public static string AssertedNothingMembership(Utf8String subject)
    {
        return $"ToldGroundWitnessAssertedNothingMembership({subject})";
    }

    /// <summary>The disjointness clash: one told term is derived into two named classes a told disjointness axiom separates.</summary>
    /// <param name="className">One named class of the clashing disjoint pair.</param>
    /// <returns>The named reason.</returns>
    public static string DisjointMembership(Utf8String className)
    {
        return $"ToldGroundWitnessDisjointMembership({className})";
    }

    /// <summary>The contradictory-edge clash: one ordered term pair is derived into a role's extension and, through a told negative property assertion, out of the same role.</summary>
    /// <param name="role">The role holding both the derived edge and its denial.</param>
    /// <returns>The named reason.</returns>
    public static string ContradictoryEdge(Utf8String role)
    {
        return $"ToldGroundWitnessContradictoryEdge({role})";
    }
}

/// <summary>
/// The Shape W window measurement the census-first recognizer's
/// pre-clausification pass reads on every told-ground-witness-jurisdiction
/// module — computed with the carrier deduplication applied BEFORE any
/// boundary comparison, so the battery's near-miss rows can pin the measured
/// quantity independently of the comparison's outcome.
/// </summary>
/// <param name="CarrierCount">The domain size: one carrier per distinct told individual term, keyed by IRI or anonymous label, and one fresh carrier where the module told no term at all — Direct Semantics admits no empty domain.</param>
/// <param name="EdgeCount">The ground role edges the completion holds — the told object-property assertions closed under told inverse mirroring, or the told edges alone where a window silence stopped the completion before it ran.</param>
/// <param name="WindowSilences">One when the carriers, the named classes, or the roles exceeded their bound — a named silence, never a verdict over an unclosed ground surface; zero otherwise.</param>
internal readonly record struct ToldGroundWitnessWindow(
    int CarrierCount,
    int EdgeCount,
    int WindowSilences)
{
    /// <summary>The empty window: no told ground surface was collected.</summary>
    public static ToldGroundWitnessWindow Empty => default;
}

/// <summary>The Shape W decider's outcome: the monotone ground refutation or the whole-module described-model certificate, and the window measurement the census carries unconditionally.</summary>
/// <param name="Consistent">The verdict — <see langword="false"/> for the derived-membership clash, <see langword="true"/> for the described-model certificate — or <see langword="null"/> when both faces are silent on the module.</param>
/// <param name="Window">The window measurement.</param>
internal readonly record struct ToldGroundWitnessOutcome(bool? Consistent, ToldGroundWitnessWindow Window)
{
    /// <summary>The named clash reason on a refutation; <see langword="null"/> on every other outcome.</summary>
    public string? ClashReason { get; init; }

    /// <summary>The named certificate route on a certification — <see cref="ContextToldGroundWitnessDecider.DescribedModelCertificate"/>; <see langword="null"/> on every other outcome.</summary>
    public string? CertificateRoute { get; init; }

    /// <summary>The silent outcome carrying only the window measurement.</summary>
    /// <param name="window">The measured window.</param>
    /// <returns>The silent outcome.</returns>
    public static ToldGroundWitnessOutcome SilentWith(ToldGroundWitnessWindow window)
    {
        return new ToldGroundWitnessOutcome(null, window);
    }
}

/// <summary>
/// The enumeration-CSP habitat decider's told-ground-witness faces (faces
/// twelve and thirteen): a tier-2 PROPAGATION over the ground memberships and
/// role edges a told-ground nominal-and-inverse module spells out, plus a
/// linear certificate-verification pass over the finished structure.
/// The CLASH face is MONOTONE: told object-property assertions and told
/// inverse pairs give the ground edges, told class assertions, domains,
/// ranges, named subclass steps, and existential definitions derive ground
/// memberships to a fixpoint, and a membership meeting its own denial, a
/// told disjoint partner, an asserted empty class, or a denied edge refutes
/// the module — unrecognized axioms are IGNORED, because a refuted told subset
/// condemns every superset. The core carries NO existential instantiation: an
/// existential definition is read only in the direction that derives a
/// membership FROM a told edge, never in the direction that would invent a
/// successor, since the witness a model owes may be an element no told term
/// denotes.
/// The CERTIFY face is the opposite discipline, a WHOLE-MODULE positive
/// admission followed by an explicit model construction: one carrier per
/// distinct told term, the told edges closed under told inverse mirroring to
/// the least fixpoint, one least-fixpoint extension per named class, and
/// <c>owl:Thing</c> and <c>owl:Nothing</c> pinned to the domain and the empty
/// set rather than left as variables. The construction is only a candidate
/// generator: EVERY axiom is then re-checked against the finished structure —
/// the told inverse axioms included, so the verifier never trusts the
/// generator — and the module is certified consistent only where every check
/// passes. A failed check is a SILENCE, never a refutation and never a repair.
/// Sound-or-silent and told-only throughout: saturation-derived facts never
/// feed either face. The carrier, class, and role ceilings are named window
/// constants; outside them both faces are silent with the measured numbers
/// already on the record.
/// </summary>
internal static class ContextToldGroundWitnessDecider
{
    /// <summary>
    /// The carrier ceiling: the model is constructed over exactly up to this
    /// many distinct told individual terms and BOTH faces are SILENT above it.
    /// Derivation (engineering, with the cost formula the battery pins): the
    /// edge completion and the class fixpoint are set operations over at most
    /// this many carriers, so the derived edge relation holds at most the cube
    /// of this constant and the class table at most its square, and the value
    /// matches the counting faces' shared sixteen ceiling — the
    /// counted-population, ground-clique, partition-anchor, gadget-atom,
    /// pair-assignment, spy-point member, and bijection-chain class bounds — so
    /// every counting-family pre-engine face carries one boundary discipline;
    /// the repairing face carries its own wider carrier, class, and role
    /// windows sized by its habitat. Collecting the told shapes is one linear
    /// pass bounded by the module's own axiom count rather than by this
    /// constant.
    /// </summary>
    public const int ToldGroundWitnessCarrierBound = 16;

    /// <summary>The named-class ceiling: one least-fixpoint extension is carried per distinct named class other than the two semantics-fixed constants, and both faces are SILENT above this many. Shares the carrier bound's derivation and value.</summary>
    public const int ToldGroundWitnessClassBound = 16;

    /// <summary>The role ceiling: one edge relation is carried per distinct told role, and both faces are SILENT above this many. Shares the carrier bound's derivation and value.</summary>
    public const int ToldGroundWitnessRoleBound = 16;

    /// <summary>The certificate route name of the described model: the finite structure the module's own told terms, told edges, and least-fixpoint class extensions spell out, verified axiom by axiom.</summary>
    public const string DescribedModelCertificate = "DescribedModel";

    /// <summary>Measures the Shape W census window without deciding anything: the carriers the ground surface holds, the completed edge count, and the window silence the bounds would charge — computed identically dark and lit, so the census ships unconditionally. No verdict is ever formed on this path.</summary>
    /// <param name="module">The module to measure.</param>
    /// <returns>The silent outcome carrying the measurement.</returns>
    public static ToldGroundWitnessOutcome Measure(ReasoningModule module)
    {
        ToldGroundWitnessGround ground = Harvest(module);
        ToldGroundWitnessWindow window = MeasureWindow(ground);
        if(window.WindowSilences > 0)
        {
            return ToldGroundWitnessOutcome.SilentWith(window);
        }

        ToldGroundWitnessRelations relations = CompleteEdges(ground);

        return ToldGroundWitnessOutcome.SilentWith(window with { EdgeCount = relations.EdgeCount });
    }

    /// <summary>
    /// Runs the told-ground-witness faces in jurisdiction order: the ground
    /// harvest and the inverse-edge completion first, since both faces read the
    /// completed edges; then the monotone clash core, which condemns the whole
    /// module and needs no admission; then the whole-module certificate pass
    /// only where the clash core stayed silent. The measurement lands first in
    /// every case, so a window silence still carries the numbers.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <returns>The outcome: the ground refutation, the described-model certificate, or silence — each with its measurement.</returns>
    public static ToldGroundWitnessOutcome Run(ReasoningModule module)
    {
        ToldGroundWitnessGround ground = Harvest(module);
        ToldGroundWitnessWindow window = MeasureWindow(ground);
        if(window.WindowSilences > 0)
        {
            return ToldGroundWitnessOutcome.SilentWith(window);
        }

        ToldGroundWitnessRelations relations = CompleteEdges(ground);
        window = window with { EdgeCount = relations.EdgeCount };

        if(TryRefute(module, ground, relations, out string? clashReason))
        {
            return new ToldGroundWitnessOutcome(false, window)
            {
                ClashReason = clashReason,
            };
        }

        if(IsDescribedModel(module, ground, relations))
        {
            return new ToldGroundWitnessOutcome(true, window)
            {
                CertificateRoute = DescribedModelCertificate,
            };
        }

        return ToldGroundWitnessOutcome.SilentWith(window);
    }

    /// <summary>One ground role edge over the interned carrier and role indices.</summary>
    /// <param name="Role">The role index.</param>
    /// <param name="Source">The source carrier index.</param>
    /// <param name="Target">The target carrier index.</param>
    private readonly record struct ToldGroundWitnessEdge(int Role, int Source, int Target);

    /// <summary>One told inverse-role pair over the interned role indices.</summary>
    /// <param name="First">The first role index.</param>
    /// <param name="Second">The second role index.</param>
    private readonly record struct ToldGroundWitnessRolePair(int First, int Second);

    /// <summary>One told domain or range axiom over an interned plain role and an interned named class.</summary>
    /// <param name="Role">The constrained role index.</param>
    /// <param name="Class">The named class index the role's sources or targets are confined to.</param>
    private readonly record struct ToldGroundWitnessRoleClass(int Role, int Class);

    /// <summary>One told subclass step between two named classes.</summary>
    /// <param name="From">The subsumed named class index.</param>
    /// <param name="To">The subsuming named class index.</param>
    private readonly record struct ToldGroundWitnessInclusion(int From, int To);

    /// <summary>One told existential membership source: a named class the module equates with, or subsumes, a top-level existential over a plain role.</summary>
    /// <param name="Target">The named class index a source of the edge is derived into.</param>
    /// <param name="Role">The existential's role index.</param>
    /// <param name="Filler">The existential's named filler class index, or <c>-1</c> where the filler is <c>owl:Thing</c> and every edge target qualifies.</param>
    private readonly record struct ToldGroundWitnessExistentialSource(int Target, int Role, int Filler);

    /// <summary>One told disjointness edge between two named classes.</summary>
    /// <param name="First">The first named class index.</param>
    /// <param name="Second">The second named class index.</param>
    private readonly record struct ToldGroundWitnessClassPair(int First, int Second);

    /// <summary>One seeding rule of the class least fixpoint: a whole admissible class expression whose extension flows into a named class.</summary>
    /// <param name="Source">The class expression the rule evaluates.</param>
    /// <param name="Target">The named class index the evaluated extension flows into.</param>
    private readonly record struct ToldGroundWitnessSeedRule(OwlClassExpression Source, int Target);

    /// <summary>
    /// The told ground surface one pass over the module's axioms collects: the
    /// interned carriers, named classes, and roles, and the told edges, denied
    /// edges, and inverse pairs read over them. Interning runs over the WHOLE
    /// module rather than over an admitted subset, because the clash face has no
    /// admission and the window bounds both faces alike.
    /// </summary>
    /// <param name="Carriers">The distinct told individual terms in first-seen order, keyed by IRI or anonymous label.</param>
    /// <param name="CarrierIndices">The identity index over the carriers.</param>
    /// <param name="Classes">The distinct named classes other than <c>owl:Thing</c> and <c>owl:Nothing</c>, in first-seen order.</param>
    /// <param name="ClassIndices">The identity index over the named classes.</param>
    /// <param name="Roles">The distinct told roles in first-seen order.</param>
    /// <param name="RoleIndices">The identity index over the roles.</param>
    /// <param name="ToldEdges">The told object-property assertion edges.</param>
    /// <param name="DeniedEdges">The told negative object-property assertion edges over plain roles.</param>
    /// <param name="InversePairs">The told inverse-role pairs over plain roles, in told argument order.</param>
    private sealed record ToldGroundWitnessGround(
        List<Utf8String> Carriers,
        Dictionary<Utf8String, int> CarrierIndices,
        List<Utf8String> Classes,
        Dictionary<Utf8String, int> ClassIndices,
        List<Utf8String> Roles,
        Dictionary<Utf8String, int> RoleIndices,
        List<ToldGroundWitnessEdge> ToldEdges,
        List<ToldGroundWitnessEdge> DeniedEdges,
        List<ToldGroundWitnessRolePair> InversePairs);

    /// <summary>The completed ground relations both faces read: the domain size, the edge relation closed under told inverse mirroring, the denial relation, and the closed edge count.</summary>
    /// <param name="DeltaSize">The domain size — the carrier count, or one where the module told no individual term.</param>
    /// <param name="Edges">The closed edge relation, indexed role-major then source then target.</param>
    /// <param name="DeniedEdges">The told denial relation, indexed identically.</param>
    /// <param name="EdgeCount">The closed relation's edge count.</param>
    private sealed record ToldGroundWitnessRelations(int DeltaSize, bool[] Edges, bool[] DeniedEdges, int EdgeCount);

    /// <summary>The constructed finite structure the certificate verifies against: the pinned domain, the least-fixpoint class table, the completed edge relation, and the identity indices the term, class, and role positions resolve through.</summary>
    /// <param name="DeltaSize">The domain size.</param>
    /// <param name="ClassCount">The named-class count.</param>
    /// <param name="Classes">The class table, indexed class-major then element.</param>
    /// <param name="Relations">The completed ground relations.</param>
    /// <param name="Ground">The harvested ground surface carrying the identity indices.</param>
    private sealed record ToldGroundWitnessModel(int DeltaSize, int ClassCount, bool[] Classes, ToldGroundWitnessRelations Relations, ToldGroundWitnessGround Ground);

    /// <summary>The axiom shapes the certify face admits — the classifier's answer and the verification pass's dispatch key, one enum shared by both so a shape can never be admitted by one and unknown to the other.</summary>
    private enum ToldGroundWitnessShape
    {
        /// <summary>Outside the whole-module admission: the certify face is silent on any module carrying one.</summary>
        Unadmitted = 0,

        /// <summary>A declaration or annotation-family axiom — no logical content, satisfied by every structure.</summary>
        NonLogical = 1,

        /// <summary>A class assertion over an admissible class expression and an individual term.</summary>
        ClassAssertion = 2,

        /// <summary>An object-property assertion between two individual terms.</summary>
        ObjectPropertyAssertion = 3,

        /// <summary>A subclass axiom between two admissible class expressions.</summary>
        SubClassOf = 4,

        /// <summary>An equivalence between two admissible class expressions.</summary>
        EquivalentClasses = 5,

        /// <summary>A disjointness over admissible class expressions.</summary>
        DisjointClasses = 6,

        /// <summary>A domain axiom over a plain role and an admissible class expression.</summary>
        ObjectPropertyDomain = 7,

        /// <summary>A range axiom over a plain role and an admissible class expression.</summary>
        ObjectPropertyRange = 8,

        /// <summary>A told inverse-role pair over plain roles.</summary>
        InverseObjectProperties = 9,

        /// <summary>A distinctness axiom over individual terms.</summary>
        DifferentIndividuals = 10,
    }

    /// <summary>Reads the window off the harvested ground surface: the domain size, the told edge count the completion starts from, and the silence any of the three bounds charges.</summary>
    /// <param name="ground">The harvested ground surface.</param>
    /// <returns>The window measurement.</returns>
    private static ToldGroundWitnessWindow MeasureWindow(ToldGroundWitnessGround ground)
    {
        int deltaSize = ground.Carriers.Count == 0 ? 1 : ground.Carriers.Count;
        bool exceeded = deltaSize > ToldGroundWitnessCarrierBound
            || ground.Classes.Count > ToldGroundWitnessClassBound
            || ground.Roles.Count > ToldGroundWitnessRoleBound;

        return new ToldGroundWitnessWindow(deltaSize, ground.ToldEdges.Count, exceeded ? 1 : 0);
    }

    /// <summary>
    /// Collects the told ground surface in ONE pass over the module's axioms:
    /// every individual-position term is interned as a carrier — assertion
    /// subjects and objects, enumeration members, has-value individuals, and
    /// distinctness and sameness members alike — every named class other than
    /// the two semantics-fixed constants takes a fixpoint variable, every told
    /// role takes an edge relation, and the told edges, denials, and inverse
    /// pairs are recorded over those indices. Nothing is rejected here: the
    /// window alone bounds the surface, and the two faces apply their own
    /// jurisdiction afterwards.
    /// </summary>
    /// <param name="module">The module to collect from.</param>
    /// <returns>The harvested ground surface.</returns>
    private static ToldGroundWitnessGround Harvest(ReasoningModule module)
    {
        ToldGroundWitnessGround ground = new([], [], [], [], [], [], [], [], []);
        Stack<OwlClassExpression> work = new();
        for(int index = 0; index < module.Axioms.Count; index++)
        {
            CollectAxiom(module.Axioms[index], ground, work);
            DrainExpressions(ground, work);
        }

        return ground;
    }

    /// <summary>Collects one axiom's direct terms, roles, and edges, and pushes its direct class expressions onto the traversal worklist.</summary>
    /// <param name="axiom">The axiom to collect.</param>
    /// <param name="ground">The ground surface accumulator.</param>
    /// <param name="workToAppendTo">The class-expression traversal worklist.</param>
    private static void CollectAxiom(OwlAxiom axiom, ToldGroundWitnessGround ground, Stack<OwlClassExpression> workToAppendTo)
    {
        switch(axiom)
        {
            case(OwlSubClassOfAxiom subClass):
            {
                workToAppendTo.Push(subClass.SubClass);
                workToAppendTo.Push(subClass.SuperClass);
                break;
            }
            case(OwlEquivalentClassesAxiom equivalent):
            {
                workToAppendTo.Push(equivalent.First);
                workToAppendTo.Push(equivalent.Second);
                break;
            }
            case(OwlDisjointClassesAxiom disjoint):
            {
                for(int index = 0; index < disjoint.Operands.Count; index++)
                {
                    workToAppendTo.Push(disjoint.Operands[index]);
                }

                break;
            }
            case(OwlDisjointUnionAxiom union):
            {
                for(int index = 0; index < union.Operands.Count; index++)
                {
                    workToAppendTo.Push(union.Operands[index]);
                }

                break;
            }
            case(OwlHasKeyAxiom key):
            {
                workToAppendTo.Push(key.Class);
                break;
            }
            case(OwlClassAssertionAxiom assertion):
            {
                CarrierIndex(ground, assertion.Individual);
                workToAppendTo.Push(assertion.Class);
                break;
            }
            case(OwlObjectPropertyAssertionAxiom assertion):
            {
                CollectToldEdge(assertion, ground);
                break;
            }
            case(OwlNegativeObjectPropertyAssertionAxiom denial):
            {
                CollectDeniedEdge(denial, ground);
                break;
            }
            case(OwlObjectPropertyDomainAxiom domain):
            {
                RoleIndex(ground, domain.Property);
                workToAppendTo.Push(domain.Domain);
                break;
            }
            case(OwlObjectPropertyRangeAxiom range):
            {
                RoleIndex(ground, range.Property);
                workToAppendTo.Push(range.Range);
                break;
            }
            case(OwlObjectPropertyCharacteristicAxiom characteristic):
            {
                RoleIndex(ground, characteristic.Property);
                break;
            }
            case(OwlInverseObjectPropertiesAxiom inverse):
            {
                CollectInversePair(inverse, ground);
                break;
            }
            case(OwlSameIndividualAxiom same):
            {
                CarrierIndex(ground, same.First);
                CarrierIndex(ground, same.Second);
                break;
            }
            case(OwlDifferentIndividualsAxiom different):
            {
                for(int index = 0; index < different.Individuals.Count; index++)
                {
                    CarrierIndex(ground, different.Individuals[index]);
                }

                break;
            }
            case(OwlDataPropertyAssertionAxiom assertion):
            {
                CarrierIndex(ground, assertion.Source);
                break;
            }
            case(OwlNegativeDataPropertyAssertionAxiom denial):
            {
                CarrierIndex(ground, denial.Source);
                break;
            }
            case(OwlDataPropertyDomainAxiom domain):
            {
                workToAppendTo.Push(domain.Domain);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Drains the class-expression worklist, interning every named class, role, and individual position the expressions carry — an explicit stack walk that descends through every combinator and filler.</summary>
    /// <param name="ground">The ground surface accumulator.</param>
    /// <param name="work">The traversal worklist.</param>
    private static void DrainExpressions(ToldGroundWitnessGround ground, Stack<OwlClassExpression> work)
    {
        while(work.Count > 0)
        {
            switch(work.Pop())
            {
                case(OwlClassReference reference):
                {
                    ClassIndex(ground, reference);
                    break;
                }
                case(OwlObjectOneOf oneOf):
                {
                    for(int index = 0; index < oneOf.Individuals.Count; index++)
                    {
                        CarrierIndex(ground, oneOf.Individuals[index]);
                    }

                    break;
                }
                case(OwlObjectComplementOf complement):
                {
                    work.Push(complement.Operand);
                    break;
                }
                case(OwlObjectIntersectionOf intersection):
                {
                    for(int index = 0; index < intersection.Operands.Count; index++)
                    {
                        work.Push(intersection.Operands[index]);
                    }

                    break;
                }
                case(OwlObjectUnionOf union):
                {
                    for(int index = 0; index < union.Operands.Count; index++)
                    {
                        work.Push(union.Operands[index]);
                    }

                    break;
                }
                case(OwlObjectSomeValuesFrom existential):
                {
                    RoleIndex(ground, existential.Property);
                    work.Push(existential.Filler);
                    break;
                }
                case(OwlObjectAllValuesFrom universal):
                {
                    RoleIndex(ground, universal.Property);
                    work.Push(universal.Filler);
                    break;
                }
                case(OwlObjectHasValue hasValue):
                {
                    RoleIndex(ground, hasValue.Property);
                    CarrierIndex(ground, hasValue.Individual);
                    break;
                }
                case(OwlObjectHasSelf hasSelf):
                {
                    RoleIndex(ground, hasSelf.Property);
                    break;
                }
                case(OwlObjectCardinality cardinality):
                {
                    RoleIndex(ground, cardinality.Property);
                    if(cardinality.Filler is OwlClassExpression filler)
                    {
                        work.Push(filler);
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

    /// <summary>Records one told object-property assertion as a ground edge over interned indices; a source or target that denotes neither a named nor an anonymous individual carries no edge.</summary>
    /// <param name="axiom">The told assertion.</param>
    /// <param name="ground">The ground surface accumulator.</param>
    private static void CollectToldEdge(OwlObjectPropertyAssertionAxiom axiom, ToldGroundWitnessGround ground)
    {
        if(!TryCarrierIndex(ground, axiom.Source, out int source) || !TryCarrierIndex(ground, axiom.Target, out int target))
        {
            return;
        }

        ground.ToldEdges.Add(new ToldGroundWitnessEdge(RoleIndex(ground, axiom.Property.Iri), source, target));
    }

    /// <summary>Records one told negative object-property assertion as a denial over interned indices; an inline inverse role would need a role normalization this face does not perform, so it carries no denial.</summary>
    /// <param name="axiom">The told denial.</param>
    /// <param name="ground">The ground surface accumulator.</param>
    private static void CollectDeniedEdge(OwlNegativeObjectPropertyAssertionAxiom axiom, ToldGroundWitnessGround ground)
    {
        if(axiom.Property is not OwlObjectPropertyReference role
            || !TryCarrierIndex(ground, axiom.Source, out int source)
            || !TryCarrierIndex(ground, axiom.Target, out int target))
        {
            return;
        }

        ground.DeniedEdges.Add(new ToldGroundWitnessEdge(RoleIndex(ground, role.Named.Iri), source, target));
    }

    /// <summary>Records one told inverse-role pair over plain roles; an inline inverse argument would need a role normalization this face does not perform, so it carries no pair.</summary>
    /// <param name="axiom">The told inverse-properties axiom.</param>
    /// <param name="ground">The ground surface accumulator.</param>
    private static void CollectInversePair(OwlInverseObjectPropertiesAxiom axiom, ToldGroundWitnessGround ground)
    {
        if(axiom.First is not OwlObjectPropertyReference first || axiom.Second is not OwlObjectPropertyReference second)
        {
            return;
        }

        ground.InversePairs.Add(new ToldGroundWitnessRolePair(RoleIndex(ground, first.Named.Iri), RoleIndex(ground, second.Named.Iri)));
    }

    /// <summary>Interns one individual term as a carrier, keyed by IRI or anonymous label under the content equality of <see cref="Utf8String"/> — the keying that keeps one term one carrier across every axiom that mentions it.</summary>
    /// <param name="ground">The ground surface accumulator.</param>
    /// <param name="term">The individual term.</param>
    private static void CarrierIndex(ToldGroundWitnessGround ground, RdfTerm term)
    {
        TryCarrierIndex(ground, term, out _);
    }

    /// <summary>Interns one individual term as a carrier and reads its index; a term that denotes neither a named nor an anonymous individual is no carrier.</summary>
    /// <param name="ground">The ground surface accumulator.</param>
    /// <param name="term">The individual term.</param>
    /// <param name="index">The carrier index; <c>-1</c> when the term is no individual.</param>
    /// <returns><see langword="true"/> on an individual term.</returns>
    private static bool TryCarrierIndex(ToldGroundWitnessGround ground, RdfTerm term, out int index)
    {
        switch(term)
        {
            case(NamedNode named):
            {
                index = Intern(ground.Carriers, ground.CarrierIndices, named.Iri);

                return true;
            }
            case(BlankNode anonymous):
            {
                index = Intern(ground.Carriers, ground.CarrierIndices, anonymous.Label);

                return true;
            }
            default:
            {
                index = -1;

                return false;
            }
        }
    }

    /// <summary>Interns one class reference as a fixpoint variable, skipping the two semantics-fixed constants whose extensions are pinned rather than propagated.</summary>
    /// <param name="ground">The ground surface accumulator.</param>
    /// <param name="reference">The class reference.</param>
    /// <returns>The class index, or <c>-1</c> for <c>owl:Thing</c> and <c>owl:Nothing</c>.</returns>
    private static int ClassIndex(ToldGroundWitnessGround ground, OwlClassReference reference)
    {
        if(reference.Class.Iri.Equals(OwlVocabulary.Thing) || reference.Class.Iri.Equals(OwlVocabulary.Nothing))
        {
            return -1;
        }

        return Intern(ground.Classes, ground.ClassIndices, reference.Class.Iri);
    }

    /// <summary>Interns one property expression's named role; an inline inverse interns the inverted role, so its edge relation exists even where no rule reads it.</summary>
    /// <param name="ground">The ground surface accumulator.</param>
    /// <param name="property">The property expression.</param>
    /// <returns>The role index.</returns>
    private static int RoleIndex(ToldGroundWitnessGround ground, OwlObjectPropertyExpression property)
    {
        return RoleIndex(ground, property.Property.Iri);
    }

    /// <summary>Interns one role IRI.</summary>
    /// <param name="ground">The ground surface accumulator.</param>
    /// <param name="role">The role IRI.</param>
    /// <returns>The role index.</returns>
    private static int RoleIndex(ToldGroundWitnessGround ground, Utf8String role)
    {
        return Intern(ground.Roles, ground.RoleIndices, role);
    }

    /// <summary>Interns one key into an identity table, appending it in first-seen order.</summary>
    /// <param name="keys">The keys in first-seen order.</param>
    /// <param name="indices">The identity index over the keys.</param>
    /// <param name="key">The key to intern.</param>
    /// <returns>The key's index.</returns>
    private static int Intern(List<Utf8String> keys, Dictionary<Utf8String, int> indices, Utf8String key)
    {
        if(indices.TryGetValue(key, out int index))
        {
            return index;
        }

        index = keys.Count;
        keys.Add(key);
        indices[key] = index;

        return index;
    }

    /// <summary>
    /// Completes the told edges to the LEAST FIXPOINT of told inverse
    /// mirroring: for a told inverse pair over two roles, every edge of the one
    /// contributes the reversed edge to the other, in both directions, until
    /// nothing more is added. At the fixpoint each told pair's two relations are
    /// exact converses of one another, so the completion satisfies the inverse
    /// axioms rather than merely approximating them. The worklist is explicit
    /// and the lattice finite, so the loop terminates; chains of pairs and
    /// self-inverse pairings need no special case.
    /// </summary>
    /// <param name="ground">The harvested ground surface, inside the window.</param>
    /// <returns>The completed relations.</returns>
    private static ToldGroundWitnessRelations CompleteEdges(ToldGroundWitnessGround ground)
    {
        int deltaSize = ground.Carriers.Count == 0 ? 1 : ground.Carriers.Count;
        int stride = deltaSize * deltaSize;
        bool[] edges = new bool[ground.Roles.Count * stride];
        bool[] denied = new bool[ground.Roles.Count * stride];
        Queue<ToldGroundWitnessEdge> work = new();
        int count = 0;
        for(int index = 0; index < ground.ToldEdges.Count; index++)
        {
            ToldGroundWitnessEdge edge = ground.ToldEdges[index];
            int slot = (edge.Role * stride) + (edge.Source * deltaSize) + edge.Target;
            if(edges[slot])
            {
                continue;
            }

            edges[slot] = true;
            count++;
            work.Enqueue(edge);
        }

        for(int index = 0; index < ground.DeniedEdges.Count; index++)
        {
            ToldGroundWitnessEdge edge = ground.DeniedEdges[index];
            denied[(edge.Role * stride) + (edge.Source * deltaSize) + edge.Target] = true;
        }

        while(work.Count > 0)
        {
            ToldGroundWitnessEdge edge = work.Dequeue();
            for(int index = 0; index < ground.InversePairs.Count; index++)
            {
                ToldGroundWitnessRolePair pair = ground.InversePairs[index];
                if(pair.First == edge.Role)
                {
                    MirrorEdge(edges, work, stride, deltaSize, pair.Second, edge.Target, edge.Source, ref count);
                }

                if(pair.Second == edge.Role)
                {
                    MirrorEdge(edges, work, stride, deltaSize, pair.First, edge.Target, edge.Source, ref count);
                }
            }
        }

        return new ToldGroundWitnessRelations(deltaSize, edges, denied, count);
    }

    /// <summary>Adds one mirrored edge to the completion, enqueueing it for further mirroring only where it is new.</summary>
    /// <param name="edgesToAppendTo">The edge relation.</param>
    /// <param name="workToAppendTo">The completion worklist.</param>
    /// <param name="stride">The per-role stride of the edge relation.</param>
    /// <param name="deltaSize">The domain size.</param>
    /// <param name="role">The role the mirrored edge lands in.</param>
    /// <param name="source">The mirrored edge's source.</param>
    /// <param name="target">The mirrored edge's target.</param>
    /// <param name="count">The running edge count.</param>
    private static void MirrorEdge(bool[] edgesToAppendTo, Queue<ToldGroundWitnessEdge> workToAppendTo, int stride, int deltaSize, int role, int source, int target, ref int count)
    {
        int slot = (role * stride) + (source * deltaSize) + target;
        if(edgesToAppendTo[slot])
        {
            return;
        }

        edgesToAppendTo[slot] = true;
        count++;
        workToAppendTo.Enqueue(new ToldGroundWitnessEdge(role, source, target));
    }

    /// <summary>
    /// The monotone clash core: collects the told ground premises, derives the
    /// ground memberships to a fixpoint over the completed edges, and answers
    /// whether the recognized told subset is unsatisfiable. Unrecognized axioms
    /// are IGNORED rather than rejecting the module, because a refuted subset
    /// condemns every superset. No rule instantiates an existential with a told
    /// term, so no derivation depends on a witness the module never named.
    /// </summary>
    /// <param name="module">The module to refute.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="relations">The completed relations.</param>
    /// <param name="clashReason">The named clash reason; <see langword="null"/> when no clash was reached.</param>
    /// <returns><see langword="true"/> when the recognized subset — and therefore the whole module — is inconsistent.</returns>
    private static bool TryRefute(ReasoningModule module, ToldGroundWitnessGround ground, ToldGroundWitnessRelations relations, [NotNullWhen(true)] out string? clashReason)
    {
        clashReason = null;
        int deltaSize = relations.DeltaSize;
        int stride = deltaSize * deltaSize;
        bool[] positive = new bool[ground.Classes.Count * deltaSize];
        bool[] negative = new bool[ground.Classes.Count * deltaSize];
        List<ToldGroundWitnessRoleClass> domains = [];
        List<ToldGroundWitnessRoleClass> ranges = [];
        List<ToldGroundWitnessInclusion> inclusions = [];
        List<ToldGroundWitnessExistentialSource> existentials = [];
        List<ToldGroundWitnessClassPair> disjointness = [];

        for(int index = 0; index < module.Axioms.Count; index++)
        {
            if(CollectClashPremise(module.Axioms[index], ground, deltaSize, positive, negative, domains, ranges, inclusions, existentials, disjointness, out clashReason))
            {
                return true;
            }
        }

        for(int index = 0; index < ground.Roles.Count; index++)
        {
            for(int slot = 0; slot < stride; slot++)
            {
                if(relations.Edges[(index * stride) + slot] && relations.DeniedEdges[(index * stride) + slot])
                {
                    clashReason = ToldGroundWitnessClashReasons.ContradictoryEdge(ground.Roles[index]);

                    return true;
                }
            }
        }

        SeedRoleClasses(domains, ranges, relations, deltaSize, positive);
        PropagateMemberships(inclusions, existentials, relations, deltaSize, positive);

        for(int index = 0; index < ground.Classes.Count; index++)
        {
            for(int element = 0; element < deltaSize; element++)
            {
                if(positive[(index * deltaSize) + element] && negative[(index * deltaSize) + element])
                {
                    clashReason = ToldGroundWitnessClashReasons.ComplementedMembership(ground.Classes[index]);

                    return true;
                }
            }
        }

        for(int index = 0; index < disjointness.Count; index++)
        {
            ToldGroundWitnessClassPair pair = disjointness[index];
            for(int element = 0; element < deltaSize; element++)
            {
                if(positive[(pair.First * deltaSize) + element] && positive[(pair.Second * deltaSize) + element])
                {
                    clashReason = ToldGroundWitnessClashReasons.DisjointMembership(ground.Classes[pair.First]);

                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Collects one axiom's told ground premises: the class assertion's positive
    /// and complemented memberships together with the two outright empty-class
    /// clashes, the domain and range constraints over named targets, the
    /// named-to-named subclass steps, the existential membership sources in
    /// equivalence and subclass-superset position, and the told disjointness
    /// edges. Every unrecognized shape is ignored.
    /// </summary>
    /// <param name="axiom">The axiom to collect.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="deltaSize">The domain size.</param>
    /// <param name="positiveToAppendTo">The positive membership table.</param>
    /// <param name="negativeToAppendTo">The negative membership table.</param>
    /// <param name="domainsToAppendTo">The domain constraints.</param>
    /// <param name="rangesToAppendTo">The range constraints.</param>
    /// <param name="inclusionsToAppendTo">The named subclass steps.</param>
    /// <param name="existentialsToAppendTo">The existential membership sources.</param>
    /// <param name="disjointnessToAppendTo">The told disjointness edges.</param>
    /// <param name="clashReason">The outright clash reason; <see langword="null"/> when the axiom carried none.</param>
    /// <returns><see langword="true"/> when the axiom is an outright empty-class assertion.</returns>
    private static bool CollectClashPremise(
        OwlAxiom axiom,
        ToldGroundWitnessGround ground,
        int deltaSize,
        bool[] positiveToAppendTo,
        bool[] negativeToAppendTo,
        List<ToldGroundWitnessRoleClass> domainsToAppendTo,
        List<ToldGroundWitnessRoleClass> rangesToAppendTo,
        List<ToldGroundWitnessInclusion> inclusionsToAppendTo,
        List<ToldGroundWitnessExistentialSource> existentialsToAppendTo,
        List<ToldGroundWitnessClassPair> disjointnessToAppendTo,
        [NotNullWhen(true)] out string? clashReason)
    {
        clashReason = null;
        switch(axiom)
        {
            case(OwlClassAssertionAxiom assertion):
            {
                return CollectAssertedMembership(assertion, ground, deltaSize, positiveToAppendTo, negativeToAppendTo, out clashReason);
            }
            case(OwlObjectPropertyDomainAxiom { Property: OwlObjectPropertyReference domainRole } domain):
            {
                CollectRoleClass(domain.Domain, domainRole.Named.Iri, ground, domainsToAppendTo);

                return false;
            }
            case(OwlObjectPropertyRangeAxiom { Property: OwlObjectPropertyReference rangeRole } range):
            {
                CollectRoleClass(range.Range, rangeRole.Named.Iri, ground, rangesToAppendTo);

                return false;
            }
            case(OwlSubClassOfAxiom subClass):
            {
                CollectInclusion(subClass, ground, inclusionsToAppendTo);
                CollectExistentialSource(subClass.SuperClass, subClass.SubClass, ground, existentialsToAppendTo);

                return false;
            }
            case(OwlEquivalentClassesAxiom equivalent):
            {
                CollectExistentialSource(equivalent.First, equivalent.Second, ground, existentialsToAppendTo);
                CollectExistentialSource(equivalent.Second, equivalent.First, ground, existentialsToAppendTo);

                return false;
            }
            case(OwlDisjointClassesAxiom disjoint):
            {
                CollectDisjointness(disjoint, ground, disjointnessToAppendTo);

                return false;
            }
            default:
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Collects one told class assertion's ground memberships: a named class
    /// types the term, a top-level complement of a named class denies it, and
    /// the intersection wrapper the strict arm's refutation probes spell is read
    /// conjunct by conjunct. Asserting <c>owl:Nothing</c>, or the complement of
    /// <c>owl:Thing</c>, demands a member of an extension empty in every
    /// interpretation and clashes outright; the complement of <c>owl:Nothing</c>
    /// asks nothing and the typing with <c>owl:Thing</c> asks only for a domain
    /// element, so neither carries a ground fact.
    /// </summary>
    /// <param name="axiom">The told class assertion.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="deltaSize">The domain size.</param>
    /// <param name="positiveToAppendTo">The positive membership table.</param>
    /// <param name="negativeToAppendTo">The negative membership table.</param>
    /// <param name="clashReason">The outright clash reason; <see langword="null"/> when the assertion carried none.</param>
    /// <returns><see langword="true"/> on the outright empty-class assertion.</returns>
    private static bool CollectAssertedMembership(
        OwlClassAssertionAxiom axiom,
        ToldGroundWitnessGround ground,
        int deltaSize,
        bool[] positiveToAppendTo,
        bool[] negativeToAppendTo,
        [NotNullWhen(true)] out string? clashReason)
    {
        clashReason = null;
        if(!TryCarrierIndex(ground, axiom.Individual, out int term))
        {
            return false;
        }

        if(axiom.Class is not OwlObjectIntersectionOf intersection)
        {
            return CollectAssertedConjunct(axiom.Class, term, ground, deltaSize, positiveToAppendTo, negativeToAppendTo, out clashReason);
        }

        for(int index = 0; index < intersection.Operands.Count; index++)
        {
            if(CollectAssertedConjunct(intersection.Operands[index], term, ground, deltaSize, positiveToAppendTo, negativeToAppendTo, out clashReason))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Collects one told conjunct of an asserted class expression into the ground membership tables.</summary>
    /// <param name="conjunct">The conjunct expression.</param>
    /// <param name="term">The carrier index the assertion types.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="deltaSize">The domain size.</param>
    /// <param name="positiveToAppendTo">The positive membership table.</param>
    /// <param name="negativeToAppendTo">The negative membership table.</param>
    /// <param name="clashReason">The outright clash reason; <see langword="null"/> when the conjunct carried none.</param>
    /// <returns><see langword="true"/> on the outright empty-class conjunct.</returns>
    private static bool CollectAssertedConjunct(
        OwlClassExpression conjunct,
        int term,
        ToldGroundWitnessGround ground,
        int deltaSize,
        bool[] positiveToAppendTo,
        bool[] negativeToAppendTo,
        [NotNullWhen(true)] out string? clashReason)
    {
        clashReason = null;
        if(conjunct is OwlClassReference asserted)
        {
            if(asserted.Class.Iri.Equals(OwlVocabulary.Nothing))
            {
                clashReason = ToldGroundWitnessClashReasons.AssertedNothingMembership(ground.Carriers[term]);

                return true;
            }

            if(ground.ClassIndices.TryGetValue(asserted.Class.Iri, out int assertedClass))
            {
                positiveToAppendTo[(assertedClass * deltaSize) + term] = true;
            }

            return false;
        }

        if(conjunct is not OwlObjectComplementOf complement || complement.Operand is not OwlClassReference denied)
        {
            return false;
        }

        if(denied.Class.Iri.Equals(OwlVocabulary.Thing))
        {
            clashReason = ToldGroundWitnessClashReasons.AssertedNothingMembership(ground.Carriers[term]);

            return true;
        }

        if(ground.ClassIndices.TryGetValue(denied.Class.Iri, out int deniedClass))
        {
            negativeToAppendTo[(deniedClass * deltaSize) + term] = true;
        }

        return false;
    }

    /// <summary>Collects one told domain or range constraint whose target is a NAMED class; a complex target confines the sources or targets to a disjunction rather than to one ground class and carries no ground fact.</summary>
    /// <param name="target">The constraint's class expression.</param>
    /// <param name="role">The constrained role's IRI.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="constraintsToAppendTo">The constraint accumulator.</param>
    private static void CollectRoleClass(OwlClassExpression target, Utf8String role, ToldGroundWitnessGround ground, List<ToldGroundWitnessRoleClass> constraintsToAppendTo)
    {
        if(target is not OwlClassReference reference
            || !ground.ClassIndices.TryGetValue(reference.Class.Iri, out int constrained)
            || !ground.RoleIndices.TryGetValue(role, out int constrainedRole))
        {
            return;
        }

        constraintsToAppendTo.Add(new ToldGroundWitnessRoleClass(constrainedRole, constrained));
    }

    /// <summary>Collects one told subclass step between two NAMED classes; a complex side carries no ground step.</summary>
    /// <param name="axiom">The candidate subclass axiom.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="inclusionsToAppendTo">The inclusion accumulator.</param>
    private static void CollectInclusion(OwlSubClassOfAxiom axiom, ToldGroundWitnessGround ground, List<ToldGroundWitnessInclusion> inclusionsToAppendTo)
    {
        if(axiom.SubClass is not OwlClassReference sub
            || axiom.SuperClass is not OwlClassReference super
            || !ground.ClassIndices.TryGetValue(sub.Class.Iri, out int from)
            || !ground.ClassIndices.TryGetValue(super.Class.Iri, out int to))
        {
            return;
        }

        inclusionsToAppendTo.Add(new ToldGroundWitnessInclusion(from, to));
    }

    /// <summary>
    /// Collects one existential membership source, read in the given side order:
    /// a top-level existential over a plain role whose filler is
    /// <c>owl:Thing</c> or a named class, paired with a named class the module
    /// tells the existential is INCLUDED IN. Only that direction is read — the
    /// converse would owe a successor the module never named.
    /// </summary>
    /// <param name="existentialSide">The candidate existential side.</param>
    /// <param name="namedSide">The candidate named-class side.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="existentialsToAppendTo">The existential-source accumulator.</param>
    private static void CollectExistentialSource(OwlClassExpression existentialSide, OwlClassExpression namedSide, ToldGroundWitnessGround ground, List<ToldGroundWitnessExistentialSource> existentialsToAppendTo)
    {
        if(existentialSide is not OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference role } existential
            || namedSide is not OwlClassReference named
            || !ground.ClassIndices.TryGetValue(named.Class.Iri, out int target)
            || !ground.RoleIndices.TryGetValue(role.Named.Iri, out int existentialRole)
            || existential.Filler is not OwlClassReference filler)
        {
            return;
        }

        if(filler.Class.Iri.Equals(OwlVocabulary.Thing))
        {
            existentialsToAppendTo.Add(new ToldGroundWitnessExistentialSource(target, existentialRole, -1));

            return;
        }

        if(ground.ClassIndices.TryGetValue(filler.Class.Iri, out int fillerClass))
        {
            existentialsToAppendTo.Add(new ToldGroundWitnessExistentialSource(target, existentialRole, fillerClass));
        }
    }

    /// <summary>Collects one told disjointness axiom of any arity as unordered pairs over its NAMED class operands — only TOLD edges, never a derived disjointness.</summary>
    /// <param name="axiom">The told disjointness axiom.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="disjointnessToAppendTo">The disjointness accumulator.</param>
    private static void CollectDisjointness(OwlDisjointClassesAxiom axiom, ToldGroundWitnessGround ground, List<ToldGroundWitnessClassPair> disjointnessToAppendTo)
    {
        for(int first = 0; first < axiom.Operands.Count; first++)
        {
            if(axiom.Operands[first] is not OwlClassReference left || !ground.ClassIndices.TryGetValue(left.Class.Iri, out int leftClass))
            {
                continue;
            }

            for(int second = first + 1; second < axiom.Operands.Count; second++)
            {
                if(axiom.Operands[second] is OwlClassReference right
                    && ground.ClassIndices.TryGetValue(right.Class.Iri, out int rightClass)
                    && rightClass != leftClass)
                {
                    disjointnessToAppendTo.Add(new ToldGroundWitnessClassPair(leftClass, rightClass));
                }
            }
        }
    }

    /// <summary>Seeds the ground memberships the told domain and range constraints force over the COMPLETED edges — the derived inverse edges included, since a constraint holds of a role's whole extension and not merely of its told part.</summary>
    /// <param name="domains">The told domain constraints.</param>
    /// <param name="ranges">The told range constraints.</param>
    /// <param name="relations">The completed relations.</param>
    /// <param name="deltaSize">The domain size.</param>
    /// <param name="membershipsToAppendTo">The positive membership table.</param>
    private static void SeedRoleClasses(List<ToldGroundWitnessRoleClass> domains, List<ToldGroundWitnessRoleClass> ranges, ToldGroundWitnessRelations relations, int deltaSize, bool[] membershipsToAppendTo)
    {
        int stride = deltaSize * deltaSize;
        for(int index = 0; index < domains.Count; index++)
        {
            ToldGroundWitnessRoleClass domain = domains[index];
            for(int source = 0; source < deltaSize; source++)
            {
                for(int target = 0; target < deltaSize; target++)
                {
                    if(relations.Edges[(domain.Role * stride) + (source * deltaSize) + target])
                    {
                        membershipsToAppendTo[(domain.Class * deltaSize) + source] = true;
                    }
                }
            }
        }

        for(int index = 0; index < ranges.Count; index++)
        {
            ToldGroundWitnessRoleClass range = ranges[index];
            for(int source = 0; source < deltaSize; source++)
            {
                for(int target = 0; target < deltaSize; target++)
                {
                    if(relations.Edges[(range.Role * stride) + (source * deltaSize) + target])
                    {
                        membershipsToAppendTo[(range.Class * deltaSize) + target] = true;
                    }
                }
            }
        }
    }

    /// <summary>Runs the bounded worklist over the named subclass steps and the existential membership sources: each derivation re-offers every rule, and the loop ends when no rule derives anything further. Every derivation adds a membership no rule can retract, and the table is finite, so the loop terminates.</summary>
    /// <param name="inclusions">The named subclass steps.</param>
    /// <param name="existentials">The existential membership sources.</param>
    /// <param name="relations">The completed relations.</param>
    /// <param name="deltaSize">The domain size.</param>
    /// <param name="membershipsToAppendTo">The positive membership table.</param>
    private static void PropagateMemberships(List<ToldGroundWitnessInclusion> inclusions, List<ToldGroundWitnessExistentialSource> existentials, ToldGroundWitnessRelations relations, int deltaSize, bool[] membershipsToAppendTo)
    {
        int rules = inclusions.Count + existentials.Count;
        bool[] queued = new bool[rules];
        Queue<int> work = new();
        for(int index = 0; index < rules; index++)
        {
            queued[index] = true;
            work.Enqueue(index);
        }

        while(work.Count > 0)
        {
            int index = work.Dequeue();
            queued[index] = false;
            bool derived = index < inclusions.Count
                ? ApplyInclusion(inclusions[index], deltaSize, membershipsToAppendTo)
                : ApplyExistentialSource(existentials[index - inclusions.Count], relations, deltaSize, membershipsToAppendTo);
            if(!derived)
            {
                continue;
            }

            for(int other = 0; other < rules; other++)
            {
                if(!queued[other])
                {
                    queued[other] = true;
                    work.Enqueue(other);
                }
            }
        }
    }

    /// <summary>Applies one named subclass step: every member of the subsumed class is a member of the subsuming one.</summary>
    /// <param name="inclusion">The subclass step.</param>
    /// <param name="deltaSize">The domain size.</param>
    /// <param name="membershipsToAppendTo">The positive membership table.</param>
    /// <returns><see langword="true"/> when the step derived a new membership.</returns>
    private static bool ApplyInclusion(ToldGroundWitnessInclusion inclusion, int deltaSize, bool[] membershipsToAppendTo)
    {
        bool derived = false;
        for(int element = 0; element < deltaSize; element++)
        {
            if(membershipsToAppendTo[(inclusion.From * deltaSize) + element] && !membershipsToAppendTo[(inclusion.To * deltaSize) + element])
            {
                membershipsToAppendTo[(inclusion.To * deltaSize) + element] = true;
                derived = true;
            }
        }

        return derived;
    }

    /// <summary>Applies one existential membership source: a term holding an edge into a qualifying target is a member of the named class the existential is included in. The filler qualifies every target where the existential ranges over <c>owl:Thing</c>, and otherwise exactly the derived members of the filler class.</summary>
    /// <param name="source">The existential membership source.</param>
    /// <param name="relations">The completed relations.</param>
    /// <param name="deltaSize">The domain size.</param>
    /// <param name="membershipsToAppendTo">The positive membership table.</param>
    /// <returns><see langword="true"/> when the source derived a new membership.</returns>
    private static bool ApplyExistentialSource(ToldGroundWitnessExistentialSource source, ToldGroundWitnessRelations relations, int deltaSize, bool[] membershipsToAppendTo)
    {
        int stride = deltaSize * deltaSize;
        bool derived = false;
        for(int subject = 0; subject < deltaSize; subject++)
        {
            if(membershipsToAppendTo[(source.Target * deltaSize) + subject])
            {
                continue;
            }

            for(int target = 0; target < deltaSize; target++)
            {
                if(!relations.Edges[(source.Role * stride) + (subject * deltaSize) + target])
                {
                    continue;
                }

                if(source.Filler >= 0 && !membershipsToAppendTo[(source.Filler * deltaSize) + target])
                {
                    continue;
                }

                membershipsToAppendTo[(source.Target * deltaSize) + subject] = true;
                derived = true;
                break;
            }
        }

        return derived;
    }

    /// <summary>
    /// The whole-module certificate pass: every axiom must classify into the
    /// admitted shape set, the described model is then constructed over the told
    /// carriers, the completed edges, and the class least fixpoint, and EVERY
    /// axiom is finally re-checked against the finished structure. Whole-module
    /// admission is mandatory here rather than monotone, because satisfying a
    /// subset says nothing about the module: an unrecognized axiom leaves the
    /// face silent. The construction only proposes; a failed check silences the
    /// face rather than refuting the module or repairing the structure.
    /// </summary>
    /// <param name="module">The module to certify.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="relations">The completed relations.</param>
    /// <returns><see langword="true"/> when the constructed model satisfies every axiom.</returns>
    private static bool IsDescribedModel(ReasoningModule module, ToldGroundWitnessGround ground, ToldGroundWitnessRelations relations)
    {
        ToldGroundWitnessShape[] shapes = new ToldGroundWitnessShape[module.Axioms.Count];
        for(int index = 0; index < module.Axioms.Count; index++)
        {
            shapes[index] = Classify(module.Axioms[index]);
            if(shapes[index] == ToldGroundWitnessShape.Unadmitted)
            {
                return false;
            }
        }

        ToldGroundWitnessModel model = new(
            relations.DeltaSize,
            ground.Classes.Count,
            new bool[ground.Classes.Count * relations.DeltaSize],
            relations,
            ground);
        List<ToldGroundWitnessSeedRule> rules = [];
        for(int index = 0; index < module.Axioms.Count; index++)
        {
            SeedClassTable(module.Axioms[index], model, rules);
        }

        if(!TryPropagateClassTable(rules, model))
        {
            return false;
        }

        for(int index = 0; index < module.Axioms.Count; index++)
        {
            if(!IsSatisfied(module.Axioms[index], shapes[index], model))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The whole-module admission classifier: a positive whitelist answering the
    /// shape the verification pass dispatches on, and
    /// <see cref="ToldGroundWitnessShape.Unadmitted"/> for everything else.
    /// Ontology imports pass through as non-logical markers: a
    /// <see cref="ReasoningModule"/> is by contract the axiom set the caller
    /// intends to be reasoned over, closed as given, with any
    /// <c>owl:imports</c> closure resolved by the caller before the module is
    /// constructed. Sameness axioms, every property characteristic, every
    /// cardinality, complement, universal, value, and self restriction, every
    /// role-hierarchy and role-algebra axiom, and every data-side axiom are
    /// outside the whitelist as well.
    /// </summary>
    /// <param name="axiom">The axiom to classify.</param>
    /// <returns>The admitted shape, or <see cref="ToldGroundWitnessShape.Unadmitted"/>.</returns>
    private static ToldGroundWitnessShape Classify(OwlAxiom axiom)
    {
        return axiom switch
        {
            OwlDeclarationAxiom or OwlAnnotationAssertionAxiom or OwlSubAnnotationPropertyOfAxiom
                or OwlAnnotationPropertyDomainAxiom or OwlAnnotationPropertyRangeAxiom or OwlImportAxiom => ToldGroundWitnessShape.NonLogical,
            OwlClassAssertionAxiom assertion => assertion.Individual is NamedNode or BlankNode && IsAdmissible(assertion.Class)
                ? ToldGroundWitnessShape.ClassAssertion
                : ToldGroundWitnessShape.Unadmitted,
            OwlObjectPropertyAssertionAxiom assertion => assertion.Source is NamedNode or BlankNode && assertion.Target is NamedNode or BlankNode
                ? ToldGroundWitnessShape.ObjectPropertyAssertion
                : ToldGroundWitnessShape.Unadmitted,
            OwlSubClassOfAxiom subClass => IsAdmissible(subClass.SubClass) && IsAdmissible(subClass.SuperClass)
                ? ToldGroundWitnessShape.SubClassOf
                : ToldGroundWitnessShape.Unadmitted,
            OwlEquivalentClassesAxiom equivalent => IsAdmissible(equivalent.First) && IsAdmissible(equivalent.Second)
                ? ToldGroundWitnessShape.EquivalentClasses
                : ToldGroundWitnessShape.Unadmitted,
            OwlDisjointClassesAxiom disjoint => AreAdmissible(disjoint.Operands)
                ? ToldGroundWitnessShape.DisjointClasses
                : ToldGroundWitnessShape.Unadmitted,
            OwlObjectPropertyDomainAxiom { Property: OwlObjectPropertyReference } domain => IsAdmissible(domain.Domain)
                ? ToldGroundWitnessShape.ObjectPropertyDomain
                : ToldGroundWitnessShape.Unadmitted,
            OwlObjectPropertyRangeAxiom { Property: OwlObjectPropertyReference } range => IsAdmissible(range.Range)
                ? ToldGroundWitnessShape.ObjectPropertyRange
                : ToldGroundWitnessShape.Unadmitted,
            OwlInverseObjectPropertiesAxiom { First: OwlObjectPropertyReference, Second: OwlObjectPropertyReference } => ToldGroundWitnessShape.InverseObjectProperties,
            OwlDifferentIndividualsAxiom different => AreIndividuals(different.Individuals)
                ? ToldGroundWitnessShape.DifferentIndividuals
                : ToldGroundWitnessShape.Unadmitted,
            _ => ToldGroundWitnessShape.Unadmitted,
        };
    }

    /// <summary>
    /// Whether a class expression lies inside the evaluable grammar: named
    /// classes including the two semantics-fixed constants, enumerations of
    /// individual terms, existentials over plain roles, and intersections and
    /// unions over admitted operands — every form MONOTONE in the class table,
    /// so the construction's fixpoint is a least fixpoint. The walk is an
    /// explicit stack; complement, universal, value, self, cardinality, inline
    /// inverse, and every data-side form is outside the grammar.
    /// </summary>
    /// <param name="root">The class expression.</param>
    /// <returns><see langword="true"/> when every position is admitted.</returns>
    private static bool IsAdmissible(OwlClassExpression root)
    {
        Stack<OwlClassExpression> work = new();
        work.Push(root);
        while(work.Count > 0)
        {
            switch(work.Pop())
            {
                case(OwlClassReference):
                {
                    break;
                }
                case(OwlObjectOneOf oneOf):
                {
                    if(!AreIndividuals(oneOf.Individuals))
                    {
                        return false;
                    }

                    break;
                }
                case(OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference } existential):
                {
                    work.Push(existential.Filler);
                    break;
                }
                case(OwlObjectIntersectionOf intersection):
                {
                    if(intersection.Operands.Count == 0)
                    {
                        return false;
                    }

                    for(int index = 0; index < intersection.Operands.Count; index++)
                    {
                        work.Push(intersection.Operands[index]);
                    }

                    break;
                }
                case(OwlObjectUnionOf union):
                {
                    if(union.Operands.Count == 0)
                    {
                        return false;
                    }

                    for(int index = 0; index < union.Operands.Count; index++)
                    {
                        work.Push(union.Operands[index]);
                    }

                    break;
                }
                default:
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether every operand of a list lies inside the evaluable grammar.</summary>
    /// <param name="operands">The operands to admit.</param>
    /// <returns><see langword="true"/> when every operand is admitted.</returns>
    private static bool AreAdmissible(IReadOnlyList<OwlClassExpression> operands)
    {
        for(int index = 0; index < operands.Count; index++)
        {
            if(!IsAdmissible(operands[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether every term of a list denotes an individual — a named or an anonymous one; a literal denotes a data value the constructed domain does not hold.</summary>
    /// <param name="terms">The terms to admit.</param>
    /// <returns><see langword="true"/> when every term is an individual.</returns>
    private static bool AreIndividuals(IReadOnlyList<RdfTerm> terms)
    {
        for(int index = 0; index < terms.Count; index++)
        {
            if(terms[index] is not NamedNode and not BlankNode)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Seeds the class table off one axiom and collects the propagation rules it
    /// carries: a told typing puts its term into a named class, a told domain or
    /// range puts every source or target of the role's COMPLETED extension into
    /// a named class, and a told subclass or equivalence with a named side
    /// carries a rule whose left side flows into that class — the general left
    /// side, so a universal-class left side seeds the whole domain rather than
    /// nothing.
    /// </summary>
    /// <param name="axiom">The axiom to seed from.</param>
    /// <param name="model">The model under construction.</param>
    /// <param name="rulesToAppendTo">The propagation-rule accumulator.</param>
    private static void SeedClassTable(OwlAxiom axiom, ToldGroundWitnessModel model, List<ToldGroundWitnessSeedRule> rulesToAppendTo)
    {
        switch(axiom)
        {
            case(OwlClassAssertionAxiom { Class: OwlClassReference asserted } assertion):
            {
                if(TryClassIndex(model, asserted, out int assertedClass) && TryCarrierIndex(model.Ground, assertion.Individual, out int term))
                {
                    model.Classes[(assertedClass * model.DeltaSize) + term] = true;
                }

                break;
            }
            case(OwlObjectPropertyDomainAxiom { Property: OwlObjectPropertyReference domainRole, Domain: OwlClassReference domainClass }):
            {
                SeedRoleEnds(model, domainRole.Named.Iri, domainClass, sources: true);
                break;
            }
            case(OwlObjectPropertyRangeAxiom { Property: OwlObjectPropertyReference rangeRole, Range: OwlClassReference rangeClass }):
            {
                SeedRoleEnds(model, rangeRole.Named.Iri, rangeClass, sources: false);
                break;
            }
            case(OwlSubClassOfAxiom subClass):
            {
                if(subClass.SuperClass is OwlClassReference super && TryClassIndex(model, super, out int superClass))
                {
                    rulesToAppendTo.Add(new ToldGroundWitnessSeedRule(subClass.SubClass, superClass));
                }

                break;
            }
            case(OwlEquivalentClassesAxiom equivalent):
            {
                if(equivalent.First is OwlClassReference first && TryClassIndex(model, first, out int firstClass))
                {
                    rulesToAppendTo.Add(new ToldGroundWitnessSeedRule(equivalent.Second, firstClass));
                }

                if(equivalent.Second is OwlClassReference second && TryClassIndex(model, second, out int secondClass))
                {
                    rulesToAppendTo.Add(new ToldGroundWitnessSeedRule(equivalent.First, secondClass));
                }

                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Seeds one named class with the sources or the targets of a role's completed extension — the told domain and range constraints' forced memberships.</summary>
    /// <param name="model">The model under construction.</param>
    /// <param name="role">The constrained role's IRI.</param>
    /// <param name="constrained">The named class the ends are confined to.</param>
    /// <param name="sources">Whether the sources are seeded; the targets otherwise.</param>
    private static void SeedRoleEnds(ToldGroundWitnessModel model, Utf8String role, OwlClassReference constrained, bool sources)
    {
        if(!TryClassIndex(model, constrained, out int constrainedClass) || !model.Ground.RoleIndices.TryGetValue(role, out int roleIndex))
        {
            return;
        }

        int stride = model.DeltaSize * model.DeltaSize;
        for(int source = 0; source < model.DeltaSize; source++)
        {
            for(int target = 0; target < model.DeltaSize; target++)
            {
                if(model.Relations.Edges[(roleIndex * stride) + (source * model.DeltaSize) + target])
                {
                    model.Classes[(constrainedClass * model.DeltaSize) + (sources ? source : target)] = true;
                }
            }
        }
    }

    /// <summary>Reads a class reference's fixpoint variable; the two semantics-fixed constants have none, their extensions being pinned to the whole domain and the empty set.</summary>
    /// <param name="model">The model under construction.</param>
    /// <param name="reference">The class reference.</param>
    /// <param name="index">The class index; <c>-1</c> for a pinned constant or an unharvested class.</param>
    /// <returns><see langword="true"/> on a fixpoint variable.</returns>
    private static bool TryClassIndex(ToldGroundWitnessModel model, OwlClassReference reference, out int index)
    {
        if(reference.Class.Iri.Equals(OwlVocabulary.Thing)
            || reference.Class.Iri.Equals(OwlVocabulary.Nothing)
            || !model.Ground.ClassIndices.TryGetValue(reference.Class.Iri, out index))
        {
            index = -1;

            return false;
        }

        return true;
    }

    /// <summary>Runs the bounded worklist over the subclass and equivalence rules to the class table's LEAST FIXPOINT: each rule evaluates its left side against the current table and unions the result into its named target, and any addition re-offers every rule. Every derivation adds a membership no rule retracts and the table is finite, so the loop terminates; the fixpoint is unique and independent of rule order.</summary>
    /// <param name="rules">The propagation rules.</param>
    /// <param name="model">The model under construction.</param>
    /// <returns><see langword="true"/> when the fixpoint completed; <see langword="false"/> when a left side fell outside the evaluable grammar.</returns>
    private static bool TryPropagateClassTable(List<ToldGroundWitnessSeedRule> rules, ToldGroundWitnessModel model)
    {
        bool[] queued = new bool[rules.Count];
        Queue<int> work = new();
        for(int index = 0; index < rules.Count; index++)
        {
            queued[index] = true;
            work.Enqueue(index);
        }

        while(work.Count > 0)
        {
            int index = work.Dequeue();
            queued[index] = false;
            ToldGroundWitnessSeedRule rule = rules[index];
            if(!TryEvaluate(rule.Source, model, out bool[]? extension))
            {
                return false;
            }

            bool derived = false;
            for(int element = 0; element < model.DeltaSize; element++)
            {
                if(extension[element] && !model.Classes[(rule.Target * model.DeltaSize) + element])
                {
                    model.Classes[(rule.Target * model.DeltaSize) + element] = true;
                    derived = true;
                }
            }

            if(!derived)
            {
                continue;
            }

            for(int other = 0; other < rules.Count; other++)
            {
                if(!queued[other])
                {
                    queued[other] = true;
                    work.Enqueue(other);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Evaluates one admissible class expression against the constructed
    /// structure. The expression is first linearized breadth-first into a node
    /// list, where every child necessarily follows its parent, and the list is
    /// then folded from the end, so each node reads its already-evaluated
    /// children — a post-order evaluation with no recursion anywhere.
    /// <c>owl:Thing</c> reads the whole domain and <c>owl:Nothing</c> the empty
    /// set; a named class reads its fixpoint variable, and a class the harvest
    /// never saw denotes the empty set.
    /// </summary>
    /// <param name="root">The expression to evaluate.</param>
    /// <param name="model">The constructed structure.</param>
    /// <param name="extension">The evaluated extension; <see langword="null"/> when a node fell outside the evaluable grammar.</param>
    /// <returns><see langword="true"/> when the expression evaluated.</returns>
    private static bool TryEvaluate(OwlClassExpression root, ToldGroundWitnessModel model, [NotNullWhen(true)] out bool[]? extension)
    {
        extension = null;
        List<OwlClassExpression> nodes = [root];
        List<int> firstChild = [0];
        List<int> childCount = [0];
        int scan = 0;
        while(scan < nodes.Count)
        {
            int start = nodes.Count;
            switch(nodes[scan])
            {
                case(OwlObjectSomeValuesFrom existential):
                {
                    AppendChild(nodes, firstChild, childCount, existential.Filler);
                    break;
                }
                case(OwlObjectIntersectionOf intersection):
                {
                    for(int index = 0; index < intersection.Operands.Count; index++)
                    {
                        AppendChild(nodes, firstChild, childCount, intersection.Operands[index]);
                    }

                    break;
                }
                case(OwlObjectUnionOf union):
                {
                    for(int index = 0; index < union.Operands.Count; index++)
                    {
                        AppendChild(nodes, firstChild, childCount, union.Operands[index]);
                    }

                    break;
                }
                default:
                {
                    break;
                }
            }

            firstChild[scan] = start;
            childCount[scan] = nodes.Count - start;
            scan++;
        }

        bool[][] results = new bool[nodes.Count][];
        for(int index = nodes.Count - 1; index >= 0; index--)
        {
            if(!TryEvaluateNode(nodes[index], firstChild[index], childCount[index], results, model, out bool[]? value))
            {
                return false;
            }

            results[index] = value;
        }

        extension = results[0];

        return true;
    }

    /// <summary>Appends one child node to the linearization, reserving its own child range for the scan to fill.</summary>
    /// <param name="nodesToAppendTo">The linearized node list.</param>
    /// <param name="firstChildToAppendTo">The per-node child-range starts.</param>
    /// <param name="childCountToAppendTo">The per-node child-range lengths.</param>
    /// <param name="child">The child expression.</param>
    private static void AppendChild(List<OwlClassExpression> nodesToAppendTo, List<int> firstChildToAppendTo, List<int> childCountToAppendTo, OwlClassExpression child)
    {
        nodesToAppendTo.Add(child);
        firstChildToAppendTo.Add(0);
        childCountToAppendTo.Add(0);
    }

    /// <summary>Evaluates one linearized node against its already-evaluated children.</summary>
    /// <param name="node">The node to evaluate.</param>
    /// <param name="firstChild">The node's first child index.</param>
    /// <param name="childCount">The node's child count.</param>
    /// <param name="results">The per-node extensions, filled from the end.</param>
    /// <param name="model">The constructed structure.</param>
    /// <param name="value">The node's extension; <see langword="null"/> outside the evaluable grammar.</param>
    /// <returns><see langword="true"/> when the node evaluated.</returns>
    private static bool TryEvaluateNode(OwlClassExpression node, int firstChild, int childCount, bool[][] results, ToldGroundWitnessModel model, [NotNullWhen(true)] out bool[]? value)
    {
        value = null;
        switch(node)
        {
            case(OwlClassReference reference):
            {
                value = ReadClassExtension(reference, model);

                return true;
            }
            case(OwlObjectOneOf oneOf):
            {
                bool[] members = new bool[model.DeltaSize];
                for(int index = 0; index < oneOf.Individuals.Count; index++)
                {
                    if(!TryCarrierIndex(model.Ground, oneOf.Individuals[index], out int member))
                    {
                        return false;
                    }

                    members[member] = true;
                }

                value = members;

                return true;
            }
            case(OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference role }):
            {
                value = ReadExistentialExtension(role.Named.Iri, results[firstChild], model);

                return true;
            }
            case(OwlObjectIntersectionOf):
            {
                bool[] intersection = new bool[model.DeltaSize];
                for(int element = 0; element < model.DeltaSize; element++)
                {
                    intersection[element] = true;
                }

                for(int child = 0; child < childCount; child++)
                {
                    for(int element = 0; element < model.DeltaSize; element++)
                    {
                        intersection[element] = intersection[element] && results[firstChild + child][element];
                    }
                }

                value = intersection;

                return true;
            }
            case(OwlObjectUnionOf):
            {
                bool[] union = new bool[model.DeltaSize];
                for(int child = 0; child < childCount; child++)
                {
                    for(int element = 0; element < model.DeltaSize; element++)
                    {
                        union[element] = union[element] || results[firstChild + child][element];
                    }
                }

                value = union;

                return true;
            }
            default:
            {
                return false;
            }
        }
    }

    /// <summary>Reads one class reference's extension: the whole domain for <c>owl:Thing</c>, the empty set for <c>owl:Nothing</c> and for a class the harvest never saw, and the fixpoint variable otherwise.</summary>
    /// <param name="reference">The class reference.</param>
    /// <param name="model">The constructed structure.</param>
    /// <returns>The extension.</returns>
    private static bool[] ReadClassExtension(OwlClassReference reference, ToldGroundWitnessModel model)
    {
        bool[] extension = new bool[model.DeltaSize];
        if(reference.Class.Iri.Equals(OwlVocabulary.Thing))
        {
            for(int element = 0; element < model.DeltaSize; element++)
            {
                extension[element] = true;
            }

            return extension;
        }

        if(reference.Class.Iri.Equals(OwlVocabulary.Nothing) || !model.Ground.ClassIndices.TryGetValue(reference.Class.Iri, out int index))
        {
            return extension;
        }

        for(int element = 0; element < model.DeltaSize; element++)
        {
            extension[element] = model.Classes[(index * model.DeltaSize) + element];
        }

        return extension;
    }

    /// <summary>Reads one existential's extension: the sources holding a completed edge of the role into the filler's extension.</summary>
    /// <param name="role">The existential's role IRI.</param>
    /// <param name="filler">The filler's extension.</param>
    /// <param name="model">The constructed structure.</param>
    /// <returns>The extension.</returns>
    private static bool[] ReadExistentialExtension(Utf8String role, bool[] filler, ToldGroundWitnessModel model)
    {
        bool[] extension = new bool[model.DeltaSize];
        if(!model.Ground.RoleIndices.TryGetValue(role, out int index))
        {
            return extension;
        }

        int stride = model.DeltaSize * model.DeltaSize;
        for(int source = 0; source < model.DeltaSize; source++)
        {
            for(int target = 0; target < model.DeltaSize; target++)
            {
                if(filler[target] && model.Relations.Edges[(index * stride) + (source * model.DeltaSize) + target])
                {
                    extension[source] = true;
                    break;
                }
            }
        }

        return extension;
    }

    /// <summary>
    /// The verification pass over one axiom: the Direct-Semantics satisfaction
    /// condition of its shape, transcribed onto the constructed finite
    /// structure. The told inverse axioms are re-checked despite the completion
    /// having built them, so the verifier never trusts the generator, and every
    /// equivalence is checked independently as an equality of extensions rather
    /// than one overriding another. The default arm is a SILENCE: a shape the
    /// classifier admitted and this pass does not know must never read as
    /// satisfied.
    /// </summary>
    /// <param name="axiom">The axiom to verify.</param>
    /// <param name="shape">The classifier's shape for the axiom.</param>
    /// <param name="model">The constructed structure.</param>
    /// <returns><see langword="true"/> when the structure satisfies the axiom.</returns>
    private static bool IsSatisfied(OwlAxiom axiom, ToldGroundWitnessShape shape, ToldGroundWitnessModel model)
    {
        switch(shape)
        {
            case(ToldGroundWitnessShape.NonLogical):
            {
                return true;
            }
            case(ToldGroundWitnessShape.ClassAssertion):
            {
                OwlClassAssertionAxiom assertion = (OwlClassAssertionAxiom)axiom;

                return TryEvaluate(assertion.Class, model, out bool[]? asserted)
                    && TryCarrierIndex(model.Ground, assertion.Individual, out int term)
                    && asserted[term];
            }
            case(ToldGroundWitnessShape.ObjectPropertyAssertion):
            {
                OwlObjectPropertyAssertionAxiom assertion = (OwlObjectPropertyAssertionAxiom)axiom;

                return TryCarrierIndex(model.Ground, assertion.Source, out int source)
                    && TryCarrierIndex(model.Ground, assertion.Target, out int target)
                    && HasEdge(model, assertion.Property.Iri, source, target);
            }
            case(ToldGroundWitnessShape.SubClassOf):
            {
                OwlSubClassOfAxiom subClass = (OwlSubClassOfAxiom)axiom;

                return TryEvaluate(subClass.SubClass, model, out bool[]? sub)
                    && TryEvaluate(subClass.SuperClass, model, out bool[]? super)
                    && IsSubset(sub, super, model.DeltaSize);
            }
            case(ToldGroundWitnessShape.EquivalentClasses):
            {
                OwlEquivalentClassesAxiom equivalent = (OwlEquivalentClassesAxiom)axiom;

                return TryEvaluate(equivalent.First, model, out bool[]? first)
                    && TryEvaluate(equivalent.Second, model, out bool[]? second)
                    && IsSubset(first, second, model.DeltaSize)
                    && IsSubset(second, first, model.DeltaSize);
            }
            case(ToldGroundWitnessShape.DisjointClasses):
            {
                return AreDisjoint((OwlDisjointClassesAxiom)axiom, model);
            }
            case(ToldGroundWitnessShape.ObjectPropertyDomain):
            {
                OwlObjectPropertyDomainAxiom domain = (OwlObjectPropertyDomainAxiom)axiom;

                return TryEvaluate(domain.Domain, model, out bool[]? confined)
                    && AreRoleEndsConfined(model, domain.Property.Property.Iri, confined, sources: true);
            }
            case(ToldGroundWitnessShape.ObjectPropertyRange):
            {
                OwlObjectPropertyRangeAxiom range = (OwlObjectPropertyRangeAxiom)axiom;

                return TryEvaluate(range.Range, model, out bool[]? confined)
                    && AreRoleEndsConfined(model, range.Property.Property.Iri, confined, sources: false);
            }
            case(ToldGroundWitnessShape.InverseObjectProperties):
            {
                OwlInverseObjectPropertiesAxiom inverse = (OwlInverseObjectPropertiesAxiom)axiom;

                return AreConverse(model, inverse.First.Property.Iri, inverse.Second.Property.Iri);
            }
            case(ToldGroundWitnessShape.DifferentIndividuals):
            {
                return AreDistinct((OwlDifferentIndividualsAxiom)axiom, model);
            }
            default:
            {
                return false;
            }
        }
    }

    /// <summary>Whether the constructed structure holds one ordered edge of a role.</summary>
    /// <param name="model">The constructed structure.</param>
    /// <param name="role">The role IRI.</param>
    /// <param name="source">The source element.</param>
    /// <param name="target">The target element.</param>
    /// <returns><see langword="true"/> when the edge is held.</returns>
    private static bool HasEdge(ToldGroundWitnessModel model, Utf8String role, int source, int target)
    {
        if(!model.Ground.RoleIndices.TryGetValue(role, out int index))
        {
            return false;
        }

        int stride = model.DeltaSize * model.DeltaSize;

        return model.Relations.Edges[(index * stride) + (source * model.DeltaSize) + target];
    }

    /// <summary>Whether one extension is contained in another.</summary>
    /// <param name="sub">The contained extension.</param>
    /// <param name="super">The containing extension.</param>
    /// <param name="deltaSize">The domain size.</param>
    /// <returns><see langword="true"/> on containment.</returns>
    private static bool IsSubset(bool[] sub, bool[] super, int deltaSize)
    {
        for(int element = 0; element < deltaSize; element++)
        {
            if(sub[element] && !super[element])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether every unordered pair of a told disjointness axiom's operands has an empty intersection in the constructed structure.</summary>
    /// <param name="axiom">The told disjointness axiom.</param>
    /// <param name="model">The constructed structure.</param>
    /// <returns><see langword="true"/> when every pair is disjoint.</returns>
    private static bool AreDisjoint(OwlDisjointClassesAxiom axiom, ToldGroundWitnessModel model)
    {
        bool[][] extensions = new bool[axiom.Operands.Count][];
        for(int index = 0; index < axiom.Operands.Count; index++)
        {
            if(!TryEvaluate(axiom.Operands[index], model, out bool[]? extension))
            {
                return false;
            }

            extensions[index] = extension;
        }

        for(int first = 0; first < extensions.Length; first++)
        {
            for(int second = first + 1; second < extensions.Length; second++)
            {
                for(int element = 0; element < model.DeltaSize; element++)
                {
                    if(extensions[first][element] && extensions[second][element])
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>Whether every source or every target of a role's completed extension lies inside a confining extension — the domain and range satisfaction conditions.</summary>
    /// <param name="model">The constructed structure.</param>
    /// <param name="role">The constrained role's IRI.</param>
    /// <param name="confined">The confining extension.</param>
    /// <param name="sources">Whether the sources are checked; the targets otherwise.</param>
    /// <returns><see langword="true"/> when every checked end is confined.</returns>
    private static bool AreRoleEndsConfined(ToldGroundWitnessModel model, Utf8String role, bool[] confined, bool sources)
    {
        if(!model.Ground.RoleIndices.TryGetValue(role, out int index))
        {
            return true;
        }

        int stride = model.DeltaSize * model.DeltaSize;
        for(int source = 0; source < model.DeltaSize; source++)
        {
            for(int target = 0; target < model.DeltaSize; target++)
            {
                if(model.Relations.Edges[(index * stride) + (source * model.DeltaSize) + target] && !confined[sources ? source : target])
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether two roles' completed extensions are exact converses of one another — the inverse-properties satisfaction condition, re-checked rather than assumed from the completion that built it.</summary>
    /// <param name="model">The constructed structure.</param>
    /// <param name="first">The first role's IRI.</param>
    /// <param name="second">The second role's IRI.</param>
    /// <returns><see langword="true"/> when each role holds exactly the other's reversed pairs.</returns>
    private static bool AreConverse(ToldGroundWitnessModel model, Utf8String first, Utf8String second)
    {
        if(!model.Ground.RoleIndices.TryGetValue(first, out int firstRole) || !model.Ground.RoleIndices.TryGetValue(second, out int secondRole))
        {
            return false;
        }

        int stride = model.DeltaSize * model.DeltaSize;
        for(int source = 0; source < model.DeltaSize; source++)
        {
            for(int target = 0; target < model.DeltaSize; target++)
            {
                bool forward = model.Relations.Edges[(firstRole * stride) + (source * model.DeltaSize) + target];
                bool backward = model.Relations.Edges[(secondRole * stride) + (target * model.DeltaSize) + source];
                if(forward != backward)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether a told distinctness axiom's terms denote pairwise distinct carriers — satisfied for free where the terms are pairwise distinct keys, and failed where one term repeats, which routes the module to silence rather than to a verdict.</summary>
    /// <param name="axiom">The told distinctness axiom.</param>
    /// <param name="model">The constructed structure.</param>
    /// <returns><see langword="true"/> when every pair denotes distinct carriers.</returns>
    private static bool AreDistinct(OwlDifferentIndividualsAxiom axiom, ToldGroundWitnessModel model)
    {
        for(int first = 0; first < axiom.Individuals.Count; first++)
        {
            if(!TryCarrierIndex(model.Ground, axiom.Individuals[first], out int left))
            {
                return false;
            }

            for(int second = first + 1; second < axiom.Individuals.Count; second++)
            {
                if(!TryCarrierIndex(model.Ground, axiom.Individuals[second], out int right) || left == right)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
