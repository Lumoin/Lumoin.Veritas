using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The in-library default behind the <see cref="DescriptionLogicDelegate"/>
/// seam: a worklist tableau deciding the ALC(H) fragment of a locality
/// module — consistency over TBox and ABox, plus module-local subsumptions
/// between named classes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Calculus.</b> Concepts normalise to negation normal form; the TBox
/// internalises into one concept every node carries (each inclusion
/// <c>C ⊑ D</c> contributes <c>¬C ⊔ D</c>); the tableau expands
/// conjunctions, branches disjunctions over an explicit choice stack with
/// state snapshots (copy-on-branch — module-bounded inputs make the copies
/// cheap), creates existential successors under ancestor subset blocking
/// (the termination device for cyclic TBoxes), and propagates universal
/// restrictions over the told role hierarchy. A clash is an atom meeting
/// its negation, or ⊥. No call-stack recursion anywhere.
/// </para>
/// <para>
/// <b>Fragment honesty.</b> Axioms or expressions beyond ALC(H) —
/// cardinalities, inverses, chains, nominals, self-restrictions, keys,
/// data ranges, negative assertions, and any role position naming the
/// reserved <c>owl:topObjectProperty</c>/<c>owl:bottomObjectProperty</c>
/// (whose fixed universal/empty semantics lies outside the calculus) —
/// are named on
/// <see cref="ModuleVerdict.UnsupportedConstructs"/> and excluded: an
/// inconsistency found in the supported fragment condemns the whole
/// module; a consistency verdict is fragment-relative.
/// <c>DifferentIndividuals</c> is vacuous in ALC(H) (nothing can force two
/// individuals equal) and <c>SameIndividual</c> merges nodes up front.
/// </para>
/// <para>
/// <b>Sibling by seam.</b> This engine branches in-place; the SAT-backed
/// variant (per-world propositional abstraction over
/// <c>Core.Sat.SatSolver</c>, conflict clauses instead of snapshots) is
/// the recorded sibling for workloads where batched subsumption checking
/// dominates — behind this same delegate, per the whole-engine seam rule.
/// </para>
/// </remarks>
public static class AlcModuleReasoner
{
    /// <summary>The largest named-class signature for which module-local subsumptions enumerate; beyond it the verdict carries consistency only.</summary>
    private const int SubsumptionSignatureCap = 16;

    /// <summary>
    /// Wraps <see cref="Decide"/> as the seam delegate.
    /// </summary>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate CreateDelegate()
    {
        return static (module, cancellationToken) => ValueTask.FromResult(Decide(module, cancellationToken));
    }

    /// <summary>
    /// Decides the module's ALC(H) fragment: consistency, module-local
    /// subsumptions, and the named remainder beyond the calculus.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">A token that aborts the tableau between rule applications.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleVerdict Decide(ReasoningModule module, CancellationToken cancellationToken = default)
    {
        return DecideCore(module, includeSubsumptions: true, cancellationToken);
    }

    /// <summary>
    /// Decides the module's ALC(H) fragment for consistency only: the
    /// verdict's subsumption list stays empty whatever the signature size,
    /// and no per-pair subsumption tableaux run. The entry for callers that
    /// consume the consistency bit alone — a satisfiability or refutation
    /// check — where the pairwise sweep would be pure overhead.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">A token that aborts the tableau between rule applications.</param>
    /// <returns>The verdict, with an empty subsumption list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleVerdict DecideConsistency(ReasoningModule module, CancellationToken cancellationToken = default)
    {
        return DecideCore(module, includeSubsumptions: false, cancellationToken);
    }

    /// <summary>
    /// Names the module's beyond-fragment remainder without deciding
    /// satisfiability: the translation runs, the tableau does not. An empty
    /// result means every axiom lies inside the ALC(H) calculus, so a
    /// subsequent decision is whole-module rather than fragment-relative.
    /// </summary>
    /// <param name="module">The module to survey.</param>
    /// <returns>The named beyond-fragment constructs, empty when the module is wholly within the fragment.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<string> Survey(ReasoningModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        return Translate(module).Unsupported;
    }

    /// <summary>Runs the tableau over the module's translation, optionally following with the module-local subsumption sweep.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="includeSubsumptions">Whether to enumerate module-local subsumptions when the signature qualifies.</param>
    /// <param name="cancellationToken">A token that aborts the tableau between rule applications.</param>
    /// <returns>The verdict.</returns>
    private static ModuleVerdict DecideCore(ReasoningModule module, bool includeSubsumptions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(module);

        Translation translation = Translate(module);

        bool isConsistent = IsSatisfiable(translation.TBox, translation.AssertedConcepts, translation.Edges, translation.SuperRoles, cancellationToken);

        List<(NamedNode SubClass, NamedNode SuperClass)> subsumptions = [];
        if(includeSubsumptions && isConsistent && translation.SignatureClasses.Count <= SubsumptionSignatureCap)
        {
            //A ⊑ B holds exactly when A ⊓ ¬B is unsatisfiable under the
            //TBox; every check is a fresh single-root tableau.
            foreach(Utf8String subClass in translation.SignatureClasses)
            {
                foreach(Utf8String superClass in translation.SignatureClasses)
                {
                    if(subClass.Equals(superClass))
                    {
                        continue;
                    }

                    Dictionary<Utf8String, List<AlcConcept>> root = new()
                    {
                        [Utf8String.FromString("?")] = [new AlcAtom(subClass), new AlcNot(new AlcAtom(superClass))],
                    };

                    if(!IsSatisfiable(translation.TBox, root, edges: [], translation.SuperRoles, cancellationToken))
                    {
                        subsumptions.Add((new NamedNode(subClass), new NamedNode(superClass)));
                    }
                }
            }
        }

        return new ModuleVerdict(isConsistent, subsumptions)
        {
            UnsupportedConstructs = translation.Unsupported,
        };
    }

    /// <summary>The module's ALC(H) reading: the internalized TBox, the ABox by individual, the role edges, the role hierarchy, the named-class signature, and the remainder.</summary>
    /// <param name="TBox">The internalized TBox conjuncts every node carries.</param>
    /// <param name="AssertedConcepts">Per-individual asserted concepts.</param>
    /// <param name="Edges">The asserted role edges.</param>
    /// <param name="SuperRoles">Per-role reflexive-transitive told super-roles.</param>
    /// <param name="SignatureClasses">The named classes of the supported fragment, in first-appearance order.</param>
    /// <param name="Unsupported">The named remainder beyond the calculus.</param>
    private sealed record Translation(
        List<AlcConcept> TBox,
        Dictionary<Utf8String, List<AlcConcept>> AssertedConcepts,
        List<(Utf8String From, Utf8String Role, Utf8String To)> Edges,
        Dictionary<Utf8String, HashSet<Utf8String>> SuperRoles,
        List<Utf8String> SignatureClasses,
        List<string> Unsupported);

    /// <summary>Translates the module's axioms into the ALC(H) reading, collecting what falls outside.</summary>
    /// <param name="module">The module.</param>
    /// <returns>The translation.</returns>
    private static Translation Translate(ReasoningModule module)
    {
        List<AlcConcept> tbox = [];
        Dictionary<Utf8String, List<AlcConcept>> asserted = [];
        List<(Utf8String, Utf8String, Utf8String)> edges = [];
        List<(Utf8String Sub, Utf8String Super)> rolePairs = [];
        List<string> unsupported = [];
        List<Utf8String> signature = [];
        HashSet<Utf8String> signatureSeen = [];
        Dictionary<Utf8String, Utf8String> merges = [];

        //SameIndividual pre-merge: a tiny union-find over individual keys.
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(axiom is OwlSameIndividualAxiom same
                && TryIndividualKey(same.First, out Utf8String first)
                && TryIndividualKey(same.Second, out Utf8String second))
            {
                merges[Find(merges, first)] = Find(merges, second);
            }
        }

        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case OwlDeclarationAxiom or OwlImportAxiom or OwlAnnotationAssertionAxiom
                    or OwlSubAnnotationPropertyOfAxiom or OwlAnnotationPropertyDomainAxiom
                    or OwlAnnotationPropertyRangeAxiom or OwlSameIndividualAxiom:
                    break;
                case OwlDifferentIndividualsAxiom:
                    //Vacuous in ALC(H): nothing can force two individuals
                    //equal, so asserted distinctness cannot clash.
                    break;
                case OwlSubClassOfAxiom subClass when TryInclusion(subClass.SubClass, subClass.SuperClass, tbox, signature, signatureSeen):
                    break;
                case OwlEquivalentClassesAxiom equivalent
                    when TryInclusion(equivalent.First, equivalent.Second, tbox, signature, signatureSeen)
                        && TryInclusion(equivalent.Second, equivalent.First, tbox, signature, signatureSeen):
                    break;
                case OwlDisjointClassesAxiom disjoint when TryDisjointness(disjoint.Operands, tbox, signature, signatureSeen):
                    break;
                case OwlObjectPropertyDomainAxiom { Property.IsInverse: false } domain
                    when !IsReservedRole(domain.Property.Property.Iri)
                        && TryTranslate(domain.Domain, negate: false, signature, signatureSeen, out AlcConcept? domainConcept):
                    //domain(r, C) is ∃r.⊤ ⊑ C, internalized ∀r.⊥ ⊔ C.
                    tbox.Add(new AlcOr([new AlcForAll(domain.Property.Property.Iri, AlcBottom.Instance), domainConcept]));

                    break;
                case OwlObjectPropertyRangeAxiom { Property.IsInverse: false } range
                    when !IsReservedRole(range.Property.Property.Iri)
                        && TryTranslate(range.Range, negate: false, signature, signatureSeen, out AlcConcept? rangeConcept):
                    //range(r, C) is ⊤ ⊑ ∀r.C.
                    tbox.Add(new AlcForAll(range.Property.Property.Iri, rangeConcept));

                    break;
                case OwlSubObjectPropertyOfAxiom { SubProperty.IsInverse: false, SuperProperty.IsInverse: false } subRole
                    when !IsReservedRole(subRole.SubProperty.Property.Iri) && !IsReservedRole(subRole.SuperProperty.Property.Iri):
                    rolePairs.Add((subRole.SubProperty.Property.Iri, subRole.SuperProperty.Property.Iri));

                    break;
                case OwlClassAssertionAxiom assertion
                    when TryIndividualKey(assertion.Individual, out Utf8String individual)
                        && TryTranslate(assertion.Class, negate: false, signature, signatureSeen, out AlcConcept? assertedConcept):
                    Append(asserted, Find(merges, individual), assertedConcept);

                    break;
                case OwlObjectPropertyAssertionAxiom roleAssertion
                    when !IsReservedRole(roleAssertion.Property.Iri)
                        && TryIndividualKey(roleAssertion.Source, out Utf8String source) && TryIndividualKey(roleAssertion.Target, out Utf8String target):
                    edges.Add((Find(merges, source), roleAssertion.Property.Iri, Find(merges, target)));

                    break;
                default:
                    unsupported.Add(axiom.GetType().Name);

                    break;
            }
        }

        //Reflexive-transitive told super-roles, iteratively to fixpoint.
        Dictionary<Utf8String, HashSet<Utf8String>> superRoles = [];
        foreach((Utf8String sub, Utf8String super) in rolePairs)
        {
            SuperSetOf(superRoles, sub).Add(super);
        }

        bool grew = true;
        while(grew)
        {
            grew = false;
            foreach(KeyValuePair<Utf8String, HashSet<Utf8String>> entry in superRoles)
            {
                List<Utf8String> reachable = [];
                foreach(Utf8String super in entry.Value)
                {
                    if(superRoles.TryGetValue(super, out HashSet<Utf8String>? next))
                    {
                        reachable.AddRange(next);
                    }
                }

                foreach(Utf8String super in reachable)
                {
                    grew |= entry.Value.Add(super);
                }
            }
        }

        return new Translation(tbox, asserted, edges, superRoles, signature, unsupported);
    }

    /// <summary>Adds the internalized inclusion <c>¬Sub ⊔ Super</c> when both sides translate.</summary>
    /// <param name="subClass">The subclass expression.</param>
    /// <param name="superClass">The superclass expression.</param>
    /// <param name="tbox">The TBox sink.</param>
    /// <param name="signature">The named-class signature sink.</param>
    /// <param name="signatureSeen">The signature dedup set.</param>
    /// <returns><see langword="true"/> when both sides are in the fragment.</returns>
    private static bool TryInclusion(
        OwlClassExpression subClass,
        OwlClassExpression superClass,
        List<AlcConcept> tbox,
        List<Utf8String> signature,
        HashSet<Utf8String> signatureSeen)
    {
        if(TryTranslate(subClass, negate: true, signature, signatureSeen, out AlcConcept? negatedSub)
            && TryTranslate(superClass, negate: false, signature, signatureSeen, out AlcConcept? super))
        {
            tbox.Add(new AlcOr([negatedSub, super]));

            return true;
        }

        return false;
    }

    /// <summary>Adds the pairwise internalized disjointness <c>¬Ci ⊔ ¬Cj</c> when every operand translates.</summary>
    /// <param name="operands">The mutually disjoint expressions.</param>
    /// <param name="tbox">The TBox sink.</param>
    /// <param name="signature">The named-class signature sink.</param>
    /// <param name="signatureSeen">The signature dedup set.</param>
    /// <returns><see langword="true"/> when the axiom is in the fragment.</returns>
    private static bool TryDisjointness(
        IReadOnlyList<OwlClassExpression> operands,
        List<AlcConcept> tbox,
        List<Utf8String> signature,
        HashSet<Utf8String> signatureSeen)
    {
        List<AlcConcept> negated = [];
        foreach(OwlClassExpression operand in operands)
        {
            if(!TryTranslate(operand, negate: true, signature, signatureSeen, out AlcConcept? negatedOperand))
            {
                return false;
            }

            negated.Add(negatedOperand);
        }

        for(int i = 0; i < negated.Count; i++)
        {
            for(int j = i + 1; j < negated.Count; j++)
            {
                tbox.Add(new AlcOr([negated[i], negated[j]]));
            }
        }

        return true;
    }

    /// <summary>
    /// Translates a class expression to NNF with an explicit post-order
    /// stack: polarity flows down, concepts assemble on the way up.
    /// </summary>
    /// <param name="root">The expression to translate.</param>
    /// <param name="negate">Whether the expression occurs under negation.</param>
    /// <param name="signature">The named-class signature sink.</param>
    /// <param name="signatureSeen">The signature dedup set.</param>
    /// <param name="concept">The translated concept.</param>
    /// <returns><see langword="false"/> when the expression leaves the ALC fragment.</returns>
    private static bool TryTranslate(
        OwlClassExpression root,
        bool negate,
        List<Utf8String> signature,
        HashSet<Utf8String> signatureSeen,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AlcConcept? concept)
    {
        Dictionary<(OwlClassExpression, bool), AlcConcept> results = [];
        Stack<(OwlClassExpression Node, bool Negate, bool ChildrenDone)> work = new();
        work.Push((root, negate, false));

        while(work.Count > 0)
        {
            (OwlClassExpression node, bool polarity, bool childrenDone) = work.Pop();
            if(results.ContainsKey((node, polarity)))
            {
                continue;
            }

            if(!childrenDone)
            {
                work.Push((node, polarity, true));
                switch(node)
                {
                    case OwlObjectIntersectionOf intersection:
                        foreach(OwlClassExpression operand in intersection.Operands)
                        {
                            work.Push((operand, polarity, false));
                        }

                        break;
                    case OwlObjectUnionOf union:
                        foreach(OwlClassExpression operand in union.Operands)
                        {
                            work.Push((operand, polarity, false));
                        }

                        break;
                    case OwlObjectComplementOf complement:
                        work.Push((complement.Operand, !polarity, false));

                        break;
                    case OwlObjectSomeValuesFrom { Property.IsInverse: false } some:
                        work.Push((some.Filler, polarity, false));

                        break;
                    case OwlObjectAllValuesFrom { Property.IsInverse: false } all:
                        work.Push((all.Filler, polarity, false));

                        break;
                    default:
                        break;
                }

                continue;
            }

            AlcConcept? translated = node switch
            {
                OwlClassReference reference => TranslateReference(reference, polarity, signature, signatureSeen),
                OwlObjectIntersectionOf intersection => CombineOperands(intersection.Operands, polarity, isIntersection: true, results),
                OwlObjectUnionOf union => CombineOperands(union.Operands, polarity, isIntersection: false, results),
                OwlObjectComplementOf complement => results.TryGetValue((complement.Operand, !polarity), out AlcConcept? inner) ? inner : null,
                OwlObjectSomeValuesFrom { Property.IsInverse: false } some
                    when !IsReservedRole(some.Property.Property.Iri)
                        && results.TryGetValue((some.Filler, polarity), out AlcConcept? filler) =>
                        polarity ? new AlcForAll(some.Property.Property.Iri, filler) : new AlcExists(some.Property.Property.Iri, filler),
                OwlObjectAllValuesFrom { Property.IsInverse: false } all
                    when !IsReservedRole(all.Property.Property.Iri)
                        && results.TryGetValue((all.Filler, polarity), out AlcConcept? filler) =>
                        polarity ? new AlcExists(all.Property.Property.Iri, filler) : new AlcForAll(all.Property.Property.Iri, filler),
                _ => null
            };

            if(translated is null)
            {
                concept = null;

                return false;
            }

            results[(node, polarity)] = translated;
        }

        concept = results[(root, negate)];

        return true;
    }

    /// <summary>Translates a named class under the polarity, recording it in the signature; the built-ins keep their fixed reading.</summary>
    /// <param name="reference">The class reference.</param>
    /// <param name="negate">Whether the reference occurs under negation.</param>
    /// <param name="signature">The named-class signature sink.</param>
    /// <param name="signatureSeen">The signature dedup set.</param>
    /// <returns>The concept.</returns>
    private static AlcConcept TranslateReference(OwlClassReference reference, bool negate, List<Utf8String> signature, HashSet<Utf8String> signatureSeen)
    {
        Utf8String iri = reference.Class.Iri;
        if(iri.Equals(OwlVocabulary.Thing))
        {
            return negate ? AlcBottom.Instance : AlcTop.Instance;
        }

        if(iri.Equals(OwlVocabulary.Nothing))
        {
            return negate ? AlcTop.Instance : AlcBottom.Instance;
        }

        if(signatureSeen.Add(iri))
        {
            signature.Add(iri);
        }

        AlcAtom atom = new(iri);

        return negate ? new AlcNot(atom) : atom;
    }

    /// <summary>Assembles a boolean node from its translated operands; polarity has already dualized the connective.</summary>
    /// <param name="operands">The source operands.</param>
    /// <param name="polarity">The polarity the operands translated under.</param>
    /// <param name="isIntersection">Whether the source connective is an intersection.</param>
    /// <param name="results">The memo of translated children.</param>
    /// <returns>The combined concept, or <see langword="null"/> when a child fell outside the fragment.</returns>
    private static AlcConcept? CombineOperands(
        IReadOnlyList<OwlClassExpression> operands,
        bool polarity,
        bool isIntersection,
        Dictionary<(OwlClassExpression, bool), AlcConcept> results)
    {
        List<AlcConcept> translated = [];
        foreach(OwlClassExpression operand in operands)
        {
            if(!results.TryGetValue((operand, polarity), out AlcConcept? child))
            {
                return null;
            }

            translated.Add(child);
        }

        //De Morgan: a negated intersection is the union of negations. An
        //empty operand list is no class expression at all.
        bool conjunction = isIntersection != polarity;

        return translated.Count switch
        {
            0 => null,
            1 => translated[0],
            _ => conjunction ? new AlcAnd(translated) : new AlcOr(translated)
        };
    }

    /// <summary>
    /// The tableau: deterministic rule scan, copy-on-branch disjunctions,
    /// ancestor subset blocking on existential expansion, universal
    /// propagation over the told role hierarchy.
    /// </summary>
    /// <param name="tbox">The internalized TBox conjuncts.</param>
    /// <param name="assertedConcepts">Per-individual asserted concepts.</param>
    /// <param name="edges">The asserted role edges.</param>
    /// <param name="superRoles">Per-role told super-roles.</param>
    /// <param name="cancellationToken">A token that aborts between rule applications.</param>
    /// <returns><see langword="true"/> when a clash-free fully expanded tableau exists.</returns>
    private static bool IsSatisfiable(
        List<AlcConcept> tbox,
        Dictionary<Utf8String, List<AlcConcept>> assertedConcepts,
        List<(Utf8String From, Utf8String Role, Utf8String To)> edges,
        Dictionary<Utf8String, HashSet<Utf8String>> superRoles,
        CancellationToken cancellationToken)
    {
        //Seed the forest: one node per asserted individual (or a single
        //anonymous root for a pure TBox check), every label opening with
        //the TBox conjuncts.
        Dictionary<Utf8String, int> nodeOf = [];
        List<List<AlcConcept>> labels = [];
        List<int> parents = [];
        List<List<(int Target, Utf8String Role)>> outgoing = [];

        int NodeFor(Utf8String individual)
        {
            if(!nodeOf.TryGetValue(individual, out int index))
            {
                index = labels.Count;
                nodeOf[individual] = index;
                labels.Add([.. tbox]);
                parents.Add(-1);
                outgoing.Add([]);
            }

            return index;
        }

        foreach(KeyValuePair<Utf8String, List<AlcConcept>> entry in assertedConcepts)
        {
            int node = NodeFor(entry.Key);
            foreach(AlcConcept concept in entry.Value)
            {
                AddConcept(labels[node], concept);
            }
        }

        foreach((Utf8String from, Utf8String role, Utf8String to) in edges)
        {
            outgoing[NodeFor(from)].Add((NodeFor(to), role));
        }

        if(labels.Count == 0)
        {
            labels.Add([.. tbox]);
            parents.Add(-1);
            outgoing.Add([]);
        }

        //The choice stack: each entry restores its snapshot and tries the
        //next disjunct of the branch that created it.
        List<(List<List<AlcConcept>> Labels, List<int> Parents, List<List<(int, Utf8String)>> Outgoing, int Node, List<AlcConcept> Remaining)> choices = [];

        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(HasClash(labels))
            {
                bool restored = false;
                while(choices.Count > 0)
                {
                    (List<List<AlcConcept>> snapshotLabels, List<int> snapshotParents, List<List<(int, Utf8String)>> snapshotOutgoing, int node, List<AlcConcept> remaining) = choices[^1];
                    if(remaining.Count == 0)
                    {
                        choices.RemoveAt(choices.Count - 1);

                        continue;
                    }

                    AlcConcept next = remaining[0];
                    remaining.RemoveAt(0);
                    (labels, parents, outgoing) = Clone(snapshotLabels, snapshotParents, snapshotOutgoing);
                    AddConcept(labels[node], next);
                    restored = true;

                    break;
                }

                if(!restored)
                {
                    return false;
                }

                continue;
            }

            if(!ApplyOneRule(labels, parents, outgoing, superRoles, tbox, choices))
            {
                return true;
            }
        }
    }

    /// <summary>
    /// Applies the first applicable rule in deterministic node and label
    /// order: conjunction decomposition, disjunction branching (recording
    /// the choice point), universal propagation, then blocked-aware
    /// existential expansion.
    /// </summary>
    /// <param name="labels">Per-node concept labels.</param>
    /// <param name="parents">Per-node tree parents; -1 for roots.</param>
    /// <param name="outgoing">Per-node outgoing role edges.</param>
    /// <param name="superRoles">Per-role told super-roles.</param>
    /// <param name="tbox">The internalized TBox conjuncts new nodes open with.</param>
    /// <param name="choices">The choice stack disjunction branching records onto.</param>
    /// <returns><see langword="false"/> when no rule applies — the tableau is fully expanded.</returns>
    private static bool ApplyOneRule(
        List<List<AlcConcept>> labels,
        List<int> parents,
        List<List<(int Target, Utf8String Role)>> outgoing,
        Dictionary<Utf8String, HashSet<Utf8String>> superRoles,
        List<AlcConcept> tbox,
        List<(List<List<AlcConcept>> Labels, List<int> Parents, List<List<(int, Utf8String)>> Outgoing, int Node, List<AlcConcept> Remaining)> choices)
    {
        for(int node = 0; node < labels.Count; node++)
        {
            List<AlcConcept> label = labels[node];
            for(int i = 0; i < label.Count; i++)
            {
                switch(label[i])
                {
                    case AlcAnd and:
                    {
                        bool added = false;
                        foreach(AlcConcept operand in and.Operands)
                        {
                            added |= AddConcept(label, operand);
                        }

                        if(added)
                        {
                            return true;
                        }

                        break;
                    }
                    case AlcOr or:
                    {
                        bool anyPresent = false;
                        foreach(AlcConcept operand in or.Operands)
                        {
                            anyPresent |= Contains(label, operand);
                        }

                        if(!anyPresent)
                        {
                            //Branch: snapshot the state, apply the first
                            //disjunct, queue the rest.
                            List<AlcConcept> remaining = [.. or.Operands];
                            AlcConcept first = remaining[0];
                            remaining.RemoveAt(0);
                            (List<List<AlcConcept>> snapshotLabels, List<int> snapshotParents, List<List<(int, Utf8String)>> snapshotOutgoing) = Clone(labels, parents, outgoing);
                            choices.Add((snapshotLabels, snapshotParents, snapshotOutgoing, node, remaining));
                            AddConcept(label, first);

                            return true;
                        }

                        break;
                    }
                    case AlcForAll forAll:
                    {
                        foreach((int target, Utf8String role) in outgoing[node])
                        {
                            if(RoleReaches(role, forAll.Role, superRoles) && AddConcept(labels[target], forAll.Filler))
                            {
                                return true;
                            }
                        }

                        break;
                    }
                    case AlcExists exists:
                    {
                        bool satisfiedByExisting = false;
                        foreach((int target, Utf8String role) in outgoing[node])
                        {
                            if(RoleReaches(role, exists.Role, superRoles) && Contains(labels[target], exists.Filler))
                            {
                                satisfiedByExisting = true;

                                break;
                            }
                        }

                        if(!satisfiedByExisting && !IsBlocked(node, labels, parents))
                        {
                            int successor = labels.Count;
                            List<AlcConcept> successorLabel = [.. tbox];
                            AddConcept(successorLabel, exists.Filler);
                            labels.Add(successorLabel);
                            parents.Add(node);
                            outgoing.Add([]);
                            outgoing[node].Add((successor, exists.Role));

                            return true;
                        }

                        break;
                    }
                    default:
                        break;
                }
            }
        }

        return false;
    }

    /// <summary>Whether an ancestor's label includes the node's — the subset blocking that terminates cyclic TBox expansion.</summary>
    /// <param name="node">The candidate node.</param>
    /// <param name="labels">Per-node labels.</param>
    /// <param name="parents">Per-node tree parents.</param>
    /// <returns><see langword="true"/> when an ancestor subsumes the label.</returns>
    private static bool IsBlocked(int node, List<List<AlcConcept>> labels, List<int> parents)
    {
        for(int ancestor = parents[node]; ancestor >= 0; ancestor = parents[ancestor])
        {
            bool subset = true;
            foreach(AlcConcept concept in labels[node])
            {
                if(!Contains(labels[ancestor], concept))
                {
                    subset = false;

                    break;
                }
            }

            if(subset)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the edge's role reaches the restriction's role through the told hierarchy, reflexively.</summary>
    /// <param name="edgeRole">The edge's role.</param>
    /// <param name="restrictionRole">The restriction's role.</param>
    /// <param name="superRoles">Per-role told super-roles.</param>
    /// <returns><see langword="true"/> when the universal applies over the edge.</returns>
    private static bool RoleReaches(Utf8String edgeRole, Utf8String restrictionRole, Dictionary<Utf8String, HashSet<Utf8String>> superRoles)
    {
        return edgeRole.Equals(restrictionRole)
            || (superRoles.TryGetValue(edgeRole, out HashSet<Utf8String>? supers) && supers.Contains(restrictionRole));
    }

    /// <summary>Whether any node label clashes: ⊥, or an atom meeting its negation.</summary>
    /// <param name="labels">Per-node labels.</param>
    /// <returns><see langword="true"/> on a clash.</returns>
    private static bool HasClash(List<List<AlcConcept>> labels)
    {
        foreach(List<AlcConcept> label in labels)
        {
            foreach(AlcConcept concept in label)
            {
                if(concept is AlcBottom)
                {
                    return true;
                }

                if(concept is AlcNot negation && Contains(label, negation.Operand))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Adds a concept to a label unless present; list-backed for deterministic iteration.</summary>
    /// <param name="label">The label.</param>
    /// <param name="concept">The concept.</param>
    /// <returns><see langword="true"/> when the label grew.</returns>
    private static bool AddConcept(List<AlcConcept> label, AlcConcept concept)
    {
        if(Contains(label, concept))
        {
            return false;
        }

        label.Add(concept);

        return true;
    }

    /// <summary>Whether the label contains the concept; labels stay small enough for the linear scan.</summary>
    /// <param name="label">The label.</param>
    /// <param name="concept">The concept.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool Contains(List<AlcConcept> label, AlcConcept concept)
    {
        foreach(AlcConcept candidate in label)
        {
            if(candidate.Equals(concept))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Deep-copies the tableau state for a choice point.</summary>
    /// <param name="labels">Per-node labels.</param>
    /// <param name="parents">Per-node parents.</param>
    /// <param name="outgoing">Per-node edges.</param>
    /// <returns>The independent copy.</returns>
    private static (List<List<AlcConcept>> Labels2, List<int> Parents2, List<List<(int, Utf8String)>> Outgoing2) Clone(
        List<List<AlcConcept>> labels,
        List<int> parents,
        List<List<(int, Utf8String)>> outgoing)
    {
        List<List<AlcConcept>> labelsCopy = new(labels.Count);
        foreach(List<AlcConcept> label in labels)
        {
            labelsCopy.Add([.. label]);
        }

        return (labelsCopy, [.. parents], CloneOutgoing(outgoing));
    }

    /// <summary>Deep-copies the edge lists.</summary>
    /// <param name="outgoing">Per-node edges.</param>
    /// <returns>The independent copy.</returns>
    private static List<List<(int, Utf8String)>> CloneOutgoing(List<List<(int, Utf8String)>> outgoing)
    {
        List<List<(int, Utf8String)>> copy = new(outgoing.Count);
        foreach(List<(int, Utf8String)> edges in outgoing)
        {
            copy.Add([.. edges]);
        }

        return copy;
    }

    /// <summary>Whether a role IRI is one of the reserved built-ins, <c>owl:topObjectProperty</c> or <c>owl:bottomObjectProperty</c>, whose fixed universal/empty semantics the calculus does not interpret.</summary>
    /// <param name="role">The role IRI.</param>
    /// <returns><see langword="true"/> for a reserved role.</returns>
    private static bool IsReservedRole(Utf8String role)
    {
        return role.Equals(OwlVocabulary.TopObjectProperty) || role.Equals(OwlVocabulary.BottomObjectProperty);
    }

    /// <summary>The individual key: a named individual by IRI, an anonymous one by label.</summary>
    /// <param name="individual">The individual term.</param>
    /// <param name="key">The key.</param>
    /// <returns><see langword="false"/> for a term that is not an individual.</returns>
    private static bool TryIndividualKey(RdfTerm individual, out Utf8String key)
    {
        switch(individual)
        {
            case NamedNode named:
                key = named.Iri;

                return true;
            case BlankNode blank:
                key = blank.Label;

                return true;
            default:
                key = default;

                return false;
        }
    }

    /// <summary>Resolves a key through the merge map, path-compressing as it goes.</summary>
    /// <param name="merges">The merge parent map.</param>
    /// <param name="key">The key to resolve.</param>
    /// <returns>The representative key.</returns>
    private static Utf8String Find(Dictionary<Utf8String, Utf8String> merges, Utf8String key)
    {
        Utf8String current = key;
        while(merges.TryGetValue(current, out Utf8String parent) && !parent.Equals(current))
        {
            current = parent;
        }

        return current;
    }

    /// <summary>The role's super-set, created on first contact.</summary>
    /// <param name="superRoles">The hierarchy index.</param>
    /// <param name="role">The role.</param>
    /// <returns>The mutable super-set.</returns>
    private static HashSet<Utf8String> SuperSetOf(Dictionary<Utf8String, HashSet<Utf8String>> superRoles, Utf8String role)
    {
        if(!superRoles.TryGetValue(role, out HashSet<Utf8String>? supers))
        {
            supers = [];
            superRoles[role] = supers;
        }

        return supers;
    }

    /// <summary>Appends a concept to an individual's asserted list, creating it on first contact.</summary>
    /// <param name="asserted">The per-individual index.</param>
    /// <param name="individual">The individual key.</param>
    /// <param name="concept">The concept.</param>
    private static void Append(Dictionary<Utf8String, List<AlcConcept>> asserted, Utf8String individual, AlcConcept concept)
    {
        if(!asserted.TryGetValue(individual, out List<AlcConcept>? list))
        {
            list = [];
            asserted[individual] = list;
        }

        list.Add(concept);
    }
}
