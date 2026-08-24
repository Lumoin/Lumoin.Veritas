namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// Opens one incremental checksum session for a windowed verification. The factory is an
/// algorithm's OPTIONAL streaming capability: an algorithm that carries none can only verify
/// artifacts that fit a single span, and a verification that needs more fails closed rather than
/// passing unverified. Both built-in algorithms carry a factory; a host-composed keyed algorithm
/// supplies one built over an incremental keyed hash, closed over its key at the composition root
/// exactly as its one-shot compute is.
/// </summary>
/// <returns>The fresh session; the caller disposes it.</returns>
public delegate ChecksumSession CreateChecksumSessionDelegate();
