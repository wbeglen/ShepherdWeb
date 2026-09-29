using ShepherdWeb.Infrastructure.Api.Generated;

namespace ShepherdWeb.Infrastructure.ArtifactStore;

public static class ArtifactFileEndpoint
{
    /// <summary>
    /// GET /artifact-files/{artifactId}: streams a stored artifact back for viewing (docs/ARCHITECTURE.md
    /// AD-9). The artifact's StoredURI comes from ShepherdAPI, and only files inside the store root are
    /// ever served, so a tampered or foreign URI cannot read arbitrary files. ShepherdWeb listens on
    /// localhost only (AD-14).
    /// </summary>
    public static IEndpointRouteBuilder MapArtifactFiles(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/artifact-files/{artifactId:long}", async (long artifactId, IArtifactsClient artifacts, ArtifactFileStore store) =>
        {
            Artifact artifact;
            try
            {
                artifact = await artifacts.ArtifactsGetAsync(artifactId); // plain long: the single-item overload
            }
            catch (ApiException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
            {
                return Results.NotFound();
            }

            var path = store.ResolvePath(artifact.StoredURI);
            if (path is null || !File.Exists(path))
            {
                return Results.NotFound();
            }

            // Shown inline (no download name), so text and images open in the browser tab.
            return Results.File(path, string.IsNullOrWhiteSpace(artifact.MimeType) ? "application/octet-stream" : artifact.MimeType);
        });

        return endpoints;
    }
}
