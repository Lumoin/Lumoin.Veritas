using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Lumoin.Veritas.Core;

/// <summary>
/// Production and test <see cref="RandomnessDelegate"/> defaults. This is the one
/// sanctioned place in the codebase that reaches for raw entropy
/// (<see cref="RandomNumberGenerator"/>, <see cref="Guid.NewGuid"/>); every other
/// component consumes a <see cref="RandomnessDelegate"/> so its randomness is
/// injected, deterministic-when-wanted, and observable.
/// </summary>
public static class VeritasRandomness
{
    /// <summary>Real cryptographic entropy. The production default.</summary>
    public static RandomnessDelegate System { get; } = SystemImpl;

    /// <summary>All-zero output (0.0, <see cref="Guid.Empty"/>, zero-filled bytes). Use only in tests where constant randomness is acceptable.</summary>
    public static RandomnessDelegate Zero { get; } = ZeroImpl;

    /// <summary>
    /// Returns a delegate that derives a reproducible value from the seed and the
    /// request's <see cref="RandomnessRequest.CallSiteSalt"/>, so the same seed and
    /// the same call sites replay the same values across runs.
    /// </summary>
    /// <param name="seed">The seed mixed with each request's salt.</param>
    /// <returns>A deterministic randomness delegate.</returns>
    public static RandomnessDelegate Seeded(ulong seed)
    {
        return new SeededRandomnessSource(seed).Next;
    }

    [SuppressMessage(
        "ApiDesign",
        "RS0030:Do not use banned APIs",
        Justification = "VeritasRandomness is the single sanctioned producer of raw entropy; RandomNumberGenerator and Guid.NewGuid are banned everywhere else so randomness flows through the injected RandomnessDelegate.")]
    private static RandomnessValue SystemImpl(in RandomnessRequest request)
    {
        switch(request.Kind)
        {
            case RandomnessKind.UniformDouble:
            {
                Span<byte> bytes = stackalloc byte[8];
                RandomNumberGenerator.Fill(bytes);

                return new RandomnessValue(RandomnessKind.UniformDouble, ToUnitDouble(BinaryPrimitives.ReadUInt64LittleEndian(bytes)), default, default);
            }

            case RandomnessKind.Uuid:
            {
                return new RandomnessValue(RandomnessKind.Uuid, Double: default, Guid.NewGuid(), default);
            }

            case RandomnessKind.Bytes:
            {
                byte[] buffer = new byte[Math.Max(0, request.ByteCount)];
                RandomNumberGenerator.Fill(buffer);

                return new RandomnessValue(RandomnessKind.Bytes, Double: default, default, buffer);
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unknown randomness kind.");
            }
        }
    }

    private static RandomnessValue ZeroImpl(in RandomnessRequest request)
    {
        return request.Kind switch
        {
            RandomnessKind.UniformDouble => new RandomnessValue(RandomnessKind.UniformDouble, 0.0, default, default),
            RandomnessKind.Uuid => new RandomnessValue(RandomnessKind.Uuid, Double: default, Guid.Empty, default),
            RandomnessKind.Bytes => new RandomnessValue(RandomnessKind.Bytes, Double: default, default, new byte[Math.Max(0, request.ByteCount)]),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unknown randomness kind.")
        };
    }

    private static RandomnessValue SeededImpl(ulong seed, in RandomnessRequest request)
    {
        ulong mixed = Mix(seed, request.CallSiteSalt.Span);

        switch(request.Kind)
        {
            case RandomnessKind.UniformDouble:
            {
                return new RandomnessValue(RandomnessKind.UniformDouble, ToUnitDouble(mixed), default, default);
            }

            case RandomnessKind.Uuid:
            {
                Span<byte> guidBytes = stackalloc byte[16];
                BinaryPrimitives.WriteUInt64LittleEndian(guidBytes, mixed);
                BinaryPrimitives.WriteUInt64LittleEndian(guidBytes[8..], Mix(mixed, request.CallSiteSalt.Span));

                return new RandomnessValue(RandomnessKind.Uuid, Double: default, new Guid(guidBytes), default);
            }

            case RandomnessKind.Bytes:
            {
                int count = Math.Max(0, request.ByteCount);
                byte[] buffer = new byte[count];
                ulong state = mixed;
                Span<byte> chunk = stackalloc byte[8];
                for(int offset = 0; offset < count; offset += 8)
                {
                    state = Mix(state, request.CallSiteSalt.Span);
                    BinaryPrimitives.WriteUInt64LittleEndian(chunk, state);
                    int take = Math.Min(8, count - offset);
                    chunk[..take].CopyTo(buffer.AsSpan(offset));
                }

                return new RandomnessValue(RandomnessKind.Bytes, Double: default, default, buffer);
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unknown randomness kind.");
            }
        }
    }

    /// <summary>Maps a 64-bit value to an <c>xsd:double</c> in [0.0, 1.0) using its top 53 bits.</summary>
    /// <param name="value">The bits to map.</param>
    /// <returns>A double in the half-open unit interval.</returns>
    private static double ToUnitDouble(ulong value)
    {
        return (value >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>A SplitMix64-finalised mix of a seed and a salt; deterministic and reasonably distributed.</summary>
    /// <param name="seed">The seed state.</param>
    /// <param name="salt">The salt bytes folded into the seed.</param>
    /// <returns>The mixed value.</returns>
    private static ulong Mix(ulong seed, ReadOnlySpan<byte> salt)
    {
        ulong hash = seed ^ 0xCBF29CE484222325UL;
        for(int i = 0; i < salt.Length; i++)
        {
            hash = (hash ^ salt[i]) * 0x100000001B3UL;
        }

        hash += 0x9E3779B97F4A7C15UL;
        ulong z = hash;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;

        return z ^ (z >> 31);
    }

    /// <summary>
    /// Carries the seed behind a <see cref="Seeded"/> delegate as explicit state so the
    /// produced <see cref="RandomnessDelegate"/> closes over no enclosing local.
    /// </summary>
    /// <param name="seed">The seed mixed with each request's salt.</param>
    private sealed class SeededRandomnessSource(ulong seed)
    {
        /// <summary>The seed mixed with each request's salt.</summary>
        private ulong Seed { get; } = seed;

        /// <summary>Derives the reproducible value for <paramref name="request"/>.</summary>
        /// <param name="request">The randomness request whose salt is mixed with the seed.</param>
        /// <returns>The deterministic value for the seed and the request's salt.</returns>
        public RandomnessValue Next(in RandomnessRequest request)
        {
            return SeededImpl(Seed, in request);
        }
    }
}
