using System.Security.Cryptography;
using Microsoft.JSInterop;
using ShepherdWeb.Features.Artifacts;
using ShepherdWeb.Infrastructure.Api;
using ShepherdWeb.Infrastructure.Api.Generated;
using ShepherdWeb.Infrastructure.Drafts;

namespace ShepherdWeb.Features.Executions;

/// <summary>
/// "The gates open" (docs/ARCHITECTURE.md AD-8): create the execution, then register the exact
/// prompt it receives. The prompt is only released for copying after registration succeeds.
/// The two steps cannot be atomic (they are two ShepherdAPI calls around a file write). If the
/// execution is created but the prompt is not registered, the result says so and the execution page
/// offers a retry, which <see cref="ArtifactRegistrar.RegisterDeliveredPromptAsync"/> makes safe.
///
/// The prompt delivered at the start is the iteration's prompt unless the operator edited it for this
/// execution (<c>overridden</c>). The registered artifact is the authoritative record either way; an
/// override differs only in its provenance (<see cref="StartPromptOverrideUri"/>).
/// </summary>
public sealed class StartExecutionWorkflow(IExecutionsClient executions, ArtifactRegistrar registrar, IDraftStore drafts)
{
    public async Task<StartExecutionResult> StartAsync(CreateExecutionRequest request, string prompt, bool overridden = false)
    {
        var execution = await executions.ExecutionsPostAsync(request); // A failure here leaves nothing behind.

        // The execution now exists but its prompt is not yet registered. Keep the exact text in the
        // browser until it is, so a retry registers precisely what was meant to be delivered.
        await KeepStartPromptAsync(execution.ExecutionID, prompt);

        try
        {
            var artifact = await RegisterStartPromptAsync(execution.ExecutionID, execution.IterationID, prompt, overridden);
            return new StartExecutionResult(execution, artifact, null);
        }
        catch (Exception ex) when (IsRegistrationFailure(ex))
        {
            return new StartExecutionResult(execution, null, ex);
        }
    }

    /// <summary>
    /// Registers (or finds the already-registered) prompt delivered at the start of an execution. An
    /// <paramref name="overridden"/> prompt is recorded with execution-specific provenance instead of the iteration's.
    /// </summary>
    public async Task<Artifact> RegisterStartPromptAsync(long executionId, long iterationId, string prompt, bool overridden = false)
    {
        var artifact = await registrar.RegisterDeliveredPromptAsync(
            executionId, prompt,
            originalName: overridden ? $"execution-{executionId}-prompt.txt" : $"iteration-{iterationId}-prompt.txt",
            originalUri: overridden ? StartPromptOverrideUri(executionId) : $"urn:goodshepherd:iteration:{iterationId}:prompt",
            reuseIfRegistered: true);

        await ForgetStartPromptAsync(executionId); // Registered: the artifact is now the record.
        return artifact;
    }

    /// <summary>Registers further prompt text delivered during an exceptional intervention (§3 rule 6).</summary>
    public Task<Artifact> RegisterAdditionalPromptAsync(long executionId, string prompt) =>
        registrar.RegisterDeliveredPromptAsync(
            executionId, prompt,
            originalName: $"execution-{executionId}-additional-prompt.txt",
            originalUri: $"urn:goodshepherd:execution:{executionId}:additional-prompt",
            reuseIfRegistered: false);

    /// <summary>Provenance of a start prompt that was edited for one execution: where the text came from.</summary>
    public static string StartPromptOverrideUri(long executionId) => $"urn:goodshepherd:execution:{executionId}:prompt";

    /// <summary>True if an artifact's OriginalURI says it is this execution's own (edited) start prompt.</summary>
    public static bool IsStartPromptOverrideUri(long executionId, string? originalUri) =>
        string.Equals(originalUri, StartPromptOverrideUri(executionId), StringComparison.Ordinal);

    /// <summary>The browser-draft key holding an execution's start prompt until it has been registered.</summary>
    public static string StartPromptDraftKey(long executionId) => $"execution:{executionId}:start-prompt";

    // The draft is a safety net, never a gate: if the browser cannot store it, starting still proceeds
    // and registration is still attempted immediately; only a later retry would then lack the text.
    private async Task KeepStartPromptAsync(long executionId, string prompt)
    {
        try
        {
            await drafts.SaveAsync(StartPromptDraftKey(executionId), prompt);
        }
        catch (Exception ex) when (IsDraftStoreFailure(ex))
        {
        }
    }

    private async Task ForgetStartPromptAsync(long executionId)
    {
        try
        {
            await drafts.DeleteAsync(StartPromptDraftKey(executionId));
        }
        catch (Exception ex) when (IsDraftStoreFailure(ex))
        {
        }
    }

    private static bool IsDraftStoreFailure(Exception ex) =>
        ex is JSException or InvalidOperationException or CryptographicException or TaskCanceledException;

    /// <summary>Failures after the execution exists that leave the prompt unregistered: API, store I/O, or a missing convention.</summary>
    public static bool IsRegistrationFailure(Exception ex) =>
        ApiErrorMapper.IsApiFailure(ex) || ex is IOException or UnauthorizedAccessException or MissingConventionException;
}

/// <param name="PromptArtifact">Null when the execution was created but its prompt could not be registered.</param>
/// <param name="RegistrationError">Why the prompt was not registered, when it wasn't.</param>
public sealed record StartExecutionResult(Execution Execution, Artifact? PromptArtifact, Exception? RegistrationError);
