using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The constant-fold pass over a module's class-expression positions: every
/// restriction whose fixed-extension reserved property makes it semantically
/// <c>owl:Thing</c> or <c>owl:Nothing</c> at every element of every
/// interpretation is replaced by that constant, so a shape the arms would
/// otherwise reject for its reserved role reaches the deciding calculi as a
/// plain named-class reference. The reserved properties whose extension is
/// fixed are <c>owl:bottomObjectProperty</c> (empty),
/// <c>owl:topObjectProperty</c> (universal over individuals), and
/// <c>owl:bottomDataProperty</c> (empty); <c>owl:topDataProperty</c> is not
/// folded — its class-expression semantics interact with datatype value
/// spaces. The fold is purely syntactic, discards the filler or range where
/// the constant holds regardless of it, and performs no boolean simplification:
/// a folded constant simplifies its parent only through the deciding calculi's
/// existing <c>owl:Thing</c> / <c>owl:Nothing</c> handling.
/// </summary>
public static class ReservedVocabularyFold
{
    /// <summary>The <c>owl:Thing</c> reference a shape that is universally true folds to.</summary>
    private static OwlClassReference ThingClass { get; } = new(new NamedNode(OwlVocabulary.Thing));

    /// <summary>The <c>owl:Nothing</c> reference a shape that is universally false folds to.</summary>
    private static OwlClassReference NothingClass { get; } = new(new NamedNode(OwlVocabulary.Nothing));

    /// <summary>
    /// Folds the module's foldable class-expression shapes to constants. A
    /// module with no foldable shape is returned by reference — a cheap
    /// containment pre-scan gates the rebuild — so an unaffected module
    /// allocates nothing; otherwise a new module is returned whose affected
    /// axioms are rebuilt (their origin and annotations carried by
    /// <c>with</c>) and whose unaffected axioms are carried by reference.
    /// </summary>
    /// <param name="module">The module to fold.</param>
    /// <returns>The folded module, or <paramref name="module"/> itself when nothing folds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    public static ReasoningModule Apply(ReasoningModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        if(!ContainsFoldable(module.Axioms))
        {
            return module;
        }

        List<OwlAxiom> rebuilt = new(module.Axioms.Count);
        foreach(OwlAxiom axiom in module.Axioms)
        {
            rebuilt.Add(FoldAxiom(axiom));
        }

        return module with { Axioms = rebuilt };
    }

    /// <summary>How a class-expression node folds.</summary>
    private enum NodeFoldKind
    {
        /// <summary>The node is a reserved restriction whose whole meaning is a constant — replace it, discarding its filler or range.</summary>
        Constant = 0,

        /// <summary>The node is a reserved restriction that is not foldable (a global top shape) — keep it verbatim and do not descend into it.</summary>
        KeepVerbatim = 1,

        /// <summary>The node is a boolean connective or a non-reserved restriction — descend into its class-expression children and rebuild.</summary>
        Recurse = 2,
    }

    /// <summary>
    /// Whether any class-expression position in the module's foldable-axiom set
    /// holds a foldable shape. The scan descends through boolean operands and
    /// the fillers of non-reserved restrictions but never into a reserved
    /// restriction, mirroring the fold's own walk, so a module the fold would
    /// leave unchanged returns <see langword="false"/> without a rebuild.
    /// </summary>
    /// <param name="axioms">The module's axioms.</param>
    /// <returns><see langword="true"/> when at least one shape folds to a constant.</returns>
    private static bool ContainsFoldable(IReadOnlyList<OwlAxiom> axioms)
    {
        Stack<(OwlClassExpression Node, bool ChildrenPushed)> scan = new();
        foreach(OwlAxiom axiom in axioms)
        {
            PushAxiomSlots(axiom, scan);
        }

        while(scan.Count > 0)
        {
            OwlClassExpression node = scan.Pop().Node;
            NodeFoldKind kind = Classify(node, out _);
            if(kind == NodeFoldKind.Constant)
            {
                return true;
            }

            if(kind == NodeFoldKind.Recurse)
            {
                PushClassChildren(node, scan);
            }
        }

        return false;
    }

    /// <summary>
    /// Pushes an axiom's foldable class-expression slots onto the walk stack —
    /// the class-expression positions of <c>SubClassOf</c>,
    /// <c>EquivalentClasses</c>, <c>DisjointClasses</c>, <c>DisjointUnion</c>,
    /// <c>ClassAssertion</c>, <c>ObjectPropertyDomain</c> (domain),
    /// <c>ObjectPropertyRange</c> (range), <c>DataPropertyDomain</c> (domain),
    /// and <c>HasKey</c> (class). Every other axiom carries no folded slot.
    /// </summary>
    /// <param name="axiom">The axiom whose slots are pushed.</param>
    /// <param name="work">The walk stack the slots are pushed onto.</param>
    private static void PushAxiomSlots(OwlAxiom axiom, Stack<(OwlClassExpression Node, bool ChildrenPushed)> work)
    {
        switch(axiom)
        {
            case OwlSubClassOfAxiom sub:
                work.Push((sub.SubClass, false));
                work.Push((sub.SuperClass, false));

                break;
            case OwlEquivalentClassesAxiom equivalent:
                work.Push((equivalent.First, false));
                work.Push((equivalent.Second, false));

                break;
            case OwlDisjointClassesAxiom disjoint:
                foreach(OwlClassExpression operand in disjoint.Operands)
                {
                    work.Push((operand, false));
                }

                break;
            case OwlDisjointUnionAxiom disjointUnion:
                foreach(OwlClassExpression operand in disjointUnion.Operands)
                {
                    work.Push((operand, false));
                }

                break;
            case OwlClassAssertionAxiom classAssertion:
                work.Push((classAssertion.Class, false));

                break;
            case OwlObjectPropertyDomainAxiom domain:
                work.Push((domain.Domain, false));

                break;
            case OwlObjectPropertyRangeAxiom range:
                work.Push((range.Range, false));

                break;
            case OwlDataPropertyDomainAxiom dataDomain:
                work.Push((dataDomain.Domain, false));

                break;
            case OwlHasKeyAxiom hasKey:
                work.Push((hasKey.Class, false));

                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Folds an axiom's foldable class-expression slots, returning the axiom by
    /// reference when no slot changed and a <c>with</c>-rebuilt axiom — origin
    /// and annotations carried — otherwise. The folded-slot set matches
    /// <see cref="PushAxiomSlots"/>.
    /// </summary>
    /// <param name="axiom">The axiom to fold.</param>
    /// <returns>The folded axiom, or <paramref name="axiom"/> itself when nothing changed.</returns>
    private static OwlAxiom FoldAxiom(OwlAxiom axiom)
    {
        switch(axiom)
        {
            case OwlSubClassOfAxiom sub:
            {
                OwlClassExpression subClass = FoldExpression(sub.SubClass);
                OwlClassExpression superClass = FoldExpression(sub.SuperClass);

                return ReferenceEquals(subClass, sub.SubClass) && ReferenceEquals(superClass, sub.SuperClass)
                    ? axiom
                    : sub with { SubClass = subClass, SuperClass = superClass };
            }

            case OwlEquivalentClassesAxiom equivalent:
            {
                OwlClassExpression first = FoldExpression(equivalent.First);
                OwlClassExpression second = FoldExpression(equivalent.Second);

                return ReferenceEquals(first, equivalent.First) && ReferenceEquals(second, equivalent.Second)
                    ? axiom
                    : equivalent with { First = first, Second = second };
            }

            case OwlDisjointClassesAxiom disjoint:
            {
                IReadOnlyList<OwlClassExpression>? folded = FoldOperands(disjoint.Operands);

                return folded is null ? axiom : disjoint with { Operands = folded };
            }

            case OwlDisjointUnionAxiom disjointUnion:
            {
                IReadOnlyList<OwlClassExpression>? folded = FoldOperands(disjointUnion.Operands);

                return folded is null ? axiom : disjointUnion with { Operands = folded };
            }

            case OwlClassAssertionAxiom classAssertion:
            {
                OwlClassExpression folded = FoldExpression(classAssertion.Class);

                return ReferenceEquals(folded, classAssertion.Class) ? axiom : classAssertion with { Class = folded };
            }

            case OwlObjectPropertyDomainAxiom domain:
            {
                OwlClassExpression folded = FoldExpression(domain.Domain);

                return ReferenceEquals(folded, domain.Domain) ? axiom : domain with { Domain = folded };
            }

            case OwlObjectPropertyRangeAxiom range:
            {
                OwlClassExpression folded = FoldExpression(range.Range);

                return ReferenceEquals(folded, range.Range) ? axiom : range with { Range = folded };
            }

            case OwlDataPropertyDomainAxiom dataDomain:
            {
                OwlClassExpression folded = FoldExpression(dataDomain.Domain);

                return ReferenceEquals(folded, dataDomain.Domain) ? axiom : dataDomain with { Domain = folded };
            }

            case OwlHasKeyAxiom hasKey:
            {
                OwlClassExpression folded = FoldExpression(hasKey.Class);

                return ReferenceEquals(folded, hasKey.Class) ? axiom : hasKey with { Class = folded };
            }

            default:
                return axiom;
        }
    }

    /// <summary>
    /// Folds every operand of a list, returning <see langword="null"/> when no
    /// operand changed and a rebuilt list — unchanged operands by reference —
    /// otherwise.
    /// </summary>
    /// <param name="operands">The operand list.</param>
    /// <returns>The rebuilt array, or <see langword="null"/> when nothing changed.</returns>
    private static OwlClassExpression[]? FoldOperands(IReadOnlyList<OwlClassExpression> operands)
    {
        OwlClassExpression[]? rebuilt = null;
        for(int index = 0; index < operands.Count; index++)
        {
            OwlClassExpression original = operands[index];
            OwlClassExpression folded = FoldExpression(original);
            if(!ReferenceEquals(folded, original))
            {
                if(rebuilt is null)
                {
                    rebuilt = new OwlClassExpression[operands.Count];
                    for(int copy = 0; copy < operands.Count; copy++)
                    {
                        rebuilt[copy] = operands[copy];
                    }
                }

                rebuilt[index] = folded;
            }
        }

        return rebuilt;
    }

    /// <summary>
    /// Folds one class expression with an explicit post-order stack — no
    /// call-stack recursion. Children fold on the way up into a
    /// reference-keyed memo, so a parent rebuilds from its folded children and
    /// returns by reference when none of them changed; the whole expression is
    /// therefore returned by reference when nothing inside it folds.
    /// </summary>
    /// <param name="root">The expression to fold.</param>
    /// <returns>The folded expression, or <paramref name="root"/> itself when nothing folds.</returns>
    private static OwlClassExpression FoldExpression(OwlClassExpression root)
    {
        Dictionary<OwlClassExpression, OwlClassExpression> results = new(ReferenceEqualityComparer.Instance);
        Stack<(OwlClassExpression Node, bool ChildrenPushed)> work = new();
        work.Push((root, false));

        while(work.Count > 0)
        {
            (OwlClassExpression node, bool childrenPushed) = work.Pop();
            if(results.ContainsKey(node))
            {
                continue;
            }

            if(childrenPushed)
            {
                results[node] = Rebuild(node, results);

                continue;
            }

            NodeFoldKind kind = Classify(node, out OwlClassReference? constant);
            switch(kind)
            {
                case NodeFoldKind.Constant:
                    results[node] = constant!;

                    break;
                case NodeFoldKind.KeepVerbatim:
                    results[node] = node;

                    break;
                default:
                    work.Push((node, true));
                    PushClassChildren(node, work);

                    break;
            }
        }

        return results[root];
    }

    /// <summary>
    /// Rebuilds a boolean connective or non-reserved restriction from its
    /// folded children, returning the node by reference when no child changed.
    /// A reserved restriction never reaches this method: it folds to a constant
    /// or is kept verbatim before its children are ever pushed.
    /// </summary>
    /// <param name="node">The node to rebuild.</param>
    /// <param name="results">The memo of folded children.</param>
    /// <returns>The rebuilt node, or <paramref name="node"/> itself when nothing changed.</returns>
    private static OwlClassExpression Rebuild(OwlClassExpression node, Dictionary<OwlClassExpression, OwlClassExpression> results)
    {
        switch(node)
        {
            case OwlObjectIntersectionOf intersection:
            {
                IReadOnlyList<OwlClassExpression>? folded = RebuildOperands(intersection.Operands, results);

                return folded is null ? node : new OwlObjectIntersectionOf(folded);
            }

            case OwlObjectUnionOf union:
            {
                IReadOnlyList<OwlClassExpression>? folded = RebuildOperands(union.Operands, results);

                return folded is null ? node : new OwlObjectUnionOf(folded);
            }

            case OwlObjectComplementOf complement:
            {
                OwlClassExpression folded = results[complement.Operand];

                return ReferenceEquals(folded, complement.Operand) ? node : new OwlObjectComplementOf(folded);
            }

            case OwlObjectSomeValuesFrom some:
            {
                OwlClassExpression folded = results[some.Filler];

                return ReferenceEquals(folded, some.Filler) ? node : new OwlObjectSomeValuesFrom(some.Property, folded);
            }

            case OwlObjectAllValuesFrom all:
            {
                OwlClassExpression folded = results[all.Filler];

                return ReferenceEquals(folded, all.Filler) ? node : new OwlObjectAllValuesFrom(all.Property, folded);
            }

            case OwlObjectCardinality card when card.Filler is not null:
            {
                OwlClassExpression folded = results[card.Filler];

                return ReferenceEquals(folded, card.Filler) ? node : card with { Filler = folded };
            }

            default:
                return node;
        }
    }

    /// <summary>
    /// Rebuilds an operand list from the memo, returning <see langword="null"/>
    /// when no operand changed and a rebuilt list — unchanged operands by
    /// reference — otherwise.
    /// </summary>
    /// <param name="operands">The operand list.</param>
    /// <param name="results">The memo of folded children.</param>
    /// <returns>The rebuilt array, or <see langword="null"/> when nothing changed.</returns>
    private static OwlClassExpression[]? RebuildOperands(IReadOnlyList<OwlClassExpression> operands, Dictionary<OwlClassExpression, OwlClassExpression> results)
    {
        OwlClassExpression[]? rebuilt = null;
        for(int index = 0; index < operands.Count; index++)
        {
            OwlClassExpression original = operands[index];
            OwlClassExpression folded = results[original];
            if(!ReferenceEquals(folded, original))
            {
                if(rebuilt is null)
                {
                    rebuilt = new OwlClassExpression[operands.Count];
                    for(int copy = 0; copy < operands.Count; copy++)
                    {
                        rebuilt[copy] = operands[copy];
                    }
                }

                rebuilt[index] = folded;
            }
        }

        return rebuilt;
    }

    /// <summary>
    /// Pushes a node's class-expression children onto the walk stack: the
    /// operands of a boolean connective and the filler of a non-reserved
    /// restriction. A reserved restriction is never passed here — it has
    /// already folded to a constant or been kept verbatim — so its discarded
    /// filler is never walked.
    /// </summary>
    /// <param name="node">The node whose children are pushed.</param>
    /// <param name="work">The walk stack the children are pushed onto.</param>
    private static void PushClassChildren(OwlClassExpression node, Stack<(OwlClassExpression Node, bool ChildrenPushed)> work)
    {
        switch(node)
        {
            case OwlObjectIntersectionOf intersection:
                foreach(OwlClassExpression operand in intersection.Operands)
                {
                    work.Push((operand, false));
                }

                break;
            case OwlObjectUnionOf union:
                foreach(OwlClassExpression operand in union.Operands)
                {
                    work.Push((operand, false));
                }

                break;
            case OwlObjectComplementOf complement:
                work.Push((complement.Operand, false));

                break;
            case OwlObjectSomeValuesFrom some:
                work.Push((some.Filler, false));

                break;
            case OwlObjectAllValuesFrom all:
                work.Push((all.Filler, false));

                break;
            case OwlObjectCardinality card when card.Filler is not null:
                work.Push((card.Filler, false));

                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Classifies a class-expression node against the fold table: a reserved
    /// restriction whose meaning is a constant yields <see cref="NodeFoldKind.Constant"/>
    /// with the constant on <paramref name="constant"/>; a global top
    /// restriction that is not foldable yields <see cref="NodeFoldKind.KeepVerbatim"/>;
    /// every boolean connective, non-reserved restriction, and leaf yields
    /// <see cref="NodeFoldKind.Recurse"/>.
    /// </summary>
    /// <param name="node">The node to classify.</param>
    /// <param name="constant">The constant the node folds to, or <see langword="null"/> when it does not fold to one.</param>
    /// <returns>The node's fold kind.</returns>
    private static NodeFoldKind Classify(OwlClassExpression node, out OwlClassReference? constant)
    {
        constant = null;
        switch(node)
        {
            case OwlObjectSomeValuesFrom some:
            {
                if(IsBottomObjectProperty(some.Property))
                {
                    //Row 1: no bottom-successor exists.
                    constant = NothingClass;

                    return NodeFoldKind.Constant;
                }

                if(IsTopObjectProperty(some.Property))
                {
                    if(IsSyntacticThing(some.Filler))
                    {
                        //Row 10: every element has a top-successor over a non-empty domain.
                        constant = ThingClass;

                        return NodeFoldKind.Constant;
                    }

                    //A top existential with a non-Thing filler is a global non-emptiness assertion.
                    return NodeFoldKind.KeepVerbatim;
                }

                return NodeFoldKind.Recurse;
            }

            case OwlObjectAllValuesFrom all:
            {
                if(IsBottomObjectProperty(all.Property))
                {
                    //Row 2: vacuously true — no bottom-successor to constrain.
                    constant = ThingClass;

                    return NodeFoldKind.Constant;
                }

                if(IsTopObjectProperty(all.Property))
                {
                    if(IsSyntacticThing(all.Filler))
                    {
                        //Row 22: every top-successor is in Thing — a pure tautology.
                        constant = ThingClass;

                        return NodeFoldKind.Constant;
                    }

                    //A top universal with a non-Thing filler is a global inclusion, not pointwise.
                    return NodeFoldKind.KeepVerbatim;
                }

                return NodeFoldKind.Recurse;
            }

            case OwlObjectHasValue hasValue:
            {
                if(IsBottomObjectProperty(hasValue.Property))
                {
                    //Row 3: no bottom-edge to the value.
                    constant = NothingClass;

                    return NodeFoldKind.Constant;
                }

                if(IsTopObjectProperty(hasValue.Property))
                {
                    //Row 11: top relates every element to the value's denotation.
                    constant = ThingClass;

                    return NodeFoldKind.Constant;
                }

                return NodeFoldKind.Recurse;
            }

            case OwlObjectHasSelf hasSelf:
            {
                if(IsBottomObjectProperty(hasSelf.Property))
                {
                    //Row 4: no bottom-loop.
                    constant = NothingClass;

                    return NodeFoldKind.Constant;
                }

                if(IsTopObjectProperty(hasSelf.Property))
                {
                    //Row 12: top holds of every element with itself.
                    constant = ThingClass;

                    return NodeFoldKind.Constant;
                }

                return NodeFoldKind.Recurse;
            }

            case OwlObjectCardinality card:
            {
                if(IsBottomObjectProperty(card.Property))
                {
                    //Rows 5-9: the bottom role has zero successors, so the bound decides the constant.
                    constant = FoldBottomCardinality(card.Kind, card.Cardinality);

                    return NodeFoldKind.Constant;
                }

                if(IsTopObjectProperty(card.Property) && TryFoldTopCardinality(card, out OwlClassReference? topConstant))
                {
                    //Rows 6, 13: a >=0 bound and a >=1 Thing-filler bound over top are tautologies.
                    constant = topConstant;

                    return NodeFoldKind.Constant;
                }

                if(IsTopObjectProperty(card.Property))
                {
                    //Every other top-cardinality shape counts the domain globally.
                    return NodeFoldKind.KeepVerbatim;
                }

                return NodeFoldKind.Recurse;
            }

            case OwlDataSomeValuesFrom dataSome when MentionsBottomDataProperty(dataSome.Properties):
                //Rows 14, 23: no tuple exists — a bottom-data slot has no value.
                constant = NothingClass;

                return NodeFoldKind.Constant;
            case OwlDataAllValuesFrom dataAll when MentionsBottomDataProperty(dataAll.Properties):
                //Rows 16, 24: vacuously true — no tuple to violate the range.
                constant = ThingClass;

                return NodeFoldKind.Constant;
            case OwlDataHasValue dataHas when IsBottomDataProperty(dataHas.Property):
                //Row 15: no bottom-data value equals the literal.
                constant = NothingClass;

                return NodeFoldKind.Constant;
            case OwlDataCardinality dataCard when IsBottomDataProperty(dataCard.Property):
                //Rows 17-21: the bottom-data role has zero values, so the bound decides the constant.
                constant = FoldBottomCardinality(dataCard.Kind, dataCard.Cardinality);

                return NodeFoldKind.Constant;
            default:
                return NodeFoldKind.Recurse;
        }
    }

    /// <summary>
    /// Folds a cardinality restriction over an empty-extension reserved
    /// property, where the counted role has exactly zero successors: a
    /// minimum or exact bound of at least one is unsatisfiable
    /// (<c>owl:Nothing</c>); a zero minimum, a zero exact, and any maximum are
    /// tautologies (<c>owl:Thing</c>).
    /// </summary>
    /// <param name="kind">The cardinality flavour.</param>
    /// <param name="cardinality">The asserted bound.</param>
    /// <returns>The constant the restriction folds to.</returns>
    private static OwlClassReference FoldBottomCardinality(OwlCardinalityKind kind, int cardinality)
    {
        return kind switch
        {
            OwlCardinalityKind.Min => cardinality >= 1 ? NothingClass : ThingClass,
            OwlCardinalityKind.Max => ThingClass,
            OwlCardinalityKind.Exact => cardinality >= 1 ? NothingClass : ThingClass,
            _ => ThingClass,
        };
    }

    /// <summary>
    /// Folds a cardinality restriction over the universal <c>owl:topObjectProperty</c>
    /// where the bound is a tautology: a minimum of zero (<c>&gt;=0</c> holds of
    /// any property) or a minimum of one whose filler is absent or
    /// syntactically <c>owl:Thing</c> (every element has a top-successor over a
    /// non-empty domain). Every other top-cardinality shape counts the domain
    /// globally and is not folded.
    /// </summary>
    /// <param name="card">The cardinality restriction.</param>
    /// <param name="constant">The constant the restriction folds to, or <see langword="null"/> when it does not fold.</param>
    /// <returns><see langword="true"/> when the restriction folds.</returns>
    private static bool TryFoldTopCardinality(OwlObjectCardinality card, out OwlClassReference? constant)
    {
        if(card.Kind == OwlCardinalityKind.Min && card.Cardinality == 0)
        {
            constant = ThingClass;

            return true;
        }

        if(card.Kind == OwlCardinalityKind.Min && card.Cardinality == 1 && IsSyntacticThing(card.Filler))
        {
            constant = ThingClass;

            return true;
        }

        constant = null;

        return false;
    }

    /// <summary>Whether an object-property expression is <c>owl:bottomObjectProperty</c>, an inverse unwrapped to its named property first.</summary>
    /// <param name="property">The property expression.</param>
    /// <returns><see langword="true"/> for the empty-extension bottom object property.</returns>
    private static bool IsBottomObjectProperty(OwlObjectPropertyExpression property)
    {
        return property.Property.Iri.Equals(OwlVocabulary.BottomObjectProperty);
    }

    /// <summary>Whether an object-property expression is <c>owl:topObjectProperty</c>, an inverse unwrapped to its named property first.</summary>
    /// <param name="property">The property expression.</param>
    /// <returns><see langword="true"/> for the universal top object property.</returns>
    private static bool IsTopObjectProperty(OwlObjectPropertyExpression property)
    {
        return property.Property.Iri.Equals(OwlVocabulary.TopObjectProperty);
    }

    /// <summary>Whether a data property is <c>owl:bottomDataProperty</c>. <c>owl:topDataProperty</c> is never matched: its class-expression semantics interact with datatype value spaces and are not folded here.</summary>
    /// <param name="property">The data property.</param>
    /// <returns><see langword="true"/> for the empty-extension bottom data property.</returns>
    private static bool IsBottomDataProperty(NamedNode property)
    {
        return property.Iri.Equals(OwlVocabulary.BottomDataProperty);
    }

    /// <summary>Whether any property slot of an n-ary data restriction is <c>owl:bottomDataProperty</c>; the empty extension of one slot empties the whole tuple relation.</summary>
    /// <param name="properties">The restriction's property slots.</param>
    /// <returns><see langword="true"/> when at least one slot is the bottom data property.</returns>
    private static bool MentionsBottomDataProperty(IReadOnlyList<NamedNode> properties)
    {
        for(int index = 0; index < properties.Count; index++)
        {
            if(IsBottomDataProperty(properties[index]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a filler is syntactically <c>owl:Thing</c>: absent (an unqualified restriction), or a class reference whose IRI is <c>owl:Thing</c>. The test is purely syntactic — no entailment widening.</summary>
    /// <param name="filler">The filler expression, or <see langword="null"/> for an unqualified restriction.</param>
    /// <returns><see langword="true"/> when the filler is absent or a syntactic <c>owl:Thing</c>.</returns>
    private static bool IsSyntacticThing(OwlClassExpression? filler)
    {
        return filler is null || (filler is OwlClassReference reference && reference.Class.Iri.Equals(OwlVocabulary.Thing));
    }
}
