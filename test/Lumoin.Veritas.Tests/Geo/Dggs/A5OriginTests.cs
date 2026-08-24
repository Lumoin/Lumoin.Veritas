using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Lattice;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/origins.json</c> for <see cref="Origins"/>: per-origin id, axis,
    /// quaternion, angle, first quintant and orientation layout; the near-antipodal
    /// <see cref="Origins.IsNearestOrigin"/> test; the <see cref="Origins.Haversine"/> distance
    /// surrogate; and the <see cref="Origins.QuintantToSegment"/> / <see cref="Origins.SegmentToQuintant"/>
    /// round trip. Axis and quaternion arrays are asserted at |diff| &lt; 1e-13; haversine cases at
    /// |diff| &lt; 0.5e-4; everything else is exact.
    /// </summary>
    [TestClass]
    internal sealed class A5OriginTests
    {
        /// <summary>Bounds axis, quaternion, and angle array comparisons against the fixture at |diff| &lt; 1e-13.</summary>
        private const double PrecisionArray13 = 1e-13;

        /// <summary>Bounds haversine distance and unit-length comparisons at |diff| &lt; 0.5e-4.</summary>
        private const double Precision4 = 0.5e-4;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that there are exactly twelve origins, one per dodecahedron face.</summary>
        [TestMethod]
        public void HasTwelveOriginsForTheDodecahedronFaces()
        {
            Assert.HasCount(12, Origins.All);
        }

        /// <summary>Pins that every origin's id, axis, quaternion, angle, orientation layout, and first quintant match the fixture field for field.</summary>
        [TestMethod]
        public async Task OriginsMatchExpectedFixtureFieldForField()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            int index = 0;
            foreach(JsonElement expected in fixture.RootElement.EnumerateArray())
            {
                Origin origin = Origins.All[index];

                Assert.AreEqual(expected.GetProperty("id").GetInt32(), origin.Id);

                JsonElement axis = expected.GetProperty("axis");
                Assert.AreEqual(axis[0].GetDouble(), origin.Axis.Theta, PrecisionArray13);
                Assert.AreEqual(axis[1].GetDouble(), origin.Axis.Phi, PrecisionArray13);

                JsonElement quaternion = expected.GetProperty("quat");
                Assert.AreEqual(quaternion[0].GetDouble(), origin.Quaternion.X, PrecisionArray13);
                Assert.AreEqual(quaternion[1].GetDouble(), origin.Quaternion.Y, PrecisionArray13);
                Assert.AreEqual(quaternion[2].GetDouble(), origin.Quaternion.Z, PrecisionArray13);
                Assert.AreEqual(quaternion[3].GetDouble(), origin.Quaternion.W, PrecisionArray13);

                Assert.AreEqual(expected.GetProperty("angle").GetDouble(), origin.Angle, PrecisionArray13);

                Orientation[] expectedOrientation = ReadOrientationArray(expected.GetProperty("orientation"));
                Orientation[] actualOrientation = Origins.OrientationsForLayout(origin.Layout);
                Assert.AreSequenceEqual(expectedOrientation, actualOrientation);

                Assert.AreEqual(expected.GetProperty("firstQuintant").GetInt32(), origin.FirstQuintant);

                index++;
            }

            Assert.HasCount(index, Origins.All);
        }

        /// <summary>Pins that every origin's axis converts to a unit-length Cartesian vector.</summary>
        [TestMethod]
        public void EveryOriginAxisIsAUnitVectorWhenConvertedToCartesian()
        {
            foreach(Origin origin in Origins.All)
            {
                Cartesian cartesian = CoordinateTransforms.ToCartesian(origin.Axis);
                double length = Math.Sqrt((cartesian.X * cartesian.X) + (cartesian.Y * cartesian.Y) + (cartesian.Z * cartesian.Z));

                Assert.AreEqual(1.0, length, Precision4);
            }
        }

        /// <summary>Pins that every origin's quaternion is unit-length.</summary>
        [TestMethod]
        public void EveryOriginQuaternionIsNormalized()
        {
            foreach(Origin origin in Origins.All)
            {
                double length = Math.Sqrt(
                    (origin.Quaternion.X * origin.Quaternion.X) +
                    (origin.Quaternion.Y * origin.Quaternion.Y) +
                    (origin.Quaternion.Z * origin.Quaternion.Z) +
                    (origin.Quaternion.W * origin.Quaternion.W));

                Assert.AreEqual(1.0, length, Precision4);
            }
        }

        /// <summary>Pins that finding the nearest origin at each origin's own axis returns that same origin.</summary>
        [TestMethod]
        public void FindNearestOriginFindsEachOriginAtItsOwnFaceCenter()
        {
            foreach(Origin origin in Origins.All)
            {
                Origin nearest = Origins.FindNearestOrigin(origin.Axis);

                Assert.AreEqual(origin.Id, nearest.Id);
            }
        }

        /// <summary>Pins that finding the nearest origin at a face boundary point returns one of the two adjacent origins.</summary>
        [TestMethod]
        public void FindNearestOriginFindsOneOfTwoOriginsAtFaceBoundaries()
        {
            (Spherical Point, int[] ExpectedOriginIds)[] boundaryPoints =
            [
                (new Spherical(0, Constants.PiOver5 / 2), [0, 1]),
                (new Spherical(2 * Constants.PiOver5, Constants.PiOver5), [3, 4]),
                (new Spherical(0, Math.PI - (Constants.PiOver5 / 2)), [9, 10])
            ];

            foreach((Spherical point, int[] expectedOriginIds) in boundaryPoints)
            {
                Origin nearest = Origins.FindNearestOrigin(point);

                Assert.Contains(nearest.Id, expectedOriginIds);
            }
        }

        /// <summary>Pins that finding the nearest origin at each origin's antipodal point never returns that same origin.</summary>
        [TestMethod]
        public void FindNearestOriginHandlesAntipodalPoints()
        {
            foreach(Origin origin in Origins.All)
            {
                Spherical antipodal = new(origin.Axis.Theta + Math.PI, Math.PI - origin.Axis.Phi);

                Origin nearest = Origins.FindNearestOrigin(antipodal);

                Assert.AreNotEqual(origin.Id, nearest.Id);
            }
        }

        /// <summary>Pins that the haversine distance between a point and itself is zero.</summary>
        [TestMethod]
        public void HaversineReturnsZeroForIdenticalPoints()
        {
            Spherical point = new(0, 0);
            Assert.AreEqual(0, Origins.Haversine(point, point));

            Spherical point2 = new(Math.PI / 4, Math.PI / 3);
            Assert.AreEqual(0, Origins.Haversine(point2, point2));
        }

        /// <summary>Pins that the haversine distance is symmetric in its two point arguments.</summary>
        [TestMethod]
        public void HaversineIsSymmetric()
        {
            Spherical p1 = new(0, Math.PI / 4);
            Spherical p2 = new(Math.PI / 2, Math.PI / 3);

            double d1 = Origins.Haversine(p1, p2);
            double d2 = Origins.Haversine(p2, p1);

            Assert.AreEqual(d2, d1, Precision4);
        }

        /// <summary>Pins that the haversine distance strictly increases as angular separation grows.</summary>
        [TestMethod]
        public void HaversineIncreasesWithAngularSeparation()
        {
            Spherical origin = new(0, 0);
            Spherical[] distances =
            [
                new Spherical(0, Math.PI / 6),
                new Spherical(0, Math.PI / 4),
                new Spherical(0, Math.PI / 3),
                new Spherical(0, Math.PI / 2)
            ];

            double lastDistance = 0;
            foreach(Spherical point in distances)
            {
                double distance = Origins.Haversine(origin, point);
                Assert.IsGreaterThan(lastDistance, distance);
                lastDistance = distance;
            }
        }

        /// <summary>Pins that the haversine distance grows with longitude separation at fixed latitude.</summary>
        [TestMethod]
        public void HaversineHandlesLongitudeSeparation()
        {
            double lat = Math.PI / 4;
            Spherical p1 = new(0, lat);
            Spherical p2 = new(Math.PI, lat);
            Spherical p3 = new(Math.PI / 2, lat);

            double d1 = Origins.Haversine(p1, p2);
            double d2 = Origins.Haversine(p1, p3);

            Assert.IsGreaterThan(d2, d1);
        }

        /// <summary>Pins that the haversine distance matches hand-derived known-value cases within Precision4.</summary>
        [TestMethod]
        public void HaversineMatchesKnownCasesAtTheFixtureTolerance()
        {
            Assert.AreEqual(0.5, Origins.Haversine(new Spherical(0, 0), new Spherical(0, Math.PI / 2)), Precision4);
            Assert.AreEqual(0.25, Origins.Haversine(new Spherical(0, Math.PI / 4), new Spherical(Math.PI / 2, Math.PI / 4)), Precision4);
        }

        /// <summary>Pins that converting a quintant to a segment and back round-trips to the original quintant for every quintant of the first origin.</summary>
        [TestMethod]
        public void QuintantToSegmentAndSegmentToQuintantRoundTrip()
        {
            Origin origin = Origins.All[0];
            for(int quintant = 0; quintant < 5; quintant++)
            {
                QuintantSegment quintantSegment = Origins.QuintantToSegment(quintant, origin);
                SegmentQuintant segmentQuintant = Origins.SegmentToQuintant(quintantSegment.Segment, origin);

                Assert.AreEqual(quintant, segmentQuintant.Quintant);
            }
        }

        /// <summary>Pins that finding the nearest origin at each origin's own axis returns that same origin, mirroring IsNearestOrigin's face-center behavior.</summary>
        [TestMethod]
        public void IsNearestOriginIsTrueAtFaceCenters()
        {
            foreach(Origin origin in Origins.All)
            {
                Origin nearest = Origins.FindNearestOrigin(origin.Axis);

                Assert.AreEqual(origin.Id, nearest.Id);
            }
        }

        /// <summary>Pins that IsNearestOrigin is false for a face-boundary point against an origin it is not nearest to.</summary>
        [TestMethod]
        public void IsNearestOriginIsFalseAtFaceBoundaries()
        {
            (Spherical Point, Origin Origin)[] boundaryPoints =
            [
                (new Spherical(0, Constants.PiOver5 / 2), Origins.All[0]),
                (new Spherical(2 * Constants.PiOver5, Constants.PiOver5), Origins.All[3]),
                (new Spherical(0, Math.PI - (Constants.PiOver5 / 2)), Origins.All[9])
            ];

            foreach((Spherical point, Origin origin) in boundaryPoints)
            {
                Assert.IsFalse(Origins.IsNearestOrigin(point, origin));
            }
        }

        /// <summary>Reads a fixture's <c>orientation</c> array of lowercase strings into <see cref="Orientation"/> values.</summary>
        private static Orientation[] ReadOrientationArray(JsonElement orientationElement)
        {
            Orientation[] orientations = new Orientation[orientationElement.GetArrayLength()];
            int index = 0;
            foreach(JsonElement entry in orientationElement.EnumerateArray())
            {
                orientations[index] = ParseOrientation(entry.GetString());
                index++;
            }

            return orientations;
        }

        /// <summary>Parses a fixture orientation string (e.g. <c>"wv"</c>) into an <see cref="Orientation"/> value.</summary>
        private static Orientation ParseOrientation(string? orientation)
        {
            return orientation switch
            {
                "uv" => Orientation.UV,
                "vu" => Orientation.VU,
                "uw" => Orientation.UW,
                "wu" => Orientation.WU,
                "vw" => Orientation.VW,
                "wv" => Orientation.WV,
                _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Unknown fixture orientation value."),
            };
        }

        /// <summary>Loads <c>fixtures/origins.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/origins.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
