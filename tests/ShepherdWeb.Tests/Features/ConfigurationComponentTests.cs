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
    public async Task VersionHeader_WarnsWhenStatusSaysLockedButThereIsNoLock()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Locked);
        var config = await LoadConfigurationAsync();

        var cut = Render<VersionHeader>(p => p.Add(x => x.Version, config.Version));

        Assert.Contains("no lock timestamp", cut.Markup);
        Assert.DoesNotContain("Configuration locked at", cut.Markup);
    }

    [Fact]
    public async Task VersionOverview_UnlockedVersion_OffersReviewAndLock_ButNotLockedStatus()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: false, statusId: Json.Draft);
        var config = await LoadConfigurationAsync();

        var cut = Render<VersionOverview>(p => p.Add(x => x.Configuration, config));

        var options = cut.FindAll("select option").Select(o => o.TextContent).ToList();
        Assert.DoesNotContain("Locked", options);
        Assert.Contains("Review and lock", cut.Markup);
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
        Assert.Contains("has 1 execution but is not locked", header.Markup);

        var overview = Render<VersionOverview>(p => p.Add(x => x.Configuration, config));
        Assert.DoesNotContain("Edit details", overview.Markup);
        Assert.Contains("Review and lock", overview.Markup);

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
    public async Task ScoreRuleTable_LockedVersion_OffersNoRetireOrAdd()
    {
        await LoadedReferenceDataAsync();
        ServeVersion(locked: true, statusId: Json.Locked, scoreRules: Json.OneScoreRule);
        var config = await LoadConfigurationAsync();

        var cut = Render<ScoreRuleTable>(p => p.Add(x => x.Configuration, config));

        Assert.Contains("Correctness", cut.Markup);
        Assert.DoesNotContain("Retire", cut.Markup);
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void ConfigurationReview_Lock_SendsLockedStatus_AndShowsTheLockedRecord()
    {
        var locked = false;
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration);
        Api.On(HttpMethod.Get, "/api/experiment-versions/1", _ =>
                StubShepherdApi.Response(HttpStatusCode.OK, Json.Version(locked, locked ? Json.Locked : Json.Draft)))
            .On(HttpMethod.Post, "/api/experiment-versions/1/lock", _ =>
            {
                locked = true;
                return StubShepherdApi.Response(HttpStatusCode.OK, Json.Version(true, Json.Locked));
            });

        var cut = Render<ConfigurationReview>(p => p.Add(x => x.VersionId, 1));
        cut.WaitForAssertion(() => Assert.Contains("Build the thing.", cut.Markup));

        cut.FindAll("button").Single(b => b.TextContent.Contains("Lock this version")).Click();
        Assert.Contains("cannot be undone", cut.Markup);
        cut.FindAll("button").Single(b => b.TextContent.Contains("Lock permanently")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Configuration locked at", cut.Markup));
        Assert.Contains("\"statusID\":2", Api.Requests.Single(r => r.Path.EndsWith("/lock")).Body);
        Assert.DoesNotContain("Lock this version", cut.Markup);
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
