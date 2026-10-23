using System.Net;
using System.Text.Json;
using ShepherdWeb.Infrastructure.Time;

// NSwag gives every generated client partial hooks. This file is the one place ShepherdWeb uses them.
//
// 1. JSON settings: all ShepherdAPI traffic goes through UtcDateTimeJsonConverter, so the UTC rules
//    in docs/ARCHITECTURE.md AD-10 (finding F4) are enforced once, at the API boundary.
// 2. Error bodies: the contract declares typed 400/404/409 responses, so the generated code throws
//    ApiException<ProblemDetails>. Its Response text, which ApiErrorMapper parses (code, title, detail,
//    field errors), is filled only when ReadResponseAsString is on, so every client turns it on.
// 3. Success statuses (contract gap, recommendation R8): ShepherdAPI answers creates with 201 Created
//    and deletes with 204 No Content. Since ShepherdAPI Issues #1-#8 the contract declares those codes
//    for artifacts, executions, iterations, parameters, score observations (with evidence) and score
//    rules, and the generated code accepts them. The remaining clients' creates are still documented
//    as 200, so for those clients ProcessResponse maps 201/204 to 200 before the generated status
//    check; otherwise a successful create would be reported as an error after the change had been
//    made, inviting a retry and a duplicate. The body of a 201 is the created entity, exactly what
//    the generated 200 path deserializes. Never add the mapping to a client whose operations declare
//    201 or 204: the generated code would then reject the 200.
//
// Add a block here whenever the contract gains a new tag/client.
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

// Clients whose creates are still documented as 200: R8 mapping applies.

public partial class AgentsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void Initialize() => ReadResponseAsString = true;
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class ArtifactTypesClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void Initialize() => ReadResponseAsString = true;
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class ExperimentVersionsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void Initialize() => ReadResponseAsString = true;
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class PhasesClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void Initialize() => ReadResponseAsString = true;
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class RatersClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void Initialize() => ReadResponseAsString = true;
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class ScoreCategoriesClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void Initialize() => ReadResponseAsString = true;
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

public partial class StatusesClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void Initialize() => ReadResponseAsString = true;
    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) => GeneratedClientCustomization.NormalizeSuccessStatus(response);
}

// Clients whose contract declares 201/204: no R8 mapping.

public partial class ArtifactsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class ExecutionsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class IterationsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class ParametersClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class ScoreObservationsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class ScoreRulesClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => GeneratedClientCustomization.ConfigureJson(settings);
    partial void Initialize() => ReadResponseAsString = true;
}
