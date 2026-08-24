using System;

namespace Lumoin.Veritas.Rdf.Values;

/// <summary>
/// The value-layer operations a <see cref="ValueDatatype"/> declares it answers. A definition must declare
/// at least one facet to register — a facet-less definition could never be consulted — and an operation
/// outside the declared set abstains, so the declaration is the definition's honest capability statement.
/// </summary>
[Flags]
public enum ValueDatatypeFacets
{
    /// <summary>No operation is declared; a registration with this declaration is rejected.</summary>
    None = 0,

    /// <summary>The definition answers <see cref="ValueDatatype.ValidateLexicalForm"/>.</summary>
    LexicalValidity = 1 << 0,

    /// <summary>The definition answers <see cref="ValueDatatype.SameValue"/>.</summary>
    ValueEquality = 1 << 1,
}
