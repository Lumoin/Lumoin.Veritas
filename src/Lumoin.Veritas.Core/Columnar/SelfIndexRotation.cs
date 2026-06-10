namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The cyclic rotation a <see cref="SelfIndexRange"/> lives in: which triple
/// position leads the conceptual sorted table the range addresses. The three
/// rotations chain cyclically — binding the position that precedes a
/// rotation's leader steps to the preceding rotation's table.
/// </summary>
public enum SelfIndexRotation
{
    /// <summary>The table sorted by subject, then predicate, then object.</summary>
    SubjectPredicateObject,

    /// <summary>The table sorted by object, then subject, then predicate.</summary>
    ObjectSubjectPredicate,

    /// <summary>The table sorted by predicate, then object, then subject.</summary>
    PredicateObjectSubject,
}
