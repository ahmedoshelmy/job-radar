using System.Reflection;
using NetArchTest.Rules;

namespace JobRadar.UnitTests;

/// <summary>The dependency rule, enforced: domain code knows nothing about EF Core, MassTransit or ASP.NET Core.</summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly[] Modules =
    [
        typeof(JobRadar.Catalog.CatalogModule).Assembly,
        typeof(JobRadar.Matching.MatchingModule).Assembly,
        typeof(JobRadar.Tracker.Infrastructure.TrackerModule).Assembly,
    ];

    public static TheoryData<string> ModuleNames => ["Catalog", "Matching", "Tracker"];

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Domain_depends_on_no_framework(string module)
    {
        var result = Types.InAssembly(Modules.Single(a => a.GetName().Name == $"JobRadar.{module}"))
            .That().ResideInNamespace($"JobRadar.{module}.Domain")
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "MassTransit", "Microsoft.AspNetCore", "Npgsql", "FluentValidation")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Application_does_not_reach_into_infrastructure(string module)
    {
        var result = Types.InAssembly(Modules.Single(a => a.GetName().Name == $"JobRadar.{module}"))
            .That().ResideInNamespace($"JobRadar.{module}.Application")
            .ShouldNot().HaveDependencyOnAny($"JobRadar.{module}.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Modules_do_not_reference_each_other()
    {
        foreach (var module in Modules)
        {
            var others = Modules.Where(m => m != module).Select(m => m.GetName().Name!).ToArray();
            module.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain(n => others.Contains(n));
        }
    }
}
