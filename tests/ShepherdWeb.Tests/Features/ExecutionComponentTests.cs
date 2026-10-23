using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ShepherdWeb.Features.Executions;
using ShepherdWeb.Features.ExperimentVersions;
using ShepherdWeb.Features.Iterations;
using ShepherdWeb.Features.Scoring;
using ShepherdWeb.Infrastructure.ArtifactStore;
using ShepherdWeb.Tests.TestSupport;

namespace ShepherdWeb.Tests.Features;

public class ExecutionComponentTests : AppTestContext
{
    /// <summary>Serves execution 11 of iteration 7 of version 1, with its artifacts and observations.</summary>
    private void ServeExecution(bool ended, bool locked, string artifacts = "[]", string observations = "[]", string scoreRules = "[]")
    {
        ServeVersion(locked, locked ? Json.Locked : Json.Draft, iterations: Json.OneIteration, scoreRules: scoreRules,
            executionsPerIteration: $"[{Json.Execution(ended)}]");
        Api.On(HttpMethod.Get, "/api/executions/11", HttpStatusCode.OK, Json.Execution(ended))
            .On(HttpMethod.Get, "/api/iterations/7", HttpStatusCode.OK, Json.OneIterationSingle)
            .On(HttpMethod.Get, "/api/artifacts", HttpStatusCode.OK, artifacts)
            .On(HttpMethod.Get, "/api/score-observations", HttpStatusCode.OK, observations);
    }

    /// <summary>Stores the prompt in the real test store and returns its artifact JSON (type DeliveredPrompt).</summary>
    private async Task<string> RegisteredPromptAsync(string text)
    {
        var stored = await Services.GetRequiredService<ArtifactFileStore>()
            .StoreAsync(11, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)), ".txt");
        return $$"""[{"artifactID":5,"executionID":11,"artifactTypeID":1,"originalName":"iteration-7-prompt.txt","originalURI":"urn:goodshepherd:iteration:7:prompt","storedURI":"{{stored.StoredUri}}","mimeType":"text/plain; charset=utf-8","byteLength":{{stored.ByteLength}},"shA256":"{{stored.Sha256}}","receivedUTC":"2026-09-02T09:00:00"}]""";
    }

    [Fact]
    public async Task InProgress_WithRegisteredPrompt_OffersCopyAndEnd_AndCopiesTheStoredText()
    {
        ServeExecution(ended: false, locked: false, artifacts: await RegisteredPromptAsync("Build the thing."));
        var clipboard = JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);
        clipboard.SetVoidResult(); // bUnit keeps JS calls pending until a result is set

        var cut = Render<ExecutionDetail>(p => p.Add(x => x.ExecutionId, 11));
        cut.WaitForAssertion(() => Assert.Contains("Copy prompt", cut.Markup));

        Assert.Contains("Execution ended", cut.Markup);
        Assert.DoesNotContain("Score this execution", cut.Markup); // hands off: no evaluation while running
        Assert.DoesNotContain("differs from this iteration", cut.Markup);

        cut.FindAll("button").Single(b => b.TextContent == "Copy prompt").Click();
        cut.WaitForAssertion(() => Assert.Contains("Copied", cut.Markup));
        Assert.Equal("Build the thing.", clipboard.Invocations.Single().Arguments[0]);
    }

    [Fact]
    public void InProgress_WithoutRegisteredPrompt_BlocksCopy_AndOffersNoSubstitute()
    {
        ServeExecution(ended: false, locked: false);

        var cut = Render<ExecutionDetail>(p => p.Add(x => x.ExecutionId, 11));
        cut.WaitForAssertion(() => Assert.Contains("start a new attempt", cut.Markup));

        Assert.Contains("No start prompt is registered for this execution", cut.Markup);
        Assert.DoesNotContain("Copy prompt", cut.Markup);
        Assert.DoesNotContain("Register it now", cut.Markup); // no saved prompt: nothing is offered for registration
    }

    [Fact]
    public async Task PromptDifferentFromTheIteration_IsFlagged()
    {
        ServeExecution(ended: false, locked: false, artifacts: await RegisteredPromptAsync("Something else."));

        var cut = Render<ExecutionDetail>(p => p.Add(x => x.ExecutionId, 11));

        cut.WaitForAssertion(() => Assert.Contains("differs from this iteration", cut.Markup));
    }

    [Fact]
    public void Ended_OffersEvaluation_NotRunFactEditing()
    {
        ServeExecution(ended: true, locked: false);

        var cut = Render<ExecutionDetail>(p => p.Add(x => x.ExecutionId, 11));
        cut.WaitForAssertion(() => Assert.Contains("Score this execution", cut.Markup));

        Assert.DoesNotContain("Execution ended…", cut.Markup);
        Assert.DoesNotContain("Edit run facts", cut.Markup);
        Assert.Contains("Add artifact", cut.Markup);
    }

    [Fact]
    public void EndedOnALockedVersion_IsReadOnly()
    {
        ServeExecution(ended: true, locked: true);

        var cut = Render<ExecutionDetail>(p => p.Add(x => x.ExecutionId, 11));
        cut.WaitForAssertion(() => Assert.Contains("View scores", cut.Markup));

        Assert.DoesNotContain("Add artifact", cut.Markup);
        Assert.DoesNotContain("Copy prompt", cut.Markup);
    }

    [Fact]
    public void EndingTheExecution_SendsEndUtcAfterConfirmation()
    {
        ServeExecution(ended: false, locked: false);
        Api.On(HttpMethod.Put, "/api/executions/11/run-facts", HttpStatusCode.OK, Json.Execution(ended: true));

        var cut = Render<ExecutionDetail>(p => p.Add(x => x.ExecutionId, 11));
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Execution ended")).Click());
        cut.Find("#run-interventions").Change("1");
        cut.Find("form").Submit();

        Assert.Contains("become permanent", cut.Markup);
        Assert.DoesNotContain(Api.Requests, r => r.Method == HttpMethod.Put); // nothing sent before confirming
        cut.FindAll("button").Single(b => b.TextContent == "End execution").Click();

        cut.WaitForAssertion(() => Assert.Contains(Api.Requests, r => r.Method == HttpMethod.Put));
        var body = Api.Requests.Single(r => r.Method == HttpMethod.Put).Body!;
        Assert.Contains("\"endUTC\":\"", body);
        Assert.Contains("\"interventionCount\":1", body);
        Assert.Contains("\"modelUsed\":\"model-x\"", body); // full set re-sent
    }

    [Fact]
    public void Scoring_BeforeTheExecutionEnds_IsNotOffered()
    {
        ServeExecution(ended: false, locked: false, scoreRules: Json.OneScoreRule);

        var cut = Render<ExecutionScoring>(p => p.Add(x => x.ExecutionId, 11));

        cut.WaitForAssertion(() => Assert.Contains("available after the execution ends", cut.Markup));
        Assert.Empty(cut.FindAll("input"));
    }

    [Fact]
    public void Scoring_SavesAnObservationForTheChosenRater()
    {
        ServeExecution(ended: true, locked: false, scoreRules: Json.OneScoreRule);
        Api.On(HttpMethod.Post, "/api/score-observations", HttpStatusCode.Created,
            """{"scoreObservationID":9,"executionID":11,"scoreRuleID":5,"raterID":1,"observedScore":4.5,"observedUTC":"2026-09-03T08:00:00","observationNotes":null}""");

        var cut = Render<ExecutionScoring>(p => p.Add(x => x.ExecutionId, 11));
        cut.WaitForAssertion(() => cut.Find("#rater"));
        cut.Find("#rater").Change("1");
        cut.WaitForAssertion(() => cut.Find("input[type=number]")); // the rows render after the rater change completes
        cut.Find("input[type=number]").Change("4.5");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save").Click();

        cut.WaitForAssertion(() => Assert.Contains("Saved", cut.Markup));
        var body = Api.Requests.Single(r => r.Method == HttpMethod.Post).Body!;
        Assert.Contains("\"raterID\":1", body);
        Assert.Contains("\"scoreRuleID\":5", body);
        Assert.Contains("\"observedScore\":4.5", body);
        Assert.Equal("1", Preferences.Items["rater"]); // remembered for the next execution
    }

    [Fact]
    public void Scoring_OutOfRange_IsRejectedBeforeSending()
    {
        ServeExecution(ended: true, locked: false, scoreRules: Json.OneScoreRule); // range 0–5

        var cut = Render<ExecutionScoring>(p => p.Add(x => x.ExecutionId, 11));
        cut.WaitForAssertion(() => cut.Find("#rater"));
        cut.Find("#rater").Change("1");
        cut.WaitForAssertion(() => cut.Find("input[type=number]"));
        cut.Find("input[type=number]").Change("6");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save").Click();

        Assert.Contains("between 0 and 5", cut.Markup);
        Assert.DoesNotContain(Api.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void Scoring_OnALockedVersion_IsReadOnly()
    {
        ServeExecution(ended: true, locked: true, scoreRules: Json.OneScoreRule,
            observations: """[{"scoreObservationID":9,"executionID":11,"scoreRuleID":5,"raterID":1,"observedScore":4.5,"observedUTC":"2026-09-03T08:00:00"}]""");

        var cut = Render<ExecutionScoring>(p => p.Add(x => x.ExecutionId, 11));

        cut.WaitForAssertion(() => Assert.Contains("Scores and evidence are final", cut.Markup));
        Assert.Empty(cut.FindAll("input"));
        Assert.Contains("4.5", cut.Markup); // still visible
    }

    [Fact]
    public void VersionPage_ShowsScoreObservationsTab_OnlyOnceAnExecutionHasEnded()
    {
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration, executionsPerIteration: $"[{Json.Execution(ended: false)}]");
        var running = Render<ExperimentVersionDetail>(p => p.Add(x => x.VersionId, 1));
        running.WaitForAssertion(() => Assert.Contains("Executions (1)", running.Markup));
        Assert.DoesNotContain("Score observations", running.Markup);

        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration, executionsPerIteration: $"[{Json.Execution(ended: true)}]");
        var ended = Render<ExperimentVersionDetail>(p => p.Add(x => x.VersionId, 1));
        ended.WaitForAssertion(() => Assert.Contains("Score observations", ended.Markup));
    }

    [Fact]
    public async Task ExecutionList_OffersStart_ExceptForRetiredVersions()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration);
        var draft = Render<ExecutionList>(p => p.Add(x => x.Configuration, LoadConfigurationAsync().Result));
        Assert.Contains("Start execution", draft.Markup);

        ServeVersion(locked: false, statusId: Json.Retired, iterations: Json.OneIteration);
        var retired = Render<ExecutionList>(p => p.Add(x => x.Configuration, LoadConfigurationAsync().Result));
        Assert.DoesNotContain("Start execution", retired.Markup);
        Assert.Contains("Retired", retired.Markup);
    }

    [Fact]
    public void ExecutionStart_CreatesTheExecution_RegistersThePrompt_AndOpensIt()
    {
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration);
        Api.On(HttpMethod.Post, "/api/executions", HttpStatusCode.Created, Json.Execution(ended: false))
            .On(HttpMethod.Get, "/api/artifacts", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/api/artifacts", HttpStatusCode.Created,
                """{"artifactID":5,"executionID":11,"artifactTypeID":1,"byteLength":16,"shA256":"X","receivedUTC":"2026-09-02T09:00:00"}""");

        var cut = Render<ExecutionStart>(p => p.Add(x => x.VersionId, 1));
        cut.WaitForAssertion(() => Assert.Contains("first execution of this version", cut.Markup)); // freeze warning
        Assert.Equal("model-x", cut.Find("#exec-model").GetAttribute("value")); // agent default filled in
        cut.Find("form").Submit();
        cut.FindAll("button").Single(b => b.TextContent == "Start").Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => Assert.EndsWith("/executions/11", navigation.Uri));
        var create = Api.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/api/executions").Body!;
        Assert.Contains("\"iterationID\":7", create);
        Assert.Contains("\"attemptNumber\":1", create);
        Assert.Contains("\"statusID\":1", create); // Draft (N10)
        Assert.Contains(Api.Requests, r => r.Method == HttpMethod.Post && r.Path == "/api/artifacts");
    }
}
