using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Parses a SHACL-SPARQL constraint node — the object of a shape's <c>sh:sparql</c> triple — into a
/// <see cref="SparqlConstraint"/>. Called once per <c>sh:sparql</c> occurrence during population, mirroring how
/// <see cref="ShapePathParser"/> resolves a <c>sh:path</c> sub-graph.
/// </summary>
/// <remarks>
/// <para>
/// <b>Structure (SHACL-SPARQL §5).</b> The constraint node carries <c>sh:select</c> (the query text),
/// optionally <c>sh:message</c> (one per language tag), <c>sh:deactivated</c>, and <c>sh:prefixes</c>. The query
/// (with its prefixes prepended) is parsed and normalized once via <see cref="SparqlQueryParsing"/> so evaluation
/// re-uses the parsed <see cref="SparqlQuery"/>.
/// </para>
/// <para>
/// <b>Slice scope.</b> Only the <c>sh:select</c> form is built. A constraint that is deactivated, has no
/// <c>sh:select</c>, or whose query does not parse cleanly to a query yields <see langword="null"/> — the
/// constraint is not enforced rather than throwing, so a shape graph the engine cannot yet handle under-validates
/// instead of aborting the whole load.
/// </para>
/// </remarks>
internal static class SparqlConstraintParser
{
    /// <summary>
    /// Parses the constraint node into a <see cref="SparqlConstraint"/>, or returns <see langword="null"/> when
    /// it is deactivated, carries no <c>sh:select</c>, or its query does not parse to a clean SELECT query.
    /// </summary>
    /// <param name="constraintNode">The object of the shape's <c>sh:sparql</c> triple.</param>
    /// <param name="shapeGraphMatch">The shape-graph triple source.</param>
    /// <param name="dictionary">Term dictionary for decoding literals and IRIs.</param>
    /// <param name="ids">Pre-resolved SHACL vocabulary identifiers.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The parsed constraint, or <see langword="null"/> when it is not (yet) enforceable.</returns>
    public static async Task<SparqlConstraint?> ParseAsync(
        TermId constraintNode,
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        TermDictionary dictionary,
        ShaclPopulationIds ids,
        CancellationToken cancellationToken)
    {
        Utf8String? selectText = null;
        bool deactivated = false;
        ImmutableDictionary<string, string>.Builder messages = ImmutableDictionary.CreateBuilder<string, string>();
        List<TermId> prefixSubjects = [];

        await foreach(EncodedTriple triple in shapeGraphMatch(constraintNode, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            IriId predicate = IriId.FromUnchecked(triple.Predicate);

            if(predicate.Equals(ids.Select) && dictionary.Resolve(triple.Object) is Literal selectLiteral)
            {
                selectText = selectLiteral.Value;
            }
            else if(predicate.Equals(ids.Deactivated) && dictionary.Resolve(triple.Object) is Literal deactivatedLiteral)
            {
                deactivated = IsTrue(deactivatedLiteral.Value);
            }
            else if(predicate.Equals(ids.Message) && dictionary.Resolve(triple.Object) is Literal messageLiteral)
            {
                string language = messageLiteral.Language is { } tag ? tag.ToString() : string.Empty;
                messages[language] = messageLiteral.Value.ToString();
            }
            else if(predicate.Equals(ids.Prefixes))
            {
                prefixSubjects.Add(triple.Object);
            }
        }

        if(deactivated || selectText is not Utf8String select)
        {
            return null;
        }

        SparqlQuery? normalized = await SparqlQueryParsing.ParseAsync(
            select, prefixSubjects, shapeGraphMatch, dictionary, ids.Declare, ids.Prefix, ids.Namespace, cancellationToken).ConfigureAwait(false);
        if(normalized is null)
        {
            return null;
        }

        return new SparqlConstraint(constraintNode, select, normalized, messages.ToImmutable());
    }

    /// <summary>Returns whether an <c>xsd:boolean</c> lexical form is true (the lexical space is exactly <c>{true, false, 1, 0}</c>).</summary>
    /// <param name="lexical">The literal's lexical value.</param>
    /// <returns><see langword="true"/> when the value is <c>true</c> or <c>1</c>.</returns>
    private static bool IsTrue(Utf8String lexical) => lexical.Span.SequenceEqual("true"u8) || lexical.Span.SequenceEqual("1"u8);
}
