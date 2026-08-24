using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The load-path census harness for the RDF/XML corpus pipeline: it drives every deduped
/// Direct-corpus premise through the same stage chain the reasoner triage loads with —
/// the stored-bytes read, <see cref="RdfXmlReader.Read"/>, import expansion, and the OWL
/// RDF mapping — over repeated whole-corpus passes, timing each stage and reading the precise
/// per-pass allocated-byte total, and writes the census table to the configured output
/// path. Every pass also folds the parsed quad stream AND the import-expanded quad stream
/// into order-sensitive corpus hashes; the passes must agree on both hashes and on every
/// count, so the printed hashes are citable identities for the parse and expansion outputs
/// that a later run can be diffed against. It is
/// opt-in measurement scaffolding, not a correctness gate: it runs only when the
/// <c>VERITAS_RDFXML_LOAD_CENSUS</c> environment variable names an output file, staying
/// out of the normal suite's wall time. Run it in Release in a contiguous block with no
/// concurrent builds or suites for citable numbers.
/// </summary>
[TestClass]
internal sealed class RdfXmlCorpusLoadCensus
{
    /// <summary>The environment variable naming the absolute output path; unset means the harness skips.</summary>
    private const string OutputPathVariable = "VERITAS_RDFXML_LOAD_CENSUS";

    /// <summary>The number of timed whole-corpus passes; the loadable-collection pass before them is the warm pass, and the later passes' wall clocks are the citable ones once tiered compilation has quiesced.</summary>
    private const int TimedPassCount = 10;

    /// <summary>The number of premises listed in the slowest-premises block, taken from the final pass.</summary>
    private const int SlowestPremiseCount = 10;

    /// <summary>The FNV-1a 64-bit offset basis the corpus hash starts from.</summary>
    private const ulong FnvOffsetBasis = 14695981039346656037UL;

    /// <summary>The FNV-1a 64-bit prime the corpus hash multiplies by per byte.</summary>
    private const ulong FnvPrime = 1099511628211UL;

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Loads every deduped, loadable Direct-corpus premise through the full stage chain
    /// across <see cref="TimedPassCount"/> timed passes and writes the per-pass census
    /// table to the configured path, asserting every pass produced the identical quad
    /// count, axiom count, and corpus hash.
    /// </summary>
    [TestMethod]
    public void MeasureRdfXmlLoadAcrossDirectCorpus()
    {
        string? outputPath = Environment.GetEnvironmentVariable(OutputPathVariable);
        if(string.IsNullOrWhiteSpace(outputPath))
        {
            //Opt-in measurement scaffolding, not a correctness gate: with no output path
            //configured the harness has nothing to do and the test passes without
            //measuring. Set VERITAS_RDFXML_LOAD_CENSUS to run it.
            TestContext.WriteLine($"Skipping the load census harness: set {OutputPathVariable} to an absolute output path to run it.");

            return;
        }

        string manifestPath = W3cCorpusPath.For("Owl2", "approved", "all.rdf");
        ManifestStage manifestCold = LoadManifest(manifestPath, out ImmutableArray<Owl2TestCase> cases);
        List<Owl2TestCase> loadable = CollectLoadablePremises(cases, out int premiseCaseCount, out int dedupedCount, out int skippedCount);
        Assert.IsNotEmpty(loadable, "The manifest yielded no loadable premises; the vendored corpus is a precondition of the census.");

        //The steady reload separates the manifest document's own parse cost from the
        //cold line's first-touch JIT: the manifest is one large RDF/XML document going
        //through the same buffered reader the premises do. The parse-only stage then
        //isolates the reader itself from the loader's case indexing.
        ManifestStage manifestSteady = LoadManifest(manifestPath, out _);
        ManifestStage manifestParseOnly = ParseManifestOnly(manifestPath);

        List<PassOutcome> passes = [];
        List<PremiseTiming> finalPassTimings = [];
        Stack<RdfTerm> hashScratch = new();
        for(int pass = 1; pass <= TimedPassCount; pass++)
        {
            List<PremiseTiming>? premiseTimingsToAppendTo = pass == TimedPassCount ? finalPassTimings : null;
            PassOutcome outcome = RunPass(loadable, hashScratch, premiseTimingsToAppendTo);
            passes.Add(outcome);
            TestContext.WriteLine(FormatPassLine(pass, outcome));
        }

        PassOutcome reference = passes[0];
        foreach(PassOutcome pass in passes)
        {
            Assert.AreEqual(reference.PremiseByteCount, pass.PremiseByteCount, "The passes disagreed on the total premise byte count.");
            Assert.AreEqual(reference.QuadCount, pass.QuadCount, "The passes disagreed on the parsed quad count.");
            Assert.AreEqual(reference.ExpandedQuadCount, pass.ExpandedQuadCount, "The passes disagreed on the import-expanded quad count.");
            Assert.AreEqual(reference.AxiomCount, pass.AxiomCount, "The passes disagreed on the mapped axiom count.");
            Assert.AreEqual(reference.CorpusHash, pass.CorpusHash, "The passes disagreed on the corpus quad hash; the parse is expected to be deterministic.");
            Assert.AreEqual(reference.ExpandedCorpusHash, pass.ExpandedCorpusHash, "The passes disagreed on the expanded corpus quad hash; import expansion is expected to be deterministic.");
        }

        string table = BuildTable(passes, finalPassTimings, manifestCold, manifestSteady, manifestParseOnly, new FileInfo(manifestPath).Length, cases.Length, premiseCaseCount, dedupedCount, skippedCount, loadable.Count);
        File.WriteAllText(outputPath, table);
        TestContext.WriteLine(table);
    }

    /// <summary>One manifest load's cost: the milliseconds and the precise allocated-byte delta across the load.</summary>
    /// <param name="Milliseconds">The load milliseconds.</param>
    /// <param name="AllocatedBytes">The precise process allocated-byte delta across the load.</param>
    private readonly record struct ManifestStage(double Milliseconds, long AllocatedBytes);

    /// <summary>Loads the manifest with the load bracketed by a timestamp pair and a precise allocated-byte pair.</summary>
    /// <param name="manifestPath">The manifest file path.</param>
    /// <param name="cases">Receives the loaded cases.</param>
    /// <returns>The load's cost.</returns>
    private static ManifestStage LoadManifest(string manifestPath, out ImmutableArray<Owl2TestCase> cases)
    {
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        long start = Stopwatch.GetTimestamp();
        cases = Owl2ManifestLoader.Load(manifestPath);
        long elapsed = Stopwatch.GetTimestamp() - start;

        return new ManifestStage(TicksToMilliseconds(elapsed), GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore);
    }

    /// <summary>Parses the manifest document through <see cref="RdfXmlReader.Read"/> alone — no case indexing — bracketed the same way, isolating the reader's own share of the manifest load.</summary>
    /// <param name="manifestPath">The manifest file path.</param>
    /// <returns>The parse-only cost.</returns>
    private static ManifestStage ParseManifestOnly(string manifestPath)
    {
        byte[] bytes = File.ReadAllBytes(manifestPath);
        string baseIri = new Uri(Path.GetFullPath(manifestPath)).AbsoluteUri;
        DiagnosticBag diagnostics = new();
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        long start = Stopwatch.GetTimestamp();
        IReadOnlyList<Quad> parsed = RdfXmlReader.Read(bytes, diagnostics, Utf8Strings.From(baseIri));
        long elapsed = Stopwatch.GetTimestamp() - start;
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        Assert.IsFalse(diagnostics.HasErrors, "The manifest failed to parse in the parse-only stage.");
        Assert.IsNotEmpty(parsed, "The manifest parse-only stage produced no quads.");

        return new ManifestStage(TicksToMilliseconds(elapsed), allocated);
    }

    /// <summary>One timed whole-corpus pass's outcome: the per-stage milliseconds, the precise allocated bytes (whole pass, the parse stage alone, and the import-expansion stage alone), the counts, and the corpus quad hashes.</summary>
    /// <param name="ReadMilliseconds">The milliseconds spent reading the stored premise bytes.</param>
    /// <param name="ParseMilliseconds">The milliseconds spent in <see cref="RdfXmlReader.Read"/>.</param>
    /// <param name="ExpandMilliseconds">The milliseconds spent in <see cref="Owl2ImportResolver.Expand"/>.</param>
    /// <param name="MapMilliseconds">The milliseconds spent in the OWL RDF mapping.</param>
    /// <param name="AllocatedBytes">The precise process allocated-byte delta across the pass.</param>
    /// <param name="ParseAllocatedBytes">The precise process allocated-byte delta accumulated over the <see cref="RdfXmlReader.Read"/> calls alone, isolating the parse stage from the stored-bytes read, import expansion, and mapping.</param>
    /// <param name="ExpandAllocatedBytes">The precise process allocated-byte delta accumulated over the <see cref="Owl2ImportResolver.Expand"/> calls alone, isolating the import-expansion stage.</param>
    /// <param name="PremiseByteCount">The total UTF-8 byte count of the stored premises.</param>
    /// <param name="QuadCount">The total parsed quad count across the premises, before import expansion.</param>
    /// <param name="ExpandedQuadCount">The total quad count after import expansion.</param>
    /// <param name="AxiomCount">The total mapped axiom count across the premises.</param>
    /// <param name="CorpusHash">The order-sensitive FNV-1a hash folded over every premise's parsed quad stream in corpus order.</param>
    /// <param name="ExpandedCorpusHash">The order-sensitive FNV-1a hash folded over every premise's import-expanded quad stream in corpus order, certifying the expansion's structural identity including merged-import blank labels.</param>
    private readonly record struct PassOutcome(
        double ReadMilliseconds,
        double ParseMilliseconds,
        double ExpandMilliseconds,
        double MapMilliseconds,
        long AllocatedBytes,
        long ParseAllocatedBytes,
        long ExpandAllocatedBytes,
        long PremiseByteCount,
        int QuadCount,
        int ExpandedQuadCount,
        int AxiomCount,
        ulong CorpusHash,
        ulong ExpandedCorpusHash);

    /// <summary>One premise's final-pass timing for the slowest-premises block.</summary>
    /// <param name="Identifier">The premise's manifest identifier.</param>
    /// <param name="ParseMilliseconds">The premise's <see cref="RdfXmlReader.Read"/> milliseconds in the final pass.</param>
    /// <param name="QuadCount">The premise's parsed quad count.</param>
    private readonly record struct PremiseTiming(string Identifier, double ParseMilliseconds, int QuadCount);

    /// <summary>
    /// Collects the deduped premises that parse, import-expand, and map cleanly, mirroring
    /// the reasoner triage's load flow so the census times exactly the premises the triage
    /// decides. This collection pass parses and maps every premise once, so it doubles as
    /// the warm pass before the timed passes.
    /// </summary>
    /// <param name="cases">The loaded manifest.</param>
    /// <param name="premiseCaseCount">Receives the number of cases carrying a premise.</param>
    /// <param name="dedupedCount">Receives the number of cases whose premise text duplicated an earlier case's.</param>
    /// <param name="skippedCount">Receives the number of premises that failed to parse or map.</param>
    /// <returns>The loadable premises in manifest order.</returns>
    private static List<Owl2TestCase> CollectLoadablePremises(ImmutableArray<Owl2TestCase> cases, out int premiseCaseCount, out int dedupedCount, out int skippedCount)
    {
        List<Owl2TestCase> loadable = [];
        HashSet<Utf8String> seenPremises = [];
        premiseCaseCount = 0;
        dedupedCount = 0;
        skippedCount = 0;
        foreach(Owl2TestCase testCase in cases)
        {
            if(testCase.RdfXmlPremise is not { } premiseText)
            {
                continue;
            }

            premiseCaseCount++;

            //First case name wins for identical premise text; a later case carrying
            //byte-identical premise adds no new load profile.
            if(!seenPremises.Add(premiseText))
            {
                dedupedCount++;

                continue;
            }

            if(!TryLoad(testCase))
            {
                skippedCount++;

                continue;
            }

            loadable.Add(testCase);
        }

        return loadable;
    }

    /// <summary>Runs the full load stage chain over one premise, reporting failure instead of asserting so an unparseable or unmappable premise is skipped and counted rather than failing the run.</summary>
    /// <param name="testCase">The case whose premise to load.</param>
    /// <returns><see langword="true"/> when the premise parsed, import-expanded, and mapped cleanly.</returns>
    private static bool TryLoad(Owl2TestCase testCase)
    {
        try
        {
            DiagnosticBag diagnostics = new();
            List<Quad> quads =
            [
                .. RdfXmlReader.Read(testCase.RdfXmlPremise!.Value.Memory, diagnostics, baseIri: Utf8Strings.From(testCase.Uri.AbsoluteUri)),
            ];
            if(diagnostics.HasErrors)
            {
                return false;
            }

            quads = Owl2ImportResolver.Expand(testCase, quads);
            OwlOntologyDocument premise = OwlRdfMapper.Map(quads);

            return !premise.Diagnostics.HasErrors;
        }
        catch(Exception exception) when(exception is FormatException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Runs one timed whole-corpus pass: every loadable premise through the stored-bytes
    /// read, parse, import expansion, and mapping, with per-stage timestamps, the precise
    /// allocated-byte deltas bracketing the pass and the parse and expansion stages,
    /// and the corpus hashes folded over the parsed and expanded quads in corpus order.
    /// </summary>
    /// <param name="loadable">The loadable premises in manifest order.</param>
    /// <param name="hashScratch">The reusable term work-stack the quad hash walks with.</param>
    /// <param name="premiseTimingsToAppendTo">The collection receiving per-premise parse timings, or <see langword="null"/> on a pass that records none.</param>
    /// <returns>The pass outcome.</returns>
    private static PassOutcome RunPass(List<Owl2TestCase> loadable, Stack<RdfTerm> hashScratch, List<PremiseTiming>? premiseTimingsToAppendTo)
    {
        long readTicks = 0;
        long parseTicks = 0;
        long expandTicks = 0;
        long mapTicks = 0;
        long parseAllocated = 0;
        long expandAllocated = 0;
        long premiseByteCount = 0;
        int quadCount = 0;
        int expandedQuadCount = 0;
        int axiomCount = 0;
        ulong corpusHash = FnvOffsetBasis;
        ulong expandedCorpusHash = FnvOffsetBasis;
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        foreach(Owl2TestCase testCase in loadable)
        {
            //The read column times a stored-bytes read; its ~0 is the ELIMINATION of the
            //encode stage, never an encoding speedup. A banked table whose column says
            //"encode ms" timed a UTF-8 encode.
            long start = Stopwatch.GetTimestamp();
            ReadOnlyMemory<byte> premiseBytes = testCase.RdfXmlPremise!.Value.Memory;
            readTicks += Stopwatch.GetTimestamp() - start;
            premiseByteCount += premiseBytes.Length;

            DiagnosticBag diagnostics = new();
            long parseAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            start = Stopwatch.GetTimestamp();
            IReadOnlyList<Quad> parsed = RdfXmlReader.Read(premiseBytes, diagnostics, baseIri: Utf8Strings.From(testCase.Uri.AbsoluteUri));
            long parseElapsed = Stopwatch.GetTimestamp() - start;
            parseTicks += parseElapsed;
            parseAllocated += GC.GetTotalAllocatedBytes(precise: true) - parseAllocatedBefore;
            Assert.IsFalse(diagnostics.HasErrors, $"Premise '{testCase.Identifier}' failed to parse in a timed pass after loading cleanly in the collection pass.");

            //The precise allocated-byte reads sit outside every Stopwatch pair: each
            //precise read forces a blocking collection, so a read inside a timed pair
            //would inflate that stage's wall column.
            long expandAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            start = Stopwatch.GetTimestamp();
            List<Quad> expanded = Owl2ImportResolver.Expand(testCase, [.. parsed]);
            expandTicks += Stopwatch.GetTimestamp() - start;
            expandAllocated += GC.GetTotalAllocatedBytes(precise: true) - expandAllocatedBefore;

            start = Stopwatch.GetTimestamp();
            OwlOntologyDocument premise = OwlRdfMapper.Map(expanded);
            mapTicks += Stopwatch.GetTimestamp() - start;

            quadCount += parsed.Count;
            expandedQuadCount += expanded.Count;
            axiomCount += premise.Axioms.Length;
            corpusHash = HashQuads(parsed, corpusHash, hashScratch);
            expandedCorpusHash = HashQuads(expanded, expandedCorpusHash, hashScratch);
            premiseTimingsToAppendTo?.Add(new PremiseTiming(testCase.Identifier, TicksToMilliseconds(parseElapsed), parsed.Count));
        }

        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        return new PassOutcome(
            TicksToMilliseconds(readTicks),
            TicksToMilliseconds(parseTicks),
            TicksToMilliseconds(expandTicks),
            TicksToMilliseconds(mapTicks),
            allocatedBytes,
            parseAllocated,
            expandAllocated,
            premiseByteCount,
            quadCount,
            expandedQuadCount,
            axiomCount,
            corpusHash,
            expandedCorpusHash);
    }

    /// <summary>Converts a <see cref="Stopwatch"/> timestamp-tick delta to milliseconds.</summary>
    /// <param name="ticks">The timestamp-tick delta.</param>
    /// <returns>The milliseconds.</returns>
    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    /// <summary>Folds one premise's parsed quad stream into the running corpus hash, in quad order with every term walked structurally.</summary>
    /// <param name="quads">The parsed quads.</param>
    /// <param name="hash">The running corpus hash.</param>
    /// <param name="workScratch">The reusable term work-stack; empty on entry and on exit.</param>
    /// <returns>The updated hash.</returns>
    private static ulong HashQuads(IReadOnlyList<Quad> quads, ulong hash, Stack<RdfTerm> workScratch)
    {
        for(int i = 0; i < quads.Count; i++)
        {
            Quad quad = quads[i];
            hash = HashTerm(hash, quad.Subject, workScratch);
            hash = HashTerm(hash, quad.Predicate, workScratch);
            hash = HashTerm(hash, quad.Object, workScratch);
            hash = quad.Graph is { } graph ? HashTerm(hash, graph, workScratch) : HashMarker(hash, 0);
            hash = HashMarker(hash, 10);
        }

        return hash;
    }

    /// <summary>Folds one term into the hash by walking its structure over an explicit stack, so a nested triple term never recurses.</summary>
    /// <param name="hash">The running hash.</param>
    /// <param name="term">The term to fold.</param>
    /// <param name="workScratch">The reusable work-stack; empty on entry and on exit.</param>
    /// <returns>The updated hash.</returns>
    private static ulong HashTerm(ulong hash, RdfTerm term, Stack<RdfTerm> workScratch)
    {
        workScratch.Push(term);
        while(workScratch.Count > 0)
        {
            RdfTerm current = workScratch.Pop();
            switch(current)
            {
                case NamedNode named:
                {
                    hash = HashMarker(hash, 1);
                    hash = HashBytes(hash, named.Iri.Span);
                    break;
                }
                case BlankNode blank:
                {
                    hash = HashMarker(hash, 2);
                    hash = HashBytes(hash, blank.Label.Span);
                    break;
                }
                case Literal literal:
                {
                    hash = HashMarker(hash, 3);
                    hash = HashBytes(hash, literal.Value.Span);
                    hash = HashMarker(hash, 4);
                    hash = HashBytes(hash, literal.Datatype.Iri.Span);
                    hash = HashMarker(hash, 5);
                    if(literal.Language is { } language)
                    {
                        hash = HashBytes(hash, language.Span);
                    }

                    byte direction = literal.BaseDirection switch
                    {
                        TextDirection.Ltr => 1,
                        TextDirection.Rtl => 2,
                        _ => 0
                    };
                    hash = HashMarker(hash, direction);
                    break;
                }
                case TripleTerm triple:
                {
                    //Pushed object-first so the pop order folds subject, predicate, object.
                    hash = HashMarker(hash, 6);
                    workScratch.Push(triple.Object);
                    workScratch.Push(triple.Predicate);
                    workScratch.Push(triple.Subject);
                    break;
                }
                default:
                {
                    Assert.Fail($"Unrecognized term kind '{current.GetType().Name}' in the corpus hash walk.");
                    break;
                }
            }
        }

        return hash;
    }

    /// <summary>Folds one structural marker byte into the hash.</summary>
    /// <param name="hash">The running hash.</param>
    /// <param name="marker">The marker byte.</param>
    /// <returns>The updated hash.</returns>
    private static ulong HashMarker(ulong hash, byte marker)
    {
        return (hash ^ marker) * FnvPrime;
    }

    /// <summary>Folds a byte span into the hash, FNV-1a per byte.</summary>
    /// <param name="hash">The running hash.</param>
    /// <param name="bytes">The bytes to fold.</param>
    /// <returns>The updated hash.</returns>
    private static ulong HashBytes(ulong hash, ReadOnlySpan<byte> bytes)
    {
        foreach(byte value in bytes)
        {
            hash = (hash ^ value) * FnvPrime;
        }

        return hash;
    }

    /// <summary>Formats one pass's progress line.</summary>
    /// <param name="pass">The one-based pass number.</param>
    /// <param name="outcome">The pass outcome.</param>
    /// <returns>The formatted line.</returns>
    private static string FormatPassLine(int pass, PassOutcome outcome)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"pass {pass}: read {outcome.ReadMilliseconds:F1} ms, parse {outcome.ParseMilliseconds:F1} ms, expand {outcome.ExpandMilliseconds:F1} ms, map {outcome.MapMilliseconds:F1} ms, allocated {outcome.AllocatedBytes / (1024.0 * 1024.0):F1} MB, quads {outcome.QuadCount}, hash {outcome.CorpusHash:x16}, expanded hash {outcome.ExpandedCorpusHash:x16}");
    }

    /// <summary>Orders premise timings slowest-first by final-pass parse milliseconds.</summary>
    /// <param name="first">The first timing.</param>
    /// <param name="second">The second timing.</param>
    /// <returns>A negative value when <paramref name="first"/> is the slower (earlier) row, a positive value when <paramref name="second"/> is, zero when neither.</returns>
    private static int CompareSlowestFirst(PremiseTiming first, PremiseTiming second)
    {
        return second.ParseMilliseconds.CompareTo(first.ParseMilliseconds);
    }

    /// <summary>Builds the census table: the header with the corpus accounting, the manifest-load lines, one row per timed pass, and the slowest-premises block from the final pass.</summary>
    /// <param name="passes">The timed pass outcomes in pass order.</param>
    /// <param name="finalPassTimings">The final pass's per-premise timings.</param>
    /// <param name="manifestCold">The first (cold) manifest load's cost.</param>
    /// <param name="manifestSteady">The post-warm manifest reload's cost.</param>
    /// <param name="manifestParseOnly">The manifest's parse-only (reader-isolated) cost.</param>
    /// <param name="manifestByteCount">The manifest file's byte length.</param>
    /// <param name="caseCount">The manifest case count.</param>
    /// <param name="premiseCaseCount">The number of cases carrying a premise.</param>
    /// <param name="dedupedCount">The number of deduplicated premise cases.</param>
    /// <param name="skippedCount">The number of premises that failed to parse or map.</param>
    /// <param name="loadableCount">The number of loadable premises each pass loads.</param>
    /// <returns>The formatted table.</returns>
    private static string BuildTable(List<PassOutcome> passes, List<PremiseTiming> finalPassTimings, ManifestStage manifestCold, ManifestStage manifestSteady, ManifestStage manifestParseOnly, long manifestByteCount, int caseCount, int premiseCaseCount, int dedupedCount, int skippedCount, int loadableCount)
    {
        StringBuilder table = new();
        PassOutcome reference = passes[0];
        table.AppendLine(CultureInfo.InvariantCulture, $"RDF/XML corpus load census ({BuildConfiguration()}): {TimedPassCount} timed passes over {loadableCount} loadable premises ({reference.PremiseByteCount} premise bytes; manifest {caseCount} cases, {premiseCaseCount} with premises, {dedupedCount} deduped, {skippedCount} skipped).");
        table.AppendLine("The loadable-collection pass parses and maps every premise once before timing, so it is the warm pass; the citable wall figures are the medians of passes 4-10.");
        table.AppendLine(CultureInfo.InvariantCulture, $"Corpus identity: {reference.QuadCount} parsed quads, {reference.ExpandedQuadCount} import-expanded quads, {reference.AxiomCount} mapped axioms, corpus hash {reference.CorpusHash:x16}, expanded hash {reference.ExpandedCorpusHash:x16}.");
        table.AppendLine(CultureInfo.InvariantCulture, $"Manifest load ({manifestByteCount} bytes through the same buffered reader): cold {manifestCold.Milliseconds:F1} ms / {manifestCold.AllocatedBytes / (1024.0 * 1024.0):F1} MB allocated; steady {manifestSteady.Milliseconds:F1} ms / {manifestSteady.AllocatedBytes / (1024.0 * 1024.0):F1} MB allocated; parse-only {manifestParseOnly.Milliseconds:F1} ms / {manifestParseOnly.AllocatedBytes / (1024.0 * 1024.0):F1} MB allocated.");
        table.AppendLine();
        table.AppendLine("pass | read ms | parse ms | expand ms | map ms | allocated MB | parse MB | expand MB");
        for(int i = 0; i < passes.Count; i++)
        {
            PassOutcome pass = passes[i];
            table.AppendLine(CultureInfo.InvariantCulture, $"{i + 1,4} | {pass.ReadMilliseconds,7:F1} | {pass.ParseMilliseconds,8:F1} | {pass.ExpandMilliseconds,9:F1} | {pass.MapMilliseconds,6:F1} | {pass.AllocatedBytes / (1024.0 * 1024.0),12:F1} | {pass.ParseAllocatedBytes / (1024.0 * 1024.0),8:F1} | {pass.ExpandAllocatedBytes / (1024.0 * 1024.0),9:F1}");
        }

        table.AppendLine();
        finalPassTimings.Sort(CompareSlowestFirst);
        int slowestCount = Math.Min(SlowestPremiseCount, finalPassTimings.Count);
        table.AppendLine(CultureInfo.InvariantCulture, $"Slowest {slowestCount} premises by parse milliseconds (final pass):");
        for(int i = 0; i < slowestCount; i++)
        {
            PremiseTiming timing = finalPassTimings[i];
            table.AppendLine(CultureInfo.InvariantCulture, $"{timing.Identifier}: {timing.ParseMilliseconds:F1} ms, {timing.QuadCount} quads");
        }

        return table.ToString();
    }

    /// <summary>The build configuration the harness runs under, for the table header.</summary>
    /// <returns><c>Release</c> or <c>Debug</c>.</returns>
    private static string BuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }
}
