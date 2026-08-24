using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>The source of a fresh concept-atom id the demand mint draws from when it mints a marker for a new canonical descriptor.</summary>
/// <returns>A fresh concept-atom id.</returns>
internal delegate int FreshAtomDelegate();

/// <summary>
/// The context clausifier's data-demand marker mint: it canonicalizes a demanded
/// data range, interns it structurally, and mints exactly one marker concept atom
/// per canonical descriptor. Its contract is the invariant "one marker atom per
/// canonical descriptor" — two demands with the same property, kind, count, and a
/// structurally identical (or facet-reordered, or degenerate-spelled) range share
/// one marker, so the descriptor side table the saturation engine reconstructs
/// obligations from carries canonical ranges.
/// </summary>
internal sealed class DataDemandMint
{
    /// <summary>The fresh-atom source a new canonical descriptor draws a marker from.</summary>
    private FreshAtomDelegate FreshAtom { get; }

    /// <summary>The canonical-range interning memo, keyed by structural equality, first instance wins — so a structurally identical range reaches the one interned instance the descriptor is keyed on.</summary>
    private Dictionary<OwlDataRange, OwlDataRange> InternedRanges { get; } = new(DataRangeStructuralComparer.Instance);

    /// <summary>The marker concept atom minted per canonical descriptor.</summary>
    private Dictionary<DataDemandDescriptor, int> MarkerByDescriptor { get; } = [];

    /// <summary>The descriptor per marker atom — the side table riding the clausification result.</summary>
    private Dictionary<int, DataDemandDescriptor> DescriptorByMarker { get; } = [];

    /// <summary>Initialises the mint against a fresh-atom source.</summary>
    /// <param name="freshAtom">The fresh concept-atom id source.</param>
    public DataDemandMint(FreshAtomDelegate freshAtom)
    {
        FreshAtom = freshAtom;
    }

    /// <summary>The descriptor side table, keyed by marker concept-atom id.</summary>
    public IReadOnlyDictionary<int, DataDemandDescriptor> Descriptors => DescriptorByMarker;

    /// <summary>
    /// The marker concept atom for a demand: the range is canonicalized, interned
    /// to its first structurally-equal instance, and keyed with the property, kind,
    /// and count into a descriptor whose marker is minted fresh on first contact
    /// and reused thereafter.
    /// </summary>
    /// <param name="property">The demanding data-property IRI.</param>
    /// <param name="kind">The demand kind.</param>
    /// <param name="count">The counting bound for a min- or max-cardinality demand, zero otherwise.</param>
    /// <param name="range">The demanded data range.</param>
    /// <returns>The marker concept-atom id.</returns>
    public int MarkerFor(Utf8String property, DataDemandKind kind, int count, OwlDataRange range)
    {
        OwlDataRange canonical = DataRangeCanonicalizer.Canonicalize(range);
        if(!InternedRanges.TryGetValue(canonical, out OwlDataRange? interned))
        {
            interned = canonical;
            InternedRanges[canonical] = interned;
        }

        DataDemandDescriptor descriptor = new(property, kind, count, interned);
        if(!MarkerByDescriptor.TryGetValue(descriptor, out int atom))
        {
            atom = FreshAtom();
            MarkerByDescriptor[descriptor] = atom;
            DescriptorByMarker[atom] = descriptor;
        }

        return atom;
    }
}
