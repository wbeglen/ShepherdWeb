using System.Text;
using Microsoft.Extensions.Options;
using ShepherdWeb.Features.Iterations;
using ShepherdWeb.Infrastructure;
using ShepherdWeb.Infrastructure.Api.Generated;
using ShepherdWeb.Infrastructure.ArtifactStore;

namespace ShepherdWeb.Features.Artifacts;

/// <summary>
/// Writes artifact bytes to the local store and registers them with ShepherdAPI
/// (docs/ARCHITECTURE.md AD-8, AD-9). The operator never types a hash, size or MIME type.
/// Artifacts are insert-only in ShepherdAPI, so nothing here updates or deletes.
/// </summary>
public sealed class ArtifactRegistrar(
    ArtifactFileStore store,
    IArtifactsClient artifacts,
    ReferenceData referenceData,
    IOptions<ConventionsOptions> conventions)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public const string PromptMimeType = "text/plain; charset=utf-8";

    public Task<StoredFile> StoreAsync(long executionId, Stream content, string extension, CancellationToken cancellationToken = default) =>
        store.StoreAsync(executionId, content, extension, cancellationToken);

    public Task<Artifact> RegisterAsync(long executionId, int artifactTypeId, StoredFile stored, string originalName, string originalUri, string mimeType) =>
        artifacts.ArtifactsPostAsync(new CreateArtifactRequest
        {
            ExecutionID = executionId,
            ArtifactTypeID = artifactTypeId,
            OriginalName = Truncate(originalName, 200),
            OriginalURI = Truncate(originalUri, 2048),
            StoredURI = stored.StoredUri,
            MimeType = Truncate(mimeType, 100),
            ByteLength = stored.ByteLength,
            ShA256 = stored.Sha256, // ShepherdAPI's SHA256 property is camel-cased to "shA256" in the contract.
            ReceivedUTC = stored.ReceivedUtc,
            StoredUTC = stored.StoredUtc,
        });

    /// <summary>
    /// Registers prompt text delivered to an agent as a DeliveredPrompt artifact: exact UTF-8 bytes
    /// without a BOM, hashed as stored (AD-8, register before release).
    /// With <paramref name="reuseIfRegistered"/>, an identical prompt already registered for this
    /// execution is returned instead of registering it again, which makes a retry after a partial
    /// failure safe. The start-of-execution prompt uses it; an additional intervention prompt does not,
    /// because delivering the same text twice is two facts.
    /// </summary>
    public async Task<Artifact> RegisterDeliveredPromptAsync(long executionId, string promptText, string originalName, string originalUri, bool reuseIfRegistered)
    {
        await referenceData.EnsureLoadedAsync();
        var typeName = conventions.Value.DeliveredPromptArtifactTypeName;
        var type = referenceData.FindArtifactType(typeName)
            ?? throw new MissingConventionException($"There is no “{typeName}” artifact type. Create it under Reference data first.");

        if (reuseIfRegistered)
        {
            var sha = new PromptText(promptText).Sha256;
            var existing = (await artifacts.ArtifactsGetAsync(executionId: executionId))
                .FirstOrDefault(a => a.ArtifactTypeID == type.ArtifactTypeID && string.Equals(a.ShA256, sha, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }
        }

        await using var bytes = new MemoryStream(Utf8NoBom.GetBytes(promptText));
        var stored = await store.StoreAsync(executionId, bytes, ".txt");
        return await RegisterAsync(executionId, type.ArtifactTypeID, stored, originalName, originalUri, PromptMimeType);
    }

    /// <summary>
    /// The exact registered text of a prompt artifact, read back from the store so what the operator
    /// copies is what was registered, not what an editor currently shows. Null if the file is missing
    /// or lies outside the store.
    /// </summary>
    public async Task<string?> ReadTextAsync(Artifact artifact)
    {
        var path = store.ResolvePath(artifact.StoredURI);
        return path is not null && File.Exists(path)
            ? Utf8NoBom.GetString(await File.ReadAllBytesAsync(path))
            : null;
    }

    /// <summary>Re-hashes an artifact's stored file and compares it with the recorded SHA-256.</summary>
    public async Task<IntegrityResult> VerifyAsync(Artifact artifact)
    {
        var path = store.ResolvePath(artifact.StoredURI);
        if (path is null)
        {
            return IntegrityResult.NotInStore;
        }

        if (!File.Exists(path))
        {
            return IntegrityResult.Missing;
        }

        var actual = await ArtifactFileStore.ComputeSha256Async(path);
        return string.Equals(actual, artifact.ShA256, StringComparison.OrdinalIgnoreCase) ? IntegrityResult.Verified : IntegrityResult.Mismatch;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

public enum IntegrityResult
{
    Verified,
    Mismatch,
    Missing,
    NotInStore,
}

/// <summary>A reference row ShepherdWeb relies on by name (AD-13) does not exist.</summary>
public sealed class MissingConventionException(string message) : Exception(message);
