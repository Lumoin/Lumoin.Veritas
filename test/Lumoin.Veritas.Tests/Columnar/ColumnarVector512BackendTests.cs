using System;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The 512-bit codec backend is differentially equivalent to the portable reference: for arbitrary packed
/// payloads, bit widths, anchors, exception sets, and frame bases, its whole-block prefixed-delta decode and its
/// frame-of-reference decode produce byte-identical output to <see cref="ColumnarPortableBackend"/>. The bundle is
/// reached through <see cref="ColumnarVector512Backend.BackendUnchecked"/>, so the check runs on every host —
/// <see cref="System.Runtime.Intrinsics.Vector512{T}"/> operations are software-emulated where AVX-512 is absent,
/// and exercise the native kernels where it is present.
/// </summary>
[TestClass]
internal sealed class ColumnarVector512BackendTests
{
    /// <summary>A deterministic 64-bit mixer standing in for randomness — entropy seams stay untouched in tests too.</summary>
    /// <param name="state">The counter to mix.</param>
    /// <returns>The mixed value.</returns>
    private static ulong Mix(ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            state = (state ^ (state >> 30)) * 0xBF58476D1CE4E5B9UL;
            state = (state ^ (state >> 27)) * 0x94D049BB133111EBUL;

            return state ^ (state >> 31);
        }
    }

    /// <summary>Both decode kernels match the portable reference across block lengths (including the vector-tail boundaries 16/17), bit widths, and exception sets.</summary>
    [TestMethod]
    public void DecodeKernelsMatchThePortableReference()
    {
        ColumnarKernelBackend v512 = ColumnarVector512Backend.BackendUnchecked;
        ColumnarKernelBackend portable = ColumnarPortableBackend.Backend;
        ulong state = 1;

        foreach(int count in (int[])[1, 7, 16, 17, 33, 64, 1000])
        {
            foreach(int bitWidth in (int[])[0, 1, 5, 16, 31, 32])
            {
                int words = ((count * bitWidth) / 64) + 2;
                ulong[] payload = new ulong[words];
                for(int w = 0; w < words; w++)
                {
                    state = Mix(state);
                    payload[w] = state;
                }

                //Lane 0's zigzag delta is zero by construction (the anchor is the first value): the scalar
                //reference treats lane 0 as the anchor while the vector kernel un-zigzags it, so they agree only
                //under that invariant. The differential input must honour it — zero lane 0's packed bits, and keep
                //exceptions off lane 0.
                if(bitWidth > 0)
                {
                    ulong laneZeroMask = bitWidth == 32 ? uint.MaxValue : (1UL << bitWidth) - 1;
                    payload[0] &= ~laneZeroMask;
                }

                state = Mix(state);
                uint anchor = (uint)state;

                int exceptionCount = Math.Min(3, Math.Max(0, count - 1));
                ushort[] exceptionPositions = new ushort[exceptionCount];
                uint[] exceptionValues = new uint[exceptionCount];
                for(int e = 0; e < exceptionCount; e++)
                {
                    exceptionPositions[e] = (ushort)(1 + e);
                    state = Mix(state);
                    exceptionValues[e] = (uint)state;
                }

                uint[] decodedVector = new uint[count];
                uint[] decodedPortable = new uint[count];
                v512.Decode(payload, bitWidth, anchor, exceptionPositions, exceptionValues, decodedVector);
                portable.Decode(payload, bitWidth, anchor, exceptionPositions, exceptionValues, decodedPortable);
                Assert.IsTrue(decodedVector.AsSpan().SequenceEqual(decodedPortable), $"Prefixed-delta decode diverged from portable (count {count}, bit width {bitWidth}).");

                state = Mix(state);
                uint frameBase = (uint)state;
                uint[] frameVector = new uint[count];
                uint[] framePortable = new uint[count];
                v512.DecodeFrame(payload, bitWidth, frameBase, frameVector);
                portable.DecodeFrame(payload, bitWidth, frameBase, framePortable);
                Assert.IsTrue(frameVector.AsSpan().SequenceEqual(framePortable), $"Frame-of-reference decode diverged from portable (count {count}, bit width {bitWidth}).");
            }
        }
    }
}
