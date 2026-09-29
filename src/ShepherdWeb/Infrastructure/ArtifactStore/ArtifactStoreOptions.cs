using System.ComponentModel.DataAnnotations;

namespace ShepherdWeb.Infrastructure.ArtifactStore;

/// <summary>The local folder that holds artifact bytes (docs/ARCHITECTURE.md AD-9, owner decisions B1/Q4).</summary>
public class ArtifactStoreOptions
{
    public const string SectionName = "ArtifactStore";

    /// <summary>Environment variables are expanded, so "%LOCALAPPDATA%\GoodShepherd\Artifacts" works.</summary>
    [Required] public string RootPath { get; set; } = string.Empty;

    /// <summary>Largest upload accepted, in bytes (owner decision N7: 50 MB).</summary>
    [Range(1, long.MaxValue)] public long MaxUploadBytes { get; set; } = 50L * 1024 * 1024;
}
