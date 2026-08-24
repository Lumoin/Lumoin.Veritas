using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Execution;

/// <summary>
/// The evaluation environment a node is interpreted under: the focus <c>$</c>, the root <c>$$</c>, and the
/// current binding <see cref="Frame"/> the named variables <c>$name</c> resolve against.
/// </summary>
/// <remarks>
/// <para>
/// The dot/map and predicate operators rebind the focus per item, so the context is carried per work-item
/// on the explicit work stack rather than as one global; a child is derived by <see cref="WithFocus"/>,
/// which rebinds <see cref="Focus"/> while inheriting <see cref="Root"/> and <see cref="Frame"/> (focus
/// rebinding does not open a new binding scope). A block opens a new scope with <see cref="WithFrame"/>,
/// which keeps the same focus but swaps in a child binding frame, and a lambda call rebinds both at once
/// with <see cref="EnterLambda"/>.
/// </para>
/// <para>
/// The context carries no nesting counter: the evaluator is iterative, so deep evaluation grows the
/// explicit work stack rather than the call stack, and evaluation is bounded by the work-stack depth
/// (<see cref="JsonataLimits.MaxEvaluationDepth"/>) and the step budget
/// (<see cref="JsonataLimits.MaxEvaluationSteps"/>). A tail call leaves no pending work frame, so a
/// tail-recursive expression runs in constant work-stack depth and is bounded only by the step budget,
/// while a non-tail recursion grows the work stack and is bounded by its depth.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</para>
/// </remarks>
internal sealed class JsonataContext
{
    /// <summary>Initializes a context.</summary>
    /// <param name="focus">The current focus value <c>$</c>.</param>
    /// <param name="root">The input document root <c>$$</c>.</param>
    /// <param name="frame">The current binding frame the named variables resolve against.</param>
    /// <param name="evaluationMillis">The single instant captured once at the top of the evaluation, as integer epoch-milliseconds (UTC).</param>
    /// <param name="randomness">The single randomness source captured once at the top of the evaluation that the entropy built-in <c>$shuffle</c> draws from.</param>
    private JsonataContext(JsonataValue focus, JsonataValue root, JsonataBindingFrame frame, long evaluationMillis, RandomnessDelegate randomness)
    {
        Focus = focus;
        Root = root;
        Frame = frame;
        EvaluationMillis = evaluationMillis;
        Randomness = randomness;
    }

    /// <summary>Gets the current focus value <c>$</c> the node evaluates against.</summary>
    public JsonataValue Focus { get; }

    /// <summary>Gets the top-level input document <c>$$</c>, constant down the whole evaluation.</summary>
    public JsonataValue Root { get; }

    /// <summary>Gets the current binding frame the named variables <c>$name</c> resolve against; a block swaps in a child frame at entry.</summary>
    public JsonataBindingFrame Frame { get; }

    /// <summary>
    /// Gets the single instant captured once at the top of the evaluation, as integer epoch-milliseconds
    /// (UTC). It is constant for the whole evaluation — every derived context shares it — so the date
    /// built-ins <c>$now</c> and <c>$millis</c> read the same instant however many times they are evaluated.
    /// </summary>
    public long EvaluationMillis { get; }

    /// <summary>
    /// Gets the single randomness source captured once at the top of the evaluation, threaded unchanged
    /// through every derived context, so the entropy built-in <c>$shuffle</c> draws its swap indices from the
    /// caller-injected source rather than from a global generator. A fixed
    /// <see cref="RandomnessDelegate"/> makes <c>$shuffle</c> deterministic under test.
    /// </summary>
    public RandomnessDelegate Randomness { get; }

    /// <summary>Creates the root context: focus and root both the input document, a fresh root binding frame.</summary>
    /// <param name="input">The input document.</param>
    /// <param name="evaluationMillis">The instant captured once at the top of the evaluation, as integer epoch-milliseconds (UTC).</param>
    /// <param name="randomness">The randomness source captured once at the top of the evaluation that <c>$shuffle</c> draws from.</param>
    /// <returns>The root context.</returns>
    public static JsonataContext ForInput(JsonataValue input, long evaluationMillis, RandomnessDelegate randomness)
    {
        return new JsonataContext(input, input, JsonataBindingFrame.CreateRoot(), evaluationMillis, randomness);
    }

    /// <summary>
    /// Derives a child context for one item: a new focus, the same root and binding frame. Focus rebinding
    /// does not open a new binding scope, so the frame is inherited unchanged, and the captured instant and
    /// randomness source are carried unchanged.
    /// </summary>
    /// <param name="itemFocus">The focus for the derived context.</param>
    /// <returns>The derived context.</returns>
    public JsonataContext WithFocus(JsonataValue itemFocus)
    {
        return new JsonataContext(itemFocus, Root, Frame, EvaluationMillis, Randomness);
    }

    /// <summary>
    /// Derives a context with the same focus and root but a different binding frame — used at block entry to
    /// open a new variable scope without rebinding the focus. The captured instant and randomness source are
    /// carried unchanged.
    /// </summary>
    /// <param name="childFrame">The binding frame the derived context resolves named variables against.</param>
    /// <returns>The derived context.</returns>
    public JsonataContext WithFrame(JsonataBindingFrame childFrame)
    {
        return new JsonataContext(Focus, Root, childFrame, EvaluationMillis, Randomness);
    }

    /// <summary>
    /// Derives the context a lambda body evaluates under: the focus is the lambda's captured definition-time
    /// focus and the binding frame is the lambda's freshly-opened argument frame, so a call rebinds both the
    /// focus and the scope at once (the only place this build does so). The root, the captured instant, and
    /// the randomness source are inherited.
    /// </summary>
    /// <param name="capturedFocus">The lambda's captured definition-time focus the body evaluates against.</param>
    /// <param name="childFrame">The child binding frame the parameters were bound into.</param>
    /// <returns>The derived context.</returns>
    public JsonataContext EnterLambda(JsonataValue capturedFocus, JsonataBindingFrame childFrame)
    {
        return new JsonataContext(capturedFocus, Root, childFrame, EvaluationMillis, Randomness);
    }
}
