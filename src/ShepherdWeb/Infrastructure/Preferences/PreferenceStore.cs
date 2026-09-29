using System.Security.Cryptography;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace ShepherdWeb.Infrastructure.Preferences;

/// <summary>
/// Per-browser conveniences such as the last-used rater or agent (docs/ARCHITECTURE.md AD-11). Never
/// domain data. Like drafts, it uses JS interop, so call it only after the first interactive render.
/// </summary>
public interface IPreferenceStore
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
}

public sealed class BrowserPreferenceStore(ProtectedLocalStorage storage) : IPreferenceStore
{
    private const string Prefix = "shepherdweb:pref:";

    public async Task<string?> GetAsync(string key)
    {
        try
        {
            var result = await storage.GetAsync<string>(Prefix + key);
            return result.Success ? result.Value : null;
        }
        catch (CryptographicException)
        {
            return null; // Written under keys that no longer exist; a preference is safe to forget.
        }
    }

    public async Task SetAsync(string key, string value) => await storage.SetAsync(Prefix + key, value);
}

/// <summary>Preference keys, in one place.</summary>
public static class PreferenceKeys
{
    public const string Agent = "agent";
    public const string Rater = "rater";
    public const string ArtifactType = "artifact-type";
}
