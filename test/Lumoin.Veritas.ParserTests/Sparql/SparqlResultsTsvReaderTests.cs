using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Verifies that <see cref="SparqlResultsTsvReader"/> parses the SPARQL Results TSV term syntax back into typed
/// terms: IRIs, plain/typed/language-tagged literals, the numeric abbreviations, blank nodes, and unbound cells.
/// </summary>
[TestClass]
internal sealed class SparqlResultsTsvReaderTests
{
    /// <summary>Parses a header, the term forms, and an unbound cell into the expected typed terms.</summary>
    [TestMethod]
    public void ParsesTermFormsAndUnboundCells()
    {
        string tsv = string.Join('\n',
            "?iri\t?str\t?int\t?dec\t?dbl\t?lang\t?bnode\t?opt",
            "<http://example.org/a>\t\"foo\"\t4\t5.5\t1.0E6\t\"chat\"@fr\t_:b0\t");

        SparqlResultSet result = SparqlResultsTsvReader.Read(Encoding.UTF8.GetBytes(tsv));

        Assert.HasCount(8, result.Variables);
        Assert.HasCount(1, result.Solutions);
        SparqlSolution row = result.Solutions[0];

        Assert.IsTrue(row.TryGetValue(Var("iri"), out RdfTerm iri));
        Assert.AreEqual("http://example.org/a", ((NamedNode)iri).Iri.ToString());

        Assert.IsTrue(row.TryGetValue(Var("str"), out RdfTerm str));
        Assert.AreEqual(Vocabulary.Xsd.String, ((Literal)str).Datatype.Iri);
        Assert.AreEqual("foo", ((Literal)str).Value.ToString());

        Assert.IsTrue(row.TryGetValue(Var("int"), out RdfTerm integer));
        Assert.AreEqual(Vocabulary.Xsd.Integer, ((Literal)integer).Datatype.Iri);

        Assert.IsTrue(row.TryGetValue(Var("dec"), out RdfTerm decimalValue));
        Assert.AreEqual(Vocabulary.Xsd.Decimal, ((Literal)decimalValue).Datatype.Iri);

        Assert.IsTrue(row.TryGetValue(Var("dbl"), out RdfTerm doubleValue));
        Assert.AreEqual(Vocabulary.Xsd.Double, ((Literal)doubleValue).Datatype.Iri);

        Assert.IsTrue(row.TryGetValue(Var("lang"), out RdfTerm lang));
        Assert.AreEqual("fr", ((Literal)lang).Language!.Value.ToString());

        Assert.IsTrue(row.TryGetValue(Var("bnode"), out RdfTerm bnode));
        Assert.IsInstanceOfType<BlankNode>(bnode);

        //The trailing empty cell is an unbound variable, not a binding.
        Assert.IsFalse(row.TryGetValue(Var("opt"), out _));
    }

    /// <summary>A literal whose value carries multi-byte UTF-8 is preserved byte-for-byte.</summary>
    [TestMethod]
    public void ParsesMultiByteUtf8LiteralValue()
    {
        string tsv = string.Join('\n', "?v", "\"café ☕\"");

        SparqlResultSet result = SparqlResultsTsvReader.Read(Encoding.UTF8.GetBytes(tsv));

        Assert.IsTrue(result.Solutions[0].TryGetValue(Var("v"), out RdfTerm term));
        Assert.AreEqual("café ☕", ((Literal)term).Value.ToString());
        Assert.AreSequenceEqual(Encoding.UTF8.GetBytes("café ☕"), ((Literal)term).Value.Span.ToArray());
    }

    /// <summary>Escaped tabs and newlines inside a value decode to their control bytes, not field or line breaks.</summary>
    [TestMethod]
    public void UnescapesTabAndNewlineInsideValue()
    {
        string tsv = string.Join('\n', "?v", "\"a\\tb\\nc\"");

        SparqlResultSet result = SparqlResultsTsvReader.Read(Encoding.UTF8.GetBytes(tsv));

        Assert.HasCount(1, result.Solutions);
        Assert.IsTrue(result.Solutions[0].TryGetValue(Var("v"), out RdfTerm term));
        Assert.AreEqual("a\tb\nc", ((Literal)term).Value.ToString());
    }

    /// <summary>Builds a variable from its name.</summary>
    /// <param name="name">The variable name (without the <c>?</c>).</param>
    /// <returns>The variable.</returns>
    private static Lumoin.Veritas.Sparql.Ast.SparqlVariable Var(string name)
    {
        return new Lumoin.Veritas.Sparql.Ast.SparqlVariable(Utf8Strings.From(name));
    }
}
