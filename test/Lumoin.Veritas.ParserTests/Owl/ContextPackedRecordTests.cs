using System;
using System.Collections.Generic;
using Lumoin.Veritas.Owl.Contexts;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The per-id boolean planes of a bare <see cref="Context"/>, driven through the
/// public accessors that own them. The liveness row pins the plane an insert
/// appends to and a tombstone clears, together with the live count that rides it.
/// The push-provenance and origin-bit rows pin the two lazily written tag planes:
/// an untagged default, a tag at a high id that leaves every neighbouring id
/// untagged, and a tag count that moves only on a flip, so a repeated tag and a
/// clear of an id the record does not cover leave it exact. The broadcast row pins
/// the third plane's separate index space — it is keyed by an image's position in
/// the engine's broadcast list, so advancing the clause-id space leaves it
/// untouched.
/// </summary>
[TestClass]
internal sealed class ContextPackedRecordTests
{
    /// <summary>The clause origin marker the fixtures stamp; the origin value is inert for the planes under test.</summary>
    private const int DerivedOrigin = -1;

    /// <summary>The first concept-atom id the fixture clauses head.</summary>
    private const int FirstAtom = 5;

    /// <summary>The second concept-atom id.</summary>
    private const int SecondAtom = 7;

    /// <summary>The third concept-atom id.</summary>
    private const int ThirdAtom = 9;

    /// <summary>An insert makes its id live and the live count follows; a tombstone clears exactly that id's bit and leaves its neighbours live.</summary>
    [TestMethod]
    public void ContextLivenessTracksInsertAndTombstone()
    {
        Context context = new(0, Array.Empty<DlLiteral>(), isRoot: false, -1, new HashSet<int>());

        int firstId = context.Insert(DlClause.Create([], [DlLiteral.Concept(FirstAtom, DlTerm.Central)], DerivedOrigin), isPredEligible: false, decidedUnderNoChoice: true, [0]);
        int secondId = context.Insert(DlClause.Create([], [DlLiteral.Concept(SecondAtom, DlTerm.Central)], DerivedOrigin), isPredEligible: false, decidedUnderNoChoice: true, [0]);
        int thirdId = context.Insert(DlClause.Create([], [DlLiteral.Concept(ThirdAtom, DlTerm.Central)], DerivedOrigin), isPredEligible: false, decidedUnderNoChoice: true, [0]);

        Assert.IsTrue(context.IsLive(firstId), "The first inserted clause is live.");
        Assert.IsTrue(context.IsLive(secondId), "The second inserted clause is live.");
        Assert.IsTrue(context.IsLive(thirdId), "The third inserted clause is live.");
        Assert.AreEqual(3, context.LiveCount, "Every inserted clause counts as live.");

        context.Tombstone(secondId);

        Assert.IsTrue(context.IsLive(firstId), "The clause below the tombstoned id stays live.");
        Assert.IsFalse(context.IsLive(secondId), "The tombstoned clause is not live.");
        Assert.IsTrue(context.IsLive(thirdId), "The clause above the tombstoned id stays live.");
        Assert.AreEqual(2, context.LiveCount, "The tombstone retires exactly one clause.");
    }

    /// <summary>A fresh context reads untagged at every id, and a tag at a high id lands there alone — the ids the record's extension spans stay untagged.</summary>
    [TestMethod]
    public void ContextPushProvenanceDefaultsUntaggedAndTagsExactly()
    {
        Context context = new(0, Array.Empty<DlLiteral>(), isRoot: false, -1, new HashSet<int>());

        Assert.IsFalse(context.IsPushed(0), "A fresh context reads untagged at the first id.");
        Assert.IsFalse(context.IsPushed(63), "A fresh context reads untagged at the first word's last id.");
        Assert.IsFalse(context.IsPushed(64), "A fresh context reads untagged at the second word's first id.");
        Assert.IsFalse(context.IsPushed(4_096), "A fresh context reads untagged far above its clause ids.");

        context.SetPushed(4_096);

        Assert.IsTrue(context.IsPushed(4_096), "The tagged id reads tagged.");
        Assert.IsFalse(context.IsPushed(0), "The first id the extension spans stays untagged.");
        Assert.IsFalse(context.IsPushed(63), "The id at the first word's end stays untagged.");
        Assert.IsFalse(context.IsPushed(64), "The id at the second word's start stays untagged.");
        Assert.IsFalse(context.IsPushed(4_095), "The id below the tagged one stays untagged.");
        Assert.IsFalse(context.IsPushed(4_097), "The id above the tagged one reads untagged.");
    }

    /// <summary>The broadcast record answers by an image's position in the broadcast list, an index space of its own: a fresh context holds nothing, a recorded position is held alone, and advancing the clause-id space leaves position zero unheld.</summary>
    [TestMethod]
    public void ContextBroadcastRecordDefaultsUnheldAndRecordsByPosition()
    {
        Context context = new(0, Array.Empty<DlLiteral>(), isRoot: false, -1, new HashSet<int>());

        foreach(int position in (int[])[0, 63, 64, 4_095, 4_096, 4_097])
        {
            Assert.IsFalse(context.HoldsBroadcastImage(position), $"A fresh context holds no image at position {position}.");
        }

        context.RecordBroadcastImageHeld(4_096);

        Assert.IsTrue(context.HoldsBroadcastImage(4_096), "The recorded position is held.");
        Assert.IsFalse(context.HoldsBroadcastImage(0), "The first position the extension spans stays unheld.");
        Assert.IsFalse(context.HoldsBroadcastImage(4_095), "The position below the recorded one stays unheld.");
        Assert.IsFalse(context.HoldsBroadcastImage(4_097), "The position above the recorded one reads unheld.");

        context.Insert(DlClause.Create([], [DlLiteral.Concept(FirstAtom, DlTerm.Central)], DerivedOrigin), isPredEligible: false, decidedUnderNoChoice: true, [0]);

        Assert.IsFalse(context.HoldsBroadcastImage(0), "Advancing the clause-id space records no broadcast position.");
    }

    /// <summary>The origin-bit tag latch follows the tag population and moves only on a flip: a repeated tag leaves it set once, the clear that retires the last tag drops it, a further clear at a tagged-record id or an id the record does not cover leaves it dropped, and a fresh tag after those clears raises it again — so no clear retreats the count past zero.</summary>
    [TestMethod]
    public void ContextChoiceTagCountTracksFlipsOnly()
    {
        Context context = new(0, Array.Empty<DlLiteral>(), isRoot: false, -1, new HashSet<int>());

        Assert.IsFalse(context.HasDerivedUnderChoiceTags, "A fresh context carries no origin-bit tag.");

        context.SetDerivedUnderChoice(5);

        Assert.IsTrue(context.HasDerivedUnderChoiceTags, "The first tag raises the latch.");

        context.SetDerivedUnderChoice(5);

        Assert.IsTrue(context.HasDerivedUnderChoiceTags, "A repeated tag on the same id leaves the latch raised.");

        context.ClearDerivedUnderChoice(5);

        Assert.IsFalse(context.HasDerivedUnderChoiceTags, "Clearing the only tag drops the latch.");

        context.ClearDerivedUnderChoice(5);

        Assert.IsFalse(context.HasDerivedUnderChoiceTags, "A repeated clear on the same id leaves the latch dropped.");

        context.ClearDerivedUnderChoice(9_999);

        Assert.IsFalse(context.HasDerivedUnderChoiceTags, "A clear at an id the record does not cover leaves the latch dropped.");

        context.SetDerivedUnderChoice(200);

        Assert.IsTrue(context.HasDerivedUnderChoiceTags, "A tag after the clears raises the latch again, so no clear retreated the count past zero.");
    }

    /// <summary>Tags at distinct ids are independent: clearing one leaves the other tagged and the latch raised.</summary>
    [TestMethod]
    public void ContextChoiceTagsSurviveAcrossDistinctIds()
    {
        Context context = new(0, Array.Empty<DlLiteral>(), isRoot: false, -1, new HashSet<int>());

        context.SetDerivedUnderChoice(5);
        context.SetDerivedUnderChoice(200);
        context.ClearDerivedUnderChoice(5);

        Assert.IsTrue(context.HasDerivedUnderChoiceTags, "The surviving tag keeps the latch raised.");
        Assert.IsTrue(context.IsDerivedUnderChoice(200), "The untouched id stays tagged.");
        Assert.IsFalse(context.IsDerivedUnderChoice(5), "The cleared id reads untagged.");
    }
}
