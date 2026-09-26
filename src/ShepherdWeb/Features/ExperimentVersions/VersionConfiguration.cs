using ShepherdWeb.Infrastructure.Api.Generated;

namespace ShepherdWeb.Features.ExperimentVersions;

/// <summary>A version and everything it configures, as ShepherdAPI returned it.</summary>
/// <param name="ExecutionCount">
/// Executions recorded against the version's iterations. Under the approved lifecycle executions
/// exist only on locked versions, but data recorded before that rule (and before R1 is enforced by
/// ShepherdAPI) can have executions on an unlocked version.
/// </param>
public sealed record VersionConfiguration(
    ExperimentVersion Version,
    IReadOnlyList<Iteration> Iterations,
    IReadOnlyList<Parameter> Parameters,
    IReadOnlyList<ScoreRule> ScoreRules,
    int ExecutionCount)
{
    /// <summary>
    /// Configuration must not change once the version is locked or once any execution has used it
    /// (docs/ARCHITECTURE.md §3, rules 2–3). The UI offers no configuration edits when this is true.
    /// </summary>
    public bool IsConfigurationFrozen => VersionLifecycle.IsLocked(Version) || ExecutionCount > 0;
}

/// <summary>
/// Loads a <see cref="VersionConfiguration"/> for the pages that show or edit it. The version,
/// iterations, parameters and score rules load concurrently; executions are then counted per iteration
/// (one call each, acceptable at MVP volume; R5 would make it one call).
/// ScoreRules contains non-retired rules only; that is what ShepherdAPI's list returns.
/// </summary>
public sealed class VersionConfigurationLoader(
    IExperimentVersionsClient versions,
    IIterationsClient iterations,
    IParametersClient parameters,
    IScoreRulesClient scoreRules,
    IExecutionsClient executions)
{
    // Named arguments: without OpenAPI operationIds (finding F5, R4) NSwag emits both
    // IterationsGetAsync(long id) and IterationsGetAsync(long? experimentVersionId); a plain long
    // argument would bind to the single-item overload.
    public async Task<VersionConfiguration> LoadAsync(long versionId)
    {
        var versionTask = versions.ExperimentVersionsGetAsync(versionId);
        var iterationsTask = iterations.IterationsGetAsync(experimentVersionId: versionId);
        var parametersTask = parameters.ParametersGetAsync(experimentVersionId: versionId);
        var scoreRulesTask = scoreRules.ScoreRulesGetAsync(experimentVersionId: versionId);
        await Task.WhenAll(versionTask, iterationsTask, parametersTask, scoreRulesTask);

        var executionLists = await Task.WhenAll(iterationsTask.Result.Select(i => executions.ExecutionsGetAsync(iterationId: i.IterationID)));

        return new VersionConfiguration(
            versionTask.Result, iterationsTask.Result, parametersTask.Result, scoreRulesTask.Result,
            executionLists.Sum(list => list.Count));
    }
}
