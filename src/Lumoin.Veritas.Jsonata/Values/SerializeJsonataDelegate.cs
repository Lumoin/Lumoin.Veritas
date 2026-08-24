using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Jsonata.Values;

/// <summary>
/// Serializes a JSONata result value to RFC 8259 UTF-8 JSON text. The named delegate is the
/// serialization seam (the project forbids bare <c>Func</c>/<c>Action</c>); the default
/// implementation is <see cref="JsonataJsonWriter.Serialize"/>.
/// </summary>
/// <remarks>
/// <para>
/// The top-level <see cref="JsonataValueKind.Undefined"/> value serializes to no bytes (it is the
/// "nothing" value and is omitted from output); a function value has no JSON representation and is
/// rejected.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/processing">the JSONata processing reference</see>.</para>
/// </remarks>
/// <param name="value">The value to serialize.</param>
/// <returns>The UTF-8 JSON text, empty for the undefined value.</returns>
public delegate Utf8String SerializeJsonataDelegate(JsonataValue value);
