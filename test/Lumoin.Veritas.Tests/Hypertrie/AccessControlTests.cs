using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class AccessControlTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task SynchronousAllowPolicyReturnsAllow()
    {
        AccessControlDelegate policy = static (request, ct) => ValueTask.FromResult(AccessDecision.Allow);

        AccessDecision decision = await policy(MakeRequest(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(AccessDecision.Allow, decision);
    }

    [TestMethod]
    public async Task SynchronousDenyPolicyReturnsDeny()
    {
        AccessControlDelegate policy = static (request, ct) => ValueTask.FromResult(AccessDecision.Deny);

        AccessDecision decision = await policy(MakeRequest(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(AccessDecision.Deny, decision);
    }

    [TestMethod]
    public async Task SynchronousNotFoundPolicyReturnsNotFound()
    {
        AccessControlDelegate policy = static (request, ct) => ValueTask.FromResult(AccessDecision.NotFound);

        AccessDecision decision = await policy(MakeRequest(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(AccessDecision.NotFound, decision);
    }

    [TestMethod]
    public async Task PolicyCanInspectTriple()
    {
        AccessControlDelegate policy = static (request, ct) =>
        {
            //Triples whose subject equals 99 are denied.
            AccessDecision result = request.Triple.Subject.Encoded == 99 ? AccessDecision.Deny : AccessDecision.Allow;

            return ValueTask.FromResult(result);
        };

        AccessDecision allowed = await policy(new(EncodedTriple.FromEncoded(1, 10, 100), new TestAccessContext("anyone")), TestContext.CancellationToken).ConfigureAwait(false);
        AccessDecision denied = await policy(new(EncodedTriple.FromEncoded(99, 10, 100), new TestAccessContext("anyone")), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(AccessDecision.Allow, allowed);
        Assert.AreEqual(AccessDecision.Deny, denied);
    }

    [TestMethod]
    public async Task PolicyCanCastContextToConcreteType()
    {
        AccessControlDelegate policy = static (request, ct) =>
        {
            //Pattern-match to the concrete context type and gate
            //on its content. This is the canonical PIC usage —
            //the library passes the abstract context through, the
            //policy owns the concrete shape.
            AccessDecision result = request.Context is TestAccessContext ctx && ctx.Subject == "alice"
                ? AccessDecision.Allow
                : AccessDecision.Deny;

            return ValueTask.FromResult(result);
        };

        AccessDecision allowed = await policy(new(EncodedTriple.FromEncoded(1, 2, 3), new TestAccessContext("alice")), TestContext.CancellationToken).ConfigureAwait(false);
        AccessDecision denied = await policy(new(EncodedTriple.FromEncoded(1, 2, 3), new TestAccessContext("mallory")), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(AccessDecision.Allow, allowed);
        Assert.AreEqual(AccessDecision.Deny, denied);
    }

    [TestMethod]
    public async Task PolicyHonoursCancellationToken()
    {
        AccessControlDelegate policy = static (request, ct) =>
        {
            ct.ThrowIfCancellationRequested();

            return ValueTask.FromResult(AccessDecision.Allow);
        };

        using CancellationTokenSource cts = new();
        await cts.CancelAsync().ConfigureAwait(false);

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await policy(MakeRequest(), cts.Token).ConfigureAwait(false)).ConfigureAwait(false);
    }

    [TestMethod]
    public void AccessRequestEqualityIsValueBased()
    {
        TestAccessContext context = new("subject");
        AccessRequest left = new(EncodedTriple.FromEncoded(1, 2, 3), context);
        AccessRequest right = new(EncodedTriple.FromEncoded(1, 2, 3), context);

        Assert.AreEqual(left, right);
    }

    [TestMethod]
    public void AccessRequestsWithDifferentTriplesCompareUnequal()
    {
        TestAccessContext context = new("subject");
        AccessRequest left = new(EncodedTriple.FromEncoded(1, 2, 3), context);
        AccessRequest right = new(EncodedTriple.FromEncoded(1, 2, 4), context);

        Assert.AreNotEqual(left, right);
    }

    private static AccessRequest MakeRequest()
    {
        return new(EncodedTriple.FromEncoded(1, 2, 3), new TestAccessContext("test"));
    }

    private sealed record TestAccessContext(string Subject): AccessContext;
}
