using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShepherdWeb.Features.Executions;
using ShepherdWeb.Features.ExperimentVersions;
using ShepherdWeb.Features.Iterations;
using ShepherdWeb.Features.Scoring;
using ShepherdWeb.Infrastructure;
using ShepherdWeb.Infrastructure.Api.Generated;
using ShepherdWeb.Infrastructure.ArtifactStore;
using ShepherdWeb.Tests.TestSupport;

namespace ShepherdWeb.Tests.Features;

public class ExecutionRulesTests
{
    private static readonly ConventionsOptions Conventions = new();

    private static VersionConfiguration Config(bool locked, int iterations = 1) => new(
        new ExperimentVersion { ExperimentVersionID = 1, LockedUTC = locked ? DateTime.UtcNow : null },
        Enumerable.Range(1, iterations).Select(i => new Iteration { IterationID = i }).ToList(),
        [], [], []);

    [Theory]
    [InlineData(false, "Draft", 1, null)]
    [InlineData(false, "Complete", 1, null)]          // Complete is not an immutability boundary (Q3)
    [InlineData(true, "Locked", 1, "locked")]
    [InlineData(false, "Retired", 1, "Retired")]        // Q3: no new executions for Retired
    [InlineData(false, "Locked", 1, "mismatch")]        // status says Locked without a lock
    [InlineData(false, "Draft", 0, "no iterations")]
    public void StartBlockedReason_FollowsTheLifecycle(bool locked, string status, int iterations, string? expected)
    {
        var reason = ExecutionRules.StartBlockedReason(Config(locked, iterations), status, Conventions);

        if (expected is null)
        {
            Assert.Null(reason);
        }
        else
        {
            Assert.Contains(expected, reason);
        }
    }

    [Fact]
    public void NextAttemptNumber_CountsOnlyTheSameIterationAgentAndPhase()
    {
        Execution[] executions =
        [
            new() { IterationID = 1, AgentID = 1, PhaseID = 1, AttemptNumber = 1 },
            new() { IterationID = 1, AgentID = 1, PhaseID = 1, AttemptNumber = 2 },
            new() { IterationID = 1, AgentID = 2, PhaseID = 1, AttemptNumber = 5 },
        ];

        Assert.Equal(3, ExecutionRules.NextAttemptNumber(executions, 1, 1, 1));
        Assert.Equal(1, ExecutionRules.NextAttemptNumber(executions, 1, 1, 2));
    }

    [Fact]
    public void Evaluation_RequiresAnEndedExecution_OnAnUnlockedVersion()
    {
        var ended = new Execution { EndUTC = DateTime.UtcNow };
        var running = new Execution();
        var unlocked = new ExperimentVersion();
        var locked = new ExperimentVersion { LockedUTC = DateTime.UtcNow };

        Assert.True(ExecutionRules.CanEvaluate(ended, unlocked));   // Q5: after the end; completing does not freeze scoring
        Assert.False(ExecutionRules.CanEvaluate(running, unlocked));
        Assert.False(ExecutionRules.CanEvaluate(ended, locked));    // Lock is final
        Assert.True(ExecutionRules.CanEditRunFacts(running, unlocked));
        Assert.False(ExecutionRules.CanEditRunFacts(ended, unlocked)); // EndUTC freezes run facts (Q2)
        Assert.False(ExecutionRules.CanEditRunFacts(running, locked));
    }

    [Fact]
    public void EndState_DistinguishesNotRecorded_AcknowledgedUnknown_AndAMissingAcknowledgment()
    {
        var ended = new Execution { ExecutionID = 1, EndUTC = DateTime.UtcNow };
        var acknowledged = new Execution { ExecutionID = 2 };
        var bare = new Execution { ExecutionID = 3 };
        VersionConfiguration Config(bool locked, params FinalizationException[] exceptions) => new(
            new ExperimentVersion { LockedUTC = locked ? DateTime.UtcNow : null }, [], [], [], [ended, acknowledged, bare], exceptions);

        var unlocked = Config(locked: false);
        Assert.Equal(ExecutionEndState.Ended, ExecutionRules.EndStateOf(ended, unlocked));
        Assert.Equal(ExecutionEndState.NotRecorded, ExecutionRules.EndStateOf(acknowledged, unlocked)); // not "in progress"

        var locked = Config(locked: true, new FinalizationException { ExecutionID = 2, Explanation = "unknown" });
        Assert.Equal(ExecutionEndState.Ended, ExecutionRules.EndStateOf(ended, locked));
        Assert.Equal(ExecutionEndState.UnknownAcknowledged, ExecutionRules.EndStateOf(acknowledged, locked));
        Assert.Equal(ExecutionEndState.MissingAcknowledgment, ExecutionRules.EndStateOf(bare, locked)); // integrity warning
    }

    [Theory]
    [InlineData(null, "Enter a score")]
    [InlineData("0.99", "between 1 and 10")]
    [InlineData("10.01", "between 1 and 10")]
    [InlineData("7.125", "two decimal")]
    [InlineData("7.25", null)]
    [InlineData("10", null)]
    public void ScoreProblem_ChecksRangeAndPrecision(string? score, string? expected)
    {
        var rule = new ScoreRule { MinScore = 1, MaxScore = 10 };
        var problem = ExecutionRules.ScoreProblem(score is null ? null : decimal.Parse(score), rule);

        if (expected is null)
        {
            Assert.Null(problem);
        }
        else
        {
            Assert.Contains(expected, problem);
        }
    }

    [Fact]
    public void RulesFor_UsesEveryRuleOfTheExecutionsPhase()
    {
        ScoreRule[] rules =
        [
            new() { ScoreRuleID = 1, PhaseID = 1, Active = true },
            new() { ScoreRuleID = 2, PhaseID = 2, Active = true },
            new() { ScoreRuleID = 3, PhaseID = 1, Active = false },
        ];

        // ScoreRule.Active is ignored, as it is by ShepherdAPI (Issue #7).
        Assert.Equal([1, 3], ExecutionRules.RulesFor(new Execution { PhaseID = 1 }, rules).Select(r => r.ScoreRuleID));
    }
}

public class RunFactsAndScoreModelTests
{
    [Fact]
    public void Ending_RequiresAnEndNotBeforeTheStart()
    {
        var start = new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc);
        var model = RunFactsModel.From(new Execution { StartUTC = start }, ending: true);

        model.EndUtc = new DateTime(2026, 9, 2, 8, 59, 0); // entered as UTC, unspecified kind
        Assert.Contains(Validate(model), r => r.ErrorMessage!.Contains("before the start"));

        model.EndUtc = new DateTime(2026, 9, 2, 10, 0, 0);
        Assert.Empty(Validate(model));
        Assert.Equal(new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc), model.ToRequest().EndUTC);
    }

    [Fact]
    public void Editing_NeverSendsAnEnd()
    {
        var model = RunFactsModel.From(new Execution { StartUTC = DateTime.UtcNow, ModelUsed = "m" }, ending: false);

        Assert.Empty(Validate(model));
        Assert.Null(model.ToRequest().EndUTC);
        Assert.Equal("m", model.ToRequest().ModelUsed); // the full set is always sent
    }

    [Fact]
    public void ScoreRow_CorrectionKeepsTheOriginalObservationTime()
    {
        var observed = new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc);
        var row = new ScoreRow(new ScoreRule { MinScore = 1, MaxScore = 10 },
            new ScoreObservation { ObservedScore = 7, ObservedUTC = observed });

        Assert.False(row.IsDirty);
        row.Score = 8.5m;
        Assert.True(row.IsDirty);
        Assert.Equal(observed, row.ToUpdateRequest().ObservedUTC);
        Assert.Equal(8.5, row.ToUpdateRequest().ObservedScore);
    }

    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}

public sealed class ArtifactFileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shepherdweb-tests", Guid.NewGuid().ToString("N"));
    private readonly ArtifactFileStore _store;

    public ArtifactFileStoreTests() => _store = new ArtifactFileStore(Options.Create(new ArtifactStoreOptions { RootPath = _root }));

    [Fact]
    public async Task Store_HashesWhileWriting_ToAContentAddressedPath()
    {
        var stored = await _store.StoreAsync(11, new MemoryStream("abc"u8.ToArray()), ".TXT");

        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", stored.Sha256);
        Assert.Equal(3, stored.ByteLength);
        var path = _store.ResolvePath(stored.StoredUri)!;
        Assert.Equal(Path.Combine(_root, "executions", "11", stored.Sha256 + ".txt"), path);
        Assert.Equal("abc", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "incoming"))); // no temp files left
    }

    [Fact]
    public async Task StoringTheSameContentAgain_ReusesTheFile()
    {
        var first = await _store.StoreAsync(11, new MemoryStream("abc"u8.ToArray()), ".txt");
        var second = await _store.StoreAsync(11, new MemoryStream("abc"u8.ToArray()), ".txt");

        Assert.Equal(first.StoredUri, second.StoredUri);
    }

    [Fact]
    public async Task ExistingFileWithDifferentContent_IsNeverOverwritten()
    {
        var stored = await _store.StoreAsync(11, new MemoryStream("abc"u8.ToArray()), ".txt");
        var path = _store.ResolvePath(stored.StoredUri)!;
        await File.WriteAllTextAsync(path, "tampered");

        await Assert.ThrowsAsync<IOException>(() => _store.StoreAsync(11, new MemoryStream("abc"u8.ToArray()), ".txt"));
        Assert.Equal("tampered", await File.ReadAllTextAsync(path)); // left untouched as evidence
    }

    [Fact]
    public void ResolvePath_RefusesAnythingOutsideTheStore()
    {
        Assert.Null(_store.ResolvePath(new Uri(Path.Combine(Path.GetTempPath(), "elsewhere.txt")).AbsoluteUri));
        Assert.Null(_store.ResolvePath(new Uri(Path.Combine(_root, "..", "escape.txt")).AbsoluteUri));
        Assert.Null(_store.ResolvePath("https://example.com/file.txt"));
        Assert.Null(_store.ResolvePath(null));
    }

    [Fact]
    public async Task Extension_IsSanitized()
    {
        var stored = await _store.StoreAsync(11, new MemoryStream("x"u8.ToArray()), "../../evil.exe/..");

        Assert.StartsWith(Path.Combine(_root, "executions", "11"), _store.ResolvePath(stored.StoredUri)!);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

public class StartExecutionWorkflowTests : AppTestContext
{
    private StartExecutionWorkflow Workflow => Services.GetRequiredService<StartExecutionWorkflow>();

    private static CreateExecutionRequest Request => new()
    {
        IterationID = 7, AgentID = 1, PhaseID = 1, StatusID = 1, AttemptNumber = 1,
        StartUTC = new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Start_CreatesTheExecution_ThenRegistersTheExactPrompt()
    {
        Api.On(HttpMethod.Post, "/api/executions", HttpStatusCode.Created, Json.Execution(ended: false))
            .On(HttpMethod.Get, "/api/artifacts", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/api/artifacts", req => StubShepherdApi.Response(HttpStatusCode.Created, EchoArtifact(req)));

        var result = await Workflow.StartAsync(Request, "Line one\nLine two é");

        Assert.NotNull(result.PromptArtifact);
        Assert.Null(result.RegistrationError);
        var posts = Api.Requests.Where(r => r.Method == HttpMethod.Post).Select(r => r.Path).ToList();
        Assert.Equal(["/api/executions", "/api/artifacts"], posts); // execution first, then the prompt

        using var body = JsonDocument.Parse(Api.Requests.Last(r => r.Path == "/api/artifacts").Body!);
        var root = body.RootElement;
        Assert.Equal(new PromptText("Line one\nLine two é").Sha256, root.GetProperty("shA256").GetString());
        Assert.Equal(Json.DeliveredPromptType, root.GetProperty("artifactTypeID").GetInt32());
        Assert.Equal("urn:goodshepherd:iteration:7:prompt", root.GetProperty("originalURI").GetString());
        Assert.Equal("text/plain; charset=utf-8", root.GetProperty("mimeType").GetString());

        var stored = Services.GetRequiredService<ArtifactFileStore>().ResolvePath(root.GetProperty("storedURI").GetString())!;
        Assert.Equal(Encoding.UTF8.GetBytes("Line one\nLine two é"), await File.ReadAllBytesAsync(stored)); // exact bytes, no BOM
    }

    [Fact]
    public async Task Start_WhenPromptRegistrationFails_ReportsItButKeepsTheExecution()
    {
        Api.On(HttpMethod.Post, "/api/executions", HttpStatusCode.Created, Json.Execution(ended: false))
            .On(HttpMethod.Get, "/api/artifacts", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/api/artifacts", HttpStatusCode.InternalServerError, """{"title":"Internal Server Error","status":500}""");

        var result = await Workflow.StartAsync(Request, "Build the thing.");

        Assert.Equal(11, result.Execution.ExecutionID);
        Assert.Null(result.PromptArtifact);
        Assert.NotNull(result.RegistrationError);
    }

    [Fact]
    public async Task RetryingTheStartPrompt_ReusesAnAlreadyRegisteredArtifact()
    {
        var sha = new PromptText("Build the thing.").Sha256;
        Api.On(HttpMethod.Get, "/api/artifacts", HttpStatusCode.OK,
            $$"""[{"artifactID":5,"executionID":11,"artifactTypeID":1,"originalURI":"urn:goodshepherd:iteration:7:prompt","shA256":"{{sha}}","receivedUTC":"2026-09-02T09:00:00","byteLength":16}]""");

        var artifact = await Workflow.RegisterStartPromptAsync(PendingStartPrompt.For(
            new Execution { ExecutionID = 11, IterationID = 7, StartUTC = new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc) },
            "Build the thing.", overridden: false));

        Assert.Equal(5, artifact.ArtifactID);
        Assert.DoesNotContain(Api.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task MissingDeliveredPromptType_IsReportedAsARegistrationFailure()
    {
        Api.On(HttpMethod.Get, "/api/artifact-types", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/api/executions", HttpStatusCode.Created, Json.Execution(ended: false));

        var result = await Workflow.StartAsync(Request, "Build the thing.");

        Assert.IsType<ShepherdWeb.Features.Artifacts.MissingConventionException>(result.RegistrationError);
    }

    private static string EchoArtifact(HttpRequestMessage request)
    {
        using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
        var r = body.RootElement;
        return $$"""{"artifactID":5,"executionID":11,"artifactTypeID":{{r.GetProperty("artifactTypeID").GetInt32()}},"originalName":"p","storedURI":{{r.GetProperty("storedURI").GetRawText()}},"mimeType":"text/plain","byteLength":{{r.GetProperty("byteLength").GetInt64()}},"shA256":"{{r.GetProperty("shA256").GetString()}}","receivedUTC":"2026-09-02T09:00:00"}""";
    }
}
