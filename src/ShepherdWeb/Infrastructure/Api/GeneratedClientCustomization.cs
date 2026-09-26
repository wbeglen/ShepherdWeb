using System.Net;
using System.Text.Json;
using ShepherdWeb.Infrastructure.Time;

// NSwag gives every generated client partial hooks. This file is the one place ShepherdWeb uses them.
//
// 1. JSON settings: all ShepherdAPI traffic goes through UtcDateTimeJsonConverter, so the UTC rules
//    in docs/ARCHITECTURE.md AD-10 (finding F4) are enforced once, at the API boundary.
// 2. Success statuses: ShepherdAPI answers creates with 201 Created (CreatedAtAction) and deletes
//    with 204 No Content, but its OpenAPI document declares only 200 for every operation. The
//    generated code would therefore report every successful create and delete as an error, after
//    the change had been made, inviting a retry and a duplicate. ProcessResponse runs before the
//    generated status check and maps 201/204 to 200 (contract gap, recommendation R8). The body of a
//    201 is the created entity, exactly what the generated 200 path deserializes.
//
// Add a line here whenever the contract gains a new tag/client.
namespace ShepherdWeb.Infrastructure.Api.Generated;

internal static class GeneratedClientCustomization
{
    public static void ConfigureJson(JsonSerializerOptions settings) =>
        settings.Converters.Add(new UtcDateTimeJsonConverter());

    public static void NormalizeSuccessStatus(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.NoContent)
        {
            response.StatusCode = HttpStatusCode.OK;
        }
    }
}

public partial class AgentsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class ArtifactsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class ArtifactTypesClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class ExecutionsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class ExperimentVersionsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class IterationsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class ParametersClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class PhasesClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class RatersClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class ScoreCategoriesClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class ScoreObservationsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class ScoreRulesClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class StatusesClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}
