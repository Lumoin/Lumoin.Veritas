using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// The Shape G window measurement the census-first recognizer's
/// pre-clausification pass reads on every gadget-jurisdiction module — the
/// compiled atom counts landed BEFORE any boundary comparison, so the battery's
/// near-miss rows can pin the measured quantity independently of the
/// comparison's outcome, together with the assignments the deciding walk
/// actually visited.
/// </summary>
/// <param name="PropertyAtomCount">The gadget-property atoms the compilation minted — one per property carrying a bare boolean cardinality gadget; zero when the jurisdiction rejected the module.</param>
/// <param name="FreeClassAtomCount">The free-class atoms the compilation minted — one per named class carrying no equivalence; zero when the jurisdiction rejected the module.</param>
/// <param name="EvaluatedVectorCount">The assignments the walk evaluated: zero on every silent and every census-only pass, the first passing assignment's index plus one on a certification, and the whole <c>2^F</c> free space on a refutation, where <c>F</c> counts the atoms surviving defined-atom elimination.</param>
/// <param name="AtomSilences">One when the atoms surviving defined-atom elimination exceeded <see cref="ContextBooleanGadgetDecider.GadgetAtomBound"/> — a named silence, never a verdict over an unwalked assignment space; zero otherwise. The two measured atom counts land raw, before the elimination and before this comparison.</param>
internal readonly record struct BooleanGadgetWindow(
    int PropertyAtomCount,
    int FreeClassAtomCount,
    int EvaluatedVectorCount,
    int AtomSilences)
{
    /// <summary>The empty window: no gadget theory was compiled.</summary>
    public static BooleanGadgetWindow Empty => default;

    /// <summary>The compiled atom total <c>B</c> the window bound compares — the gadget-property atoms beside the free-class atoms.</summary>
    public int AtomCount
    {
        get
        {
            return PropertyAtomCount + FreeClassAtomCount;
        }
    }
}

/// <summary>
/// The gadget faces' CONSTRUCTION-ONLY variation point, accepted only by the
/// internal entry points. The value left at <see langword="default"/> is
/// bit-identical to production. A construction variation is safe here: the walk
/// re-evaluates the whole compiled theory on every induced assignment, so a
/// non-default construction can produce a wider enumeration or a different
/// visit order and never a wrong verdict.
/// </summary>
/// <param name="SuppressDefinedAtomElimination">Whether every compiled atom is enumerated instead of the defined ones being computed; production computes the defined ones.</param>
internal readonly record struct GadgetConstruction(bool SuppressDefinedAtomElimination);

/// <summary>The Shape G decider's outcome: the bounded-evaluation verdict when every jurisdiction condition held inside the window, and the window measurement the census carries unconditionally.</summary>
/// <param name="Consistent">The verdict — <see langword="true"/> for the witnessed model built from the first passing assignment, <see langword="false"/> for the exhaustion refutation — or <see langword="null"/> when the face is silent on the module.</param>
/// <param name="Window">The window measurement.</param>
internal readonly record struct BooleanGadgetOutcome(bool? Consistent, BooleanGadgetWindow Window)
{
    /// <summary>The silent outcome carrying only the window measurement.</summary>
    /// <param name="window">The measured window.</param>
    /// <returns>The silent outcome.</returns>
    public static BooleanGadgetOutcome SilentWith(BooleanGadgetWindow window)
    {
        return new BooleanGadgetOutcome(null, window);
    }
}

/// <summary>
/// The enumeration-CSP habitat decider's boolean-cardinality-gadget faces
/// (faces five and six): a tier-3 BOUNDED ASSIGNMENT EVALUATION over the told
/// axiom surfaces of a propositional gadget module — named classes defined by
/// bare unqualified cardinality gadgets (<c>min 1</c>, <c>max 0</c>, or
/// <c>exact 0</c>, on object or data properties) and by intersections of named
/// classes, linked by named-to-named subclass axioms, over an ABox of one
/// individual carrying one class assertion. The module compiles to a
/// propositional theory with one atom per gadget property — "the element has at
/// least one successor on this property" — and one atom per free class. A class
/// the told axioms define BOTH by a gadget restriction and by a further
/// definition pins its gadget atom to the further definition's value — the
/// agreement the definitions must reach makes the atom a DEFINED bit, computed
/// rather than enumerated — so the face walks every assignment of the SURVIVING
/// FREE atoms, inducing the defined ones in a compiled topological order and
/// re-evaluating the whole compiled theory on each induced assignment: the
/// first assignment that satisfies every definition agreement, every subclass
/// implication, and the typed individual's obligation certifies the module
/// CONSISTENT (the explicit witness model the assignment induces, self-loops
/// for object gadgets and one literal for data gadgets), and an exhausted free
/// space refutes it INCONSISTENT (every model induces a passing assignment at
/// the typed individual, and every passing assignment agrees with its own
/// induced defined bits, so it appears in the free enumeration). The
/// elimination carries no soundness weight — an atom no acyclic definer
/// reaches simply stays free — and the whole-theory re-check on every induced
/// assignment is what both verdict directions stand on.
/// A fixed modal PRELUDE is admitted beside the propositional core: a typed
/// class defined as <c>⊓(named conjuncts…, ∃outer.A)</c> with
/// <c>A ≡ ⊓(∃inner.PMerge, ≤1 inner)</c> and a told inverse linking the two
/// roles, whose entire semantic effect at the typed individual is the
/// at-most-one merge that forces it into <c>PMerge</c> — no unique-name
/// assumption used. Sound-or-silent and told-only: the jurisdiction is a
/// closed-world whole-module admission, and ANY unmet condition — an
/// unrecognized axiom kind, a non-boolean bound, a qualified filler, a
/// definition cycle, a mis-shaped or aliased prelude, an ABox extra — leaves
/// the module to ordinary saturation. The atom total is a named window
/// constant; outside it the face is silent with the measured counts already on
/// the record.
/// </summary>
internal static class ContextBooleanGadgetDecider
{
    /// <summary>
    /// The atom ceiling: the assignment walk is exact up to this many atoms
    /// SURVIVING defined-atom elimination and SILENT above it — a module whose
    /// raw atom total exceeds the ceiling still decides when enough of its
    /// atoms are definitionally pinned, which is the elimination's window
    /// headroom. Derivation (engineering, with the corpus clearance the battery
    /// pins): <c>2^16 = 65,536</c> assignments, each one linear pass over the
    /// compiled definition, implication, and obligation arrays, stays
    /// microseconds-cheap and allocation-free, and the value matches the
    /// counting faces' shared sixteen ceiling so every counting-family
    /// pre-engine face carries one boundary discipline; the repairing face
    /// carries its own wider windows sized by its habitat. The corpus maximum
    /// is eight raw atoms — two-fold margin in atoms before any elimination,
    /// two-hundred-fifty-six-fold in assignments. Compiling the theory and
    /// planning the elimination are together one sweep bounded by the module's
    /// own axiom count rather than by this constant.
    /// </summary>
    public const int GadgetAtomBound = 16;

    /// <summary>Measures the Shape G census window without deciding anything: the compiled gadget-property and free-class atom counts and the window-exceeded silence the bound would charge — computed identically dark and lit, so the census ships unconditionally. No assignment is ever evaluated on this path.</summary>
    /// <param name="module">The module to measure.</param>
    /// <returns>The silent outcome carrying the measurement; all-zero when the jurisdiction rejects the module.</returns>
    public static BooleanGadgetOutcome Measure(ReasoningModule module)
    {
        return TryCompile(module, construction: default, out GadgetTheory? theory)
            ? BooleanGadgetOutcome.SilentWith(MeasureWindow(theory, evaluatedVectors: 0))
            : BooleanGadgetOutcome.SilentWith(BooleanGadgetWindow.Empty);
    }

    /// <summary>
    /// Runs the gadget faces under the production construction: the closed-world
    /// jurisdiction admission, the definition-graph resolution and defined-atom
    /// elimination, the free-atom window check, and then the bounded walk over
    /// the surviving assignment space that answers the module. The measurement
    /// lands first in every case, so a window silence still carries the counts.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <returns>The outcome: a bounded-evaluation verdict with its measurement, or silence.</returns>
    public static BooleanGadgetOutcome Run(ReasoningModule module)
    {
        return Run(module, construction: default);
    }

    /// <summary>Measures the compiled atom space without walking: the raw atom total and the atoms surviving defined-atom elimination under the given construction — the instrument surface the corpus head-to-head pins <c>B_free &lt;= B_raw</c> on. No assignment is ever evaluated on this path.</summary>
    /// <param name="module">The module to measure.</param>
    /// <param name="construction">The construction variation.</param>
    /// <param name="rawAtomCount">The raw atom total <c>B</c>; zero when the jurisdiction rejects the module.</param>
    /// <param name="freeAtomCount">The surviving free atoms <c>F</c>; zero when the jurisdiction rejects the module.</param>
    /// <returns><see langword="true"/> when the jurisdiction admitted the module.</returns>
    public static bool TryMeasureAtomSpace(ReasoningModule module, GadgetConstruction construction, out int rawAtomCount, out int freeAtomCount)
    {
        if(!TryCompile(module, construction, out GadgetTheory? theory))
        {
            rawAtomCount = 0;
            freeAtomCount = 0;

            return false;
        }

        rawAtomCount = theory.AtomCount;
        freeAtomCount = theory.FreeAtomCount;

        return true;
    }

    /// <summary>Runs the gadget faces under an explicit construction — the seam the battery's head-to-head rows and the corpus instrument drive; the <see langword="default"/> construction is exactly the production path.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="construction">The construction variation.</param>
    /// <returns>The outcome: a bounded-evaluation verdict with its measurement, or silence.</returns>
    public static BooleanGadgetOutcome Run(ReasoningModule module, GadgetConstruction construction)
    {
        if(!TryCompile(module, construction, out GadgetTheory? theory))
        {
            return BooleanGadgetOutcome.SilentWith(BooleanGadgetWindow.Empty);
        }

        BooleanGadgetWindow window = MeasureWindow(theory, evaluatedVectors: 0);
        if(window.AtomSilences > 0)
        {
            return BooleanGadgetOutcome.SilentWith(window);
        }

        bool consistent = TryFindPassingVector(theory, out int evaluatedVectors);

        return new BooleanGadgetOutcome(consistent, MeasureWindow(theory, evaluatedVectors));
    }

    /// <summary>The definition flavour a determined class's told equivalence carries.</summary>
    private enum GadgetDefinitionKind
    {
        /// <summary>A bare boolean cardinality gadget over one property: the class holds exactly when the property atom does, negated for the <c>max 0</c> and <c>exact 0</c> spellings.</summary>
        GadgetRestriction = 0,

        /// <summary>An intersection of named-class references: the class holds exactly when every operand does.</summary>
        NamedIntersection = 1,
    }

    /// <summary>One told definition of a determined class, compiled into index form.</summary>
    /// <param name="Kind">The definition flavour.</param>
    /// <param name="PropertyAtom">The gadget property's atom index on a <see cref="GadgetDefinitionKind.GadgetRestriction"/>; minus one otherwise.</param>
    /// <param name="Negated">Whether the gadget restriction denies the property atom — the <c>max 0</c> and <c>exact 0</c> spellings; <see langword="false"/> otherwise.</param>
    /// <param name="OperandStart">The definition's first operand slot in the compiled operand array; zero on a gadget restriction.</param>
    /// <param name="OperandCount">The definition's operand count; zero on a gadget restriction.</param>
    private readonly record struct GadgetDefinition(GadgetDefinitionKind Kind, int PropertyAtom, bool Negated, int OperandStart, int OperandCount);

    /// <summary>One compiled named class: a free atom, or a determined class whose contiguous definition slice the resolution reads.</summary>
    /// <param name="AtomIndex">The class's own atom index when it is free — no equivalence defines it; minus one on a determined class.</param>
    /// <param name="DefinitionStart">The class's first definition slot in the compiled definition array.</param>
    /// <param name="DefinitionCount">The class's definition count; zero on a free class, and above one when the told axioms define it several times and the definitions must agree.</param>
    private readonly record struct GadgetClass(int AtomIndex, int DefinitionStart, int DefinitionCount);

    /// <summary>One told named-to-named subclass implication, compiled into index form.</summary>
    /// <param name="SubClass">The subclass's compiled index.</param>
    /// <param name="SuperClass">The superclass's compiled index.</param>
    private readonly record struct GadgetImplication(int SubClass, int SuperClass);

    /// <summary>The admitted modal prelude: the typed class's single existential definition, its anchor class's merge definition, and the two roles the told inverse links. The typed class and the anchor live in the fixed two-element witness construction, never in the per-assignment resolution.</summary>
    /// <param name="AnchorClass">The anchor class <c>A</c> the typed class's existential reaches.</param>
    /// <param name="MergeClass">The class <c>PMerge</c> the at-most-one merge forces onto the typed individual — distinct from the anchor and from the typed class.</param>
    /// <param name="OuterRole">The role the typed class's existential runs over.</param>
    /// <param name="InnerRole">The role the anchor's existential and cardinality conjuncts run over.</param>
    /// <param name="Conjuncts">The typed class definition's named conjuncts, in told order; empty on the bare existential spelling.</param>
    private sealed record GadgetPrelude(
        Utf8String AnchorClass,
        Utf8String MergeClass,
        Utf8String OuterRole,
        Utf8String InnerRole,
        IReadOnlyList<Utf8String> Conjuncts);

    /// <summary>One atom's standing in the elimination plan.</summary>
    private enum GadgetAtomState
    {
        /// <summary>The atom is enumerated: no definer candidates exist, or the plan demoted it after none proved acyclic.</summary>
        Free = 0,

        /// <summary>The atom carries at least one definer candidate the plan has not yet selected or demoted; a pending atom blocks every definition reading it.</summary>
        Pending = 1,

        /// <summary>The atom is computed from its selected definer and never enumerated.</summary>
        Defined = 2,
    }

    /// <summary>One step of the compiled joint resolution order the assignment induction executes per enumerated free vector: resolve one class's value from one named definition, or compute one defined property atom from its definer.</summary>
    /// <param name="ComputesAtom">Whether the step computes a defined property atom; otherwise it resolves a class.</param>
    /// <param name="Target">The property atom index computed, or the class index resolved.</param>
    /// <param name="Definition">The definition slot the step evaluates: the definer on an atom step, the class's first definition on a class step, and minus one on a free-class step that reads its own atom.</param>
    /// <param name="Inverted">Whether the computed atom denies the evaluated definer — the defining gadget restriction's negation; <see langword="false"/> on a class step.</param>
    private readonly record struct GadgetComputationStep(bool ComputesAtom, int Target, int Definition, bool Inverted);

    /// <summary>The compiled propositional theory: the atom counts, the class table with its definition slices in resolution order, the told implications, the typed individual's obligation, and the elimination plan the walk enumerates under.</summary>
    /// <param name="PropertyAtomCount">The gadget-property atoms, occupying the low atom indices.</param>
    /// <param name="FreeClassAtomCount">The free-class atoms, occupying the atom indices above the gadget-property block.</param>
    /// <param name="Classes">The compiled class table.</param>
    /// <param name="Definitions">The compiled definitions, grouped by owning class.</param>
    /// <param name="Operands">The compiled intersection operands, sliced by definition.</param>
    /// <param name="EvaluationOrder">The class indices in dependency order — every operand of a class resolves before the class itself.</param>
    /// <param name="Implications">The compiled told subclass implications.</param>
    /// <param name="Obligations">The class indices the typed individual's obligation forces true.</param>
    /// <param name="FreeAtomCount">The atoms surviving defined-atom elimination — the count <c>F</c> the walk enumerates and the window bound compares.</param>
    /// <param name="AtomFreeSlots">Per atom, its enumeration bit position when it survives as free, and minus one when it is defined and computed.</param>
    /// <param name="ComputationSteps">The joint resolution order inducing each full assignment from an enumerated free vector; empty when no atom is defined, which makes the walk the raw enumeration.</param>
    private sealed record GadgetTheory(
        int PropertyAtomCount,
        int FreeClassAtomCount,
        GadgetClass[] Classes,
        GadgetDefinition[] Definitions,
        int[] Operands,
        int[] EvaluationOrder,
        GadgetImplication[] Implications,
        int[] Obligations,
        int FreeAtomCount,
        int[] AtomFreeSlots,
        GadgetComputationStep[] ComputationSteps)
    {
        /// <summary>The raw atom total <c>B</c> the compilation minted, before any elimination.</summary>
        public int AtomCount
        {
            get
            {
                return PropertyAtomCount + FreeClassAtomCount;
            }
        }
    }

    /// <summary>The mutable accumulator one compilation pass fills: the interned classes and gadget properties, the raw definitions with their owners, and the operand, implication, and obligation lists the compiled arrays are cut from.</summary>
    private sealed class GadgetCompilation
    {
        /// <summary>The interned named classes, in first-seen order; the list index is the compiled class index.</summary>
        public List<Utf8String> ClassNames { get; } = [];

        /// <summary>The compiled class index by class IRI.</summary>
        private Dictionary<Utf8String, int> ClassIndices { get; } = [];

        /// <summary>The gadget-property atom index by property IRI — object and data properties share one index space, as the atom states successor existence for either.</summary>
        public Dictionary<Utf8String, int> PropertyAtoms { get; } = [];

        /// <summary>The class IRIs barred from interning — the prelude's typed class and anchor class, which the fixed witness construction carries; empty without a prelude.</summary>
        public List<Utf8String> BarredClasses { get; } = [];

        /// <summary>The definitions in told order, before they are grouped by owning class.</summary>
        public List<GadgetDefinition> RawDefinitions { get; } = [];

        /// <summary>The owning class index of each raw definition, parallel to <see cref="RawDefinitions"/>.</summary>
        public List<int> RawOwners { get; } = [];

        /// <summary>The intersection operands, sliced by definition.</summary>
        public List<int> Operands { get; } = [];

        /// <summary>The told subclass implications.</summary>
        public List<GadgetImplication> Implications { get; } = [];

        /// <summary>The class indices the typed individual's obligation forces true.</summary>
        public List<int> Obligations { get; } = [];

        /// <summary>Interns one named class, minting its compiled index on first sight; a barred class rejects, which is how a prelude class occurring outside the prelude silences the module.</summary>
        /// <param name="name">The class IRI.</param>
        /// <param name="index">The compiled class index; minus one on rejection.</param>
        /// <returns><see langword="true"/> when the class is admitted.</returns>
        public bool TryIntern(Utf8String name, out int index)
        {
            for(int i = 0; i < BarredClasses.Count; i++)
            {
                if(BarredClasses[i].Equals(name))
                {
                    index = -1;

                    return false;
                }
            }

            if(ClassIndices.TryGetValue(name, out index))
            {
                return true;
            }

            index = ClassNames.Count;
            ClassNames.Add(name);
            ClassIndices.Add(name, index);

            return true;
        }

        /// <summary>Interns one gadget property, minting its atom index on first sight.</summary>
        /// <param name="name">The property IRI.</param>
        /// <returns>The property's atom index.</returns>
        public int InternProperty(Utf8String name)
        {
            if(PropertyAtoms.TryGetValue(name, out int atom))
            {
                return atom;
            }

            atom = PropertyAtoms.Count;
            PropertyAtoms.Add(name, atom);

            return atom;
        }
    }

    /// <summary>Reads the window off a compiled theory: the two measured raw atom counts, the assignments the caller walked, and the boundary silence the free-atom bound charges — the comparison runs over the atoms surviving elimination, so a definitionally composed module keeps its headroom.</summary>
    /// <param name="theory">The compiled theory.</param>
    /// <param name="evaluatedVectors">The assignments the caller evaluated; zero on the census-only and window-silent paths.</param>
    /// <returns>The window measurement.</returns>
    private static BooleanGadgetWindow MeasureWindow(GadgetTheory theory, int evaluatedVectors)
    {
        return new BooleanGadgetWindow(
            theory.PropertyAtomCount,
            theory.FreeClassAtomCount,
            evaluatedVectors,
            theory.FreeAtomCount > GadgetAtomBound ? 1 : 0);
    }

    /// <summary>
    /// The closed-world jurisdiction admission over the module's ENTIRE axiom
    /// set, followed by the compilation into propositional form: every axiom
    /// must be a class equivalence between a named class and either a bare
    /// boolean cardinality gadget or an intersection of named-class references,
    /// a subclass axiom between two named-class references, the optional
    /// prelude, the single ABox class assertion, or non-logical content —
    /// declarations, imports, and annotation axioms that assert no class or
    /// property constraint. Every other axiom kind, every restriction outside
    /// the admitted forms, every non-boolean or qualified cardinality, a
    /// definition graph that fails to resolve, and a module without a single
    /// gadget equivalence all reject. The atom counts are computed after the
    /// resolution succeeds, so a window-exceeding module still compiles and
    /// carries its measurement.
    /// </summary>
    /// <param name="module">The module to admit or reject.</param>
    /// <param name="construction">The construction variation the elimination plan honours.</param>
    /// <param name="theory">The compiled theory; <see langword="null"/> on rejection.</param>
    /// <returns><see langword="true"/> when every jurisdiction condition outside the window held.</returns>
    private static bool TryCompile(ReasoningModule module, GadgetConstruction construction, [NotNullWhen(true)] out GadgetTheory? theory)
    {
        theory = null;

        List<OwlEquivalentClassesAxiom> equivalences = [];
        List<OwlSubClassOfAxiom> subClasses = [];
        OwlClassAssertionAxiom? assertion = null;
        OwlInverseObjectPropertiesAxiom? inverse = null;
        int assertions = 0;
        int inverses = 0;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case(OwlDeclarationAxiom or OwlImportAxiom or OwlAnnotationAssertionAxiom or OwlSubAnnotationPropertyOfAxiom
                    or OwlAnnotationPropertyDomainAxiom or OwlAnnotationPropertyRangeAxiom):
                {
                    break;
                }
                case(OwlEquivalentClassesAxiom equivalence):
                {
                    equivalences.Add(equivalence);
                    break;
                }
                case(OwlSubClassOfAxiom subClass):
                {
                    subClasses.Add(subClass);
                    break;
                }
                case(OwlClassAssertionAxiom classAssertion):
                {
                    assertions++;
                    assertion = classAssertion;
                    break;
                }
                case(OwlInverseObjectPropertiesAxiom inverseAxiom):
                {
                    inverses++;
                    inverse = inverseAxiom;
                    break;
                }
                default:
                {
                    return false;
                }
            }
        }

        if(assertions != 1 || assertion is null
            || assertion.Class is not OwlClassReference typedReference
            || !ContextHabitatRecognizer.IsChainNodeClass(typedReference))
        {
            return false;
        }

        Utf8String typedClass = typedReference.Class.Iri;
        if(!TryReadPrelude(equivalences, inverse, inverses, typedClass, out GadgetPrelude? prelude))
        {
            return false;
        }

        GadgetCompilation compilation = new();
        if(prelude is not null)
        {
            compilation.BarredClasses.Add(typedClass);
            compilation.BarredClasses.Add(prelude.AnchorClass);
        }

        for(int i = 0; i < equivalences.Count; i++)
        {
            if(!TrySplitDefinition(equivalences[i], out OwlClassReference? defined, out OwlClassExpression? body))
            {
                return false;
            }

            Utf8String definedClass = defined.Class.Iri;
            if(prelude is not null && (definedClass.Equals(typedClass) || definedClass.Equals(prelude.AnchorClass)))
            {
                continue;
            }

            if(!compilation.TryIntern(definedClass, out int definedIndex) || !TryReadDefinition(body, compilation, definedIndex))
            {
                return false;
            }
        }

        for(int i = 0; i < subClasses.Count; i++)
        {
            if(subClasses[i].SubClass is not OwlClassReference sub || !ContextHabitatRecognizer.IsChainNodeClass(sub)
                || subClasses[i].SuperClass is not OwlClassReference super || !ContextHabitatRecognizer.IsChainNodeClass(super)
                || !compilation.TryIntern(sub.Class.Iri, out int subIndex)
                || !compilation.TryIntern(super.Class.Iri, out int superIndex))
            {
                return false;
            }

            compilation.Implications.Add(new GadgetImplication(subIndex, superIndex));
        }

        if(!TryReadObligation(compilation, prelude, typedClass) || compilation.PropertyAtoms.Count == 0)
        {
            return false;
        }

        GadgetClass[] classes = GroupDefinitions(compilation, out GadgetDefinition[] definitions, out int freeAtoms);
        int[] order = new int[classes.Length];
        if(!TryOrderClasses(classes, definitions, compilation.Operands, order))
        {
            return false;
        }

        PlanElimination(
            classes,
            definitions,
            compilation.Operands,
            compilation.PropertyAtoms.Count,
            compilation.PropertyAtoms.Count + freeAtoms,
            construction,
            out int[] atomFreeSlots,
            out int freeAtomCount,
            out GadgetComputationStep[] computationSteps);
        theory = new GadgetTheory(
            compilation.PropertyAtoms.Count,
            freeAtoms,
            classes,
            definitions,
            [.. compilation.Operands],
            order,
            [.. compilation.Implications],
            [.. compilation.Obligations],
            freeAtomCount,
            atomFreeSlots,
            computationSteps);

        return true;
    }

    /// <summary>
    /// Plans defined-atom elimination over the resolved class table. Every
    /// multiply-defined class carrying a gadget restriction is a candidate
    /// definer for that restriction's property atom — the agreement its
    /// definitions must reach makes the atom equal the sibling definition's
    /// value under the restriction's polarity — and the plan greedily selects,
    /// in deterministic index order, every definer whose value is computable
    /// from atoms already free or defined, interleaving the class resolutions
    /// the definers read. A pending atom no evaluable definer reaches when a
    /// round stalls is demoted to FREE one at a time, so a mutually dependent
    /// definer pair degrades to enumeration and never to silence: the plan
    /// carries no soundness weight, because the walk re-evaluates the whole
    /// theory on every induced assignment. Every class places before the loop
    /// ends, because with no atom left pending the placement condition is
    /// exactly the dependency order <see cref="TryOrderClasses"/> already
    /// proved. Suppressing the elimination leaves every atom free with an empty
    /// step order, which is exactly the raw walk.
    /// </summary>
    /// <param name="classes">The compiled class table.</param>
    /// <param name="definitions">The grouped definitions.</param>
    /// <param name="operands">The compiled operands.</param>
    /// <param name="propertyAtomCount">The gadget-property atoms, the only eliminable kind.</param>
    /// <param name="atomCount">The raw atom total, properties and free classes together.</param>
    /// <param name="construction">The construction variation; suppression takes the raw branch.</param>
    /// <param name="atomFreeSlots">Per atom, its enumeration bit position or minus one when defined.</param>
    /// <param name="freeAtomCount">The atoms surviving as free.</param>
    /// <param name="computationSteps">The joint resolution order; empty when no atom is defined.</param>
    private static void PlanElimination(
        GadgetClass[] classes,
        GadgetDefinition[] definitions,
        List<int> operands,
        int propertyAtomCount,
        int atomCount,
        GadgetConstruction construction,
        out int[] atomFreeSlots,
        out int freeAtomCount,
        out GadgetComputationStep[] computationSteps)
    {
        atomFreeSlots = new int[atomCount];
        if(construction.SuppressDefinedAtomElimination)
        {
            for(int atom = 0; atom < atomCount; atom++)
            {
                atomFreeSlots[atom] = atom;
            }

            freeAtomCount = atomCount;
            computationSteps = [];

            return;
        }

        GadgetAtomState[] atomStates = new GadgetAtomState[atomCount];
        for(int classIndex = 0; classIndex < classes.Length; classIndex++)
        {
            GadgetClass entry = classes[classIndex];
            if(entry.DefinitionCount < 2)
            {
                continue;
            }

            for(int offset = 0; offset < entry.DefinitionCount; offset++)
            {
                GadgetDefinition definition = definitions[entry.DefinitionStart + offset];
                if(definition.Kind == GadgetDefinitionKind.GadgetRestriction)
                {
                    atomStates[definition.PropertyAtom] = GadgetAtomState.Pending;
                }
            }
        }

        bool[] classPlaced = new bool[classes.Length];
        List<GadgetComputationStep> steps = [];
        bool progressed = true;
        while(progressed)
        {
            progressed = false;
            for(int classIndex = 0; classIndex < classes.Length; classIndex++)
            {
                if(classPlaced[classIndex] || !EveryDefinitionEvaluable(classes[classIndex], definitions, operands, atomStates, classPlaced))
                {
                    continue;
                }

                classPlaced[classIndex] = true;
                steps.Add(new GadgetComputationStep(ComputesAtom: false, classIndex, classes[classIndex].DefinitionCount == 0 ? -1 : classes[classIndex].DefinitionStart, Inverted: false));
                progressed = true;
            }

            for(int classIndex = 0; classIndex < classes.Length; classIndex++)
            {
                GadgetClass entry = classes[classIndex];
                if(entry.DefinitionCount < 2)
                {
                    continue;
                }

                for(int offset = 0; offset < entry.DefinitionCount; offset++)
                {
                    GadgetDefinition gadget = definitions[entry.DefinitionStart + offset];
                    if(gadget.Kind != GadgetDefinitionKind.GadgetRestriction || atomStates[gadget.PropertyAtom] != GadgetAtomState.Pending)
                    {
                        continue;
                    }

                    for(int siblingOffset = 0; siblingOffset < entry.DefinitionCount; siblingOffset++)
                    {
                        int siblingSlot = entry.DefinitionStart + siblingOffset;
                        if(siblingOffset == offset || !DefinitionEvaluable(definitions[siblingSlot], operands, atomStates, classPlaced))
                        {
                            continue;
                        }

                        atomStates[gadget.PropertyAtom] = GadgetAtomState.Defined;
                        steps.Add(new GadgetComputationStep(ComputesAtom: true, gadget.PropertyAtom, siblingSlot, gadget.Negated));
                        progressed = true;
                        break;
                    }
                }
            }

            if(!progressed)
            {
                for(int atom = 0; atom < propertyAtomCount; atom++)
                {
                    if(atomStates[atom] == GadgetAtomState.Pending)
                    {
                        atomStates[atom] = GadgetAtomState.Free;
                        progressed = true;
                        break;
                    }
                }
            }
        }

        freeAtomCount = 0;
        for(int atom = 0; atom < atomCount; atom++)
        {
            if(atomStates[atom] == GadgetAtomState.Defined)
            {
                atomFreeSlots[atom] = -1;
                continue;
            }

            atomFreeSlots[atom] = freeAtomCount;
            freeAtomCount++;
        }

        computationSteps = freeAtomCount == atomCount ? [] : [.. steps];
    }

    /// <summary>Whether every definition of one class is evaluable under the plan's current state.</summary>
    /// <param name="entry">The class's compiled entry.</param>
    /// <param name="definitions">The grouped definitions.</param>
    /// <param name="operands">The compiled operands.</param>
    /// <param name="atomStates">The per-atom plan states.</param>
    /// <param name="classPlaced">The per-class placement flags.</param>
    /// <returns><see langword="true"/> when the class may be placed.</returns>
    private static bool EveryDefinitionEvaluable(GadgetClass entry, GadgetDefinition[] definitions, List<int> operands, GadgetAtomState[] atomStates, bool[] classPlaced)
    {
        for(int offset = 0; offset < entry.DefinitionCount; offset++)
        {
            if(!DefinitionEvaluable(definitions[entry.DefinitionStart + offset], operands, atomStates, classPlaced))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether one definition is evaluable under the plan's current state: a gadget restriction needs its atom free or already defined, an intersection needs every operand class already placed.</summary>
    /// <param name="definition">The definition to test.</param>
    /// <param name="operands">The compiled operands.</param>
    /// <param name="atomStates">The per-atom plan states.</param>
    /// <param name="classPlaced">The per-class placement flags.</param>
    /// <returns><see langword="true"/> when the definition's value is computable.</returns>
    private static bool DefinitionEvaluable(GadgetDefinition definition, List<int> operands, GadgetAtomState[] atomStates, bool[] classPlaced)
    {
        if(definition.Kind == GadgetDefinitionKind.GadgetRestriction)
        {
            return atomStates[definition.PropertyAtom] != GadgetAtomState.Pending;
        }

        for(int offset = 0; offset < definition.OperandCount; offset++)
        {
            if(!classPlaced[operands[definition.OperandStart + offset]])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads the typed individual's obligation into the compilation: the typed class alone on the pure shape, and every named conjunct of the prelude's definition together with the merge class on the prelude shape. The prelude's roles must be distinct from every gadget property, or the witness construction's edge disjointness fails.</summary>
    /// <param name="compilation">The compilation the obligation appends to.</param>
    /// <param name="prelude">The admitted prelude; <see langword="null"/> on the pure shape.</param>
    /// <param name="typedClass">The ABox assertion's class.</param>
    /// <returns><see langword="true"/> when the obligation is admitted.</returns>
    private static bool TryReadObligation(GadgetCompilation compilation, GadgetPrelude? prelude, Utf8String typedClass)
    {
        if(prelude is null)
        {
            if(!compilation.TryIntern(typedClass, out int typedIndex))
            {
                return false;
            }

            compilation.Obligations.Add(typedIndex);

            return true;
        }

        for(int i = 0; i < prelude.Conjuncts.Count; i++)
        {
            if(!compilation.TryIntern(prelude.Conjuncts[i], out int conjunctIndex))
            {
                return false;
            }

            compilation.Obligations.Add(conjunctIndex);
        }

        if(!compilation.TryIntern(prelude.MergeClass, out int mergeIndex))
        {
            return false;
        }

        compilation.Obligations.Add(mergeIndex);

        return !compilation.PropertyAtoms.ContainsKey(prelude.OuterRole) && !compilation.PropertyAtoms.ContainsKey(prelude.InnerRole);
    }

    /// <summary>Groups the raw definitions into contiguous per-class slices and mints one atom for every class no definition determines.</summary>
    /// <param name="compilation">The filled compilation.</param>
    /// <param name="definitions">The grouped definition array.</param>
    /// <param name="freeAtoms">The free-class atoms minted.</param>
    /// <returns>The compiled class table.</returns>
    private static GadgetClass[] GroupDefinitions(GadgetCompilation compilation, out GadgetDefinition[] definitions, out int freeAtoms)
    {
        definitions = new GadgetDefinition[compilation.RawDefinitions.Count];
        GadgetClass[] classes = new GadgetClass[compilation.ClassNames.Count];
        int propertyAtoms = compilation.PropertyAtoms.Count;
        int cursor = 0;
        freeAtoms = 0;
        for(int classIndex = 0; classIndex < classes.Length; classIndex++)
        {
            int start = cursor;
            for(int i = 0; i < compilation.RawDefinitions.Count; i++)
            {
                if(compilation.RawOwners[i] == classIndex)
                {
                    definitions[cursor] = compilation.RawDefinitions[i];
                    cursor++;
                }
            }

            int count = cursor - start;
            classes[classIndex] = new GadgetClass(count == 0 ? propertyAtoms + freeAtoms : -1, start, count);
            if(count == 0)
            {
                freeAtoms++;
            }
        }

        return classes;
    }

    /// <summary>Orders the class table so every intersection operand resolves before the class it defines — an explicit sweep run to fixpoint, no recursion. A class the sweep never places sits on a definition cycle, which silences the module.</summary>
    /// <param name="classes">The compiled class table.</param>
    /// <param name="definitions">The grouped definitions.</param>
    /// <param name="operands">The compiled operands.</param>
    /// <param name="orderToAppendTo">The order array the placed class indices fill, in placement order.</param>
    /// <returns><see langword="true"/> when every class was placed.</returns>
    private static bool TryOrderClasses(GadgetClass[] classes, GadgetDefinition[] definitions, List<int> operands, int[] orderToAppendTo)
    {
        bool[] resolved = new bool[classes.Length];
        int placed = 0;
        bool progressed = true;
        while(progressed && placed < classes.Length)
        {
            progressed = false;
            for(int classIndex = 0; classIndex < classes.Length; classIndex++)
            {
                if(resolved[classIndex] || !DependenciesResolved(classes[classIndex], definitions, operands, resolved))
                {
                    continue;
                }

                resolved[classIndex] = true;
                orderToAppendTo[placed] = classIndex;
                placed++;
                progressed = true;
            }
        }

        return placed == classes.Length;
    }

    /// <summary>Whether every operand of every definition of one class is already placed.</summary>
    /// <param name="entry">The class's compiled entry.</param>
    /// <param name="definitions">The grouped definitions.</param>
    /// <param name="operands">The compiled operands.</param>
    /// <param name="resolved">The placement flags.</param>
    /// <returns><see langword="true"/> when the class may be placed.</returns>
    private static bool DependenciesResolved(GadgetClass entry, GadgetDefinition[] definitions, List<int> operands, bool[] resolved)
    {
        for(int offset = 0; offset < entry.DefinitionCount; offset++)
        {
            GadgetDefinition definition = definitions[entry.DefinitionStart + offset];
            for(int operandOffset = 0; operandOffset < definition.OperandCount; operandOffset++)
            {
                if(!resolved[operands[definition.OperandStart + operandOffset]])
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Splits one told equivalence into the named class it defines and the body defining it; an equivalence whose sides are not exactly one named non-constant class and one non-reference expression rejects.</summary>
    /// <param name="equivalence">The told equivalence axiom.</param>
    /// <param name="defined">The defined named class; <see langword="null"/> on rejection.</param>
    /// <param name="body">The defining expression; <see langword="null"/> on rejection.</param>
    /// <returns><see langword="true"/> on the definition shape.</returns>
    private static bool TrySplitDefinition(OwlEquivalentClassesAxiom equivalence, [NotNullWhen(true)] out OwlClassReference? defined, [NotNullWhen(true)] out OwlClassExpression? body)
    {
        if(equivalence is { First: OwlClassReference first, Second: not OwlClassReference } && ContextHabitatRecognizer.IsChainNodeClass(first))
        {
            defined = first;
            body = equivalence.Second;

            return true;
        }

        if(equivalence is { First: not OwlClassReference, Second: OwlClassReference second } && ContextHabitatRecognizer.IsChainNodeClass(second))
        {
            defined = second;
            body = equivalence.First;

            return true;
        }

        defined = null;
        body = null;

        return false;
    }

    /// <summary>Reads one definition body into the compilation: a bare boolean cardinality gadget, or an intersection whose operands are all named non-constant class references. Every other body shape rejects.</summary>
    /// <param name="body">The defining expression.</param>
    /// <param name="compilation">The compilation the definition appends to.</param>
    /// <param name="ownerIndex">The defined class's compiled index.</param>
    /// <returns><see langword="true"/> when the body lies within the admitted grammar.</returns>
    private static bool TryReadDefinition(OwlClassExpression body, GadgetCompilation compilation, int ownerIndex)
    {
        if(TryReadGadgetRestriction(body, out Utf8String property, out bool negated))
        {
            compilation.RawDefinitions.Add(new GadgetDefinition(GadgetDefinitionKind.GadgetRestriction, compilation.InternProperty(property), negated, OperandStart: 0, OperandCount: 0));
            compilation.RawOwners.Add(ownerIndex);

            return true;
        }

        if(body is not OwlObjectIntersectionOf intersection || intersection.Operands.Count == 0)
        {
            return false;
        }

        int start = compilation.Operands.Count;
        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            if(intersection.Operands[i] is not OwlClassReference operand
                || !ContextHabitatRecognizer.IsChainNodeClass(operand)
                || !compilation.TryIntern(operand.Class.Iri, out int operandIndex))
            {
                return false;
            }

            compilation.Operands.Add(operandIndex);
        }

        compilation.RawDefinitions.Add(new GadgetDefinition(GadgetDefinitionKind.NamedIntersection, PropertyAtom: -1, Negated: false, start, intersection.Operands.Count));
        compilation.RawOwners.Add(ownerIndex);

        return true;
    }

    /// <summary>Reads one bare boolean cardinality gadget — an UNQUALIFIED minimum of one, maximum of zero, or exact zero over a named object property or a data property. A qualified filler or range, an inverse role, and every other bound reject: a wider bound changes the counting semantics the single successor-existence atom states.</summary>
    /// <param name="expression">The candidate definition body.</param>
    /// <param name="property">The gadget property's IRI; the default on rejection.</param>
    /// <param name="negated">Whether the gadget denies the property atom.</param>
    /// <returns><see langword="true"/> on a gadget restriction.</returns>
    private static bool TryReadGadgetRestriction(OwlClassExpression expression, out Utf8String property, out bool negated)
    {
        property = default;
        negated = false;
        switch(expression)
        {
            case(OwlObjectCardinality { Property: OwlObjectPropertyReference role } cardinality):
            {
                if(!ContextHabitatRecognizer.IsUnqualifiedFiller(cardinality.Filler) || !TryReadGadgetBound(cardinality.Kind, cardinality.Cardinality, out negated))
                {
                    return false;
                }

                property = role.Property.Iri;

                return true;
            }
            case(OwlDataCardinality { Range: null } dataCardinality):
            {
                if(!TryReadGadgetBound(dataCardinality.Kind, dataCardinality.Cardinality, out negated))
                {
                    return false;
                }

                property = dataCardinality.Property.Iri;

                return true;
            }
            default:
            {
                return false;
            }
        }
    }

    /// <summary>Reads one told cardinality bound as a boolean gadget: a minimum of one asserts the property atom, a maximum of zero and an exact zero deny it, and every other bound — including a bound that failed to parse as a nonnegative integer upstream — rejects.</summary>
    /// <param name="kind">The told cardinality flavour.</param>
    /// <param name="bound">The told bound.</param>
    /// <param name="negated">Whether the gadget denies the property atom.</param>
    /// <returns><see langword="true"/> on one of the three boolean forms.</returns>
    private static bool TryReadGadgetBound(OwlCardinalityKind kind, int bound, out bool negated)
    {
        negated = kind is not OwlCardinalityKind.Min;
        if(bound < 0)
        {
            return false;
        }

        return (kind, bound) switch
        {
            (OwlCardinalityKind.Min, 1) => true,
            (OwlCardinalityKind.Max, 0) => true,
            (OwlCardinalityKind.Exact, 0) => true,
            _ => false,
        };
    }

    /// <summary>
    /// Reads the optional modal prelude anchored at the typed class. The module
    /// carries a prelude exactly when the typed class has ONE told definition
    /// and that definition is the bare existential <c>∃outer.A</c> or an
    /// intersection of named conjuncts with exactly one such existential; then a
    /// told inverse must exist, the anchor class must carry exactly one
    /// definition of the form <c>⊓(∃inner.PMerge, ≤1 inner)</c>, the two roles
    /// must be distinct and linked by that inverse, and the merge class must be
    /// distinct from both the anchor and the typed class — the aliasing that
    /// would leave the merge class's value unresolvable, since the anchor and
    /// the typed class live in the fixed witness construction. Without a
    /// prelude-shaped definition the module is the pure shape, and any told
    /// inverse then rejects it.
    /// </summary>
    /// <param name="equivalences">The told equivalence axioms.</param>
    /// <param name="inverse">The told inverse axiom, or <see langword="null"/>.</param>
    /// <param name="inverses">The told inverse axiom count.</param>
    /// <param name="typedClass">The ABox assertion's class.</param>
    /// <param name="prelude">The admitted prelude; <see langword="null"/> on the pure shape.</param>
    /// <returns><see langword="true"/> when the prelude question is settled — a well-formed prelude, or none at all.</returns>
    private static bool TryReadPrelude(List<OwlEquivalentClassesAxiom> equivalences, OwlInverseObjectPropertiesAxiom? inverse, int inverses, Utf8String typedClass, out GadgetPrelude? prelude)
    {
        prelude = null;
        if(inverses > 1)
        {
            return false;
        }

        if(!TrySelectDefinition(equivalences, typedClass, out OwlClassExpression? targetBody))
        {
            return inverse is null;
        }

        List<Utf8String> conjuncts = [];
        if(!TryReadPreludeTarget(targetBody, conjuncts, out Utf8String outerRole, out Utf8String anchorClass))
        {
            return inverse is null;
        }

        if(inverse is null
            || anchorClass.Equals(typedClass)
            || !TrySelectDefinition(equivalences, anchorClass, out OwlClassExpression? anchorBody)
            || !TryReadPreludeAnchor(anchorBody, out Utf8String innerRole, out Utf8String mergeClass))
        {
            return false;
        }

        if(outerRole.Equals(innerRole)
            || mergeClass.Equals(anchorClass)
            || mergeClass.Equals(typedClass)
            || !LinksPreludeRoles(inverse, outerRole, innerRole))
        {
            return false;
        }

        for(int i = 0; i < conjuncts.Count; i++)
        {
            if(conjuncts[i].Equals(typedClass) || conjuncts[i].Equals(anchorClass))
            {
                return false;
            }
        }

        prelude = new GadgetPrelude(anchorClass, mergeClass, outerRole, innerRole, conjuncts);

        return true;
    }

    /// <summary>Selects the single told definition of one named class; a class defined zero times or several times has no single definition.</summary>
    /// <param name="equivalences">The told equivalence axioms.</param>
    /// <param name="className">The class IRI.</param>
    /// <param name="body">The single definition's body; <see langword="null"/> when there is not exactly one.</param>
    /// <returns><see langword="true"/> when exactly one told equivalence defines the class.</returns>
    private static bool TrySelectDefinition(List<OwlEquivalentClassesAxiom> equivalences, Utf8String className, [NotNullWhen(true)] out OwlClassExpression? body)
    {
        body = null;
        OwlClassExpression? selected = null;
        int definitions = 0;
        for(int i = 0; i < equivalences.Count; i++)
        {
            if(TrySplitDefinition(equivalences[i], out OwlClassReference? defined, out OwlClassExpression? candidate) && defined.Class.Iri.Equals(className))
            {
                definitions++;
                selected = candidate;
            }
        }

        if(definitions != 1 || selected is null)
        {
            return false;
        }

        body = selected;

        return true;
    }

    /// <summary>Reads the prelude's target definition: the bare existential over a named role into a named anchor class, or an intersection carrying exactly one such existential beside named conjuncts. Any other operand shape rejects.</summary>
    /// <param name="body">The typed class's single definition body.</param>
    /// <param name="conjunctsToAppendTo">The named conjuncts, in told order; cleared when the read rejects.</param>
    /// <param name="outerRole">The existential's role IRI.</param>
    /// <param name="anchorClass">The existential's filler class IRI.</param>
    /// <returns><see langword="true"/> on a prelude-shaped target definition.</returns>
    private static bool TryReadPreludeTarget(OwlClassExpression body, List<Utf8String> conjunctsToAppendTo, out Utf8String outerRole, out Utf8String anchorClass)
    {
        if(TryReadPreludeExistential(body, out outerRole, out anchorClass))
        {
            return true;
        }

        if(body is not OwlObjectIntersectionOf intersection)
        {
            return false;
        }

        int existentials = 0;
        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            if(TryReadPreludeExistential(intersection.Operands[i], out Utf8String role, out Utf8String anchor))
            {
                existentials++;
                outerRole = role;
                anchorClass = anchor;
                continue;
            }

            if(intersection.Operands[i] is not OwlClassReference conjunct || !ContextHabitatRecognizer.IsChainNodeClass(conjunct))
            {
                conjunctsToAppendTo.Clear();

                return false;
            }

            conjunctsToAppendTo.Add(conjunct.Class.Iri);
        }

        if(existentials != 1)
        {
            conjunctsToAppendTo.Clear();

            return false;
        }

        return true;
    }

    /// <summary>Reads the prelude's anchor definition: exactly two conjuncts, one existential over a named role into a named merge class and one UNQUALIFIED maximum cardinality of one over the same role. A third conjunct, a different bound, a qualified cap, and a role mismatch all reject.</summary>
    /// <param name="body">The anchor class's single definition body.</param>
    /// <param name="innerRole">The anchor's role IRI.</param>
    /// <param name="mergeClass">The existential's filler class IRI.</param>
    /// <returns><see langword="true"/> on a prelude-shaped anchor definition.</returns>
    private static bool TryReadPreludeAnchor(OwlClassExpression body, out Utf8String innerRole, out Utf8String mergeClass)
    {
        innerRole = default;
        mergeClass = default;
        if(body is not OwlObjectIntersectionOf intersection || intersection.Operands.Count != 2)
        {
            return false;
        }

        int existentials = 0;
        int caps = 0;
        Utf8String capRole = default;
        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            if(TryReadPreludeExistential(intersection.Operands[i], out Utf8String role, out Utf8String merge))
            {
                existentials++;
                innerRole = role;
                mergeClass = merge;
                continue;
            }

            if(intersection.Operands[i] is not OwlObjectCardinality { Kind: OwlCardinalityKind.Max, Cardinality: 1, Property: OwlObjectPropertyReference capReference } cap
                || !ContextHabitatRecognizer.IsUnqualifiedFiller(cap.Filler))
            {
                return false;
            }

            caps++;
            capRole = capReference.Property.Iri;
        }

        return existentials == 1 && caps == 1 && innerRole.Equals(capRole);
    }

    /// <summary>Reads one existential over a named forward role into a named non-constant class — the only existential shape the prelude admits.</summary>
    /// <param name="expression">The candidate conjunct.</param>
    /// <param name="role">The role IRI; the default on rejection.</param>
    /// <param name="filler">The filler class IRI; the default on rejection.</param>
    /// <returns><see langword="true"/> on the admitted existential shape.</returns>
    private static bool TryReadPreludeExistential(OwlClassExpression expression, out Utf8String role, out Utf8String filler)
    {
        if(expression is OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference reference, Filler: OwlClassReference named } && ContextHabitatRecognizer.IsChainNodeClass(named))
        {
            role = reference.Property.Iri;
            filler = named.Class.Iri;

            return true;
        }

        role = default;
        filler = default;

        return false;
    }

    /// <summary>Whether the told inverse axiom links the prelude's two roles, in either declaration direction; an inverse over an inverse property expression rejects.</summary>
    /// <param name="inverse">The told inverse axiom.</param>
    /// <param name="outerRole">The target definition's role.</param>
    /// <param name="innerRole">The anchor definition's role.</param>
    /// <returns><see langword="true"/> when the axiom links the pair.</returns>
    private static bool LinksPreludeRoles(OwlInverseObjectPropertiesAxiom inverse, Utf8String outerRole, Utf8String innerRole)
    {
        if(inverse is not { First: OwlObjectPropertyReference first, Second: OwlObjectPropertyReference second })
        {
            return false;
        }

        Utf8String left = first.Property.Iri;
        Utf8String right = second.Property.Iri;

        return (left.Equals(outerRole) && right.Equals(innerRole)) || (left.Equals(innerRole) && right.Equals(outerRole));
    }

    /// <summary>Walks the surviving free-atom assignment space in index order, inducing each full assignment and stopping at the first one every check passes — the certificate's witness — and exhausting the free space otherwise. The per-assignment check re-evaluates the whole compiled theory on the induced assignment, so the induction carries no soundness weight; and every passing full assignment agrees with its own induced defined bits, so exhausting the free space exhausts the passing set. One atom buffer and one value buffer serve the whole walk.</summary>
    /// <param name="theory">The compiled theory, inside the free-atom window.</param>
    /// <param name="evaluatedVectors">The assignments evaluated: the passing assignment's index plus one, or the whole free space.</param>
    /// <returns><see langword="true"/> when an assignment passed.</returns>
    private static bool TryFindPassingVector(GadgetTheory theory, out int evaluatedVectors)
    {
        int total = 1 << theory.FreeAtomCount;
        bool[] atomValues = new bool[theory.AtomCount];
        bool[] values = new bool[theory.Classes.Length];
        for(int vector = 0; vector < total; vector++)
        {
            InduceAssignment(theory, vector, atomValues, values);
            if(VectorPasses(theory, atomValues, values))
            {
                evaluatedVectors = vector + 1;

                return true;
            }
        }

        evaluatedVectors = total;

        return false;
    }

    /// <summary>Induces one full assignment from one enumerated free vector: every surviving free atom reads its enumeration bit, and the compiled steps then compute every defined atom from its definer, the interleaved class resolutions landing the operand values the definers read. With an empty step order the free bits ARE the assignment and the induction is the raw expansion.</summary>
    /// <param name="theory">The compiled theory.</param>
    /// <param name="vector">The enumerated free vector, one bit per surviving free atom.</param>
    /// <param name="atomValues">The per-atom value buffer the induction fills.</param>
    /// <param name="values">The per-class value buffer the step resolutions fill.</param>
    private static void InduceAssignment(GadgetTheory theory, int vector, bool[] atomValues, bool[] values)
    {
        for(int atom = 0; atom < atomValues.Length; atom++)
        {
            int slot = theory.AtomFreeSlots[atom];
            atomValues[atom] = slot >= 0 && ((vector >> slot) & 1) != 0;
        }

        for(int i = 0; i < theory.ComputationSteps.Length; i++)
        {
            GadgetComputationStep step = theory.ComputationSteps[i];
            if(step.ComputesAtom)
            {
                atomValues[step.Target] = EvaluateDefinition(theory, theory.Definitions[step.Definition], atomValues, values) != step.Inverted;
                continue;
            }

            values[step.Target] = step.Definition < 0
                ? atomValues[theory.Classes[step.Target].AtomIndex]
                : EvaluateDefinition(theory, theory.Definitions[step.Definition], atomValues, values);
        }
    }

    /// <summary>Evaluates one induced assignment: resolve every class in dependency order, requiring a multiply-defined class's definitions to agree, then check every told implication and the typed individual's obligation.</summary>
    /// <param name="theory">The compiled theory.</param>
    /// <param name="atomValues">The induced assignment, one value per atom.</param>
    /// <param name="values">The per-class value buffer the resolution fills.</param>
    /// <returns><see langword="true"/> when the assignment satisfies the compiled theory.</returns>
    private static bool VectorPasses(GadgetTheory theory, bool[] atomValues, bool[] values)
    {
        for(int position = 0; position < theory.EvaluationOrder.Length; position++)
        {
            int classIndex = theory.EvaluationOrder[position];
            GadgetClass entry = theory.Classes[classIndex];
            if(entry.AtomIndex >= 0)
            {
                values[classIndex] = atomValues[entry.AtomIndex];
                continue;
            }

            bool value = EvaluateDefinition(theory, theory.Definitions[entry.DefinitionStart], atomValues, values);
            for(int offset = 1; offset < entry.DefinitionCount; offset++)
            {
                if(EvaluateDefinition(theory, theory.Definitions[entry.DefinitionStart + offset], atomValues, values) != value)
                {
                    return false;
                }
            }

            values[classIndex] = value;
        }

        for(int i = 0; i < theory.Implications.Length; i++)
        {
            GadgetImplication implication = theory.Implications[i];
            if(values[implication.SubClass] && !values[implication.SuperClass])
            {
                return false;
            }
        }

        for(int i = 0; i < theory.Obligations.Length; i++)
        {
            if(!values[theory.Obligations[i]])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Evaluates one definition under an assignment: the property atom, possibly denied, or the conjunction of the operands' already-resolved values.</summary>
    /// <param name="theory">The compiled theory.</param>
    /// <param name="definition">The definition to evaluate.</param>
    /// <param name="atomValues">The assignment, one value per atom.</param>
    /// <param name="values">The per-class value buffer, filled for every operand of this definition.</param>
    /// <returns>The definition's value under the assignment.</returns>
    private static bool EvaluateDefinition(GadgetTheory theory, GadgetDefinition definition, bool[] atomValues, bool[] values)
    {
        if(definition.Kind == GadgetDefinitionKind.GadgetRestriction)
        {
            return atomValues[definition.PropertyAtom] != definition.Negated;
        }

        for(int offset = 0; offset < definition.OperandCount; offset++)
        {
            if(!values[theory.Operands[definition.OperandStart + offset]])
            {
                return false;
            }
        }

        return true;
    }
}
