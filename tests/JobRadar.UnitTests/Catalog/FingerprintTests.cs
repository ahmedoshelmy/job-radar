using JobRadar.Catalog.Domain;
using JobRadar.Contracts;

namespace JobRadar.UnitTests.Catalog;

public sealed class FingerprintTests
{
    [Fact]
    public void A_mirrored_posting_meets_itself_across_spelling_suffix_and_remote_markers()
    {
        var board = Fingerprint.For("Nilewave Labs", "Senior Backend Engineer (.NET)", WorkMode.Remote, "Remote, Egypt");
        var aggregator = Fingerprint.For("Nilewave Labs Inc.", "Sr. Back-end Engineer (.NET) - Remote", WorkMode.Remote, "EMEA");

        aggregator.ShouldBe(board);
        board.ShouldBe("nilewave|senior backend engineer .net|remote");
    }

    [Theory]
    [InlineData("Platform Engineer (m/w/d)", "platform engineer")]
    [InlineData("Platform Engineer m/f/d", "platform engineer")]
    [InlineData("Jr. Full Stack Developer [Remote, EU]", "junior fullstack developer")]
    [InlineData("Dot Net Developer", ".net developer")]
    [InlineData("C# / C++ Engineer", "c# c++ engineer")]
    public void Title_normalisation(string title, string expected) => Fingerprint.NormTitle(title).ShouldBe(expected);

    [Theory]
    [InlineData("Deltasoft GmbH", "deltasoft")]
    [InlineData("Pyramid Data, Ltd.", "pyramid data")]
    [InlineData("Salt & Pepper Technologies", "salt and pepper")]
    [InlineData("Café Systems", "cafe systems")]
    public void Company_normalisation(string company, string expected) => Fingerprint.NormCompany(company).ShouldBe(expected);

    [Fact]
    public void Onsite_jobs_in_different_cities_stay_apart()
    {
        var cairo = Fingerprint.For("Deltasoft", "QA Engineer", WorkMode.Onsite, "Cairo, Egypt");
        var giza = Fingerprint.For("Deltasoft", "QA Engineer", WorkMode.Onsite, "Giza HQ, Egypt");

        cairo.ShouldNotBe(giza);
        Fingerprint.PlaceBucket(WorkMode.Onsite, "Giza HQ, Egypt").ShouldBe("giza");
        Fingerprint.PlaceBucket(WorkMode.Unknown, "").ShouldBe("any");
    }

    [Theory]
    [InlineData("https://www.Example.com/jobs/1/?utm_source=x&utm_medium=y#apply", "https://example.com/jobs/1")]
    [InlineData("https://example.com/jobs/1?gh_src=abc&id=7", "https://example.com/jobs/1?id=7")]
    [InlineData("https://example.com/jobs/1?lever-source=feed", "https://example.com/jobs/1")]
    public void Canonical_url_drops_tracking_noise(string url, string expected) => Fingerprint.CanonicalUrl(url).ShouldBe(expected);

    [Fact]
    public void A_sighting_from_a_new_feed_is_credited_once()
    {
        var at = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
        var posting = new JobPosting
        {
            Id = Guid.NewGuid(),
            Fingerprint = "f",
            CanonicalUrl = "https://example.com/1",
            Source = "greenhouse",
            ExternalId = "1",
            Url = "https://example.com/1",
            Title = "T",
            Company = "C",
            FirstSeenAt = at,
        };

        posting.RecordSighting("remotive", at.AddHours(1)).ShouldBeTrue();
        posting.RecordSighting("remotive", at.AddHours(2)).ShouldBeFalse();
        posting.RecordSighting("greenhouse", at.AddHours(3)).ShouldBeFalse();
        posting.AlsoSeenIn.ShouldBe(["remotive"]);
        posting.LastSeenAt.ShouldBe(at.AddHours(3));
    }
}
