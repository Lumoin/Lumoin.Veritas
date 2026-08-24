using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// The kind of a context term in the DL-clause grammar of the consequence-based
/// context calculus (KR 2016,
/// Section 2, <see href="https://arxiv.org/abs/1602.04498"/>; nominal-extended
/// per the ALCHOIQ calculus, <see href="https://arxiv.org/abs/1805.01396"/>
/// Definition 1): the central variable <c>x</c>, the context variable <c>y</c>
/// (used only by the term order), a neighbour variable <c>z_i</c>, a function
/// term <c>f(x)</c>, a named individual <c>o</c>, or the depth-one root term
/// <c>f(o)</c> over a named individual — the last two carry the nominal
/// vocabulary and appear only in the clauses of nominal-jurisdiction modules.
/// </summary>
internal enum DlTermKind
{
    /// <summary>The central variable <c>x</c>.</summary>
    Central = 0,

    /// <summary>The context variable <c>y</c> — a constant the term order needs; never part of a clause.</summary>
    Context = 1,

    /// <summary>A neighbour variable <c>z_i</c>, indexed by <see cref="DlTerm.Index"/>.</summary>
    Neighbour = 2,

    /// <summary>A function term <c>f(x)</c> over the Skolem function symbol <see cref="DlTerm.Index"/>.</summary>
    Function = 3,

    /// <summary>A named individual <c>o</c> — an input individual interned at clausification or a generated nominal minted through the bounded in-saturation channel; the payload is the interned individual id.</summary>
    Individual = 4,

    /// <summary>The depth-one root term <c>f(o)</c> over a Skolem function symbol and a named individual (the root-context literal universe); the payload packs the function symbol above the individual id (<see cref="DlTerm.FunctionSymbol"/> / <see cref="DlTerm.IndividualId"/>).</summary>
    FunctionOfIndividual = 5,
}

/// <summary>
/// A context a-term of the DL-clause grammar (KR 2016 Section 2; nominal terms
/// per arXiv:1805.01396 Definition 1), packed into a single 32-bit value: a
/// three-bit <see cref="DlTermKind"/> tag in the top bits and a payload in the
/// low bits (the neighbour index for <see cref="DlTermKind.Neighbour"/>, the
/// Skolem function symbol id for <see cref="DlTermKind.Function"/>, the interned
/// individual id for <see cref="DlTermKind.Individual"/>, the function symbol
/// packed above the individual id for <see cref="DlTermKind.FunctionOfIndividual"/>,
/// zero for <c>x</c> and <c>y</c>). The packing round-trips: <see cref="Kind"/>
/// and the payload accessors recover the structure the factories built.
/// </summary>
/// <param name="Packed">The packed 32-bit encoding.</param>
internal readonly record struct DlTerm(uint Packed): IComparable<DlTerm>
{
    /// <summary>The bit width of the payload below the kind tag.</summary>
    private const int PayloadBits = 29;

    /// <summary>The mask selecting the payload below the kind tag.</summary>
    private const uint PayloadMask = (1u << PayloadBits) - 1u;

    /// <summary>The bit width of the function-symbol field of a <see cref="DlTermKind.FunctionOfIndividual"/> payload — the high field above the individual id.</summary>
    public const int FunctionSymbolBits = 14;

    /// <summary>The bit width of the individual-id field of a <see cref="DlTermKind.FunctionOfIndividual"/> payload — the low field below the function symbol.</summary>
    public const int IndividualBits = PayloadBits - FunctionSymbolBits;

    /// <summary>The mask selecting the individual-id field of a <see cref="DlTermKind.FunctionOfIndividual"/> payload.</summary>
    private const uint IndividualMask = (1u << IndividualBits) - 1u;

    /// <summary>The exclusive ceiling on function symbol ids representable in a <see cref="DlTermKind.FunctionOfIndividual"/> term; a module whose frozen signature exceeds it delegates named (<c>PackedTermWidthExceeded</c>) at clausification.</summary>
    public const int FunctionSymbolLimit = 1 << FunctionSymbolBits;

    /// <summary>The exclusive ceiling on individual ids representable in a <see cref="DlTermKind.FunctionOfIndividual"/> term; a module whose individual population exceeds it delegates named (<c>PackedTermWidthExceeded</c>) at clausification.</summary>
    public const int IndividualLimit = 1 << IndividualBits;

    /// <summary>The central variable <c>x</c>.</summary>
    public static DlTerm Central { get; } = new(Pack(DlTermKind.Central, 0));

    /// <summary>The context variable <c>y</c> — a constant the term order needs; never part of a clause body or head.</summary>
    public static DlTerm Context { get; } = new(Pack(DlTermKind.Context, 0));

    /// <summary>Builds a neighbour variable <c>z_i</c>.</summary>
    /// <param name="index">The neighbour index <c>i</c>.</param>
    /// <returns>The neighbour term.</returns>
    public static DlTerm Neighbour(int index)
    {
        return new DlTerm(Pack(DlTermKind.Neighbour, index));
    }

    /// <summary>Builds a function term <c>f(x)</c>.</summary>
    /// <param name="functionSymbol">The Skolem function symbol id.</param>
    /// <returns>The function term.</returns>
    public static DlTerm Function(int functionSymbol)
    {
        return new DlTerm(Pack(DlTermKind.Function, functionSymbol));
    }

    /// <summary>Builds a named-individual term <c>o</c>.</summary>
    /// <param name="individualId">The interned individual id.</param>
    /// <returns>The individual term.</returns>
    public static DlTerm Individual(int individualId)
    {
        return new DlTerm(Pack(DlTermKind.Individual, individualId));
    }

    /// <summary>Builds the root term <c>f(o)</c> over a Skolem function symbol and a named individual; the caller has bound-checked both fields against <see cref="FitsFunctionOfIndividual"/> at clausification, so the width guards here assert an invariant rather than steer control flow.</summary>
    /// <param name="functionSymbol">The Skolem function symbol id, below <see cref="FunctionSymbolLimit"/>.</param>
    /// <param name="individualId">The interned individual id, below <see cref="IndividualLimit"/>.</param>
    /// <returns>The function-of-individual term.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A field is negative or exceeds its packed width.</exception>
    public static DlTerm FunctionOf(int functionSymbol, int individualId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(functionSymbol);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(functionSymbol, FunctionSymbolLimit);
        ArgumentOutOfRangeException.ThrowIfNegative(individualId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(individualId, IndividualLimit);

        return new DlTerm(Pack(DlTermKind.FunctionOfIndividual, (functionSymbol << IndividualBits) | individualId));
    }

    /// <summary>Whether a function symbol id and an individual id both fit the <see cref="DlTermKind.FunctionOfIndividual"/> payload split — the clausification bound check whose failure delegates the module named (<c>PackedTermWidthExceeded</c>) instead of ever reaching the factory guard.</summary>
    /// <param name="functionSymbolCount">The module's Skolem function symbol count.</param>
    /// <param name="individualCount">The module's interned individual count, generated nominals included.</param>
    /// <returns><see langword="true"/> when every combination of the module's symbols packs.</returns>
    public static bool FitsFunctionOfIndividual(int functionSymbolCount, int individualCount)
    {
        return functionSymbolCount <= FunctionSymbolLimit && individualCount <= IndividualLimit;
    }

    /// <summary>The term's kind.</summary>
    public DlTermKind Kind
    {
        get
        {
            return (DlTermKind)(Packed >> PayloadBits);
        }
    }

    /// <summary>The raw payload below the kind tag: the neighbour index (for <see cref="DlTermKind.Neighbour"/>), the Skolem function symbol id (for <see cref="DlTermKind.Function"/>), the interned individual id (for <see cref="DlTermKind.Individual"/>), zero for <c>x</c> and <c>y</c>. A <see cref="DlTermKind.FunctionOfIndividual"/> payload holds TWO fields, which this whole-payload accessor cannot decode — read <see cref="FunctionSymbol"/> and <see cref="IndividualId"/> instead.</summary>
    public int Index
    {
        get
        {
            return (int)(Packed & PayloadMask);
        }
    }

    /// <summary>The Skolem function symbol of a function-bearing term: the whole payload for <see cref="DlTermKind.Function"/>, the high field for <see cref="DlTermKind.FunctionOfIndividual"/>.</summary>
    public int FunctionSymbol
    {
        get
        {
            return Kind == DlTermKind.FunctionOfIndividual ? (int)((Packed & PayloadMask) >> IndividualBits) : Index;
        }
    }

    /// <summary>The interned individual id of an individual-bearing term: the whole payload for <see cref="DlTermKind.Individual"/>, the low field for <see cref="DlTermKind.FunctionOfIndividual"/>.</summary>
    public int IndividualId
    {
        get
        {
            return Kind == DlTermKind.FunctionOfIndividual ? (int)(Packed & IndividualMask) : Index;
        }
    }

    /// <summary>Whether this term is the central variable <c>x</c>.</summary>
    public bool IsCentral
    {
        get
        {
            return Kind == DlTermKind.Central;
        }
    }

    /// <summary>Whether this term is a function term <c>f(x)</c>.</summary>
    public bool IsFunction
    {
        get
        {
            return Kind == DlTermKind.Function;
        }
    }

    /// <summary>Whether this term is a named-individual term <c>o</c>.</summary>
    public bool IsIndividual
    {
        get
        {
            return Kind == DlTermKind.Individual;
        }
    }

    /// <summary>Whether this term is the root term <c>f(o)</c>.</summary>
    public bool IsFunctionOfIndividual
    {
        get
        {
            return Kind == DlTermKind.FunctionOfIndividual;
        }
    }

    /// <summary>Whether this term is ground — a named individual or a function of one; the nominal vocabulary of the widened grammar.</summary>
    public bool IsGround
    {
        get
        {
            return Kind is DlTermKind.Individual or DlTermKind.FunctionOfIndividual;
        }
    }

    /// <summary>Whether this term is a variable — the central, context, or a neighbour variable; a variable slot is never an equality-rewrite position.</summary>
    public bool IsVariable
    {
        get
        {
            return Kind is DlTermKind.Central or DlTermKind.Context or DlTermKind.Neighbour;
        }
    }

    /// <summary>Orders terms by their packed encoding — a total, well-founded order stable across a run, used to canonicalise clause literal sets.</summary>
    /// <param name="other">The term to compare against.</param>
    /// <returns>A signed comparison of the packed encodings.</returns>
    public int CompareTo(DlTerm other)
    {
        return Packed.CompareTo(other.Packed);
    }

    /// <summary>Packs a kind and a payload into the 32-bit encoding.</summary>
    /// <param name="kind">The term kind.</param>
    /// <param name="payload">The payload (a non-negative neighbour index or function symbol id).</param>
    /// <returns>The packed encoding.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="payload"/> is negative or exceeds the payload width.</exception>
    private static uint Pack(DlTermKind kind, int payload)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(payload);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)payload, PayloadMask);

        return ((uint)kind << PayloadBits) | (uint)payload;
    }
}

/// <summary>The kind of a DL-clause literal (KR 2016 Section 2): a concept atom, a role atom, or an equality/inequality between F-terms.</summary>
internal enum DlLiteralKind
{
    /// <summary>A concept atom <c>B(t)</c>.</summary>
    Concept = 0,

    /// <summary>A role atom <c>S(t1, t2)</c> over a directioned role symbol.</summary>
    Role = 1,

    /// <summary>An equality literal <c>t1 approx t2</c>.</summary>
    Equality = 2,

    /// <summary>An inequality literal <c>t1 not-approx t2</c>.</summary>
    Inequality = 3,
}

/// <summary>
/// A DL-clause literal (KR 2016 Section 2): a concept atom <c>B(t)</c>, a role
/// atom <c>S(t1, t2)</c> over a directioned role symbol, or an equality /
/// inequality between two F-terms. The literal is a value: two literals are
/// equal exactly when their kind, symbol, and terms match, so a clause's body
/// and head become canonical sets once sorted and de-duplicated.
/// </summary>
/// <param name="Kind">The literal kind.</param>
/// <param name="Symbol">The concept-atom id (<see cref="DlLiteralKind.Concept"/>) or the directioned role id (<see cref="DlLiteralKind.Role"/>); unused (<c>-1</c>) for an equality or inequality.</param>
/// <param name="First">The sole argument of a concept atom, or the first argument of a role atom / (in)equality.</param>
/// <param name="Second">The second argument of a role atom or (in)equality; unused (<c>default</c>) for a concept atom.</param>
internal readonly record struct DlLiteral(DlLiteralKind Kind, int Symbol, DlTerm First, DlTerm Second): IComparable<DlLiteral>
{
    /// <summary>The sentinel symbol for an equality or inequality literal, which carries no concept or role symbol.</summary>
    public const int NoSymbol = -1;

    /// <summary>Builds a concept atom <c>B(t)</c>.</summary>
    /// <param name="atom">The concept-atom id.</param>
    /// <param name="argument">The argument term.</param>
    /// <returns>The concept-atom literal.</returns>
    public static DlLiteral Concept(int atom, DlTerm argument)
    {
        return new DlLiteral(DlLiteralKind.Concept, atom, argument, default);
    }

    /// <summary>Builds a role atom <c>S(first, second)</c> over a directioned role symbol.</summary>
    /// <param name="role">The directioned role id.</param>
    /// <param name="first">The first (source) argument.</param>
    /// <param name="second">The second (target) argument.</param>
    /// <returns>The role-atom literal.</returns>
    public static DlLiteral Role(int role, DlTerm first, DlTerm second)
    {
        return new DlLiteral(DlLiteralKind.Role, role, first, second);
    }

    /// <summary>Builds an equality literal <c>first approx second</c>.</summary>
    /// <param name="first">The first term.</param>
    /// <param name="second">The second term.</param>
    /// <returns>The equality literal.</returns>
    public static DlLiteral Equality(DlTerm first, DlTerm second)
    {
        return new DlLiteral(DlLiteralKind.Equality, NoSymbol, first, second);
    }

    /// <summary>Builds an inequality literal <c>first not-approx second</c>.</summary>
    /// <param name="first">The first term.</param>
    /// <param name="second">The second term.</param>
    /// <returns>The inequality literal.</returns>
    public static DlLiteral Inequality(DlTerm first, DlTerm second)
    {
        return new DlLiteral(DlLiteralKind.Inequality, NoSymbol, first, second);
    }

    /// <summary>Whether this literal is a concept or role atom (as opposed to an equality or inequality).</summary>
    public bool IsAtom
    {
        get
        {
            return Kind is DlLiteralKind.Concept or DlLiteralKind.Role;
        }
    }

    /// <summary>Orders literals by kind, then symbol, then the two packed terms — a total order that canonicalises a literal set.</summary>
    /// <param name="other">The literal to compare against.</param>
    /// <returns>A signed comparison.</returns>
    public int CompareTo(DlLiteral other)
    {
        int byKind = ((int)Kind).CompareTo((int)other.Kind);
        if(byKind != 0)
        {
            return byKind;
        }

        int bySymbol = Symbol.CompareTo(other.Symbol);
        if(bySymbol != 0)
        {
            return bySymbol;
        }

        int byFirst = First.CompareTo(other.First);
        if(byFirst != 0)
        {
            return byFirst;
        }

        return Second.CompareTo(other.Second);
    }
}

/// <summary>
/// A DL-clause <c>Body -&gt; Head</c> (KR 2016 Section 2): the body is a set of
/// atoms restricted to <c>B(x)</c>, <c>S(x, z_i)</c>, <c>S(z_i, x)</c>; the head
/// is a set of literals. An empty body reads as the tautology antecedent
/// (<c>top</c>); an empty head reads as the contradiction consequent
/// (<c>bottom</c>). The body and head sit in one contiguous immutable array,
/// split by <see cref="BodyLength"/>, so saturation can index heads by their
/// maximal literal without a second allocation.
/// </summary>
internal sealed class DlClause: IEquatable<DlClause>
{
    /// <summary>The body atoms followed by the head literals, in one array; both spans are canonical (sorted, de-duplicated).</summary>
    private ImmutableArray<DlLiteral> Literals { get; }

    /// <summary>The number of body atoms; the head literals follow from this offset to the end.</summary>
    public int BodyLength { get; }

    /// <summary>The index of the source axiom in the module this clause was clausified from — provenance for reporting, not part of logical identity.</summary>
    public int Origin { get; }

    /// <summary>Initialises a clause from its canonicalised literal array and body split.</summary>
    /// <param name="literals">The body atoms followed by the head literals.</param>
    /// <param name="bodyLength">The number of leading body atoms.</param>
    /// <param name="origin">The source-axiom index.</param>
    private DlClause(ImmutableArray<DlLiteral> literals, int bodyLength, int origin)
    {
        Literals = literals;
        BodyLength = bodyLength;
        Origin = origin;
    }

    /// <summary>The body atoms as a canonical (sorted, de-duplicated) span.</summary>
    public ReadOnlySpan<DlLiteral> Body
    {
        get
        {
            return Literals.AsSpan(0, BodyLength);
        }
    }

    /// <summary>The head literals as a canonical (sorted, de-duplicated) span.</summary>
    public ReadOnlySpan<DlLiteral> Head
    {
        get
        {
            return Literals.AsSpan(BodyLength, Literals.Length - BodyLength);
        }
    }

    /// <summary>Builds a clause, sorting and de-duplicating each of the body and head into canonical sets.</summary>
    /// <param name="body">The body atoms (order and duplicates are normalised away).</param>
    /// <param name="head">The head literals (order and duplicates are normalised away).</param>
    /// <param name="origin">The source-axiom index.</param>
    /// <returns>The canonical clause.</returns>
    public static DlClause Create(IReadOnlyList<DlLiteral> body, IReadOnlyList<DlLiteral> head, int origin)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(head);

        List<DlLiteral> canonicalBody = Canonicalise(body);
        List<DlLiteral> canonicalHead = Canonicalise(head);

        ImmutableArray<DlLiteral>.Builder builder = ImmutableArray.CreateBuilder<DlLiteral>(canonicalBody.Count + canonicalHead.Count);
        builder.AddRange(canonicalBody);
        builder.AddRange(canonicalHead);

        return new DlClause(builder.MoveToImmutable(), canonicalBody.Count, origin);
    }

    /// <summary>Sorts a literal list and drops adjacent duplicates, yielding the set as a canonical sequence; the canonical form is <see cref="CanonicaliseInPlace"/>'s, so the copying and in-place faces cannot diverge.</summary>
    /// <param name="literals">The literals to canonicalise.</param>
    /// <returns>The sorted, de-duplicated list.</returns>
    private static List<DlLiteral> Canonicalise(IReadOnlyList<DlLiteral> literals)
    {
        List<DlLiteral> copy = new(literals);
        CanonicaliseInPlace(copy);

        return copy;
    }

    /// <summary>
    /// Sorts a literal list IN PLACE and drops adjacent duplicates, leaving the
    /// list holding the literal set as a canonical ascending sequence — the ONE
    /// definition of the canonical form. <see cref="Create"/> reaches it through
    /// <see cref="Canonicalise"/> and a caller assembling literals into a reusable
    /// scratch buffer reaches it directly, so a clause built from spans and a
    /// clause built from lists carry structurally the same canonical form rather
    /// than two agreeing implementations.
    /// </summary>
    /// <param name="literals">The literal buffer canonicalised in place.</param>
    internal static void CanonicaliseInPlace(List<DlLiteral> literals)
    {
        ArgumentNullException.ThrowIfNull(literals);

        literals.Sort();

        int distinct = 0;
        for(int i = 0; i < literals.Count; i++)
        {
            if(distinct == 0 || !literals[distinct - 1].Equals(literals[i]))
            {
                literals[distinct] = literals[i];
                distinct++;
            }
        }

        literals.RemoveRange(distinct, literals.Count - distinct);
    }

    /// <summary>
    /// Builds a clause from spans that are ALREADY canonical — sorted ascending
    /// and de-duplicated, as <see cref="CanonicaliseInPlace"/> leaves a buffer and
    /// as <see cref="Body"/> and <see cref="Head"/> read. The spans are copied into
    /// the clause's own storage, so the caller's buffer may be reused immediately.
    /// The callers span the whole span-face gate pipeline of the single mutation
    /// point — the survivor materialisation, the two O(1) absorption-join builds,
    /// the one-time out-of-grammar sample render, and the conditionality lint's
    /// pre-check build — together with the r-Succ seed arms, whose single-literal
    /// spans are canonical by construction, and the test exercisers; a caller
    /// holding un-canonicalised literals uses <see cref="Create"/> instead.
    /// </summary>
    /// <param name="body">The canonical body atoms.</param>
    /// <param name="head">The canonical head literals.</param>
    /// <param name="origin">The source-axiom index.</param>
    /// <returns>The clause over the given canonical spans.</returns>
    internal static DlClause FromCanonicalSpans(ReadOnlySpan<DlLiteral> body, ReadOnlySpan<DlLiteral> head, int origin)
    {
        ImmutableArray<DlLiteral>.Builder builder = ImmutableArray.CreateBuilder<DlLiteral>(body.Length + head.Length);
        for(int i = 0; i < body.Length; i++)
        {
            builder.Add(body[i]);
        }

        for(int i = 0; i < head.Length; i++)
        {
            builder.Add(head[i]);
        }

        return new DlClause(builder.MoveToImmutable(), body.Length, origin);
    }

    /// <summary>Whether two clauses have the same body and head sets; <see cref="Origin"/> is provenance and does not affect logical identity.</summary>
    /// <param name="other">The clause to compare against.</param>
    /// <returns><see langword="true"/> when the body split and every literal match.</returns>
    public bool Equals(DlClause? other)
    {
        if(other is null || other.BodyLength != BodyLength || other.Literals.Length != Literals.Length)
        {
            return false;
        }

        for(int i = 0; i < Literals.Length; i++)
        {
            if(!Literals[i].Equals(other.Literals[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether an object is an equal clause.</summary>
    /// <param name="obj">The object to compare against.</param>
    /// <returns><see langword="true"/> when it is an equal <see cref="DlClause"/>.</returns>
    public override bool Equals(object? obj)
    {
        return Equals(obj as DlClause);
    }

    /// <summary>Whether two clauses are equal by VALUE — the same body split and the same canonical literal sequence, with <see cref="Origin"/> excluded as provenance, exactly the <see cref="Equals(DlClause)"/> contract. Two null references are equal; one null reference is equal to nothing.</summary>
    /// <param name="left">The left clause.</param>
    /// <param name="right">The right clause.</param>
    /// <returns><see langword="true"/> when both are null or both carry the same body split and literals.</returns>
    public static bool operator ==(DlClause? left, DlClause? right)
    {
        return left?.Equals(right) ?? right is null;
    }

    /// <summary>The negation of <see cref="op_Equality(DlClause, DlClause)"/>: whether two clauses differ by value.</summary>
    /// <param name="left">The left clause.</param>
    /// <param name="right">The right clause.</param>
    /// <returns><see langword="true"/> when the two are not equal by value.</returns>
    public static bool operator !=(DlClause? left, DlClause? right)
    {
        return !(left == right);
    }

    /// <summary>A hash over the body split and the literal sequence, consistent with <see cref="Equals(DlClause)"/>.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(BodyLength);
        foreach(DlLiteral literal in Literals)
        {
            hash.Add(literal);
        }

        return hash.ToHashCode();
    }

    /// <summary>Renders the clause against a symbol table for the clausifier battery's round-trip check.</summary>
    /// <param name="symbols">The symbol table naming the clause's atoms and roles.</param>
    /// <returns>The rendered clause, of the form <c>B(x), S(x,z1) -&gt; C(x)</c>.</returns>
    public string Render(ContextSymbolTable symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        StringBuilder builder = new();
        RenderLiterals(builder, Body, symbols);
        builder.Append(" -> ");
        RenderLiterals(builder, Head, symbols);

        return builder.ToString();
    }

    /// <summary>Appends a comma-separated rendering of a literal span.</summary>
    /// <param name="builder">The buffer to append to.</param>
    /// <param name="literals">The literals to render.</param>
    /// <param name="symbols">The symbol table naming atoms and roles.</param>
    private static void RenderLiterals(StringBuilder builder, ReadOnlySpan<DlLiteral> literals, ContextSymbolTable symbols)
    {
        for(int i = 0; i < literals.Length; i++)
        {
            if(i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(RenderLiteral(literals[i], symbols));
        }
    }

    /// <summary>Renders one literal against a symbol table.</summary>
    /// <param name="literal">The literal to render.</param>
    /// <param name="symbols">The symbol table naming atoms and roles.</param>
    /// <returns>The rendered literal.</returns>
    private static string RenderLiteral(DlLiteral literal, ContextSymbolTable symbols)
    {
        return literal.Kind switch
        {
            DlLiteralKind.Concept => $"{symbols.RenderAtom(literal.Symbol)}({RenderTerm(literal.First)})",
            DlLiteralKind.Role => $"{symbols.RenderRole(literal.Symbol)}({RenderTerm(literal.First)},{RenderTerm(literal.Second)})",
            DlLiteralKind.Equality => $"{RenderTerm(literal.First)} = {RenderTerm(literal.Second)}",
            DlLiteralKind.Inequality => $"{RenderTerm(literal.First)} != {RenderTerm(literal.Second)}",
            _ => "?",
        };
    }

    /// <summary>Renders one a-term.</summary>
    /// <param name="term">The term to render.</param>
    /// <returns>The rendered term (<c>x</c>, <c>y</c>, <c>z{i}</c>, <c>f{k}(x)</c>, <c>o{i}</c>, or <c>f{k}(o{i})</c>).</returns>
    private static string RenderTerm(DlTerm term)
    {
        return term.Kind switch
        {
            DlTermKind.Central => "x",
            DlTermKind.Context => "y",
            DlTermKind.Neighbour => $"z{term.Index}",
            DlTermKind.Function => $"f{term.Index}(x)",
            DlTermKind.Individual => $"o{term.IndividualId}",
            DlTermKind.FunctionOfIndividual => $"f{term.FunctionSymbol}(o{term.IndividualId})",
            _ => "?",
        };
    }
}
