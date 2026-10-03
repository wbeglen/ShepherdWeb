using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ShepherdWeb.Features.Executions;
using ShepherdWeb.Features.Iterations;
using ShepherdWeb.Infrastructure.ArtifactStore;
using ShepherdWeb.Infrastructure.Drafts;
using ShepherdWeb.Tests.TestSupport;

namespace ShepherdWeb.Tests.Features;

/// <summary>
/// The Start page and execution page with an execution-specific start prompt. The iteration's prompt
/// is the default; an edit applies to one execution only and is registered as the delivered prompt.
/// </summary>
public class StartPromptOverrideComponentTests : AppTestContext
{
    private const string TwoIterations =
        """[{"iterationID":7,"experimentVersionID":1,"sequence":1,"iterationLabel":"First","prompt":"Build the thing.","scenario":null,"notes":null},""" +
        """{"iterationID":8,"experimentVersionID":1,"sequence":2,"iterationLabel":"Second","prompt":"Build the other thing.","scenario":null,"notes":null}]""";

    private const string CreatedArtifact =
        """{"artifactID":5,"executionID":11,"artifactTypeID":1,"byteLength":16,"shA256":"X","receivedUTC":"2026-09-02T09:00:00"}""";

    private void ServeStartPage(string iterations)
    {
        ServeVersion(locked: false, statusId: Json.Draft, iterations: iterations);
        Api.On(HttpMethod.Post, "/api/executions", HttpStatusCode.Created, Json.Execution(ended: false))
            .On(HttpMethod.Get, "/api/artifacts", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/api/artifacts", HttpStatusCode.Created, CreatedArtifact);
    }

    private IRenderedComponent<ExecutionStart> RenderStartPage()
    {
        var cut = Render<ExecutionStart>(p => p.Add(x => x.VersionId, 1));
        cut.WaitForAssertion(() => Assert.Contains("first execution of this version", cut.Markup));
        return cut;
    }

    private JsonElement RegisteredArtifact() =>
        JsonDocument.Parse(Api.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/api/artifacts").Body!).RootElement.Clone();

    private void StartAndWaitForExecutionPage(IRenderedComponent<ExecutionStart> cut)
    {
        cut.Find("form").Submit();
        cut.FindAll("button").Single(b => b.TextContent == "Start").Click();
        var navigation = Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => Assert.EndsWith("/executions/11", navigation.Uri));
    }

    // ------------------------------------------------------------------------------- Start page ----

    [Fact]
    public void StartPage_PrefillsTheIterationsPrompt_AndRegistersItUnchangedWithIterationProvenance()
    {
        ServeStartPage(Json.OneIteration);
        var cut = RenderStartPage();

        Assert.Equal("Build the thing.", cut.Find("#exec-prompt").GetAttribute("value"));
        Assert.DoesNotContain("Edited for this execution", cut.Markup);
        Assert.DoesNotContain("Reset to iteration prompt", cut.Markup);

        StartAndWaitForExecutionPage(cut);

        var artifact = RegisteredArtifact();
        Assert.Equal(new PromptText("Build the thing.").Sha256, artifact.GetProperty("shA256").GetString());
        Assert.Equal("urn:goodshepherd:iteration:7:prompt", artifact.GetProperty("originalURI").GetString());
    }

    [Fact]
    public void StartPage_AnEditedPrompt_IsRegisteredAsTheExecutionsOwnDeliveredPrompt()
    {
        ServeStartPage(Json.OneIteration);
        var cut = RenderStartPage();

        cut.Find("#exec-prompt").Input("Build the thing, but write tests first.");

        Assert.Contains("Edited for this execution", cut.Markup);
        Assert.Contains("Reset to iteration prompt", cut.Markup);
        StartAndWaitForExecutionPage(cut);

        var artifact = RegisteredArtifact();
        Assert.Equal(new PromptText("Build the thing, but write tests first.").Sha256, artifact.GetProperty("shA256").GetString());
        Assert.Equal("urn:goodshepherd:execution:11:prompt", artifact.GetProperty("originalURI").GetString());
        Assert.Equal("execution-11-prompt.txt", artifact.GetProperty("originalName").GetString());

        // The execution record itself carries no prompt, and the iteration was never written.
        var create = Api.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/api/executions").Body!;
        Assert.DoesNotContain("write tests first", create);
        Assert.DoesNotContain(Api.Requests, r => r.Method != HttpMethod.Get && r.Path.StartsWith("/api/iterations"));
    }

    [Fact]
    public void StartPage_EditingBackToTheIterationsExactText_IsNotAnOverride()
    {
        ServeStartPage(Json.OneIteration);
        var cut = RenderStartPage();

        cut.Find("#exec-prompt").Input("Something else.");
        cut.Find("#exec-prompt").Input("Build the thing.");

        Assert.DoesNotContain("Edited for this execution", cut.Markup);
        StartAndWaitForExecutionPage(cut);
        Assert.Equal("urn:goodshepherd:iteration:7:prompt", RegisteredArtifact().GetProperty("originalURI").GetString());
    }

    [Fact]
    public void StartPage_ResetToIterationPrompt_RestoresTheDefault()
    {
        ServeStartPage(Json.OneIteration);
        var cut = RenderStartPage();
        cut.Find("#exec-prompt").Input("Something else.");

        cut.FindAll("button").Single(b => b.TextContent.Contains("Reset to iteration prompt")).Click();

        Assert.Equal("Build the thing.", cut.Find("#exec-prompt").GetAttribute("value"));
        Assert.DoesNotContain("Edited for this execution", cut.Markup);
    }

    [Fact]
    public void StartPage_ChangingTheIterationAfterAnEdit_ResetsThePromptAndSaysSo()
    {
        ServeStartPage(TwoIterations);
        var cut = RenderStartPage();
        cut.Find("#exec-prompt").Input("My edit for iteration one.");

        cut.Find("#exec-iteration").Change("8");

        Assert.Equal("Build the other thing.", cut.Find("#exec-prompt").GetAttribute("value"));
        Assert.Contains("The prompt was reset to the selected iteration's prompt", cut.Markup);
        Assert.Contains("Your edit was discarded", cut.Markup);
        Assert.DoesNotContain("Edited for this execution", cut.Markup);
    }

    [Fact]
    public void StartPage_ChangingTheIterationWithoutAnEdit_ShowsTheNewPromptWithoutANotice()
    {
        ServeStartPage(TwoIterations);
        var cut = RenderStartPage();

        cut.Find("#exec-iteration").Change("8");

        Assert.Equal("Build the other thing.", cut.Find("#exec-prompt").GetAttribute("value"));
        Assert.DoesNotContain("was reset", cut.Markup);
    }

    [Fact]
    public void StartPage_AnEmptyPrompt_CannotBeStarted()
    {
        ServeStartPage(Json.OneIteration);
        var cut = RenderStartPage();

        cut.Find("#exec-prompt").Input("   ");
        cut.Find("form").Submit();

        Assert.Contains("The prompt cannot be empty.", cut.Markup);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Start"); // confirmation never opened
        Assert.DoesNotContain(Api.Requests, r => r.Method == HttpMethod.Post);
    }

    // ---------------------------------------------------------------------------- Execution page ----

    private void ServeExecution(string artifacts = "[]")
    {
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration,
            executionsPerIteration: $"[{Json.Execution(ended: false)}]");
        Api.On(HttpMethod.Get, "/api/executions/11", HttpStatusCode.OK, Json.Execution(ended: false))
            .On(HttpMethod.Get, "/api/iterations/7", HttpStatusCode.OK, Json.OneIterationSingle)
            .On(HttpMethod.Get, "/api/artifacts", HttpStatusCode.OK, artifacts)
            .On(HttpMethod.Get, "/api/score-observations", HttpStatusCode.OK, "[]");
    }

    private async Task<string> RegisteredStartPromptAsync(string text, string originalUri)
    {
        var stored = await Services.GetRequiredService<ArtifactFileStore>()
            .StoreAsync(11, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)), ".txt");
        return $$"""[{"artifactID":5,"executionID":11,"artifactTypeID":1,"originalName":"x.txt","originalURI":"{{originalUri}}","storedURI":"{{stored.StoredUri}}","mimeType":"text/plain; charset=utf-8","byteLength":{{stored.ByteLength}},"shA256":"{{stored.Sha256}}","receivedUTC":"2026-09-02T09:00:00"}]""";
    }

    [Fact]
    public async Task ExecutionPage_AnExecutionSpecificStartPrompt_IsExplainedNotFlaggedAsAMismatch()
    {
        ServeExecution(await RegisteredStartPromptAsync("Edited for this run.", "urn:goodshepherd:execution:11:prompt"));

        var cut = Render<ExecutionDetail>(p => p.Add(x => x.ExecutionId, 11));

        cut.WaitForAssertion(() => Assert.Contains("Provenance: edited for this execution before Start", cut.Markup));
        Assert.DoesNotContain("differs from this iteration", cut.Markup);
    }

    [Fact]
    public async Task ExecutionPage_ADifferentPromptClaimingTheIterationAsItsSource_IsStillFlagged()
    {
        ServeExecution(await RegisteredStartPromptAsync("Something else.", "urn:goodshepherd:iteration:7:prompt"));

        var cut = Render<ExecutionDetail>(p => p.Add(x => x.ExecutionId, 11));

        cut.WaitForAssertion(() => Assert.Contains("differs from this iteration", cut.Markup));
        Assert.DoesNotContain("Provenance: edited for this execution before Start", cut.Markup);
        Assert.DoesNotContain("changed outside ShepherdWeb", cut.Markup); // observed fact only, no inferred cause
    }

    [Fact]
    public void ExecutionPage_RetryRegistersTheExactTextSavedAtStart_NotTheIterationsPrompt_AndOffersNoEditing()
    {
        ServeExecution();
        Drafts.Items["execution:11:start-prompt"] = new Draft(SavedAtStart("Edited\r\nfor this run.", overridden: true).Serialize(), DateTime.UtcNow);
        Api.On(HttpMethod.Post, "/api/artifacts", HttpStatusCode.Created, CreatedArtifact);

        var cut = Render<ExecutionDetail>(p => p.Add(x => x.ExecutionId, 11));
        cut.WaitForAssertion(() => Assert.Contains("Exactly this text will be registered", cut.Markup));

        Assert.Empty(cut.FindAll("textarea")); // shown, never editable
        Assert.Contains("edited for this execution before Start", cut.Markup);
        cut.FindAll("button").Single(b => b.TextContent == "Register it now").Click();

        cut.WaitForAssertion(() => Assert.Contains(Api.Requests, r => r.Method == HttpMethod.Post && r.Path == "/api/artifacts"));
        var artifact = RegisteredArtifact();
        Assert.Equal(new PromptText("Edited\r\nfor this run.").Sha256, artifact.GetProperty("shA256").GetString());
        Assert.NotEqual(new PromptText("Build the thing.").Sha256, artifact.GetProperty("shA256").GetString());
        Assert.Equal("urn:goodshepherd:execution:11:prompt", artifact.GetProperty("originalURI").GetString());
        cut.WaitForAssertion(() => Assert.DoesNotContain("execution:11:start-prompt", Drafts.Items.Keys));
    }

    [Fact]
    public void ExecutionPage_RetryWithNoSavedCopy_RegistersNothing_AndRequiresANewAttempt()
    {
        // Owner decision (fix 6): if the exact Start prompt cannot be established, no substitute is offered.
        ServeExecution();

        var cut = Render<ExecutionDetail>(p => p.Add(x => x.ExecutionId, 11));
        cut.WaitForAssertion(() => Assert.Contains("The prompt text entered at Start is not saved in this browser", cut.Markup));

        Assert.Contains("cannot establish the exact prompt this execution was started with", cut.Markup);
        Assert.Contains("start a new attempt", cut.Markup);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Register it now");
        Assert.DoesNotContain("Copy prompt", cut.Markup);
        Assert.DoesNotContain(Api.Requests, r => r.Method == HttpMethod.Post);
    }

    /// <summary>The pending start prompt the Start page would have saved for execution 11 (Json.Execution).</summary>
    internal static PendingStartPrompt SavedAtStart(string text, bool overridden) => PendingStartPrompt.For(
        new ShepherdWeb.Infrastructure.Api.Generated.Execution
        {
            ExecutionID = 11, IterationID = 7, StartUTC = new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc),
        },
        text, overridden);
}
