using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Encoding;

/// <summary>
/// A <see cref="TermId"/> that is known to refer to a <see cref="NamedNode"/>
/// (an IRI term).
/// </summary>
/// <remarks>
/// <para>
/// Many RDF operations are valid only on IRI terms. For example, the predicate
/// position of a triple must be an IRI; <c>sh:targetClass</c> values must be
/// IRIs; property paths are built from IRI predicates. Passing
/// <see cref="IriId"/> at these API boundaries — rather than raw
/// <see cref="TermId"/> — captures that constraint in the type system.
/// </para>
/// <para>
/// <b>Constructing an <see cref="IriId"/>.</b> Two entry points, with different
/// safety trade-offs:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="From(TermId, TermDictionary)"/> — validates that the resolved
/// term is a <see cref="NamedNode"/>, throws otherwise. Use at boundaries
/// where the kind is unknown.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="FromUnchecked(TermId)"/> — wraps without validation. Use on hot
/// paths where the kind is already known from context (e.g. the predicate
/// field of an <see cref="EncodedTriple"/>).
/// </description>
/// </item>
/// </list>
/// <para>
/// <b>Widening.</b> <see cref="IriId"/> widens implicitly to <see cref="TermId"/>.
/// To obtain the raw encoded numeric value, use <see cref="Encoded"/>
/// explicitly. No implicit narrowing from <see cref="TermId"/>; use the
/// factories.
/// </para>
/// </remarks>
/// <param name="Value">The underlying <see cref="TermId"/>.</param>
[DebuggerDisplay("IriId({Value.Encoded})")]
public readonly record struct IriId(TermId Value)
{
    /// <summary>
    /// The raw encoded identifier, forwarded from the underlying <see cref="TermId"/>.
    /// </summary>
    public uint Encoded => Value.Encoded;

    /// <summary>
    /// Validates that <paramref name="termId"/> resolves to a
    /// <see cref="NamedNode"/> and returns it as an <see cref="IriId"/>.
    /// </summary>
    /// <param name="termId">The identifier to narrow.</param>
    /// <param name="dictionary">The dictionary used to resolve <paramref name="termId"/>.</param>
    /// <returns>A validated <see cref="IriId"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The resolved term is not a <see cref="NamedNode"/>.
    /// </exception>
    public static IriId From(TermId termId, TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        RdfTerm term = dictionary.Resolve(termId.Encoded);
        if(term is not NamedNode)
        {
            throw new InvalidOperationException(
                $"TermId {termId.Encoded} does not resolve to a NamedNode (IRI); actual kind: {term.GetType().Name}.");
        }

        return new IriId(termId);
    }

    /// <summary>
    /// Attempts to narrow <paramref name="termId"/> to an <see cref="IriId"/>
    /// without throwing.
    /// </summary>
    /// <param name="termId">The identifier to narrow.</param>
    /// <param name="dictionary">The dictionary used to resolve <paramref name="termId"/>.</param>
    /// <param name="iriId">On success, the validated <see cref="IriId"/>.</param>
    /// <returns><c>true</c> if <paramref name="termId"/> resolves to a <see cref="NamedNode"/>.</returns>
    public static bool TryFrom(TermId termId, TermDictionary dictionary, out IriId iriId)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        RdfTerm term = dictionary.Resolve(termId.Encoded);
        if(term is NamedNode)
        {
            iriId = new IriId(termId);
            return true;
        }

        iriId = default;
        return false;
    }

    /// <summary>
    /// Wraps <paramref name="termId"/> as an <see cref="IriId"/> without
    /// validating the underlying term kind.
    /// </summary>
    /// <remarks>
    /// The caller asserts by construction that <paramref name="termId"/>
    /// refers to a <see cref="NamedNode"/>. Use this on hot paths where the
    /// kind is guaranteed by context (e.g. the predicate position of a triple).
    /// Misuse produces confusing errors downstream.
    /// </remarks>
    /// <param name="termId">The identifier to wrap.</param>
    /// <returns>The wrapped <see cref="IriId"/>.</returns>
    public static IriId FromUnchecked(TermId termId) => new(termId);

    /// <summary>
    /// Implicit widening from <see cref="IriId"/> to <see cref="TermId"/>.
    /// </summary>
    public static implicit operator TermId(IriId iriId) => iriId.Value;

    /// <inheritdoc/>
    public override string ToString() => $"IriId({Encoded})";
}
