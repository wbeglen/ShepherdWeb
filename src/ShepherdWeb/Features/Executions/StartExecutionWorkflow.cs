using ShepherdWeb.Features.Artifacts;
using ShepherdWeb.Infrastructure.Api;
using ShepherdWeb.Infrastructure.Api.Generated;

namespace ShepherdWeb.Features.Executions;

/// <summary>
/// "The gates open" (docs/ARCHITECTURE.md AD-8): create the execution, then register the exact
/// prompt it receives. The prompt is only released for copying after registration succeeds.
/// The two steps cannot be atomic (they are two ShepherdAPI calls around a file write). If the
/// execution is created but the prompt is not registered, the result says so and the execution page
/// offers a retry, which <see cref="ArtifactRegistrar.RegisterDeliveredPromptAsync"/> makes safe.
/// </summary>
public sealed class StartExecutionWorkflow(IExecutionsClient executions, ArtifactRegistrar registrar)
{
    public async Task<StartExecutionResult> StartAsync(CreateExecutionRequest request, string prompt)
    {
        var execution = await executions.ExecutionsPostAsync(request); // A failure here leaves nothing behind.

        try
        {
            var artifact = await RegisterStartPromptAsync(execution.ExecutionID, execution.IterationID, prompt);
            return new StartExecutionResult(execution, artifact, null);
        }
        catch (Exception ex) when (IsRegistrationFailure(ex))
        {
            return new StartExecutionResult(execution, null, ex);
        }
    }

    /// <summary>Registers (or finds the already-registered) prompt delivered at the start of an execution.</summary>
    public Task<Artifact> RegisterStartPromptAsync(long executionId, long iterationId, string prompt) =>
        registrar.RegisterDeliveredPromptAsync(
            executionId, prompt,
            originalName: $"iteration-{iterationId}-prompt.txt",
            originalUri: $"urn:goodshepherd:iteration:{iterationId}:prompt",
            reuseIfRegistered: true);

    /// <summary>Registers further prompt text delivered during an exceptional intervention (§3 rule 6).</summary>
    public Task<Artifact> RegisterAdditionalPromptAsync(long executionId, string prompt) =>
        registrar.RegisterDeliveredPromptAsync(
            executionId, prompt,
            originalName: $"execution-{executionId}-additional-prompt.txt",
            originalUri: $"urn:goodshepherd:execution:{executionId}:additional-prompt",
            reuseIfRegistered: false);

    /// <summary>Failures after the execution exists that leave the prompt unregistered: API, store I/O, or a missing convention.</summary>
    public static bool IsRegistrationFailure(Exception ex) =>
        ApiErrorMapper.IsApiFailure(ex) || ex is IOException or UnauthorizedAccessException or MissingConventionException;
}

/// <param name="PromptArtifact">Null when the execution was created but its prompt could not be registered.</param>
/// <param name="RegistrationError">Why the prompt was not registered, when it wasn't.</param>
public sealed record StartExecutionResult(Execution Execution, Artifact? PromptArtifact, Exception? RegistrationError);
