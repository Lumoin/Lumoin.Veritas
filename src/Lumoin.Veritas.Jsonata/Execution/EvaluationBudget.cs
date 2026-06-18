namespace Lumoin.Veritas.Jsonata.Execution;

/// <summary>
/// The deterministic step budget for one evaluation: one step is charged per work frame the driver
/// processes, so a runaway expression terminates with a <see cref="JsonataEvaluationLimitException"/>
/// rather than spinning. The bound is supplied per evaluation — <see cref="JsonataLimits.MaxEvaluationSteps"/>
/// is the conservative production default; a host running a legitimately large but finite computation (a
/// conformance batch, an offline job) may raise it. A non-terminating recursion is bounded independently and
/// far sooner by the work-stack depth limit, so this bound only governs runaway finite iteration.
/// </summary>
internal sealed class EvaluationBudget
{
    /// <summary>The maximum number of steps this budget allows before it trips.</summary>
    private readonly int maxSteps;

    /// <summary>The number of steps charged so far.</summary>
    private int steps;

    /// <summary>Creates a step budget that trips once <paramref name="maxSteps"/> steps are exceeded.</summary>
    /// <param name="maxSteps">The step bound; defaults to <see cref="JsonataLimits.MaxEvaluationSteps"/> (the production bound).</param>
    public EvaluationBudget(int maxSteps = JsonataLimits.MaxEvaluationSteps)
    {
        this.maxSteps = maxSteps;
    }

    /// <summary>
    /// Charges one evaluation step, throwing <see cref="JsonataEvaluationLimitException"/>
    /// (<see cref="JsonataLimit.EvaluationSteps"/>) once the step bound is exceeded.
    /// </summary>
    /// <exception cref="JsonataEvaluationLimitException">The step bound was exceeded.</exception>
    public void Charge()
    {
        if(++steps > maxSteps)
        {
            throw new JsonataEvaluationLimitException(JsonataLimit.EvaluationSteps, WellKnownJsonataErrors.NonTerminatingRecursion, "JSONata evaluation exceeded the maximum number of steps.");
        }
    }
}
