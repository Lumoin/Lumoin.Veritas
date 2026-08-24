using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Persistence.Segment;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// Pins the scalar-overflow discipline of the dictionary verify seam: the block-count ceiling division runs
/// in long, so adversarial term-count/block-term-count scalars can neither wrap the geometry negative (an
/// OverflowException escaping the documented InvalidDataException contract) nor collapse it to a zero-block
/// parse that verifies clean an image the load path rejects. Malformed geometry always surfaces as a refusal
/// or an invalid front-matter verdict — never a wrong-clean report — matching the sibling
/// <see cref="ItemSegment.RunVerifyRound"/> guard, which the parity pin below holds in place.
/// </summary>
[TestClass]
internal sealed class DictionaryVerifyOverflowProbeTests
{
    /// <summary>Header (19) + dictionary epoch (8): the byte offset of the term-count scalar.</summary>
    private const int TermCountOffset = 19 + 8;

    /// <summary>Header (19) + epoch (8) + term count (4): the byte offset of the block-term-count scalar.</summary>
    private const int BlockTermCountOffset = 19 + 12;

    /// <summary>Header (19): the byte offset of the item-segment item-count scalar.</summary>
    private const int ItemCountOffset = 19;

    /// <summary>Header (19) + item count (4): the byte offset of the item-segment block-item-count scalar.</summary>
    private const int BlockItemCountOffset = 19 + 4;

    /// <summary>A small, well-formed dictionary image (2-term blocks over 4 terms) written under XxHash3.</summary>
    /// <returns>The image bytes.</returns>
    private static byte[] BuildDictionaryImage()
    {
        TermDictionary dictionary = new(epoch: 0x1234);
        for(uint i = 0; i < 4; i++)
        {
            dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(string.Create(CultureInfo.InvariantCulture, $"http://example.org/term/{i}"))));
        }

        DictionarySegment segment = new(dictionary, blockTermCount: 2);
        int size = segment.ComputeSerializedSize(ChecksumAlgorithm.XxHash3);
        byte[] image = new byte[size];
        segment.WriteTo(image, ChecksumAlgorithm.XxHash3);

        return image;
    }

    /// <summary>A small, well-formed item-segment image (10-item blocks over 5 triples) written under XxHash3.</summary>
    /// <returns>The image bytes.</returns>
    private static byte[] BuildItemSegmentImage()
    {
        EncodedTriple[] triples = PersistenceStagingFixture.SampleTriples(5);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        int size = (int)segment.ComputeSerializedSize(ChecksumAlgorithm.XxHash3);
        byte[] image = new byte[size];
        segment.WriteTo(image, ChecksumAlgorithm.XxHash3);

        return image;
    }

    /// <summary>Scalars near int.MaxValue produce a huge-but-positive block count under the long ceiling
    /// division, whose front matter cannot fit the image, so the truncation guard refuses with the documented
    /// InvalidDataException — the exception the scrub's catch records as a finding — and never an
    /// OverflowException from a wrapped-negative geometry.</summary>
    [TestMethod]
    public void DictionaryVerifyAdversarialScalarsRefusesWithInvalidData()
    {
        byte[] image = BuildDictionaryImage();
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(TermCountOffset), int.MaxValue);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(BlockTermCountOffset), 3);

        Exception? caught = null;
        try
        {
            DictionarySegment.RunVerifyRound(image);
        }
        catch(Exception ex)
        {
            caught = ex;
        }

        Assert.IsNotNull(caught, "RunVerifyRound must surface the malformed geometry rather than read out of range silently.");
        Assert.IsInstanceOfType<InvalidDataException>(caught, $"Malformed geometry refuses within the documented contract, inside the scrub's catch. Actual: {caught.GetType().Name} — {caught.Message}");
    }

    /// <summary>The sibling divergence, empirically: the same adversarial scalar pattern on an item segment does
    /// NOT throw OverflowException — the (long) cast at ItemSegment.cs:389 keeps the block count in range, so the
    /// truncation guard fires and the method refuses with InvalidDataException, the documented contract.</summary>
    [TestMethod]
    public void ItemSegmentVerifyAdversarialScalarsRefusesWithInvalidDataNotOverflow()
    {
        byte[] image = BuildItemSegmentImage();
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(ItemCountOffset), int.MaxValue);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(BlockItemCountOffset), 3);

        Exception? caught = null;
        try
        {
            ItemSegment.RunVerifyRound(image);
        }
        catch(Exception ex)
        {
            caught = ex;
        }

        Assert.IsNotInstanceOfType<OverflowException>(caught, $"The sibling's (long) cast must prevent the overflow the dictionary path hits. Actual: {caught?.GetType().Name}");
        Assert.IsInstanceOfType<InvalidDataException>(caught, $"The sibling refuses the malformed geometry with the documented InvalidDataException. Actual: {caught?.GetType().Name} — {caught?.Message}");
    }

    /// <summary>Scalars that once wrapped the int block count to zero now parse as a small positive geometry
    /// under the long division; the patched image can then never verify wholly clean — it either refuses within
    /// the documented contract or reports its front matter invalid (the trailer no longer covers the patched
    /// scalars) — so a declared multi-billion-term dictionary is never a clean zero-block report.</summary>
    [TestMethod]
    public void DictionaryVerifyHugeScalarsNeverVerifyClean()
    {
        byte[] image = BuildDictionaryImage();
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(TermCountOffset), 2_000_000_000);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(BlockTermCountOffset), 2_000_000_000);

        ArtifactVerifyReport? report = null;
        Exception? caught = null;
        try
        {
            report = DictionarySegment.RunVerifyRound(image);
        }
        catch(Exception ex)
        {
            caught = ex;
        }

        if(caught is not null)
        {
            Assert.IsInstanceOfType<InvalidDataException>(caught, $"A refusal must stay within the documented contract. Actual: {caught.GetType().Name} — {caught.Message}");

            return;
        }

        Assert.IsNotNull(report);
        Assert.IsTrue(report.HasFrontMatterChecksum, "The staged image carries a front-matter trailer.");
        Assert.IsFalse(report.FrontMatterValid, "The trailer does not cover the patched scalars, so the report surfaces the damage rather than verifying clean.");
    }
}
