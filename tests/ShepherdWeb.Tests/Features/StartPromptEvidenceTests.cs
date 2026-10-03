using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ShepherdWeb.Features.Artifacts;
using ShepherdWeb.Features.Executions;
using ShepherdWeb.Features.Iterations;
using ShepherdWeb.Infrastructure.Api.Generated;
using ShepherdWeb.Infrastructure.ArtifactStore;
using ShepherdWeb.Infrastructure.Drafts;
using ShepherdWeb.Tests.TestSupport;

namespace ShepherdWeb.Tests.Features;

/// <summary>
/// Evidence integrity of the start prompt (review of 1a808b2, findings 2–9 and 11): role comes from
/// explicit provenance, identity includes provenance, only verified bytes are released, a retry
/// reproduces exactly what was decided at Start, and nothing is substituted when that cannot be established.
/// </summary>
public class StartPromptEvidenceTests : AppTestContext
{
    private static readonly Execution Execution11 =
        new() { ExecutionID = 11, IterationID = 7, StartUTC = new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc) };

    private const string IterationUri = "urn:goodshepherd:iteration:7:prompt";
    private const string OverrideUri = "urn:goodshepherd:execution:11:prompt";
    private const string InterventionUri = "urn:goodshepherd:execution:11:additional-prompt";

    // --------------------------------------------------------------------------------- helpers ----

    private void ServeExecutionPage(params string[] artifacts)
    {
        ServeVersion(locked: false, statusId: Json.Draft, iterations: Json.OneIteration,
            executionsPerIteration: $"[{Json.Execution(ended: false)}]");
        Api.On(HttpMethod.Get, "/api/executions/11", HttpStatusCode.OK, Json.Execution(ended: false))
            .On(HttpMethod.Get, "/api/iterations/7", HttpStatusCode.OK, Json.OneIterationSingle)
            .On(HttpMethod.Get, "/api/artifacts", HttpStatusCode.OK, "[" + string.Join(",", artifacts) + "]")
            .On(HttpMethod.Get, "/api/score-observations", HttpStatusCode.OK, "[]");
    }

    /// <summary>Stores <paramref name="text"/> in the real test store and returns the artifact JSON with that provenance.</summary>
    private async Task<(string Json, string Path)> StoredPromptAsync(long artifactId, string text, string originalUri, string receivedUtc)
    {
        var stored = await Services.GetRequiredService<ArtifactFileStore>().StoreAsync(11, new MemoryStream(Encoding.UTF8.GetBytes(text)), ".txt");
        var json = $$"""{"artifactID":{{artifactId}},"executionID":11,"artifactTypeID":1,"originalName":"p.txt","originalURI":"{{originalUri}}","storedURI":"{{stored.StoredUri}}","mimeType":"text/plain; charset=utf-8","byteLength":{{stored.ByteLength}},"shA256":"{{stored.Sha256}}","receivedUTC":"{{receivedUtc}}"}""";
        return (json, Services.GetRequiredService<ArtifactFileStore>().ResolvePath(stored.StoredUri)!);
    }

    private IRenderedComponent<ExecutionDetail> RenderExecutionPage(string waitFor)
    {
        var cut = Render<ExecutionDetail>(p => p.Add(x => x.ExecutionId, 11));
        cut.WaitForAssertion(() => Assert.Contains(waitFor, cut.Markup));
        return cut;
    }

    private static string EchoArtifact(HttpRequestMessage request)
    {
        using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
        var r = body.RootElement;
        return $$"""{"artifactID":5,"executionID":11,"artifactTypeID":1,"originalURI":{{r.GetProperty("originalURI").GetRawText()}},"storedURI":{{r.GetProperty("storedURI").GetRawText()}},"byteLength":{{r.GetProperty("byteLength").GetInt64()}},"shA256":"{{r.GetProperty("shA256").GetString()}}","receivedUTC":"2026-09-02T09:00:01"}""";
    }

    private JsonElement PostedArtifact() =>
        JsonDocument.Parse(Api.Requests.Last(r => r.Method == HttpMethod.Post && r.Path == "/api/artifacts").Body!).RootElement.Clone();

    // ------------------------------------------------------ finding 2: role, not order ----

    [Fact]
    public async Task TheStartPrompt_IsIdentifiedByProvenance_NotByBeingFirst()
    {
        var intervention = await StoredPromptAsync(4, "Please continue.", InterventionUri, "2026-09-02T08:59:00"); // received earlier
        var start = await StoredPromptAsync(5, "Build the thing.", IterationUri, "2026-09-02T09:00:00");
        ServeExecutionPage(intervention.Json, start.Json);
        var clipboard = JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);
        clipboard.SetVoidResult();

        var cut = RenderExecutionPage("Copy prompt");
        Assert.Contains("Additional prompts delivered during interventions", cut.Markup);
        cut.FindAll("button").Single(b => b.TextContent == "Copy prompt").Click();

        cut.WaitForAssertion(() => Assert.Equal("Build the thing.", clipboard.Invocations.Single().Arguments[0]));
    }

    [Fact]
    public async Task AnInterventionPromptAlone_IsNotTreatedAsTheStartPrompt()
    {
        var intervention = await StoredPromptAsync(4, "Please continue.", InterventionUri, "2026-09-02T08:59:00");
        ServeExecutionPage(intervention.Json);

        var cut = RenderExecutionPage("No start prompt is registered for this execution");

        Assert.DoesNotContain("Copy prompt", cut.Markup);
    }

    [Fact]
    public async Task TwoStartPrompts_AreReportedAsAFact_AndNeitherIsReleased()
    {
        var first = await StoredPromptAsync(4, "Build the thing.", IterationUri, "2026-09-02T09:00:00");
        var second = await StoredPromptAsync(5, "Build it differently.", OverrideUri, "2026-09-02T09:00:05");
        ServeExecutionPage(first.Json, second.Json);

        var cut = RenderExecutionPage("2 start prompts are registered for this execution");

        Assert.DoesNotContain("Copy prompt", cut.Markup);
    }

    [Fact]
    public void TheInterventionAction_IsUnavailableUntilAStartPromptIsRegistered()
    {
        ServeExecutionPage();

        var cut = RenderExecutionPage("No start prompt is registered");

        var button = cut.FindAll("button").Single(b => b.TextContent.Contains("Deliver additional prompt"));
        Assert.True(button.HasAttribute("disabled"));
    }

    [Fact]
    public void PromptRoles_AreReadFromExactProvenance()
    {
        Assert.Equal(DeliveredPromptRole.Start, PromptProvenance.RoleOf(IterationUri, Execution11));
        Assert.Equal(DeliveredPromptRole.Start, PromptProvenance.RoleOf(OverrideUri, Execution11));
        Assert.Equal(DeliveredPromptRole.Intervention, PromptProvenance.RoleOf(InterventionUri, Execution11));
        Assert.Equal(DeliveredPromptRole.Unrecorded, PromptProvenance.RoleOf("urn:goodshepherd:iteration:8:prompt", Execution11)); // another iteration
        Assert.Equal(DeliveredPromptRole.Unrecorded, PromptProvenance.RoleOf("URN:goodshepherd:iteration:7:prompt", Execution11)); // ordinal
        Assert.Equal(DeliveredPromptRole.Unrecorded, PromptProvenance.RoleOf(null, Execution11));
    }

    // ---------------------------------------------- finding 3: identity includes provenance ----

    [Fact]
    public async Task AnInterventionWithTheSameBytes_IsNotReusedAsTheStartPrompt()
    {
        var sha = new PromptText("Build the thing.").Sha256;
        Api.On(HttpMethod.Get, "/api/artifacts", HttpStatusCode.OK,
                $$"""[{"artifactID":4,"executionID":11,"artifactTypeID":1,"originalURI":"{{InterventionUri}}","shA256":"{{sha}}","receivedUTC":"2026-09-02T09:00:00","byteLength":16}]""")
            .On(HttpMethod.Post, "/api/artifacts", req => StubShepherdApi.Response(HttpStatusCode.Created, EchoArtifact(req)));

        var artifact = await Services.GetRequiredService<StartExecutionWorkflow>()
            .RegisterStartPromptAsync(PendingStartPrompt.For(Execution11, "Build the thing.", overridden: false));

        Assert.Equal(5, artifact.ArtifactID); // a new artifact, not #4
        Assert.Equal(IterationUri, PostedArtifact().GetProperty("originalURI").GetString());
    }

    // ------------------------------------------------- finding 4: verify before release ----

    [Fact]
    public async Task AStartPromptWhoseFileNoLongerMatchesItsHash_IsNeitherShownNorReleased()
    {
        var start = await StoredPromptAsync(5, "Build the thing.", IterationUri, "2026-09-02T09:00:00");
        await File.WriteAllTextAsync(start.Path, "Build something else.");
        ServeExecutionPage(start.Json);

        var cut = RenderExecutionPage("does not match the SHA-256 recorded for this artifact");

        Assert.DoesNotContain("Copy prompt", cut.Markup);
        Assert.DoesNotContain("Build something else.", cut.Markup);
    }

    [Fact]
    public async Task AStartPromptWhoseFileIsMissing_IsNotReleased()
    {
        var start = await StoredPromptAsync(5, "Build the thing.", IterationUri, "2026-09-02T09:00:00");
        File.Delete(start.Path);
        ServeExecutionPage(start.Json);

        var cut = RenderExecutionPage("The stored file is missing");

        Assert.DoesNotContain("Copy prompt", cut.Markup);
    }

    [Fact]
    public async Task ReadVerifiedText_ReturnsNoTextOnAMismatch()
    {
        var start = await StoredPromptAsync(5, "Build the thing.", IterationUri, "2026-09-02T09:00:00");
        var artifact = JsonSerializer.Deserialize<Artifact>(start.Json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var registrar = Services.GetRequiredService<ArtifactRegistrar>();

        Assert.Equal("Build the thing.", (await registrar.ReadVerifiedTextAsync(artifact)).Text);

        await File.WriteAllTextAsync(start.Path, "tampered");
        var verified = await registrar.ReadVerifiedTextAsync(artifact);
        Assert.Equal(IntegrityResult.Mismatch, verified.Status);
        Assert.Null(verified.Text);
    }

    // -------------------------------------- findings 5, 7: the draft's identity and provenance ----

    [Fact]
    public void ADraftFromADifferentExecutionRecord_IsNotUsed()
    {
        ServeExecutionPage();
        var otherRecord = PendingStartPrompt.For(
            new Execution { ExecutionID = 11, IterationID = 7, StartUTC = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc) }, // e.g. before a restore
            "A prompt from another database.", overridden: true);
        Drafts.Items["execution:11:start-prompt"] = new Draft(otherRecord.Serialize(), DateTime.UtcNow);

        var cut = RenderExecutionPage("does not belong to this execution record");

        Assert.DoesNotContain("A prompt from another database.", cut.Markup);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Register it now");
    }

    [Fact]
    public void ADraftWithoutRecordIdentity_IsNotUsed()
    {
        ServeExecutionPage();
        Drafts.Items["execution:11:start-prompt"] = new Draft("Plain text saved by an earlier build.", DateTime.UtcNow);

        var cut = RenderExecutionPage("not in a form that identifies its execution record");

        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Register it now");
    }

    [Fact]
    public void ADraftWhoseTextNoLongerMatchesItsHash_IsNotUsed()
    {
        ServeExecutionPage();
        var altered = PendingStartPrompt.For(Execution11, "Build the thing.", overridden: false) with { Text = "Build something else." };
        Drafts.Items["execution:11:start-prompt"] = new Draft(altered.Serialize(), DateTime.UtcNow);

        var cut = RenderExecutionPage("does not match the SHA-256 saved with it");

        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Register it now");
    }

    [Fact]
    public void AMatchingDraft_RegistersWithTheProvenanceSavedAtStart_EvenIfTheIterationPromptChangedSince()
    {
        ServeExecutionPage(); // the iteration's prompt is now "Build the thing."
        var savedAtStart = PendingStartPrompt.For(Execution11, "The iteration's prompt at Start.", overridden: false);
        Drafts.Items["execution:11:start-prompt"] = new Draft(savedAtStart.Serialize(), DateTime.UtcNow);
        Api.On(HttpMethod.Post, "/api/artifacts", req => StubShepherdApi.Response(HttpStatusCode.Created, EchoArtifact(req)));

        var cut = RenderExecutionPage("Exactly this text will be registered");
        cut.FindAll("button").Single(b => b.TextContent == "Register it now").Click();

        cut.WaitForAssertion(() => Assert.Contains(Api.Requests, r => r.Method == HttpMethod.Post && r.Path == "/api/artifacts"));
        var posted = PostedArtifact();
        Assert.Equal(IterationUri, posted.GetProperty("originalURI").GetString()); // saved, not re-decided against today's iteration
        Assert.Equal(new PromptText("The iteration's prompt at Start.").Sha256, posted.GetProperty("shA256").GetString());
    }

    // ------------------------------------------------------ findings 8, 9: the Start page ----

    private void ServeStartPage(string iterations, Func<HttpRequestMessage, HttpResponseMessage> postArtifact)
    {
        ServeVersion(locked: false, statusId: Json.Draft, iterations: iterations);
        Api.On(HttpMethod.Post, "/api/executions", HttpStatusCode.Created, Json.Execution(ended: false))
            .On(HttpMethod.Get, "/api/artifacts", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/api/artifacts", postArtifact);
    }

    private IRenderedComponent<ExecutionStart> StartWithEdit(string? edit)
    {
        var cut = Render<ExecutionStart>(p => p.Add(x => x.VersionId, 1));
        cut.WaitForAssertion(() => Assert.Contains("first execution of this version", cut.Markup));
        if (edit is not null)
        {
            cut.Find("#exec-prompt").Input(edit);
        }

        cut.Find("form").Submit();
        cut.FindAll("button").Single(b => b.TextContent == "Start").Click();
        return cut;
    }

    private static HttpResponseMessage ServerError() =>
        StubShepherdApi.Response(HttpStatusCode.InternalServerError, """{"title":"Internal Server Error","status":500,"traceId":"T-1"}""");

    [Fact]
    public void AFailedRegistration_KeepsTheOperatorOnTheStartPage_AndRetriesTheExactPromptAndProvenance()
    {
        var failing = true;
        ServeStartPage(Json.OneIteration, req => failing ? ServerError() : StubShepherdApi.Response(HttpStatusCode.Created, EchoArtifact(req)));

        var cut = StartWithEdit("Build the thing, tests first.");
        cut.WaitForAssertion(() => Assert.Contains("Execution #11 was created. Its start prompt was not registered.", cut.Markup));
        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.DoesNotContain("/executions/11", navigation.Uri);

        failing = false;
        cut.FindAll("button").Single(b => b.TextContent == "Retry registration").Click();

        cut.WaitForAssertion(() => Assert.EndsWith("/executions/11", navigation.Uri));
        var posted = PostedArtifact();
        Assert.Equal(new PromptText("Build the thing, tests first.").Sha256, posted.GetProperty("shA256").GetString());
        Assert.Equal(OverrideUri, posted.GetProperty("originalURI").GetString());
    }

    [Fact]
    public void AFailedRegistration_WithNoBrowserCopy_SaysTheTextIsHeldOnlyOnThisPage()
    {
        Services.AddSingleton<IDraftStore>(new ThrowingDraftStore());
        ServeStartPage(Json.OneIteration, _ => ServerError());

        var cut = StartWithEdit(edit: null);

        cut.WaitForAssertion(() => Assert.Contains("The prompt text is held only on this page", cut.Markup));
    }

    [Fact]
    public void AnUneditedCrlfIterationPrompt_IsRegisteredByteForByte()
    {
        const string crlfIteration =
            """[{"iterationID":7,"experimentVersionID":1,"sequence":1,"iterationLabel":"First","prompt":"Line one\r\nLine two","scenario":null,"notes":null}]""";
        ServeStartPage(crlfIteration, req => StubShepherdApi.Response(HttpStatusCode.Created, EchoArtifact(req)));

        var cut = StartWithEdit(edit: null);
        var navigation = Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => Assert.EndsWith("/executions/11", navigation.Uri));

        var posted = PostedArtifact();
        Assert.Equal(new PromptText("Line one\r\nLine two").Sha256, posted.GetProperty("shA256").GetString());
        Assert.Equal(IterationUri, posted.GetProperty("originalURI").GetString());
        var stored = Services.GetRequiredService<ArtifactFileStore>().ResolvePath(posted.GetProperty("storedURI").GetString())!;
        Assert.Equal(Encoding.UTF8.GetBytes("Line one\r\nLine two"), File.ReadAllBytes(stored));
    }

    private sealed class ThrowingDraftStore : IDraftStore
    {
        public Task<Draft?> LoadAsync(string key) => throw new InvalidOperationException("JavaScript interop calls cannot be issued at this time.");
        public Task SaveAsync(string key, string text) => throw new InvalidOperationException("JavaScript interop calls cannot be issued at this time.");
        public Task DeleteAsync(string key) => throw new InvalidOperationException("JavaScript interop calls cannot be issued at this time.");
    }
}
