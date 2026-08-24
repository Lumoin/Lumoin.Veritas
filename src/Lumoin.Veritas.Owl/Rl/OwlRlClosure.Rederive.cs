using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Owl.Rl;

public static partial class OwlRlClosure
{
    internal sealed partial class ClosureContext
    {
        /// <summary>Whether the triple of the three terms is present in the current accumulated set.</summary>
        /// <param name="subject">The triple's subject.</param>
        /// <param name="predicate">The triple's predicate.</param>
        /// <param name="object">The triple's object.</param>
        /// <returns><c>true</c> when the triple is present.</returns>
        private bool Present(TermId subject, TermId predicate, TermId @object)
        {
            return All.Contains(Fact(subject, predicate, @object));
        }

        /// <summary>Whether the current index state concludes <paramref name="fact"/> by some producer rule — the head-bound backward matcher that restores a deleted fact with a surviving derivation.</summary>
        /// <param name="fact">The candidate fact whose rederivability is tested.</param>
        /// <returns><c>true</c> when at least one producer entry confirms <paramref name="fact"/> against the current state.</returns>
        internal bool CheckRederivable(EncodedTriple fact)
        {
            //The three eq-rep entries are timed as one child region; the rest of
            //the chain stays untimed inside the parent Rederive region, and the
            //short-circuit is identical to a single || expression because an
            //eq-rep hit skips the remainder either way.
            long eqRepStart = OwlRlMaintenanceInstrumentation.Begin();
            bool eqRep = RederiveEqRepS(fact) || RederiveEqRepP(fact) || RederiveEqRepO(fact);
            OwlRlMaintenanceInstrumentation.End(OwlRlMaintenancePhase.RederiveEqRep, eqRepStart);

            if(eqRep
                || RederivePrpSymp(fact) || RederivePrpTrp(fact)
                || RederivePrpSpo1(fact) || RederivePrpSpo2(fact)
                || RederivePrpEqp1(fact) || RederivePrpEqp2(fact)
                || RederivePrpInv1(fact) || RederivePrpInv2(fact)
                || RederiveClsHv1(fact) || RederiveReflexiveInstantiation(fact))
            {
                return true;
            }

            return fact.Predicate switch
            {
                _ when fact.Predicate == Terms.SameAs => RederiveEqRef(fact) || RederiveEqSym(fact) || RederiveEqTrans(fact)
                    || RederivePrpFp(fact) || RederivePrpIfp(fact) || RederivePrpKey(fact)
                    || RederiveClsMaxc2(fact) || RederiveClsMaxqc4(fact),
                _ when fact.Predicate == Terms.Type => RederivePrpDom(fact) || RederivePrpRng(fact)
                    || RederiveChainTransitivity(fact) || RederiveClsInt1(fact) || RederiveClsInt2(fact)
                    || RederiveClsUni(fact) || RederiveClsSvf1(fact) || RederiveClsSvf2(fact)
                    || RederiveClsAvf(fact) || RederiveClsHv2(fact) || RederiveClsOo(fact)
                    || RederiveCaxSco(fact) || RederiveCaxEqc1(fact) || RederiveCaxEqc2(fact)
                    || RederiveInverseCharacteristicTransfer(fact) || RederiveSingletonEnumerationCharacteristic(fact)
                    || RederiveMinCardinalityOneMembership(fact),
                _ when fact.Predicate == Terms.SubClassOf => RederiveScmCls(fact) || RederiveScmSco(fact)
                    || RederiveScmEqc1(fact) || RederiveScmInt(fact) || RederiveScmUni(fact)
                    || RederiveScmSvf1(fact) || RederiveScmSvf2(fact) || RederiveScmAvf1(fact)
                    || RederiveScmAvf2(fact) || RederiveScmHv(fact)
                    || RederiveOneOfMemberSubset(fact) || RederiveUnionOfMemberSubset(fact),
                _ when fact.Predicate == Terms.EquivalentClass => RederiveScmCls(fact) || RederiveScmEqc1(fact)
                    || RederiveScmEqc2(fact),
                _ when fact.Predicate == Terms.SubPropertyOf => RederiveScmSpo(fact) || RederiveScmEqp1(fact)
                    || RederiveScmOp(fact) || RederiveScmDp(fact),
                _ when fact.Predicate == Terms.EquivalentProperty => RederiveScmEqp1(fact) || RederiveScmEqp2(fact)
                    || RederiveScmOp(fact) || RederiveScmDp(fact),
                _ when fact.Predicate == Terms.Domain => RederiveScmDom1(fact) || RederiveScmDom2(fact),
                _ when fact.Predicate == Terms.Range => RederiveScmRng1(fact) || RederiveScmRng2(fact)
                    || RederiveDtRangeIntersection(fact),
                _ when fact.Predicate == Terms.DifferentFrom => RederiveDifferentFromSymmetry(fact),
                _ when fact.Predicate == Terms.ComplementOf => RederiveComplementOfSymmetry(fact),
                _ when fact.Predicate == Terms.DisjointWith => RederiveCaxDw(fact) || RederiveCaxAdc(fact),
                _ when fact.Predicate == Terms.PropertyDisjointWith => RederivePrpPdw(fact) || RederivePrpAdp(fact),
                _ when fact.Predicate == Terms.PropertyChainAxiom => RederiveTransitivityChain(fact),
                _ when fact.Predicate == Terms.First => RederiveTransitivityChain(fact),
                _ when fact.Predicate == Terms.Rest => RederiveTransitivityChain(fact),
                _ => false,
            };
        }

        /// <summary>Whether the named producer rule concludes <paramref name="fact"/> against the current state — the single head-bound entry for that rule, keyed by its <see cref="EntailmentRules"/> name; falsity and unknown rules answer <c>false</c>.</summary>
        /// <param name="rule">The rule name to test <paramref name="fact"/> against.</param>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the named rule's entry confirms <paramref name="fact"/>.</returns>
        internal bool CheckRederiveEntry(string rule, EncodedTriple fact)
        {
            return rule switch
            {
                EntailmentRules.EqRepS => RederiveEqRepS(fact),
                EntailmentRules.EqRepP => RederiveEqRepP(fact),
                EntailmentRules.EqRepO => RederiveEqRepO(fact),
                EntailmentRules.PrpSymp => RederivePrpSymp(fact),
                EntailmentRules.PrpTrp => RederivePrpTrp(fact),
                EntailmentRules.PrpSpo1 => RederivePrpSpo1(fact),
                EntailmentRules.PrpSpo2 => RederivePrpSpo2(fact),
                EntailmentRules.PrpEqp1 => RederivePrpEqp1(fact),
                EntailmentRules.PrpEqp2 => RederivePrpEqp2(fact),
                EntailmentRules.PrpInv1 => RederivePrpInv1(fact),
                EntailmentRules.PrpInv2 => RederivePrpInv2(fact),
                EntailmentRules.ClsHv1 => RederiveClsHv1(fact),
                EntailmentRules.ReflexiveInstantiation => RederiveReflexiveInstantiation(fact),
                EntailmentRules.EqSym => RederiveEqSym(fact),
                EntailmentRules.EqTrans => RederiveEqTrans(fact),
                EntailmentRules.PrpFp => RederivePrpFp(fact),
                EntailmentRules.PrpIfp => RederivePrpIfp(fact),
                EntailmentRules.PrpKey => RederivePrpKey(fact),
                EntailmentRules.ClsMaxc2 => RederiveClsMaxc2(fact),
                EntailmentRules.ClsMaxqc4 => RederiveClsMaxqc4(fact),
                EntailmentRules.PrpDom => RederivePrpDom(fact),
                EntailmentRules.PrpRng => RederivePrpRng(fact),
                EntailmentRules.ChainTransitivity => RederiveChainTransitivity(fact),
                EntailmentRules.ClsInt1 => RederiveClsInt1(fact),
                EntailmentRules.ClsInt2 => RederiveClsInt2(fact),
                EntailmentRules.ClsUni => RederiveClsUni(fact),
                EntailmentRules.ClsSvf1 => RederiveClsSvf1(fact),
                EntailmentRules.ClsSvf2 => RederiveClsSvf2(fact),
                EntailmentRules.ClsAvf => RederiveClsAvf(fact),
                EntailmentRules.ClsHv2 => RederiveClsHv2(fact),
                EntailmentRules.ClsOo => RederiveClsOo(fact),
                EntailmentRules.CaxSco => RederiveCaxSco(fact),
                EntailmentRules.CaxEqc1 => RederiveCaxEqc1(fact),
                EntailmentRules.CaxEqc2 => RederiveCaxEqc2(fact),
                EntailmentRules.ScmCls => RederiveScmCls(fact),
                EntailmentRules.ScmSco => RederiveScmSco(fact),
                EntailmentRules.ScmEqc1 => RederiveScmEqc1(fact),
                EntailmentRules.ScmEqc2 => RederiveScmEqc2(fact),
                EntailmentRules.ScmInt => RederiveScmInt(fact),
                EntailmentRules.ScmUni => RederiveScmUni(fact),
                EntailmentRules.ScmSpo => RederiveScmSpo(fact),
                EntailmentRules.ScmEqp1 => RederiveScmEqp1(fact),
                EntailmentRules.ScmEqp2 => RederiveScmEqp2(fact),
                EntailmentRules.ScmDom1 => RederiveScmDom1(fact),
                EntailmentRules.ScmDom2 => RederiveScmDom2(fact),
                EntailmentRules.ScmRng1 => RederiveScmRng1(fact),
                EntailmentRules.ScmRng2 => RederiveScmRng2(fact),
                EntailmentRules.DtRangeIntersection => RederiveDtRangeIntersection(fact),
                EntailmentRules.DifferentFromSymmetry => RederiveDifferentFromSymmetry(fact),
                EntailmentRules.CaxDw => RederiveCaxDw(fact),
                EntailmentRules.CaxAdc => RederiveCaxAdc(fact),
                EntailmentRules.PrpPdw => RederivePrpPdw(fact),
                EntailmentRules.PrpAdp => RederivePrpAdp(fact),
                EntailmentRules.TransitivityChain => RederiveTransitivityChain(fact),
                EntailmentRules.EqRef => RederiveEqRef(fact),
                EntailmentRules.ScmOp => RederiveScmOp(fact),
                EntailmentRules.ScmDp => RederiveScmDp(fact),
                EntailmentRules.ScmSvf1 => RederiveScmSvf1(fact),
                EntailmentRules.ScmSvf2 => RederiveScmSvf2(fact),
                EntailmentRules.ScmAvf1 => RederiveScmAvf1(fact),
                EntailmentRules.ScmAvf2 => RederiveScmAvf2(fact),
                EntailmentRules.ScmHv => RederiveScmHv(fact),
                EntailmentRules.InverseCharacteristicTransfer => RederiveInverseCharacteristicTransfer(fact),
                EntailmentRules.SingletonEnumerationCharacteristic => RederiveSingletonEnumerationCharacteristic(fact),
                EntailmentRules.ComplementOfSymmetry => RederiveComplementOfSymmetry(fact),
                EntailmentRules.OneOfMemberSubset => RederiveOneOfMemberSubset(fact),
                EntailmentRules.UnionOfMemberSubset => RederiveUnionOfMemberSubset(fact),
                EntailmentRules.MinCardinalityOneMembership => RederiveMinCardinalityOneMembership(fact),
                _ => false,
            };
        }

        /// <summary>Whether the min-cardinality-1 membership completion concludes <paramref name="fact"/> — the object is a min-1 restriction on a property the subject carries at least one edge under.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveMinCardinalityOneMembership(EncodedTriple fact)
        {
            bool hasOneBound = false;
            foreach(TermId bound in ObjectsOf(fact.Object, Terms.MinCardinality))
            {
                if(Terms.OneBounds.Contains(bound))
                {
                    hasOneBound = true;

                    break;
                }
            }

            if(!hasOneBound)
            {
                return false;
            }

            foreach(TermId p in ObjectsOf(fact.Object, Terms.OnProperty))
            {
                if(ObjectsOf(fact.Subject, p).Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether eq-ref concludes <paramref name="fact"/> — a reflexive sameAs whose term some surviving triple still mentions in any position.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveEqRef(EncodedTriple fact)
        {
            if(fact.Subject != fact.Object)
            {
                return false;
            }

            TermId term = fact.Subject;
            foreach(TermId predicate in PredicatesOfSubjectList(term))
            {
                if(ObjectsOf(term, predicate).Count > 0)
                {
                    return true;
                }
            }

            if(Pairs(term).Count > 0)
            {
                return true;
            }

            foreach(TermId predicate in PredicatesOfObjectList(term))
            {
                if(SubjectsOf(term, predicate).Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether scm-op concludes <paramref name="fact"/> — a reflexive sub- or equivalent-property statement of a declared object property.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmOp(EncodedTriple fact)
        {
            return fact.Subject == fact.Object && HasType(fact.Subject, Terms.ObjectPropertyTerm);
        }

        /// <summary>Whether scm-dp concludes <paramref name="fact"/> — a reflexive sub- or equivalent-property statement of a declared datatype property.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmDp(EncodedTriple fact)
        {
            return fact.Subject == fact.Object && HasType(fact.Subject, Terms.DatatypePropertyTerm);
        }

        /// <summary>Whether scm-svf1 concludes <paramref name="fact"/> — two some-values restrictions sharing a property with fillers in the subclass relation.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmSvf1(EncodedTriple fact)
        {
            return RederiveFillerComparison(fact, Terms.SomeValuesFrom);
        }

        /// <summary>Whether scm-avf1 concludes <paramref name="fact"/> — two all-values restrictions sharing a property with fillers in the subclass relation.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmAvf1(EncodedTriple fact)
        {
            return RederiveFillerComparison(fact, Terms.AllValuesFrom);
        }

        /// <summary>Whether scm-svf2 concludes <paramref name="fact"/> — two some-values restrictions on one filler whose properties stand in the subproperty relation.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmSvf2(EncodedTriple fact)
        {
            return RederiveSharedFillerComparison(fact.Subject, fact.Object, Terms.SomeValuesFrom);
        }

        /// <summary>Whether scm-avf2 concludes <paramref name="fact"/> — two all-values restrictions on one filler whose properties stand in the subproperty relation, with the contravariant conclusion: the subclass side sits on the superproperty.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmAvf2(EncodedTriple fact)
        {
            return RederiveSharedFillerComparison(fact.Object, fact.Subject, Terms.AllValuesFrom);
        }

        /// <summary>Whether scm-hv concludes <paramref name="fact"/> — two has-value restrictions on one value whose properties stand in the subproperty relation.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmHv(EncodedTriple fact)
        {
            return RederiveSharedFillerComparison(fact.Subject, fact.Object, Terms.HasValue);
        }

        /// <summary>Whether a same-property filler comparison concludes the subclass statement <paramref name="fact"/> — both restrictions carry a filler under <paramref name="fillerPredicate"/>, the fillers stand in rdfs:subClassOf, and the restrictions share an <c>owl:onProperty</c> value.</summary>
        /// <param name="fact">The candidate subclass fact between two restriction nodes.</param>
        /// <param name="fillerPredicate">The filler predicate the comparison reads.</param>
        /// <returns><c>true</c> when the comparison confirms <paramref name="fact"/>.</returns>
        private bool RederiveFillerComparison(EncodedTriple fact, TermId fillerPredicate)
        {
            bool fillersRelated = false;
            foreach(TermId firstFiller in ObjectsOf(fact.Subject, fillerPredicate))
            {
                foreach(TermId secondFiller in ObjectsOf(fact.Object, fillerPredicate))
                {
                    if(Present(firstFiller, Terms.SubClassOf, secondFiller))
                    {
                        fillersRelated = true;

                        break;
                    }
                }

                if(fillersRelated)
                {
                    break;
                }
            }

            if(!fillersRelated)
            {
                return false;
            }

            foreach(TermId p in ObjectsOf(fact.Subject, Terms.OnProperty))
            {
                if(ObjectsOf(fact.Object, Terms.OnProperty).Contains(p))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether a shared-filler comparison concludes the subclass statement between the two restrictions — equal fillers under <paramref name="fillerPredicate"/> and a subproperty bridge from the subproperty-side restriction's property to the superproperty-side restriction's.</summary>
        /// <param name="subPropertyRestriction">The restriction on the subproperty side of the bridge.</param>
        /// <param name="superPropertyRestriction">The restriction on the superproperty side of the bridge.</param>
        /// <param name="fillerPredicate">The filler predicate the comparison reads.</param>
        /// <returns><c>true</c> when the comparison confirms the statement.</returns>
        private bool RederiveSharedFillerComparison(TermId subPropertyRestriction, TermId superPropertyRestriction, TermId fillerPredicate)
        {
            bool sharesFiller = false;
            foreach(TermId filler in ObjectsOf(subPropertyRestriction, fillerPredicate))
            {
                if(ObjectsOf(superPropertyRestriction, fillerPredicate).Contains(filler))
                {
                    sharesFiller = true;

                    break;
                }
            }

            if(!sharesFiller)
            {
                return false;
            }

            foreach(TermId subProperty in ObjectsOf(subPropertyRestriction, Terms.OnProperty))
            {
                foreach(TermId superProperty in ObjectsOf(superPropertyRestriction, Terms.OnProperty))
                {
                    if(Present(subProperty, Terms.SubPropertyOf, superProperty))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        //Any-predicate producers: eq-rep in each position and the
        //predicate-conditioned property and restriction rules.

        /// <summary>Whether eq-rep-s concludes <paramref name="fact"/> — a sameAs neighbour of the subject with the substituted source triple present.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveEqRepS(EncodedTriple fact)
        {
            foreach(TermId x in SubjectsOf(fact.Subject, Terms.SameAs))
            {
                if(x != fact.Subject && Present(x, fact.Predicate, fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether eq-rep-p concludes <paramref name="fact"/> — a sameAs neighbour of the predicate with the substituted source triple present.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveEqRepP(EncodedTriple fact)
        {
            foreach(TermId x in SubjectsOf(fact.Predicate, Terms.SameAs))
            {
                if(x != fact.Predicate && Present(fact.Subject, x, fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether eq-rep-o concludes <paramref name="fact"/> — a sameAs neighbour of the object with the substituted source triple present.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveEqRepO(EncodedTriple fact)
        {
            foreach(TermId x in SubjectsOf(fact.Object, Terms.SameAs))
            {
                if(x != fact.Object && Present(fact.Subject, fact.Predicate, x))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether prp-symp concludes <paramref name="fact"/> — the predicate is symmetric and the reverse edge is present.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederivePrpSymp(EncodedTriple fact)
        {
            return HasType(fact.Predicate, Terms.SymmetricProperty) && Present(fact.Object, fact.Predicate, fact.Subject);
        }

        /// <summary>Whether prp-trp concludes <paramref name="fact"/> — the predicate is transitive and an intermediate composes the endpoints.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederivePrpTrp(EncodedTriple fact)
        {
            if(!HasType(fact.Predicate, Terms.TransitiveProperty))
            {
                return false;
            }

            foreach(TermId middle in ObjectsOf(fact.Subject, fact.Predicate))
            {
                if(Present(middle, fact.Predicate, fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether prp-spo1 concludes <paramref name="fact"/> — a sub-property of the predicate carries the same edge between the endpoints.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederivePrpSpo1(EncodedTriple fact)
        {
            foreach(TermId subProperty in SubjectsOf(fact.Predicate, Terms.SubPropertyOf))
            {
                if(Present(fact.Subject, subProperty, fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether prp-spo2 concludes <paramref name="fact"/> — the predicate heads a property chain reachable from the subject to the object, walked iteratively over the parsed chain.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederivePrpSpo2(EncodedTriple fact)
        {
            foreach(TermId listHead in ObjectsOf(fact.Predicate, Terms.PropertyChainAxiom))
            {
                if(ListOf(listHead) is not List<TermId> chain || chain.Count == 0)
                {
                    continue;
                }

                HashSet<TermId> frontier = [fact.Subject];
                foreach(TermId hop in chain)
                {
                    HashSet<TermId> next = [];
                    foreach(TermId node in frontier)
                    {
                        foreach(TermId reached in ObjectsOf(node, hop))
                        {
                            next.Add(reached);
                        }
                    }

                    frontier = next;
                    if(frontier.Count == 0)
                    {
                        break;
                    }
                }

                if(frontier.Contains(fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether prp-eqp1 concludes <paramref name="fact"/> — an equivalent property with the predicate as the equivalence's second member carries the edge.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederivePrpEqp1(EncodedTriple fact)
        {
            foreach(TermId equivalent in SubjectsOf(fact.Predicate, Terms.EquivalentProperty))
            {
                if(Present(fact.Subject, equivalent, fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether prp-eqp2 concludes <paramref name="fact"/> — an equivalent property with the predicate as the equivalence's first member carries the edge.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederivePrpEqp2(EncodedTriple fact)
        {
            foreach(TermId equivalent in ObjectsOf(fact.Predicate, Terms.EquivalentProperty))
            {
                if(Present(fact.Subject, equivalent, fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether prp-inv1 concludes <paramref name="fact"/> — an inverse of the predicate carries the reversed edge, the inverse as the pair's first member.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederivePrpInv1(EncodedTriple fact)
        {
            foreach(TermId inverse in SubjectsOf(fact.Predicate, Terms.InverseOf))
            {
                if(Present(fact.Object, inverse, fact.Subject))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether prp-inv2 concludes <paramref name="fact"/> — an inverse of the predicate carries the reversed edge, the inverse as the pair's second member.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederivePrpInv2(EncodedTriple fact)
        {
            foreach(TermId inverse in ObjectsOf(fact.Predicate, Terms.InverseOf))
            {
                if(Present(fact.Object, inverse, fact.Subject))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether cls-hv1 concludes <paramref name="fact"/> — a has-value restriction on the predicate asserting the object as a value, carried by an instance subject.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveClsHv1(EncodedTriple fact)
        {
            foreach(TermId restriction in SubjectsOf(fact.Predicate, Terms.OnProperty))
            {
                if(ObjectsOf(restriction, Terms.HasValue).Contains(fact.Object) && HasType(fact.Subject, restriction))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether reflexive instantiation concludes <paramref name="fact"/> — a reflexive-property self-edge on a named individual.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveReflexiveInstantiation(EncodedTriple fact)
        {
            return fact.Subject == fact.Object
                && HasType(fact.Predicate, Terms.ReflexiveProperty)
                && HasType(fact.Subject, Terms.NamedIndividual);
        }

        //owl:sameAs producers.

        /// <summary>Whether eq-sym concludes <paramref name="fact"/> — the reverse sameAs is present.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveEqSym(EncodedTriple fact)
        {
            return Present(fact.Object, Terms.SameAs, fact.Subject);
        }

        /// <summary>Whether eq-trans concludes <paramref name="fact"/> — an intermediate bridges the subject and the object over sameAs.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveEqTrans(EncodedTriple fact)
        {
            foreach(TermId middle in ObjectsOf(fact.Subject, Terms.SameAs))
            {
                if(Present(middle, Terms.SameAs, fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether prp-fp concludes <paramref name="fact"/> — a functional property maps one subject to both distinct ends.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederivePrpFp(EncodedTriple fact)
        {
            if(fact.Subject == fact.Object || !InstancesOf.TryGetValue(Terms.FunctionalProperty, out List<TermId>? functionals))
            {
                return false;
            }

            foreach(TermId property in functionals)
            {
                foreach(TermId key in SubjectsOf(fact.Subject, property))
                {
                    if(Present(key, property, fact.Object))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Whether prp-ifp concludes <paramref name="fact"/> — an inverse-functional property maps both distinct ends to one value.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederivePrpIfp(EncodedTriple fact)
        {
            if(fact.Subject == fact.Object || !InstancesOf.TryGetValue(Terms.InverseFunctionalProperty, out List<TermId>? inverseFunctionals))
            {
                return false;
            }

            foreach(TermId property in inverseFunctionals)
            {
                foreach(TermId key in ObjectsOf(fact.Subject, property))
                {
                    if(Present(fact.Object, property, key))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Whether prp-key concludes <paramref name="fact"/> — both distinct ends instance a keyed class and share a value for every key property.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederivePrpKey(EncodedTriple fact)
        {
            if(fact.Subject == fact.Object)
            {
                return false;
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.HasKey))
            {
                if(ListOf(listHead) is not List<TermId> keys || keys.Count == 0 || !HasType(fact.Subject, c) || !HasType(fact.Object, c))
                {
                    continue;
                }

                bool sharesAll = true;
                foreach(TermId key in keys)
                {
                    if(!TryGetSharedValue(fact.Subject, fact.Object, key, out TermId _))
                    {
                        sharesAll = false;

                        break;
                    }
                }

                if(sharesAll)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether cls-maxc2 concludes <paramref name="fact"/> — a one-bounded max-cardinality restriction whose instance reaches both distinct ends over the restricted property.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveClsMaxc2(EncodedTriple fact)
        {
            if(fact.Subject == fact.Object)
            {
                return false;
            }

            foreach((TermId restriction, TermId bound) in Pairs(Terms.MaxCardinality))
            {
                if(!Terms.OneBounds.Contains(bound))
                {
                    continue;
                }

                foreach(TermId property in ObjectsOf(restriction, Terms.OnProperty))
                {
                    foreach(TermId instance in SubjectsOf(fact.Subject, property))
                    {
                        if(HasType(instance, restriction) && Present(instance, property, fact.Object))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>Whether cls-maxqc4 concludes <paramref name="fact"/> — a one-bounded qualified max-cardinality restriction whose instance reaches both distinct, filler-qualified ends over the restricted property.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveClsMaxqc4(EncodedTriple fact)
        {
            if(fact.Subject == fact.Object)
            {
                return false;
            }

            //The rule requires the owl:onClass triple; an absent onClass
            //matches nothing, mirroring the forward closure.
            foreach((TermId restriction, TermId bound) in Pairs(Terms.MaxQualifiedCardinality))
            {
                if(!Terms.OneBounds.Contains(bound))
                {
                    continue;
                }

                foreach(TermId filler in ObjectsOf(restriction, Terms.OnClass))
                {
                    if(filler != Terms.Thing && (!HasType(fact.Subject, filler) || !HasType(fact.Object, filler)))
                    {
                        continue;
                    }

                    foreach(TermId property in ObjectsOf(restriction, Terms.OnProperty))
                    {
                        foreach(TermId instance in SubjectsOf(fact.Subject, property))
                        {
                            if(HasType(instance, restriction) && Present(instance, property, fact.Object))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        //Type producers.

        /// <summary>Whether prp-dom concludes <paramref name="fact"/> — a property with the object as domain has an edge out of the subject.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederivePrpDom(EncodedTriple fact)
        {
            foreach(TermId property in SubjectsOf(fact.Object, Terms.Domain))
            {
                if(ObjectsOf(fact.Subject, property).Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether prp-rng concludes <paramref name="fact"/> — a property with the object as range has an edge into the subject, and the subject lies inside the range's value space.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederivePrpRng(EncodedTriple fact)
        {
            foreach(TermId property in SubjectsOf(fact.Object, Terms.Range))
            {
                if(SubjectsOf(fact.Subject, property).Count > 0 && !DatatypeOracle.LiteralOutsideDatatype(fact.Subject, fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether chain-trans concludes <paramref name="fact"/> — the subject heads a chain <c>p ∘ p ⊑ p</c> that states transitivity.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveChainTransitivity(EncodedTriple fact)
        {
            if(fact.Object != Terms.TransitiveProperty)
            {
                return false;
            }

            foreach(TermId listHead in ObjectsOf(fact.Subject, Terms.PropertyChainAxiom))
            {
                if(ListOf(listHead) is List<TermId> chain && chain.Count == 2 && chain[0] == fact.Subject && chain[1] == fact.Subject)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether cls-int1 concludes <paramref name="fact"/> — the subject instances every member of the object's intersection.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveClsInt1(EncodedTriple fact)
        {
            foreach(TermId listHead in ObjectsOf(fact.Object, Terms.IntersectionOf))
            {
                if(ListOf(listHead) is not List<TermId> members || members.Count == 0)
                {
                    continue;
                }

                bool inAll = true;
                foreach(TermId member in members)
                {
                    if(!HasType(fact.Subject, member))
                    {
                        inAll = false;

                        break;
                    }
                }

                if(inAll)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether cls-int2 concludes <paramref name="fact"/> — the subject instances an intersection that lists the object as a member.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveClsInt2(EncodedTriple fact)
        {
            foreach((TermId c, TermId listHead) in Pairs(Terms.IntersectionOf))
            {
                if(!HasType(fact.Subject, c))
                {
                    continue;
                }

                if(ListOf(listHead) is List<TermId> members && members.Contains(fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether cls-uni concludes <paramref name="fact"/> — the subject instances some member of the object's union.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveClsUni(EncodedTriple fact)
        {
            foreach(TermId listHead in ObjectsOf(fact.Object, Terms.UnionOf))
            {
                if(ListOf(listHead) is not List<TermId> members)
                {
                    continue;
                }

                foreach(TermId member in members)
                {
                    if(HasType(fact.Subject, member))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Whether cls-svf1 concludes <paramref name="fact"/> — a some-values restriction (object) on a property along which the subject reaches a filler-typed value.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveClsSvf1(EncodedTriple fact)
        {
            foreach(TermId someFiller in ObjectsOf(fact.Object, Terms.SomeValuesFrom))
            {
                if(someFiller == Terms.Thing)
                {
                    continue;
                }

                foreach(TermId property in ObjectsOf(fact.Object, Terms.OnProperty))
                {
                    foreach(TermId value in ObjectsOf(fact.Subject, property))
                    {
                        if(HasType(value, someFiller))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>Whether cls-svf2 concludes <paramref name="fact"/> — a some-values-from-<c>owl:Thing</c> restriction (object) on a property along which the subject has any edge.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveClsSvf2(EncodedTriple fact)
        {
            if(!ObjectsOf(fact.Object, Terms.SomeValuesFrom).Contains(Terms.Thing))
            {
                return false;
            }

            foreach(TermId property in ObjectsOf(fact.Object, Terms.OnProperty))
            {
                if(ObjectsOf(fact.Subject, property).Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether cls-avf concludes <paramref name="fact"/> — an all-values restriction asserting the object as a filler types the subject as a value of an instance, inside the filler's value space.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveClsAvf(EncodedTriple fact)
        {
            foreach(TermId restriction in SubjectsOf(fact.Object, Terms.AllValuesFrom))
            {
                foreach(TermId property in ObjectsOf(restriction, Terms.OnProperty))
                {
                    foreach(TermId instance in SubjectsOf(fact.Subject, property))
                    {
                        if(HasType(instance, restriction) && !DatatypeOracle.LiteralOutsideDatatype(fact.Subject, fact.Object))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>Whether cls-hv2 concludes <paramref name="fact"/> — a has-value restriction (object) on a property carrying any of the restriction's asserted values out of the subject.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveClsHv2(EncodedTriple fact)
        {
            foreach(TermId value in ObjectsOf(fact.Object, Terms.HasValue))
            {
                foreach(TermId property in ObjectsOf(fact.Object, Terms.OnProperty))
                {
                    if(Present(fact.Subject, property, value))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Whether cls-oo concludes <paramref name="fact"/> — the subject is an enumerated member of the object's one-of.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveClsOo(EncodedTriple fact)
        {
            foreach(TermId listHead in ObjectsOf(fact.Object, Terms.OneOf))
            {
                if(ListOf(listHead) is List<TermId> members && members.Contains(fact.Subject))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether cax-sco concludes <paramref name="fact"/> — the subject instances a subclass of the object.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveCaxSco(EncodedTriple fact)
        {
            foreach(TermId subClass in SubjectsOf(fact.Object, Terms.SubClassOf))
            {
                if(HasType(fact.Subject, subClass))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether cax-eqc1 concludes <paramref name="fact"/> — the subject instances a class equivalent to the object, the object as the pair's second member.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveCaxEqc1(EncodedTriple fact)
        {
            foreach(TermId equivalent in SubjectsOf(fact.Object, Terms.EquivalentClass))
            {
                if(HasType(fact.Subject, equivalent))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether cax-eqc2 concludes <paramref name="fact"/> — the subject instances a class equivalent to the object, the object as the pair's first member.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveCaxEqc2(EncodedTriple fact)
        {
            foreach(TermId equivalent in ObjectsOf(fact.Object, Terms.EquivalentClass))
            {
                if(HasType(fact.Subject, equivalent))
                {
                    return true;
                }
            }

            return false;
        }

        //SubClassOf and EquivalentClass producers.

        /// <summary>Whether scm-cls concludes <paramref name="fact"/> — the reflexive, top, and bottom schema edges of a declared class, and the reflexive equivalence.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmCls(EncodedTriple fact)
        {
            return fact.Predicate switch
            {
                _ when fact.Predicate == Terms.SubClassOf =>
                    (fact.Subject == fact.Object && HasType(fact.Subject, Terms.ClassTerm))
                    || (fact.Object == Terms.Thing && HasType(fact.Subject, Terms.ClassTerm))
                    || (fact.Subject == Terms.Nothing && HasType(fact.Object, Terms.ClassTerm)),
                _ when fact.Predicate == Terms.EquivalentClass =>
                    fact.Subject == fact.Object && HasType(fact.Subject, Terms.ClassTerm),
                _ => false,
            };
        }

        /// <summary>Whether scm-sco concludes <paramref name="fact"/> — a subclass step composes through an intermediate class.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmSco(EncodedTriple fact)
        {
            foreach(TermId middle in ObjectsOf(fact.Subject, Terms.SubClassOf))
            {
                if(Present(middle, Terms.SubClassOf, fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether scm-eqc1 concludes <paramref name="fact"/> — a subclass edge or the equivalence flip of a present equivalent-class pair.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmEqc1(EncodedTriple fact)
        {
            return fact.Predicate switch
            {
                _ when fact.Predicate == Terms.SubClassOf =>
                    Present(fact.Subject, Terms.EquivalentClass, fact.Object) || Present(fact.Object, Terms.EquivalentClass, fact.Subject),
                _ when fact.Predicate == Terms.EquivalentClass =>
                    Present(fact.Object, Terms.EquivalentClass, fact.Subject),
                _ => false,
            };
        }

        /// <summary>Whether scm-eqc2 concludes <paramref name="fact"/> — mutual subclass edges between the endpoints make them equivalent.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmEqc2(EncodedTriple fact)
        {
            return Present(fact.Subject, Terms.SubClassOf, fact.Object) && Present(fact.Object, Terms.SubClassOf, fact.Subject);
        }

        /// <summary>Whether scm-int concludes <paramref name="fact"/> — the subject's intersection lists the object as a member, so the intersection is below it.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmInt(EncodedTriple fact)
        {
            foreach(TermId listHead in ObjectsOf(fact.Subject, Terms.IntersectionOf))
            {
                if(ListOf(listHead) is List<TermId> members && members.Contains(fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether scm-uni concludes <paramref name="fact"/> — the object's union lists the subject as a member, so the member is below the union.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmUni(EncodedTriple fact)
        {
            foreach(TermId listHead in ObjectsOf(fact.Object, Terms.UnionOf))
            {
                if(ListOf(listHead) is List<TermId> members && members.Contains(fact.Subject))
                {
                    return true;
                }
            }

            return false;
        }

        //SubPropertyOf and EquivalentProperty producers.

        /// <summary>Whether scm-spo concludes <paramref name="fact"/> — a subproperty step composes through an intermediate property.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmSpo(EncodedTriple fact)
        {
            foreach(TermId middle in ObjectsOf(fact.Subject, Terms.SubPropertyOf))
            {
                if(Present(middle, Terms.SubPropertyOf, fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether scm-eqp1 concludes <paramref name="fact"/> — a subproperty edge or the equivalence flip of a present equivalent-property pair.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmEqp1(EncodedTriple fact)
        {
            return fact.Predicate switch
            {
                _ when fact.Predicate == Terms.SubPropertyOf =>
                    Present(fact.Subject, Terms.EquivalentProperty, fact.Object) || Present(fact.Object, Terms.EquivalentProperty, fact.Subject),
                _ when fact.Predicate == Terms.EquivalentProperty =>
                    Present(fact.Object, Terms.EquivalentProperty, fact.Subject),
                _ => false,
            };
        }

        /// <summary>Whether scm-eqp2 concludes <paramref name="fact"/> — mutual subproperty edges between the endpoints make them equivalent.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmEqp2(EncodedTriple fact)
        {
            return Present(fact.Subject, Terms.SubPropertyOf, fact.Object) && Present(fact.Object, Terms.SubPropertyOf, fact.Subject);
        }

        //Domain and Range producers.

        /// <summary>Whether scm-dom1 concludes <paramref name="fact"/> — the subject property has a domain that is a subclass of the object.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmDom1(EncodedTriple fact)
        {
            foreach(TermId domain in ObjectsOf(fact.Subject, Terms.Domain))
            {
                if(Present(domain, Terms.SubClassOf, fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether scm-dom2 concludes <paramref name="fact"/> — a super-property of the subject has the object as a domain.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmDom2(EncodedTriple fact)
        {
            foreach(TermId superProperty in ObjectsOf(fact.Subject, Terms.SubPropertyOf))
            {
                if(Present(superProperty, Terms.Domain, fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether scm-rng1 concludes <paramref name="fact"/> — the subject property has a range that is a subclass of the object.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmRng1(EncodedTriple fact)
        {
            foreach(TermId range in ObjectsOf(fact.Subject, Terms.Range))
            {
                if(Present(range, Terms.SubClassOf, fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether scm-rng2 concludes <paramref name="fact"/> — a super-property of the subject has the object as a range.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveScmRng2(EncodedTriple fact)
        {
            foreach(TermId superProperty in ObjectsOf(fact.Subject, Terms.SubPropertyOf))
            {
                if(Present(superProperty, Terms.Range, fact.Object))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether dt-range-intersection concludes <paramref name="fact"/> — two distinct present ranges of the subject property confine the value space to an intersection the object's value space contains.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveDtRangeIntersection(EncodedTriple fact)
        {
            List<TermId> ranges = ObjectsOf(fact.Subject, Terms.Range);
            for(int i = 0; i < ranges.Count; i++)
            {
                for(int j = i + 1; j < ranges.Count; j++)
                {
                    TermId first = ranges[i];
                    TermId second = ranges[j];
                    if(first == second || first == fact.Object || second == fact.Object)
                    {
                        continue;
                    }

                    foreach(TermId superset in DatatypeOracle.RangeIntersectionSupersets(first, second))
                    {
                        if(superset == fact.Object)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        //DifferentFrom, DisjointWith, and PropertyDisjointWith producers.

        /// <summary>Whether different-from symmetry concludes <paramref name="fact"/> — the reverse differentFrom is present.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveDifferentFromSymmetry(EncodedTriple fact)
        {
            return Present(fact.Object, Terms.DifferentFrom, fact.Subject);
        }

        /// <summary>Whether cax-dw concludes <paramref name="fact"/> — the reverse disjointWith is present.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveCaxDw(EncodedTriple fact)
        {
            return Present(fact.Object, Terms.DisjointWith, fact.Subject);
        }

        /// <summary>Whether cax-adc concludes <paramref name="fact"/> — an <c>owl:AllDisjointClasses</c> members list holds the subject before the object, the sole producer of that orientation.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveCaxAdc(EncodedTriple fact)
        {
            if(!InstancesOf.TryGetValue(Terms.AllDisjointClasses, out List<TermId>? nodes))
            {
                return false;
            }

            foreach(TermId node in nodes)
            {
                foreach(TermId head in ObjectsOf(node, Terms.Members))
                {
                    if(ListOf(head) is not List<TermId> members)
                    {
                        continue;
                    }

                    for(int i = 0; i < members.Count; i++)
                    {
                        if(members[i] != fact.Subject)
                        {
                            continue;
                        }

                        for(int j = i + 1; j < members.Count; j++)
                        {
                            if(members[j] == fact.Object)
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>Whether prp-pdw concludes <paramref name="fact"/> — the reverse propertyDisjointWith is present.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederivePrpPdw(EncodedTriple fact)
        {
            return Present(fact.Object, Terms.PropertyDisjointWith, fact.Subject);
        }

        /// <summary>Whether prp-adp concludes <paramref name="fact"/> — an <c>owl:AllDisjointProperties</c> members list holds the subject before the object, the sole producer of that orientation.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederivePrpAdp(EncodedTriple fact)
        {
            if(!InstancesOf.TryGetValue(Terms.AllDisjointProperties, out List<TermId>? nodes))
            {
                return false;
            }

            foreach(TermId node in nodes)
            {
                foreach(TermId head in ObjectsOf(node, Terms.Members))
                {
                    if(ListOf(head) is not List<TermId> members)
                    {
                        continue;
                    }

                    for(int i = 0; i < members.Count; i++)
                    {
                        if(members[i] != fact.Subject)
                        {
                            continue;
                        }

                        for(int j = i + 1; j < members.Count; j++)
                        {
                            if(members[j] == fact.Object)
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        //RDF-Based completion producers.

        /// <summary>Whether the inverse-characteristic transfer concludes <paramref name="fact"/> — a functional or inverse-functional typing whose subject has an <c>owl:inverseOf</c> partner, on either orientation, carrying the exchanged characteristic.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveInverseCharacteristicTransfer(EncodedTriple fact)
        {
            TermId required;
            if(fact.Object == Terms.InverseFunctionalProperty)
            {
                required = Terms.FunctionalProperty;
            }
            else if(fact.Object == Terms.FunctionalProperty)
            {
                required = Terms.InverseFunctionalProperty;
            }
            else
            {
                return false;
            }

            foreach(TermId partner in ObjectsOf(fact.Subject, Terms.InverseOf))
            {
                if(HasType(partner, required))
                {
                    return true;
                }
            }

            foreach(TermId partner in SubjectsOf(fact.Subject, Terms.InverseOf))
            {
                if(HasType(partner, required))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether the singleton-enumeration characteristic concludes <paramref name="fact"/> — a functional typing whose subject has a singleton-enumeration range, or an inverse-functional typing whose subject has a singleton-enumeration domain.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveSingletonEnumerationCharacteristic(EncodedTriple fact)
        {
            TermId confiningPredicate;
            if(fact.Object == Terms.FunctionalProperty)
            {
                confiningPredicate = Terms.Range;
            }
            else if(fact.Object == Terms.InverseFunctionalProperty)
            {
                confiningPredicate = Terms.Domain;
            }
            else
            {
                return false;
            }

            foreach(TermId c in ObjectsOf(fact.Subject, confiningPredicate))
            {
                foreach(TermId head in ObjectsOf(c, Terms.OneOf))
                {
                    if(ListOf(head) is List<TermId> members && members.Count == 1)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Whether complement-of symmetry concludes <paramref name="fact"/> — the reverse complementOf is present.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveComplementOfSymmetry(EncodedTriple fact)
        {
            return Present(fact.Object, Terms.ComplementOf, fact.Subject);
        }

        /// <summary>Whether the one-of member subset concludes <paramref name="fact"/> — an enumeration list of the subject whose member set is contained in one of the object's.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveOneOfMemberSubset(EncodedTriple fact)
        {
            return RederiveMemberSubset(fact, Terms.OneOf);
        }

        /// <summary>Whether the union-of member subset concludes <paramref name="fact"/> — a union list of the subject whose disjunct set is contained in one of the object's.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveUnionOfMemberSubset(EncodedTriple fact)
        {
            return RederiveMemberSubset(fact, Terms.UnionOf);
        }

        /// <summary>Whether a member-subset comparison under <paramref name="constructor"/> concludes the subclass statement <paramref name="fact"/> — some list of the subject reads as a set contained in some list of the object, the two ends distinct.</summary>
        /// <param name="fact">The candidate subclass fact.</param>
        /// <param name="constructor">The order-insensitive constructor the comparison reads.</param>
        /// <returns><c>true</c> when the comparison confirms <paramref name="fact"/>.</returns>
        private bool RederiveMemberSubset(EncodedTriple fact, TermId constructor)
        {
            if(fact.Subject == fact.Object)
            {
                return false;
            }

            foreach(TermId subjectHead in ObjectsOf(fact.Subject, constructor))
            {
                if(ListOf(subjectHead) is not List<TermId> subjectMembers)
                {
                    continue;
                }

                HashSet<TermId> subjectSet = [.. subjectMembers];
                foreach(TermId objectHead in ObjectsOf(fact.Object, constructor))
                {
                    if(ListOf(objectHead) is List<TermId> objectMembers && subjectSet.IsSubsetOf(objectMembers))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        //Transitivity-chain structure producer.

        /// <summary>Whether trans-chain concludes <paramref name="fact"/> — one of the five deterministic chain-structure triples of a transitive property.</summary>
        /// <param name="fact">The candidate fact.</param>
        /// <returns><c>true</c> when the entry confirms <paramref name="fact"/>.</returns>
        private bool RederiveTransitivityChain(EncodedTriple fact)
        {
            if(!InstancesOf.TryGetValue(Terms.TransitiveProperty, out List<TermId>? transitives))
            {
                return false;
            }

            foreach(TermId property in transitives)
            {
                TermId head = Terms.TransitivityChainNode(property, 0);
                TermId tail = Terms.TransitivityChainNode(property, 1);
                bool match = (fact.Subject == property && fact.Predicate == Terms.PropertyChainAxiom && fact.Object == head)
                    || (fact.Subject == head && fact.Predicate == Terms.First && fact.Object == property)
                    || (fact.Subject == head && fact.Predicate == Terms.Rest && fact.Object == tail)
                    || (fact.Subject == tail && fact.Predicate == Terms.First && fact.Object == property)
                    || (fact.Subject == tail && fact.Predicate == Terms.Rest && fact.Object == Terms.Nil);
                if(match)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
