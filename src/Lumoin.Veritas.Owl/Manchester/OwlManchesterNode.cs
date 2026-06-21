using System.Collections.Generic;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Owl.Manchester;

/// <summary>The bracket families a Manchester group node can carry.</summary>
internal enum OwlManchesterGroupKind
{
    /// <summary>A parenthesized expression group.</summary>
    Paren = 0,

    /// <summary>A braced one-of enumeration.</summary>
    Brace = 1,

    /// <summary>A bracketed facet list.</summary>
    Bracket = 2
}

/// <summary>
/// One node of the Manchester token tree: an atom token, or a bracketed group
/// whose children are the nodes between its delimiters.
/// </summary>
internal sealed class OwlManchesterNode
{
    /// <summary>The atom token for a leaf, default for a group.</summary>
    public OwlManchesterToken Atom { get; init; }

    /// <summary>Whether this node is an atom.</summary>
    public bool IsAtom { get; init; }

    /// <summary>The bracket family for a group node.</summary>
    public OwlManchesterGroupKind GroupKind { get; init; }

    /// <summary>The node's source extent: the atom's token, or the run from the opening to the closing delimiter.</summary>
    public SourceSpan Span { get; set; }

    /// <summary>The byte offset the group's span starts at, held until the closing delimiter fixes the end.</summary>
    public int SpanStart { get; init; }

    /// <summary>The group's children, in document order.</summary>
    public List<OwlManchesterNode> Children { get; } = [];
}
