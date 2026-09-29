using System.ComponentModel.DataAnnotations;
using ShepherdWeb.Infrastructure.Api.Generated;
using ShepherdWeb.Infrastructure.Time;

namespace ShepherdWeb.Features.Executions;

/// <summary>
/// Edit model for starting an execution (docs/ARCHITECTURE.md §8.2). It differs from
/// CreateExecutionRequest because selections start as "nothing chosen" and the start time is normally
/// the moment Start is clicked. The iteration, agent, phase, attempt and start time cannot be changed
/// after creation (ShepherdAPI §5.1), so they are all chosen here.
/// </summary>
public sealed class ExecutionStartForm : IValidatableObject
{
    public long IterationID { get; set; }
    public int AgentID { get; set; }
    public int PhaseID { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Attempt number must be 1 or more.")]
    public int AttemptNumber { get; set; } = 1;

    [StringLength(200)] public string? ModelUsed { get; set; }
    [StringLength(100)] public string? InterfaceUsed { get; set; }

    /// <summary>True: the start time is the moment Start is clicked. False: <see cref="StartUtc"/>, entered as UTC.</summary>
    public bool StartWhenClicked { get; set; } = true;

    public DateTime StartUtc { get; set; } = Utc.Now;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (IterationID == 0)
        {
            yield return new ValidationResult("Choose an iteration.", [nameof(IterationID)]);
        }

        if (AgentID == 0)
        {
            yield return new ValidationResult("Choose an agent.", [nameof(AgentID)]);
        }

        if (PhaseID == 0)
        {
            yield return new ValidationResult("Choose a phase.", [nameof(PhaseID)]);
        }

        if (!StartWhenClicked && Utc.FromUtcInput(StartUtc) > Utc.Now)
        {
            yield return new ValidationResult("The start time cannot be in the future.", [nameof(StartUtc)]);
        }
    }

    /// <param name="clickedUtc">When Start was clicked.</param>
    public CreateExecutionRequest ToRequest(int statusId, DateTime clickedUtc) => new()
    {
        IterationID = IterationID,
        AgentID = AgentID,
        PhaseID = PhaseID,
        StatusID = statusId,
        StartUTC = StartWhenClicked ? clickedUtc : Utc.FromUtcInput(StartUtc),
        AttemptNumber = AttemptNumber,
        ModelUsed = string.IsNullOrWhiteSpace(ModelUsed) ? null : ModelUsed.Trim(),
        InterfaceUsed = string.IsNullOrWhiteSpace(InterfaceUsed) ? null : InterfaceUsed.Trim(),
        InterventionCount = 0, // Hands-off is the norm; recorded when the execution ends (§3 rule 6).
    };
}
