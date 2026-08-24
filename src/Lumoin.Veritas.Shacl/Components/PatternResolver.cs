using System.Text.RegularExpressions;

namespace Lumoin.Veritas.Shacl.Components;

/// <summary>
/// Optional resolver that maps a SHACL <c>sh:pattern</c> source
/// (pattern text, flags, and single-line flag) to a caller-supplied
/// <see cref="Regex"/> instance — typically a
/// <see cref="GeneratedRegexAttribute"/>-backed subclass for zero
/// startup cost on known patterns.
/// </summary>
/// <remarks>
/// <para>
/// When a library user has prior knowledge of the regexes their shape
/// graphs will contain — for example, a validator with a fixed set
/// of identifier patterns — they can register a resolver that returns
/// source-generator-compiled matchers for known inputs and <c>null</c>
/// for unknown ones. <see cref="ParameterBag.CompilePattern"/> consults
/// the resolver first; if it returns <c>null</c>, a per-session memo is
/// checked next, and only on a miss there is a fresh
/// <see cref="Regex"/> compiled with
/// <see cref="RegexOptions.NonBacktracking"/> for ReDoS safety on
/// untrusted input.
/// </para>
/// <para>
/// <b>Thread safety and reuse.</b> The returned <see cref="Regex"/> is
/// retained by the <see cref="Constraints.PatternConstraint"/> record
/// and accessed concurrently during validation;
/// <see cref="Regex"/> instances are thread-safe per the .NET runtime
/// documentation, so caching and sharing a single instance across many
/// validations is correct and efficient. Source-generator-backed
/// matchers are likewise safe to reuse.
/// </para>
/// <para>
/// <b>Semantic constraint.</b> The returned matcher must implement the
/// SHACL-spec regex semantics for the supplied <paramref name="pattern"/>,
/// <paramref name="flags"/>, and <paramref name="singleLine"/>. The loader
/// does not verify that the resolver's matcher is consistent with the
/// source strings; supplying a mismatched matcher is a caller bug that
/// leads to silent validation divergence. Resolvers that don't recognise
/// a given input should return <c>null</c> to trigger the default
/// compilation fallback.
/// </para>
/// </remarks>
/// <param name="pattern">The regex source text from <c>sh:pattern</c>.</param>
/// <param name="flags">
/// The optional flag string from <c>sh:flags</c>, or <c>null</c> when
/// absent.
/// </param>
/// <param name="singleLine">
/// <c>true</c> when <c>sh:singleLine true</c> is asserted on the shape,
/// or the <c>s</c> flag is present in <paramref name="flags"/>. Callers
/// may use this to distinguish source variants that produce equivalent
/// matchers.
/// </param>
/// <returns>
/// A compiled <see cref="Regex"/> for the supplied source, or
/// <c>null</c> if the resolver does not recognise this input and the
/// loader should fall back to default compilation.
/// </returns>
public delegate Regex? PatternResolver(string pattern, string? flags, bool singleLine);
