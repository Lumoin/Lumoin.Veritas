using System;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.El;

/// <summary>
/// Consequence-based OWL 2 EL classification: normalizes the TBox to the
/// EL normal forms and saturates the completion rules to fixpoint,
/// producing the subclass closure over named classes in polynomial time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Normal forms.</b> Every interpreted axiom reduces to
/// <c>A ⊑ B</c>, <c>A₁ ⊓ A₂ ⊑ B</c>, <c>A ⊑ ∃r.B</c>, <c>∃r.A ⊑ B</c>
/// over atoms (named classes, <c>⊤</c>, <c>⊥</c>, and fresh normalization
/// names), plus the role axioms <c>r ⊑ s</c> and <c>r₁∘r₂ ⊑ s</c>
/// (transitivity is <c>r∘r ⊑ r</c>; longer chains decompose with fresh
/// roles). Complex sides decompose monotonically with fresh atoms.
/// </para>
/// <para>
/// <b>Completion.</b> The saturation maintains the subsumer sets
/// <c>S(C)</c> and role edges <c>R(r)</c> with an explicit work queue —
/// the no-recursion discipline holds — firing the standard rules:
/// told subsumption, conjunction, existential introduction and
/// elimination, bottom propagation through role edges, role hierarchy,
/// and role composition.
/// </para>
/// <para>
/// <b>Coverage.</b> Constructs outside this calculus — value and self
/// restrictions, enumerations, data ranges — are not interpreted; each
/// occurrence is recorded on
/// <see cref="ElClassification.UnsupportedConstructs"/> so consumers see
/// exactly what the closure does not account for. The classification is
/// sound for what it does interpret.
/// </para>
/// </remarks>
public static class ElClassifier
{
    /// <summary>
    /// Classifies the document's TBox.
    /// </summary>
    /// <param name="document">The structural ontology document.</param>
    /// <param name="cancellationToken">A token that aborts saturation between work items.</param>
    /// <returns>The classification.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ElClassification Classify(OwlOntologyDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        ClassifierContext context = new();

        context.Normalize(document);
        context.Saturate(cancellationToken);

        return context.BuildResult();
    }

    //The per-run state: atom and role interning, the normal-form indexes,
    //the saturation sets, and the work queue.
    private sealed class ClassifierContext
    {
        private const int Top = 0;

        private const int Bottom = 1;

        /// <summary>Interned atom names; fresh normalization atoms carry <c>null</c>.</summary>
        private List<Utf8String?> AtomNames { get; } = [new("http://www.w3.org/2002/07/owl#Thing"u8.ToArray()), new("http://www.w3.org/2002/07/owl#Nothing"u8.ToArray())];

        private Dictionary<Utf8String, int> AtomIds { get; } = [];

        /// <summary>Interned role names; fresh chain-decomposition roles carry <c>null</c>.</summary>
        private List<Utf8String?> RoleNames { get; } = [];

        private Dictionary<Utf8String, int> RoleIds { get; } = [];

        //Normal-form indexes.
        private Dictionary<int, List<int>> ToldSubsumptions { get; } = [];

        private Dictionary<(int First, int Second), List<int>> Conjunctions { get; } = [];

        /// <summary>Conjunction partners per atom: for A, the (other, conclusion) pairs of every A ⊓ other ⊑ conclusion.</summary>
        private Dictionary<int, List<(int Other, int Conclusion)>> ConjunctionsByAtom { get; } = [];

        private Dictionary<int, List<(int Role, int Filler)>> RightExistentials { get; } = [];

        private Dictionary<(int Role, int Filler), List<int>> LeftExistentials { get; } = [];

        private Dictionary<int, List<int>> RoleSubsumptions { get; } = [];

        /// <summary>Chains indexed by their FIRST role: (second, conclusion).</summary>
        private Dictionary<int, List<(int Second, int Conclusion)>> ChainsByFirst { get; } = [];

        /// <summary>Chains indexed by their SECOND role: (first, conclusion).</summary>
        private Dictionary<int, List<(int First, int Conclusion)>> ChainsBySecond { get; } = [];

        /// <summary>Range atoms per role: (C,D) ∈ R(r) puts each range atom of r into S(D).</summary>
        private Dictionary<int, List<int>> RangesByRole { get; } = [];

        //Saturation state.
        private Dictionary<int, HashSet<int>> Subsumers { get; } = [];

        /// <summary>Role edges by role, with per-source and per-target adjacency for the join rules.</summary>
        private Dictionary<int, HashSet<(int Source, int Target)>> Edges { get; } = [];

        private Dictionary<(int Role, int Source), List<int>> EdgesBySource { get; } = [];

        private Dictionary<(int Role, int Target), List<int>> EdgesByTarget { get; } = [];

        private Queue<WorkItem> Work { get; } = new();

        private List<string> Unsupported { get; } = [];

        /// <summary>The atoms that are user-named classes (classification output covers exactly these).</summary>
        private HashSet<int> NamedAtoms { get; } = [];

        public ClassifierContext()
        {
            //The pre-seeded ⊤ and ⊥ atoms must intern to their fixed
            //indices — every owl:Thing / owl:Nothing reference resolves to
            //them, never to a fresh atom.
            AtomIds[AtomNames[Top]!.Value] = Top;
            AtomIds[AtomNames[Bottom]!.Value] = Bottom;
        }

        private readonly record struct WorkItem(bool IsEdge, int Subject, int Atom, int Role, int Target);

        //Normalization.

        /// <summary>Reduces the document's interpreted axioms to the EL normal forms.</summary>
        public void Normalize(OwlOntologyDocument document)
        {
            foreach(OwlAxiom axiom in document.Axioms)
            {
                switch(axiom)
                {
                    case OwlSubClassOfAxiom subClass:
                    {
                        NormalizeInclusion(subClass.SubClass, subClass.SuperClass);
                        break;
                    }
                    case OwlEquivalentClassesAxiom equivalent:
                    {
                        NormalizeInclusion(equivalent.First, equivalent.Second);
                        NormalizeInclusion(equivalent.Second, equivalent.First);
                        break;
                    }
                    case OwlDisjointClassesAxiom disjoint:
                    {
                        for(int i = 0; i < disjoint.Operands.Count; i++)
                        {
                            for(int j = i + 1; j < disjoint.Operands.Count; j++)
                            {
                                NormalizeInclusion(new OwlObjectIntersectionOf([disjoint.Operands[i], disjoint.Operands[j]]), BottomReference);
                            }
                        }

                        break;
                    }
                    case OwlObjectPropertyDomainAxiom domain when !domain.Property.IsInverse:
                    {
                        //∃r.⊤ ⊑ D, through the inclusion machinery so a
                        //complex domain decomposes normally.
                        NormalizeInclusion(new OwlObjectSomeValuesFrom(domain.Property, TopReference), domain.Domain);
                        break;
                    }
                    case OwlObjectPropertyRangeAxiom range when !range.Property.IsInverse:
                    {
                        int role = RoleOf(range.Property.Property);
                        int atom;
                        if(range.Range is OwlClassReference namedRange)
                        {
                            atom = AtomOf(namedRange.Class.Iri);
                        }
                        else
                        {
                            atom = FreshAtom();
                            NormalizeInclusion(AtomReference(atom), range.Range);
                        }

                        if(!RangesByRole.TryGetValue(role, out List<int>? ranges))
                        {
                            ranges = [];
                            RangesByRole[role] = ranges;
                        }

                        ranges.Add(atom);
                        break;
                    }
                    case OwlSubObjectPropertyOfAxiom subProperty when !subProperty.SubProperty.IsInverse && !subProperty.SuperProperty.IsInverse:
                    {
                        AddRoleSubsumption(RoleOf(subProperty.SubProperty.Property), RoleOf(subProperty.SuperProperty.Property));
                        break;
                    }
                    case OwlPropertyChainAxiom chain:
                    {
                        NormalizeChain(chain);
                        break;
                    }
                    case OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Transitive } transitive when !transitive.Property.IsInverse:
                    {
                        int role = RoleOf(transitive.Property.Property);
                        AddChain(role, role, role);
                        break;
                    }
                    case OwlEquivalentObjectPropertiesAxiom equivalentProperties when !equivalentProperties.First.IsInverse && !equivalentProperties.Second.IsInverse:
                    {
                        int first = RoleOf(equivalentProperties.First.Property);
                        int second = RoleOf(equivalentProperties.Second.Property);
                        AddRoleSubsumption(first, second);
                        AddRoleSubsumption(second, first);
                        break;
                    }
                    case OwlDeclarationAxiom { Kind: OwlEntityKind.Class } declaration:
                    {
                        NamedAtoms.Add(AtomOf(declaration.Entity.Iri));
                        break;
                    }
                    case OwlDeclarationAxiom or OwlImportAxiom or OwlAnnotationAssertionAxiom or OwlSubAnnotationPropertyOfAxiom
                        or OwlAnnotationPropertyDomainAxiom or OwlAnnotationPropertyRangeAxiom or OwlClassAssertionAxiom
                        or OwlObjectPropertyAssertionAxiom or OwlDataPropertyAssertionAxiom or OwlSameIndividualAxiom
                        or OwlDifferentIndividualsAxiom or OwlNegativeObjectPropertyAssertionAxiom or OwlNegativeDataPropertyAssertionAxiom
                        or OwlSubDataPropertyOfAxiom or OwlEquivalentDataPropertiesAxiom or OwlDataPropertyDomainAxiom
                        or OwlDataPropertyRangeAxiom or OwlFunctionalDataPropertyAxiom:
                    {
                        //ABox, annotation, and data-property axioms do not
                        //participate in class subsumption here.
                        break;
                    }
                    default:
                    {
                        Unsupported.Add($"{axiom.GetType().Name} is outside the EL classification calculus.");
                        break;
                    }
                }
            }
        }

        private static OwlClassReference BottomReference { get; } = new(new NamedNode(new Utf8String("http://www.w3.org/2002/07/owl#Nothing"u8.ToArray())));

        private static OwlClassReference TopReference { get; } = new(new NamedNode(new Utf8String("http://www.w3.org/2002/07/owl#Thing"u8.ToArray())));

        //Reduces one inclusion to normal forms with an explicit pending
        //queue; complex sides decompose monotonically through fresh atoms.
        private void NormalizeInclusion(OwlClassExpression left, OwlClassExpression right)
        {
            Queue<(OwlClassExpression Left, OwlClassExpression Right)> pending = new();
            pending.Enqueue((left, right));

            while(pending.Count > 0)
            {
                (OwlClassExpression currentLeft, OwlClassExpression currentRight) = pending.Dequeue();

                //Right-side intersections split; the conjuncts inherit the left side.
                if(currentRight is OwlObjectIntersectionOf rightIntersection)
                {
                    foreach(OwlClassExpression operand in rightIntersection.Operands)
                    {
                        pending.Enqueue((currentLeft, operand));
                    }

                    continue;
                }

                //Right-side existential with a complex filler: name the filler.
                if(currentRight is OwlObjectSomeValuesFrom { Filler: not OwlClassReference } rightExistential)
                {
                    if(rightExistential.Property.IsInverse)
                    {
                        Unsupported.Add("ObjectInverseOf in an existential is outside EL.");

                        continue;
                    }

                    int filler = FreshAtom();
                    pending.Enqueue((AtomReference(filler), rightExistential.Filler));
                    pending.Enqueue((currentLeft, new OwlObjectSomeValuesFrom(rightExistential.Property, AtomReference(filler))));

                    continue;
                }

                int subject = NameLeft(currentLeft);

                if(currentRight is OwlClassReference rightReference)
                {
                    AddToldSubsumption(subject, AtomOf(rightReference.Class.Iri));
                }
                else if(currentRight is OwlObjectSomeValuesFrom { Filler: OwlClassReference namedFiller } existential && !existential.Property.IsInverse)
                {
                    AddRightExistential(subject, RoleOf(existential.Property.Property), AtomOf(namedFiller.Class.Iri));
                }
                else
                {
                    Unsupported.Add($"{currentRight.GetType().Name} on the superclass side is outside the EL classification calculus.");
                }
            }
        }

        //Names a subclass-side expression with an atom, emitting the
        //defining normal forms. Expressions nest arbitrarily, so the walk
        //runs post-order over an explicit stack: a node constructs once
        //every child has its atom.
        private int NameLeft(OwlClassExpression root)
        {
            Dictionary<OwlClassExpression, int> named = new(ReferenceEqualityComparer.Instance);
            Stack<OwlClassExpression> work = new();
            work.Push(root);

            while(work.Count > 0)
            {
                OwlClassExpression node = work.Peek();
                if(named.ContainsKey(node))
                {
                    work.Pop();

                    continue;
                }

                switch(node)
                {
                    case OwlClassReference reference:
                    {
                        named[node] = AtomOf(reference.Class.Iri);
                        work.Pop();
                        break;
                    }
                    case OwlObjectIntersectionOf intersection:
                    {
                        bool ready = true;
                        foreach(OwlClassExpression operand in intersection.Operands)
                        {
                            if(!named.ContainsKey(operand))
                            {
                                ready = false;
                                work.Push(operand);
                            }
                        }

                        if(!ready)
                        {
                            break;
                        }

                        //Fold the conjunction pairwise through fresh atoms:
                        //A₁ ⊓ A₂ ⊑ F, F ⊓ A₃ ⊑ F′, …
                        int current = intersection.Operands.Count == 0 ? Top : named[intersection.Operands[0]];
                        for(int i = 1; i < intersection.Operands.Count; i++)
                        {
                            int folded = FreshAtom();
                            AddConjunction(current, named[intersection.Operands[i]], folded);
                            current = folded;
                        }

                        named[node] = current;
                        work.Pop();
                        break;
                    }
                    case OwlObjectSomeValuesFrom existential when !existential.Property.IsInverse:
                    {
                        if(!named.TryGetValue(existential.Filler, out int filler))
                        {
                            work.Push(existential.Filler);

                            break;
                        }

                        //∃r.C on the left: F with ∃r.C ⊑ F.
                        int fresh = FreshAtom();
                        AddLeftExistential(RoleOf(existential.Property.Property), filler, fresh);
                        named[node] = fresh;
                        work.Pop();
                        break;
                    }
                    default:
                    {
                        Unsupported.Add($"{node.GetType().Name} is outside the EL classification calculus.");
                        named[node] = FreshAtom();
                        work.Pop();
                        break;
                    }
                }
            }

            return named[root];
        }

        private void NormalizeChain(OwlPropertyChainAxiom chain)
        {
            List<int> links = [];
            foreach(OwlObjectPropertyExpression link in chain.Chain)
            {
                if(link.IsInverse || chain.SuperProperty.IsInverse)
                {
                    Unsupported.Add("ObjectInverseOf in a property chain is outside EL.");

                    return;
                }

                links.Add(RoleOf(link.Property));
            }

            int conclusion = RoleOf(chain.SuperProperty.Property);

            if(links.Count == 1)
            {
                AddRoleSubsumption(links[0], conclusion);

                return;
            }

            //Longer chains decompose left-associatively with fresh roles:
            //r₁∘r₂ ⊑ f₁, f₁∘r₃ ⊑ f₂, …, fₙ₋₁∘rₙ ⊑ s.
            int current = links[0];
            for(int i = 1; i < links.Count; i++)
            {
                int target = i == links.Count - 1 ? conclusion : FreshRole();
                AddChain(current, links[i], target);
                current = target;
            }
        }

        //Saturation.

        /// <summary>Saturates the completion rules to fixpoint over the work queue.</summary>
        public void Saturate(CancellationToken cancellationToken)
        {
            //Initialise: every atom subsumes itself and ⊤.
            for(int atom = 0; atom < AtomNames.Count; atom++)
            {
                EnqueueSubsumer(atom, atom);
                EnqueueSubsumer(atom, Top);
            }

            while(Work.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                WorkItem item = Work.Dequeue();

                if(item.IsEdge)
                {
                    ProcessEdge(item.Role, item.Subject, item.Target);
                }
                else
                {
                    ProcessSubsumer(item.Subject, item.Atom);
                }
            }
        }

        //S(C) gains A: told subsumptions, conjunctions, right existentials,
        //and the left-existential join over incoming edges fire.
        private void ProcessSubsumer(int subject, int atom)
        {
            HashSet<int> subjectSubsumers = SubsumersOf(subject);

            //Told subsumption: A ⊑ B.
            if(ToldSubsumptions.TryGetValue(atom, out List<int>? told))
            {
                foreach(int b in told)
                {
                    EnqueueSubsumer(subject, b);
                }
            }

            //Conjunction: A ⊓ A′ ⊑ B with A′ already present.
            if(ConjunctionsByAtom.TryGetValue(atom, out List<(int Other, int Conclusion)>? conjunctions))
            {
                foreach((int other, int conclusion) in conjunctions)
                {
                    if(subjectSubsumers.Contains(other))
                    {
                        EnqueueSubsumer(subject, conclusion);
                    }
                }
            }

            //Right existential: A ⊑ ∃r.B introduces an edge.
            if(RightExistentials.TryGetValue(atom, out List<(int Role, int Filler)>? existentials))
            {
                foreach((int role, int filler) in existentials)
                {
                    EnqueueEdge(role, subject, filler);
                }
            }

            //Left-existential join over INCOMING edges: (C,subject) ∈ R(r)
            //and ∃r.atom ⊑ B puts B into S(C); ⊥ propagates backwards the
            //same way.
            foreach(KeyValuePair<int, HashSet<(int Source, int Target)>> roleEdges in Edges)
            {
                if(!EdgesByTarget.TryGetValue((roleEdges.Key, subject), out List<int>? sources))
                {
                    continue;
                }

                if(LeftExistentials.TryGetValue((roleEdges.Key, atom), out List<int>? conclusions))
                {
                    foreach(int source in sources)
                    {
                        foreach(int conclusion in conclusions)
                        {
                            EnqueueSubsumer(source, conclusion);
                        }
                    }
                }

                if(atom == Bottom)
                {
                    foreach(int source in sources)
                    {
                        EnqueueSubsumer(source, Bottom);
                    }
                }
            }
        }

        //R(r) gains (source, target): hierarchy, ranges, the
        //left-existential join, bottom propagation, and compositions fire.
        private void ProcessEdge(int role, int source, int target)
        {
            //Role hierarchy: r ⊑ s.
            if(RoleSubsumptions.TryGetValue(role, out List<int>? superRoles))
            {
                foreach(int super in superRoles)
                {
                    EnqueueEdge(super, source, target);
                }
            }

            //Ranges: the target gains r's range atoms.
            if(RangesByRole.TryGetValue(role, out List<int>? ranges))
            {
                foreach(int range in ranges)
                {
                    EnqueueSubsumer(target, range);
                }
            }

            //Left-existential elimination over the target's current
            //subsumers — snapshotted, because a self-edge makes the source
            //and target sets the same instance.
            foreach(int atom in (int[])[.. SubsumersOf(target)])
            {
                if(LeftExistentials.TryGetValue((role, atom), out List<int>? conclusions))
                {
                    foreach(int conclusion in conclusions)
                    {
                        EnqueueSubsumer(source, conclusion);
                    }
                }
            }

            //Bottom propagation: an edge into an unsatisfiable target.
            if(SubsumersOf(target).Contains(Bottom))
            {
                EnqueueSubsumer(source, Bottom);
            }

            //Composition r₁∘r₂ ⊑ s, this edge as the FIRST link.
            if(ChainsByFirst.TryGetValue(role, out List<(int Second, int Conclusion)>? asFirst))
            {
                foreach((int second, int conclusion) in asFirst)
                {
                    if(EdgesBySource.TryGetValue((second, target), out List<int>? continuations))
                    {
                        foreach(int end in continuations)
                        {
                            EnqueueEdge(conclusion, source, end);
                        }
                    }
                }
            }

            //Composition with this edge as the SECOND link.
            if(ChainsBySecond.TryGetValue(role, out List<(int First, int Conclusion)>? asSecond))
            {
                foreach((int first, int conclusion) in asSecond)
                {
                    if(EdgesByTarget.TryGetValue((first, source), out List<int>? starts))
                    {
                        foreach(int start in starts)
                        {
                            EnqueueEdge(conclusion, start, target);
                        }
                    }
                }
            }
        }

        //Result.

        /// <summary>Projects the saturation onto the user-named classes.</summary>
        public ElClassification BuildResult()
        {
            Dictionary<Utf8String, IReadOnlySet<Utf8String>> result = [];
            HashSet<Utf8String> unsatisfiable = [];

            foreach(int atom in NamedAtoms)
            {
                if(AtomNames[atom] is not Utf8String name)
                {
                    continue;
                }

                HashSet<Utf8String> named = [];
                bool isUnsatisfiable = false;
                foreach(int subsumer in SubsumersOf(atom))
                {
                    if(subsumer == Bottom)
                    {
                        isUnsatisfiable = true;
                    }

                    if(AtomNames[subsumer] is Utf8String subsumerName && (NamedAtoms.Contains(subsumer) || subsumer == Top))
                    {
                        named.Add(subsumerName);
                    }
                }

                result[name] = named;
                if(isUnsatisfiable)
                {
                    unsatisfiable.Add(name);
                }
            }

            return new ElClassification(result, unsatisfiable, Unsupported);
        }

        //Interning and index helpers.

        private OwlClassReference AtomReference(int atom)
        {
            //Fresh atoms get a synthetic IRI node only for re-entry into the
            //normalization queue; it never reaches the result.
            Utf8String name = AtomNames[atom] ?? Utf8String.FromString($"urn:veritas:el:{atom}");
            AtomNames[atom] ??= name;
            AtomIds[name] = atom;

            return new OwlClassReference(new NamedNode(name));
        }

        private int AtomOf(Utf8String iri)
        {
            if(AtomIds.TryGetValue(iri, out int existing))
            {
                return existing;
            }

            int atom = AtomNames.Count;
            AtomNames.Add(iri);
            AtomIds[iri] = atom;
            NamedAtoms.Add(atom);

            return atom;
        }

        private int FreshAtom()
        {
            int atom = AtomNames.Count;
            AtomNames.Add(null);

            return atom;
        }

        private int RoleOf(NamedNode property)
        {
            if(RoleIds.TryGetValue(property.Iri, out int existing))
            {
                return existing;
            }

            int role = RoleNames.Count;
            RoleNames.Add(property.Iri);
            RoleIds[property.Iri] = role;

            return role;
        }

        private int FreshRole()
        {
            int role = RoleNames.Count;
            RoleNames.Add(null);

            return role;
        }

        private void AddToldSubsumption(int sub, int super)
        {
            if(!ToldSubsumptions.TryGetValue(sub, out List<int>? list))
            {
                list = [];
                ToldSubsumptions[sub] = list;
            }

            list.Add(super);
        }

        private void AddConjunction(int first, int second, int conclusion)
        {
            if(!Conjunctions.TryGetValue((first, second), out List<int>? list))
            {
                list = [];
                Conjunctions[(first, second)] = list;
            }

            list.Add(conclusion);

            AddConjunctionByAtom(first, second, conclusion);
            AddConjunctionByAtom(second, first, conclusion);
        }

        private void AddConjunctionByAtom(int atom, int other, int conclusion)
        {
            if(!ConjunctionsByAtom.TryGetValue(atom, out List<(int Other, int Conclusion)>? list))
            {
                list = [];
                ConjunctionsByAtom[atom] = list;
            }

            list.Add((other, conclusion));
        }

        private void AddRightExistential(int subject, int role, int filler)
        {
            if(!RightExistentials.TryGetValue(subject, out List<(int Role, int Filler)>? list))
            {
                list = [];
                RightExistentials[subject] = list;
            }

            list.Add((role, filler));
        }

        private void AddLeftExistential(int role, int filler, int conclusion)
        {
            if(!LeftExistentials.TryGetValue((role, filler), out List<int>? list))
            {
                list = [];
                LeftExistentials[(role, filler)] = list;
            }

            list.Add(conclusion);
        }

        private void AddRoleSubsumption(int sub, int super)
        {
            if(!RoleSubsumptions.TryGetValue(sub, out List<int>? list))
            {
                list = [];
                RoleSubsumptions[sub] = list;
            }

            list.Add(super);
        }

        private void AddChain(int first, int second, int conclusion)
        {
            if(!ChainsByFirst.TryGetValue(first, out List<(int Second, int Conclusion)>? byFirst))
            {
                byFirst = [];
                ChainsByFirst[first] = byFirst;
            }

            byFirst.Add((second, conclusion));

            if(!ChainsBySecond.TryGetValue(second, out List<(int First, int Conclusion)>? bySecond))
            {
                bySecond = [];
                ChainsBySecond[second] = bySecond;
            }

            bySecond.Add((first, conclusion));
        }

        private HashSet<int> SubsumersOf(int atom)
        {
            if(!Subsumers.TryGetValue(atom, out HashSet<int>? set))
            {
                set = [];
                Subsumers[atom] = set;
            }

            return set;
        }

        private void EnqueueSubsumer(int subject, int atom)
        {
            if(SubsumersOf(subject).Add(atom))
            {
                Work.Enqueue(new WorkItem(IsEdge: false, subject, atom, Role: 0, Target: 0));
            }
        }

        private void EnqueueEdge(int role, int source, int target)
        {
            if(!Edges.TryGetValue(role, out HashSet<(int Source, int Target)>? set))
            {
                set = [];
                Edges[role] = set;
            }

            if(!set.Add((source, target)))
            {
                return;
            }

            if(!EdgesBySource.TryGetValue((role, source), out List<int>? bySource))
            {
                bySource = [];
                EdgesBySource[(role, source)] = bySource;
            }

            bySource.Add(target);

            if(!EdgesByTarget.TryGetValue((role, target), out List<int>? byTarget))
            {
                byTarget = [];
                EdgesByTarget[(role, target)] = byTarget;
            }

            byTarget.Add(source);

            Work.Enqueue(new WorkItem(IsEdge: true, Subject: source, Atom: 0, role, target));
        }
    }
}
