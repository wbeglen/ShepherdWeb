using System.ComponentModel.DataAnnotations;

namespace ShepherdWeb.Infrastructure;

/// <summary>
/// Names of reference-data rows ShepherdWeb relies on (docs/ARCHITECTURE.md AD-13). These are
/// ShepherdWeb conventions, not domain rules: ShepherdAPI hard-codes no status or artifact-type
/// names. Names are resolved against ReferenceData when used; a missing row produces an
/// actionable message rather than a silent failure.
/// </summary>
public class ConventionsOptions
{
    public const string SectionName = "Conventions";

    /// <summary>Status given to new experiment versions.</summary>
    [Required] public string DraftStatusName { get; set; } = "Draft";

    /// <summary>Status the Lock action sets together with LockedUTC.</summary>
    [Required] public string LockedStatusName { get; set; } = "Locked";

    /// <summary>Status given to new executions (owner decision N10). The API cannot change it later.</summary>
    [Required] public string DefaultExecutionStatusName { get; set; } = "Draft";

    /// <summary>ArtifactType ShepherdWeb assigns to every delivered prompt (AD-8).</summary>
    [Required] public string DeliveredPromptArtifactTypeName { get; set; } = "DeliveredPrompt";
}
