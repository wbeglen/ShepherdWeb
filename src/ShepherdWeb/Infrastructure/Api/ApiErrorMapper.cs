using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShepherdWeb.Infrastructure.Api.Generated;

namespace ShepherdWeb.Infrastructure.Api;

/// <summary>
/// Turns a failed ShepherdAPI call into an <see cref="ApiProblem"/> and logs it
/// (docs/ARCHITECTURE.md AD-6). ShepherdAPI's OpenAPI document only declares 200 responses, so
/// the generated client throws a plain <see cref="ApiException"/> whose Response is the raw
/// RFC 7807 ProblemDetails body; this class parses it.
///
/// Typical use in a component:
/// <code>
/// try { ... await client.SomethingAsync(...); }
/// catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex)) { problem = Errors.Map(ex); }
/// </code>
/// </summary>
public sealed class ApiErrorMapper(ILogger<ApiErrorMapper> logger)
{
    // ShepherdAPI's ExceptionHandlingMiddleware uses this exact title for its lock rejections.
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

        return status switch
        {
            (int)HttpStatusCode.BadRequest => new ApiProblem(
                ApiProblemKind.Validation, title ?? "Validation failed", detail, status, body?.TraceId,
                body?.Errors ?? new Dictionary<string, string[]>()),
            (int)HttpStatusCode.NotFound => new ApiProblem(
                ApiProblemKind.NotFound, "Not found", detail ?? "The requested record no longer exists.", status, body?.TraceId),
            (int)HttpStatusCode.Conflict when title == RecordLockedTitle => new ApiProblem(
                ApiProblemKind.RecordLocked, "Record locked", detail, status, body?.TraceId),
            (int)HttpStatusCode.Conflict => new ApiProblem(
                ApiProblemKind.Conflict, "Conflict", detail, status, body?.TraceId),
            >= 500 => new ApiProblem(
                ApiProblemKind.ServerError, "ShepherdAPI error", detail ?? "ShepherdAPI failed to process the request.", status, body?.TraceId),
            _ => new ApiProblem(
                ApiProblemKind.Unexpected, title ?? $"Unexpected response ({status})", detail, status, body?.TraceId),
        };
    }

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

        // ShepherdAPI puts traceId in ProblemDetails.Extensions, which serializes as a top-level property.
        [JsonPropertyName("traceId")]
        public string? TraceId { get; set; }
    }
}
