using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Owl.Rl;

public static partial class OwlRlClosure
{
    internal sealed partial class ClosureContext
    {
        /// <summary>The round's accepted triples in merge order — the seed of every delta variant that ranges the whole round's growth.</summary>
        private List<EncodedTriple> MergedThisRound { get; } = [];

        /// <summary>The round's accepted triples as a set — the membership skip that keeps eq-rep V2 from redoing V1's work.</summary>
        private HashSet<EncodedTriple> MergedThisRoundSet { get; } = [];

        /// <summary>Predicate → the pre-merge length of its <see cref="ByPredicate"/> list — the tail from there is the predicate's delta this round.</summary>
        private Dictionary<TermId, int> DeltaStartByPredicate { get; } = [];

        /// <summary>(subject, predicate) → the pre-merge length of its object list — the tail is that pair's delta this round.</summary>
        private Dictionary<(TermId Subject, TermId Predicate), int> DeltaStartBySubjectPredicate { get; } = [];

        /// <summary>(object, predicate) → the pre-merge length of its subject list — the tail is that pair's delta this round.</summary>
        private Dictionary<(TermId Object, TermId Predicate), int> DeltaStartByObjectPredicate { get; } = [];

        /// <summary>Type → the pre-merge length of its instance list — the tail is the type's newly typed instances this round.</summary>
        private Dictionary<TermId, int> DeltaStartInstancesOf { get; } = [];

        /// <summary>Whether the round merged a triple whose predicate structures RDF lists — the conservative trigger for list-shaped constructs.</summary>
        private bool ListStructureDirty { get; set; }

        /// <summary>The properties named as members of some <c>owl:AllDisjointProperties</c> list — a conservative trigger for prp-adp; <c>null</c> until first built.</summary>
        private HashSet<TermId>? AdpMemberProperties { get; set; }

        /// <summary>The classes named as members of some <c>owl:AllDisjointClasses</c> list — a conservative trigger for cax-adc; <c>null</c> until first built.</summary>
        private HashSet<TermId>? AdcMemberClasses { get; set; }

        /// <summary>The properties named in some <c>owl:hasKey</c> list — a conservative trigger for prp-key; <c>null</c> until first built.</summary>
        private HashSet<TermId>? KeyProperties { get; set; }

        /// <summary>Subjects of the (object, predicate) pair — the reverse-join primitive, <c>[]</c> when absent, mirroring <see cref="ObjectsOf"/>.</summary>
        /// <param name="object">The object end.</param>
        /// <param name="predicate">The predicate.</param>
        /// <returns>The subjects reaching <paramref name="object"/> over <paramref name="predicate"/>.</returns>
        private List<TermId> SubjectsOf(TermId @object, TermId predicate)
        {
            return ByObjectPredicate.TryGetValue((@object, predicate), out List<TermId>? subjects) ? subjects : [];
        }

        /// <summary>The predicate's pairs merged this round — the tail of its <see cref="ByPredicate"/> list from the recorded start.</summary>
        /// <param name="predicate">The predicate whose delta pairs are wanted.</param>
        /// <returns>The (subject, object) pairs added this round under <paramref name="predicate"/>.</returns>
        private List<(TermId Subject, TermId Object)> DeltaPairs(TermId predicate)
        {
            List<(TermId Subject, TermId Object)> pairs = Pairs(predicate);
            if(!DeltaStartByPredicate.TryGetValue(predicate, out int start))
            {
                return [];
            }

            List<(TermId Subject, TermId Object)> tail = [];
            for(int i = start; i < pairs.Count; i++)
            {
                tail.Add(pairs[i]);
            }

            return tail;
        }

        /// <summary>The objects a (subject, predicate) pair gained this round — the tail of its object list from the recorded start.</summary>
        /// <param name="key">The (subject, predicate) pair.</param>
        /// <returns>The objects added this round under that pair.</returns>
        private List<TermId> DeltaObjectsTail((TermId Subject, TermId Predicate) key)
        {
            List<TermId> objects = ObjectsOf(key.Subject, key.Predicate);
            if(!DeltaStartBySubjectPredicate.TryGetValue(key, out int start))
            {
                return [];
            }

            List<TermId> tail = [];
            for(int i = start; i < objects.Count; i++)
            {
                tail.Add(objects[i]);
            }

            return tail;
        }

        /// <summary>The subjects an (object, predicate) pair gained this round — the tail of its subject list from the recorded start.</summary>
        /// <param name="key">The (object, predicate) pair.</param>
        /// <returns>The subjects added this round under that pair.</returns>
        private List<TermId> DeltaSubjectsTail((TermId Object, TermId Predicate) key)
        {
            List<TermId> subjects = SubjectsOf(key.Object, key.Predicate);
            if(!DeltaStartByObjectPredicate.TryGetValue(key, out int start))
            {
                return [];
            }

            List<TermId> tail = [];
            for(int i = start; i < subjects.Count; i++)
            {
                tail.Add(subjects[i]);
            }

            return tail;
        }

        /// <summary>The instances a type gained this round — the tail of its instance list from the recorded start.</summary>
        /// <param name="type">The type whose newly typed instances are wanted.</param>
        /// <returns>The instances added this round under <paramref name="type"/>.</returns>
        private List<TermId> DeltaInstancesTail(TermId type)
        {
            if(!InstancesOf.TryGetValue(type, out List<TermId>? instances) || !DeltaStartInstancesOf.TryGetValue(type, out int start))
            {
                return [];
            }

            List<TermId> tail = [];
            for(int i = start; i < instances.Count; i++)
            {
                tail.Add(instances[i]);
            }

            return tail;
        }

        /// <summary>Whether the type gained any instance this round.</summary>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> when the type's instance list grew this round.</returns>
        private bool HasDeltaInstances(TermId type)
        {
            return DeltaStartInstancesOf.ContainsKey(type);
        }

        /// <summary>Fires every rule family once over the round's merged delta — the semi-naive counterpart of <see cref="FireRules"/>, five families in order with an early return between families on falsity.</summary>
        public void FireRulesDelta()
        {
            ListStructureDirty = false;
            foreach(EncodedTriple triple in MergedThisRound)
            {
                if(triple.Predicate == Terms.First || triple.Predicate == Terms.Rest || triple.Predicate == Terms.Members || triple.Predicate == Terms.DistinctMembers)
                {
                    ListStructureDirty = true;

                    break;
                }
            }

            FireEqualityDelta();
            if(InconsistencyRule is not null)
            {
                return;
            }

            FirePropertiesDelta();
            if(InconsistencyRule is not null)
            {
                return;
            }

            FireClassesDelta();
            if(InconsistencyRule is not null)
            {
                return;
            }

            FireClassAxiomsDelta();
            if(InconsistencyRule is not null)
            {
                return;
            }

            FireSchemaDelta();

            //The comprehension completion family re-fires in full when any
            //of its premise kinds grew this round — the adds dedup.
            if(Comprehension == OwlComprehension.InformativeConditions && ComprehensionPremisesGrew())
            {
                FireComprehension();
            }
        }

        /// <summary>Fires the eq-* rules over the round's delta — eq-ref over the merged triples, the sameAs-pair block, the differentFrom block, eq-rep over old sameAs × new triples, and the AllDifferent re-check.</summary>
        private void FireEqualityDelta()
        {
            //eq-ref: every term of a merged triple equals itself; the pair
            //block picks the self-pairs up on the round they merge.
            foreach(EncodedTriple triple in MergedThisRound)
            {
                Add(triple.Subject, Terms.SameAs, triple.Subject, EntailmentRules.EqRef, [triple]);
                Add(triple.Predicate, Terms.SameAs, triple.Predicate, EntailmentRules.EqRef, [triple]);
                Add(triple.Object, Terms.SameAs, triple.Object, EntailmentRules.EqRef, [triple]);
            }

            //sameAs-pair block: each new pair drives eq-sym, eq-trans in both
            //atom positions, the eq-diff / dt falsities, and eq-rep in each
            //position — mirroring naive's per-pair interleaving.
            foreach((TermId x, TermId y) in DeltaPairs(Terms.SameAs))
            {
                EncodedTriple same = Fact(x, Terms.SameAs, y);

                //eq-sym.
                Add(y, Terms.SameAs, x, EntailmentRules.EqSym, [same]);

                //eq-trans, new pair as the first atom.
                foreach(TermId z in ObjectsOf(y, Terms.SameAs))
                {
                    Add(x, Terms.SameAs, z, EntailmentRules.EqTrans, [same, Fact(y, Terms.SameAs, z)]);
                }

                //eq-trans, new pair as the second atom.
                foreach(TermId w in SubjectsOf(x, Terms.SameAs))
                {
                    Add(w, Terms.SameAs, y, EntailmentRules.EqTrans, [Fact(w, Terms.SameAs, x), same]);
                }

                //eq-diff1: a sameAs and a differentFrom between the same pair.
                if(ObjectsOf(x, Terms.DifferentFrom).Contains(y))
                {
                    Inconsistent(EntailmentRules.EqDiff1, [same, Fact(x, Terms.DifferentFrom, y)]);

                    return;
                }

                //dt-diff: sameAs between literals denoting distinct values.
                if(x != y && DatatypeOracle.LiteralsKnownDistinct(x, y))
                {
                    Inconsistent(EntailmentRules.DtDiff, [same]);

                    return;
                }

                //dt-disjoint-identity: sameAs between datatypes of
                //known-disjoint value spaces.
                if(x != y && DatatypeOracle.DatatypesKnownDisjoint(x, y))
                {
                    Inconsistent(EntailmentRules.DtDisjointIdentity, [same]);

                    return;
                }

                if(x == y)
                {
                    continue;
                }

                //eq-rep-s / eq-rep-p / eq-rep-o: every triple mentioning x
                //holds with y substituted, reached by the by-term access
                //paths rather than a scan of the whole set.
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

            //differentFrom block: symmetry and the mirror eq-diff1 direction.
            foreach((TermId x, TermId y) in DeltaPairs(Terms.DifferentFrom))
            {
                Add(y, Terms.DifferentFrom, x, EntailmentRules.DifferentFromSymmetry, [Fact(x, Terms.DifferentFrom, y)]);

                if(ObjectsOf(x, Terms.SameAs).Contains(y))
                {
                    Inconsistent(EntailmentRules.EqDiff1, [Fact(x, Terms.SameAs, y), Fact(x, Terms.DifferentFrom, y)]);

                    return;
                }
            }

            //eq-rep V2: an OLD sameAs applied to a NEW triple. For each
            //position of each merged triple, substitute every equal term the
            //old index already holds; the set-membership skip avoids redoing
            //what the pair block above already covered this round.
            foreach(EncodedTriple triple in MergedThisRound)
            {
                EqRepOverNewTriple(triple.Subject, triple, Position.Subject);
                EqRepOverNewTriple(triple.Predicate, triple, Position.Predicate);
                EqRepOverNewTriple(triple.Object, triple, Position.Object);
            }

            //eq-diff2 / eq-diff3: re-check the AllDifferent nodes the round
            //could have made inconsistent — newly typed nodes always, and all
            //nodes when list structure or sameAs grew.
            bool sameAsGrew = DeltaStartByPredicate.ContainsKey(Terms.SameAs);
            if(ListStructureDirty || sameAsGrew)
            {
                if(InstancesOf.TryGetValue(Terms.AllDifferent, out List<TermId>? allNodes))
                {
                    foreach(TermId node in allNodes)
                    {
                        if(CheckAllDifferentNode(node))
                        {
                            return;
                        }
                    }
                }
            }
            else
            {
                foreach(TermId node in DeltaInstancesTail(Terms.AllDifferent))
                {
                    if(CheckAllDifferentNode(node))
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>The distinct predicates the term appears under as a subject — <c>[]</c> when it never has, matching the by-term access path.</summary>
        /// <param name="subject">The subject term.</param>
        /// <returns>The predicates <paramref name="subject"/> appears under as a subject.</returns>
        private List<TermId> PredicatesOfSubjectList(TermId subject)
        {
            return PredicatesOfSubject.TryGetValue(subject, out List<TermId>? predicates) ? predicates : [];
        }

        /// <summary>The distinct predicates the term appears under as an object — <c>[]</c> when it never has.</summary>
        /// <param name="object">The object term.</param>
        /// <returns>The predicates <paramref name="object"/> appears under as an object.</returns>
        private List<TermId> PredicatesOfObjectList(TermId @object)
        {
            return PredicatesOfObject.TryGetValue(@object, out List<TermId>? predicates) ? predicates : [];
        }

        /// <summary>The position of a term within a triple, selecting which eq-rep rule an old sameAs on it fires.</summary>
        private enum Position
        {
            /// <summary>The subject position — eq-rep-s.</summary>
            Subject,

            /// <summary>The predicate position — eq-rep-p.</summary>
            Predicate,

            /// <summary>The object position — eq-rep-o.</summary>
            Object,
        }

        /// <summary>Applies every old <c>owl:sameAs</c> of <paramref name="term"/> to the new <paramref name="triple"/> at <paramref name="position"/>, substituting the equal term — the eq-rep variant over old equalities and new triples.</summary>
        /// <param name="term">The term at <paramref name="position"/> whose old equalities are applied.</param>
        /// <param name="triple">The newly merged triple.</param>
        /// <param name="position">The term's position within the triple.</param>
        private void EqRepOverNewTriple(TermId term, EncodedTriple triple, Position position)
        {
            foreach(TermId y in ObjectsOf(term, Terms.SameAs))
            {
                if(y == term || MergedThisRoundSet.Contains(Fact(term, Terms.SameAs, y)))
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

        /// <summary>Fires the prp-* rules over the round's delta, in naive family order, returning between rules on falsity.</summary>
        private void FirePropertiesDelta()
        {
            //prp-dom.
            foreach((TermId p, TermId c) in DeltaPairs(Terms.Domain))
            {
                EncodedTriple domain = Fact(p, Terms.Domain, c);
                foreach((TermId x, TermId y) in Pairs(p))
                {
                    Add(x, Terms.Type, c, EntailmentRules.PrpDom, [domain, Fact(x, p, y)]);
                }
            }

            foreach(EncodedTriple t in MergedThisRound)
            {
                foreach(TermId c in ObjectsOf(t.Predicate, Terms.Domain))
                {
                    Add(t.Subject, Terms.Type, c, EntailmentRules.PrpDom, [Fact(t.Predicate, Terms.Domain, c), t]);
                }
            }

            //prp-rng + dt-not-type, check-then-add per edge as naive does.
            foreach((TermId p, TermId c) in DeltaPairs(Terms.Range))
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

            foreach(EncodedTriple t in MergedThisRound)
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

            //dt-range-intersection: a new range pairs with every earlier range
            //of the same property. The new elements are at the tail of the
            //property's range list and each (s,p) list holds distinct objects,
            //so walking from index 0 until the new element reproduces naive's
            //i<j pair set exactly.
            foreach((TermId p, TermId d) in DeltaPairs(Terms.Range))
            {
                List<TermId> ranges = ObjectsOf(p, Terms.Range);
                for(int i = 0; i < ranges.Count; i++)
                {
                    TermId earlier = ranges[i];
                    if(earlier == d)
                    {
                        break;
                    }

                    foreach(TermId superset in DatatypeOracle.RangeIntersectionSupersets(earlier, d))
                    {
                        if(superset != earlier && superset != d)
                        {
                            Add(p, Terms.Range, superset, EntailmentRules.DtRangeIntersection, [Fact(p, Terms.Range, earlier), Fact(p, Terms.Range, d)]);
                        }
                    }
                }
            }

            //Characteristics — a new typing re-fires the characteristic body
            //over the full indexes; a new edge fires the per-edge variants.
            foreach((TermId p, TermId characteristic) in DeltaPairs(Terms.Type))
            {
                if(FireCharacteristic(p, characteristic))
                {
                    return;
                }
            }

            if(FireCharacteristicDataDelta())
            {
                return;
            }

            //prp-spo1.
            foreach((TermId p1, TermId p2) in DeltaPairs(Terms.SubPropertyOf))
            {
                EncodedTriple subProperty = Fact(p1, Terms.SubPropertyOf, p2);
                foreach((TermId x, TermId y) in Pairs(p1))
                {
                    Add(x, p2, y, EntailmentRules.PrpSpo1, [subProperty, Fact(x, p1, y)]);
                }
            }

            foreach(EncodedTriple t in MergedThisRound)
            {
                foreach(TermId p2 in ObjectsOf(t.Predicate, Terms.SubPropertyOf))
                {
                    Add(t.Subject, p2, t.Object, EntailmentRules.PrpSpo1, [Fact(t.Predicate, Terms.SubPropertyOf, p2), t]);
                }
            }

            //prp-spo2 + chain-trans.
            FireChainAxiomsDelta();

            //prp-eqp1 / prp-eqp2.
            foreach((TermId p1, TermId p2) in DeltaPairs(Terms.EquivalentProperty))
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

            foreach(EncodedTriple t in MergedThisRound)
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

            //prp-pdw.
            foreach((TermId p1, TermId p2) in DeltaPairs(Terms.PropertyDisjointWith))
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

            foreach(EncodedTriple t in MergedThisRound)
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

            //prp-adp: conservative full per-node re-check on its triggers.
            if(FireAllDisjointPropertiesDelta())
            {
                return;
            }

            //prp-inv1 / prp-inv2.
            foreach((TermId p1, TermId p2) in DeltaPairs(Terms.InverseOf))
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

            foreach(EncodedTriple t in MergedThisRound)
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

            //prp-key: full per-axiom on structural triggers, ordered
            //tail-pairing on newly typed instances, conservative full re-fire
            //when a key-property edge grew.
            FireKeyDelta();

            //prp-npa: conservative full re-check of every node carrying
            //negative-assertion helpers, keyed off owl:sourceIndividual —
            //distinct subjects only, no typing antecedent.
            HashSet<TermId> negativeAssertionNodes = [];
            foreach((TermId node, TermId _) in Pairs(Terms.SourceIndividual))
            {
                if(negativeAssertionNodes.Add(node) && FireNegativePropertyAssertionNode(node))
                {
                    return;
                }
            }
        }

        /// <summary>Fires the per-edge / per-typing characteristic variants over the round's delta — the data-side counterpart of the naive characteristic body. Returns whether a falsity (irp / asyp) fired.</summary>
        /// <returns><c>true</c> when a characteristic made the closure inconsistent.</returns>
        private bool FireCharacteristicDataDelta()
        {
            //Functional: a new edge under a functional property pairs with the
            //pair's earlier objects (ordered tail-pairing).
            foreach(KeyValuePair<(TermId Subject, TermId Predicate), int> entry in DeltaStartBySubjectPredicate)
            {
                (TermId s, TermId p) = entry.Key;
                if(!HasType(p, Terms.FunctionalProperty))
                {
                    continue;
                }

                EncodedTriple typing = Fact(p, Terms.Type, Terms.FunctionalProperty);
                List<TermId> list = ObjectsOf(s, p);
                for(int k = entry.Value; k < list.Count; k++)
                {
                    for(int i = 0; i < k; i++)
                    {
                        if(list[i] != list[k])
                        {
                            Add(list[i], Terms.SameAs, list[k], EntailmentRules.PrpFp, [typing, Fact(s, p, list[i]), Fact(s, p, list[k])]);
                        }
                    }
                }
            }

            //Inverse-functional: same over the object-keyed lists.
            foreach(KeyValuePair<(TermId Object, TermId Predicate), int> entry in DeltaStartByObjectPredicate)
            {
                (TermId o, TermId p) = entry.Key;
                if(!HasType(p, Terms.InverseFunctionalProperty))
                {
                    continue;
                }

                EncodedTriple typing = Fact(p, Terms.Type, Terms.InverseFunctionalProperty);
                List<TermId> list = SubjectsOf(o, p);
                for(int k = entry.Value; k < list.Count; k++)
                {
                    for(int i = 0; i < k; i++)
                    {
                        if(list[i] != list[k])
                        {
                            Add(list[i], Terms.SameAs, list[k], EntailmentRules.PrpIfp, [typing, Fact(list[i], p, o), Fact(list[k], p, o)]);
                        }
                    }
                }
            }

            //Per-edge variants over the round's merged edges.
            foreach(EncodedTriple t in MergedThisRound)
            {
                //irp.
                if(t.Subject == t.Object && HasType(t.Predicate, Terms.IrreflexiveProperty))
                {
                    Inconsistent(EntailmentRules.PrpIrp, [Fact(t.Predicate, Terms.Type, Terms.IrreflexiveProperty), t]);

                    return true;
                }

                //symp.
                if(HasType(t.Predicate, Terms.SymmetricProperty))
                {
                    Add(t.Object, t.Predicate, t.Subject, EntailmentRules.PrpSymp, [Fact(t.Predicate, Terms.Type, Terms.SymmetricProperty), t]);
                }

                //asyp.
                if(HasType(t.Predicate, Terms.AsymmetricProperty) && ObjectsOf(t.Object, t.Predicate).Contains(t.Subject))
                {
                    Inconsistent(EntailmentRules.PrpAsyp, [Fact(t.Predicate, Terms.Type, Terms.AsymmetricProperty), t, Fact(t.Object, t.Predicate, t.Subject)]);

                    return true;
                }

                //trp, both directions the new edge participates in.
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

            //Reflexive: newly named individuals instantiate every reflexive
            //property; new reflexive typings are covered by the per-typing
            //characteristic re-fire in the caller.
            foreach(TermId x in DeltaInstancesTail(Terms.NamedIndividual))
            {
                if(InstancesOf.TryGetValue(Terms.ReflexiveProperty, out List<TermId>? reflexives))
                {
                    foreach(TermId p in reflexives)
                    {
                        Add(x, p, x, EntailmentRules.ReflexiveInstantiation, [Fact(p, Terms.Type, Terms.ReflexiveProperty), Fact(x, Terms.Type, Terms.NamedIndividual)]);
                    }
                }
            }

            return false;
        }

        /// <summary>Fires prp-spo2 and chain-trans over the round's delta — a dirty axiom re-fires its full body; otherwise a new edge on any chain hop drives a per-hop backward-and-forward walk.</summary>
        private void FireChainAxiomsDelta()
        {
            foreach((TermId p, TermId listHead) in Pairs(Terms.PropertyChainAxiom))
            {
                if(ListOf(listHead) is not List<TermId> chain || chain.Count == 0)
                {
                    continue;
                }

                bool axiomDirty = ListStructureDirty || DeltaAxiomTouched(p, listHead, Terms.PropertyChainAxiom);
                if(axiomDirty)
                {
                    FireChainAxiom(p, listHead, chain);

                    continue;
                }

                EncodedTriple chainAxiom = Fact(p, Terms.PropertyChainAxiom, listHead);
                for(int i = 0; i < chain.Count; i++)
                {
                    List<(TermId Subject, TermId Object)> delta = DeltaPairs(chain[i]);
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

        /// <summary>Walks one chain hop's new edge to every start and end it completes — backward through the earlier hops and forward through the later ones, iteratively — and fires prp-spo2 for each start-to-end pairing with the naive frontier premises.</summary>
        /// <param name="p">The super-property the chain implies.</param>
        /// <param name="chainAxiom">The chain axiom triple.</param>
        /// <param name="chain">The chain properties in positional order.</param>
        /// <param name="index">The hop position the new edge fills.</param>
        /// <param name="u">The new edge's subject.</param>
        /// <param name="v">The new edge's object.</param>
        private void FireChainHopDelta(TermId p, EncodedTriple chainAxiom, List<TermId> chain, int index, TermId u, TermId v)
        {
            EncodedTriple hop = Fact(u, chain[index], v);

            //Backward: from u through chain[index-1]..chain[0], collecting the
            //start and the hops traversed in positional order.
            List<(TermId Node, List<EncodedTriple> Hops)> backward = [(u, [])];
            for(int j = index - 1; j >= 0; j--)
            {
                List<(TermId Node, List<EncodedTriple> Hops)> extended = [];
                foreach((TermId node, List<EncodedTriple> hops) in backward)
                {
                    foreach(TermId prev in SubjectsOf(node, chain[j]))
                    {
                        extended.Add((prev, [Fact(prev, chain[j], node), .. hops]));
                    }
                }

                backward = extended;
                if(backward.Count == 0)
                {
                    return;
                }
            }

            //Forward: from v through chain[index+1].., collecting the end and
            //the hops traversed in positional order.
            List<(TermId Node, List<EncodedTriple> Hops)> forward = [(v, [])];
            for(int j = index + 1; j < chain.Count; j++)
            {
                List<(TermId Node, List<EncodedTriple> Hops)> extended = [];
                foreach((TermId node, List<EncodedTriple> hops) in forward)
                {
                    foreach(TermId next in ObjectsOf(node, chain[j]))
                    {
                        extended.Add((next, [.. hops, Fact(node, chain[j], next)]));
                    }
                }

                forward = extended;
                if(forward.Count == 0)
                {
                    return;
                }
            }

            foreach((TermId start, List<EncodedTriple> backHops) in backward)
            {
                foreach((TermId end, List<EncodedTriple> forwardHops) in forward)
                {
                    List<EncodedTriple> hops = [.. backHops, hop, .. forwardHops];
                    Add(start, p, end, EntailmentRules.PrpSpo2, [chainAxiom, .. hops]);
                }
            }
        }

        /// <summary>Fires prp-adp over the round's delta by a conservative full per-node re-check — newly typed <c>owl:AllDisjointProperties</c> nodes always, and all nodes when list structure or a member property's edge grew. Returns whether a falsity fired.</summary>
        /// <returns><c>true</c> when a disjoint pair shared an edge and made the closure inconsistent.</returns>
        private bool FireAllDisjointPropertiesDelta()
        {
            bool newTyping = HasDeltaInstances(Terms.AllDisjointProperties);
            bool memberEdgeGrew = false;
            if(!ListStructureDirty && !newTyping)
            {
                AdpMemberProperties ??= BuildAdpMemberProperties();
                foreach(EncodedTriple t in MergedThisRound)
                {
                    if(AdpMemberProperties.Contains(t.Predicate))
                    {
                        memberEdgeGrew = true;

                        break;
                    }
                }
            }

            if(ListStructureDirty || newTyping)
            {
                //A list or a new typing may have grown the member sets — the
                //trigger cache is rebuilt so a later round sees them.
                AdpMemberProperties = BuildAdpMemberProperties();
            }

            if(ListStructureDirty || memberEdgeGrew)
            {
                if(InstancesOf.TryGetValue(Terms.AllDisjointProperties, out List<TermId>? nodes))
                {
                    foreach(TermId node in nodes)
                    {
                        if(FireAllDisjointPropertiesNode(node))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            foreach(TermId node in DeltaInstancesTail(Terms.AllDisjointProperties))
            {
                if(FireAllDisjointPropertiesNode(node))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Builds the set of properties named in any <c>owl:AllDisjointProperties</c> members list, every asserted list included — the conservative trigger for prp-adp.</summary>
        /// <returns>The member properties.</returns>
        private HashSet<TermId> BuildAdpMemberProperties()
        {
            HashSet<TermId> properties = [];
            if(InstancesOf.TryGetValue(Terms.AllDisjointProperties, out List<TermId>? nodes))
            {
                foreach(TermId node in nodes)
                {
                    foreach(TermId head in ObjectsOf(node, Terms.Members))
                    {
                        if(ListOf(head) is List<TermId> members)
                        {
                            foreach(TermId member in members)
                            {
                                properties.Add(member);
                            }
                        }
                    }
                }
            }

            return properties;
        }

        /// <summary>Fires prp-key over the round's delta — full per-axiom on structural triggers, ordered tail-pairing over a class's newly typed instances, and a conservative full re-fire when a key-property edge grew.</summary>
        private void FireKeyDelta()
        {
            bool hasKeyGrew = DeltaStartByPredicate.ContainsKey(Terms.HasKey);
            if(ListStructureDirty || hasKeyGrew)
            {
                KeyProperties = BuildKeyProperties();
            }
            else
            {
                KeyProperties ??= BuildKeyProperties();
            }

            if(ListStructureDirty || hasKeyGrew)
            {
                //Structural change re-fires every key axiom's full body; this
                //subsumes the per-typing and per-edge variants for the round.
                foreach((TermId c, TermId listHead) in Pairs(Terms.HasKey))
                {
                    FireKeyAxiom(c, listHead);
                }

                return;
            }

            //A key-property edge grew: conservatively re-fire every key axiom
            //(the falsity-free pairwise Adds dedup).
            bool keyEdgeGrew = false;
            foreach(EncodedTriple t in MergedThisRound)
            {
                if(KeyProperties.Contains(t.Predicate))
                {
                    keyEdgeGrew = true;

                    break;
                }
            }

            if(keyEdgeGrew)
            {
                foreach((TermId c, TermId listHead) in Pairs(Terms.HasKey))
                {
                    FireKeyAxiom(c, listHead);
                }

                return;
            }

            //Otherwise only newly typed instances of a keyed class can create
            //a new sharing pair — tail-pair each such instance against the
            //class's earlier instances.
            foreach((TermId c, TermId listHead) in Pairs(Terms.HasKey))
            {
                if(ListOf(listHead) is not List<TermId> keys || keys.Count == 0 || !InstancesOf.TryGetValue(c, out List<TermId>? instances))
                {
                    continue;
                }

                if(!DeltaStartInstancesOf.TryGetValue(c, out int start))
                {
                    continue;
                }

                EncodedTriple hasKey = Fact(c, Terms.HasKey, listHead);
                for(int k = start; k < instances.Count; k++)
                {
                    for(int i = 0; i < k; i++)
                    {
                        FireKeyPair(hasKey, c, keys, instances[i], instances[k]);
                    }
                }
            }
        }

        /// <summary>Builds the set of properties named in any <c>owl:hasKey</c> list — the conservative trigger for prp-key.</summary>
        /// <returns>The key properties.</returns>
        private HashSet<TermId> BuildKeyProperties()
        {
            HashSet<TermId> properties = [];
            foreach((TermId _, TermId listHead) in Pairs(Terms.HasKey))
            {
                if(ListOf(listHead) is List<TermId> keys)
                {
                    foreach(TermId key in keys)
                    {
                        properties.Add(key);
                    }
                }
            }

            return properties;
        }

        /// <summary>Fires the cls-* rules over the round's delta, in naive family order, returning between rules on falsity.</summary>
        private void FireClassesDelta()
        {
            //cls-nothing2.
            List<TermId> newNothings = DeltaInstancesTail(Terms.Nothing);
            if(newNothings.Count > 0)
            {
                Inconsistent(EntailmentRules.ClsNothing2, [Fact(newNothings[0], Terms.Type, Terms.Nothing)]);

                return;
            }

            //The rdf:nil structure falsity: only a round that merged an
            //rdf:first or rdf:rest triple can have put one on rdf:nil.
            if(ListStructureDirty && CheckNilStructure())
            {
                return;
            }

            //cls-int1 / cls-int2.
            FireIntersectionDelta();

            //cls-uni.
            FireUnionDelta();

            //cls-com.
            if(FireComplementDelta())
            {
                return;
            }

            //Restrictions.
            if(FireRestrictionsDelta())
            {
                return;
            }

            //The Thing-enumeration falsity keys off the axiom triple alone,
            //so only the round merging an owl:oneOf pair can introduce it.
            foreach((TermId c, TermId listHead) in DeltaPairs(Terms.OneOf))
            {
                if(c == Terms.Thing)
                {
                    Inconsistent(EntailmentRules.ThingEnumerationClash, [Fact(c, Terms.OneOf, listHead)]);

                    return;
                }
            }

            //cls-oo.
            foreach((TermId c, TermId listHead) in Pairs(Terms.OneOf))
            {
                if(ListStructureDirty || DeltaAxiomTouched(c, listHead, Terms.OneOf))
                {
                    FireOneOfAxiom(c, listHead);
                }
            }
        }

        /// <summary>Fires cls-int1 / cls-int2 over the round's delta — a dirty axiom re-fires its full body; otherwise a newly typed instance drives int1 for the axioms whose first member it gained and int2 for the intersections it became.</summary>
        private void FireIntersectionDelta()
        {
            foreach((TermId c, TermId listHead) in Pairs(Terms.IntersectionOf))
            {
                if(ListOf(listHead) is not List<TermId> members || members.Count == 0)
                {
                    continue;
                }

                if(ListStructureDirty || DeltaAxiomTouched(c, listHead, Terms.IntersectionOf))
                {
                    FireIntersectionAxiom(c, listHead, members);
                }
            }

            List<(TermId Subject, TermId Object)> typingDelta = DeltaPairs(Terms.Type);
            if(typingDelta.Count == 0)
            {
                return;
            }

            //cls-int1: a newly typed instance may complete some intersection
            //whose first member it now has.
            foreach((TermId c, TermId listHead) in Pairs(Terms.IntersectionOf))
            {
                if(ListOf(listHead) is not List<TermId> members || members.Count == 0)
                {
                    continue;
                }

                if(ListStructureDirty || DeltaAxiomTouched(c, listHead, Terms.IntersectionOf))
                {
                    //Already fired in full above.
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

            //cls-int2: a newly typed instance of an intersection is an
            //instance of every member.
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

        /// <summary>Fires cls-uni over the round's delta — a dirty axiom re-fires its full body; otherwise a newly typed instance of any member becomes an instance of the union.</summary>
        private void FireUnionDelta()
        {
            foreach((TermId c, TermId listHead) in Pairs(Terms.UnionOf))
            {
                if(ListOf(listHead) is not List<TermId> members)
                {
                    continue;
                }

                if(ListStructureDirty || DeltaAxiomTouched(c, listHead, Terms.UnionOf))
                {
                    FireUnionAxiom(c, listHead, members);
                }
            }

            List<(TermId Subject, TermId Object)> typingDelta = DeltaPairs(Terms.Type);
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

                if(ListStructureDirty || DeltaAxiomTouched(c, listHead, Terms.UnionOf))
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

        /// <summary>Fires cls-com over the round's delta — a new complement axiom materialises its symmetric statement and scans its instances, and a new typing checks the term's complements in both directions. Returns whether a falsity fired.</summary>
        /// <returns><c>true</c> when a term held a class and its complement and made the closure inconsistent.</returns>
        private bool FireComplementDelta()
        {
            //V1: a new complementOf axiom reverses symmetrically and scans
            //the first class's instances.
            foreach((TermId c1, TermId c2) in DeltaPairs(Terms.ComplementOf))
            {
                EncodedTriple complement = Fact(c1, Terms.ComplementOf, c2);
                Add(c2, Terms.ComplementOf, c1, EntailmentRules.ComplementOfSymmetry, [complement]);
                if(InstancesOf.TryGetValue(c1, out List<TermId>? instances))
                {
                    foreach(TermId x in instances)
                    {
                        if(HasType(x, c2))
                        {
                            Inconsistent(EntailmentRules.ClsCom, [complement, Fact(x, Terms.Type, c1), Fact(x, Terms.Type, c2)]);

                            return true;
                        }
                    }
                }
            }

            //V2: a new typing checks the term's complements both ways.
            foreach((TermId x, TermId c) in DeltaPairs(Terms.Type))
            {
                foreach(TermId c2 in ObjectsOf(c, Terms.ComplementOf))
                {
                    if(HasType(x, c2))
                    {
                        Inconsistent(EntailmentRules.ClsCom, [Fact(c, Terms.ComplementOf, c2), Fact(x, Terms.Type, c), Fact(x, Terms.Type, c2)]);

                        return true;
                    }
                }

                foreach(TermId c1 in SubjectsOf(c, Terms.ComplementOf))
                {
                    if(HasType(x, c1))
                    {
                        Inconsistent(EntailmentRules.ClsCom, [Fact(c1, Terms.ComplementOf, c), Fact(x, Terms.Type, c1), Fact(x, Terms.Type, c)]);

                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Fires every restriction rule over the round's delta — structure-dirty restriction nodes re-fire in full, new edges and new typings drive the per-edge and per-typing variants, and the filler side reaches restrictions through the reverse indexes with the first-object mirror re-verified. Returns whether a falsity fired.</summary>
        /// <returns><c>true</c> when a restriction falsity made the closure inconsistent.</returns>
        private bool FireRestrictionsDelta()
        {
            //Structure-dirty re-fire: any restriction node whose defining
            //triples grew re-fires its full body for each of its properties.
            HashSet<TermId> dirtyRestrictions = [];
            CollectRestrictionSubjects(Terms.OnProperty, dirtyRestrictions);
            CollectRestrictionSubjects(Terms.SomeValuesFrom, dirtyRestrictions);
            CollectRestrictionSubjects(Terms.AllValuesFrom, dirtyRestrictions);
            CollectRestrictionSubjects(Terms.HasValue, dirtyRestrictions);
            CollectRestrictionSubjects(Terms.MaxCardinality, dirtyRestrictions);
            CollectRestrictionSubjects(Terms.MaxQualifiedCardinality, dirtyRestrictions);
            CollectRestrictionSubjects(Terms.MinCardinality, dirtyRestrictions);
            CollectRestrictionSubjects(Terms.OnClass, dirtyRestrictions);

            foreach(TermId x in dirtyRestrictions)
            {
                foreach(TermId p in ObjectsOf(x, Terms.OnProperty))
                {
                    if(FireRestrictionBody(x, p))
                    {
                        return true;
                    }
                }
            }

            //Per-edge data variants: a new edge under a restricted property
            //fires svf / avf / hv2 / maxc-0 / maxqc-0 for the restrictions on
            //that property that are not already covered by a full re-fire.
            foreach(EncodedTriple t in MergedThisRound)
            {
                foreach(TermId x in SubjectsOf(t.Predicate, Terms.OnProperty))
                {
                    if(dirtyRestrictions.Contains(x))
                    {
                        continue;
                    }

                    if(FireRestrictionEdgeVariants(x, t.Predicate, t.Subject, t.Object))
                    {
                        return true;
                    }
                }
            }

            //Per-touched-pair max variants: a (u,p) whose object list grew
            //may exceed a one-bound. Ordered tail-pairing over its new objects.
            foreach(KeyValuePair<(TermId Subject, TermId Predicate), int> entry in DeltaStartBySubjectPredicate)
            {
                (TermId u, TermId p) = entry.Key;
                foreach(TermId x in SubjectsOf(p, Terms.OnProperty))
                {
                    if(dirtyRestrictions.Contains(x) || !HasType(u, x))
                    {
                        continue;
                    }

                    FireMaxPairVariants(x, p, u, entry.Value);
                }
            }

            //Per-typing variants: a newly typed instance of a restriction
            //fires avf / hv1 / maxc / maxqc over its edges.
            foreach((TermId u, TermId x) in DeltaPairs(Terms.Type))
            {
                if(dirtyRestrictions.Contains(x))
                {
                    continue;
                }

                foreach(TermId p in ObjectsOf(x, Terms.OnProperty))
                {
                    if(FireRestrictionTypingVariants(x, p, u))
                    {
                        return true;
                    }
                }
            }

            //Filler-side variants: a newly typed instance may be a filler a
            //restriction reads. Reach the restriction through the reverse
            //index and re-verify the first-object mirror.
            foreach((TermId v, TermId filler) in DeltaPairs(Terms.Type))
            {
                if(FireRestrictionFillerVariants(v, filler, dirtyRestrictions))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Collects the subjects of the predicate's new pairs into <paramref name="target"/> — the restriction nodes a delta on that defining relation makes structure-dirty.</summary>
        /// <param name="predicate">The restriction-defining predicate.</param>
        /// <param name="target">The set of dirty restriction subjects to add to.</param>
        private void CollectRestrictionSubjects(TermId predicate, HashSet<TermId> target)
        {
            foreach((TermId x, TermId _) in DeltaPairs(predicate))
            {
                target.Add(x);
            }
        }

        /// <summary>Fires the per-edge restriction variants for a new edge <c>(u,p,v)</c> under restriction <paramref name="x"/> — svf, avf, hv2, the zero-bound cardinality falsities, and the min-cardinality-1 membership — each field firing once per asserted value, mirroring the full body. Returns whether a falsity fired.</summary>
        /// <param name="x">The restriction node on <paramref name="p"/>.</param>
        /// <param name="p">The restricted property.</param>
        /// <param name="u">The new edge's subject.</param>
        /// <param name="v">The new edge's object.</param>
        /// <returns><c>true</c> when a restriction falsity made the closure inconsistent.</returns>
        private bool FireRestrictionEdgeVariants(TermId x, TermId p, TermId u, TermId v)
        {
            EncodedTriple onProperty = Fact(x, Terms.OnProperty, p);

            //cls-svf1 / cls-svf2.
            foreach(TermId someFiller in ObjectsOf(x, Terms.SomeValuesFrom))
            {
                EncodedTriple someValues = Fact(x, Terms.SomeValuesFrom, someFiller);
                if(someFiller == Terms.Thing)
                {
                    Add(u, Terms.Type, x, EntailmentRules.ClsSvf2, [onProperty, someValues, Fact(u, p, v)]);
                }
                else if(HasType(v, someFiller))
                {
                    Add(u, Terms.Type, x, EntailmentRules.ClsSvf1, [onProperty, someValues, Fact(u, p, v), Fact(v, Terms.Type, someFiller)]);
                }
            }

            //cls-avf: the new edge's object is typed when its subject is an
            //instance of the restriction.
            if(HasType(u, x))
            {
                foreach(TermId allFiller in ObjectsOf(x, Terms.AllValuesFrom))
                {
                    EncodedTriple allValues = Fact(x, Terms.AllValuesFrom, allFiller);
                    if(DatatypeOracle.LiteralOutsideDatatype(v, allFiller))
                    {
                        Inconsistent(EntailmentRules.DtNotType, [onProperty, allValues, Fact(u, Terms.Type, x), Fact(u, p, v)]);

                        return true;
                    }

                    Add(v, Terms.Type, allFiller, EntailmentRules.ClsAvf, [onProperty, allValues, Fact(u, Terms.Type, x), Fact(u, p, v)]);
                }
            }

            //cls-hv2.
            foreach(TermId value in ObjectsOf(x, Terms.HasValue))
            {
                if(v == value)
                {
                    Add(u, Terms.Type, x, EntailmentRules.ClsHv2, [onProperty, Fact(x, Terms.HasValue, value), Fact(u, p, value)]);
                }
            }

            //cls-maxc1 zero-bound: the new edge's subject is an instance.
            foreach(TermId bound in ObjectsOf(x, Terms.MaxCardinality))
            {
                if(Terms.ZeroBounds.Contains(bound) && HasType(u, x))
                {
                    Inconsistent(EntailmentRules.ClsMaxc1, [onProperty, Fact(x, Terms.MaxCardinality, bound), Fact(u, Terms.Type, x), Fact(u, p, v)]);

                    return true;
                }
            }

            //cls-maxqc1 zero-bound: the new edge's object counts. The rule
            //requires the owl:onClass triple; an absent onClass matches
            //nothing.
            foreach(TermId qualifiedBound in ObjectsOf(x, Terms.MaxQualifiedCardinality))
            {
                if(!Terms.ZeroBounds.Contains(qualifiedBound) || !HasType(u, x))
                {
                    continue;
                }

                EncodedTriple maxQualified = Fact(x, Terms.MaxQualifiedCardinality, qualifiedBound);
                foreach(TermId filler in ObjectsOf(x, Terms.OnClass))
                {
                    if(filler == Terms.Thing)
                    {
                        Inconsistent(EntailmentRules.ClsMaxqc1, [onProperty, maxQualified, Fact(u, Terms.Type, x), Fact(u, p, v)]);

                        return true;
                    }

                    if(HasType(v, filler))
                    {
                        Inconsistent(EntailmentRules.ClsMaxqc1, [onProperty, maxQualified, Fact(u, Terms.Type, x), Fact(u, p, v), Fact(v, Terms.Type, filler)]);

                        return true;
                    }
                }
            }

            //The min-cardinality-1 membership: the new edge is the
            //witnessing value, so its subject joins the restriction.
            foreach(TermId minBound in ObjectsOf(x, Terms.MinCardinality))
            {
                if(Terms.OneBounds.Contains(minBound))
                {
                    Add(u, Terms.Type, x, EntailmentRules.MinCardinalityOneMembership, [onProperty, Fact(x, Terms.MinCardinality, minBound), Fact(u, p, v)]);
                }
            }

            return false;
        }

        /// <summary>Fires the one-bound max variants for an instance <paramref name="u"/> of restriction <paramref name="x"/> whose object list under <paramref name="p"/> grew from <paramref name="start"/> — ordered tail-pairing that equates the new objects with the earlier ones.</summary>
        /// <param name="x">The restriction node on <paramref name="p"/>.</param>
        /// <param name="p">The restricted property.</param>
        /// <param name="u">The instance whose objects grew.</param>
        /// <param name="start">The pre-merge length of the object list.</param>
        private void FireMaxPairVariants(TermId x, TermId p, TermId u, int start)
        {
            EncodedTriple onProperty = Fact(x, Terms.OnProperty, p);
            List<TermId> objects = ObjectsOf(u, p);

            //cls-maxc2, once per asserted one-bound.
            foreach(TermId bound in ObjectsOf(x, Terms.MaxCardinality))
            {
                if(!Terms.OneBounds.Contains(bound))
                {
                    continue;
                }

                EncodedTriple maxCardinality = Fact(x, Terms.MaxCardinality, bound);
                for(int k = start; k < objects.Count; k++)
                {
                    for(int i = 0; i < k; i++)
                    {
                        if(objects[i] != objects[k])
                        {
                            Add(objects[i], Terms.SameAs, objects[k], EntailmentRules.ClsMaxc2, [onProperty, maxCardinality, Fact(u, Terms.Type, x), Fact(u, p, objects[i]), Fact(u, p, objects[k])]);
                        }
                    }
                }
            }

            //cls-maxqc4, once per asserted one-bound and onClass filler.
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
                    for(int k = start; k < objects.Count; k++)
                    {
                        if(filler != Terms.Thing && !HasType(objects[k], filler))
                        {
                            continue;
                        }

                        for(int i = 0; i < k; i++)
                        {
                            if(filler != Terms.Thing && !HasType(objects[i], filler))
                            {
                                continue;
                            }

                            if(objects[i] != objects[k])
                            {
                                Add(objects[i], Terms.SameAs, objects[k], EntailmentRules.ClsMaxqc4, [onProperty, maxQualified, Fact(u, Terms.Type, x), Fact(u, p, objects[i]), Fact(u, p, objects[k])]);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Fires the per-typing restriction variants for a newly typed instance <paramref name="u"/> of restriction <paramref name="x"/> on <paramref name="p"/> — avf, hv1, and the max cardinality rules over <paramref name="u"/>'s edges. Returns whether a falsity fired.</summary>
        /// <param name="x">The restriction node on <paramref name="p"/>.</param>
        /// <param name="p">The restricted property.</param>
        /// <param name="u">The newly typed instance.</param>
        /// <returns><c>true</c> when a restriction falsity made the closure inconsistent.</returns>
        private bool FireRestrictionTypingVariants(TermId x, TermId p, TermId u)
        {
            EncodedTriple onProperty = Fact(x, Terms.OnProperty, p);

            //cls-avf over all of u's current edges, once per asserted filler.
            foreach(TermId allFiller in ObjectsOf(x, Terms.AllValuesFrom))
            {
                EncodedTriple allValues = Fact(x, Terms.AllValuesFrom, allFiller);
                foreach(TermId v in ObjectsOf(u, p))
                {
                    if(DatatypeOracle.LiteralOutsideDatatype(v, allFiller))
                    {
                        Inconsistent(EntailmentRules.DtNotType, [onProperty, allValues, Fact(u, Terms.Type, x), Fact(u, p, v)]);

                        return true;
                    }

                    Add(v, Terms.Type, allFiller, EntailmentRules.ClsAvf, [onProperty, allValues, Fact(u, Terms.Type, x), Fact(u, p, v)]);
                }
            }

            //cls-hv1, once per asserted value.
            foreach(TermId value in ObjectsOf(x, Terms.HasValue))
            {
                Add(u, p, value, EntailmentRules.ClsHv1, [onProperty, Fact(x, Terms.HasValue, value), Fact(u, Terms.Type, x)]);
            }

            //cls-maxc1 zero-bound / cls-maxc2 one-bound, once per asserted bound.
            foreach(TermId bound in ObjectsOf(x, Terms.MaxCardinality))
            {
                EncodedTriple maxCardinality = Fact(x, Terms.MaxCardinality, bound);
                List<TermId> objects = ObjectsOf(u, p);
                if(Terms.ZeroBounds.Contains(bound) && objects.Count > 0)
                {
                    Inconsistent(EntailmentRules.ClsMaxc1, [onProperty, maxCardinality, Fact(u, Terms.Type, x), Fact(u, p, objects[0])]);

                    return true;
                }

                if(Terms.OneBounds.Contains(bound))
                {
                    EquateAllPairs(objects, EntailmentRules.ClsMaxc2, u, p, [onProperty, maxCardinality, Fact(u, Terms.Type, x)]);
                }
            }

            //cls-maxqc1 zero-bound / cls-maxqc4 one-bound, per asserted
            //bound and onClass filler. The rules require the owl:onClass
            //triple; an absent onClass matches nothing.
            foreach(TermId qualifiedBound in ObjectsOf(x, Terms.MaxQualifiedCardinality))
            {
                EncodedTriple maxQualified = Fact(x, Terms.MaxQualifiedCardinality, qualifiedBound);
                foreach(TermId filler in ObjectsOf(x, Terms.OnClass))
                {

                    if(Terms.ZeroBounds.Contains(qualifiedBound))
                    {
                        foreach(TermId y in ObjectsOf(u, p))
                        {
                            if(filler == Terms.Thing)
                            {
                                Inconsistent(EntailmentRules.ClsMaxqc1, [onProperty, maxQualified, Fact(u, Terms.Type, x), Fact(u, p, y)]);

                                return true;
                            }

                            if(HasType(y, filler))
                            {
                                Inconsistent(EntailmentRules.ClsMaxqc1, [onProperty, maxQualified, Fact(u, Terms.Type, x), Fact(u, p, y), Fact(y, Terms.Type, filler)]);

                                return true;
                            }
                        }
                    }

                    if(Terms.OneBounds.Contains(qualifiedBound))
                    {
                        List<TermId> qualified = [];
                        foreach(TermId y in ObjectsOf(u, p))
                        {
                            if(filler == Terms.Thing || HasType(y, filler))
                            {
                                qualified.Add(y);
                            }
                        }

                        EquateAllPairs(qualified, EntailmentRules.ClsMaxqc4, u, p, [onProperty, maxQualified, Fact(u, Terms.Type, x)]);
                    }
                }
            }

            return false;
        }

        /// <summary>Fires the filler-side restriction variants for a newly typed instance <paramref name="v"/> of a filler class — svf1 through the someValuesFrom reverse index and the qualified max rules through the onClass reverse index. Every restriction asserting the class as a filler fires: the filler triple reached through the reverse index is itself the asserted premise, so no canonical mirror gates it. Returns whether a falsity fired.</summary>
        /// <param name="v">The newly typed instance that may be a filler.</param>
        /// <param name="filler">The class it gained — a candidate restriction filler.</param>
        /// <param name="dirtyRestrictions">The restriction nodes already re-fired in full this round, skipped here.</param>
        /// <returns><c>true</c> when a restriction falsity made the closure inconsistent.</returns>
        private bool FireRestrictionFillerVariants(TermId v, TermId filler, HashSet<TermId> dirtyRestrictions)
        {
            //svf1: v now has the filler, so any u reaching v over the
            //restriction's property becomes an instance of the restriction.
            foreach(TermId x in SubjectsOf(filler, Terms.SomeValuesFrom))
            {
                if(dirtyRestrictions.Contains(x))
                {
                    continue;
                }

                EncodedTriple someValues = Fact(x, Terms.SomeValuesFrom, filler);
                foreach(TermId p in ObjectsOf(x, Terms.OnProperty))
                {
                    EncodedTriple onProperty = Fact(x, Terms.OnProperty, p);
                    foreach(TermId u in SubjectsOf(v, p))
                    {
                        Add(u, Terms.Type, x, EntailmentRules.ClsSvf1, [onProperty, someValues, Fact(u, p, v), Fact(v, Terms.Type, filler)]);
                    }
                }
            }

            //Qualified max: v now has the filler, so it counts toward the
            //onClass-qualified cardinality of any restriction reaching it,
            //once per asserted bound.
            foreach(TermId x in SubjectsOf(filler, Terms.OnClass))
            {
                if(dirtyRestrictions.Contains(x))
                {
                    continue;
                }

                foreach(TermId qualifiedBound in ObjectsOf(x, Terms.MaxQualifiedCardinality))
                {
                    EncodedTriple maxQualified = Fact(x, Terms.MaxQualifiedCardinality, qualifiedBound);
                    foreach(TermId p in ObjectsOf(x, Terms.OnProperty))
                    {
                        EncodedTriple onProperty = Fact(x, Terms.OnProperty, p);
                        foreach(TermId u in SubjectsOf(v, p))
                        {
                            if(!HasType(u, x))
                            {
                                continue;
                            }

                            if(Terms.ZeroBounds.Contains(qualifiedBound))
                            {
                                Inconsistent(EntailmentRules.ClsMaxqc1, [onProperty, maxQualified, Fact(u, Terms.Type, x), Fact(u, p, v), Fact(v, Terms.Type, filler)]);

                                return true;
                            }

                            if(Terms.OneBounds.Contains(qualifiedBound))
                            {
                                List<TermId> qualified = [];
                                foreach(TermId y in ObjectsOf(u, p))
                                {
                                    if(filler == Terms.Thing || HasType(y, filler))
                                    {
                                        qualified.Add(y);
                                    }
                                }

                                EquateAllPairs(qualified, EntailmentRules.ClsMaxqc4, u, p, [onProperty, maxQualified, Fact(u, Terms.Type, x)]);
                            }
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>Fires the cax-* rules over the round's delta, in naive family order, returning between rules on falsity.</summary>
        private void FireClassAxiomsDelta()
        {
            //cax-sco.
            foreach((TermId c1, TermId c2) in DeltaPairs(Terms.SubClassOf))
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

            foreach((TermId x, TermId c1) in DeltaPairs(Terms.Type))
            {
                foreach(TermId c2 in ObjectsOf(c1, Terms.SubClassOf))
                {
                    Add(x, Terms.Type, c2, EntailmentRules.CaxSco, [Fact(c1, Terms.SubClassOf, c2), Fact(x, Terms.Type, c1)]);
                }
            }

            //cax-eqc1 / cax-eqc2.
            foreach((TermId c1, TermId c2) in DeltaPairs(Terms.EquivalentClass))
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

            foreach((TermId x, TermId c) in DeltaPairs(Terms.Type))
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

            //cax-dw.
            foreach((TermId c1, TermId c2) in DeltaPairs(Terms.DisjointWith))
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

            foreach((TermId x, TermId c) in DeltaPairs(Terms.Type))
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

            //cax-adc: conservative full per-node re-check on its triggers.
            if(FireAllDisjointClassesDelta())
            {
                return;
            }
        }

        /// <summary>Fires cax-adc over the round's delta by a conservative full per-node re-check — newly typed <c>owl:AllDisjointClasses</c> nodes always, and all nodes when list structure or a member class's typing grew. Returns whether a falsity fired.</summary>
        /// <returns><c>true</c> when a disjoint pair shared an instance and made the closure inconsistent.</returns>
        private bool FireAllDisjointClassesDelta()
        {
            bool newTyping = HasDeltaInstances(Terms.AllDisjointClasses);
            bool memberTypingGrew = false;
            if(!ListStructureDirty && !newTyping)
            {
                AdcMemberClasses ??= BuildAdcMemberClasses();
                foreach((TermId _, TermId c) in DeltaPairs(Terms.Type))
                {
                    if(AdcMemberClasses.Contains(c))
                    {
                        memberTypingGrew = true;

                        break;
                    }
                }
            }

            if(ListStructureDirty || newTyping)
            {
                AdcMemberClasses = BuildAdcMemberClasses();
            }

            if(ListStructureDirty || memberTypingGrew)
            {
                if(InstancesOf.TryGetValue(Terms.AllDisjointClasses, out List<TermId>? nodes))
                {
                    foreach(TermId node in nodes)
                    {
                        if(FireAllDisjointClassesNode(node))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            foreach(TermId node in DeltaInstancesTail(Terms.AllDisjointClasses))
            {
                if(FireAllDisjointClassesNode(node))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Builds the set of classes named in any <c>owl:AllDisjointClasses</c> members list, every asserted list included — the conservative trigger for cax-adc.</summary>
        /// <returns>The member classes.</returns>
        private HashSet<TermId> BuildAdcMemberClasses()
        {
            HashSet<TermId> classes = [];
            if(InstancesOf.TryGetValue(Terms.AllDisjointClasses, out List<TermId>? nodes))
            {
                foreach(TermId node in nodes)
                {
                    foreach(TermId head in ObjectsOf(node, Terms.Members))
                    {
                        if(ListOf(head) is List<TermId> members)
                        {
                            foreach(TermId member in members)
                            {
                                classes.Add(member);
                            }
                        }
                    }
                }
            }

            return classes;
        }

        /// <summary>Fires the scm-* rules over the round's delta, in naive family order.</summary>
        private void FireSchemaDelta()
        {
            //scm-cls: every newly declared class gets its four schema triples.
            foreach(TermId c in DeltaInstancesTail(Terms.ClassTerm))
            {
                EncodedTriple declaration = Fact(c, Terms.Type, Terms.ClassTerm);
                Add(c, Terms.SubClassOf, c, EntailmentRules.ScmCls, [declaration]);
                Add(c, Terms.EquivalentClass, c, EntailmentRules.ScmCls, [declaration]);
                Add(c, Terms.SubClassOf, Terms.Thing, EntailmentRules.ScmCls, [declaration]);
                Add(Terms.Nothing, Terms.SubClassOf, c, EntailmentRules.ScmCls, [declaration]);
            }

            //scm-op / scm-dp over the newly declared properties.
            FireSelfSubsumption(DeltaInstancesTail(Terms.ObjectPropertyTerm), Terms.ObjectPropertyTerm, EntailmentRules.ScmOp);
            FireSelfSubsumption(DeltaInstancesTail(Terms.DatatypePropertyTerm), Terms.DatatypePropertyTerm, EntailmentRules.ScmDp);

            //scm-sco / scm-eqc2: a new subClassOf edge composes with the
            //existing hierarchy on both sides, and closes an equivalence when
            //the reverse edge already holds.
            foreach((TermId c1, TermId c2) in DeltaPairs(Terms.SubClassOf))
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

            //scm-eqc1.
            foreach((TermId c1, TermId c2) in DeltaPairs(Terms.EquivalentClass))
            {
                EncodedTriple equivalent = Fact(c1, Terms.EquivalentClass, c2);
                Add(c2, Terms.EquivalentClass, c1, EntailmentRules.ScmEqc1, [equivalent]);
                Add(c1, Terms.SubClassOf, c2, EntailmentRules.ScmEqc1, [equivalent]);
                Add(c2, Terms.SubClassOf, c1, EntailmentRules.ScmEqc1, [equivalent]);
            }

            //scm-spo / scm-eqp2.
            foreach((TermId p1, TermId p2) in DeltaPairs(Terms.SubPropertyOf))
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

            //scm-eqp1.
            foreach((TermId p1, TermId p2) in DeltaPairs(Terms.EquivalentProperty))
            {
                EncodedTriple equivalent = Fact(p1, Terms.EquivalentProperty, p2);
                Add(p2, Terms.EquivalentProperty, p1, EntailmentRules.ScmEqp1, [equivalent]);
                Add(p1, Terms.SubPropertyOf, p2, EntailmentRules.ScmEqp1, [equivalent]);
                Add(p2, Terms.SubPropertyOf, p1, EntailmentRules.ScmEqp1, [equivalent]);
            }

            //scm-dom1.
            foreach((TermId p, TermId c1) in DeltaPairs(Terms.Domain))
            {
                EncodedTriple domain = Fact(p, Terms.Domain, c1);
                foreach(TermId c2 in ObjectsOf(c1, Terms.SubClassOf))
                {
                    Add(p, Terms.Domain, c2, EntailmentRules.ScmDom1, [domain, Fact(c1, Terms.SubClassOf, c2)]);
                }
            }

            foreach((TermId c1, TermId c2) in DeltaPairs(Terms.SubClassOf))
            {
                foreach(TermId p in SubjectsOf(c1, Terms.Domain))
                {
                    Add(p, Terms.Domain, c2, EntailmentRules.ScmDom1, [Fact(p, Terms.Domain, c1), Fact(c1, Terms.SubClassOf, c2)]);
                }
            }

            //scm-dom2 + scm-rng2.
            foreach((TermId p1, TermId p2) in DeltaPairs(Terms.SubPropertyOf))
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

            foreach((TermId p2, TermId c) in DeltaPairs(Terms.Domain))
            {
                foreach(TermId p1 in SubjectsOf(p2, Terms.SubPropertyOf))
                {
                    Add(p1, Terms.Domain, c, EntailmentRules.ScmDom2, [Fact(p1, Terms.SubPropertyOf, p2), Fact(p2, Terms.Domain, c)]);
                }
            }

            foreach((TermId p2, TermId c) in DeltaPairs(Terms.Range))
            {
                foreach(TermId p1 in SubjectsOf(p2, Terms.SubPropertyOf))
                {
                    Add(p1, Terms.Range, c, EntailmentRules.ScmRng2, [Fact(p1, Terms.SubPropertyOf, p2), Fact(p2, Terms.Range, c)]);
                }
            }

            //scm-rng1.
            foreach((TermId p, TermId c1) in DeltaPairs(Terms.Range))
            {
                EncodedTriple range = Fact(p, Terms.Range, c1);
                foreach(TermId c2 in ObjectsOf(c1, Terms.SubClassOf))
                {
                    Add(p, Terms.Range, c2, EntailmentRules.ScmRng1, [range, Fact(c1, Terms.SubClassOf, c2)]);
                }
            }

            foreach((TermId c1, TermId c2) in DeltaPairs(Terms.SubClassOf))
            {
                foreach(TermId p in SubjectsOf(c1, Terms.Range))
                {
                    Add(p, Terms.Range, c2, EntailmentRules.ScmRng1, [Fact(p, Terms.Range, c1), Fact(c1, Terms.SubClassOf, c2)]);
                }
            }

            //scm-int / scm-uni.
            foreach((TermId c, TermId listHead) in Pairs(Terms.IntersectionOf))
            {
                if(ListStructureDirty || DeltaAxiomTouched(c, listHead, Terms.IntersectionOf))
                {
                    FireSchemaIntersectionAxiom(c, listHead);
                }
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.UnionOf))
            {
                if(ListStructureDirty || DeltaAxiomTouched(c, listHead, Terms.UnionOf))
                {
                    FireSchemaUnionAxiom(c, listHead);
                }
            }

            //The restriction comparisons re-fire in full when any of their
            //premise kinds grew this round — a defining restriction triple,
            //the filler hierarchy, or the property hierarchy; the adds
            //dedup.
            if(DeltaStartByPredicate.ContainsKey(Terms.OnProperty)
                || DeltaStartByPredicate.ContainsKey(Terms.SomeValuesFrom)
                || DeltaStartByPredicate.ContainsKey(Terms.AllValuesFrom)
                || DeltaStartByPredicate.ContainsKey(Terms.HasValue)
                || DeltaStartByPredicate.ContainsKey(Terms.SubClassOf)
                || DeltaStartByPredicate.ContainsKey(Terms.SubPropertyOf))
            {
                FireRestrictionComparisons();
            }

            //The inverse-characteristic transfer over the round's delta: a
            //new inverseOf pair reads both ends' present characteristics,
            //and a new characteristic typing reads its inverseOf partners.
            foreach((TermId p1, TermId p2) in DeltaPairs(Terms.InverseOf))
            {
                FireInverseCharacteristicPair(p1, p2);
            }

            foreach((TermId p, TermId characteristic) in DeltaPairs(Terms.Type))
            {
                FireInverseCharacteristicTyping(p, characteristic);
            }

            //The singleton-enumeration characteristics: a new range or
            //domain edge reads the confining class's enumerations, and
            //enumeration or list growth re-fires the rule in full — the
            //adds dedup.
            foreach((TermId p, TermId c) in DeltaPairs(Terms.Range))
            {
                FireSingletonEnumerationEdge(p, c, Terms.Range, Terms.FunctionalProperty);
            }

            foreach((TermId p, TermId c) in DeltaPairs(Terms.Domain))
            {
                FireSingletonEnumerationEdge(p, c, Terms.Domain, Terms.InverseFunctionalProperty);
            }

            if(ListStructureDirty || DeltaStartByPredicate.ContainsKey(Terms.OneOf))
            {
                FireSingletonEnumerationCharacteristics();
            }

            //The member-subset comparisons re-fire in full when an
            //enumeration, a union, or list structure grew this round.
            if(ListStructureDirty || DeltaStartByPredicate.ContainsKey(Terms.OneOf) || DeltaStartByPredicate.ContainsKey(Terms.UnionOf))
            {
                FireEnumerationComparisons();
            }
        }

        /// <summary>Whether a (subject, object) axiom pair was itself merged this round under the predicate — the trigger that re-fires a reified construct's full body.</summary>
        /// <param name="subject">The axiom's subject.</param>
        /// <param name="object">The axiom's object — the list head.</param>
        /// <param name="predicate">The axiom predicate.</param>
        /// <returns><c>true</c> when the axiom triple is in this round's delta.</returns>
        private bool DeltaAxiomTouched(TermId subject, TermId @object, TermId predicate)
        {
            return MergedThisRoundSet.Contains(Fact(subject, predicate, @object));
        }
    }
}
