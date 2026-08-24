using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The subsumption sweep signature the consequence-based engines share: the ALC
/// translation's named classes (in first-appearance order) unioned with every
/// named class occurring anywhere in the module's axioms (other than
/// <c>owl:Thing</c> / <c>owl:Nothing</c>). The base ALC <c>Translate</c> never
/// reaches several class positions the equality and Self tiers decide against — a
/// cardinality filler (<c>≤1 r.B</c>, <c>≥2 r.B</c>), an inverse-role existential
/// or universal filler (<c>C</c> in <c>B ⊑ ∃r⁻.C</c> / <c>∀r⁻.C</c>, gated
/// <c>Property.IsInverse: false</c>), or a HasSelf consumer/producer class
/// (<c>ObjectHasSelf</c> has no ALC translation) — so those classes are absent from
/// the ALC signature and, without this widening, their subsumptions are unswept and
/// unobservable.
/// </summary>
internal static class ModuleSweepSignature
{
    /// <summary>
    /// Builds the module's widened subsumption sweep signature: the ALC
    /// translation's named classes, then every remaining named class the axiom walk
    /// reaches, deduplicated and in walk order after the ALC prefix.
    /// </summary>
    /// <param name="module">The admitted module whose signature to build.</param>
    /// <returns>The deduplicated sweep signature: the ALC prefix in <c>Translate</c> order, then the remaining walk-collected classes in module-walk order.</returns>
    public static List<Utf8String> Build(ReasoningModule module)
    {
        List<Utf8String> signature = AlcModuleReasoner.Translate(module).SignatureClasses;
        HashSet<Utf8String> seen = new(signature);

        List<Utf8String> axiomClasses = [];
        foreach(OwlAxiom axiom in module.Axioms)
        {
            axiomClasses.Clear();
            CollectAxiomClasses(axiom, axiomClasses);
            foreach(Utf8String iri in axiomClasses)
            {
                if(seen.Add(iri))
                {
                    signature.Add(iri);
                }
            }
        }

        return signature;
    }

    /// <summary>
    /// Whether any class expression in the module carries an
    /// <see cref="OwlObjectHasSelf"/> restriction, at any nesting depth. The walk
    /// descends the same composites as <see cref="Build"/>, so a HasSelf under an
    /// intersection, union, complement, or restriction filler is found.
    /// </summary>
    /// <param name="module">The module to inspect.</param>
    /// <returns><see langword="true"/> when the module mentions a local-reflexivity restriction.</returns>
    public static bool CarriesHasSelf(ReasoningModule module)
    {
        Stack<OwlClassExpression> work = new();
        foreach(OwlAxiom axiom in module.Axioms)
        {
            foreach(OwlClassExpression root in AxiomClassExpressions(axiom))
            {
                work.Push(root);
            }

            while(work.Count > 0)
            {
                OwlClassExpression expression = work.Pop();
                if(expression is OwlObjectHasSelf)
                {
                    return true;
                }

                foreach(OwlClassExpression child in ClassExpressionChildren(expression))
                {
                    work.Push(child);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Walks an axiom's class expressions with an explicit stack, appending every
    /// named class it mentions (other than <c>owl:Thing</c>/<c>owl:Nothing</c>,
    /// which the ALC signature also omits) to <paramref name="classesToAppendTo"/>
    /// in walk order. The walk descends every composite — intersections, unions,
    /// complements, existential/universal/cardinality restriction fillers — so a
    /// HasSelf consumer/producer class, an inverse-role filler, and a cardinality
    /// filler are all captured regardless of the enclosing construct.
    /// </summary>
    /// <param name="axiom">The axiom to walk.</param>
    /// <param name="classesToAppendTo">The list the axiom's named-class IRIs are appended to.</param>
    private static void CollectAxiomClasses(OwlAxiom axiom, List<Utf8String> classesToAppendTo)
    {
        Stack<OwlClassExpression> work = new();
        foreach(OwlClassExpression root in AxiomClassExpressions(axiom))
        {
            work.Push(root);
        }

        while(work.Count > 0)
        {
            OwlClassExpression expression = work.Pop();
            if(expression is OwlClassReference reference)
            {
                Utf8String iri = reference.Class.Iri;
                if(!iri.Equals(OwlVocabulary.Thing) && !iri.Equals(OwlVocabulary.Nothing))
                {
                    classesToAppendTo.Add(iri);
                }
            }

            foreach(OwlClassExpression child in ClassExpressionChildren(expression))
            {
                work.Push(child);
            }
        }
    }

    /// <summary>The class expressions to survey directly on an axiom — the roots the walk descends from; an axiom carrying no class expression yields none. A disjoint union contributes its defined class and every member (the members assert named subsumptions the sweep must observe even when a member leaves the ALC prefix), and a class assertion contributes its asserted type.</summary>
    /// <param name="axiom">The axiom.</param>
    /// <returns>The axiom's root class expressions.</returns>
    private static IReadOnlyList<OwlClassExpression> AxiomClassExpressions(OwlAxiom axiom)
    {
        return axiom switch
        {
            OwlSubClassOfAxiom subClass => [subClass.SubClass, subClass.SuperClass],
            OwlEquivalentClassesAxiom equivalent => [equivalent.First, equivalent.Second],
            OwlDisjointClassesAxiom disjoint => disjoint.Operands,
            OwlDisjointUnionAxiom disjointUnion => [new OwlClassReference(disjointUnion.Class), .. disjointUnion.Operands],
            OwlClassAssertionAxiom assertion => [assertion.Class],
            OwlObjectPropertyDomainAxiom domain => [domain.Domain],
            OwlObjectPropertyRangeAxiom range => [range.Range],
            _ => [],
        };
    }

    /// <summary>The immediate class-expression subexpressions of a composite — the operands, complemented operand, or restriction filler; a leaf or a filler-free restriction yields none.</summary>
    /// <param name="expression">The class expression.</param>
    /// <returns>The subexpressions to descend into.</returns>
    private static IReadOnlyList<OwlClassExpression> ClassExpressionChildren(OwlClassExpression expression)
    {
        return expression switch
        {
            OwlObjectIntersectionOf intersection => intersection.Operands,
            OwlObjectUnionOf union => union.Operands,
            OwlObjectComplementOf complement => [complement.Operand],
            OwlObjectSomeValuesFrom existential => [existential.Filler],
            OwlObjectAllValuesFrom universal => [universal.Filler],
            OwlObjectCardinality { Filler: not null } cardinality => [cardinality.Filler],
            _ => [],
        };
    }
}
