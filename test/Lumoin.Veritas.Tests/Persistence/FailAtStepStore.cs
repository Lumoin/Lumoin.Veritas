using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;

namespace Lumoin.Veritas.Tests.Persistence;

/// <summary>
/// The publish step a <see cref="FailAtStepStore"/> crashes before, modelling a torn publish at a specific
/// point in the manifest-commit protocol (stage manifest → stage CURRENT pointer → atomic rename → retained
/// copy). Each step leaves the prior committed generation wholly in force; they differ only in which staged
/// artifacts the crash leaves behind.
/// </summary>
internal enum PublishFailStep
{
    /// <summary>Crash before the CURRENT pointer is staged: the new manifest is staged (an orphan), but no CURRENT staging is written and the rename never runs.</summary>
    BeforeCurrentStaging,

    /// <summary>Crash before the atomic rename: the new manifest and the CURRENT staging are both written, but the rename that makes the generation live never happens.</summary>
    BeforeRename,
}

/// <summary>
/// A <see cref="PersistenceStore"/> decorator that throws at a named publish step to model a crash at the
/// single commit point: the operations that precede the failing step are forwarded, then it fails, so the
/// generation is never committed and the steps after never run. This generalizes the per-test publish-crash
/// decorators the manifest and sketch-round suites each carried into one shared injector keyed by the step.
/// </summary>
internal sealed class FailAtStepStore: PersistenceStore
{
    /// <summary>The real store the surviving operations are forwarded to.</summary>
    private readonly PersistenceStore inner;

    /// <summary>The publish step this store fails before.</summary>
    private readonly PublishFailStep failStep;

    /// <summary>Creates a decorator over <paramref name="inner"/> that crashes before <paramref name="failStep"/>.</summary>
    /// <param name="inner">The real store the surviving operations are forwarded to.</param>
    /// <param name="failStep">The publish step the decorator crashes before.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    internal FailAtStepStore(PersistenceStore inner, PublishFailStep failStep)
    {
        ArgumentNullException.ThrowIfNull(inner);

        this.inner = inner;
        this.failStep = failStep;
    }

    /// <inheritdoc/>
    public override void WriteStaged(string name, ReadOnlySpan<byte> content)
    {
        if(failStep == PublishFailStep.BeforeCurrentStaging && name == ManifestNaming.CurrentStagingName)
        {
            throw new IOException("Simulated crash before the CURRENT pointer was staged.");
        }

        inner.WriteStaged(name, content);
    }

    /// <inheritdoc/>
    public override void Publish(string stagedName, string finalName)
    {
        if(failStep == PublishFailStep.BeforeRename)
        {
            throw new IOException("Simulated crash at the commit point before the CURRENT rename.");
        }

        inner.Publish(stagedName, finalName);
    }

    /// <inheritdoc/>
    public override byte[]? Read(string name)
    {
        return inner.Read(name);
    }

    /// <inheritdoc/>
    public override SegmentImageSource? OpenImage(string name)
    {
        return inner.OpenImage(name);
    }

    /// <inheritdoc/>
    public override PooledSegmentImageSource? OpenPooledImage(string name, MemoryPool<byte> pool)
    {
        return inner.OpenPooledImage(name, pool);
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> List(string prefix)
    {
        return inner.List(prefix);
    }

    /// <inheritdoc/>
    public override void Delete(string name)
    {
        inner.Delete(name);
    }
}
