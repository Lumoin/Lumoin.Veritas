using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Owl.Functional;

/// <summary>
/// One node of the functional-syntax constructor tree: a headed constructor
/// group, a bare parenthesis group, or an atom leaf.
/// </summary>
internal sealed class OwlFunctionalNode
{
    /// <summary>The constructor name for a headed group, <see langword="null"/> for a bare parenthesis group or an atom. A zero-copy window over the reader's byte buffer.</summary>
    public Utf8String? Head { get; init; }

    /// <summary>The atom token for a leaf, default for a group.</summary>
    public OwlFunctionalToken Atom { get; init; }

    /// <summary>Whether this node is an atom.</summary>
    public bool IsAtom { get; init; }

    /// <summary>The node's source extent: the atom's token, or the run from the group's head to its closing parenthesis.</summary>
    public SourceSpan Span { get; set; }

    /// <summary>The byte offset the group's span starts at, held until the closing parenthesis fixes the end.</summary>
    public int SpanStart { get; init; }

    /// <summary>The group's children, in document order.</summary>
    public List<OwlFunctionalNode> Children { get; } = [];
}
