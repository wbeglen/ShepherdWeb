using ShepherdWeb.Features.ExperimentVersions;
using ShepherdWeb.Infrastructure;
using ShepherdWeb.Infrastructure.Api.Generated;

namespace ShepherdWeb.Features.Executions;

/// <summary>
/// What the UI offers for executions and scoring (docs/ARCHITECTURE.md §3, revision 4). ShepherdAPI
/// enforces the lock and the EndUTC run-fact freeze; the rest (no new executions for Retired
/// versions, scoring only after an execution ends) is UI-only until A1/A5 exist.
/// </summary>
public static class ExecutionRules
{
    /// <summary>EndUTC is the authoritative indicator that an execution has ended (owner decision Q2).</summary>
    public static bool HasEnded(Execution execution) => execution.EndUTC is not null;

    /// <summary>
    /// True when the prompt delivered at the start of an execution is not the iteration's own prompt
    /// (AD-8, revision: execution-specific prompt). Exact, ordinal comparison, because the delivered
    /// prompt is byte-exact evidence: a prompt that is identical to the iteration's is simply the
    /// iteration's prompt, however the operator arrived at it.
    /// </summary>
    public static bool IsPromptOverride(string? prompt, string? iterationPrompt) =>
        !string.Equals(prompt ?? string.Empty, iterationPrompt ?? string.Empty, StringComparison.Ordinal);

    /// <summary>Why a new execution cannot be started on this version, or null if it can.</summary>
    public static string? StartBlockedReason(VersionConfiguration configuration, string statusName, ConventionsOptions conventions)
    {
        if (VersionLifecycle.IsLocked(configuration.Version))
        {
            return "This version is locked. No further executions can be started.";
        }

        if (Is(statusName, conventions.RetiredStatusName))
        {
            return $"This version is “{statusName}”. No further executions can be started.";
        }

        if (Is(statusName, conventions.LockedStatusName))
        {
            // A status/lock mismatch (VersionLifecycle.StatusMismatch). The status says the record is final.
            return $"This version's status is “{statusName}”. Resolve the status mismatch before starting executions.";
        }

        if (configuration.Iterations.Count == 0)
        {
            return "This version has no iterations to execute yet.";
        }

        return null;
    }

    /// <summary>Scoring, evidence and evaluation artifacts: after the execution ends (Q5), until the version is locked.</summary>
    public static bool CanEvaluate(Execution execution, ExperimentVersion version) =>
        HasEnded(execution) && !VersionLifecycle.IsLocked(version);

    /// <summary>Run facts can be written until EndUTC is set, and never after the version is locked (ShepherdAPI §5.1, §5.3).</summary>
    public static bool CanEditRunFacts(Execution execution, ExperimentVersion version) =>
        !HasEnded(execution) && !VersionLifecycle.IsLocked(version);

    /// <summary>The suggested attempt number: one more than the highest for the same iteration, agent and phase.</summary>
    public static int NextAttemptNumber(IEnumerable<Execution> executions, long iterationId, int agentId, int phaseId) =>
        executions.Where(e => e.IterationID == iterationId && e.AgentID == agentId && e.PhaseID == phaseId)
            .Select(e => e.AttemptNumber)
            .DefaultIfEmpty(0)
            .Max() + 1;

    /// <summary>The score rules an execution is scored against: non-retired rules for its phase.</summary>
    public static IReadOnlyList<ScoreRule> RulesFor(Execution execution, IEnumerable<ScoreRule> versionRules) =>
        versionRules.Where(r => r.PhaseID == execution.PhaseID && r.RowState != "D").ToList();

    /// <summary>Why a score is not acceptable for a rule, or null. ShepherdAPI does not check this yet (A3).</summary>
    public static string? ScoreProblem(decimal? score, ScoreRule rule)
    {
        if (score is null)
        {
            return "Enter a score.";
        }

        if (score < rule.MinScore || score > rule.MaxScore)
        {
            return $"Score must be between {rule.MinScore} and {rule.MaxScore}.";
        }

        return decimal.Round(score.Value, 2) != score.Value ? "Use at most two decimal places." : null;
    }

    private static bool Is(string value, string conventionName) =>
        string.Equals(value, conventionName, StringComparison.OrdinalIgnoreCase);
}
