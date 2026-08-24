using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// The census-first enumeration-CSP habitat recognizer's survey-time shape
/// gate: classifies one module as Shape N (nominal-funnel counting), Shape E
/// (role-free enumeration algebra), Shape G (the nominal-free
/// boolean-cardinality gadget), Shape P (the nominal-free partition-counting
/// template), Shape S (the spy-point domain-bound encoding), Shape B (the
/// bijection-chain cardinality arithmetic), Shape R (the restriction-rich
/// ground ontology), Shape W (the told-ground witness
/// encoding), Shape M (the bounded skolem-expansion modal module), Shape K (the
/// branching modal-gadget module), Shape D (the diagonal-pinned role module),
/// mixed, or
/// none,
/// from axiom shapes alone.
/// The gate is syntactic, side-effect-free, and zero-allocation on the none
/// path: the probes match told axiom surfaces and told equivalence sides in
/// place, and the two probes that own containers — the branching modal-gadget
/// layer scan and the enumeration-algebra grammar walk — allocate only once
/// their own allocation-free clauses have matched.
/// The order the probes answer in, and each probe's admission on each census
/// path, are declared in the registry table <see cref="ProbeOrder"/>, which is
/// the single ordering surface; this file holds the shape predicates alone.
/// The class is a census label the
/// assembled statistics and trace records carry — the decider faces apply
/// their own exact jurisdiction predicates (the funnel-chain reachability
/// walk, the closed-world signature admission) independently, so the gate
/// may classify a module the faces stay silent on, never the reverse claim.
/// The shape predicates here are the single source of truth the faces reuse.
/// </summary>
internal static partial class ContextHabitatRecognizer
{
    /// <summary>The Shape E row's match step: the whole-module positive closed-world enumeration-algebra admission carrying at least one one-of.</summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see cref="EnumerationHabitatClass.EnumerationAlgebra"/> on the row's signal, otherwise <see cref="EnumerationHabitatClass.None"/>.</returns>
    private static EnumerationHabitatClass MatchEnumerationAlgebra(ReasoningModule module)
    {
        return IsEnumerationAlgebraModule(module, out bool hasOneOf) && hasOneOf
            ? EnumerationHabitatClass.EnumerationAlgebra
            : EnumerationHabitatClass.None;
    }

    /// <summary>The Shape N row's match step: the funnel-and-cap scan over the module's told subclass axioms — a direct <c>Thing ⊑ ∃r⁻.O</c> funnel, or a <c>Thing ⊑ A</c> chain opening beside a named-class funnel step, together with a one-of-anchored max cap — answering the mixed label where an enumeration-algebra one-of axiom stands beside those signals, the scan reading every told subclass axiom rather than stopping at the first hit.</summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see cref="EnumerationHabitatClass.Mixed"/> or <see cref="EnumerationHabitatClass.NominalCounting"/> on the row's signal, otherwise <see cref="EnumerationHabitatClass.None"/>.</returns>
    private static EnumerationHabitatClass MatchNominalFunnelAndCap(ReasoningModule module)
    {
        bool hasDirectFunnel = false;
        bool hasChainOpening = false;
        bool hasNamedFunnelStep = false;
        bool hasCap = false;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(axiom is not OwlSubClassOfAxiom subClass)
            {
                continue;
            }

            if(TryMatchFunnelShape(subClass.SuperClass, out _, out _))
            {
                if(IsThingReference(subClass.SubClass))
                {
                    hasDirectFunnel = true;
                }
                else if(IsChainNodeClass(subClass.SubClass))
                {
                    hasNamedFunnelStep = true;
                }
            }
            else if(IsThingReference(subClass.SubClass) && IsChainNodeClass(subClass.SuperClass))
            {
                hasChainOpening = true;
            }

            if(TryMatchCapShape(subClass, out _, out _, out _, out _))
            {
                hasCap = true;
            }
        }

        if(hasCap && (hasDirectFunnel || (hasChainOpening && hasNamedFunnelStep)))
        {
            return HasEnumerationClusterAxiom(module) ? EnumerationHabitatClass.Mixed : EnumerationHabitatClass.NominalCounting;
        }

        return EnumerationHabitatClass.None;
    }

    /// <summary>The Shape K row's match step: the branching modal-gadget module's structural signal (<see cref="TryMatchModalGadgetTreeShape"/>).</summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see cref="EnumerationHabitatClass.ModalGadgetTree"/> on the row's signal, otherwise <see cref="EnumerationHabitatClass.None"/>.</returns>
    private static EnumerationHabitatClass MatchModalGadgetTree(ReasoningModule module)
    {
        return TryMatchModalGadgetTreeShape(module)
            ? EnumerationHabitatClass.ModalGadgetTree
            : EnumerationHabitatClass.None;
    }

    /// <summary>The Shape G row's match step: the boolean-cardinality gadget's structural signal (<see cref="TryMatchGadgetShape"/>).</summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see cref="EnumerationHabitatClass.BooleanCardinalityGadget"/> on the row's signal, otherwise <see cref="EnumerationHabitatClass.None"/>.</returns>
    private static EnumerationHabitatClass MatchBooleanCardinalityGadget(ReasoningModule module)
    {
        return TryMatchGadgetShape(module)
            ? EnumerationHabitatClass.BooleanCardinalityGadget
            : EnumerationHabitatClass.None;
    }

    /// <summary>The Shape P row's match step: the partition template's structural signal (<see cref="TryMatchPartitionShape"/>).</summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see cref="EnumerationHabitatClass.PartitionCounting"/> on the row's signal, otherwise <see cref="EnumerationHabitatClass.None"/>.</returns>
    private static EnumerationHabitatClass MatchPartitionCounting(ReasoningModule module)
    {
        return TryMatchPartitionShape(module)
            ? EnumerationHabitatClass.PartitionCounting
            : EnumerationHabitatClass.None;
    }

    /// <summary>The Shape S row's match step: the spy-point encoding's structural signal (<see cref="TryMatchSpyPointShape"/>).</summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see cref="EnumerationHabitatClass.SpyPointDomainBound"/> on the row's signal, otherwise <see cref="EnumerationHabitatClass.None"/>.</returns>
    private static EnumerationHabitatClass MatchSpyPointDomainBound(ReasoningModule module)
    {
        return TryMatchSpyPointShape(module)
            ? EnumerationHabitatClass.SpyPointDomainBound
            : EnumerationHabitatClass.None;
    }

    /// <summary>The Shape B row's match step: the bijection-chain encoding's structural signal (<see cref="TryMatchBijectionChainShape"/>).</summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see cref="EnumerationHabitatClass.BijectionChainArithmetic"/> on the row's signal, otherwise <see cref="EnumerationHabitatClass.None"/>.</returns>
    private static EnumerationHabitatClass MatchBijectionChainArithmetic(ReasoningModule module)
    {
        return TryMatchBijectionChainShape(module)
            ? EnumerationHabitatClass.BijectionChainArithmetic
            : EnumerationHabitatClass.None;
    }

    /// <summary>The Shape R row's match step: the restriction-rich ground encoding's structural signal (<see cref="TryMatchRestrictionRichGroundShape"/>).</summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see cref="EnumerationHabitatClass.RestrictionRichGround"/> on the row's signal, otherwise <see cref="EnumerationHabitatClass.None"/>.</returns>
    private static EnumerationHabitatClass MatchRestrictionRichGround(ReasoningModule module)
    {
        return TryMatchRestrictionRichGroundShape(module)
            ? EnumerationHabitatClass.RestrictionRichGround
            : EnumerationHabitatClass.None;
    }

    /// <summary>The Shape W row's match step: the told-ground witness encoding's structural signal (<see cref="TryMatchToldGroundWitnessShape"/>).</summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see cref="EnumerationHabitatClass.ToldGroundWitness"/> on the row's signal, otherwise <see cref="EnumerationHabitatClass.None"/>.</returns>
    private static EnumerationHabitatClass MatchToldGroundWitness(ReasoningModule module)
    {
        return TryMatchToldGroundWitnessShape(module)
            ? EnumerationHabitatClass.ToldGroundWitness
            : EnumerationHabitatClass.None;
    }

    /// <summary>The Shape M row's match step: the bounded skolem-expansion modal module's structural signal (<see cref="TryMatchModalRoleExpansionShape"/>).</summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see cref="EnumerationHabitatClass.ModalRoleExpansion"/> on the row's signal, otherwise <see cref="EnumerationHabitatClass.None"/>.</returns>
    private static EnumerationHabitatClass MatchModalRoleExpansion(ReasoningModule module)
    {
        return TryMatchModalRoleExpansionShape(module)
            ? EnumerationHabitatClass.ModalRoleExpansion
            : EnumerationHabitatClass.None;
    }

    /// <summary>The Shape D row's match step: the diagonal-pinned role module's structural signal (<see cref="TryMatchNominalPinnedRoleShape"/>).</summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see cref="EnumerationHabitatClass.NominalPinnedRole"/> on the row's signal, otherwise <see cref="EnumerationHabitatClass.None"/>.</returns>
    private static EnumerationHabitatClass MatchNominalPinnedRole(ReasoningModule module)
    {
        return TryMatchNominalPinnedRoleShape(module)
            ? EnumerationHabitatClass.NominalPinnedRole
            : EnumerationHabitatClass.None;
    }

    /// <summary>
    /// The Shape K composition threshold: a module needs MORE than this many
    /// binary-intersection equivalences on named subjects before the branching
    /// modal-gadget signal is considered. The clause is a HABITAT-IDENTITY
    /// condition rather than a looseness one — the composition layer is what the
    /// monotone clash face consumes, and it is the quantity that separates a
    /// branching modal-gadget module from an ordinary cardinality-gadget module,
    /// whose own census signal is satisfied by a SINGLE named intersection. A
    /// module carrying a composition layer below the threshold is decidable by
    /// the modal-gadget faces and unreachable through this probe: that is a PROBE
    /// reach loss and never a wrong verdict, which is the direction the house
    /// probe doctrine prefers.
    /// The value is not a boundary the habitat itself imposes and is deliberately
    /// not set at the smallest value that admits a branching module: a threshold
    /// sitting one axiom above the largest composition layer a sibling shape
    /// carries decides by coin toss which of the two the probe claims, so it is
    /// placed clear of both sides instead — well above an incidental handful of
    /// named intersections and well below the composition breadth a branching
    /// modal-gadget module is defined by.
    /// </summary>
    private const int ModalGadgetCompositionThreshold = 32;

    /// <summary>
    /// Counts the module's COMPOSITION LAYER: the told equivalences pairing a
    /// named-class subject with a strictly BINARY intersection whose two operands
    /// are named-class references. That is the exact axiom shape the monotone
    /// composition rule consumes, so the count is the quantity the Shape K
    /// threshold is charged against and the quantity the habitat-label census
    /// instrument reports beside the label. The scan allocates nothing: it matches
    /// told equivalence sides in place.
    /// </summary>
    /// <param name="module">The module to count over.</param>
    /// <returns>The composition-layer axiom count.</returns>
    public static int CountModalGadgetCompositions(ReasoningModule module)
    {
        int compositions = 0;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(axiom is OwlEquivalentClassesAxiom equivalent && IsModalGadgetComposition(equivalent))
            {
                compositions++;
            }
        }

        return compositions;
    }

    /// <summary>Whether one told equivalence is a composition-layer axiom: one side a named-class reference and the other a binary intersection of named-class references. The name side is identified by CONSTRUCT and never by argument position, since the abstract syntax is unordered.</summary>
    /// <param name="equivalent">The told equivalence.</param>
    /// <returns><see langword="true"/> on the composition shape.</returns>
    private static bool IsModalGadgetComposition(OwlEquivalentClassesAxiom equivalent)
    {
        return (equivalent.First is OwlClassReference && IsBinaryNamedIntersection(equivalent.Second))
            || (equivalent.Second is OwlClassReference && IsBinaryNamedIntersection(equivalent.First));
    }

    /// <summary>Whether one class expression is a strictly binary intersection whose two operands are both named-class references. Arity is part of the shape: an intersection of three or more operands is a different axiom and is never operand-paired into this one.</summary>
    /// <param name="expression">The candidate equivalence side.</param>
    /// <returns><see langword="true"/> on the binary named intersection.</returns>
    private static bool IsBinaryNamedIntersection(OwlClassExpression expression)
    {
        return expression is OwlObjectIntersectionOf { Operands.Count: 2 } intersection
            && intersection.Operands[0] is OwlClassReference
            && intersection.Operands[1] is OwlClassReference;
    }

    /// <summary>
    /// The Shape K probe's bounded scan state: whether every told class surface
    /// stayed inside the branching modal-gadget grammar, the single role the
    /// module quantifies over, how many modal restrictions carry it, and the
    /// properties some cardinality restriction bounds — the two layers the
    /// separation clause compares.
    /// </summary>
    private sealed class ModalGadgetLayerScan
    {
        /// <summary>Whether every told class surface stayed inside the grammar; cleared by a disjunctive construct, a nominal, a has-value, a has-self, a data range, or an inverse-spelled modal role.</summary>
        public bool Admitted { get; set; }

        /// <summary>The single role standing in existential or universal position; <see langword="null"/> until the first modal restriction binds it.</summary>
        public NamedNode? ModalRole { get; set; }

        /// <summary>The modal restrictions carrying that role — existential and universal occurrences together.</summary>
        public int ModalRestrictions { get; set; }

        /// <summary>The property IRIs some cardinality restriction bounds — the gadget layer.</summary>
        public HashSet<Utf8String> CardinalityProperties { get; } = [];
    }

    /// <summary>
    /// The Shape K census signal: whether the module carries the BRANCHING
    /// MODAL-GADGET shape — exactly ONE role in existential or universal position
    /// carrying no characteristic, domain, range, sub-property or inverse pairing;
    /// layer separation between that role and every property a cardinality
    /// restriction bounds; a composition layer above
    /// <see cref="ModalGadgetCompositionThreshold"/>; at least one modal
    /// restriction; and no disjunctive construct, nominal or has-value anywhere.
    /// COMPLEMENTS ARE TOLERATED and no clause is an exact-match test, a
    /// complement-free-module test, an axiom-count test or an individual-count
    /// test, so the label is STABLE under the addition of one complement class
    /// assertion to an existing individual — which is exactly what the
    /// conformance arm's refutation builder adds, and the label must not move
    /// where the dispatch is needed. The complement ban lives on the certify
    /// FACE, where a probe module is silenced rather than un-labelled.
    /// The probe is looser than either face except at the composition threshold,
    /// an habitat-identity condition, and the modal-restriction clause, an
    /// ordering-safety one: a pure-gadget module and a below-threshold module are
    /// both face-admissible and unreachable here, which is a reach cost and never
    /// a soundness one. The composition count runs first and allocates nothing, so
    /// the none path stays allocation-free; the bounded class-surface walk and the
    /// layer scan, which own the probe's only containers, run only once it clears.
    /// </summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see langword="true"/> on the branching modal-gadget module's signal.</returns>
    public static bool TryMatchModalGadgetTreeShape(ReasoningModule module)
    {
        if(CountModalGadgetCompositions(module) <= ModalGadgetCompositionThreshold)
        {
            return false;
        }

        ModalGadgetLayerScan scan = ScanModalGadgetLayers(module);

        return scan.Admitted
            && scan.ModalRole is NamedNode modalRole
            && scan.ModalRestrictions > 0
            && !scan.CardinalityProperties.Contains(modalRole.Iri)
            && SeparatesModalGadgetLayers(module, scan);
    }

    /// <summary>Walks every told class surface of the module with an explicit stack, binding the single modal role, counting its restrictions, and collecting the bounded properties; a surface outside the grammar clears the admission and stops the walk.</summary>
    /// <param name="module">The module to scan.</param>
    /// <returns>The scan state.</returns>
    private static ModalGadgetLayerScan ScanModalGadgetLayers(ReasoningModule module)
    {
        ModalGadgetLayerScan scan = new() { Admitted = true };
        Stack<OwlClassExpression> work = new();
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(axiom is OwlDisjointClassesAxiom or OwlDisjointUnionAxiom)
            {
                scan.Admitted = false;

                return scan;
            }

            AppendModalGadgetClassPositions(axiom, work);
            while(work.Count > 0)
            {
                if(!ScanModalGadgetExpression(work.Pop(), scan, work))
                {
                    scan.Admitted = false;

                    return scan;
                }
            }
        }

        return scan;
    }

    /// <summary>Pushes one axiom's told class positions onto the walk.</summary>
    /// <param name="axiom">The axiom.</param>
    /// <param name="positionsToAppendTo">The walk's stack.</param>
    private static void AppendModalGadgetClassPositions(OwlAxiom axiom, Stack<OwlClassExpression> positionsToAppendTo)
    {
        switch(axiom)
        {
            case(OwlSubClassOfAxiom subClass):
            {
                positionsToAppendTo.Push(subClass.SubClass);
                positionsToAppendTo.Push(subClass.SuperClass);
                break;
            }
            case(OwlEquivalentClassesAxiom equivalent):
            {
                positionsToAppendTo.Push(equivalent.First);
                positionsToAppendTo.Push(equivalent.Second);
                break;
            }
            case(OwlClassAssertionAxiom assertion):
            {
                positionsToAppendTo.Push(assertion.Class);
                break;
            }
            case(OwlObjectPropertyDomainAxiom domain):
            {
                positionsToAppendTo.Push(domain.Domain);
                break;
            }
            case(OwlObjectPropertyRangeAxiom range):
            {
                positionsToAppendTo.Push(range.Range);
                break;
            }
            case(OwlDataPropertyDomainAxiom dataDomain):
            {
                positionsToAppendTo.Push(dataDomain.Domain);
                break;
            }
            case(OwlHasKeyAxiom hasKey):
            {
                positionsToAppendTo.Push(hasKey.Class);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Reads one class expression into the scan, pushing its operands onto the walk. A named class and a complement pass through, an intersection descends, a modal restriction binds the single role, a cardinality restriction records its bounded property, and every other construct — union, one-of, has-value, has-self, a data range shape, an inverse-spelled modal role — is outside the grammar.</summary>
    /// <param name="expression">The class expression.</param>
    /// <param name="scan">The scan state.</param>
    /// <param name="workToAppendTo">The walk's stack.</param>
    /// <returns><see langword="true"/> when the expression stays inside the grammar.</returns>
    private static bool ScanModalGadgetExpression(OwlClassExpression expression, ModalGadgetLayerScan scan, Stack<OwlClassExpression> workToAppendTo)
    {
        switch(expression)
        {
            case(OwlClassReference):
            {
                return true;
            }
            case(OwlObjectIntersectionOf intersection):
            {
                for(int i = 0; i < intersection.Operands.Count; i++)
                {
                    workToAppendTo.Push(intersection.Operands[i]);
                }

                return true;
            }
            case(OwlObjectComplementOf complement):
            {
                workToAppendTo.Push(complement.Operand);

                return true;
            }
            case(OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference existentialRole } existential):
            {
                workToAppendTo.Push(existential.Filler);

                return BindsModalGadgetRole(scan, existentialRole.Named);
            }
            case(OwlObjectAllValuesFrom { Property: OwlObjectPropertyReference universalRole } universal):
            {
                workToAppendTo.Push(universal.Filler);

                return BindsModalGadgetRole(scan, universalRole.Named);
            }
            case(OwlObjectCardinality { Property: OwlObjectPropertyReference boundedRole } cardinality):
            {
                if(cardinality.Filler is not null)
                {
                    workToAppendTo.Push(cardinality.Filler);
                }

                scan.CardinalityProperties.Add(boundedRole.Named.Iri);

                return true;
            }
            case(OwlDataCardinality dataCardinality):
            {
                scan.CardinalityProperties.Add(dataCardinality.Property.Iri);

                return true;
            }
            default:
            {
                return false;
            }
        }
    }

    /// <summary>Binds the module's single modal role on the first restriction that names one and re-checks it by FULL IRI on every later restriction, so two roles sharing a local name across namespaces never pair.</summary>
    /// <param name="scan">The scan state.</param>
    /// <param name="role">The role this restriction quantifies.</param>
    /// <returns><see langword="true"/> when the restriction carries the bound role.</returns>
    private static bool BindsModalGadgetRole(ModalGadgetLayerScan scan, NamedNode role)
    {
        if(scan.ModalRole is null)
        {
            scan.ModalRole = role;
        }
        else if(!scan.ModalRole.Iri.Equals(role.Iri))
        {
            return false;
        }

        scan.ModalRestrictions++;

        return true;
    }

    /// <summary>Whether the module's two layers stay separate: no told property axiom and no told property assertion names the modal role or a property a cardinality restriction bounds, so the modal role occurs only in modal position and every bounded property only inside cardinality restrictions.</summary>
    /// <param name="module">The module to probe.</param>
    /// <param name="scan">The scan state carrying the two layers.</param>
    /// <returns><see langword="true"/> when the layers stay separate.</returns>
    private static bool SeparatesModalGadgetLayers(ReasoningModule module, ModalGadgetLayerScan scan)
    {
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(MentionsModalGadgetPropertyOutOfLayer(axiom, scan))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether one axiom names a layered property in a position that is neither a cardinality restriction nor a modal restriction — a characteristic, a domain, a range, a sub-property, an equivalence, an inverse pairing, a disjointness, a chain, a key, or a told property assertion of either side.</summary>
    /// <param name="axiom">The axiom.</param>
    /// <param name="scan">The scan state carrying the two layers.</param>
    /// <returns><see langword="true"/> when a layered property stands out of its layer.</returns>
    private static bool MentionsModalGadgetPropertyOutOfLayer(OwlAxiom axiom, ModalGadgetLayerScan scan)
    {
        switch(axiom)
        {
            case(OwlObjectPropertyCharacteristicAxiom characteristic):
            {
                return IsLayeredRole(characteristic.Property, scan);
            }
            case(OwlObjectPropertyDomainAxiom domain):
            {
                return IsLayeredRole(domain.Property, scan);
            }
            case(OwlObjectPropertyRangeAxiom range):
            {
                return IsLayeredRole(range.Property, scan);
            }
            case(OwlSubObjectPropertyOfAxiom subRole):
            {
                return IsLayeredRole(subRole.SubProperty, scan) || IsLayeredRole(subRole.SuperProperty, scan);
            }
            case(OwlEquivalentObjectPropertiesAxiom equivalentRoles):
            {
                return IsLayeredRole(equivalentRoles.First, scan) || IsLayeredRole(equivalentRoles.Second, scan);
            }
            case(OwlInverseObjectPropertiesAxiom inverse):
            {
                return IsLayeredRole(inverse.First, scan) || IsLayeredRole(inverse.Second, scan);
            }
            case(OwlDisjointObjectPropertiesAxiom disjointRoles):
            {
                for(int i = 0; i < disjointRoles.Operands.Count; i++)
                {
                    if(IsLayeredRole(disjointRoles.Operands[i], scan))
                    {
                        return true;
                    }
                }

                return false;
            }
            case(OwlPropertyChainAxiom chain):
            {
                for(int i = 0; i < chain.Chain.Count; i++)
                {
                    if(IsLayeredRole(chain.Chain[i], scan))
                    {
                        return true;
                    }
                }

                return IsLayeredRole(chain.SuperProperty, scan);
            }
            case(OwlObjectPropertyAssertionAxiom assertion):
            {
                return IsLayeredProperty(assertion.Property, scan);
            }
            case(OwlNegativeObjectPropertyAssertionAxiom negativeAssertion):
            {
                return IsLayeredRole(negativeAssertion.Property, scan);
            }
            case(OwlDataPropertyAssertionAxiom dataAssertion):
            {
                return IsLayeredProperty(dataAssertion.Property, scan);
            }
            case(OwlNegativeDataPropertyAssertionAxiom negativeDataAssertion):
            {
                return IsLayeredProperty(negativeDataAssertion.Property, scan);
            }
            case(OwlSubDataPropertyOfAxiom subData):
            {
                return IsLayeredProperty(subData.SubProperty, scan) || IsLayeredProperty(subData.SuperProperty, scan);
            }
            case(OwlEquivalentDataPropertiesAxiom equivalentData):
            {
                return IsLayeredProperty(equivalentData.First, scan) || IsLayeredProperty(equivalentData.Second, scan);
            }
            case(OwlDisjointDataPropertiesAxiom disjointData):
            {
                for(int i = 0; i < disjointData.Operands.Count; i++)
                {
                    if(IsLayeredProperty(disjointData.Operands[i], scan))
                    {
                        return true;
                    }
                }

                return false;
            }
            case(OwlDataPropertyDomainAxiom dataDomain):
            {
                return IsLayeredProperty(dataDomain.Property, scan);
            }
            case(OwlDataPropertyRangeAxiom dataRange):
            {
                return IsLayeredProperty(dataRange.Property, scan);
            }
            case(OwlFunctionalDataPropertyAxiom functionalData):
            {
                return IsLayeredProperty(functionalData.Property, scan);
            }
            case(OwlHasKeyAxiom hasKey):
            {
                for(int i = 0; i < hasKey.ObjectProperties.Count; i++)
                {
                    if(IsLayeredRole(hasKey.ObjectProperties[i], scan))
                    {
                        return true;
                    }
                }

                for(int i = 0; i < hasKey.DataProperties.Count; i++)
                {
                    if(IsLayeredProperty(hasKey.DataProperties[i], scan))
                    {
                        return true;
                    }
                }

                return false;
            }
            default:
            {
                return false;
            }
        }
    }

    /// <summary>Whether one object-property expression names a layered property, either spelling.</summary>
    /// <param name="property">The object-property expression.</param>
    /// <param name="scan">The scan state carrying the two layers.</param>
    /// <returns><see langword="true"/> when the expression names a layered property.</returns>
    private static bool IsLayeredRole(OwlObjectPropertyExpression property, ModalGadgetLayerScan scan)
    {
        return property switch
        {
            OwlObjectPropertyReference reference => IsLayeredProperty(reference.Named, scan),
            OwlInverseObjectProperty inverse => IsLayeredProperty(inverse.Inverted, scan),
            _ => false,
        };
    }

    /// <summary>Whether one named property is the modal role or carries a cardinality bound, compared by full IRI.</summary>
    /// <param name="property">The named property.</param>
    /// <param name="scan">The scan state carrying the two layers.</param>
    /// <returns><see langword="true"/> when the property belongs to one of the two layers.</returns>
    private static bool IsLayeredProperty(NamedNode property, ModalGadgetLayerScan scan)
    {
        return scan.CardinalityProperties.Contains(property.Iri)
            || (scan.ModalRole is NamedNode modalRole && modalRole.Iri.Equals(property.Iri));
    }

    /// <summary>
    /// The Shape G census signal: whether the module carries a told equivalence
    /// axiom one side of which is a bare unqualified 0/1 cardinality gadget over
    /// a named property — data or object — AND a told equivalence axiom one side
    /// of which is an intersection of named-class references only. Deliberately
    /// looser than the gadget face's own jurisdiction predicate, so a recognized
    /// module the face stays silent on is an expected, visible census state —
    /// never the reverse claim. The scan allocates nothing: it indexes the told
    /// operand lists in place and matches top-level equivalence sides only.
    /// </summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see langword="true"/> on the gadget module's signal.</returns>
    private static bool TryMatchGadgetShape(ReasoningModule module)
    {
        bool hasGadget = false;
        bool hasNamedIntersection = false;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(axiom is not OwlEquivalentClassesAxiom equivalent)
            {
                continue;
            }

            hasGadget = hasGadget || IsGadgetSignal(equivalent.First) || IsGadgetSignal(equivalent.Second);
            hasNamedIntersection = hasNamedIntersection || IsNamedOnlyIntersection(equivalent.First) || IsNamedOnlyIntersection(equivalent.Second);
            if(hasGadget && hasNamedIntersection)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one class expression is a bare 0/1 cardinality gadget: an unqualified minimum, maximum, or exact cardinality restriction of bound zero or one over a named object property or a data property. The face's own jurisdiction predicate is tighter — it admits only the three boolean forms.</summary>
    /// <param name="expression">The candidate equivalence side.</param>
    /// <returns><see langword="true"/> on the gadget signal.</returns>
    private static bool IsGadgetSignal(OwlClassExpression expression)
    {
        return expression switch
        {
            OwlObjectCardinality { Property: OwlObjectPropertyReference } cardinality => IsUnqualifiedFiller(cardinality.Filler) && IsBooleanBound(cardinality.Cardinality),
            OwlDataCardinality { Range: null } dataCardinality => IsBooleanBound(dataCardinality.Cardinality),
            _ => false,
        };
    }

    /// <summary>Whether a told cardinality bound is one of the two boolean values the gadget encoding uses.</summary>
    /// <param name="bound">The told bound.</param>
    /// <returns><see langword="true"/> for zero or one.</returns>
    private static bool IsBooleanBound(int bound)
    {
        return bound is 0 or 1;
    }

    /// <summary>Whether one class expression is a non-empty intersection whose operands are ALL named-class references — the definitional composition the gadget habitat's classes carry beside their cardinality gadgets.</summary>
    /// <param name="expression">The candidate equivalence side.</param>
    /// <returns><see langword="true"/> on the named-only intersection.</returns>
    private static bool IsNamedOnlyIntersection(OwlClassExpression expression)
    {
        if(expression is not OwlObjectIntersectionOf intersection || intersection.Operands.Count == 0)
        {
            return false;
        }

        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            if(intersection.Operands[i] is not OwlClassReference)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The Shape P census signal: whether some told equivalence axiom equates a
    /// class expression with an intersection whose top-level conjuncts carry at
    /// least one existential restriction and EXACTLY ONE unqualified
    /// max-cardinality restriction, every one of them over the SAME named
    /// object property. Deliberately looser than the partition face's own
    /// jurisdiction predicate, so a recognized module the face stays silent on
    /// is an expected, visible census state — never the reverse claim. The scan
    /// allocates nothing: it indexes the told operand lists in place and matches
    /// top-level conjuncts only.
    /// </summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see langword="true"/> on the partition template's signal.</returns>
    private static bool TryMatchPartitionShape(ReasoningModule module)
    {
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(axiom is not OwlEquivalentClassesAxiom equivalent)
            {
                continue;
            }

            if(IsPartitionIntersection(equivalent.First) || IsPartitionIntersection(equivalent.Second))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one class expression is a partition-template intersection: at least one existential conjunct, exactly one unqualified max-cardinality conjunct, all over one named object property. Conjunct kinds outside the two are ignored here — the face's jurisdiction predicate rejects them.</summary>
    /// <param name="expression">The candidate equivalence side.</param>
    /// <returns><see langword="true"/> on the template intersection.</returns>
    private static bool IsPartitionIntersection(OwlClassExpression expression)
    {
        if(expression is not OwlObjectIntersectionOf intersection)
        {
            return false;
        }

        NamedNode? role = null;
        int existentials = 0;
        int caps = 0;
        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            switch(intersection.Operands[i])
            {
                case(OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference existentialRole }):
                {
                    if(!SharesTemplateRole(ref role, existentialRole.Property))
                    {
                        return false;
                    }

                    existentials++;
                    break;
                }
                case(OwlObjectCardinality { Kind: OwlCardinalityKind.Max, Property: OwlObjectPropertyReference capRole } cap):
                {
                    if(!IsUnqualifiedFiller(cap.Filler))
                    {
                        break;
                    }

                    if(!SharesTemplateRole(ref role, capRole.Property))
                    {
                        return false;
                    }

                    caps++;
                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        return existentials >= 1 && caps == 1;
    }

    /// <summary>Binds the template's single object property on the first conjunct that names one, and re-checks textual identity on every later conjunct.</summary>
    /// <param name="role">The bound role; <see langword="null"/> until the first conjunct binds it.</param>
    /// <param name="candidate">The role this conjunct names.</param>
    /// <returns><see langword="true"/> when the conjunct's role matches the binding.</returns>
    public static bool SharesTemplateRole(ref NamedNode? role, NamedNode candidate)
    {
        if(role is null)
        {
            role = candidate;

            return true;
        }

        return role.Iri.Equals(candidate.Iri);
    }

    /// <summary>Whether a cardinality restriction's filler leaves the count unqualified: no filler at all, or the explicit <c>owl:Thing</c> — the two spellings of the same unrestricted count.</summary>
    /// <param name="filler">The restriction's qualification filler.</param>
    /// <returns><see langword="true"/> for an unqualified count.</returns>
    public static bool IsUnqualifiedFiller(OwlClassExpression? filler)
    {
        return filler is null || IsThingReference(filler);
    }

    /// <summary>
    /// The positive closed-world Shape E admission over the module's ENTIRE
    /// axiom set: admit exactly when every axiom is one of the admitted kinds —
    /// <c>SubClassOf</c>, <c>EquivalentClasses</c>, <c>DisjointClasses</c>,
    /// <c>ClassAssertion</c>, told <c>SameIndividual</c>, told
    /// <c>DifferentIndividuals</c>, or a semantics-free declaration, import, or
    /// annotation axiom — over the named-concept grammar (named classes,
    /// one-ofs of named individuals with at least one member, complement,
    /// union, intersection) with every individual term named; reject
    /// otherwise, never a blacklist. The kind scan runs first and rejects
    /// without allocating; the grammar walk runs only on a kind-complete
    /// module. <c>DisjointUnion</c> is not an admitted kind and rejects the
    /// module whole — the pinned behavior — as do property axioms,
    /// property assertions, has-value, has-self, cardinalities, and every
    /// data-side shape.
    /// </summary>
    /// <param name="module">The module to admit or reject.</param>
    /// <param name="hasOneOf">Whether at least one one-of occurs on an admitted surface.</param>
    /// <returns><see langword="true"/> when every axiom is admitted.</returns>
    public static bool IsEnumerationAlgebraModule(ReasoningModule module, out bool hasOneOf)
    {
        hasOneOf = false;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(!IsEnumerationAlgebraAxiomKind(axiom))
            {
                return false;
            }
        }

        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(!AdmitsEnumerationAlgebraAxiom(axiom, ref hasOneOf))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether the axiom's kind is one of the Shape E admitted kinds — the allocation-free first phase of the closed-world admission.</summary>
    /// <param name="axiom">The axiom.</param>
    /// <returns><see langword="true"/> for an admitted kind.</returns>
    private static bool IsEnumerationAlgebraAxiomKind(OwlAxiom axiom)
    {
        return axiom switch
        {
            OwlSubClassOfAxiom or OwlEquivalentClassesAxiom or OwlDisjointClassesAxiom
                or OwlClassAssertionAxiom or OwlSameIndividualAxiom or OwlDifferentIndividualsAxiom => true,
            OwlDeclarationAxiom or OwlImportAxiom or OwlAnnotationAssertionAxiom or OwlSubAnnotationPropertyOfAxiom
                or OwlAnnotationPropertyDomainAxiom or OwlAnnotationPropertyRangeAxiom => true,
            _ => false,
        };
    }

    /// <summary>Whether one kind-admitted axiom's expression and individual surfaces lie within the Shape E named-concept grammar — the second phase of the closed-world admission.</summary>
    /// <param name="axiom">The kind-admitted axiom.</param>
    /// <param name="hasOneOf">Set when a one-of occurs on an admitted surface.</param>
    /// <returns><see langword="true"/> when every surface is admitted.</returns>
    private static bool AdmitsEnumerationAlgebraAxiom(OwlAxiom axiom, ref bool hasOneOf)
    {
        switch(axiom)
        {
            case(OwlSubClassOfAxiom subClass):
            {
                return IsEnumerationAlgebraConcept(subClass.SubClass, ref hasOneOf) && IsEnumerationAlgebraConcept(subClass.SuperClass, ref hasOneOf);
            }
            case(OwlEquivalentClassesAxiom equivalent):
            {
                return IsEnumerationAlgebraConcept(equivalent.First, ref hasOneOf) && IsEnumerationAlgebraConcept(equivalent.Second, ref hasOneOf);
            }
            case(OwlDisjointClassesAxiom disjoint):
            {
                for(int i = 0; i < disjoint.Operands.Count; i++)
                {
                    if(!IsEnumerationAlgebraConcept(disjoint.Operands[i], ref hasOneOf))
                    {
                        return false;
                    }
                }

                return true;
            }
            case(OwlClassAssertionAxiom assertion):
            {
                return assertion.Individual is NamedNode && IsEnumerationAlgebraConcept(assertion.Class, ref hasOneOf);
            }
            case(OwlSameIndividualAxiom same):
            {
                return same.First is NamedNode && same.Second is NamedNode;
            }
            case(OwlDifferentIndividualsAxiom different):
            {
                for(int i = 0; i < different.Individuals.Count; i++)
                {
                    if(different.Individuals[i] is not NamedNode)
                    {
                        return false;
                    }
                }

                return true;
            }
            default:
            {
                return true;
            }
        }
    }

    /// <summary>
    /// Whether a class expression lies within the Shape E named-concept
    /// grammar: named classes (<c>owl:Thing</c> and <c>owl:Nothing</c>
    /// included), one-ofs of at least one named individual, and complement,
    /// union, and intersection over admitted operands — an explicit-stack
    /// walk that descends into complement subtrees so complement-wrapped
    /// one-of members are first-class.
    /// </summary>
    /// <param name="root">The class expression.</param>
    /// <param name="hasOneOf">Set when a one-of occurs anywhere in the expression.</param>
    /// <returns><see langword="true"/> when the expression is admitted.</returns>
    private static bool IsEnumerationAlgebraConcept(OwlClassExpression root, ref bool hasOneOf)
    {
        Stack<OwlClassExpression> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            OwlClassExpression expression = work.Pop();
            switch(expression)
            {
                case(OwlClassReference):
                {
                    break;
                }
                case(OwlObjectOneOf oneOf):
                {
                    if(oneOf.Individuals.Count == 0)
                    {
                        return false;
                    }

                    for(int i = 0; i < oneOf.Individuals.Count; i++)
                    {
                        if(oneOf.Individuals[i] is not NamedNode)
                        {
                            return false;
                        }
                    }

                    hasOneOf = true;
                    break;
                }
                case(OwlObjectComplementOf complement):
                {
                    work.Push(complement.Operand);
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
                case(OwlObjectIntersectionOf intersection):
                {
                    for(int i = 0; i < intersection.Operands.Count; i++)
                    {
                        work.Push(intersection.Operands[i]);
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

    /// <summary>Whether the module carries an enumeration-algebra one-of axiom — a Shape E admitted kind whose surfaces pass the named-concept grammar and carry at least one one-of. The funnel and cap shapes exclude themselves: both carry a restriction the grammar rejects.</summary>
    /// <param name="module">The module to scan.</param>
    /// <returns><see langword="true"/> when an enumeration-cluster axiom exists.</returns>
    private static bool HasEnumerationClusterAxiom(ReasoningModule module)
    {
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(axiom is OwlDeclarationAxiom or OwlImportAxiom or OwlAnnotationAssertionAxiom or OwlSubAnnotationPropertyOfAxiom
                or OwlAnnotationPropertyDomainAxiom or OwlAnnotationPropertyRangeAxiom)
            {
                continue;
            }

            if(!IsEnumerationAlgebraAxiomKind(axiom))
            {
                continue;
            }

            bool hasOneOf = false;
            if(AdmitsEnumerationAlgebraAxiom(axiom, ref hasOneOf) && hasOneOf)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a class expression is EXACTLY the funnel shape
    /// <c>∃r⁻.O</c>: an existential over an inverse role whose filler is a
    /// one-of with at least one member, every member named. The match is
    /// top-level only — a funnel under a union, complement, or any other
    /// combinator never matches, because a disjunctive funnel pins nobody.
    /// </summary>
    /// <param name="expression">The candidate superclass expression.</param>
    /// <param name="role">The funnel role's name — the named role under the inverse.</param>
    /// <param name="members">The funnel's one-of filler.</param>
    /// <returns><see langword="true"/> on the exact funnel shape.</returns>
    public static bool TryMatchFunnelShape(OwlClassExpression expression, out NamedNode? role, out OwlObjectOneOf? members)
    {
        role = null;
        members = null;
        if(expression is not OwlObjectSomeValuesFrom { Property: OwlInverseObjectProperty inverse, Filler: OwlObjectOneOf oneOf })
        {
            return false;
        }

        if(oneOf.Individuals.Count == 0)
        {
            return false;
        }

        for(int i = 0; i < oneOf.Individuals.Count; i++)
        {
            if(oneOf.Individuals[i] is not NamedNode)
            {
                return false;
            }
        }

        role = inverse.Property;
        members = oneOf;

        return true;
    }

    /// <summary>
    /// Whether an axiom is EXACTLY the cap shape <c>O′ ⊑ ≤k r′[.F]</c>: a
    /// subclass axiom whose subclass is a one-of of at least one named member
    /// and whose superclass is a max-cardinality over a plain named role —
    /// the textual spelling the funnel's successor count is capped in; an
    /// inverse cap role or any other spelling never matches.
    /// </summary>
    /// <param name="axiom">The candidate axiom.</param>
    /// <param name="anchors">The cap's one-of anchor list.</param>
    /// <param name="role">The cap role's name.</param>
    /// <param name="bound">The cap bound <c>k</c>.</param>
    /// <param name="filler">The qualification filler; <see langword="null"/> for an unqualified cap.</param>
    /// <returns><see langword="true"/> on the exact cap shape.</returns>
    public static bool TryMatchCapShape(OwlAxiom axiom, out OwlObjectOneOf? anchors, out NamedNode? role, out int bound, out OwlClassExpression? filler)
    {
        anchors = null;
        role = null;
        bound = 0;
        filler = null;
        if(axiom is not OwlSubClassOfAxiom
        {
            SubClass: OwlObjectOneOf anchorOneOf,
            SuperClass: OwlObjectCardinality { Kind: OwlCardinalityKind.Max, Property: OwlObjectPropertyReference reference } cap,
        })
        {
            return false;
        }

        if(anchorOneOf.Individuals.Count == 0)
        {
            return false;
        }

        for(int i = 0; i < anchorOneOf.Individuals.Count; i++)
        {
            if(anchorOneOf.Individuals[i] is not NamedNode)
            {
                return false;
            }
        }

        anchors = anchorOneOf;
        role = reference.Property;
        bound = cap.Cardinality;
        filler = cap.Filler;

        return true;
    }

    /// <summary>Whether a class expression is the <c>owl:Thing</c> reference.</summary>
    /// <param name="expression">The expression to test.</param>
    /// <returns><see langword="true"/> for <c>owl:Thing</c>.</returns>
    public static bool IsThingReference(OwlClassExpression expression)
    {
        return expression is OwlClassReference reference && reference.Class.Iri.Equals(OwlVocabulary.Thing);
    }

    /// <summary>Whether a class expression is a chain-node class — a named class reference other than <c>owl:Thing</c> and <c>owl:Nothing</c>, the only shape a funnel chain hop may pass through.</summary>
    /// <param name="expression">The expression to test.</param>
    /// <returns><see langword="true"/> for a plain named class.</returns>
    public static bool IsChainNodeClass(OwlClassExpression expression)
    {
        return expression is OwlClassReference reference
            && !reference.Class.Iri.Equals(OwlVocabulary.Thing)
            && !reference.Class.Iri.Equals(OwlVocabulary.Nothing);
    }

    /// <summary>
    /// The Shape S census signal: whether the module carries a told
    /// <c>owl:Thing</c> subclass axiom whose superclass is a top-level
    /// existential into a non-empty one-of — either property spelling, plain or
    /// inline inverse — beside a told unqualified max-cardinality construct over
    /// a plain role on either cap route, the superclass of a subclass axiom or
    /// the class of a class assertion. Deliberately looser than the spy-point
    /// face's own jurisdiction predicate, which additionally demands named
    /// members, the told inverse linkage between the funnel and cap roles, and a
    /// cap on every member: a recognized module the face stays silent on is an
    /// expected, visible census state — never the reverse claim. The probe reuses
    /// only the general utilities, never the funnel and cap predicates hardwired
    /// to the inline-inverse funnel and the one-of-anchored cap the spy-point
    /// routes do not use. The scan allocates nothing: it matches told axiom
    /// surfaces in place.
    /// </summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see langword="true"/> on the spy-point encoding's signal.</returns>
    private static bool TryMatchSpyPointShape(ReasoningModule module)
    {
        bool hasFunnel = false;
        bool hasCap = false;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case(OwlSubClassOfAxiom subClass):
                {
                    hasFunnel = hasFunnel || (IsThingReference(subClass.SubClass) && IsOneOfExistential(subClass.SuperClass));
                    hasCap = hasCap || IsSpyPointCapSignal(subClass.SuperClass);
                    break;
                }
                case(OwlClassAssertionAxiom assertion):
                {
                    hasCap = hasCap || IsSpyPointCapSignal(assertion.Class);
                    break;
                }
                default:
                {
                    break;
                }
            }

            if(hasFunnel && hasCap)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one class expression is a top-level existential whose filler is a non-empty one-of, under either property spelling — the funnel signal the spy-point encoding drives the whole domain through. Member namedness is left to the face's own predicate.</summary>
    /// <param name="expression">The candidate superclass expression.</param>
    /// <returns><see langword="true"/> on the one-of existential.</returns>
    private static bool IsOneOfExistential(OwlClassExpression expression)
    {
        return expression is OwlObjectSomeValuesFrom { Filler: OwlObjectOneOf oneOf } && oneOf.Individuals.Count > 0;
    }

    /// <summary>Whether one class expression is a bare unqualified max-cardinality restriction over a plain named role — the cap signal both spy-point cap routes spell, with the route's own anchoring left to the face's predicate.</summary>
    /// <param name="expression">The candidate expression.</param>
    /// <returns><see langword="true"/> on the cap signal.</returns>
    private static bool IsSpyPointCapSignal(OwlClassExpression expression)
    {
        return expression is OwlObjectCardinality { Kind: OwlCardinalityKind.Max, Property: OwlObjectPropertyReference } cap && IsUnqualifiedFiller(cap.Filler);
    }

    /// <summary>
    /// The Shape B census signal: whether the module carries ONE plain role that
    /// simultaneously bears a told functional or inverse-functional
    /// characteristic, stands in a told inverse-object-properties axiom over
    /// plain roles, and heads a told top-level existential restriction in
    /// subclass or equivalence position. The three ingredients are bound to a
    /// SINGLE role because that is the linkage every bijection-chain size-variable
    /// source reading a characteristic requires: the equality derivation reads the
    /// chain step's OWN role told both functional and inverse-functional beside
    /// its told inverse partner, and the fiber product reads the existential
    /// definition's OWN role told functional beside the told inverse the counted
    /// cardinality sits on. A module whose three ingredients sit on unrelated
    /// roles carries no premise either derivation can consume, so it is not this
    /// habitat. Deliberately looser than the faces' own jurisdiction predicates,
    /// which additionally demand named class positions, the exact pairing of the
    /// chain steps, the anchor and distinctness coverage, and — for the
    /// certifying face — a whole-module admission: a recognized module the faces
    /// stay silent on is an expected, visible census state, never the reverse
    /// claim. The scan allocates nothing: it matches told
    /// axiom surfaces in place.
    /// </summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see langword="true"/> on the bijection-chain encoding's signal.</returns>
    private static bool TryMatchBijectionChainShape(ReasoningModule module)
    {
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(axiom is OwlObjectPropertyCharacteristicAxiom
            {
                Characteristic: OwlPropertyCharacteristic.Functional or OwlPropertyCharacteristic.InverseFunctional,
                Property: OwlObjectPropertyReference role,
            } && CarriesInversePairAndExistential(module, role.Property))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one role additionally stands in a told inverse-object-properties axiom over plain roles and heads a told top-level existential restriction in subclass or equivalence position — the two remaining Shape B ingredients, checked on the role the characteristic already sits on.</summary>
    /// <param name="module">The module to probe.</param>
    /// <param name="role">The role a told functional or inverse-functional characteristic sits on.</param>
    /// <returns><see langword="true"/> when the same role carries both remaining ingredients.</returns>
    private static bool CarriesInversePairAndExistential(ReasoningModule module, NamedNode role)
    {
        bool hasInversePair = false;
        bool hasExistential = false;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case(OwlInverseObjectPropertiesAxiom { First: OwlObjectPropertyReference first, Second: OwlObjectPropertyReference second }):
                {
                    hasInversePair = hasInversePair || first.Property.Iri.Equals(role.Iri) || second.Property.Iri.Equals(role.Iri);
                    break;
                }
                case(OwlSubClassOfAxiom subClass):
                {
                    hasExistential = hasExistential || IsExistentialOverRole(subClass.SuperClass, role);
                    break;
                }
                case(OwlEquivalentClassesAxiom equivalent):
                {
                    hasExistential = hasExistential || IsExistentialOverRole(equivalent.First, role) || IsExistentialOverRole(equivalent.Second, role);
                    break;
                }
                default:
                {
                    break;
                }
            }

            if(hasInversePair && hasExistential)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one class expression is a top-level existential restriction over the named plain role — the chain-step signal bound to one role, with the filler's shape left to the faces' own predicates.</summary>
    /// <param name="expression">The candidate expression.</param>
    /// <param name="role">The role the restriction must be over.</param>
    /// <returns><see langword="true"/> on the chain-step signal over that role.</returns>
    private static bool IsExistentialOverRole(OwlClassExpression expression, NamedNode role)
    {
        return expression is OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference existentialRole } && existentialRole.Property.Iri.Equals(role.Iri);
    }

    /// <summary>
    /// The Shape R obligation threshold: a module needs at least this many
    /// value, universal, or cardinality restrictions in obligation position
    /// before the restriction-rich ground signal is considered. Shape W already
    /// tolerates exactly ONE lone plain-role existential, so the threshold sits
    /// at two, which is what makes Shape R a genuine narrowing rather than a
    /// superset of Shape W.
    /// </summary>
    private const int RestrictionObligationThreshold = 2;

    /// <summary>
    /// The Shape R individual floor: a module needs at least this many distinct
    /// told individual terms before the restriction-rich ground signal matches.
    /// The value sits STRICTLY ABOVE the told-ground carrier ceiling
    /// <see cref="ContextToldGroundWitnessDecider.ToldGroundWitnessCarrierBound"/>
    /// and measures the SAME population that ceiling measures — distinct told
    /// individual terms — so the two labels partition the space exactly: at the
    /// ceiling or below a module stays Shape W, and above it both told-ground
    /// faces are already silent and the module is available to Shape R, with no
    /// band left in either direction.
    /// </summary>
    private const int RestrictionRichIndividualFloor = 17;

    /// <summary>
    /// The Shape R census signal: whether the module carries at least
    /// <see cref="RestrictionObligationThreshold"/> value, universal, or
    /// cardinality restrictions in OBLIGATION POSITION — a top-level
    /// <c>SubClassOf</c> superclass, either side of a told equivalence, or a
    /// top-level conjunct of an intersection standing in one of those positions
    /// — beside at least <see cref="RestrictionRichIndividualFloor"/> distinct
    /// told individual terms. The two clauses are the premise-derived reading of
    /// what the repairing faces consume: obligations whose witnessing edge the
    /// module never told, over a ground population large enough that the
    /// defeating mechanism is restriction breadth OVER a told ABox rather than
    /// restriction breadth alone. The term count is SYNTACTIC — told terms, no
    /// sameness quotient and no union-find — which is the same population the
    /// told-ground carrier ceiling measures. Deliberately looser than the faces'
    /// own jurisdictions, which additionally demand a whole-module admission for
    /// the certify side: a recognized module the faces stay silent on is an
    /// expected, visible census state, never the reverse claim. The obligation scan allocates
    /// nothing, matching told axiom surfaces in place; the term count runs only
    /// once that scan clears its threshold, so the none path stays
    /// allocation-free.
    /// </summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see langword="true"/> on the restriction-rich ground encoding's signal.</returns>
    private static bool TryMatchRestrictionRichGroundShape(ReasoningModule module)
    {
        int obligations = 0;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case(OwlSubClassOfAxiom subClass):
                {
                    obligations += CountObligationRestrictions(subClass.SuperClass);
                    break;
                }
                case(OwlEquivalentClassesAxiom equivalent):
                {
                    obligations += CountObligationRestrictions(equivalent.First) + CountObligationRestrictions(equivalent.Second);
                    break;
                }
                default:
                {
                    break;
                }
            }

            if(obligations >= RestrictionObligationThreshold)
            {
                return HasRestrictionRichIndividualFloor(module);
            }
        }

        return false;
    }

    /// <summary>Counts the obligation-position restrictions one told expression standing in obligation position carries: the expression itself when it is one, or its top-level intersection conjuncts. The scan indexes the told operand list in place and allocates nothing.</summary>
    /// <param name="expression">The expression standing in obligation position.</param>
    /// <returns>The obligation-position restriction count.</returns>
    private static int CountObligationRestrictions(OwlClassExpression expression)
    {
        if(expression is not OwlObjectIntersectionOf intersection)
        {
            return IsObligationRestriction(expression) ? 1 : 0;
        }

        int count = 0;
        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            count += IsObligationRestriction(intersection.Operands[i]) ? 1 : 0;
        }

        return count;
    }

    /// <summary>Whether one class expression is a value, universal, or cardinality restriction over an object property — the three restriction forms the repairing construction's deterministic and bounded-choice phases consume. An existential is deliberately excluded: Shape W already tolerates one, so counting it would make the threshold a superset rather than a narrowing.</summary>
    /// <param name="expression">The candidate conjunct.</param>
    /// <returns><see langword="true"/> on one of the three obligation forms.</returns>
    private static bool IsObligationRestriction(OwlClassExpression expression)
    {
        return expression is OwlObjectHasValue or OwlObjectAllValuesFrom or OwlObjectCardinality;
    }

    /// <summary>Whether the module names at least <see cref="RestrictionRichIndividualFloor"/> DISTINCT told individual terms, keyed syntactically by IRI or anonymous label with no sameness quotient. The walk drains each axiom's own individual-position terms through the structural traversal seam and stops at the floor, so a module clearing it early reads no further.</summary>
    /// <param name="module">The module to count over.</param>
    /// <returns><see langword="true"/> when the distinct told term count reaches the floor.</returns>
    private static bool HasRestrictionRichIndividualFloor(ReasoningModule module)
    {
        HashSet<Utf8String> terms = [];
        List<RdfTerm> individuals = [];
        Stack<OwlClassExpression> work = new();
        foreach(OwlAxiom axiom in module.Axioms)
        {
            individuals.Clear();
            axiom.AppendMentionedIndividuals(individuals, work);
            while(work.Count > 0)
            {
                work.Pop().AppendMentionedIndividuals(individuals, work);
            }

            for(int i = 0; i < individuals.Count; i++)
            {
                bool added = individuals[i] switch
                {
                    NamedNode named => terms.Add(named.Iri),
                    BlankNode anonymous => terms.Add(anonymous.Label),
                    _ => false,
                };

                if(added && terms.Count >= RestrictionRichIndividualFloor)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The Shape W census signal: whether the module carries a told
    /// object-property assertion, a told inverse-object-properties axiom over
    /// plain roles, and a told top-level existential restriction over a plain
    /// role in subclass or equivalence position — the three told ingredients a
    /// told-ground witness module needs before either face has anything to read.
    /// Deliberately looser than the faces' own jurisdictions, which additionally
    /// demand named class positions, individual terms in every assertion
    /// position, and — for the certifying face — a whole-module admission: a
    /// recognized module the faces stay silent on is an expected, visible census
    /// state, never the reverse claim. Shape B's own signal additionally requires
    /// a told functional or inverse-functional characteristic on the very role
    /// that carries its inverse pair and its existential — a characteristic the
    /// certify face excludes outright, so the two jurisdictions cannot both
    /// claim one module. The scan allocates nothing: it matches told
    /// axiom surfaces in place.
    /// </summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see langword="true"/> on the told-ground witness encoding's signal.</returns>
    private static bool TryMatchToldGroundWitnessShape(ReasoningModule module)
    {
        bool hasAssertion = false;
        bool hasInversePair = false;
        bool hasExistential = false;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case(OwlObjectPropertyAssertionAxiom):
                {
                    hasAssertion = true;
                    break;
                }
                case(OwlInverseObjectPropertiesAxiom { First: OwlObjectPropertyReference, Second: OwlObjectPropertyReference }):
                {
                    hasInversePair = true;
                    break;
                }
                case(OwlSubClassOfAxiom subClass):
                {
                    hasExistential = hasExistential || IsPlainRoleExistential(subClass.SuperClass);
                    break;
                }
                case(OwlEquivalentClassesAxiom equivalent):
                {
                    hasExistential = hasExistential || IsPlainRoleExistential(equivalent.First) || IsPlainRoleExistential(equivalent.Second);
                    break;
                }
                default:
                {
                    break;
                }
            }

            if(hasAssertion && hasInversePair && hasExistential)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one class expression is a top-level existential restriction over a plain named role — the chain-step signal, with the filler's shape left to the faces' own predicates.</summary>
    /// <param name="expression">The candidate expression.</param>
    /// <returns><see langword="true"/> on the chain-step signal.</returns>
    private static bool IsPlainRoleExistential(OwlClassExpression expression)
    {
        return expression is OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference };
    }

    /// <summary>
    /// The Shape M unfold ceiling: the spawner-reachability walk follows at most
    /// this many name-to-definition hops out of a told root label before it stops
    /// looking. The walk is a REACHABILITY question over the TBox, so a bounded
    /// traversal is what "syntactic" means here — no ABox expansion, no node
    /// spawning, no union-find and no least-fixpoint iteration to convergence. A
    /// module whose only spawner sits deeper than this reads none: that is a
    /// PROBE reach loss and never a wrong verdict, which is the direction the
    /// house probe doctrine prefers.
    /// </summary>
    private const int ModalUnfoldHopCeiling = 4;

    /// <summary>
    /// The Shape M census signal: whether the module carries a told class
    /// assertion, named or anonymous; an existential reachable from a told root
    /// label within <see cref="ModalUnfoldHopCeiling"/> name-to-definition hops;
    /// a universal whose role is a told INVERSE of a role an existential or a
    /// told object-property assertion uses; a numeric clash template or an
    /// <c>owl:Nothing</c> occurrence; and NO disjunctive construct anywhere in
    /// the module. The inverse-channel clause and the disjunction-free clause
    /// together are what make the signal disjoint from every shipped shape by a
    /// named quantity rather than by probe order alone: no sibling predicate
    /// inspects a universal's role against a told inverse of a spawning role, so
    /// no sibling shape is narrowed by this one.
    /// Deliberately looser than the face's own jurisdiction, which additionally
    /// demands unqualified bounds, a determined property kind, and simple roles
    /// under every cardinality: a recognized module the face stays silent on is
    /// an expected, visible census state, never the reverse claim. In particular
    /// the clash-template clause does NOT check qualification, so a module whose
    /// only minimum-above-maximum pair is QUALIFIED matches here and is then
    /// silenced by the face.
    /// The three allocation-free clauses run first and match told axiom surfaces in
    /// place; the bounded reachability walk, which owns the probe's only
    /// containers, runs ONLY once they have all matched, so the none path stays
    /// allocation-free.
    /// </summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see langword="true"/> on the bounded skolem-expansion modal module's signal.</returns>
    private static bool TryMatchModalRoleExpansionShape(ReasoningModule module)
    {
        bool hasRoot = false;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(CarriesModalDisjunction(axiom))
            {
                return false;
            }

            hasRoot = hasRoot || axiom is OwlClassAssertionAxiom;
        }

        return hasRoot
            && CarriesModalUpwardChannel(module)
            && CarriesModalClashTemplate(module)
            && ReachesModalSpawner(module);
    }

    /// <summary>Whether one axiom carries a disjunctive construct in its own positions: the two disjunctive axiom kinds, or a union, complement, or enumeration standing in a told class position or as a top-level conjunct of one.</summary>
    /// <param name="axiom">The candidate axiom.</param>
    /// <returns><see langword="true"/> when the axiom carries a disjunctive construct.</returns>
    private static bool CarriesModalDisjunction(OwlAxiom axiom)
    {
        return axiom switch
        {
            OwlDisjointClassesAxiom or OwlDisjointUnionAxiom => true,
            OwlSubClassOfAxiom subClass => IsModalDisjunctivePosition(subClass.SubClass) || IsModalDisjunctivePosition(subClass.SuperClass),
            OwlEquivalentClassesAxiom equivalent => IsModalDisjunctivePosition(equivalent.First) || IsModalDisjunctivePosition(equivalent.Second),
            OwlClassAssertionAxiom assertion => IsModalDisjunctivePosition(assertion.Class),
            _ => false,
        };
    }

    /// <summary>Whether one told class position carries a disjunctive construct at its top level or in a top-level conjunct.</summary>
    /// <param name="expression">The told class position.</param>
    /// <returns><see langword="true"/> on a disjunctive construct.</returns>
    private static bool IsModalDisjunctivePosition(OwlClassExpression expression)
    {
        if(expression is not OwlObjectIntersectionOf intersection)
        {
            return expression is OwlObjectUnionOf or OwlObjectComplementOf or OwlObjectOneOf;
        }

        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            if(intersection.Operands[i] is OwlObjectUnionOf or OwlObjectComplementOf or OwlObjectOneOf)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the module carries an upward channel: a told inverse pair one side of which carries a universal while the other appears in an existential or in a told object-property assertion. The two roles are compared by FULL IRI, so two roles sharing a local name across namespaces never pair.</summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see langword="true"/> on the upward channel's signal.</returns>
    private static bool CarriesModalUpwardChannel(ReasoningModule module)
    {
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(axiom is not OwlInverseObjectPropertiesAxiom { First: OwlObjectPropertyReference first, Second: OwlObjectPropertyReference second })
            {
                continue;
            }

            if(PairsModalUniversalWithUse(module, first.Named.Iri, second.Named.Iri)
                || PairsModalUniversalWithUse(module, second.Named.Iri, first.Named.Iri))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one told inverse pair's two roles carry the channel: a universal over the first and a spawning or told use of the second.</summary>
    /// <param name="module">The module to probe.</param>
    /// <param name="universalRole">The role a universal must quantify.</param>
    /// <param name="useRole">The role an existential or a told edge must use.</param>
    /// <returns><see langword="true"/> when both halves are present.</returns>
    private static bool PairsModalUniversalWithUse(ReasoningModule module, Utf8String universalRole, Utf8String useRole)
    {
        bool hasUniversal = false;
        bool hasUse = false;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            hasUniversal = hasUniversal || CarriesModalUniversal(axiom, universalRole);
            hasUse = hasUse || CarriesModalRoleUse(axiom, useRole);
            if(hasUniversal && hasUse)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one axiom carries a universal over the named plain role in a told class position or in a top-level conjunct of one.</summary>
    /// <param name="axiom">The candidate axiom.</param>
    /// <param name="role">The role the universal must quantify.</param>
    /// <returns><see langword="true"/> on the universal.</returns>
    private static bool CarriesModalUniversal(OwlAxiom axiom, Utf8String role)
    {
        return axiom switch
        {
            OwlSubClassOfAxiom subClass => IsModalUniversalPosition(subClass.SuperClass, role),
            OwlEquivalentClassesAxiom equivalent => IsModalUniversalPosition(equivalent.First, role) || IsModalUniversalPosition(equivalent.Second, role),
            OwlClassAssertionAxiom assertion => IsModalUniversalPosition(assertion.Class, role),
            _ => false,
        };
    }

    /// <summary>Whether one told class position carries a universal over the named plain role at its top level or in a top-level conjunct.</summary>
    /// <param name="expression">The told class position.</param>
    /// <param name="role">The role the universal must quantify.</param>
    /// <returns><see langword="true"/> on the universal.</returns>
    private static bool IsModalUniversalPosition(OwlClassExpression expression, Utf8String role)
    {
        if(expression is not OwlObjectIntersectionOf intersection)
        {
            return IsModalUniversal(expression, role);
        }

        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            if(IsModalUniversal(intersection.Operands[i], role))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one class expression is a universal over the named plain role, compared by full IRI.</summary>
    /// <param name="expression">The candidate expression.</param>
    /// <param name="role">The role the universal must quantify.</param>
    /// <returns><see langword="true"/> on the universal.</returns>
    private static bool IsModalUniversal(OwlClassExpression expression, Utf8String role)
    {
        return expression is OwlObjectAllValuesFrom { Property: OwlObjectPropertyReference reference } && reference.Named.Iri.Equals(role);
    }

    /// <summary>Whether one axiom uses the named plain role in a way the expansion materialises an edge for: a told object-property assertion, or an existential in a told class position or a top-level conjunct of one.</summary>
    /// <param name="axiom">The candidate axiom.</param>
    /// <param name="role">The role the use must be over.</param>
    /// <returns><see langword="true"/> on the role use.</returns>
    private static bool CarriesModalRoleUse(OwlAxiom axiom, Utf8String role)
    {
        return axiom switch
        {
            OwlObjectPropertyAssertionAxiom assertion => assertion.Property.Iri.Equals(role),
            OwlSubClassOfAxiom subClass => IsModalExistentialPosition(subClass.SuperClass, role),
            OwlEquivalentClassesAxiom equivalent => IsModalExistentialPosition(equivalent.First, role) || IsModalExistentialPosition(equivalent.Second, role),
            OwlClassAssertionAxiom classAssertion => IsModalExistentialPosition(classAssertion.Class, role),
            _ => false,
        };
    }

    /// <summary>Whether one told class position carries an existential over the named plain role at its top level or in a top-level conjunct.</summary>
    /// <param name="expression">The told class position.</param>
    /// <param name="role">The role the existential must be over.</param>
    /// <returns><see langword="true"/> on the existential.</returns>
    private static bool IsModalExistentialPosition(OwlClassExpression expression, Utf8String role)
    {
        if(expression is not OwlObjectIntersectionOf intersection)
        {
            return IsModalExistential(expression, role);
        }

        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            if(IsModalExistential(intersection.Operands[i], role))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one class expression is an existential over the named plain role, compared by full IRI.</summary>
    /// <param name="expression">The candidate expression.</param>
    /// <param name="role">The role the existential must be over.</param>
    /// <returns><see langword="true"/> on the existential.</returns>
    private static bool IsModalExistential(OwlClassExpression expression, Utf8String role)
    {
        return expression is OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference reference } && reference.Named.Iri.Equals(role);
    }

    /// <summary>Whether the module carries a clash template: some property IRI with a minimum strictly above a maximum on it, or an <c>owl:Nothing</c> occurrence in a told class position. The clause does not check qualification, so it is deliberately looser than the face's own template signal.</summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see langword="true"/> on the clash template's signal.</returns>
    private static bool CarriesModalClashTemplate(ReasoningModule module)
    {
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(CarriesModalEmptyClass(axiom))
            {
                return true;
            }

            if(TryReadModalMinimum(axiom, out NamedNode? property, out int minimum) && OutrunsModalMaximum(module, property.Iri, minimum))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one axiom carries <c>owl:Nothing</c> in a told class position, as a superclass, an equivalence side, an asserted class, or a restriction filler one hop inside one of those.</summary>
    /// <param name="axiom">The candidate axiom.</param>
    /// <returns><see langword="true"/> on the empty-class occurrence.</returns>
    private static bool CarriesModalEmptyClass(OwlAxiom axiom)
    {
        return axiom switch
        {
            OwlSubClassOfAxiom subClass => IsModalEmptyClassPosition(subClass.SuperClass),
            OwlEquivalentClassesAxiom equivalent => IsModalEmptyClassPosition(equivalent.First) || IsModalEmptyClassPosition(equivalent.Second),
            OwlClassAssertionAxiom assertion => IsModalEmptyClassPosition(assertion.Class),
            _ => false,
        };
    }

    /// <summary>Whether one told class position is <c>owl:Nothing</c>, carries it as a top-level conjunct, or carries it as a restriction filler.</summary>
    /// <param name="expression">The told class position.</param>
    /// <returns><see langword="true"/> on the empty-class occurrence.</returns>
    private static bool IsModalEmptyClassPosition(OwlClassExpression expression)
    {
        if(expression is not OwlObjectIntersectionOf intersection)
        {
            return IsModalEmptyClass(expression);
        }

        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            if(IsModalEmptyClass(intersection.Operands[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one class expression is <c>owl:Nothing</c> itself or a restriction whose filler is.</summary>
    /// <param name="expression">The candidate expression.</param>
    /// <returns><see langword="true"/> on the empty-class occurrence.</returns>
    private static bool IsModalEmptyClass(OwlClassExpression expression)
    {
        return expression switch
        {
            OwlClassReference reference => reference.Class.Iri.Equals(OwlVocabulary.Nothing),
            OwlObjectSomeValuesFrom existential => IsModalEmptyClass(existential.Filler),
            OwlObjectAllValuesFrom universal => IsModalEmptyClass(universal.Filler),
            _ => false,
        };
    }

    /// <summary>Reads one axiom's first minimum-cardinality bound over a plain named object property or a data property in a told class position.</summary>
    /// <param name="axiom">The candidate axiom.</param>
    /// <param name="property">The bounded property; <see langword="null"/> when no minimum was read.</param>
    /// <param name="minimum">The minimum bound; zero when no minimum was read.</param>
    /// <returns><see langword="true"/> when a minimum was read.</returns>
    private static bool TryReadModalMinimum(OwlAxiom axiom, [NotNullWhen(true)] out NamedNode? property, out int minimum)
    {
        property = null;
        minimum = 0;

        return axiom switch
        {
            OwlSubClassOfAxiom subClass => TryReadModalBoundPosition(subClass.SuperClass, OwlCardinalityKind.Min, out property, out minimum),
            OwlEquivalentClassesAxiom equivalent => TryReadModalBoundPosition(equivalent.First, OwlCardinalityKind.Min, out property, out minimum)
                || TryReadModalBoundPosition(equivalent.Second, OwlCardinalityKind.Min, out property, out minimum),
            OwlClassAssertionAxiom assertion => TryReadModalBoundPosition(assertion.Class, OwlCardinalityKind.Min, out property, out minimum),
            _ => false,
        };
    }

    /// <summary>Whether the module carries a maximum on the named property strictly below the minimum offered — the second half of the numeric clash template.</summary>
    /// <param name="module">The module to probe.</param>
    /// <param name="property">The bounded property IRI.</param>
    /// <param name="minimum">The minimum the maximum must fall below.</param>
    /// <returns><see langword="true"/> when the template closes.</returns>
    private static bool OutrunsModalMaximum(ReasoningModule module, Utf8String property, int minimum)
    {
        foreach(OwlAxiom axiom in module.Axioms)
        {
            bool read = axiom switch
            {
                OwlSubClassOfAxiom subClass => ReadsModalMaximumBelow(subClass.SuperClass, property, minimum),
                OwlEquivalentClassesAxiom equivalent => ReadsModalMaximumBelow(equivalent.First, property, minimum) || ReadsModalMaximumBelow(equivalent.Second, property, minimum),
                OwlClassAssertionAxiom assertion => ReadsModalMaximumBelow(assertion.Class, property, minimum),
                _ => false,
            };

            if(read)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one told class position carries a maximum on the named property strictly below the minimum offered.</summary>
    /// <param name="expression">The told class position.</param>
    /// <param name="property">The bounded property IRI.</param>
    /// <param name="minimum">The minimum the maximum must fall below.</param>
    /// <returns><see langword="true"/> when the position closes the template.</returns>
    private static bool ReadsModalMaximumBelow(OwlClassExpression expression, Utf8String property, int minimum)
    {
        return TryReadModalBoundPosition(expression, OwlCardinalityKind.Max, out NamedNode? bounded, out int bound)
            && bounded.Iri.Equals(property)
            && bound < minimum;
    }

    /// <summary>Reads the first cardinality bound of the requested flavour a told class position carries at its top level or in a top-level conjunct, over a plain named object property or a data property.</summary>
    /// <param name="expression">The told class position.</param>
    /// <param name="kind">The cardinality flavour to read; an exact restriction answers either flavour.</param>
    /// <param name="property">The bounded property; <see langword="null"/> when nothing was read.</param>
    /// <param name="bound">The bound; zero when nothing was read.</param>
    /// <returns><see langword="true"/> when a bound was read.</returns>
    private static bool TryReadModalBoundPosition(OwlClassExpression expression, OwlCardinalityKind kind, [NotNullWhen(true)] out NamedNode? property, out int bound)
    {
        if(expression is not OwlObjectIntersectionOf intersection)
        {
            return TryReadModalBound(expression, kind, out property, out bound);
        }

        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            if(TryReadModalBound(intersection.Operands[i], kind, out property, out bound))
            {
                return true;
            }
        }

        property = null;
        bound = 0;

        return false;
    }

    /// <summary>Reads one class expression's cardinality bound of the requested flavour, an exact restriction answering either flavour since it is read as its minimum and maximum halves together.</summary>
    /// <param name="expression">The candidate expression.</param>
    /// <param name="kind">The cardinality flavour to read.</param>
    /// <param name="property">The bounded property; <see langword="null"/> when nothing was read.</param>
    /// <param name="bound">The bound; zero when nothing was read.</param>
    /// <returns><see langword="true"/> when a bound was read.</returns>
    private static bool TryReadModalBound(OwlClassExpression expression, OwlCardinalityKind kind, [NotNullWhen(true)] out NamedNode? property, out int bound)
    {
        switch(expression)
        {
            case(OwlObjectCardinality { Property: OwlObjectPropertyReference reference } objectBound) when objectBound.Kind == kind || objectBound.Kind == OwlCardinalityKind.Exact:
            {
                property = reference.Named;
                bound = objectBound.Cardinality;

                return true;
            }
            case(OwlDataCardinality dataBound) when dataBound.Kind == kind || dataBound.Kind == OwlCardinalityKind.Exact:
            {
                property = dataBound.Property;
                bound = dataBound.Cardinality;

                return true;
            }
            default:
            {
                property = null;
                bound = 0;

                return false;
            }
        }
    }

    /// <summary>
    /// Whether an existential is reachable from a told root label within
    /// <see cref="ModalUnfoldHopCeiling"/> name-to-definition hops. The walk is a
    /// BOUNDED worklist over class definitions with a visited set over class
    /// IRIs: it never expands an ABox, never spawns a node and never iterates to
    /// convergence, and it runs only once every allocation-free clause has
    /// matched, so the probe's none path allocates nothing.
    /// </summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see langword="true"/> when a spawner is reachable inside the ceiling.</returns>
    private static bool ReachesModalSpawner(ReasoningModule module)
    {
        HashSet<Utf8String> visited = [];
        List<Utf8String> frontier = [];
        List<Utf8String> next = [];
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(axiom is not OwlClassAssertionAxiom assertion)
            {
                continue;
            }

            if(CarriesModalSpawner(assertion.Class))
            {
                return true;
            }

            AppendModalNames(assertion.Class, visited, frontier);
        }

        for(int hop = 0; hop < ModalUnfoldHopCeiling && frontier.Count > 0; hop++)
        {
            next.Clear();
            foreach(OwlAxiom axiom in module.Axioms)
            {
                if(!TryReadModalDefinition(axiom, frontier, out OwlClassExpression? definition))
                {
                    continue;
                }

                if(CarriesModalSpawner(definition))
                {
                    return true;
                }

                AppendModalNames(definition, visited, next);
            }

            frontier.Clear();
            frontier.AddRange(next);
        }

        return false;
    }

    /// <summary>Whether one told class position carries an existential over a plain named role at its top level or in a top-level conjunct — the spawner the walk is looking for, over any role.</summary>
    /// <param name="expression">The told class position.</param>
    /// <returns><see langword="true"/> on a spawner.</returns>
    private static bool CarriesModalSpawner(OwlClassExpression expression)
    {
        if(expression is not OwlObjectIntersectionOf intersection)
        {
            return IsPlainRoleExistential(expression);
        }

        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            if(IsPlainRoleExistential(intersection.Operands[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads the definition side of one axiom whose defined name stands in the current frontier: the superclass of a told subsumption, or the other side of a told equivalence.</summary>
    /// <param name="axiom">The candidate axiom.</param>
    /// <param name="frontier">The names the current hop is unfolding.</param>
    /// <param name="definition">The definition side; <see langword="null"/> when the axiom defines no frontier name.</param>
    /// <returns><see langword="true"/> when a definition was read.</returns>
    private static bool TryReadModalDefinition(OwlAxiom axiom, List<Utf8String> frontier, [NotNullWhen(true)] out OwlClassExpression? definition)
    {
        definition = null;
        switch(axiom)
        {
            case(OwlSubClassOfAxiom { SubClass: OwlClassReference subReference } subClass) when NamesModalFrontier(frontier, subReference.Class.Iri):
            {
                definition = subClass.SuperClass;

                return true;
            }
            case(OwlEquivalentClassesAxiom { First: OwlClassReference firstReference } first) when NamesModalFrontier(frontier, firstReference.Class.Iri):
            {
                definition = first.Second;

                return true;
            }
            case(OwlEquivalentClassesAxiom { Second: OwlClassReference secondReference } second) when NamesModalFrontier(frontier, secondReference.Class.Iri):
            {
                definition = second.First;

                return true;
            }
            default:
            {
                return false;
            }
        }
    }

    /// <summary>Whether one class IRI stands in the current frontier, compared by full IRI.</summary>
    /// <param name="frontier">The names the current hop is unfolding.</param>
    /// <param name="name">The class IRI.</param>
    /// <returns><see langword="true"/> when the name is being unfolded.</returns>
    private static bool NamesModalFrontier(List<Utf8String> frontier, Utf8String name)
    {
        for(int i = 0; i < frontier.Count; i++)
        {
            if(frontier[i].Equals(name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Appends one told class position's named classes to the next hop's frontier, the top-level conjuncts of an intersection included, skipping every name the walk has already unfolded.</summary>
    /// <param name="expression">The told class position.</param>
    /// <param name="visited">The names the walk has already unfolded.</param>
    /// <param name="frontierToAppendTo">The next hop's frontier.</param>
    private static void AppendModalNames(OwlClassExpression expression, HashSet<Utf8String> visited, List<Utf8String> frontierToAppendTo)
    {
        if(expression is not OwlObjectIntersectionOf intersection)
        {
            AppendModalName(expression, visited, frontierToAppendTo);

            return;
        }

        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            AppendModalName(intersection.Operands[i], visited, frontierToAppendTo);
        }
    }

    /// <summary>Appends one named class to the next hop's frontier, skipping a name the walk has already unfolded and every expression that is no named class.</summary>
    /// <param name="expression">The candidate expression.</param>
    /// <param name="visited">The names the walk has already unfolded.</param>
    /// <param name="frontierToAppendTo">The next hop's frontier.</param>
    private static void AppendModalName(OwlClassExpression expression, HashSet<Utf8String> visited, List<Utf8String> frontierToAppendTo)
    {
        if(expression is OwlClassReference reference && visited.Add(reference.Class.Iri))
        {
            frontierToAppendTo.Add(reference.Class.Iri);
        }
    }

    /// <summary>
    /// The Shape D census signal: whether the module carries a told
    /// inverse-functional characteristic over a plain role AND a told range
    /// over a plain role resolving — inline, or through one told hop from a
    /// named class to a one-of an equivalence pairs it with in either operand
    /// order or a subclass axiom with the class in SUBCLASS position bounds it
    /// by — to a non-empty one-of of NAMED individuals. Deliberately looser
    /// than the face's own jurisdiction: no role-identity linkage between the
    /// characteristic and the range, no self-loop totality, and no denial
    /// check, so a recognized module the face stays silent on is an expected,
    /// visible census state — never the reverse claim. The probe reuses only
    /// the general utilities and implements its own checks; it allocates
    /// nothing, resolving a hop by a bounded rescan of the told axioms in
    /// place.
    /// </summary>
    /// <param name="module">The module to probe.</param>
    /// <returns><see langword="true"/> on the diagonal-pinned role signal.</returns>
    internal static bool TryMatchNominalPinnedRoleShape(ReasoningModule module)
    {
        bool hasCandidate = false;
        bool hasPinnedRange = false;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case(OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.InverseFunctional, Property: OwlObjectPropertyReference }):
                {
                    hasCandidate = true;
                    break;
                }
                case(OwlObjectPropertyRangeAxiom { Property: OwlObjectPropertyReference } range):
                {
                    hasPinnedRange = hasPinnedRange || IsAllNamedOneOf(range.Range) || ResolvesToAllNamedOneOf(module, range.Range);
                    break;
                }
                default:
                {
                    break;
                }
            }

            if(hasCandidate && hasPinnedRange)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one class expression is a non-empty one-of of NAMED individuals — the resolved range shape the diagonal-pinned role signal demands.</summary>
    /// <param name="expression">The candidate expression.</param>
    /// <returns><see langword="true"/> on the all-named one-of.</returns>
    private static bool IsAllNamedOneOf(OwlClassExpression expression)
    {
        if(expression is not OwlObjectOneOf oneOf || oneOf.Individuals.Count == 0)
        {
            return false;
        }

        for(int index = 0; index < oneOf.Individuals.Count; index++)
        {
            if(oneOf.Individuals[index] is not NamedNode)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a range target is a named class ONE told hop from an all-named one-of: an equivalence pairing the class with the one-of in either operand order, or a subclass axiom carrying the class in SUBCLASS position under the one-of. Deeper chains resolve nothing — the walk is a single rescan, never a closure.</summary>
    /// <param name="module">The module whose told axioms supply the hop.</param>
    /// <param name="target">The range's target expression.</param>
    /// <returns><see langword="true"/> where one told hop resolves the class.</returns>
    private static bool ResolvesToAllNamedOneOf(ReasoningModule module, OwlClassExpression target)
    {
        if(target is not OwlClassReference reference)
        {
            return false;
        }

        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case(OwlEquivalentClassesAxiom equivalence):
                {
                    if((IsReferenceTo(equivalence.First, reference.Class.Iri) && IsAllNamedOneOf(equivalence.Second))
                        || (IsReferenceTo(equivalence.Second, reference.Class.Iri) && IsAllNamedOneOf(equivalence.First)))
                    {
                        return true;
                    }

                    break;
                }
                case(OwlSubClassOfAxiom subClass):
                {
                    if(IsReferenceTo(subClass.SubClass, reference.Class.Iri) && IsAllNamedOneOf(subClass.SuperClass))
                    {
                        return true;
                    }

                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        return false;
    }

    /// <summary>Whether one class expression is a reference to the named class with the given interned IRI.</summary>
    /// <param name="expression">The candidate expression.</param>
    /// <param name="classIri">The named class's interned IRI.</param>
    /// <returns><see langword="true"/> on the exact reference.</returns>
    private static bool IsReferenceTo(OwlClassExpression expression, Utf8String classIri)
    {
        return expression is OwlClassReference reference && reference.Class.Iri.Equals(classIri);
    }
}
