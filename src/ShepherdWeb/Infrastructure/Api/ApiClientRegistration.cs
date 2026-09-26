using Microsoft.Extensions.Options;
using ShepherdWeb.Infrastructure.Api.Generated;

namespace ShepherdWeb.Infrastructure.Api;

public static class ApiClientRegistration
{
    /// <summary>
    /// Registers every generated ShepherdAPI client as a typed HttpClient (docs/ARCHITECTURE.md AD-2).
    /// IHttpClientFactory owns handler lifetimes; a future auth token-forwarding DelegatingHandler
    /// is one AddHttpMessageHandler call in <see cref="AddClient{TClient,TImplementation}"/> (AD-14).
    /// </summary>
    public static IServiceCollection AddShepherdApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ShepherdApiOptions>()
            .Bind(configuration.GetSection(ShepherdApiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddClient<IAgentsClient, AgentsClient>();
        services.AddClient<IArtifactsClient, ArtifactsClient>();
        services.AddClient<IArtifactTypesClient, ArtifactTypesClient>();
        services.AddClient<IExecutionsClient, ExecutionsClient>();
        services.AddClient<IExperimentVersionsClient, ExperimentVersionsClient>();
        services.AddClient<IIterationsClient, IterationsClient>();
        services.AddClient<IParametersClient, ParametersClient>();
        services.AddClient<IPhasesClient, PhasesClient>();
        services.AddClient<IRatersClient, RatersClient>();
        services.AddClient<IScoreCategoriesClient, ScoreCategoriesClient>();
        services.AddClient<IScoreObservationsClient, ScoreObservationsClient>();
        services.AddClient<IScoreRulesClient, ScoreRulesClient>();
        services.AddClient<IStatusesClient, StatusesClient>();

        services.AddSingleton<ApiErrorMapper>();
        return services;
    }

    private static void AddClient<TClient, TImplementation>(this IServiceCollection services)
        where TClient : class
        where TImplementation : class, TClient =>
        services.AddHttpClient<TClient, TImplementation>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ShepherdApiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
}
