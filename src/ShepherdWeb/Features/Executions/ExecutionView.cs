using Microsoft.Extensions.Options;
using ShepherdWeb.Features.ExperimentVersions;
using ShepherdWeb.Infrastructure;
using ShepherdWeb.Infrastructure.Api.Generated;

namespace ShepherdWeb.Features.Executions;

/// <summary>One execution with everything its pages need, as ShepherdAPI returned it.</summary>
/// <param name="Artifacts">The execution's artifacts in the order they were received.</param>
/// <param name="DeliveredPromptTypeId">The DeliveredPrompt artifact type's ID, or null if that row does not exist.</param>
public sealed record ExecutionView(
    Execution Execution,
    Iteration Iteration,
    VersionConfiguration Configuration,
    IReadOnlyList<Artifact> Artifacts,
    int? DeliveredPromptTypeId)
{
    public ExperimentVersion Version => Configuration.Version;
    public bool HasEnded => ExecutionRules.HasEnded(Execution);
    public bool CanEditRunFacts => ExecutionRules.CanEditRunFacts(Execution, Version);
    public bool CanEvaluate => ExecutionRules.CanEvaluate(Execution, Version);

    /// <summary>Prompts delivered to the agent, oldest first: the start prompt, then any intervention prompts.</summary>
    public IReadOnlyList<Artifact> DeliveredPrompts =>
        Artifacts.Where(a => a.ArtifactTypeID == DeliveredPromptTypeId).ToList();

    /// <summary>Artifacts captured for evaluation, i.e. everything that is not a delivered prompt.</summary>
    public IReadOnlyList<Artifact> EvaluationArtifacts =>
        Artifacts.Where(a => a.ArtifactTypeID != DeliveredPromptTypeId).ToList();
}

public sealed class ExecutionLoader(
    IExecutionsClient executions,
    IIterationsClient iterations,
    IArtifactsClient artifacts,
    VersionConfigurationLoader versionLoader,
    ReferenceData referenceData,
    IOptions<ConventionsOptions> conventions)
{
    public async Task<ExecutionView> LoadAsync(long executionId)
    {
        var executionTask = executions.ExecutionsGetAsync(executionId); // plain long: the single-item overload
        var artifactsTask = artifacts.ArtifactsGetAsync(executionId: executionId);
        await Task.WhenAll(executionTask, artifactsTask, referenceData.EnsureLoadedAsync());

        var execution = executionTask.Result;
        var iteration = await iterations.IterationsGetAsync(execution.IterationID);
        var configuration = await versionLoader.LoadAsync(iteration.ExperimentVersionID);

        return new ExecutionView(
            execution, iteration, configuration,
            artifactsTask.Result.OrderBy(a => a.ReceivedUTC).ToList(),
            referenceData.FindArtifactType(conventions.Value.DeliveredPromptArtifactTypeName)?.ArtifactTypeID);
    }
}
