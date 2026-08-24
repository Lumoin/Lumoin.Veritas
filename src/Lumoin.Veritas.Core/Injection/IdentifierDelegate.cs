using System;

namespace Lumoin.Veritas.Core;

/// <summary>
/// Produces a fresh identifier for components that would otherwise call
/// <see cref="Guid.NewGuid"/> directly (session ids, correlation ids). Injecting it
/// makes those identities deterministic-when-wanted and observable, and keeps
/// <see cref="Guid.NewGuid"/> confined to <see cref="VeritasIdentifiers"/>.
/// Defaults live in <see cref="VeritasIdentifiers"/>.
/// </summary>
/// <param name="request">What the caller is asking for.</param>
/// <returns>The identifier.</returns>
public delegate Guid IdentifierDelegate(in IdentifierRequest request);
