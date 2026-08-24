using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Json.Stj;
using Lumoin.Veritas.Jsonata;
using Lumoin.Veritas.Jsonata.Functions;
using Lumoin.Veritas.Jsonata.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JsonataEngine = Lumoin.Veritas.Jsonata.Jsonata;

namespace Lumoin.Veritas.ParserTests.Jsonata;

/// <summary>
/// Tests for the JSONata function-signature validator: the iterative signature parser over the type letters,
/// modifiers, unions, and array-element subtypes; and the validator's three behaviours through both the
/// direct API and the engine — context substitution (T0411 when the context is incompatible), array
/// singleton-wrapping, and the type errors (T0410 on an argument mismatch, T0412 on a wrong array-element
/// subtype).
/// </summary>
[TestClass]
internal sealed class JsonataSignatureTests
{
    /// <summary>The JSONata error code for arguments that do not match the function signature.</summary>
    private const string CodeArgumentMismatch = "T0410";

    /// <summary>The JSONata error code for a context value incompatible with a context-substituted parameter.</summary>
    private const string CodeContextIncompatible = "T0411";

    /// <summary>The JSONata error code for an array argument whose elements are not the required subtype.</summary>
    private const string CodeWrongElementType = "T0412";

    /// <summary>The signature <c>&lt;s-:s&gt;</c> parses to one string parameter marked context-substituted and relaxed to optional.</summary>
    [TestMethod]
    public void ParsesContextStringParameter()
    {
        JsonataSignature signature = JsonataSignature.Parse("<s-:s>");

        Assert.HasCount(1, signature.Parameters);

        SignatureParam param = signature.Parameters[0];
        Assert.AreEqual(SignatureType.String | SignatureType.Missing, param.TypeSet);
        Assert.IsTrue(param.IsContext);
        Assert.AreEqual(SignatureQuantifier.Optional, param.Quantifier);
        Assert.AreEqual(SignatureType.String | SignatureType.Missing, param.ContextTypeSet);
        Assert.IsFalse(param.IsArray);
    }

    /// <summary>The signature <c>&lt;a&lt;n&gt;:n&gt;</c> parses to one array parameter carrying the number element subtype.</summary>
    [TestMethod]
    public void ParsesArrayWithNumberSubtype()
    {
        JsonataSignature signature = JsonataSignature.Parse("<a<n>:n>");

        Assert.HasCount(1, signature.Parameters);

        SignatureParam param = signature.Parameters[0];
        Assert.IsTrue(param.IsArray);
        Assert.AreEqual('n', param.Subtype);
        Assert.AreEqual(SignatureQuantifier.One, param.Quantifier);
    }

    /// <summary>The signature <c>&lt;x-b?:s&gt;</c> parses to two parameters: a context-substituted any parameter and an optional boolean parameter.</summary>
    [TestMethod]
    public void ParsesContextAnyAndOptionalBoolean()
    {
        JsonataSignature signature = JsonataSignature.Parse("<x-b?:s>");

        Assert.HasCount(2, signature.Parameters);

        SignatureParam first = signature.Parameters[0];
        Assert.IsTrue(first.IsContext);
        Assert.AreEqual(SignatureQuantifier.Optional, first.Quantifier);

        SignatureParam second = signature.Parameters[1];
        Assert.AreEqual(SignatureType.Boolean | SignatureType.Missing, second.TypeSet);
        Assert.AreEqual(SignatureQuantifier.Optional, second.Quantifier);
        Assert.IsFalse(second.IsContext);
    }

    /// <summary>The signature <c>&lt;s-(sf)n?:a&lt;s&gt;&gt;</c> parses to three parameters: a context string, a required string-or-function union, and an optional number.</summary>
    [TestMethod]
    public void ParsesUnionAndOptionalNumber()
    {
        JsonataSignature signature = JsonataSignature.Parse("<s-(sf)n?:a<s>>");

        Assert.HasCount(3, signature.Parameters);

        SignatureParam first = signature.Parameters[0];
        Assert.IsTrue(first.IsContext);

        SignatureParam union = signature.Parameters[1];
        Assert.AreEqual(SignatureType.String | SignatureType.Function | SignatureType.Missing, union.TypeSet);
        Assert.AreEqual(SignatureQuantifier.One, union.Quantifier);

        SignatureParam third = signature.Parameters[2];
        Assert.AreEqual(SignatureType.Number | SignatureType.Missing, third.TypeSet);
        Assert.AreEqual(SignatureQuantifier.Optional, third.Quantifier);
    }

    /// <summary>The validator substitutes the focus for an absent context parameter when the focus type satisfies the context set.</summary>
    [TestMethod]
    public void ValidatorSubstitutesCompatibleContext()
    {
        JsonataSignature signature = JsonataSignature.Parse("<s-:s>");

        JsonataValue[] validated = JsonataSignatureValidator.Validate(signature, [], JsonataValue.String("root"));

        Assert.HasCount(1, validated);
        Assert.AreEqual("root", validated[0].AsString);
    }

    /// <summary>The validator raises T0411 when an absent context parameter's focus type is incompatible.</summary>
    [TestMethod]
    public void ValidatorRejectsIncompatibleContext()
    {
        JsonataSignature signature = JsonataSignature.Parse("<s-:s>");

        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(
            () => JsonataSignatureValidator.Validate(signature, [], JsonataValue.Number(23)));

        Assert.AreEqual(CodeContextIncompatible, error.Code.ToString());
    }

    /// <summary>The validator singleton-wraps a scalar supplied to an array parameter into a one-element plain array.</summary>
    [TestMethod]
    public void ValidatorSingletonWrapsScalar()
    {
        JsonataSignature signature = JsonataSignature.Parse("<a:n>");

        JsonataValue[] validated = JsonataSignatureValidator.Validate(signature, [JsonataValue.String("x")], JsonataValue.Undefined);

        Assert.HasCount(1, validated);
        Assert.AreEqual(JsonataValueKind.Array, validated[0].Kind);
        Assert.HasCount(1, validated[0].AsArray);
        Assert.AreEqual("x", validated[0].AsArray[0].AsString);
    }

    /// <summary>The validator raises T0410 when a supplied argument cannot be matched against the signature.</summary>
    [TestMethod]
    public void ValidatorRejectsArgumentMismatch()
    {
        JsonataSignature signature = JsonataSignature.Parse("<n-:n>");

        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(
            () => JsonataSignatureValidator.Validate(signature, [JsonataValue.String("x")], JsonataValue.Undefined));

        Assert.AreEqual(CodeArgumentMismatch, error.Code.ToString());
    }

    /// <summary>The validator raises T0412 when an array element's symbol is not the parameter's element subtype.</summary>
    [TestMethod]
    public void ValidatorRejectsWrongElementSubtype()
    {
        JsonataSignature signature = JsonataSignature.Parse("<a<n>:n>");
        JsonataValue mixed = JsonataValue.Array([JsonataValue.Number(1), JsonataValue.String("2")]);

        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(
            () => JsonataSignatureValidator.Validate(signature, [mixed], JsonataValue.Undefined));

        Assert.AreEqual(CodeWrongElementType, error.Code.ToString());
    }

    /// <summary>An empty signature passes the supplied arguments through unchanged.</summary>
    [TestMethod]
    public void ValidatorPassesEmptySignatureThrough()
    {
        JsonataSignature signature = JsonataSignature.Parse("<>");

        JsonataValue[] validated = JsonataSignatureValidator.Validate(signature, [JsonataValue.Number(1)], JsonataValue.Undefined);

        Assert.HasCount(1, validated);
        Assert.AreEqual(1d, validated[0].AsNumber);
    }

    /// <summary>Context substitution over a string root makes <c>$uppercase()</c> upper-case the root.</summary>
    [TestMethod]
    public void EngineContextSubstitutionOverStringRoot()
    {
        Assert.AreEqual("ROOT", Evaluate("$uppercase()", "\"root\"").AsString);
    }

    /// <summary>Context substitution over a number root makes <c>$uppercase()</c> raise T0411 (a number is not a compatible string context).</summary>
    [TestMethod]
    public void EngineContextIncompatibleRaisesT0411()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$uppercase()", "23"));

        Assert.AreEqual(CodeContextIncompatible, error.Code.ToString());
    }

    /// <summary>The singleton-wrap lets <c>$count("x")</c> count the wrapped one-element array as 1.</summary>
    [TestMethod]
    public void EngineSingletonWrapCountsOne()
    {
        Assert.AreEqual(1d, Evaluate("$count(\"x\")", "{}").AsNumber);
    }

    /// <summary>A wrong-typed argument raises T0410: <c>$abs("x")</c> cannot match the number parameter.</summary>
    [TestMethod]
    public void EngineWrongTypeRaisesT0410()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$abs(\"x\")", "{}"));

        Assert.AreEqual(CodeArgumentMismatch, error.Code.ToString());
    }

    /// <summary>A surplus argument raises T0410: <c>$boolean(2, 3)</c> supplies one more value than the signature accepts.</summary>
    [TestMethod]
    public void EngineSurplusArgumentRaisesT0410()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$boolean(2, 3)", "{}"));

        Assert.AreEqual(CodeArgumentMismatch, error.Code.ToString());
    }

    /// <summary>A non-numeric array element raises T0412: <c>$sum([1, "2"])</c> violates the number element subtype.</summary>
    [TestMethod]
    public void EngineWrongElementSubtypeRaisesT0412()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$sum([1, \"2\"])", "{}"));

        Assert.AreEqual(CodeWrongElementType, error.Code.ToString());
    }

    /// <summary>Evaluates a JSONata expression against a JSON input document, parsing input through the host adapter.</summary>
    /// <param name="expression">The JSONata expression source.</param>
    /// <param name="inputJson">The input JSON document text.</param>
    /// <returns>The normalized result value.</returns>
    private static JsonataValue Evaluate(string expression, string inputJson)
    {
        JsonNode input = StjJsonAdapter.Parse(new Utf8String(Encoding.UTF8.GetBytes(inputJson)));

        return JsonataEngine.Evaluate(Encoding.UTF8.GetBytes(expression), input);
    }
}
