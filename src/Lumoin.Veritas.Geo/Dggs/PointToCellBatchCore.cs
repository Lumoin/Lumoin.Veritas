using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Lattice;
using Lumoin.Veritas.Geo.Dggs.Numerics;
using Lumoin.Veritas.Geo.Dggs.Projections;

namespace Lumoin.Veritas.Geo.Dggs;

/// <summary>
/// The width-generic batch point-to-cell core every SIMD ladder rung instantiates: lane-parallel
/// mirrors of the scalar estimate pipeline (<see cref="Cell.LonLatToCell"/> →
/// <see cref="Cell.SphericalToCell"/>'s fast path) at 2, 4 or 8 <see cref="double"/> lanes via
/// <see cref="IPointToCellLanes{TSelf}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bit-exactness doctrine.</b> Every vector stage uses
/// only IEEE-754 correctly-rounded operations in the scalar source's exact association order — no FMA,
/// no vector min/max, no approximations — so each lane computes the same bits the scalar reference
/// computes. Every transcendental (<c>Sin</c>, <c>Cos</c>, <c>Tan</c>, <c>Atan2</c>, <c>Acos</c>,
/// <c>Asin</c>) is a per-lane scalar <see cref="Math"/> call: identical call sites, identical operation
/// order, bit-exact by construction. Every mixed or discrete stage (sector index, reflection decision,
/// quintant tables, Hilbert curve, serialization, containment) is a per-lane call into the EXISTING
/// internal scalar helpers — reuse, never re-transcription.
/// </para>
/// <para>
/// <b>Two bit-identical hoists</b> the batch shape legitimizes (same inputs → same deterministic
/// function → same bits; the ladder's bit-identity gates verify): (1) the containment test reuses the
/// estimate's own <see cref="DodecahedronProjection.Forward"/> result via
/// <see cref="Cell.A5CellContainsPointProjected"/> — the scalar path computes the identical projection
/// twice per fast-path point; (2) <see cref="Origins.FindNearestOrigin"/>'s <c>sin(point.Phi)</c> is
/// computed once per point and <c>sin(axis.Phi)</c> once per origin into a static table — the scalar
/// path recomputes both inside every one of the twelve per-origin distance evaluations.
/// </para>
/// <para>
/// <b>Fallback discipline.</b> Resolutions below <see cref="Serialization.FirstHilbertResolution"/>
/// (including the world cell) delegate wholesale to the scalar path, as does the tail block shorter
/// than the lane width; any lane whose estimate fails strict containment falls back per point to
/// <see cref="Cell.SphericalToCell"/> — the fallback IS the scalar reference, so divergence on the
/// spiral/neighbor path is impossible by construction.
/// </para>
/// <para>
/// <b>Measured standing on x64:</b> parity with the scalar reference within noise — the
/// per-lane scalar transcendentals dominate and the lane-staging overhead consumes the algebra win, so
/// this core is NOT the recommended x64 kernel; it is kept bit-identical and gated for the
/// WASM-SIMD/NEON tier.
/// </para>
/// </remarks>
internal static class PointToCellBatchCore
{
    /// <summary>Number of dodecahedron-face origins; the nearest-origin search walks all of them in id order.</summary>
    private const int OriginCount = 12;

    /// <summary>
    /// <c>sin(axis.Phi)</c> per origin, hoisted out of the per-point nearest-origin search: the axis is
    /// a process-lifetime constant, so <see cref="Math.Sin"/> of it is the same bits the scalar
    /// <see cref="Origins.Haversine"/> computes per call.
    /// </summary>
    private static double[] OriginAxisPhiSine { get; } = BuildOriginAxisPhiSine();

    /// <summary>Per-origin axis theta, copied once so the hot loop broadcasts from a flat table.</summary>
    private static double[] OriginAxisTheta { get; } = BuildOriginAxisTheta();

    /// <summary>Per-origin axis phi, copied once so the hot loop broadcasts from a flat table.</summary>
    private static double[] OriginAxisPhi { get; } = BuildOriginAxisPhi();

    /// <summary>Per-origin inverse-quaternion X components, for lane-wise gathers.</summary>
    private static double[] OriginInverseQuaternionX { get; } = BuildOriginInverseQuaternionComponent(0);

    /// <summary>Per-origin inverse-quaternion Y components, for lane-wise gathers.</summary>
    private static double[] OriginInverseQuaternionY { get; } = BuildOriginInverseQuaternionComponent(1);

    /// <summary>Per-origin inverse-quaternion Z components, for lane-wise gathers.</summary>
    private static double[] OriginInverseQuaternionZ { get; } = BuildOriginInverseQuaternionComponent(2);

    /// <summary>Per-origin inverse-quaternion W components, for lane-wise gathers.</summary>
    private static double[] OriginInverseQuaternionW { get; } = BuildOriginInverseQuaternionComponent(3);

    /// <summary>Per-origin face-axis rotation angle, for lane-wise gathers.</summary>
    private static double[] OriginAngle { get; } = BuildOriginAngle();

    /// <summary>
    /// The five quintant rotation matrices <c>Cell.FaceToEstimate</c> constructs per call for
    /// non-zero quintants, precomputed with the identical argument expression
    /// (<c>-(2 · π/5 · quintant)</c>) so each entry is the same bits. Entry 0 is populated for table
    /// regularity but never selected: quintant-0 lanes take the scalar path's rotation SKIP verbatim
    /// (an identity multiply would not be bit-safe for negative-zero components).
    /// </summary>
    private static Matrix2x2d[] QuintantRotationMatrix { get; } = BuildQuintantRotationMatrices();

    /// <summary>
    /// Runs the batch kernel over interleaved longitude/latitude degrees at one lane width. Same
    /// contract as <see cref="A5PointToCellKernel"/>; same span-length validation as the scalar kernel.
    /// </summary>
    internal static void Run<TLanes>(ReadOnlySpan<double> sourceLongitudeLatitude, int resolution, Span<A5CellId> destinationCellIds)
        where TLanes : struct, IPointToCellLanes<TLanes>
    {
        if(sourceLongitudeLatitude.Length % 2 != 0)
        {
            throw new ArgumentException(
                $"Source span length ({sourceLongitudeLatitude.Length}) must be even — interleaved longitude/latitude pairs.",
                nameof(sourceLongitudeLatitude));
        }

        int pointCount = sourceLongitudeLatitude.Length / 2;
        if(destinationCellIds.Length != pointCount)
        {
            throw new ArgumentException(
                $"Destination length ({destinationCellIds.Length}) must equal source length / 2 ({pointCount}).",
                nameof(destinationCellIds));
        }

        // The world cell and the flat (non-Hilbert) resolutions have no estimate/containment fast path
        // to vectorize; they delegate wholesale to the scalar reference.
        if(resolution < Serialization.FirstHilbertResolution)
        {
            for(int index = 0; index < pointCount; index++)
            {
                double longitude = sourceLongitudeLatitude[2 * index];
                double latitude = sourceLongitudeLatitude[(2 * index) + 1];
                destinationCellIds[index] = new A5CellId(Cell.LonLatToCell(new LonLat(longitude, latitude), resolution));
            }

            return;
        }

        // Hoisted per call: both are deterministic functions of the shared resolution, so lifting them
        // out of the per-point loop reproduces the scalar path's per-point values bit-for-bit.
        int hilbertResolution = 1 + resolution - Serialization.FirstHilbertResolution;
        double resolutionScale = Math.Pow(2, hilbertResolution);

        int laneCount = TLanes.LaneCount;
        int blockCount = pointCount / laneCount;

        // Per-lane scratch, allocated once outside the block loop. Grouped by pipeline stage.
        Span<double> longitudeDegrees = stackalloc double[laneCount];
        Span<double> latitudeDegrees = stackalloc double[laneCount];
        Span<double> pointTheta = stackalloc double[laneCount];
        Span<double> pointPhi = stackalloc double[laneCount];
        Span<double> geodeticSine = stackalloc double[laneCount];
        Span<double> geodeticCosine = stackalloc double[laneCount];
        Span<double> pointPhiSine = stackalloc double[laneCount];
        Span<double> deltaPhiHalf = stackalloc double[laneCount];
        Span<double> deltaThetaHalf = stackalloc double[laneCount];
        Span<double> deltaPhiHalfSine = stackalloc double[laneCount];
        Span<double> deltaThetaHalfSine = stackalloc double[laneCount];
        Span<int> nearestOriginId = stackalloc int[laneCount];
        Span<double> pointThetaSine = stackalloc double[laneCount];
        Span<double> pointThetaCosine = stackalloc double[laneCount];
        Span<double> pointPhiCosine = stackalloc double[laneCount];
        Span<double> quaternionX = stackalloc double[laneCount];
        Span<double> quaternionY = stackalloc double[laneCount];
        Span<double> quaternionZ = stackalloc double[laneCount];
        Span<double> quaternionW = stackalloc double[laneCount];
        Span<double> rotatedX = stackalloc double[laneCount];
        Span<double> rotatedY = stackalloc double[laneCount];
        Span<double> originSpaceTheta = stackalloc double[laneCount];
        Span<double> originSpaceZOverR = stackalloc double[laneCount];
        Span<double> sectorRho = stackalloc double[laneCount];
        Span<double> sectorGamma = stackalloc double[laneCount];
        Span<double> sphericalVertexAX = stackalloc double[laneCount];
        Span<double> sphericalVertexAY = stackalloc double[laneCount];
        Span<double> sphericalVertexAZ = stackalloc double[laneCount];
        Span<double> sphericalVertexBX = stackalloc double[laneCount];
        Span<double> sphericalVertexBY = stackalloc double[laneCount];
        Span<double> sphericalVertexBZ = stackalloc double[laneCount];
        Span<double> sphericalVertexCX = stackalloc double[laneCount];
        Span<double> sphericalVertexCY = stackalloc double[laneCount];
        Span<double> sphericalVertexCZ = stackalloc double[laneCount];
        Span<double> faceVertexAX = stackalloc double[laneCount];
        Span<double> faceVertexAY = stackalloc double[laneCount];
        Span<double> faceVertexBX = stackalloc double[laneCount];
        Span<double> faceVertexBY = stackalloc double[laneCount];
        Span<double> faceVertexCX = stackalloc double[laneCount];
        Span<double> faceVertexCY = stackalloc double[laneCount];
        Span<double> triangleAreaClamped = stackalloc double[laneCount];
        Span<double> triangleArea1 = stackalloc double[laneCount];
        Span<double> triangleArea2 = stackalloc double[laneCount];
        Span<double> facePointX = stackalloc double[laneCount];
        Span<double> facePointY = stackalloc double[laneCount];
        Span<int> quintantIndex = stackalloc int[laneCount];
        Span<int> segmentIndex = stackalloc int[laneCount];
        Span<Orientation> segmentOrientation = stackalloc Orientation[laneCount];
        Span<double> rotationM0 = stackalloc double[laneCount];
        Span<double> rotationM1 = stackalloc double[laneCount];
        Span<double> rotationM2 = stackalloc double[laneCount];
        Span<double> rotationM3 = stackalloc double[laneCount];
        Span<double> rotatedFaceX = stackalloc double[laneCount];
        Span<double> rotatedFaceY = stackalloc double[laneCount];
        Span<double> latticeI = stackalloc double[laneCount];
        Span<double> latticeJ = stackalloc double[laneCount];

        TLanes half = TLanes.Broadcast(0.5);
        TLanes one = TLanes.Broadcast(1);
        TLanes two = TLanes.Broadcast(2);
        TLanes zero = TLanes.Broadcast(0);
        TLanes degreesToRadians = TLanes.Broadcast(Math.PI / 180);
        TLanes longitudeOffset = TLanes.Broadcast(CoordinateTransforms.LongitudeOffsetDegrees);
        TLanes piOverTwo = TLanes.Broadcast(Math.PI / 2);
        TLanes scaleBroadcast = TLanes.Broadcast(resolutionScale);
        ReadOnlySpan<double> authalicCoefficients = AuthalicProjection.GeodeticToAuthalicCoefficients;
        TLanes coefficient0 = TLanes.Broadcast(authalicCoefficients[0]);
        TLanes coefficient1 = TLanes.Broadcast(authalicCoefficients[1]);
        TLanes coefficient2 = TLanes.Broadcast(authalicCoefficients[2]);
        TLanes coefficient3 = TLanes.Broadcast(authalicCoefficients[3]);
        TLanes coefficient4 = TLanes.Broadcast(authalicCoefficients[4]);
        TLanes coefficient5 = TLanes.Broadcast(authalicCoefficients[5]);
        EqualAreaTriangleConstants equalAreaConstants = DodecahedronProjection.EqualAreaConstants;
        TLanes canonicalTriangleArea = TLanes.Broadcast(equalAreaConstants.TriangleArea);
        Matrix2x2d basisInverse = PentagonConstants.BasisInverse;
        TLanes basisInverseM0 = TLanes.Broadcast(basisInverse.M0);
        TLanes basisInverseM1 = TLanes.Broadcast(basisInverse.M1);
        TLanes basisInverseM2 = TLanes.Broadcast(basisInverse.M2);
        TLanes basisInverseM3 = TLanes.Broadcast(basisInverse.M3);

        for(int block = 0; block < blockCount; block++)
        {
            int blockStart = block * laneCount;

            // Deinterleave the block's longitude/latitude pairs into lane order.
            for(int lane = 0; lane < laneCount; lane++)
            {
                longitudeDegrees[lane] = sourceLongitudeLatitude[2 * (blockStart + lane)];
                latitudeDegrees[lane] = sourceLongitudeLatitude[(2 * (blockStart + lane)) + 1];
            }

            // Stage 1 — CoordinateTransforms.FromLonLat: theta = (lon + offset) · π/180; the geodetic
            // latitude runs through AuthalicProjection.Forward's Clenshaw recurrence (vector, exact
            // operation order) around per-lane scalar sin/cos.
            TLanes longitudeVector = TLanes.FromSpan(longitudeDegrees);
            TLanes latitudeVector = TLanes.FromSpan(latitudeDegrees);
            TLanes thetaVector = (longitudeVector + longitudeOffset) * degreesToRadians;
            TLanes geodeticVector = latitudeVector * degreesToRadians;

            TLanes.CopyTo(geodeticVector, deltaPhiHalf);
            for(int lane = 0; lane < laneCount; lane++)
            {
                geodeticSine[lane] = Math.Sin(deltaPhiHalf[lane]);
                geodeticCosine[lane] = Math.Cos(deltaPhiHalf[lane]);
            }

            TLanes geodeticSineVector = TLanes.FromSpan(geodeticSine);
            TLanes geodeticCosineVector = TLanes.FromSpan(geodeticCosine);

            // x = 2 · (cos − sin) · (cos + sin), left-associated exactly as the scalar recurrence.
            TLanes clenshawX = (two * (geodeticCosineVector - geodeticSineVector)) * (geodeticCosineVector + geodeticSineVector);
            TLanes u0 = (clenshawX * coefficient5) + coefficient4;
            TLanes u1 = (clenshawX * u0) + coefficient3;
            u0 = ((clenshawX * u1) - u0) + coefficient2;
            u1 = ((clenshawX * u0) - u1) + coefficient1;
            u0 = ((clenshawX * u1) - u0) + coefficient0;
            TLanes authalicVector = geodeticVector + (((two * geodeticSineVector) * geodeticCosineVector) * u0);
            TLanes phiVector = piOverTwo - authalicVector;

            TLanes.CopyTo(thetaVector, pointTheta);
            TLanes.CopyTo(phiVector, pointPhi);

            // Stage 2 — Origins.FindNearestOrigin: the surrogate-haversine argmin over the twelve
            // origins, in id order with a strict less-than (first origin wins ties), exactly like the
            // scalar loop. sin(point.Phi) is hoisted per point and sin(axis.Phi) per origin (static
            // table) — same bits the scalar path recomputes per origin.
            for(int lane = 0; lane < laneCount; lane++)
            {
                pointPhiSine[lane] = Math.Sin(pointPhi[lane]);
                nearestOriginId[lane] = 0;
            }

            TLanes pointPhiSineVector = TLanes.FromSpan(pointPhiSine);
            TLanes minimumDistance = TLanes.Broadcast(double.PositiveInfinity);
            for(int originId = 0; originId < OriginCount; originId++)
            {
                TLanes deltaThetaVector = (TLanes.Broadcast(OriginAxisTheta[originId]) - thetaVector) / two;
                TLanes deltaPhiVector = (TLanes.Broadcast(OriginAxisPhi[originId]) - phiVector) / two;
                TLanes.CopyTo(deltaPhiVector, deltaPhiHalf);
                TLanes.CopyTo(deltaThetaVector, deltaThetaHalf);
                for(int lane = 0; lane < laneCount; lane++)
                {
                    deltaPhiHalfSine[lane] = Math.Sin(deltaPhiHalf[lane]);
                    deltaThetaHalfSine[lane] = Math.Sin(deltaThetaHalf[lane]);
                }

                TLanes a1 = TLanes.FromSpan(deltaPhiHalfSine);
                TLanes a2 = TLanes.FromSpan(deltaThetaHalfSine);
                TLanes distance = (a1 * a1) + (((a2 * a2) * pointPhiSineVector) * TLanes.Broadcast(OriginAxisPhiSine[originId]));
                TLanes closerMask = TLanes.LessThan(distance, minimumDistance);
                minimumDistance = TLanes.Select(closerMask, distance, minimumDistance);
                for(int lane = 0; lane < laneCount; lane++)
                {
                    if(TLanes.IsLaneSet(closerMask, lane))
                    {
                        nearestOriginId[lane] = originId;
                    }
                }
            }

            // Stage 3 — CoordinateTransforms.ToCartesian: per-lane sin/cos, vector products.
            // sin(point.Phi) is reused from stage 2 (identical argument → identical bits).
            for(int lane = 0; lane < laneCount; lane++)
            {
                pointThetaCosine[lane] = Math.Cos(pointTheta[lane]);
                pointThetaSine[lane] = Math.Sin(pointTheta[lane]);
                pointPhiCosine[lane] = Math.Cos(pointPhi[lane]);
            }

            LaneVector3<TLanes> cartesian = new(
                pointPhiSineVector * TLanes.FromSpan(pointThetaCosine),
                pointPhiSineVector * TLanes.FromSpan(pointThetaSine),
                TLanes.FromSpan(pointPhiCosine));

            // Stage 4 — DodecahedronProjection.ForwardCartesian, first half: rotate into origin space
            // with the origin's inverse quaternion via Vector3d.Transform's exact double-cross
            // expansion (uuv is computed from the PRE-scaled uv, then uv is scaled by 2w and uuv by 2).
            for(int lane = 0; lane < laneCount; lane++)
            {
                int originId = nearestOriginId[lane];
                quaternionX[lane] = OriginInverseQuaternionX[originId];
                quaternionY[lane] = OriginInverseQuaternionY[originId];
                quaternionZ[lane] = OriginInverseQuaternionZ[originId];
                quaternionW[lane] = OriginInverseQuaternionW[originId];
            }

            TLanes qx = TLanes.FromSpan(quaternionX);
            TLanes qy = TLanes.FromSpan(quaternionY);
            TLanes qz = TLanes.FromSpan(quaternionZ);
            TLanes qw = TLanes.FromSpan(quaternionW);
            TLanes uvX = (qy * cartesian.Z) - (qz * cartesian.Y);
            TLanes uvY = (qz * cartesian.X) - (qx * cartesian.Z);
            TLanes uvZ = (qx * cartesian.Y) - (qy * cartesian.X);
            TLanes uuvX = (qy * uvZ) - (qz * uvY);
            TLanes uuvY = (qz * uvX) - (qx * uvZ);
            TLanes uuvZ = (qx * uvY) - (qy * uvX);
            TLanes wTimesTwo = qw * two;
            uvX = uvX * wTimesTwo;
            uvY = uvY * wTimesTwo;
            uvZ = uvZ * wTimesTwo;
            uuvX = uuvX * two;
            uuvY = uuvY * two;
            uuvZ = uuvZ * two;
            LaneVector3<TLanes> inOriginSpace = new(
                cartesian.X + uvX + uuvX,
                cartesian.Y + uvY + uuvY,
                cartesian.Z + uvZ + uuvZ);

            // Stage 5 — ToSpherical + GnomonicProjection.Forward + the origin-angle removal:
            // r via vector sqrt (IEEE-exact), atan2/acos/tan per lane.
            TLanes radius = TLanes.SquareRoot(
                ((inOriginSpace.X * inOriginSpace.X) + (inOriginSpace.Y * inOriginSpace.Y)) + (inOriginSpace.Z * inOriginSpace.Z));
            TLanes zOverRadius = inOriginSpace.Z / radius;
            TLanes.CopyTo(inOriginSpace.X, rotatedX);
            TLanes.CopyTo(inOriginSpace.Y, rotatedY);
            TLanes.CopyTo(zOverRadius, originSpaceZOverR);
            for(int lane = 0; lane < laneCount; lane++)
            {
                originSpaceTheta[lane] = Math.Atan2(rotatedY[lane], rotatedX[lane]);
                double originSpacePhi = Math.Acos(originSpaceZOverR[lane]);
                sectorRho[lane] = Math.Tan(originSpacePhi);
                sectorGamma[lane] = originSpaceTheta[lane] - OriginAngle[nearestOriginId[lane]];
            }

            // Stage 6 — sector index, reflection decision and triangle lookups, per lane through the
            // EXISTING scalar helpers; the matched triangle pair's components are gathered lane-wise
            // for the vector equal-area stage.
            for(int lane = 0; lane < laneCount; lane++)
            {
                int faceTriangleIndex = DodecahedronProjection.GetFaceTriangleIndex(sectorGamma[lane]);
                bool reflected = DodecahedronProjection.ShouldReflect(new Polar(sectorRho[lane], sectorGamma[lane]));
                FaceTriangle faceTriangle = DodecahedronProjection.GetFaceTriangle(faceTriangleIndex, reflected, squashed: false);
                SphericalTriangle sphericalTriangle = DodecahedronProjection.GetSphericalTriangle(faceTriangleIndex, nearestOriginId[lane], reflected);
                sphericalVertexAX[lane] = sphericalTriangle.A.X;
                sphericalVertexAY[lane] = sphericalTriangle.A.Y;
                sphericalVertexAZ[lane] = sphericalTriangle.A.Z;
                sphericalVertexBX[lane] = sphericalTriangle.B.X;
                sphericalVertexBY[lane] = sphericalTriangle.B.Y;
                sphericalVertexBZ[lane] = sphericalTriangle.B.Z;
                sphericalVertexCX[lane] = sphericalTriangle.C.X;
                sphericalVertexCY[lane] = sphericalTriangle.C.Y;
                sphericalVertexCZ[lane] = sphericalTriangle.C.Z;
                faceVertexAX[lane] = faceTriangle.A.X;
                faceVertexAY[lane] = faceTriangle.A.Y;
                faceVertexBX[lane] = faceTriangle.B.X;
                faceVertexBY[lane] = faceTriangle.B.Y;
                faceVertexCX[lane] = faceTriangle.C.X;
                faceVertexCY[lane] = faceTriangle.C.Y;
            }

            LaneVector3<TLanes> vertexA = new(TLanes.FromSpan(sphericalVertexAX), TLanes.FromSpan(sphericalVertexAY), TLanes.FromSpan(sphericalVertexAZ));
            LaneVector3<TLanes> vertexB = new(TLanes.FromSpan(sphericalVertexBX), TLanes.FromSpan(sphericalVertexBY), TLanes.FromSpan(sphericalVertexBZ));
            LaneVector3<TLanes> vertexC = new(TLanes.FromSpan(sphericalVertexCX), TLanes.FromSpan(sphericalVertexCY), TLanes.FromSpan(sphericalVertexCZ));

            // Stage 7 — EqualAreaProjection.Forward, lane-wise: the quadruple-product intersection,
            // the two VectorDifference ratios, the two spherical triangle areas (vector algebra with a
            // per-lane Asin behind the scalar branch), and BarycentricToFace.
            LaneVector3<TLanes> differenceFromVertexA = Normalize(Subtract(cartesian, vertexA), one, zero);
            LaneVector3<TLanes> intersection = Normalize(QuadrupleProduct(vertexA, differenceFromVertexA, vertexB, vertexC), one, zero);
            TLanes heightRatio = VectorDifference(vertexA, cartesian, half, one, zero)
                / VectorDifference(vertexA, intersection, half, one, zero);
            TLanes scaledArea = heightRatio / canonicalTriangleArea;
            SphericalTriangleArea(vertexA, intersection, vertexC, half, one, zero, triangleAreaClamped, triangleArea1);
            SphericalTriangleArea(vertexA, vertexB, intersection, half, one, zero, triangleAreaClamped, triangleArea2);
            TLanes barycentric0 = one - heightRatio;
            TLanes barycentric1 = scaledArea * TLanes.FromSpan(triangleArea1);
            TLanes barycentric2 = scaledArea * TLanes.FromSpan(triangleArea2);
            TLanes facePointXVector =
                ((barycentric0 * TLanes.FromSpan(faceVertexAX)) + (barycentric1 * TLanes.FromSpan(faceVertexBX)))
                + (barycentric2 * TLanes.FromSpan(faceVertexCX));
            TLanes facePointYVector =
                ((barycentric0 * TLanes.FromSpan(faceVertexAY)) + (barycentric1 * TLanes.FromSpan(faceVertexBY)))
                + (barycentric2 * TLanes.FromSpan(faceVertexCY));
            TLanes.CopyTo(facePointXVector, facePointX);
            TLanes.CopyTo(facePointYVector, facePointY);

            // Stage 8 — Cell.FaceToEstimate's continuous prefix. The scalar path's ToPolar rho is dead
            // on this path (GetQuintantPolar is a function of gamma alone and rho is never read
            // afterwards), so only the per-lane atan2 is computed; the quintant/segment mapping goes
            // through the existing scalar helpers.
            for(int lane = 0; lane < laneCount; lane++)
            {
                double faceGamma = Math.Atan2(facePointY[lane], facePointX[lane]);
                int quintant = Tiling.GetQuintantPolar(new Polar(0, faceGamma));
                quintantIndex[lane] = quintant;
                QuintantSegment quintantSegment = Origins.QuintantToSegment(quintant, Origins.All[nearestOriginId[lane]]);
                segmentIndex[lane] = quintantSegment.Segment;
                segmentOrientation[lane] = quintantSegment.Orientation;
                Matrix2x2d rotation = QuintantRotationMatrix[quintant];
                rotationM0[lane] = rotation.M0;
                rotationM1[lane] = rotation.M1;
                rotationM2[lane] = rotation.M2;
                rotationM3[lane] = rotation.M3;
            }

            // Rotate into the canonical quintant (vector), then reproduce the scalar path's SKIP for
            // quintant-0 lanes exactly — an identity multiply could flip a negative-zero component.
            TLanes rotatedFaceXVector = (TLanes.FromSpan(rotationM0) * facePointXVector) + (TLanes.FromSpan(rotationM2) * facePointYVector);
            TLanes rotatedFaceYVector = (TLanes.FromSpan(rotationM1) * facePointXVector) + (TLanes.FromSpan(rotationM3) * facePointYVector);
            TLanes.CopyTo(rotatedFaceXVector, rotatedFaceX);
            TLanes.CopyTo(rotatedFaceYVector, rotatedFaceY);
            for(int lane = 0; lane < laneCount; lane++)
            {
                if(quintantIndex[lane] == 0)
                {
                    rotatedFaceX[lane] = facePointX[lane];
                    rotatedFaceY[lane] = facePointY[lane];
                }
            }

            // Scale to the resolution's lattice and convert to IJ via the basis inverse (vector; the
            // matrix product is column-major exactly like Matrix2x2d.Transform).
            TLanes scaledFaceX = TLanes.FromSpan(rotatedFaceX) * scaleBroadcast;
            TLanes scaledFaceY = TLanes.FromSpan(rotatedFaceY) * scaleBroadcast;
            TLanes latticeIVector = (basisInverseM0 * scaledFaceX) + (basisInverseM2 * scaledFaceY);
            TLanes latticeJVector = (basisInverseM1 * scaledFaceX) + (basisInverseM3 * scaledFaceY);
            TLanes.CopyTo(latticeIVector, latticeI);
            TLanes.CopyTo(latticeJVector, latticeJ);

            // Stage 9 — the discrete tail per lane, all through the existing scalar implementations:
            // Hilbert index, serialization, and the containment test on the REUSED projection from
            // stage 7. Lanes that fail strict containment fall back to the full scalar search.
            for(int lane = 0; lane < laneCount; lane++)
            {
                ulong hilbertIndex = HilbertCurve.IJToS(
                    new IJ(latticeI[lane], latticeJ[lane]),
                    hilbertResolution,
                    segmentOrientation[lane]);
                A5Cell estimate = new(Origins.All[nearestOriginId[lane]], segmentIndex[lane], hilbertIndex, resolution);
                ulong estimateKey = Serialization.Serialize(estimate);
                double containmentDistance = Cell.A5CellContainsPointProjected(estimate, new Face(facePointX[lane], facePointY[lane]));

                destinationCellIds[blockStart + lane] = containmentDistance > 0
                    ? new A5CellId(estimateKey)
                    : new A5CellId(Cell.SphericalToCell(new Spherical(pointTheta[lane], pointPhi[lane]), resolution));
            }
        }

        // Tail shorter than one lane width: the scalar reference per point.
        for(int index = blockCount * laneCount; index < pointCount; index++)
        {
            double longitude = sourceLongitudeLatitude[2 * index];
            double latitude = sourceLongitudeLatitude[(2 * index) + 1];
            destinationCellIds[index] = new A5CellId(Cell.LonLatToCell(new LonLat(longitude, latitude), resolution));
        }
    }

    /// <summary>Lane-wise 3-component vector: three lane registers carrying X, Y and Z.</summary>
    private readonly struct LaneVector3<TLanes>
        where TLanes : struct, IPointToCellLanes<TLanes>
    {
        /// <summary>X lanes.</summary>
        public readonly TLanes X;

        /// <summary>Y lanes.</summary>
        public readonly TLanes Y;

        /// <summary>Z lanes.</summary>
        public readonly TLanes Z;

        /// <summary>Bundles three lane registers.</summary>
        public LaneVector3(TLanes x, TLanes y, TLanes z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>Lane-wise <see cref="Vector3d"/> subtraction (component order preserved).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LaneVector3<TLanes> Subtract<TLanes>(LaneVector3<TLanes> a, LaneVector3<TLanes> b)
        where TLanes : struct, IPointToCellLanes<TLanes>
    {
        return new LaneVector3<TLanes>(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    /// <summary>Lane-wise <see cref="Vector3d.Cross"/>, component order fixed by fixture parity.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LaneVector3<TLanes> Cross<TLanes>(LaneVector3<TLanes> a, LaneVector3<TLanes> b)
        where TLanes : struct, IPointToCellLanes<TLanes>
    {
        return new LaneVector3<TLanes>(
            (a.Y * b.Z) - (a.Z * b.Y),
            (a.Z * b.X) - (a.X * b.Z),
            (a.X * b.Y) - (a.Y * b.X));
    }

    /// <summary>Lane-wise <see cref="Vector3d.Dot"/>: left-associated component product sum.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TLanes Dot<TLanes>(LaneVector3<TLanes> a, LaneVector3<TLanes> b)
        where TLanes : struct, IPointToCellLanes<TLanes>
    {
        return ((a.X * b.X) + (a.Y * b.Y)) + (a.Z * b.Z);
    }

    /// <summary>Lane-wise scalar scaling, mirroring <c>Vector3d operator *(Vector3d, double)</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LaneVector3<TLanes> Scale<TLanes>(LaneVector3<TLanes> a, TLanes scale)
        where TLanes : struct, IPointToCellLanes<TLanes>
    {
        return new LaneVector3<TLanes>(a.X * scale, a.Y * scale, a.Z * scale);
    }

    /// <summary>Lane-wise <see cref="Vector3d.Lerp"/>: exactly <c>a + t·(b − a)</c> per component.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LaneVector3<TLanes> Lerp<TLanes>(LaneVector3<TLanes> a, LaneVector3<TLanes> b, TLanes t)
        where TLanes : struct, IPointToCellLanes<TLanes>
    {
        return new LaneVector3<TLanes>(
            a.X + (t * (b.X - a.X)),
            a.Y + (t * (b.Y - a.Y)),
            a.Z + (t * (b.Z - a.Z)));
    }

    /// <summary>
    /// Lane-wise <see cref="Vector3d.Normalize"/>: multiply by <c>1/sqrt(x²+y²+z²)</c> when the squared
    /// length is positive, otherwise multiply by the (zero) squared length itself — the scalar branch
    /// reproduced with a select.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LaneVector3<TLanes> Normalize<TLanes>(LaneVector3<TLanes> value, TLanes one, TLanes zero)
        where TLanes : struct, IPointToCellLanes<TLanes>
    {
        TLanes squaredLength = ((value.X * value.X) + (value.Y * value.Y)) + (value.Z * value.Z);
        TLanes factor = TLanes.Select(
            TLanes.GreaterThan(squaredLength, zero),
            one / TLanes.SquareRoot(squaredLength),
            squaredLength);

        return new LaneVector3<TLanes>(value.X * factor, value.Y * factor, value.Z * factor);
    }

    /// <summary>
    /// Lane-wise <see cref="JsMath.Hypot(double, double, double)"/> for the finite, in-domain inputs
    /// this pipeline produces: the same normalize-by-max Kahan-compensated summation, unrolled with the
    /// exact per-term operation order; the max is taken with explicit compare-selects (never vector
    /// min/max) and the zero-max branch is a select. The scalar NaN/infinity guards are pure
    /// pass-throughs for finite inputs and are deliberately not mirrored (kernel contract: callers
    /// supply valid coordinates).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TLanes Hypot<TLanes>(LaneVector3<TLanes> value, TLanes zero)
        where TLanes : struct, IPointToCellLanes<TLanes>
    {
        TLanes absoluteX = TLanes.Abs(value.X);
        TLanes absoluteY = TLanes.Abs(value.Y);
        TLanes absoluteZ = TLanes.Abs(value.Z);

        // Math.Max(x, y) for non-NaN operands: x when x >= y, else y — mirrored per nesting level.
        TLanes maxXY = TLanes.Select(TLanes.GreaterThanOrEqual(absoluteX, absoluteY), absoluteX, absoluteY);
        TLanes max = TLanes.Select(TLanes.GreaterThanOrEqual(maxXY, absoluteZ), maxXY, absoluteZ);

        // AddScaledSquare unrolled three times: sum starts at 0 with 0 compensation; the operation
        // order within each term is load-bearing and must not be reassociated.
        TLanes sum = zero;
        TLanes compensation = zero;
        TLanes n = absoluteX / max;
        TLanes summand = (n * n) - compensation;
        TLanes preliminary = sum + summand;
        compensation = (preliminary - sum) - summand;
        sum = preliminary;
        n = absoluteY / max;
        summand = (n * n) - compensation;
        preliminary = sum + summand;
        compensation = (preliminary - sum) - summand;
        sum = preliminary;
        n = absoluteZ / max;
        summand = (n * n) - compensation;
        preliminary = sum + summand;
        compensation = (preliminary - sum) - summand;
        sum = preliminary;

        TLanes result = TLanes.SquareRoot(sum) * max;

        // max == 0 returns 0 exactly as the scalar early-out; the discarded division-by-zero lanes are
        // masked away by this select.
        return TLanes.Select(TLanes.EqualTo(max, zero), zero, result);
    }

    /// <summary>
    /// Lane-wise <see cref="Utils.VectorUtilities.QuadrupleProduct"/>: <c>(b · [a,c,d]) − (a · [b,c,d])</c>
    /// evaluated in exactly the scalar order.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LaneVector3<TLanes> QuadrupleProduct<TLanes>(
        LaneVector3<TLanes> a,
        LaneVector3<TLanes> b,
        LaneVector3<TLanes> c,
        LaneVector3<TLanes> d)
        where TLanes : struct, IPointToCellLanes<TLanes>
    {
        LaneVector3<TLanes> crossCD = Cross(c, d);
        TLanes tripleProductACD = Dot(a, crossCD);
        TLanes tripleProductBCD = Dot(b, crossCD);
        LaneVector3<TLanes> scaledA = Scale(a, tripleProductBCD);
        LaneVector3<TLanes> scaledB = Scale(b, tripleProductACD);

        return Subtract(scaledB, scaledA);
    }

    /// <summary>
    /// Lane-wise <see cref="Utils.VectorUtilities.VectorDifference"/>: the cross-with-normalized-midpoint
    /// magnitude, with the scalar's <c>1e-8</c> half-chord fallback reproduced as a select (both sides
    /// are computed; the unselected side is discarded, matching the scalar result lane-for-lane).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TLanes VectorDifference<TLanes>(LaneVector3<TLanes> a, LaneVector3<TLanes> b, TLanes half, TLanes one, TLanes zero)
        where TLanes : struct, IPointToCellLanes<TLanes>
    {
        LaneVector3<TLanes> midpoint = Normalize(Lerp(a, b, half), one, zero);
        TLanes difference = Hypot(Cross(a, midpoint), zero);
        TLanes halfDistance = half * Hypot(Subtract(a, b), zero);

        return TLanes.Select(TLanes.LessThan(difference, TLanes.Broadcast(1e-8)), halfDistance, difference);
    }

    /// <summary>
    /// Lane-wise <see cref="Geometry.SphericalPolygonPrimitives.SphericalTriangleArea"/>: midpoint triple
    /// product (vector algebra), the ±1 clamp mirrored as compare-selects in the scalar's exact
    /// <c>Min</c>-then-<c>Max</c> order, and the small-angle branch with its <see cref="Math.Asin"/>
    /// evaluated per lane.
    /// </summary>
    private static void SphericalTriangleArea<TLanes>(
        LaneVector3<TLanes> v1,
        LaneVector3<TLanes> v2,
        LaneVector3<TLanes> v3,
        TLanes half,
        TLanes one,
        TLanes zero,
        Span<double> clampedScratch,
        Span<double> areaDestination)
        where TLanes : struct, IPointToCellLanes<TLanes>
    {
        LaneVector3<TLanes> midpointOppositeV1 = Normalize(Lerp(v2, v3, half), one, zero);
        LaneVector3<TLanes> midpointOppositeV2 = Normalize(Lerp(v3, v1, half), one, zero);
        LaneVector3<TLanes> midpointOppositeV3 = Normalize(Lerp(v1, v2, half), one, zero);
        TLanes scalarTripleProduct = Dot(midpointOppositeV1, Cross(midpointOppositeV2, midpointOppositeV3));

        // Math.Min(1.0, x) then Math.Max(-1.0, min): for non-NaN, Min(1, x) is 1 when 1 < x else x;
        // Max(-1, m) is -1 when -1 > m else m — mirrored literally.
        TLanes negativeOne = zero - one;
        TLanes minimum = TLanes.Select(TLanes.LessThan(one, scalarTripleProduct), one, scalarTripleProduct);
        TLanes clamped = TLanes.Select(TLanes.GreaterThan(negativeOne, minimum), negativeOne, minimum);
        TLanes.CopyTo(clamped, clampedScratch);

        for(int lane = 0; lane < clampedScratch.Length; lane++)
        {
            double clampedLane = clampedScratch[lane];
            areaDestination[lane] = Math.Abs(clampedLane) < 1e-8 ? 2 * clampedLane : Math.Asin(clampedLane) * 2;
        }
    }

    /// <summary>Builds the hoisted <c>sin(axis.Phi)</c> table from the origin table itself.</summary>
    private static double[] BuildOriginAxisPhiSine()
    {
        double[] table = new double[OriginCount];
        for(int originId = 0; originId < OriginCount; originId++)
        {
            table[originId] = Math.Sin(Origins.All[originId].Axis.Phi);
        }

        return table;
    }

    /// <summary>Builds the flat axis-theta table from the origin table itself.</summary>
    private static double[] BuildOriginAxisTheta()
    {
        double[] table = new double[OriginCount];
        for(int originId = 0; originId < OriginCount; originId++)
        {
            table[originId] = Origins.All[originId].Axis.Theta;
        }

        return table;
    }

    /// <summary>Builds the flat axis-phi table from the origin table itself.</summary>
    private static double[] BuildOriginAxisPhi()
    {
        double[] table = new double[OriginCount];
        for(int originId = 0; originId < OriginCount; originId++)
        {
            table[originId] = Origins.All[originId].Axis.Phi;
        }

        return table;
    }

    /// <summary>
    /// Builds one flat inverse-quaternion component table (<paramref name="component"/> 0 through 3
    /// selecting X, Y, Z, W) from the origin table itself.
    /// </summary>
    private static double[] BuildOriginInverseQuaternionComponent(int component)
    {
        double[] table = new double[OriginCount];
        for(int originId = 0; originId < OriginCount; originId++)
        {
            QuaternionD quaternion = Origins.All[originId].InverseQuaternion;
            table[originId] = component switch
            {
                0 => quaternion.X,
                1 => quaternion.Y,
                2 => quaternion.Z,
                _ => quaternion.W,
            };
        }

        return table;
    }

    /// <summary>Builds the flat face-angle table from the origin table itself.</summary>
    private static double[] BuildOriginAngle()
    {
        double[] table = new double[OriginCount];
        for(int originId = 0; originId < OriginCount; originId++)
        {
            table[originId] = Origins.All[originId].Angle;
        }

        return table;
    }

    /// <summary>
    /// Builds the five quintant rotation matrices with the identical argument expression
    /// <c>Cell.FaceToEstimate</c> evaluates per call (<c>extraAngle = 2 · π/5 · quintant</c>,
    /// rotation by <c>-extraAngle</c>), so each table entry is the same bits.
    /// </summary>
    private static Matrix2x2d[] BuildQuintantRotationMatrices()
    {
        Matrix2x2d[] table = new Matrix2x2d[5];
        for(int quintant = 0; quintant < table.Length; quintant++)
        {
            double extraAngle = 2 * Constants.PiOver5 * quintant;
            table[quintant] = Matrix2x2d.FromRotation(-extraAngle);
        }

        return table;
    }
}
