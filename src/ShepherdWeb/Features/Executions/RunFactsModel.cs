using System.ComponentModel.DataAnnotations;
using ShepherdWeb.Infrastructure.Api.Generated;
using ShepherdWeb.Infrastructure.Time;

namespace ShepherdWeb.Features.Executions;

/// <summary>
/// Edit model for an execution's run facts: the fields ShepherdAPI lets change until EndUTC is set
/// (ShepherdAPI §5.1). ShepherdAPI replaces the whole set on every write, so every field is always sent.
/// </summary>
public sealed class RunFactsModel : IValidatableObject
{
    [StringLength(200)] public string? ModelUsed { get; set; }
    [StringLength(100)] public string? InterfaceUsed { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Intervention count cannot be negative.")]
    public int InterventionCount { get; set; }

    public string? OutcomeNotes { get; set; }

    /// <summary>Entered as UTC. Required only when ending the execution.</summary>
    public DateTime? EndUtc { get; set; }

    /// <summary>True when this write ends the execution.</summary>
    public bool Ending { get; init; }

    public DateTime StartUtc { get; init; }

    public static RunFactsModel From(Execution execution, bool ending) => new()
    {
        ModelUsed = execution.ModelUsed,
        InterfaceUsed = execution.InterfaceUsed,
        InterventionCount = execution.InterventionCount,
        OutcomeNotes = execution.OutcomeNotes,
        EndUtc = ending ? Utc.Now : null,
        Ending = ending,
        StartUtc = execution.StartUTC,
    };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Ending)
        {
            yield break;
        }

        if (EndUtc is null)
        {
            yield return new ValidationResult("Enter the end time.", [nameof(EndUtc)]);
        }
        else if (Utc.FromUtcInput(EndUtc.Value) < Utc.FromApi(StartUtc))
        {
            yield return new ValidationResult($"The end time cannot be before the start time ({Utc.Format(StartUtc)}).", [nameof(EndUtc)]);
        }
    }

    public UpdateExecutionRunFactsRequest ToRequest() => new()
    {
        ModelUsed = string.IsNullOrWhiteSpace(ModelUsed) ? null : ModelUsed.Trim(),
        InterfaceUsed = string.IsNullOrWhiteSpace(InterfaceUsed) ? null : InterfaceUsed.Trim(),
        InterventionCount = InterventionCount,
        OutcomeNotes = string.IsNullOrWhiteSpace(OutcomeNotes) ? null : OutcomeNotes,
        EndUTC = Ending && EndUtc is { } end ? Utc.FromUtcInput(end) : null,
    };
}
