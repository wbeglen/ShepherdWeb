using System.ComponentModel.DataAnnotations;
using System.Net;
using ShepherdWeb.Features.ExperimentVersions;
using ShepherdWeb.Features.Iterations;
using ShepherdWeb.Features.ScoreRules;
using ShepherdWeb.Infrastructure;
using ShepherdWeb.Infrastructure.Api.Generated;
using ShepherdWeb.Tests.TestSupport;

namespace ShepherdWeb.Tests.Features;

public class VersionLifecycleTests
{
    private static readonly ConventionsOptions Conventions = new();

    private static readonly Status[] Statuses =
    [
        new() { StatusID = 1, Name = "Draft" }, new() { StatusID = 2, Name = "Locked" },
        new() { StatusID = 3, Name = "Retired" }, new() { StatusID = 4, Name = "Complete" },
    ];

    private static ExperimentVersion Version(bool locked) =>
        new() { ExperimentVersionID = 1, LockedUTC = locked ? new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc) : null };

    [Fact]
    public void Unlocked_NeverOffersLocked()
    {
        var names = VersionLifecycle.OfferableStatuses(Version(false), Statuses, Conventions).Select(s => s.Name);

        Assert.Equal(["Draft", "Retired", "Complete"], names);
    }

    [Fact]
    public void Locked_NeverOffersDraft()
    {
        var names = VersionLifecycle.OfferableStatuses(Version(true), Statuses, Conventions).Select(s => s.Name);

        Assert.Equal(["Locked", "Retired", "Complete"], names);
    }

    [Fact]
    public void LockedStatusWithoutLockTimestamp_IsAMismatch()
    {
        var message = VersionLifecycle.StatusMismatch(Version(false), "Locked", Conventions);

        Assert.NotNull(message);
        Assert.Contains("no lock timestamp", message);
    }

    [Fact]
    public void DraftStatusOnLockedVersion_IsAMismatch() =>
        Assert.NotNull(VersionLifecycle.StatusMismatch(Version(true), "Draft", Conventions));

    [Theory]
    [InlineData(false, "Draft")]
    [InlineData(true, "Locked")]
    [InlineData(true, "Retired")]
    [InlineData(false, "Retired")]
    public void ConsistentStates_AreNotMismatches(bool locked, string status) =>
        Assert.Null(VersionLifecycle.StatusMismatch(Version(locked), status, Conventions));
}

public class PromptTextTests
{
    [Fact]
    public void Sha256_IsOfUtf8BytesWithoutBom_UpperCaseHex()
    {
        // Well-known SHA-256 of "abc".
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", new PromptText("abc").Sha256);
    }

    [Fact]
    public void LineEndingsAreSignificant()
    {
        var lf = new PromptText("a\nb");
        var crlf = new PromptText("a\r\nb");

        Assert.NotEqual(lf.Sha256, crlf.Sha256);
        Assert.Equal(2, lf.LineCount);
        Assert.Equal(2, crlf.LineCount);
        Assert.Equal(4, crlf.ByteCount);
    }

    [Fact]
    public void CountsBytesNotCharacters()
    {
        var text = new PromptText("é✓");

        Assert.Equal(2, text.CharacterCount);
        Assert.Equal(5, text.ByteCount); // 2 + 3 bytes in UTF-8
    }

    [Fact]
    public void Empty_HasNoLines() => Assert.Equal(0, new PromptText(null).LineCount);
}

public class ScoreRuleFormTests
{
    private static List<ValidationResult> Validate(ScoreRuleForm form)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void RequiresPhaseCategoryAndMinBelowMax()
    {
        var errors = Validate(new ScoreRuleForm { MinScore = 5, MaxScore = 5 }).Select(r => r.MemberNames.Single()).ToList();

        Assert.Contains(nameof(ScoreRuleForm.PhaseID), errors);
        Assert.Contains(nameof(ScoreRuleForm.ScoreCategoryID), errors);
        Assert.Contains(nameof(ScoreRuleForm.MaxScore), errors);
    }

    [Fact]
    public void RejectsAnExistingPhaseCategoryPair()
    {
        var form = new ScoreRuleForm { PhaseID = 1, ScoreCategoryID = 2, MinScore = 0, MaxScore = 5, ExistingPairs = new HashSet<(int, int)> { (1, 2) } };

        Assert.Contains(Validate(form), r => r.ErrorMessage!.Contains("already has a rule"));
    }

    [Fact]
    public void ValidForm_HasNoErrors_AndSendsActiveTrue()
    {
        var form = new ScoreRuleForm { PhaseID = 1, ScoreCategoryID = 2, WeightPercent = 33.33m, MinScore = 0, MaxScore = 5, RuleNotes = "  " };

        Assert.Empty(Validate(form));
        var request = form.ToRequest(9);
        Assert.True(request.Active); // N2: Active ignored, sent as the API's own default.
        Assert.Equal(9, request.ExperimentVersionID);
        Assert.Null(request.RuleNotes);
        Assert.Equal(33.33, request.WeightPercent, 2);
    }
}

public class GeneratedClientCustomizationTests
{
    [Fact]
    public async Task DeclaredDelete_Accepts204NoContent()
    {
        // The contract declares 204 for this delete; the generated code accepts it without R8.
        var api = new StubShepherdApi().On(HttpMethod.Delete, "/api/parameters/3", HttpStatusCode.NoContent);

        await new ParametersClient(api.CreateHttpClient()).ParametersDeleteAsync(3);

        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task DeclaredCreate_Accepts201Created_AndReturnsTheCreatedEntity()
    {
        var api = new StubShepherdApi().On(HttpMethod.Post, "/api/score-rules", HttpStatusCode.Created,
            """{"scoreRuleID":7,"experimentVersionID":1,"phaseID":1,"scoreCategoryID":1,"weightPercent":100,"minScore":0,"maxScore":5,"active":true,"ruleNotes":null}""");

        var created = await new ScoreRulesClient(api.CreateHttpClient()).ScoreRulesPostAsync(new CreateScoreRuleRequest());

        Assert.Equal(7, created.ScoreRuleID);
    }

    [Fact]
    public async Task UndeclaredCreate_Accepts201Created_ThroughR8()
    {
        // This create is still documented as 200; R8 maps the real 201 (GeneratedClientCustomization).
        var api = new StubShepherdApi().On(HttpMethod.Post, "/api/artifact-types", HttpStatusCode.Created,
            """{"artifactTypeID":1,"name":"DeliveredPrompt","description":null}""");

        var created = await new ArtifactTypesClient(api.CreateHttpClient())
            .ArtifactTypesPostAsync(new ArtifactTypeRequest { Name = "DeliveredPrompt" });

        Assert.Equal(1, created.ArtifactTypeID);
    }

    [Fact]
    public async Task UndeclaredCreate_ExperimentVersion_Accepts201Created_ThroughR8()
    {
        var api = new StubShepherdApi().On(HttpMethod.Post, "/api/experiment-versions", HttpStatusCode.Created,
            """{"experimentVersionID":4,"name":"N","versionLabel":"1","statusID":1,"effectiveDateUTC":"2026-10-23T00:00:00Z","description":"d","lockedUTC":null}""");

        var created = await new ExperimentVersionsClient(api.CreateHttpClient())
            .ExperimentVersionsPostAsync(new CreateExperimentVersionRequest { EffectiveDateUTC = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc) });

        Assert.Equal(4, created.ExperimentVersionID);
    }

    [Fact]
    public async Task TypedFailure_KeepsTheResponseBodyForTheMapper()
    {
        // Declared 409s throw ApiException<ProblemDetails>; ReadResponseAsString keeps the raw body.
        const string body = """{"title":"Version Locked","status":409,"detail":"locked","code":"version-locked"}""";
        var api = new StubShepherdApi().On(HttpMethod.Delete, "/api/parameters/3", HttpStatusCode.Conflict, body);

        var ex = await Assert.ThrowsAsync<ApiException<ProblemDetails>>(() => new ParametersClient(api.CreateHttpClient()).ParametersDeleteAsync(3));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(body, ex.Response);
    }
}
