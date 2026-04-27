using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class VariableRegistryTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void NewRegistryIsEmpty()
    {
        VariableRegistry registry = new();

        Assert.AreEqual(0, registry.Count);
    }

    [TestMethod]
    public void GetOrAddRegistersFirstTimeAndReturnsSameOnRepeat()
    {
        VariableRegistry registry = new();

        Variable first = registry.GetOrAdd("person");
        Variable second = registry.GetOrAdd("person");

        Assert.AreEqual(first, second);
        Assert.AreEqual(1, registry.Count);
    }

    [TestMethod]
    public void GetOrAddAssignsDistinctIdsToDistinctNames()
    {
        VariableRegistry registry = new();

        Variable person = registry.GetOrAdd("person");
        Variable friend = registry.GetOrAdd("friend");
        Variable city = registry.GetOrAdd("city");

        Assert.AreNotEqual(person, friend);
        Assert.AreNotEqual(person, city);
        Assert.AreNotEqual(friend, city);
        Assert.AreEqual(3, registry.Count);
    }

    [TestMethod]
    public void IdsStartAtZeroAndIncrement()
    {
        VariableRegistry registry = new();

        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");

        Assert.AreEqual(0, a.Id);
        Assert.AreEqual(1, b.Id);
        Assert.AreEqual(2, c.Id);
    }

    [TestMethod]
    public void GetNameReturnsRegisteredName()
    {
        VariableRegistry registry = new();
        Variable variable = registry.GetOrAdd("inquirer");

        string name = registry.GetName(variable);

        Assert.AreEqual("inquirer", name);
    }

    [TestMethod]
    public void TryGetReturnsTrueForRegisteredName()
    {
        VariableRegistry registry = new();
        Variable registered = registry.GetOrAdd("answer");

        bool found = registry.TryGet("answer", out Variable retrieved);

        Assert.IsTrue(found);
        Assert.AreEqual(registered, retrieved);
    }

    [TestMethod]
    public void TryGetReturnsFalseForUnregisteredName()
    {
        VariableRegistry registry = new();

        bool found = registry.TryGet("missing", out Variable retrieved);

        Assert.IsFalse(found);
        Assert.AreEqual(default, retrieved);
    }

    [TestMethod]
    public void NamesAreCaseSensitive()
    {
        VariableRegistry registry = new();

        Variable lower = registry.GetOrAdd("x");
        Variable upper = registry.GetOrAdd("X");

        Assert.AreNotEqual(lower, upper);
        Assert.AreEqual(2, registry.Count);
    }

    [TestMethod]
    public void GetOrAddRejectsNullName()
    {
        VariableRegistry registry = new();

        Assert.Throws<ArgumentException>(() => registry.GetOrAdd(null!));
    }

    [TestMethod]
    public void GetOrAddRejectsEmptyName()
    {
        VariableRegistry registry = new();

        Assert.Throws<ArgumentException>(() => registry.GetOrAdd(string.Empty));
    }

    [TestMethod]
    public void GetOrAddRejectsWhitespaceName()
    {
        VariableRegistry registry = new();

        Assert.Throws<ArgumentException>(() => registry.GetOrAdd("   "));
    }

    [TestMethod]
    public void GetNameThrowsForUnknownVariable()
    {
        VariableRegistry registry = new();
        registry.GetOrAdd("only-one");

        Variable unknown = new(99);

        Assert.Throws<ArgumentOutOfRangeException>(() => registry.GetName(unknown));
    }
}
