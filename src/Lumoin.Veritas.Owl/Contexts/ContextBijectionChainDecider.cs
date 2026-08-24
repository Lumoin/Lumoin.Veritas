using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>The Shape B clash reason family — the bijection-chain counterpart of the spy-point clash reasons: two stable leading identifiers the statistics assembly and the battery discriminate on.</summary>
internal static class BijectionChainClashReasons
{
    /// <summary>The arithmetic clash over the propagated size variables: one equality class is forced to two different sizes, or a forced size falls outside the class's told bounds.</summary>
    /// <param name="className">A named class of the impossible equality class.</param>
    /// <returns>The named reason.</returns>
    public static string ForcedConstantConflict(Utf8String className)
    {
        return $"BijectionChainForcedConstantConflict({className})";
    }

    /// <summary>The asserted-conjunct clash: a told class assertion whose class expression carries a top-level complement of <c>owl:Thing</c>, an extension that is empty in every interpretation while the assertion demands a member.</summary>
    /// <param name="subject">The individual term the unsatisfiable class was asserted on.</param>
    /// <returns>The named reason.</returns>
    public static string UnsatisfiableAssertedConjunct(Utf8String subject)
    {
        return $"BijectionChainUnsatisfiableAssertedConjunct({subject})";
    }
}

/// <summary>
/// The Shape B window measurement the census-first recognizer's
/// pre-clausification pass reads on every bijection-chain-jurisdiction module —
/// computed with the class deduplication applied BEFORE any boundary
/// comparison, so the battery's near-miss rows can pin the measured quantity
/// independently of the comparison's outcome.
/// </summary>
/// <param name="ClassCount">The recognized size variables — the distinct named classes carrying a told constraint source, deduplicated by class identity; zero when no source was recognized.</param>
/// <param name="ConstraintCount">The collected constraint sources: the told constants, equalities, sums, products, upper bounds, lower bounds, and the outright asserted-conjunct clash.</param>
/// <param name="ClassSilences">One when <see cref="ClassCount"/> exceeded <see cref="ContextBijectionChainDecider.BijectionChainClassBound"/> — a named silence, never a verdict over an unpropagated variable set; zero otherwise.</param>
internal readonly record struct BijectionChainWindow(
    int ClassCount,
    int ConstraintCount,
    int ClassSilences)
{
    /// <summary>The empty window: no bijection-chain constraint source was recognized.</summary>
    public static BijectionChainWindow Empty => default;
}

/// <summary>The Shape B decider's outcome: the propagated refutation or the certified consistency when every jurisdiction condition held inside the window, and the window measurement the census carries unconditionally.</summary>
/// <param name="Consistent">The verdict — <see langword="false"/> for the propagation clash, <see langword="true"/> for a certificate route — or <see langword="null"/> when both faces are silent on the module.</param>
/// <param name="Window">The window measurement.</param>
internal readonly record struct BijectionChainOutcome(bool? Consistent, BijectionChainWindow Window)
{
    /// <summary>The named clash reason on a refutation; <see langword="null"/> on every other outcome.</summary>
    public string? ClashReason { get; init; }

    /// <summary>The named certificate route on a certification — <see cref="ContextBijectionChainDecider.VacuityCertificate"/> or <see cref="ContextBijectionChainDecider.GroundedTowerCertificate"/>; <see langword="null"/> on every other outcome.</summary>
    public string? CertificateRoute { get; init; }

    /// <summary>The silent outcome carrying only the window measurement.</summary>
    /// <param name="window">The measured window.</param>
    /// <returns>The silent outcome.</returns>
    public static BijectionChainOutcome SilentWith(BijectionChainWindow window)
    {
        return new BijectionChainOutcome(null, window);
    }
}

/// <summary>
/// The enumeration-CSP habitat decider's bijection-chain cardinality faces
/// (faces ten and eleven): a tier-2 PROPAGATION over the told axiom surfaces of
/// a cardinality-arithmetic module — each named class carries a size variable,
/// told one-of enumerations and anchored fan-ins ground constants, told
/// functional-and-inverse-functional role pairs over paired existential
/// restrictions merge classes into equality classes, told disjoint unions add,
/// told fan-in cardinalities multiply, and subclass-position enumerations and
/// class assertions bound from above and below. A bounded worklist propagates
/// the collected sources to a fixpoint over at most a named window of
/// variables: no enumeration, no assignment vectors, no partition search.
/// The CLASH face refutes on an impossible state — two different forced sizes
/// in one equality class, a forced size outside its bounds, a negative sum
/// residue, a product with no cardinal solution, or a told assertion of an
/// empty conjunct — with a MONOTONE jurisdiction: unrecognized axioms are
/// IGNORED rather than rejecting the module, because extra axioms only shrink
/// the model class and can never rescue a refuted subset. The CERTIFY face is
/// the opposite discipline, a closed-world WHOLE-MODULE admission carrying two
/// certificate routes: the vacuity route, whose all-empty interpretation
/// satisfies every axiom of a whitelisted module, and the grounded-tower route,
/// whose canonical fiber model witnesses the diamond template when its level
/// constants multiply out. A whole-module-admitted module fitting neither
/// route's own list stays silent — consistent arithmetic alone certifies
/// nothing. Sound-or-silent and told-only throughout: saturation-derived facts
/// never feed either face. The variable ceiling is a named window constant;
/// outside it both faces are silent with the measured numbers already on the
/// record.
/// </summary>
internal static class ContextBijectionChainDecider
{
    /// <summary>
    /// The size-variable ceiling: the propagation runs exactly up to this many
    /// distinct named classes carrying a recognized constraint source and is
    /// SILENT above it. Derivation (engineering, with the cost formula the
    /// battery pins): every worklist step grounds one of at most sixteen
    /// equality classes or retires a constraint, so the fixpoint costs at most
    /// sixteen groundings times the collected constraint count, and the value
    /// matches the counting faces' shared sixteen ceiling — the
    /// counted-population, ground-clique, partition-anchor, gadget-atom,
    /// pair-assignment, and spy-point member bounds — so every counting-family
    /// pre-engine face carries one boundary discipline; the repairing face
    /// carries its own wider carrier, class, and role windows sized by its
    /// habitat. Collecting the told shapes is one linear pass bounded by the
    /// module's own axiom count rather than by this constant.
    /// </summary>
    public const int BijectionChainClassBound = 16;

    /// <summary>The certificate route name of the all-empty witness model: every named class denotes the empty set, every role the empty relation, and the whitelisted axioms all hold.</summary>
    public const string VacuityCertificate = "Vacuity";

    /// <summary>The certificate route name of the canonical fiber model: the diamond template's anchor, mid, and top levels are witnessed by an explicit element set whose level sizes multiply out exactly.</summary>
    public const string GroundedTowerCertificate = "GroundedTower";

    /// <summary>Measures the Shape B census window without deciding anything: the recognized size variables, the collected constraint sources, and the class-window silence the bound would charge — computed identically dark and lit, so the census ships unconditionally. No verdict is ever formed on this path.</summary>
    /// <param name="module">The module to measure.</param>
    /// <returns>The silent outcome carrying the measurement; all-zero when no constraint source was recognized.</returns>
    public static BijectionChainOutcome Measure(ReasoningModule module)
    {
        return TryCollectSources(module, out BijectionChainSources? sources)
            ? BijectionChainOutcome.SilentWith(MeasureWindow(sources))
            : BijectionChainOutcome.SilentWith(BijectionChainWindow.Empty);
    }

    /// <summary>
    /// Runs the bijection-chain faces in jurisdiction order: the told-shape
    /// collection and the bounded worklist propagation first, since a clash
    /// condemns the whole module and needs no admission, and the whole-module
    /// certificate pass only where the propagation stayed silent. The
    /// measurement lands first in every case, so a window silence still carries
    /// the numbers.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <returns>The outcome: the propagated refutation, a route certificate, or silence — each with its measurement.</returns>
    public static BijectionChainOutcome Run(ReasoningModule module)
    {
        BijectionChainWindow window = BijectionChainWindow.Empty;
        if(TryCollectSources(module, out BijectionChainSources? sources))
        {
            window = MeasureWindow(sources);
            if(window.ClassSilences > 0)
            {
                return BijectionChainOutcome.SilentWith(window);
            }

            if(TryRefute(sources, out string? clashReason))
            {
                return new BijectionChainOutcome(false, window)
                {
                    ClashReason = clashReason,
                };
            }
        }

        if(TryCertify(module, out string? certificateRoute))
        {
            return new BijectionChainOutcome(true, window)
            {
                CertificateRoute = certificateRoute,
            };
        }

        return BijectionChainOutcome.SilentWith(window);
    }

    /// <summary>One told size constant: an equality class is forced to an exact size.</summary>
    /// <param name="Class">The class variable index the constant was told at.</param>
    /// <param name="Value">The forced size.</param>
    private readonly record struct BijectionChainConstant(int Class, long Value);

    /// <summary>One told size equality: two class variables denote equinumerous sets in every model.</summary>
    /// <param name="First">The first class variable index.</param>
    /// <param name="Second">The second class variable index.</param>
    private readonly record struct BijectionChainEquality(int First, int Second);

    /// <summary>One told additive decomposition: a class variable's size is the sum of its pairwise-disjoint operands' sizes.</summary>
    /// <param name="Total">The class variable index of the union.</param>
    /// <param name="Operands">The class variable indices of the disjoint operands, deduplicated in first-seen order.</param>
    private readonly record struct BijectionChainSum(int Total, List<int> Operands);

    /// <summary>One told fiber product: a class variable's size is a told factor times another class variable's size.</summary>
    /// <param name="Product">The class variable index of the fibered class.</param>
    /// <param name="Factor">The told fiber size.</param>
    /// <param name="Operand">The class variable index of the base class.</param>
    private readonly record struct BijectionChainProduct(int Product, long Factor, int Operand);

    /// <summary>One told size bound: an upper or lower limit on a class variable's size.</summary>
    /// <param name="Class">The bounded class variable index.</param>
    /// <param name="Value">The told limit.</param>
    private readonly record struct BijectionChainBound(int Class, long Value);

    /// <summary>The collected constraint sources: the size variables in first-seen order, the derived constraints by kind, and the outright asserted-conjunct clash.</summary>
    /// <param name="Classes">The size variables — the distinct named classes carrying a source, in first-seen order.</param>
    /// <param name="Constants">The told size constants.</param>
    /// <param name="Equalities">The told size equalities.</param>
    /// <param name="Sums">The told additive decompositions.</param>
    /// <param name="Products">The told fiber products.</param>
    /// <param name="Uppers">The told upper bounds.</param>
    /// <param name="Lowers">The told lower bounds.</param>
    /// <param name="HasEmptyConjunct">Whether a told class assertion carried a top-level complement of <c>owl:Thing</c> — the outright clash route, decided before any propagation.</param>
    /// <param name="EmptyConjunctSubject">The individual term of the outright clash's assertion; the default value when no such assertion was told.</param>
    private sealed record BijectionChainSources(
        List<Utf8String> Classes,
        List<BijectionChainConstant> Constants,
        List<BijectionChainEquality> Equalities,
        List<BijectionChainSum> Sums,
        List<BijectionChainProduct> Products,
        List<BijectionChainBound> Uppers,
        List<BijectionChainBound> Lowers,
        bool HasEmptyConjunct,
        Utf8String EmptyConjunctSubject);

    /// <summary>One told existential subclass step: a named class is subsumed by an existential over a plain role into a named class — the premise shape a bijection pairs.</summary>
    /// <param name="Source">The subsumed named class.</param>
    /// <param name="Role">The plain role of the existential.</param>
    /// <param name="Target">The existential's named filler class.</param>
    private readonly record struct BijectionChainLink(Utf8String Source, Utf8String Role, Utf8String Target);

    /// <summary>One told enumeration definition: a named class is equivalent to a one-of of named individuals.</summary>
    /// <param name="Class">The defined named class.</param>
    /// <param name="Members">The distinct named members, deduplicated by individual identity in first-seen order.</param>
    private readonly record struct BijectionChainEnumeration(Utf8String Class, List<Utf8String> Members);

    /// <summary>One told union definition: a named class is equivalent to a union of named classes.</summary>
    /// <param name="Class">The defined named class.</param>
    /// <param name="Operands">The union's named operands, in told order.</param>
    private readonly record struct BijectionChainUnionDefinition(Utf8String Class, List<Utf8String> Operands);

    /// <summary>One told existential definition: a named class is equivalent to an existential over a plain role into a named class.</summary>
    /// <param name="Class">The defined named class.</param>
    /// <param name="Role">The plain role of the existential.</param>
    /// <param name="Filler">The existential's named filler class.</param>
    private readonly record struct BijectionChainExistentialDefinition(Utf8String Class, Utf8String Role, Utf8String Filler);

    /// <summary>One told exact-cardinality definition: a named class is equivalent to an unqualified exact cardinality over a plain role.</summary>
    /// <param name="Class">The defined named class.</param>
    /// <param name="Role">The counted plain role.</param>
    /// <param name="Cardinality">The told exact count.</param>
    private readonly record struct BijectionChainExactDefinition(Utf8String Class, Utf8String Role, int Cardinality);

    /// <summary>One told upper-bound source: a named class is subsumed by an enumeration of a known member count.</summary>
    /// <param name="Class">The bounded named class.</param>
    /// <param name="Members">The distinct member count the enumeration admits.</param>
    private readonly record struct BijectionChainUpperSource(Utf8String Class, int Members);

    /// <summary>The told facts one pass over the module's axioms collects, before any lemma derivation reads them.</summary>
    /// <param name="FunctionalRoles">The plain roles told functional.</param>
    /// <param name="InverseFunctionalRoles">The plain roles told inverse-functional.</param>
    /// <param name="InverseRoles">The told inverse-role relation over plain roles, recorded in both argument orders.</param>
    /// <param name="DifferentIndividuals">The told distinctness relation over named individuals, recorded in both directions.</param>
    /// <param name="DisjointClasses">The told disjointness relation over named classes, recorded in both directions.</param>
    /// <param name="Links">The told existential subclass steps.</param>
    /// <param name="Enumerations">The told enumeration definitions.</param>
    /// <param name="Unions">The told union definitions.</param>
    /// <param name="Existentials">The told existential definitions.</param>
    /// <param name="Exacts">The told exact-cardinality definitions.</param>
    /// <param name="Uppers">The told upper-bound sources.</param>
    /// <param name="AssertedClasses">The named classes some told assertion types an individual with, in first-seen order.</param>
    /// <param name="AssertedMembers">The distinct NAMED individuals told into each asserted class, in first-seen order.</param>
    private sealed record BijectionChainTold(
        HashSet<Utf8String> FunctionalRoles,
        HashSet<Utf8String> InverseFunctionalRoles,
        Dictionary<Utf8String, HashSet<Utf8String>> InverseRoles,
        Dictionary<Utf8String, HashSet<Utf8String>> DifferentIndividuals,
        Dictionary<Utf8String, HashSet<Utf8String>> DisjointClasses,
        List<BijectionChainLink> Links,
        List<BijectionChainEnumeration> Enumerations,
        List<BijectionChainUnionDefinition> Unions,
        List<BijectionChainExistentialDefinition> Existentials,
        List<BijectionChainExactDefinition> Exacts,
        List<BijectionChainUpperSource> Uppers,
        List<Utf8String> AssertedClasses,
        Dictionary<Utf8String, List<Utf8String>> AssertedMembers);

    /// <summary>Reads the window off the collected sources: the size variables, the constraint-source count, and the class-window silence the bound charges.</summary>
    /// <param name="sources">The collected sources.</param>
    /// <returns>The window measurement.</returns>
    private static BijectionChainWindow MeasureWindow(BijectionChainSources sources)
    {
        int constraints = sources.Constants.Count
            + sources.Equalities.Count
            + sources.Sums.Count
            + sources.Products.Count
            + sources.Uppers.Count
            + sources.Lowers.Count
            + (sources.HasEmptyConjunct ? 1 : 0);

        return new BijectionChainWindow(sources.Classes.Count, constraints, sources.Classes.Count > BijectionChainClassBound ? 1 : 0);
    }

    /// <summary>
    /// Collects the told shapes in ONE pass over the module's axioms and then
    /// derives the constraint sources from them. Every unrecognized axiom is
    /// IGNORED rather than rejecting the module — the refutation is monotone, so
    /// a clash over a recognized subset condemns the whole module and no
    /// closed-world admission is needed. Recognized shapes are structurally
    /// exact: named class references only, never <c>owl:Thing</c> and never
    /// <c>owl:Nothing</c> in a variable position, plain object-property
    /// references only, top-level restrictions only, object cardinalities only,
    /// and unqualified exact cardinalities only. The one rejection is the
    /// absence of any recognized source, which leaves nothing to propagate.
    /// </summary>
    /// <param name="module">The module to collect from.</param>
    /// <param name="sources">The collected sources; <see langword="null"/> when no source was recognized.</param>
    /// <returns><see langword="true"/> when at least one constraint source was recognized.</returns>
    private static bool TryCollectSources(ReasoningModule module, [NotNullWhen(true)] out BijectionChainSources? sources)
    {
        sources = null;

        BijectionChainTold told = new([], [], [], [], [], [], [], [], [], [], [], [], []);
        bool hasEmptyConjunct = false;
        Utf8String emptyConjunctSubject = default;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case(OwlObjectPropertyCharacteristicAxiom { Property: OwlObjectPropertyReference role } characteristic):
                {
                    CollectCharacteristic(characteristic.Characteristic, role.Named.Iri, told);
                    break;
                }
                case(OwlInverseObjectPropertiesAxiom { First: OwlObjectPropertyReference first, Second: OwlObjectPropertyReference second }):
                {
                    LinkPair(told.InverseRoles, first.Named.Iri, second.Named.Iri);
                    break;
                }
                case(OwlDifferentIndividualsAxiom different):
                {
                    CollectDistinctness(different, told);
                    break;
                }
                case(OwlDisjointClassesAxiom disjoint):
                {
                    CollectDisjointness(disjoint, told);
                    break;
                }
                case(OwlSubClassOfAxiom subClass):
                {
                    CollectLink(subClass, told);
                    CollectUpperSource(subClass, told);
                    break;
                }
                case(OwlEquivalentClassesAxiom equivalent):
                {
                    CollectDefinition(equivalent.First, equivalent.Second, told);
                    CollectDefinition(equivalent.Second, equivalent.First, told);
                    break;
                }
                case(OwlClassAssertionAxiom assertion):
                {
                    if(!hasEmptyConjunct && IsEmptyConjunctClass(assertion.Class) && TryReadIndividual(assertion.Individual, out Utf8String subject))
                    {
                        hasEmptyConjunct = true;
                        emptyConjunctSubject = subject;
                    }

                    CollectAssertion(assertion, told);
                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        List<Utf8String> classes = [];
        Dictionary<Utf8String, int> indices = [];
        List<BijectionChainConstant> constants = [];
        List<BijectionChainEquality> equalities = [];
        List<BijectionChainSum> sums = [];
        List<BijectionChainProduct> products = [];
        List<BijectionChainBound> uppers = [];
        List<BijectionChainBound> lowers = [];

        DeriveConstants(told, classes, indices, constants);
        DeriveEqualities(told, classes, indices, equalities);
        DeriveSums(told, classes, indices, sums);
        DeriveFanIns(told, classes, indices, constants, products);
        DeriveUppers(told, classes, indices, uppers);
        DeriveLowers(told, classes, indices, lowers);

        if(classes.Count == 0 && !hasEmptyConjunct)
        {
            return false;
        }

        sources = new BijectionChainSources(classes, constants, equalities, sums, products, uppers, lowers, hasEmptyConjunct, emptyConjunctSubject);

        return true;
    }

    /// <summary>Records a told functionality or inverse-functionality characteristic of a plain role; every other characteristic carries no premise this face reads.</summary>
    /// <param name="characteristic">The told characteristic.</param>
    /// <param name="role">The plain role the characteristic sits on.</param>
    /// <param name="told">The told-fact accumulator.</param>
    private static void CollectCharacteristic(OwlPropertyCharacteristic characteristic, Utf8String role, BijectionChainTold told)
    {
        if(characteristic == OwlPropertyCharacteristic.Functional)
        {
            told.FunctionalRoles.Add(role);

            return;
        }

        if(characteristic == OwlPropertyCharacteristic.InverseFunctional)
        {
            told.InverseFunctionalRoles.Add(role);
        }
    }

    /// <summary>Records a told distinctness axiom as unordered pairs over its NAMED members; an anonymous member contributes no pair, since the coverage rule counts named denotations.</summary>
    /// <param name="axiom">The told distinctness axiom.</param>
    /// <param name="told">The told-fact accumulator.</param>
    private static void CollectDistinctness(OwlDifferentIndividualsAxiom axiom, BijectionChainTold told)
    {
        for(int first = 0; first < axiom.Individuals.Count; first++)
        {
            if(axiom.Individuals[first] is not NamedNode left)
            {
                continue;
            }

            for(int second = first + 1; second < axiom.Individuals.Count; second++)
            {
                if(axiom.Individuals[second] is NamedNode right)
                {
                    LinkPair(told.DifferentIndividuals, left.Iri, right.Iri);
                }
            }
        }
    }

    /// <summary>Records a told disjointness axiom of any arity as unordered pairs over its named non-constant class operands — only TOLD links, never a transitive or derived disjointness.</summary>
    /// <param name="axiom">The told disjointness axiom.</param>
    /// <param name="told">The told-fact accumulator.</param>
    private static void CollectDisjointness(OwlDisjointClassesAxiom axiom, BijectionChainTold told)
    {
        for(int first = 0; first < axiom.Operands.Count; first++)
        {
            if(!TryReadClass(axiom.Operands[first], out Utf8String left))
            {
                continue;
            }

            for(int second = first + 1; second < axiom.Operands.Count; second++)
            {
                if(TryReadClass(axiom.Operands[second], out Utf8String right) && !right.Equals(left))
                {
                    LinkPair(told.DisjointClasses, left, right);
                }
            }
        }
    }

    /// <summary>Records a told existential subclass step: a named non-constant class subsumed by a TOP-LEVEL existential over a plain role into a named non-constant class. Any other spelling carries no bijection premise.</summary>
    /// <param name="axiom">The candidate subclass axiom.</param>
    /// <param name="told">The told-fact accumulator.</param>
    private static void CollectLink(OwlSubClassOfAxiom axiom, BijectionChainTold told)
    {
        if(!TryReadClass(axiom.SubClass, out Utf8String source)
            || axiom.SuperClass is not OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference role } existential
            || !TryReadClass(existential.Filler, out Utf8String target))
        {
            return;
        }

        told.Links.Add(new BijectionChainLink(source, role.Named.Iri, target));
    }

    /// <summary>
    /// Records a told upper-bound source: a named non-constant class subsumed by
    /// a TOP-LEVEL enumeration of named members, or by a TOP-LEVEL union whose
    /// operands are ALL singleton enumerations of named members. Members are
    /// deduplicated by individual identity, since colliding denotations only
    /// shrink the bounded set.
    /// </summary>
    /// <param name="axiom">The candidate subclass axiom.</param>
    /// <param name="told">The told-fact accumulator.</param>
    private static void CollectUpperSource(OwlSubClassOfAxiom axiom, BijectionChainTold told)
    {
        if(!TryReadClass(axiom.SubClass, out Utf8String bounded))
        {
            return;
        }

        HashSet<Utf8String> members = [];
        if(axiom.SuperClass is OwlObjectOneOf oneOf)
        {
            if(!TryReadNamedMembers(oneOf, members))
            {
                return;
            }

            told.Uppers.Add(new BijectionChainUpperSource(bounded, members.Count));

            return;
        }

        if(axiom.SuperClass is not OwlObjectUnionOf union || union.Operands.Count == 0)
        {
            return;
        }

        for(int index = 0; index < union.Operands.Count; index++)
        {
            if(union.Operands[index] is not OwlObjectOneOf operand || operand.Individuals.Count != 1 || !TryReadNamedMembers(operand, members))
            {
                return;
            }
        }

        told.Uppers.Add(new BijectionChainUpperSource(bounded, members.Count));
    }

    /// <summary>Records one told class definition, read in the given side order so a told equivalence is offered to the derivation in both argument orders: the defined side must be a named non-constant class, and the defining side one of the four recognized right-hand shapes.</summary>
    /// <param name="defined">The candidate defined side.</param>
    /// <param name="defining">The candidate defining side.</param>
    /// <param name="told">The told-fact accumulator.</param>
    private static void CollectDefinition(OwlClassExpression defined, OwlClassExpression defining, BijectionChainTold told)
    {
        if(!TryReadClass(defined, out Utf8String definedClass))
        {
            return;
        }

        switch(defining)
        {
            case(OwlObjectOneOf oneOf):
            {
                HashSet<Utf8String> distinct = [];
                List<Utf8String> members = [];
                if(TryReadNamedMemberList(oneOf, distinct, members))
                {
                    told.Enumerations.Add(new BijectionChainEnumeration(definedClass, members));
                }

                break;
            }
            case(OwlObjectUnionOf union):
            {
                List<Utf8String> operands = [];
                if(TryReadNamedOperands(union, operands))
                {
                    told.Unions.Add(new BijectionChainUnionDefinition(definedClass, operands));
                }

                break;
            }
            case(OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference role } existential):
            {
                if(TryReadClass(existential.Filler, out Utf8String filler))
                {
                    told.Existentials.Add(new BijectionChainExistentialDefinition(definedClass, role.Named.Iri, filler));
                }

                break;
            }
            case(OwlObjectCardinality { Kind: OwlCardinalityKind.Exact, Property: OwlObjectPropertyReference counted } cardinality):
            {
                if(ContextHabitatRecognizer.IsUnqualifiedFiller(cardinality.Filler) && cardinality.Cardinality >= 0)
                {
                    told.Exacts.Add(new BijectionChainExactDefinition(definedClass, counted.Named.Iri, cardinality.Cardinality));
                }

                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Records a told class assertion as a lower-bound source: the asserted class must be a named non-constant class, told directly or through the intersection wrapper the strict arm's refutation probes spell, and a NAMED individual additionally counts toward the told-distinct member count.</summary>
    /// <param name="axiom">The candidate class assertion.</param>
    /// <param name="told">The told-fact accumulator.</param>
    private static void CollectAssertion(OwlClassAssertionAxiom axiom, BijectionChainTold told)
    {
        if(axiom.Individual is not NamedNode and not BlankNode || !TryReadAssertedClass(axiom.Class, out Utf8String assertedClass))
        {
            return;
        }

        if(!told.AssertedMembers.TryGetValue(assertedClass, out List<Utf8String>? members))
        {
            members = [];
            told.AssertedMembers[assertedClass] = members;
            told.AssertedClasses.Add(assertedClass);
        }

        if(axiom.Individual is not NamedNode named)
        {
            return;
        }

        for(int index = 0; index < members.Count; index++)
        {
            if(members[index].Equals(named.Iri))
            {
                return;
            }
        }

        members.Add(named.Iri);
    }

    /// <summary>Records one unordered pair in a symmetric told relation, both directions.</summary>
    /// <param name="relationToAppendTo">The symmetric relation.</param>
    /// <param name="left">The pair's first member.</param>
    /// <param name="right">The pair's second member.</param>
    private static void LinkPair(Dictionary<Utf8String, HashSet<Utf8String>> relationToAppendTo, Utf8String left, Utf8String right)
    {
        LinkDirection(relationToAppendTo, left, right);
        LinkDirection(relationToAppendTo, right, left);
    }

    /// <summary>Records one direction of a symmetric told relation.</summary>
    /// <param name="relationToAppendTo">The symmetric relation.</param>
    /// <param name="from">The relation's key.</param>
    /// <param name="to">The partner to record.</param>
    private static void LinkDirection(Dictionary<Utf8String, HashSet<Utf8String>> relationToAppendTo, Utf8String from, Utf8String to)
    {
        if(!relationToAppendTo.TryGetValue(from, out HashSet<Utf8String>? partners))
        {
            partners = [];
            relationToAppendTo[from] = partners;
        }

        partners.Add(to);
    }

    /// <summary>Whether every unordered pair of the listed keys is covered by the told relation — the coverage rule the distinctness and disjointness premises both need, computed across ALL told axioms rather than within one.</summary>
    /// <param name="relation">The symmetric told relation.</param>
    /// <param name="keys">The keys whose pairs must all be covered.</param>
    /// <returns><see langword="true"/> when every pair is told.</returns>
    private static bool ArePairwiseLinked(Dictionary<Utf8String, HashSet<Utf8String>> relation, List<Utf8String> keys)
    {
        for(int first = 0; first < keys.Count; first++)
        {
            if(!relation.TryGetValue(keys[first], out HashSet<Utf8String>? partners))
            {
                return keys.Count <= 1;
            }

            for(int second = first + 1; second < keys.Count; second++)
            {
                if(!partners.Contains(keys[second]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether two plain roles are told inverses of one another, in either argument order.</summary>
    /// <param name="told">The told facts.</param>
    /// <param name="role">The first role.</param>
    /// <param name="partner">The second role.</param>
    /// <returns><see langword="true"/> on a told inverse link.</returns>
    private static bool AreToldInverse(BijectionChainTold told, Utf8String role, Utf8String partner)
    {
        return told.InverseRoles.TryGetValue(role, out HashSet<Utf8String>? partners) && partners.Contains(partner);
    }

    /// <summary>Reads a class expression as a size variable: a named class reference other than <c>owl:Thing</c> and <c>owl:Nothing</c>, whose extensions are semantics-fixed and carry no free size.</summary>
    /// <param name="expression">The candidate expression.</param>
    /// <param name="iri">The class IRI; the default value when the shape did not match.</param>
    /// <returns><see langword="true"/> on a plain named class.</returns>
    private static bool TryReadClass(OwlClassExpression expression, out Utf8String iri)
    {
        if(expression is OwlClassReference reference && ContextHabitatRecognizer.IsChainNodeClass(reference))
        {
            iri = reference.Class.Iri;

            return true;
        }

        iri = default;

        return false;
    }

    /// <summary>Reads an individual term's identity — a named individual's IRI or an anonymous individual's label.</summary>
    /// <param name="individual">The individual term.</param>
    /// <param name="identity">The term's identity; the default value when the term is neither named nor anonymous.</param>
    /// <returns><see langword="true"/> on an individual term.</returns>
    private static bool TryReadIndividual(RdfTerm individual, out Utf8String identity)
    {
        switch(individual)
        {
            case(NamedNode named):
            {
                identity = named.Iri;

                return true;
            }
            case(BlankNode anonymous):
            {
                identity = anonymous.Label;

                return true;
            }
            default:
            {
                identity = default;

                return false;
            }
        }
    }

    /// <summary>Adds a non-empty enumeration's distinct NAMED members to the accumulator; a single anonymous member drops the source whole, since an anonymous denotation carries no told distinctness.</summary>
    /// <param name="oneOf">The told enumeration.</param>
    /// <param name="membersToAppendTo">The distinct member accumulator.</param>
    /// <returns><see langword="true"/> when every member was named and the enumeration was non-empty.</returns>
    private static bool TryReadNamedMembers(OwlObjectOneOf oneOf, HashSet<Utf8String> membersToAppendTo)
    {
        if(oneOf.Individuals.Count == 0)
        {
            return false;
        }

        for(int index = 0; index < oneOf.Individuals.Count; index++)
        {
            if(oneOf.Individuals[index] is not NamedNode member)
            {
                return false;
            }

            membersToAppendTo.Add(member.Iri);
        }

        return true;
    }

    /// <summary>Reads a non-empty enumeration's distinct NAMED members in first-seen order; a single anonymous member drops the source whole.</summary>
    /// <param name="oneOf">The told enumeration.</param>
    /// <param name="distinct">The identity set the deduplication runs against.</param>
    /// <param name="membersToAppendTo">The distinct members in first-seen order.</param>
    /// <returns><see langword="true"/> when every member was named and the enumeration was non-empty.</returns>
    private static bool TryReadNamedMemberList(OwlObjectOneOf oneOf, HashSet<Utf8String> distinct, List<Utf8String> membersToAppendTo)
    {
        if(oneOf.Individuals.Count == 0)
        {
            return false;
        }

        for(int index = 0; index < oneOf.Individuals.Count; index++)
        {
            if(oneOf.Individuals[index] is not NamedNode member)
            {
                return false;
            }

            if(distinct.Add(member.Iri))
            {
                membersToAppendTo.Add(member.Iri);
            }
        }

        return true;
    }

    /// <summary>Reads a non-empty union's operands as size variables; a complex or constant operand drops the source whole.</summary>
    /// <param name="union">The told union.</param>
    /// <param name="operandsToAppendTo">The operands in told order.</param>
    /// <returns><see langword="true"/> when every operand is a plain named class.</returns>
    private static bool TryReadNamedOperands(OwlObjectUnionOf union, List<Utf8String> operandsToAppendTo)
    {
        if(union.Operands.Count == 0)
        {
            return false;
        }

        for(int index = 0; index < union.Operands.Count; index++)
        {
            if(!TryReadClass(union.Operands[index], out Utf8String operand))
            {
                return false;
            }

            operandsToAppendTo.Add(operand);
        }

        return true;
    }

    /// <summary>
    /// Reads an assertion's class as a size variable: a plain named class, or
    /// the intersection wrapper <c>C ⊓ ¬owl:Nothing</c> the strict arm's shared
    /// refutation builder emits, whose second conjunct denotes the whole domain
    /// in every interpretation and leaves the intersection equal to <c>C</c>.
    /// Every other complex assertion shape is ignored.
    /// </summary>
    /// <param name="expression">The asserted class expression.</param>
    /// <param name="iri">The class IRI; the default value when the shape did not match.</param>
    /// <returns><see langword="true"/> on a lower-bound source.</returns>
    private static bool TryReadAssertedClass(OwlClassExpression expression, out Utf8String iri)
    {
        if(TryReadClass(expression, out iri))
        {
            return true;
        }

        if(expression is not OwlObjectIntersectionOf intersection || intersection.Operands.Count != 2)
        {
            return false;
        }

        bool hasWrapper = false;
        bool hasClass = false;
        for(int index = 0; index < intersection.Operands.Count; index++)
        {
            if(intersection.Operands[index] is OwlObjectComplementOf complement && IsConstantReference(complement.Operand, OwlVocabulary.Nothing))
            {
                hasWrapper = true;
            }
            else if(TryReadClass(intersection.Operands[index], out iri))
            {
                hasClass = true;
            }
        }

        return hasWrapper && hasClass;
    }

    /// <summary>Whether a told assertion's class carries a top-level complement of <c>owl:Thing</c> — either as one conjunct of an intersection or as the whole expression — whose extension is empty in every interpretation while the assertion demands a member.</summary>
    /// <param name="expression">The asserted class expression.</param>
    /// <returns><see langword="true"/> on the empty-conjunct shape.</returns>
    private static bool IsEmptyConjunctClass(OwlClassExpression expression)
    {
        if(expression is OwlObjectComplementOf whole && IsConstantReference(whole.Operand, OwlVocabulary.Thing))
        {
            return true;
        }

        if(expression is not OwlObjectIntersectionOf intersection)
        {
            return false;
        }

        for(int index = 0; index < intersection.Operands.Count; index++)
        {
            if(intersection.Operands[index] is OwlObjectComplementOf conjunct && IsConstantReference(conjunct.Operand, OwlVocabulary.Thing))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a class expression is the named reference of one OWL class constant.</summary>
    /// <param name="expression">The candidate expression.</param>
    /// <param name="constant">The constant's IRI.</param>
    /// <returns><see langword="true"/> on the constant reference.</returns>
    private static bool IsConstantReference(OwlClassExpression expression, Utf8String constant)
    {
        return expression is OwlClassReference reference && reference.Class.Iri.Equals(constant);
    }

    /// <summary>Interns one size variable, appending it in first-seen order.</summary>
    /// <param name="classes">The size variables in first-seen order.</param>
    /// <param name="indices">The identity index over the variables.</param>
    /// <param name="iri">The class IRI.</param>
    /// <returns>The variable's index.</returns>
    private static int ClassIndex(List<Utf8String> classes, Dictionary<Utf8String, int> indices, Utf8String iri)
    {
        if(indices.TryGetValue(iri, out int index))
        {
            return index;
        }

        index = classes.Count;
        classes.Add(iri);
        indices[iri] = index;

        return index;
    }

    /// <summary>Derives the told size constants: a singleton enumeration pins a size of one unconditionally, and a wider enumeration pins its distinct member count only where every unordered member pair is told different — without full coverage the members may collide and no constant follows.</summary>
    /// <param name="told">The told facts.</param>
    /// <param name="classes">The size variables in first-seen order.</param>
    /// <param name="indices">The identity index over the variables.</param>
    /// <param name="constantsToAppendTo">The derived constants.</param>
    private static void DeriveConstants(BijectionChainTold told, List<Utf8String> classes, Dictionary<Utf8String, int> indices, List<BijectionChainConstant> constantsToAppendTo)
    {
        for(int index = 0; index < told.Enumerations.Count; index++)
        {
            BijectionChainEnumeration enumeration = told.Enumerations[index];
            if(enumeration.Members.Count == 0)
            {
                continue;
            }

            if(enumeration.Members.Count == 1 || ArePairwiseLinked(told.DifferentIndividuals, enumeration.Members))
            {
                constantsToAppendTo.Add(new BijectionChainConstant(ClassIndex(classes, indices, enumeration.Class), enumeration.Members.Count));
            }
        }
    }

    /// <summary>
    /// Derives the told size equalities: two told existential steps running in
    /// opposite directions between the same two named classes, over told
    /// inverse roles, with the STEP'S OWN role told both functional and
    /// inverse-functional, induce a bijection between the two extensions. The
    /// premise role is the one the checked step carries, so both orientations
    /// are offered and each fires only where its own role holds both
    /// characteristics.
    /// </summary>
    /// <param name="told">The told facts.</param>
    /// <param name="classes">The size variables in first-seen order.</param>
    /// <param name="indices">The identity index over the variables.</param>
    /// <param name="equalitiesToAppendTo">The derived equalities.</param>
    private static void DeriveEqualities(BijectionChainTold told, List<Utf8String> classes, Dictionary<Utf8String, int> indices, List<BijectionChainEquality> equalitiesToAppendTo)
    {
        HashSet<(int First, int Second)> recorded = [];
        for(int forward = 0; forward < told.Links.Count; forward++)
        {
            BijectionChainLink step = told.Links[forward];
            if(!told.FunctionalRoles.Contains(step.Role) || !told.InverseFunctionalRoles.Contains(step.Role))
            {
                continue;
            }

            for(int backward = 0; backward < told.Links.Count; backward++)
            {
                BijectionChainLink partner = told.Links[backward];
                if(!partner.Source.Equals(step.Target) || !partner.Target.Equals(step.Source) || !AreToldInverse(told, step.Role, partner.Role))
                {
                    continue;
                }

                int left = ClassIndex(classes, indices, step.Source);
                int right = ClassIndex(classes, indices, step.Target);
                (int First, int Second) key = left <= right ? (left, right) : (right, left);
                if(recorded.Add(key))
                {
                    equalitiesToAppendTo.Add(new BijectionChainEquality(left, right));
                }
            }
        }
    }

    /// <summary>Derives the told additive decompositions: a union definition over named operands adds only where every unordered operand pair is told disjoint. A repeated operand IRI drops the source WHOLE — the union's operand list is semantically a multiset there, and deduplicating it would understate the sum rather than merely loosening a bound.</summary>
    /// <param name="told">The told facts.</param>
    /// <param name="classes">The size variables in first-seen order.</param>
    /// <param name="indices">The identity index over the variables.</param>
    /// <param name="sumsToAppendTo">The derived sums.</param>
    private static void DeriveSums(BijectionChainTold told, List<Utf8String> classes, Dictionary<Utf8String, int> indices, List<BijectionChainSum> sumsToAppendTo)
    {
        HashSet<Utf8String> distinct = [];
        for(int index = 0; index < told.Unions.Count; index++)
        {
            BijectionChainUnionDefinition union = told.Unions[index];
            distinct.Clear();
            bool repeated = false;
            for(int operand = 0; operand < union.Operands.Count; operand++)
            {
                repeated = repeated || !distinct.Add(union.Operands[operand]);
            }

            if(repeated || !ArePairwiseLinked(told.DisjointClasses, union.Operands))
            {
                continue;
            }

            List<int> operands = [];
            for(int operand = 0; operand < union.Operands.Count; operand++)
            {
                operands.Add(ClassIndex(classes, indices, union.Operands[operand]));
            }

            sumsToAppendTo.Add(new BijectionChainSum(ClassIndex(classes, indices, union.Class), operands));
        }
    }

    /// <summary>
    /// Derives the anchored fan-in constants and the fiber products: an
    /// existential definition into an anchor class whose told exact cardinality
    /// counts the existential role's told inverse makes the defined class the
    /// anchor's predecessor set. Where the anchor is additionally a singleton
    /// enumeration the predecessor count is the told cardinality outright; where
    /// the existential role is told functional the predecessor sets fiber the
    /// defined class over the anchor, so the size is the cardinality times the
    /// anchor's own size.
    /// </summary>
    /// <param name="told">The told facts.</param>
    /// <param name="classes">The size variables in first-seen order.</param>
    /// <param name="indices">The identity index over the variables.</param>
    /// <param name="constantsToAppendTo">The derived constants.</param>
    /// <param name="productsToAppendTo">The derived products.</param>
    private static void DeriveFanIns(BijectionChainTold told, List<Utf8String> classes, Dictionary<Utf8String, int> indices, List<BijectionChainConstant> constantsToAppendTo, List<BijectionChainProduct> productsToAppendTo)
    {
        HashSet<Utf8String> singletons = [];
        for(int index = 0; index < told.Enumerations.Count; index++)
        {
            if(told.Enumerations[index].Members.Count == 1)
            {
                singletons.Add(told.Enumerations[index].Class);
            }
        }

        for(int index = 0; index < told.Existentials.Count; index++)
        {
            BijectionChainExistentialDefinition existential = told.Existentials[index];
            for(int counted = 0; counted < told.Exacts.Count; counted++)
            {
                BijectionChainExactDefinition exact = told.Exacts[counted];
                if(!exact.Class.Equals(existential.Filler) || !AreToldInverse(told, existential.Role, exact.Role))
                {
                    continue;
                }

                if(singletons.Contains(existential.Filler))
                {
                    constantsToAppendTo.Add(new BijectionChainConstant(ClassIndex(classes, indices, existential.Class), exact.Cardinality));
                }

                if(told.FunctionalRoles.Contains(existential.Role))
                {
                    productsToAppendTo.Add(new BijectionChainProduct(
                        ClassIndex(classes, indices, existential.Class),
                        exact.Cardinality,
                        ClassIndex(classes, indices, existential.Filler)));
                }
            }
        }
    }

    /// <summary>Derives the told upper bounds off the subclass-position enumeration sources.</summary>
    /// <param name="told">The told facts.</param>
    /// <param name="classes">The size variables in first-seen order.</param>
    /// <param name="indices">The identity index over the variables.</param>
    /// <param name="uppersToAppendTo">The derived upper bounds.</param>
    private static void DeriveUppers(BijectionChainTold told, List<Utf8String> classes, Dictionary<Utf8String, int> indices, List<BijectionChainBound> uppersToAppendTo)
    {
        for(int index = 0; index < told.Uppers.Count; index++)
        {
            BijectionChainUpperSource upper = told.Uppers[index];
            uppersToAppendTo.Add(new BijectionChainBound(ClassIndex(classes, indices, upper.Class), upper.Members));
        }
    }

    /// <summary>Derives the told lower bounds: an asserted class holds at least one element, raised to the asserted NAMED members' distinct count where every unordered member pair is told different.</summary>
    /// <param name="told">The told facts.</param>
    /// <param name="classes">The size variables in first-seen order.</param>
    /// <param name="indices">The identity index over the variables.</param>
    /// <param name="lowersToAppendTo">The derived lower bounds.</param>
    private static void DeriveLowers(BijectionChainTold told, List<Utf8String> classes, Dictionary<Utf8String, int> indices, List<BijectionChainBound> lowersToAppendTo)
    {
        for(int index = 0; index < told.AssertedClasses.Count; index++)
        {
            Utf8String assertedClass = told.AssertedClasses[index];
            List<Utf8String> members = told.AssertedMembers[assertedClass];
            long bound = members.Count >= 2 && ArePairwiseLinked(told.DifferentIndividuals, members) ? members.Count : 1;
            lowersToAppendTo.Add(new BijectionChainBound(ClassIndex(classes, indices, assertedClass), bound));
        }
    }

    /// <summary>What one propagation step produced.</summary>
    private enum BijectionChainDerivation
    {
        /// <summary>The step derived nothing new — its premises are incomplete, or its conclusion was already on the record.</summary>
        None,

        /// <summary>The step grounded one equality class's size, so every constraint is re-offered.</summary>
        Grounded,

        /// <summary>The step reached an impossible state, so the module is refuted.</summary>
        Clash,

        /// <summary>The step's arithmetic would leave the long range, so the propagation stops without a verdict.</summary>
        Overflow,
    }

    /// <summary>The propagation state over the size variables: the union-find forest of equality classes and, per class root, the forced size and the tightest told bounds.</summary>
    /// <param name="Parent">The union-find forest, one entry per size variable.</param>
    /// <param name="Grounded">Whether each root's size is forced.</param>
    /// <param name="Value">Each root's forced size, valid where <paramref name="Grounded"/> holds.</param>
    /// <param name="Lower">Each root's largest told lower bound; zero when none was told.</param>
    /// <param name="Upper">Each root's smallest told upper bound, valid where <paramref name="Bounded"/> holds.</param>
    /// <param name="Bounded">Whether each root carries a told upper bound.</param>
    private sealed record BijectionChainState(int[] Parent, bool[] Grounded, long[] Value, long[] Lower, long[] Upper, bool[] Bounded);

    /// <summary>
    /// Propagates the collected constraint sources to a fixpoint and answers
    /// whether the recognized subset is unsatisfiable. The outright
    /// asserted-conjunct clash fires ahead of every propagation step; otherwise
    /// the equality constraints merge the variables into equality classes, the
    /// constants and bounds fold onto the class roots, and a bounded worklist
    /// runs the sums and products until nothing more grounds. Every step either
    /// grounds one previously-unforced class, retires a constraint, or clashes,
    /// and the classes are finite, so the loop terminates.
    /// </summary>
    /// <param name="sources">The collected sources, inside the class window.</param>
    /// <param name="clashReason">The named clash reason; <see langword="null"/> when no clash was reached.</param>
    /// <returns><see langword="true"/> when the recognized subset — and therefore the whole module — is inconsistent.</returns>
    private static bool TryRefute(BijectionChainSources sources, [NotNullWhen(true)] out string? clashReason)
    {
        clashReason = null;
        if(sources.HasEmptyConjunct)
        {
            clashReason = BijectionChainClashReasons.UnsatisfiableAssertedConjunct(sources.EmptyConjunctSubject);

            return true;
        }

        int count = sources.Classes.Count;
        BijectionChainState state = new(new int[count], new bool[count], new long[count], new long[count], new long[count], new bool[count]);
        for(int index = 0; index < count; index++)
        {
            state.Parent[index] = index;
        }

        for(int index = 0; index < sources.Equalities.Count; index++)
        {
            Merge(state, sources.Equalities[index].First, sources.Equalities[index].Second);
        }

        for(int index = 0; index < sources.Uppers.Count; index++)
        {
            BijectionChainBound bound = sources.Uppers[index];
            int root = Find(state, bound.Class);
            state.Upper[root] = state.Bounded[root] ? Math.Min(state.Upper[root], bound.Value) : bound.Value;
            state.Bounded[root] = true;
        }

        for(int index = 0; index < sources.Lowers.Count; index++)
        {
            BijectionChainBound bound = sources.Lowers[index];
            int root = Find(state, bound.Class);
            state.Lower[root] = Math.Max(state.Lower[root], bound.Value);
        }

        for(int index = 0; index < sources.Constants.Count; index++)
        {
            BijectionChainConstant constant = sources.Constants[index];
            if(Ground(state, constant.Class, constant.Value) == BijectionChainDerivation.Clash)
            {
                clashReason = BijectionChainClashReasons.ForcedConstantConflict(sources.Classes[constant.Class]);

                return true;
            }
        }

        for(int index = 0; index < count; index++)
        {
            if(!WithinBounds(state, Find(state, index)))
            {
                clashReason = BijectionChainClashReasons.ForcedConstantConflict(sources.Classes[index]);

                return true;
            }
        }

        return TryPropagate(sources, state, out clashReason);
    }

    /// <summary>Runs the bounded worklist over the sums and products: each grounding re-offers every constraint, and the loop ends when no constraint derives anything further.</summary>
    /// <param name="sources">The collected sources.</param>
    /// <param name="state">The propagation state.</param>
    /// <param name="clashReason">The named clash reason; <see langword="null"/> when no clash was reached.</param>
    /// <returns><see langword="true"/> when the propagation reached an impossible state.</returns>
    private static bool TryPropagate(BijectionChainSources sources, BijectionChainState state, [NotNullWhen(true)] out string? clashReason)
    {
        clashReason = null;

        int constraints = sources.Sums.Count + sources.Products.Count;
        bool[] queued = new bool[constraints];
        Queue<int> work = new();
        for(int index = 0; index < constraints; index++)
        {
            queued[index] = true;
            work.Enqueue(index);
        }

        while(work.Count > 0)
        {
            int index = work.Dequeue();
            queued[index] = false;

            int subject;
            BijectionChainDerivation derivation;
            if(index < sources.Sums.Count)
            {
                subject = sources.Sums[index].Total;
                derivation = ApplySum(sources.Sums[index], state);
            }
            else
            {
                subject = sources.Products[index - sources.Sums.Count].Product;
                derivation = ApplyProduct(sources.Products[index - sources.Sums.Count], state);
            }

            if(derivation == BijectionChainDerivation.Clash)
            {
                clashReason = BijectionChainClashReasons.ForcedConstantConflict(sources.Classes[subject]);

                return true;
            }

            if(derivation == BijectionChainDerivation.Overflow)
            {
                return false;
            }

            if(derivation != BijectionChainDerivation.Grounded)
            {
                continue;
            }

            for(int other = 0; other < constraints; other++)
            {
                if(!queued[other])
                {
                    queued[other] = true;
                    work.Enqueue(other);
                }
            }
        }

        return false;
    }

    /// <summary>Finds a size variable's equality-class root, compressing the path in an explicit two-pass loop.</summary>
    /// <param name="state">The propagation state.</param>
    /// <param name="index">The size variable.</param>
    /// <returns>The class root.</returns>
    private static int Find(BijectionChainState state, int index)
    {
        int root = index;
        while(state.Parent[root] != root)
        {
            root = state.Parent[root];
        }

        int walk = index;
        while(state.Parent[walk] != root)
        {
            int next = state.Parent[walk];
            state.Parent[walk] = root;
            walk = next;
        }

        return root;
    }

    /// <summary>Merges two size variables into one equality class, carrying the merged root's forced size and tightest bounds over.</summary>
    /// <param name="state">The propagation state.</param>
    /// <param name="first">The first size variable.</param>
    /// <param name="second">The second size variable.</param>
    private static void Merge(BijectionChainState state, int first, int second)
    {
        int left = Find(state, first);
        int right = Find(state, second);
        if(left == right)
        {
            return;
        }

        state.Parent[right] = left;
        state.Lower[left] = Math.Max(state.Lower[left], state.Lower[right]);
        if(state.Bounded[right])
        {
            state.Upper[left] = state.Bounded[left] ? Math.Min(state.Upper[left], state.Upper[right]) : state.Upper[right];
            state.Bounded[left] = true;
        }

        if(state.Grounded[right] && !state.Grounded[left])
        {
            state.Grounded[left] = true;
            state.Value[left] = state.Value[right];
        }
    }

    /// <summary>Grounds one equality class's size, answering a clash when the class already carries a different size or when the candidate falls outside the class's told bounds.</summary>
    /// <param name="state">The propagation state.</param>
    /// <param name="classIndex">A size variable of the class.</param>
    /// <param name="candidate">The candidate size.</param>
    /// <returns>What the grounding produced.</returns>
    private static BijectionChainDerivation Ground(BijectionChainState state, int classIndex, long candidate)
    {
        if(candidate < 0)
        {
            return BijectionChainDerivation.Clash;
        }

        int root = Find(state, classIndex);
        if(state.Grounded[root])
        {
            return state.Value[root] == candidate ? BijectionChainDerivation.None : BijectionChainDerivation.Clash;
        }

        state.Grounded[root] = true;
        state.Value[root] = candidate;

        return WithinBounds(state, root) ? BijectionChainDerivation.Grounded : BijectionChainDerivation.Clash;
    }

    /// <summary>Whether one equality class's told bounds admit each other and its forced size, if any.</summary>
    /// <param name="state">The propagation state.</param>
    /// <param name="root">The class root.</param>
    /// <returns><see langword="true"/> when the class is still satisfiable on its own numbers.</returns>
    private static bool WithinBounds(BijectionChainState state, int root)
    {
        if(state.Bounded[root] && state.Lower[root] > state.Upper[root])
        {
            return false;
        }

        if(!state.Grounded[root])
        {
            return true;
        }

        return state.Value[root] >= state.Lower[root] && (!state.Bounded[root] || state.Value[root] <= state.Upper[root]);
    }

    /// <summary>
    /// Applies one additive decomposition: every operand forced grounds the
    /// total, the total plus all but one operand grounds the residue — a
    /// negative residue clashing outright — and a decomposition whose every
    /// operand lies in the TOTAL'S OWN equality class collapses that class to
    /// zero, since the cardinal equation is solved only by zero and the infinite
    /// cardinals a told finite bound excludes.
    /// </summary>
    /// <param name="sum">The decomposition.</param>
    /// <param name="state">The propagation state.</param>
    /// <returns>What the step produced.</returns>
    private static BijectionChainDerivation ApplySum(BijectionChainSum sum, BijectionChainState state)
    {
        int root = Find(state, sum.Total);
        long total = 0;
        int ungrounded = 0;
        int ungroundedOperand = -1;
        bool selfReferential = sum.Operands.Count >= 2;
        for(int index = 0; index < sum.Operands.Count; index++)
        {
            int operand = Find(state, sum.Operands[index]);
            selfReferential = selfReferential && operand == root;
            if(!state.Grounded[operand])
            {
                ungrounded++;
                ungroundedOperand = sum.Operands[index];
                continue;
            }

            if(total > long.MaxValue - state.Value[operand])
            {
                return BijectionChainDerivation.Overflow;
            }

            total += state.Value[operand];
        }

        if(ungrounded == 0)
        {
            return Ground(state, sum.Total, total);
        }

        if(ungrounded == 1 && state.Grounded[root])
        {
            return Ground(state, ungroundedOperand, state.Value[root] - total);
        }

        if(selfReferential && (state.Bounded[root] || state.Grounded[root]))
        {
            return Ground(state, sum.Total, 0);
        }

        return BijectionChainDerivation.None;
    }

    /// <summary>
    /// Applies one fiber product: a zero fiber empties the product, a forced
    /// base multiplies out under the long overflow guard, a forced product
    /// divides back onto the base — a non-integral quotient clashing, since no
    /// cardinal solves it — and a product fibered over its OWN equality class
    /// with a fiber of at least two collapses that class to zero under a told
    /// finite bound.
    /// </summary>
    /// <param name="product">The fiber product.</param>
    /// <param name="state">The propagation state.</param>
    /// <returns>What the step produced.</returns>
    private static BijectionChainDerivation ApplyProduct(BijectionChainProduct product, BijectionChainState state)
    {
        int productRoot = Find(state, product.Product);
        int operandRoot = Find(state, product.Operand);
        if(product.Factor == 0)
        {
            return Ground(state, product.Product, 0);
        }

        if(state.Grounded[operandRoot])
        {
            if(state.Value[operandRoot] > long.MaxValue / product.Factor)
            {
                return BijectionChainDerivation.Overflow;
            }

            BijectionChainDerivation multiplied = Ground(state, product.Product, product.Factor * state.Value[operandRoot]);
            if(multiplied != BijectionChainDerivation.None)
            {
                return multiplied;
            }
        }

        if(state.Grounded[productRoot])
        {
            if(state.Value[productRoot] % product.Factor != 0)
            {
                return BijectionChainDerivation.Clash;
            }

            BijectionChainDerivation divided = Ground(state, product.Operand, state.Value[productRoot] / product.Factor);
            if(divided != BijectionChainDerivation.None)
            {
                return divided;
            }
        }

        if(productRoot == operandRoot && product.Factor >= 2 && (state.Bounded[productRoot] || state.Grounded[productRoot]))
        {
            return Ground(state, product.Product, 0);
        }

        return BijectionChainDerivation.None;
    }

    /// <summary>One told inverse-role pair over plain roles.</summary>
    /// <param name="First">The first role.</param>
    /// <param name="Second">The second role.</param>
    private readonly record struct BijectionChainRolePair(Utf8String First, Utf8String Second);

    /// <summary>One told domain or range axiom over a plain role and a named class.</summary>
    /// <param name="Role">The constrained plain role.</param>
    /// <param name="Class">The named class the role's sources or targets are confined to.</param>
    private readonly record struct BijectionChainRoleClass(Utf8String Role, Utf8String Class);

    /// <summary>
    /// The whole-module certificate pass: every axiom must lie in the union of
    /// the two routes' form sets, and each route then INDEPENDENTLY re-validates
    /// the whole module against its own list. The certificate fires only where
    /// exactly one route validates — a union-admitted module fitting neither
    /// route's own list carries no model construction and stays silent, since
    /// consistent arithmetic alone certifies nothing.
    /// </summary>
    /// <param name="module">The module to admit or reject.</param>
    /// <param name="route">The named certificate route; <see langword="null"/> when no route validated.</param>
    /// <returns><see langword="true"/> when exactly one route certified the module consistent.</returns>
    private static bool TryCertify(ReasoningModule module, [NotNullWhen(true)] out string? route)
    {
        route = null;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(!IsVacuityForm(axiom) && !IsTowerEnumerationForm(axiom))
            {
                return false;
            }
        }

        bool vacuity = IsVacuityModule(module);
        bool tower = IsGroundedTowerModule(module);
        if(vacuity == tower)
        {
            return false;
        }

        route = vacuity ? VacuityCertificate : GroundedTowerCertificate;

        return true;
    }

    /// <summary>Whether every axiom lies in the vacuity route's whitelist, which is the route's whole certificate: the all-empty interpretation — one fresh element per told individual term, every named class empty, every role the empty relation — satisfies each whitelisted form.</summary>
    /// <param name="module">The module to validate.</param>
    /// <returns><see langword="true"/> when the all-empty witness model satisfies the whole module.</returns>
    private static bool IsVacuityModule(ReasoningModule module)
    {
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(!IsVacuityForm(axiom))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether one axiom is a vacuity-whitelisted form: non-logical
    /// passthrough, a functional or inverse-functional characteristic, a told
    /// inverse-role pair, a domain or range axiom, a subclass axiom whose
    /// SUBJECT is a named class other than <c>owl:Thing</c>, a whitelisted class
    /// equivalence, a disjointness over such named classes, or a told
    /// distinctness. <c>owl:Thing</c> is barred from every recognized
    /// named-class position, since its extension is the whole domain under every
    /// interpretation and the all-empty witness cannot honour it;
    /// <c>owl:Nothing</c> stays admissible everywhere, its extension being the
    /// witness's own. The one assertion shape admitted is a typing with
    /// <c>owl:Thing</c> itself, which asks only that its individual term denote
    /// a domain element. Nothing else is admitted — no other assertion, no
    /// same-individual axiom, no key, no data axiom, and no enumeration,
    /// zero-count, maximum-count, universal, or complement shape in a
    /// recognized position.
    /// </summary>
    /// <param name="axiom">The axiom to admit or reject.</param>
    /// <returns><see langword="true"/> on a whitelisted form.</returns>
    private static bool IsVacuityForm(OwlAxiom axiom)
    {
        return axiom switch
        {
            OwlDeclarationAxiom or OwlImportAxiom or OwlAnnotationAssertionAxiom or OwlSubAnnotationPropertyOfAxiom
                or OwlAnnotationPropertyDomainAxiom or OwlAnnotationPropertyRangeAxiom => true,
            OwlObjectPropertyCharacteristicAxiom
            {
                Characteristic: OwlPropertyCharacteristic.Functional or OwlPropertyCharacteristic.InverseFunctional,
                Property: OwlObjectPropertyReference,
            } => true,
            OwlInverseObjectPropertiesAxiom { First: OwlObjectPropertyReference, Second: OwlObjectPropertyReference } => true,
            OwlObjectPropertyDomainAxiom or OwlObjectPropertyRangeAxiom => true,
            OwlSubClassOfAxiom subClass => IsVacuitySafeClass(subClass.SubClass),
            OwlEquivalentClassesAxiom equivalent => IsVacuityEquivalence(equivalent),
            OwlDisjointClassesAxiom disjoint => AreVacuitySafeClasses(disjoint.Operands),
            OwlDifferentIndividualsAxiom => true,
            OwlClassAssertionAxiom assertion => IsUniversalClassAssertion(assertion),
            _ => false,
        };
    }

    /// <summary>
    /// Whether a told class assertion types an individual term with
    /// <c>owl:Thing</c> and nothing else — the one assertion shape both
    /// certificate routes admit, because <c>owl:Thing</c> denotes the whole
    /// domain in every interpretation, so the assertion demands only that the
    /// term denote a domain element, which every interpretation's non-empty
    /// domain supplies. It puts no element into any named class, so neither the
    /// all-empty walk nor the canonical fiber model's exactness is touched.
    /// </summary>
    /// <param name="axiom">The candidate class assertion.</param>
    /// <returns><see langword="true"/> on a universal-class assertion of an individual term.</returns>
    private static bool IsUniversalClassAssertion(OwlAxiom axiom)
    {
        return axiom is OwlClassAssertionAxiom { Class: OwlClassReference asserted, Individual: NamedNode or BlankNode }
            && asserted.Class.Iri.Equals(OwlVocabulary.Thing);
    }

    /// <summary>Whether a told equivalence pairs a vacuity-safe named class with one of the four right-hand shapes whose extension is empty under the all-empty witness, in either argument order.</summary>
    /// <param name="equivalent">The told equivalence.</param>
    /// <returns><see langword="true"/> on a whitelisted equivalence.</returns>
    private static bool IsVacuityEquivalence(OwlEquivalentClassesAxiom equivalent)
    {
        return (IsVacuitySafeClass(equivalent.First) && IsVacuitySafeDefinition(equivalent.Second))
            || (IsVacuitySafeClass(equivalent.Second) && IsVacuitySafeDefinition(equivalent.First));
    }

    /// <summary>Whether a class expression evaluates to the empty set under the all-empty witness: a vacuity-safe named class, a union of such classes, an existential over a plain role into such a class, or an unqualified exact cardinality of at least one over a plain role — under empty relations every element has zero successors, and zero differs from every admitted count.</summary>
    /// <param name="expression">The candidate defining side.</param>
    /// <returns><see langword="true"/> on an empty-evaluating shape.</returns>
    private static bool IsVacuitySafeDefinition(OwlClassExpression expression)
    {
        return expression switch
        {
            OwlClassReference => IsVacuitySafeClass(expression),
            OwlObjectUnionOf union => union.Operands.Count > 0 && AreVacuitySafeClasses(union.Operands),
            OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference } existential => IsVacuitySafeClass(existential.Filler),
            OwlObjectCardinality { Kind: OwlCardinalityKind.Exact, Property: OwlObjectPropertyReference, Cardinality: >= 1 } cardinality => ContextHabitatRecognizer.IsUnqualifiedFiller(cardinality.Filler),
            _ => false,
        };
    }

    /// <summary>Whether a class expression is a named class reference other than <c>owl:Thing</c> — the only class position the all-empty witness may occupy freely.</summary>
    /// <param name="expression">The candidate expression.</param>
    /// <returns><see langword="true"/> on a vacuity-safe named class.</returns>
    private static bool IsVacuitySafeClass(OwlClassExpression expression)
    {
        return expression is OwlClassReference reference && !reference.Class.Iri.Equals(OwlVocabulary.Thing);
    }

    /// <summary>Whether every operand is a vacuity-safe named class.</summary>
    /// <param name="operands">The operands to check.</param>
    /// <returns><see langword="true"/> when every operand is admitted.</returns>
    private static bool AreVacuitySafeClasses(IReadOnlyList<OwlClassExpression> operands)
    {
        for(int index = 0; index < operands.Count; index++)
        {
            if(!IsVacuitySafeClass(operands[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether one axiom is the single form the grounded-tower route needs beyond the vacuity whitelist: a named class equivalent to an enumeration of named individuals, which the all-empty witness cannot host.</summary>
    /// <param name="axiom">The axiom to classify.</param>
    /// <returns><see langword="true"/> on the tower-only form.</returns>
    private static bool IsTowerEnumerationForm(OwlAxiom axiom)
    {
        if(axiom is not OwlEquivalentClassesAxiom equivalent)
        {
            return false;
        }

        return (IsVacuitySafeClass(equivalent.First) && equivalent.Second is OwlObjectOneOf)
            || (IsVacuitySafeClass(equivalent.Second) && equivalent.First is OwlObjectOneOf);
    }

    /// <summary>The told parts of a candidate diamond template, bucketed by kind in one pass.</summary>
    /// <param name="FunctionalRoles">The plain roles told functional.</param>
    /// <param name="InversePairs">The told inverse-role pairs over plain roles.</param>
    /// <param name="Enumerations">The told enumeration definitions.</param>
    /// <param name="Existentials">The told existential definitions.</param>
    /// <param name="Exacts">The told unqualified exact-cardinality definitions.</param>
    /// <param name="Domains">The told domain axioms over plain roles and named classes.</param>
    /// <param name="Ranges">The told range axioms over plain roles and named classes.</param>
    private sealed record BijectionChainTowerParts(
        List<Utf8String> FunctionalRoles,
        List<BijectionChainRolePair> InversePairs,
        List<BijectionChainEnumeration> Enumerations,
        List<BijectionChainExistentialDefinition> Existentials,
        List<BijectionChainExactDefinition> Exacts,
        List<BijectionChainRoleClass> Domains,
        List<BijectionChainRoleClass> Ranges);

    /// <summary>The resolved diamond template: its three levels, the three forward roles and their told inverses, and the three level constants.</summary>
    /// <param name="Anchor">The anchor level — the singleton-enumerated class.</param>
    /// <param name="Mid">The mid level.</param>
    /// <param name="Top">The top level.</param>
    /// <param name="MidStepRole">The functional role the mid level's existential runs into the anchor along.</param>
    /// <param name="TopMidRole">The functional role the top level's existential runs into the mid level along.</param>
    /// <param name="TopAnchorRole">The functional role the top level's second existential runs into the anchor along.</param>
    /// <param name="MidStepInverse">The told inverse of <paramref name="MidStepRole"/>, counted at the anchor.</param>
    /// <param name="TopMidInverse">The told inverse of <paramref name="TopMidRole"/>, counted at the mid level.</param>
    /// <param name="TopAnchorInverse">The told inverse of <paramref name="TopAnchorRole"/>, counted at the anchor.</param>
    /// <param name="MidLevel">The anchor's told count over <paramref name="MidStepInverse"/> — the mid level's size.</param>
    /// <param name="FiberLevel">The mid level's told count over <paramref name="TopMidInverse"/> — each mid element's fiber size.</param>
    /// <param name="TopLevel">The anchor's told count over <paramref name="TopAnchorInverse"/> — the top level's size.</param>
    private readonly record struct BijectionChainTowerShape(
        Utf8String Anchor,
        Utf8String Mid,
        Utf8String Top,
        Utf8String MidStepRole,
        Utf8String TopMidRole,
        Utf8String TopAnchorRole,
        Utf8String MidStepInverse,
        Utf8String TopMidInverse,
        Utf8String TopAnchorInverse,
        long MidLevel,
        long FiberLevel,
        long TopLevel);

    /// <summary>
    /// Whether the module is EXACTLY the grounded-tower template, whose
    /// canonical fiber model is the route's certificate: one singleton-anchored
    /// class, a mid level defined by a functional existential into the anchor
    /// and by the anchor's told count over that role's inverse, and a top level
    /// defined by functional existentials into both lower levels and by the
    /// anchor's told count over the second inverse — the three level constants
    /// multiplying out exactly, the six role names distinct, and the three class
    /// names distinct and free of the semantics-fixed constants, whose
    /// extensions the construction's free choice of level sets cannot honour.
    /// Any extra axiom, omission, or domain or range axiom outside the
    /// template's own six leaves the route silent.
    /// </summary>
    /// <param name="module">The module to validate.</param>
    /// <returns><see langword="true"/> when the canonical fiber model witnesses the whole module.</returns>
    private static bool IsGroundedTowerModule(ReasoningModule module)
    {
        BijectionChainTowerParts parts = new([], [], [], [], [], [], []);
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(!TryBucketTowerAxiom(axiom, parts))
            {
                return false;
            }
        }

        if(!TryResolveTower(parts, out BijectionChainTowerShape shape))
        {
            return false;
        }

        return HasDistinctTowerRoles(shape)
            && HasTowerCharacteristics(parts, shape)
            && HasTowerDomainsAndRanges(parts, shape)
            && shape.MidLevel >= 1
            && shape.FiberLevel >= 1
            && shape.TopLevel >= 1
            && shape.TopLevel == shape.MidLevel * shape.FiberLevel;
    }

    /// <summary>Buckets one axiom into the candidate template's parts; an axiom outside the template's kinds and the non-logical passthrough rejects the module whole.</summary>
    /// <param name="axiom">The axiom to bucket.</param>
    /// <param name="parts">The parts accumulator.</param>
    /// <returns><see langword="true"/> when the axiom belongs to the template.</returns>
    private static bool TryBucketTowerAxiom(OwlAxiom axiom, BijectionChainTowerParts parts)
    {
        switch(axiom)
        {
            case(OwlDeclarationAxiom or OwlImportAxiom or OwlAnnotationAssertionAxiom or OwlSubAnnotationPropertyOfAxiom
                or OwlAnnotationPropertyDomainAxiom or OwlAnnotationPropertyRangeAxiom):
            {
                return true;
            }
            case(OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Functional, Property: OwlObjectPropertyReference functional }):
            {
                parts.FunctionalRoles.Add(functional.Named.Iri);

                return true;
            }
            case(OwlInverseObjectPropertiesAxiom { First: OwlObjectPropertyReference first, Second: OwlObjectPropertyReference second }):
            {
                parts.InversePairs.Add(new BijectionChainRolePair(first.Named.Iri, second.Named.Iri));

                return true;
            }
            case(OwlObjectPropertyDomainAxiom { Property: OwlObjectPropertyReference domainRole } domain):
            {
                return TryReadClass(domain.Domain, out Utf8String domainClass) && AddRoleClass(parts.Domains, domainRole.Named.Iri, domainClass);
            }
            case(OwlObjectPropertyRangeAxiom { Property: OwlObjectPropertyReference rangeRole } range):
            {
                return TryReadClass(range.Range, out Utf8String rangeClass) && AddRoleClass(parts.Ranges, rangeRole.Named.Iri, rangeClass);
            }
            case(OwlEquivalentClassesAxiom equivalent):
            {
                return TryReadTowerSide(equivalent.First, equivalent.Second, parts) || TryReadTowerSide(equivalent.Second, equivalent.First, parts);
            }
            case(OwlClassAssertionAxiom assertion):
            {
                return IsUniversalClassAssertion(assertion);
            }
            default:
            {
                return false;
            }
        }
    }

    /// <summary>Records one told domain or range constraint.</summary>
    /// <param name="constraintsToAppendTo">The domain or range accumulator.</param>
    /// <param name="role">The constrained plain role.</param>
    /// <param name="constrainedClass">The named class the constraint names.</param>
    /// <returns><see langword="true"/> — the constraint is always recorded, and its membership in the template's own list is checked once the shape resolves.</returns>
    private static bool AddRoleClass(List<BijectionChainRoleClass> constraintsToAppendTo, Utf8String role, Utf8String constrainedClass)
    {
        constraintsToAppendTo.Add(new BijectionChainRoleClass(role, constrainedClass));

        return true;
    }

    /// <summary>Reads one told equivalence in the given side order into the template's parts: a named non-constant class paired with an enumeration, a plain-role existential into a named class, or an unqualified exact cardinality of at least one over a plain role.</summary>
    /// <param name="defined">The candidate defined side.</param>
    /// <param name="defining">The candidate defining side.</param>
    /// <param name="parts">The parts accumulator.</param>
    /// <returns><see langword="true"/> when the side order matched a template shape.</returns>
    private static bool TryReadTowerSide(OwlClassExpression defined, OwlClassExpression defining, BijectionChainTowerParts parts)
    {
        if(!TryReadClass(defined, out Utf8String definedClass))
        {
            return false;
        }

        switch(defining)
        {
            case(OwlObjectOneOf oneOf):
            {
                HashSet<Utf8String> distinct = [];
                List<Utf8String> members = [];
                if(!TryReadNamedMemberList(oneOf, distinct, members))
                {
                    return false;
                }

                parts.Enumerations.Add(new BijectionChainEnumeration(definedClass, members));

                return true;
            }
            case(OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference role } existential):
            {
                if(!TryReadClass(existential.Filler, out Utf8String filler))
                {
                    return false;
                }

                parts.Existentials.Add(new BijectionChainExistentialDefinition(definedClass, role.Named.Iri, filler));

                return true;
            }
            case(OwlObjectCardinality { Kind: OwlCardinalityKind.Exact, Property: OwlObjectPropertyReference counted } cardinality):
            {
                if(!ContextHabitatRecognizer.IsUnqualifiedFiller(cardinality.Filler) || cardinality.Cardinality < 1)
                {
                    return false;
                }

                parts.Exacts.Add(new BijectionChainExactDefinition(definedClass, counted.Named.Iri, cardinality.Cardinality));

                return true;
            }
            default:
            {
                return false;
            }
        }
    }

    /// <summary>Resolves the bucketed parts into the diamond template's three levels, six roles, and three level constants; any count, pairing, or filler outside the template leaves the shape unresolved.</summary>
    /// <param name="parts">The bucketed parts.</param>
    /// <param name="shape">The resolved template; the default value when the parts did not form one.</param>
    /// <returns><see langword="true"/> when the parts resolve to the template.</returns>
    private static bool TryResolveTower(BijectionChainTowerParts parts, out BijectionChainTowerShape shape)
    {
        shape = default;
        if(parts.FunctionalRoles.Count != 3
            || parts.InversePairs.Count != 3
            || parts.Enumerations.Count != 1
            || parts.Existentials.Count != 3
            || parts.Exacts.Count != 3
            || parts.Enumerations[0].Members.Count != 1)
        {
            return false;
        }

        Utf8String anchor = parts.Enumerations[0].Class;
        List<BijectionChainExactDefinition> anchorCounts = [];
        List<BijectionChainExactDefinition> midCounts = [];
        for(int index = 0; index < parts.Exacts.Count; index++)
        {
            if(parts.Exacts[index].Class.Equals(anchor))
            {
                anchorCounts.Add(parts.Exacts[index]);
                continue;
            }

            midCounts.Add(parts.Exacts[index]);
        }

        if(anchorCounts.Count != 2 || midCounts.Count != 1)
        {
            return false;
        }

        Utf8String mid = midCounts[0].Class;
        BijectionChainExistentialDefinition midStep = default;
        int midSteps = 0;
        List<BijectionChainExistentialDefinition> topSteps = [];
        for(int index = 0; index < parts.Existentials.Count; index++)
        {
            if(parts.Existentials[index].Class.Equals(mid))
            {
                midStep = parts.Existentials[index];
                midSteps++;
                continue;
            }

            topSteps.Add(parts.Existentials[index]);
        }

        if(midSteps != 1 || topSteps.Count != 2 || !midStep.Filler.Equals(anchor))
        {
            return false;
        }

        Utf8String top = topSteps[0].Class;
        if(!topSteps[1].Class.Equals(top) || top.Equals(anchor) || top.Equals(mid))
        {
            return false;
        }

        if(!TryOrderTopSteps(topSteps, mid, anchor, out Utf8String topMidRole, out Utf8String topAnchorRole))
        {
            return false;
        }

        if(!TrySoleInversePartner(parts.InversePairs, midStep.Role, out Utf8String midStepInverse)
            || !TrySoleInversePartner(parts.InversePairs, topMidRole, out Utf8String topMidInverse)
            || !TrySoleInversePartner(parts.InversePairs, topAnchorRole, out Utf8String topAnchorInverse)
            || !midCounts[0].Role.Equals(topMidInverse)
            || !TryOrderAnchorCounts(anchorCounts, midStepInverse, topAnchorInverse, out long midLevel, out long topLevel))
        {
            return false;
        }

        shape = new BijectionChainTowerShape(
            anchor,
            mid,
            top,
            midStep.Role,
            topMidRole,
            topAnchorRole,
            midStepInverse,
            topMidInverse,
            topAnchorInverse,
            midLevel,
            midCounts[0].Cardinality,
            topLevel);

        return true;
    }

    /// <summary>Orders the top level's two existentials by filler: one runs into the mid level, the other into the anchor.</summary>
    /// <param name="topSteps">The top level's two existential definitions.</param>
    /// <param name="mid">The mid level.</param>
    /// <param name="anchor">The anchor level.</param>
    /// <param name="topMidRole">The role running into the mid level; the default value when the fillers did not split.</param>
    /// <param name="topAnchorRole">The role running into the anchor; the default value when the fillers did not split.</param>
    /// <returns><see langword="true"/> when the two fillers are exactly the mid level and the anchor.</returns>
    private static bool TryOrderTopSteps(List<BijectionChainExistentialDefinition> topSteps, Utf8String mid, Utf8String anchor, out Utf8String topMidRole, out Utf8String topAnchorRole)
    {
        if(topSteps[0].Filler.Equals(mid) && topSteps[1].Filler.Equals(anchor))
        {
            topMidRole = topSteps[0].Role;
            topAnchorRole = topSteps[1].Role;

            return true;
        }

        if(topSteps[1].Filler.Equals(mid) && topSteps[0].Filler.Equals(anchor))
        {
            topMidRole = topSteps[1].Role;
            topAnchorRole = topSteps[0].Role;

            return true;
        }

        topMidRole = default;
        topAnchorRole = default;

        return false;
    }

    /// <summary>Orders the anchor's two told counts by the role each counts: one counts the mid level's step inverse, the other the top level's anchor-step inverse.</summary>
    /// <param name="anchorCounts">The anchor's two exact-cardinality definitions.</param>
    /// <param name="midStepInverse">The mid level's step inverse.</param>
    /// <param name="topAnchorInverse">The top level's anchor-step inverse.</param>
    /// <param name="midLevel">The mid level's size; zero when the roles did not split.</param>
    /// <param name="topLevel">The top level's size; zero when the roles did not split.</param>
    /// <returns><see langword="true"/> when the two counted roles are exactly the two inverses.</returns>
    private static bool TryOrderAnchorCounts(List<BijectionChainExactDefinition> anchorCounts, Utf8String midStepInverse, Utf8String topAnchorInverse, out long midLevel, out long topLevel)
    {
        if(anchorCounts[0].Role.Equals(midStepInverse) && anchorCounts[1].Role.Equals(topAnchorInverse))
        {
            midLevel = anchorCounts[0].Cardinality;
            topLevel = anchorCounts[1].Cardinality;

            return true;
        }

        if(anchorCounts[1].Role.Equals(midStepInverse) && anchorCounts[0].Role.Equals(topAnchorInverse))
        {
            midLevel = anchorCounts[1].Cardinality;
            topLevel = anchorCounts[0].Cardinality;

            return true;
        }

        midLevel = 0;
        topLevel = 0;

        return false;
    }

    /// <summary>Reads a role's told inverse partner, demanding that EXACTLY ONE told pair mentions the role — a role paired twice leaves the template's role assignment ambiguous.</summary>
    /// <param name="pairs">The told inverse pairs.</param>
    /// <param name="role">The role whose partner is read.</param>
    /// <param name="partner">The partner role; the default value when the role is not paired exactly once.</param>
    /// <returns><see langword="true"/> when exactly one pair mentions the role.</returns>
    private static bool TrySoleInversePartner(List<BijectionChainRolePair> pairs, Utf8String role, out Utf8String partner)
    {
        partner = default;
        int found = 0;
        for(int index = 0; index < pairs.Count; index++)
        {
            if(pairs[index].First.Equals(role))
            {
                partner = pairs[index].Second;
                found++;
                continue;
            }

            if(pairs[index].Second.Equals(role))
            {
                partner = pairs[index].First;
                found++;
            }
        }

        return found == 1;
    }

    /// <summary>Whether the template's six role names are pairwise distinct — the canonical model keeps its three edge families on separate roles, and a coinciding name would merge them.</summary>
    /// <param name="shape">The resolved template.</param>
    /// <returns><see langword="true"/> on six distinct names.</returns>
    private static bool HasDistinctTowerRoles(BijectionChainTowerShape shape)
    {
        Utf8String[] roles = [shape.MidStepRole, shape.TopMidRole, shape.TopAnchorRole, shape.MidStepInverse, shape.TopMidInverse, shape.TopAnchorInverse];
        for(int first = 0; first < roles.Length; first++)
        {
            for(int second = first + 1; second < roles.Length; second++)
            {
                if(roles[first].Equals(roles[second]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether the told functionality characteristics are exactly the template's three forward roles — the count is already three, so covering all three forward roles fixes the set.</summary>
    /// <param name="parts">The bucketed parts.</param>
    /// <param name="shape">The resolved template.</param>
    /// <returns><see langword="true"/> when the three forward roles are the told functional ones.</returns>
    private static bool HasTowerCharacteristics(BijectionChainTowerParts parts, BijectionChainTowerShape shape)
    {
        return ContainsRole(parts.FunctionalRoles, shape.MidStepRole)
            && ContainsRole(parts.FunctionalRoles, shape.TopMidRole)
            && ContainsRole(parts.FunctionalRoles, shape.TopAnchorRole);
    }

    /// <summary>Whether a role list names a role.</summary>
    /// <param name="roles">The role list.</param>
    /// <param name="role">The role to find.</param>
    /// <returns><see langword="true"/> when the list names the role.</returns>
    private static bool ContainsRole(List<Utf8String> roles, Utf8String role)
    {
        for(int index = 0; index < roles.Count; index++)
        {
            if(roles[index].Equals(role))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether every told domain and range axiom is one the canonical model's edge families already honour: the mid level's step runs from the mid level into the anchor, and both top-level steps run from the top level into the mid level and the anchor.</summary>
    /// <param name="parts">The bucketed parts.</param>
    /// <param name="shape">The resolved template.</param>
    /// <returns><see langword="true"/> when every constraint lies in the template's own list.</returns>
    private static bool HasTowerDomainsAndRanges(BijectionChainTowerParts parts, BijectionChainTowerShape shape)
    {
        for(int index = 0; index < parts.Domains.Count; index++)
        {
            BijectionChainRoleClass domain = parts.Domains[index];
            bool admitted = (domain.Role.Equals(shape.MidStepRole) && domain.Class.Equals(shape.Mid))
                || (domain.Role.Equals(shape.TopMidRole) && domain.Class.Equals(shape.Top))
                || (domain.Role.Equals(shape.TopAnchorRole) && domain.Class.Equals(shape.Top));
            if(!admitted)
            {
                return false;
            }
        }

        for(int index = 0; index < parts.Ranges.Count; index++)
        {
            BijectionChainRoleClass range = parts.Ranges[index];
            bool admitted = (range.Role.Equals(shape.MidStepRole) && range.Class.Equals(shape.Anchor))
                || (range.Role.Equals(shape.TopMidRole) && range.Class.Equals(shape.Mid))
                || (range.Role.Equals(shape.TopAnchorRole) && range.Class.Equals(shape.Anchor));
            if(!admitted)
            {
                return false;
            }
        }

        return true;
    }
}
