using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace ShepherdWeb.Infrastructure.ArtifactStore;

/// <summary>A file written to the artifact store, with the facts ShepherdAPI records about it.</summary>
public sealed record StoredFile(string StoredUri, string Sha256, long ByteLength, DateTime ReceivedUtc, DateTime StoredUtc);

/// <summary>
/// The local, write-once artifact store (docs/ARCHITECTURE.md AD-9). ShepherdAPI never holds bytes; it
/// records the SHA-256 and StoredURI of files kept here.
///
/// Files live at {root}/executions/{ExecutionID}/{SHA256}{extension}. A file is written to a temporary
/// name while its hash is computed, then moved into place. An existing file is never overwritten: if
/// the target already exists it must have the same hash, and it is reused.
/// </summary>
public sealed class ArtifactFileStore
{
    private readonly string _root;

    public ArtifactFileStore(IOptions<ArtifactStoreOptions> options)
    {
        _root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.Value.RootPath));
        Directory.CreateDirectory(_root);
    }

    public string RootPath => _root;

    public async Task<StoredFile> StoreAsync(long executionId, Stream content, string extension, CancellationToken cancellationToken = default)
    {
        var receivedUtc = DateTime.UtcNow;
        var tempDirectory = Directory.CreateDirectory(Path.Combine(_root, "incoming")).FullName;
        var tempPath = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + ".tmp");

        string sha256;
        long length = 0;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    length += read;
                }
            }

            sha256 = Convert.ToHexString(hash.GetHashAndReset()); // Upper-case hex, as ShepherdAPI stores it.

            var directory = Directory.CreateDirectory(Path.Combine(_root, "executions", executionId.ToString())).FullName;
            var finalPath = Path.Combine(directory, sha256 + NormalizeExtension(extension));

            if (File.Exists(finalPath))
            {
                // Same content already stored (for example a retry). Reuse it only if it really is identical.
                if (!string.Equals(await ComputeSha256Async(finalPath, cancellationToken), sha256, StringComparison.Ordinal))
                {
                    throw new IOException($"Artifact file {finalPath} exists but its content does not match its name. It has been left untouched.");
                }

                File.Delete(tempPath);
            }
            else
            {
                File.Move(tempPath, finalPath, overwrite: false);
            }

            return new StoredFile(new Uri(finalPath).AbsoluteUri, sha256, length, receivedUtc, DateTime.UtcNow);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// The local path for a StoredURI, or null if it does not point inside this store. Artifacts
    /// registered elsewhere, or a tampered URI, are never read from outside the store root.
    /// </summary>
    public string? ResolvePath(string? storedUri)
    {
        if (!Uri.TryCreate(storedUri, UriKind.Absolute, out var uri) || !uri.IsFile)
        {
            return null;
        }

        var path = Path.GetFullPath(uri.LocalPath);
        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var cleaned = new string(extension.Trim().TrimStart('.').Where(char.IsLetterOrDigit).Take(16).ToArray());
        return cleaned.Length == 0 ? string.Empty : "." + cleaned.ToLowerInvariant();
    }
}
