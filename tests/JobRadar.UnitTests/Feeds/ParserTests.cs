using JobRadar.Contracts;
using JobRadar.Feeds.Sources;

namespace JobRadar.UnitTests.Feeds;

/// <summary>Each adapter against an invented fixture in its board's real response shape.</summary>
public sealed class ParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    internal static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "feeds", name));

    private static IReadOnlyList<JobPostingDiscovered> Parse(IJobBoardSource source, string file, string? board, string label) =>
        source.Parse(Fixture(file), new BoardTarget(source.Kind, board, label, TimeSpan.FromDays(1)), Now);

    [Fact]
    public void Greenhouse_reads_escaped_html_remote_countries_and_skips_jobs_without_links()
    {
        var jobs = Parse(new GreenhouseSource(), "greenhouse-nilewave.json", "nilewave", "Nilewave Labs");

        jobs.Count.ShouldBe(4);
        var backend = jobs[0];
        backend.ExternalId.ShouldBe("nilewave:4100001");
        backend.Company.ShouldBe("Nilewave Labs");
        backend.WorkMode.ShouldBe(WorkMode.Remote);
        backend.Countries.ShouldBe(["Egypt", "Germany"]);
        backend.Seniority.ShouldBe(Seniority.Senior);
        backend.Description.ShouldContain("• C# and ASP.NET Core");
        backend.Description.ShouldNotContain("&lt;");
        backend.PostedAt.ShouldBe(new DateTimeOffset(2026, 11, 17, 14, 30, 0, TimeSpan.Zero));
        backend.Tags.ShouldBe(["Engineering"]);
        jobs.Single(j => j.Title == "Staff Platform Engineer").Seniority.ShouldBe(Seniority.Lead);
        jobs.Single(j => j.Title == "Data Analyst").WorkMode.ShouldBe(WorkMode.Unknown);
    }

    [Fact]
    public void Lever_maps_workplace_type_epoch_dates_and_usd_salary_only()
    {
        var jobs = Parse(new LeverSource(), "lever-cairo-robotics.json", "cairo-robotics", "Cairo Robotics");

        jobs.Count.ShouldBe(3);
        var robotics = jobs[0];
        robotics.Company.ShouldBe("Cairo Robotics");
        robotics.WorkMode.ShouldBe(WorkMode.Hybrid);
        robotics.SalaryMinUsd.ShouldBe(30_000);
        robotics.SalaryMaxUsd.ShouldBe(42_000);
        robotics.SalaryText.ShouldBe("USD 30,000–42,000 / per-year-salary");
        robotics.PostedAt.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1763370000000));
        jobs[1].WorkMode.ShouldBe(WorkMode.Remote);
        jobs[2].Seniority.ShouldBe(Seniority.Mid);
        jobs[2].SalaryMaxUsd.ShouldBeNull("EUR is not converted");
        jobs[2].SalaryText.ShouldNotBeNull();
    }

    [Fact]
    public void Ashby_skips_unlisted_and_reads_address_and_secondary_locations()
    {
        var jobs = Parse(new AshbySource(), "ashby-deltasoft.json", "deltasoft", "Deltasoft");

        jobs.Count.ShouldBe(3);
        var platform = jobs[0];
        platform.WorkMode.ShouldBe(WorkMode.Remote);
        platform.Location.ShouldBe("Germany");
        platform.Countries.ShouldBe(["Germany", "EMEA"]);
        platform.SalaryText.ShouldBe("USD 70K – 90K");
        jobs[1].WorkMode.ShouldBe(WorkMode.Onsite);
        jobs[1].Location.ShouldBe("Cairo, Egypt");
        jobs[1].Description.ShouldBe("Manual and Playwright testing.");
        jobs[2].Seniority.ShouldBe(Seniority.Mid);
        jobs.ShouldNotContain(j => j.Title.Contains("Unlisted", StringComparison.Ordinal));
    }

    [Fact]
    public void Workable_uses_account_name_telecommuting_and_experience_hint()
    {
        var jobs = Parse(new WorkableSource(), "workable-pyramid-data.json", "pyramid-data", "ignored label");

        jobs.Count.ShouldBe(3);
        jobs.ShouldAllBe(j => j.Company == "Pyramid Data");
        jobs[0].WorkMode.ShouldBe(WorkMode.Onsite);
        jobs[0].Location.ShouldBe("Giza, Egypt");
        jobs[0].Seniority.ShouldBe(Seniority.Senior);
        jobs[1].WorkMode.ShouldBe(WorkMode.Remote);
        jobs[1].Seniority.ShouldBe(Seniority.Junior, "\"Associate\" level hint");
        jobs[1].PostedAt.ShouldBe(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Remotive_is_always_remote_and_splits_required_locations()
    {
        var jobs = Parse(new RemotiveSource(), "remotive.json", "software-dev", "Remotive");

        jobs.Count.ShouldBe(4);
        jobs.ShouldAllBe(j => j.WorkMode == WorkMode.Remote && j.Source == "remotive");
        jobs[0].Company.ShouldBe("Nilewave Labs Inc.");
        jobs[0].Countries.ShouldBe(["EMEA"]);
        jobs[0].SalaryText.ShouldBe("$55k - $75k");
        jobs[0].PostedAt.ShouldBe(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero), "naive timestamps are UTC");
        jobs[2].SalaryText.ShouldBeNull();
        jobs[2].Tags.ShouldContain("Software Development");
    }

    [Fact]
    public void Arbeitnow_treats_vague_places_as_unrestricted_and_keeps_onsite_jobs_onsite()
    {
        var jobs = Parse(new ArbeitnowSource(), "arbeitnow.json", null, "Arbeitnow");

        jobs.Count.ShouldBe(3);
        jobs[0].Countries.ShouldBeEmpty();
        jobs[0].WorkMode.ShouldBe(WorkMode.Remote);
        jobs[1].Countries.ShouldBe(["Germany"]);
        jobs[2].WorkMode.ShouldBe(WorkMode.Onsite);
        jobs[2].Seniority.ShouldBe(Seniority.Intern);
        jobs[0].PostedAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1763460000));
    }

    [Theory]
    [InlineData("greenhouse", "nilewave", "https://boards-api.greenhouse.io/v1/boards/nilewave/jobs?content=true")]
    [InlineData("lever", "cairo-robotics", "https://api.lever.co/v0/postings/cairo-robotics?mode=json")]
    [InlineData("ashby", "deltasoft", "https://api.ashbyhq.com/posting-api/job-board/deltasoft?includeCompensation=true")]
    [InlineData("workable", "pyramid-data", "https://apply.workable.com/api/v1/widget/accounts/pyramid-data?details=true")]
    [InlineData("remotive", "software-dev", "https://remotive.com/api/remote-jobs?category=software-dev")]
    [InlineData("arbeitnow", null, "https://www.arbeitnow.com/api/job-board-api")]
    public void Each_source_builds_its_public_api_url(string kind, string? board, string expected)
    {
        IJobBoardSource[] all = [new GreenhouseSource(), new LeverSource(), new AshbySource(), new WorkableSource(), new RemotiveSource(), new ArbeitnowSource()];
        var source = all.Single(s => s.Kind == kind);

        source.BuildUri(new BoardTarget(kind, board, kind, TimeSpan.FromDays(1))).AbsoluteUri.ShouldBe(expected);
    }

    [Fact]
    public void Garbage_fields_never_throw()
    {
        var jobs = new GreenhouseSource().Parse("""{"jobs":[{"id":{},"title":42,"absolute_url":"javascript:alert(1)"},{"title":"Ok","absolute_url":"https://example.com/a","location":"oops"}]}""",
            new BoardTarget("greenhouse", "x", "X", TimeSpan.FromDays(1)), Now);

        jobs.Count.ShouldBe(1);
        jobs[0].Location.ShouldBe("");
    }
}
