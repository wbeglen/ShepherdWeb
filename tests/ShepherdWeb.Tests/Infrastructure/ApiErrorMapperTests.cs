using System.Net;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging.Abstractions;
using ShepherdWeb.Infrastructure.Api;
using ShepherdWeb.Infrastructure.Api.Generated;
using ShepherdWeb.Tests.TestSupport;

namespace ShepherdWeb.Tests.Infrastructure;

/// <summary>
/// Response bodies below are the shapes ShepherdAPI's ExceptionHandlingMiddleware produces.
/// Each case goes through a real generated client so the exception type is the real one.
/// </summary>
public class ApiErrorMapperTests
{
    private readonly ApiErrorMapper _mapper = new(NullLogger<ApiErrorMapper>.Instance);

    private async Task<ApiProblem> ProblemFor(HttpStatusCode status, string? body)
    {
        var api = new StubShepherdApi().On(HttpMethod.Get, "/api/statuses", status, body);
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => new StatusesClient(api.CreateHttpClient()).StatusesGetAsync());
        Assert.True(ApiErrorMapper.IsApiFailure(ex));
        return _mapper.Map(ex);
    }

    [Fact]
    public async Task BadRequest_MapsFieldErrors()
    {
        var problem = await ProblemFor(HttpStatusCode.BadRequest,
            """{"title":"Validation Failed","status":400,"errors":{"maxScore":["MaxScore must be greater than MinScore."]}}""");

        Assert.Equal(ApiProblemKind.Validation, problem.Kind);
        Assert.Equal(["MaxScore must be greater than MinScore."], problem.FieldErrors["maxScore"]);
    }

    [Fact]
    public async Task Conflict_WithRecordLockedTitle_IsRecordLocked()
    {
        var problem = await ProblemFor(HttpStatusCode.Conflict,
            """{"title":"Record Locked","status":409,"detail":"ExperimentVersion 1 was locked at 2026-09-01."}""");

        Assert.Equal(ApiProblemKind.RecordLocked, problem.Kind);
        Assert.Equal("ExperimentVersion 1 was locked at 2026-09-01.", problem.Detail);
    }

    [Fact]
    public async Task Conflict_Otherwise_IsConflict()
    {
        var problem = await ProblemFor(HttpStatusCode.Conflict,
            """{"title":"Conflict","status":409,"detail":"A record with the same unique key already exists."}""");

        Assert.Equal(ApiProblemKind.Conflict, problem.Kind);
    }

    [Theory]
    [InlineData("version-locked", "Version Locked")]
    [InlineData("design-frozen", "Design Frozen")]
    [InlineData("execution-run-facts-frozen", "Execution Run-Facts Frozen")]
    public async Task Conflict_WithALockCode_IsRecordLocked_AndKeepsTheApiTitle(string code, string title)
    {
        var problem = await ProblemFor(HttpStatusCode.Conflict,
            $$"""{"title":"{{title}}","status":409,"detail":"No longer changeable.","code":"{{code}}"}""");

        Assert.Equal(ApiProblemKind.RecordLocked, problem.Kind);
        Assert.Equal(title, problem.Title);
        Assert.Equal(code, problem.Code);
        Assert.Equal("No longer changeable.", problem.Detail);
    }

    [Theory]
    [InlineData("duplicate-observation", "Duplicate Observation")]
    [InlineData("invalid-rule-configuration", "Invalid Rule Configuration")]
    [InlineData("finalize-execution-not-ended", "Finalization Failed: Execution Not Ended")]
    [InlineData("finalize-phase-weight-invalid", "Finalization Failed: Phase Weight Total Invalid")]
    [InlineData("lock-requires-finalization", "Lock Requires Finalization")]
    public async Task Conflict_WithAnyOtherCode_IsConflict_AndKeepsTheApiTitle(string code, string title)
    {
        var problem = await ProblemFor(HttpStatusCode.Conflict,
            $$"""{"title":"{{title}}","status":409,"detail":"Why.","code":"{{code}}"}""");

        Assert.Equal(ApiProblemKind.Conflict, problem.Kind);
        Assert.Equal(title, problem.Title);
        Assert.Equal(code, problem.Code);
        Assert.False(problem.Retryable);
    }

    [Fact]
    public async Task Conflict_TheRecordLockedTitle_WinsOnlyWithoutACode()
    {
        // A code always decides; the old title is a fallback for responses that have none.
        var problem = await ProblemFor(HttpStatusCode.Conflict,
            """{"title":"Record Locked","status":409,"code":"duplicate-key"}""");

        Assert.Equal(ApiProblemKind.Conflict, problem.Kind);
    }

    [Fact]
    public async Task RetryableConflict_IsMarkedRetryable()
    {
        var problem = await ProblemFor(HttpStatusCode.Conflict,
            """{"title":"Concurrent Operation Conflict","status":409,"detail":"Try again.","code":"retryable-conflict","retryable":true}""");

        Assert.Equal(ApiProblemKind.Conflict, problem.Kind);
        Assert.True(problem.Retryable);
    }

    [Fact]
    public async Task DeclaredConflict_FromTheTypedException_IsMappedFromItsBody()
    {
        // Score-rule delete declares 409 ProblemDetails, so the client throws ApiException<ProblemDetails>.
        var api = new StubShepherdApi().On(HttpMethod.Delete, "/api/score-rules/5", HttpStatusCode.Conflict,
            """{"title":"Design Frozen","status":409,"detail":"ExperimentVersion 1 has executions.","code":"design-frozen"}""");
        var ex = await Assert.ThrowsAsync<ApiException<ProblemDetails>>(() => new ScoreRulesClient(api.CreateHttpClient()).ScoreRulesDeleteAsync(5));

        var problem = _mapper.Map(ex);

        Assert.Equal(ApiProblemKind.RecordLocked, problem.Kind);
        Assert.Equal("design-frozen", problem.Code);
        Assert.Equal("ExperimentVersion 1 has executions.", problem.Detail);
    }

    [Fact]
    public async Task Conflict_WithAnUnparsableBody_IsAGenericConflict()
    {
        var problem = await ProblemFor(HttpStatusCode.Conflict, "<html>proxy error</html>");

        Assert.Equal(ApiProblemKind.Conflict, problem.Kind);
        Assert.Equal("Conflict", problem.Title);
        Assert.Null(problem.Code);
    }

    [Fact]
    public async Task NotFound_IsNotFound()
    {
        var problem = await ProblemFor(HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"Agent 9 was not found."}""");

        Assert.Equal(ApiProblemKind.NotFound, problem.Kind);
        Assert.Equal("Agent 9 was not found.", problem.Detail);
    }

    [Fact]
    public async Task ServerError_CarriesTraceId()
    {
        var problem = await ProblemFor(HttpStatusCode.InternalServerError,
            """{"title":"Internal Server Error","status":500,"detail":"An unexpected error occurred.","traceId":"0HN7:00000001"}""");

        Assert.Equal(ApiProblemKind.ServerError, problem.Kind);
        Assert.Equal("0HN7:00000001", problem.TraceId);
    }

    [Fact]
    public async Task NonJsonBody_StillMapsByStatus()
    {
        var api = new StubShepherdApi().On(HttpMethod.Get, "/api/statuses",
            _ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>proxy</html>") });
        var ex = await Assert.ThrowsAsync<ApiException>(() => new StatusesClient(api.CreateHttpClient()).StatusesGetAsync());

        Assert.Equal(ApiProblemKind.ServerError, _mapper.Map(ex).Kind);
    }

    [Fact]
    public async Task ConnectionFailure_IsUnreachable()
    {
        var api = new StubShepherdApi { Unreachable = true };
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => new StatusesClient(api.CreateHttpClient()).StatusesGetAsync());

        Assert.True(ApiErrorMapper.IsApiFailure(ex));
        Assert.Equal(ApiProblemKind.Unreachable, _mapper.Map(ex).Kind);
    }

    [Fact]
    public void ProgrammingErrors_AreNotApiFailures() =>
        Assert.False(ApiErrorMapper.IsApiFailure(new InvalidOperationException()));
}

public class EditContextProblemExtensionsTests
{
    private sealed class RuleForm
    {
        public int MinScore { get; set; }
        public int MaxScore { get; set; }
    }

    [Theory]
    [InlineData("maxScore")]
    [InlineData("MaxScore")]
    [InlineData("$.maxScore")]
    [InlineData("request.MaxScore")]
    public void FieldErrors_LandOnTheMatchingProperty(string key)
    {
        var form = new RuleForm();
        var context = new EditContext(form);
        var store = new ValidationMessageStore(context);

        store.AddApiErrors(context, Validation(key, "MaxScore must be greater than MinScore."));

        Assert.Equal(["MaxScore must be greater than MinScore."], context.GetValidationMessages(() => form.MaxScore));
    }

    [Fact]
    public void UnmatchedField_BecomesModelLevelMessage_NamingTheField()
    {
        var form = new RuleForm();
        var context = new EditContext(form);
        var store = new ValidationMessageStore(context);

        store.AddApiErrors(context, Validation("phaseId", "Unknown phase."));

        Assert.Equal(["phaseId: Unknown phase."], context.GetValidationMessages(new FieldIdentifier(form, string.Empty)));
    }

    [Fact]
    public void ProblemWithoutFieldErrors_ShowsItsDetailAtModelLevel()
    {
        var form = new RuleForm();
        var context = new EditContext(form);
        var store = new ValidationMessageStore(context);

        store.AddApiErrors(context, new ApiProblem(ApiProblemKind.Conflict, "Conflict", "Duplicate phase and category."));

        Assert.Equal(["Duplicate phase and category."], context.GetValidationMessages());
    }

    private static ApiProblem Validation(string key, string message) =>
        new(ApiProblemKind.Validation, "Validation Failed", null, 400, FieldErrors: new Dictionary<string, string[]> { [key] = [message] });
}
