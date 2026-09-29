using ShepherdWeb.Features.Executions;
using ShepherdWeb.Infrastructure.Api.Generated;
using ShepherdWeb.Infrastructure.Time;

namespace ShepherdWeb.Features.Scoring;

/// <summary>
/// One row of the scoring grid: a score rule and, for the selected rater, its observation (if one has
/// been saved) plus the values being edited. Each row saves on its own, so one failure affects one row.
/// </summary>
public sealed class ScoreRow
{
    public ScoreRow(ScoreRule rule, ScoreObservation? observation)
    {
        Rule = rule;
        Observation = observation;
        // Scores are decimal(5,2) in the database but double on the wire; round-trip through decimal.
        Score = observation is null ? null : (decimal)observation.ObservedScore;
        Notes = observation?.ObservationNotes;
    }

    public ScoreRule Rule { get; }
    public ScoreObservation? Observation { get; private set; }
    public decimal? Score { get; set; }
    public string? Notes { get; set; }
    public string? Message { get; set; }
    public bool Saving { get; set; }
    public bool ShowEvidence { get; set; }
    /// <summary>Loaded when the evidence panel is first opened; null until then.</summary>
    public IReadOnlyList<ScoreObservationEvidence>? Evidence { get; set; }

    public bool IsDirty =>
        Observation is null
            ? Score is not null || !string.IsNullOrWhiteSpace(Notes)
            : Score != (decimal)Observation.ObservedScore || (Notes ?? string.Empty) != (Observation.ObservationNotes ?? string.Empty);

    public string? Problem => ExecutionRules.ScoreProblem(Score, Rule);

    public CreateScoreObservationRequest ToCreateRequest(long executionId, int raterId) => new()
    {
        ExecutionID = executionId,
        ScoreRuleID = Rule.ScoreRuleID,
        RaterID = raterId,
        ObservedScore = (double)Score!.Value,
        ObservedUTC = Utc.Now,
        ObservationNotes = NormalizedNotes,
    };

    /// <summary>A correction keeps the original observation time; it corrects what was observed then.</summary>
    public UpdateScoreObservationContentRequest ToUpdateRequest() => new()
    {
        ObservedScore = (double)Score!.Value,
        ObservedUTC = Observation!.ObservedUTC ?? Utc.Now,
        ObservationNotes = NormalizedNotes,
    };

    public void Saved(ScoreObservation observation)
    {
        Observation = observation;
        Score = (decimal)observation.ObservedScore;
        Notes = observation.ObservationNotes;
    }

    private string? NormalizedNotes => string.IsNullOrWhiteSpace(Notes) ? null : Notes;
}
