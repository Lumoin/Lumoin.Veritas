using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.Wasm;
using System.Runtime.Intrinsics.X86;
using Lumoin.Veritas.Geo.Dggs;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Behavior tests for the SIMD ladder on <see cref="A5PointToCellKernelSelection"/> and its dispatch
    /// facade (<see cref="SimdPointToCellKernel"/>): capability-ordered selection, the pinned scalar
    /// <c>Default</c>, throwing per-ISA opt-ins, stable delegate references, and agreement of every
    /// supported public rung with the scalar reference over pinned points. The deep bit-identity gates
    /// live in <see cref="A5PointToCellBatchCoreBitIdentityTests"/>.
    /// </summary>
    [TestClass]
    internal sealed class A5PointToCellKernelLadderTests
    {
        /// <summary>
        /// Pinned agreement points: capitals across faces, the origin, near-pole and near-antimeridian
        /// coordinates (the spiral-fallback-reachable classes), and exact face-boundary-ish values.
        /// </summary>
        private static LonLat[] PinnedPoints { get; } =
        [
            new(0, 0),
            new(24.9384, 60.1699),
            new(-74.006, 40.7128),
            new(139.7624, 35.6774),
            new(-43.1729, -22.9068),
            new(151.2093, -33.8688),
            new(18.4241, -33.9249),
            new(-149.4937, -17.6797),
            new(179.9999, 0.5),
            new(-179.9999, -0.5),
            new(0.1, 89.9),
            new(-0.1, -89.9),
            new(93, 45),
            new(-87, -45)
        ];

        /// <summary>The resolutions the pinned agreement runs across (Hilbert range boundaries included).</summary>
        private static int[] PinnedResolutions { get; } = [2, 10, 15, 29, 30];

        /// <summary>Pins that SimdPointToCellKernel.IsSupported equals the logical OR of every per-ISA backend's IsSupported flag.</summary>
        [TestMethod]
        public void FacadeIsSupportedReflectsOrOfBackends()
        {
            bool expected =
                Avx512PointToCellKernelBackend.IsSupported
                || Avx2PointToCellKernelBackend.IsSupported
                || NeonPointToCellKernelBackend.IsSupported
                || WasmPackedSimdPointToCellKernelBackend.IsSupported;

            Assert.AreEqual(expected, SimdPointToCellKernel.IsSupported);
        }

        /// <summary>Pins that the facade selects the highest-capability backend's delegate, verified by method identity, in AVX-512, AVX2, NEON, WASM order.</summary>
        [TestMethod]
        public void FacadePicksHighestAvailableBackend()
        {
            if(!SimdPointToCellKernel.IsSupported)
            {
                Assert.Inconclusive("No SIMD backend is supported on this host CPU; facade selection cannot be exercised.");

                return;
            }

            A5PointToCellKernel facadeKernel = SimdPointToCellKernel.GetPointToCell();

            // The facade returns the delegate produced by the highest-capability backend; delegate
            // Method identity verifies the selection rather than just behaviour.
            if(Avx512F.IsSupported && Vector512.IsHardwareAccelerated)
            {
                Assert.AreEqual(Avx512PointToCellKernelBackend.GetPointToCell().Method, facadeKernel.Method,
                    "Facade should select AVX-512 when AVX-512F with accelerated Vector512 is supported.");

                return;
            }

            if(Avx2.IsSupported && Vector256.IsHardwareAccelerated)
            {
                Assert.AreEqual(Avx2PointToCellKernelBackend.GetPointToCell().Method, facadeKernel.Method,
                    "Facade should select AVX2 when AVX-512 is not supported but AVX2 is.");

                return;
            }

            if(AdvSimd.Arm64.IsSupported)
            {
                Assert.AreEqual(NeonPointToCellKernelBackend.GetPointToCell().Method, facadeKernel.Method,
                    "Facade should select NEON when only AArch64 NEON is supported.");

                return;
            }

            if(PackedSimd.IsSupported)
            {
                Assert.AreEqual(WasmPackedSimdPointToCellKernelBackend.GetPointToCell().Method, facadeKernel.Method,
                    "Facade should select WASM packed SIMD when only the WASM 128-bit SIMD proposal is supported.");

                return;
            }

            Assert.Fail("SimdPointToCellKernel.IsSupported was true but no specific backend matched.");
        }

        /// <summary>Pins that A5PointToCellKernelSelection.Simd resolves to the facade's rung when SIMD is supported, else falls back to the scalar reference.</summary>
        [TestMethod]
        public void SimdSelectionEqualsFacadeWhenSupportedElseScalarBehaviour()
        {
            A5PointToCellKernel simd = A5PointToCellKernelSelection.Simd;

            if(SimdPointToCellKernel.IsSupported)
            {
                Assert.AreEqual(SimdPointToCellKernel.GetPointToCell().Method, simd.Method,
                    "Simd should resolve to the facade's highest rung when SIMD is supported.");
            }
            else
            {
                Assert.AreEqual(A5PointToCellKernelSelection.Scalar.Method, simd.Method,
                    "Simd should fall back to the scalar reference when no SIMD rung is supported.");
            }
        }

        /// <summary>Pins that Default remains the scalar reference kernel and never coincides with Simd when SIMD is supported.</summary>
        [TestMethod]
        public void DefaultStaysThePinnedScalarReference()
        {
            Assert.AreEqual(A5PointToCellKernelSelection.Scalar.Method, A5PointToCellKernelSelection.Default.Method,
                "Default must remain the scalar reference kernel — never silently displaced by a faster backend.");

            if(SimdPointToCellKernel.IsSupported)
            {
                Assert.AreNotEqual(A5PointToCellKernelSelection.Simd.Method, A5PointToCellKernelSelection.Default.Method,
                    "With SIMD supported, Simd must be a different kernel than the pinned Default.");
            }
        }

        /// <summary>Pins that reading a supported per-ISA rung property twice returns the same delegate reference each time.</summary>
        [TestMethod]
        public void SupportedPerIsaPropertiesReturnStableReferences()
        {
            int exercisedCount = 0;

            if(Avx512PointToCellKernelBackend.IsSupported)
            {
                A5PointToCellKernel first = A5PointToCellKernelSelection.Avx512;
                A5PointToCellKernel second = A5PointToCellKernelSelection.Avx512;

                Assert.AreSame(first, second);
                exercisedCount++;
            }

            if(Avx2PointToCellKernelBackend.IsSupported)
            {
                A5PointToCellKernel first = A5PointToCellKernelSelection.Avx2;
                A5PointToCellKernel second = A5PointToCellKernelSelection.Avx2;

                Assert.AreSame(first, second);
                exercisedCount++;
            }

            if(NeonPointToCellKernelBackend.IsSupported)
            {
                A5PointToCellKernel first = A5PointToCellKernelSelection.Neon;
                A5PointToCellKernel second = A5PointToCellKernelSelection.Neon;

                Assert.AreSame(first, second);
                exercisedCount++;
            }

            if(WasmPackedSimdPointToCellKernelBackend.IsSupported)
            {
                A5PointToCellKernel first = A5PointToCellKernelSelection.WasmPackedSimd;
                A5PointToCellKernel second = A5PointToCellKernelSelection.WasmPackedSimd;

                Assert.AreSame(first, second);
                exercisedCount++;
            }

            if(exercisedCount == 0)
            {
                Assert.Inconclusive("No SIMD rung is supported on this host; reference stability cannot be exercised.");
            }
        }

        /// <summary>Pins that reading an unsupported per-ISA rung property throws PlatformNotSupportedException.</summary>
        [TestMethod]
        public void UnsupportedPerIsaPropertiesThrowPlatformNotSupported()
        {
            int exercisedCount = 0;

            if(!Avx512PointToCellKernelBackend.IsSupported)
            {
                exercisedCount += AssertRungThrows(static () => A5PointToCellKernelSelection.Avx512);
            }

            if(!Avx2PointToCellKernelBackend.IsSupported)
            {
                exercisedCount += AssertRungThrows(static () => A5PointToCellKernelSelection.Avx2);
            }

            if(!NeonPointToCellKernelBackend.IsSupported)
            {
                exercisedCount += AssertRungThrows(static () => A5PointToCellKernelSelection.Neon);
            }

            if(!WasmPackedSimdPointToCellKernelBackend.IsSupported)
            {
                exercisedCount += AssertRungThrows(static () => A5PointToCellKernelSelection.WasmPackedSimd);
            }

            if(exercisedCount == 0)
            {
                Assert.Inconclusive("Every SIMD rung is supported on this host; the unsupported-rung throw cannot be exercised.");
            }
        }

        /// <summary>Pins that every supported public rung agrees with the scalar reference over the pinned agreement points at every pinned resolution.</summary>
        [TestMethod]
        public void EverySupportedPublicRungAgreesWithScalarOverPinnedPoints()
        {
            double[] source = new double[2 * PinnedPoints.Length];
            for(int index = 0; index < PinnedPoints.Length; index++)
            {
                source[2 * index] = PinnedPoints[index].Longitude;
                source[(2 * index) + 1] = PinnedPoints[index].Latitude;
            }

            List<(string Name, A5PointToCellKernel Kernel)> rungs = [("Simd", A5PointToCellKernelSelection.Simd)];
            if(Avx512PointToCellKernelBackend.IsSupported)
            {
                rungs.Add(("Avx512", A5PointToCellKernelSelection.Avx512));
            }

            if(Avx2PointToCellKernelBackend.IsSupported)
            {
                rungs.Add(("Avx2", A5PointToCellKernelSelection.Avx2));
            }

            if(NeonPointToCellKernelBackend.IsSupported)
            {
                rungs.Add(("Neon", A5PointToCellKernelSelection.Neon));
            }

            if(WasmPackedSimdPointToCellKernelBackend.IsSupported)
            {
                rungs.Add(("WasmPackedSimd", A5PointToCellKernelSelection.WasmPackedSimd));
            }

            A5CellId[] scalarCells = new A5CellId[PinnedPoints.Length];
            A5CellId[] rungCells = new A5CellId[PinnedPoints.Length];
            foreach(int resolution in PinnedResolutions)
            {
                A5PointToCellKernelSelection.Scalar(source, resolution, scalarCells);
                foreach((string name, A5PointToCellKernel kernel) in rungs)
                {
                    kernel(source, resolution, rungCells);
                    Assert.AreSequenceEqual(scalarCells, rungCells, $"Rung {name} diverged from Scalar at resolution {resolution}.");
                }
            }
        }

        /// <summary>
        /// Asserts that reading a rung property throws <see cref="PlatformNotSupportedException"/>;
        /// returns 1 so callers can count exercised rungs.
        /// </summary>
        private static int AssertRungThrows(RungAccessor accessRung)
        {
            bool threw = false;
            try
            {
                _ = accessRung();
            }
            catch(PlatformNotSupportedException)
            {
                threw = true;
            }

            Assert.IsTrue(threw, "An unsupported rung property must throw PlatformNotSupportedException.");

            return 1;
        }

        /// <summary>Reads one ladder rung property (used to defer the read into the throw assertion).</summary>
        private delegate A5PointToCellKernel RungAccessor();
    }
}
