using System.ComponentModel.DataAnnotations;
using ShepherdWeb.Infrastructure.Api.Generated;

namespace ShepherdWeb.Features.ScoreRules;

/// <summary>
/// Edit model for adding a score rule. It differs from CreateScoreRuleRequest because it needs
/// "nothing chosen yet" selections and cross-field checks (docs/ARCHITECTURE.md AD-5). These checks give
/// immediate feedback only; ShepherdAPI and the database remain the authority (brief §7.3).
/// </summary>
public sealed class ScoreRuleForm : IValidatableObject
{
    public int PhaseID { get; set; }
    public int ScoreCategoryID { get; set; }

    [Range(0, 100, ErrorMessage = "Weight must be between 0 and 100.")]
    public decimal WeightPercent { get; set; }

    public int MinScore { get; set; }
    public int MaxScore { get; set; }

    [StringLength(300)]
    public string? RuleNotes { get; set; }

    /// <summary>(PhaseID, ScoreCategoryID) pairs that already have a rule in this version.</summary>
    public IReadOnlySet<(int PhaseID, int ScoreCategoryID)> ExistingPairs { get; init; } = new HashSet<(int, int)>();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PhaseID == 0)
        {
            yield return new ValidationResult("Choose a phase.", [nameof(PhaseID)]);
        }

        if (ScoreCategoryID == 0)
        {
            yield return new ValidationResult("Choose a score category.", [nameof(ScoreCategoryID)]);
        }

        if (ExistingPairs.Contains((PhaseID, ScoreCategoryID)))
        {
            // Unique (version, phase, category), including retired rules (finding F2).
            yield return new ValidationResult("This phase already has a rule for this category.", [nameof(ScoreCategoryID)]);
        }

        if (MinScore >= MaxScore)
        {
            yield return new ValidationResult("Max score must be greater than min score.", [nameof(MaxScore)]);
        }
    }

    public CreateScoreRuleRequest ToRequest(long experimentVersionId) => new()
    {
        ExperimentVersionID = experimentVersionId,
        PhaseID = PhaseID,
        ScoreCategoryID = ScoreCategoryID,
        WeightPercent = (double)WeightPercent,
        MinScore = MinScore,
        MaxScore = MaxScore,
        // ScoreRule.Active is ignored by ShepherdWeb (owner decision N2). The generated request defaults
        // it to false; true is the API's own default, so sending true leaves the column neutral.
        Active = true,
        RuleNotes = string.IsNullOrWhiteSpace(RuleNotes) ? null : RuleNotes.Trim(),
    };
}
