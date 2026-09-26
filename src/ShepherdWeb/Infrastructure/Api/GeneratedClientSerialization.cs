using System.Text.Json;
using ShepherdWeb.Infrastructure.Time;

// NSwag gives every generated client a static partial hook for its JSON settings. Implementing it
// here routes all ShepherdAPI traffic through UtcDateTimeJsonConverter, so the UTC rules in
// docs/ARCHITECTURE.md AD-10 (finding F4) are enforced once, at the API boundary, rather than at
// every call site. Add a line here whenever the contract gains a new tag/client.
namespace ShepherdWeb.Infrastructure.Api.Generated;

internal static class GeneratedClientSerialization
{
    public static void Configure(JsonSerializerOptions settings) =>
        settings.Converters.Add(new UtcDateTimeJsonConverter());
}

public partial class AgentsClient { static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientSerialization.Configure(settings); }
public partial class ArtifactsClient { static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientSerialization.Configure(settings); }
public partial class ArtifactTypesClient { static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientSerialization.Configure(settings); }
public partial class ExecutionsClient { static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientSerialization.Configure(settings); }
public partial class ExperimentVersionsClient { static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientSerialization.Configure(settings); }
public partial class IterationsClient { static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientSerialization.Configure(settings); }
public partial class ParametersClient { static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientSerialization.Configure(settings); }
public partial class PhasesClient { static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientSerialization.Configure(settings); }
public partial class RatersClient { static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientSerialization.Configure(settings); }
public partial class ScoreCategoriesClient { static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientSerialization.Configure(settings); }
public partial class ScoreObservationsClient { static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientSerialization.Configure(settings); }
public partial class ScoreRulesClient { static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientSerialization.Configure(settings); }
public partial class StatusesClient { static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientSerialization.Configure(settings); }
