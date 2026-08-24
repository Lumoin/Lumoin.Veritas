using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// Structural value equality over <see cref="OwlDataRange"/> trees: two ranges
/// are equal when their constructor shapes match and their operand lists are
/// equal ELEMENTWISE IN ORDER (order-sensitive), with <see cref="Literal"/> and
/// <see cref="NamedNode"/> leaves compared by their record value equality. It is
/// the semantic counterpart of the record types' default equality, whose
/// <see cref="IReadOnlyList{T}"/> fields compare by reference; the default record
/// equality of the range types stays untouched, so this comparer is the one the
/// demand mint keys its interning memo by.
/// </summary>
internal static class DataRangeEquality
{
    /// <summary>
    /// Whether two data ranges are structurally equal, walked iteratively over an
    /// explicit pair worklist. The walk is total: an <see cref="OwlDataRange"/> is
    /// a finite immutable tree (its nesting depth is bounded by the parser's
    /// uniform nesting cap), so there is no depth at which the walk cannot answer.
    /// </summary>
    /// <param name="first">The first range, or <see langword="null"/>.</param>
    /// <param name="second">The second range, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the two ranges are structurally equal.</returns>
    public static bool StructuralEquals(OwlDataRange? first, OwlDataRange? second)
    {
        Stack<(OwlDataRange? First, OwlDataRange? Second)> work = new();
        work.Push((first, second));
        while(work.Count > 0)
        {
            (OwlDataRange? left, OwlDataRange? right) = work.Pop();
            if(ReferenceEquals(left, right))
            {
                continue;
            }

            if(left is null || right is null || left.GetType() != right.GetType())
            {
                return false;
            }

            switch(left)
            {
                case OwlDatatypeReference reference when right is OwlDatatypeReference otherReference:
                {
                    if(!reference.Datatype.Equals(otherReference.Datatype))
                    {
                        return false;
                    }

                    break;
                }

                case OwlDataOneOf oneOf when right is OwlDataOneOf otherOneOf:
                {
                    if(!LiteralsEqual(oneOf.Literals, otherOneOf.Literals))
                    {
                        return false;
                    }

                    break;
                }

                case OwlDatatypeRestriction restriction when right is OwlDatatypeRestriction otherRestriction:
                {
                    if(!restriction.Datatype.Equals(otherRestriction.Datatype) || !FacetsEqual(restriction.Restrictions, otherRestriction.Restrictions))
                    {
                        return false;
                    }

                    break;
                }

                case OwlDataComplementOf complement when right is OwlDataComplementOf otherComplement:
                {
                    work.Push((complement.Range, otherComplement.Range));

                    break;
                }

                case OwlDataIntersectionOf intersection when right is OwlDataIntersectionOf otherIntersection:
                {
                    if(!PushChildren(intersection.Ranges, otherIntersection.Ranges, work))
                    {
                        return false;
                    }

                    break;
                }

                case OwlDataUnionOf union when right is OwlDataUnionOf otherUnion:
                {
                    if(!PushChildren(union.Ranges, otherUnion.Ranges, work))
                    {
                        return false;
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

    /// <summary>
    /// A structural hash of a data range, consistent with
    /// <see cref="StructuralEquals(OwlDataRange?, OwlDataRange?)"/>: equal ranges
    /// hash equal. Walked iteratively as a pre-order fold, combining a per-node
    /// type tag, arity, and leaf data in operand order.
    /// </summary>
    /// <param name="range">The data range.</param>
    /// <returns>The structural hash.</returns>
    public static int StructuralHash(OwlDataRange range)
    {
        int hash = 17;
        Stack<OwlDataRange> work = new();
        work.Push(range);
        while(work.Count > 0)
        {
            OwlDataRange node = work.Pop();
            switch(node)
            {
                case OwlDatatypeReference reference:
                {
                    Combine(ref hash, 1);
                    Combine(ref hash, reference.Datatype.GetHashCode());

                    break;
                }

                case OwlDataOneOf oneOf:
                {
                    Combine(ref hash, 2);
                    Combine(ref hash, oneOf.Literals.Count);
                    foreach(Literal literal in oneOf.Literals)
                    {
                        Combine(ref hash, literal.GetHashCode());
                    }

                    break;
                }

                case OwlDatatypeRestriction restriction:
                {
                    Combine(ref hash, 3);
                    Combine(ref hash, restriction.Datatype.GetHashCode());
                    Combine(ref hash, restriction.Restrictions.Count);
                    foreach(OwlFacetRestriction facet in restriction.Restrictions)
                    {
                        Combine(ref hash, facet.GetHashCode());
                    }

                    break;
                }

                case OwlDataComplementOf complement:
                {
                    Combine(ref hash, 4);
                    work.Push(complement.Range);

                    break;
                }

                case OwlDataIntersectionOf intersection:
                {
                    Combine(ref hash, 5);
                    Combine(ref hash, intersection.Ranges.Count);
                    for(int index = intersection.Ranges.Count - 1; index >= 0; index--)
                    {
                        work.Push(intersection.Ranges[index]);
                    }

                    break;
                }

                case OwlDataUnionOf union:
                {
                    Combine(ref hash, 6);
                    Combine(ref hash, union.Ranges.Count);
                    for(int index = union.Ranges.Count - 1; index >= 0; index--)
                    {
                        work.Push(union.Ranges[index]);
                    }

                    break;
                }

                default:
                {
                    Combine(ref hash, 0);

                    break;
                }
            }
        }

        return hash;
    }

    /// <summary>Whether two literal lists are equal elementwise in order.</summary>
    /// <param name="first">The first list.</param>
    /// <param name="second">The second list.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    private static bool LiteralsEqual(IReadOnlyList<Literal> first, IReadOnlyList<Literal> second)
    {
        if(first.Count != second.Count)
        {
            return false;
        }

        for(int index = 0; index < first.Count; index++)
        {
            if(!first[index].Equals(second[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether two facet lists are equal elementwise in order.</summary>
    /// <param name="first">The first list.</param>
    /// <param name="second">The second list.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    private static bool FacetsEqual(IReadOnlyList<OwlFacetRestriction> first, IReadOnlyList<OwlFacetRestriction> second)
    {
        if(first.Count != second.Count)
        {
            return false;
        }

        for(int index = 0; index < first.Count; index++)
        {
            if(!first[index].Equals(second[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Pushes the paired children of two operand lists onto the worklist, reporting a length mismatch.</summary>
    /// <param name="first">The first operand list.</param>
    /// <param name="second">The second operand list.</param>
    /// <param name="workToPushOnto">The pair worklist, pushed onto.</param>
    /// <returns><see langword="true"/> when the lists have equal length.</returns>
    private static bool PushChildren(IReadOnlyList<OwlDataRange> first, IReadOnlyList<OwlDataRange> second, Stack<(OwlDataRange? First, OwlDataRange? Second)> workToPushOnto)
    {
        if(first.Count != second.Count)
        {
            return false;
        }

        for(int index = 0; index < first.Count; index++)
        {
            workToPushOnto.Push((first[index], second[index]));
        }

        return true;
    }

    /// <summary>Folds a value into a running hash in an order-sensitive way.</summary>
    /// <param name="hash">The running hash.</param>
    /// <param name="value">The value to fold.</param>
    private static void Combine(ref int hash, int value)
    {
        unchecked
        {
            hash = (hash * 31) + value;
        }
    }
}

/// <summary>
/// The <see cref="IEqualityComparer{T}"/> exposing
/// <see cref="DataRangeEquality"/>'s structural equality and hash — the key
/// comparer the demand mint interns canonical ranges by.
/// </summary>
internal sealed class DataRangeStructuralComparer: IEqualityComparer<OwlDataRange>
{
    /// <summary>The shared comparer instance.</summary>
    public static DataRangeStructuralComparer Instance { get; } = new();

    /// <summary>Whether two data ranges are structurally equal.</summary>
    /// <param name="x">The first range.</param>
    /// <param name="y">The second range.</param>
    /// <returns><see langword="true"/> when structurally equal.</returns>
    public bool Equals(OwlDataRange? x, OwlDataRange? y)
    {
        return DataRangeEquality.StructuralEquals(x, y);
    }

    /// <summary>The structural hash of a data range.</summary>
    /// <param name="obj">The data range.</param>
    /// <returns>The structural hash.</returns>
    public int GetHashCode(OwlDataRange obj)
    {
        return obj is null ? 0 : DataRangeEquality.StructuralHash(obj);
    }
}
