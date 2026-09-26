using System.ComponentModel.DataAnnotations;

namespace ShepherdWeb.Infrastructure.Api;

/// <summary>Where ShepherdAPI lives. Bound from the "ShepherdApi" configuration section.</summary>
public class ShepherdApiOptions
{
    public const string SectionName = "ShepherdApi";

    [Required, Url]
    public string BaseUrl { get; set; } = string.Empty;

    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 30;
}
