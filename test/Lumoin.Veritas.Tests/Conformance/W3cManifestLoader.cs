using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle;

namespace Lumoin.Veritas.Tests.Conformance;

/// <summary>
/// Parses a W3C conformance manifest (written in Turtle) into a
/// <see cref="W3cManifest"/>.
/// </summary>
/// <remarks>
/// <para>
/// W3C manifests use the test-manifest vocabulary
/// (<c>mf:</c>) plus <c>rdft:</c> for test-type IRIs. Each
/// manifest declares its tests with <c>mf:entries</c> as an
/// RDF list; the loader walks <c>rdf:first</c>/<c>rdf:rest</c>
/// links iteratively. Top-level manifests aggregate suite
/// manifests via <c>mf:include</c> (also an RDF list); the
/// loader follows those references transitively. An include
/// that does not exist on disk is recorded on
/// <see cref="W3cManifest.UnresolvedIncludes"/> rather than
/// failing the load — vendored snapshots commonly point at
/// out-of-scope corpora (the RDF 1.1 cross-reference, for
/// instance).
/// </para>
/// <para>
/// The loader synthesises absolute file paths for
/// <c>mf:action</c> and <c>mf:result</c> by resolving the
/// declared IRI against the absolute file URL of the
/// containing manifest. Manifests that use
/// <c>mf:assumedTestBase</c> for HTTP-style IRIs are not
/// short-circuited; the loader still resolves on-disk file
/// names from the manifest's location.
/// </para>
/// </remarks>
internal static class W3cManifestLoader
{
    private const string MfNamespace = "http://www.w3.org/2001/sw/DataAccess/tests/test-manifest#";
    private const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string RdfsNamespace = "http://www.w3.org/2000/01/rdf-schema#";

    /// <summary>
    /// Loads a manifest synchronously, suitable for use from
    /// <see cref="MSTest"/> dynamic-data sources that block.
    /// </summary>
    /// <param name="manifestPath">Absolute path to the manifest file.</param>
    /// <returns>The parsed manifest including all transitively-included tests.</returns>
    public static W3cManifest Load(string manifestPath)
    {
        return LoadAsync(manifestPath, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Loads a manifest asynchronously.
    /// </summary>
    /// <param name="manifestPath">Absolute path to the manifest file.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The parsed manifest including all transitively-included tests.</returns>
    public static async Task<W3cManifest> LoadAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifestPath);

        Uri rootUri = new(Path.GetFullPath(manifestPath));
        ImmutableArray<W3cTestCase>.Builder tests = ImmutableArray.CreateBuilder<W3cTestCase>();
        ImmutableArray<string>.Builder unresolved = ImmutableArray.CreateBuilder<string>();

        //Iterative breadth-first traversal of mf:include references; the visited set guards against cycles.
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        Queue<string> queue = new();
        queue.Enqueue(rootUri.LocalPath);

        while(queue.Count > 0)
        {
            string current = queue.Dequeue();
            if(!visited.Add(current))
            {
                continue;
            }

            if(!File.Exists(current))
            {
                unresolved.Add(current);
                continue;
            }

            ManifestParse parsed = await ParseSingleAsync(current, cancellationToken).ConfigureAwait(false);
            foreach(W3cTestCase test in parsed.Tests)
            {
                tests.Add(test);
            }

            foreach(string include in parsed.Includes)
            {
                queue.Enqueue(include);
            }
        }

        return new W3cManifest(rootUri, tests.ToImmutable(), unresolved.ToImmutable());
    }

    private static async Task<ManifestParse> ParseSingleAsync(string path, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        Uri manifestUri = new(Path.GetFullPath(path));

        //The manifest's own file URL is its retrieval base: its relative IRIs (#manifest, sibling
        //action/result files, ../-style includes) resolve to absolute file URIs, which the path
        //helpers below map straight back to disk. The base is harness bookkeeping and never reaches
        //test output, so a file:// base is the right choice here.
        List<Quad> quads = [];
        await foreach(Quad q in TurtleReader.ReadAsync(
            bytes,
            TurtleSyntax.Turtle,
            pool: null,
            baseIri: manifestUri.AbsoluteUri,
            cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            quads.Add(q);
        }

        Dictionary<string, List<Quad>> bySubject = IndexBySubject(quads);

        //Locate the manifest subject — the term that has rdf:type mf:Manifest.
        string? manifestSubject = FindManifestSubject(bySubject);
        if(manifestSubject is null)
        {
            return new ManifestParse([], []);
        }

        ImmutableArray<W3cTestCase> tests = ImmutableArray<W3cTestCase>.Empty;
        ImmutableArray<string> includes = ImmutableArray<string>.Empty;

        if(bySubject.TryGetValue(manifestSubject, out List<Quad>? manifestProperties))
        {
            string? entriesHead = FindObject(manifestProperties, MfNamespace + "entries");
            if(entriesHead is not null)
            {
                List<string> entrySubjects = WalkRdfList(entriesHead, bySubject);
                tests = BuildTests(entrySubjects, bySubject, manifestUri);
            }

            string? includeHead = FindObject(manifestProperties, MfNamespace + "include");
            if(includeHead is not null)
            {
                List<string> rawIncludes = WalkRdfList(includeHead, bySubject);
                includes = ResolveIncludesToPaths(rawIncludes, manifestUri);
            }
        }

        return new ManifestParse(tests, includes);
    }

    private static Dictionary<string, List<Quad>> IndexBySubject(List<Quad> quads)
    {
        Dictionary<string, List<Quad>> result = new(StringComparer.Ordinal);
        for(int i = 0; i < quads.Count; i++)
        {
            string key = TermKey(quads[i].Subject);
            if(!result.TryGetValue(key, out List<Quad>? bucket))
            {
                bucket = [];
                result[key] = bucket;
            }

            bucket.Add(quads[i]);
        }

        return result;
    }

    private static string? FindManifestSubject(Dictionary<string, List<Quad>> bySubject)
    {
        foreach(KeyValuePair<string, List<Quad>> entry in bySubject)
        {
            for(int i = 0; i < entry.Value.Count; i++)
            {
                Quad q = entry.Value[i];
                if(q.Predicate.Iri.ToString() == RdfNamespace + "type"
                    && q.Object is NamedNode obj
                    && obj.Iri.ToString() == MfNamespace + "Manifest")
                {
                    return entry.Key;
                }
            }
        }

        return null;
    }

    private static string? FindObject(List<Quad> properties, string predicateIri)
    {
        for(int i = 0; i < properties.Count; i++)
        {
            if(properties[i].Predicate.Iri.ToString() == predicateIri)
            {
                return TermKey(properties[i].Object);
            }
        }

        return null;
    }

    private static List<string> FindObjects(List<Quad> properties, string predicateIri)
    {
        List<string> result = [];
        for(int i = 0; i < properties.Count; i++)
        {
            if(properties[i].Predicate.Iri.ToString() == predicateIri)
            {
                result.Add(TermKey(properties[i].Object));
            }
        }

        return result;
    }

    private static List<string> WalkRdfList(string head, Dictionary<string, List<Quad>> bySubject)
    {
        //RDF lists chain rdf:first / rdf:rest until rdf:nil. Iterative traversal guards against cycles.
        List<string> result = [];
        HashSet<string> visited = new(StringComparer.Ordinal);
        string current = head;
        string nil = "i:" + RdfNamespace + "nil";
        while(current != nil)
        {
            if(!visited.Add(current))
            {
                break;
            }

            if(!bySubject.TryGetValue(current, out List<Quad>? nodeProperties))
            {
                break;
            }

            string? first = FindObject(nodeProperties, RdfNamespace + "first");
            string? rest = FindObject(nodeProperties, RdfNamespace + "rest");
            if(first is null || rest is null)
            {
                break;
            }

            result.Add(first);
            current = rest;
        }

        return result;
    }

    private static ImmutableArray<W3cTestCase> BuildTests(
        List<string> entrySubjects,
        Dictionary<string, List<Quad>> bySubject,
        Uri manifestUri)
    {
        ImmutableArray<W3cTestCase>.Builder builder = ImmutableArray.CreateBuilder<W3cTestCase>(entrySubjects.Count);

        for(int i = 0; i < entrySubjects.Count; i++)
        {
            string entry = entrySubjects[i];
            if(!bySubject.TryGetValue(entry, out List<Quad>? properties))
            {
                continue;
            }

            string typeIri = FindObjects(properties, RdfNamespace + "type") switch
            {
                { Count: > 0 } list => StripIriPrefix(list[0]),
                _ => string.Empty
            };

            string name = ResolveStringLiteral(properties, MfNamespace + "name") ?? string.Empty;
            string comment = ResolveStringLiteral(properties, RdfsNamespace + "comment") ?? string.Empty;
            string? actionTerm = FindObject(properties, MfNamespace + "action");
            string? resultTerm = FindObject(properties, MfNamespace + "result");

            if(actionTerm is null)
            {
                continue;
            }

            string? inputPath = ResolveSiblingPath(actionTerm, manifestUri);
            if(inputPath is null)
            {
                continue;
            }

            string? expectedPath = resultTerm is null ? null : ResolveSiblingPath(resultTerm, manifestUri);

            Uri testUri = TryBuildAbsoluteUri(entry) ?? new Uri(manifestUri, "#" + entry);
            W3cTestType type = ClassifyTestType(typeIri);
            W3cTestCase testCase = new(testUri, type, typeIri, name, comment, inputPath, expectedPath);
            builder.Add(testCase);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<string> ResolveIncludesToPaths(List<string> rawIncludes, Uri manifestUri)
    {
        ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>(rawIncludes.Count);
        for(int i = 0; i < rawIncludes.Count; i++)
        {
            string? resolved = ResolveSiblingPath(rawIncludes[i], manifestUri);
            if(resolved is not null)
            {
                builder.Add(resolved);
            }
        }

        return builder.ToImmutable();
    }

    private static string? ResolveSiblingPath(string termKey, Uri manifestUri)
    {
        string iri = StripIriPrefix(termKey);
        if(iri.Length == 0)
        {
            return null;
        }

        if(Uri.TryCreate(iri, UriKind.Absolute, out Uri? absolute) && absolute.IsFile)
        {
            return absolute.LocalPath;
        }

        if(Uri.TryCreate(manifestUri, iri, out Uri? combined) && combined.IsFile)
        {
            return combined.LocalPath;
        }

        //IRI is a real HTTP IRI; fall back to "same directory as manifest, take last segment as file name."
        string fileName = LastPathSegment(iri);
        string? directory = Path.GetDirectoryName(manifestUri.LocalPath);
        if(directory is null || fileName.Length == 0)
        {
            return null;
        }

        return Path.Combine(directory, fileName);
    }

    private static string LastPathSegment(string iri)
    {
        int hash = iri.IndexOf('#', StringComparison.Ordinal);
        string withoutFragment = hash >= 0 ? iri[..hash] : iri;
        int slash = withoutFragment.LastIndexOf('/');
        return slash >= 0 ? withoutFragment[(slash + 1)..] : withoutFragment;
    }

    private static Uri? TryBuildAbsoluteUri(string termKey)
    {
        string iri = StripIriPrefix(termKey);
        return Uri.TryCreate(iri, UriKind.Absolute, out Uri? absolute) ? absolute : null;
    }

    private static string? ResolveStringLiteral(List<Quad> properties, string predicateIri)
    {
        for(int i = 0; i < properties.Count; i++)
        {
            if(properties[i].Predicate.Iri.ToString() == predicateIri && properties[i].Object is Literal literal)
            {
                return literal.Value.ToString();
            }
        }

        return null;
    }

    private static W3cTestType ClassifyTestType(string typeIri)
    {
        //All evaluation variants — TestTurtleEval, TestTrigEval, etc. — share an "Eval" suffix and produce an expected file.
        //Negative evaluations share a "NegativeEval" suffix. Syntax tests share PositiveSyntax / NegativeSyntax.
        //Canonicalisation tests share a PositiveC14N suffix (TestNTriplesPositiveC14N, TestNQuadsPositiveC14N).
        if(typeIri.EndsWith("PositiveSyntax", StringComparison.Ordinal))
        {
            return W3cTestType.PositiveSyntax;
        }

        if(typeIri.EndsWith("NegativeSyntax", StringComparison.Ordinal))
        {
            return W3cTestType.NegativeSyntax;
        }

        if(typeIri.EndsWith("PositiveC14N", StringComparison.Ordinal))
        {
            return W3cTestType.PositiveC14N;
        }

        if(typeIri.EndsWith("NegativeEval", StringComparison.Ordinal))
        {
            return W3cTestType.NegativeEvaluation;
        }

        if(typeIri.EndsWith("Eval", StringComparison.Ordinal))
        {
            return W3cTestType.Evaluation;
        }

        return W3cTestType.Unknown;
    }

    private static string StripIriPrefix(string termKey)
    {
        //TermKey wraps IRIs as "i:<iri>" and blank nodes as "b:<label>". Manifests use IRIs almost everywhere.
        if(termKey.StartsWith("i:", StringComparison.Ordinal))
        {
            return termKey[2..];
        }

        return termKey;
    }

    private static string TermKey(RdfTerm term)
    {
        return term switch
        {
            NamedNode named => "i:" + named.Iri.ToString(),
            BlankNode blank => "b:" + blank.Label.ToString(),
            Literal literal => "l:" + literal.Value.ToString() + ":" + (literal.Language?.ToString() ?? string.Empty) + ":" + literal.Datatype.Iri.ToString(),
            TripleTerm tt => "t:" + TermKey(tt.Subject) + "|" + tt.Predicate.Iri.ToString() + "|" + TermKey(tt.Object),
            _ => "x:" + term.ToString()
        };
    }

    private readonly record struct ManifestParse(
        ImmutableArray<W3cTestCase> Tests,
        ImmutableArray<string> Includes);
}
