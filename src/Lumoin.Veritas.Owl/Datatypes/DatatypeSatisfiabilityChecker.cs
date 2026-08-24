using System;
using System.Collections.Generic;
using System.Numerics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes.Automata;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// The satisfiability verdict of a data range or a conjunction of them over
/// the OWL 2 restricted concrete domain.
/// </summary>
public enum DatatypeSatisfiability
{
    /// <summary>The value space could not be decided within the modelled subset — the sound abstention. <c>default</c> resolves here, so a missing verdict never fires or suppresses a clash.</summary>
    Unknown = 0,

    /// <summary>The value space is non-empty: some value satisfies every conjunct.</summary>
    Satisfiable,

    /// <summary>The value space is empty: no value satisfies every conjunct.</summary>
    Unsatisfiable,
}

/// <summary>
/// Decides the satisfiability of a (possibly negated) OWL 2 data range, or of
/// a conjunction of data ranges on one data node, over OWL 2's restricted
/// concrete domain — a fixed datatype map with unary facets and no n-ary value
/// relations.
/// </summary>
/// <remarks>
/// <para>
/// Both decisive verdicts are sound: <see cref="DatatypeSatisfiability.Unsatisfiable"/>
/// is returned only with a proof of emptiness within the modelled subset, and
/// <see cref="DatatypeSatisfiability.Satisfiable"/> only when a value satisfying
/// every conjunct provably exists. Anything outside the modelled subset —
/// string <c>pattern</c> regular expressions, length facets on non-enumerated
/// spaces, the ordering of the temporal datatypes, or datatypes outside the
/// map — answers <see cref="DatatypeSatisfiability.Unknown"/> rather than
/// guessing.
/// </para>
/// <para>
/// The checker works on the <see cref="OwlDataRange"/> structural AST directly:
/// the AST carries its literals and datatype nodes, so the checker needs no term
/// dictionary and is reusable from the tableau and from unit tests alike. It
/// reuses the numeric value-space algebra of <see cref="OwlNumericRanges"/> and
/// <see cref="OwlNumericLexicals"/> and the family classification of
/// <see cref="OwlDatatypeFamilies"/>; it adds no numeric parsing of its own.
/// </para>
/// <para>
/// The hard spec rule it honours: the <c>owl:real</c>/<c>rational</c>/<c>decimal</c>/
/// <c>integer</c> tower shares one ordered value space the interval algebra
/// reasons over, while <c>xsd:float</c> and <c>xsd:double</c> are fresh copies
/// disjoint from it and from each other, so a positive <c>owl:real</c> together
/// with a positive <c>xsd:double</c> is empty.
/// </para>
/// </remarks>
public static class DatatypeSatisfiabilityChecker
{
    /// <summary>The disjunctive-normal-form size beyond which the checker abstains rather than expand a pathological nesting — far above any real data range.</summary>
    private static int MaxDisjuncts { get; } = 64;

    /// <summary>
    /// Decides the satisfiability of a single data range's value space.
    /// </summary>
    /// <param name="range">The data range.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains; <see cref="DatatypeRegistry.Empty"/> for no registration.</param>
    /// <returns>The verdict; <see cref="DatatypeSatisfiability.Unknown"/> when the range is not fully modelled.</returns>
    public static DatatypeSatisfiability DecideRange(OwlDataRange range, DatatypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(registry);

        List<Disjunct> dnf = BuildDisjunctiveNormalForm(range, out bool tooComplex);
        if(tooComplex)
        {
            return DatatypeSatisfiability.Unknown;
        }

        return CombineDisjuncts(dnf, registry);
    }

    /// <summary>
    /// Decides the joint satisfiability of a conjunction of data ranges on one
    /// data node — every range must hold of the node's single value.
    /// </summary>
    /// <param name="conjuncts">The conjoined data ranges.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains; <see cref="DatatypeRegistry.Empty"/> for no registration.</param>
    /// <returns>The verdict; <see cref="DatatypeSatisfiability.Satisfiable"/> for an empty conjunction (the unconstrained domain).</returns>
    public static DatatypeSatisfiability DecideConjunction(IReadOnlyList<OwlDataRange> conjuncts, DatatypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(conjuncts);
        ArgumentNullException.ThrowIfNull(registry);

        return conjuncts.Count switch
        {
            0 => DatatypeSatisfiability.Satisfiable,
            1 => DecideRange(conjuncts[0], registry),
            _ => DecideRange(new OwlDataIntersectionOf(conjuncts), registry)
        };
    }

    /// <summary>
    /// Decides whether the value space of a conjunction of data ranges holds
    /// at least <paramref name="count"/> pairwise-distinct values — the
    /// cardinality test a positive <c>DataMinCardinality</c> demands of its
    /// qualifying range conjoined with the node's universal constraints.
    /// </summary>
    /// <param name="conjuncts">The conjoined data ranges every counted value must satisfy.</param>
    /// <param name="count">The minimum number of distinct values demanded.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains; <see cref="DatatypeRegistry.Empty"/> for no registration.</param>
    /// <returns><see cref="DatatypeSatisfiability.Satisfiable"/> when at least <paramref name="count"/> distinct values provably exist, <see cref="DatatypeSatisfiability.Unsatisfiable"/> when fewer than <paramref name="count"/> provably exist, and <see cref="DatatypeSatisfiability.Unknown"/> otherwise.</returns>
    public static DatatypeSatisfiability DecideMinCardinality(IReadOnlyList<OwlDataRange> conjuncts, int count, DatatypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(conjuncts);
        ArgumentNullException.ThrowIfNull(registry);

        if(count <= 0)
        {
            //A demand for zero or fewer distinct values is vacuous.
            return DatatypeSatisfiability.Satisfiable;
        }

        if(count == 1)
        {
            //At least one value exists exactly when the conjunction is satisfiable.
            return DecideConjunction(conjuncts, registry);
        }

        OwlDataRange range = conjuncts.Count == 1 ? conjuncts[0] : new OwlDataIntersectionOf(conjuncts);
        List<Disjunct> dnf = BuildDisjunctiveNormalForm(range, out bool tooComplex);
        if(tooComplex)
        {
            return DatatypeSatisfiability.Unknown;
        }

        return CombineDisjunctCounts(dnf, count, registry);
    }

    /// <summary>
    /// Combines the per-disjunct distinct-value bounds of a disjunctive normal
    /// form against a threshold: a value satisfies the range when it satisfies
    /// some disjunct, so the count is at least the largest single-disjunct
    /// count (a lower bound proving the threshold met) and at most the sum of
    /// the per-disjunct counts (an upper bound proving it unmet).
    /// </summary>
    /// <param name="dnf">The disjuncts.</param>
    /// <param name="count">The threshold.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <returns>The combined verdict.</returns>
    private static DatatypeSatisfiability CombineDisjunctCounts(List<Disjunct> dnf, int count, DatatypeRegistry registry)
    {
        long upperSum = 0;
        bool upperKnown = true;
        foreach(Disjunct disjunct in dnf)
        {
            (DatatypeCountBound lower, DatatypeCountBound upper) = DisjunctBounds(disjunct, count, registry);
            if(lower.Kind == DatatypeCountKind.Infinite || lower.Value >= count)
            {
                return DatatypeSatisfiability.Satisfiable;
            }

            if(upper.Kind == DatatypeCountKind.Infinite || upper.Kind == DatatypeCountKind.Unknown)
            {
                upperKnown = false;
            }
            else
            {
                upperSum += upper.Value;
            }
        }

        return upperKnown && upperSum < count ? DatatypeSatisfiability.Unsatisfiable : DatatypeSatisfiability.Unknown;
    }

    /// <summary>
    /// The lower and upper bounds on the number of distinct values one product
    /// disjunct admits: a lower bound is a set of pairwise-distinct admissible
    /// values exhibited, an upper bound bounds the value space from above.
    /// </summary>
    /// <param name="disjunct">The disjunct.</param>
    /// <param name="threshold">The minimum-cardinality threshold the count is asked against, passed to a registered handler's counting question.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <returns>The (lower, upper) distinct-value bounds.</returns>
    private static (DatatypeCountBound Lower, DatatypeCountBound Upper) DisjunctBounds(Disjunct disjunct, int threshold, DatatypeRegistry registry)
    {
        DatatypeSatisfiability emptiness = DecideDisjunct(disjunct, registry);
        if(emptiness == DatatypeSatisfiability.Unsatisfiable)
        {
            return (DatatypeCountBound.Of(0), DatatypeCountBound.Of(0));
        }

        if(emptiness == DatatypeSatisfiability.Unknown)
        {
            return (DatatypeCountBound.Of(0), DatatypeCountBound.Unknown);
        }

        //The disjunct is satisfiable; size its admissible set.
        List<DataAtom> positives = [];
        foreach(DataAtom positive in disjunct.Positives)
        {
            if(positive.Datatype is NamedNode datatype && OwlDatatypeFamilies.Classify(datatype.Iri) == OwlDatatypeFamily.Literal)
            {
                continue;
            }

            positives.Add(positive);
        }

        foreach(DataAtom positive in positives)
        {
            if(positive.OneOf is OwlDataOneOf oneOf)
            {
                return FiniteBounds(oneOf.Literals, positives, disjunct.Negatives, registry);
            }
        }

        return InfiniteBounds(positives, disjunct.Negatives, threshold, registry);
    }

    /// <summary>
    /// The distinct-value bounds of a disjunct pinned to a finite enumeration:
    /// the lower bound is a greedily grown set of pairwise-distinct admitted
    /// candidates, the upper bound the number of value-identity groups the
    /// candidates not provably excluded fall into. A disjunct carrying negated
    /// atoms the bounds cannot price exactly drops its lower bound to zero
    /// rather than overstate it.
    /// </summary>
    /// <param name="candidates">The enumerated candidate literals.</param>
    /// <param name="positives">All positive atoms.</param>
    /// <param name="negatives">The negated atoms.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <returns>The (lower, upper) bounds.</returns>
    private static (DatatypeCountBound Lower, DatatypeCountBound Upper) FiniteBounds(IReadOnlyList<Literal> candidates, List<DataAtom> positives, IReadOnlyList<DataAtom> negatives, DatatypeRegistry registry)
    {
        List<Literal> distinctAdmitted = [];
        List<Literal> representatives = [];
        foreach(Literal candidate in candidates)
        {
            CandidateStatus status = ClassifyCandidate(candidate, positives, negatives, registry);
            if(status == CandidateStatus.Excluded)
            {
                continue;
            }

            //Both an admitted and an undetermined candidate can denote a value,
            //so both are grouped: a candidate joins the first representative it
            //is PROVEN the same value as, and otherwise opens a group of its
            //own. Every group denotes exactly one value, so the group count
            //bounds the distinct values from above; an indeterminate pair never
            //merges, which keeps the bound an over-approximation.
            if(!JoinsARepresentative(candidate, representatives, registry))
            {
                representatives.Add(candidate);
            }

            if(status == CandidateStatus.Admitted && IsDistinctFromAll(candidate, distinctAdmitted, registry))
            {
                distinctAdmitted.Add(candidate);
            }
        }

        return (DatatypeCountBound.Of(distinctAdmitted.Count), DatatypeCountBound.Of(representatives.Count));
    }

    /// <summary>Whether a candidate is provably the same value as some group representative, which merges it into that group.</summary>
    /// <param name="candidate">The candidate literal.</param>
    /// <param name="representatives">The representatives of the groups grown so far.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <returns><see langword="true"/> when the candidate joins an existing group.</returns>
    private static bool JoinsARepresentative(Literal candidate, List<Literal> representatives, DatatypeRegistry registry)
    {
        foreach(Literal representative in representatives)
        {
            if(SameDataValue(candidate, representative, registry) == DatatypeValueIdentity.Same)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a candidate is provably distinct from every member of a set.</summary>
    /// <param name="candidate">The candidate literal.</param>
    /// <param name="members">The members to test against.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <returns><see langword="true"/> when the candidate differs from all members.</returns>
    private static bool IsDistinctFromAll(Literal candidate, List<Literal> members, DatatypeRegistry registry)
    {
        foreach(Literal member in members)
        {
            if(SameDataValue(candidate, member, registry) != DatatypeValueIdentity.Distinct)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The distinct-value bounds of a disjunct over an infinite candidate
    /// domain: a disjunct constraining the value to nothing beyond the data
    /// domain itself is infinite, an exact-real continuum or unbounded integer
    /// space is infinite, a bounded integer space with no unpriced negation is
    /// its integer footprint, a boolean space its admitted-value count, and any
    /// other family is left unbounded above and zero below (a sound abstention).
    /// </summary>
    /// <param name="positives">The positive atoms (no enumeration, rdfs:Literal dropped).</param>
    /// <param name="negatives">The negated atoms.</param>
    /// <param name="threshold">The minimum-cardinality threshold the count is asked against, passed to a registered handler's counting question.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <returns>The (lower, upper) bounds.</returns>
    private static (DatatypeCountBound Lower, DatatypeCountBound Upper) InfiniteBounds(List<DataAtom> positives, IReadOnlyList<DataAtom> negatives, int threshold, DatatypeRegistry registry)
    {
        if(TryRegisteredBase(positives, registry, out RegisteredDatatype? registered, out OwlDatatypeRestriction? registeredRestriction))
        {
            //A positive registered base sizes its own conjunction: the registered
            //counting question over the disjunct's facets, negated atoms, and threshold.
            DatatypeConjunction question = RegisteredConjunction(registeredRestriction!, negatives, threshold);
            DatatypeCountBound bound = registered!.DistinctValues(question);

            return bound.Kind switch
            {
                DatatypeCountKind.Finite => (DatatypeCountBound.Of(bound.Value), DatatypeCountBound.Of(bound.Value)),
                DatatypeCountKind.Infinite => (DatatypeCountBound.Infinite, DatatypeCountBound.Infinite),
                _ => (DatatypeCountBound.Of(0), DatatypeCountBound.Unknown)
            };
        }

        if(positives.Count == 0 && negatives.Count == 0)
        {
            //Nothing constrains the value beyond the data domain itself, which
            //contains the countably infinite xsd:string value space, so the
            //disjunct admits more pairwise-distinct values than any threshold.
            return (DatatypeCountBound.Infinite, DatatypeCountBound.Infinite);
        }

        OwlDatatypeFamily family = OwlDatatypeFamily.Unknown;
        foreach(DataAtom positive in positives)
        {
            if(positive.BaseDatatype() is NamedNode datatype)
            {
                OwlDatatypeFamily positiveFamily = OwlDatatypeFamilies.Classify(datatype.Iri);
                if(positiveFamily is not (OwlDatatypeFamily.Unknown or OwlDatatypeFamily.Literal))
                {
                    family = positiveFamily;

                    break;
                }
            }
        }

        if(family == OwlDatatypeFamily.Boolean)
        {
            long admitted = 0;
            long nonExcluded = 0;
            foreach(Literal candidate in BooleanCandidates)
            {
                CandidateStatus status = ClassifyCandidate(candidate, positives, negatives, registry);
                if(status == CandidateStatus.Admitted)
                {
                    admitted++;
                }

                if(status != CandidateStatus.Excluded)
                {
                    nonExcluded++;
                }
            }

            return (DatatypeCountBound.Of(admitted), DatatypeCountBound.Of(nonExcluded));
        }

        if(family == OwlDatatypeFamily.Numeric)
        {
            return ExactRealBounds(positives, negatives);
        }

        //Text, temporal, binary, anyURI and unknown families: the checker does
        //not size their value spaces, so abstain on the count. A disjunct with no
        //positive atom left but a negated one to price lands here too — the
        //removal it makes from the domain is not sized.
        return (DatatypeCountBound.Of(0), DatatypeCountBound.Unknown);
    }

    /// <summary>
    /// The distinct-value bounds of an exact-real disjunct: a non-degenerate
    /// continuum or an unbounded integer interval holds infinitely many values;
    /// a bounded integer interval with no negation holds its integer footprint;
    /// a degenerate point holds one. Anything the count cannot be read off
    /// exactly (a float/double space, an interval with negations to subtract)
    /// abstains.
    /// </summary>
    /// <param name="positives">The positive numeric atoms.</param>
    /// <param name="negatives">The negated atoms.</param>
    /// <returns>The (lower, upper) bounds.</returns>
    private static (DatatypeCountBound Lower, DatatypeCountBound Upper) ExactRealBounds(List<DataAtom> positives, IReadOnlyList<DataAtom> negatives)
    {
        OwlNumericSpace space = OwlNumericSpace.None;
        ExactInterval interval = ExactInterval.Unbounded;
        foreach(DataAtom positive in positives)
        {
            if(positive.BaseDatatype() is not NamedNode datatype)
            {
                continue;
            }

            OwlNumericSpace positiveSpace = OwlDatatypeFamilies.NumericSpaceOf(datatype.Iri);
            if(space == OwlNumericSpace.None)
            {
                space = positiveSpace;
            }

            if(positiveSpace != OwlNumericSpace.ExactReal || !TryBuildInterval(positive, out ExactInterval atomInterval, out _))
            {
                //A float/double space, or an interval the checker cannot pin.
                return (DatatypeCountBound.Of(0), DatatypeCountBound.Unknown);
            }

            interval = ExactInterval.Intersect(interval, atomInterval);
        }

        if(space != OwlNumericSpace.ExactReal)
        {
            return (DatatypeCountBound.Of(0), DatatypeCountBound.Unknown);
        }

        if(negatives.Count > 0)
        {
            //A bounded integer footprint minus finitely many enumerated points is
            //still an exact count; any other negation cannot be sized.
            return NegatedPointBounds(interval, negatives);
        }

        if(!interval.IntegersOnly)
        {
            //A non-degenerate continuum interval is infinite; a single point holds one value.
            return interval.TryDegeneratePoint(out _) ? (DatatypeCountBound.Of(1), DatatypeCountBound.Of(1)) : (DatatypeCountBound.Infinite, DatatypeCountBound.Infinite);
        }

        if(!interval.TryIntegerFootprint(out BigInteger? low, out BigInteger? high))
        {
            return (DatatypeCountBound.Of(0), DatatypeCountBound.Of(0));
        }

        if(low is not BigInteger lowValue || high is not BigInteger highValue)
        {
            //An unbounded integer side holds infinitely many integers.
            return (DatatypeCountBound.Infinite, DatatypeCountBound.Infinite);
        }

        BigInteger footprint = highValue - lowValue + BigInteger.One;
        if(footprint > long.MaxValue)
        {
            return (DatatypeCountBound.Infinite, DatatypeCountBound.Infinite);
        }

        long size = (long)footprint;

        return (DatatypeCountBound.Of(size), DatatypeCountBound.Of(size));
    }

    /// <summary>
    /// The exact distinct-value bounds of a bounded exact-real integer footprint
    /// minus negated point enumerations: the footprint size less the count of the
    /// distinct in-range integers the enumerations remove. Any negation that is
    /// not a point enumeration, or an unbounded footprint, or a non-integers-only
    /// interval leaves the count unsized (the counting path abstains, exactly as
    /// before this extension) — so the extension only sharpens the previously
    /// unsized bounded-integer-minus-points case the cross-pair min-cardinality
    /// subtraction depends on.
    /// </summary>
    /// <param name="interval">The positive exact-real interval.</param>
    /// <param name="negatives">The negated atoms.</param>
    /// <returns>The (lower, upper) bounds.</returns>
    private static (DatatypeCountBound Lower, DatatypeCountBound Upper) NegatedPointBounds(ExactInterval interval, IReadOnlyList<DataAtom> negatives)
    {
        if(!interval.IntegersOnly || !interval.TryIntegerFootprint(out BigInteger? low, out BigInteger? high))
        {
            return (DatatypeCountBound.Of(0), DatatypeCountBound.Unknown);
        }

        if(low is not BigInteger lowValue || high is not BigInteger highValue)
        {
            //An unbounded integer side minus finitely many points is still infinite.
            return (DatatypeCountBound.Infinite, DatatypeCountBound.Infinite);
        }

        HashSet<BigInteger> removedInRange = [];
        foreach(DataAtom negated in negatives)
        {
            if(negated.OneOf is not OwlDataOneOf oneOf)
            {
                //A negated datatype or restriction removal cannot be sized here.
                return (DatatypeCountBound.Of(0), DatatypeCountBound.Unknown);
            }

            foreach(Literal literal in oneOf.Literals)
            {
                if(OwlDatatypeFamilies.NumericSpaceOf(literal.Datatype.Iri) != OwlNumericSpace.ExactReal)
                {
                    //A non-exact-real point is off the integer line and removes nothing.
                    continue;
                }

                if(!OwlNumericLexicals.TryGetValue(literal.Value.ToString(), literal.Datatype.Iri, out NumericValue value))
                {
                    //An exact-real literal that will not parse cannot be placed; do not size.
                    return (DatatypeCountBound.Of(0), DatatypeCountBound.Unknown);
                }

                if(ExactIntervals.IsIntegral(value, out BigInteger integer) && integer >= lowValue && integer <= highValue)
                {
                    removedInRange.Add(integer);
                }
            }
        }

        BigInteger remaining = highValue - lowValue + BigInteger.One - removedInRange.Count;
        if(remaining <= BigInteger.Zero)
        {
            return (DatatypeCountBound.Of(0), DatatypeCountBound.Of(0));
        }

        if(remaining > long.MaxValue)
        {
            return (DatatypeCountBound.Infinite, DatatypeCountBound.Infinite);
        }

        long size = (long)remaining;

        return (DatatypeCountBound.Of(size), DatatypeCountBound.Of(size));
    }

    /// <summary>
    /// Combines the per-disjunct verdicts of a disjunctive normal form: the
    /// range is satisfiable when any disjunct is, unsatisfiable only when every
    /// disjunct provably is, and unknown otherwise.
    /// </summary>
    /// <param name="dnf">The disjuncts; an empty list denotes the empty value space.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <returns>The combined verdict.</returns>
    private static DatatypeSatisfiability CombineDisjuncts(List<Disjunct> dnf, DatatypeRegistry registry)
    {
        bool allUnsatisfiable = true;
        foreach(Disjunct disjunct in dnf)
        {
            DatatypeSatisfiability verdict = DecideDisjunct(disjunct, registry);
            if(verdict == DatatypeSatisfiability.Satisfiable)
            {
                return DatatypeSatisfiability.Satisfiable;
            }

            if(verdict != DatatypeSatisfiability.Unsatisfiable)
            {
                allUnsatisfiable = false;
            }
        }

        return allUnsatisfiable ? DatatypeSatisfiability.Unsatisfiable : DatatypeSatisfiability.Unknown;
    }

    /// <summary>
    /// Decides one product disjunct: a set of positive atoms and a set of
    /// negated atoms that must all hold of a single value.
    /// </summary>
    /// <param name="disjunct">The disjunct.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <returns>The verdict for this disjunct.</returns>
    private static DatatypeSatisfiability DecideDisjunct(Disjunct disjunct, DatatypeRegistry registry)
    {
        //A negated rdfs:Literal excludes the whole domain, and an atom asserted
        //both positively and negatively is a direct contradiction — both empty
        //the disjunct outright.
        foreach(DataAtom negated in disjunct.Negatives)
        {
            if(negated.Datatype is NamedNode negatedDatatype && OwlDatatypeFamilies.Classify(negatedDatatype.Iri) == OwlDatatypeFamily.Literal)
            {
                return DatatypeSatisfiability.Unsatisfiable;
            }
        }

        if(HasDirectContradiction(disjunct))
        {
            return DatatypeSatisfiability.Unsatisfiable;
        }

        //rdfs:Literal positives are the whole domain and impose no constraint.
        List<DataAtom> positives = [];
        foreach(DataAtom positive in disjunct.Positives)
        {
            if(positive.Datatype is NamedNode positiveDatatype && OwlDatatypeFamilies.Classify(positiveDatatype.Iri) == OwlDatatypeFamily.Literal)
            {
                continue;
            }

            positives.Add(positive);
        }

        //A positive enumeration pins the value to a finite candidate set.
        foreach(DataAtom positive in positives)
        {
            if(positive.OneOf is OwlDataOneOf oneOf)
            {
                return DecideFinite(oneOf.Literals, positives, disjunct.Negatives, registry);
            }
        }

        return DecideInfinite(positives, disjunct.Negatives, registry);
    }

    /// <summary>
    /// Whether the disjunct asserts a named datatype both positively and
    /// negatively — a value in and out of the same space, which is empty.
    /// </summary>
    /// <param name="disjunct">The disjunct.</param>
    /// <returns><see langword="true"/> when a contradicting datatype pair is present.</returns>
    private static bool HasDirectContradiction(Disjunct disjunct)
    {
        foreach(DataAtom positive in disjunct.Positives)
        {
            if(positive.Datatype is not NamedNode positiveDatatype)
            {
                continue;
            }

            foreach(DataAtom negated in disjunct.Negatives)
            {
                if(negated.Datatype is NamedNode negatedDatatype && positiveDatatype.Iri.Equals(negatedDatatype.Iri))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Decides a disjunct whose value is pinned to a finite candidate set: it is
    /// satisfiable when some candidate provably lies in every positive atom and
    /// no negative atom, unsatisfiable when every candidate is provably excluded,
    /// and unknown otherwise.
    /// </summary>
    /// <param name="candidates">The enumerated candidate literals from the positive enumeration.</param>
    /// <param name="positives">All positive atoms (including the enumeration itself).</param>
    /// <param name="negatives">The negated atoms.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <returns>The verdict.</returns>
    private static DatatypeSatisfiability DecideFinite(IReadOnlyList<Literal> candidates, List<DataAtom> positives, IReadOnlyList<DataAtom> negatives, DatatypeRegistry registry)
    {
        bool allExcluded = true;
        foreach(Literal candidate in candidates)
        {
            CandidateStatus status = ClassifyCandidate(candidate, positives, negatives, registry);
            if(status == CandidateStatus.Admitted)
            {
                return DatatypeSatisfiability.Satisfiable;
            }

            if(status != CandidateStatus.Excluded)
            {
                allExcluded = false;
            }
        }

        return allExcluded ? DatatypeSatisfiability.Unsatisfiable : DatatypeSatisfiability.Unknown;
    }

    /// <summary>
    /// Classifies a single candidate value against all atoms: admitted when it
    /// provably satisfies every positive and no negative, excluded when some
    /// atom provably rules it out, undetermined otherwise.
    /// </summary>
    /// <param name="candidate">The candidate literal.</param>
    /// <param name="positives">The positive atoms.</param>
    /// <param name="negatives">The negated atoms.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <returns>The candidate's status.</returns>
    private static CandidateStatus ClassifyCandidate(Literal candidate, List<DataAtom> positives, IReadOnlyList<DataAtom> negatives, DatatypeRegistry registry)
    {
        //An ill-typed literal — one whose value lies outside its own datatype's
        //value space, e.g. "256"^^xsd:unsignedByte — denotes no value, so it is no
        //member of any space and can never witness satisfiability.
        DatatypeMembership selfType = DatatypeSpaceMembership(candidate.Datatype.Iri, candidate, registry);
        if(selfType == DatatypeMembership.Out)
        {
            return CandidateStatus.Excluded;
        }

        bool admissible = selfType == DatatypeMembership.In;
        foreach(DataAtom positive in positives)
        {
            DatatypeMembership membership = Membership(positive, candidate, registry);
            if(membership == DatatypeMembership.Out)
            {
                return CandidateStatus.Excluded;
            }

            if(membership == DatatypeMembership.Indeterminate)
            {
                admissible = false;
            }
        }

        foreach(DataAtom negated in negatives)
        {
            DatatypeMembership membership = Membership(negated, candidate, registry);
            if(membership == DatatypeMembership.In)
            {
                return CandidateStatus.Excluded;
            }

            if(membership == DatatypeMembership.Indeterminate)
            {
                admissible = false;
            }
        }

        return admissible ? CandidateStatus.Admitted : CandidateStatus.Undetermined;
    }

    /// <summary>
    /// Decides a disjunct over an infinite candidate domain: it partitions the
    /// positive atoms by value-space family, declares the disjunct empty when
    /// they span two disjoint families, dispatches a single numeric or boolean
    /// family to its decision procedure, and abstains otherwise.
    /// </summary>
    /// <param name="positives">The positive atoms (no enumeration among them, rdfs:Literal already dropped).</param>
    /// <param name="negatives">The negated atoms.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <returns>The verdict.</returns>
    private static DatatypeSatisfiability DecideInfinite(List<DataAtom> positives, IReadOnlyList<DataAtom> negatives, DatatypeRegistry registry)
    {
        if(TryRegisteredBase(positives, registry, out RegisteredDatatype? registered, out OwlDatatypeRestriction? registeredRestriction))
        {
            //A positive registered base decides its own emptiness: the registered
            //conjunction over the disjunct's facets and negated atoms.
            return registered!.DecideConjunction(RegisteredConjunction(registeredRestriction!, negatives, 0));
        }

        OwlDatatypeFamily family = OwlDatatypeFamily.Unknown;
        bool hasUnmodelledPositive = false;
        foreach(DataAtom positive in positives)
        {
            if(positive.BaseDatatype() is not NamedNode datatype)
            {
                hasUnmodelledPositive = true;

                continue;
            }

            OwlDatatypeFamily positiveFamily = OwlDatatypeFamilies.Classify(datatype.Iri);
            if(positiveFamily is OwlDatatypeFamily.Unknown or OwlDatatypeFamily.Literal)
            {
                hasUnmodelledPositive = true;

                continue;
            }

            if(family == OwlDatatypeFamily.Unknown)
            {
                family = positiveFamily;
            }
            else if(family != positiveFamily)
            {
                //Two disjoint families on one value: empty regardless of anything else.
                return DatatypeSatisfiability.Unsatisfiable;
            }
        }

        if(family == OwlDatatypeFamily.Unknown)
        {
            //No modelled positive constraint. The whole domain minus finitely many
            //modelled family-subsets stays non-empty; an unmodelled removal blocks the witness.
            return AnyUnmodelledNegative(negatives, registry) || hasUnmodelledPositive
                ? DatatypeSatisfiability.Unknown
                : DatatypeSatisfiability.Satisfiable;
        }

        if(hasUnmodelledPositive)
        {
            return DatatypeSatisfiability.Unknown;
        }

        return family switch
        {
            OwlDatatypeFamily.Numeric => DecideNumeric(positives, negatives),
            OwlDatatypeFamily.Boolean => DecideFinite(BooleanCandidates, positives, negatives, registry),
            OwlDatatypeFamily.Temporal => DecideTemporal(positives, negatives),
            OwlDatatypeFamily.Text => DecideTextAutomaton(positives, negatives),
            _ => DatatypeSatisfiability.Unknown
        };
    }

    /// <summary>
    /// Finds the sole positive atom whose base datatype is a registered declarative type — a datatype the
    /// family classifier abstains on but the registry defines — so the disjunct is decided by the registered
    /// handler rather than a built-in family procedure. Reports no registered base when there is none, more
    /// than one, or any built-in-family positive alongside (a mixed conjunction the checker leaves to its own
    /// abstention). A delegate-backed (self-certified) registered base qualifies here: it decides its own
    /// conjunction.
    /// </summary>
    /// <param name="positives">The positive atoms.</param>
    /// <param name="registry">The registered-datatype set.</param>
    /// <param name="registered">The registered handler, when exactly one positive registered base is present with no built-in-family positive.</param>
    /// <param name="restriction">The registered atom's restriction, when the atom carried facets; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a single registered base governs the disjunct.</returns>
    private static bool TryRegisteredBase(IReadOnlyList<DataAtom> positives, DatatypeRegistry registry, out RegisteredDatatype? registered, out OwlDatatypeRestriction? restriction)
    {
        registered = null;
        restriction = null;
        foreach(DataAtom positive in positives)
        {
            if(positive.BaseDatatype() is not NamedNode datatype)
            {
                continue;
            }

            OwlDatatypeFamily positiveFamily = OwlDatatypeFamilies.Classify(datatype.Iri);
            if(positiveFamily is OwlDatatypeFamily.Literal)
            {
                continue;
            }

            if(positiveFamily != OwlDatatypeFamily.Unknown || !registry.TryGet(datatype.Iri, out RegisteredDatatype? handler))
            {
                //A built-in-family positive, or an unmodelled base with no registration, alongside a
                //registered base is a mixed conjunction; leave it to the family procedures.
                if(positiveFamily != OwlDatatypeFamily.Unknown)
                {
                    registered = null;
                    restriction = null;

                    return false;
                }

                continue;
            }

            if(registered is not null)
            {
                //More than one registered base: a mixed conjunction the checker does not fold.
                registered = null;
                restriction = null;

                return false;
            }

            registered = handler;
            restriction = positive.Restriction;
        }

        return registered is not null;
    }

    /// <summary>Builds the registered-datatype conjunction of a disjunct: the registered atom's positive facets, the disjunct's negated atoms, and the counting threshold.</summary>
    /// <param name="registeredRestriction">The registered atom's restriction, or <see langword="null"/> when the atom was a bare datatype reference.</param>
    /// <param name="negatives">The disjunct's negated atoms.</param>
    /// <param name="threshold">The minimum-cardinality threshold, or zero when the emptiness question is asked.</param>
    /// <returns>The conjunction.</returns>
    private static DatatypeConjunction RegisteredConjunction(OwlDatatypeRestriction? registeredRestriction, IReadOnlyList<DataAtom> negatives, int threshold)
    {
        IReadOnlyList<OwlFacetRestriction> facets = registeredRestriction is null ? [] : registeredRestriction.Restrictions;
        List<OwlDataRange> negatedRanges = [];
        foreach(DataAtom negated in negatives)
        {
            if(negated.OneOf is OwlDataOneOf oneOf)
            {
                negatedRanges.Add(oneOf);
            }
            else if(negated.Restriction is OwlDatatypeRestriction restriction)
            {
                negatedRanges.Add(restriction);
            }
            else if(negated.Datatype is NamedNode datatype)
            {
                negatedRanges.Add(new OwlDatatypeReference(datatype));
            }
        }

        return new DatatypeConjunction(facets, negatedRanges, threshold);
    }

    /// <summary>
    /// Decides a text-family disjunct over the <c>xsd:string</c> value space through the built-in automaton
    /// route: the universal string language intersected with the positive pattern and length facets and the
    /// complements of the negated string enumerations and restrictions, read off as emptiness. The route
    /// fires only when there is a pattern or length facet to compile or a negated string range to subtract —
    /// a bare string positive keeps the pre-route abstention, so an empty registry with no pattern facets is
    /// byte-identical. A positive over a text sibling other than <c>xsd:string</c>, or any construct the
    /// automata do not model, abstains.
    /// </summary>
    /// <param name="positives">The positive text atoms.</param>
    /// <param name="negatives">The negated atoms.</param>
    /// <returns>The verdict.</returns>
    private static DatatypeSatisfiability DecideTextAutomaton(List<DataAtom> positives, IReadOnlyList<DataAtom> negatives)
    {
        List<OwlFacetRestriction> facets = [];
        bool hasAutomatonFacet = false;
        foreach(DataAtom positive in positives)
        {
            if(positive.BaseDatatype() is not NamedNode datatype || !datatype.Iri.Equals(Vocabulary.Xsd.String))
            {
                //A text sibling other than xsd:string needs base-automaton intersection the route defers.
                return DatatypeSatisfiability.Unknown;
            }

            if(positive.Restriction is OwlDatatypeRestriction restriction)
            {
                foreach(OwlFacetRestriction facet in restriction.Restrictions)
                {
                    facets.Add(facet);
                    hasAutomatonFacet |= IsAutomatonFacet(facet.Facet.Iri);
                }
            }
        }

        List<OwlDataRange> negatedStringRanges = [];
        bool hasNegatedString = false;
        foreach(DataAtom negated in negatives)
        {
            if(negated.OneOf is OwlDataOneOf oneOf && AllStringLiterals(oneOf.Literals))
            {
                negatedStringRanges.Add(oneOf);
                hasNegatedString = true;
            }
            else if(negated.Restriction is OwlDatatypeRestriction restriction && restriction.Datatype.Iri.Equals(Vocabulary.Xsd.String))
            {
                negatedStringRanges.Add(restriction);
                hasNegatedString = true;
            }
            else
            {
                //An unmodelled negation over the string space blocks a witness but is sound to ignore for emptiness.
                return DatatypeSatisfiability.Unknown;
            }
        }

        if(!hasAutomatonFacet && !hasNegatedString)
        {
            //Nothing to decide beyond the universal string language: keep the pre-route abstention.
            return DatatypeSatisfiability.Unknown;
        }

        DatatypeConjunction conjunction = new(facets, negatedStringRanges, 0);

        return DatatypeAutomata.DecideEmptiness(LengthAutomaton.AtLeast(0), conjunction, AutomatonBudgets.Default);
    }

    /// <summary>Whether a facet IRI is one the built-in string automaton route compiles — a pattern or a length facet.</summary>
    /// <param name="facetIri">The facet IRI.</param>
    /// <returns><see langword="true"/> for a pattern, length, minLength, or maxLength facet.</returns>
    private static bool IsAutomatonFacet(Utf8String facetIri)
    {
        return facetIri.Equals(Vocabulary.XsdFacets.Pattern)
            || facetIri.Equals(Vocabulary.XsdFacets.Length)
            || facetIri.Equals(Vocabulary.XsdFacets.MinLength)
            || facetIri.Equals(Vocabulary.XsdFacets.MaxLength);
    }

    /// <summary>Whether every literal of an enumeration is an <c>xsd:string</c> literal, so its exact-string complement is sound in the string value space.</summary>
    /// <param name="literals">The enumerated literals.</param>
    /// <returns><see langword="true"/> when every literal is a plain <c>xsd:string</c>.</returns>
    private static bool AllStringLiterals(IReadOnlyList<Literal> literals)
    {
        foreach(Literal literal in literals)
        {
            if(literal.Language is not null || !literal.Datatype.Iri.Equals(Vocabulary.Xsd.String))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether any negated atom is a datatype or restriction the checker cannot
    /// model, so its removal from the domain cannot be accounted for.
    /// </summary>
    /// <param name="negatives">The negated atoms.</param>
    /// <param name="registry">The registered-datatype set: a negated registered declarative type is a structurally non-covering removal and counts as modelled; a delegate-backed one does not, so it stays unmodelled.</param>
    /// <returns><see langword="true"/> when an unmodelled negation is present.</returns>
    private static bool AnyUnmodelledNegative(IReadOnlyList<DataAtom> negatives, DatatypeRegistry registry)
    {
        foreach(DataAtom negated in negatives)
        {
            if(negated.BaseDatatype() is not NamedNode datatype)
            {
                //A negated enumeration removes only finitely many points — modelled.
                if(negated.OneOf is null)
                {
                    return true;
                }

                continue;
            }

            if(OwlDatatypeFamilies.Classify(datatype.Iri) == OwlDatatypeFamily.Unknown)
            {
                //A registered declarative type guarantees a proper, non-covering value space, so removing it
                //from the whole domain leaves a witness — modelled. A delegate-backed (self-certified)
                //definition never checks domain coverage, so a domain-covering delegate would make the
                //difference empty; it stays unmodelled and the branch abstains.
                if(registry.TryGet(datatype.Iri, out RegisteredDatatype? handler) && !handler.SelfCertified)
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Decides a numeric-family disjunct: it splits the positive atoms into the
    /// three disjoint numeric spaces, declares emptiness when they span more than
    /// one, and runs the decision procedure of that space — the exact-real
    /// interval algebra, or the discrete rank algebra of the float and double
    /// spaces, whose adjacency the interval algebra cannot express.
    /// </summary>
    /// <param name="positives">The positive numeric atoms.</param>
    /// <param name="negatives">The negated atoms.</param>
    /// <returns>The verdict.</returns>
    private static DatatypeSatisfiability DecideNumeric(List<DataAtom> positives, IReadOnlyList<DataAtom> negatives)
    {
        OwlNumericSpace space = OwlNumericSpace.None;
        foreach(DataAtom positive in positives)
        {
            if(positive.BaseDatatype() is not NamedNode datatype)
            {
                continue;
            }

            OwlNumericSpace positiveSpace = OwlDatatypeFamilies.NumericSpaceOf(datatype.Iri);
            if(space == OwlNumericSpace.None)
            {
                space = positiveSpace;
            }
            else if(space != positiveSpace)
            {
                //Disjoint numeric value spaces (e.g. owl:real and xsd:double).
                return DatatypeSatisfiability.Unsatisfiable;
            }
        }

        return space switch
        {
            OwlNumericSpace.ExactReal => DecideExactReal(positives, negatives),
            OwlNumericSpace.Float or OwlNumericSpace.Double => DecideFloatingSpace(space, positives, negatives),
            _ => DatatypeSatisfiability.Unknown
        };
    }

    /// <summary>
    /// Decides a disjunct over one IEEE-754 value space by folding its ordered
    /// facets into a rank run and reading off the run's emptiness — the open
    /// interval between two adjacent floating values holds nothing, which the
    /// dense interval algebra cannot see.
    /// </summary>
    /// <remarks>
    /// The decided shape is a conjunction of bare base-type atoms and
    /// restrictions of that base carrying only the four ordered facets, with no
    /// negated atom present. Anything else — a negation, an enumeration, a facet
    /// that is not an ordered bound, a bound of another value space, a bound that
    /// does not parse, a <c>NaN</c> bound — abstains, so decisiveness reaches
    /// exactly the interval-emptiness question and no further.
    /// </remarks>
    /// <param name="space">The IEEE-754 space every positive atom shares.</param>
    /// <param name="positives">The positive atoms of that space.</param>
    /// <param name="negatives">The negated atoms.</param>
    /// <returns>The verdict.</returns>
    private static DatatypeSatisfiability DecideFloatingSpace(OwlNumericSpace space, List<DataAtom> positives, IReadOnlyList<DataAtom> negatives)
    {
        if(negatives.Count > 0 || !FloatingPointSpaces.IsModelled(space))
        {
            return DatatypeSatisfiability.Unknown;
        }

        FloatingRankInterval interval = FloatingPointSpaces.Whole(space);
        foreach(DataAtom positive in positives)
        {
            if(positive.OneOf is not null)
            {
                return DatatypeSatisfiability.Unknown;
            }

            if(positive.Restriction is not OwlDatatypeRestriction restriction)
            {
                //A bare base-type atom pins the space and constrains nothing further.
                continue;
            }

            foreach(OwlFacetRestriction facet in restriction.Restrictions)
            {
                if(!FloatingPointSpaces.TryApplyFacet(space, facet.Facet.Iri, facet.Value, interval, out interval))
                {
                    return DatatypeSatisfiability.Unknown;
                }
            }
        }

        return interval.IsEmpty ? DatatypeSatisfiability.Unsatisfiable : DatatypeSatisfiability.Satisfiable;
    }

    /// <summary>
    /// Decides an exact-real disjunct by building the positive interval (base
    /// spaces intersected with facet bounds), then testing whether subtracting
    /// the negated spaces and points leaves a value.
    /// </summary>
    /// <param name="positives">The positive exact-real atoms.</param>
    /// <param name="negatives">The negated atoms.</param>
    /// <returns>The verdict.</returns>
    private static DatatypeSatisfiability DecideExactReal(List<DataAtom> positives, IReadOnlyList<DataAtom> negatives)
    {
        ExactInterval positive = ExactInterval.Unbounded;
        bool hasUnmodelledPositive = false;
        RealLevel narrowestPositiveLevel = RealLevel.Real;
        foreach(DataAtom atom in positives)
        {
            if(TryBuildInterval(atom, out ExactInterval interval, out RealLevel level))
            {
                positive = ExactInterval.Intersect(positive, interval);
                if(level < narrowestPositiveLevel)
                {
                    narrowestPositiveLevel = level;
                }
            }
            else
            {
                hasUnmodelledPositive = true;
            }
        }

        if(positive.IsEmpty())
        {
            return DatatypeSatisfiability.Unsatisfiable;
        }

        List<ExactInterval> negativeIntervals = [];
        List<NumericValue> negativePoints = [];
        bool blockSatisfiable = hasUnmodelledPositive;
        foreach(DataAtom atom in negatives)
        {
            NegativeEffect effect = ClassifyNegative(atom, narrowestPositiveLevel, negativeIntervals, negativePoints);
            if(effect == NegativeEffect.EmptiesAll)
            {
                return DatatypeSatisfiability.Unsatisfiable;
            }

            blockSatisfiable |= effect == NegativeEffect.BlocksWitness;
        }

        return ResidueStatus(positive, negativeIntervals, negativePoints) switch
        {
            Residue.Empty => DatatypeSatisfiability.Unsatisfiable,
            Residue.NonEmpty => blockSatisfiable ? DatatypeSatisfiability.Unknown : DatatypeSatisfiability.Satisfiable,
            _ => DatatypeSatisfiability.Unknown
        };
    }

    /// <summary>
    /// Classifies a negated atom's effect on an exact-real positive interval,
    /// appending the genuine removals to the working lists.
    /// </summary>
    /// <param name="atom">The negated atom.</param>
    /// <param name="narrowestPositiveLevel">The narrowest exact-real level among the positive atoms — a bare complement of a space at least this broad empties the positive interval.</param>
    /// <param name="negativeIntervalsToAppendTo">The interval removals collected so far, appended to.</param>
    /// <param name="negativePointsToAppendTo">The point removals collected so far, appended to.</param>
    /// <returns>The effect category.</returns>
    private static NegativeEffect ClassifyNegative(DataAtom atom, RealLevel narrowestPositiveLevel, List<ExactInterval> negativeIntervalsToAppendTo, List<NumericValue> negativePointsToAppendTo)
    {
        if(atom.OneOf is OwlDataOneOf oneOf)
        {
            return ClassifyNegativePoints(oneOf.Literals, negativePointsToAppendTo);
        }

        if(atom.BaseDatatype() is not NamedNode datatype)
        {
            return NegativeEffect.BlocksWitness;
        }

        OwlDatatypeFamily family = OwlDatatypeFamilies.Classify(datatype.Iri);
        if(family == OwlDatatypeFamily.Numeric)
        {
            return ClassifyNegativeNumeric(atom, datatype.Iri, narrowestPositiveLevel, negativeIntervalsToAppendTo);
        }

        if(family == OwlDatatypeFamily.Unknown)
        {
            //An unknown datatype could overlap the exact-real line.
            return NegativeEffect.BlocksWitness;
        }

        //A disjoint family removes nothing from the exact-real line.
        return NegativeEffect.None;
    }

    /// <summary>
    /// Classifies a negated numeric atom's effect on an exact-real positive
    /// interval. The exact-real value spaces nest (<c>integer</c> ⊂ <c>decimal</c>
    /// ⊂ <c>rational</c> ⊂ <c>real</c>) but the interval algebra represents them
    /// all as the real line, so a continuum removal (a complement of, or a
    /// bounded restriction on, <c>decimal</c>/<c>rational</c>/<c>real</c>) is exact
    /// only when the positive space is contained in the removed space; otherwise it
    /// would wrongly discard the positive's non-decimal values, so it blocks the
    /// witness instead. An integer-tower removal is always an exact integer footprint.
    /// </summary>
    /// <param name="atom">The negated atom.</param>
    /// <param name="datatypeIri">The atom's base datatype IRI (a numeric datatype).</param>
    /// <param name="narrowestPositiveLevel">The narrowest exact-real level among the positive atoms.</param>
    /// <param name="negativeIntervalsToAppendTo">The interval removals collected so far, appended to.</param>
    /// <returns>The effect category.</returns>
    private static NegativeEffect ClassifyNegativeNumeric(DataAtom atom, Utf8String datatypeIri, RealLevel narrowestPositiveLevel, List<ExactInterval> negativeIntervalsToAppendTo)
    {
        if(OwlDatatypeFamilies.NumericSpaceOf(datatypeIri) != OwlNumericSpace.ExactReal)
        {
            //A disjoint numeric space (float, double) removes nothing from the exact-real line.
            return NegativeEffect.None;
        }

        if(atom.Restriction is null)
        {
            //A bare complement of a continuum datatype removes a dense subset: it
            //empties the positive interval when the positive space is contained in
            //it, and otherwise cannot be represented exactly.
            if(LevelOfBareContinuum(datatypeIri) is RealLevel continuumLevel)
            {
                return narrowestPositiveLevel <= continuumLevel ? NegativeEffect.EmptiesAll : NegativeEffect.BlocksWitness;
            }

            //A bare complement of an integer-tower datatype is an exact integer footprint.
            if(TryBuildInterval(atom, out ExactInterval integerSpace, out _))
            {
                negativeIntervalsToAppendTo.Add(integerSpace);

                return NegativeEffect.None;
            }

            return NegativeEffect.BlocksWitness;
        }

        if(!TryBuildInterval(atom, out ExactInterval interval, out RealLevel level))
        {
            //An unmodelled facet (length, pattern, digit counts) cannot be pinned.
            return NegativeEffect.BlocksWitness;
        }

        //An integer footprint is exact for both integer and continuum positives;
        //a continuum sub-interval over-removes unless the positive space is
        //contained in the negative's space.
        if(interval.IntegersOnly || narrowestPositiveLevel <= level)
        {
            negativeIntervalsToAppendTo.Add(interval);

            return NegativeEffect.None;
        }

        return NegativeEffect.BlocksWitness;
    }

    /// <summary>
    /// Classifies the points of a negated enumeration: exact-real values are
    /// collected as removals, clearly disjoint-family literals are ignored, and
    /// an exact-real literal that will not parse blocks the witness.
    /// </summary>
    /// <param name="literals">The enumerated literals.</param>
    /// <param name="negativePointsToAppendTo">The point removals collected so far, appended to.</param>
    /// <returns>The combined effect.</returns>
    private static NegativeEffect ClassifyNegativePoints(IReadOnlyList<Literal> literals, List<NumericValue> negativePointsToAppendTo)
    {
        NegativeEffect effect = NegativeEffect.None;
        foreach(Literal literal in literals)
        {
            OwlNumericSpace space = OwlDatatypeFamilies.NumericSpaceOf(literal.Datatype.Iri);
            if(space != OwlNumericSpace.ExactReal)
            {
                //Float, double, and non-numeric points are not on the exact-real line.
                continue;
            }

            if(OwlNumericLexicals.TryGetValue(literal.Value.ToString(), literal.Datatype.Iri, out NumericValue value))
            {
                negativePointsToAppendTo.Add(value);
            }
            else
            {
                effect = NegativeEffect.BlocksWitness;
            }
        }

        return effect;
    }

    /// <summary>
    /// Builds the exact-real interval and value-space level of a datatype or
    /// restriction atom, or reports that it cannot be modelled exactly.
    /// </summary>
    /// <param name="atom">The atom.</param>
    /// <param name="interval">The interval, when modelled.</param>
    /// <param name="level">The atom's exact-real value-space level, when modelled.</param>
    /// <returns><see langword="true"/> when the atom is a fully modelled exact-real constraint.</returns>
    private static bool TryBuildInterval(DataAtom atom, out ExactInterval interval, out RealLevel level)
    {
        if(atom.BaseDatatype() is NamedNode datatype)
        {
            return ExactIntervals.TryBuildInterval(datatype, atom.Restriction, out interval, out level);
        }

        interval = ExactInterval.Unbounded;
        level = RealLevel.Real;

        return false;
    }

    /// <summary>
    /// Tests whether a positive interval minus the negated intervals and points
    /// still contains a value of the required kind.
    /// </summary>
    /// <param name="positive">The positive interval.</param>
    /// <param name="negativeIntervals">The interval removals.</param>
    /// <param name="negativePoints">The point removals.</param>
    /// <returns>The residue status.</returns>
    private static Residue ResidueStatus(ExactInterval positive, List<ExactInterval> negativeIntervals, List<NumericValue> negativePoints)
    {
        return positive.IntegersOnly
            ? IntegerResidue(positive, negativeIntervals, negativePoints)
            : ContinuumResidue(positive, negativeIntervals, negativePoints);
    }

    /// <summary>
    /// Computes the residue of an integers-only positive interval after removing
    /// the integer footprints of the negated intervals and the integer points.
    /// </summary>
    /// <param name="positive">The positive interval.</param>
    /// <param name="negativeIntervals">The interval removals.</param>
    /// <param name="negativePoints">The point removals.</param>
    /// <returns>The residue status.</returns>
    private static Residue IntegerResidue(ExactInterval positive, List<ExactInterval> negativeIntervals, List<NumericValue> negativePoints)
    {
        if(!positive.TryIntegerFootprint(out BigInteger? positiveLow, out BigInteger? positiveHigh))
        {
            return Residue.Empty;
        }

        List<(BigInteger? Low, BigInteger? High)> removals = [];
        foreach(ExactInterval negative in negativeIntervals)
        {
            if(negative.TryIntegerFootprint(out BigInteger? low, out BigInteger? high))
            {
                removals.Add((low, high));
            }
        }

        foreach(NumericValue point in negativePoints)
        {
            if(ExactIntervals.IsIntegral(point, out BigInteger integer))
            {
                removals.Add((integer, integer));
            }
        }

        return IntegerResidueNonEmpty(positiveLow, positiveHigh, removals) ? Residue.NonEmpty : Residue.Empty;
    }

    /// <summary>
    /// Computes the residue of a continuum positive interval. Countable removals
    /// (integer footprints, points) cannot empty a non-degenerate interval, so
    /// only continuum interval removals matter — except for a degenerate single
    /// point, where any covering removal empties it.
    /// </summary>
    /// <param name="positive">The positive interval.</param>
    /// <param name="negativeIntervals">The interval removals.</param>
    /// <param name="negativePoints">The point removals.</param>
    /// <returns>The residue status.</returns>
    private static Residue ContinuumResidue(ExactInterval positive, List<ExactInterval> negativeIntervals, List<NumericValue> negativePoints)
    {
        if(positive.TryDegeneratePoint(out NumericValue point))
        {
            foreach(ExactInterval negative in negativeIntervals)
            {
                if(negative.Contains(point))
                {
                    return Residue.Empty;
                }
            }

            foreach(NumericValue removed in negativePoints)
            {
                if(NumericValue.Compare(point, removed) == ComparisonResult.Equal)
                {
                    return Residue.Empty;
                }
            }

            return Residue.NonEmpty;
        }

        List<ExactInterval> continuumNegatives = [];
        foreach(ExactInterval negative in negativeIntervals)
        {
            if(!negative.IntegersOnly)
            {
                if(negative.Lower is null && negative.Upper is null)
                {
                    //A removal covering the whole continuum empties the interval.
                    return Residue.Empty;
                }

                continuumNegatives.Add(negative);
            }
        }

        if(continuumNegatives.Count == 0)
        {
            //A non-degenerate real interval minus countably many removals stays non-empty.
            return Residue.NonEmpty;
        }

        return SampleCoverage(positive, continuumNegatives);
    }

    /// <summary>
    /// Tests, by sampling each cell of the endpoint arrangement, whether a
    /// non-degenerate continuum interval has a value outside every continuum
    /// removal.
    /// </summary>
    /// <param name="positive">The positive interval.</param>
    /// <param name="continuumNegatives">The continuum interval removals.</param>
    /// <returns>The residue status.</returns>
    private static Residue SampleCoverage(ExactInterval positive, List<ExactInterval> continuumNegatives)
    {
        List<decimal> anchors = [];
        if(!TryCollectAnchor(positive.Lower, anchors) || !TryCollectAnchor(positive.Upper, anchors))
        {
            return Residue.Indeterminate;
        }

        foreach(ExactInterval negative in continuumNegatives)
        {
            if(!TryCollectAnchor(negative.Lower, anchors) || !TryCollectAnchor(negative.Upper, anchors))
            {
                return Residue.Indeterminate;
            }
        }

        anchors.Sort();

        //Distinct endpoints only: a value shared by the positive interval and a
        //removal appears twice, and an equal consecutive pair has no open cell
        //between it (its midpoint would equal the endpoint and trip the guard below).
        List<decimal> boundaries = [];
        foreach(decimal anchor in anchors)
        {
            if(boundaries.Count == 0 || boundaries[^1] != anchor)
            {
                boundaries.Add(anchor);
            }
        }

        anchors = boundaries;

        List<decimal> samples = [];
        if(anchors.Count == 0)
        {
            samples.Add(0m);
        }
        else
        {
            samples.Add(anchors[0] - 1m);
            samples.Add(anchors[^1] + 1m);
            for(int index = 0; index < anchors.Count; index++)
            {
                samples.Add(anchors[index]);
                if(index + 1 < anchors.Count)
                {
                    decimal midpoint = (anchors[index] + anchors[index + 1]) / 2m;
                    if(midpoint <= anchors[index] || midpoint >= anchors[index + 1])
                    {
                        //The open cell between two adjacent decimals holds reals the
                        //decimal grid cannot sample — abstain rather than miss it.
                        return Residue.Indeterminate;
                    }

                    samples.Add(midpoint);
                }
            }
        }

        foreach(decimal sample in samples)
        {
            NumericValue candidate = new(sample);
            if(positive.Contains(candidate) && !CoveredBy(continuumNegatives, candidate))
            {
                return Residue.NonEmpty;
            }
        }

        return Residue.Empty;
    }

    /// <summary>Whether a value lies in any of the removal intervals.</summary>
    /// <param name="intervals">The removal intervals.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when the value is covered.</returns>
    private static bool CoveredBy(List<ExactInterval> intervals, NumericValue value)
    {
        foreach(ExactInterval interval in intervals)
        {
            if(interval.Contains(value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Appends an endpoint's value as a decimal anchor, reporting failure when it does not fit a decimal.</summary>
    /// <param name="endpoint">The endpoint, or <c>null</c> for an unbounded side.</param>
    /// <param name="anchorsToAppendTo">The anchor list, appended to.</param>
    /// <returns><see langword="true"/> on success (including an unbounded side, which adds nothing).</returns>
    private static bool TryCollectAnchor(Endpoint? endpoint, List<decimal> anchorsToAppendTo)
    {
        if(endpoint is not Endpoint present)
        {
            return true;
        }

        if(!TryToDecimal(present.Value, out decimal value))
        {
            return false;
        }

        anchorsToAppendTo.Add(value);

        return true;
    }

    /// <summary>
    /// Whether an integers-only positive range minus a set of integer-footprint
    /// removals still contains an integer, computed by clipping and merging the
    /// removals over the (possibly unbounded) positive range.
    /// </summary>
    /// <param name="positiveLow">The positive lower bound, or <c>null</c> for −∞.</param>
    /// <param name="positiveHigh">The positive upper bound, or <c>null</c> for +∞.</param>
    /// <param name="removals">The integer-footprint removals.</param>
    /// <returns><see langword="true"/> when an integer survives.</returns>
    private static bool IntegerResidueNonEmpty(BigInteger? positiveLow, BigInteger? positiveHigh, List<(BigInteger? Low, BigInteger? High)> removals)
    {
        List<(BigInteger? Low, BigInteger? High)> clipped = [];
        foreach((BigInteger? Low, BigInteger? High) removal in removals)
        {
            BigInteger? low = MaxBound(removal.Low, positiveLow);
            BigInteger? high = MinBound(removal.High, positiveHigh);
            if(low is BigInteger lowValue && high is BigInteger highValue && lowValue > highValue)
            {
                continue;
            }

            clipped.Add((low, high));
        }

        clipped.Sort(static (left, right) => CompareLowerBound(left.Low, right.Low));

        BigInteger? next = positiveLow;
        bool nextIsNegativeInfinity = positiveLow is null;
        foreach((BigInteger? Low, BigInteger? High) removal in clipped)
        {
            if(nextIsNegativeInfinity)
            {
                if(removal.Low is not null)
                {
                    //A first removal starting above −∞ leaves an uncovered integer below it.
                    return true;
                }

                if(removal.High is null)
                {
                    return false;
                }

                next = removal.High.Value + BigInteger.One;
                nextIsNegativeInfinity = false;
            }
            else
            {
                BigInteger frontier = next!.Value;
                if(removal.Low is BigInteger removalLow && removalLow > frontier)
                {
                    return true;
                }

                if(removal.High is null)
                {
                    return false;
                }

                BigInteger advanced = removal.High.Value + BigInteger.One;
                if(advanced > frontier)
                {
                    next = advanced;
                }
            }

            if(!nextIsNegativeInfinity && positiveHigh is BigInteger upperBound && next!.Value > upperBound)
            {
                return false;
            }
        }

        if(nextIsNegativeInfinity)
        {
            return true;
        }

        return positiveHigh is not BigInteger finalUpper || next!.Value <= finalUpper;
    }

    /// <summary>
    /// Decides whether a single value lies in an atom's value space: a three-valued
    /// membership test (in, out, or undetermined).
    /// </summary>
    /// <param name="atom">The atom.</param>
    /// <param name="candidate">The candidate literal.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <returns>The membership result.</returns>
    private static DatatypeMembership Membership(DataAtom atom, Literal candidate, DatatypeRegistry registry)
    {
        if(atom.OneOf is OwlDataOneOf oneOf)
        {
            return EnumerationMembership(oneOf.Literals, candidate, registry);
        }

        if(atom.Restriction is OwlDatatypeRestriction restriction)
        {
            DatatypeMembership baseMembership = DatatypeSpaceMembership(restriction.Datatype.Iri, candidate, registry);
            if(baseMembership != DatatypeMembership.In)
            {
                return baseMembership;
            }

            return FacetMembership(restriction.Restrictions, candidate);
        }

        if(atom.Datatype is NamedNode datatype)
        {
            return DatatypeSpaceMembership(datatype.Iri, candidate, registry);
        }

        return DatatypeMembership.Indeterminate;
    }

    /// <summary>Membership of a candidate in a named datatype's value space.</summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <param name="candidate">The candidate literal.</param>
    /// <param name="registry">The registered-datatype set: a registered target answers membership from its own value space where the family classifier abstains.</param>
    /// <returns>The membership result.</returns>
    private static DatatypeMembership DatatypeSpaceMembership(Utf8String datatypeIri, Literal candidate, DatatypeRegistry registry)
    {
        OwlDatatypeFamily targetFamily = OwlDatatypeFamilies.Classify(datatypeIri);
        if(targetFamily == OwlDatatypeFamily.Literal)
        {
            return DatatypeMembership.In;
        }

        if(targetFamily == OwlDatatypeFamily.Unknown && registry.TryGet(datatypeIri, out RegisteredDatatype? registered))
        {
            return registered.Contains(candidate);
        }

        OwlDatatypeFamily candidateFamily = candidate.Language is not null ? OwlDatatypeFamily.Text : OwlDatatypeFamilies.Classify(candidate.Datatype.Iri);
        if(targetFamily == OwlDatatypeFamily.Unknown || candidateFamily == OwlDatatypeFamily.Unknown)
        {
            return DatatypeMembership.Indeterminate;
        }

        if(targetFamily != candidateFamily)
        {
            return DatatypeMembership.Out;
        }

        if(targetFamily == OwlDatatypeFamily.Numeric)
        {
            return NumericMembership(datatypeIri, candidate);
        }

        //Within a non-numeric family the value is a member of its own datatype;
        //a different datatype of the same family depends on lexical validity the
        //checker does not model.
        if(candidate.Datatype.Iri.Equals(datatypeIri))
        {
            return DatatypeMembership.In;
        }

        return DatatypeMembership.Indeterminate;
    }

    /// <summary>Membership of a numeric candidate in a numeric datatype's value space.</summary>
    /// <param name="datatypeIri">The numeric datatype IRI.</param>
    /// <param name="candidate">The candidate literal.</param>
    /// <returns>The membership result.</returns>
    private static DatatypeMembership NumericMembership(Utf8String datatypeIri, Literal candidate)
    {
        OwlNumericSpace targetSpace = OwlDatatypeFamilies.NumericSpaceOf(datatypeIri);
        OwlNumericSpace candidateSpace = OwlDatatypeFamilies.NumericSpaceOf(candidate.Datatype.Iri);
        if(targetSpace != candidateSpace)
        {
            //The three numeric spaces are disjoint.
            return DatatypeMembership.Out;
        }

        if(targetSpace != OwlNumericSpace.ExactReal)
        {
            //Any float (double) literal is a value of xsd:float (xsd:double).
            return DatatypeMembership.In;
        }

        if(OwlNumericLexicals.TryGetFraction(candidate.Value.ToString(), candidate.Datatype.Iri, out ExactRational fraction))
        {
            return FractionMembership(datatypeIri, fraction);
        }

        if(!OwlNumericLexicals.TryGetValue(candidate.Value.ToString(), candidate.Datatype.Iri, out NumericValue value))
        {
            return DatatypeMembership.Indeterminate;
        }

        if(!OwlNumericRanges.TryGetRange(datatypeIri, out OwlNumericRange range))
        {
            return DatatypeMembership.Indeterminate;
        }

        return range.ContainsValue(value) ? DatatypeMembership.In : DatatypeMembership.Out;
    }

    /// <summary>
    /// Membership of an exact fraction in an exact-real datatype's value space,
    /// read off the value spaces themselves rather than off the interval
    /// algebra's shared continuum: <c>owl:real</c> and <c>owl:rational</c> hold
    /// every fraction, <c>xsd:decimal</c> exactly the terminating ones, and an
    /// integer-tower datatype exactly the whole numbers within its bounds.
    /// </summary>
    /// <param name="datatypeIri">The exact-real target datatype IRI.</param>
    /// <param name="fraction">The candidate's exact value.</param>
    /// <returns>The membership result.</returns>
    private static DatatypeMembership FractionMembership(Utf8String datatypeIri, ExactRational fraction)
    {
        RealLevel level = ExactIntervals.LevelOf(datatypeIri);
        if(level is RealLevel.Rational or RealLevel.Real)
        {
            return DatatypeMembership.In;
        }

        if(level == RealLevel.Decimal)
        {
            return fraction.HasTerminatingDecimalExpansion() ? DatatypeMembership.In : DatatypeMembership.Out;
        }

        if(!fraction.Denominator.IsOne)
        {
            //A non-integral value is outside every datatype of the integer tower.
            return DatatypeMembership.Out;
        }

        if(!OwlNumericRanges.TryGetRange(datatypeIri, out OwlNumericRange range))
        {
            return DatatypeMembership.Indeterminate;
        }

        return range.ContainsValue(new NumericValue(fraction.Numerator)) ? DatatypeMembership.In : DatatypeMembership.Out;
    }

    /// <summary>Whether a candidate satisfies every ordered bound facet of a restriction, over the numeric or temporal value space the candidate and bound share.</summary>
    /// <param name="facets">The facet restrictions.</param>
    /// <param name="candidate">The candidate literal.</param>
    /// <returns>The membership result over the facets.</returns>
    private static DatatypeMembership FacetMembership(IReadOnlyList<OwlFacetRestriction> facets, Literal candidate)
    {
        bool indeterminate = false;
        foreach(OwlFacetRestriction facet in facets)
        {
            Utf8String facetIri = facet.Facet.Iri;
            if(facetIri.Equals(Vocabulary.XsdFacets.Pattern))
            {
                DatatypeMembership patternMembership = PatternFacetMembership(facet.Value, candidate);
                if(patternMembership == DatatypeMembership.Out)
                {
                    return DatatypeMembership.Out;
                }

                indeterminate |= patternMembership == DatatypeMembership.Indeterminate;

                continue;
            }

            bool isLower = facetIri.Equals(Vocabulary.XsdFacets.MinInclusive) || facetIri.Equals(Vocabulary.XsdFacets.MinExclusive);
            bool isUpper = facetIri.Equals(Vocabulary.XsdFacets.MaxInclusive) || facetIri.Equals(Vocabulary.XsdFacets.MaxExclusive);
            if(!isLower && !isUpper)
            {
                indeterminate = true;

                continue;
            }

            bool inclusive = facetIri.Equals(Vocabulary.XsdFacets.MinInclusive) || facetIri.Equals(Vocabulary.XsdFacets.MaxInclusive);
            DatatypeMembership bound = BoundMembership(candidate, facet.Value, isLower, inclusive);
            if(bound == DatatypeMembership.Out)
            {
                return DatatypeMembership.Out;
            }

            indeterminate |= bound == DatatypeMembership.Indeterminate;
        }

        return indeterminate ? DatatypeMembership.Indeterminate : DatatypeMembership.In;
    }

    /// <summary>
    /// Whether a candidate's lexical form is a member of an <c>xsd:pattern</c> facet's language, decided by
    /// compiling the XSD-dialect pattern to a table automaton and walking the candidate's runes. A pattern
    /// that will not compile or crosses the automaton budget leaves membership Indeterminate.
    /// </summary>
    /// <param name="pattern">The pattern facet's value literal.</param>
    /// <param name="candidate">The candidate literal.</param>
    /// <returns>In when the lexical form matches, Out when it does not, Indeterminate when the pattern is not modelled.</returns>
    private static DatatypeMembership PatternFacetMembership(Literal pattern, Literal candidate)
    {
        PatternCompileResult compiled = XsdPatternCompiler.Compile(pattern.Value.Span, AutomatonBudgets.Default);
        if(compiled.Status != PatternCompileStatus.Compiled)
        {
            return DatatypeMembership.Indeterminate;
        }

        return compiled.Automaton!.Accepts(DatatypeLexical.CodePoints(candidate.Value)) ? DatatypeMembership.In : DatatypeMembership.Out;
    }

    /// <summary>Whether a candidate satisfies a single ordered bound facet, dispatching to the exact-real or temporal value space the candidate and bound share.</summary>
    /// <param name="candidate">The candidate literal.</param>
    /// <param name="bound">The facet bound literal.</param>
    /// <param name="isLower">Whether the facet is a lower bound.</param>
    /// <param name="inclusive">Whether the bound is inclusive.</param>
    /// <returns>In when satisfied, Out when violated, Indeterminate when the value space is not modelled.</returns>
    private static DatatypeMembership BoundMembership(Literal candidate, Literal bound, bool isLower, bool inclusive)
    {
        if(OwlDatatypeFamilies.NumericSpaceOf(candidate.Datatype.Iri) == OwlNumericSpace.ExactReal
            && OwlDatatypeFamilies.NumericSpaceOf(bound.Datatype.Iri) == OwlNumericSpace.ExactReal)
        {
            if(OwlNumericLexicals.TryGetFraction(candidate.Value.ToString(), candidate.Datatype.Iri, out ExactRational candidateFraction)
                && OwlNumericLexicals.TryGetFraction(bound.Value.ToString(), bound.Datatype.Iri, out ExactRational boundFraction))
            {
                return BoundResult(ExactRational.Compare(candidateFraction, boundFraction), isLower, inclusive);
            }

            return OwlNumericLexicals.TryGetValue(candidate.Value.ToString(), candidate.Datatype.Iri, out NumericValue value)
                && OwlNumericLexicals.TryGetValue(bound.Value.ToString(), bound.Datatype.Iri, out NumericValue boundValue)
                ? BoundResult(NumericValue.Compare(value, boundValue), isLower, inclusive)
                : DatatypeMembership.Indeterminate;
        }

        if(OwlDatatypeFamilies.Classify(candidate.Datatype.Iri) == OwlDatatypeFamily.Temporal
            && OwlDatatypeFamilies.Classify(bound.Datatype.Iri) == OwlDatatypeFamily.Temporal
            && TryParseTemporal(candidate, out DateTimeValue temporalValue, out TemporalKind valueKind)
            && TryParseTemporal(bound, out DateTimeValue temporalBound, out TemporalKind boundKind)
            && valueKind == boundKind)
        {
            return BoundResult(DateTimeValue.Compare(temporalValue, temporalBound), isLower, inclusive);
        }

        return DatatypeMembership.Indeterminate;
    }

    /// <summary>Resolves an ordered bound facet from the comparison of the value to the bound: In when satisfied, Out when violated, Indeterminate when the comparison itself is indeterminate.</summary>
    /// <param name="comparison">The comparison of the value to the bound.</param>
    /// <param name="isLower">Whether the facet is a lower bound.</param>
    /// <param name="inclusive">Whether the bound is inclusive.</param>
    /// <returns>The membership result.</returns>
    private static DatatypeMembership BoundResult(ComparisonResult comparison, bool isLower, bool inclusive)
    {
        if(comparison == ComparisonResult.Incomparable)
        {
            return DatatypeMembership.Indeterminate;
        }

        return SatisfiesBound(comparison, isLower, inclusive) ? DatatypeMembership.In : DatatypeMembership.Out;
    }

    /// <summary>Whether a value-to-bound comparison satisfies an ordered bound facet.</summary>
    /// <param name="comparison">The comparison of the value to the bound.</param>
    /// <param name="isLower">Whether the facet is a lower bound.</param>
    /// <param name="inclusive">Whether the bound is inclusive.</param>
    /// <returns><see langword="true"/> when the comparison satisfies the bound.</returns>
    private static bool SatisfiesBound(ComparisonResult comparison, bool isLower, bool inclusive)
    {
        return (isLower, inclusive) switch
        {
            (true, true) => comparison is ComparisonResult.Greater or ComparisonResult.Equal,
            (true, false) => comparison == ComparisonResult.Greater,
            (false, true) => comparison is ComparisonResult.Less or ComparisonResult.Equal,
            (false, false) => comparison == ComparisonResult.Less
        };
    }

    /// <summary>Membership of a candidate in an enumeration: in when equal to some member, out when distinct from all, undetermined otherwise.</summary>
    /// <param name="members">The enumerated members.</param>
    /// <param name="candidate">The candidate literal.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <returns>The membership result.</returns>
    private static DatatypeMembership EnumerationMembership(IReadOnlyList<Literal> members, Literal candidate, DatatypeRegistry registry)
    {
        bool allDistinct = true;
        foreach(Literal member in members)
        {
            DatatypeValueIdentity identity = SameDataValue(candidate, member, registry);
            if(identity == DatatypeValueIdentity.Same)
            {
                return DatatypeMembership.In;
            }

            if(identity != DatatypeValueIdentity.Distinct)
            {
                allDistinct = false;
            }
        }

        return allDistinct ? DatatypeMembership.Out : DatatypeMembership.Indeterminate;
    }

    /// <summary>
    /// The three-valued value identity of two literals — the checker's own value
    /// comparator exposed for the data-property disjointness rule, which must
    /// decide whether two point demands across a disjoint pair force the same value.
    /// </summary>
    /// <param name="first">The first literal.</param>
    /// <param name="second">The second literal.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains; <see cref="DatatypeRegistry.Empty"/> for no registration.</param>
    /// <returns>The identity result.</returns>
    internal static DatatypeValueIdentity CompareValues(Literal first, Literal second, DatatypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return SameDataValue(first, second, registry);
    }

    /// <summary>
    /// The three-valued value identity of two literals: the same data value,
    /// distinct data values, or undetermined.
    /// </summary>
    /// <param name="first">The first literal.</param>
    /// <param name="second">The second literal.</param>
    /// <param name="registry">The registered-datatype set: two literals of the same registered datatype answer identity from its own value space.</param>
    /// <returns>The identity result.</returns>
    private static DatatypeValueIdentity SameDataValue(Literal first, Literal second, DatatypeRegistry registry)
    {
        if(first.Language is not null || second.Language is not null)
        {
            return LanguageTaggedIdentity(first, second);
        }

        OwlDatatypeFamily firstFamily = OwlDatatypeFamilies.Classify(first.Datatype.Iri);
        OwlDatatypeFamily secondFamily = OwlDatatypeFamilies.Classify(second.Datatype.Iri);
        if(firstFamily == OwlDatatypeFamily.Unknown || secondFamily == OwlDatatypeFamily.Unknown)
        {
            if(first.Datatype.Iri.Equals(second.Datatype.Iri) && registry.TryGet(first.Datatype.Iri, out RegisteredDatatype? registered))
            {
                return registered.SameValue(first, second);
            }

            return first.Datatype.Iri.Equals(second.Datatype.Iri) && first.Value.Equals(second.Value)
                ? DatatypeValueIdentity.Same
                : DatatypeValueIdentity.Indeterminate;
        }

        if(firstFamily != secondFamily)
        {
            return DatatypeValueIdentity.Distinct;
        }

        return firstFamily switch
        {
            OwlDatatypeFamily.Numeric => NumericIdentity(first, second),
            OwlDatatypeFamily.Boolean => BooleanIdentity(first, second),
            OwlDatatypeFamily.Text => TextIdentity(first, second),
            OwlDatatypeFamily.XmlLiteral => XmlLiteralValues.Compare(first.Value.Span, second.Value.Span),
            _ => first.Datatype.Iri.Equals(second.Datatype.Iri) && first.Value.Equals(second.Value)
                ? DatatypeValueIdentity.Same
                : DatatypeValueIdentity.Indeterminate
        };
    }

    /// <summary>
    /// The value identity of two text-family literals. Two <c>xsd:string</c>
    /// literals answer decisively either way: the <c>xsd:string</c>
    /// lexical-to-value mapping is the identity function, so equal lexical
    /// forms denote one value and differing forms denote distinct values
    /// (UTF-8 is injective, so byte inequality is codepoint inequality). The
    /// whitespace-normalizing string restrictions and cross-type pairs stay
    /// three-valued: same type and lexical form is the same value, anything
    /// else is indeterminate — differing lexical forms of a
    /// whitespace-collapsing type can denote one value, and two types with
    /// overlapping value spaces can share a value under differing IRIs.
    /// </summary>
    /// <param name="first">The first text literal.</param>
    /// <param name="second">The second text literal.</param>
    /// <returns>The identity result.</returns>
    private static DatatypeValueIdentity TextIdentity(Literal first, Literal second)
    {
        if(first.Datatype.Iri.Equals(second.Datatype.Iri) && first.Value.Equals(second.Value))
        {
            return DatatypeValueIdentity.Same;
        }

        if(first.Datatype.Iri.Equals(Vocabulary.Xsd.String) && second.Datatype.Iri.Equals(Vocabulary.Xsd.String))
        {
            return DatatypeValueIdentity.Distinct;
        }

        return DatatypeValueIdentity.Indeterminate;
    }

    /// <summary>The value identity of two literals at least one of which is language-tagged.</summary>
    /// <param name="first">The first literal.</param>
    /// <param name="second">The second literal.</param>
    /// <returns>The identity result.</returns>
    private static DatatypeValueIdentity LanguageTaggedIdentity(Literal first, Literal second)
    {
        if(first.Language is not Utf8String firstLanguage || second.Language is not Utf8String secondLanguage)
        {
            //A language-tagged value and a plain value occupy disjoint spaces.
            return DatatypeValueIdentity.Distinct;
        }

        bool sameLanguage = firstLanguage.ToString().Equals(secondLanguage.ToString(), StringComparison.OrdinalIgnoreCase);

        return sameLanguage && first.Value.Equals(second.Value) ? DatatypeValueIdentity.Same : DatatypeValueIdentity.Distinct;
    }

    /// <summary>The value identity of two numeric literals: an exact-real pair whose lexical forms both yield fractions is settled by exact comparison, and the floating spaces keep their signed zeros apart.</summary>
    /// <param name="first">The first numeric literal.</param>
    /// <param name="second">The second numeric literal.</param>
    /// <returns>The identity result.</returns>
    private static DatatypeValueIdentity NumericIdentity(Literal first, Literal second)
    {
        OwlNumericSpace firstSpace = OwlDatatypeFamilies.NumericSpaceOf(first.Datatype.Iri);
        OwlNumericSpace secondSpace = OwlDatatypeFamilies.NumericSpaceOf(second.Datatype.Iri);
        if(firstSpace != secondSpace)
        {
            return DatatypeValueIdentity.Distinct;
        }

        if(firstSpace == OwlNumericSpace.ExactReal
            && OwlNumericLexicals.TryGetFraction(first.Value.ToString(), first.Datatype.Iri, out ExactRational firstFraction)
            && OwlNumericLexicals.TryGetFraction(second.Value.ToString(), second.Datatype.Iri, out ExactRational secondFraction))
        {
            //The exact-real line is totally ordered and carries neither a signed
            //zero nor a NaN, so an exact fraction comparison settles identity
            //either way — including for the non-terminating rationals the
            //NumericValue route leaves unparsed.
            return ExactRational.Compare(firstFraction, secondFraction) == ComparisonResult.Equal
                ? DatatypeValueIdentity.Same
                : DatatypeValueIdentity.Distinct;
        }

        if(!OwlNumericLexicals.TryGetValue(first.Value.ToString(), first.Datatype.Iri, out NumericValue firstValue)
            || !OwlNumericLexicals.TryGetValue(second.Value.ToString(), second.Datatype.Iri, out NumericValue secondValue))
        {
            return DatatypeValueIdentity.Indeterminate;
        }

        ComparisonResult comparison = NumericValue.Compare(firstValue, secondValue);
        if(comparison is ComparisonResult.Less or ComparisonResult.Greater)
        {
            return DatatypeValueIdentity.Distinct;
        }

        if(comparison == ComparisonResult.Incomparable)
        {
            return DatatypeValueIdentity.Indeterminate;
        }

        //Equal under numeric comparison; the float and double spaces still
        //separate +0 from −0.
        if(IsSignedZero(firstValue, out bool firstNegative) && IsSignedZero(secondValue, out bool secondNegative))
        {
            return firstNegative == secondNegative ? DatatypeValueIdentity.Same : DatatypeValueIdentity.Distinct;
        }

        return DatatypeValueIdentity.Same;
    }

    /// <summary>The value identity of two boolean literals.</summary>
    /// <param name="first">The first boolean literal.</param>
    /// <param name="second">The second boolean literal.</param>
    /// <returns>The identity result.</returns>
    private static DatatypeValueIdentity BooleanIdentity(Literal first, Literal second)
    {
        if(BooleanValue(first) is not bool firstValue || BooleanValue(second) is not bool secondValue)
        {
            return DatatypeValueIdentity.Indeterminate;
        }

        return firstValue == secondValue ? DatatypeValueIdentity.Same : DatatypeValueIdentity.Distinct;
    }

    /// <summary>Parses a boolean literal's lexical form.</summary>
    /// <param name="literal">The boolean literal.</param>
    /// <returns>The boolean value, or <c>null</c> when the lexical form is not a boolean.</returns>
    private static bool? BooleanValue(Literal literal)
    {
        return literal.Value.ToString() switch
        {
            "true" or "1" => true,
            "false" or "0" => false,
            _ => null
        };
    }

    /// <summary>Whether a value is a signed zero of the float or double space, reporting its sign.</summary>
    /// <param name="value">The value.</param>
    /// <param name="negative">Whether the zero is negative, when it is a signed zero.</param>
    /// <returns><see langword="true"/> when the value is a floating zero.</returns>
    private static bool IsSignedZero(NumericValue value, out bool negative)
    {
        if(value.Kind == NumericKind.Float && value.AsFloat() == 0f)
        {
            negative = float.IsNegative(value.AsFloat());

            return true;
        }

        if(value.Kind == NumericKind.Double && value.AsDouble() == 0d)
        {
            negative = double.IsNegative(value.AsDouble());

            return true;
        }

        negative = false;

        return false;
    }

    /// <summary>
    /// Decides a temporal-family disjunct over an ordered XSD date/time value
    /// space: it intersects the positive facet bounds into one interval and
    /// reports its emptiness. The order is exact over fully-timezoned values (a
    /// total order, XSD 1.1 Part 2 dateTime §3.3.7.1, duration §3.3.6.1); a bound whose comparison is
    /// indeterminate — a timezoned bound against an untimezoned one inside the
    /// ±14-hour window — or a facet the procedure does not model leaves the
    /// disjunct <see cref="DatatypeSatisfiability.Unknown"/>. Distinct temporal
    /// datatypes (date vs time vs dateTime) on one value and any negated
    /// temporal atom are not modelled and abstain.
    /// </summary>
    /// <param name="positives">The positive temporal atoms.</param>
    /// <param name="negatives">The negated atoms.</param>
    /// <returns>The verdict.</returns>
    private static DatatypeSatisfiability DecideTemporal(List<DataAtom> positives, IReadOnlyList<DataAtom> negatives)
    {
        List<TemporalBound> lowers = [];
        List<TemporalBound> uppers = [];
        TemporalKind kind = TemporalKind.None;
        bool blocked = false;
        foreach(DataAtom atom in positives)
        {
            if(!TryCollectTemporalBounds(atom, ref kind, lowers, uppers))
            {
                blocked = true;
            }
        }

        foreach(DataAtom negated in negatives)
        {
            //A negation on the same temporal line (a temporal datatype or an
            //enumeration that might hold a temporal point) blocks a witness; a
            //clearly disjoint-family datatype removes nothing.
            if(negated.BaseDatatype() is not NamedNode datatype || OwlDatatypeFamilies.Classify(datatype.Iri) is OwlDatatypeFamily.Temporal or OwlDatatypeFamily.Unknown)
            {
                blocked = true;
            }
        }

        if(!TryTightestBound(lowers, lower: true, out TemporalBound low, out bool hasLow)
            || !TryTightestBound(uppers, lower: false, out TemporalBound high, out bool hasHigh))
        {
            //Two same-side bounds whose order is indeterminate cannot be reduced.
            return DatatypeSatisfiability.Unknown;
        }

        if(hasLow && hasHigh)
        {
            ComparisonResult comparison = DateTimeValue.Compare(low.Value, high.Value);
            if(comparison == ComparisonResult.Incomparable)
            {
                return DatatypeSatisfiability.Unknown;
            }

            if(comparison == ComparisonResult.Greater || (comparison == ComparisonResult.Equal && !(low.Inclusive && high.Inclusive)))
            {
                return DatatypeSatisfiability.Unsatisfiable;
            }

            //xsd:date is a discrete day grid, not a dense line: a strict open
            //interval (both bounds exclusive) can be empty even when low < high —
            //consecutive days have no date strictly between them. Deciding that
            //precisely needs day-snapping under the ±14h timezone band, so abstain
            //rather than claim the dense Satisfiable. An inclusive endpoint is
            //itself a witnessing date, so an interval with one stays decisive.
            if(kind == TemporalKind.Date && comparison == ComparisonResult.Less && !low.Inclusive && !high.Inclusive)
            {
                return DatatypeSatisfiability.Unknown;
            }
        }

        return blocked ? DatatypeSatisfiability.Unknown : DatatypeSatisfiability.Satisfiable;
    }

    /// <summary>
    /// Collects a positive temporal atom's facet bounds onto the working
    /// lower/upper lists and pins the disjunct's temporal datatype, reporting
    /// failure for a mixed temporal datatype, an unmodelled facet, or a bound
    /// value that does not parse.
    /// </summary>
    /// <param name="atom">The positive temporal atom.</param>
    /// <param name="kind">The disjunct's temporal datatype kind, pinned on first contact.</param>
    /// <param name="lowersToAppendTo">The lower bounds collected so far, appended to.</param>
    /// <param name="uppersToAppendTo">The upper bounds collected so far, appended to.</param>
    /// <returns><see langword="true"/> when the atom is a fully modelled temporal constraint.</returns>
    private static bool TryCollectTemporalBounds(DataAtom atom, ref TemporalKind kind, List<TemporalBound> lowersToAppendTo, List<TemporalBound> uppersToAppendTo)
    {
        if(atom.BaseDatatype() is not NamedNode datatype)
        {
            return false;
        }

        TemporalKind atomKind = TemporalKindOf(datatype.Iri);
        if(atomKind == TemporalKind.None)
        {
            return false;
        }

        if(kind == TemporalKind.None)
        {
            kind = atomKind;
        }
        else if(kind != atomKind)
        {
            return false;
        }

        if(atom.Restriction is not OwlDatatypeRestriction restriction)
        {
            return true;
        }

        foreach(OwlFacetRestriction facet in restriction.Restrictions)
        {
            Utf8String facetIri = facet.Facet.Iri;
            bool isLower = facetIri.Equals(Vocabulary.XsdFacets.MinInclusive) || facetIri.Equals(Vocabulary.XsdFacets.MinExclusive);
            bool isUpper = facetIri.Equals(Vocabulary.XsdFacets.MaxInclusive) || facetIri.Equals(Vocabulary.XsdFacets.MaxExclusive);
            if((!isLower && !isUpper) || !TryParseTemporal(facet.Value, out DateTimeValue value, out TemporalKind boundKind) || boundKind != atomKind)
            {
                return false;
            }

            bool inclusive = facetIri.Equals(Vocabulary.XsdFacets.MinInclusive) || facetIri.Equals(Vocabulary.XsdFacets.MaxInclusive);
            (isLower ? lowersToAppendTo : uppersToAppendTo).Add(new TemporalBound(value, inclusive));
        }

        return true;
    }

    /// <summary>
    /// Reduces a list of same-side temporal bounds to the tightest one — the
    /// greatest lower bound or the least upper bound — reporting failure when
    /// two bounds are order-indeterminate and so cannot be reduced.
    /// </summary>
    /// <param name="bounds">The bounds to reduce.</param>
    /// <param name="lower">Whether these are lower bounds (the tightest is the greatest) rather than upper bounds (the least).</param>
    /// <param name="tightest">The tightest bound, when there is one.</param>
    /// <param name="hasBound">Whether any bound was present.</param>
    /// <returns><see langword="true"/> when every pair was comparable.</returns>
    private static bool TryTightestBound(List<TemporalBound> bounds, bool lower, out TemporalBound tightest, out bool hasBound)
    {
        tightest = default;
        hasBound = false;
        foreach(TemporalBound bound in bounds)
        {
            if(!hasBound)
            {
                tightest = bound;
                hasBound = true;

                continue;
            }

            ComparisonResult comparison = DateTimeValue.Compare(bound.Value, tightest.Value);
            if(comparison == ComparisonResult.Incomparable)
            {
                return false;
            }

            bool tighter = comparison == (lower ? ComparisonResult.Greater : ComparisonResult.Less)
                || (comparison == ComparisonResult.Equal && !bound.Inclusive);
            if(tighter)
            {
                tightest = bound;
            }
        }

        return true;
    }

    /// <summary>Parses a temporal literal into a comparable value and its datatype kind, reporting failure for a non-temporal or malformed literal.</summary>
    /// <param name="literal">The temporal literal.</param>
    /// <param name="value">The parsed value, on success.</param>
    /// <param name="kind">The temporal datatype kind, on success.</param>
    /// <returns><see langword="true"/> when the literal parsed.</returns>
    private static bool TryParseTemporal(Literal literal, out DateTimeValue value, out TemporalKind kind)
    {
        value = default;
        kind = TemporalKindOf(literal.Datatype.Iri);

        return kind switch
        {
            TemporalKind.DateTime => DateTimeValue.TryParseDateTime(literal.Value.Span, requireTimezone: literal.Datatype.Iri.Equals(Vocabulary.Xsd.DateTimeStamp), out value),
            TemporalKind.Date => DateTimeValue.TryParseDate(literal.Value.Span, out value),
            TemporalKind.Time => DateTimeValue.TryParseTime(literal.Value.Span, out value),
            _ => false
        };
    }

    /// <summary>The temporal datatype kind of an IRI; <c>xsd:dateTime</c> and <c>xsd:dateTimeStamp</c> share one value space.</summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <returns>The kind, or <see cref="TemporalKind.None"/> when the IRI is not a modelled temporal datatype.</returns>
    private static TemporalKind TemporalKindOf(Utf8String datatypeIri)
    {
        if(datatypeIri.Equals(Vocabulary.Xsd.DateTime) || datatypeIri.Equals(Vocabulary.Xsd.DateTimeStamp))
        {
            return TemporalKind.DateTime;
        }

        if(datatypeIri.Equals(Vocabulary.Xsd.Date))
        {
            return TemporalKind.Date;
        }

        if(datatypeIri.Equals(Vocabulary.Xsd.Time))
        {
            return TemporalKind.Time;
        }

        return TemporalKind.None;
    }

    /// <summary>
    /// Builds the disjunctive normal form of a data range by an iterative
    /// post-order fold over the AST — leaves become single-atom disjuncts, an
    /// intersection a cross product, a union a concatenation, and a complement
    /// the De Morgan dual.
    /// </summary>
    /// <param name="root">The data range.</param>
    /// <param name="tooComplex">Whether the form exceeded the size bound, in which case the result is incomplete and the caller abstains.</param>
    /// <returns>The disjuncts.</returns>
    private static List<Disjunct> BuildDisjunctiveNormalForm(OwlDataRange root, out bool tooComplex)
    {
        bool exceeded = false;
        Stack<(OwlDataRange Node, bool Expanded)> work = new();
        Stack<List<Disjunct>> results = new();
        work.Push((root, false));
        while(work.Count > 0)
        {
            (OwlDataRange node, bool expanded) = work.Pop();
            IReadOnlyList<OwlDataRange> children = ChildrenOf(node);
            if(children.Count == 0)
            {
                results.Push([LeafDisjunct(node)]);

                continue;
            }

            if(!expanded)
            {
                work.Push((node, true));
                foreach(OwlDataRange child in children)
                {
                    work.Push((child, false));
                }

                continue;
            }

            List<List<Disjunct>> childForms = new(children.Count);
            for(int index = 0; index < children.Count; index++)
            {
                childForms.Add(results.Pop());
            }

            results.Push(Combine(node, childForms, ref exceeded));
            if(exceeded)
            {
                tooComplex = true;

                return [];
            }
        }

        tooComplex = false;

        return results.Pop();
    }

    /// <summary>The child data ranges of a constructor node, or an empty list for a leaf.</summary>
    /// <param name="node">The data range.</param>
    /// <returns>The children.</returns>
    private static IReadOnlyList<OwlDataRange> ChildrenOf(OwlDataRange node)
    {
        return node switch
        {
            OwlDataIntersectionOf intersection => intersection.Ranges,
            OwlDataUnionOf union => union.Ranges,
            OwlDataComplementOf complement => [complement.Range],
            _ => []
        };
    }

    /// <summary>The single-atom disjunct of a leaf data range (datatype, enumeration, or datatype restriction).</summary>
    /// <param name="node">The leaf data range.</param>
    /// <returns>The disjunct.</returns>
    private static Disjunct LeafDisjunct(OwlDataRange node)
    {
        DataAtom atom = node switch
        {
            OwlDatatypeReference reference => new DataAtom(reference.Datatype, null, null),
            OwlDatatypeRestriction restriction => new DataAtom(null, restriction, null),
            OwlDataOneOf oneOf => new DataAtom(null, null, oneOf),
            _ => new DataAtom(null, null, null)
        };

        return new Disjunct([atom], []);
    }

    /// <summary>Combines child disjunctive normal forms under a constructor node.</summary>
    /// <param name="node">The constructor node.</param>
    /// <param name="childForms">The children's forms, in declaration order.</param>
    /// <param name="exceeded">Set when the combined form exceeds the size bound.</param>
    /// <returns>The combined form.</returns>
    private static List<Disjunct> Combine(OwlDataRange node, List<List<Disjunct>> childForms, ref bool exceeded)
    {
        return node switch
        {
            OwlDataIntersectionOf => Intersect(childForms, ref exceeded),
            OwlDataUnionOf => Union(childForms),
            OwlDataComplementOf => Complement(childForms[0], ref exceeded),
            _ => []
        };
    }

    /// <summary>The cross product of child forms — the disjunctive normal form of their conjunction.</summary>
    /// <param name="childForms">The child forms.</param>
    /// <param name="exceeded">Set when the product exceeds the size bound.</param>
    /// <returns>The product form.</returns>
    private static List<Disjunct> Intersect(List<List<Disjunct>> childForms, ref bool exceeded)
    {
        List<Disjunct> accumulator = [new Disjunct([], [])];
        foreach(List<Disjunct> childForm in childForms)
        {
            List<Disjunct> next = [];
            foreach(Disjunct left in accumulator)
            {
                foreach(Disjunct right in childForm)
                {
                    List<DataAtom> positives = [.. left.Positives, .. right.Positives];
                    List<DataAtom> negatives = [.. left.Negatives, .. right.Negatives];
                    next.Add(new Disjunct(positives, negatives));
                    if(next.Count > MaxDisjuncts)
                    {
                        exceeded = true;

                        return next;
                    }
                }
            }

            accumulator = next;
        }

        return accumulator;
    }

    /// <summary>The concatenation of child forms — the disjunctive normal form of their union.</summary>
    /// <param name="childForms">The child forms.</param>
    /// <returns>The union form.</returns>
    private static List<Disjunct> Union(List<List<Disjunct>> childForms)
    {
        List<Disjunct> union = [];
        foreach(List<Disjunct> childForm in childForms)
        {
            union.AddRange(childForm);
        }

        return union;
    }

    /// <summary>
    /// The complement of a disjunctive normal form: the conjunction of the
    /// per-disjunct complements, re-expanded into a product.
    /// </summary>
    /// <param name="form">The form to complement.</param>
    /// <param name="exceeded">Set when the expansion exceeds the size bound.</param>
    /// <returns>The complemented form.</returns>
    private static List<Disjunct> Complement(List<Disjunct> form, ref bool exceeded)
    {
        List<List<Disjunct>> perDisjunct = new(form.Count);
        foreach(Disjunct disjunct in form)
        {
            List<Disjunct> negatedAlternatives = [];
            foreach(DataAtom positive in disjunct.Positives)
            {
                negatedAlternatives.Add(new Disjunct([], [positive]));
            }

            foreach(DataAtom negated in disjunct.Negatives)
            {
                negatedAlternatives.Add(new Disjunct([negated], []));
            }

            perDisjunct.Add(negatedAlternatives);
        }

        return Intersect(perDisjunct, ref exceeded);
    }

    /// <summary>The level of a bare continuum datatype (<c>xsd:decimal</c>, <c>owl:rational</c>, <c>owl:real</c>), whose complement can empty a positive interval; <c>null</c> for the integer tower (handled as a footprint).</summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <returns>The level, or <c>null</c>.</returns>
    private static RealLevel? LevelOfBareContinuum(Utf8String datatypeIri)
    {
        if(datatypeIri.Equals(OwlVocabulary.Real))
        {
            return RealLevel.Real;
        }

        if(datatypeIri.Equals(OwlVocabulary.Rational))
        {
            return RealLevel.Rational;
        }

        if(datatypeIri.Equals(Vocabulary.Xsd.Decimal))
        {
            return RealLevel.Decimal;
        }

        return null;
    }

    /// <summary>Converts an exact-real value to a decimal, reporting failure when it does not fit.</summary>
    /// <param name="value">The value.</param>
    /// <param name="result">The decimal, on success.</param>
    /// <returns><see langword="true"/> when the value fits a decimal.</returns>
    private static bool TryToDecimal(NumericValue value, out decimal result)
    {
        if(value.Kind == NumericKind.Decimal)
        {
            result = value.AsDecimal();

            return true;
        }

        if(value.Kind == NumericKind.Integer)
        {
            BigInteger integer = value.AsInteger();
            if(integer >= new BigInteger(decimal.MinValue) && integer <= new BigInteger(decimal.MaxValue))
            {
                result = (decimal)integer;

                return true;
            }
        }

        result = 0m;

        return false;
    }

    /// <summary>The larger of two lower bounds, treating <c>null</c> as −∞.</summary>
    /// <param name="first">The first lower bound.</param>
    /// <param name="second">The second lower bound.</param>
    /// <returns>The larger bound.</returns>
    private static BigInteger? MaxBound(BigInteger? first, BigInteger? second)
    {
        return (first, second) switch
        {
            (null, _) => second,
            (_, null) => first,
            (BigInteger left, BigInteger right) => BigInteger.Max(left, right)
        };
    }

    /// <summary>The smaller of two upper bounds, treating <c>null</c> as +∞.</summary>
    /// <param name="first">The first upper bound.</param>
    /// <param name="second">The second upper bound.</param>
    /// <returns>The smaller bound.</returns>
    private static BigInteger? MinBound(BigInteger? first, BigInteger? second)
    {
        return (first, second) switch
        {
            (null, _) => second,
            (_, null) => first,
            (BigInteger left, BigInteger right) => BigInteger.Min(left, right)
        };
    }

    /// <summary>Orders two lower bounds for the merge sweep, treating <c>null</c> as −∞ (smallest).</summary>
    /// <param name="first">The first lower bound.</param>
    /// <param name="second">The second lower bound.</param>
    /// <returns>The sign of the comparison.</returns>
    private static int CompareLowerBound(BigInteger? first, BigInteger? second)
    {
        return (first, second) switch
        {
            (null, null) => 0,
            (null, _) => -1,
            (_, null) => 1,
            (BigInteger left, BigInteger right) => left.CompareTo(right)
        };
    }

    /// <summary>The two boolean candidate literals enumerated for the boolean family.</summary>
    private static IReadOnlyList<Literal> BooleanCandidates { get; } =
    [
        new Literal(Utf8Strings.From("true"), new NamedNode(Vocabulary.Xsd.Boolean)),
        new Literal(Utf8Strings.From("false"), new NamedNode(Vocabulary.Xsd.Boolean)),
    ];

    /// <summary>
    /// One atom of a disjunctive normal form: exactly one of a named datatype, a
    /// datatype restriction, or an enumeration.
    /// </summary>
    /// <param name="Datatype">The named datatype, when the atom is a datatype reference.</param>
    /// <param name="Restriction">The datatype restriction, when the atom is one.</param>
    /// <param name="OneOf">The enumeration, when the atom is one.</param>
    private readonly record struct DataAtom(NamedNode? Datatype, OwlDatatypeRestriction? Restriction, OwlDataOneOf? OneOf)
    {
        /// <summary>The base datatype node of a datatype or restriction atom; <c>null</c> for an enumeration.</summary>
        /// <returns>The base datatype node, or <c>null</c>.</returns>
        public NamedNode? BaseDatatype()
        {
            if(Datatype is NamedNode datatype)
            {
                return datatype;
            }

            return Restriction?.Datatype;
        }
    }

    /// <summary>One product disjunct: positive atoms that must hold and negated atoms that must not.</summary>
    /// <param name="Positives">The positive atoms.</param>
    /// <param name="Negatives">The negated atoms.</param>
    private sealed record Disjunct(List<DataAtom> Positives, List<DataAtom> Negatives);

    /// <summary>One endpoint of a temporal interval: a parsed date/time value and whether the endpoint is included.</summary>
    /// <param name="Value">The endpoint value.</param>
    /// <param name="Inclusive">Whether the endpoint is included.</param>
    private readonly record struct TemporalBound(DateTimeValue Value, bool Inclusive);

    /// <summary>The XSD date/time datatype whose value space a temporal constraint ranges over.</summary>
    private enum TemporalKind
    {
        /// <summary>No modelled temporal datatype.</summary>
        None,

        /// <summary>The <c>xsd:dateTime</c> value space (shared with <c>xsd:dateTimeStamp</c>).</summary>
        DateTime,

        /// <summary>The <c>xsd:date</c> value space.</summary>
        Date,

        /// <summary>The <c>xsd:time</c> value space.</summary>
        Time,
    }

    /// <summary>The status of a finite candidate against all atoms.</summary>
    private enum CandidateStatus
    {
        /// <summary>The candidate provably satisfies every positive and no negative atom.</summary>
        Admitted,

        /// <summary>Some atom provably rules the candidate out.</summary>
        Excluded,

        /// <summary>The candidate is neither provably admitted nor provably excluded.</summary>
        Undetermined,
    }

    /// <summary>The effect of a negated atom on an exact-real positive interval.</summary>
    private enum NegativeEffect
    {
        /// <summary>The negation removes nothing relevant from the positive interval.</summary>
        None,

        /// <summary>The negation removes every value of the positive interval.</summary>
        EmptiesAll,

        /// <summary>The negation cannot be pinned, so it blocks a satisfiability witness but is sound to ignore for an emptiness proof.</summary>
        BlocksWitness,
    }

    /// <summary>The residue of a positive interval after the negated removals.</summary>
    private enum Residue
    {
        /// <summary>A value provably survives.</summary>
        NonEmpty,

        /// <summary>No value survives.</summary>
        Empty,

        /// <summary>The residue could not be decided.</summary>
        Indeterminate,
    }
}
