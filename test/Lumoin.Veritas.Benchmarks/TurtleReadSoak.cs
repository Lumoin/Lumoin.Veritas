using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Turtle;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// A deterministic allocation-and-throughput soak for <see cref="TurtleReader"/> ingest, the anchor
/// measurement for the parser performance pass. It parses a representative generated Turtle corpus a fixed
/// number of times and reports bytes allocated per parse (via <see cref="GC.GetTotalAllocatedBytes(bool)"/>,
/// which is exact, unlike a BenchmarkDotNet MemoryDiagnoser short run over a heavy async parse) plus
/// wall-clock per parse. Run with <c>dotnet run -c Release -- --profile-turtle-read</c>.
/// </summary>
public static class TurtleReadSoak
{
    /// <summary>The number of subjects the generated corpus declares; each contributes several triples.</summary>
    private const int SubjectCount = 20_000;

    /// <summary>The number of timed parses; allocation is exact so a modest count gives a stable mean.</summary>
    private const int Iterations = 20;

    /// <summary>Builds the corpus once, warms up, then measures allocation and time per parse over <see cref="Iterations"/> runs.</summary>
    /// <returns>A task that completes when the soak has run and reported.</returns>
    public static async Task RunTurtleReadSoakAsync()
    {
        byte[] corpus = BuildCorpus();

        //Warm up the JIT and the type system so the measured runs see steady-state allocation.
        long warmQuads = 0;
        for(int i = 0; i < 3; i++)
        {
            warmQuads += await DrainOnceAsync(corpus).ConfigureAwait(false);
        }

        long quadsPerParse = warmQuads / 3;

        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        Stopwatch stopwatch = Stopwatch.StartNew();
        for(int i = 0; i < Iterations; i++)
        {
            await DrainOnceAsync(corpus).ConfigureAwait(false);
        }

        stopwatch.Stop();
        long allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);

        double bytesPerParse = (double)(allocatedAfter - allocatedBefore) / Iterations;
        double millisecondsPerParse = stopwatch.Elapsed.TotalMilliseconds / Iterations;

        Console.WriteLine($"Turtle read soak — corpus {corpus.Length:N0} bytes, {quadsPerParse:N0} quads/parse, {Iterations} iterations");
        Console.WriteLine($"  allocated: {bytesPerParse:N0} bytes/parse ({bytesPerParse / (1024 * 1024):F2} MB/parse, {bytesPerParse / quadsPerParse:F0} bytes/quad)");
        Console.WriteLine($"  time:      {millisecondsPerParse:F2} ms/parse");
    }

    /// <summary>Parses the corpus once, draining the async iterator (the in-memory source completes synchronously), and returns the quad count.</summary>
    /// <param name="corpus">The corpus bytes.</param>
    /// <returns>A task producing the number of quads parsed.</returns>
    private static async Task<long> DrainOnceAsync(byte[] corpus)
    {
        DiagnosticBag diagnostics = new();
        long count = 0;

        await foreach(Quad _ in TurtleReader.ReadAsync(corpus, TurtleSyntax.Turtle, diagnostics).ConfigureAwait(false))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Builds the mixed UTF-8 Turtle corpus: every subject emits three leaf triples; every sixteenth adds a
    /// collection and a blank-node property list (the compound term-resolution path); every sixty-fourth
    /// adds an escaped literal.
    /// </summary>
    /// <returns>The corpus bytes.</returns>
    private static byte[] BuildCorpus()
    {
        StringBuilder builder = new(SubjectCount * 200);
        builder.Append("@prefix ex: <http://example.org/> .\n");
        builder.Append("@prefix xsd: <http://www.w3.org/2001/XMLSchema#> .\n");

        for(int i = 0; i < SubjectCount; i++)
        {
            builder.Append("ex:s").Append(i).Append(" ex:p ex:o").Append(i).Append(" ;\n");
            builder.Append("  ex:name \"name ").Append(i).Append("\" ;\n");
            builder.Append("  ex:count \"").Append(i).Append("\"^^xsd:integer .\n");

            if(i % 16 == 0)
            {
                builder.Append("ex:s").Append(i).Append(" ex:list ( ex:a ex:b ex:c ) ;\n");
                builder.Append("  ex:meta [ ex:k \"v\" ; ex:n \"").Append(i).Append("\" ] .\n");
            }

            if(i % 64 == 0)
            {
                builder.Append("ex:s").Append(i).Append(" ex:note \"line1\\nline2\\t\\\"quoted\\\"\" .\n");
            }
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
