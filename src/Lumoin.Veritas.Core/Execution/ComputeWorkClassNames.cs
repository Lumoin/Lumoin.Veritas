namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// Human- and metric-readable names for <see cref="ComputeWorkClass"/>
/// values — kept off the value type itself (which carries only the
/// priority), following the project family's <c>BufferKindNames</c> /
/// <c>PurposeNames</c> companion pattern. The names double as the
/// OpenTelemetry class tag, so they are lowercase and snake-cased; a
/// consumer-created class falls back to a generated tag.
/// </summary>
public static class ComputeWorkClassNames
{
    /// <summary>Returns the name (and metric tag) for a class.</summary>
    /// <param name="workClass">The class.</param>
    /// <returns>The name.</returns>
    public static string GetName(ComputeWorkClass workClass)
    {
        return GetName(workClass.Priority);
    }

    /// <summary>Returns the name (and metric tag) for a class priority.</summary>
    /// <param name="priority">The class priority.</param>
    /// <returns>The name, or a generated <c>custom_*</c> tag for an unregistered priority.</returns>
    public static string GetName(int priority)
    {
        return priority switch
        {
            var p when p == ComputeWorkClass.ControlPlaneTick.Priority => "control_plane_tick",
            var p when p == ComputeWorkClass.ViewBuild.Priority => "view_build",
            var p when p == ComputeWorkClass.BulkSort.Priority => "bulk_sort",
            var p when p == ComputeWorkClass.Reasoning.Priority => "reasoning",
            var p when p == ComputeWorkClass.SketchUpdate.Priority => "sketch_update",
            var p when p == ComputeWorkClass.Scrub.Priority => "scrub",
            _ => $"custom_{priority}",
        };
    }
}
