using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShepherdWeb.Components.Shared;
using ShepherdWeb.Infrastructure;
using ShepherdWeb.Infrastructure.Api;
using ShepherdWeb.Tests.TestSupport;

namespace ShepherdWeb.Tests.Components;

public class SharedComponentTests : BunitContext
{
    private readonly StubShepherdApi _api = new();

    public SharedComponentTests()
    {
        Services.AddSingleton(Options.Create(new ShepherdApiOptions { BaseUrl = "http://localhost:5231" }));
        Services.AddSingleton(new ApiErrorMapper(NullLogger<ApiErrorMapper>.Instance));
        Services.AddScoped<ReferenceData>(_ => _api.CreateReferenceData());
    }

    [Fact]
    public void ApiStatusIndicator_ShowsConnected_WhenReferenceDataLoads()
    {
        _api.WithEmptyLookups();

        var cut = Render<ApiStatusIndicator>();

        cut.WaitForAssertion(() => Assert.Contains("ShepherdAPI connected", cut.Markup));
    }

    [Fact]
    public void ApiStatusIndicator_ShowsUnreachableWithRetry_AndRecovers()
    {
        _api.Unreachable = true;

        var cut = Render<ApiStatusIndicator>();
        cut.WaitForAssertion(() => Assert.Contains("ShepherdAPI unreachable", cut.Markup));

        _api.Unreachable = false;
        _api.WithEmptyLookups();
        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains("ShepherdAPI connected", cut.Markup));
    }

    [Fact]
    public async Task ApiStatusIndicator_FollowsAReloadStartedElsewhereInTheCircuit()
    {
        _api.Unreachable = true;
        var cut = Render<ApiStatusIndicator>();
        cut.WaitForAssertion(() => Assert.Contains("ShepherdAPI unreachable", cut.Markup));

        // A page's own Retry reloads the shared ReferenceData, not the indicator.
        _api.Unreachable = false;
        _api.WithEmptyLookups();
        await Services.GetRequiredService<ReferenceData>().RefreshAsync();

        cut.WaitForAssertion(() => Assert.Contains("ShepherdAPI connected", cut.Markup));
    }

    [Fact]
    public void ProblemDisplay_RendersNothingWithoutAProblem()
    {
        var cut = Render<ProblemDisplay>();

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void ProblemDisplay_ShowsTraceId_ForServerErrors()
    {
        var cut = Render<ProblemDisplay>(p => p.Add(x => x.Problem,
            new ApiProblem(ApiProblemKind.ServerError, "ShepherdAPI error", "boom", 500, "0HN7:00000001")));

        Assert.Contains("0HN7:00000001", cut.Markup);
        Assert.Contains("alert-danger", cut.Markup);
    }

    [Fact]
    public void ProblemDisplay_ShowsRecordLockedAsWarning()
    {
        var cut = Render<ProblemDisplay>(p => p.Add(x => x.Problem,
            new ApiProblem(ApiProblemKind.RecordLocked, "Record locked", "ExperimentVersion 1 is locked.", 409)));

        Assert.Contains("alert-warning", cut.Markup);
    }

    [Fact]
    public void UtcTimestamp_LabelsUtc_AndShowsPlaceholderForNull()
    {
        var withValue = Render<UtcTimestamp>(p => p.Add(x => x.Value, new DateTime(2026, 9, 25, 14, 3, 12)));
        var withoutValue = Render<UtcTimestamp>(p => p.Add(x => x.Missing, "not locked"));

        Assert.Contains("2026-09-25 14:03:12 UTC", withValue.Markup);
        Assert.Contains("not locked", withoutValue.Markup);
    }
}
