namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Human- and metric-readable names for <see cref="JoinStrategySelectorKind"/> values — kept off the
/// value type itself (which carries only the code), following the project family's
/// <see cref="Lumoin.Veritas.Core.Execution.ComputeWorkClassNames"/> companion pattern. The names double
/// as the telemetry tag, so they are lowercase and snake-cased; a consumer-created kind falls back to a
/// generated tag.
/// </summary>
public static class JoinStrategySelectorKindNames
{
    /// <summary>Returns the name (and metric tag) for a kind.</summary>
    /// <param name="selectorKind">The kind.</param>
    /// <returns>The name.</returns>
    public static string GetName(JoinStrategySelectorKind selectorKind)
    {
        return GetName(selectorKind.Code);
    }

    /// <summary>Returns the name (and metric tag) for a kind code.</summary>
    /// <param name="code">The kind code.</param>
    /// <returns>The name, or a generated <c>custom_*</c> tag for an unregistered code.</returns>
    public static string GetName(int code)
    {
        return code switch
        {
            var c when c == JoinStrategySelectorKind.None.Code => "none",
            var c when c == JoinStrategySelectorKind.Forced.Code => "forced",
            var c when c == JoinStrategySelectorKind.Structural.Code => "structural",
            var c when c == JoinStrategySelectorKind.Manual.Code => "manual",
            var c when c == JoinStrategySelectorKind.Calibrated.Code => "calibrated",
            var c when c == JoinStrategySelectorKind.Hinted.Code => "hinted",
            _ => $"custom_{code}",
        };
    }
}
