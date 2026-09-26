using System.Net;
using ShepherdWeb.Infrastructure.Api.Generated;
using ShepherdWeb.Infrastructure.Time;
using ShepherdWeb.Tests.TestSupport;

namespace ShepherdWeb.Tests.Infrastructure;

public class UtcTests
{
    private static readonly DateTime Clock = new(2026, 9, 25, 14, 3, 12);

    [Fact]
    public void FromApi_DeclaresUnspecifiedValuesUtcWithoutShiftingTheClock()
    {
        var result = Utc.FromApi(DateTime.SpecifyKind(Clock, DateTimeKind.Unspecified));

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(Clock, result);
    }

    [Fact]
    public void ToApi_RejectsUnspecifiedKind()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Utc.ToApi(DateTime.SpecifyKind(Clock, DateTimeKind.Unspecified)));
        Assert.Contains("unspecified kind", ex.Message);
    }

    [Fact]
    public void ToApi_ConvertsLocalToUtc()
    {
        var local = DateTime.SpecifyKind(Clock, DateTimeKind.Local);

        Assert.Equal(local.ToUniversalTime(), Utc.ToApi(local));
        Assert.Equal(DateTimeKind.Utc, Utc.ToApi(local).Kind);
    }

    [Fact]
    public void FromUtcInput_KeepsTheClockAndMarksUtc()
    {
        var result = Utc.FromUtcInput(Clock);

        Assert.Equal(Clock, result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void Format_IsLabeledUtc() =>
        Assert.Equal("2026-09-25 14:03:12 UTC", Utc.Format(Clock));
}

/// <summary>
/// The converter is wired into the generated clients (GeneratedClientSerialization), so these tests
/// go through a real client to prove the wiring as well as the conversion (finding F4, AD-10).
/// </summary>
public class UtcDateTimeJsonConverterTests
{
    private const string VersionJson =
        """{"experimentVersionID":1,"name":"Pit Stop","versionLabel":"1.0","statusID":2,"effectiveDateUTC":"__DATE__","description":"d","lockedUTC":null}""";

    [Theory]
    [InlineData("2026-08-30T00:00:00", "2026-08-30T00:00:00")]        // ShepherdAPI today: no offset, means UTC
    [InlineData("2026-08-30T00:00:00Z", "2026-08-30T00:00:00")]
    [InlineData("2026-08-30T02:00:00+02:00", "2026-08-30T00:00:00")]  // explicit offset is converted
    public async Task Read_ProducesUtcKind(string wire, string expectedUtc)
    {
        var api = new StubShepherdApi()
            .On(HttpMethod.Get, "/api/experiment-versions/1", HttpStatusCode.OK, VersionJson.Replace("__DATE__", wire));

        var version = await new ExperimentVersionsClient(api.CreateHttpClient()).ExperimentVersionsGetAsync(1);

        Assert.Equal(DateTimeKind.Utc, version.EffectiveDateUTC.Kind);
        Assert.Equal(DateTime.Parse(expectedUtc), version.EffectiveDateUTC);
        Assert.Null(version.LockedUTC);
    }

    [Fact]
    public async Task Write_SendsIsoUtcWithZ()
    {
        var api = new StubShepherdApi()
            .On(HttpMethod.Post, "/api/experiment-versions", HttpStatusCode.OK, VersionJson.Replace("__DATE__", "2026-09-25T14:03:12"));

        await new ExperimentVersionsClient(api.CreateHttpClient()).ExperimentVersionsPostAsync(new CreateExperimentVersionRequest
        {
            Name = "Pit Stop", VersionLabel = "1.0", StatusID = 1, Description = "d",
            EffectiveDateUTC = new DateTime(2026, 9, 25, 14, 3, 12, DateTimeKind.Utc),
        });

        Assert.Contains("\"effectiveDateUTC\":\"2026-09-25T14:03:12.000Z\"", api.Requests.Single().Body);
    }

    [Fact]
    public async Task Write_RefusesUnspecifiedKind_BeforeAnythingIsSent()
    {
        var api = new StubShepherdApi();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExperimentVersionsClient(api.CreateHttpClient()).ExperimentVersionsPostAsync(new CreateExperimentVersionRequest
            {
                Name = "Pit Stop", VersionLabel = "1.0", StatusID = 1, Description = "d",
                EffectiveDateUTC = new DateTime(2026, 9, 25, 14, 3, 12, DateTimeKind.Unspecified),
            }));

        Assert.Empty(api.Requests);
    }
}
