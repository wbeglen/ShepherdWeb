using ShepherdWeb.Infrastructure.Api.Generated;

namespace ShepherdWeb.Features.ExperimentVersions;

/// <summary>
/// The Lock dialog's input (revision 4.4, owner decision W2). Every execution of the version that has no recorded end
/// time must be acknowledged, one by one, as having an unknown end time, each with an explanation, before Lock can be
/// confirmed. ShepherdAPI V11 records each acknowledgment as an immutable FinalizationException in the finalization
/// transaction; the execution's end time can never be recorded afterwards. The rules here mirror ShepherdAPI's
/// (an explanation that is not blank, at most 2000 characters, each execution once); ShepherdAPI decides again under
/// its lock.
/// </summary>
public sealed class LockRequestForm
{
    public const int ExplanationMaxLength = 2000;

    private LockRequestForm(IReadOnlyList<UnknownEnd> unknownEnds) => UnknownEnds = unknownEnds;

    /// <summary>One per execution without a recorded end time, oldest first.</summary>
    public IReadOnlyList<UnknownEnd> UnknownEnds { get; }

    public static LockRequestForm For(VersionConfiguration configuration) => new(
        configuration.Executions
            .Where(e => e.EndUTC is null)
            .OrderBy(e => e.StartUTC).ThenBy(e => e.ExecutionID)
            .Select(e => new UnknownEnd(e))
            .ToList());

    /// <summary>True when every execution without an end is acknowledged with an acceptable explanation.</summary>
    public bool CanSubmit => UnknownEnds.All(u => u.Problem is null);

    /// <summary>The request: no status (ShepherdAPI ignores it) and each acknowledgment's explanation exactly as entered.</summary>
    public LockExperimentVersionRequest ToRequest() => new()
    {
        UnknownEnds = UnknownEnds.Count == 0
            ? null
            : UnknownEnds.Select(u => new UnknownEndAcknowledgment { ExecutionID = u.Execution.ExecutionID, Explanation = u.Explanation }).ToList(),
    };

    public sealed class UnknownEnd(Execution execution)
    {
        public Execution Execution { get; } = execution;

        /// <summary>The operator confirms that this execution's end time is unknown and may stay unrecorded for good.</summary>
        public bool Acknowledged { get; set; }

        public string Explanation { get; set; } = string.Empty;

        /// <summary>Why this execution does not yet allow Lock, or null when it does.</summary>
        public string? Problem =>
            !Acknowledged ? "Acknowledge that this execution's end time is unknown, or end it first."
            : string.IsNullOrWhiteSpace(Explanation) ? "Explain why the end time is unknown."
            : Explanation.Length > ExplanationMaxLength ? $"The explanation can be at most {ExplanationMaxLength} characters."
            : null;
    }
}
