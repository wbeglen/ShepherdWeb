using System.Net;
using Bunit;
using ShepherdWeb.Features.ExperimentVersions;
using ShepherdWeb.Features.Iterations;
using ShepherdWeb.Features.Parameters;
using ShepherdWeb.Features.ScoreRules;
using ShepherdWeb.Infrastructure.Drafts;
using ShepherdWeb.Tests.TestSupport;

namespace ShepherdWeb.Tests.Features;

public class ConfigurationComponentTests : AppTestContext
{
    [Fact]
    public async Task Loader_ForALockedVersion_LoadsItsFinalizationExceptions()
    {
        ServeVersion(locked: true, statusId: Json.Locked, iterations: Json.OneIteration, executionsPerIteration: Json.OneExecutionInProgress,
            finalizationExceptions: Json.UnknownEnd(12, "Cursor could not report the end time."));

        var config = await LoadConfigurationAsync();

        var exception = Assert.Single(config.Exceptions);
        Assert.Equal("Cursor could not report the end time.", config.ExceptionFor(config.Executions.Single())!.Explanation);
        Assert.Equal("end-time-unknown", exception.ExceptionCode);
    }

    [Fact]
    public async Task VersionOverview_LockedVersion_ListsItsFinalizationExceptions_AndCountsTheUnknownEnd()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: true, statusId: Json.Locked, iterations: Json.OneIteration, executionsPerIteration: Json.OneExecutionInProgress,
            finalizationExceptions: Json.UnknownEnd(12, "Cursor could not report the end time.\nNo log was kept."));
        var config = await LoadConfigurationAsync();

        var cut = Render<VersionOverview>(p => p.Add(x => x.Configuration, config));

        Assert.Contains("Finalization exceptions", cut.Markup);
        Assert.Contains("Cursor could not report the end time.", cut.Markup);
        Assert.Contains("#12", cut.Markup);
        Assert.Contains("1 (1 end time unknown — acknowledged)", cut.Markup);
        Assert.DoesNotContain("in progress", cut.Markup);
    }

    [Fact]
    public async Task VersionOverview_UnlockedVersion_SaysEndTimeNotRecorded_NotInProgress()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration, executionsPerIteration: Json.OneExecutionInProgress);
        var config = await LoadConfigurationAsync();

        var cut = Render<VersionOverview>(p => p.Add(x => x.Configuration, config));

        Assert.Contains("1 (1 end time not recorded)", cut.Markup);
        Assert.DoesNotContain("in progress", cut.Markup);
        Assert.DoesNotContain("Finalization exceptions", cut.Markup);
    }

    [Fact]
    public async Task Loader_ForAnUnlockedVersion_MakesNoFinalizationExceptionCall()
    {
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration, executionsPerIteration: Json.OneExecutionInProgress);

        var config = await LoadConfigurationAsync();

        Assert.Empty(config.Exceptions);
        Assert.DoesNotContain(Api.Requests, r => r.Path.EndsWith("/finalization-exceptions"));
    }

    [Fact]
    public async Task VersionHeader_WarnsWhenStatusSaysLockedButThereIsNoLock()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Locked);
        var config = await LoadConfigurationAsync();

        var cut = Render<VersionHeader>(p => p.Add(x => x.Version, config.Version));

        Assert.Contains("no lock timestamp", cut.Markup);
        Assert.DoesNotContain("This record is final", cut.Markup);
    }

    [Fact]
    public async Task VersionOverview_UnlockedVersion_OffersLock_ButNotLockedStatus()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Draft);
        var config = await LoadConfigurationAsync();

        var cut = Render<VersionOverview>(p => p.Add(x => x.Configuration, config));

        var options = cut.FindAll("select option").Select(o => o.TextContent).ToList();
        Assert.DoesNotContain("Locked", options);
        Assert.Contains("Lock version", cut.Markup);
        Assert.Contains("Edit details", cut.Markup);
    }

    [Fact]
    public async Task VersionOverview_MismatchedCurrentStatus_IsStillShownAsSelected()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Locked);
        var config = await LoadConfigurationAsync();

        var cut = Render<VersionOverview>(p => p.Add(x => x.Configuration, config));

        Assert.Equal("Locked", cut.Find("select option").TextContent); // current value, first
    }

    [Fact]
    public async Task ParameterTable_LockedVersion_IsReadOnly()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: true, statusId: Json.Locked, parameters: Json.OneParameter);
        var config = await LoadConfigurationAsync();

        var cut = Render<ParameterTable>(p => p.Add(x => x.Configuration, config));

        Assert.Contains("Timeout", cut.Markup);
        Assert.Empty(cut.FindAll("form"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent is "Edit" or "Delete");
    }

    [Fact]
    public async Task UnlockedVersionWithExecutions_IsFrozen_ButCanStillBeLocked()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Locked, iterations: Json.OneIteration,
            parameters: Json.OneParameter, scoreRules: Json.OneScoreRule, executionsPerIteration: Json.OneExecution);
        var config = await LoadConfigurationAsync();

        Assert.Equal(1, config.ExecutionCount);
        Assert.True(config.IsConfigurationFrozen);

        var header = Render<VersionHeader>(p => p.Add(x => x.Version, config.Version).Add(x => x.ExecutionCount, config.ExecutionCount));
        Assert.Contains("1 execution recorded. The configuration is frozen", header.Markup);

        var overview = Render<VersionOverview>(p => p.Add(x => x.Configuration, config));
        Assert.DoesNotContain("Edit details", overview.Markup);
        Assert.Contains("Lock version", overview.Markup);

        Assert.Empty(Render<ParameterTable>(p => p.Add(x => x.Configuration, config)).FindAll("form"));
        Assert.Empty(Render<ScoreRuleTable>(p => p.Add(x => x.Configuration, config)).FindAll("form"));
        Assert.DoesNotContain("New iteration", Render<IterationList>(p => p.Add(x => x.Configuration, config)).Markup);
    }

    [Fact]
    public void IterationEditor_UnlockedVersionWithExecutions_ShowsThePromptReadOnly()
    {
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration, executionsPerIteration: Json.OneExecution);

        var cut = Render<IterationEditor>(p => p.Add(x => x.VersionId, 1).Add(x => x.IterationId, 7));

        cut.WaitForAssertion(() => Assert.Contains("Build the thing.", cut.Markup));
        Assert.Empty(cut.FindAll("textarea"));
    }

    [Fact]
    public async Task ScoreRuleTable_DisablesCategoriesThatAlreadyHaveARuleInTheChosenPhase()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Draft, scoreRules: Json.OneScoreRule); // phase 1 / category 1
        var config = await LoadConfigurationAsync();

        var cut = Render<ScoreRuleTable>(p => p.Add(x => x.Configuration, config));
        cut.Find("#rule-phase").Change("1");

        var correctness = cut.FindAll("#rule-category option").Single(o => o.GetAttribute("value") == "1");
        var defects = cut.FindAll("#rule-category option").Single(o => o.GetAttribute("value") == "2");
        Assert.True(correctness.HasAttribute("disabled"));
        Assert.False(defects.HasAttribute("disabled"));
    }

    [Fact]
    public async Task ScoreRuleTable_Add_SendsActiveTrue_AndReportsChange()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Draft);
        Api.On(HttpMethod.Post, "/api/score-rules", HttpStatusCode.Created, Json.OneScoreRule.Trim('[', ']'));
        var config = await LoadConfigurationAsync();
        var changed = false;

        var cut = Render<ScoreRuleTable>(p => p
            .Add(x => x.Configuration, config)
            .Add(x => x.OnChanged, () => changed = true));
        cut.Find("#rule-phase").Change("1");
        cut.Find("#rule-category").Change("2");
        cut.Find("#rule-weight").Change("40");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.True(changed));
        var body = Api.Requests.Single(r => r.Method == HttpMethod.Post).Body!;
        Assert.Contains("\"active\":true", body);
        Assert.Contains("\"scoreCategoryID\":2", body);
    }

    [Fact]
    public async Task ScoreRuleTable_LockedVersion_OffersNoDeleteOrAdd()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: true, statusId: Json.Locked, scoreRules: Json.OneScoreRule);
        var config = await LoadConfigurationAsync();

        var cut = Render<ScoreRuleTable>(p => p.Add(x => x.Configuration, config));

        Assert.Contains("Correctness", cut.Markup);
        Assert.DoesNotContain("Delete", cut.Markup);
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public async Task ScoreRuleTable_VersionWithExecutions_OffersNoDeleteOrAdd()
    {
        // The first execution freezes the design (ShepherdAPI design-frozen).
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration,
            executionsPerIteration: Json.OneExecutionInProgress, scoreRules: Json.OneScoreRule);
        var config = await LoadConfigurationAsync();

        var cut = Render<ScoreRuleTable>(p => p.Add(x => x.Configuration, config));

        Assert.Contains("Correctness", cut.Markup);
        Assert.DoesNotContain("Delete", cut.Markup);
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public async Task ScoreRuleTable_Delete_ExplainsThePairIsFreed_AndDeletesTheRule()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Draft, scoreRules: Json.OneScoreRule);
        Api.On(HttpMethod.Delete, "/api/score-rules/5", HttpStatusCode.NoContent);
        var config = await LoadConfigurationAsync();
        var changed = false;

        var cut = Render<ScoreRuleTable>(p => p.Add(x => x.Configuration, config).Add(x => x.OnChanged, () => changed = true));
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Delete").Click();

        Assert.Contains("become free", cut.Markup);
        Assert.DoesNotContain("retire", cut.Markup, StringComparison.OrdinalIgnoreCase);
        cut.FindAll("button").Last(b => b.TextContent.Trim() == "Delete").Click();

        cut.WaitForAssertion(() => Assert.True(changed));
        Assert.Single(Api.Requests, r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task VersionOverview_Lock_WithAnExecutionWithoutAnEnd_RequiresItsAcknowledgmentAndExplanation()
    {
        // W2: Lock stays available; each execution without an end must be acknowledged, with an explanation.
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration, executionsPerIteration: Json.OneExecutionInProgress);
        Api.On(HttpMethod.Post, "/api/experiment-versions/1/lock", HttpStatusCode.OK, Json.Version(true, Json.Draft))
            .On(HttpMethod.Put, "/api/experiment-versions/1/status", HttpStatusCode.OK, Json.Version(true, Json.Locked));
        var config = await LoadConfigurationAsync();
        var changed = false;
        var cut = Render<VersionOverview>(p => p.Add(x => x.Configuration, config).Add(x => x.OnChanged, () => changed = true));
        var lockButton = cut.FindAll("button").Single(b => b.TextContent.Contains("Lock version"));
        Assert.False(lockButton.HasAttribute("disabled"));
        Assert.Contains("acknowledge its end time as unknown when locking", cut.Markup);

        lockButton.Click();
        bool ConfirmEnabled() => !cut.FindAll("button").Single(b => b.TextContent.Contains("Lock permanently")).HasAttribute("disabled");

        Assert.Contains("Executions without a recorded end time", cut.Markup);
        Assert.Contains("never be recorded afterwards", cut.Markup);
        Assert.False(ConfirmEnabled());
        Assert.Contains("Acknowledge that this execution's end time is unknown", cut.Markup);

        cut.Find("#ack-12").Change(true);
        Assert.False(ConfirmEnabled()); // still no explanation
        cut.Find("#why-12").Input("   ");
        Assert.False(ConfirmEnabled()); // a blank explanation does not count
        Assert.Contains("Explain why the end time is unknown", cut.Markup);

        const string explanation = "  Cursor could not report the end time.\nNo run log was kept.  ";
        cut.Find("#why-12").Input(explanation);
        Assert.True(ConfirmEnabled());
        Assert.Contains($"{explanation.Length} / 2000", cut.Markup);

        cut.FindAll("button").Single(b => b.TextContent.Contains("Lock permanently")).Click();

        cut.WaitForAssertion(() => Assert.True(changed));
        var body = System.Text.Json.JsonDocument.Parse(Api.Requests.Single(r => r.Path.EndsWith("/lock")).Body!).RootElement;
        var unknownEnd = Assert.Single(body.GetProperty("unknownEnds").EnumerateArray());
        Assert.Equal(12, unknownEnd.GetProperty("executionID").GetInt64());
        Assert.Equal(explanation, unknownEnd.GetProperty("explanation").GetString()); // exactly as entered
        Assert.Single(Api.Requests, r => r.Path.EndsWith("/status")); // D1 still applies
    }

    // ---- Lock refusals and unclear outcomes (M5, W3) ----

    /// <summary>A version whose one execution ended, rendered with the Lock dialog open; counts reloads.</summary>
    private async Task<(IRenderedComponent<VersionOverview> Cut, Func<int> Reloads)> OpenLockDialogAsync()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration, executionsPerIteration: Json.OneExecution);
        Api.On(HttpMethod.Put, "/api/experiment-versions/1/status", HttpStatusCode.OK, Json.Version(true, Json.Locked));
        var config = await LoadConfigurationAsync();
        var reloads = 0;
        var cut = Render<VersionOverview>(p => p.Add(x => x.Configuration, config).Add(x => x.OnChanged, () => reloads++));
        cut.FindAll("button").Single(b => b.TextContent.Contains("Lock version")).Click();
        return (cut, () => reloads);
    }

    private static void ConfirmLock(IRenderedComponent<VersionOverview> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Contains("Lock permanently")).Click();

    private bool StatusWasSet => Api.Requests.Any(r => r.Path.EndsWith("/status"));

    [Fact]
    public async Task Lock_ARejectedRequest_400_KeepsTheDialog_AndChangesNothing()
    {
        var (cut, reloads) = await OpenLockDialogAsync();
        Api.On(HttpMethod.Post, "/api/experiment-versions/1/lock", HttpStatusCode.BadRequest,
            """{"title":"Validation Failed","status":400,"errors":{"UnknownEnds":["Every unknown end must have an explanation that is not blank."]},"code":"validation-failed"}""");

        ConfirmLock(cut);

        cut.WaitForAssertion(() => Assert.Contains("Validation Failed", cut.Markup));
        Assert.Contains("Lock permanently", cut.Markup); // still open
        Assert.Equal(0, reloads());
        Assert.False(StatusWasSet);
    }

    [Fact]
    public async Task Lock_AStaleAcknowledgment_ShowsWhy_AndReloadsThePage()
    {
        var (cut, reloads) = await OpenLockDialogAsync();
        Api.On(HttpMethod.Post, "/api/experiment-versions/1/lock", HttpStatusCode.Conflict,
            """{"title":"Finalization Failed: Unknown End Not Accepted","status":409,"detail":"1 acknowledged unknown end(s) are not accepted: 12.","code":"finalize-unknown-end-invalid","executionIDs":[12],"total":1}""");

        ConfirmLock(cut);

        cut.WaitForAssertion(() => Assert.Equal(1, reloads()));
        Assert.Contains("Unknown End Not Accepted", cut.Markup);
        Assert.Contains("not accepted: 12", cut.Markup);
        Assert.DoesNotContain("Lock permanently", cut.Markup); // closed: the page reloads with current data
        Assert.False(StatusWasSet);
    }

    [Fact]
    public async Task Lock_AlreadyLockedElsewhere_ShowsTheConflict_AndReloads()
    {
        var (cut, reloads) = await OpenLockDialogAsync();
        Api.On(HttpMethod.Post, "/api/experiment-versions/1/lock", HttpStatusCode.Conflict,
            """{"title":"Version Locked","status":409,"detail":"ExperimentVersion 1 is already locked; it cannot be finalized again.","code":"version-locked"}""");

        ConfirmLock(cut);

        cut.WaitForAssertion(() => Assert.Equal(1, reloads()));
        Assert.Contains("already locked", cut.Markup);
        Assert.False(StatusWasSet);
    }

    [Fact]
    public async Task Lock_ARetryableConflict_KeepsTheDialog_AndARetryCanSucceed()
    {
        var (cut, reloads) = await OpenLockDialogAsync();
        Api.On(HttpMethod.Post, "/api/experiment-versions/1/lock", HttpStatusCode.Conflict,
            """{"title":"Concurrent Operation Conflict","status":409,"detail":"Another operation held the version.","code":"retryable-conflict","retryable":true}""");

        ConfirmLock(cut);

        cut.WaitForAssertion(() => Assert.Contains("Nothing was changed. You can try again.", cut.Markup));
        Assert.Contains("Lock permanently", cut.Markup);
        Assert.Equal(0, reloads());
        Assert.Single(Api.Requests, r => r.Path.EndsWith("/lock")); // never retried by itself

        Api.On(HttpMethod.Post, "/api/experiment-versions/1/lock", HttpStatusCode.OK, Json.Version(true, Json.Draft));
        ConfirmLock(cut); // the operator retries

        cut.WaitForAssertion(() => Assert.Equal(1, reloads()));
        Assert.True(StatusWasSet);
    }

    [Fact]
    public async Task Lock_TimedOutButCommitted_IsConfirmedByReadingTheVersion_NeverReportedAsFailed()
    {
        var (cut, reloads) = await OpenLockDialogAsync();
        Api.On(HttpMethod.Post, "/api/experiment-versions/1/lock", _ =>
        {
            // ShepherdAPI committed, but the answer never arrived: the version now reads as locked.
            Api.On(HttpMethod.Get, "/api/experiment-versions/1", HttpStatusCode.OK, Json.Version(true, Json.Draft));
            throw new TaskCanceledException("The request was canceled.", new TimeoutException());
        });

        ConfirmLock(cut);

        cut.WaitForAssertion(() => Assert.Equal(1, reloads()));
        Assert.Contains("ShepherdAPI confirms the version was locked", cut.Markup);
        Assert.DoesNotContain("The version was not locked", cut.Markup);
        Assert.DoesNotContain("Lock outcome unknown", cut.Markup);
        Assert.DoesNotContain("unreachable", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.True(StatusWasSet); // D1 still follows a confirmed lock
        Assert.Single(Api.Requests, r => r.Path.EndsWith("/lock"));
    }

    [Fact]
    public async Task Lock_AServerErrorWithNoLock_SaysTheVersionWasNotLocked()
    {
        var (cut, reloads) = await OpenLockDialogAsync();
        Api.On(HttpMethod.Post, "/api/experiment-versions/1/lock", HttpStatusCode.InternalServerError,
            """{"title":"Internal Server Error","status":500,"detail":"boom"}""");

        ConfirmLock(cut);

        cut.WaitForAssertion(() => Assert.Contains("The version was not locked", cut.Markup));
        Assert.Contains("nothing was finalized", cut.Markup);
        Assert.False(StatusWasSet);
        Assert.Single(Api.Requests, r => r.Path.EndsWith("/lock"));
    }

    [Fact]
    public async Task Lock_WhenNeitherLockNorTheCheckAnswers_SaysTheOutcomeIsUnknown()
    {
        var (cut, reloads) = await OpenLockDialogAsync();
        Api.On(HttpMethod.Post, "/api/experiment-versions/1/lock", _ =>
        {
            Api.Unreachable = true; // ShepherdAPI is gone: the follow-up read fails too
            throw new HttpRequestException("connection reset");
        });

        ConfirmLock(cut);

        cut.WaitForAssertion(() => Assert.Contains("Lock outcome unknown", cut.Markup));
        Assert.Contains("Reload the page", cut.Markup);
        Assert.DoesNotContain("confirms the version was locked", cut.Markup);
        Assert.False(StatusWasSet);
    }

    [Fact]
    public async Task VersionOverview_Lock_WithTwoExecutionsWithoutAnEnd_RequiresBothAcknowledgments()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration,
            executionsPerIteration: """[{"executionID":12,"iterationID":7,"agentID":1,"phaseID":1,"statusID":1,"startUTC":"2026-09-02T09:00:00","endUTC":null,"interventionCount":0,"attemptNumber":1},{"executionID":13,"iterationID":7,"agentID":1,"phaseID":1,"statusID":1,"startUTC":"2026-09-02T11:00:00","endUTC":null,"interventionCount":0,"attemptNumber":2}]""");
        var config = await LoadConfigurationAsync();
        var cut = Render<VersionOverview>(p => p.Add(x => x.Configuration, config));
        cut.FindAll("button").Single(b => b.TextContent.Contains("Lock version")).Click();
        bool ConfirmEnabled() => !cut.FindAll("button").Single(b => b.TextContent.Contains("Lock permanently")).HasAttribute("disabled");

        cut.Find("#ack-12").Change(true);
        cut.Find("#why-12").Input("unknown");
        Assert.False(ConfirmEnabled());

        cut.Find("#ack-13").Change(true);
        cut.Find("#why-13").Input("also unknown");
        Assert.True(ConfirmEnabled());
        Assert.DoesNotContain(Api.Requests, r => r.Method == HttpMethod.Post); // nothing sent before confirming
    }

    [Fact]
    public async Task VersionOverview_Lock_FinalizesThenSetsTheLockedStatus()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration, executionsPerIteration: Json.OneExecution);
        Api.On(HttpMethod.Post, "/api/experiment-versions/1/lock", HttpStatusCode.OK, Json.Version(true, Json.Draft))
            .On(HttpMethod.Put, "/api/experiment-versions/1/status", HttpStatusCode.OK, Json.Version(true, Json.Locked));
        var config = await LoadConfigurationAsync();
        var changed = false;

        var cut = Render<VersionOverview>(p => p.Add(x => x.Configuration, config).Add(x => x.OnChanged, () => changed = true));
        cut.FindAll("button").Single(b => b.TextContent.Contains("Lock version")).Click();

        Assert.Contains("cannot be undone", cut.Markup);
        Assert.Contains("Status is then set to “Locked”", cut.Markup);
        Assert.DoesNotContain("still in progress", cut.Markup);
        cut.FindAll("button").Single(b => b.TextContent.Contains("Lock permanently")).Click();

        cut.WaitForAssertion(() => Assert.True(changed));
        var writes = Api.Requests.Where(r => r.Method != HttpMethod.Get).ToList();
        Assert.Equal(["/api/experiment-versions/1/lock", "/api/experiment-versions/1/status"], writes.Select(r => r.Path));
        Assert.DoesNotContain("\"statusID\":2", writes[0].Body); // the lock carries no status
        Assert.DoesNotContain("executionID", writes[0].Body);    // and, with every execution ended, no unknown ends
        Assert.Contains("\"statusID\":2", writes[1].Body);
        Assert.DoesNotContain("not updated", cut.Markup);
    }

    [Fact]
    public async Task VersionOverview_Lock_FinalizationFailure_ShowsWhy_AndSetsNoStatus()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration, executionsPerIteration: Json.OneExecution);
        Api.On(HttpMethod.Post, "/api/experiment-versions/1/lock", HttpStatusCode.Conflict,
            """{"title":"Finalization Failed: Execution Without Observation","status":409,"detail":"1 Execution(s) have no observation: 11.","code":"finalize-execution-without-observation"}""");
        var config = await LoadConfigurationAsync();
        var changed = false;

        var cut = Render<VersionOverview>(p => p.Add(x => x.Configuration, config).Add(x => x.OnChanged, () => changed = true));
        cut.FindAll("button").Single(b => b.TextContent.Contains("Lock version")).Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Lock permanently")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Finalization Failed: Execution Without Observation", cut.Markup));
        Assert.Contains("have no observation: 11", cut.Markup);
        Assert.False(changed);
        Assert.DoesNotContain(Api.Requests, r => r.Path.EndsWith("/status"));
    }

    [Fact]
    public async Task VersionOverview_Lock_StatusFailure_ReportsTheLockSucceeded_AndDoesNotRetryIt()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration, executionsPerIteration: Json.OneExecution);
        Api.On(HttpMethod.Post, "/api/experiment-versions/1/lock", HttpStatusCode.OK, Json.Version(true, Json.Draft))
            .On(HttpMethod.Put, "/api/experiment-versions/1/status", HttpStatusCode.InternalServerError,
                """{"title":"Internal Server Error","status":500,"detail":"boom"}""");
        var config = await LoadConfigurationAsync();
        var changed = false;

        var cut = Render<VersionOverview>(p => p.Add(x => x.Configuration, config).Add(x => x.OnChanged, () => changed = true));
        cut.FindAll("button").Single(b => b.TextContent.Contains("Lock version")).Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Lock permanently")).Click();

        cut.WaitForAssertion(() => Assert.True(changed)); // reloaded: the version is locked
        Assert.Contains("Locked, but the status was not updated", cut.Markup);
        Assert.Contains("Finalization succeeded", cut.Markup);
        Assert.Contains("Use Change status", cut.Markup);
        Assert.Single(Api.Requests, r => r.Path.EndsWith("/lock"));
    }

    [Fact]
    public void ConfigurationReview_IsReadOnly_WithNoLockAction()
    {
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration);

        var cut = Render<ConfigurationReview>(p => p.Add(x => x.VersionId, 1));

        cut.WaitForAssertion(() => Assert.Contains("Build the thing.", cut.Markup));
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void IterationEditor_NewIteration_SuggestsNextSequence()
    {
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration);

        var cut = Render<IterationEditor>(p => p.Add(x => x.VersionId, 1));

        cut.WaitForAssertion(() => Assert.Equal("2", cut.Find("#sequence").GetAttribute("value")));
    }

    [Fact]
    public void IterationEditor_OffersAStoredDraft_AndRestoresItOnRequest()
    {
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration);
        Drafts.Items["iteration:7:prompt"] = new Draft("Build the thing, better.", DateTime.UtcNow);

        var cut = Render<IterationEditor>(p => p.Add(x => x.VersionId, 1).Add(x => x.IterationId, 7));
        cut.WaitForAssertion(() => Assert.Contains("Unsaved prompt text", cut.Markup));
        Assert.Equal("Build the thing.", cut.Find("#prompt").GetAttribute("value")); // not applied until asked

        cut.FindAll("button").Single(b => b.TextContent == "Restore").Click();

        Assert.Equal("Build the thing, better.", cut.Find("#prompt").GetAttribute("value"));
    }

    [Fact]
    public void IterationEditor_LockedVersion_ShowsThePromptReadOnly()
    {
        ServeVersion(locked: true, statusId: Json.Locked, iterations: Json.OneIteration);

        var cut = Render<IterationEditor>(p => p.Add(x => x.VersionId, 1).Add(x => x.IterationId, 7));

        cut.WaitForAssertion(() => Assert.Contains("Build the thing.", cut.Markup));
        Assert.Empty(cut.FindAll("textarea"));
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void ExperimentVersionCreate_ShowsAConflictInsideTheForm()
    {
        Api.On(HttpMethod.Get, "/api/experiment-versions", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/api/experiment-versions", HttpStatusCode.Conflict,
                """{"title":"Conflict","status":409,"detail":"A record with the same unique key already exists."}""");

        var cut = Render<ExperimentVersionCreate>();
        cut.WaitForAssertion(() => cut.Find("#name"));
        cut.Find("#name").Change("Pit Stop");
        cut.Find("#label").Change("1.0");
        cut.Find("#description").Change("Again");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.Contains("same unique key", cut.Find(".validation-errors").TextContent));
        Assert.Contains("\"statusID\":1", Api.Requests.Single(r => r.Method == HttpMethod.Post).Body); // Draft
    }
}
