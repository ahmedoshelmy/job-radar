using JobRadar.Contracts;
using JobRadar.Feeds.Text;

namespace JobRadar.UnitTests.Feeds;

public sealed class TextTests
{
    [Fact]
    public void Html_to_text_keeps_structure_and_drops_scripts()
    {
        var text = FeedText.HtmlToText("<h2>About</h2><p>We&nbsp;build &amp; ship.</p><script>x()</script><ul><li>One</li><li>Two</li></ul>");

        text.ShouldBe("About\nWe build & ship.\n\n• One\n• Two");
    }

    [Theory]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("ftp://example.com/x", null)]
    [InlineData("not a url", null)]
    [InlineData(" https://Example.com/jobs/1 ", "https://example.com/jobs/1")]
    public void Safe_url_accepts_only_http_links(string input, string? expected) => FeedText.SafeUrl(input).ShouldBe(expected);

    [Theory]
    [InlineData("1763460000", 1763460000L)]
    [InlineData("1763460000000", 1763460000L)]
    public void Parse_date_reads_unix_seconds_and_milliseconds(string input, long expectedSeconds) =>
        FeedText.ParseDate(input)!.Value.ToUnixTimeSeconds().ShouldBe(expectedSeconds);

    [Theory]
    [InlineData(50_000, "USD", null, 50_000)]
    [InlineData(50_000, "EUR", null, null)]
    [InlineData(50, "USD", null, null)]
    [InlineData(4_000, "USD", "per-month", null)]
    public void Usd_per_year_ignores_other_currencies_periods_and_noise(int amount, string currency, string? period, int? expected) =>
        FeedText.UsdPerYear(amount, currency, period).ShouldBe(expected);

    [Theory]
    [InlineData("Software Engineering Intern", Seniority.Intern)]
    [InlineData("Principal Engineer", Seniority.Lead)]
    [InlineData("Sr. Backend Engineer", Seniority.Senior)]
    [InlineData("Software Engineer III", Seniority.Senior)]
    [InlineData("Software Engineer I", Seniority.Junior)]
    [InlineData("Software Engineer II, Payments", Seniority.Mid)]
    [InlineData("Backend Engineer", Seniority.Unknown)]
    public void Seniority_comes_from_the_title(string title, Seniority expected) => Classify.SeniorityOf(title).ShouldBe(expected);

    [Fact]
    public void Seniority_falls_back_to_the_level_hint() => Classify.SeniorityOf("Backend Engineer", "Entry level").ShouldBe(Seniority.Junior);

    [Theory]
    [InlineData("Hybrid - Cairo", WorkMode.Hybrid)]
    [InlineData("Remote (EMEA)", WorkMode.Remote)]
    [InlineData("On-site, Berlin", WorkMode.Onsite)]
    [InlineData("Berlin", WorkMode.Unknown)]
    public void Work_mode_is_read_from_free_text(string text, WorkMode expected) => Classify.WorkModeOf(text).ShouldBe(expected);
}
