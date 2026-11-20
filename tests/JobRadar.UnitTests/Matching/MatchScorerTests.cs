using JobRadar.Contracts;
using JobRadar.Matching.Domain;

namespace JobRadar.UnitTests.Matching;

/// <summary>One test per reason, plus the end-to-end totals the README quotes.</summary>
public sealed class MatchScorerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private readonly Profile _profile = Profile.Demo(Now);

    private static PostingSnapshot Posting(
        string title = "Backend Engineer",
        string description = "",
        WorkMode mode = WorkMode.Remote,
        string location = "Remote",
        string[]? countries = null,
        Seniority seniority = Seniority.Unknown,
        int? salaryMax = null) =>
        new(title, "Nilewave Labs", location, mode, countries ?? [], seniority, null, salaryMax, description, []);

    private static MatchReason Reason(MatchResult result, string code) => result.Reasons.Single(r => r.Code == code);

    [Fact]
    public void Skills_in_the_title_weigh_more_than_skills_in_the_description()
    {
        var result = MatchScorer.Score(Posting(title: "Senior .NET Engineer", description: "C# with PostgreSQL"), _profile);

        var skills = Reason(result, "skills");
        skills.Points.ShouldBe(MatchScorer.TitleSkillPoints + (2 * MatchScorer.BodySkillPoints));
        skills.Text.ShouldBe("Skills: .NET in the title; C#, PostgreSQL in the description");
    }

    [Fact]
    public void Skill_points_are_capped()
    {
        var result = MatchScorer.Score(Posting(title: ".NET C# Azure Docker Engineer", description: "ASP.NET Core PostgreSQL RabbitMQ"), _profile);

        Reason(result, "skills").Points.ShouldBe(MatchScorer.SkillCap);
    }

    [Fact]
    public void Dot_net_inside_asp_net_does_not_count_twice()
    {
        var result = MatchScorer.Score(Posting(description: "We use ASP.NET Core."), _profile);

        Reason(result, "skills").Text.ShouldBe("Skills: ASP.NET Core in the description");
    }

    [Fact]
    public void No_skills_is_a_zero_point_reason() =>
        Reason(MatchScorer.Score(Posting(description: "Ruby on Rails"), _profile), "skills").Points.ShouldBe(0);

    [Theory]
    [InlineData(Seniority.Senior, MatchScorer.LevelFits)]
    [InlineData(Seniority.Unknown, MatchScorer.LevelUnknown)]
    [InlineData(Seniority.Lead, MatchScorer.LevelOff)]
    [InlineData(Seniority.Intern, MatchScorer.LevelOff)]
    public void Level(Seniority level, int points) =>
        Reason(MatchScorer.Score(Posting(seniority: level), _profile), "level").Points.ShouldBe(points);

    [Fact]
    public void Remote_earns_points_and_onsite_is_penalised_for_a_remote_only_profile()
    {
        Reason(MatchScorer.Score(Posting(), _profile), "remote").Points.ShouldBe(MatchScorer.RemotePoints);

        var onsite = MatchScorer.Score(Posting(mode: WorkMode.Onsite, location: "Cairo, Egypt"), _profile);
        Reason(onsite, "remote").Points.ShouldBe(MatchScorer.NotRemotePenalty);
        onsite.Reasons.ShouldNotContain(r => r.Code == "place", "a job you cannot take is not also scored on place");
    }

    [Theory]
    [InlineData(new string[0], MatchScorer.PlaceUnstated)]
    [InlineData(new[] { "Worldwide" }, MatchScorer.PlaceFits)]
    [InlineData(new[] { "EMEA" }, MatchScorer.PlaceFits)]
    [InlineData(new[] { "Germany", "Egypt" }, MatchScorer.PlaceFits)]
    [InlineData(new[] { "USA only" }, MatchScorer.PlaceClosed)]
    public void Place(string[] countries, int points) =>
        Reason(MatchScorer.Score(Posting(countries: countries), _profile), "place").Points.ShouldBe(points);

    [Theory]
    [InlineData(null, 0)]
    [InlineData(60_000, MatchScorer.SalaryFits)]
    [InlineData(30_000, MatchScorer.SalaryBelow)]
    public void Salary_against_the_floor(int? salaryMax, int points) =>
        Reason(MatchScorer.Score(Posting(salaryMax: salaryMax), _profile), "salary").Points.ShouldBe(points);

    [Fact]
    public void Too_many_years_asked_is_marked_down()
    {
        var result = MatchScorer.Score(Posting(description: "You bring 3-5 years of C#, ideally 8+ years overall."), _profile);

        Reason(result, "years").ShouldBe(new MatchReason("years", MatchScorer.TooManyYears, "Asks for 8+ years"));
        MatchScorer.Score(Posting(description: "At least 2 years."), _profile).Reasons.ShouldNotContain(r => r.Code == "years");
    }

    [Theory]
    [InlineData("at least 5 years", 5)]
    [InlineData("minimum of 6 yrs", 6)]
    [InlineData("3 to 4 years", 3)]
    [InlineData("no number here", null)]
    public void Years_asked(string text, int? expected) => MatchScorer.YearsAsked(text).ShouldBe(expected);

    [Fact]
    public void Excluded_title_words_sink_the_score()
    {
        var result = MatchScorer.Score(Posting(title: "Frontend Engineer"), _profile);

        Reason(result, "excluded").Points.ShouldBe(MatchScorer.ExcludedWord);
        result.Score.ShouldBe(0, "totals are clamped at zero");
    }

    [Fact]
    public void A_strong_fit_scores_high_and_explains_itself()
    {
        var result = MatchScorer.Score(
            Posting(title: "Senior Backend Engineer (.NET)", description: "C#, ASP.NET Core, PostgreSQL, RabbitMQ on Azure.", countries: ["Egypt", "Germany"], seniority: Seniority.Senior),
            _profile);

        result.Score.ShouldBe(82);
        result.Reasons.Select(r => r.Code).ShouldBe(["skills", "level", "remote", "place", "salary"]);
    }

    [Fact]
    public void Profile_rules_are_enforced_by_the_domain()
    {
        var profile = Profile.Demo(Now);
        var draft = new ProfileDraft("x", [], [], false, [], null, null, [], 50);

        Should.Throw<DomainRuleException>(() => profile.Update(draft, Now));
        Should.Throw<DomainRuleException>(() => profile.Update(draft with { Skills = ["C#"], NotifyThreshold = 101 }, Now));
    }

    [Fact]
    public void A_posting_is_announced_once_when_it_reaches_the_threshold()
    {
        var scored = new ScoredPosting { PostingId = Guid.NewGuid(), Url = "https://example.com/1", Snapshot = Posting() };
        scored.Apply(new MatchResult(40, []), Now);
        scored.TryAnnounce(_profile, Now).ShouldBeFalse();

        scored.Apply(new MatchResult(75, []), Now);
        scored.TryAnnounce(_profile, Now).ShouldBeTrue();
        scored.TryAnnounce(_profile, Now).ShouldBeFalse();
    }
}
