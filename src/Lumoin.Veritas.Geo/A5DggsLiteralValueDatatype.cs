using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// The value-layer definition of the house <c>a5Literal</c> subclass datatype
/// (<see cref="A5DggsVocabulary.DatatypeIri"/>), declaring
/// <see cref="ValueDatatypeFacets.LexicalValidity"/> and <see cref="ValueDatatypeFacets.ValueEquality"/>.
/// The subclass names the specific DGGS implementation, so its whole grammar is certified: a lexical
/// form is valid when it is empty (the empty geometry) or carries exactly the house grid IRI in its
/// angle-bracket prefix and a conformant <see cref="A5DggsBody"/> cell body; every other form —
/// including a foreign grid IRI, whose data this datatype cannot certify but whose presence under the
/// implementation-naming subclass is itself the violation — is invalid.
/// <see cref="SameValue"/> decides by canonical cell-SET equality: duplicate tokens, token order,
/// token case, and leading zeros carry no meaning, and two forms are the same value exactly when
/// their deduplicated, sorted cell sequences are equal — the reading under which same-valued
/// literals materialize bit-identical geometry. A cell set is never collapsed through the grid
/// hierarchy, because child cells only approximately tile their parent.
/// </summary>
public sealed class A5DggsLiteralValueDatatype : ValueDatatype
{
    /// <summary>The shared definition instance; nothing registers it — a composing host does.</summary>
    public static A5DggsLiteralValueDatatype Instance { get; } = new();

    /// <summary>Only <see cref="Instance"/> exists.</summary>
    private A5DggsLiteralValueDatatype()
    {
    }

    /// <inheritdoc/>
    public override Utf8String DatatypeIri => A5DggsVocabulary.DatatypeIri;

    /// <inheritdoc/>
    public override ValueDatatypeFacets Facets => ValueDatatypeFacets.LexicalValidity | ValueDatatypeFacets.ValueEquality;

    /// <inheritdoc/>
    public override ValueLexicalValidity ValidateLexicalForm(Utf8String lexicalForm)
    {
        if(lexicalForm.Span.Length == 0)
        {
            return ValueLexicalValidity.Valid;
        }

        List<A5CellId> cells = [];

        return A5DggsBody.TryReadCanonicalCells(lexicalForm.Span, cells)
            ? ValueLexicalValidity.Valid
            : ValueLexicalValidity.Invalid;
    }

    /// <inheritdoc/>
    public override ValueIdentity SameValue(Utf8String first, Utf8String second)
    {
        List<A5CellId> firstCells = [];
        if(!A5DggsBody.TryReadCanonicalCells(first.Span, firstCells))
        {
            return ValueIdentity.Indeterminate;
        }

        List<A5CellId> secondCells = [];
        if(!A5DggsBody.TryReadCanonicalCells(second.Span, secondCells))
        {
            return ValueIdentity.Indeterminate;
        }

        if(firstCells.Count != secondCells.Count)
        {
            return ValueIdentity.Distinct;
        }

        for(int index = 0; index < firstCells.Count; index++)
        {
            if(firstCells[index].Value != secondCells[index].Value)
            {
                return ValueIdentity.Distinct;
            }
        }

        return ValueIdentity.Same;
    }
}
