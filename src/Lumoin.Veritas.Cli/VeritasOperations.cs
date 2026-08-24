using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Columnar.Analytics;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.Json;
using Lumoin.Veritas.Geo.Json.Stj;
using Lumoin.Veritas.NQuads;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Owl.Xml;
using Lumoin.Veritas.Rdf.Json;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Xml;
using IoPath = System.IO.Path;

namespace Lumoin.Veritas.Cli;

/// <summary>
/// The shared engine operations the command-line, MCP, and HTTP surfaces all call directly.
/// Each surface is a thin transport over these methods; there is no interface between them and
/// the engine, and the library stays transport-free. Expected conditions (a file that cannot be
/// read, a query that does not parse, an execution the engine refuses) are returned as an
/// <see cref="OperationResult"/> rather than thrown.
/// </summary>
internal static class VeritasOperations
{
    /// <summary>
    /// The GeoJSON geometry reader this application composes the Geo extension with: the binding
    /// assembly's System.Text.Json implementation of the library's caller-supplied read seam. The Geo
    /// library carries no JSON tokenizer of its own, so the composition root names the reader once and
    /// every surface answers through that one — the registered <c>geof:</c> catalog below, and the
    /// literal-diagnostics endpoint that describes a <c>geo:geoJSONLiteral</c> body. Declared ahead of
    /// <see cref="EngineOptions"/> so it is initialized before the composition that consumes it.
    /// </summary>
    internal static GeoJsonGeometryReadDelegate GeoJsonReader { get; } = GeoJsonGeometryReader.TryRead;

    /// <summary>
    /// The engine configuration every database this application opens runs under: the engine's
    /// fully-wired defaults extended with the whole Geo extension module, so the <c>query</c> command,
    /// the HTTP endpoint (and the Studio page it serves), and the MCP surface all answer the GeoSPARQL
    /// extension — the <c>geof:</c> function catalog with its spatial aggregates, the geometry
    /// serialization value datatypes, and the topological-relations query rewrite. Composed once per
    /// process through the field initializer.
    /// </summary>
    internal static VeritasEngineOptions EngineOptions { get; } = BuildEngineOptions();

    /// <summary>
    /// Builds <see cref="EngineOptions"/>: registers the Geo module into fresh registry builders and
    /// composes its rewrite pipeline onto the default SPARQL policy. A registration the module's own
    /// catalog cannot land cleanly is a composition defect, so it throws here rather than serving a
    /// silently narrowed function or datatype surface.
    /// </summary>
    /// <returns>The composed options.</returns>
    /// <exception cref="InvalidOperationException">A Geo registration was not accepted.</exception>
    private static VeritasEngineOptions BuildEngineOptions()
    {
        SparqlFunctionRegistryBuilder functions = new();
        GeoExtensionModule.RegisterFunctions(functions, GeoJsonReader);
        foreach(SparqlFunctionRegistration outcome in functions.Outcomes)
        {
            if(outcome.Kind != SparqlFunctionRegistrationKind.Accepted)
            {
                throw new InvalidOperationException($"The Geo extension function '{outcome.FunctionIri}' did not register cleanly ({outcome.Kind}).");
            }
        }

        ValueDatatypeRegistryBuilder datatypes = new();
        GeoExtensionModule.RegisterValueDatatypes(datatypes);
        foreach(ValueDatatypeRegistration outcome in datatypes.Outcomes)
        {
            if(outcome.Kind != ValueDatatypeRegistrationKind.Accepted)
            {
                throw new InvalidOperationException($"The Geo value datatype '{outcome.DatatypeIri}' did not register cleanly ({outcome.Kind}).");
            }
        }

        return VeritasEngineOptions.Default with
        {
            SparqlExecution = SparqlEnginePolicy.Default with { Rewrites = GeoExtensionModule.CreateRewritePipeline() },
            ValueDatatypes = datatypes.Build(),
            ExtensionFunctions = functions.Build()
        };
    }

    /// <summary>Opens a Veritas database over the RDF documents at <paramref name="dataPaths"/> (default graph plus any named graphs the files carry), under <paramref name="options"/> — normally <see cref="EngineOptions"/>, the fully-wired default configuration extended with the Geo module; a surface that layers a per-server seam on top (the serve command's trace projection) passes an extended copy of it. The documents stream straight into the engine — each file opened as a sequential-scan handle-backed read stream, wrapped in a <see cref="PipeReader"/>, parsed and encoded quad by quad — so no document is read whole into memory and no intermediate parse-object list is built (the XML formats, which have no incremental reader, are the named exception).</summary>
    /// <param name="dataPaths">The data document paths (<c>.ttl</c>/<c>.nt</c>/<c>.trig</c>/<c>.nq</c>/<c>.rdf</c>/<c>.owl</c>).</param>
    /// <param name="options">The engine configuration the database opens under.</param>
    /// <param name="mutable">Whether to open the database mutable — the open that accepts SPARQL Update and carries the worlds registry (the serve command's posture); the default immutable open fits the one-shot commands, which never mutate.</param>
    /// <param name="cancellationToken">A token that aborts loading and opening.</param>
    /// <returns>The opened database, or an error message describing the first document that could not be read.</returns>
    public static async Task<(VeritasEngine? Database, string? Error)> OpenDatabaseAsync(
        IReadOnlyList<string> dataPaths,
        VeritasEngineOptions options,
        bool mutable = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataPaths);
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            VeritasEngine database = mutable
                ? await VeritasEngine.OpenMutableAsync(StreamQuadsAsync(dataPaths, cancellationToken), options, cancellationToken).ConfigureAwait(false)
                : await VeritasEngine.OpenAsync(StreamQuadsAsync(dataPaths, cancellationToken), options, cancellationToken).ConfigureAwait(false);

            return (database, null);
        }
        catch(DataDocumentException ex)
        {
            //A missing file, an unsupported format, or a parse error surfaces as a named operation error rather than
            //throwing out of the open; the streaming iterator raised it while the engine was draining the stream.
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// Streams the quads of every document at <paramref name="dataPaths"/>, in order, as one combined async sequence
    /// the engine ingests directly. Each file is opened, parsed, and disposed before the next is opened; a missing
    /// file, an unsupported format, or a parse error is raised as a <see cref="DataDocumentException"/> naming the
    /// failing document, which <see cref="OpenDatabaseAsync"/> turns back into an operation error.
    /// </summary>
    /// <param name="dataPaths">The data document paths forming the dataset, ingested in order.</param>
    /// <param name="cancellationToken">A token that aborts the read.</param>
    /// <returns>The combined quad sequence.</returns>
    internal static async IAsyncEnumerable<Quad> StreamQuadsAsync(IReadOnlyList<string> dataPaths, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach(string path in dataPaths)
        {
            if(!File.Exists(path))
            {
                throw new DataDocumentException($"Data file not found: {path}");
            }

            await foreach(Quad quad in ReadDocumentAsync(path, cancellationToken).ConfigureAwait(false))
            {
                yield return quad;
            }
        }
    }

    /// <summary>Dispatches a document to its streaming reader by extension; an unrecognised extension is a named refusal. Turtle-family and N-Quads stream through a pipe; the XML formats have no incremental reader and are read whole (a named limitation of those formats).</summary>
    /// <param name="path">The document path.</param>
    /// <param name="cancellationToken">A token that aborts the read.</param>
    /// <returns>The document's quad sequence.</returns>
    /// <exception cref="DataDocumentException">The extension is not a supported data format.</exception>
    private static IAsyncEnumerable<Quad> ReadDocumentAsync(string path, CancellationToken cancellationToken)
    {
        string extension = IoPath.GetExtension(path);
        if(string.Equals(extension, ".nq", StringComparison.OrdinalIgnoreCase))
        {
            return StreamNQuadsAsync(path, cancellationToken);
        }

        if(string.Equals(extension, ".ttl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".nt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".trig", StringComparison.OrdinalIgnoreCase))
        {
            return StreamTurtleAsync(path, extension, cancellationToken);
        }

        if(string.Equals(extension, ".rdf", StringComparison.OrdinalIgnoreCase))
        {
            return StreamRdfXmlAsync(path, cancellationToken);
        }

        if(string.Equals(extension, ".owl", StringComparison.OrdinalIgnoreCase))
        {
            return StreamOwlXmlAsync(path, cancellationToken);
        }

        throw new DataDocumentException($"Unsupported data format '{extension}' for '{path}'; supported: .ttl, .nt, .trig, .nq, .rdf, .owl.");
    }

    /// <summary>Streams an N-Quads document through a pipe. A malformed line throws <see cref="NQuadsParseException"/>, which propagates out of the open as it did before this pipeline.</summary>
    /// <param name="path">The N-Quads document path.</param>
    /// <param name="cancellationToken">A token that aborts the read.</param>
    /// <returns>The document's quads.</returns>
    private static async IAsyncEnumerable<Quad> StreamNQuadsAsync(string path, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using SequentialReadStream stream = SequentialReadStream.Open(path);
        PipeReader reader = PipeReader.Create(stream, LeaveStreamOpenReaderOptions);
        await foreach(Quad quad in NQuadsReader.ReadAsync(reader, pool: null, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            yield return quad;
        }
    }

    /// <summary>Streams a Turtle, N-Triples, or TriG document through a pipe. The reader recovers malformed input into a diagnostic bag rather than throwing, so a parse error is raised as a named <see cref="DataDocumentException"/> once the document has been drained.</summary>
    /// <param name="path">The document path.</param>
    /// <param name="extension">The document extension, selecting Turtle versus TriG syntax.</param>
    /// <param name="cancellationToken">A token that aborts the read.</param>
    /// <returns>The document's quads.</returns>
    /// <exception cref="DataDocumentException">The document did not parse.</exception>
    private static async IAsyncEnumerable<Quad> StreamTurtleAsync(string path, string extension, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        TurtleSyntax syntax = string.Equals(extension, ".trig", StringComparison.OrdinalIgnoreCase) ? TurtleSyntax.TriG : TurtleSyntax.Turtle;
        string baseIri = new Uri(IoPath.GetFullPath(path)).AbsoluteUri;
        DiagnosticBag diagnostics = new();
        using SequentialReadStream stream = SequentialReadStream.Open(path);
        PipeReader reader = PipeReader.Create(stream, LeaveStreamOpenReaderOptions);
        await foreach(Quad quad in TurtleReader.ReadAsync(reader, syntax, diagnostics, pool: null, baseIri: baseIri, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            yield return quad;
        }

        if(diagnostics.HasErrors)
        {
            throw new DataDocumentException($"Document '{path}' did not parse: {DescribeFirstError(diagnostics.Diagnostics)}");
        }
    }

    /// <summary>Reads an RDF/XML document and streams its quads. RDF/XML has no incremental reader, so the whole document is read into memory (a named limitation of the format, not of this pipeline).</summary>
    /// <param name="path">The RDF/XML document path.</param>
    /// <param name="cancellationToken">A token that aborts the read.</param>
    /// <returns>The document's quads.</returns>
    /// <exception cref="DataDocumentException">The document did not parse.</exception>
    private static async IAsyncEnumerable<Quad> StreamRdfXmlAsync(string path, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        string baseIri = new Uri(IoPath.GetFullPath(path)).AbsoluteUri;
        DiagnosticBag diagnostics = new();
        IReadOnlyList<Quad> quads = RdfXmlReader.Read(bytes, diagnostics, Utf8Strings.From(baseIri));
        if(diagnostics.HasErrors)
        {
            throw new DataDocumentException($"Document '{path}' did not parse: {DescribeFirstError(diagnostics.Diagnostics)}");
        }

        foreach(Quad quad in quads)
        {
            yield return quad;
        }
    }

    /// <summary>Reads an OWL/XML document and streams its quads mapped to RDF. OWL/XML has no incremental reader, so the whole document is read into memory (a named limitation of the format, not of this pipeline).</summary>
    /// <param name="path">The OWL/XML document path.</param>
    /// <param name="cancellationToken">A token that aborts the read.</param>
    /// <returns>The document's quads.</returns>
    /// <exception cref="DataDocumentException">The document did not parse.</exception>
    private static async IAsyncEnumerable<Quad> StreamOwlXmlAsync(string path, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        OwlOntologyDocument document = OwlXmlSyntaxReader.Read(bytes);
        if(document.Diagnostics.HasErrors)
        {
            throw new DataDocumentException($"Document '{path}' did not parse: {DescribeFirstError(document.Diagnostics.Diagnostics)}");
        }

        foreach(Quad quad in OwlStructuralToRdf.ToQuads(document))
        {
            yield return quad;
        }
    }

    /// <summary>The pipe-reader options that leave the underlying stream open, so the enclosing <c>using</c> owns and disposes the <see cref="SequentialReadStream"/> (and its file handle) exactly once; the reader completing the pipe does not dispose it.</summary>
    private static StreamPipeReaderOptions LeaveStreamOpenReaderOptions { get; } = new(leaveOpen: true);

    /// <summary>
    /// A read-only, forward-only <see cref="Stream"/> over a file handle, used to feed a <see cref="PipeReader"/> for
    /// streaming ingest. Reads go through a <see cref="SafeFileHandle"/> and <see cref="RandomAccess"/> — the durable
    /// file-I/O primitive this codebase opens files with — advancing an internal read cursor, so a document streams
    /// through the pipe a chunk at a time without ever being read whole into memory. It exposes only what
    /// <see cref="PipeReader.Create(Stream, StreamPipeReaderOptions?)"/> pulls on: forward reads and length; seeking
    /// and writing are unsupported.
    /// </summary>
    private sealed class SequentialReadStream : Stream
    {
        /// <summary>The file handle reads are issued against; owned and disposed by this stream.</summary>
        private SafeFileHandle Handle { get; }

        /// <summary>The next byte offset a read continues from — the forward cursor a pipe's single pass advances.</summary>
        private long Offset { get; set; }

        /// <summary>Constructs the stream over an owned file handle.</summary>
        /// <param name="handle">The read handle this stream owns.</param>
        private SequentialReadStream(SafeFileHandle handle)
        {
            Handle = handle;
        }

        /// <summary>Opens <paramref name="path"/> for asynchronous, read-only, sequential-scan streaming — the access shape a single forward pass over an RDF document wants.</summary>
        /// <param name="path">The file path.</param>
        /// <returns>The opened stream.</returns>
        public static SequentialReadStream Open(string path)
        {
            return new SequentialReadStream(File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous | FileOptions.SequentialScan));
        }

        /// <summary>Always <see langword="true"/>; this stream is for reading.</summary>
        public override bool CanRead => true;

        /// <summary>Always <see langword="false"/>; the stream is forward-only.</summary>
        public override bool CanSeek => false;

        /// <summary>Always <see langword="false"/>; the stream is read-only.</summary>
        public override bool CanWrite => false;

        /// <summary>The file length in bytes.</summary>
        public override long Length => RandomAccess.GetLength(Handle);

        /// <summary>The forward read cursor; not settable (the stream does not seek).</summary>
        /// <exception cref="NotSupportedException">Always thrown by the setter.</exception>
        public override long Position
        {
            get => Offset;
            set => throw new NotSupportedException("The sequential read stream does not support seeking.");
        }

        /// <summary>Reads the next bytes into <paramref name="buffer"/>, advancing the cursor.</summary>
        /// <param name="buffer">The destination span.</param>
        /// <returns>The number of bytes read, or zero at end of file.</returns>
        public override int Read(Span<byte> buffer)
        {
            int read = RandomAccess.Read(Handle, buffer, Offset);
            Offset += read;

            return read;
        }

        /// <summary>Reads the next bytes into a byte array, advancing the cursor.</summary>
        /// <param name="buffer">The destination array.</param>
        /// <param name="start">The offset into <paramref name="buffer"/>.</param>
        /// <param name="count">The maximum number of bytes to read.</param>
        /// <returns>The number of bytes read, or zero at end of file.</returns>
        public override int Read(byte[] buffer, int start, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            return Read(buffer.AsSpan(start, count));
        }

        /// <summary>Reads the next bytes into <paramref name="buffer"/> asynchronously, advancing the cursor.</summary>
        /// <param name="buffer">The destination memory.</param>
        /// <param name="cancellationToken">A token that aborts the read.</param>
        /// <returns>The number of bytes read, or zero at end of file.</returns>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await RandomAccess.ReadAsync(Handle, buffer, Offset, cancellationToken).ConfigureAwait(false);
            Offset += read;

            return read;
        }

        /// <summary>Reads the next bytes into a byte array asynchronously, advancing the cursor.</summary>
        /// <param name="buffer">The destination array.</param>
        /// <param name="start">The offset into <paramref name="buffer"/>.</param>
        /// <param name="count">The maximum number of bytes to read.</param>
        /// <param name="cancellationToken">A token that aborts the read.</param>
        /// <returns>The number of bytes read, or zero at end of file.</returns>
        public override Task<int> ReadAsync(byte[] buffer, int start, int count, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            return ReadAsync(buffer.AsMemory(start, count), cancellationToken).AsTask();
        }

        /// <summary>A no-op; there is nothing buffered to flush on a read-only stream.</summary>
        public override void Flush()
        {
        }

        /// <summary>Unsupported; the stream is forward-only.</summary>
        /// <param name="offset">Ignored.</param>
        /// <param name="origin">Ignored.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("The sequential read stream does not support seeking.");
        }

        /// <summary>Unsupported; the stream is read-only.</summary>
        /// <param name="value">Ignored.</param>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override void SetLength(long value)
        {
            throw new NotSupportedException("The sequential read stream is read-only.");
        }

        /// <summary>Unsupported; the stream is read-only.</summary>
        /// <param name="buffer">Ignored.</param>
        /// <param name="start">Ignored.</param>
        /// <param name="count">Ignored.</param>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override void Write(byte[] buffer, int start, int count)
        {
            throw new NotSupportedException("The sequential read stream is read-only.");
        }

        /// <summary>Disposes the owned file handle.</summary>
        /// <param name="disposing">Whether managed resources are being released.</param>
        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                Handle.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>The plain-text content type failure bodies and unrendered answers default to.</summary>
    internal const string PlainTextContentType = "text/plain; charset=utf-8";

    /// <summary>The SPARQL-results-JSON media type.</summary>
    internal const string JsonResultsContentType = "application/sparql-results+json";

    /// <summary>The SPARQL-results-XML media type.</summary>
    internal const string XmlResultsContentType = "application/sparql-results+xml";

    /// <summary>
    /// Executes a SPARQL query — any of the four forms — against the open database and renders the
    /// result per the caller's format preferences, dispatching on the RESULT SHAPE first: a SELECT
    /// renders to <paramref name="tabularFormat"/>; an ASK renders to <paramref name="tabularFormat"/>
    /// except that a delimited preference answers SPARQL-results-JSON (no W3C delimited format defines
    /// a boolean document); a CONSTRUCT/DESCRIBE graph renders to <paramref name="graphFormat"/>
    /// whatever the tabular preference says — the server-choice arm of content negotiation, so a graph
    /// result under a tabular-only Accept still answers rather than failing. This is the single query
    /// entry every surface shares, so the engine-boundary failure mapping lives exactly once: a parse
    /// failure (or an update where a query belongs) is <see cref="OperationFailureKind.Malformed"/>, an
    /// unresolvable dataset graph is <see cref="OperationFailureKind.DatasetNotAcceptable"/>, and an
    /// engine refusal is <see cref="OperationFailureKind.Refused"/>.
    /// </summary>
    /// <param name="database">The database opened by <see cref="OpenDatabaseAsync"/>.</param>
    /// <param name="queryText">The query as UTF-8 bytes — the byte-native entry the HTTP endpoint routes an <c>application/sparql-query</c> POST body to, with no string round-trip.</param>
    /// <param name="baseIri">The base IRI relative references resolve against (the request base).</param>
    /// <param name="tabularFormat">The format a SELECT/ASK result renders to.</param>
    /// <param name="graphFormat">The RDF serialization a CONSTRUCT/DESCRIBE result renders to.</param>
    /// <param name="protocolDataset">A dataset description supplied outside the query text (the protocol's <c>default-graph-uri</c>/<c>named-graph-uri</c> parameters), or <see langword="null"/> to leave the query's own dataset clause in force.</param>
    /// <param name="world">The registered world the query runs in, or <see langword="null"/> for the primary path; a name that is not registered maps to a malformed-request failure like any other bad argument.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The rendered answer with its media type.</returns>
    public static async Task<QueryAnswer> ExecuteQueryAsync(
        VeritasEngine database,
        Utf8String queryText,
        string baseIri,
        SparqlTabularResultsFormat tabularFormat = SparqlTabularResultsFormat.Csv,
        SparqlGraphResultsFormat graphFormat = SparqlGraphResultsFormat.NTriples,
        DatasetClause? protocolDataset = null,
        string? world = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);

        VeritasQueryResult result;
        try
        {
            result = await database.QueryAsync(queryText, Utf8Strings.From(baseIri), protocolDataset: protocolDataset, world: world, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch(UnknownGraphSourceException ex)
        {
            return new QueryAnswer(OperationResult.Failed(ex.Message, OperationFailureKind.DatasetNotAcceptable), PlainTextContentType);
        }
        catch(ArgumentException ex)
        {
            return new QueryAnswer(OperationResult.Failed(ex.Message, OperationFailureKind.Malformed), PlainTextContentType);
        }
        catch(NotSupportedException ex)
        {
            return new QueryAnswer(OperationResult.Failed(ex.Message, OperationFailureKind.Refused), PlainTextContentType);
        }

        if(result.IsGraph)
        {
            return graphFormat == SparqlGraphResultsFormat.Turtle
                ? new QueryAnswer(OperationResult.Ok(RenderTurtle(result.Graph!)), "text/turtle; charset=utf-8")
                : new QueryAnswer(OperationResult.Ok(await RenderNTriplesAsync(result.Graph!, cancellationToken).ConfigureAwait(false)), "application/n-triples; charset=utf-8");
        }

        if(result.IsAsk)
        {
            SparqlResultSet boolean = SparqlResultSet.ForAsk(result.Boolean!.Value);

            return tabularFormat == SparqlTabularResultsFormat.Xml
                ? new QueryAnswer(OperationResult.Ok(SparqlResultsXmlWriter.WriteToUtf8String(boolean).ToString()), XmlResultsContentType)
                : new QueryAnswer(OperationResult.Ok(SparqlResultsJsonWriter.WriteToUtf8String(boolean, indented: false).ToString()), JsonResultsContentType);
        }

        return tabularFormat switch
        {
            SparqlTabularResultsFormat.Json => new QueryAnswer(OperationResult.Ok(SparqlResultsJsonWriter.WriteToUtf8String(result.Bindings!, indented: false).ToString()), JsonResultsContentType),
            SparqlTabularResultsFormat.Xml => new QueryAnswer(OperationResult.Ok(SparqlResultsXmlWriter.WriteToUtf8String(result.Bindings!).ToString()), XmlResultsContentType),
            SparqlTabularResultsFormat.Tsv => new QueryAnswer(OperationResult.Ok(SparqlResultsDelimitedWriter.WriteToString(result.Bindings!, SparqlDelimitedResultsFormat.Tsv)), "text/tab-separated-values; charset=utf-8"),
            _ => new QueryAnswer(OperationResult.Ok(SparqlResultsDelimitedWriter.WriteToString(result.Bindings!, SparqlDelimitedResultsFormat.Csv)), "text/csv; charset=utf-8")
        };
    }

    /// <summary>Renders a result graph (default-graph quads) as an N-Triples document.</summary>
    /// <param name="graph">The result graph.</param>
    /// <param name="cancellationToken">A token that aborts the write.</param>
    /// <returns>The N-Triples document.</returns>
    private static async Task<string> RenderNTriplesAsync(IReadOnlyList<Quad> graph, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        PipeWriter writer = PipeWriter.Create(buffer, new StreamPipeWriterOptions(leaveOpen: true));
        await NQuadsWriter.WriteAsync(graph, writer, cancellationToken).ConfigureAwait(false);

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Renders a graph (default-graph quads) as a Turtle document; shared with the endpoint's service-description rendering.</summary>
    /// <param name="graph">The graph to render.</param>
    /// <returns>The Turtle document.</returns>
    internal static string RenderTurtle(IReadOnlyList<Quad> graph)
    {
        using MemoryStream buffer = new();
        PipeWriter writer = PipeWriter.Create(buffer, new StreamPipeWriterOptions(leaveOpen: true));
        TurtleWriter.Write(graph, writer, TurtleSyntax.Turtle);

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Reads a SPARQL query file and evaluates it against the documents at <paramref name="dataPaths"/>; the convenience the <c>query</c> command calls.</summary>
    /// <param name="queryPath">The query file path.</param>
    /// <param name="dataPaths">The data document paths forming the dataset.</param>
    /// <param name="format">The delimited output format.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns>The rendered results, or an error message.</returns>
    public static async Task<OperationResult> RunQueryFileAsync(
        string queryPath,
        IReadOnlyList<string> dataPaths,
        SparqlDelimitedResultsFormat format,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryPath);
        ArgumentNullException.ThrowIfNull(dataPaths);

        if(!File.Exists(queryPath))
        {
            return OperationResult.Failed($"Query file not found: {queryPath}");
        }

        string baseIri = new Uri(IoPath.GetFullPath(queryPath)).AbsoluteUri;
        string queryText = await File.ReadAllTextAsync(queryPath, cancellationToken).ConfigureAwait(false);

        return await RunQueryTextAsync(queryText, dataPaths, baseIri, format, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Evaluates a SPARQL query given as text against the documents at <paramref name="dataPaths"/>; the convenience the MCP and HTTP surfaces call.</summary>
    /// <param name="queryText">The query text.</param>
    /// <param name="dataPaths">The data document paths forming the dataset.</param>
    /// <param name="baseIri">The base IRI relative references resolve against.</param>
    /// <param name="format">The delimited output format.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns>The rendered results, or an error message.</returns>
    public static async Task<OperationResult> RunQueryTextAsync(
        string queryText,
        IReadOnlyList<string> dataPaths,
        string baseIri,
        SparqlDelimitedResultsFormat format,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentNullException.ThrowIfNull(dataPaths);
        ArgumentNullException.ThrowIfNull(baseIri);

        (VeritasEngine? database, string? error) = await OpenDatabaseAsync(dataPaths, EngineOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        if(error is not null)
        {
            return OperationResult.Failed(error);
        }

        await using var scope = database!.ConfigureAwait(false);

        QueryAnswer answer = await ExecuteQueryAsync(
            database!,
            Utf8Strings.From(queryText),
            baseIri,
            format == SparqlDelimitedResultsFormat.Tsv ? SparqlTabularResultsFormat.Tsv : SparqlTabularResultsFormat.Csv,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return answer.Result;
    }

    /// <summary>
    /// Runs a graph-analytics algorithm from the <see cref="GraphAnalyticsCatalog"/> over the documents at
    /// <paramref name="dataPaths"/> and renders the result. The data is read into a single delta-free columnar
    /// index (every predicate, the whole graph; predicate/graph selection and access-control-scoped acquisition
    /// are surfacing follow-ups), the algorithm produces a SPARQL SELECT result set, and that is serialized in the
    /// requested delimited format — the same rendering the query surface uses.
    /// </summary>
    /// <param name="algorithm">The algorithm name (see <see cref="DescribeAnalytics"/>).</param>
    /// <param name="dataPaths">The data document paths forming the graph.</param>
    /// <param name="parameters">The algorithm parameters, each <c>name=value</c>.</param>
    /// <param name="format">The delimited output format.</param>
    /// <param name="cancellationToken">A token that aborts loading and the run.</param>
    /// <returns>The rendered result, or an error message for an unknown algorithm, unreadable data, or a bad parameter.</returns>
    public static async Task<OperationResult> RunGraphAnalyticsAsync(
        string algorithm,
        IReadOnlyList<string> dataPaths,
        IReadOnlyList<string> parameters,
        SparqlDelimitedResultsFormat format,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(algorithm);
        ArgumentNullException.ThrowIfNull(dataPaths);
        ArgumentNullException.ThrowIfNull(parameters);

        if(!GraphAnalyticsCatalog.TryGet(algorithm, out GraphAnalyticsDescriptor descriptor))
        {
            string known = string.Join(", ", GraphAnalyticsCatalog.All.Select(static d => d.Name));

            return OperationResult.Failed($"Unknown analytics algorithm '{algorithm}'. Available: {known}.");
        }

        AnalyticsParameters arguments;
        try
        {
            arguments = new AnalyticsParameters(parameters);
        }
        catch(FormatException ex)
        {
            return OperationResult.Failed(ex.Message);
        }

        string graph = arguments.GetString("graph", "union");
        (ColumnarTripleIndex? index, TermDictionary dictionary, string? error) = await BuildAnalyticsIndexAsync(dataPaths, graph, cancellationToken).ConfigureAwait(false);
        if(error is not null)
        {
            return OperationResult.Failed(error);
        }

        SparqlResultSet result;
        try
        {
            AnalyticsContext context = new(new ColumnarGraphAnalytics(index!), dictionary, arguments, cancellationToken);
            result = descriptor.Run(context);
        }
        catch(FormatException ex)
        {
            return OperationResult.Failed(ex.Message);
        }
        catch(ArgumentException ex)
        {
            return OperationResult.Failed(ex.Message);
        }

        return OperationResult.Ok(SparqlResultsDelimitedWriter.WriteToString(result, format));
    }

    /// <summary>Lists the available graph-analytics algorithms, one per line as <c>name  summary</c>; the discovery the CLI <c>--list</c>, the MCP list tool, and the HTTP <c>GET /analytics</c> all render.</summary>
    /// <returns>The rendered algorithm list.</returns>
    public static OperationResult DescribeAnalytics()
    {
        StringBuilder builder = new();
        foreach(GraphAnalyticsDescriptor descriptor in GraphAnalyticsCatalog.All)
        {
            builder.Append(descriptor.Name).Append("  ").Append(descriptor.Summary).Append('\n');
        }

        return OperationResult.Ok(builder.ToString());
    }

    /// <summary>Reads the documents at <paramref name="dataPaths"/> into a single delta-free columnar index, with a term dictionary for decoding result ids. The <paramref name="graph"/> selector chooses which triples enter the index: <c>union</c> (all graphs), <c>default</c> (the default graph only), or a graph IRI (that named graph only).</summary>
    /// <param name="dataPaths">The data document paths.</param>
    /// <param name="graph">The graph selector: <c>union</c>, <c>default</c>, or a named-graph IRI.</param>
    /// <param name="cancellationToken">A token that aborts reading.</param>
    /// <returns>The built index and dictionary, or an error message for the first unreadable document.</returns>
    private static async Task<(ColumnarTripleIndex? Index, TermDictionary Dictionary, string? Error)> BuildAnalyticsIndexAsync(
        IReadOnlyList<string> dataPaths,
        string graph,
        CancellationToken cancellationToken)
    {
        bool unionGraph = string.Equals(graph, "union", StringComparison.OrdinalIgnoreCase);
        bool defaultGraph = string.Equals(graph, "default", StringComparison.OrdinalIgnoreCase);
        Utf8String namedGraph = unionGraph || defaultGraph ? default : Utf8Strings.From(graph);

        TermDictionary dictionary = new();
        List<EncodedTriple> triples = [];

        foreach(string path in dataPaths)
        {
            if(!File.Exists(path))
            {
                return (null, dictionary, $"Data file not found: {path}");
            }

            (IReadOnlyList<Quad>? quads, string? error) = await ReadQuadsAsync(path, cancellationToken).ConfigureAwait(false);
            if(error is not null)
            {
                return (null, dictionary, error);
            }

            foreach(Quad quad in quads!)
            {
                if(!unionGraph && !MatchesGraph(quad.Graph, defaultGraph, namedGraph))
                {
                    continue;
                }

                uint subject = dictionary.GetOrAdd(quad.Subject).Encoded;
                uint predicate = dictionary.GetOrAdd(quad.Predicate).Encoded;
                uint @object = dictionary.GetOrAdd(quad.Object).Encoded;
                triples.Add(EncodedTriple.FromEncoded(subject, predicate, @object));
            }
        }

        return (ColumnarTripleIndex.Build(triples), dictionary, null);
    }

    /// <summary>Whether a quad's graph matches the analytics selection: the default graph (a <see langword="null"/> graph) when <paramref name="defaultGraph"/>, or the named graph whose IRI equals <paramref name="namedGraph"/> otherwise.</summary>
    /// <param name="quadGraph">The quad's graph term, or <see langword="null"/> for the default graph.</param>
    /// <param name="defaultGraph">Whether the default graph is selected.</param>
    /// <param name="namedGraph">The selected named-graph IRI, when a named graph is selected.</param>
    /// <returns><see langword="true"/> when the quad belongs to the selected graph.</returns>
    private static bool MatchesGraph(RdfTerm? quadGraph, bool defaultGraph, Utf8String namedGraph)
    {
        return defaultGraph
            ? quadGraph is null
            : quadGraph is NamedNode named && named.Iri == namedGraph;
    }

    /// <summary>Reads a document's quads, dispatching on the file extension; Turtle/N-Triples/TriG via the Turtle reader, N-Quads via the N-Quads reader, RDF/XML via the byte-native RDF/XML reader, and OWL/XML via the OWL/XML reader mapped to RDF.</summary>
    /// <param name="path">The document path.</param>
    /// <param name="cancellationToken">A token that aborts reading.</param>
    /// <returns>The quads, or an error message for an unreadable format or malformed document.</returns>
    private static async Task<(IReadOnlyList<Quad>? Quads, string? Error)> ReadQuadsAsync(string path, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        string extension = IoPath.GetExtension(path);
        List<Quad> quads = [];

        if(string.Equals(extension, ".nq", StringComparison.OrdinalIgnoreCase))
        {
            await foreach(Quad quad in NQuadsReader.ReadAsync(bytes, pool: null, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                quads.Add(quad);
            }

            return (quads, null);
        }

        if(string.Equals(extension, ".ttl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".nt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".trig", StringComparison.OrdinalIgnoreCase))
        {
            TurtleSyntax syntax = string.Equals(extension, ".trig", StringComparison.OrdinalIgnoreCase) ? TurtleSyntax.TriG : TurtleSyntax.Turtle;
            string baseIri = new Uri(IoPath.GetFullPath(path)).AbsoluteUri;
            DiagnosticBag diagnostics = new();
            await foreach(Quad quad in TurtleReader.ReadAsync(bytes, syntax, diagnostics, pool: null, baseIri: baseIri, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                quads.Add(quad);
            }

            return diagnostics.HasErrors
                ? (null, $"Document '{path}' did not parse: {DescribeFirstError(diagnostics.Diagnostics)}")
                : (quads, null);
        }

        if(string.Equals(extension, ".rdf", StringComparison.OrdinalIgnoreCase))
        {
            string baseIri = new Uri(IoPath.GetFullPath(path)).AbsoluteUri;
            DiagnosticBag diagnostics = new();
            IReadOnlyList<Quad> rdfQuads = RdfXmlReader.Read(bytes, diagnostics, Utf8Strings.From(baseIri));

            return diagnostics.HasErrors
                ? (null, $"Document '{path}' did not parse: {DescribeFirstError(diagnostics.Diagnostics)}")
                : (rdfQuads, null);
        }

        if(string.Equals(extension, ".owl", StringComparison.OrdinalIgnoreCase))
        {
            OwlOntologyDocument document = OwlXmlSyntaxReader.Read(bytes);

            return document.Diagnostics.HasErrors
                ? (null, $"Document '{path}' did not parse: {DescribeFirstError(document.Diagnostics.Diagnostics)}")
                : (OwlStructuralToRdf.ToQuads(document), null);
        }

        return (null, $"Unsupported data format '{extension}' for '{path}'; supported: .ttl, .nt, .trig, .nq, .rdf, .owl.");
    }

    /// <summary>Renders the first error diagnostic (code and message) for an operation-result error string.</summary>
    /// <param name="diagnostics">The diagnostics.</param>
    /// <returns>A one-line description of the first error, or a generic message when none carry detail.</returns>
    private static string DescribeFirstError(IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach(Diagnostic diagnostic in diagnostics)
        {
            if(diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return $"{diagnostic.Code} {diagnostic.Message}";
            }
        }

        return "unspecified parse error.";
    }
}
