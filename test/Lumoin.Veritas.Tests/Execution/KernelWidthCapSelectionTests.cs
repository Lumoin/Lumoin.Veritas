using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Execution;

namespace Lumoin.Veritas.Tests.Execution;

/// <summary>
/// Threading <see cref="KernelWidthCap"/> into codec backend selection:
/// the cap narrows the capability ladder below the hardware's best —
/// <see cref="KernelWidthCap.Auto"/> is uncapped, <see cref="KernelWidthCap.Portable"/>
/// forces the scalar backend, and a vector cap admits only rungs at or
/// below it. Capability stays static and process-wide; the cap is policy
/// applied on top, so the assertions mirror the ladder against the
/// machine's actual support flags.
/// </summary>
[TestClass]
internal sealed class KernelWidthCapSelectionTests
{
    /// <summary>The best backend at or below the 128-bit rung the machine actually supports.</summary>
    /// <returns>The expected capped backend.</returns>
    private static ColumnarKernelBackend BestAtOrBelow128()
    {
        if(ColumnarWasmPackedSimdBackend.IsSupported)
        {
            return ColumnarWasmPackedSimdBackend.Backend;
        }

        if(ColumnarVector128Backend.IsSupported)
        {
            return ColumnarVector128Backend.Backend;
        }

        return ColumnarPortableBackend.Backend;
    }

    [TestMethod]
    public void AutoCapReturnsTheUncappedDefaultBackend()
    {
        Assert.AreEqual(ColumnarKernelBackend.Default, ColumnarKernelBackend.ForCap(KernelWidthCap.Auto));
    }

    [TestMethod]
    public void PortableCapForcesTheScalarBackend()
    {
        Assert.AreEqual(ColumnarPortableBackend.Backend, ColumnarKernelBackend.ForCap(KernelWidthCap.Portable));
    }

    [TestMethod]
    public void Bits128CapExcludesThe256BitRung()
    {
        ColumnarKernelBackend capped = ColumnarKernelBackend.ForCap(KernelWidthCap.Bits128);

        Assert.AreEqual(BestAtOrBelow128(), capped);

        //The cap's whole purpose: on a 256-capable machine it must not pick the 256-bit rung.
        if(ColumnarVector256Backend.IsSupported)
        {
            Assert.AreNotEqual(ColumnarVector256Backend.Backend, capped);
        }
    }

    [TestMethod]
    public void Bits256CapAdmitsThe256BitRungWhereSupported()
    {
        ColumnarKernelBackend capped = ColumnarKernelBackend.ForCap(KernelWidthCap.Bits256);

        if(ColumnarVector256Backend.IsSupported)
        {
            Assert.AreEqual(ColumnarVector256Backend.Backend, capped);
        }
        else
        {
            //Without 256-bit hardware the cap falls to the best supported narrower rung.
            Assert.AreEqual(BestAtOrBelow128(), capped);
        }
    }
}
