using System.Collections.Generic;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Indexing;

/// <summary>One buildable entry of a declared predicate: the subject, the value term's locator, and the value literal.</summary>
/// <param name="Subject">The entry's subject.</param>
/// <param name="ValueTerm">The value term's encoded id — the locator a probe hit reports.</param>
/// <param name="Value">The value literal (lexical form and datatype) the access method parses onto its axis.</param>
public readonly record struct ValueSegmentEntry(TermId Subject, TermId ValueTerm, Literal Value);

/// <summary>
/// The data surface a <see cref="ValueAccessMethod"/> builds from: subject-keyed (subject, value-term)
/// enumeration per declared predicate.
/// </summary>
/// <remarks>
/// <para>
/// The interval-pair build is a first-class operation against this contract, never a store reach-around:
/// the source exposes each declared predicate's entries (one predicate for a point axis, both for an
/// interval pair), and the build joins the two enumerations on the occurrence subject. At registration
/// time the registrant supplies a sample-corpus source for the acceptance self-test; in the engine the
/// source is backed by the post-commit store's predicate access.
/// </para>
/// </remarks>
public abstract class ValueSegmentSource
{
    /// <summary>Enumerates the (subject, value) entries of one declared predicate.</summary>
    /// <param name="predicateIri">The declared predicate's IRI.</param>
    /// <returns>The entries, in an order of the source's choosing.</returns>
    public abstract IEnumerable<ValueSegmentEntry> EnumerateDeclared(Utf8String predicateIri);
}
