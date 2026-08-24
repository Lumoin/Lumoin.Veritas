using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Structural;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// The context clausifier's data-demand marker mint: its contract is one marker
/// concept atom per canonical descriptor. These rows exercise the mint in
/// isolation — structurally identical ranges, facet-reordered ranges, and
/// degenerate-interval ranges reach one marker, while a different property or a
/// different range mints a fresh one.
/// </summary>
[TestClass]
internal sealed class DataDemandMintTests
{
    /// <summary>The example-namespace property IRIs the rows use.</summary>
    private static Utf8String PropertyD { get; } = Utf8Strings.From("http://example.org/d");

    /// <summary>A second example-namespace property IRI.</summary>
    private static Utf8String PropertyE { get; } = Utf8Strings.From("http://example.org/e");

    /// <summary>MRK-01: two syntactic occurrences of one structurally identical range mint one shared marker atom.</summary>
    [TestMethod]
    public void MRK01StructurallyIdenticalRangesShareOneMarker()
    {
        AtomCounter counter = new();
        DataDemandMint mint = new(counter.Next);

        int first = mint.MarkerFor(PropertyD, DataDemandKind.Existential, 0, IntegerAtLeast(5));
        int second = mint.MarkerFor(PropertyD, DataDemandKind.Existential, 0, IntegerAtLeast(5));

        Assert.AreEqual(first, second, "Two occurrences of one canonical descriptor share one marker.");
        Assert.HasCount(1, mint.Descriptors, "Only one descriptor is registered.");
    }

    /// <summary>MRK-02: a re-emitted demand for the same range reuses the marker, while a different range or property mints a fresh one.</summary>
    [TestMethod]
    public void MRK02DifferentRangeOrPropertyMintsFreshMarker()
    {
        AtomCounter counter = new();
        DataDemandMint mint = new(counter.Next);

        int baseline = mint.MarkerFor(PropertyD, DataDemandKind.Existential, 0, IntegerAtLeast(5));
        int reemitted = mint.MarkerFor(PropertyD, DataDemandKind.Existential, 0, IntegerAtLeast(5));
        int differentRange = mint.MarkerFor(PropertyD, DataDemandKind.Existential, 0, IntegerAtLeast(7));
        int differentProperty = mint.MarkerFor(PropertyE, DataDemandKind.Existential, 0, IntegerAtLeast(5));

        Assert.AreEqual(baseline, reemitted, "A re-emission of the same descriptor reuses the marker.");
        Assert.AreNotEqual(baseline, differentRange, "A structurally different range mints a fresh marker.");
        Assert.AreNotEqual(baseline, differentProperty, "A different property mints a fresh marker.");
        Assert.HasCount(3, mint.Descriptors, "Three distinct descriptors are registered.");
    }

    /// <summary>CAN-08 at the mint: two restrictions with the same facet set in different order intern to one marker (the facet sort recovers sharing).</summary>
    [TestMethod]
    public void MintSharesMarkerForFacetReorderedRanges()
    {
        AtomCounter counter = new();
        DataDemandMint mint = new(counter.Next);

        int forward = mint.MarkerFor(PropertyD, DataDemandKind.Existential, 0, IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 1), (Vocabulary.XsdFacets.MaxInclusive, 10)));
        int reversed = mint.MarkerFor(PropertyD, DataDemandKind.Existential, 0, IntegerRestriction((Vocabulary.XsdFacets.MaxInclusive, 10), (Vocabulary.XsdFacets.MinInclusive, 1)));

        Assert.AreEqual(forward, reversed, "Facet-reordered ranges intern to one marker.");
        Assert.HasCount(1, mint.Descriptors);
    }

    /// <summary>The mint's descriptor carries the CANONICAL range: a degenerate interval demand is registered as its point enumeration.</summary>
    [TestMethod]
    public void MintDescriptorCarriesCanonicalRange()
    {
        AtomCounter counter = new();
        DataDemandMint mint = new(counter.Next);

        int marker = mint.MarkerFor(PropertyD, DataDemandKind.Existential, 0, IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 5), (Vocabulary.XsdFacets.MaxInclusive, 5)));

        Assert.IsInstanceOfType<OwlDataOneOf>(mint.Descriptors[marker].Range, "The descriptor carries the canonical point enumeration, not the original restriction.");
    }

    /// <summary>An integer range bounded below inclusively.</summary>
    /// <param name="bound">The inclusive lower bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerAtLeast(int bound)
    {
        return IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, bound));
    }

    /// <summary>An integer datatype restriction over the given integer facet bounds.</summary>
    /// <param name="bounds">The facet–bound pairs.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerRestriction(params (Utf8String Facet, int Bound)[] bounds)
    {
        List<OwlFacetRestriction> facets = [];
        foreach((Utf8String facet, int bound) in bounds)
        {
            facets.Add(new OwlFacetRestriction(new NamedNode(facet), new Literal(Utf8Strings.From(bound.ToString(System.Globalization.CultureInfo.InvariantCulture)), new NamedNode(Vocabulary.Xsd.Integer))));
        }

        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.Integer), facets);
    }

    /// <summary>A fresh concept-atom id source that hands out ascending ids, bound as the mint's <see cref="FreshAtomDelegate"/> without a closure.</summary>
    private sealed class AtomCounter
    {
        /// <summary>The next id to hand out.</summary>
        private int nextId;

        /// <summary>The next fresh id.</summary>
        /// <returns>An ascending id.</returns>
        public int Next()
        {
            return nextId++;
        }
    }
}
