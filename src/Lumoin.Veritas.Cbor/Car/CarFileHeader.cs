using System.Collections.Generic;
using CidValue = Lumoin.Veritas.Cid.Cid;

namespace Lumoin.Veritas.Cbor.Car;

/// <summary>
/// The header of a CARv1 file: the wire version (always <c>1</c> for
/// CARv1) and the list of root CIDs identifying the top of the DAG the
/// CAR file carries. See the IPLD CARv1 specification.
/// </summary>
/// <seealso href="https://ipld.io/specs/transport/car/carv1/"/>
public sealed record CarFileHeader(long Version, IReadOnlyList<CidValue> Roots);
