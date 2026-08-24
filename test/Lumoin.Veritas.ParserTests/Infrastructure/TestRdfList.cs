using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.ParserTests.Infrastructure;

/// <summary>
/// Builds <c>rdf:list</c> chains on a focus node for use in evaluator
/// tests. Replaces the per-test-class <c>AssembleIntegerList</c> and
/// <c>AssembleRdfTermList</c> helpers that previously duplicated the
/// list-assembly logic.
/// </summary>
/// <remarks>
/// <para>
/// Emits one <c>(focus, path, head-cell)</c> triple plus, for each
/// member, one <c>(cell-i, rdf:first, member-i)</c> and
/// <c>(cell-i, rdf:rest, cell-(i+1))</c> triple, with the final cell
/// pointing to <c>rdf:nil</c>. For an empty member list, emits a
/// single <c>(focus, path, rdf:nil)</c> triple representing the
/// empty SHACL list.
/// </para>
/// <para>
/// <b>Cell IRI scoping.</b> Cell IRIs include the focus IRI as a
/// suffix, so multiple lists in the same data graph (one per focus)
/// don't collide. The cell IRI scheme is purely an implementation
/// detail; SHACL evaluators don't care whether list cells are IRIs
/// or blank nodes, so using IRIs keeps the test fixtures simple.
/// </para>
/// </remarks>
internal static class TestRdfList
{
    private const string RdfFirst = "http://www.w3.org/1999/02/22-rdf-syntax-ns#first";
    private const string RdfRest = "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest";
    private const string RdfNil = "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil";

    /// <summary>
    /// Adds an <c>rdf:list</c> chain rooted at the focus and reachable
    /// via the given path predicate.
    /// </summary>
    /// <param name="dataState">The pipeline data state to extend.</param>
    /// <param name="focusIri">The focus IRI.</param>
    /// <param name="pathIri">The predicate IRI from focus to list head.</param>
    /// <param name="members">The list members in order.</param>
    /// <returns>The extended data state.</returns>
    public static TestShaclPipelineDataState Assemble(
        TestShaclPipelineDataState dataState,
        string focusIri,
        string pathIri,
        IReadOnlyList<RdfTerm> members)
    {
        if(members.Count == 0)
        {
            return dataState.WithExplicitTriple(
                subjectIri: focusIri,
                predicateIri: pathIri,
                @object: new NamedNode(Utf8Strings.From(RdfNil)));
        }

        TestShaclPipelineDataState state = dataState.WithExplicitTriple(
            subjectIri: focusIri,
            predicateIri: pathIri,
            @object: new NamedNode(Utf8Strings.From(CellIri(focusIri, 0))));

        for(int i = 0; i < members.Count; i++)
        {
            state = state.WithExplicitTriple(
                subjectIri: CellIri(focusIri, i),
                predicateIri: RdfFirst,
                @object: members[i]);

            string restIri = i == members.Count - 1
                ? RdfNil
                : CellIri(focusIri, i + 1);

            state = state.WithExplicitTriple(
                subjectIri: CellIri(focusIri, i),
                predicateIri: RdfRest,
                @object: new NamedNode(Utf8Strings.From(restIri)));
        }

        return state;
    }

    private static string CellIri(string focusIri, int index)
        => $"{focusIri}#cell-{index}";
}
