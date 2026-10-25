using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShepherdWeb.Features.Artifacts;
using ShepherdWeb.Features.Executions;
using ShepherdWeb.Features.ExperimentVersions;
using ShepherdWeb.Infrastructure;
using ShepherdWeb.Infrastructure.Api;
using ShepherdWeb.Infrastructure.Api.Generated;
using ShepherdWeb.Infrastructure.ArtifactStore;
using ShepherdWeb.Infrastructure.Drafts;
using ShepherdWeb.Infrastructure.Preferences;

namespace ShepherdWeb.Tests.TestSupport;

/// <summary>
/// A bUnit context wired like the real app: the real generated clients over <see cref="StubShepherdApi"/>,
/// ReferenceData, ApiErrorMapper, conventions, in-memory drafts and preferences, and a real artifact
/// store in a temporary folder that is deleted afterwards.
/// </summary>
public abstract class AppTestContext : BunitContext
{
    protected StubShepherdApi Api { get; } = new();
    protected InMemoryDraftStore Drafts { get; } = new();
    protected InMemoryPreferenceStore Preferences { get; } = new();
    protected string ArtifactRoot { get; } = Path.Combine(Path.GetTempPath(), "shepherdweb-tests", Guid.NewGuid().ToString("N"));

    protected AppTestContext()
    {
        var http = Api.CreateHttpClient();
        Services.AddSingleton(Options.Create(new ShepherdApiOptions { BaseUrl = "http://localhost:5231" }));
        Services.AddSingleton(Options.Create(new ConventionsOptions()));
        Services.AddSingleton(new ApiErrorMapper(NullLogger<ApiErrorMapper>.Instance));
        Services.AddSingleton<IDraftStore>(Drafts);
        Services.AddSingleton<IPreferenceStore>(Preferences);
        Services.AddSingleton(Options.Create(new ArtifactStoreOptions { RootPath = ArtifactRoot }));
        Services.AddSingleton<ArtifactFileStore>();

        Services.AddSingleton<IAgentsClient>(new AgentsClient(http));
        Services.AddSingleton<IArtifactsClient>(new ArtifactsClient(http));
        Services.AddSingleton<IScoreObservationsClient>(new ScoreObservationsClient(http));
        Services.AddSingleton<IArtifactTypesClient>(new ArtifactTypesClient(http));
        Services.AddSingleton<IExperimentVersionsClient>(new ExperimentVersionsClient(http));
        Services.AddSingleton<IExecutionsClient>(new ExecutionsClient(http));
        Services.AddSingleton<IIterationsClient>(new IterationsClient(http));
        Services.AddSingleton<IParametersClient>(new ParametersClient(http));
        Services.AddSingleton<IPhasesClient>(new PhasesClient(http));
        Services.AddSingleton<IRatersClient>(new RatersClient(http));
        Services.AddSingleton<IScoreCategoriesClient>(new ScoreCategoriesClient(http));
        Services.AddSingleton<IScoreRulesClient>(new ScoreRulesClient(http));
        Services.AddSingleton<IStatusesClient>(new StatusesClient(http));
        Services.AddScoped<ReferenceData>();
        Services.AddScoped<VersionConfigurationLoader>();
        Services.AddScoped<ExecutionLoader>();
        Services.AddScoped<ArtifactRegistrar>();
        Services.AddScoped<StartExecutionWorkflow>();

        Api.WithEmptyLookups()
            .On(HttpMethod.Get, "/api/statuses", HttpStatusCode.OK, Json.Statuses)
            .On(HttpMethod.Get, "/api/phases", HttpStatusCode.OK, Json.Phases)
            .On(HttpMethod.Get, "/api/score-categories", HttpStatusCode.OK, Json.ScoreCategories)
            .On(HttpMethod.Get, "/api/agents", HttpStatusCode.OK, Json.Agents)
            .On(HttpMethod.Get, "/api/raters", HttpStatusCode.OK, Json.Raters)
            .On(HttpMethod.Get, "/api/artifact-types", HttpStatusCode.OK, Json.ArtifactTypes);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(ArtifactRoot))
        {
            Directory.Delete(ArtifactRoot, recursive: true);
        }
    }

    /// <summary>Loads ReferenceData up front, as the pages do before rendering version components.</summary>
    protected async Task<ReferenceData> LoadedReferenceDataAsync()
    {
        var referenceData = Services.GetRequiredService<ReferenceData>();
        await referenceData.EnsureLoadedAsync();
        return referenceData;
    }

    /// <summary>Serves version 1 with the given lock/status and its (empty unless given) configuration.</summary>
    protected void ServeVersion(bool locked, int statusId, string iterations = "[]", string parameters = "[]",
        string scoreRules = "[]", string executionsPerIteration = "[]", string finalizationExceptions = "[]")
    {
        Api.On(HttpMethod.Get, "/api/experiment-versions/1", HttpStatusCode.OK, Json.Version(locked, statusId))
            .On(HttpMethod.Get, "/api/iterations", HttpStatusCode.OK, iterations)
            .On(HttpMethod.Get, "/api/parameters", HttpStatusCode.OK, parameters)
            .On(HttpMethod.Get, "/api/score-rules", HttpStatusCode.OK, scoreRules)
            .On(HttpMethod.Get, "/api/executions", HttpStatusCode.OK, executionsPerIteration)
            .On(HttpMethod.Get, "/api/experiment-versions/1/finalization-exceptions", HttpStatusCode.OK, finalizationExceptions);
    }

    protected async Task<VersionConfiguration> LoadConfigurationAsync() =>
        await Services.GetRequiredService<VersionConfigurationLoader>().LoadAsync(1);
}

public sealed class InMemoryPreferenceStore : IPreferenceStore
{
    public Dictionary<string, string> Items { get; } = new();

    public Task<string?> GetAsync(string key) => Task.FromResult(Items.GetValueOrDefault(key));

    public Task SetAsync(string key, string value)
    {
        Items[key] = value;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryDraftStore : IDraftStore
{
    public Dictionary<string, Draft> Items { get; } = new();

    public Task<Draft?> LoadAsync(string key) => Task.FromResult(Items.GetValueOrDefault(key));

    public Task SaveAsync(string key, string text)
    {
        Items[key] = new Draft(text, DateTime.UtcNow);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key)
    {
        Items.Remove(key);
        return Task.CompletedTask;
    }
}

/// <summary>ShepherdAPI response bodies used across tests. Status IDs match the live database.</summary>
public static class Json
{
    public const int Draft = 1, Locked = 2, Retired = 3, Complete = 4;

    public const string Statuses =
        """[{"statusID":4,"name":"Complete"},{"statusID":1,"name":"Draft"},{"statusID":2,"name":"Locked"},{"statusID":3,"name":"Retired"}]""";

    public const string Phases =
        """[{"phaseID":1,"name":"Architecture","sequence":1},{"phaseID":2,"name":"Boilerplate","sequence":2}]""";

    public const string ScoreCategories =
        """[{"scoreCategoryID":1,"name":"Correctness","higherIsBetter":true},{"scoreCategoryID":2,"name":"Defects","higherIsBetter":false}]""";

    public static string Version(bool locked, int statusId) =>
        $$"""{"experimentVersionID":1,"name":"Pit Stop","versionLabel":"1.0","statusID":{{statusId}},"effectiveDateUTC":"2026-08-30T00:00:00","description":"Baseline","lockedUTC":{{(locked ? "\"2026-09-01T12:00:00\"" : "null")}}}""";

    public const string OneIteration =
        """[{"iterationID":7,"experimentVersionID":1,"sequence":1,"iterationLabel":"First","prompt":"Build the thing.","scenario":null,"notes":null}]""";

    public const string OneExecution =
        """[{"executionID":11,"iterationID":7,"agentID":1,"phaseID":1,"statusID":1,"startUTC":"2026-09-02T09:00:00","endUTC":"2026-09-02T10:00:00","interventionCount":0,"attemptNumber":1}]""";

    public const string OneExecutionInProgress =
        """[{"executionID":12,"iterationID":7,"agentID":1,"phaseID":1,"statusID":1,"startUTC":"2026-09-02T09:00:00","endUTC":null,"interventionCount":0,"attemptNumber":1}]""";

    /// <summary>The FinalizationException list accepting the given execution's unknown end (recorded at version 1's lock time).</summary>
    public static string UnknownEnd(long executionId, string explanation) =>
        $$"""[{"experimentVersionID":1,"executionID":{{executionId}},"exceptionCode":"end-time-unknown","explanation":{{System.Text.Json.JsonSerializer.Serialize(explanation)}},"recordedUTC":"2026-09-01T12:00:00"}]""";

    public const string Agents =
        """[{"agentID":1,"name":"Claude","provider":"Anthropic","defaultModel":"model-x","defaultInterface":"Desktop","active":true}]""";

    public const string Raters = """[{"raterID":1,"name":"Bill","raterType":"Human","agentID":null}]""";

    public const int DeliveredPromptType = 1, LogType = 2;

    public const string ArtifactTypes =
        """[{"artifactTypeID":1,"name":"DeliveredPrompt"},{"artifactTypeID":2,"name":"Log"}]""";

    /// <summary>Execution 11 (iteration 7, agent 1, phase 1) with the given end.</summary>
    public static string Execution(bool ended) =>
        $$"""{"executionID":11,"iterationID":7,"agentID":1,"phaseID":1,"statusID":1,"startUTC":"2026-09-02T09:00:00","endUTC":{{(ended ? "\"2026-09-02T10:00:00\"" : "null")}},"modelUsed":"model-x","interfaceUsed":"Desktop","interventionCount":0,"attemptNumber":1}""";

    public const string OneIterationSingle =
        """{"iterationID":7,"experimentVersionID":1,"sequence":1,"iterationLabel":"First","prompt":"Build the thing.","scenario":null,"notes":null}""";

    public const string OneParameter =
        """[{"parameterID":3,"experimentVersionID":1,"name":"Timeout","value":"30","unit":"min","notes":null}]""";

    public const string OneScoreRule =
        """[{"scoreRuleID":5,"experimentVersionID":1,"phaseID":1,"scoreCategoryID":1,"weightPercent":50,"minScore":0,"maxScore":5,"active":true,"ruleNotes":null}]""";
}
