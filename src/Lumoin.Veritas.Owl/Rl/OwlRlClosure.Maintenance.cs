using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Owl.Rl;

public static partial class OwlRlClosure
{
    internal sealed partial class ClosureContext
    {
        /// <summary>The current asserted facts — the base the maintained closure is kept equal to the from-scratch closure of.</summary>
        private HashSet<EncodedTriple> Base { get; } = [];

        /// <summary>The datatype-hierarchy seed triples recorded at construction; deletion never propagates through a seed.</summary>
        private HashSet<EncodedTriple> Seeded { get; } = [];

        /// <summary>The facts marked for deletion during an overdelete pass — cumulative across the pass's rounds.</summary>
        private HashSet<EncodedTriple> DeletionSet { get; } = [];

        /// <summary>The facts the current overdelete round marked, seeding the next round; the marking sink appends here.</summary>
        private List<EncodedTriple> NextFrontier { get; set; } = [];

        /// <summary>The overdelete round's predicate → (subject, object) pairs — the deletion-mode counterpart of <see cref="ByPredicate"/>.</summary>
        private Dictionary<TermId, List<(TermId Subject, TermId Object)>> FrontierByPredicate { get; } = [];

        /// <summary>The overdelete round's (subject, predicate) → objects — the deletion-mode counterpart of <see cref="BySubjectPredicate"/>.</summary>
        private Dictionary<(TermId Subject, TermId Predicate), List<TermId>> FrontierBySubjectPredicate { get; } = [];

        /// <summary>The overdelete round's (object, predicate) → subjects — the deletion-mode counterpart of <see cref="ByObjectPredicate"/>.</summary>
        private Dictionary<(TermId Object, TermId Predicate), List<TermId>> FrontierByObjectPredicate { get; } = [];

        /// <summary>The overdelete round's type → instances — the deletion-mode counterpart of <see cref="InstancesOf"/>.</summary>
        private Dictionary<TermId, List<TermId>> FrontierInstancesOf { get; } = [];

        /// <summary>The overdelete round's marked triples in order — the deletion-mode twin of <see cref="MergedThisRound"/>.</summary>
        private List<EncodedTriple> FrontierTriples { get; } = [];

        /// <summary>The overdelete round's marked triples as a set — the deletion-mode twin of <see cref="MergedThisRoundSet"/>.</summary>
        private HashSet<EncodedTriple> FrontierSet { get; } = [];

        /// <summary>Whether the overdelete round's frontier holds a list-structuring triple — the deletion-mode counterpart of <see cref="ListStructureDirty"/>.</summary>
        private bool FrontierListStructureDirty { get; set; }

        /// <summary>Whether rule conclusions route to the deletion marking sink instead of the derive-side pending set.</summary>
        private bool OverdeleteSink { get; set; }

        /// <summary>The restriction subjects a choice-predicate edit touched — their old-choice bodies are re-processed.</summary>
        private HashSet<TermId> RestrictionOwners { get; } = [];

        /// <summary>The negative-property-assertion nodes a read-predicate edit touched — their bodies are re-checked live.</summary>
        private HashSet<TermId> NpaOwners { get; } = [];

        /// <summary>Whether a list-structuring triple was edited, so every list-consuming construct is re-processed.</summary>
        private bool AllListConsumersDirty { get; set; }

        /// <summary>The number of facts marked after the overdelete fixpoint — the size of <see cref="DeletionSet"/>.</summary>
        private int StatOverdeleteMarked { get; set; }

        /// <summary>The number of overdelete rounds run in the current pass.</summary>
        private int StatDeletionRounds { get; set; }

        /// <summary>The number of deleted facts the head-bound matcher restored directly.</summary>
        private int StatDirectlyRederived { get; set; }

        /// <summary>The number of marked facts present again in the closure at completion.</summary>
        private int StatRestoredTotal { get; set; }

        /// <summary>The number of semi-naive insert rounds run.</summary>
        private int StatInsertRounds { get; set; }

        /// <summary>The number of choice/list owner construct re-fires.</summary>
        private int StatChoiceOwnerReFires { get; set; }

        /// <summary>The number of base additions that demoted an existing derived fact.</summary>
        private int StatBaseDemotions { get; set; }

        /// <summary>The number of seeded removals promoted to derived.</summary>
        private int StatBasePromotions { get; set; }

        /// <summary>The statistics of the last <see cref="ApplyCore"/>.</summary>
        internal OwlRlMaintenanceStatistics MaintenanceStatistics { get; private set; }

        /// <summary>Whether the accumulated-set and derived-set membership changes are being recorded — true for the span of one <see cref="ApplyCore"/>, false through the from-scratch build, so the shared <see cref="MergePending"/> records only on the maintained incremental path.</summary>
        private bool RecordingMembershipDeltas { get; set; }

        /// <summary>The facts that net-entered <see cref="All"/> during the current <see cref="ApplyCore"/>; folded against <see cref="AllLeftFacts"/> so a fact that leaves then re-enters within one Apply appears in neither.</summary>
        private HashSet<EncodedTriple> AllEnteredFacts { get; } = [];

        /// <summary>The facts that net-left <see cref="All"/> during the current <see cref="ApplyCore"/>.</summary>
        private HashSet<EncodedTriple> AllLeftFacts { get; } = [];

        /// <summary>The facts that net-entered <see cref="Derived"/> during the current <see cref="ApplyCore"/>; folded against <see cref="DerivedLeftFacts"/>.</summary>
        private HashSet<EncodedTriple> DerivedEnteredFacts { get; } = [];

        /// <summary>The facts that net-left <see cref="Derived"/> during the current <see cref="ApplyCore"/>.</summary>
        private HashSet<EncodedTriple> DerivedLeftFacts { get; } = [];

        /// <summary>The net membership change to <see cref="All"/> (base ∪ derived) the last maintained <see cref="ApplyCore"/> recorded — a live view valid until the next Apply resets it.</summary>
        internal OwlRlMembershipDelta AllDelta => new(AllEnteredFacts, AllLeftFacts);

        /// <summary>The net membership change to <see cref="Derived"/> the last maintained <see cref="ApplyCore"/> recorded — a live view valid until the next Apply resets it.</summary>
        internal OwlRlMembershipDelta DerivedDelta => new(DerivedEnteredFacts, DerivedLeftFacts);

        /// <summary>Records that <paramref name="triple"/> entered <see cref="All"/>, cancelling a same-Apply leave so a leave-then-re-enter folds to neither side; a no-op unless <see cref="RecordingMembershipDeltas"/>.</summary>
        /// <param name="triple">The triple that entered the accumulated set.</param>
        private void RecordAllEntered(EncodedTriple triple)
        {
            if(RecordingMembershipDeltas && !AllLeftFacts.Remove(triple))
            {
                AllEnteredFacts.Add(triple);
            }
        }

        /// <summary>Records that <paramref name="triple"/> left <see cref="All"/>, cancelling a same-Apply entry; a no-op unless <see cref="RecordingMembershipDeltas"/>.</summary>
        /// <param name="triple">The triple that left the accumulated set.</param>
        private void RecordAllLeft(EncodedTriple triple)
        {
            if(RecordingMembershipDeltas && !AllEnteredFacts.Remove(triple))
            {
                AllLeftFacts.Add(triple);
            }
        }

        /// <summary>Records that <paramref name="triple"/> entered <see cref="Derived"/>, cancelling a same-Apply leave; a no-op unless <see cref="RecordingMembershipDeltas"/>.</summary>
        /// <param name="triple">The triple that entered the derived set.</param>
        private void RecordDerivedEntered(EncodedTriple triple)
        {
            if(RecordingMembershipDeltas && !DerivedLeftFacts.Remove(triple))
            {
                DerivedEnteredFacts.Add(triple);
            }
        }

        /// <summary>Records that <paramref name="triple"/> left <see cref="Derived"/>, cancelling a same-Apply entry; a no-op unless <see cref="RecordingMembershipDeltas"/>.</summary>
        /// <param name="triple">The triple that left the derived set.</param>
        private void RecordDerivedLeft(EncodedTriple triple)
        {
            if(RecordingMembershipDeltas && !DerivedEnteredFacts.Remove(triple))
            {
                DerivedLeftFacts.Add(triple);
            }
        }

        /// <summary>Clears the recorded membership deltas — called at the start of each maintained Apply so each records only its own net change, and for the empty short-circuit that records nothing.</summary>
        internal void ResetMembershipDeltas()
        {
            AllEnteredFacts.Clear();
            AllLeftFacts.Clear();
            DerivedEnteredFacts.Clear();
            DerivedLeftFacts.Clear();
        }

        /// <summary>The predicate's frontier pairs — the deletion-mode counterpart of <see cref="DeltaPairs"/>.</summary>
        /// <param name="predicate">The predicate whose frontier pairs are wanted.</param>
        /// <returns>The (subject, object) pairs marked this round under <paramref name="predicate"/>.</returns>
        private List<(TermId Subject, TermId Object)> FrontierPairs(TermId predicate)
        {
            return FrontierByPredicate.TryGetValue(predicate, out List<(TermId Subject, TermId Object)>? pairs) ? pairs : [];
        }

        /// <summary>The (subject, predicate) pair's frontier objects — the deletion-mode counterpart of <see cref="DeltaObjectsTail"/>.</summary>
        /// <param name="key">The (subject, predicate) pair.</param>
        /// <returns>The objects marked this round under that pair.</returns>
        private List<TermId> FrontierObjects((TermId Subject, TermId Predicate) key)
        {
            return FrontierBySubjectPredicate.TryGetValue(key, out List<TermId>? objects) ? objects : [];
        }

        /// <summary>The (object, predicate) pair's frontier subjects — the deletion-mode counterpart of <see cref="DeltaSubjectsTail"/>.</summary>
        /// <param name="key">The (object, predicate) pair.</param>
        /// <returns>The subjects marked this round under that pair.</returns>
        private List<TermId> FrontierSubjects((TermId Object, TermId Predicate) key)
        {
            return FrontierByObjectPredicate.TryGetValue(key, out List<TermId>? subjects) ? subjects : [];
        }

        /// <summary>The type's frontier instances — the deletion-mode counterpart of <see cref="DeltaInstancesTail"/>.</summary>
        /// <param name="type">The type whose marked instances are wanted.</param>
        /// <returns>The instances marked this round under <paramref name="type"/>.</returns>
        private List<TermId> FrontierInstances(TermId type)
        {
            return FrontierInstancesOf.TryGetValue(type, out List<TermId>? instances) ? instances : [];
        }

        /// <summary>Whether the type gained any frontier instance this round — the deletion-mode counterpart of <see cref="HasDeltaInstances"/>.</summary>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> when the type's instance list grew in this round's frontier.</returns>
        private bool FrontierHasInstances(TermId type)
        {
            return FrontierInstancesOf.ContainsKey(type);
        }

        /// <summary>Whether the axiom triple is in this round's frontier — the deletion-mode counterpart of <see cref="DeltaAxiomTouched"/>.</summary>
        /// <param name="subject">The axiom's subject.</param>
        /// <param name="object">The axiom's object — the list head.</param>
        /// <param name="predicate">The axiom predicate.</param>
        /// <returns><c>true</c> when the axiom triple is in this round's frontier.</returns>
        private bool FrontierAxiomTouched(TermId subject, TermId @object, TermId predicate)
        {
            return FrontierSet.Contains(Fact(subject, predicate, @object));
        }

        /// <summary>Marks one rule conclusion for deletion when it is a present, non-base, non-seed fact newly reached, appending it to the next round's frontier.</summary>
        /// <param name="conclusion">The conclusion a rule body produced while marking.</param>
        private void MarkOverdeleteCandidate(EncodedTriple conclusion)
        {
            if(All.Contains(conclusion) && !Base.Contains(conclusion) && !Seeded.Contains(conclusion) && DeletionSet.Add(conclusion))
            {
                NextFrontier.Add(conclusion);
            }
        }

        /// <summary>Rebuilds the frontier groupings from one round's marked triples, so the overdelete families read the round's deletions the way the delta families read a round's derivations.</summary>
        /// <param name="frontier">The round's marked triples.</param>
        private void RebuildFrontierGroupings(List<EncodedTriple> frontier)
        {
            FrontierByPredicate.Clear();
            FrontierBySubjectPredicate.Clear();
            FrontierByObjectPredicate.Clear();
            FrontierInstancesOf.Clear();
            FrontierTriples.Clear();
            FrontierSet.Clear();
            FrontierListStructureDirty = false;

            foreach(EncodedTriple triple in frontier)
            {
                FrontierTriples.Add(triple);
                FrontierSet.Add(triple);

                if(triple.Predicate == Terms.First || triple.Predicate == Terms.Rest || triple.Predicate == Terms.Members || triple.Predicate == Terms.DistinctMembers)
                {
                    FrontierListStructureDirty = true;
                }

                if(!FrontierByPredicate.TryGetValue(triple.Predicate, out List<(TermId Subject, TermId Object)>? pairs))
                {
                    pairs = [];
                    FrontierByPredicate[triple.Predicate] = pairs;
                }

                pairs.Add((triple.Subject, triple.Object));

                if(!FrontierBySubjectPredicate.TryGetValue((triple.Subject, triple.Predicate), out List<TermId>? objects))
                {
                    objects = [];
                    FrontierBySubjectPredicate[(triple.Subject, triple.Predicate)] = objects;
                }

                objects.Add(triple.Object);

                if(!FrontierByObjectPredicate.TryGetValue((triple.Object, triple.Predicate), out List<TermId>? subjects))
                {
                    subjects = [];
                    FrontierByObjectPredicate[(triple.Object, triple.Predicate)] = subjects;
                }

                subjects.Add(triple.Subject);

                if(triple.Predicate == Terms.Type)
                {
                    if(!FrontierInstancesOf.TryGetValue(triple.Object, out List<TermId>? instances))
                    {
                        instances = [];
                        FrontierInstancesOf[triple.Object] = instances;
                    }

                    instances.Add(triple.Subject);
                }
            }
        }

        /// <summary>Clears the transient overdelete-round groupings and the pending frontier — the frontier state carries nothing across an <see cref="ApplyCore"/>.</summary>
        private void ClearFrontierState()
        {
            FrontierByPredicate.Clear();
            FrontierBySubjectPredicate.Clear();
            FrontierByObjectPredicate.Clear();
            FrontierInstancesOf.Clear();
            FrontierTriples.Clear();
            FrontierSet.Clear();
            FrontierListStructureDirty = false;
            NextFrontier = [];
        }

        /// <summary>Marks the eq-* deletions over the round's frontier — eq-ref over the frontier triples' terms, the sameAs pair block, the differentFrom symmetry, and eq-rep over old equalities and frontier triples; the eq-diff falsities mark nothing.</summary>
        private void FireEqualityOverdelete()
        {
            //eq-ref: a deleted triple marks its terms' self-equalities; the
            //rederive pass restores any whose term a surviving triple still
            //mentions.
            foreach(EncodedTriple triple in FrontierTriples)
            {
                Add(triple.Subject, Terms.SameAs, triple.Subject, EntailmentRules.EqRef, [triple]);
                Add(triple.Predicate, Terms.SameAs, triple.Predicate, EntailmentRules.EqRef, [triple]);
                Add(triple.Object, Terms.SameAs, triple.Object, EntailmentRules.EqRef, [triple]);
            }

            foreach((TermId x, TermId y) in FrontierPairs(Terms.SameAs))
            {
                EncodedTriple same = Fact(x, Terms.SameAs, y);

                Add(y, Terms.SameAs, x, EntailmentRules.EqSym, [same]);

                foreach(TermId z in ObjectsOf(y, Terms.SameAs))
                {
                    Add(x, Terms.SameAs, z, EntailmentRules.EqTrans, [same, Fact(y, Terms.SameAs, z)]);
                }

                foreach(TermId w in SubjectsOf(x, Terms.SameAs))
                {
                    Add(w, Terms.SameAs, y, EntailmentRules.EqTrans, [Fact(w, Terms.SameAs, x), same]);
                }

                if(ObjectsOf(x, Terms.DifferentFrom).Contains(y))
                {
                    Inconsistent(EntailmentRules.EqDiff1, [same, Fact(x, Terms.DifferentFrom, y)]);

                    return;
                }

                if(x != y && DatatypeOracle.LiteralsKnownDistinct(x, y))
                {
                    Inconsistent(EntailmentRules.DtDiff, [same]);

                    return;
                }

                if(x != y && DatatypeOracle.DatatypesKnownDisjoint(x, y))
                {
                    Inconsistent(EntailmentRules.DtDisjointIdentity, [same]);

                    return;
                }

                if(x == y)
                {
                    continue;
                }

                foreach(TermId subjectPredicate in PredicatesOfSubjectList(x))
                {
                    foreach(TermId o in ObjectsOf(x, subjectPredicate))
                    {
                        Add(y, subjectPredicate, o, EntailmentRules.EqRepS, [same, Fact(x, subjectPredicate, o)]);
                    }
                }

                foreach((TermId s, TermId o) in Pairs(x))
                {
                    Add(s, y, o, EntailmentRules.EqRepP, [same, Fact(s, x, o)]);
                }

                foreach(TermId objectPredicate in PredicatesOfObjectList(x))
                {
                    foreach(TermId s in SubjectsOf(x, objectPredicate))
                    {
                        Add(s, objectPredicate, y, EntailmentRules.EqRepO, [same, Fact(s, objectPredicate, x)]);
                    }
                }
            }

            foreach((TermId x, TermId y) in FrontierPairs(Terms.DifferentFrom))
            {
                Add(y, Terms.DifferentFrom, x, EntailmentRules.DifferentFromSymmetry, [Fact(x, Terms.DifferentFrom, y)]);

                if(ObjectsOf(x, Terms.SameAs).Contains(y))
                {
                    Inconsistent(EntailmentRules.EqDiff1, [Fact(x, Terms.SameAs, y), Fact(x, Terms.DifferentFrom, y)]);

                    return;
                }
            }

            foreach(EncodedTriple triple in FrontierTriples)
            {
                EqRepOverFrontierTriple(triple.Subject, triple, Position.Subject);
                EqRepOverFrontierTriple(triple.Predicate, triple, Position.Predicate);
                EqRepOverFrontierTriple(triple.Object, triple, Position.Object);
            }
        }

        /// <summary>Marks the eq-rep deletions of every old <c>owl:sameAs</c> of <paramref name="term"/> applied to the frontier <paramref name="triple"/> at <paramref name="position"/> — the deletion-mode counterpart of <see cref="EqRepOverNewTriple"/>.</summary>
        /// <param name="term">The term at <paramref name="position"/> whose old equalities are applied.</param>
        /// <param name="triple">The frontier triple.</param>
        /// <param name="position">The term's position within the triple.</param>
        private void EqRepOverFrontierTriple(TermId term, EncodedTriple triple, Position position)
        {
            foreach(TermId y in ObjectsOf(term, Terms.SameAs))
            {
                if(y == term || FrontierSet.Contains(Fact(term, Terms.SameAs, y)))
                {
                    continue;
                }

                EncodedTriple same = Fact(term, Terms.SameAs, y);
                switch(position)
                {
                    case Position.Subject:
                        Add(y, triple.Predicate, triple.Object, EntailmentRules.EqRepS, [same, triple]);

                        break;

                    case Position.Predicate:
                        Add(triple.Subject, y, triple.Object, EntailmentRules.EqRepP, [same, triple]);

                        break;

                    case Position.Object:
                        Add(triple.Subject, triple.Predicate, y, EntailmentRules.EqRepO, [same, triple]);

                        break;

                    default:

                        break;
                }
            }
        }

        /// <summary>Marks the prp-* deletions over the round's frontier — domain, range and the range intersection, characteristics, sub-property, chains, equivalence, disjointness, inverses, and keys.</summary>
        private void FirePropertiesOverdelete()
        {
            foreach((TermId p, TermId c) in FrontierPairs(Terms.Domain))
            {
                EncodedTriple domain = Fact(p, Terms.Domain, c);
                foreach((TermId x, TermId y) in Pairs(p))
                {
                    Add(x, Terms.Type, c, EntailmentRules.PrpDom, [domain, Fact(x, p, y)]);
                }
            }

            foreach(EncodedTriple t in FrontierTriples)
            {
                foreach(TermId c in ObjectsOf(t.Predicate, Terms.Domain))
                {
                    Add(t.Subject, Terms.Type, c, EntailmentRules.PrpDom, [Fact(t.Predicate, Terms.Domain, c), t]);
                }
            }

            foreach((TermId p, TermId c) in FrontierPairs(Terms.Range))
            {
                EncodedTriple range = Fact(p, Terms.Range, c);
                foreach((TermId x, TermId y) in Pairs(p))
                {
                    if(DatatypeOracle.LiteralOutsideDatatype(y, c))
                    {
                        Inconsistent(EntailmentRules.DtNotType, [range, Fact(x, p, y)]);

                        return;
                    }

                    Add(y, Terms.Type, c, EntailmentRules.PrpRng, [range, Fact(x, p, y)]);
                }
            }

            foreach(EncodedTriple t in FrontierTriples)
            {
                foreach(TermId c in ObjectsOf(t.Predicate, Terms.Range))
                {
                    if(DatatypeOracle.LiteralOutsideDatatype(t.Object, c))
                    {
                        Inconsistent(EntailmentRules.DtNotType, [Fact(t.Predicate, Terms.Range, c), t]);

                        return;
                    }

                    Add(t.Object, Terms.Type, c, EntailmentRules.PrpRng, [Fact(t.Predicate, Terms.Range, c), t]);
                }
            }

            //dt-range-intersection: a deleted range pairs with the full current
            //range list in both operand roles, marking every superset it confines.
            foreach((TermId p, TermId d) in FrontierPairs(Terms.Range))
            {
                List<TermId> ranges = ObjectsOf(p, Terms.Range);
                foreach(TermId other in ranges)
                {
                    if(other == d)
                    {
                        continue;
                    }

                    foreach(TermId superset in DatatypeOracle.RangeIntersectionSupersets(d, other))
                    {
                        if(superset != d && superset != other)
                        {
                            Add(p, Terms.Range, superset, EntailmentRules.DtRangeIntersection, [Fact(p, Terms.Range, d), Fact(p, Terms.Range, other)]);
                        }
                    }

                    foreach(TermId superset in DatatypeOracle.RangeIntersectionSupersets(other, d))
                    {
                        if(superset != d && superset != other)
                        {
                            Add(p, Terms.Range, superset, EntailmentRules.DtRangeIntersection, [Fact(p, Terms.Range, other), Fact(p, Terms.Range, d)]);
                        }
                    }
                }
            }

            //A deleted characteristic typing re-fires the naive characteristic
            //body, marking the property's whole characteristic materialisation.
            foreach((TermId p, TermId characteristic) in FrontierPairs(Terms.Type))
            {
                FireCharacteristic(p, characteristic);
            }

            FireCharacteristicDataOverdelete();

            foreach((TermId p1, TermId p2) in FrontierPairs(Terms.SubPropertyOf))
            {
                EncodedTriple subProperty = Fact(p1, Terms.SubPropertyOf, p2);
                foreach((TermId x, TermId y) in Pairs(p1))
                {
                    Add(x, p2, y, EntailmentRules.PrpSpo1, [subProperty, Fact(x, p1, y)]);
                }
            }

            foreach(EncodedTriple t in FrontierTriples)
            {
                foreach(TermId p2 in ObjectsOf(t.Predicate, Terms.SubPropertyOf))
                {
                    Add(t.Subject, p2, t.Object, EntailmentRules.PrpSpo1, [Fact(t.Predicate, Terms.SubPropertyOf, p2), t]);
                }
            }

            FireChainAxiomsOverdelete();

            foreach((TermId p1, TermId p2) in FrontierPairs(Terms.EquivalentProperty))
            {
                EncodedTriple equivalent = Fact(p1, Terms.EquivalentProperty, p2);
                foreach((TermId x, TermId y) in Pairs(p1))
                {
                    Add(x, p2, y, EntailmentRules.PrpEqp1, [equivalent, Fact(x, p1, y)]);
                }

                foreach((TermId x, TermId y) in Pairs(p2))
                {
                    Add(x, p1, y, EntailmentRules.PrpEqp2, [equivalent, Fact(x, p2, y)]);
                }
            }

            foreach(EncodedTriple t in FrontierTriples)
            {
                foreach(TermId p2 in ObjectsOf(t.Predicate, Terms.EquivalentProperty))
                {
                    Add(t.Subject, p2, t.Object, EntailmentRules.PrpEqp1, [Fact(t.Predicate, Terms.EquivalentProperty, p2), t]);
                }

                foreach(TermId p1 in SubjectsOf(t.Predicate, Terms.EquivalentProperty))
                {
                    Add(t.Subject, p1, t.Object, EntailmentRules.PrpEqp2, [Fact(p1, Terms.EquivalentProperty, t.Predicate), t]);
                }
            }

            foreach((TermId p1, TermId p2) in FrontierPairs(Terms.PropertyDisjointWith))
            {
                EncodedTriple disjoint = Fact(p1, Terms.PropertyDisjointWith, p2);
                Add(p2, Terms.PropertyDisjointWith, p1, EntailmentRules.PrpPdw, [disjoint]);
                foreach((TermId x, TermId y) in Pairs(p1))
                {
                    if(ObjectsOf(x, p2).Contains(y))
                    {
                        Inconsistent(EntailmentRules.PrpPdw, [disjoint, Fact(x, p1, y), Fact(x, p2, y)]);

                        return;
                    }
                }
            }

            foreach(EncodedTriple t in FrontierTriples)
            {
                foreach(TermId p2 in ObjectsOf(t.Predicate, Terms.PropertyDisjointWith))
                {
                    if(ObjectsOf(t.Subject, p2).Contains(t.Object))
                    {
                        Inconsistent(EntailmentRules.PrpPdw, [Fact(t.Predicate, Terms.PropertyDisjointWith, p2), t, Fact(t.Subject, p2, t.Object)]);

                        return;
                    }
                }

                foreach(TermId p1 in SubjectsOf(t.Predicate, Terms.PropertyDisjointWith))
                {
                    if(ObjectsOf(t.Subject, p1).Contains(t.Object))
                    {
                        Inconsistent(EntailmentRules.PrpPdw, [Fact(p1, Terms.PropertyDisjointWith, t.Predicate), Fact(t.Subject, p1, t.Object), t]);

                        return;
                    }
                }
            }

            FireAllDisjointPropertiesOverdelete();

            foreach((TermId p1, TermId p2) in FrontierPairs(Terms.InverseOf))
            {
                EncodedTriple inverse = Fact(p1, Terms.InverseOf, p2);
                foreach((TermId x, TermId y) in Pairs(p1))
                {
                    Add(y, p2, x, EntailmentRules.PrpInv1, [inverse, Fact(x, p1, y)]);
                }

                foreach((TermId x, TermId y) in Pairs(p2))
                {
                    Add(y, p1, x, EntailmentRules.PrpInv2, [inverse, Fact(x, p2, y)]);
                }
            }

            foreach(EncodedTriple t in FrontierTriples)
            {
                foreach(TermId p2 in ObjectsOf(t.Predicate, Terms.InverseOf))
                {
                    Add(t.Object, p2, t.Subject, EntailmentRules.PrpInv1, [Fact(t.Predicate, Terms.InverseOf, p2), t]);
                }

                foreach(TermId p1 in SubjectsOf(t.Predicate, Terms.InverseOf))
                {
                    Add(t.Object, p1, t.Subject, EntailmentRules.PrpInv2, [Fact(p1, Terms.InverseOf, t.Predicate), t]);
                }
            }

            FireKeyOverdelete();
        }

        /// <summary>Marks the per-edge and per-typing characteristic deletions — functional and inverse-functional over the deleted objects, symmetry and transitivity over the deleted edges, and reflexive over deleted named individuals; irp / asyp mark nothing.</summary>
        private void FireCharacteristicDataOverdelete()
        {
            long characteristicDataStart = OwlRlMaintenanceInstrumentation.Begin();

            foreach(KeyValuePair<(TermId Subject, TermId Predicate), List<TermId>> entry in FrontierBySubjectPredicate)
            {
                (TermId s, TermId p) = entry.Key;
                if(!HasType(p, Terms.FunctionalProperty))
                {
                    continue;
                }

                EncodedTriple typing = Fact(p, Terms.Type, Terms.FunctionalProperty);
                List<TermId> objects = ObjectsOf(s, p);
                foreach(TermId deleted in entry.Value)
                {
                    foreach(TermId other in objects)
                    {
                        if(other != deleted)
                        {
                            Add(deleted, Terms.SameAs, other, EntailmentRules.PrpFp, [typing, Fact(s, p, deleted), Fact(s, p, other)]);
                            Add(other, Terms.SameAs, deleted, EntailmentRules.PrpFp, [typing, Fact(s, p, other), Fact(s, p, deleted)]);
                        }
                    }
                }
            }

            foreach(KeyValuePair<(TermId Object, TermId Predicate), List<TermId>> entry in FrontierByObjectPredicate)
            {
                (TermId o, TermId p) = entry.Key;
                if(!HasType(p, Terms.InverseFunctionalProperty))
                {
                    continue;
                }

                EncodedTriple typing = Fact(p, Terms.Type, Terms.InverseFunctionalProperty);
                List<TermId> subjects = SubjectsOf(o, p);
                foreach(TermId deleted in entry.Value)
                {
                    foreach(TermId other in subjects)
                    {
                        if(other != deleted)
                        {
                            Add(deleted, Terms.SameAs, other, EntailmentRules.PrpIfp, [typing, Fact(deleted, p, o), Fact(other, p, o)]);
                            Add(other, Terms.SameAs, deleted, EntailmentRules.PrpIfp, [typing, Fact(other, p, o), Fact(deleted, p, o)]);
                        }
                    }
                }
            }

            foreach(EncodedTriple t in FrontierTriples)
            {
                if(HasType(t.Predicate, Terms.SymmetricProperty))
                {
                    Add(t.Object, t.Predicate, t.Subject, EntailmentRules.PrpSymp, [Fact(t.Predicate, Terms.Type, Terms.SymmetricProperty), t]);
                }

                if(HasType(t.Predicate, Terms.TransitiveProperty))
                {
                    EncodedTriple typing = Fact(t.Predicate, Terms.Type, Terms.TransitiveProperty);
                    foreach(TermId z in ObjectsOf(t.Object, t.Predicate))
                    {
                        Add(t.Subject, t.Predicate, z, EntailmentRules.PrpTrp, [typing, t, Fact(t.Object, t.Predicate, z)]);
                    }

                    foreach(TermId w in SubjectsOf(t.Subject, t.Predicate))
                    {
                        Add(w, t.Predicate, t.Object, EntailmentRules.PrpTrp, [typing, Fact(w, t.Predicate, t.Subject), t]);
                    }
                }
            }

            foreach(TermId x in FrontierInstances(Terms.NamedIndividual))
            {
                if(InstancesOf.TryGetValue(Terms.ReflexiveProperty, out List<TermId>? reflexives))
                {
                    foreach(TermId p in reflexives)
                    {
                        Add(x, p, x, EntailmentRules.ReflexiveInstantiation, [Fact(p, Terms.Type, Terms.ReflexiveProperty), Fact(x, Terms.Type, Terms.NamedIndividual)]);
                    }
                }
            }

            OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.OverdeleteCharacteristicData, characteristicDataStart);
        }

        /// <summary>Marks the prp-spo2 deletions — a dirty axiom re-marks its full body; otherwise a deleted hop edge drives the per-hop walk.</summary>
        private void FireChainAxiomsOverdelete()
        {
            foreach((TermId p, TermId listHead) in Pairs(Terms.PropertyChainAxiom))
            {
                if(ListOf(listHead) is not List<TermId> chain || chain.Count == 0)
                {
                    continue;
                }

                if(FrontierListStructureDirty || FrontierAxiomTouched(p, listHead, Terms.PropertyChainAxiom))
                {
                    FireChainAxiom(p, listHead, chain);

                    continue;
                }

                EncodedTriple chainAxiom = Fact(p, Terms.PropertyChainAxiom, listHead);
                for(int i = 0; i < chain.Count; i++)
                {
                    List<(TermId Subject, TermId Object)> delta = FrontierPairs(chain[i]);
                    if(delta.Count == 0)
                    {
                        continue;
                    }

                    foreach((TermId u, TermId v) in delta)
                    {
                        FireChainHopDelta(p, chainAxiom, chain, i, u, v);
                    }
                }
            }
        }

        /// <summary>Marks the prp-adp materialisations of every touched <c>owl:AllDisjointProperties</c> node — the deletion-mode counterpart of <see cref="FireAllDisjointPropertiesDelta"/>.</summary>
        private void FireAllDisjointPropertiesOverdelete()
        {
            bool newTyping = FrontierHasInstances(Terms.AllDisjointProperties);
            bool memberEdgeGrew = false;
            if(!FrontierListStructureDirty && !newTyping)
            {
                AdpMemberProperties ??= BuildAdpMemberProperties();
                foreach(EncodedTriple t in FrontierTriples)
                {
                    if(AdpMemberProperties.Contains(t.Predicate))
                    {
                        memberEdgeGrew = true;

                        break;
                    }
                }
            }

            if(FrontierListStructureDirty || newTyping)
            {
                AdpMemberProperties = BuildAdpMemberProperties();
            }

            if(FrontierListStructureDirty || memberEdgeGrew)
            {
                if(InstancesOf.TryGetValue(Terms.AllDisjointProperties, out List<TermId>? nodes))
                {
                    foreach(TermId node in nodes)
                    {
                        FireAllDisjointPropertiesNode(node);
                    }
                }

                return;
            }

            foreach(TermId node in FrontierInstances(Terms.AllDisjointProperties))
            {
                FireAllDisjointPropertiesNode(node);
            }
        }

        /// <summary>Marks the prp-key deletions — any key trigger conservatively re-marks every key axiom's sameAs materialisation.</summary>
        private void FireKeyOverdelete()
        {
            bool hasKeyDirty = FrontierByPredicate.ContainsKey(Terms.HasKey);
            if(FrontierListStructureDirty || hasKeyDirty)
            {
                KeyProperties = BuildKeyProperties();
            }
            else
            {
                KeyProperties ??= BuildKeyProperties();
            }

            bool trigger = FrontierListStructureDirty || hasKeyDirty;
            if(!trigger)
            {
                foreach(EncodedTriple t in FrontierTriples)
                {
                    if(KeyProperties.Contains(t.Predicate))
                    {
                        trigger = true;

                        break;
                    }
                }
            }

            if(!trigger)
            {
                foreach((TermId c, TermId _) in Pairs(Terms.HasKey))
                {
                    if(FrontierInstances(c).Count > 0)
                    {
                        trigger = true;

                        break;
                    }
                }
            }

            if(trigger)
            {
                foreach((TermId c, TermId listHead) in Pairs(Terms.HasKey))
                {
                    FireKeyAxiom(c, listHead);
                }
            }
        }

        /// <summary>Marks the cls-* deletions over the round's frontier — intersections, unions, the complement symmetry, restrictions, and enumerations; nothing2 and the cls falsities mark nothing.</summary>
        private void FireClassesOverdelete()
        {
            FireIntersectionOverdelete();
            FireUnionOverdelete();

            //A deleted complementOf statement marks its symmetric mate.
            foreach((TermId c1, TermId c2) in FrontierPairs(Terms.ComplementOf))
            {
                Add(c2, Terms.ComplementOf, c1, EntailmentRules.ComplementOfSymmetry, [Fact(c1, Terms.ComplementOf, c2)]);
            }

            FireRestrictionsOverdelete();

            foreach((TermId c, TermId listHead) in Pairs(Terms.OneOf))
            {
                if(FrontierListStructureDirty || FrontierAxiomTouched(c, listHead, Terms.OneOf))
                {
                    FireOneOfAxiom(c, listHead);
                }
            }
        }

        /// <summary>Marks the cls-int1 / cls-int2 deletions — the deletion-mode counterpart of <see cref="FireIntersectionDelta"/>.</summary>
        private void FireIntersectionOverdelete()
        {
            foreach((TermId c, TermId listHead) in Pairs(Terms.IntersectionOf))
            {
                if(ListOf(listHead) is not List<TermId> members || members.Count == 0)
                {
                    continue;
                }

                if(FrontierListStructureDirty || FrontierAxiomTouched(c, listHead, Terms.IntersectionOf))
                {
                    FireIntersectionAxiom(c, listHead, members);
                }
            }

            List<(TermId Subject, TermId Object)> typingDelta = FrontierPairs(Terms.Type);
            if(typingDelta.Count == 0)
            {
                return;
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.IntersectionOf))
            {
                if(ListOf(listHead) is not List<TermId> members || members.Count == 0)
                {
                    continue;
                }

                if(FrontierListStructureDirty || FrontierAxiomTouched(c, listHead, Terms.IntersectionOf))
                {
                    continue;
                }

                EncodedTriple intersection = Fact(c, Terms.IntersectionOf, listHead);
                HashSet<TermId> memberSet = [.. members];
                foreach((TermId u, TermId m) in typingDelta)
                {
                    if(memberSet.Contains(m))
                    {
                        FireIntersectionCandidate(c, intersection, members, u);
                    }
                }
            }

            foreach((TermId u, TermId c) in typingDelta)
            {
                foreach(TermId listHead in ObjectsOf(c, Terms.IntersectionOf))
                {
                    if(ListOf(listHead) is not List<TermId> members)
                    {
                        continue;
                    }

                    EncodedTriple intersection = Fact(c, Terms.IntersectionOf, listHead);
                    foreach(TermId member in members)
                    {
                        Add(u, Terms.Type, member, EntailmentRules.ClsInt2, [intersection, Fact(u, Terms.Type, c)]);
                    }
                }
            }
        }

        /// <summary>Marks the cls-uni deletions — the deletion-mode counterpart of <see cref="FireUnionDelta"/>.</summary>
        private void FireUnionOverdelete()
        {
            foreach((TermId c, TermId listHead) in Pairs(Terms.UnionOf))
            {
                if(ListOf(listHead) is not List<TermId> members)
                {
                    continue;
                }

                if(FrontierListStructureDirty || FrontierAxiomTouched(c, listHead, Terms.UnionOf))
                {
                    FireUnionAxiom(c, listHead, members);
                }
            }

            List<(TermId Subject, TermId Object)> typingDelta = FrontierPairs(Terms.Type);
            if(typingDelta.Count == 0)
            {
                return;
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.UnionOf))
            {
                if(ListOf(listHead) is not List<TermId> members)
                {
                    continue;
                }

                if(FrontierListStructureDirty || FrontierAxiomTouched(c, listHead, Terms.UnionOf))
                {
                    continue;
                }

                EncodedTriple union = Fact(c, Terms.UnionOf, listHead);
                HashSet<TermId> memberSet = [.. members];
                foreach((TermId u, TermId m) in typingDelta)
                {
                    if(memberSet.Contains(m))
                    {
                        Add(u, Terms.Type, c, EntailmentRules.ClsUni, [union, Fact(u, Terms.Type, m)]);
                    }
                }
            }
        }

        /// <summary>Marks the restriction deletions over the round's frontier — deleted data edges, deleted objects of a one-bounded pair, deleted instance typings, and deleted filler typings; the structure/choice edits ride the owner re-process.</summary>
        private void FireRestrictionsOverdelete()
        {
            HashSet<TermId> noneDirty = [];

            foreach(EncodedTriple t in FrontierTriples)
            {
                foreach(TermId x in SubjectsOf(t.Predicate, Terms.OnProperty))
                {
                    FireRestrictionEdgeVariants(x, t.Predicate, t.Subject, t.Object);
                }
            }

            foreach(KeyValuePair<(TermId Subject, TermId Predicate), List<TermId>> entry in FrontierBySubjectPredicate)
            {
                (TermId u, TermId p) = entry.Key;
                foreach(TermId x in SubjectsOf(p, Terms.OnProperty))
                {
                    if(HasType(u, x))
                    {
                        FireMaxPairOverdelete(x, p, u, entry.Value);
                    }
                }
            }

            foreach((TermId u, TermId x) in FrontierPairs(Terms.Type))
            {
                foreach(TermId p in ObjectsOf(x, Terms.OnProperty))
                {
                    FireRestrictionTypingVariants(x, p, u);
                }
            }

            foreach((TermId v, TermId filler) in FrontierPairs(Terms.Type))
            {
                FireRestrictionFillerVariants(v, filler, noneDirty);
            }
        }

        /// <summary>Marks the one-bounded max deletions for restriction <paramref name="x"/> on <paramref name="p"/> at instance <paramref name="u"/> — each deleted object equates with the full current list in both orientations, with the qualified filter applied for cls-maxqc4.</summary>
        /// <param name="x">The restriction node on <paramref name="p"/>.</param>
        /// <param name="p">The restricted property.</param>
        /// <param name="u">The instance whose objects were deleted.</param>
        /// <param name="deleted">The deleted objects of the (instance, property) pair.</param>
        private void FireMaxPairOverdelete(TermId x, TermId p, TermId u, List<TermId> deleted)
        {
            long maxPairsStart = OwlRlMaintenanceInstrumentation.Begin();
            EncodedTriple onProperty = Fact(x, Terms.OnProperty, p);
            List<TermId> objects = ObjectsOf(u, p);

            foreach(TermId bound in ObjectsOf(x, Terms.MaxCardinality))
            {
                if(!Terms.OneBounds.Contains(bound))
                {
                    continue;
                }

                EncodedTriple maxCardinality = Fact(x, Terms.MaxCardinality, bound);
                foreach(TermId d in deleted)
                {
                    foreach(TermId other in objects)
                    {
                        if(other != d)
                        {
                            Add(d, Terms.SameAs, other, EntailmentRules.ClsMaxc2, [onProperty, maxCardinality, Fact(u, Terms.Type, x), Fact(u, p, d), Fact(u, p, other)]);
                            Add(other, Terms.SameAs, d, EntailmentRules.ClsMaxc2, [onProperty, maxCardinality, Fact(u, Terms.Type, x), Fact(u, p, other), Fact(u, p, d)]);
                        }
                    }
                }
            }

            //The rule requires the owl:onClass triple; an absent onClass
            //matches nothing.
            foreach(TermId qualifiedBound in ObjectsOf(x, Terms.MaxQualifiedCardinality))
            {
                if(!Terms.OneBounds.Contains(qualifiedBound))
                {
                    continue;
                }

                EncodedTriple maxQualified = Fact(x, Terms.MaxQualifiedCardinality, qualifiedBound);
                foreach(TermId filler in ObjectsOf(x, Terms.OnClass))
                {
                    foreach(TermId d in deleted)
                    {
                        if(filler != Terms.Thing && !HasType(d, filler))
                        {
                            continue;
                        }

                        foreach(TermId other in objects)
                        {
                            if(other == d || (filler != Terms.Thing && !HasType(other, filler)))
                            {
                                continue;
                            }

                            Add(d, Terms.SameAs, other, EntailmentRules.ClsMaxqc4, [onProperty, maxQualified, Fact(u, Terms.Type, x), Fact(u, p, d), Fact(u, p, other)]);
                            Add(other, Terms.SameAs, d, EntailmentRules.ClsMaxqc4, [onProperty, maxQualified, Fact(u, Terms.Type, x), Fact(u, p, other), Fact(u, p, d)]);
                        }
                    }
                }
            }

            OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.OverdeleteMaxPairs, maxPairsStart);
        }

        /// <summary>Marks the cax-* deletions over the round's frontier — subclass, equivalence, disjointness symmetry and the all-disjoint materialisation; the cax falsities mark nothing.</summary>
        private void FireClassAxiomsOverdelete()
        {
            foreach((TermId c1, TermId c2) in FrontierPairs(Terms.SubClassOf))
            {
                EncodedTriple subClass = Fact(c1, Terms.SubClassOf, c2);
                if(InstancesOf.TryGetValue(c1, out List<TermId>? instances))
                {
                    foreach(TermId x in instances)
                    {
                        Add(x, Terms.Type, c2, EntailmentRules.CaxSco, [subClass, Fact(x, Terms.Type, c1)]);
                    }
                }
            }

            foreach((TermId x, TermId c1) in FrontierPairs(Terms.Type))
            {
                foreach(TermId c2 in ObjectsOf(c1, Terms.SubClassOf))
                {
                    Add(x, Terms.Type, c2, EntailmentRules.CaxSco, [Fact(c1, Terms.SubClassOf, c2), Fact(x, Terms.Type, c1)]);
                }
            }

            foreach((TermId c1, TermId c2) in FrontierPairs(Terms.EquivalentClass))
            {
                EncodedTriple equivalent = Fact(c1, Terms.EquivalentClass, c2);
                if(InstancesOf.TryGetValue(c1, out List<TermId>? forward))
                {
                    foreach(TermId x in forward)
                    {
                        Add(x, Terms.Type, c2, EntailmentRules.CaxEqc1, [equivalent, Fact(x, Terms.Type, c1)]);
                    }
                }

                if(InstancesOf.TryGetValue(c2, out List<TermId>? backward))
                {
                    foreach(TermId x in backward)
                    {
                        Add(x, Terms.Type, c1, EntailmentRules.CaxEqc2, [equivalent, Fact(x, Terms.Type, c2)]);
                    }
                }
            }

            foreach((TermId x, TermId c) in FrontierPairs(Terms.Type))
            {
                foreach(TermId c2 in ObjectsOf(c, Terms.EquivalentClass))
                {
                    Add(x, Terms.Type, c2, EntailmentRules.CaxEqc1, [Fact(c, Terms.EquivalentClass, c2), Fact(x, Terms.Type, c)]);
                }

                foreach(TermId c1 in SubjectsOf(c, Terms.EquivalentClass))
                {
                    Add(x, Terms.Type, c1, EntailmentRules.CaxEqc2, [Fact(c1, Terms.EquivalentClass, c), Fact(x, Terms.Type, c)]);
                }
            }

            foreach((TermId c1, TermId c2) in FrontierPairs(Terms.DisjointWith))
            {
                EncodedTriple disjoint = Fact(c1, Terms.DisjointWith, c2);
                Add(c2, Terms.DisjointWith, c1, EntailmentRules.CaxDw, [disjoint]);
                if(InstancesOf.TryGetValue(c1, out List<TermId>? instances))
                {
                    foreach(TermId x in instances)
                    {
                        if(HasType(x, c2))
                        {
                            Inconsistent(EntailmentRules.CaxDw, [disjoint, Fact(x, Terms.Type, c1), Fact(x, Terms.Type, c2)]);

                            return;
                        }
                    }
                }
            }

            foreach((TermId x, TermId c) in FrontierPairs(Terms.Type))
            {
                foreach(TermId c2 in ObjectsOf(c, Terms.DisjointWith))
                {
                    if(HasType(x, c2))
                    {
                        Inconsistent(EntailmentRules.CaxDw, [Fact(c, Terms.DisjointWith, c2), Fact(x, Terms.Type, c), Fact(x, Terms.Type, c2)]);

                        return;
                    }
                }

                foreach(TermId c1 in SubjectsOf(c, Terms.DisjointWith))
                {
                    if(HasType(x, c1))
                    {
                        Inconsistent(EntailmentRules.CaxDw, [Fact(c1, Terms.DisjointWith, c), Fact(x, Terms.Type, c1), Fact(x, Terms.Type, c)]);

                        return;
                    }
                }
            }

            FireAllDisjointClassesOverdelete();
        }

        /// <summary>Marks the cax-adc materialisations of every touched <c>owl:AllDisjointClasses</c> node — the deletion-mode counterpart of <see cref="FireAllDisjointClassesDelta"/>.</summary>
        private void FireAllDisjointClassesOverdelete()
        {
            bool newTyping = FrontierHasInstances(Terms.AllDisjointClasses);
            bool memberTypingGrew = false;
            if(!FrontierListStructureDirty && !newTyping)
            {
                AdcMemberClasses ??= BuildAdcMemberClasses();
                foreach((TermId _, TermId c) in FrontierPairs(Terms.Type))
                {
                    if(AdcMemberClasses.Contains(c))
                    {
                        memberTypingGrew = true;

                        break;
                    }
                }
            }

            if(FrontierListStructureDirty || newTyping)
            {
                AdcMemberClasses = BuildAdcMemberClasses();
            }

            if(FrontierListStructureDirty || memberTypingGrew)
            {
                if(InstancesOf.TryGetValue(Terms.AllDisjointClasses, out List<TermId>? nodes))
                {
                    foreach(TermId node in nodes)
                    {
                        FireAllDisjointClassesNode(node);
                    }
                }

                return;
            }

            foreach(TermId node in FrontierInstances(Terms.AllDisjointClasses))
            {
                FireAllDisjointClassesNode(node);
            }
        }

        /// <summary>Marks the scm-* deletions over the round's frontier — the deletion-mode counterpart of <see cref="FireSchemaDelta"/>.</summary>
        private void FireSchemaOverdelete()
        {
            foreach(TermId c in FrontierInstances(Terms.ClassTerm))
            {
                EncodedTriple declaration = Fact(c, Terms.Type, Terms.ClassTerm);
                Add(c, Terms.SubClassOf, c, EntailmentRules.ScmCls, [declaration]);
                Add(c, Terms.EquivalentClass, c, EntailmentRules.ScmCls, [declaration]);
                Add(c, Terms.SubClassOf, Terms.Thing, EntailmentRules.ScmCls, [declaration]);
                Add(Terms.Nothing, Terms.SubClassOf, c, EntailmentRules.ScmCls, [declaration]);
            }

            //scm-op / scm-dp over the frontier's deleted property declarations.
            FireSelfSubsumption(FrontierInstances(Terms.ObjectPropertyTerm), Terms.ObjectPropertyTerm, EntailmentRules.ScmOp);
            FireSelfSubsumption(FrontierInstances(Terms.DatatypePropertyTerm), Terms.DatatypePropertyTerm, EntailmentRules.ScmDp);

            foreach((TermId c1, TermId c2) in FrontierPairs(Terms.SubClassOf))
            {
                EncodedTriple subClass = Fact(c1, Terms.SubClassOf, c2);
                foreach(TermId c3 in ObjectsOf(c2, Terms.SubClassOf))
                {
                    Add(c1, Terms.SubClassOf, c3, EntailmentRules.ScmSco, [subClass, Fact(c2, Terms.SubClassOf, c3)]);
                }

                foreach(TermId c0 in SubjectsOf(c1, Terms.SubClassOf))
                {
                    Add(c0, Terms.SubClassOf, c2, EntailmentRules.ScmSco, [Fact(c0, Terms.SubClassOf, c1), subClass]);
                }

                if(ObjectsOf(c2, Terms.SubClassOf).Contains(c1))
                {
                    Add(c1, Terms.EquivalentClass, c2, EntailmentRules.ScmEqc2, [subClass, Fact(c2, Terms.SubClassOf, c1)]);
                    Add(c2, Terms.EquivalentClass, c1, EntailmentRules.ScmEqc2, [Fact(c2, Terms.SubClassOf, c1), subClass]);
                }
            }

            foreach((TermId c1, TermId c2) in FrontierPairs(Terms.EquivalentClass))
            {
                EncodedTriple equivalent = Fact(c1, Terms.EquivalentClass, c2);
                Add(c2, Terms.EquivalentClass, c1, EntailmentRules.ScmEqc1, [equivalent]);
                Add(c1, Terms.SubClassOf, c2, EntailmentRules.ScmEqc1, [equivalent]);
                Add(c2, Terms.SubClassOf, c1, EntailmentRules.ScmEqc1, [equivalent]);
            }

            foreach((TermId p1, TermId p2) in FrontierPairs(Terms.SubPropertyOf))
            {
                EncodedTriple subProperty = Fact(p1, Terms.SubPropertyOf, p2);
                foreach(TermId p3 in ObjectsOf(p2, Terms.SubPropertyOf))
                {
                    Add(p1, Terms.SubPropertyOf, p3, EntailmentRules.ScmSpo, [subProperty, Fact(p2, Terms.SubPropertyOf, p3)]);
                }

                foreach(TermId p0 in SubjectsOf(p1, Terms.SubPropertyOf))
                {
                    Add(p0, Terms.SubPropertyOf, p2, EntailmentRules.ScmSpo, [Fact(p0, Terms.SubPropertyOf, p1), subProperty]);
                }

                if(ObjectsOf(p2, Terms.SubPropertyOf).Contains(p1))
                {
                    Add(p1, Terms.EquivalentProperty, p2, EntailmentRules.ScmEqp2, [subProperty, Fact(p2, Terms.SubPropertyOf, p1)]);
                    Add(p2, Terms.EquivalentProperty, p1, EntailmentRules.ScmEqp2, [Fact(p2, Terms.SubPropertyOf, p1), subProperty]);
                }
            }

            foreach((TermId p1, TermId p2) in FrontierPairs(Terms.EquivalentProperty))
            {
                EncodedTriple equivalent = Fact(p1, Terms.EquivalentProperty, p2);
                Add(p2, Terms.EquivalentProperty, p1, EntailmentRules.ScmEqp1, [equivalent]);
                Add(p1, Terms.SubPropertyOf, p2, EntailmentRules.ScmEqp1, [equivalent]);
                Add(p2, Terms.SubPropertyOf, p1, EntailmentRules.ScmEqp1, [equivalent]);
            }

            foreach((TermId p, TermId c1) in FrontierPairs(Terms.Domain))
            {
                EncodedTriple domain = Fact(p, Terms.Domain, c1);
                foreach(TermId c2 in ObjectsOf(c1, Terms.SubClassOf))
                {
                    Add(p, Terms.Domain, c2, EntailmentRules.ScmDom1, [domain, Fact(c1, Terms.SubClassOf, c2)]);
                }
            }

            foreach((TermId c1, TermId c2) in FrontierPairs(Terms.SubClassOf))
            {
                foreach(TermId p in SubjectsOf(c1, Terms.Domain))
                {
                    Add(p, Terms.Domain, c2, EntailmentRules.ScmDom1, [Fact(p, Terms.Domain, c1), Fact(c1, Terms.SubClassOf, c2)]);
                }
            }

            foreach((TermId p1, TermId p2) in FrontierPairs(Terms.SubPropertyOf))
            {
                EncodedTriple subProperty = Fact(p1, Terms.SubPropertyOf, p2);
                foreach(TermId c in ObjectsOf(p2, Terms.Domain))
                {
                    Add(p1, Terms.Domain, c, EntailmentRules.ScmDom2, [subProperty, Fact(p2, Terms.Domain, c)]);
                }

                foreach(TermId c in ObjectsOf(p2, Terms.Range))
                {
                    Add(p1, Terms.Range, c, EntailmentRules.ScmRng2, [subProperty, Fact(p2, Terms.Range, c)]);
                }
            }

            foreach((TermId p2, TermId c) in FrontierPairs(Terms.Domain))
            {
                foreach(TermId p1 in SubjectsOf(p2, Terms.SubPropertyOf))
                {
                    Add(p1, Terms.Domain, c, EntailmentRules.ScmDom2, [Fact(p1, Terms.SubPropertyOf, p2), Fact(p2, Terms.Domain, c)]);
                }
            }

            foreach((TermId p2, TermId c) in FrontierPairs(Terms.Range))
            {
                foreach(TermId p1 in SubjectsOf(p2, Terms.SubPropertyOf))
                {
                    Add(p1, Terms.Range, c, EntailmentRules.ScmRng2, [Fact(p1, Terms.SubPropertyOf, p2), Fact(p2, Terms.Range, c)]);
                }
            }

            foreach((TermId p, TermId c1) in FrontierPairs(Terms.Range))
            {
                EncodedTriple range = Fact(p, Terms.Range, c1);
                foreach(TermId c2 in ObjectsOf(c1, Terms.SubClassOf))
                {
                    Add(p, Terms.Range, c2, EntailmentRules.ScmRng1, [range, Fact(c1, Terms.SubClassOf, c2)]);
                }
            }

            foreach((TermId c1, TermId c2) in FrontierPairs(Terms.SubClassOf))
            {
                foreach(TermId p in SubjectsOf(c1, Terms.Range))
                {
                    Add(p, Terms.Range, c2, EntailmentRules.ScmRng1, [Fact(p, Terms.Range, c1), Fact(c1, Terms.SubClassOf, c2)]);
                }
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.IntersectionOf))
            {
                if(FrontierListStructureDirty || FrontierAxiomTouched(c, listHead, Terms.IntersectionOf))
                {
                    FireSchemaIntersectionAxiom(c, listHead);
                }
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.UnionOf))
            {
                if(FrontierListStructureDirty || FrontierAxiomTouched(c, listHead, Terms.UnionOf))
                {
                    FireSchemaUnionAxiom(c, listHead);
                }
            }

            //The restriction comparisons re-mark in full when the frontier
            //holds any of their premise kinds; the rederive pass restores
            //the still-derivable conclusions.
            if(FrontierByPredicate.ContainsKey(Terms.OnProperty)
                || FrontierByPredicate.ContainsKey(Terms.SomeValuesFrom)
                || FrontierByPredicate.ContainsKey(Terms.AllValuesFrom)
                || FrontierByPredicate.ContainsKey(Terms.HasValue)
                || FrontierByPredicate.ContainsKey(Terms.SubClassOf)
                || FrontierByPredicate.ContainsKey(Terms.SubPropertyOf))
            {
                FireRestrictionComparisons();
            }

            //The inverse-characteristic transfer over the round's frontier:
            //a deleted inverseOf pair marks both ends' transferred
            //characteristics, and a deleted characteristic typing marks its
            //partners' — the typing body no-ops on any other typing.
            foreach((TermId p1, TermId p2) in FrontierPairs(Terms.InverseOf))
            {
                FireInverseCharacteristicPair(p1, p2);
            }

            foreach((TermId p, TermId characteristic) in FrontierPairs(Terms.Type))
            {
                FireInverseCharacteristicTyping(p, characteristic);
            }

            //The singleton-enumeration characteristics: deleted range or
            //domain edges mark precisely; a deleted enumeration or list cell
            //re-marks the rule in full.
            foreach((TermId p, TermId c) in FrontierPairs(Terms.Range))
            {
                FireSingletonEnumerationEdge(p, c, Terms.Range, Terms.FunctionalProperty);
            }

            foreach((TermId p, TermId c) in FrontierPairs(Terms.Domain))
            {
                FireSingletonEnumerationEdge(p, c, Terms.Domain, Terms.InverseFunctionalProperty);
            }

            if(FrontierListStructureDirty || FrontierByPredicate.ContainsKey(Terms.OneOf))
            {
                FireSingletonEnumerationCharacteristics();
            }

            //The member-subset comparisons re-mark in full when the
            //frontier holds an enumeration, a union, or a list cell.
            if(FrontierListStructureDirty || FrontierByPredicate.ContainsKey(Terms.OneOf) || FrontierByPredicate.ContainsKey(Terms.UnionOf))
            {
                FireEnumerationComparisons();
            }
        }

        /// <summary>Collects the choice and list owners a set of edits touches into <see cref="RestrictionOwners"/>, <see cref="NpaOwners"/>, and <see cref="AllListConsumersDirty"/>.</summary>
        /// <param name="removals">The op's net removals.</param>
        /// <param name="additions">The op's net additions.</param>
        private void CollectOwners(IReadOnlyCollection<EncodedTriple> removals, IReadOnlyCollection<EncodedTriple> additions)
        {
            RestrictionOwners.Clear();
            NpaOwners.Clear();
            AllListConsumersDirty = false;
            CollectOwnersFrom(removals);
            CollectOwnersFrom(additions);
        }

        /// <summary>Collects the choice and list owners of one edit set.</summary>
        /// <param name="triples">The edited triples.</param>
        private void CollectOwnersFrom(IReadOnlyCollection<EncodedTriple> triples)
        {
            foreach(EncodedTriple t in triples)
            {
                TermId predicate = t.Predicate;
                if(predicate == Terms.OnProperty || predicate == Terms.SomeValuesFrom || predicate == Terms.AllValuesFrom
                    || predicate == Terms.HasValue || predicate == Terms.MaxCardinality || predicate == Terms.MaxQualifiedCardinality
                    || predicate == Terms.MinCardinality || predicate == Terms.OnClass)
                {
                    RestrictionOwners.Add(t.Subject);
                }
                else if(predicate == Terms.SourceIndividual || predicate == Terms.AssertionProperty
                    || predicate == Terms.TargetIndividual || predicate == Terms.TargetValue)
                {
                    NpaOwners.Add(t.Subject);
                }
                else if(predicate == Terms.First || predicate == Terms.Rest || predicate == Terms.Members || predicate == Terms.DistinctMembers)
                {
                    AllListConsumersDirty = true;
                }
            }
        }

        /// <summary>Marks the old-choice conclusions of the collected owners over the intact indexes — the restriction bodies, and every list-consuming construct when list structure was edited.</summary>
        private void MarkOwnerConclusions()
        {
            foreach(TermId x in RestrictionOwners)
            {
                foreach(TermId p in ObjectsOf(x, Terms.OnProperty))
                {
                    FireRestrictionBody(x, p);
                }
            }

            if(AllListConsumersDirty)
            {
                MarkListConsumers();
            }
        }

        /// <summary>Marks the materialisations of every list-consuming construct over the intact indexes — the old-list conclusions a list-structure edit tears down. The falsity-only <c>owl:AllDifferent</c> and negative-property-assertion re-checks ride the live re-fire.</summary>
        private void MarkListConsumers()
        {
            foreach((TermId p, TermId listHead) in Pairs(Terms.PropertyChainAxiom))
            {
                if(ListOf(listHead) is List<TermId> chain && chain.Count > 0)
                {
                    FireChainAxiom(p, listHead, chain);
                }
            }

            if(InstancesOf.TryGetValue(Terms.AllDisjointProperties, out List<TermId>? adpNodes))
            {
                foreach(TermId node in adpNodes)
                {
                    FireAllDisjointPropertiesNode(node);
                }
            }

            if(InstancesOf.TryGetValue(Terms.AllDisjointClasses, out List<TermId>? adcNodes))
            {
                foreach(TermId node in adcNodes)
                {
                    FireAllDisjointClassesNode(node);
                }
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.HasKey))
            {
                FireKeyAxiom(c, listHead);
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.IntersectionOf))
            {
                if(ListOf(listHead) is List<TermId> members && members.Count > 0)
                {
                    FireIntersectionAxiom(c, listHead, members);
                }

                FireSchemaIntersectionAxiom(c, listHead);
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.UnionOf))
            {
                if(ListOf(listHead) is List<TermId> members)
                {
                    FireUnionAxiom(c, listHead, members);
                }

                FireSchemaUnionAxiom(c, listHead);
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.OneOf))
            {
                FireOneOfAxiom(c, listHead);
            }

            //The residue completions read lists too: the singleton-
            //enumeration characteristics walk the enumeration's cells and
            //the member-subset comparisons read whole member sets, so a
            //list edit marks their old-choice conclusions here as well.
            FireSingletonEnumerationCharacteristics();
            FireEnumerationComparisons();
        }

        /// <summary>Re-fires the collected owners' bodies over the post-removal, post-admission state with the derive sink and live falsity — the new-choice conclusions feed the insert rounds. Returns whether a falsity fired.</summary>
        /// <returns><c>true</c> when an owner re-fire made the closure inconsistent.</returns>
        private bool ReFireOwners()
        {
            foreach(TermId x in RestrictionOwners)
            {
                foreach(TermId p in ObjectsOf(x, Terms.OnProperty))
                {
                    StatChoiceOwnerReFires++;
                    if(FireRestrictionBody(x, p))
                    {
                        return true;
                    }
                }
            }

            foreach(TermId node in NpaOwners)
            {
                StatChoiceOwnerReFires++;
                if(FireNegativePropertyAssertionNode(node))
                {
                    return true;
                }
            }

            return AllListConsumersDirty && ReFireListConsumers();
        }

        /// <summary>Re-fires every list-consuming construct's body over the current state with the derive sink and live falsity. Returns whether a falsity fired.</summary>
        /// <returns><c>true</c> when a list-consuming re-fire made the closure inconsistent.</returns>
        private bool ReFireListConsumers()
        {
            //Every recorded malformed shape is a list-chain refusal, and this
            //re-walk reads every list-consuming construct over the current
            //state — so the record set resets here: a still-broken chain
            //re-records during the walk and a repaired one drops, keeping the
            //maintained result's channel a statement about the current graph.
            MalformedShapes.Clear();

            foreach((TermId p, TermId listHead) in Pairs(Terms.PropertyChainAxiom))
            {
                if(ListOf(listHead) is List<TermId> chain && chain.Count > 0)
                {
                    StatChoiceOwnerReFires++;
                    FireChainAxiom(p, listHead, chain);
                }
            }

            if(InstancesOf.TryGetValue(Terms.AllDifferent, out List<TermId>? diffNodes))
            {
                foreach(TermId node in diffNodes)
                {
                    StatChoiceOwnerReFires++;
                    if(CheckAllDifferentNode(node))
                    {
                        return true;
                    }
                }
            }

            if(InstancesOf.TryGetValue(Terms.AllDisjointProperties, out List<TermId>? adpNodes))
            {
                foreach(TermId node in adpNodes)
                {
                    StatChoiceOwnerReFires++;
                    if(FireAllDisjointPropertiesNode(node))
                    {
                        return true;
                    }
                }
            }

            if(InstancesOf.TryGetValue(Terms.AllDisjointClasses, out List<TermId>? adcNodes))
            {
                foreach(TermId node in adcNodes)
                {
                    StatChoiceOwnerReFires++;
                    if(FireAllDisjointClassesNode(node))
                    {
                        return true;
                    }
                }
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.HasKey))
            {
                StatChoiceOwnerReFires++;
                FireKeyAxiom(c, listHead);
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.IntersectionOf))
            {
                if(ListOf(listHead) is List<TermId> members && members.Count > 0)
                {
                    StatChoiceOwnerReFires++;
                    FireIntersectionAxiom(c, listHead, members);
                }

                FireSchemaIntersectionAxiom(c, listHead);
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.UnionOf))
            {
                if(ListOf(listHead) is List<TermId> members)
                {
                    StatChoiceOwnerReFires++;
                    FireUnionAxiom(c, listHead, members);
                }

                FireSchemaUnionAxiom(c, listHead);
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.OneOf))
            {
                StatChoiceOwnerReFires++;
                FireOneOfAxiom(c, listHead);
            }

            //The residue completions' new-choice conclusions: a list edit
            //may have flipped a canonical cell, so both list-reading rules
            //re-fire over the post-edit state.
            StatChoiceOwnerReFires++;
            FireSingletonEnumerationCharacteristics();
            StatChoiceOwnerReFires++;
            FireEnumerationComparisons();

            return false;
        }

        /// <summary>Runs the overdelete fixpoint from the round's initial frontier — round 0 also marks the owner old-choice conclusions over the intact indexes.</summary>
        /// <param name="round0">The initial frontier: the op's non-seeded removals.</param>
        /// <param name="cancellationToken">A token that aborts the pass between rounds.</param>
        private void RunOverdeleteFixpoint(List<EncodedTriple> round0, CancellationToken cancellationToken)
        {
            OverdeleteSink = true;

            NextFrontier = [];
            long ownerMarkingStart = OwlRlMaintenanceInstrumentation.Begin();
            MarkOwnerConclusions();
            OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.OwnerMarking, ownerMarkingStart);
            round0.AddRange(NextFrontier);

            List<EncodedTriple> frontier = round0;
            while(frontier.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NextFrontier = [];

                long groupingStart = OwlRlMaintenanceInstrumentation.Begin();
                RebuildFrontierGroupings(frontier);
                OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.OverdeleteGrouping, groupingStart);

                long frontierOwnersStart = OwlRlMaintenanceInstrumentation.Begin();
                CollectFrontierOwners();
                OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.OwnerMarking, frontierOwnersStart);

                long equalityStart = OwlRlMaintenanceInstrumentation.Begin();
                FireEqualityOverdelete();
                OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.OverdeleteEquality, equalityStart);

                long propertiesStart = OwlRlMaintenanceInstrumentation.Begin();
                FirePropertiesOverdelete();
                OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.OverdeleteProperties, propertiesStart);

                long classesStart = OwlRlMaintenanceInstrumentation.Begin();
                FireClassesOverdelete();
                OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.OverdeleteClasses, classesStart);

                long classAxiomsStart = OwlRlMaintenanceInstrumentation.Begin();
                FireClassAxiomsOverdelete();
                OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.OverdeleteClassAxioms, classAxiomsStart);

                long schemaStart = OwlRlMaintenanceInstrumentation.Begin();
                FireSchemaOverdelete();
                OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.OverdeleteSchema, schemaStart);

                StatDeletionRounds++;
                frontier = NextFrontier;
            }

            OverdeleteSink = false;
        }

        /// <summary>Collects the choice and list owners the round's frontier touches — a structural or read triple deleted by propagation re-processes its construct exactly like an op-touched one. A newly seen restriction owner's old-choice body is marked immediately (the indexes stay intact through the whole fixpoint, so the old choice is still visible in any round), a touched negative-property-assertion node joins the live re-check set, and frontier list dirt joins the list-consumer re-fire — the list-consumer MARKING already runs per round in the overdelete families.</summary>
        private void CollectFrontierOwners()
        {
            foreach(EncodedTriple t in FrontierTriples)
            {
                TermId predicate = t.Predicate;
                if(predicate == Terms.OnProperty || predicate == Terms.SomeValuesFrom || predicate == Terms.AllValuesFrom
                    || predicate == Terms.HasValue || predicate == Terms.MaxCardinality || predicate == Terms.MaxQualifiedCardinality
                    || predicate == Terms.MinCardinality || predicate == Terms.OnClass)
                {
                    if(RestrictionOwners.Add(t.Subject))
                    {
                        foreach(TermId p in ObjectsOf(t.Subject, Terms.OnProperty))
                        {
                            FireRestrictionBody(t.Subject, p);
                        }
                    }
                }
                else if(predicate == Terms.SourceIndividual || predicate == Terms.AssertionProperty
                    || predicate == Terms.TargetIndividual || predicate == Terms.TargetValue)
                {
                    NpaOwners.Add(t.Subject);
                }
            }

            if(FrontierListStructureDirty)
            {
                AllListConsumersDirty = true;
            }
        }

        /// <summary>Physically removes every still-present fact in <see cref="DeletionSet"/> from the accumulated set, the derived set, and the indexes — the batched structural inverse of <see cref="IndexTriple"/> over the whole marked set at once.</summary>
        /// <remarks>
        /// <para>
        /// Husk contract: a list emptied by removal, and its dictionary key,
        /// stay in place — neither the creation-gated distinct-predicate lists
        /// (<see cref="PredicatesOfSubject"/> / <see cref="PredicatesOfObject"/>)
        /// nor the emptied index husks are pruned, mirroring
        /// <see cref="IndexTriple"/>'s add-only keying (which never re-keys or
        /// removes). The field coverage matches the per-fact unindex exactly:
        /// non-type triples never touch <see cref="TypesOf"/> or
        /// <see cref="InstancesOf"/>.
        /// </para>
        /// <para>
        /// Uniqueness: dropping every grouped occurrence removes exactly what a
        /// per-fact swap-remove would remove one at a time, sound because an
        /// index list never holds a duplicate entry for one triple — every
        /// indexing goes through <see cref="IndexTriple"/> under a first-time
        /// <see cref="All"/> admission, so a distinct triple contributes one
        /// entry to each list it participates in.
        /// </para>
        /// <para>
        /// Cost: O(total length of the touched index lists) — one compaction
        /// sweep per touched key.
        /// </para>
        /// </remarks>
        private void RemoveMarkedFactsFromIndexes()
        {
            if(DeletionSet.Count == 0)
            {
                return;
            }

            Dictionary<TermId, HashSet<(TermId Subject, TermId Object)>> predicatePairs = [];
            Dictionary<(TermId Subject, TermId Predicate), HashSet<TermId>> subjectPredicateObjects = [];
            Dictionary<(TermId Object, TermId Predicate), HashSet<TermId>> objectPredicateSubjects = [];
            Dictionary<TermId, HashSet<TermId>> typeInstances = [];

            foreach(EncodedTriple d in DeletionSet)
            {
                if(!All.Remove(d))
                {
                    continue;
                }

                RecordAllLeft(d);

                if(Derived.Remove(d))
                {
                    RecordDerivedLeft(d);
                }

                GroupRemoval(predicatePairs, d.Predicate, (d.Subject, d.Object));
                GroupRemoval(subjectPredicateObjects, (d.Subject, d.Predicate), d.Object);
                GroupRemoval(objectPredicateSubjects, (d.Object, d.Predicate), d.Subject);

                if(d.Predicate == Terms.Type)
                {
                    if(TypesOf.TryGetValue(d.Subject, out HashSet<TermId>? types))
                    {
                        types.Remove(d.Object);
                    }

                    GroupRemoval(typeInstances, d.Object, d.Subject);
                }
            }

            foreach(KeyValuePair<TermId, HashSet<(TermId Subject, TermId Object)>> entry in predicatePairs)
            {
                if(ByPredicate.TryGetValue(entry.Key, out List<(TermId Subject, TermId Object)>? pairs))
                {
                    CompactList(pairs, entry.Value);
                }
            }

            foreach(KeyValuePair<(TermId Subject, TermId Predicate), HashSet<TermId>> entry in subjectPredicateObjects)
            {
                if(BySubjectPredicate.TryGetValue(entry.Key, out List<TermId>? objects))
                {
                    CompactList(objects, entry.Value);
                }
            }

            foreach(KeyValuePair<(TermId Object, TermId Predicate), HashSet<TermId>> entry in objectPredicateSubjects)
            {
                if(ByObjectPredicate.TryGetValue(entry.Key, out List<TermId>? subjects))
                {
                    CompactList(subjects, entry.Value);
                }
            }

            foreach(KeyValuePair<TermId, HashSet<TermId>> entry in typeInstances)
            {
                if(InstancesOf.TryGetValue(entry.Key, out List<TermId>? instances))
                {
                    CompactList(instances, entry.Value);
                }
            }
        }

        /// <summary>Adds one entry to the removal group under <paramref name="key"/>, creating the group's set on first use.</summary>
        /// <typeparam name="TKey">The group key type.</typeparam>
        /// <typeparam name="TValue">The grouped entry type.</typeparam>
        /// <param name="groups">The removal groups being accumulated.</param>
        /// <param name="key">The key whose group <paramref name="value"/> joins.</param>
        /// <param name="value">The entry to drop from that key's index list.</param>
        private static void GroupRemoval<TKey, TValue>(Dictionary<TKey, HashSet<TValue>> groups, TKey key, TValue value) where TKey : notnull
        {
            if(!groups.TryGetValue(key, out HashSet<TValue>? bucket))
            {
                bucket = [];
                groups[key] = bucket;
            }

            bucket.Add(value);
        }

        /// <summary>Compacts an index list in place, dropping every entry present in <paramref name="toRemove"/> with a single write-index sweep, then trimming the tail. An emptied list is left in place (the husk contract); relative order of the survivors is preserved but carries no rule semantics.</summary>
        /// <typeparam name="T">The list's entry type.</typeparam>
        /// <param name="list">The index list to compact.</param>
        /// <param name="toRemove">The entries to drop.</param>
        private static void CompactList<T>(List<T> list, HashSet<T> toRemove)
        {
            int write = 0;
            for(int read = 0; read < list.Count; read++)
            {
                if(!toRemove.Contains(list[read]))
                {
                    list[write] = list[read];
                    write++;
                }
            }

            list.RemoveRange(write, list.Count - write);
        }

        /// <summary>Admits one triple into the accumulated set, the indexes, and this round's merge bookkeeping — the shared path base additions and rederived restorations enter through. Admission is first-time-only: an already-present triple indexes nothing, enforcing locally the no-duplicate-indexing invariant the batched compaction relies on.</summary>
        /// <param name="triple">The triple to admit.</param>
        private void AdmitTriple(EncodedTriple triple)
        {
            if(All.Add(triple))
            {
                RecordAllEntered(triple);
                IndexTriple(triple);
                MergedThisRound.Add(triple);
                MergedThisRoundSet.Add(triple);
            }
        }

        /// <summary>Applies an add-set and a retract-set to the maintained closure, keeping it equal to the from-scratch closure of the edited base — the incremental pipeline for a consistent state.</summary>
        /// <param name="added">The facts to add.</param>
        /// <param name="retracted">The facts to retract.</param>
        /// <param name="cancellationToken">A token that aborts the pipeline between rounds.</param>
        /// <returns>The result over the edited base, with <see cref="MaintenanceStatistics"/> populated.</returns>
        internal OwlRlResult ApplyCore(IReadOnlyCollection<EncodedTriple> added, IReadOnlyCollection<EncodedTriple> retracted, CancellationToken cancellationToken)
        {
            ResetStatistics();

            //Record the net All/Derived membership change for the span of this
            //Apply; the sets hold the result as a live view until the next one.
            ResetMembershipDeltas();
            RecordingMembershipDeltas = true;

            //Normalize to the net effect before any base mutation: an add wins
            //over a co-retract, a non-base or absent retract is a no-op, and a
            //triple named twice in one set counts once.
            HashSet<EncodedTriple> addedSet = [.. added];
            HashSet<EncodedTriple> removalSet = [];
            List<EncodedTriple> removals = [];
            foreach(EncodedTriple r in retracted)
            {
                if(Base.Contains(r) && !addedSet.Contains(r) && removalSet.Add(r))
                {
                    removals.Add(r);
                }
            }

            HashSet<EncodedTriple> additionSet = [];
            List<EncodedTriple> additions = [];
            foreach(EncodedTriple a in added)
            {
                if(!Base.Contains(a) && additionSet.Add(a))
                {
                    additions.Add(a);
                }
            }

            CollectOwners(removals, additions);

            if(removals.Count > 0 || AllListConsumersDirty)
            {
                AdpMemberProperties = null;
                AdcMemberClasses = null;
                KeyProperties = null;
            }

            //The atomic base edit — the only base mutation, ahead of propagation.
            foreach(EncodedTriple r in removals)
            {
                Base.Remove(r);
            }

            foreach(EncodedTriple a in additions)
            {
                Base.Add(a);
            }

            //Seeded removals promote to derived; non-seeded removals seed the
            //deletion frontier.
            List<EncodedTriple> round0 = [];
            foreach(EncodedTriple r in removals)
            {
                if(Seeded.Contains(r))
                {
                    if(Derived.Add(r))
                    {
                        RecordDerivedEntered(r);
                    }

                    StatBasePromotions++;
                }
                else if(DeletionSet.Add(r))
                {
                    round0.Add(r);
                }
            }

            RunOverdeleteFixpoint(round0, cancellationToken);

            //Physical removal of every marked fact from the closure and indexes.
            long physicalRemovalStart = OwlRlMaintenanceInstrumentation.Begin();
            RemoveMarkedFactsFromIndexes();
            OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.PhysicalRemoval, physicalRemovalStart);

            //Clear the previous round's bookkeeping, then admit the base
            //additions with demotion of any that were derived.
            MergedThisRound.Clear();
            MergedThisRoundSet.Clear();
            DeltaStartByPredicate.Clear();
            DeltaStartBySubjectPredicate.Clear();
            DeltaStartByObjectPredicate.Clear();
            DeltaStartInstancesOf.Clear();

            long baseAdmissionStart = OwlRlMaintenanceInstrumentation.Begin();
            foreach(EncodedTriple a in additions)
            {
                if(All.Contains(a))
                {
                    if(Derived.Remove(a))
                    {
                        RecordDerivedLeft(a);
                    }

                    StatBaseDemotions++;
                }
                else
                {
                    AdmitTriple(a);
                }
            }

            OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.BaseAdmission, baseAdmissionStart);

            //Rederive the deleted facts with a surviving derivation over the
            //post-op state and its new canonical choices.
            foreach(EncodedTriple d in DeletionSet)
            {
                if(All.Contains(d))
                {
                    continue;
                }

                long rederiveStart = OwlRlMaintenanceInstrumentation.Begin();
                bool rederivable = CheckRederivable(d);
                OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.Rederive, rederiveStart);
                if(rederivable)
                {
                    AdmitTriple(d);

                    if(Derived.Add(d))
                    {
                        RecordDerivedEntered(d);
                    }

                    StatDirectlyRederived++;
                }
            }

            long ownerReFireStart = OwlRlMaintenanceInstrumentation.Begin();
            bool ownerReFireInconsistent = ReFireOwners();
            OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.OwnerReFire, ownerReFireStart);
            if(ownerReFireInconsistent)
            {
                return Finish(OwlRlMaintenanceMode.Incremental);
            }

            while(true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long insertRoundStart = OwlRlMaintenanceInstrumentation.Begin();
                FireRulesDelta();
                StatInsertRounds++;
                if(InconsistencyRule is not null)
                {
                    OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.InsertRounds, insertRoundStart);

                    return Finish(OwlRlMaintenanceMode.Incremental);
                }

                bool merged = MergePending();
                OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.InsertRounds, insertRoundStart);
                if(!merged)
                {
                    break;
                }
            }

            return Finish(OwlRlMaintenanceMode.Incremental);
        }

        /// <summary>Records the statistics of the completed pass, clears the transient maintenance state, and returns the live-view result.</summary>
        /// <param name="mode">The path the pass took.</param>
        /// <returns>The result over the edited base.</returns>
        private OwlRlResult Finish(OwlRlMaintenanceMode mode)
        {
            StatOverdeleteMarked = DeletionSet.Count;
            int restored = 0;
            foreach(EncodedTriple d in DeletionSet)
            {
                if(All.Contains(d))
                {
                    restored++;
                }
            }

            StatRestoredTotal = restored;

            MaintenanceStatistics = new OwlRlMaintenanceStatistics(
                StatOverdeleteMarked,
                StatDeletionRounds,
                StatDirectlyRederived,
                StatRestoredTotal,
                StatInsertRounds,
                StatChoiceOwnerReFires,
                StatBaseDemotions,
                StatBasePromotions,
                mode);

            DeletionSet.Clear();
            RestrictionOwners.Clear();
            NpaOwners.Clear();
            AllListConsumersDirty = false;
            ClearFrontierState();

            //Stop recording, but leave the recorded deltas standing as the
            //live view until the next Apply resets them.
            RecordingMembershipDeltas = false;

            return new OwlRlResult(Derived, InconsistencyRule is null, InconsistencyRule, InconsistencyPremises, MalformedShapeSnapshot());
        }

        /// <summary>Resets the per-Apply statistics counters.</summary>
        private void ResetStatistics()
        {
            StatOverdeleteMarked = 0;
            StatDeletionRounds = 0;
            StatDirectlyRederived = 0;
            StatRestoredTotal = 0;
            StatInsertRounds = 0;
            StatChoiceOwnerReFires = 0;
            StatBaseDemotions = 0;
            StatBasePromotions = 0;
        }

        /// <summary>The base that results from folding a net edit into the current base — the target of a from-scratch rebuild.</summary>
        /// <param name="added">The facts to add.</param>
        /// <param name="retracted">The facts to retract.</param>
        /// <returns>The edited base.</returns>
        internal HashSet<EncodedTriple> ComputeRebuiltBase(IReadOnlyCollection<EncodedTriple> added, IReadOnlyCollection<EncodedTriple> retracted)
        {
            HashSet<EncodedTriple> newBase = [.. Base];
            HashSet<EncodedTriple> addedSet = [.. added];
            foreach(EncodedTriple r in retracted)
            {
                if(!addedSet.Contains(r))
                {
                    newBase.Remove(r);
                }
            }

            foreach(EncodedTriple a in added)
            {
                newBase.Add(a);
            }

            return newBase;
        }

        /// <summary>The overdelete marking a single deletion of <paramref name="frontierFact"/> produces over the intact indexes — the sandboxed face-2 probe; the state is bit-identical afterwards.</summary>
        /// <param name="frontierFact">The fact whose deletion is marked.</param>
        /// <returns>The facts marked deleted, the frontier fact included.</returns>
        internal HashSet<EncodedTriple> ComputeOverdeleteMarkingCore(EncodedTriple frontierFact)
        {
            bool wasInBase = Base.Remove(frontierFact);
            int savedRounds = StatDeletionRounds;

            DeletionSet.Clear();
            RestrictionOwners.Clear();
            NpaOwners.Clear();
            AllListConsumersDirty = false;
            ClearFrontierState();

            List<EncodedTriple> single = [frontierFact];
            CollectOwnersFrom(single);
            DeletionSet.Add(frontierFact);

            List<EncodedTriple> round0 = [frontierFact];
            RunOverdeleteFixpoint(round0, CancellationToken.None);

            HashSet<EncodedTriple> snapshot = [.. DeletionSet];

            if(wasInBase)
            {
                Base.Add(frontierFact);
            }

            StatDeletionRounds = savedRounds;
            DeletionSet.Clear();
            RestrictionOwners.Clear();
            NpaOwners.Clear();
            AllListConsumersDirty = false;
            ClearFrontierState();

            return snapshot;
        }
    }
}
