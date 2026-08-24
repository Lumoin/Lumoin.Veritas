using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The named box fixtures shared by the packed-index suites: the parity
/// family every oracle iterates — the cross adversary included, so the brute
/// scan and the standalone containment index both exercise the shape that
/// defeats union-bound pruning — plus the extension fixture whose overlapping
/// and nested boxes make multi-result containing rows possible (the golden
/// fixture's fourteen boxes are pairwise disjoint, so multi-result
/// containment is geometrically impossible over it), and the probe grid the
/// parity suites read against.
/// </summary>
internal static class PackedBoxIndexFixtureFamily
{
    /// <summary>The named parity fixtures, each stressing a different candidate-set shape.</summary>
    /// <returns>The fixtures, name first.</returns>
    public static IEnumerable<(string Name, BoundingBox[] Items)> NamedFixtures()
    {
        var disjoint = new BoundingBox[24];

        for(int index = 0; index < disjoint.Length; index++)
        {
            double offset = index * 2d;
            disjoint[index] = new BoundingBox(offset, 0, offset + 1, 1);
        }

        yield return ("disjoint lattice", disjoint);

        var nested = new BoundingBox[8];

        for(int index = 0; index < nested.Length; index++)
        {
            double inset = index * 5d;
            nested[index] = new BoundingBox(inset, inset, 100 - inset, 100 - inset);
        }

        yield return ("nested pyramid", nested);

        var duplicates = new BoundingBox[18];

        for(int index = 0; index < duplicates.Length; index++)
        {
            double offset = (index / 3) * 10d;
            duplicates[index] = new BoundingBox(offset, offset, offset + 4, offset + 4);
        }

        yield return ("three-fold duplicates", duplicates);

        var touching = new BoundingBox[16];

        for(int index = 0; index < touching.Length; index++)
        {
            double x = (index % 4) * 10d;
            double y = (index / 4) * 10d;
            touching[index] = new BoundingBox(x, y, x + 10, y + 10);
        }

        yield return ("touching lattice", touching);

        var archipelago = new BoundingBox[26];
        archipelago[0] = new BoundingBox(0, 0, 1000, 1000);

        for(int island = 0; island < 25; island++)
        {
            double x = 50 + ((island % 5) * 190d);
            double y = 50 + ((island / 5) * 190d);
            archipelago[island + 1] = new BoundingBox(x, y, x + 8, y + 8);
        }

        yield return ("archipelago", archipelago);

        var points = new BoundingBox[12];

        for(int index = 0; index < points.Length; index++)
        {
            double x = index * 7d;
            points[index] = new BoundingBox(x, x, x, x);
        }

        yield return ("point boxes", points);

        yield return ("mixed extremes", new[]
        {
            new BoundingBox(-1e12, -1e12, 1e12, 1e12),
            new BoundingBox(0, 0, 1e-9, 1e-9),
            new BoundingBox(5, 5, 5, 5),
            new BoundingBox(-3, -3, 400, 2),
            new BoundingBox(399, 1, 401, 3),
            new BoundingBox(1e9, -1e-6, 1e9 + 1, 1e-6)
        });

        //The cross adversary: coincident-centre interleaved slats whose every node union is
        //the full field — the shape that defeats union-bound pruning in every mode while the
        //per-item boxes stay thin.
        var cross = new BoundingBox[30];
        CrossSlatFixture.WriteSlats(cross, fieldExtent: 1_000d, thickness: 2d);

        yield return ("cross slats", cross);
    }

    /// <summary>
    /// The extension fixture: twenty overlapping and nested boxes over a
    /// 100×100 field — a five-deep nesting chain around the centre (0 through
    /// 4), an outer envelope (10), tiny centre boxes inside the chain (9, 16),
    /// and mid-size overlappers — so containing probes have up to eight
    /// containers, and at capacity four the build carries two node levels
    /// above the leaf level, exercising the per-level re-sort the packings
    /// apply. Registration order is deliberately scattered so emission rank
    /// and registration index visibly disagree.
    /// </summary>
    /// <returns>The fixture items in registration order.</returns>
    public static BoundingBox[] ExtensionFixture()
    {
        return
        [
            new BoundingBox(10, 10, 90, 90),
            new BoundingBox(20, 20, 80, 80),
            new BoundingBox(30, 30, 70, 70),
            new BoundingBox(40, 40, 60, 60),
            new BoundingBox(45, 45, 55, 55),
            new BoundingBox(5, 5, 15, 15),
            new BoundingBox(12, 40, 38, 70),
            new BoundingBox(60, 12, 88, 38),
            new BoundingBox(35, 60, 65, 88),
            new BoundingBox(48, 48, 52, 52),
            new BoundingBox(2, 2, 98, 98),
            new BoundingBox(18, 42, 30, 58),
            new BoundingBox(70, 70, 78, 78),
            new BoundingBox(44, 20, 56, 34),
            new BoundingBox(25, 25, 45, 45),
            new BoundingBox(55, 55, 75, 75),
            new BoundingBox(47, 47, 53, 53),
            new BoundingBox(6, 80, 14, 94),
            new BoundingBox(80, 6, 94, 14),
            new BoundingBox(10, 60, 20, 75)
        ];
    }

    /// <summary>A probe grid over the fixture's extent plus point probes and one everything box — enough shapes that every mode has hits and misses.</summary>
    /// <param name="items">The fixture the probes are sized against.</param>
    /// <returns>The probe boxes.</returns>
    public static BoundingBox[] ProbesFor(BoundingBox[] items)
    {
        double minX = 0, minY = 0, maxX = 100, maxY = 100;

        if(items.Length > 0)
        {
            minX = items.Min(box => box.MinX);
            minY = items.Min(box => box.MinY);
            maxX = items.Max(box => box.MaxX);
            maxY = items.Max(box => box.MaxY);
        }

        double spanX = Math.Max(maxX - minX, 1);
        double spanY = Math.Max(maxY - minY, 1);
        var probes = new List<BoundingBox>();

        for(int gridX = 0; gridX < 4; gridX++)
        {
            for(int gridY = 0; gridY < 4; gridY++)
            {
                double x = minX + (gridX * spanX / 4d);
                double y = minY + (gridY * spanY / 4d);
                probes.Add(new BoundingBox(x, y, x + (spanX / 5d), y + (spanY / 5d)));
                probes.Add(new BoundingBox(x, y, x, y));
            }
        }

        probes.Add(new BoundingBox(minX - spanX, minY - spanY, maxX + spanX, maxY + spanY));
        probes.Add(new BoundingBox(maxX + spanX, maxY + spanY, maxX + (2 * spanX), maxY + (2 * spanY)));

        return [.. probes];
    }
}
