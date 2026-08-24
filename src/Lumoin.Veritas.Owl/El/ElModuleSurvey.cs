using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.El;

/// <summary>
/// Decides whether a module lies wholly within the EL⊥ fragment that
/// <see cref="ElClassifier.ClassifyModule"/> soundly and completely decides —
/// the gate the EL-coupled reasoner consults before trusting the EL fast-path.
/// </summary>
/// <remarks>
/// <para>
/// Conservative by construction: it admits only the constructs the EL saturation
/// interprets soundly and completely, so a <see langword="true"/> verdict is a
/// correct EL⊥ decision; anything it cannot vouch for is delegated to the tableau
/// oracle. The admitted fragment: conjunction, existential restriction over a forward
/// role, the bottom concept, named classes (including <c>owl:Thing</c>/<c>owl:Nothing</c>),
/// the role hierarchy, property chains, transitive roles, property domains, property
/// ranges, single-property data existentials
/// (<c>DataSomeValuesFrom</c>/<c>DataHasValue</c>) over a non-reserved data property in
/// every class position this survey reads, single-individual nominals in negative position (as
/// the filler of an existential over a forward or inverse role in a class assertion, a bare class
/// assertion, on the subclass side, and as a singleton disjointness operand), and the ABox (class and
/// role assertions, <c>SameIndividual</c>, <c>DifferentIndividuals</c>).
/// </para>
/// <para>
/// On the constructs that are also inside the tableau oracle's ALC(H)+S fragment the
/// EL verdict equals the tableau's, which the differential tests pin. <b>Property
/// chains are one place the coupled engine decides strictly more than the
/// tableau</b>: the snapshot tableau treats <c>ObjectPropertyChain</c> as beyond its
/// fragment and returns a chain-dropped, fragment-relative verdict, whereas EL is
/// sound and complete for role composition and decides the chain in full. That is a
/// deliberate capability gain — EL composes <c>r∘s ⊑ t</c> edges to fixpoint and so
/// catches, for instance, an inconsistency forced through a composed edge that the
/// tableau misses — so chain modules are verified against the known correct answer,
/// not against the (chain-blind) tableau.
/// </para>
/// <para>
/// Outside the fragment — and so delegated: disjunction, complement, universal
/// restriction, value restrictions,
/// a multi-individual <c>ObjectOneOf</c> in any position, every
/// object/data cardinality, data universals (<c>DataAllValuesFrom</c>), a data
/// existential over several data properties or over a reserved one, an inverse-role nominal in a property
/// domain or range class, a role axiom spelled inverse on both sides, negative object-property
/// assertions, keys, disjoint unions, and any
/// reserved role in either spelling. An inverse EXISTENTIAL is admitted in every class position this
/// survey reads — subclass, superclass, equivalence, disjointness operand, property domain, property
/// range, and class assertion — each reducing to the synthetic mirror or generator role. An
/// individual-valued restriction over an inverse role — <c>ObjectHasValue(r⁻, a)</c> and its
/// <c>∃r⁻.{a}</c> enumeration spelling, one claim in two spellings — is admitted wherever the forward
/// spelling is, being the same edge with its endpoints exchanged, and <c>ObjectHasSelf(r⁻)</c> is
/// admitted wherever <c>ObjectHasSelf(r)</c> is, a self-edge being its own reverse.
/// </para>
/// <para>
/// <b>The data-property RBox is admitted.</b> <c>DataPropertyRange</c>, <c>FunctionalDataProperty</c>,
/// <c>SubDataPropertyOf</c>, <c>EquivalentDataProperties</c>, and <c>DisjointDataProperties</c> over
/// non-reserved data properties, and a <c>DataPropertyDomain</c> whose domain is a named class, are
/// admitted: the classifier builds them into a per-module data-property box and decides each atom's data
/// existentials against it — inheriting super-property ranges, pooling one carrier's demands on a
/// functional property into one value, forbidding a shared value across a disjoint pair, and telling a
/// demand-carrying atom its domain class — through the same value-space checker the tableau uses, so the
/// verdict matches on the admitted fragment. A disjointness configuration the checker cannot decisively
/// discharge surfaces the undecided marker (a sound abstention), and a <c>DataPropertyDomain</c> with a
/// complex domain stays delegated.
/// </para>
/// <para>
/// <b>Data existentials are admitted in both positions.</b> A
/// <c>DataSomeValuesFrom</c> over a single non-reserved data property, or a
/// <c>DataHasValue</c> over one, is admitted wherever this survey reads a class
/// expression, and the classifier reads the two positions as the two claims they are.
/// On the superclass side of an inclusion or in a class assertion the occurrence is a
/// value demand — a member carries a data-property value in the range — decided by the
/// range's value-space emptiness: an empty range makes the carrier unsatisfiable. On
/// the subclass side, in an equivalence, or in a disjointness the occurrence is a
/// concept in its own right, everything carrying such a value, which the classifier
/// names <c>∃d.R ⊑ F</c> and recognizes on each demand carrier: a class whose own
/// demands force a <c>d</c>-value inside <c>R</c> — the entailment decided as the joint
/// unsatisfiability of those demands with <c>∀d.¬R</c> against the same data-property
/// box — is told <c>F</c>, so a nested interval definition subsumes the narrower one and
/// a disjointness against such a concept can condemn its carrier. Both decisions use the
/// same value-space checker the tableau does, so the verdict matches on the admitted
/// fragment, and a range the checker cannot decide surfaces the undecided marker rather
/// than a recognition that was never tested.
/// </para>
/// <para>
/// <b>Three data shapes stay delegated with their reasons.</b> A <c>DataAllValuesFrom</c>
/// is a universal the EL calculus cannot represent in either position. A
/// <c>DataSomeValuesFrom</c> over several data properties is a value tuple with no
/// single-property reading, and a reserved data property has a fixed extension the
/// calculus does not interpret; both fall outside every arm above. And a module whose
/// functional data property is reached by value demands on two or more distinct classes
/// is admitted here but named unsupported by the classifier: a common subsumee inherits
/// both demands and functionality forces them onto one value, which the per-carrier
/// decision does not test, so the module is delegated whole.
/// </para>
/// <para>
/// <b>Local and global reflexivity are admitted as a capability gain.</b>
/// <c>ObjectHasSelf(r)</c> — a member has an <c>r</c>-edge to itself — and the
/// <c>Reflexive</c> object-property characteristic (every individual has an
/// <c>r</c>-self-edge) are modelled in the saturation as a reflexive role edge
/// <c>(r, x, x)</c>. Like property chains, the snapshot tableau treats both as beyond
/// its fragment and drops them, so EL decides strictly more — it catches an
/// inconsistency forced through a self-edge the tableau misses — and these modules are
/// verified against the known correct answer, not the (self-blind) tableau.
/// </para>
/// <para>
/// <b>Symmetric and inverse object properties are admitted over the asserted ground graph as a
/// capability gain.</b> <c>SymmetricObjectProperty(r)</c> makes an asserted pair <c>(a, b) ∈ rᴵ</c>
/// force <c>(b, a) ∈ rᴵ</c>, and <c>InverseObjectProperties(r, s)</c> makes <c>(a, b) ∈ rᴵ</c> force
/// <c>(b, a) ∈ sᴵ</c> (and conversely), in every model — so the classifier mirrors the reverse of
/// every edge over a paired role under its inverse, a genuine role atom because both endpoints are
/// concrete individuals. The admission is gated to roles whose only edges are those asserted ground
/// edges: a paired role the classifier finds to bear a positive-position existential, a self-demand,
/// or a chain (anywhere in its sub-role closure) would also carry shared-filler or composed edges the
/// mirror cannot reproduce, so such a module is delegated. A one-directional inverse sub-property,
/// <c>SubObjectPropertyOf(ObjectInverseOf(r), s)</c> (<c>r⁻ ⊑ s</c>, forcing an s-edge reverse of
/// every r-edge) or <c>SubObjectPropertyOf(s, ObjectInverseOf(r))</c> (<c>s ⊑ r⁻</c>), is admitted the
/// same way — the mirror seeds the reverse in one direction only, and a functional role that receives
/// those reverse edges is delegated. <b>An inverse existential in a class position is admitted.</b>
/// In negative position — the subclass side (<c>∃r⁻.C ⊑ Y</c>) and a disjointness operand (pairwise
/// <c>∃r⁻.C ⊓ X ⊑ ⊥</c>) — the classifier reduces it to an ordinary left existential over a synthetic
/// mirror role of <c>r</c> (every <c>r</c>-edge forces the reverse mirror edge, so a node has an
/// <c>r</c>-predecessor in <c>C</c> exactly when it has a mirror-successor in <c>C</c>). In positive
/// position — the superclass side (<c>A ⊑ ∃r⁻.C</c>) and both sides of an equivalence — the classifier
/// reduces it at normalization to a forward existential over a synthetic per-<c>r</c> generator role
/// <c>g</c> (<c>g ⊑ r⁻</c>): each owner's <c>r</c>-predecessor is minted as a forward
/// <c>g</c>-successor — one content-keyed successor per <c>(role, filler)</c> shared by every owner
/// when no chain or self feature reaches the witness-carrying roles, a distinct provenance-keyed
/// successor per owner otherwise — and the mirror writes the real <c>r</c>-edge back onto the owner,
/// so a range or domain clash on the owner, or a left existential over the witness edge, is decided. Both ride the
/// shipped mirror + left-/right-existential rules with no new saturation rule. <c>ObjectInverseOf</c> in
/// the remaining class positions is admitted with no machinery of its own: an individual-valued
/// restriction <c>ObjectHasValue(r⁻, a)</c> or <c>∃r⁻.{a}</c> in a class assertion IS the ground fact
/// <c>(a, x) ∈ r</c>, the forward spelling's asserted edge with its endpoints exchanged; on the
/// superclass side it rewrites into the enumeration form the generator reduction already carries, and on
/// the subclass side into a left existential over the synthetic mirror role keyed on the individual
/// node; and <c>ObjectHasSelf(r⁻)</c> registers its demand and its elimination on the forward role, a
/// self-edge being its own reverse. An
/// inverse axiom spelled over inverse roles on both sides stays delegated; an inverse existential in a
/// domain, range, or assertion class is admitted, each position reaching that same generator through its
/// own normal form — a property domain through the inclusion <c>∃p.⊤ ⊑ D</c> the axiom is, a property
/// range through the fresh atom the complex range is named as, and a class assertion onto the asserted
/// individual's own node, whose inhabitance makes the minted predecessor forced rather than
/// hypothetical; the generator reduction additionally delegates the whole module when the forward
/// role or a super-role bears a self-demand or chain. The snapshot tableau drops the inverse role, so an
/// admitted module is verified against the known correct answer, not the (symmetry/inverse-blind)
/// tableau.
/// </para>
/// <para>
/// <b>Inverse-role range, domain, and transitivity axioms are admitted</b> because each reduces to a
/// forward axiom on the paired role: <c>range(r⁻) = domain(r)</c> (typing the edge source),
/// <c>domain(r⁻) = range(r)</c> (typing the edge target), and <c>Transitive(r⁻)</c> holds exactly when
/// <c>Transitive(r)</c> does. Each is an owner-independent write — a range or domain concept holds over
/// every model edge and so cannot be attributed to a single existential owner — so none needs the
/// per-occurrence successor the backward-existential tier requires. A forward existential over an
/// inverse-paired role (<c>A ⊑ ∃r.B</c> with <c>r</c> symmetric or <c>InverseObjectProperties</c>-paired)
/// is decided by the classifier's witness mint: a module whose witness-reachable roles bear no chain or
/// self feature mints one content-keyed successor per <c>(role, filler)</c>, shared by every owner and
/// refined by the backward facts recorded into its intern key, while a module bearing such a feature
/// over a witness-reachable super-role keeps the per-owner provenance-keyed mint so the mirror stays
/// owner-local — with delegation retained only when a mirrored role itself bears a self-demand or
/// chain (the backward-existential tier the forward calculus does not yet reach). A range
/// over a mirrored role is decided: it is an owner-independent constraint that reduces to a domain on the
/// mirrored source role, exactly as an inverse-spelled range axiom does.
/// </para>
/// <para>
/// <b>Functional and inverse-functional object properties are admitted over the asserted ground
/// graph as a capability gain.</b> <c>FunctionalObjectProperty(r)</c> makes the two <c>r</c>-successors
/// of one individual the same, and <c>InverseFunctionalObjectProperty(r)</c> makes the two
/// <c>r</c>-predecessors of one individual the same — a <c>SameIndividual</c> the module did not state.
/// The classifier discovers it by unioning the asserted successors (resp. predecessors) over the role
/// and its sub-roles into the same union-find the stated identities use, so a resulting
/// <c>DifferentIndividuals</c> collision or a pooled disjoint-type clash is decided with no extra
/// machinery. The admission is gated to roles whose successors are exactly those asserted ground
/// edges: a functional role bearing a positive-position existential, a self-demand, or a chain — or
/// one that is itself symmetric or inverse-paired (whose mirror would add successors the asserted-edge
/// scan cannot see), anywhere in its sub-role closure — is delegated. The merge of two existential
/// successors (a TBox-subsumption concern) stays delegated. The snapshot tableau drops both
/// characteristics, so an admitted module is verified against the known correct answer.
/// </para>
/// <para>
/// <b>Asymmetric and irreflexive object properties are admitted over the asserted ground graph as a
/// capability gain.</b> <c>AsymmetricObjectProperty(r)</c> and <c>IrreflexiveObjectProperty(r)</c> are
/// negative global constraints — they generate no edge, they forbid configurations — and the classifier
/// decides them over the constrained role's asserted post-merge ground edges: a self-edge (which
/// asymmetry forbids too, since it implies irreflexivity), or — for an asymmetric role — an edge and its
/// reverse, anywhere in the role's sub-role closure, makes the module inconsistent. A told global
/// reflexivity under a constrained role (<c>ReflexiveObjectProperty(s)</c> or <c>⊤ ⊑ ∃s.Self</c> with
/// <c>s ⊑* r</c>) forces a self-edge on every element of the non-empty
/// domain and is decided inconsistent outright. The admission is gated to roles whose only edges are
/// those asserted ground edges: a constrained role bearing a positive-position existential, a self-demand,
/// a chain or transitivity, or a symmetric/inverse pairing anywhere in its sub-role closure — where the
/// saturation could add a self-edge or a reverse edge the asserted-edge scan cannot see — is delegated.
/// Both spellings of the six ground characteristics are admitted: each inverse spelling is exactly a forward
/// characteristic (<c>Asymmetric(r⁻)</c>, <c>Irreflexive(r⁻)</c>, <c>Symmetric(r⁻)</c>, <c>Reflexive(r⁻)</c>
/// each equal the forward characteristic on <c>r</c>, while the functional pair swaps —
/// <c>Functional(r⁻) ≡ InverseFunctional(r)</c> and <c>InverseFunctional(r⁻) ≡ Functional(r)</c>). A role
/// that is both symmetric-in-effect and asymmetric-constrained — itself asymmetric, or under an asymmetric
/// super-role — is decided EMPTY in every model: its characteristics reduce to <c>∃r.⊤ ⊑ ⊥</c> and the
/// module no longer delegates for that combination, deciding inconsistent for anything that populates the
/// role and consistent otherwise. The snapshot tableau names both as uninterpreted and answers
/// fragment-relative, so an admitted module is verified against the known correct answer.
/// </para>
/// <para>
/// <b>Property ranges are admitted</b> through a sound per-edge rule: the
/// <see cref="ElClassifier"/> routes every existential over a range-bearing role to a
/// fresh successor atom told to be the original filler, so the range types that
/// anonymous successor and never the named filler class. A range therefore reaches
/// the tableau's per-node universal reading — it constrains an existential's
/// successor, and a range disjoint from the filler makes the existential's owner
/// unsatisfiable — without contaminating the filler class everywhere it is used.
/// Domains, similarly, write to the edge source, which genuinely bears the role.
/// </para>
/// <para>
/// <b>Single-individual nominals are admitted wherever the nominal stays an edge target or a
/// negative-position concept, a capability gain.</b> The classifier interprets the singleton
/// <c>{a}</c> as the individual node for <c>a</c>, which lives only as an edge endpoint or in the
/// individual space and so never contaminates the named-class projection. Identity lives in two
/// regimes: a pre-intern union-find over ground keys (told <c>SameIndividual</c> identities, nominal
/// assertions, ground-spine folds, functional collapses — the state the distinctness and functional
/// scans read), and a saturation-time pooling of a live carrier's subsumers onto the individual it is
/// told to be behind the liveness gate, which is genuine discovered equality. The classifier's
/// ground-identity completion loop closes the two: a module whose told nominal can produce a
/// discovered identity while a ground-key consumer reads the union-find replays every discovered
/// identity as a told one and rebuilds, so those modules are DECIDED rather than answered from an
/// identity-incomplete pre-merge. Each rebuild merges at least two individuals, which bounds the
/// sequence by the module's individual count; a module passing that structural bound is delegated with
/// the restart marker named. Admitted: the nominal as the
/// filler of an existential in a class assertion (<c>ObjectHasValue(r, a)</c> /
/// <c>∃r.{a}</c>, an asserted edge from <c>x</c> to <c>a</c>, and the inverse spellings
/// <c>ObjectHasValue(r⁻, a)</c> / <c>∃r⁻.{a}</c>, the same edge from <c>a</c> to <c>x</c>);
/// the same nominal <b>on the conjunct spine of an asserted filler</b>
/// (<c>x : ∃r.(D ⊓ {a})</c> and the inverse spellings), where the witness IS <c>a</c>: the
/// existential is the ground edge and the filler's remaining conjuncts are assertions on <c>a</c>
/// itself, so the groundness descends and a further existential on that spine
/// (<c>x : ∃r.(D ⊓ {a} ⊓ ∃s.{b})</c>) is the further ground edge <c>(a, b) ∈ s</c>; a nominal
/// under a nominal-free layer (<c>x : ∃s.(∃r.{a})</c>) keeps the existential-filler proxy told the
/// nominal, whose constraints reach <c>a</c> through the liveness-gated merge;
/// the nominal shapes on the <b>conjunct spine</b> of an asserted class — an edge spelling
/// (<c>x : D ⊓ ∃r.{a}</c>, <c>x : D ⊓ ObjectHasValue(r⁻, a)</c>), written as the asserted edge, and a
/// bare one (<c>x : D ⊓ {a}</c>), folded into the union-find with the other told identities;
/// a bare class assertion
/// <c>x : {a}</c> (the told identity <c>x = a</c>, folded into the <c>SameIndividual</c>
/// union-find — so a <c>DifferentIndividuals</c> over individuals thereby collapsed is no longer
/// vacuous and forces inconsistency); the subclass side (<c>∃r.{a} ⊑ B</c> as a left existential
/// keyed on the individual node, <c>{a} ⊑ B</c> as told typing of <c>a</c>); a singleton operand
/// of a disjointness (<c>{a} ⊓ X ⊑ ⊥</c>); and a nominal in <b>superclass</b> position
/// (<c>A ⊑ ∃r.{a}</c>) or on either side of an equivalence, decided through a fresh
/// existential-filler proxy whose constraints reach the real individual only once it is inhabited —
/// the liveness gate — so an empty carrier class types <c>a</c> with nothing. The snapshot tableau
/// has no nominal reading and drops all of these, so EL decides strictly more and such modules are
/// verified against the known correct answer, not the (nominal-blind) tableau. <b>Delegated</b>: a
/// multi-individual <c>ObjectOneOf</c> in any position (a genuine disjunction); a nominal, of either
/// role direction, in a property domain or range class, where the survey seam is the
/// singleton-nominal flag alone and cannot separate the inverse half from the forward one; and a
/// nominal in any reserved-role spelling.
/// </para>
/// </remarks>
internal static class ElModuleSurvey
{
    /// <summary>Whether every axiom of the module lies within the EL⊥ fragment the saturation decides.</summary>
    /// <param name="axioms">The module's axioms.</param>
    /// <returns><see langword="true"/> when EL saturation soundly and completely decides the module.</returns>
    public static bool IsElDecidable(IReadOnlyList<OwlAxiom> axioms)
    {
        foreach(OwlAxiom axiom in axioms)
        {
            if(!IsElAxiom(axiom))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether one axiom lies within the EL fragment.</summary>
    /// <param name="axiom">The axiom.</param>
    /// <returns><see langword="true"/> when the axiom is EL-decidable.</returns>
    private static bool IsElAxiom(OwlAxiom axiom)
    {
        return axiom switch
        {
            OwlSubClassOfAxiom subClass => IsElClass(subClass.SubClass, admitSingletonNominals: true, admitInverseExistentials: true) && IsElSuperClass(subClass.SuperClass, admitSingletonNominals: true, admitInverseExistentials: true),
            OwlEquivalentClassesAxiom equivalent => IsElClass(equivalent.First, admitSingletonNominals: true, admitInverseExistentials: true) && IsElClass(equivalent.Second, admitSingletonNominals: true, admitInverseExistentials: true),
            OwlDisjointClassesAxiom disjoint => AllElClasses(disjoint.Operands, admitSingletonNominals: true, admitInverseExistentials: true),
            OwlClassAssertionAxiom assertion => IsElAssertedClass(assertion.Class),
            OwlObjectPropertyAssertionAxiom roleAssertion => !IsReservedRole(roleAssertion.Property.Iri),
            OwlSameIndividualAxiom or OwlDifferentIndividualsAxiom => true,
            OwlObjectPropertyDomainAxiom domain => !IsReservedRole(domain.Property.Property.Iri) && IsElClass(domain.Domain, admitInverseExistentials: true),
            OwlObjectPropertyRangeAxiom range => !IsReservedRole(range.Property.Property.Iri) && IsElClass(range.Range, admitInverseExistentials: true),
            OwlSubObjectPropertyOfAxiom { SubProperty.IsInverse: false, SuperProperty.IsInverse: false } subRole => !IsReservedRole(subRole.SubProperty.Property.Iri) && !IsReservedRole(subRole.SuperProperty.Property.Iri),
            OwlSubObjectPropertyOfAxiom { SubProperty.IsInverse: true, SuperProperty.IsInverse: false } inverseSubRole => !IsReservedRole(inverseSubRole.SubProperty.Property.Iri) && !IsReservedRole(inverseSubRole.SuperProperty.Property.Iri),
            OwlSubObjectPropertyOfAxiom { SubProperty.IsInverse: false, SuperProperty.IsInverse: true } subRoleInverse => !IsReservedRole(subRoleInverse.SubProperty.Property.Iri) && !IsReservedRole(subRoleInverse.SuperProperty.Property.Iri),
            OwlEquivalentObjectPropertiesAxiom { First.IsInverse: false, Second.IsInverse: false } equivalentRoles => !IsReservedRole(equivalentRoles.First.Property.Iri) && !IsReservedRole(equivalentRoles.Second.Property.Iri),
            OwlDataPropertyDomainAxiom dataDomain => !IsReservedDataProperty(dataDomain.Property.Iri) && dataDomain.Domain is OwlClassReference,
            OwlDataPropertyRangeAxiom dataRange => !IsReservedDataProperty(dataRange.Property.Iri),
            OwlSubDataPropertyOfAxiom subData => !IsReservedDataProperty(subData.SubProperty.Iri) && !IsReservedDataProperty(subData.SuperProperty.Iri),
            OwlEquivalentDataPropertiesAxiom equivalentData => !IsReservedDataProperty(equivalentData.First.Iri) && !IsReservedDataProperty(equivalentData.Second.Iri),
            OwlFunctionalDataPropertyAxiom functionalData => !IsReservedDataProperty(functionalData.Property.Iri),
            OwlDisjointDataPropertiesAxiom disjointData => AllNonReservedDataProperties(disjointData.Operands),
            OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Transitive } transitive => !IsReservedRole(transitive.Property.Property.Iri),
            OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Reflexive } reflexive => !IsReservedRole(reflexive.Property.Property.Iri),
            OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Symmetric } symmetric => !IsReservedRole(symmetric.Property.Property.Iri),
            OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Functional } functional => !IsReservedRole(functional.Property.Property.Iri),
            OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.InverseFunctional } inverseFunctional => !IsReservedRole(inverseFunctional.Property.Property.Iri),
            OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Asymmetric } asymmetric => !IsReservedRole(asymmetric.Property.Property.Iri),
            OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Irreflexive } irreflexive => !IsReservedRole(irreflexive.Property.Property.Iri),
            OwlInverseObjectPropertiesAxiom { First.IsInverse: false, Second.IsInverse: false } inverse => !IsReservedRole(inverse.First.Property.Iri) && !IsReservedRole(inverse.Second.Property.Iri),
            OwlPropertyChainAxiom chain => IsElChain(chain),
            OwlDeclarationAxiom or OwlImportAxiom or OwlAnnotationAssertionAxiom or OwlSubAnnotationPropertyOfAxiom
                or OwlAnnotationPropertyDomainAxiom or OwlAnnotationPropertyRangeAxiom => true,
            _ => false,
        };
    }

    /// <summary>
    /// Whether a property chain is an EL role inclusion the saturation interprets: a
    /// chain of forward, non-reserved links included in a forward, non-reserved
    /// superrole. The saturation decomposes a longer chain left-associatively through
    /// fresh roles and composes its edges to fixpoint.
    /// </summary>
    /// <param name="chain">The property-chain axiom.</param>
    /// <returns><see langword="true"/> when the chain is an EL role inclusion.</returns>
    private static bool IsElChain(OwlPropertyChainAxiom chain)
    {
        if(chain.SuperProperty.IsInverse || IsReservedRole(chain.SuperProperty.Property.Iri))
        {
            return false;
        }

        foreach(OwlObjectPropertyExpression link in chain.Chain)
        {
            if(link.IsInverse || IsReservedRole(link.Property.Iri))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether every expression in the list is an EL class expression.</summary>
    /// <param name="expressions">The expressions.</param>
    /// <param name="admitSingletonNominals">Whether to admit single-individual nominal leaves — set for disjointness operands, whose pairwise <c>Intersection(...) ⊑ ⊥</c> reduction keeps each operand on the subclass side.</param>
    /// <param name="admitInverseExistentials">Whether to admit inverse-existential leaves — set for disjointness operands for the same reason: the pairwise reduction keeps each operand in subclass polarity, where the synthetic-mirror reduction decides <c>∃r⁻.C</c>.</param>
    /// <returns><see langword="true"/> when all are EL.</returns>
    private static bool AllElClasses(IReadOnlyList<OwlClassExpression> expressions, bool admitSingletonNominals = false, bool admitInverseExistentials = false)
    {
        foreach(OwlClassExpression expression in expressions)
        {
            if(!IsElClass(expression, admitSingletonNominals, admitInverseExistentials))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a class expression is an EL concept: a named class, a conjunction
    /// of EL concepts, an existential over a forward non-reserved role with an
    /// EL filler, or a single-property data existential
    /// (<c>DataSomeValuesFrom</c>/<c>DataHasValue</c>) over a non-reserved data property, which
    /// the classifier names as the concept <c>∃d.R ⊑ F</c> and recognizes on the carriers whose
    /// own value demands entail it. The data range itself is not walked — the classifier decides it
    /// against the value-space checker and names an undecidable one on the remainder. The walk is
    /// an explicit stack — no call-stack recursion.
    /// </summary>
    /// <param name="root">The class expression.</param>
    /// <param name="admitSingletonNominals">
    /// Whether to admit a single-individual nominal leaf — a one-individual
    /// <c>ObjectOneOf</c> or an <c>ObjectHasValue</c> over a non-reserved role in either direction.
    /// Set in <b>negative (subclass / disjointness-operand) position</b>, where the
    /// classifier decides the nominal as a told identity or a left existential keyed on the
    /// individual node — over the role itself for the forward spelling and over its synthetic mirror
    /// for the inverse one — and on <b>both sides of an equivalence</b>, whose superclass-direction
    /// occurrence the classifier decides through the liveness-gated proxy of its superclass branch
    /// while the subclass-direction one rides the left-naming nominal arms. Left
    /// <see langword="false"/> for a <b>property domain or range class</b> alone, the one seam that
    /// carries this direction-blind flag with no arm of its own.
    /// Polarity never flips inside the EL fragment, so the flag holds for every nested leaf.
    /// </param>
    /// <param name="admitInverseExistentials">
    /// Whether to admit an inverse existential (<c>∃r⁻.C</c>) leaf. Set on every position this survey
    /// admits the expression in: in <b>negative (subclass / disjointness-operand) position</b>, where the
    /// classifier decides it as an ordinary left existential over a synthetic mirror role (every
    /// <c>r</c>-edge forces the reverse mirror edge, so <c>∃r⁻.C ⊑ Y</c> and a disjointness operand's
    /// pairwise <c>∃r⁻.C ⊓ X ⊑ ⊥</c> both ride the shipped mirror + left-existential rules); on <b>either
    /// side of an equivalence</b>, whose superclass-direction occurrence the classifier decides by the
    /// eager generator reduction to a forward existential over a synthetic per-<c>r</c> generator role
    /// (<c>g ⊑ r⁻</c>); and in a <b>property domain or range class</b>, each of which normalizes into that
    /// same superclass polarity — a domain axiom into the inclusion <c>∃p.⊤ ⊑ D</c>, a range axiom into a
    /// fresh range atom told the range expression — so the generator reduction reaches both and the
    /// per-owner witness is minted from the <c>p</c>-source, respectively the <c>p</c>-target, that
    /// genuinely bears the role. Polarity never flips inside the EL fragment, so the flag holds for every
    /// nested occurrence on the admitted side.
    /// </param>
    /// <returns><see langword="true"/> when the expression is EL.</returns>
    private static bool IsElClass(OwlClassExpression root, bool admitSingletonNominals = false, bool admitInverseExistentials = false)
    {
        Stack<OwlClassExpression> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            IReadOnlyList<OwlClassExpression>? children = work.Pop() switch
            {
                OwlClassReference => [],
                OwlObjectIntersectionOf intersection => intersection.Operands,
                OwlObjectSomeValuesFrom { Property.IsInverse: false } existential when !IsReservedRole(existential.Property.Property.Iri) => [existential.Filler],
                (OwlObjectSomeValuesFrom { Property.IsInverse: true } inverseExistential) when admitInverseExistentials && !IsReservedRole(inverseExistential.Property.Property.Iri) => [inverseExistential.Filler],
                OwlObjectHasSelf hasSelf when !IsReservedRole(hasSelf.Property.Property.Iri) => [],
                OwlDataSomeValuesFrom { Properties.Count: 1 } dataSome when !IsReservedDataProperty(dataSome.Properties[0].Iri) => [],
                OwlDataHasValue dataHas when !IsReservedDataProperty(dataHas.Property.Iri) => [],
                (OwlObjectOneOf { Individuals: [NamedNode or BlankNode] }) when admitSingletonNominals => [],
                (OwlObjectHasValue { Individual: NamedNode or BlankNode } hasValue) when admitSingletonNominals && !IsReservedRole(hasValue.Property.Property.Iri) => [],
                _ => null,
            };

            if(children is null)
            {
                return false;
            }

            foreach(OwlClassExpression child in children)
            {
                work.Push(child);
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a class expression is an EL concept admitted in <b>positive
    /// (superclass / class-assertion) position</b>: everything <see cref="IsElClass"/>
    /// admits, plus a single-property <c>DataSomeValuesFrom</c> or a <c>DataHasValue</c>
    /// over a non-reserved data property. In positive position a data restriction is a
    /// value demand the classifier decides by the range's emptiness; in negative
    /// (subclass / equivalence / disjointness) position the same construct reads as a
    /// data universal the EL calculus cannot represent, so it is admitted only here.
    /// Polarity never flips inside the EL fragment (conjunction and existential both
    /// preserve it), so a data restriction nested through them stays a demand and is
    /// admitted; its object-existential filler is surveyed at the same positive polarity.
    /// </summary>
    /// <param name="root">The class expression.</param>
    /// <param name="admitSingletonNominals">
    /// Whether to admit a single-individual nominal leaf — a one-individual <c>ObjectOneOf</c> or an
    /// <c>ObjectHasValue</c> over a non-reserved role in either direction, the inverse spelling
    /// rewriting into the enumeration form the same machinery carries. Set for the <b>superclass side
    /// of an
    /// inclusion and equivalence</b>, where the classifier decides the nominal through a fresh
    /// existential-filler proxy whose constraints reach the real individual only once it is inhabited
    /// (the liveness gate), and for the <b>class-assertion fallback</b>, which reaches every position
    /// the assertion's own arms do not: a nominal below the top level of a filler, where the same
    /// proxy path carries it from a genuinely inhabited owner, and one on the asserted class's
    /// conjunct spine, where the classifier writes the asserted edge or folds the told identity. The
    /// class assertion's top-level asserted-edge and bare-identity shapes are decided by its own arms
    /// before the fallback is reached.
    /// </param>
    /// <param name="admitInverseExistentials">
    /// Whether to admit an inverse existential (<c>∃r⁻.C</c>) leaf. Set for the <b>superclass side of an
    /// inclusion and equivalence</b>, where the classifier reduces it at normalization to a forward
    /// existential over a synthetic per-<c>r</c> generator role (<c>g ⊑ r⁻</c>) — each owner's
    /// <c>r</c>-predecessor minted as a per-owner forward <c>g</c>-successor, the mirror writing the real
    /// <c>r</c>-edge back onto the owner — riding the shipped right-existential mint; and for the
    /// <b>class-assertion default</b>, where the same reduction runs on the asserted individual's own
    /// node, so the demanded predecessor is forced from a genuinely inhabited element rather than from a
    /// possibly-empty carrier class. Polarity never flips inside the EL fragment, so the flag holds for
    /// every nested occurrence on the admitted side.
    /// </param>
    /// <returns><see langword="true"/> when the expression is EL in positive position.</returns>
    private static bool IsElSuperClass(OwlClassExpression root, bool admitSingletonNominals = false, bool admitInverseExistentials = false)
    {
        Stack<OwlClassExpression> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            IReadOnlyList<OwlClassExpression>? children = work.Pop() switch
            {
                OwlClassReference => [],
                OwlObjectIntersectionOf intersection => intersection.Operands,
                OwlObjectSomeValuesFrom { Property.IsInverse: false } existential when !IsReservedRole(existential.Property.Property.Iri) => [existential.Filler],
                (OwlObjectSomeValuesFrom { Property.IsInverse: true } inverseExistential) when admitInverseExistentials && !IsReservedRole(inverseExistential.Property.Property.Iri) => [inverseExistential.Filler],
                OwlObjectHasSelf hasSelf when !IsReservedRole(hasSelf.Property.Property.Iri) => [],
                OwlDataSomeValuesFrom { Properties.Count: 1 } dataSome when !IsReservedDataProperty(dataSome.Properties[0].Iri) => [],
                OwlDataHasValue dataHas when !IsReservedDataProperty(dataHas.Property.Iri) => [],
                (OwlObjectOneOf { Individuals: [NamedNode or BlankNode] }) when admitSingletonNominals => [],
                (OwlObjectHasValue { Individual: NamedNode or BlankNode } hasValue) when admitSingletonNominals && !IsReservedRole(hasValue.Property.Property.Iri) => [],
                _ => null,
            };

            if(children is null)
            {
                return false;
            }

            foreach(OwlClassExpression child in children)
            {
                work.Push(child);
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a <b>class-assertion</b> class is admitted: everything <see cref="IsElSuperClass"/>
    /// admits, plus the two nominal shapes a class assertion decides — a single-individual nominal
    /// as the filler of an existential (<c>ObjectHasValue(r, a)</c> or
    /// <c>ObjectSomeValuesFrom(r, ObjectOneOf(a))</c>, both <c>∃r.{a}</c>, seeding an asserted edge
    /// to the individual node, and the inverse spellings <c>ObjectHasValue(r⁻, a)</c> and
    /// <c>ObjectSomeValuesFrom(r⁻, ObjectOneOf(a))</c>, both <c>∃r⁻.{a}</c>, seeding that same edge with
    /// its endpoints exchanged — the ground fact <c>(a, x) ∈ r</c>, which is what "<c>x</c> has an
    /// <c>r</c>-predecessor which is <c>a</c>" says), and a bare singleton nominal <c>{a}</c> (the told
    /// identity <c>x = a</c>, folded into the <c>SameIndividual</c> union-find). Both name a concrete
    /// individual, so the constraint is forced from a genuinely inhabited node — the soundness
    /// condition the superclass position cannot meet, where a possibly-empty carrier would route the
    /// nominal onto the individual node hypothetically and a forced clash there would wrongly condemn
    /// the module. An <b>inverse existential</b> <c>x : ∃r⁻.C</c> is admitted on the same argument:
    /// the classifier reduces it at normalization to a forward existential over the synthetic
    /// per-<c>r</c> generator role and mints the demanded <c>r</c>-predecessor from the asserted
    /// individual's node, so the witness is forced by an inhabited owner and the mirror's real
    /// <c>r</c>-edge back onto that individual carries a range or domain clash to it. A self-restriction
    /// <c>x : ObjectHasSelf(r)</c> is admitted in either spelling, a self-edge being its own reverse.
    /// Subclass-side and disjointness-operand nominals are admitted separately by
    /// <see cref="IsElClass"/> in negative position, and superclass-side ones by
    /// <see cref="IsElSuperClass"/> behind the classifier's liveness gate. The fallback carries the
    /// remaining assertion-position nominal shapes: one <b>on a filler's conjunct spine</b>
    /// (<c>x : ∃r.(D ⊓ {a})</c> and its inverse and deeper spellings), where the witness IS the named
    /// individual, so the classifier writes the ground edge and asserts the filler's remaining
    /// conjuncts on that individual, descending through it for a further ground edge; one <b>under a
    /// nominal-free layer</b> (<c>x : ∃s.(∃r.{a})</c>), which the classifier names as an
    /// existential-filler proxy told the nominal, forced from the asserted individual's own inhabited
    /// node; and the shapes on the asserted class's <b>conjunct spine</b>, an edge spelling written as
    /// the asserted edge and a bare <c>{a}</c> folded into the union-find with the other told
    /// identities. A multi-individual enumeration and a reserved-role spelling delegate at any depth,
    /// and a module pairing a told nominal identity with a pre-intern identity consumer is decided by
    /// the classifier's ground-identity completion loop, which replays each discovered identity as a
    /// told one and rebuilds until the two regimes agree.
    /// </summary>
    /// <param name="assertedClass">The asserted class expression.</param>
    /// <returns><see langword="true"/> when the asserted class is EL.</returns>
    private static bool IsElAssertedClass(OwlClassExpression assertedClass)
    {
        return assertedClass switch
        {
            OwlObjectHasValue hasValue => !IsReservedRole(hasValue.Property.Property.Iri),
            OwlObjectSomeValuesFrom { Filler: OwlObjectOneOf { Individuals.Count: 1 } } singletonNominal => !IsReservedRole(singletonNominal.Property.Property.Iri),
            (OwlObjectOneOf { Individuals: [NamedNode or BlankNode] }) => true,
            _ => IsElSuperClass(assertedClass, admitSingletonNominals: true, admitInverseExistentials: true),
        };
    }

    /// <summary>Whether an object-property IRI is one of the reserved built-ins, whose fixed full/empty extension the EL calculus does not interpret.</summary>
    /// <param name="role">The object-property IRI.</param>
    /// <returns><see langword="true"/> for a reserved object property.</returns>
    private static bool IsReservedRole(Utf8String role)
    {
        return role.Equals(OwlVocabulary.TopObjectProperty) || role.Equals(OwlVocabulary.BottomObjectProperty);
    }

    /// <summary>Whether a data-property IRI is one of the reserved built-ins, whose fixed full/empty extension the EL calculus does not interpret.</summary>
    /// <param name="property">The data-property IRI.</param>
    /// <returns><see langword="true"/> for a reserved data property.</returns>
    private static bool IsReservedDataProperty(Utf8String property)
    {
        return property.Equals(OwlVocabulary.TopDataProperty) || property.Equals(OwlVocabulary.BottomDataProperty);
    }

    /// <summary>Whether every operand of a data-property list names a non-reserved data property.</summary>
    /// <param name="properties">The data properties.</param>
    /// <returns><see langword="true"/> when none is a reserved built-in.</returns>
    private static bool AllNonReservedDataProperties(IReadOnlyList<NamedNode> properties)
    {
        foreach(NamedNode property in properties)
        {
            if(IsReservedDataProperty(property.Iri))
            {
                return false;
            }
        }

        return true;
    }
}
