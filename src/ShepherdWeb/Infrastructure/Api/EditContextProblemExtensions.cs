using System.Reflection;
using Microsoft.AspNetCore.Components.Forms;

namespace ShepherdWeb.Infrastructure.Api;

/// <summary>
/// Shows ShepherdAPI's 400 validation errors inside the form, exactly like client-side
/// DataAnnotations errors (docs/ARCHITECTURE.md AD-5). One EditContext, one validation pipeline.
/// </summary>
public static class EditContextProblemExtensions
{
    /// <summary>
    /// Adds <paramref name="problem"/>'s field errors to <paramref name="store"/>. Keys are matched to
    /// the form model's properties case-insensitively ("maxScore", "MaxScore", "$.maxScore" and
    /// "request.MaxScore" all match MaxScore). Unmatched keys, and a problem with no field errors,
    /// become model-level messages, which ValidationSummary shows.
    /// </summary>
    public static void AddApiErrors(this ValidationMessageStore store, EditContext editContext, ApiProblem problem)
    {
        var model = editContext.Model;
        var properties = model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var modelLevel = new FieldIdentifier(model, string.Empty);

        if (problem.FieldErrors.Count == 0)
        {
            store.Add(modelLevel, problem.Detail ?? problem.Title);
        }

        foreach (var (key, messages) in problem.FieldErrors)
        {
            var name = NormalizeKey(key);
            var property = properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            var field = property is null ? modelLevel : new FieldIdentifier(model, property.Name);
            var prefix = property is null && !string.IsNullOrEmpty(name) ? $"{name}: " : string.Empty;
            foreach (var message in messages)
            {
                store.Add(field, prefix + message);
            }
        }

        editContext.NotifyValidationStateChanged();
    }

    private static string NormalizeKey(string key)
    {
        // Model-binding keys can look like "$.field", "request.Field" or "Field[0]".
        var name = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0)
        {
            name = name[(lastDot + 1)..];
        }

        var bracket = name.IndexOf('[');
        return bracket >= 0 ? name[..bracket] : name;
    }
}
