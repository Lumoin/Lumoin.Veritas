using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Conformance;

[TestClass]
internal sealed class W3cManifestLoaderTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void LoadsEmptyManifestAsZeroTests()
    {
        string path = W3cCorpusPath.FixturePath("empty-manifest.ttl");

        W3cManifest manifest = W3cManifestLoader.Load(path);

        Assert.IsEmpty(manifest.Tests);
    }

    [TestMethod]
    public void LoadsManifestWithSingleTest()
    {
        string path = W3cCorpusPath.FixturePath("single-test-manifest.ttl");

        W3cManifest manifest = W3cManifestLoader.Load(path);

        Assert.HasCount(1, manifest.Tests);
        Assert.AreEqual("single test", manifest.Tests[0].Name);
        Assert.AreEqual(W3cTestType.PositiveSyntax, manifest.Tests[0].Type);
    }

    [TestMethod]
    public void LoadsManifestWithMultipleTestsInOrder()
    {
        string path = W3cCorpusPath.FixturePath("multi-test-manifest.ttl");

        W3cManifest manifest = W3cManifestLoader.Load(path);

        Assert.HasCount(5, manifest.Tests);
        Assert.AreEqual("positive syntax one", manifest.Tests[0].Name);
        Assert.AreEqual("negative syntax two", manifest.Tests[1].Name);
        Assert.AreEqual("evaluation three", manifest.Tests[2].Name);
        Assert.AreEqual("negative evaluation four", manifest.Tests[3].Name);
        Assert.AreEqual("unknown test type", manifest.Tests[4].Name);
    }

    [TestMethod]
    public void LoadsAllFourTestTypes()
    {
        string path = W3cCorpusPath.FixturePath("multi-test-manifest.ttl");

        W3cManifest manifest = W3cManifestLoader.Load(path);

        Assert.AreEqual(W3cTestType.PositiveSyntax, manifest.Tests[0].Type);
        Assert.AreEqual(W3cTestType.NegativeSyntax, manifest.Tests[1].Type);
        Assert.AreEqual(W3cTestType.Evaluation, manifest.Tests[2].Type);
        Assert.AreEqual(W3cTestType.NegativeEvaluation, manifest.Tests[3].Type);
    }

    [TestMethod]
    public void UnknownTestTypeBecomesUnknownEnum()
    {
        string path = W3cCorpusPath.FixturePath("multi-test-manifest.ttl");

        W3cManifest manifest = W3cManifestLoader.Load(path);

        W3cTestCase unknown = manifest.Tests.Single(t => t.Name == "unknown test type");
        Assert.AreEqual(W3cTestType.Unknown, unknown.Type);
        Assert.AreEqual("http://example.org/unknown-test-type", unknown.RawTypeIri);
    }

    [TestMethod]
    public void RelativePathsResolveAgainstManifestLocation()
    {
        string path = W3cCorpusPath.FixturePath("single-test-manifest.ttl");

        W3cManifest manifest = W3cManifestLoader.Load(path);

        string inputPath = manifest.Tests[0].InputPath;
        Assert.IsTrue(System.IO.File.Exists(inputPath), $"Input path should exist: {inputPath}");
    }

    [TestMethod]
    public void CommentAndNamePopulatedFromManifest()
    {
        string path = W3cCorpusPath.FixturePath("single-test-manifest.ttl");

        W3cManifest manifest = W3cManifestLoader.Load(path);

        Assert.AreEqual("single test", manifest.Tests[0].Name);
        Assert.AreEqual("A single positive-syntax test.", manifest.Tests[0].Comment);
    }

    [TestMethod]
    public void EvaluationTestCarriesExpectedPath()
    {
        string path = W3cCorpusPath.FixturePath("multi-test-manifest.ttl");

        W3cManifest manifest = W3cManifestLoader.Load(path);

        W3cTestCase eval = manifest.Tests.Single(t => t.Name == "evaluation three");
        Assert.IsNotNull(eval.ExpectedPath);
        Assert.IsTrue(System.IO.File.Exists(eval.ExpectedPath!), $"Expected fixture should exist: {eval.ExpectedPath}");
    }

    [TestMethod]
    public void PositiveSyntaxTestHasNullExpectedPath()
    {
        string path = W3cCorpusPath.FixturePath("multi-test-manifest.ttl");

        W3cManifest manifest = W3cManifestLoader.Load(path);

        W3cTestCase positive = manifest.Tests.Single(t => t.Name == "positive syntax one");
        Assert.IsNull(positive.ExpectedPath);
    }

    [TestMethod]
    public void LoadsShaclCorpusThroughRepeatedIncludes()
    {
        string path = W3cCorpusPath.For("Shacl", "data-shapes-test-suite/tests", "manifest.ttl");

        W3cManifest manifest = W3cManifestLoader.Load(path);

        //The corpus declares its leaves only through repeated mf:include
        //predicates down the per-directory manifests to each leaf's own
        //manifest; this guards against the manifest-driven suite silently
        //discovering nothing. Every sht:Validate entry must classify, name
        //itself (the leaves carry rdfs:label, not mf:name), and resolve a
        //shapes graph the runner can read.
        Assert.IsGreaterThan(100, manifest.Tests.Length);
        Assert.IsTrue(manifest.Tests.All(t => t.Type == W3cTestType.ShaclValidate), "Every vendored SHACL test should classify as ShaclValidate.");
        Assert.IsTrue(manifest.Tests.All(t => t.Name.Length > 0), "Every SHACL test should carry a name from rdfs:label.");
        Assert.IsTrue(manifest.Tests.All(t => t.ShapesGraphPath is not null), "Every SHACL test should carry a shapes graph.");
    }
}
