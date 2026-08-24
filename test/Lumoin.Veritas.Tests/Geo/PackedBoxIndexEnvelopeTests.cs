using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The consumer-shaped integration gate: geometry envelopes flow from the
/// envelope seam into the index, per-pair candidate pruning runs in the mode
/// matching each predicate direction, and every surviving pair is decided by
/// the real topological evaluation — the filter may over-report, but a pair
/// the predicate affirms must never be pruned. Zero false negatives is the
/// exact-superset contract at the geometry level.
/// </summary>
[TestClass]
internal sealed class PackedBoxIndexEnvelopeTests
{
    /// <summary>No pair any of the three predicate directions affirms is ever pruned by the envelope filter.</summary>
    [TestMethod]
    public void EnvelopePruningNeverDropsAPredicatePair()
    {
        //A small feature set with every relationship shape the three predicate directions
        //distinguish: containment both ways, overlap, touching, disjoint, a point, a line.
        string[] wellKnownTexts =
        [
            "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))",
            "POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))",
            "POLYGON ((8 8, 14 8, 14 14, 8 14, 8 8))",
            "POLYGON ((20 20, 30 20, 30 30, 20 30, 20 20))",
            "LINESTRING (1 1, 9 9)",
            "POINT (3 3)",
            "POLYGON ((0 0, 40 0, 40 40, 0 40, 0 0))",
            "LINESTRING (10 0, 10 10)"
        ];

        var geometries = new FlatGeometry[wellKnownTexts.Length];
        var envelopes = new BoundingBox[wellKnownTexts.Length];

        for(int feature = 0; feature < wellKnownTexts.Length; feature++)
        {
            Assert.IsTrue(WktGeometryReader.TryRead(wellKnownTexts[feature], out geometries[feature], out _), $"'{wellKnownTexts[feature]}' must parse.");
            Assert.IsTrue(GeometryEnvelope.TryComputeBounds(in geometries[feature], out envelopes[feature]), "Every fixture feature is non-empty.");
        }

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 4));

        Assert.IsTrue(index.TryBuild(envelopes));

        int intersectsAffirmed = 0;
        int containsAffirmed = 0;
        int withinAffirmed = 0;

        for(int first = 0; first < geometries.Length; first++)
        {
            var intersectingCandidates = new HashSet<int>();
            var containedCandidates = new HashSet<int>();
            var containingCandidates = new HashSet<int>();

            foreach(int candidate in index.Intersecting(in envelopes[first]))
            {
                intersectingCandidates.Add(candidate);
            }

            foreach(int candidate in index.ContainedIn(in envelopes[first]))
            {
                containedCandidates.Add(candidate);
            }

            foreach(int candidate in index.Containing(in envelopes[first]))
            {
                containingCandidates.Add(candidate);
            }

            for(int second = 0; second < geometries.Length; second++)
            {
                //Each predicate prunes in its own direction: an intersection needs the
                //envelopes to meet; first containing second needs second's envelope inside
                //first's; first within second needs second's envelope enclosing first's.
                Assert.IsTrue(GeometryRelate.TryEvaluate(in geometries[first], in geometries[second], TopologicalPredicate.SfIntersects, out bool intersects));

                if(intersects)
                {
                    //The vacuity counters take only off-diagonal pairs: every feature
                    //reflexively intersects, contains, and lies within itself, so diagonal
                    //affirmations would satisfy the thresholds without one genuine
                    //cross-feature pair. The prune assertions still run for every pair,
                    //self-pairs included.
                    if(second != first)
                    {
                        intersectsAffirmed++;
                    }

                    Assert.Contains(second, intersectingCandidates,
                        $"Features {first} and {second} intersect, so the envelope filter must keep the pair.");
                }

                Assert.IsTrue(GeometryRelate.TryEvaluate(in geometries[first], in geometries[second], TopologicalPredicate.SfContains, out bool contains));

                if(contains)
                {
                    if(second != first)
                    {
                        containsAffirmed++;
                    }

                    Assert.Contains(second, containedCandidates,
                        $"Feature {first} contains {second}, so the contained-in prune must keep the pair.");
                }

                Assert.IsTrue(GeometryRelate.TryEvaluate(in geometries[first], in geometries[second], TopologicalPredicate.SfWithin, out bool within));

                if(within)
                {
                    if(second != first)
                    {
                        withinAffirmed++;
                    }

                    Assert.Contains(second, containingCandidates,
                        $"Feature {first} is within {second}, so the containing prune must keep the pair.");
                }
            }
        }

        //The gate is vacuous unless the fixture actually affirms each direction.
        Assert.IsGreaterThan(8, intersectsAffirmed, "The fixture must affirm a healthy set of intersections.");
        Assert.IsGreaterThan(2, containsAffirmed, "The fixture must affirm containment pairs beyond self-containment.");
        Assert.IsGreaterThan(2, withinAffirmed, "The fixture must affirm within pairs beyond self-containment.");
    }
}
