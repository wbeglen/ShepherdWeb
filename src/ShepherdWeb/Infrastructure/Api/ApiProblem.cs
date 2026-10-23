namespace ShepherdWeb.Infrastructure.Api;

public enum ApiProblemKind
{
    /// <summary>400: ShepherdAPI rejected the input. <see cref="ApiProblem.FieldErrors"/> says why.</summary>
    Validation,
    /// <summary>404.</summary>
    NotFound,
    /// <summary>
    /// 409: the record can no longer change: the version is locked (<c>version-locked</c>), its design is
    /// frozen by its first execution (<c>design-frozen</c>), or the execution has ended
    /// (<c>execution-run-facts-frozen</c>). See <see cref="ApiProblem.Code"/>.
    /// </summary>
    RecordLocked,
    /// <summary>409 otherwise: a duplicate, a failed finalization precondition, an invalid rule configuration, etc. See <see cref="ApiProblem.Code"/>.</summary>
    Conflict,
    /// <summary>5xx: ShepherdAPI failed. <see cref="ApiProblem.TraceId"/> locates it in the ShepherdAPI log.</summary>
    ServerError,
    /// <summary>ShepherdAPI could not be reached or did not answer in time.</summary>
    Unreachable,
    /// <summary>Any other unexpected HTTP status.</summary>
    Unexpected,
}

/// <summary>A failed ShepherdAPI call, in the form every page displays (docs/ARCHITECTURE.md AD-6).</summary>
public sealed record ApiProblem(
    ApiProblemKind Kind,
    string Title,
    string? Detail,
    int? StatusCode = null,
    string? TraceId = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null)
{
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; init; } =
        FieldErrors ?? new Dictionary<string, string[]>();

    /// <summary>ShepherdAPI's stable error code (e.g. <c>duplicate-observation</c>), or null when the response had none.</summary>
    public string? Code { get; init; }

    /// <summary>True when ShepherdAPI says the request can be retried unchanged (<c>retryable-conflict</c>).</summary>
    public bool Retryable { get; init; }
}
