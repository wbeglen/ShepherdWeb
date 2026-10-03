using ShepherdWeb.Infrastructure.Api.Generated;

namespace ShepherdWeb.Features.Executions;

/// <summary>The evidentiary role of a delivered-prompt artifact, read from its explicit provenance.</summary>
public enum DeliveredPromptRole
{
    /// <summary>The prompt delivered when the execution started.</summary>
    Start,

    /// <summary>Further prompt text delivered during an exceptional intervention.</summary>
    Intervention,

    /// <summary>The provenance does not state a role ShepherdWeb recognises for this execution.</summary>
    Unrecorded,
}

/// <summary>
/// The one definition of delivered-prompt provenance (docs/ARCHITECTURE.md AD-8, role contract). An
/// artifact's role is read from its exact <c>OriginalURI</c>, never from timestamps or ordering, and
/// identical bytes registered under different provenance are different evidence.
/// </summary>
public static class PromptProvenance
{
    /// <summary>Start prompt taken unchanged from the iteration.</summary>
    public static string IterationStartUri(long iterationId) => $"urn:goodshepherd:iteration:{iterationId}:prompt";

    public static string IterationStartName(long iterationId) => $"iteration-{iterationId}-prompt.txt";

    /// <summary>Start prompt edited for this one execution before Start.</summary>
    public static string ExecutionStartUri(long executionId) => $"urn:goodshepherd:execution:{executionId}:prompt";

    public static string ExecutionStartName(long executionId) => $"execution-{executionId}-prompt.txt";

    /// <summary>Additional prompt delivered during an intervention.</summary>
    public static string InterventionUri(long executionId) => $"urn:goodshepherd:execution:{executionId}:additional-prompt";

    public static string InterventionName(long executionId) => $"execution-{executionId}-additional-prompt.txt";

    /// <summary>The role of a delivered prompt of <paramref name="execution"/>, by exact, ordinal comparison of its provenance.</summary>
    public static DeliveredPromptRole RoleOf(string? originalUri, Execution execution)
    {
        if (IsStartUri(originalUri, execution.ExecutionID, execution.IterationID))
        {
            return DeliveredPromptRole.Start;
        }

        return string.Equals(originalUri, InterventionUri(execution.ExecutionID), StringComparison.Ordinal)
            ? DeliveredPromptRole.Intervention
            : DeliveredPromptRole.Unrecorded;
    }

    /// <summary>True for either start-prompt provenance of this execution: its iteration's, or its own (edited).</summary>
    public static bool IsStartUri(string? originalUri, long executionId, long iterationId) =>
        string.Equals(originalUri, IterationStartUri(iterationId), StringComparison.Ordinal) ||
        IsExecutionStartUri(originalUri, executionId);

    /// <summary>True when the start prompt was edited for this execution before Start.</summary>
    public static bool IsExecutionStartUri(string? originalUri, long executionId) =>
        string.Equals(originalUri, ExecutionStartUri(executionId), StringComparison.Ordinal);
}
