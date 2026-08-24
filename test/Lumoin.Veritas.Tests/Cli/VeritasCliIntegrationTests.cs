using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Tests.Workbench;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Cli;

/// <summary>
/// Drives the built <c>Lumoin.Veritas.Cli</c> executable as a child process: the <c>query</c>
/// command over captured stdout, and the <c>serve</c> command's real Kestrel SPARQL endpoint over
/// an <see cref="HttpClient"/>. The HTTP test launches the actual executable with <c>--port 0</c>,
/// reads the bound address the server prints, and queries it — so the shipped artifact (process,
/// argument parsing, and Kestrel) is exercised end-to-end, not an in-process stand-in.
/// </summary>
[TestClass]
internal sealed partial class VeritasCliIntegrationTests
{
    /// <summary>The compiled pattern matching the SPARQL endpoint URL the <c>serve</c> command prints (source-generated, so no per-call regex compilation).</summary>
    /// <returns>The endpoint-address regex.</returns>
    [GeneratedRegex(@"(https?://[^\s]+/sparql)")]
    private static partial Regex ListeningEndpointRegex();

    /// <summary>The compiled pattern matching the worker-thread floor line the <c>serve</c> command prints (source-generated, so no per-call regex compilation).</summary>
    /// <returns>The worker-thread floor regex.</returns>
    [GeneratedRegex(@"Worker-thread minimum floored at (\d+)\.")]
    private static partial Regex WorkerThreadFloorRegex();

    /// <summary>The compiled pattern matching a trace frame's correlation-id field, capturing the GUID text (source-generated, so no per-call regex compilation).</summary>
    /// <returns>The correlation-id regex.</returns>
    [GeneratedRegex("\"correlationId\":\"([0-9a-f-]{36})\"")]
    private static partial Regex TraceCorrelationRegex();

    /// <summary>The compiled pattern matching a literal diagnosis document's <c>byteOffset</c> field, capturing the signed offset (source-generated, so no per-call regex compilation).</summary>
    /// <returns>The byte-offset regex.</returns>
    [GeneratedRegex("\"byteOffset\":(-?[0-9]+)")]
    private static partial Regex LiteralDiagnosisByteOffsetRegex();

    /// <summary>Gets or sets the test execution context (supplies the cancellation token).</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The <c>query</c> command evaluates a SELECT against a Turtle file and prints the CSV rows.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task QueryCommandSelectsKnownObjects()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        (string dataPath, string queryPath) = WriteFixture();

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executable,
            ["query", queryPath, "--data", dataPath],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, $"stderr: {result.Stderr}");
        Assert.Contains("http://example.org/bob", result.Stdout);
        Assert.Contains("http://example.org/carol", result.Stdout);
    }

    /// <summary>The <c>query</c> command loads an RDF/XML (<c>.rdf</c>) dataset through the byte-native RDF/XML reader and selects from it.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task QueryCommandLoadsRdfXmlData()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        (string dataPath, string queryPath) = WriteRdfXmlFixture();

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executable,
            ["query", queryPath, "--data", dataPath],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, $"stderr: {result.Stderr}");
        Assert.Contains("http://example.org/bob", result.Stdout);
        Assert.Contains("http://example.org/carol", result.Stdout);
    }

    /// <summary>The <c>query</c> command loads an OWL/XML (<c>.owl</c>) ontology, mapped to RDF, and selects the subclass axiom it carries.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task QueryCommandLoadsOwlXmlData()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        (string dataPath, string queryPath) = WriteOwlXmlFixture();

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executable,
            ["query", queryPath, "--data", dataPath],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, $"stderr: {result.Stderr}");
        Assert.Contains("http://example.org/Animal", result.Stdout);
    }

    /// <summary>The <c>query</c> command loads an N-Quads (<c>.nq</c>) dataset through the streaming pipe pipeline — the sequential handle-backed stream, the pipe reader, and the line-oriented N-Quads reader — serving both the default-graph triple and the named-graph quad the file carries.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task QueryCommandLoadsNQuadsDataIncludingANamedGraph()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        (string dataPath, string queryPath) = WriteNQuadsFixture();

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executable,
            ["query", queryPath, "--data", dataPath],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, $"stderr: {result.Stderr}");
        Assert.Contains("http://example.org/bob", result.Stdout);
        Assert.Contains("http://example.org/dana", result.Stdout);
    }

    /// <summary>The <c>query</c> command surfaces a missing data file as a named operation error — the file's path on stderr and a non-zero exit — rather than an unhandled exception.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task QueryCommandNamesAMissingDataFile()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        (string dataPath, string queryPath) = WriteFixture();
        string missingPath = dataPath + ".missing.ttl";

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executable,
            ["query", queryPath, "--data", missingPath],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreNotEqual(0, result.ExitCode, "A missing data file is an operation error.");
        Assert.Contains("Data file not found", result.Stderr + result.Stdout);
        Assert.Contains(missingPath, result.Stderr + result.Stdout);
    }

    /// <summary>The <c>query</c> command evaluates the registered <c>geof:sfContains</c> extension function over <c>geo:wktLiteral</c> data: the strictly contained geometry answers, the disjoint one does not, and the containing operand is pinned to a fixed subject so no self-containment pair leaks in.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task QueryCommandEvaluatesGeoFunctionFilter()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        (string dataPath, string queryPath) = WriteGeoFixture();

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executable,
            ["query", queryPath, "--data", dataPath],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, $"stderr: {result.Stderr}");
        Assert.Contains("http://example.org/gInner", result.Stdout);
        Assert.DoesNotContain("http://example.org/gFar", result.Stdout);
    }

    /// <summary>The <c>serve</c> command's Kestrel endpoint answers a SPARQL Protocol GET over HTTP.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandAnswersHttpQuery()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        (string dataPath, _) = WriteFixture();

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("serve");
        process.StartInfo.ArgumentList.Add("--port");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add("--data");
        process.StartInfo.ArgumentList.Add(dataPath);

        process.Start();
        Task<string> serverErrors = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);

        try
        {
            Uri? endpoint = await ReadListeningEndpointAsync(process, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(endpoint, "The server did not report a listening address." + await DescribeServerFailureAsync(process, serverErrors).ConfigureAwait(false));

            using HttpClient client = new();
            Uri requestUri = new(endpoint, "?query=" + Uri.EscapeDataString("PREFIX : <http://example.org/> SELECT ?who WHERE { :alice :knows ?who }"));
            string body = await client.GetStringAsync(requestUri, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.Contains("http://example.org/bob", body);
            Assert.Contains("http://example.org/carol", body);
        }
        finally
        {
            if(!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The <c>serve</c> command's Kestrel endpoint answers a SPARQL Protocol POST with an <c>application/sparql-query</c> body, which is routed to the byte-native query entry.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandAnswersHttpPostQuery()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        (string dataPath, _) = WriteFixture();

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("serve");
        process.StartInfo.ArgumentList.Add("--port");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add("--data");
        process.StartInfo.ArgumentList.Add(dataPath);

        process.Start();
        Task<string> serverErrors = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);

        try
        {
            Uri? endpoint = await ReadListeningEndpointAsync(process, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(endpoint, "The server did not report a listening address." + await DescribeServerFailureAsync(process, serverErrors).ConfigureAwait(false));

            using HttpClient client = new();
            using StringContent content = new("PREFIX : <http://example.org/> SELECT ?who WHERE { :alice :knows ?who }", Encoding.UTF8, "application/sparql-query");
            using HttpResponseMessage response = await client.PostAsync(endpoint, content, TestContext.CancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode, body);
            Assert.Contains("http://example.org/bob", body);
            Assert.Contains("http://example.org/carol", body);
        }
        finally
        {
            if(!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The <c>serve</c> command's endpoint answers a bare topological-relation triple pattern through the registered query rewrite: the containing feature derives from the stored geometries alone (no <c>geo:sfContains</c> triple is asserted anywhere in the fixture), and the disjoint feature does not. The rewrite's other case rules also bind geometry nodes and the self-contained feature; the assertions pin one derived member and one excluded one.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandAnswersGeoRewriteQuery()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        (string dataPath, _) = WriteGeoFixture();

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("serve");
        process.StartInfo.ArgumentList.Add("--port");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add("--data");
        process.StartInfo.ArgumentList.Add(dataPath);

        process.Start();
        Task<string> serverErrors = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);

        try
        {
            Uri? endpoint = await ReadListeningEndpointAsync(process, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(endpoint, "The server did not report a listening address." + await DescribeServerFailureAsync(process, serverErrors).ConfigureAwait(false));

            using HttpClient client = new();
            Uri requestUri = new(endpoint, "?query=" + Uri.EscapeDataString("PREFIX : <http://example.org/> PREFIX geo: <http://www.opengis.net/ont/geosparql#> SELECT ?f WHERE { ?f geo:sfContains :fInner }"));
            string body = await client.GetStringAsync(requestUri, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.Contains("http://example.org/fSquare", body);
            Assert.DoesNotContain("http://example.org/fFar", body);
        }
        finally
        {
            if(!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The <c>serve</c> command's <c>--cors-origin</c> allowlist grants cross-origin access to exactly the named origin: the preflight an <c>application/sparql-query</c> POST requires answers with the origin echoed, the actual POST carries the allow-origin header beside the results, and a request from an origin outside the allowlist gets no CORS header at all.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandHonorsCorsOriginAllowlist()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        (string dataPath, _) = WriteFixture();

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("serve");
        process.StartInfo.ArgumentList.Add("--port");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add("--data");
        process.StartInfo.ArgumentList.Add(dataPath);
        process.StartInfo.ArgumentList.Add("--cors-origin");
        process.StartInfo.ArgumentList.Add("http://studio.example");

        process.Start();
        Task<string> serverErrors = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);

        try
        {
            Uri? endpoint = await ReadListeningEndpointAsync(process, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(endpoint, "The server did not report a listening address." + await DescribeServerFailureAsync(process, serverErrors).ConfigureAwait(false));

            using HttpClient client = new();

            using HttpRequestMessage preflight = new(HttpMethod.Options, endpoint);
            preflight.Headers.Add("Origin", "http://studio.example");
            preflight.Headers.Add("Access-Control-Request-Method", "POST");
            preflight.Headers.Add("Access-Control-Request-Headers", "content-type");
            using HttpResponseMessage preflightResponse = await client.SendAsync(preflight, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(System.Net.HttpStatusCode.NoContent, preflightResponse.StatusCode);
            Assert.IsTrue(preflightResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out IEnumerable<string>? preflightOrigins), "The preflight response carries no Access-Control-Allow-Origin header.");
            Assert.AreEqual("http://studio.example", string.Join(",", preflightOrigins!));

            using HttpRequestMessage allowedRequest = new(HttpMethod.Post, endpoint)
            {
                Content = new StringContent("PREFIX : <http://example.org/> SELECT ?who WHERE { :alice :knows ?who }", Encoding.UTF8, "application/sparql-query")
            };
            allowedRequest.Headers.Add("Origin", "http://studio.example");
            using HttpResponseMessage allowedResponse = await client.SendAsync(allowedRequest, TestContext.CancellationToken).ConfigureAwait(false);
            string allowedBody = await allowedResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(System.Net.HttpStatusCode.OK, allowedResponse.StatusCode, allowedBody);
            Assert.Contains("http://example.org/bob", allowedBody);
            Assert.IsTrue(allowedResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out IEnumerable<string>? allowedOrigins), "The allowlisted origin's response carries no Access-Control-Allow-Origin header.");
            Assert.AreEqual("http://studio.example", string.Join(",", allowedOrigins!));

            using HttpRequestMessage disallowedRequest = new(HttpMethod.Post, endpoint)
            {
                Content = new StringContent("PREFIX : <http://example.org/> SELECT ?who WHERE { :alice :knows ?who }", Encoding.UTF8, "application/sparql-query")
            };
            disallowedRequest.Headers.Add("Origin", "http://intruder.example");
            using HttpResponseMessage disallowedResponse = await client.SendAsync(disallowedRequest, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsFalse(disallowedResponse.Headers.Contains("Access-Control-Allow-Origin"), "An origin outside the allowlist received an Access-Control-Allow-Origin header.");
        }
        finally
        {
            if(!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The <c>serve</c> command's <c>--cors-origin *</c> grants any origin cross-origin access: the response to a POST from an arbitrary origin carries the wildcard allow-origin header beside the results.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandAllowsAnyOriginWithCorsWildcard()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        (string dataPath, _) = WriteFixture();

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("serve");
        process.StartInfo.ArgumentList.Add("--port");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add("--data");
        process.StartInfo.ArgumentList.Add(dataPath);
        process.StartInfo.ArgumentList.Add("--cors-origin");
        process.StartInfo.ArgumentList.Add("*");

        process.Start();
        Task<string> serverErrors = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);

        try
        {
            Uri? endpoint = await ReadListeningEndpointAsync(process, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(endpoint, "The server did not report a listening address." + await DescribeServerFailureAsync(process, serverErrors).ConfigureAwait(false));

            using HttpClient client = new();
            using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
            {
                Content = new StringContent("PREFIX : <http://example.org/> SELECT ?who WHERE { :alice :knows ?who }", Encoding.UTF8, "application/sparql-query")
            };
            request.Headers.Add("Origin", "http://anywhere.example");
            using HttpResponseMessage response = await client.SendAsync(request, TestContext.CancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode, body);
            Assert.Contains("http://example.org/carol", body);
            Assert.IsTrue(response.Headers.TryGetValues("Access-Control-Allow-Origin", out IEnumerable<string>? origins), "The wildcard response carries no Access-Control-Allow-Origin header.");
            Assert.AreEqual("*", string.Join(",", origins!));
        }
        finally
        {
            if(!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The <c>serve</c> command reports its worker-thread minimum at startup. The floor ships lifted by default (the compute lane moved the build CPU off the serve pool, so a measured floor-lift gate retired it), so the reported value is the runtime's own minimum — the host still announces it.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandReportsWorkerThreadMinimumAtStartup()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        (string dataPath, _) = WriteFixture();

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("serve");
        process.StartInfo.ArgumentList.Add("--port");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add("--data");
        process.StartInfo.ArgumentList.Add(dataPath);

        process.Start();
        _ = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);

        try
        {
            //The floor ships lifted (default multiplier zero), so the host applies no raise and reports
            //the runtime's own worker-thread minimum, read back inside the host. The assertion confirms
            //the host announces a positive minimum at startup rather than a specific floored value.
            int? floor = await ReadWorkerThreadFloorAsync(process, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(floor, "The server did not report a worker-thread minimum.");
            Assert.IsGreaterThan(0, floor.Value, "The reported worker-thread minimum should be positive.");
        }
        finally
        {
            if(!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The <c>serve</c> command maps <c>GET /trace</c> as a Server-Sent-Events stream: a query answered while a subscriber is attached streams execution-trace frames — <c>event: trace</c> with a JSON data line carrying a non-empty correlation id, the sequence, and the kind.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandStreamsExecutionTraceOverServerSentEvents()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        (string dataPath, _) = WriteFixture();

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("serve");
        process.StartInfo.ArgumentList.Add("--port");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add("--data");
        process.StartInfo.ArgumentList.Add(dataPath);

        process.Start();
        Task<string> serverErrors = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);

        try
        {
            Uri? endpoint = await ReadListeningEndpointAsync(process, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(endpoint, "The server did not report a listening address." + await DescribeServerFailureAsync(process, serverErrors).ConfigureAwait(false));

            using HttpClient client = new();

            //Open the trace stream FIRST: the response headers flush only after the server registered the
            //subscription, so the query below cannot race an unsubscribed hub.
            using HttpRequestMessage traceRequest = new(HttpMethod.Get, new Uri(endpoint, "/trace"));
            using HttpResponseMessage traceResponse = await client.SendAsync(traceRequest, HttpCompletionOption.ResponseHeadersRead, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual("text/event-stream", traceResponse.Content.Headers.ContentType?.MediaType);

            using HttpRequestMessage queryRequest = new(HttpMethod.Post, endpoint)
            {
                Content = new StringContent("PREFIX : <http://example.org/> SELECT ?who WHERE { :alice :knows ?who }", Encoding.UTF8, "application/sparql-query")
            };
            using HttpResponseMessage queryResponse = await client.SendAsync(queryRequest, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(System.Net.HttpStatusCode.OK, queryResponse.StatusCode);

            //A crashed server closes the stream and the read returns null fast; a genuinely hung
            //server blocks the token-bound read and surfaces at the runner-level hang guard.
            using StreamReader reader = new(await traceResponse.Content.ReadAsStreamAsync(TestContext.CancellationToken).ConfigureAwait(false));
            bool sawTraceEvent = false;
            string? dataLine = null;
            while(await reader.ReadLineAsync(TestContext.CancellationToken).ConfigureAwait(false) is { } line)
            {
                if(line == "event: trace")
                {
                    sawTraceEvent = true;

                    continue;
                }

                if(sawTraceEvent && line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    dataLine = line;

                    break;
                }
            }

            Assert.IsNotNull(dataLine, "No trace frame arrived for the answered query." + await DescribeServerFailureAsync(process, serverErrors).ConfigureAwait(false));
            Assert.Contains("\"sequence\":", dataLine);
            Assert.Contains("\"kind\":\"", dataLine);
            Match correlation = TraceCorrelationRegex().Match(dataLine);
            Assert.IsTrue(correlation.Success, "The trace frame carries no correlation id: " + dataLine);
            Assert.AreNotEqual("00000000-0000-0000-0000-000000000000", correlation.Groups[1].Value, "The trace frame's correlation id is empty — per-run correlation minting failed.");
        }
        finally
        {
            if(!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The <c>serve</c> endpoint negotiates SPARQL-results-XML: a SELECT under <c>Accept: application/sparql-results+xml</c> answers the XML results document, and a token weighted <c>q=0</c> is excluded so the next acceptable format answers instead.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandNegotiatesSparqlResultsXml()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        Uri requestUri = new(server.Endpoint, "?query=" + Uri.EscapeDataString("PREFIX : <http://example.org/> SELECT ?who WHERE { :alice :knows ?who }"));

        using HttpRequestMessage xmlRequest = new(HttpMethod.Get, requestUri);
        xmlRequest.Headers.TryAddWithoutValidation("Accept", "application/sparql-results+xml");
        using HttpResponseMessage xmlResponse = await client.SendAsync(xmlRequest, TestContext.CancellationToken).ConfigureAwait(false);
        string xmlBody = await xmlResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, xmlResponse.StatusCode, xmlBody);
        Assert.AreEqual("application/sparql-results+xml", xmlResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<sparql", xmlBody);
        Assert.Contains("http://example.org/bob", xmlBody);

        //An XML token weighted q=0 is "not acceptable" (RFC 7231): the JSON token beside it answers instead.
        using HttpRequestMessage excludedRequest = new(HttpMethod.Get, requestUri);
        excludedRequest.Headers.TryAddWithoutValidation("Accept", "application/sparql-results+xml;q=0, application/sparql-results+json");
        using HttpResponseMessage excludedResponse = await client.SendAsync(excludedRequest, TestContext.CancellationToken).ConfigureAwait(false);
        string excludedBody = await excludedResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("application/sparql-results+json", excludedResponse.Content.Headers.ContentType?.MediaType, excludedBody);
    }

    /// <summary>An ASK answers a real results document: SPARQL-results-JSON under the default (no delimited boolean format exists) and SPARQL-results-XML when the Accept header asks.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandAnswersAskAsResultsJsonAndXml()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        Uri requestUri = new(server.Endpoint, "?query=" + Uri.EscapeDataString("PREFIX : <http://example.org/> ASK { :alice :knows :bob }"));

        using HttpResponseMessage jsonResponse = await client.GetAsync(requestUri, TestContext.CancellationToken).ConfigureAwait(false);
        string jsonBody = await jsonResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, jsonResponse.StatusCode, jsonBody);
        Assert.AreEqual("application/sparql-results+json", jsonResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"boolean\":true", jsonBody);

        using HttpRequestMessage xmlRequest = new(HttpMethod.Get, requestUri);
        xmlRequest.Headers.TryAddWithoutValidation("Accept", "application/sparql-results+xml");
        using HttpResponseMessage xmlResponse = await client.SendAsync(xmlRequest, TestContext.CancellationToken).ConfigureAwait(false);
        string xmlBody = await xmlResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("application/sparql-results+xml", xmlResponse.Content.Headers.ContentType?.MediaType, xmlBody);
        Assert.Contains("<boolean>true</boolean>", xmlBody);
    }

    /// <summary>A CONSTRUCT answers an RDF serialization: N-Triples by default and Turtle under <c>Accept: text/turtle</c>.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandAnswersConstructAsNTriplesAndTurtle()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        using StringContent construct = new("PREFIX : <http://example.org/> CONSTRUCT { :alice :linked ?who } WHERE { :alice :knows ?who }", Encoding.UTF8, "application/sparql-query");
        using HttpResponseMessage defaultResponse = await client.PostAsync(server.Endpoint, construct, TestContext.CancellationToken).ConfigureAwait(false);
        string defaultBody = await defaultResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, defaultResponse.StatusCode, defaultBody);
        Assert.AreEqual("application/n-triples", defaultResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<http://example.org/alice> <http://example.org/linked> <http://example.org/bob> .", defaultBody);
        Assert.Contains("<http://example.org/alice> <http://example.org/linked> <http://example.org/carol> .", defaultBody);

        Uri requestUri = new(server.Endpoint, "?query=" + Uri.EscapeDataString("PREFIX : <http://example.org/> CONSTRUCT { :alice :linked ?who } WHERE { :alice :knows ?who }"));
        using HttpRequestMessage turtleRequest = new(HttpMethod.Get, requestUri);
        turtleRequest.Headers.TryAddWithoutValidation("Accept", "text/turtle");
        using HttpResponseMessage turtleResponse = await client.SendAsync(turtleRequest, TestContext.CancellationToken).ConfigureAwait(false);
        string turtleBody = await turtleResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("text/turtle", turtleResponse.Content.Headers.ContentType?.MediaType, turtleBody);
        Assert.Contains("http://example.org/", turtleBody);
    }

    /// <summary>A CONSTRUCT under a tabular-results Accept header (or none) still answers its graph as N-Triples — the shape dispatches before the format, so no bindings writer ever sees a graph result.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandAnswersConstructUnderATabularAccept()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        Uri requestUri = new(server.Endpoint, "?query=" + Uri.EscapeDataString("PREFIX : <http://example.org/> CONSTRUCT { :alice :linked ?who } WHERE { :alice :knows ?who }"));

        using HttpRequestMessage jsonAcceptRequest = new(HttpMethod.Get, requestUri);
        jsonAcceptRequest.Headers.TryAddWithoutValidation("Accept", "application/sparql-results+json");
        using HttpResponseMessage jsonAcceptResponse = await client.SendAsync(jsonAcceptRequest, TestContext.CancellationToken).ConfigureAwait(false);
        string jsonAcceptBody = await jsonAcceptResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, jsonAcceptResponse.StatusCode, jsonAcceptBody);
        Assert.AreEqual("application/n-triples", jsonAcceptResponse.Content.Headers.ContentType?.MediaType, "A graph result under a tabular Accept answers the server-choice RDF serialization.");
        Assert.Contains("<http://example.org/alice> <http://example.org/linked> <http://example.org/bob> .", jsonAcceptBody);

        using HttpResponseMessage bareResponse = await client.GetAsync(requestUri, TestContext.CancellationToken).ConfigureAwait(false);
        string bareBody = await bareResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, bareResponse.StatusCode, bareBody);
        Assert.AreEqual("application/n-triples", bareResponse.Content.Headers.ContentType?.MediaType, "A graph result with no Accept answers N-Triples.");
    }

    /// <summary>The protocol dataset parameters select the queried dataset: <c>default-graph-uri</c> naming the loaded named graph makes its content the default graph (and hides the original default), and with NO protocol parameter the query's own <c>FROM NAMED</c> clause still resolves against the loaded graphs.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandSelectsTheNamedGraphThroughProtocolDataset()
    {
        (string dataPath, _) = WriteNQuadsFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        string datasetParameter = "&default-graph-uri=" + Uri.EscapeDataString("http://example.org/friends");

        //The named graph's content becomes the effective default graph.
        Uri selectedUri = new(server.Endpoint, "?query=" + Uri.EscapeDataString("ASK { <http://example.org/carol> <http://example.org/knows> <http://example.org/dana> }") + datasetParameter);
        using HttpResponseMessage selectedResponse = await client.GetAsync(selectedUri, TestContext.CancellationToken).ConfigureAwait(false);
        string selectedBody = await selectedResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, selectedResponse.StatusCode, selectedBody);
        Assert.Contains("\"boolean\":true", selectedBody);

        //The protocol dataset replaces the whole dataset, so the original default graph's triple is hidden.
        Uri hiddenUri = new(server.Endpoint, "?query=" + Uri.EscapeDataString("ASK { <http://example.org/alice> <http://example.org/knows> <http://example.org/bob> }") + datasetParameter);
        using HttpResponseMessage hiddenResponse = await client.GetAsync(hiddenUri, TestContext.CancellationToken).ConfigureAwait(false);
        string hiddenBody = await hiddenResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.Contains("\"boolean\":false", hiddenBody);

        //With no protocol parameter, the query's own FROM NAMED clause resolves against the loaded graphs.
        Uri fromNamedUri = new(server.Endpoint, "?query=" + Uri.EscapeDataString("ASK FROM NAMED <http://example.org/friends> { GRAPH <http://example.org/friends> { <http://example.org/carol> <http://example.org/knows> <http://example.org/dana> } }"));
        using HttpResponseMessage fromNamedResponse = await client.GetAsync(fromNamedUri, TestContext.CancellationToken).ConfigureAwait(false);
        string fromNamedBody = await fromNamedResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, fromNamedResponse.StatusCode, fromNamedBody);
        Assert.Contains("\"boolean\":true", fromNamedBody);
    }

    /// <summary>A dataset parameter naming no loaded graph refuses with 400 and names the IRI.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandRefusesAnUnknownDatasetGraph()
    {
        (string dataPath, _) = WriteNQuadsFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        Uri requestUri = new(server.Endpoint, "?query=" + Uri.EscapeDataString("ASK { ?s ?p ?o }") + "&default-graph-uri=" + Uri.EscapeDataString("http://example.org/nowhere"));
        using HttpResponseMessage response = await client.GetAsync(requestUri, TestContext.CancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, response.StatusCode, body);
        Assert.Contains("http://example.org/nowhere", body);
    }

    /// <summary>The protocol fault split: a malformed query and an update posted as a query answer 400, while a well-formed query the engine refuses (a <c>SERVICE</c> call with no configured transport) answers 500.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandSplitsProtocolFaults()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();

        Uri malformedUri = new(server.Endpoint, "?query=" + Uri.EscapeDataString("SELECT ?s WHERE { broken"));
        using HttpResponseMessage malformedResponse = await client.GetAsync(malformedUri, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, malformedResponse.StatusCode, "A malformed query is the protocol's 400 class.");

        Uri updateUri = new(server.Endpoint, "?query=" + Uri.EscapeDataString("INSERT DATA { <http://example.org/a> <http://example.org/b> <http://example.org/c> }"));
        using HttpResponseMessage updateResponse = await client.GetAsync(updateUri, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, updateResponse.StatusCode, "An update where a query belongs is malformed for the query operation.");

        Uri refusedUri = new(server.Endpoint, "?query=" + Uri.EscapeDataString("SELECT ?s WHERE { SERVICE <http://example.invalid/sparql> { ?s ?p ?o } }"));
        using HttpResponseMessage refusedResponse = await client.GetAsync(refusedUri, TestContext.CancellationToken).ConfigureAwait(false);
        string refusedBody = await refusedResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.InternalServerError, refusedResponse.StatusCode, refusedBody);
    }

    /// <summary>A POST whose content type is neither protocol form answers 415, while a charset-suffixed <c>application/sparql-query</c> stays accepted.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandRefusesAnUnknownPostContentType()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();

        using StringContent unknown = new("PREFIX : <http://example.org/> SELECT ?who WHERE { :alice :knows ?who }", Encoding.UTF8, "text/plain");
        using HttpResponseMessage unknownResponse = await client.PostAsync(server.Endpoint, unknown, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.UnsupportedMediaType, unknownResponse.StatusCode, "An unrecognized POST content type is 415.");

        //StringContent with a media type stamps "application/sparql-query; charset=utf-8" — the legal
        //charset-suffixed spelling must keep answering.
        using StringContent charsetSuffixed = new("PREFIX : <http://example.org/> SELECT ?who WHERE { :alice :knows ?who }", Encoding.UTF8, "application/sparql-query");
        using HttpResponseMessage charsetResponse = await client.PostAsync(server.Endpoint, charsetSuffixed, TestContext.CancellationToken).ConfigureAwait(false);
        string charsetBody = await charsetResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, charsetResponse.StatusCode, charsetBody);
        Assert.Contains("http://example.org/bob", charsetBody);
    }

    /// <summary>A GET with NO <c>query</c> parameter answers the SPARQL 1.1 Service Description generated from live state — the extension-function catalog names itself — while a present-but-empty <c>query</c> stays the missing-query 400.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandServesTheServiceDescription()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();

        using HttpResponseMessage described = await client.GetAsync(server.Endpoint, TestContext.CancellationToken).ConfigureAwait(false);
        string description = await described.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, described.StatusCode, description);
        Assert.AreEqual("text/turtle", described.Content.Headers.ContentType?.MediaType);
        Assert.Contains("sparql-service-description", description);
        Assert.Contains("SPARQL_Results_XML", description);
        Assert.Contains("http://www.opengis.net/def/function/geosparql/distance", description);

        //A PRESENT but empty query parameter is a malformed request, never the service description.
        Uri emptyQueryUri = new(server.Endpoint, "?query=");
        using HttpResponseMessage emptyQueryResponse = await client.GetAsync(emptyQueryUri, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, emptyQueryResponse.StatusCode, "An empty query parameter answers the missing-query 400.");
    }

    /// <summary>The <c>serve</c> endpoint answers the protocol's form submission: an <c>application/x-www-form-urlencoded</c> POST carries the query as the <c>query</c> field and the dataset parameters as sibling fields, which replace the queried dataset exactly as their URL counterparts do.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandAnswersUrlEncodedFormPostQuery()
    {
        (string dataPath, _) = WriteNQuadsFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();

        using FormUrlEncodedContent queryForm = new(new Dictionary<string, string>
        {
            ["query"] = "ASK { <http://example.org/alice> <http://example.org/knows> <http://example.org/bob> }"
        });

        using HttpResponseMessage queryResponse = await client.PostAsync(server.Endpoint, queryForm, TestContext.CancellationToken).ConfigureAwait(false);
        string queryBody = await queryResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, queryResponse.StatusCode, queryBody);
        Assert.Contains("\"boolean\":true", queryBody);

        //The dataset parameters ride the same form: the named graph becomes the effective default graph.
        using FormUrlEncodedContent selectedForm = new(new Dictionary<string, string>
        {
            ["query"] = "ASK { <http://example.org/carol> <http://example.org/knows> <http://example.org/dana> }",
            ["default-graph-uri"] = "http://example.org/friends"
        });

        using HttpResponseMessage selectedResponse = await client.PostAsync(server.Endpoint, selectedForm, TestContext.CancellationToken).ConfigureAwait(false);
        string selectedBody = await selectedResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, selectedResponse.StatusCode, selectedBody);
        Assert.Contains("\"boolean\":true", selectedBody);

        //The replacement dataset hides the original default graph's triple, so the field is honored, not ignored.
        using FormUrlEncodedContent hiddenForm = new(new Dictionary<string, string>
        {
            ["query"] = "ASK { <http://example.org/alice> <http://example.org/knows> <http://example.org/bob> }",
            ["default-graph-uri"] = "http://example.org/friends"
        });

        using HttpResponseMessage hiddenResponse = await client.PostAsync(server.Endpoint, hiddenForm, TestContext.CancellationToken).ConfigureAwait(false);
        string hiddenBody = await hiddenResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.Contains("\"boolean\":false", hiddenBody);
    }

    /// <summary>The <c>serve</c> endpoint negotiates the delimited SELECT formats: <c>Accept: text/tab-separated-values</c> answers the TSV results document and <c>Accept: text/csv</c> answers the CSV one.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandNegotiatesTheDelimitedFormats()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        Uri requestUri = new(server.Endpoint, "?query=" + Uri.EscapeDataString("PREFIX : <http://example.org/> SELECT ?who WHERE { :alice :knows ?who }"));

        using HttpRequestMessage tsvRequest = new(HttpMethod.Get, requestUri);
        tsvRequest.Headers.TryAddWithoutValidation("Accept", "text/tab-separated-values");
        using HttpResponseMessage tsvResponse = await client.SendAsync(tsvRequest, TestContext.CancellationToken).ConfigureAwait(false);
        string tsvBody = await tsvResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, tsvResponse.StatusCode, tsvBody);
        Assert.AreEqual("text/tab-separated-values", tsvResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("http://example.org/bob", tsvBody);
        Assert.Contains("http://example.org/carol", tsvBody);

        using HttpRequestMessage csvRequest = new(HttpMethod.Get, requestUri);
        csvRequest.Headers.TryAddWithoutValidation("Accept", "text/csv");
        using HttpResponseMessage csvResponse = await client.SendAsync(csvRequest, TestContext.CancellationToken).ConfigureAwait(false);
        string csvBody = await csvResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, csvResponse.StatusCode, csvBody);
        Assert.AreEqual("text/csv", csvResponse.Content.Headers.ContentType?.MediaType, csvBody);
        Assert.Contains("http://example.org/bob", csvBody);
    }

    /// <summary>The literal-diagnostics endpoint answers a broken <c>geo:gmlLiteral</c> body with the invalid diagnosis: the truncated XML fragment cannot extend its grammar, so the codec's malformed-document kind and a located byte offset both cross the wire beside the echoed datatype. The offsets themselves are pinned engine-side; this row pins that a located one survives the round trip.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandDescribesAnInvalidGmlLiteral()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        using StringContent request = new("""{"datatype":"http://www.opengis.net/ont/geosparql#gmlLiteral","body":"<gml:Point xmlns:gml=\"http://www.opengis.net/gml/3.2\" srsName=\"http://www.opengis.net/def/crs/OGC/1.3/CRS84\"><gml:pos>1 2</gml:pos>"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(LiteralDiagnosticsEndpoint(server.Endpoint), request, TestContext.CancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode, body);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType, body);
        Assert.Contains("\"status\":\"invalid\"", body);
        Assert.Contains("\"kind\":\"MalformedDocument\"", body);
        Assert.Contains("\"datatype\":\"http://www.opengis.net/ont/geosparql#gmlLiteral\"", body);

        Match offset = LiteralDiagnosisByteOffsetRegex().Match(body);
        Assert.IsTrue(offset.Success, "An invalid diagnosis carries the refusal's byte offset: " + body);
        Assert.IsGreaterThanOrEqualTo(0, int.Parse(offset.Groups[1].Value, CultureInfo.InvariantCulture), "The refusal names an offending byte of the literal body: " + body);
    }

    /// <summary>A well-formed <c>geo:wktLiteral</c> body the reader accepts answers the plain valid diagnosis: no refusal fields ride a status that locates nothing.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandDescribesAValidWktLiteral()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        using StringContent request = new("""{"datatype":"http://www.opengis.net/ont/geosparql#wktLiteral","body":"POINT (1 2)"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(LiteralDiagnosticsEndpoint(server.Endpoint), request, TestContext.CancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode, body);
        Assert.Contains("\"status\":\"valid\"", body);
        Assert.Contains("\"datatype\":\"http://www.opengis.net/ont/geosparql#wktLiteral\"", body);
        Assert.DoesNotContain("\"kind\"", body);
        Assert.DoesNotContain("\"byteOffset\"", body);
    }

    /// <summary>A structurally thin <c>geo:wktLiteral</c> body — a line string carrying one position — answers the warning diagnosis: the datatype's own grammar tolerates it, yet the codec reader refuses it, so no <c>geof:</c> evaluation over it can succeed. The warning locates the refusal exactly as the invalid state does.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandWarnsOnAStructurallyThinWktLiteral()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        using StringContent request = new("""{"datatype":"http://www.opengis.net/ont/geosparql#wktLiteral","body":"LINESTRING (1 2)"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(LiteralDiagnosticsEndpoint(server.Endpoint), request, TestContext.CancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode, body);
        Assert.Contains("\"status\":\"warning\"", body);
        Assert.Contains("\"kind\":\"StructuralViolation\"", body);

        Match offset = LiteralDiagnosisByteOffsetRegex().Match(body);
        Assert.IsTrue(offset.Success, "A warning diagnosis carries the refusal's byte offset: " + body);
        Assert.IsGreaterThanOrEqualTo(0, int.Parse(offset.Groups[1].Value, CultureInfo.InvariantCulture), "The refusal names an offending byte of the literal body: " + body);
    }

    /// <summary>A datatype outside the geometry-literal family is an abstention, never a fault: the endpoint answers 200 with the unsupported status and no refusal fields, so an editor may send every typed literal it sees without classifying them first.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandAbstainsOnAnUnsupportedLiteralDatatype()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        using StringContent request = new("""{"datatype":"http://www.w3.org/2001/XMLSchema#string","body":"POINT (1 2)"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(LiteralDiagnosticsEndpoint(server.Endpoint), request, TestContext.CancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode, body);
        Assert.Contains("\"status\":\"unsupported\"", body);
        Assert.Contains("\"datatype\":\"http://www.w3.org/2001/XMLSchema#string\"", body);
        Assert.DoesNotContain("\"kind\"", body);
        Assert.DoesNotContain("\"byteOffset\"", body);
    }

    /// <summary>A literal-diagnostics POST whose content type is not <c>application/json</c> answers 415 — the same unsupported-media-type split the protocol route draws.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandRefusesANonJsonLiteralDiagnosticsRequest()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        using StringContent request = new("""{"datatype":"http://www.opengis.net/ont/geosparql#wktLiteral","body":"POINT (1 2)"}""", Encoding.UTF8, "text/plain");
        using HttpResponseMessage response = await client.PostAsync(LiteralDiagnosticsEndpoint(server.Endpoint), request, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.UnsupportedMediaType, response.StatusCode, "A literal-diagnostics request outside application/json is 415.");
    }

    /// <summary>The request document must be the two-field object: JSON the reader cannot scan, a document missing one field, and a document carrying an unrecognized field all answer 400 rather than a diagnosis.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandRefusesAMalformedLiteralDiagnosticsRequest()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        Uri endpoint = LiteralDiagnosticsEndpoint(server.Endpoint);

        using StringContent truncated = new("""{"datatype":"http://www.opengis.net/ont/geosparql#wktLiteral","body":""", Encoding.UTF8, "application/json");
        using HttpResponseMessage truncatedResponse = await client.PostAsync(endpoint, truncated, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, truncatedResponse.StatusCode, "A document the reader cannot scan is a malformed request.");

        using StringContent missingField = new("""{"datatype":"http://www.opengis.net/ont/geosparql#wktLiteral"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage missingFieldResponse = await client.PostAsync(endpoint, missingField, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, missingFieldResponse.StatusCode, "Both fields are required.");

        using StringContent unknownField = new("""{"datatype":"http://www.opengis.net/ont/geosparql#wktLiteral","body":"POINT (1 2)","offset":3}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage unknownFieldResponse = await client.PostAsync(endpoint, unknownField, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, unknownFieldResponse.StatusCode, "An unrecognized field is a malformed request, never a silently ignored one.");
    }

    /// <summary>The completion endpoint answers the caret's context against the dataset this server serves: the object variable of a predicate whose only observed object is a typed literal comes back carrying that datatype and the sampling source, so the server tier's completion describes the loaded data rather than the grammar alone.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task CompletionEndpointResolvesVariableDatatypesAgainstServedData()
    {
        string dataPath = WriteTypedLiteralFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        string query = "SELECT ?o WHERE { ?s <http://example.org/age> ?o }";
        using HttpClient client = new();
        using StringContent request = new("{\"query\":\"" + query + "\",\"caret\":" + query.Length.ToString(CultureInfo.InvariantCulture) + "}", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(CompletionEndpoint(server.Endpoint), request, TestContext.CancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode, body);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType, body);
        Assert.Contains("\"name\":\"o\"", body);
        Assert.Contains("\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\"", body);
        Assert.Contains("\"datatypeSource\":\"DataSample\"", body);
    }

    /// <summary>The Turtle-family completion endpoint answers the caret's context from the grammar alone: after a subject the expected tokens carry a prefixed-name verb and the innermost enclosing production is the subject statement, and the caret returns as the byte offset the editor's code-unit index transcodes to.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task TurtleCompletionEndpointAnswersTheContext()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        string source = "@prefix ex: <http://example.org/> .\nex:s ";
        string caretText = source.Length.ToString(CultureInfo.InvariantCulture);
        using HttpClient client = new();
        using StringContent request = new("{\"source\":\"" + source.Replace("\n", "\\n", StringComparison.Ordinal) + "\",\"caret\":" + caretText + ",\"syntax\":\"turtle\"}", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(TurtleCompletionEndpoint(server.Endpoint), request, TestContext.CancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode, body);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType, body);

        //The buffer is ASCII, so its byte offset and its code-unit index coincide — the value the caret must keep.
        Assert.Contains("\"caret\":" + caretText, body);
        Assert.Contains("\"PrefixedName\"", body);
        Assert.Contains("\"SubjectStatement\"", body);
    }

    /// <summary>The editor-vocabulary endpoint carries the corpus this composition admits: a geometry datatype the geospatial roster already names rides it once as a prefixed name, while the house A5 literal — which no conventional prefix names — rides it as a full IRI, so the registry's own registrations reach the editor.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task EditorVocabularyCarriesTheRegistryDatatypeLane()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        using HttpResponseMessage response = await client.GetAsync(EditorVocabularyEndpoint(server.Endpoint), TestContext.CancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode, body);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType, body);
        Assert.AreEqual(1, CountOccurrences(body, "\"geo:wktLiteral\""), "A registered datatype the geospatial roster already carries is offered once, as its prefixed name: " + body);
        Assert.Contains("\"<https://lumoin.com/veritas/dggs/a5Literal>\"", body);
        Assert.DoesNotContain("\"<http://www.opengis.net/ont/geosparql#", body);
    }

    /// <summary>A completion POST whose content type is not <c>application/json</c> answers 415 — the same unsupported-media-type split the protocol route draws.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task CompletionEndpointRefusesANonJsonRequest()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        using StringContent request = new("""{"query":"SELECT * WHERE { ?s ?p ?o }","caret":6}""", Encoding.UTF8, "text/plain");
        using HttpResponseMessage response = await client.PostAsync(CompletionEndpoint(server.Endpoint), request, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.UnsupportedMediaType, response.StatusCode, "A completion request outside application/json is 415.");
    }

    /// <summary>The caret is the editor's code-unit index, so its contract is a JSON number a 32-bit integer represents: a fraction, a magnitude outside that range, and a value sent as a string all answer 400 rather than faulting the request.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task CompletionEndpointRefusesACaretThatIsNoInteger()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        Uri endpoint = CompletionEndpoint(server.Endpoint);

        using StringContent fraction = new("""{"query":"SELECT * WHERE { ?s ?p ?o }","caret":3.5}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage fractionResponse = await client.PostAsync(endpoint, fraction, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, fractionResponse.StatusCode, "A fractional caret names no code unit.");

        using StringContent outOfRange = new("""{"query":"SELECT * WHERE { ?s ?p ?o }","caret":99999999999}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage outOfRangeResponse = await client.PostAsync(endpoint, outOfRange, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, outOfRangeResponse.StatusCode, "A caret outside the 32-bit range names no code unit.");

        using StringContent quoted = new("""{"query":"SELECT * WHERE { ?s ?p ?o }","caret":"3"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage quotedResponse = await client.PostAsync(endpoint, quoted, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, quotedResponse.StatusCode, "The caret crosses the wire as a number, never as a string.");
    }

    /// <summary>A Turtle-completion POST whose content type is not <c>application/json</c> answers 415, exactly as the SPARQL completion route does.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task TurtleCompletionEndpointRefusesANonJsonRequest()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        using StringContent request = new("""{"source":"ex:s ","caret":5,"syntax":"turtle"}""", Encoding.UTF8, "text/plain");
        using HttpResponseMessage response = await client.PostAsync(TurtleCompletionEndpoint(server.Endpoint), request, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(System.Net.HttpStatusCode.UnsupportedMediaType, response.StatusCode, "A Turtle-completion request outside application/json is 415.");
    }

    /// <summary>The Turtle-completion caret carries the same number contract: a fraction, an out-of-range magnitude, and a string value all answer 400.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task TurtleCompletionEndpointRefusesACaretThatIsNoInteger()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        Uri endpoint = TurtleCompletionEndpoint(server.Endpoint);

        using StringContent fraction = new("""{"source":"ex:s ","caret":3.5,"syntax":"turtle"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage fractionResponse = await client.PostAsync(endpoint, fraction, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, fractionResponse.StatusCode, "A fractional caret names no code unit.");

        using StringContent outOfRange = new("""{"source":"ex:s ","caret":99999999999,"syntax":"turtle"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage outOfRangeResponse = await client.PostAsync(endpoint, outOfRange, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, outOfRangeResponse.StatusCode, "A caret outside the 32-bit range names no code unit.");

        using StringContent quoted = new("""{"source":"ex:s ","caret":"3","syntax":"turtle"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage quotedResponse = await client.PostAsync(endpoint, quoted, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, quotedResponse.StatusCode, "The caret crosses the wire as a number, never as a string.");
    }

    /// <summary>
    /// The whole what-if flow over the worlds face: the listing seeds with the primary world, a fork
    /// registers with its lineage and shares the primary's state identifier until an update diverges it,
    /// the world-scoped query sees the hypothetical in the fork and not in the primary world, the diff
    /// names exactly the one addition with exact totals, and the drop returns the listing to the primary
    /// world alone.
    /// </summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task WorldsFaceCarriesTheWhatIfFlow()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();

        string listing = await client.GetStringAsync(WorldsEndpoint(server.Endpoint, "/worlds"), TestContext.CancellationToken).ConfigureAwait(false);
        Assert.Contains("\"name\":\"main\"", listing);
        Assert.Contains("\"parent\":null", listing);
        Assert.DoesNotContain("what-if", listing);

        using StringContent forkRequest = new("""{"source":"main","name":"what-if"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage forkResponse = await client.PostAsync(WorldsEndpoint(server.Endpoint, "/worlds/fork"), forkRequest, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual("{\"outcome\":\"forked\"}", await forkResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false));

        using StringContent updateRequest = new("""{"world":"what-if","update":"PREFIX : <http://example.org/> INSERT DATA { :alice :knows :dave }"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage updateResponse = await client.PostAsync(WorldsEndpoint(server.Endpoint, "/worlds/update"), updateRequest, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual("{\"outcome\":\"updated\"}", await updateResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false));

        using HttpRequestMessage forkQuery = new(HttpMethod.Post, WorldsEndpoint(server.Endpoint, "/worlds/query"))
        {
            Content = new StringContent("""{"world":"what-if","query":"PREFIX : <http://example.org/> SELECT ?who WHERE { :alice :knows ?who }"}""", Encoding.UTF8, "application/json")
        };
        forkQuery.Headers.Accept.ParseAdd("application/sparql-results+json");
        using HttpResponseMessage forkQueryResponse = await client.SendAsync(forkQuery, TestContext.CancellationToken).ConfigureAwait(false);
        string forkResults = await forkQueryResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, forkQueryResponse.StatusCode, forkResults);
        Assert.Contains("dave", forkResults, "The fork answers its own committed hypothetical.");

        using HttpRequestMessage primaryQuery = new(HttpMethod.Post, WorldsEndpoint(server.Endpoint, "/worlds/query"))
        {
            Content = new StringContent("""{"world":"main","query":"PREFIX : <http://example.org/> SELECT ?who WHERE { :alice :knows ?who }"}""", Encoding.UTF8, "application/json")
        };
        primaryQuery.Headers.Accept.ParseAdd("application/sparql-results+json");
        using HttpResponseMessage primaryQueryResponse = await client.SendAsync(primaryQuery, TestContext.CancellationToken).ConfigureAwait(false);
        string primaryResults = await primaryQueryResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false);
        Assert.DoesNotContain("dave", primaryResults, "The primary world never sees the fork's commit.");

        string divergedListing = await client.GetStringAsync(WorldsEndpoint(server.Endpoint, "/worlds"), TestContext.CancellationToken).ConfigureAwait(false);
        Assert.Contains("\"name\":\"what-if\"", divergedListing);
        Assert.Contains("\"parent\":\"main\"", divergedListing);

        string diff = await client.GetStringAsync(WorldsEndpoint(server.Endpoint, "/worlds/diff?from=main&to=what-if"), TestContext.CancellationToken).ConfigureAwait(false);
        Assert.Contains("\"outcome\":\"diffed\"", diff);
        Assert.Contains("\"totalTransitions\":1", diff);
        Assert.Contains("\"totalTriples\":1", diff);
        Assert.Contains("\"truncated\":false", diff);
        Assert.Contains("<http://example.org/dave>", diff, "The one addition crosses decoded to lexical forms: " + diff);

        using StringContent dropRequest = new("""{"world":"what-if"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage dropResponse = await client.PostAsync(WorldsEndpoint(server.Endpoint, "/worlds/drop"), dropRequest, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual("{\"outcome\":\"dropped\"}", await dropResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false));

        string finalListing = await client.GetStringAsync(WorldsEndpoint(server.Endpoint, "/worlds"), TestContext.CancellationToken).ConfigureAwait(false);
        Assert.DoesNotContain("what-if", finalListing, "The drop returns the listing to the primary world alone.");
    }

    /// <summary>The expected worlds conditions cross as outcome tokens, never faults: an unknown fork source, a taken fork name, the never-droppable primary world, an unknown drop name, and an unknown diff side each answer their document, and a query or update naming an unknown world answers the 400 failure document.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task WorldsFaceAnswersExpectedConditionsAsOutcomes()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();
        Uri forkEndpoint = WorldsEndpoint(server.Endpoint, "/worlds/fork");

        using StringContent unknownSource = new("""{"source":"missing","name":"other"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage unknownSourceResponse = await client.PostAsync(forkEndpoint, unknownSource, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual("{\"outcome\":\"unknownSource\"}", await unknownSourceResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false));

        using StringContent duplicateName = new("""{"source":"main","name":"main"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage duplicateNameResponse = await client.PostAsync(forkEndpoint, duplicateName, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual("{\"outcome\":\"duplicateName\"}", await duplicateNameResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false));

        using StringContent primaryDrop = new("""{"world":"main"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage primaryDropResponse = await client.PostAsync(WorldsEndpoint(server.Endpoint, "/worlds/drop"), primaryDrop, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual("{\"outcome\":\"primaryWorld\"}", await primaryDropResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false));

        using StringContent unknownDrop = new("""{"world":"missing"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage unknownDropResponse = await client.PostAsync(WorldsEndpoint(server.Endpoint, "/worlds/drop"), unknownDrop, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual("{\"outcome\":\"unknownWorld\"}", await unknownDropResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false));

        string unknownDiff = await client.GetStringAsync(WorldsEndpoint(server.Endpoint, "/worlds/diff?from=main&to=missing"), TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual("{\"outcome\":\"unknownWorld\"}", unknownDiff);

        using HttpRequestMessage unknownWorldQuery = new(HttpMethod.Post, WorldsEndpoint(server.Endpoint, "/worlds/query"))
        {
            Content = new StringContent("""{"world":"missing","query":"SELECT ?s WHERE { ?s ?p ?o }"}""", Encoding.UTF8, "application/json")
        };
        unknownWorldQuery.Headers.Accept.ParseAdd("application/sparql-results+json");
        using HttpResponseMessage unknownWorldQueryResponse = await client.SendAsync(unknownWorldQuery, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, unknownWorldQueryResponse.StatusCode, "A query naming an unknown world is the caller's contract fault.");
        Assert.Contains("\"error\"", await unknownWorldQueryResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false));

        using StringContent unknownWorldUpdate = new("""{"world":"missing","update":"INSERT DATA { <http://example.org/s> <http://example.org/p> <http://example.org/o> }"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage unknownWorldUpdateResponse = await client.PostAsync(WorldsEndpoint(server.Endpoint, "/worlds/update"), unknownWorldUpdate, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, unknownWorldUpdateResponse.StatusCode);
        Assert.Contains("\"error\"", await unknownWorldUpdateResponse.Content.ReadAsStringAsync(TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>The worlds POST routes draw the same request boundaries the other first-party routes draw: a content type outside <c>application/json</c> answers 415, and a document that is not exactly the named string fields — a missing field, an unrecognized field, a non-string value, or a body the reader cannot scan — answers 400.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task WorldsRoutesRefuseNonJsonAndMalformedRequests()
    {
        (string dataPath, _) = WriteFixture();
        ServeSession server = await StartServeAsync(dataPath).ConfigureAwait(false);
        await using var serverScope = server.ConfigureAwait(false);

        using HttpClient client = new();

        using StringContent plainText = new("""{"source":"main","name":"other"}""", Encoding.UTF8, "text/plain");
        using HttpResponseMessage plainTextResponse = await client.PostAsync(WorldsEndpoint(server.Endpoint, "/worlds/fork"), plainText, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.UnsupportedMediaType, plainTextResponse.StatusCode, "A worlds request outside application/json is 415.");

        using StringContent missingField = new("""{"source":"main"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage missingFieldResponse = await client.PostAsync(WorldsEndpoint(server.Endpoint, "/worlds/fork"), missingField, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, missingFieldResponse.StatusCode, "Both fields are required.");

        using StringContent unknownField = new("""{"world":"main","update":"INSERT DATA { <a:s> <a:p> <a:o> }","base":"x"}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage unknownFieldResponse = await client.PostAsync(WorldsEndpoint(server.Endpoint, "/worlds/update"), unknownField, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, unknownFieldResponse.StatusCode, "An unrecognized field is a malformed request, never a silently ignored one.");

        using StringContent numberValue = new("""{"world":7}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage numberValueResponse = await client.PostAsync(WorldsEndpoint(server.Endpoint, "/worlds/drop"), numberValue, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, numberValueResponse.StatusCode, "The world crosses the wire as a string, never as a number.");

        using StringContent truncated = new("""{"world":"main","query":""", Encoding.UTF8, "application/json");
        using HttpResponseMessage truncatedResponse = await client.PostAsync(WorldsEndpoint(server.Endpoint, "/worlds/query"), truncated, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, truncatedResponse.StatusCode, "A document the reader cannot scan is a malformed request.");

        using HttpResponseMessage missingDiffParameters = await client.GetAsync(WorldsEndpoint(server.Endpoint, "/worlds/diff?from=main"), TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, missingDiffParameters.StatusCode, "The diff route requires both world parameters.");
    }

    /// <summary>The literal-diagnostics endpoint on the origin a serve session reports — its own route beside the SPARQL endpoint.</summary>
    /// <param name="sparqlEndpoint">The SPARQL endpoint address the session reported.</param>
    /// <returns>The literal-diagnostics address.</returns>
    private static Uri LiteralDiagnosticsEndpoint(Uri sparqlEndpoint)
    {
        return new Uri(sparqlEndpoint, "/literal-diagnostics");
    }

    /// <summary>The SPARQL completion endpoint on the origin a serve session reports.</summary>
    /// <param name="sparqlEndpoint">The SPARQL endpoint address the session reported.</param>
    /// <returns>The completion address.</returns>
    private static Uri CompletionEndpoint(Uri sparqlEndpoint)
    {
        return new Uri(sparqlEndpoint, "/completion");
    }

    /// <summary>The Turtle-family completion endpoint on the origin a serve session reports.</summary>
    /// <param name="sparqlEndpoint">The SPARQL endpoint address the session reported.</param>
    /// <returns>The Turtle-completion address.</returns>
    private static Uri TurtleCompletionEndpoint(Uri sparqlEndpoint)
    {
        return new Uri(sparqlEndpoint, "/turtle-completion");
    }

    /// <summary>The editor-vocabulary endpoint on the origin a serve session reports.</summary>
    /// <param name="sparqlEndpoint">The SPARQL endpoint address the session reported.</param>
    /// <returns>The editor-vocabulary address.</returns>
    private static Uri EditorVocabularyEndpoint(Uri sparqlEndpoint)
    {
        return new Uri(sparqlEndpoint, "/editor-vocabulary");
    }

    /// <summary>A worlds-face route on the origin a serve session reports: the listing route itself, or one of the routes under it.</summary>
    /// <param name="sparqlEndpoint">The SPARQL endpoint address the session reported.</param>
    /// <param name="relative">The route below the origin, e.g. <c>/worlds</c> or <c>/worlds/fork</c>.</param>
    /// <returns>The route address.</returns>
    private static Uri WorldsEndpoint(Uri sparqlEndpoint, string relative)
    {
        return new Uri(sparqlEndpoint, relative);
    }

    /// <summary>Counts the non-overlapping occurrences of <paramref name="value"/> in <paramref name="text"/> — the exactly-once pin a corpus row needs, which a containment assert alone cannot state.</summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="value">The value to count.</param>
    /// <returns>The number of occurrences.</returns>
    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = text.IndexOf(value, StringComparison.Ordinal);
        while(index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }

    /// <summary>A running <c>serve</c> child for one protocol row: started over a data file, endpoint awaited, killed whole on disposal.</summary>
    private sealed class ServeSession : IAsyncDisposable
    {
        /// <summary>The server process.</summary>
        private Process Process { get; }

        /// <summary>The token disposal's exit wait runs under.</summary>
        private CancellationToken CancellationToken { get; }

        /// <summary>The reported SPARQL endpoint address.</summary>
        public Uri Endpoint { get; }

        /// <summary>Wraps a started server.</summary>
        /// <param name="process">The server process.</param>
        /// <param name="endpoint">The reported endpoint address.</param>
        /// <param name="cancellationToken">The token disposal's exit wait runs under.</param>
        public ServeSession(Process process, Uri endpoint, CancellationToken cancellationToken)
        {
            Process = process;
            Endpoint = endpoint;
            CancellationToken = cancellationToken;
        }

        /// <summary>Kills the server's whole process tree and waits for its exit.</summary>
        /// <returns>The asynchronous disposal.</returns>
        public async ValueTask DisposeAsync()
        {
            if(!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
            }

            await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
            Process.Dispose();
        }
    }

    /// <summary>Starts the <c>serve</c> command over <paramref name="dataPath"/> on an ephemeral port and waits for its endpoint; inconclusive when the CLI executable is not built, failed when the server does not report an address.</summary>
    /// <param name="dataPath">The data file to serve.</param>
    /// <returns>The running session.</returns>
    private async Task<ServeSession> StartServeAsync(string dataPath)
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");
        }

        Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("serve");
        process.StartInfo.ArgumentList.Add("--port");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add("--data");
        process.StartInfo.ArgumentList.Add(dataPath);

        process.Start();
        Task<string> serverErrors = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);

        Uri? endpoint = await ReadListeningEndpointAsync(process, TestContext.CancellationToken).ConfigureAwait(false);
        if(endpoint is null)
        {
            string failure = await DescribeServerFailureAsync(process, serverErrors).ConfigureAwait(false);
            if(!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(TestContext.CancellationToken).ConfigureAwait(false);
            process.Dispose();
            Assert.Fail("The server did not report a listening address." + failure);
        }

        return new ServeSession(process, endpoint!, TestContext.CancellationToken);
    }

    /// <summary>Describes a serve-command startup failure for the endpoint assert: for a child that already exited (the crash face, whose error stream is drained), the exit code and stderr text; empty for a live server, whose error stream stays open and is never awaited here.</summary>
    /// <param name="process">The server process.</param>
    /// <param name="serverErrors">The stderr drain started at launch.</param>
    /// <returns>The failure description, or an empty string for a live server.</returns>
    private static async Task<string> DescribeServerFailureAsync(Process process, Task<string> serverErrors)
    {
        if(!process.HasExited)
        {
            return string.Empty;
        }

        string errors = await serverErrors.ConfigureAwait(false);

        return " The server process exited with code " + process.ExitCode + ". Server stderr: " + errors;
    }

    /// <summary>Reads the child's standard output until it prints the SPARQL endpoint address. A crashed child closes its output and the read returns <see langword="null"/> fast; a hung child blocks the token-bound read for the runner-level hang guard.</summary>
    /// <param name="process">The running server process.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>The endpoint URI, or <see langword="null"/> if it was not reported.</returns>
    private static async Task<Uri?> ReadListeningEndpointAsync(Process process, CancellationToken cancellationToken)
    {
        while(await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            Match match = ListeningEndpointRegex().Match(line);
            if(match.Success)
            {
                return new Uri(match.Groups[1].Value);
            }
        }

        return null;
    }

    /// <summary>Reads the child's standard output until it prints the worker-thread floor. A crashed child closes its output and the read returns <see langword="null"/> fast; a hung child blocks the token-bound read for the runner-level hang guard.</summary>
    /// <param name="process">The running server process.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>The reported worker-thread floor, or <see langword="null"/> if it was not reported.</returns>
    private static async Task<int?> ReadWorkerThreadFloorAsync(Process process, CancellationToken cancellationToken)
    {
        while(await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            Match match = WorkerThreadFloorRegex().Match(line);
            if(match.Success)
            {
                return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    /// <summary>Writes a small Turtle dataset and a SELECT query to a fresh temporary directory.</summary>
    /// <returns>The data file path and the query file path.</returns>
    private static (string DataPath, string QueryPath) WriteFixture()
    {
        string directory = Path.Combine(Path.GetTempPath(), "veritas-cli-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        string dataPath = Path.Combine(directory, "data.ttl");
        File.WriteAllText(dataPath, "@prefix : <http://example.org/> .\n:alice :knows :bob .\n:alice :knows :carol .\n");

        string queryPath = Path.Combine(directory, "query.rq");
        File.WriteAllText(queryPath, "PREFIX : <http://example.org/>\nSELECT ?who WHERE { :alice :knows ?who } ORDER BY ?who\n");

        return (dataPath, queryPath);
    }

    /// <summary>Writes a Turtle dataset whose one predicate carries a typed literal object, so a completion request over the served data can resolve that object variable's datatype from the data itself.</summary>
    /// <returns>The data file path.</returns>
    private static string WriteTypedLiteralFixture()
    {
        string directory = Path.Combine(Path.GetTempPath(), "veritas-cli-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        string dataPath = Path.Combine(directory, "data.ttl");
        File.WriteAllText(dataPath, "@prefix : <http://example.org/> .\n:alice :age \"42\"^^<http://www.w3.org/2001/XMLSchema#integer> .\n");

        return dataPath;
    }

    /// <summary>Writes a small N-Quads dataset — a default-graph triple and a named-graph quad — and a SELECT query unioning both graphs to a fresh temporary directory.</summary>
    /// <returns>The data file path and the query file path.</returns>
    private static (string DataPath, string QueryPath) WriteNQuadsFixture()
    {
        string directory = Path.Combine(Path.GetTempPath(), "veritas-cli-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        string dataPath = Path.Combine(directory, "data.nq");
        File.WriteAllText(
            dataPath,
            "<http://example.org/alice> <http://example.org/knows> <http://example.org/bob> .\n"
            + "<http://example.org/carol> <http://example.org/knows> <http://example.org/dana> <http://example.org/friends> .\n");

        string queryPath = Path.Combine(directory, "query.rq");
        File.WriteAllText(
            queryPath,
            "PREFIX : <http://example.org/>\nSELECT ?who WHERE { { :alice :knows ?who } UNION { GRAPH :friends { :carol :knows ?who } } } ORDER BY ?who\n");

        return (dataPath, queryPath);
    }

    /// <summary>Writes a small RDF/XML (<c>.rdf</c>) dataset and a SELECT query to a fresh temporary directory.</summary>
    /// <returns>The data file path and the query file path.</returns>
    private static (string DataPath, string QueryPath) WriteRdfXmlFixture()
    {
        string directory = Path.Combine(Path.GetTempPath(), "veritas-cli-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        string dataPath = Path.Combine(directory, "data.rdf");
        File.WriteAllText(
            dataPath,
            "<?xml version=\"1.0\"?>\n"
            + "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:ex=\"http://example.org/\">\n"
            + "  <rdf:Description rdf:about=\"http://example.org/alice\">\n"
            + "    <ex:knows rdf:resource=\"http://example.org/bob\"/>\n"
            + "    <ex:knows rdf:resource=\"http://example.org/carol\"/>\n"
            + "  </rdf:Description>\n"
            + "</rdf:RDF>\n");

        string queryPath = Path.Combine(directory, "query.rq");
        File.WriteAllText(queryPath, "PREFIX : <http://example.org/>\nSELECT ?who WHERE { :alice :knows ?who } ORDER BY ?who\n");

        return (dataPath, queryPath);
    }

    /// <summary>Writes a small OWL/XML (<c>.owl</c>) ontology and a SELECT query for its subclass axiom to a fresh temporary directory.</summary>
    /// <returns>The data file path and the query file path.</returns>
    private static (string DataPath, string QueryPath) WriteOwlXmlFixture()
    {
        string directory = Path.Combine(Path.GetTempPath(), "veritas-cli-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        string dataPath = Path.Combine(directory, "data.owl");
        File.WriteAllText(
            dataPath,
            "<?xml version=\"1.0\"?>\n"
            + "<Ontology xmlns=\"http://www.w3.org/2002/07/owl#\" ontologyIRI=\"http://example.org/o\">\n"
            + "  <Declaration><Class IRI=\"http://example.org/Dog\"/></Declaration>\n"
            + "  <Declaration><Class IRI=\"http://example.org/Animal\"/></Declaration>\n"
            + "  <SubClassOf><Class IRI=\"http://example.org/Dog\"/><Class IRI=\"http://example.org/Animal\"/></SubClassOf>\n"
            + "</Ontology>\n");

        string queryPath = Path.Combine(directory, "query.rq");
        File.WriteAllText(queryPath, "PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>\nSELECT ?super WHERE { <http://example.org/Dog> rdfs:subClassOf ?super }\n");

        return (dataPath, queryPath);
    }

    /// <summary>Writes a small GeoSPARQL dataset — three features whose default geometries carry WKT polygons (a square, a square strictly inside it, and a disjoint far square; no topological relation is asserted) — and a SELECT query filtering by the <c>geof:sfContains</c> extension function over the geometry nodes, to a fresh temporary directory.</summary>
    /// <returns>The data file path and the query file path.</returns>
    private static (string DataPath, string QueryPath) WriteGeoFixture()
    {
        string directory = Path.Combine(Path.GetTempPath(), "veritas-cli-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        string dataPath = Path.Combine(directory, "data.ttl");
        File.WriteAllText(
            dataPath,
            "@prefix : <http://example.org/> .\n"
            + "@prefix geo: <http://www.opengis.net/ont/geosparql#> .\n"
            + ":fSquare geo:hasDefaultGeometry :gSquare .\n"
            + ":gSquare geo:asWKT \"POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))\"^^geo:wktLiteral .\n"
            + ":fInner geo:hasDefaultGeometry :gInner .\n"
            + ":gInner geo:asWKT \"POLYGON ((2 2, 3 2, 3 3, 2 3, 2 2))\"^^geo:wktLiteral .\n"
            + ":fFar geo:hasDefaultGeometry :gFar .\n"
            + ":gFar geo:asWKT \"POLYGON ((10 10, 12 10, 12 12, 10 12, 10 10))\"^^geo:wktLiteral .\n");

        string queryPath = Path.Combine(directory, "query.rq");
        File.WriteAllText(
            queryPath,
            "PREFIX : <http://example.org/>\n"
            + "PREFIX geo: <http://www.opengis.net/ont/geosparql#>\n"
            + "PREFIX geof: <http://www.opengis.net/def/function/geosparql/>\n"
            + "SELECT ?other WHERE { :gSquare geo:asWKT ?square . ?other geo:asWKT ?wkt . FILTER(?other != :gSquare && geof:sfContains(?square, ?wkt)) } ORDER BY ?other\n");

        return (dataPath, queryPath);
    }

    /// <summary>Locates the built CLI executable under <c>src/Lumoin.Veritas.Cli/bin/&lt;config&gt;/&lt;tfm&gt;/</c>, or <see langword="null"/> when it has not been built.</summary>
    /// <returns>The executable path, or <see langword="null"/>.</returns>
    private static string? GetCliExecutablePath()
    {
        string basePath = AppContext.BaseDirectory;
        string repoRoot = Path.GetFullPath(Path.Combine(basePath, "../../../../.."));
        string targetFramework = Path.GetFileName(basePath.TrimEnd(Path.DirectorySeparatorChar));
        if(string.IsNullOrEmpty(targetFramework))
        {
            targetFramework = "net10.0";
        }

        string extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        string[] configurations = ["Debug", "Release"];

        foreach(string configuration in configurations)
        {
            string candidate = Path.Combine(
                repoRoot,
                "src", "Lumoin.Veritas.Cli", "bin", configuration, targetFramework,
                $"Lumoin.Veritas.Cli{extension}");
            if(File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
