using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Owl.Rl;

public static partial class OwlRlClosure
{
    internal sealed partial class ClosureContext
    {
        /// <summary>
        /// How this closure reads the comprehension conditions of the OWL 2
        /// RDF-Based Semantics. Under
        /// <see cref="OwlComprehension.InformativeConditions"/> the
        /// comprehension completion family fires over the expression
        /// structure the conditions grant; under
        /// <see cref="OwlComprehension.None"/> — every consistency check,
        /// the maintained engine, and every production closure — the family
        /// stays dark and the closure is the normative rule set alone.
        /// </summary>
        private OwlComprehension Comprehension { get; }

        /// <summary>
        /// The restriction nodes on each existential witness's derivation
        /// chain — the witness rule's cycle bound. Only minted witnesses
        /// carry an entry; every other term has an empty ancestry and is
        /// never refused. A witness's ancestry is its parent's plus the
        /// restriction that minted it, which is stable across re-fires, so
        /// recording is an idempotent overwrite. Identity is the
        /// restriction node's own term — a sameAs-aliased copy of a
        /// visited restriction reads as new, which can only refuse less,
        /// never conclude more.
        /// </summary>
        private Dictionary<TermId, HashSet<TermId>> WitnessAncestry { get; } = [];

        /// <summary>
        /// Whether a fire pass has observed an admitted datatype-alias
        /// sameAs pair. Once set it stays set, and the family re-fires on
        /// every growing round: the retype rule's second dimension is the
        /// closure's object-term roster, which any new triple can extend,
        /// so an alias-carrying graph keeps the family live until the
        /// fixpoint. Alias pairs exist only in alias-carrying graphs, so
        /// the always-refire tail is confined to exactly the graphs that
        /// need it.
        /// </summary>
        private bool DatatypeAliasPairSeen { get; set; }

        /// <summary>
        /// Fires the comprehension completion family once over the current
        /// indexes: the disjoint-range emptiness rules, the excluded-middle
        /// and value-dichotomy coverings of <c>owl:Thing</c>, the
        /// functional max-1 universal, the empty enumeration under
        /// <c>owl:Nothing</c>, the intersection range completion, the De
        /// Morgan subset comparisons, the exact cardinality shorthand, the
        /// bounded existential witness for some-values-from members, the
        /// type-domain universal subsumption, the shared has-value
        /// property collapse, the datatype-alias literal retype, and the
        /// fibre-cardinality count-certificate propagation with its
        /// anchored read-back. The disjoint-range clash is the family's
        /// one falsity, so it fires first and the family returns on it;
        /// every other rule derives no falsity.
        /// </summary>
        private void FireComprehension()
        {
            FireDisjointRangeEmptiness();
            if(InconsistencyRule is not null)
            {
                return;
            }

            foreach((TermId u, TermId listHead) in Pairs(Terms.UnionOf))
            {
                FireUnionCoverings(u, listHead);
            }

            foreach((TermId r, TermId bound) in Pairs(Terms.MaxCardinality))
            {
                FireFunctionalMaxOneUniversal(r, bound);
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.OneOf))
            {
                FireEmptyEnumeration(c, listHead);
            }

            foreach((TermId i, TermId listHead) in Pairs(Terms.IntersectionOf))
            {
                FireIntersectionRangeCompletion(i, listHead);
                FireDeMorganComparisons(i, listHead);
                FireCardinalityShorthand(i, listHead);
            }

            foreach((TermId r, TermId c) in Pairs(Terms.SomeValuesFrom))
            {
                FireExistentialWitness(r, c);
            }

            foreach(TermId y in ObjectsOf(Terms.Type, Terms.Domain))
            {
                FireTypeDomainSubsumption(y);
            }

            foreach((TermId z, TermId v) in Pairs(Terms.HasValue))
            {
                FireSharedHasValueCollapse(z, v);
            }

            foreach((TermId x, TermId y) in Pairs(Terms.SameAs))
            {
                FireDatatypeAliasRetype(x, y);
            }

            FireFibreCardinalityCertificates();
        }

        /// <summary>
        /// Whether any premise kind of the comprehension family grew this
        /// round — the conservative semi-naive trigger: list structure, a
        /// constructor or constraint predicate, a range or domain edge, a
        /// has-value edge, a sameAs edge, an equivalent-class or inverse
        /// edge, a new functional-property typing,
        /// a new class or property typing, a statement of a ranged
        /// property, or a new instance of a some-values-from restriction
        /// re-fires the whole family, and the adds dedup; a graph holding
        /// an admitted datatype-alias pair re-fires it on every growing
        /// round. The instance checks are what carry the late-arrival
        /// rules: a typing or a statement reaches its rule through the
        /// other families on a later round than the rule's own structure.
        /// The two roster checks carry the widened universal rules: a
        /// graph declaring a domain of <c>rdf:type</c> re-fires on class
        /// evidence growth, and a graph holding a multi-ranged property
        /// re-fires on a new predicate's arrival.
        /// </summary>
        /// <returns><c>true</c> when the family must re-fire this round.</returns>
        private bool ComprehensionPremisesGrew()
        {
            return ListStructureDirty
                || DatatypeAliasPairSeen
                || DeltaStartByPredicate.ContainsKey(Terms.SameAs)
                || DeltaStartByPredicate.ContainsKey(Terms.UnionOf)
                || DeltaStartByPredicate.ContainsKey(Terms.IntersectionOf)
                || DeltaStartByPredicate.ContainsKey(Terms.OneOf)
                || DeltaStartByPredicate.ContainsKey(Terms.ComplementOf)
                || DeltaStartByPredicate.ContainsKey(Terms.OnProperty)
                || DeltaStartByPredicate.ContainsKey(Terms.SomeValuesFrom)
                || DeltaStartByPredicate.ContainsKey(Terms.MaxCardinality)
                || DeltaStartByPredicate.ContainsKey(Terms.MinCardinality)
                || DeltaStartByPredicate.ContainsKey(Terms.Cardinality)
                || DeltaStartByPredicate.ContainsKey(Terms.Range)
                || DeltaStartByPredicate.ContainsKey(Terms.Domain)
                || DeltaStartByPredicate.ContainsKey(Terms.HasValue)
                || DeltaStartByPredicate.ContainsKey(Terms.EquivalentClass)
                || DeltaStartByPredicate.ContainsKey(Terms.InverseOf)
                || HasDeltaInstances(Terms.FunctionalProperty)
                || HasDeltaInstances(Terms.RdfsClass)
                || HasDeltaInstances(Terms.RdfProperty)
                || HasDeltaInstances(Terms.ObjectPropertyTerm)
                || HasDeltaInstances(Terms.DatatypePropertyTerm)
                || SomeValuesFromInstancesGrew()
                || RangedPropertyStatementsGrew()
                || TypeDomainRosterGrew()
                || VacuousSubpropertyRosterGrew();
        }

        /// <summary>
        /// Whether any some-values-from restriction node gained an instance
        /// this round — the witness rule's member-typing trigger, read per
        /// restriction subject so unrelated typings never re-fire the
        /// family.
        /// </summary>
        /// <returns><c>true</c> when a restriction's instance list grew this round.</returns>
        private bool SomeValuesFromInstancesGrew()
        {
            foreach((TermId r, TermId _) in Pairs(Terms.SomeValuesFrom))
            {
                if(HasDeltaInstances(r))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether any ranged property gained a statement this round — the
        /// disjoint-range rules' late-edge trigger, read per range subject
        /// so unrelated statements never re-fire the family.
        /// </summary>
        /// <returns><c>true</c> when a ranged property's statement list grew this round.</returns>
        private bool RangedPropertyStatementsGrew()
        {
            foreach((TermId p, TermId _) in Pairs(Terms.Range))
            {
                if(DeltaStartByPredicate.ContainsKey(p))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the type-domain rule's class roster could have grown
        /// this round — a typing or subclass delta while a domain of
        /// <c>rdf:type</c> is declared. Gated on the declaration so no
        /// other graph ever pays the re-fire; domain and range deltas
        /// already re-fire the family through their own keys.
        /// </summary>
        /// <returns><c>true</c> when the roster may hold new classes this round.</returns>
        private bool TypeDomainRosterGrew()
        {
            if(ObjectsOf(Terms.Type, Terms.Domain).Count == 0)
            {
                return false;
            }

            return DeltaStartByPredicate.ContainsKey(Terms.Type) || DeltaStartByPredicate.ContainsKey(Terms.SubClassOf);
        }

        /// <summary>
        /// Whether the vacuous-subproperty rule's predicate roster gained a
        /// member this round — a delta list that began at index zero is a
        /// predicate whose first statement arrived this round. Gated on a
        /// multi-ranged property's possibility (two range pairs) since the
        /// rule can do nothing without a disjoint range pair.
        /// </summary>
        /// <returns><c>true</c> when a new predicate arrived this round.</returns>
        private bool VacuousSubpropertyRosterGrew()
        {
            if(Pairs(Terms.Range).Count < 2)
            {
                return false;
            }

            foreach((TermId _, int start) in DeltaStartByPredicate)
            {
                if(start == 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Fires the two <c>owl:Thing</c> coverings for one union axiom: a
        /// member set holding a class and a complement of that class covers
        /// everything by excluded middle, and one holding a
        /// some-values-from-<c>owl:Thing</c> restriction and a max-0
        /// restriction on the same property covers everything because every
        /// individual either has a value for the property or has none.
        /// </summary>
        /// <param name="u">The union class.</param>
        /// <param name="listHead">The member list's head node.</param>
        private void FireUnionCoverings(TermId u, TermId listHead)
        {
            if(ListOf(listHead) is not List<TermId> members || members.Count == 0)
            {
                return;
            }

            EncodedTriple union = Fact(u, Terms.UnionOf, listHead);
            HashSet<TermId> memberSet = [.. members];

            foreach(TermId member in memberSet)
            {
                foreach(TermId complemented in ObjectsOf(member, Terms.ComplementOf))
                {
                    if(memberSet.Contains(complemented))
                    {
                        Add(Terms.Thing, Terms.SubClassOf, u, EntailmentRules.UnionExcludedMiddle, [union, Fact(member, Terms.ComplementOf, complemented)]);
                    }
                }
            }

            foreach(TermId some in memberSet)
            {
                if(!ObjectsOf(some, Terms.SomeValuesFrom).Contains(Terms.Thing))
                {
                    continue;
                }

                foreach(TermId p in ObjectsOf(some, Terms.OnProperty))
                {
                    foreach(TermId capped in memberSet)
                    {
                        if(capped == some || !ObjectsOf(capped, Terms.OnProperty).Contains(p))
                        {
                            continue;
                        }

                        foreach(TermId bound in ObjectsOf(capped, Terms.MaxCardinality))
                        {
                            if(!Terms.ZeroBounds.Contains(bound))
                            {
                                continue;
                            }

                            Add(
                                Terms.Thing,
                                Terms.SubClassOf,
                                u,
                                EntailmentRules.UnionValueDichotomy,
                                [
                                    union,
                                    Fact(some, Terms.OnProperty, p),
                                    Fact(some, Terms.SomeValuesFrom, Terms.Thing),
                                    Fact(capped, Terms.OnProperty, p),
                                    Fact(capped, Terms.MaxCardinality, bound),
                                ]);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Fires the functional max-1 universal for one max-cardinality
        /// edge: a functional property confines every individual to at most
        /// one value, so <c>owl:Thing</c> subsumes under a max-1
        /// restriction on it.
        /// </summary>
        /// <param name="r">The restriction node.</param>
        /// <param name="bound">The asserted max-cardinality bound.</param>
        private void FireFunctionalMaxOneUniversal(TermId r, TermId bound)
        {
            if(!Terms.OneBounds.Contains(bound))
            {
                return;
            }

            foreach(TermId p in ObjectsOf(r, Terms.OnProperty))
            {
                if(!HasType(p, Terms.FunctionalProperty))
                {
                    continue;
                }

                Add(
                    Terms.Thing,
                    Terms.SubClassOf,
                    r,
                    EntailmentRules.FunctionalMaxOneUniversal,
                    [Fact(r, Terms.OnProperty, p), Fact(r, Terms.MaxCardinality, bound), Fact(p, Terms.Type, Terms.FunctionalProperty)]);
            }
        }

        /// <summary>
        /// Fires the empty-enumeration rule for one <c>owl:oneOf</c> axiom:
        /// an enumeration over the empty list denotes the empty class, so
        /// it subsumes under <c>owl:Nothing</c>; the equivalence composes
        /// through scm-cls and scm-eqc2 when the node is a declared class.
        /// </summary>
        /// <param name="c">The enumerated class.</param>
        /// <param name="listHead">The member list's head node.</param>
        private void FireEmptyEnumeration(TermId c, TermId listHead)
        {
            if(ListOf(listHead) is List<TermId> members && members.Count == 0)
            {
                Add(c, Terms.SubClassOf, Terms.Nothing, EntailmentRules.EmptyEnumerationNothing, [Fact(c, Terms.OneOf, listHead)]);
            }
        }

        /// <summary>
        /// Fires the intersection range completion for one
        /// <c>owl:intersectionOf</c> axiom: a property ranged by every
        /// member is ranged by the intersection, whose extension is the
        /// members' common extension under the iff reading.
        /// </summary>
        /// <param name="i">The intersection class.</param>
        /// <param name="listHead">The member list's head node.</param>
        private void FireIntersectionRangeCompletion(TermId i, TermId listHead)
        {
            if(ListOf(listHead) is not List<TermId> members || members.Count == 0)
            {
                return;
            }

            foreach(TermId p in SubjectsOf(members[0], Terms.Range))
            {
                bool rangesAll = true;
                foreach(TermId member in members)
                {
                    if(!ObjectsOf(p, Terms.Range).Contains(member))
                    {
                        rangesAll = false;

                        break;
                    }
                }

                if(!rangesAll)
                {
                    continue;
                }

                List<EncodedTriple> premises = [Fact(i, Terms.IntersectionOf, listHead)];
                foreach(TermId member in members)
                {
                    premises.Add(Fact(p, Terms.Range, member));
                }

                Add(p, Terms.Range, i, EntailmentRules.IntersectionRangeCompletion, [.. premises]);
            }
        }

        /// <summary>
        /// Fires the De Morgan subset comparisons for one intersection of
        /// complements: with <c>C</c> the union of the members' complemented
        /// classes, the intersection subsumes under a complement of any
        /// union whose disjunct set is contained in <c>C</c>, and that
        /// complement subsumes under the intersection when <c>C</c> is
        /// contained in the disjunct set; equal sets compose the
        /// equivalence through scm-eqc2. Every member must carry a
        /// complement — the complemented classes of one member share one
        /// extension, so any of them stands for it in the sets.
        /// </summary>
        /// <param name="i">The intersection class.</param>
        /// <param name="listHead">The member list's head node.</param>
        private void FireDeMorganComparisons(TermId i, TermId listHead)
        {
            //The empty intersection is a reading the class rules deliberately
            //never commit to, so the comparison demands members.
            if(ListOf(listHead) is not List<TermId> members || members.Count == 0)
            {
                return;
            }

            EncodedTriple intersection = Fact(i, Terms.IntersectionOf, listHead);
            HashSet<TermId> complemented = [];
            List<EncodedTriple> witnesses = [];
            foreach(TermId member in members)
            {
                List<TermId> memberComplements = ObjectsOf(member, Terms.ComplementOf);
                if(memberComplements.Count == 0)
                {
                    return;
                }

                foreach(TermId k in memberComplements)
                {
                    complemented.Add(k);
                }

                witnesses.Add(Fact(member, Terms.ComplementOf, memberComplements[0]));
            }

            foreach((TermId cu, TermId u2) in Pairs(Terms.ComplementOf))
            {
                foreach(TermId unionHead in ObjectsOf(u2, Terms.UnionOf))
                {
                    if(ListOf(unionHead) is not List<TermId> disjuncts)
                    {
                        continue;
                    }

                    HashSet<TermId> disjunctSet = [.. disjuncts];
                    EncodedTriple complement = Fact(cu, Terms.ComplementOf, u2);
                    EncodedTriple union = Fact(u2, Terms.UnionOf, unionHead);

                    if(disjunctSet.IsSubsetOf(complemented))
                    {
                        Add(i, Terms.SubClassOf, cu, EntailmentRules.DeMorganSubset, [intersection, complement, union, .. witnesses]);
                    }

                    if(complemented.IsSubsetOf(disjunctSet))
                    {
                        Add(cu, Terms.SubClassOf, i, EntailmentRules.DeMorganSubset, [intersection, complement, union, .. witnesses]);
                    }
                }
            }
        }

        /// <summary>
        /// Fires the exact cardinality shorthand for one intersection of a
        /// same-bound min- and max-cardinality pair on one property: the
        /// pair's intersection is exactly the exact-cardinality extension,
        /// so the class also carries the intersection over any singleton
        /// list whose member is the matching exact-cardinality restriction.
        /// The bounds match by encoded literal identity.
        /// </summary>
        /// <param name="c">The intersection class.</param>
        /// <param name="listHead">The member list's head node.</param>
        private void FireCardinalityShorthand(TermId c, TermId listHead)
        {
            if(ListOf(listHead) is not List<TermId> members)
            {
                return;
            }

            HashSet<TermId> memberSet = [.. members];
            if(memberSet.Count != 2)
            {
                return;
            }

            List<TermId> pair = [.. memberSet];
            FireCardinalityShorthandAssignment(c, listHead, pair[0], pair[1]);
            FireCardinalityShorthandAssignment(c, listHead, pair[1], pair[0]);
        }

        /// <summary>
        /// Fires one assignment of the exact cardinality shorthand: with
        /// <paramref name="capped"/> read as the max-cardinality member and
        /// <paramref name="floored"/> as the min-cardinality member on one
        /// property and bound, every singleton list whose member is an
        /// exact-cardinality restriction on the same property and bound
        /// concludes the intersection statement.
        /// </summary>
        /// <param name="c">The intersection class.</param>
        /// <param name="listHead">The premise list's head node.</param>
        /// <param name="capped">The member read as the max-cardinality restriction.</param>
        /// <param name="floored">The member read as the min-cardinality restriction.</param>
        private void FireCardinalityShorthandAssignment(TermId c, TermId listHead, TermId capped, TermId floored)
        {
            //The cardinality conditions are stated over nonnegative integers,
            //so the shared bound must be a recognized bound encoding; wider
            //integer recognition belongs to the datatype-oracle seam. The
            //property and bound positions pair per asserted value; the
            //singleton cell's rdf:first/rdf:rest read canonically, matching
            //the list walker's cell read.
            foreach(TermId p in ObjectsOf(capped, Terms.OnProperty))
            {
                foreach(TermId bound in ObjectsOf(capped, Terms.MaxCardinality))
                {
                    if((!Terms.ZeroBounds.Contains(bound) && !Terms.OneBounds.Contains(bound))
                        || !ObjectsOf(floored, Terms.OnProperty).Contains(p)
                        || !ObjectsOf(floored, Terms.MinCardinality).Contains(bound))
                    {
                        continue;
                    }

                    foreach((TermId cell, TermId member) in Pairs(Terms.First))
                    {
                        if(MinimumObjectOf(cell, Terms.Rest) != Terms.Nil
                            || MinimumObjectOf(cell, Terms.First) != member
                            || !ObjectsOf(member, Terms.OnProperty).Contains(p)
                            || !ObjectsOf(member, Terms.Cardinality).Contains(bound))
                        {
                            continue;
                        }

                        Add(
                            c,
                            Terms.IntersectionOf,
                            cell,
                            EntailmentRules.CardinalityShorthand,
                            [
                                Fact(c, Terms.IntersectionOf, listHead),
                                Fact(capped, Terms.OnProperty, p),
                                Fact(capped, Terms.MaxCardinality, bound),
                                Fact(floored, Terms.OnProperty, p),
                                Fact(floored, Terms.MinCardinality, bound),
                                Fact(cell, Terms.First, member),
                                Fact(cell, Terms.Rest, Terms.Nil),
                                Fact(member, Terms.OnProperty, p),
                                Fact(member, Terms.Cardinality, bound),
                            ]);
                    }
                }
            }
        }

        /// <summary>
        /// Fires the bounded existential witness for one
        /// <c>owl:someValuesFrom</c> edge: every member of the restriction
        /// has a value for the property inside the filler, so a fresh
        /// deterministic witness carries the edge and the typing. Each
        /// (filler, property) pair on a restriction node states its own
        /// independent existential and mints its own witness — a shared
        /// node would assert an unentailed coincidence of the witnesses.
        /// Minting refuses when the restriction already sits on the
        /// member's witness-derivation chain, so every chain is a simple
        /// path over the finite restriction set and the fixpoint
        /// terminates; a refused unfolding merely leaves the conclusion
        /// unsettled.
        /// </summary>
        /// <param name="r">The restriction node.</param>
        /// <param name="c">The asserted filler class.</param>
        private void FireExistentialWitness(TermId r, TermId c)
        {
            if(!InstancesOf.TryGetValue(r, out List<TermId>? instances))
            {
                return;
            }

            EncodedTriple someValues = Fact(r, Terms.SomeValuesFrom, c);
            foreach(TermId p in ObjectsOf(r, Terms.OnProperty))
            {
                EncodedTriple onProperty = Fact(r, Terms.OnProperty, p);
                foreach(TermId x in instances)
                {
                    if(WitnessAncestry.TryGetValue(x, out HashSet<TermId>? ancestry) && ancestry.Contains(r))
                    {
                        continue;
                    }

                    TermId w = Terms.SomeValuesFromWitnessNode(x, r, p, c);
                    RecordWitnessAncestry(w, ancestry, r);
                    Add(x, p, w, EntailmentRules.SomeValuesFromWitness, [Fact(x, Terms.Type, r), onProperty, someValues]);
                    Add(w, Terms.Type, c, EntailmentRules.SomeValuesFromWitness, [Fact(x, Terms.Type, r), onProperty, someValues]);
                }
            }
        }

        /// <summary>
        /// Records a witness's derivation chain: its parent's restriction
        /// ancestry plus the restriction that minted it. A parent's
        /// ancestry is stable from its own first mint, so re-recording on a
        /// later re-fire overwrites with the same value.
        /// </summary>
        /// <param name="witness">The minted witness.</param>
        /// <param name="parentAncestry">The parent member's ancestry, or <c>null</c> for an original individual.</param>
        /// <param name="r">The restriction the witness was minted under.</param>
        private void RecordWitnessAncestry(TermId witness, HashSet<TermId>? parentAncestry, TermId r)
        {
            if(WitnessAncestry.ContainsKey(witness))
            {
                return;
            }

            HashSet<TermId> ancestry = parentAncestry is null ? [] : [.. parentAncestry];
            ancestry.Add(r);
            WitnessAncestry[witness] = ancestry;
        }

        /// <summary>
        /// Fires the disjoint-range emptiness rules over every ranged
        /// property: a property ranged by two datatypes with disjoint value
        /// spaces has a provably empty extension in every model, so an
        /// asserted statement of it is the family's one falsity and a
        /// statement-free property subsumes under every typed property. The
        /// family returns on the falsity.
        /// </summary>
        private void FireDisjointRangeEmptiness()
        {
            HashSet<TermId> visited = [];
            foreach((TermId p, TermId _) in Pairs(Terms.Range))
            {
                if(!visited.Add(p))
                {
                    continue;
                }

                FireDisjointRangeProperty(p);
                if(InconsistencyRule is not null)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Fires the disjoint-range rules for one ranged property. One
        /// disjoint range pair witnesses the emptiness whole, so the first
        /// pair the oracle confirms decides and further pairs add nothing.
        /// The clash is sound for any object term kind — the range
        /// conditions place the object's denotation in both value spaces —
        /// and the vacuous subsumption is sound independent of the
        /// statement check, which only keeps the cascade unreachable: a
        /// statement of the property makes the graph itself inconsistent.
        /// </summary>
        /// <param name="p">The ranged property.</param>
        private void FireDisjointRangeProperty(TermId p)
        {
            List<TermId> ranges = ObjectsOf(p, Terms.Range);
            for(int i = 0; i < ranges.Count; i++)
            {
                for(int j = i + 1; j < ranges.Count; j++)
                {
                    if(!DatatypeOracle.DatatypesKnownDisjoint(ranges[i], ranges[j]))
                    {
                        continue;
                    }

                    EncodedTriple firstRange = Fact(p, Terms.Range, ranges[i]);
                    EncodedTriple secondRange = Fact(p, Terms.Range, ranges[j]);
                    List<(TermId Subject, TermId Object)> statements = Pairs(p);
                    if(statements.Count > 0)
                    {
                        Inconsistent(EntailmentRules.DisjointRangeClash, [firstRange, secondRange, Fact(statements[0].Subject, p, statements[0].Object)]);

                        return;
                    }

                    FireVacuousSubproperties(p, firstRange, secondRange);

                    return;
                }
            }
        }

        /// <summary>
        /// Emits the provably empty property as a subproperty of every term
        /// the closure proves a property — an explicit <c>rdf:Property</c>,
        /// <c>owl:ObjectProperty</c>, or <c>owl:DatatypeProperty</c> typing,
        /// or occurrence in predicate position: a satisfied statement's
        /// predicate denotes a property, so one statement of the candidate
        /// is the witness. The emission is bounded by the closure's own
        /// roster; the model-side quantifier over all of IP needs no wider
        /// emission because a conclusion can only name terms it mentions.
        /// Cascade through the emissions is fenced by the driver: a
        /// statement of the empty property clashes at round end and the
        /// poisoned round's pending set never merges.
        /// </summary>
        /// <param name="p">The provably empty property.</param>
        /// <param name="firstRange">The first range statement of the disjoint pair.</param>
        /// <param name="secondRange">The second range statement of the disjoint pair.</param>
        private void FireVacuousSubproperties(TermId p, EncodedTriple firstRange, EncodedTriple secondRange)
        {
            HashSet<TermId> emitted = [];
            Span<TermId> typings = [Terms.RdfProperty, Terms.ObjectPropertyTerm, Terms.DatatypePropertyTerm];
            foreach(TermId typing in typings)
            {
                if(!InstancesOf.TryGetValue(typing, out List<TermId>? candidates))
                {
                    continue;
                }

                foreach(TermId q in candidates)
                {
                    if(!emitted.Add(q))
                    {
                        continue;
                    }

                    Add(p, Terms.SubPropertyOf, q, EntailmentRules.DisjointRangeVacuousSubproperty, [firstRange, secondRange, Fact(q, Terms.Type, typing)]);
                }
            }

            foreach((TermId q, List<(TermId Subject, TermId Object)> statements) in ByPredicate)
            {
                if(statements.Count > 0 && emitted.Add(q))
                {
                    Add(p, Terms.SubPropertyOf, q, EntailmentRules.DisjointRangeVacuousSubproperty, [firstRange, secondRange, Fact(statements[0].Subject, q, statements[0].Object)]);
                }
            }
        }

        /// <summary>
        /// Fires the type-domain universal subsumption for one declared
        /// domain of <c>rdf:type</c>: ICEXT is the <c>rdf:type</c> slice,
        /// so every member of any class is an <c>rdf:type</c> subject and
        /// lands in the domain — every class subsumes under it, and since
        /// every resource is an <c>owl:Thing</c> instance the domain's
        /// extension is the whole universe, so the <c>owl:Thing</c>
        /// bracket holds in both directions and scm-eqc2 composes the
        /// equivalence. The domain statement's subject is the fixed
        /// <c>rdf:type</c> term; the conclusion ranges over every term the
        /// closure evidences as a class — an explicit <c>rdfs:Class</c> or
        /// <c>owl:Class</c> typing, occurrence as an <c>rdf:type</c>
        /// object, either <c>rdfs:subClassOf</c> position, or a domain or
        /// range object, each witnessed by its evidencing statement. The
        /// first evidence found names the emission's witness.
        /// </summary>
        /// <param name="y">The declared domain of <c>rdf:type</c>.</param>
        private void FireTypeDomainSubsumption(TermId y)
        {
            EncodedTriple domain = Fact(Terms.Type, Terms.Domain, y);
            Add(Terms.Thing, Terms.SubClassOf, y, EntailmentRules.TypeDomainUniversalSubsumption, [domain]);
            Add(y, Terms.SubClassOf, Terms.Thing, EntailmentRules.TypeDomainUniversalSubsumption, [domain]);

            HashSet<TermId> emitted = [Terms.Thing];
            Span<TermId> typings = [Terms.RdfsClass, Terms.ClassTerm];
            foreach(TermId typing in typings)
            {
                if(!InstancesOf.TryGetValue(typing, out List<TermId>? classes))
                {
                    continue;
                }

                foreach(TermId x in classes)
                {
                    if(emitted.Add(x))
                    {
                        Add(x, Terms.SubClassOf, y, EntailmentRules.TypeDomainUniversalSubsumption, [domain, Fact(x, Terms.Type, typing)]);
                    }
                }
            }

            foreach((TermId x, List<TermId> instances) in InstancesOf)
            {
                if(instances.Count > 0 && emitted.Add(x))
                {
                    Add(x, Terms.SubClassOf, y, EntailmentRules.TypeDomainUniversalSubsumption, [domain, Fact(instances[0], Terms.Type, x)]);
                }
            }

            foreach((TermId sub, TermId super) in Pairs(Terms.SubClassOf))
            {
                EncodedTriple edge = Fact(sub, Terms.SubClassOf, super);
                if(emitted.Add(sub))
                {
                    Add(sub, Terms.SubClassOf, y, EntailmentRules.TypeDomainUniversalSubsumption, [domain, edge]);
                }

                if(emitted.Add(super))
                {
                    Add(super, Terms.SubClassOf, y, EntailmentRules.TypeDomainUniversalSubsumption, [domain, edge]);
                }
            }

            Span<TermId> positions = [Terms.Domain, Terms.Range];
            foreach(TermId position in positions)
            {
                foreach((TermId p, TermId x) in Pairs(position))
                {
                    if(emitted.Add(x))
                    {
                        Add(x, Terms.SubClassOf, y, EntailmentRules.TypeDomainUniversalSubsumption, [domain, Fact(p, position, x)]);
                    }
                }
            }
        }

        /// <summary>
        /// Fires the shared has-value property collapse for one has-value
        /// node: each <c>owl:onProperty</c> edge states its own extension
        /// equation over the same node, so two functional properties whose
        /// domains are that node both have extension domain × value and
        /// subsume each other — scm-eqp2 composes the equivalence. Every
        /// antecedent is load-bearing: the domain edges force the domains
        /// onto the node, functionality pins each member to the one value,
        /// and only <c>owl:hasValue</c> pins which value that is.
        /// </summary>
        /// <param name="z">The has-value node.</param>
        /// <param name="v">The asserted value.</param>
        private void FireSharedHasValueCollapse(TermId z, TermId v)
        {
            List<TermId> onProperties = ObjectsOf(z, Terms.OnProperty);
            if(onProperties.Count < 2)
            {
                return;
            }

            EncodedTriple hasValue = Fact(z, Terms.HasValue, v);
            for(int i = 0; i < onProperties.Count; i++)
            {
                for(int j = 0; j < onProperties.Count; j++)
                {
                    if(i == j)
                    {
                        continue;
                    }

                    TermId p = onProperties[i];
                    TermId q = onProperties[j];
                    if(!ObjectsOf(p, Terms.Domain).Contains(z)
                        || !ObjectsOf(q, Terms.Domain).Contains(z)
                        || !HasType(p, Terms.FunctionalProperty)
                        || !HasType(q, Terms.FunctionalProperty))
                    {
                        continue;
                    }

                    Add(
                        p,
                        Terms.SubPropertyOf,
                        q,
                        EntailmentRules.SharedHasValuePropertyCollapse,
                        [
                            hasValue,
                            Fact(z, Terms.OnProperty, p),
                            Fact(z, Terms.OnProperty, q),
                            Fact(p, Terms.Domain, z),
                            Fact(q, Terms.Domain, z),
                            Fact(p, Terms.Type, Terms.FunctionalProperty),
                            Fact(q, Terms.Type, Terms.FunctionalProperty)
                        ]);
                }
            }
        }

        /// <summary>
        /// Fires the datatype-alias literal retype for one sameAs pair,
        /// trying both orientations as (alias, target): the RDF-Based
        /// semantics keys a typed literal's denotation on the datatype its
        /// type IRI DENOTES, so the identity forces every valid-lexical
        /// alias-typed literal onto the target's own lexical-to-value map
        /// and the retype sameAs holds in every model of the edge. The
        /// self-pair guard keeps the eq-ref flood out; only genuine merges
        /// reach the oracle.
        /// </summary>
        /// <param name="x">The sameAs edge's subject.</param>
        /// <param name="y">The sameAs edge's object.</param>
        private void FireDatatypeAliasRetype(TermId x, TermId y)
        {
            if(x == y)
            {
                return;
            }

            FireDatatypeAliasRetypeOrientation(x, y, alias: x, target: y);
            FireDatatypeAliasRetypeOrientation(x, y, alias: y, target: x);
        }

        /// <summary>
        /// Fires one orientation of the datatype-alias retype: behind the
        /// oracle's pair admission — which also latches the family's
        /// every-growing-round re-fire — every distinct object term is
        /// offered to the retype member, and each minted retype emits the
        /// literal-to-retype sameAs carried by the identity edge alone. The
        /// literal's occurrence bounds which retypes mint; it is not a
        /// premise of the derivation.
        /// </summary>
        /// <param name="x">The sameAs edge's subject.</param>
        /// <param name="y">The sameAs edge's object.</param>
        /// <param name="alias">The side read as the alias IRI.</param>
        /// <param name="target">The side read as the recognized datatype-map member.</param>
        private void FireDatatypeAliasRetypeOrientation(TermId x, TermId y, TermId alias, TermId target)
        {
            if(!DatatypeOracle.DatatypeAliasRecognized(alias, target))
            {
                return;
            }

            DatatypeAliasPairSeen = true;
            EncodedTriple edge = Fact(x, Terms.SameAs, y);
            HashSet<TermId> visited = [];
            foreach(EncodedTriple triple in All)
            {
                if(!visited.Add(triple.Object))
                {
                    continue;
                }

                TermId retyped = DatatypeOracle.DatatypeAliasRetype(triple.Object, alias, target);
                if(retyped != TermId.None)
                {
                    Add(triple.Object, Terms.SameAs, retyped, EntailmentRules.DatatypeAliasRetype, [edge]);
                }
            }
        }

        /// <summary>
        /// Fires the count-certificate propagation over the current
        /// indexes: singleton enumerations seed count-1 certificates,
        /// anchored fibre counts and functional fibre products propagate
        /// proven counts through equivalent cardinality restrictions on
        /// inverse properties, and the anchored read-backs emit each
        /// counted bound's <c>owl:sameAs</c> pin onto the minted digit
        /// literal of its proven count. The table is rebuilt on every
        /// fire, a class certifies at most once, no rule mints a term
        /// before the read-back's literal, and the worklist visits each
        /// certified class once over the finite roster, so the pass
        /// terminates structurally. The analyzer derives no falsity: an
        /// emission whose existing pins contradict the proven count
        /// witnesses a premise inconsistency the equality and datatype
        /// machinery finds.
        /// </summary>
        private void FireFibreCardinalityCertificates()
        {
            Dictionary<TermId, FibreCertificate> certificates = [];
            Queue<TermId> worklist = new();
            List<TermId> certifiedOrder = [];

            foreach((TermId enumerated, TermId listHead) in Pairs(Terms.OneOf))
            {
                if(certificates.ContainsKey(enumerated) || ListOf(listHead) is not List<TermId> members || members.Count != 1)
                {
                    continue;
                }

                certificates[enumerated] = new FibreCertificate(1, TermId.None, pinConflicted: false, [Fact(enumerated, Terms.OneOf, listHead)]);
                worklist.Enqueue(enumerated);
                certifiedOrder.Add(enumerated);
            }

            while(worklist.Count > 0)
            {
                FireFibreCertificatePropagation(worklist.Dequeue(), certificates, worklist, certifiedOrder);
            }

            foreach(TermId anchor in certifiedOrder)
            {
                if(certificates[anchor].Count == 1)
                {
                    FireFibreCertificateReadBack(anchor, certificates);
                }
            }
        }

        /// <summary>
        /// Propagates one certified class's count to its consumers: an
        /// equivalent restriction of the class carrying a cardinality
        /// bound on an inverse property supplies the fibre factor, and
        /// every class equivalent to a some-values-from restriction over
        /// the certified class on the inverse's own property receives the
        /// product of the count and the factor. A count of one needs no
        /// property characteristic — the union over a singleton is one
        /// fibre — while a larger count demands the consuming property
        /// functional, because only disjoint fibres sum to the product.
        /// The first certificate a class receives wins: a second
        /// derivation proving a different count would witness a premise
        /// inconsistency, where every certificate is vacuously true, and
        /// determinism rides the indexes' insertion order. Overflowing
        /// products refuse.
        /// </summary>
        /// <param name="source">The certified class whose count propagates.</param>
        /// <param name="certificates">The certificate table.</param>
        /// <param name="worklistToAppendTo">The worklist newly certified classes enqueue onto.</param>
        /// <param name="certifiedOrderToAppendTo">The certification order the read-backs replay.</param>
        private void FireFibreCertificatePropagation(TermId source, Dictionary<TermId, FibreCertificate> certificates, Queue<TermId> worklistToAppendTo, List<TermId> certifiedOrderToAppendTo)
        {
            FibreCertificate certificate = certificates[source];
            foreach((TermId cardinalityNode, EncodedTriple sourceEquivalence) in EquivalentClassPartnersOf(source))
            {
                foreach(TermId inverse in ObjectsOf(cardinalityNode, Terms.OnProperty))
                {
                    foreach(TermId bound in ObjectsOf(cardinalityNode, Terms.Cardinality))
                    {
                        if(!TryReadFibreBound(bound, out long factor, out TermId pinDatatype, out bool pinConflicted, out List<EncodedTriple> pinPremises)
                            || (factor != 0 && certificate.Count > long.MaxValue / factor))
                        {
                            continue;
                        }

                        long product = certificate.Count * factor;
                        foreach(TermId restriction in SubjectsOf(source, Terms.SomeValuesFrom))
                        {
                            foreach(TermId property in ObjectsOf(restriction, Terms.OnProperty))
                            {
                                if(!TryFindInverseEdge(property, inverse, out EncodedTriple inverseEdge))
                                {
                                    continue;
                                }

                                bool needsFunctional = certificate.Count != 1;
                                if(needsFunctional && !HasType(property, Terms.FunctionalProperty))
                                {
                                    continue;
                                }

                                foreach((TermId consumer, EncodedTriple consumerEquivalence) in EquivalentClassPartnersOf(restriction))
                                {
                                    if(certificates.ContainsKey(consumer))
                                    {
                                        continue;
                                    }

                                    MergePinDatatypes(certificate.PinDatatype, certificate.PinConflicted, pinDatatype, pinConflicted, out TermId mergedDatatype, out bool mergedConflicted);
                                    List<EncodedTriple> premises = [.. certificate.Premises];
                                    HashSet<EncodedTriple> seen = [.. premises];
                                    AppendPremise(premises, seen, sourceEquivalence);
                                    AppendPremise(premises, seen, Fact(cardinalityNode, Terms.OnProperty, inverse));
                                    AppendPremise(premises, seen, Fact(cardinalityNode, Terms.Cardinality, bound));
                                    foreach(EncodedTriple pin in pinPremises)
                                    {
                                        AppendPremise(premises, seen, pin);
                                    }

                                    AppendPremise(premises, seen, Fact(restriction, Terms.SomeValuesFrom, source));
                                    AppendPremise(premises, seen, Fact(restriction, Terms.OnProperty, property));
                                    AppendPremise(premises, seen, inverseEdge);
                                    if(needsFunctional)
                                    {
                                        AppendPremise(premises, seen, Fact(property, Terms.Type, Terms.FunctionalProperty));
                                    }

                                    AppendPremise(premises, seen, consumerEquivalence);
                                    certificates[consumer] = new FibreCertificate(product, mergedDatatype, mergedConflicted, premises);
                                    worklistToAppendTo.Enqueue(consumer);
                                    certifiedOrderToAppendTo.Add(consumer);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Emits one anchored read-back: an equivalent restriction of a
        /// count-1 anchor carries a cardinality bound on an inverse
        /// property, a certified class is equivalent to a some-values-from
        /// restriction over the anchor on the inverse's own property, and
        /// the bound term is pinned <c>owl:sameAs</c> the minted digit
        /// literal of the certified count under the chain's single pin
        /// datatype. A bound that reads as a literal itself needs no pin;
        /// a conflicted or absent pin datatype and an out-of-range count
        /// refuse the mint. The emission ignores existing pins on the
        /// bound: a contradicting pin witnesses a premise inconsistency
        /// whose clash the equality and datatype machinery derives.
        /// </summary>
        /// <param name="anchor">The count-1 anchor class.</param>
        /// <param name="certificates">The certificate table.</param>
        private void FireFibreCertificateReadBack(TermId anchor, Dictionary<TermId, FibreCertificate> certificates)
        {
            FibreCertificate anchorCertificate = certificates[anchor];
            foreach((TermId cardinalityNode, EncodedTriple anchorEquivalence) in EquivalentClassPartnersOf(anchor))
            {
                foreach(TermId inverse in ObjectsOf(cardinalityNode, Terms.OnProperty))
                {
                    foreach(TermId bound in ObjectsOf(cardinalityNode, Terms.Cardinality))
                    {
                        if(DatatypeOracle.LiteralNonNegativeInteger(bound, out _, out _))
                        {
                            continue;
                        }

                        foreach(TermId restriction in SubjectsOf(anchor, Terms.SomeValuesFrom))
                        {
                            foreach(TermId property in ObjectsOf(restriction, Terms.OnProperty))
                            {
                                if(!TryFindInverseEdge(property, inverse, out EncodedTriple inverseEdge))
                                {
                                    continue;
                                }

                                foreach((TermId counted, EncodedTriple countedEquivalence) in EquivalentClassPartnersOf(restriction))
                                {
                                    if(!certificates.TryGetValue(counted, out FibreCertificate countedCertificate)
                                        || countedCertificate.PinDatatype == TermId.None
                                        || countedCertificate.PinConflicted)
                                    {
                                        continue;
                                    }

                                    TermId minted = DatatypeOracle.NonNegativeIntegerLiteral(countedCertificate.Count, countedCertificate.PinDatatype);
                                    if(minted == TermId.None)
                                    {
                                        continue;
                                    }

                                    List<EncodedTriple> premises = [.. countedCertificate.Premises];
                                    HashSet<EncodedTriple> seen = [.. premises];
                                    foreach(EncodedTriple premise in anchorCertificate.Premises)
                                    {
                                        AppendPremise(premises, seen, premise);
                                    }

                                    AppendPremise(premises, seen, anchorEquivalence);
                                    AppendPremise(premises, seen, Fact(cardinalityNode, Terms.OnProperty, inverse));
                                    AppendPremise(premises, seen, Fact(cardinalityNode, Terms.Cardinality, bound));
                                    AppendPremise(premises, seen, Fact(restriction, Terms.SomeValuesFrom, anchor));
                                    AppendPremise(premises, seen, Fact(restriction, Terms.OnProperty, property));
                                    AppendPremise(premises, seen, inverseEdge);
                                    AppendPremise(premises, seen, countedEquivalence);
                                    Add(bound, Terms.SameAs, minted, EntailmentRules.FibreCardinalityCertificate, [.. premises]);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Reads a bound term's nonnegative-integer value: a literal bound
        /// reads directly, and any other term reads through its one-hop
        /// <c>owl:sameAs</c> pins onto exact-tower literals, in either
        /// orientation — multi-hop pins arrive here because the equality
        /// rules materialise the sameAs closure and the family re-fires on
        /// its growth. Exactly one distinct pinned value succeeds; two
        /// distinct values refuse whole, leaving the conflict to the
        /// equality machinery's inconsistency. Pins agreeing on the value
        /// but not the datatype read the value and mark the datatype
        /// conflicted, which refuses the read-back mint.
        /// </summary>
        /// <param name="bound">The bound term.</param>
        /// <param name="value">The read value.</param>
        /// <param name="pinDatatype">The single contributing pin datatype, or the literal bound's own.</param>
        /// <param name="pinConflicted">Whether the contributing pins mixed datatypes.</param>
        /// <param name="pinPremises">The pin edges that contributed the value or the conflict.</param>
        /// <returns><c>true</c> when the bound reads one value.</returns>
        private bool TryReadFibreBound(TermId bound, out long value, out TermId pinDatatype, out bool pinConflicted, out List<EncodedTriple> pinPremises)
        {
            pinPremises = [];
            pinConflicted = false;
            if(DatatypeOracle.LiteralNonNegativeInteger(bound, out value, out pinDatatype))
            {
                return true;
            }

            List<(TermId Candidate, EncodedTriple Edge)> pins = [];
            foreach(TermId candidate in ObjectsOf(bound, Terms.SameAs))
            {
                pins.Add((candidate, Fact(bound, Terms.SameAs, candidate)));
            }

            foreach(TermId candidate in SubjectsOf(bound, Terms.SameAs))
            {
                pins.Add((candidate, Fact(candidate, Terms.SameAs, bound)));
            }

            bool found = false;
            foreach((TermId candidate, EncodedTriple edge) in pins)
            {
                if(!DatatypeOracle.LiteralNonNegativeInteger(candidate, out long candidateValue, out TermId candidateDatatype))
                {
                    continue;
                }

                if(!found)
                {
                    found = true;
                    value = candidateValue;
                    pinDatatype = candidateDatatype;
                    pinPremises.Add(edge);

                    continue;
                }

                if(candidateValue != value)
                {
                    value = 0;
                    pinDatatype = TermId.None;
                    pinConflicted = false;
                    pinPremises.Clear();

                    return false;
                }

                if(candidateDatatype != pinDatatype && !pinConflicted)
                {
                    pinConflicted = true;
                    pinPremises.Add(edge);
                }
            }

            return found;
        }

        /// <summary>
        /// The classes an <c>owl:equivalentClass</c> edge joins to the
        /// term, read in both orientations of the asserted-or-derived edge
        /// set with the edge each partner rides — the equivalence
        /// condition is an iff, symmetric in its pair, so neither
        /// orientation waits on materialised symmetry.
        /// <c>rdfs:subClassOf</c> never substitutes: containment does not
        /// transfer a count.
        /// </summary>
        /// <param name="term">The class term.</param>
        /// <returns>The (partner, edge) pairs, in index order.</returns>
        private List<(TermId Partner, EncodedTriple Edge)> EquivalentClassPartnersOf(TermId term)
        {
            List<(TermId Partner, EncodedTriple Edge)> partners = [];
            foreach(TermId partner in ObjectsOf(term, Terms.EquivalentClass))
            {
                partners.Add((partner, Fact(term, Terms.EquivalentClass, partner)));
            }

            foreach(TermId partner in SubjectsOf(term, Terms.EquivalentClass))
            {
                partners.Add((partner, Fact(partner, Terms.EquivalentClass, term)));
            }

            return partners;
        }

        /// <summary>
        /// Finds an <c>owl:inverseOf</c> edge between the two properties
        /// in either orientation — the inverse condition is an iff over
        /// converse extensions, symmetric in its pair.
        /// </summary>
        /// <param name="property">The consuming property.</param>
        /// <param name="inverse">The cardinality restriction's property.</param>
        /// <param name="edge">The asserted-or-derived edge found.</param>
        /// <returns><c>true</c> when an edge relates the pair.</returns>
        private bool TryFindInverseEdge(TermId property, TermId inverse, out EncodedTriple edge)
        {
            if(ObjectsOf(property, Terms.InverseOf).Contains(inverse))
            {
                edge = Fact(property, Terms.InverseOf, inverse);

                return true;
            }

            if(ObjectsOf(inverse, Terms.InverseOf).Contains(property))
            {
                edge = Fact(inverse, Terms.InverseOf, property);

                return true;
            }

            edge = default;

            return false;
        }

        /// <summary>
        /// Merges two pin-datatype readings: an absent side passes the
        /// other through, agreement passes the shared datatype, and
        /// disagreement marks the merge conflicted — a conflicted chain
        /// still counts soundly but refuses the read-back mint, which
        /// types its literal by the single datatype every contributing pin
        /// carried.
        /// </summary>
        /// <param name="first">The first reading's datatype.</param>
        /// <param name="firstConflicted">Whether the first reading is already conflicted.</param>
        /// <param name="second">The second reading's datatype.</param>
        /// <param name="secondConflicted">Whether the second reading is already conflicted.</param>
        /// <param name="merged">The merged datatype.</param>
        /// <param name="conflicted">Whether the merge is conflicted.</param>
        private static void MergePinDatatypes(TermId first, bool firstConflicted, TermId second, bool secondConflicted, out TermId merged, out bool conflicted)
        {
            conflicted = firstConflicted || secondConflicted || (first != TermId.None && second != TermId.None && first != second);
            merged = first == TermId.None ? second : first;
        }

        /// <summary>Appends a matched premise once, keyed by the seen set — the derivation record carries each matched triple exactly once.</summary>
        /// <param name="premisesToAppendTo">The accumulated premise list.</param>
        /// <param name="seen">The premises already accumulated.</param>
        /// <param name="premise">The matched premise.</param>
        private static void AppendPremise(List<EncodedTriple> premisesToAppendTo, HashSet<EncodedTriple> seen, EncodedTriple premise)
        {
            if(seen.Add(premise))
            {
                premisesToAppendTo.Add(premise);
            }
        }

        /// <summary>
        /// One proven class-extension cardinality: the count, the single
        /// datatype every contributing input pin carried with whether the
        /// chain mixed datatypes, and the matched premise triples the
        /// derivation accumulated. A certificate attaches to an existing
        /// class term and is never overwritten.
        /// </summary>
        private readonly struct FibreCertificate
        {
            /// <summary>Creates the certificate.</summary>
            /// <param name="count">The proven class-extension cardinality.</param>
            /// <param name="pinDatatype">The single contributing pin datatype, or <see cref="TermId.None"/> while no pin has contributed.</param>
            /// <param name="pinConflicted">Whether the chain mixed pin datatypes, refusing the read-back mint.</param>
            /// <param name="premises">The matched premise triples, each exactly once.</param>
            public FibreCertificate(long count, TermId pinDatatype, bool pinConflicted, List<EncodedTriple> premises)
            {
                Count = count;
                PinDatatype = pinDatatype;
                PinConflicted = pinConflicted;
                Premises = premises;
            }

            /// <summary>The proven class-extension cardinality.</summary>
            public long Count { get; }

            /// <summary>The single datatype every contributing input pin carried, or <see cref="TermId.None"/> while no pin has contributed.</summary>
            public TermId PinDatatype { get; }

            /// <summary>Whether the chain mixed pin datatypes — a conflicted chain counts soundly but refuses the read-back mint.</summary>
            public bool PinConflicted { get; }

            /// <summary>The matched premise triples the derivation accumulated, each exactly once.</summary>
            public List<EncodedTriple> Premises { get; }
        }
    }
}
