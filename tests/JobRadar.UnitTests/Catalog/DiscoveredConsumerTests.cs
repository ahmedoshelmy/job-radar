using JobRadar.Catalog.Application;
using JobRadar.Catalog.Domain;
using JobRadar.Contracts;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace JobRadar.UnitTests.Catalog;

public sealed class DiscoveredConsumerTests
{
    private sealed class InMemoryPostings : IPostingRepository
    {
        public List<JobPosting> Rows { get; } = [];

        public Task<JobPosting?> FindDuplicateAsync(string fingerprint, string canonicalUrl, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.FirstOrDefault(p => p.Fingerprint == fingerprint) ?? Rows.FirstOrDefault(p => p.CanonicalUrl == canonicalUrl));

        public void Add(JobPosting posting) => Rows.Add(posting);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static JobPostingDiscovered Found(string source, string title, string company, string url) => new()
    {
        Source = source,
        ExternalId = $"{source}:{title}",
        Url = url,
        Title = title,
        Company = company,
        WorkMode = WorkMode.Remote,
        DiscoveredAt = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public async Task The_same_opening_from_two_feeds_is_added_once_and_credited_to_both()
    {
        var postings = new InMemoryPostings();
        await using var provider = new ServiceCollection()
            .AddSingleton<IPostingRepository>(postings)
            .AddMassTransitTestHarness(x => x.AddConsumer<JobPostingDiscoveredConsumer>())
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(Found("greenhouse", "Senior Backend Engineer (.NET)", "Nilewave Labs", "https://example.com/gh/1"));
        await harness.Bus.Publish(Found("remotive", "Sr. Back-end Engineer (.NET) - Remote", "Nilewave Labs Inc.", "https://example.com/rm/9"));
        await harness.Bus.Publish(Found("remotive", "Robotics Engineer", "Cairo Robotics", "https://example.com/gh/2?utm_source=remotive"));
        await harness.Bus.Publish(Found("lever", "C++ Robotics Software Engineer", "Cairo Robotics", "https://example.com/gh/2"));

        await harness.InactivityTask;
        harness.Consumed.Select<JobPostingDiscovered>().Count().ShouldBe(4);
        harness.Published.Select<JobPostingAdded>().Count().ShouldBe(2);
        postings.Rows.Count.ShouldBe(2);
        postings.Rows[0].AlsoSeenIn.ShouldBe(["remotive"]);
        postings.Rows[1].AlsoSeenIn.ShouldBe(["lever"], "matched by canonical URL although the titles differ");
    }
}
