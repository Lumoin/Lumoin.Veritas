using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Ast;
using Lumoin.Veritas.Jsonata.Functions;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Execution;

/// <summary>
/// A user-defined function value: the closure produced when a <see cref="LambdaExpression"/> is evaluated.
/// The captured binding frame and the captured focus are taken at definition time and stored here rather
/// than captured by a C# delegate, so the body is later evaluated against exactly the environment the
/// lambda was defined in. The optional <see cref="Signature"/> is the parsed type signature an application
/// validates its arguments against before binding them. The value is carried in the
/// <see cref="JsonataValue.Function(object)"/> slot.
/// </summary>
/// <param name="Parameters">The parameter names in declaration order, each without the leading <c>$</c>; an argument is bound to each by position.</param>
/// <param name="Body">The body expression evaluated when the lambda is applied.</param>
/// <param name="CapturedFrame">The binding frame snapshotted at definition; a call evaluates the body in a fresh child of this frame, so the body resolves the variables the lambda was defined among (recursion included).</param>
/// <param name="CapturedFocus">The focus <c>$</c> snapshotted at definition; the body evaluates against this focus, so a bare <c>$</c> in the body is the definition-time input, not the call-site argument.</param>
/// <param name="Signature">The parsed type signature the lambda declared (<c>function(...)&lt;sig&gt;{...}</c>), or <see langword="null"/> when it declared none; an application runs it through <see cref="JsonataSignatureValidator"/> to resolve the effective arguments (context substitution, singleton-wrapping, and the T0410/T0411/T0412 checks) before binding.</param>
/// <remarks>See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
internal sealed record JsonataLambda(
    IReadOnlyList<Utf8String> Parameters,
    JsonataExpression Body,
    JsonataBindingFrame CapturedFrame,
    JsonataValue CapturedFocus,
    JsonataSignature? Signature);
