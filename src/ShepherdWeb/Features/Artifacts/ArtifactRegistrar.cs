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
            // Identity is bytes AND provenance: the same text registered with a different role (for
            // example as an intervention prompt) is different evidence and is never reused.
            var sha = new PromptText(promptText).Sha256;
            var existing = (await artifacts.ArtifactsGetAsync(executionId: executionId))
                .FirstOrDefault(a => a.ArtifactTypeID == type.ArtifactTypeID &&
                                     string.Equals(a.ShA256, sha, StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(a.OriginalURI, originalUri, StringComparison.Ordinal));
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
    /// The registered text of a prompt artifact, released only if the stored bytes match the artifact's
    /// recorded SHA-256 (AD-8). The file is read once and those same bytes are hashed and returned, so
    /// what is displayed or copied is exactly what was verified.
    /// </summary>
    public async Task<VerifiedText> ReadVerifiedTextAsync(Artifact artifact)
    {
        var path = store.ResolvePath(artifact.StoredURI);
        if (path is null)
        {
            return new VerifiedText(IntegrityResult.NotInStore, null);
        }

        if (!File.Exists(path))
        {
            return new VerifiedText(IntegrityResult.Missing, null);
        }

        var bytes = await File.ReadAllBytesAsync(path);
        var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        return string.Equals(actual, artifact.ShA256, StringComparison.OrdinalIgnoreCase)
            ? new VerifiedText(IntegrityResult.Verified, Utf8NoBom.GetString(bytes))
            : new VerifiedText(IntegrityResult.Mismatch, null);
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

/// <param name="Text">The stored text, present only when <paramref name="Status"/> is Verified.</param>
public sealed record VerifiedText(IntegrityResult Status, string? Text)
{
    /// <summary>The observed fact when the text cannot be released, or null when it is verified.</summary>
    public string? Problem => Status switch
    {
        IntegrityResult.Verified => null,
        IntegrityResult.Mismatch => "The stored file does not match the SHA-256 recorded for this artifact.",
        IntegrityResult.Missing => "The stored file is missing from the artifact store.",
        _ => "The stored file is not in this artifact store.",
    };
}

/// <summary>A reference row ShepherdWeb relies on by name (AD-13) does not exist.</summary>
public sealed class MissingConventionException(string message) : Exception(message);
