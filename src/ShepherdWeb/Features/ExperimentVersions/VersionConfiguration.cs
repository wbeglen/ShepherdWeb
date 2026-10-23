using ShepherdWeb.Infrastructure.Api.Generated;

namespace ShepherdWeb.Features.ExperimentVersions;

/// <summary>A version, everything it configures, and the executions recorded against it, as ShepherdAPI returned them.</summary>
/// <param name="Executions">All executions of the version's iterations, newest start first.</param>
public sealed record VersionConfiguration(
    ExperimentVersion Version,
    IReadOnlyList<Iteration> Iterations,
    IReadOnlyList<Parameter> Parameters,
    IReadOnlyList<ScoreRule> ScoreRules,
    IReadOnlyList<Execution> Executions)
{
    public int ExecutionCount => Executions.Count;

    /// <summary>
    /// Configuration is immutable once the first execution exists, and everything is immutable once
    /// the version is locked (docs/ARCHITECTURE.md §3, rules 3–4). The UI offers no configuration
    /// edits when this is true. ShepherdAPI enforces both (design-frozen, version-locked).
    /// </summary>
    public bool IsConfigurationFrozen => VersionLifecycle.IsLocked(Version) || ExecutionCount > 0;

    /// <summary>True once any execution has ended, which is when score observations become relevant.</summary>
    public bool HasEndedExecution => Executions.Any(e => e.EndUTC is not null);

    public Iteration? IterationOf(Execution execution) => Iterations.FirstOrDefault(i => i.IterationID == execution.IterationID);
}

/// <summary>
/// Loads a <see cref="VersionConfiguration"/> for the pages that show or edit it. The version,
/// iterations, parameters and score rules load concurrently; executions are then listed per iteration
/// (one call each, acceptable at MVP volume; A4 would make it one call).
/// ScoreRules contains every rule of the version (ShepherdAPI deletes rules physically, Issue #7).
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
            executionLists.SelectMany(list => list).OrderByDescending(e => e.StartUTC).ToList());
    }
}
