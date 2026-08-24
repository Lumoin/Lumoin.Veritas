using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The named cryptographic digest algorithms SPARQL's hash functions (§17.4) expose.
/// </summary>
public enum SparqlHashAlgorithm
{
    /// <summary>MD5 (<c>MD5</c>).</summary>
    Md5,

    /// <summary>SHA-1 (<c>SHA1</c>).</summary>
    Sha1,

    /// <summary>SHA-256 (<c>SHA256</c>).</summary>
    Sha256,

    /// <summary>SHA-384 (<c>SHA384</c>).</summary>
    Sha384,

    /// <summary>SHA-512 (<c>SHA512</c>).</summary>
    Sha512
}

/// <summary>
/// Computes a cryptographic digest of <paramref name="data"/> under <paramref name="algorithm"/>. The seam
/// through which SPARQL's hash functions obtain digests, so the crypto provider is swappable and inventoried for
/// the CBOM: the default is in-process, but a deployment may route to a TPM / HSM / remote provider.
/// </summary>
/// <param name="algorithm">The digest algorithm.</param>
/// <param name="data">The bytes to digest.</param>
/// <returns>The raw digest bytes.</returns>
public delegate byte[] SparqlHashDelegate(SparqlHashAlgorithm algorithm, ReadOnlySpan<byte> data);

/// <summary>
/// Compiles a regular expression for SPARQL's <c>REGEX</c> and <c>REPLACE</c> (§17.4.3.14–15), or returns
/// <see langword="null"/> when the pattern or flags cannot be honoured (which makes the function err). This is the
/// seam through which an application supplies its own regular-expression engine.
/// </summary>
/// <remarks>
/// The returned <see cref="Regex"/> serves both functions — <c>REGEX</c> calls <see cref="Regex.IsMatch(string)"/>
/// and <c>REPLACE</c> calls <see cref="Regex.Replace(string, string)"/> — so it must be a compiled matcher, not a
/// match result. The default compiles with <see cref="RegexOptions.NonBacktracking"/> (ReDoS-safe), mapping the
/// XPath <c>i</c>/<c>s</c>/<c>m</c>/<c>x</c> flags; an application may instead route to a source-generated
/// (<c>[GeneratedRegex]</c>) or otherwise specialised engine. This mirrors the SHACL pattern-resolver seam.
/// </remarks>
/// <param name="pattern">The regular-expression pattern.</param>
/// <param name="flags">The XPath flag string (<c>i</c>/<c>s</c>/<c>m</c>/<c>x</c>), or <see langword="null"/>.</param>
/// <returns>A compiled regular expression, or <see langword="null"/> when it cannot be built.</returns>
public delegate Regex? SparqlRegexResolver(string pattern, string? flags);

/// <summary>
/// The injected, swappable seams the expression evaluator's non-pure functions consume: randomness
/// (<c>RAND</c>, <c>UUID</c>, <c>STRUUID</c>), cryptographic digests (<c>MD5</c>/<c>SHA*</c>), and the
/// query-execution timestamp (<c>NOW</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Synchronous by design.</b> These run on the per-solution expression hot path (a <c>FILTER</c> over every
/// solution, an <c>ORDER BY</c> comparator, an aggregate over every group member), so the seams are synchronous
/// delegates — swappable at ~zero cost, without pushing <see langword="async"/> through the comparator-driven
/// evaluation path. A provider that genuinely needs async I/O (a remote/hardware crypto service) belongs at the
/// signing/provenance boundary, not on the value-expression hot path.
/// </para>
/// <para>
/// <b>NOW is captured once.</b> <see cref="Now"/> is the single query-execution instant SPARQL <c>NOW()</c>
/// returns; it is fixed for the lifetime of this context so every <c>NOW()</c> in a query agrees.
/// </para>
/// <para>
/// <b>The implicit timezone is captured once.</b> <see cref="ImplicitTimezone"/> is the SPARQL §17.3 /
/// XPath&#160;F&amp;O dynamic-context implicit timezone that totalizes comparisons of timezone-naive temporal
/// values; it is fixed for the lifetime of this context so every comparison in a query normalizes the same way.
/// </para>
/// </remarks>
public sealed class SparqlExpressionContext
{
    /// <summary>Constructs a context over explicit seams.</summary>
    /// <param name="randomness">The randomness seam for <c>RAND</c>/<c>UUID</c>/<c>STRUUID</c>.</param>
    /// <param name="hash">The cryptographic-digest seam for <c>MD5</c>/<c>SHA*</c>.</param>
    /// <param name="now">The query-execution instant <c>NOW()</c> returns.</param>
    /// <param name="baseIri">The query's effective base IRI that <c>IRI</c>/<c>URI</c> resolve a relative argument against, or <see langword="null"/> when the query has no base (a relative argument then yields an error).</param>
    /// <param name="blankNodes">The seam that mints <c>BNODE</c>'s blank-node labels; <see langword="null"/> uses <see cref="VeritasBlankNodes.System"/>.</param>
    /// <param name="regexResolver">The regular-expression seam for <c>REGEX</c>/<c>REPLACE</c>; <see langword="null"/> uses the default non-backtracking engine.</param>
    /// <param name="implicitTimezone">The implicit timezone temporal comparisons normalize timezone-naive operands with (§17.3); <see langword="null"/> uses UTC. The magnitude may not exceed the XSD 14-hour bound.</param>
    /// <param name="valueDatatypes">The value-layer datatype registry <c>=</c>/<c>!=</c> consult when both operands are literals of one registered datatype IRI; <see langword="null"/> uses <see cref="ValueDatatypeRegistry.Empty"/>, which preserves the built-in term-identity semantics exactly.</param>
    /// <param name="extensionFunctions">The extension-function registry IRI-named function calls (§17.6) consult; <see langword="null"/> uses <see cref="SparqlFunctionRegistry.Empty"/>, under which every extension-function IRI evaluates to the expression error value.</param>
    /// <exception cref="ArgumentNullException">A required seam is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="implicitTimezone"/> exceeds the XSD ±14:00 offset bound.</exception>
    public SparqlExpressionContext(RandomnessDelegate randomness, SparqlHashDelegate hash, DateTimeOffset now, Utf8String? baseIri = null, BlankNodeDelegate? blankNodes = null, SparqlRegexResolver? regexResolver = null, TimeSpan? implicitTimezone = null, ValueDatatypeRegistry? valueDatatypes = null, SparqlFunctionRegistry? extensionFunctions = null)
        : this(randomness, hash, now, baseIri, new SolutionBlankNodeScope(blankNodes ?? VeritasBlankNodes.System, new Utf8StringPool()), regexResolver, implicitTimezone, valueDatatypes, extensionFunctions)
    {
    }

    /// <summary>Constructs a context over explicit seams and an existing blank-node scope (so a per-query derivation keeps one correlation scope).</summary>
    /// <param name="randomness">The randomness seam.</param>
    /// <param name="hash">The cryptographic-digest seam.</param>
    /// <param name="now">The query-execution instant.</param>
    /// <param name="baseIri">The query's effective base IRI, or <see langword="null"/>.</param>
    /// <param name="blankNodeScope">The blank-node correlation scope to carry.</param>
    /// <param name="regexResolver">The regular-expression seam, or <see langword="null"/> for the default.</param>
    /// <param name="implicitTimezone">The implicit timezone for temporal comparisons, or <see langword="null"/> for UTC.</param>
    /// <param name="valueDatatypes">The value-layer datatype registry, or <see langword="null"/> for <see cref="ValueDatatypeRegistry.Empty"/>.</param>
    /// <param name="extensionFunctions">The extension-function registry, or <see langword="null"/> for <see cref="SparqlFunctionRegistry.Empty"/>.</param>
    /// <exception cref="ArgumentNullException">A required seam or the scope is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="implicitTimezone"/> exceeds the XSD ±14:00 offset bound.</exception>
    private SparqlExpressionContext(RandomnessDelegate randomness, SparqlHashDelegate hash, DateTimeOffset now, Utf8String? baseIri, SolutionBlankNodeScope blankNodeScope, SparqlRegexResolver? regexResolver, TimeSpan? implicitTimezone, ValueDatatypeRegistry? valueDatatypes, SparqlFunctionRegistry? extensionFunctions)
    {
        ArgumentNullException.ThrowIfNull(randomness);
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(blankNodeScope);

        TimeSpan timezone = implicitTimezone ?? TimeSpan.Zero;
        if(timezone < TimeSpan.FromHours(-14) || timezone > TimeSpan.FromHours(14))
        {
            throw new ArgumentOutOfRangeException(nameof(implicitTimezone), timezone, "The implicit timezone must lie within the XSD +/-14:00 offset bound.");
        }

        Randomness = randomness;
        Hash = hash;
        Now = now;
        BaseIri = baseIri;
        BlankNodeScope = blankNodeScope;
        RegexResolver = regexResolver ?? DefaultRegex;
        ImplicitTimezone = timezone;
        ValueDatatypes = valueDatatypes ?? ValueDatatypeRegistry.Empty;
        ExtensionFunctions = extensionFunctions ?? SparqlFunctionRegistry.Empty;
    }

    /// <summary>The randomness seam backing <c>RAND</c> (uniform double) and <c>UUID</c>/<c>STRUUID</c> (a fresh UUID).</summary>
    public RandomnessDelegate Randomness { get; }

    /// <summary>The cryptographic-digest seam backing <c>MD5</c>/<c>SHA1</c>/<c>SHA256</c>/<c>SHA384</c>/<c>SHA512</c>.</summary>
    public SparqlHashDelegate Hash { get; }

    /// <summary>The query-execution instant <c>NOW()</c> returns, fixed for this context.</summary>
    public DateTimeOffset Now { get; }

    /// <summary>
    /// The query's effective base IRI — the value <c>IRI(rel)</c>/<c>URI(rel)</c> (§17.4.2.8) resolve a relative
    /// argument against — or <see langword="null"/> when the query declares no base. A relative argument with no
    /// base in scope is an error (an unbound result), per the function definition.
    /// </summary>
    public Utf8String? BaseIri { get; }

    /// <summary>The per-solution blank-node identity scope <c>BNODE</c> (§17.4.2.3) builds against — and the substrate SPARQL Update's <c>INSERT … WHERE</c> reuses for per-solution template blank nodes.</summary>
    public SolutionBlankNodeScope BlankNodeScope { get; }

    /// <summary>The regular-expression seam backing <c>REGEX</c> and <c>REPLACE</c> (§17.4.3.14–15).</summary>
    public SparqlRegexResolver RegexResolver { get; }

    /// <summary>
    /// The value-layer datatype registry <c>=</c>/<c>!=</c> comparisons consult when both operands are
    /// literals of one registered datatype IRI whose definition declares the value-equality facet.
    /// <see cref="ValueDatatypeRegistry.Empty"/> — the default — preserves the built-in term-identity
    /// semantics exactly; frozen and fixed for the lifetime of this context.
    /// </summary>
    public ValueDatatypeRegistry ValueDatatypes { get; }

    /// <summary>
    /// The extension-function registry IRI-named function calls (§17.6) consult after the built-in XSD
    /// constructor casts. <see cref="SparqlFunctionRegistry.Empty"/> — the default — makes every
    /// extension-function IRI evaluate to the expression error value; frozen and fixed for the lifetime of
    /// this context.
    /// </summary>
    public SparqlFunctionRegistry ExtensionFunctions { get; }

    /// <summary>
    /// The implicit timezone (SPARQL §17.3 via the XPath F&amp;O dynamic context) that temporal value comparisons
    /// normalize timezone-naive operands with — the totalization that makes <c>xsd:dateTime</c>/<c>xsd:date</c>/
    /// <c>xsd:time</c> ordering a total order. UTC unless the host configures otherwise; fixed for this context.
    /// </summary>
    public TimeSpan ImplicitTimezone { get; }

    /// <summary>Returns a copy of this context with the base IRI replaced — the per-query derivation, since a query carries its own <c>BASE</c> while the seams (randomness, hash, clock, implicit timezone) and the blank-node scope are shared.</summary>
    /// <param name="baseIri">The query's effective base IRI, or <see langword="null"/> for none.</param>
    /// <returns>A context identical to this one but for <see cref="BaseIri"/>.</returns>
    public SparqlExpressionContext WithBaseIri(Utf8String? baseIri)
    {
        return new SparqlExpressionContext(Randomness, Hash, Now, baseIri, BlankNodeScope, RegexResolver, ImplicitTimezone, ValueDatatypes, ExtensionFunctions);
    }

    /// <summary>
    /// Creates a context over the default seams: <see cref="VeritasRandomness.System"/>, the in-process digest
    /// implementation, the current instant from <paramref name="timeProvider"/> (or
    /// <see cref="TimeProvider.System"/>), and the UTC implicit timezone unless
    /// <paramref name="implicitTimezone"/> overrides it.
    /// </summary>
    /// <param name="timeProvider">The clock <c>NOW()</c> is captured from; <see langword="null"/> uses <see cref="TimeProvider.System"/>.</param>
    /// <param name="implicitTimezone">The implicit timezone for temporal comparisons; <see langword="null"/> uses UTC.</param>
    /// <param name="valueDatatypes">The value-layer datatype registry; <see langword="null"/> uses <see cref="ValueDatatypeRegistry.Empty"/>.</param>
    /// <param name="extensionFunctions">The extension-function registry; <see langword="null"/> uses <see cref="SparqlFunctionRegistry.Empty"/>.</param>
    /// <returns>The default context.</returns>
    public static SparqlExpressionContext CreateDefault(TimeProvider? timeProvider = null, TimeSpan? implicitTimezone = null, ValueDatatypeRegistry? valueDatatypes = null, SparqlFunctionRegistry? extensionFunctions = null)
    {
        return new SparqlExpressionContext(VeritasRandomness.System, DefaultHash, (timeProvider ?? TimeProvider.System).GetUtcNow(), implicitTimezone: implicitTimezone, valueDatatypes: valueDatatypes, extensionFunctions: extensionFunctions);
    }

    /// <summary>The default in-process digest implementation, dispatching to <see cref="System.Security.Cryptography"/>.</summary>
    /// <param name="algorithm">The digest algorithm.</param>
    /// <param name="data">The bytes to digest.</param>
    /// <returns>The raw digest bytes.</returns>
    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "MD5 is a SPARQL 1.2 built-in (§17.4) — a content digest function exposed to queries, not used to protect anything; the algorithm is dictated by the query, not chosen for security.")]
    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "SHA-1 is a SPARQL 1.2 built-in (§17.4) — a content digest function exposed to queries, not used to protect anything; the algorithm is dictated by the query, not chosen for security.")]
    [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "The library does not target the browser platform; the digest primitives are available on every targeted runtime.")]
    private static byte[] DefaultHash(SparqlHashAlgorithm algorithm, ReadOnlySpan<byte> data)
    {
        return algorithm switch
        {
            SparqlHashAlgorithm.Md5 => MD5.HashData(data),
            SparqlHashAlgorithm.Sha1 => SHA1.HashData(data),
            SparqlHashAlgorithm.Sha256 => SHA256.HashData(data),
            SparqlHashAlgorithm.Sha384 => SHA384.HashData(data),
            SparqlHashAlgorithm.Sha512 => SHA512.HashData(data),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown SPARQL hash algorithm.")
        };
    }

    /// <summary>The default regular-expression engine: a non-backtracking, culture-invariant <see cref="Regex"/> mapping the XPath <c>i</c>/<c>s</c>/<c>m</c>/<c>x</c> flags; a bad pattern, an unsupported construct, or an unknown flag yields <see langword="null"/>.</summary>
    /// <param name="pattern">The regular-expression pattern.</param>
    /// <param name="flags">The XPath flag string, or <see langword="null"/>.</param>
    /// <returns>The compiled regular expression, or <see langword="null"/>.</returns>
    private static Regex? DefaultRegex(string pattern, string? flags)
    {
        RegexOptions options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;
        if(flags is not null)
        {
            foreach(char flag in flags)
            {
                RegexOptions? mapped = flag switch
                {
                    'i' => RegexOptions.IgnoreCase,
                    's' => RegexOptions.Singleline,
                    'm' => RegexOptions.Multiline,
                    'x' => RegexOptions.IgnorePatternWhitespace,
                    _ => null
                };

                if(mapped is not RegexOptions option)
                {
                    return null;
                }

                options |= option;
            }
        }

        try
        {
            return new Regex(pattern, options);
        }
        catch(Exception exception) when(exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
