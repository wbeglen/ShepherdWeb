namespace ShepherdWeb.Infrastructure.Api;

public enum ApiProblemKind
{
    /// <summary>400: ShepherdAPI rejected the input. <see cref="ApiProblem.FieldErrors"/> says why.</summary>
    Validation,
    /// <summary>404.</summary>
    NotFound,
    /// <summary>409 "Record Locked": the configuration is locked or the execution has ended (ShepherdAPI §5).</summary>
    RecordLocked,
    /// <summary>409 otherwise: a unique or foreign-key conflict.</summary>
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
}
