using System.Text.Json;
using ShepherdWeb.Features.Iterations;
using ShepherdWeb.Infrastructure.Api.Generated;
using ShepherdWeb.Infrastructure.Time;

namespace ShepherdWeb.Features.Executions;

/// <summary>
/// The start prompt of an execution between the moment the execution is created and the moment its
/// prompt is registered (docs/ARCHITECTURE.md AD-8). It carries everything decided at Start, including
/// the provenance, so a retry reproduces exactly what was decided and never re-decides it. It also
/// carries the identity of the execution record it belongs to, so a saved copy is never applied to a
/// different execution that happens to have the same ID (for example after a database restore).
/// </summary>
public sealed record PendingStartPrompt(
    long ExecutionId,
    long IterationId,
    DateTime StartUtc,
    string OriginalUri,
    string OriginalName,
    string Sha256,
    string Text)
{
    /// <summary>The prompt decided at Start for the execution ShepherdAPI just created.</summary>
    public static PendingStartPrompt For(Execution execution, string text, bool overridden) => new(
        execution.ExecutionID,
        execution.IterationID,
        Utc.FromApi(execution.StartUTC),
        overridden ? PromptProvenance.ExecutionStartUri(execution.ExecutionID) : PromptProvenance.IterationStartUri(execution.IterationID),
        overridden ? PromptProvenance.ExecutionStartName(execution.ExecutionID) : PromptProvenance.IterationStartName(execution.IterationID),
        new PromptText(text).Sha256,
        text);

    public bool IsOverride => PromptProvenance.IsExecutionStartUri(OriginalUri, ExecutionId);

    public string Serialize() => JsonSerializer.Serialize(this);

    /// <summary>Null if the saved text is not a pending start prompt (for example, a format from an earlier build).</summary>
    public static PendingStartPrompt? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PendingStartPrompt>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Why this saved prompt cannot be used for <paramref name="execution"/>, as an observed fact, or
    /// null if it belongs to that execution record and its text is intact.
    /// </summary>
    public string? ProblemFor(Execution execution)
    {
        var belongs = ExecutionId == execution.ExecutionID &&
                      IterationId == execution.IterationID &&
                      Utc.FromApi(StartUtc) == Utc.FromApi(execution.StartUTC) &&
                      PromptProvenance.IsStartUri(OriginalUri, execution.ExecutionID, execution.IterationID);
        if (!belongs)
        {
            return "A prompt saved in this browser was found, but it does not belong to this execution record " +
                   "(its execution, iteration, start time or provenance differ).";
        }

        return string.Equals(new PromptText(Text).Sha256, Sha256, StringComparison.Ordinal)
            ? null
            : "A prompt saved in this browser was found, but its text does not match the SHA-256 saved with it.";
    }
}

/// <summary>The outcome of looking for an execution's pending start prompt: a usable one, or why there is none.</summary>
public sealed record PendingStartPromptLookup(PendingStartPrompt? Usable, string? Problem);
