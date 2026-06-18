using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Json.Stj;
using Lumoin.Veritas.Jsonata;
using Lumoin.Veritas.Jsonata.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JsonataEngine = Lumoin.Veritas.Jsonata.Jsonata;

namespace Lumoin.Veritas.ParserTests.Jsonata;

/// <summary>
/// Tests for the SUB-4 tuple-stream tail: the parent operator <c>%</c> (and the grandparent <c>%.%</c>)
/// resolving through a parenthesised nested path / block step. A trailing <c>%</c> in a constructor, predicate,
/// sort term, or conditional that sits after a parenthesised <c>(a.b)</c> sub-path must reach the structural
/// ancestor the sub-path navigated from — the inner sub-path is promoted to a keep-tuples tuple path whose
/// captured inner step binds the ancestor, and the enclosing tuple step merges that inner tuple stream. Also
/// guards the grandparent level arithmetic (a <c>%.%</c> bubbled from inside a constructor must climb exactly
/// two structural steps, not three).
/// </summary>
[TestClass]
internal sealed class JsonataParentThroughParensTests
{
    /// <summary>The standard nested Account/Order/Product document the parent-operator cases navigate.</summary>
    private const string Dataset =
        """
        { "Account": { "Account Name": "Firefly", "Order": [
            { "OrderID": "order103", "Product": [
                { "Product Name": "Bowler Hat", "Price": 34.45, "Quantity": 2 },
                { "Product Name": "Trilby hat", "Price": 21.67, "Quantity": 1 } ] },
            { "OrderID": "order104", "Product": [
                { "Product Name": "Bowler Hat", "Price": 34.45, "Quantity": 4 },
                { "Product Name": "Cloak", "Price": 107.99, "Quantity": 1 } ] } ] } }
        """;

    /// <summary>A grandparent <c>%.%</c> bubbled from inside a trailing array constructor climbs exactly two structural steps (Product, Order) to the Account, not three — the single-source of the earlier off-by-one.</summary>
    [TestMethod]
    public void GrandparentInArrayConstructorClimbsTwoSteps()
    {
        Assert.AreEqual("[\"order103\",\"Firefly\",\"order103\",\"Firefly\",\"order104\",\"Firefly\",\"order104\",\"Firefly\"]", Evaluate("Account.Order.Product.[%.OrderID, %.%.`Account Name`]"));
    }

    /// <summary>A grandparent <c>%.%</c> inside a trailing object constructor resolves the Account through the Order parent.</summary>
    [TestMethod]
    public void GrandparentInObjectConstructorResolvesAccount()
    {
        Assert.AreEqual("[{\"o\":\"order103\",\"a\":\"Firefly\"},{\"o\":\"order103\",\"a\":\"Firefly\"},{\"o\":\"order104\",\"a\":\"Firefly\"},{\"o\":\"order104\",\"a\":\"Firefly\"}]", Evaluate("Account.Order.Product.{ 'o': %.OrderID, 'a': %.%.`Account Name` }"));
    }

    /// <summary>A trailing <c>%</c> resolves through a parenthesised leading sub-path <c>(Order.Product)</c> to the Order.</summary>
    [TestMethod]
    public void ParentResolvesThroughParenthesisedSubPath()
    {
        Assert.AreEqual("[\"order103\",\"order103\",\"order104\",\"order104\"]", Evaluate("Account.(Order.Product).%.OrderID"));
    }

    /// <summary>A constructor after a parenthesised sub-path reads the parent through the merged inner tuple stream (parent001).</summary>
    [TestMethod]
    public void ConstructorAfterParenthesisedSubPathReadsParent()
    {
        Assert.AreEqual("[{\"order\":\"order103\"},{\"order\":\"order103\"},{\"order\":\"order104\"},{\"order\":\"order104\"}]", Evaluate("Account.(Order.Product).{ 'order': %.OrderID }"));
    }

    /// <summary>A doubly-nested parenthesised path <c>(Order.(Product))</c> composes the inner tuple-stream merge so both the Order parent and Account grandparent resolve (parent004).</summary>
    [TestMethod]
    public void DoublyNestedParenthesesComposeTheMerge()
    {
        Assert.AreEqual("[{\"o\":\"order103\",\"a\":\"Firefly\"},{\"o\":\"order103\",\"a\":\"Firefly\"},{\"o\":\"order104\",\"a\":\"Firefly\"},{\"o\":\"order104\",\"a\":\"Firefly\"}]", Evaluate("Account.(Order.(Product)).{ 'o': %.OrderID, 'a': %.%.`Account Name` }"));
    }

    /// <summary>A predicate <c>%</c> after a parenthesised leading path filters by the parent's field (parent.json[10]).</summary>
    [TestMethod]
    public void PredicateParentAfterParenthesisedLeadingPath()
    {
        Assert.AreEqual("[34.45,21.67]", Evaluate("(Account.Order.Product)[%.OrderID='order103'].Price"));
    }

    /// <summary>Evaluates the expression against the dataset and serializes the result to compact JSON.</summary>
    /// <param name="expression">The JSONata expression.</param>
    /// <returns>The result serialized as compact JSON.</returns>
    private static string Evaluate(string expression)
    {
        JsonNode input = StjJsonAdapter.Parse(new Utf8String(Encoding.UTF8.GetBytes(Dataset)));
        JsonataValue result = JsonataEngine.Evaluate(Encoding.UTF8.GetBytes(expression), input);

        return JsonataEngine.SerializeToJson(result).ToString();
    }
}
