using System.Net;
using ShepherdWeb.Tests.TestSupport;

namespace ShepherdWeb.Tests.Infrastructure;

public class ReferenceDataTests
{
    [Fact]
    public async Task EnsureLoaded_LoadsEachListOnce()
    {
        var api = new StubShepherdApi().WithEmptyLookups();
        var referenceData = api.CreateReferenceData();

        await referenceData.EnsureLoadedAsync();
        await referenceData.EnsureLoadedAsync();

        Assert.True(referenceData.IsLoaded);
        Assert.Equal(StubShepherdApi.LookupPaths.Length, api.Requests.Count);
    }

    [Fact]
    public async Task Refresh_LoadsAgain()
    {
        var api = new StubShepherdApi().WithEmptyLookups();
        var referenceData = api.CreateReferenceData();

        await referenceData.EnsureLoadedAsync();
        await referenceData.RefreshAsync();

        Assert.Equal(2 * StubShepherdApi.LookupPaths.Length, api.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // regression: a synchronously failing load used to be cached forever
    public async Task FailedLoad_IsRetriedByTheNextCall(bool completeSynchronously)
    {
        var api = new StubShepherdApi { Unreachable = true, CompleteSynchronously = completeSynchronously };
        var referenceData = api.CreateReferenceData();

        await Assert.ThrowsAsync<HttpRequestException>(referenceData.EnsureLoadedAsync);
        Assert.False(referenceData.IsLoaded);

        api.Unreachable = false;
        api.WithEmptyLookups();
        await referenceData.EnsureLoadedAsync();

        Assert.True(referenceData.IsLoaded);
    }

    [Fact]
    public async Task LoadCompleted_FiresOncePerAttempt_AndRecordsTheOutcome()
    {
        var api = new StubShepherdApi { Unreachable = true };
        var referenceData = api.CreateReferenceData();
        var raised = 0;
        referenceData.LoadCompleted += () => raised++;

        // Two concurrent callers share one attempt.
        var first = referenceData.EnsureLoadedAsync();
        var second = referenceData.EnsureLoadedAsync();
        await Assert.ThrowsAsync<HttpRequestException>(() => first);
        await Assert.ThrowsAsync<HttpRequestException>(() => second);
        Assert.Equal(1, raised);
        Assert.IsType<HttpRequestException>(referenceData.LastLoadError);

        api.Unreachable = false;
        api.WithEmptyLookups();
        await referenceData.RefreshAsync();
        Assert.Equal(2, raised);
        Assert.Null(referenceData.LastLoadError);
    }

    [Fact]
    public async Task Lookups_ResolveNamesAndConventions()
    {
        var api = new StubShepherdApi().WithEmptyLookups()
            .On(HttpMethod.Get, "/api/statuses", HttpStatusCode.OK,
                """[{"statusID":1,"name":"Draft","description":null},{"statusID":2,"name":"Locked","description":null}]""");
        var referenceData = api.CreateReferenceData();

        await referenceData.EnsureLoadedAsync();

        Assert.Equal("Locked", referenceData.StatusName(2));
        Assert.Equal("Status #9 (unknown)", referenceData.StatusName(9));
        Assert.Equal(1, referenceData.FindStatus("draft")?.StatusID);
        Assert.Null(referenceData.FindArtifactType("DeliveredPrompt"));
    }
}
