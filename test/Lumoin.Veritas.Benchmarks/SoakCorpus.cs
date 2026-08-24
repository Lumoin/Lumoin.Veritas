using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Reads and writes a soak triple corpus to a flat throwaway file so a soak can
/// replay a saved corpus instead of regenerating it (reproducible runs) or run
/// on a real dataset dumped once. This is a benchmark FIXTURE, not engine
/// storage — the layout is deliberately trivial: a three-int header (magic,
/// version, triple count) then the triples as interleaved subject/predicate/
/// object <c>uint32</c>s in native byte order.
/// </summary>
internal static class SoakCorpus
{
    /// <summary>The file magic identifying a soak corpus dump.</summary>
    private const int Magic = 0x_534F_414B;

    /// <summary>The dump layout version.</summary>
    private const int Version = 1;

    /// <summary>Produces a triple corpus when no saved file exists.</summary>
    /// <returns>The generated corpus.</returns>
    internal delegate IReadOnlyList<EncodedTriple> Builder();

    /// <summary>Writes a corpus to a file, overwriting any existing one.</summary>
    /// <param name="triples">The corpus to write.</param>
    /// <param name="path">The destination path.</param>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> or <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The corpus is too large for the flat dump.</exception>
    public static void Save(IReadOnlyList<EncodedTriple> triples, string path)
    {
        ArgumentNullException.ThrowIfNull(triples);
        ArgumentNullException.ThrowIfNull(path);

        long words = (long)triples.Count * 3;
        if(words > int.MaxValue)
        {
            throw new ArgumentException("The corpus is too large for the flat soak dump.", nameof(triples));
        }

        uint[] flat = new uint[(int)words];
        for(int i = 0; i < triples.Count; i++)
        {
            EncodedTriple triple = triples[i];
            flat[(i * 3) + 0] = triple.Subject.Encoded;
            flat[(i * 3) + 1] = triple.Predicate.Encoded;
            flat[(i * 3) + 2] = triple.Object.Encoded;
        }

        int[] header = [Magic, Version, triples.Count];
        using FileStream stream = File.Create(path);
        stream.Write(MemoryMarshal.AsBytes(header.AsSpan()));
        stream.Write(MemoryMarshal.AsBytes(flat.AsSpan()));
    }

    /// <summary>Reads a corpus written by <see cref="Save"/>.</summary>
    /// <param name="path">The source path.</param>
    /// <returns>The corpus.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The file is not a soak corpus, is a different version, or its count is out of range.</exception>
    public static List<EncodedTriple> Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using FileStream stream = File.OpenRead(path);
        int[] header = new int[3];
        stream.ReadExactly(MemoryMarshal.AsBytes(header.AsSpan()));
        if(header[0] != Magic)
        {
            throw new InvalidDataException("The file is not a soak corpus dump.");
        }

        if(header[1] != Version)
        {
            throw new InvalidDataException($"Unsupported soak corpus version {header[1]}.");
        }

        int count = header[2];
        long words = (long)count * 3;
        if(count < 0 || words > int.MaxValue)
        {
            throw new InvalidDataException("The soak corpus header count is out of range.");
        }

        uint[] flat = new uint[(int)words];
        stream.ReadExactly(MemoryMarshal.AsBytes(flat.AsSpan()));

        List<EncodedTriple> triples = new(count);
        for(int i = 0; i < count; i++)
        {
            triples.Add(EncodedTriple.FromEncoded(flat[(i * 3) + 0], flat[(i * 3) + 1], flat[(i * 3) + 2]));
        }

        return triples;
    }

    /// <summary>Loads the corpus from <paramref name="path"/> when it exists, otherwise builds it, saves it, and returns it.</summary>
    /// <param name="path">The cache path.</param>
    /// <param name="build">The generator used when the file is absent.</param>
    /// <returns>The corpus.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static IReadOnlyList<EncodedTriple> LoadOrGenerate(string path, Builder build)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(build);

        if(File.Exists(path))
        {
            return Load(path);
        }

        IReadOnlyList<EncodedTriple> triples = build();
        Save(triples, path);

        return triples;
    }

    /// <summary>Round-trips a small known corpus through a temp file and reports whether save/load preserved it.</summary>
    public static void RunSelfTest()
    {
        List<EncodedTriple> original = new(1_000);
        for(uint i = 0; i < 1_000; i++)
        {
            original.Add(EncodedTriple.FromEncoded(i * 3, 1_000, (i * 3) + 1));
        }

        string path = Path.Combine(Path.GetTempPath(), "veritas-soak-corpus-selftest.bin");
        try
        {
            Save(original, path);
            List<EncodedTriple> loaded = Load(path);

            bool match = loaded.Count == original.Count;
            for(int i = 0; match && i < original.Count; i++)
            {
                match = loaded[i].Equals(original[i]);
            }

            long size = new FileInfo(path).Length;
            Console.WriteLine($"[soak-corpus] round-trip {(match ? "OK" : "FAILED")}: {original.Count:N0} triples, {size:N0} bytes ({size / (double)original.Count:F1} bytes/triple)");
        }
        finally
        {
            if(File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
