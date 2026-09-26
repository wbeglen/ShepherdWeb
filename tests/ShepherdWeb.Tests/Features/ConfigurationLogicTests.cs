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
    public async Task Delete_Accepts204NoContent()
    {
        var api = new StubShepherdApi().On(HttpMethod.Delete, "/api/parameters/3", HttpStatusCode.NoContent);

        await new ParametersClient(api.CreateHttpClient()).ParametersDeleteAsync(3); // would throw ApiException without the fix

        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task Create_Accepts201Created_AndReturnsTheCreatedEntity()
    {
        // ShepherdAPI answers every POST-create with 201 (CreatedAtAction) while documenting 200.
        var api = new StubShepherdApi().On(HttpMethod.Post, "/api/artifact-types", HttpStatusCode.Created,
            """{"artifactTypeID":1,"name":"DeliveredPrompt","description":null}""");

        var created = await new ArtifactTypesClient(api.CreateHttpClient())
            .ArtifactTypesPostAsync(new ArtifactTypeRequest { Name = "DeliveredPrompt" });

        Assert.Equal(1, created.ArtifactTypeID);
    }

    [Fact]
    public async Task Delete_StillReportsRealFailures()
    {
        var api = new StubShepherdApi().On(HttpMethod.Delete, "/api/parameters/3", HttpStatusCode.Conflict,
            """{"title":"Record Locked","status":409}""");

        var ex = await Assert.ThrowsAsync<ApiException>(() => new ParametersClient(api.CreateHttpClient()).ParametersDeleteAsync(3));
        Assert.Equal(409, ex.StatusCode);
    }
}
