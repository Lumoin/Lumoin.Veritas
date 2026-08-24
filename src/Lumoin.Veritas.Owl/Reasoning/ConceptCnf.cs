using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Sat;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// Encodes ALC concepts in negation normal form into propositional CNF:
/// every structurally distinct subformula owns one dense variable,
/// conjunctions and disjunctions are defined by one-directional clauses
/// (negation normal form leaves each subformula in a single polarity),
/// existential and universal restrictions are opaque atoms whose fillers
/// stay unencoded, and ⊤/⊥ simplify structurally during encoding.
/// Concepts encode on demand; the clause list only grows, and callers may
/// append clauses of their own.
/// </summary>
/// <remarks>
/// <para>
/// Variable <c>0</c> is reserved always-true and pinned by a unit clause
/// at construction. A concept that simplifies wholly to ⊤ or ⊥ encodes to
/// that variable's positive or negative literal, so callers branch on a
/// constant outcome by comparing against <see cref="TrueLiteral"/> and
/// <see cref="FalseLiteral"/>.
/// </para>
/// <para>
/// Simplification is structural only: a ⊤ conjunct drops and a ⊤ disjunct
/// collapses the disjunction to true; a ⊥ conjunct collapses the
/// conjunction to false and a ⊥ disjunct drops; a connective left with
/// one operand encodes as that operand, and one left with none encodes as
/// its neutral constant. Restrictions never simplify — their fillers
/// carry no propositional reading here, so ∃r.⊥ and ∀r.⊤ stay opaque
/// atoms.
/// </para>
/// </remarks>
internal sealed class ConceptCnf
{
    /// <summary>The reserved always-true variable index.</summary>
    private const int ReservedTrueVariable = 0;

    /// <summary>The subformula table: one dense variable per structurally distinct variable-bearing subformula, assigned on first encounter.</summary>
    private readonly Dictionary<AlcConcept, int> variableOf = [];

    /// <summary>The modal-atom registrations, in allocation order.</summary>
    private readonly List<ModalAtom> modalAtoms = [];

    /// <summary>The data-restriction atom registrations, in allocation order — the concrete-domain leaves a model's datatype-consistency check reads.</summary>
    private readonly List<DataAtom> dataAtoms = [];

    /// <summary>The clause list; it only grows.</summary>
    private readonly List<IReadOnlyList<SatLiteral>> clauses = [];

    /// <summary>The number of variables allocated so far.</summary>
    private int variableCount;

    /// <summary>
    /// The reusable per-call memo of <see cref="GetLiteral"/>'s post-order encoding,
    /// cleared at each entry so no state carries between calls; instance scratch
    /// because the encoder is single-threaded and never re-enters itself.
    /// </summary>
    private readonly Dictionary<AlcConcept, SatLiteral> encodeResults = [];

    /// <summary>The reusable post-order traversal stack of <see cref="GetLiteral"/>, cleared at each entry.</summary>
    private readonly Stack<(AlcConcept Node, bool ChildrenDone)> encodeWork = new();

    /// <summary>
    /// The reusable traversal stack of <see cref="AssertFact"/>, cleared at each
    /// entry; its own field because <see cref="AssertFact"/> calls
    /// <see cref="GetLiteral"/> while this stack is live.
    /// </summary>
    private readonly Stack<AlcConcept> assertWork = new();

    /// <summary>Reserves the always-true variable and pins it with its unit clause.</summary>
    public ConceptCnf()
    {
        variableCount = ReservedTrueVariable + 1;
        clauses.Add([TrueLiteral]);
    }

    /// <summary>The literal true in every model — what a concept simplifying wholly to ⊤ encodes to.</summary>
    public SatLiteral TrueLiteral { get; } = new(ReservedTrueVariable, true);

    /// <summary>The literal false in every model — what a concept simplifying wholly to ⊥ encodes to.</summary>
    public SatLiteral FalseLiteral { get; } = new(ReservedTrueVariable, false);

    /// <summary>The clauses encoded and appended so far; pass alongside <see cref="VariableCount"/> to a solver.</summary>
    public IReadOnlyList<IReadOnlyList<SatLiteral>> Clauses => clauses;

    /// <summary>The number of variables allocated so far; every clause literal indexes below it.</summary>
    public int VariableCount => variableCount;

    /// <summary>
    /// The modal atoms registered so far — every existential or universal
    /// restriction that owns a variable, in allocation order, so ascending
    /// by variable. Callers interpreting a model read a restriction's truth
    /// here; the restriction kind is the concept's concrete type.
    /// </summary>
    public IReadOnlyList<ModalAtom> ModalAtoms => modalAtoms;

    /// <summary>
    /// The data-restriction atoms registered so far — every existential,
    /// universal, or minimum-cardinality data restriction that owns a variable,
    /// in allocation order. A caller interpreting a model reads which concrete-
    /// domain demands hold here and hands them to the datatype checker.
    /// </summary>
    public IReadOnlyList<DataAtom> DataAtoms => dataAtoms;

    /// <summary>
    /// The concept's literal, encoding the concept on first encounter: the
    /// definitional clauses its connectives need append to
    /// <see cref="Clauses"/>, and a concept that simplifies wholly to a
    /// constant comes back as <see cref="TrueLiteral"/> or
    /// <see cref="FalseLiteral"/>.
    /// </summary>
    /// <param name="concept">The concept in negation normal form.</param>
    /// <returns>The literal that stands for the concept.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="concept"/> is <see langword="null"/>.</exception>
    public SatLiteral GetLiteral(AlcConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);

        //Explicit post-order traversal: operands encode before the
        //connective that combines them; restrictions push no children. The
        //memo and the stack are cleared instance scratch, so one call's
        //containers serve the whole decision.
        Dictionary<AlcConcept, SatLiteral> results = encodeResults;
        Stack<(AlcConcept Node, bool ChildrenDone)> work = encodeWork;
        results.Clear();
        work.Clear();
        work.Push((concept, false));

        while(work.Count > 0)
        {
            (AlcConcept node, bool childrenDone) = work.Pop();
            if(results.ContainsKey(node))
            {
                continue;
            }

            if(!childrenDone)
            {
                if(variableOf.TryGetValue(node, out int known))
                {
                    results[node] = new SatLiteral(known, true);

                    continue;
                }

                work.Push((node, true));
                switch(node)
                {
                    case AlcAnd and:
                    {
                        for(int operandIndex = 0; operandIndex < and.Operands.Count; operandIndex++)
                        {
                            work.Push((and.Operands[operandIndex], false));
                        }

                        break;
                    }
                    case AlcOr or:
                    {
                        for(int operandIndex = 0; operandIndex < or.Operands.Count; operandIndex++)
                        {
                            work.Push((or.Operands[operandIndex], false));
                        }

                        break;
                    }
                    default:
                    {
                        break;
                    }
                }

                continue;
            }

            results[node] = node switch
            {
                AlcTop => TrueLiteral,
                AlcBottom => FalseLiteral,
                AlcAtom atom => new SatLiteral(VariableFor(atom), true),
                AlcNot negation => new SatLiteral(VariableFor(negation.Operand), false),
                AlcExists or AlcForAll => new SatLiteral(VariableFor(node), true),
                AlcDataSome or AlcDataAll or AlcDataMinCard or AlcDataMaxCard => new SatLiteral(VariableFor(node), true),
                AlcAnd and => CombineConjunction(and, results),
                AlcOr or => CombineDisjunction(or, results),
                _ => throw new ArgumentException("The concept is not a negation-normal-form ALC concept.", nameof(concept))
            };
        }

        return results[concept];
    }

    /// <summary>
    /// Asserts a concept as a top-level fact: a conjunction recurses into
    /// its conjuncts, a disjunction emits one clause of its disjunct
    /// literals without an auxiliary variable, anything else emits a unit
    /// clause of its literal; ⊤ emits nothing and ⊥ emits the empty
    /// clause.
    /// </summary>
    /// <param name="concept">The concept in negation normal form.</param>
    /// <exception cref="ArgumentNullException"><paramref name="concept"/> is <see langword="null"/>.</exception>
    public void AssertFact(AlcConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);

        Stack<AlcConcept> work = assertWork;
        work.Clear();
        work.Push(concept);
        while(work.Count > 0)
        {
            AlcConcept node = work.Pop();
            switch(node)
            {
                case AlcTop:
                {
                    break;
                }
                case AlcBottom:
                {
                    clauses.Add([]);

                    break;
                }
                case AlcAnd and:
                {
                    for(int i = and.Operands.Count - 1; i >= 0; i--)
                    {
                        work.Push(and.Operands[i]);
                    }

                    break;
                }
                case AlcOr or:
                {
                    AssertDisjunction(or);

                    break;
                }
                default:
                {
                    clauses.Add([GetLiteral(node)]);

                    break;
                }
            }
        }
    }

    /// <summary>Appends a caller-built clause; every literal must index an already-allocated variable.</summary>
    /// <param name="clause">The clause to append.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clause"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A literal indexes a variable outside the allocated range.</exception>
    public void Append(IReadOnlyList<SatLiteral> clause)
    {
        ArgumentNullException.ThrowIfNull(clause);

        for(int literalIndex = 0; literalIndex < clause.Count; literalIndex++)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(clause[literalIndex].Variable, nameof(clause));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(clause[literalIndex].Variable, variableCount, nameof(clause));
        }

        clauses.Add(clause);
    }

    /// <summary>Maps a variable to its replica in a numbered block of a caller-frozen width.</summary>
    /// <param name="blockIndex">The zero-based block index.</param>
    /// <param name="variable">The variable index inside a block; must lie below <paramref name="blockWidth"/>.</param>
    /// <param name="blockWidth">The frozen per-block variable count.</param>
    /// <returns>The replica's variable index, <c>blockIndex * blockWidth + variable</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An index is negative, the width is non-positive, or the variable does not fit the width.</exception>
    public static int BlockVariable(int blockIndex, int variable, int blockWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(variable);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockWidth);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(variable, blockWidth);

        return (blockIndex * blockWidth) + variable;
    }

    /// <summary>Instantiates a template clause into a numbered block: every literal's variable maps through <see cref="BlockVariable"/>, polarities unchanged.</summary>
    /// <param name="template">The template clause over within-block variables.</param>
    /// <param name="blockIndex">The zero-based block index.</param>
    /// <param name="blockWidth">The frozen per-block variable count.</param>
    /// <returns>The instantiated clause.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An index is negative, the width is non-positive, or a template variable does not fit the width.</exception>
    public static IReadOnlyList<SatLiteral> InstantiateInBlock(IReadOnlyList<SatLiteral> template, int blockIndex, int blockWidth)
    {
        ArgumentNullException.ThrowIfNull(template);

        SatLiteral[] instantiated = new SatLiteral[template.Count];
        for(int i = 0; i < template.Count; i++)
        {
            instantiated[i] = new SatLiteral(BlockVariable(blockIndex, template[i].Variable, blockWidth), template[i].IsPositive);
        }

        return instantiated;
    }

    /// <summary>Emits a top-level disjunction as one clause of its disjunct literals: a ⊤ disjunct makes the clause vacuous, ⊥ disjuncts drop, and a disjunction left empty emits the empty clause.</summary>
    /// <param name="disjunction">The top-level disjunction.</param>
    private void AssertDisjunction(AlcOr disjunction)
    {
        List<SatLiteral> clause = [];
        for(int operandIndex = 0; operandIndex < disjunction.Operands.Count; operandIndex++)
        {
            SatLiteral literal = GetLiteral(disjunction.Operands[operandIndex]);
            if(literal == TrueLiteral)
            {
                return;
            }

            if(literal != FalseLiteral)
            {
                clause.Add(literal);
            }
        }

        clauses.Add(clause);
    }

    /// <summary>Folds a conjunction over its operand literals: a false operand collapses it, true operands drop, and the survivors define a fresh variable through one implication clause each.</summary>
    /// <param name="conjunction">The conjunction.</param>
    /// <param name="results">The memo of encoded operands.</param>
    /// <returns>The conjunction's literal.</returns>
    private SatLiteral CombineConjunction(AlcAnd conjunction, Dictionary<AlcConcept, SatLiteral> results)
    {
        List<SatLiteral> survivors = [];
        foreach(AlcConcept operand in conjunction.Operands)
        {
            SatLiteral literal = results[operand];
            if(literal == FalseLiteral)
            {
                return FalseLiteral;
            }

            if(literal != TrueLiteral)
            {
                survivors.Add(literal);
            }
        }

        if(survivors.Count == 0)
        {
            return TrueLiteral;
        }

        if(survivors.Count == 1)
        {
            return survivors[0];
        }

        SatLiteral defined = new(VariableFor(conjunction), true);
        foreach(SatLiteral survivor in survivors)
        {
            clauses.Add([defined.Negated(), survivor]);
        }

        return defined;
    }

    /// <summary>Folds a disjunction over its operand literals: a true operand collapses it, false operands drop, and the survivors define a fresh variable through one clause over them all.</summary>
    /// <param name="disjunction">The disjunction.</param>
    /// <param name="results">The memo of encoded operands.</param>
    /// <returns>The disjunction's literal.</returns>
    private SatLiteral CombineDisjunction(AlcOr disjunction, Dictionary<AlcConcept, SatLiteral> results)
    {
        List<SatLiteral> survivors = [];
        foreach(AlcConcept operand in disjunction.Operands)
        {
            SatLiteral literal = results[operand];
            if(literal == TrueLiteral)
            {
                return TrueLiteral;
            }

            if(literal != FalseLiteral)
            {
                survivors.Add(literal);
            }
        }

        if(survivors.Count == 0)
        {
            return FalseLiteral;
        }

        if(survivors.Count == 1)
        {
            return survivors[0];
        }

        SatLiteral defined = new(VariableFor(disjunction), true);
        List<SatLiteral> clause = new(survivors.Count + 1) { defined.Negated() };
        clause.AddRange(survivors);
        clauses.Add(clause);

        return defined;
    }

    /// <summary>The subformula's variable from the table, allocated densely on first encounter.</summary>
    /// <param name="concept">The variable-bearing subformula.</param>
    /// <returns>The variable index.</returns>
    private int VariableFor(AlcConcept concept)
    {
        if(!variableOf.TryGetValue(concept, out int variable))
        {
            variable = variableCount;
            variableCount++;
            variableOf[concept] = variable;
            if(concept is AlcExists or AlcForAll)
            {
                modalAtoms.Add(new ModalAtom(concept, variable));
            }
            else if(concept is AlcDataSome or AlcDataAll or AlcDataMinCard or AlcDataMaxCard)
            {
                dataAtoms.Add(new DataAtom(concept, variable));
            }
        }

        return variable;
    }

    /// <summary>One modal-atom registration: an existential or universal restriction and the variable that stands for it.</summary>
    /// <param name="Concept">The restriction concept; an <see cref="AlcExists"/> or an <see cref="AlcForAll"/>.</param>
    /// <param name="Variable">The variable the restriction owns.</param>
    internal readonly record struct ModalAtom(AlcConcept Concept, int Variable);

    /// <summary>One data-restriction atom registration: a concrete-domain leaf and the variable that stands for it.</summary>
    /// <param name="Concept">The restriction concept; an <see cref="AlcDataSome"/>, <see cref="AlcDataAll"/>, <see cref="AlcDataMinCard"/>, or <see cref="AlcDataMaxCard"/>.</param>
    /// <param name="Variable">The variable the restriction owns.</param>
    internal readonly record struct DataAtom(AlcConcept Concept, int Variable);
}
