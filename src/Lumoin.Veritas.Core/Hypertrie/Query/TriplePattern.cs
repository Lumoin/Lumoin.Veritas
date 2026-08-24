using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Query;

/// <summary>
/// A single triple pattern: three <see cref="PatternPosition"/>
/// values, one per RDF position (subject, predicate, object).
/// Each position is independently a bound term or a variable.
/// </summary>
/// <remarks>
/// <para>
/// A pattern is the building block of a
/// <see cref="BasicGraphPattern"/>. The query engine descends the
/// hypertrie following the pattern's bound positions and produces
/// bindings for the variable positions.
/// </para>
/// <para>
/// <b>Self-joins.</b> A pattern may legitimately contain the same
/// variable in multiple positions (for example,
/// <c>?x knows ?x</c>). The current iterator will reject this case
/// at construction time; a later batch lifts that restriction.
/// Patterns themselves do not validate self-joins — they are
/// pure data.
/// </para>
/// </remarks>
[DebuggerDisplay("({Subject}, {Predicate}, {Object})")]
public readonly record struct TriplePattern(
    PatternPosition Subject,
    PatternPosition Predicate,
    PatternPosition Object)
{
    /// <summary>
    /// Returns the position at the given RDF position index
    /// (0 = subject, 1 = predicate, 2 = object).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is outside [0, 2].</exception>
    public PatternPosition At(int position)
    {
        return position switch
        {
            0 => Subject,
            1 => Predicate,
            2 => Object,
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Position must be 0 (subject), 1 (predicate), or 2 (object)."),
        };
    }

    /// <summary>
    /// Enumerates the distinct variables appearing in this pattern,
    /// in subject-predicate-object order. A variable that appears
    /// in multiple positions is yielded once.
    /// </summary>
    public IEnumerable<Variable> Variables()
    {
        HashSet<Variable> seen = [];

        for(int i = 0; i < 3; i++)
        {
            PatternPosition position = At(i);

            if(position.IsVariable && seen.Add(position.Variable))
            {
                yield return position.Variable;
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the same variable appears in two or
    /// more positions of this pattern (a self-join).
    /// </summary>
    public bool HasSelfJoin()
    {
        bool subjectIsVar = Subject.IsVariable;
        bool predicateIsVar = Predicate.IsVariable;
        bool objectIsVar = Object.IsVariable;

        if(subjectIsVar && predicateIsVar && Subject.Variable == Predicate.Variable)
        {
            return true;
        }

        if(subjectIsVar && objectIsVar && Subject.Variable == Object.Variable)
        {
            return true;
        }

        if(predicateIsVar && objectIsVar && Predicate.Variable == Object.Variable)
        {
            return true;
        }

        return false;
    }
}
