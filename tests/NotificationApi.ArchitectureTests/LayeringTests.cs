using System.Reflection;
using NetArchTest.Rules;
using NotificationApi.Application.Channels;

namespace NotificationApi.ArchitectureTests;

/// <summary>Enforces the dependency rule (engineering_standards.md §2.1, §4.5).</summary>
public sealed class LayeringTests
{
    private const string Domain = "NotificationApi.Domain";
    private const string Application = "NotificationApi.Application";
    private const string Infrastructure = "NotificationApi.Infrastructure";
    private const string Web = "NotificationApi.Web";

    private static readonly Assembly DomainAssembly = typeof(global::NotificationApi.Domain.Recipients.Handle).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(INotificationChannel).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(global::NotificationApi.Infrastructure.StorageOptions).Assembly;
    private static readonly Assembly WebAssembly = typeof(Program).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_else() =>
        Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(Application, Infrastructure, Web, "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "System.Net.Http")
            .GetResult()
            .ShouldPass();

    [Fact]
    public void Application_does_not_depend_on_infrastructure_web_or_ef() =>
        Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(Infrastructure, Web, "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult()
            .ShouldPass();

    [Fact]
    public void Infrastructure_does_not_depend_on_web() =>
        Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(Web)
            .GetResult()
            .ShouldPass();

    [Fact]
    public void Web_uses_infrastructure_only_from_the_composition_root() =>
        Types.InAssembly(WebAssembly)
            .That()
            .DoNotHaveName("Program")
            .And()
            .DoNotHaveNameStartingWith("<") // compiler-generated
            .ShouldNot()
            .HaveDependencyOn(Infrastructure)
            .GetResult()
            .ShouldPass();

    [Fact]
    public void Channel_implementations_live_in_infrastructure_and_are_sealed() =>
        Types.InAssemblies([ApplicationAssembly, InfrastructureAssembly, WebAssembly])
            .That()
            .ImplementInterface(typeof(INotificationChannel))
            .Should()
            .ResideInNamespaceStartingWith(Infrastructure)
            .And()
            .BeSealed()
            .GetResult()
            .ShouldPass();
}

internal static class TestResultExtensions
{
    public static void ShouldPass(this NetArchTest.Rules.TestResult result) =>
        result.IsSuccessful.ShouldBeTrue(
            "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []));
}
