using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShepherdWeb.Infrastructure.Api.Generated;

namespace ShepherdWeb.Infrastructure.Api;

/// <summary>
/// Turns a failed ShepherdAPI call into an <see cref="ApiProblem"/> and logs it
/// (docs/ARCHITECTURE.md AD-6). The generated client throws <see cref="ApiException"/> (or
/// <c>ApiException&lt;ProblemDetails&gt;</c> where the contract declares the error) whose Response is the
/// raw RFC 7807 ProblemDetails body (GeneratedClientCustomization turns ReadResponseAsString on);
/// this class parses it. ShepherdAPI gives every error a stable <c>code</c>, which decides the kind.
///
/// Typical use in a component:
/// <code>
/// try { ... await client.SomethingAsync(...); }
/// catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex)) { problem = Errors.Map(ex); }
/// </code>
/// </summary>
public sealed class ApiErrorMapper(ILogger<ApiErrorMapper> logger)
{
    // ShepherdAPI codes meaning "this record can no longer change": the version is locked, its design
    // is frozen by its first execution, or the execution's run facts are frozen by EndUTC. "record-locked"
    // and the "Record Locked" title are what ShepherdAPI sent before Issues #1-#8; kept as a fallback.
    private static readonly HashSet<string> LockedCodes =
        ["version-locked", "design-frozen", "execution-run-facts-frozen", "record-locked"];
    private const string RecordLockedTitle = "Record Locked";

    /// <summary>True for the failures this mapper understands. Anything else is a bug and should reach the ErrorBoundary.</summary>
    public static bool IsApiFailure(Exception exception) =>
        exception is ApiException or HttpRequestException
        || (exception is TaskCanceledException tce && tce.InnerException is TimeoutException);

    public ApiProblem Map(Exception exception)
    {
        var problem = exception switch
        {
            ApiException api => FromApiException(api),
            HttpRequestException => Unreachable("ShepherdAPI could not be reached. Is it running?"),
            TaskCanceledException => Unreachable("ShepherdAPI did not respond in time."),
            _ => throw new ArgumentException($"Not an API failure: {exception.GetType().Name}", nameof(exception)),
        };

        logger.LogWarning(exception,
            "ShepherdAPI call failed: {Kind} {StatusCode} {Title} (TraceId: {TraceId})",
            problem.Kind, problem.StatusCode, problem.Title, problem.TraceId);
        return problem;
    }

    private static ApiProblem FromApiException(ApiException exception)
    {
        var body = TryParse(exception.Response);
        var status = exception.StatusCode;
        var title = body?.Title;
        var detail = body?.Detail;
        var code = body?.Code;

        var problem = status switch
        {
            (int)HttpStatusCode.BadRequest => new ApiProblem(
                ApiProblemKind.Validation, title ?? "Validation failed", detail, status, body?.TraceId,
                body?.Errors ?? new Dictionary<string, string[]>()),
            (int)HttpStatusCode.NotFound => new ApiProblem(
                ApiProblemKind.NotFound, "Not found", detail ?? "The requested record no longer exists.", status, body?.TraceId),
            (int)HttpStatusCode.Conflict when IsLocked(code, title) => new ApiProblem(
                ApiProblemKind.RecordLocked, title ?? "Record locked", detail, status, body?.TraceId),
            // ShepherdAPI's title names the conflict (e.g. "Finalization Failed: Execution Not Ended").
            (int)HttpStatusCode.Conflict => new ApiProblem(
                ApiProblemKind.Conflict, title ?? "Conflict", detail, status, body?.TraceId),
            >= 500 => new ApiProblem(
                ApiProblemKind.ServerError, "ShepherdAPI error", detail ?? "ShepherdAPI failed to process the request.", status, body?.TraceId),
            _ => new ApiProblem(
                ApiProblemKind.Unexpected, title ?? $"Unexpected response ({status})", detail, status, body?.TraceId),
        };

        return problem with { Code = code, Retryable = body?.Retryable ?? false };
    }

    private static bool IsLocked(string? code, string? title) =>
        code is not null ? LockedCodes.Contains(code) : title == RecordLockedTitle;

    private static ApiProblem Unreachable(string detail) =>
        new(ApiProblemKind.Unreachable, "ShepherdAPI unreachable", detail);

    private static ProblemBody? TryParse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProblemBody>(response, JsonOptions);
        }
        catch (JsonException)
        {
            return null; // Not ProblemDetails (e.g. a proxy error page); status-based mapping still applies.
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class ProblemBody
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }

        // ShepherdAPI's stable error code and retry hint (ProblemDetails extensions, top-level properties).
        public string? Code { get; set; }
        public bool? Retryable { get; set; }

        // ShepherdAPI puts traceId in ProblemDetails.Extensions, which serializes as a top-level property.
        [JsonPropertyName("traceId")]
        public string? TraceId { get; set; }
    }
}
