using System;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Parser tests for SPARQL 1.2 Update: every operation form (<c>INSERT</c>/<c>DELETE DATA</c>, <c>DELETE WHERE</c>,
/// the general modify, <c>LOAD</c>, <c>CLEAR</c>/<c>DROP</c>, <c>CREATE</c>, <c>ADD</c>/<c>MOVE</c>/<c>COPY</c>),
/// the <c>GRAPH</c>-grouped quad block, and the <c>;</c>-separated sequence with interleaved prologue, parse to the
/// expected <see cref="SparqlUpdateRequest"/> AST without errors.
/// </summary>
[TestClass]
internal sealed class SparqlUpdateParserTests
{
    /// <summary>Parses an update request, asserting it is error-free, and returns it.</summary>
    /// <param name="text">The update text.</param>
    /// <param name="pool">The interning pool (kept alive by the caller while the AST is inspected).</param>
    /// <returns>The parsed update request.</returns>
    private static SparqlUpdateRequest ParseUpdate(string text, Utf8StringPool pool)
    {
        ParseResult<SparqlRequest> result = SparqlParser.ParseRequest(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(text)), pool);
        Assert.IsFalse(result.HasErrors, "the update request should parse without diagnostics");

        return (SparqlUpdateRequest)result.Tree;
    }

    [TestMethod]
    public void InsertDataParsesGroundTriple()
    {
        using Utf8StringPool pool = new();
        SparqlUpdateRequest update = ParseUpdate("INSERT DATA { <urn:s> <urn:p> <urn:o> }", pool);

        Assert.HasCount(1, update.Operations);
        InsertDataOperation operation = (InsertDataOperation)update.Operations[0];
        Assert.HasCount(1, operation.Data.DefaultTriples);
        Assert.HasCount(0, operation.Data.GraphGroups);
    }

    [TestMethod]
    public void DeleteDataParses()
    {
        using Utf8StringPool pool = new();
        SparqlUpdateRequest update = ParseUpdate("DELETE DATA { <urn:s> <urn:p> <urn:o> }", pool);

        Assert.IsInstanceOfType<DeleteDataOperation>(update.Operations[0]);
    }

    [TestMethod]
    public void DataWithGraphGroupParses()
    {
        using Utf8StringPool pool = new();
        SparqlUpdateRequest update = ParseUpdate("INSERT DATA { <urn:s> <urn:p> <urn:o> . GRAPH <urn:g> { <urn:a> <urn:b> <urn:c> } }", pool);

        InsertDataOperation operation = (InsertDataOperation)update.Operations[0];
        Assert.HasCount(1, operation.Data.DefaultTriples);
        Assert.HasCount(1, operation.Data.GraphGroups);
        Assert.IsInstanceOfType<GraphIriTerm>(operation.Data.GraphGroups[0].Graph);
    }

    [TestMethod]
    public void DeleteWhereParses()
    {
        using Utf8StringPool pool = new();
        SparqlUpdateRequest update = ParseUpdate("DELETE WHERE { ?s <urn:p> ?o }", pool);

        DeleteWhereOperation operation = (DeleteWhereOperation)update.Operations[0];
        Assert.HasCount(1, operation.Pattern.DefaultTriples);
    }

    [TestMethod]
    public void ModifyDeleteInsertWhereParses()
    {
        using Utf8StringPool pool = new();
        SparqlUpdateRequest update = ParseUpdate("DELETE { ?s <urn:p> ?o } INSERT { ?s <urn:p2> ?o } WHERE { ?s <urn:p> ?o }", pool);

        ModifyOperation operation = (ModifyOperation)update.Operations[0];
        Assert.IsNotNull(operation.Delete);
        Assert.IsNotNull(operation.Insert);
        Assert.IsNull(operation.With);
        Assert.IsInstanceOfType<GroupGraphPattern>(operation.Where);
    }

    [TestMethod]
    public void InsertWhereWithUsingAndWithParses()
    {
        using Utf8StringPool pool = new();
        SparqlUpdateRequest update = ParseUpdate("WITH <urn:g> INSERT { ?s <urn:p2> ?o } USING <urn:u> USING NAMED <urn:n> WHERE { ?s <urn:p> ?o }", pool);

        ModifyOperation operation = (ModifyOperation)update.Operations[0];
        Assert.IsNull(operation.Delete);
        Assert.IsNotNull(operation.Insert);
        Assert.IsNotNull(operation.With);
        Assert.HasCount(2, operation.Using);
        Assert.IsTrue(operation.Using[1].IsNamed);
    }

    [TestMethod]
    public void LoadParsesWithIntoGraph()
    {
        using Utf8StringPool pool = new();
        SparqlUpdateRequest update = ParseUpdate("LOAD SILENT <urn:doc> INTO GRAPH <urn:g>", pool);

        LoadOperation operation = (LoadOperation)update.Operations[0];
        Assert.IsTrue(operation.Silent);
        Assert.IsNotNull(operation.Into);
    }

    [TestMethod]
    public void ClearAndDropParseGraphReferences()
    {
        using Utf8StringPool pool = new();
        SparqlUpdateRequest update = ParseUpdate("CLEAR ALL ; DROP GRAPH <urn:g>", pool);

        Assert.HasCount(2, update.Operations);
        Assert.IsInstanceOfType<GraphRefAll>(((ClearOperation)update.Operations[0]).Target);
        Assert.IsInstanceOfType<GraphRefIri>(((DropOperation)update.Operations[1]).Target);
    }

    [TestMethod]
    public void CreateParses()
    {
        using Utf8StringPool pool = new();
        SparqlUpdateRequest update = ParseUpdate("CREATE GRAPH <urn:g>", pool);

        Assert.IsInstanceOfType<CreateOperation>(update.Operations[0]);
    }

    [TestMethod]
    public void AddMoveCopyParse()
    {
        using Utf8StringPool pool = new();
        SparqlUpdateRequest update = ParseUpdate("ADD DEFAULT TO <urn:g> ; MOVE <urn:g> TO DEFAULT ; COPY <urn:a> TO <urn:b>", pool);

        Assert.HasCount(3, update.Operations);
        Assert.IsInstanceOfType<AddOperation>(update.Operations[0]);
        Assert.IsInstanceOfType<MoveOperation>(update.Operations[1]);
        Assert.IsInstanceOfType<CopyOperation>(update.Operations[2]);
        Assert.IsInstanceOfType<GraphRefDefault>(((AddOperation)update.Operations[0]).Source);
    }

    [TestMethod]
    public void PrologueInterleavesBetweenOperations()
    {
        using Utf8StringPool pool = new();
        SparqlUpdateRequest update = ParseUpdate("PREFIX : <urn:ns#> INSERT DATA { :s :p :o } ; PREFIX q: <urn:q#> INSERT DATA { q:a q:b q:c }", pool);

        Assert.HasCount(2, update.Operations);
        Assert.IsInstanceOfType<InsertDataOperation>(update.Operations[0]);
        Assert.IsInstanceOfType<InsertDataOperation>(update.Operations[1]);
    }

    [TestMethod]
    public void EmptyUpdateParsesToNoOperations()
    {
        using Utf8StringPool pool = new();
        SparqlUpdateRequest update = ParseUpdate("PREFIX : <urn:ns#>", pool);

        Assert.HasCount(0, update.Operations);
    }
}
