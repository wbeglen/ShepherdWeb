using System.Security.Cryptography;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace ShepherdWeb.Infrastructure.Drafts;

/// <summary>Unsaved text kept in the operator's browser. Never domain data; never submitted automatically.</summary>
public sealed record Draft(string Text, DateTime SavedUtc);

/// <summary>
/// Recoverable drafts (docs/ARCHITECTURE.md AD-11). An interface so components that keep drafts can
/// be tested without a browser.
/// </summary>
public interface IDraftStore
{
    Task<Draft?> LoadAsync(string key);
    Task SaveAsync(string key, string text);
    Task DeleteAsync(string key);
}

/// <summary>
/// Drafts in browser localStorage, encrypted by ASP.NET Core Data Protection (ProtectedLocalStorage).
/// Blazor note: this uses JS interop, so it can only be called after the component's first
/// interactive render (OnAfterRenderAsync), never from OnInitializedAsync.
/// </summary>
public sealed class BrowserDraftStore(ProtectedLocalStorage storage, ILogger<BrowserDraftStore> logger) : IDraftStore
{
    private const string Prefix = "shepherdweb:draft:";

    public async Task<Draft?> LoadAsync(string key)
    {
        try
        {
            var result = await storage.GetAsync<Draft>(Prefix + key);
            return result.Success ? result.Value : null;
        }
        catch (CryptographicException ex)
        {
            // Written under Data Protection keys that no longer exist. Unreadable, so discard it.
            logger.LogWarning(ex, "Discarding unreadable draft {Key}", key);
            await DeleteAsync(key);
            return null;
        }
    }

    public async Task SaveAsync(string key, string text) =>
        await storage.SetAsync(Prefix + key, new Draft(text, DateTime.UtcNow));

    public async Task DeleteAsync(string key) => await storage.DeleteAsync(Prefix + key);
}
