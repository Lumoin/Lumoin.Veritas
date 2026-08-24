using System;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Shacl.Components;

/// <summary>
/// Parsed parameter values for a single
/// <see cref="ConstraintComponentFactory"/> invocation. Exposes typed
/// accessors that convert raw <see cref="TermId"/> values into the
/// shapes factories consume: integers, booleans, IRIs, literal strings,
/// node kinds, RDF lists, and shape references.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per-invocation scope.</b> A bag corresponds to exactly one
/// occurrence of a primary-parameter triple on a shape. If a shape
/// declares <c>sh:property</c> three times, the loader constructs three
/// bags and invokes the property factory three times. The primary
/// accessor <see cref="RequirePrimaryTerm"/> returns the value
/// associated with <em>this</em> invocation; companion-parameter
/// accessors return values shared at shape scope.
/// </para>
/// <para>
/// <b>Pre-resolved lists; shape references as ids.</b> RDF lists
/// (<c>sh:in</c>, <c>sh:languageIn</c>, <c>sh:ignoredProperties</c>,
/// <c>sh:and</c>, <c>sh:or</c>, <c>sh:xone</c>) are walked by the
/// loader before factory invocation and their member term ids cached
/// here. Shape-referencing constraints capture the referenced shapes
/// as <see cref="TermId"/> values through the <c>…ShapeId</c>
/// accessors; resolution against the shape registry is deferred to
/// evaluation time. Factories never await and never need a resolver
/// delegate to complete a shape reference.
/// </para>
/// <para>
/// <b>Error model.</b> Missing required parameters throw
/// <see cref="InvalidOperationException"/>. Type mismatches — an
/// integer parameter whose value is not a plain-integer literal, an IRI
/// parameter whose value is a blank node — throw
/// <see cref="FormatException"/>. Error messages include the offending
/// parameter IRI for diagnostics.
/// </para>
/// </remarks>
[DebuggerDisplay("{Dictionary.Resolve(PrimaryParameter)} = {Dictionary.Resolve(PrimaryValue)} (+{Companions.Count} companions)")]
public readonly struct ParameterBag: IEquatable<ParameterBag>
{
    /// <summary>
    /// Initializes a new bag for a single factory invocation.
    /// </summary>
    /// <param name="primaryParameter">The component's primary parameter IRI id.</param>
    /// <param name="primaryValue">
    /// The term value associated with this invocation's primary-parameter
    /// triple.
    /// </param>
    /// <param name="companions">
    /// Values for every companion parameter declared on the owning shape,
    /// keyed by parameter IRI id. Must not contain
    /// <paramref name="primaryParameter"/>.
    /// </param>
    /// <param name="resolvedLists">
    /// Pre-walked RDF lists, keyed by list-head term id. Every RDF list
    /// referenced by this invocation's parameters must already be
    /// populated.
    /// </param>
    /// <param name="dictionary">Term dictionary for decoding term values.</param>
    /// <param name="rdfsVocabulary">
    /// Pre-resolved RDFS vocabulary ids, used by class-hierarchy
    /// constraint factories.
    /// </param>
    /// <param name="options">Per-load options carrying resolvers and other configuration.</param>
    /// <param name="patternMemo">
    /// Per-load memoization of compiled regexes. Shared across every bag
    /// in the same load so that a pattern that appears on multiple shapes
    /// is compiled once.
    /// </param>
    public ParameterBag(
        IriId primaryParameter,
        TermId primaryValue,
        IReadOnlyDictionary<IriId, IReadOnlyList<TermId>> companions,
        IReadOnlyDictionary<TermId, ImmutableArray<TermId>> resolvedLists,
        TermDictionary dictionary,
        RdfsVocabularyIds rdfsVocabulary,
        ShapeLoaderOptions options,
        ConcurrentDictionary<(string, string?, bool), Regex> patternMemo)
    {
        ArgumentNullException.ThrowIfNull(companions);
        ArgumentNullException.ThrowIfNull(resolvedLists);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(patternMemo);

        PrimaryParameter = primaryParameter;
        PrimaryValue = primaryValue;
        Companions = companions;
        ResolvedLists = resolvedLists;
        Dictionary = dictionary;
        RdfsVocabulary = rdfsVocabulary;
        Options = options;
        PatternMemo = patternMemo;
    }

    /// <summary>The primary parameter IRI id for this invocation.</summary>
    public IriId PrimaryParameter { get; }

    /// <summary>The primary parameter value for this invocation.</summary>
    public TermId PrimaryValue { get; }

    /// <summary>
    /// Values for every companion parameter declared on the owning shape,
    /// keyed by parameter IRI id. Does not include the primary parameter.
    /// </summary>
    private IReadOnlyDictionary<IriId, IReadOnlyList<TermId>> Companions { get; }

    /// <summary>
    /// Pre-walked RDF lists, keyed by list-head term id. Populated by
    /// the loader before factory invocation.
    /// </summary>
    private IReadOnlyDictionary<TermId, ImmutableArray<TermId>> ResolvedLists { get; }

    /// <summary>Term dictionary for decoding term ids into <c>RdfTerm</c> values.</summary>
    public TermDictionary Dictionary { get; }

    /// <summary>Pre-resolved RDFS vocabulary ids, for class-hierarchy constraints.</summary>
    public RdfsVocabularyIds RdfsVocabulary { get; }

    /// <summary>Per-load options carrying resolvers and other configuration.</summary>
    private ShapeLoaderOptions Options { get; }

    /// <summary>Per-load regex compilation memo shared across bags.</summary>
    private ConcurrentDictionary<(string, string?, bool), Regex> PatternMemo { get; }

    /// <summary>
    /// Structural equality by field identity. Reference-typed fields
    /// compare by reference (two bags sharing the same state are equal);
    /// the ID structs and <see cref="RdfsVocabulary"/> compare by value
    /// through their own equality.
    /// </summary>
    public bool Equals(ParameterBag other)
    {
        return PrimaryParameter.Equals(other.PrimaryParameter)
            && PrimaryValue.Equals(other.PrimaryValue)
            && ReferenceEquals(Companions, other.Companions)
            && ReferenceEquals(ResolvedLists, other.ResolvedLists)
            && ReferenceEquals(Dictionary, other.Dictionary)
            && RdfsVocabulary.Equals(other.RdfsVocabulary)
            && ReferenceEquals(Options, other.Options)
            && ReferenceEquals(PatternMemo, other.PatternMemo);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ParameterBag other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(PrimaryParameter);
        hash.Add(PrimaryValue);
        hash.Add(RuntimeHelpers.GetHashCode(Companions));
        hash.Add(RuntimeHelpers.GetHashCode(ResolvedLists));
        hash.Add(RuntimeHelpers.GetHashCode(Dictionary));
        hash.Add(RdfsVocabulary);
        hash.Add(RuntimeHelpers.GetHashCode(Options));
        hash.Add(RuntimeHelpers.GetHashCode(PatternMemo));
        return hash.ToHashCode();
    }

    /// <summary>Determines whether two bags share identical per-invocation state.</summary>
    public static bool operator ==(ParameterBag left, ParameterBag right) => left.Equals(right);

    /// <summary>Determines whether two bags differ in their per-invocation state.</summary>
    public static bool operator !=(ParameterBag left, ParameterBag right) => !left.Equals(right);

    /// <summary>Returns the primary parameter value as a <see cref="TermId"/>.</summary>
    public TermId RequirePrimaryTerm() => PrimaryValue;

    /// <summary>Returns the primary parameter value parsed as an integer.</summary>
    /// <exception cref="FormatException">Value is not a plain-integer literal.</exception>
    public int RequirePrimaryInt() => ParseInt(PrimaryValue, PrimaryParameter);

    /// <summary>Returns the primary parameter value parsed as a boolean.</summary>
    /// <exception cref="FormatException">Value is not a plain-boolean literal.</exception>
    public bool RequirePrimaryBool() => ParseBool(PrimaryValue, PrimaryParameter);

    /// <summary>Returns the primary parameter value narrowed to an <see cref="IriId"/>.</summary>
    /// <exception cref="FormatException">Value is not an IRI.</exception>
    public IriId RequirePrimaryIri() => ParseIri(PrimaryValue, PrimaryParameter);

    /// <summary>Returns the primary parameter value as a literal string.</summary>
    /// <exception cref="FormatException">Value is not a literal.</exception>
    public string RequirePrimaryString() => ParseString(PrimaryValue, PrimaryParameter);

    /// <summary>
    /// Returns the integer value associated with <paramref name="parameter"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="parameter"/> has no value on the owning shape.
    /// </exception>
    /// <exception cref="FormatException">
    /// The value is present but not a plain-integer literal.
    /// </exception>
    public int RequireInt(IriId parameter)
    {
        TermId value = RequireSingle(parameter);
        return ParseInt(value, parameter);
    }

    /// <summary>
    /// Returns the boolean value associated with <paramref name="parameter"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Parameter absent on shape.</exception>
    /// <exception cref="FormatException">Value is not a plain-boolean literal.</exception>
    public bool RequireBool(IriId parameter)
    {
        TermId value = RequireSingle(parameter);
        return ParseBool(value, parameter);
    }

    /// <summary>
    /// Returns the IRI value associated with <paramref name="parameter"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Parameter absent on shape.</exception>
    /// <exception cref="FormatException">Value is not an IRI.</exception>
    public IriId RequireIri(IriId parameter)
    {
        TermId value = RequireSingle(parameter);
        return ParseIri(value, parameter);
    }

    /// <summary>
    /// Returns the literal string value associated with
    /// <paramref name="parameter"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Parameter absent on shape.</exception>
    /// <exception cref="FormatException">Value is not a literal.</exception>
    public string RequireString(IriId parameter)
    {
        TermId value = RequireSingle(parameter);
        return ParseString(value, parameter);
    }

    /// <summary>
    /// Returns the raw <see cref="TermId"/> value associated with
    /// <paramref name="parameter"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Parameter absent on shape.</exception>
    public TermId RequireTerm(IriId parameter) => RequireSingle(parameter);

    // Factories naturally hold Utf8String constants (ShaclConstraintVocabulary entries).
    // These overloads resolve through the dictionary once and then forward to the
    // IriId-keyed implementation.

    /// <summary>Integer companion parameter by Utf8String IRI.</summary>
    public int RequireInt(Utf8String parameter) => RequireInt(Dictionary.GetOrAdd(new NamedNode(parameter)));

    /// <summary>Boolean companion parameter by Utf8String IRI.</summary>
    public bool RequireBool(Utf8String parameter) => RequireBool(Dictionary.GetOrAdd(new NamedNode(parameter)));

    /// <summary>IRI companion parameter by Utf8String IRI.</summary>
    public IriId RequireIri(Utf8String parameter) => RequireIri(Dictionary.GetOrAdd(new NamedNode(parameter)));

    /// <summary>String companion parameter by Utf8String IRI.</summary>
    public string RequireString(Utf8String parameter) => RequireString(Dictionary.GetOrAdd(new NamedNode(parameter)));

    /// <summary>Raw term companion parameter by Utf8String IRI.</summary>
    public TermId RequireTerm(Utf8String parameter) => RequireTerm(Dictionary.GetOrAdd(new NamedNode(parameter)));

    /// <summary>Optional integer companion parameter by Utf8String IRI.</summary>
    public int? OptionalInt(Utf8String parameter) => OptionalInt(Dictionary.GetOrAdd(new NamedNode(parameter)));

    /// <summary>Optional boolean companion parameter by Utf8String IRI.</summary>
    public bool? OptionalBool(Utf8String parameter) => OptionalBool(Dictionary.GetOrAdd(new NamedNode(parameter)));

    /// <summary>Optional string companion parameter by Utf8String IRI.</summary>
    public string? OptionalString(Utf8String parameter) => OptionalString(Dictionary.GetOrAdd(new NamedNode(parameter)));

    /// <summary>
    /// Resolves a shape-valued companion parameter to the term id of the
    /// referenced shape. The caller resolves the id against the shape
    /// registry at evaluation time; the bag does no lookup.
    /// </summary>
    /// <exception cref="InvalidOperationException">Parameter absent on shape.</exception>
    public TermId RequireShapeId(IriId parameter) => RequireSingle(parameter);

    /// <summary>Shape-id companion parameter by Utf8String IRI.</summary>
    public TermId RequireShapeId(Utf8String parameter) => RequireShapeId(Dictionary.GetOrAdd(new NamedNode(parameter)));

    /// <summary>
    /// Resolves a shape-valued companion parameter to the term id of the
    /// referenced shape, or <see langword="null"/> when the parameter is
    /// absent on the shape. Used by components whose shape reference is a
    /// mandatory companion that gates instantiation (the qualified-value-shape
    /// pair).
    /// </summary>
    public TermId? OptionalShapeId(Utf8String parameter)
    {
        return TryGetSingle(Dictionary.GetOrAdd(new NamedNode(parameter)), out TermId value) ? value : null;
    }

    /// <summary>
    /// Returns the members of an optional RDF list companion, typed as
    /// <see cref="IriId"/> handles, or <c>null</c> if the parameter is
    /// absent. Used by optional list companions such as
    /// <c>sh:ignoredProperties</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Parameter is present but its value is not a pre-resolved list.
    /// </exception>
    /// <exception cref="FormatException">A list member is not an IRI.</exception>
    public ImmutableArray<IriId>? OptionalRdfListOfIris(IriId parameter)
    {
        if(!TryGetSingle(parameter, out TermId head))
        {
            return null;
        }

        ImmutableArray<TermId> members = RequireResolvedList(head, parameter);
        ImmutableArray<IriId>.Builder builder = ImmutableArray.CreateBuilder<IriId>(members.Length);
        foreach(TermId member in members)
        {
            builder.Add(ParseIri(member, parameter));
        }

        return builder.ToImmutable();
    }

    /// <summary>Optional RDF-list-of-IRIs companion by Utf8String IRI.</summary>
    public ImmutableArray<IriId>? OptionalRdfListOfIris(Utf8String parameter)
        => OptionalRdfListOfIris(Dictionary.GetOrAdd(new NamedNode(parameter)));

    /// <summary>
    /// Returns the integer value associated with <paramref name="parameter"/>,
    /// or <c>null</c> if the parameter is absent.
    /// </summary>
    /// <exception cref="FormatException">
    /// Parameter is present but value is not a plain-integer literal.
    /// </exception>
    public int? OptionalInt(IriId parameter)
    {
        return TryGetSingle(parameter, out TermId value)
            ? ParseInt(value, parameter)
            : null;
    }

    /// <summary>
    /// Returns the boolean value associated with <paramref name="parameter"/>,
    /// or <c>null</c> if the parameter is absent.
    /// </summary>
    /// <exception cref="FormatException">
    /// Parameter is present but value is not a plain-boolean literal.
    /// </exception>
    public bool? OptionalBool(IriId parameter)
    {
        return TryGetSingle(parameter, out TermId value)
            ? ParseBool(value, parameter)
            : null;
    }

    /// <summary>
    /// Returns the literal string value associated with
    /// <paramref name="parameter"/>, or <c>null</c> if the parameter is
    /// absent.
    /// </summary>
    /// <exception cref="FormatException">
    /// Parameter is present but value is not a literal.
    /// </exception>
    public string? OptionalString(IriId parameter)
    {
        return TryGetSingle(parameter, out TermId value)
            ? ParseString(value, parameter)
            : null;
    }

    /// <summary>
    /// Returns the members of the RDF list whose head is the primary
    /// parameter value, typed as <see cref="IriId"/> handles. Used by
    /// list-of-IRIs parameters such as <c>sh:ignoredProperties</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The list was not pre-resolved by the loader, or the primary value
    /// is not a list head.
    /// </exception>
    /// <exception cref="FormatException">
    /// A list member is not an IRI.
    /// </exception>
    public ImmutableArray<IriId> RequirePrimaryRdfListOfIris()
    {
        ImmutableArray<TermId> members = RequireResolvedList(PrimaryValue, PrimaryParameter);
        ImmutableArray<IriId>.Builder builder = ImmutableArray.CreateBuilder<IriId>(members.Length);
        foreach(TermId member in members)
        {
            builder.Add(ParseIri(member, PrimaryParameter));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Returns the members of the RDF list whose head is the primary
    /// parameter value, as raw <see cref="TermId"/> values. Used by
    /// <c>sh:in</c> where members can be any kind of term.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The list was not pre-resolved by the loader, or the primary value
    /// is not a list head.
    /// </exception>
    public ImmutableArray<TermId> RequirePrimaryRdfListOfTerms()
    {
        return RequireResolvedList(PrimaryValue, PrimaryParameter);
    }

    /// <summary>
    /// Returns the members of the RDF list whose head is the primary
    /// parameter value, as literal strings. Used by
    /// <c>sh:languageIn</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The list was not pre-resolved by the loader, or the primary value
    /// is not a list head.
    /// </exception>
    /// <exception cref="FormatException">
    /// A list member is not a literal.
    /// </exception>
    public ImmutableArray<Utf8String> RequirePrimaryRdfListOfUtf8Strings()
    {
        ImmutableArray<TermId> members = RequireResolvedList(PrimaryValue, PrimaryParameter);
        ImmutableArray<Utf8String>.Builder builder = ImmutableArray.CreateBuilder<Utf8String>(members.Length);
        foreach(TermId member in members)
        {
            if(Dictionary.Resolve(member) is Literal literal)
            {
                builder.Add(literal.Value);
            }
            else
            {
                throw new FormatException(
                    $"Expected literal value in RDF list for parameter '{PrimaryParameter}', got {Dictionary.Resolve(member)}.");
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Returns the members of the RDF list whose head is the primary
    /// parameter value, as their term ids. Used by the shape-list
    /// combinator constraints (<c>sh:and</c>, <c>sh:or</c>,
    /// <c>sh:xone</c>). Callers resolve each id against the shape
    /// registry at evaluation time.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The list was not pre-resolved, or the primary value is not a
    /// list head.
    /// </exception>
    public ImmutableArray<TermId> RequirePrimaryRdfListOfShapeIds()
    {
        return RequireResolvedList(PrimaryValue, PrimaryParameter);
    }

    /// <summary>
    /// Returns the members of the RDF list associated with
    /// <paramref name="parameter"/>, typed as <see cref="IriId"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Parameter absent on shape, or its value is not a pre-resolved
    /// list.
    /// </exception>
    /// <exception cref="FormatException">A list member is not an IRI.</exception>
    public ImmutableArray<IriId> RequireRdfListOfIris(IriId parameter)
    {
        TermId head = RequireSingle(parameter);
        ImmutableArray<TermId> members = RequireResolvedList(head, parameter);
        ImmutableArray<IriId>.Builder builder = ImmutableArray.CreateBuilder<IriId>(members.Length);
        foreach(TermId member in members)
        {
            builder.Add(ParseIri(member, parameter));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Returns the primary parameter value as the term id of a
    /// referenced shape. The caller resolves the id against the shape
    /// registry at evaluation time; the bag does no lookup.
    /// </summary>
    public TermId RequirePrimaryShapeId() => PrimaryValue;

    /// <summary>
    /// Parses the primary parameter value as a <see cref="NodeKind"/>
    /// enum value. The value must be one of the six SHACL node-kind
    /// IRIs.
    /// </summary>
    /// <exception cref="FormatException">
    /// Value is not an IRI or is not one of the six node-kind IRIs.
    /// </exception>
    public NodeKind RequirePrimaryNodeKind()
    {
        if(Dictionary.Resolve(PrimaryValue) is not NamedNode named)
        {
            throw new FormatException(
                $"Expected node-kind IRI for parameter '{PrimaryParameter}', got {Dictionary.Resolve(PrimaryValue)}.");
        }

        if(ShaclNodeKindVocabulary.TryGetNodeKind(named.Iri, out NodeKind kind))
        {
            return kind;
        }

        throw new FormatException(
            $"Value '{named.Iri}' is not a recognised SHACL node-kind IRI.");
    }

    /// <summary>
    /// Compiles a regex from its SHACL source representation, consulting
    /// the <see cref="PatternResolver"/> first, then a per-session memo,
    /// and finally falling back to a fresh <see cref="Regex"/> with
    /// <see cref="RegexOptions.NonBacktracking"/> for ReDoS safety.
    /// </summary>
    /// <param name="pattern">The regex source text.</param>
    /// <param name="flags">The optional flag string, or <c>null</c>.</param>
    /// <param name="singleLine">Whether <c>sh:singleLine</c> is asserted.</param>
    /// <returns>The compiled matcher.</returns>
    public Regex CompilePattern(string pattern, string? flags, bool singleLine)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if(Options.PatternResolver is { } resolver)
        {
            Regex? resolved = resolver(pattern, flags, singleLine);
            if(resolved is not null)
            {
                return resolved;
            }
        }

        (string, string?, bool) key = (pattern, flags, singleLine);
        return PatternMemo.GetOrAdd(key, static k => CompileDefault(k.Item1, k.Item2, k.Item3));
    }

    private static Regex CompileDefault(string pattern, string? flags, bool singleLine)
    {
        RegexOptions regexOptions = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;
        if(flags is not null)
        {
            if(flags.Contains('i', StringComparison.Ordinal))
            {
                regexOptions |= RegexOptions.IgnoreCase;
            }

            if(flags.Contains('m', StringComparison.Ordinal))
            {
                regexOptions |= RegexOptions.Multiline;
            }

            if(flags.Contains('s', StringComparison.Ordinal))
            {
                regexOptions |= RegexOptions.Singleline;
            }

            if(flags.Contains('x', StringComparison.Ordinal))
            {
                regexOptions |= RegexOptions.IgnorePatternWhitespace;
            }
        }

        if(singleLine)
        {
            regexOptions |= RegexOptions.Singleline;
        }

        return new Regex(pattern, regexOptions);
    }

    /// <summary>
    /// Enumerates every scalar companion parameter: parameter IRI paired
    /// with its single <see cref="TermId"/> value. Parameters whose
    /// value is a pre-resolved list head are excluded and surface
    /// through <see cref="EnumerateCompanionLists"/> instead. The
    /// primary parameter is excluded — callers obtain the primary
    /// value directly through <see cref="PrimaryValue"/> and
    /// <see cref="PrimaryParameter"/>.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="Constraints.DynamicConstraint"/> factories to
    /// capture parameter values without knowing the parameter set at
    /// compile time. Order of enumeration is not specified.
    /// </remarks>
    public IEnumerable<KeyValuePair<IriId, TermId>> EnumerateCompanionScalars()
    {
        foreach(KeyValuePair<IriId, IReadOnlyList<TermId>> entry in Companions)
        {
            if(entry.Value.Count == 0)
            {
                continue;
            }

            TermId value = entry.Value[0];
            if(ResolvedLists.ContainsKey(value))
            {
                continue;
            }

            yield return new KeyValuePair<IriId, TermId>(entry.Key, value);
        }
    }

    /// <summary>
    /// Enumerates every companion parameter whose value is a
    /// pre-resolved RDF list: parameter IRI paired with the list's
    /// members as <see cref="TermId"/>s. The primary parameter is
    /// excluded; callers consume a primary-valued list through
    /// <see cref="RequirePrimaryRdfListOfTerms"/> or the typed
    /// <c>RequirePrimaryRdfListOf…</c> variants.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="Constraints.DynamicConstraint"/> factories.
    /// Order of enumeration is not specified.
    /// </remarks>
    public IEnumerable<KeyValuePair<IriId, ImmutableArray<TermId>>> EnumerateCompanionLists()
    {
        foreach(KeyValuePair<IriId, IReadOnlyList<TermId>> entry in Companions)
        {
            if(entry.Value.Count == 0)
            {
                continue;
            }

            TermId value = entry.Value[0];
            if(ResolvedLists.TryGetValue(value, out ImmutableArray<TermId> members))
            {
                yield return new KeyValuePair<IriId, ImmutableArray<TermId>>(entry.Key, members);
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the primary parameter value is a
    /// pre-resolved RDF list head. Useful for
    /// <see cref="Constraints.DynamicConstraint"/> factories deciding
    /// whether to capture the primary as a scalar or as a list.
    /// </summary>
    public bool PrimaryValueIsList => ResolvedLists.ContainsKey(PrimaryValue);

    /// <summary>
    /// Returns the primary parameter value as list members if the
    /// value is a pre-resolved list head. Throws if the primary is not
    /// a list — dynamic factories should check
    /// <see cref="PrimaryValueIsList"/> first, or call this inside a
    /// branch on the declared parameter's expected kind.
    /// </summary>
    /// <exception cref="InvalidOperationException">Primary value is not a pre-resolved list head.</exception>
    public ImmutableArray<TermId> RequirePrimaryListMembers()
        => RequireResolvedList(PrimaryValue, PrimaryParameter);

    private TermId RequireSingle(IriId parameter)
    {
        if(!Companions.TryGetValue(parameter, out IReadOnlyList<TermId>? values) || values.Count == 0)
        {
            throw new InvalidOperationException(
                $"Required parameter '{parameter}' is absent on the shape.");
        }

        return values[0];
    }

    private bool TryGetSingle(IriId parameter, out TermId value)
    {
        if(Companions.TryGetValue(parameter, out IReadOnlyList<TermId>? values) && values.Count > 0)
        {
            value = values[0];
            return true;
        }

        value = TermId.None;
        return false;
    }

    private ImmutableArray<TermId> RequireResolvedList(TermId listHead, IriId forParameter)
    {
        if(ResolvedLists.TryGetValue(listHead, out ImmutableArray<TermId> members))
        {
            return members;
        }

        throw new InvalidOperationException(
            $"RDF list for parameter '{forParameter}' was not pre-resolved by the loader.");
    }

    private int ParseInt(TermId value, IriId forParameter)
    {
        if(Dictionary.Resolve(value) is not Literal literal)
        {
            throw new FormatException(
                $"Expected integer literal for parameter '{forParameter}', got {Dictionary.Resolve(value)}.");
        }

        if(!Utf8Parser.TryParse(literal.Value.Span, out int parsed, out int consumed)
            || consumed != literal.Value.Span.Length)
        {
            throw new FormatException(
                $"Value '{literal.Value}' for parameter '{forParameter}' is not a valid integer.");
        }

        return parsed;
    }

    private bool ParseBool(TermId value, IriId forParameter)
    {
        if(Dictionary.Resolve(value) is not Literal literal)
        {
            throw new FormatException(
                $"Expected boolean literal for parameter '{forParameter}', got {Dictionary.Resolve(value)}.");
        }

        //SHACL boolean parameters are activation switches: a constraint is
        //active only when the value is the literal "true". This is lexical,
        //not value-based — "1"^^xsd:boolean is a valid xsd:boolean whose
        //value is true, yet it does NOT activate the switch (W3C
        //core/property/uniqueLang-002: "Only true is mentioned in the spec,
        //meaning that '1' will not activate the constraint"). The other
        //three lexical forms of the xsd:boolean space ("false", "0", "1")
        //all leave the switch off.
        ReadOnlySpan<byte> lexical = literal.Value.Span;
        if(lexical.SequenceEqual("true"u8))
        {
            return true;
        }

        if(lexical.SequenceEqual("false"u8) || lexical.SequenceEqual("0"u8) || lexical.SequenceEqual("1"u8))
        {
            return false;
        }

        throw new FormatException(
            $"Value '{literal.Value}' for parameter '{forParameter}' is not a valid boolean.");
    }

    private IriId ParseIri(TermId value, IriId forParameter)
    {
        if(Dictionary.Resolve(value) is not NamedNode)
        {
            throw new FormatException(
                $"Expected IRI for parameter '{forParameter}', got {Dictionary.Resolve(value)}.");
        }

        return IriId.FromUnchecked(value);
    }

    private string ParseString(TermId value, IriId forParameter)
    {
        if(Dictionary.Resolve(value) is not Literal literal)
        {
            throw new FormatException(
                $"Expected literal for parameter '{forParameter}', got {Dictionary.Resolve(value)}.");
        }

        return literal.Value.ToString();
    }
}
