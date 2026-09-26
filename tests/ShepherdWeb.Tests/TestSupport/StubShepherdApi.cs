using System.Net;
using System.Text;
using ShepherdWeb.Infrastructure;
using ShepherdWeb.Infrastructure.Api.Generated;

namespace ShepherdWeb.Tests.TestSupport;

/// <summary>
/// A fake ShepherdAPI at the HTTP level. Tests use the real generated clients against it, so
/// serialization (including the UTC converter) is exercised exactly as in production, without a
/// mocking library.
/// </summary>
public sealed class StubShepherdApi : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();

    public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];

    public StubShepherdApi On(HttpMethod method, string path, HttpStatusCode status, string? json = null) =>
        On(method, path, _ => Response(status, json));

    public StubShepherdApi On(HttpMethod method, string path, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes[Key(method, path)] = respond;
        return this;
    }

    /// <summary>Makes every request fail as if ShepherdAPI were not running.</summary>
    public bool Unreachable { get; set; }

    /// <summary>
    /// By default each request yields first, as real network I/O does. Set true to complete
    /// synchronously, which exercises code paths that must cope with already-completed tasks.
    /// </summary>
    public bool CompleteSynchronously { get; set; }

    /// <summary>Registers empty 200 responses for all six lookup lists.</summary>
    public StubShepherdApi WithEmptyLookups()
    {
        foreach (var path in LookupPaths)
        {
            On(HttpMethod.Get, path, HttpStatusCode.OK, "[]");
        }

        return this;
    }

    public static readonly string[] LookupPaths =
        ["/api/agents", "/api/artifact-types", "/api/phases", "/api/raters", "/api/score-categories", "/api/statuses"];

    public HttpClient CreateHttpClient() => new(this) { BaseAddress = new Uri("http://shepherdapi.test/") };

    public ReferenceData CreateReferenceData()
    {
        var http = CreateHttpClient();
        return new ReferenceData(
            new AgentsClient(http), new ArtifactTypesClient(http), new PhasesClient(http),
            new RatersClient(http), new ScoreCategoriesClient(http), new StatusesClient(http));
    }

    public static HttpResponseMessage Response(HttpStatusCode status, string? json) => new(status)
    {
        Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json"),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!CompleteSynchronously)
        {
            await Task.Yield();
        }

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = request.RequestUri!.AbsolutePath;
        Requests.Add((request.Method, path, body));

        if (Unreachable)
        {
            throw new HttpRequestException("No connection could be made because the target machine actively refused it.");
        }

        var response = _routes.TryGetValue(Key(request.Method, path), out var respond)
            ? respond(request)
            : throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        response.RequestMessage = request; // As the real socket handler does.
        return response;
    }

    private static string Key(HttpMethod method, string path) => $"{method.Method} {path}";
}
