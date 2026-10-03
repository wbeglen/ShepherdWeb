using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ShepherdWeb.Features.Executions;
using ShepherdWeb.Features.Iterations;
using ShepherdWeb.Infrastructure.Api.Generated;
using ShepherdWeb.Infrastructure.ArtifactStore;
using ShepherdWeb.Infrastructure.Drafts;
using ShepherdWeb.Tests.TestSupport;

namespace ShepherdWeb.Tests.Features;

/// <summary>
/// An execution-specific start prompt: the iteration's prompt is the default, and the operator may edit
/// it for one execution before Start. What is delivered is whatever was registered; the iteration is
/// never changed.
/// </summary>
public class StartPromptOverrideRuleTests
{
    private const string IterationPrompt = "Line one\r\nLine two";

    [Fact]
    public void AnUntouchedPrompt_IsTheIterationsPromptByteForByte()
    {
        var form = new ExecutionStartForm();

        var (text, overridden) = form.ResolvePrompt(IterationPrompt);

        Assert.False(overridden);
        Assert.Same(IterationPrompt, text); // never round-tripped through a textarea: "\r\n" survives
    }

    [Fact]
    public void AnEditedPrompt_IsAnOverride()
    {
        var form = new ExecutionStartForm { PromptEdit = "Something else." };

        var (text, overridden) = form.ResolvePrompt(IterationPrompt);

        Assert.True(overridden);
        Assert.Equal("Something else.", text);
    }

    [Fact]
    public void AnEditThatMatchesTheIterationExactly_IsNotAnOverride()
    {
        var form = new ExecutionStartForm { PromptEdit = IterationPrompt };

        Assert.False(form.ResolvePrompt(IterationPrompt).Overridden);
    }

    [Fact]
    public void AnEditThatDiffersOnlyInLineEndings_IsAnOverride()
    {
        // The bytes differ, so what is delivered is not the iteration's prompt.
        var form = new ExecutionStartForm { PromptEdit = "Line one\nLine two" };

        var (text, overridden) = form.ResolvePrompt(IterationPrompt);

        Assert.True(overridden);
        Assert.Equal("Line one\nLine two", text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void AnEmptyOrBlankPrompt_IsRejected(string edit)
    {
        var form = new ExecutionStartForm { IterationID = 7, AgentID = 1, PhaseID = 1, PromptEdit = edit };

        var results = form.Validate(new ValidationContext(form)).ToList();

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ExecutionStartForm.PromptEdit)));
    }

    [Fact]
    public void AnUntouchedOrEditedPrompt_ValidatesWhenTheRestOfTheFormIsValid()
    {
        var untouched = new ExecutionStartForm { IterationID = 7, AgentID = 1, PhaseID = 1 };
        Assert.Empty(untouched.Validate(new ValidationContext(untouched)));

        var edited = new ExecutionStartForm { IterationID = 7, AgentID = 1, PhaseID = 1, PromptEdit = "Real text." };
        Assert.Empty(edited.Validate(new ValidationContext(edited)));
    }

    [Theory]
    [InlineData("a", "a", false)]
    [InlineData("a", "b", true)]
    [InlineData("a", "A", true)]        // ordinal: case matters
    [InlineData("a ", "a", true)]       // so does trailing whitespace
    [InlineData(null, "", false)]
    [InlineData("x", null, true)]
    public void IsPromptOverride_IsAnExactOrdinalComparison(string? prompt, string? iteration, bool expected) =>
        Assert.Equal(expected, ExecutionRules.IsPromptOverride(prompt, iteration));

    [Fact]
    public void OverrideProvenance_IsRecognisedOnlyForThatExecutionsOwnPrompt()
    {
        Assert.True(StartExecutionWorkflow.IsStartPromptOverrideUri(11, "urn:goodshepherd:execution:11:prompt"));
        Assert.False(StartExecutionWorkflow.IsStartPromptOverrideUri(11, "urn:goodshepherd:execution:12:prompt"));
        Assert.False(StartExecutionWorkflow.IsStartPromptOverrideUri(11, "urn:goodshepherd:iteration:7:prompt"));
        Assert.False(StartExecutionWorkflow.IsStartPromptOverrideUri(11, "urn:goodshepherd:execution:11:additional-prompt"));
        Assert.False(StartExecutionWorkflow.IsStartPromptOverrideUri(11, null));
    }
}

public class StartPromptOverrideWorkflowTests : AppTestContext
{
    private StartExecutionWorkflow Workflow => Services.GetRequiredService<StartExecutionWorkflow>();

    private static CreateExecutionRequest Request => new()
    {
        IterationID = 7, AgentID = 1, PhaseID = 1, StatusID = 1, AttemptNumber = 1,
        StartUTC = new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc),
    };

    private void ServeStart(Func<HttpRequestMessage, HttpResponseMessage>? postArtifact = null) =>
        Api.On(HttpMethod.Post, "/api/executions", HttpStatusCode.Created, Json.Execution(ended: false))
            .On(HttpMethod.Get, "/api/artifacts", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/api/artifacts", postArtifact ?? (req => StubShepherdApi.Response(HttpStatusCode.Created, EchoArtifact(req))));

    private JsonElement RegisteredArtifactBody()
    {
        var body = Api.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/api/artifacts").Body!;
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [Fact]
    public async Task Start_WithAnOverride_RegistersTheEditedText_WithExecutionProvenance_AndNothingElse()
    {
        ServeStart();

        var result = await Workflow.StartAsync(Request, "Edited for this run.\nLine two", overridden: true);

        Assert.NotNull(result.PromptArtifact);
        var artifact = RegisteredArtifactBody(); // Single(): exactly one artifact was registered, not the iteration's too
        Assert.Equal(new PromptText("Edited for this run.\nLine two").Sha256, artifact.GetProperty("shA256").GetString());
        Assert.Equal("urn:goodshepherd:execution:11:prompt", artifact.GetProperty("originalURI").GetString());
        Assert.Equal("execution-11-prompt.txt", artifact.GetProperty("originalName").GetString());
        Assert.Equal(Json.DeliveredPromptType, artifact.GetProperty("artifactTypeID").GetInt32());
        Assert.Equal("text/plain; charset=utf-8", artifact.GetProperty("mimeType").GetString());

        var stored = Services.GetRequiredService<ArtifactFileStore>().ResolvePath(artifact.GetProperty("storedURI").GetString())!;
        Assert.Equal(Encoding.UTF8.GetBytes("Edited for this run.\nLine two"), await File.ReadAllBytesAsync(stored));
    }

    [Fact]
    public async Task Start_WithoutAnOverride_KeepsTheIterationsProvenance()
    {
        ServeStart();

        await Workflow.StartAsync(Request, "Build the thing.");

        var artifact = RegisteredArtifactBody();
        Assert.Equal("urn:goodshepherd:iteration:7:prompt", artifact.GetProperty("originalURI").GetString());
        Assert.Equal("iteration-7-prompt.txt", artifact.GetProperty("originalName").GetString());
    }

    [Fact]
    public async Task TheExecutionCreateRequest_CarriesNoPromptText()
    {
        ServeStart();

        await Workflow.StartAsync(Request, "Edited for this run.", overridden: true);

        var create = Api.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/api/executions").Body!;
        Assert.DoesNotContain("prompt", create, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Edited for this run.", create);
    }

    [Fact]
    public async Task TheExactPrompt_IsKeptInTheBrowserUntilItIsRegistered_ThenForgotten()
    {
        ServeStart(_ => StubShepherdApi.Response(HttpStatusCode.InternalServerError, """{"title":"Internal Server Error","status":500}"""));

        var failed = await Workflow.StartAsync(Request, "Edited\r\nfor this run.", overridden: true);

        Assert.Null(failed.PromptArtifact);
        Assert.Equal("Edited\r\nfor this run.", Drafts.Items["execution:11:start-prompt"].Text); // exact, including \r\n

        // The retry registers those exact bytes, with override provenance, and clears the saved copy.
        Api.On(HttpMethod.Post, "/api/artifacts", req => StubShepherdApi.Response(HttpStatusCode.Created, EchoArtifact(req)));
        await Workflow.RegisterStartPromptAsync(11, 7, Drafts.Items["execution:11:start-prompt"].Text, overridden: true);

        var registered = JsonDocument.Parse(Api.Requests.Last(r => r.Method == HttpMethod.Post && r.Path == "/api/artifacts").Body!).RootElement;
        Assert.Equal(new PromptText("Edited\r\nfor this run.").Sha256, registered.GetProperty("shA256").GetString());
        Assert.Equal("urn:goodshepherd:execution:11:prompt", registered.GetProperty("originalURI").GetString());
        Assert.DoesNotContain("execution:11:start-prompt", Drafts.Items.Keys);
    }

    [Fact]
    public async Task ARetry_ReusesAnOverrideThatWasAlreadyRegistered()
    {
        var sha = new PromptText("Edited for this run.").Sha256;
        Api.On(HttpMethod.Get, "/api/artifacts", HttpStatusCode.OK,
            $$"""[{"artifactID":5,"executionID":11,"artifactTypeID":1,"shA256":"{{sha}}","receivedUTC":"2026-09-02T09:00:00","byteLength":20}]""");

        var artifact = await Workflow.RegisterStartPromptAsync(11, 7, "Edited for this run.", overridden: true);

        Assert.Equal(5, artifact.ArtifactID);
        Assert.DoesNotContain(Api.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task AnUnavailableBrowserStore_DoesNotStopTheStart()
    {
        Services.AddSingleton<IDraftStore>(new ThrowingDraftStore()); // replaces the in-memory store before first use
        ServeStart();

        var result = await Workflow.StartAsync(Request, "Edited for this run.", overridden: true);

        Assert.NotNull(result.PromptArtifact);
        Assert.Null(result.RegistrationError);
    }

    [Fact]
    public async Task AnAdditionalInterventionPrompt_IsUnchanged()
    {
        Api.On(HttpMethod.Get, "/api/artifacts", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/api/artifacts", req => StubShepherdApi.Response(HttpStatusCode.Created, EchoArtifact(req)));

        await Workflow.RegisterAdditionalPromptAsync(11, "One more thing.");

        var artifact = RegisteredArtifactBody();
        Assert.Equal("urn:goodshepherd:execution:11:additional-prompt", artifact.GetProperty("originalURI").GetString());
        Assert.Equal("execution-11-additional-prompt.txt", artifact.GetProperty("originalName").GetString());
        Assert.False(StartExecutionWorkflow.IsStartPromptOverrideUri(11, artifact.GetProperty("originalURI").GetString()));
    }

    private sealed class ThrowingDraftStore : IDraftStore
    {
        public Task<Draft?> LoadAsync(string key) => throw new InvalidOperationException("JavaScript interop calls cannot be issued at this time.");
        public Task SaveAsync(string key, string text) => throw new InvalidOperationException("JavaScript interop calls cannot be issued at this time.");
        public Task DeleteAsync(string key) => throw new InvalidOperationException("JavaScript interop calls cannot be issued at this time.");
    }

    private static string EchoArtifact(HttpRequestMessage request)
    {
        using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
        var r = body.RootElement;
        return $$"""{"artifactID":5,"executionID":11,"artifactTypeID":{{r.GetProperty("artifactTypeID").GetInt32()}},"originalName":"p","storedURI":{{r.GetProperty("storedURI").GetRawText()}},"mimeType":"text/plain","byteLength":{{r.GetProperty("byteLength").GetInt64()}},"shA256":"{{r.GetProperty("shA256").GetString()}}","receivedUTC":"2026-09-02T09:00:00"}""";
    }
}
