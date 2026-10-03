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
/// execution is created but the prompt is not registered, the result says so, and a retry registers
/// exactly the <see cref="PendingStartPrompt"/> decided at Start: the same text and the same provenance.
///
/// The prompt delivered at the start is the iteration's prompt unless the operator edited it for this
/// execution. The registered artifact is the authoritative record either way; the two differ only in
/// provenance (<see cref="PromptProvenance"/>).
/// </summary>
public sealed class StartExecutionWorkflow(IExecutionsClient executions, ArtifactRegistrar registrar, IDraftStore drafts)
{
    public async Task<StartExecutionResult> StartAsync(CreateExecutionRequest request, string prompt, bool overridden = false)
    {
        var execution = await executions.ExecutionsPostAsync(request); // A failure here leaves nothing behind.
        var pending = PendingStartPrompt.For(execution, prompt, overridden);

        // The execution now exists but its prompt is not yet registered. Keep exactly what was decided in
        // the browser until it is, so a later retry can reproduce it.
        var draftSaved = await KeepAsync(pending);

        try
        {
            var artifact = await RegisterStartPromptAsync(pending);
            return new StartExecutionResult(execution, pending, artifact, null, draftSaved);
        }
        catch (Exception ex) when (IsRegistrationFailure(ex))
        {
            return new StartExecutionResult(execution, pending, null, ex, draftSaved);
        }
    }

    /// <summary>
    /// Registers (or finds the already-registered) start prompt exactly as decided at Start. An existing
    /// artifact is reused only if it has the same bytes <em>and</em> the same provenance.
    /// </summary>
    public async Task<Artifact> RegisterStartPromptAsync(PendingStartPrompt pending)
    {
        var artifact = await registrar.RegisterDeliveredPromptAsync(
            pending.ExecutionId, pending.Text, pending.OriginalName, pending.OriginalUri, reuseIfRegistered: true);

        await ForgetAsync(pending.ExecutionId); // Registered: the artifact is now the record.
        return artifact;
    }

    /// <summary>
    /// The start prompt saved in this browser for <paramref name="execution"/>, if it can be established
    /// that it belongs to that execution record and is intact. Otherwise the observed reason there is none.
    /// </summary>
    public async Task<PendingStartPromptLookup> FindPendingStartPromptAsync(Execution execution)
    {
        Draft? draft;
        try
        {
            draft = await drafts.LoadAsync(StartPromptDraftKey(execution.ExecutionID));
        }
        catch (Exception ex) when (IsDraftStoreFailure(ex))
        {
            return new PendingStartPromptLookup(null, "Browser storage is not available, so a prompt saved at Start cannot be read.");
        }

        if (draft is null)
        {
            return new PendingStartPromptLookup(null, "The prompt text entered at Start is not saved in this browser.");
        }

        if (PendingStartPrompt.TryDeserialize(draft.Text) is not { } pending)
        {
            return new PendingStartPromptLookup(null,
                "A prompt saved in this browser was found, but it is not in a form that identifies its execution record.");
        }

        return pending.ProblemFor(execution) is { } problem
            ? new PendingStartPromptLookup(null, problem)
            : new PendingStartPromptLookup(pending, null);
    }

    /// <summary>Registers further prompt text delivered during an exceptional intervention (§3 rule 6).</summary>
    public Task<Artifact> RegisterAdditionalPromptAsync(long executionId, string prompt) =>
        registrar.RegisterDeliveredPromptAsync(
            executionId, prompt,
            originalName: PromptProvenance.InterventionName(executionId),
            originalUri: PromptProvenance.InterventionUri(executionId),
            reuseIfRegistered: false);

    /// <summary>Provenance of a start prompt that was edited for one execution: where the text came from.</summary>
    public static string StartPromptOverrideUri(long executionId) => PromptProvenance.ExecutionStartUri(executionId);

    /// <summary>True if an artifact's OriginalURI says it is this execution's own (edited) start prompt.</summary>
    public static bool IsStartPromptOverrideUri(long executionId, string? originalUri) =>
        PromptProvenance.IsExecutionStartUri(originalUri, executionId);

    /// <summary>The browser-draft key holding an execution's pending start prompt until it has been registered.</summary>
    public static string StartPromptDraftKey(long executionId) => $"execution:{executionId}:start-prompt";

    // The draft is a safety net, never a gate: if the browser cannot store it, starting still proceeds
    // and registration is still attempted immediately. The result reports whether it was saved.
    private async Task<bool> KeepAsync(PendingStartPrompt pending)
    {
        try
        {
            await drafts.SaveAsync(StartPromptDraftKey(pending.ExecutionId), pending.Serialize());
            return true;
        }
        catch (Exception ex) when (IsDraftStoreFailure(ex))
        {
            return false;
        }
    }

    private async Task ForgetAsync(long executionId)
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

/// <param name="Pending">What was decided at Start: the exact text and its provenance.</param>
/// <param name="PromptArtifact">Null when the execution was created but its prompt could not be registered.</param>
/// <param name="RegistrationError">Why the prompt was not registered, when it wasn't.</param>
/// <param name="DraftSaved">Whether the pending prompt was saved in the browser for a later retry.</param>
public sealed record StartExecutionResult(
    Execution Execution,
    PendingStartPrompt Pending,
    Artifact? PromptArtifact,
    Exception? RegistrationError,
    bool DraftSaved);
