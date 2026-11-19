using System.Reflection;
using FluentAssertions;
using GSS.Business;
using GSS.Core.Entities;
using GSS.Util;
using NetArchTest.Rules;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>
/// Keeps Dapper, Npgsql, DbUp and StackExchange.Redis out of Core and Business,
/// and Microsoft.AspNetCore.* out of Core, Business and Util.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class DependencyGuardTests
{
    private static readonly Assembly CoreAssembly = typeof(EntityBase).Assembly;
    private static readonly Assembly BusinessAssembly = typeof(DependencyInjection).Assembly;
    private static readonly Assembly UtilAssembly = typeof(ConfigurationKeys).Assembly;

    #region Dapper (ORM)

    [Fact]
    public void CoreLayer_ShouldNotDependOn_Dapper()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot().HaveDependencyOn("Dapper")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "GSS.Core must remain persistence-ignorant - Dapper belongs only in GSS.Infrastructure");
    }

    [Fact]
    public void BusinessLayer_ShouldNotDependOn_Dapper()
    {
        var result = Types.InAssembly(BusinessAssembly)
            .ShouldNot().HaveDependencyOn("Dapper")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "GSS.Business must not depend on Dapper; data access concerns belong in repositories within GSS.Infrastructure");
    }

    #endregion

    #region Npgsql (PostgreSQL driver)

    [Fact]
    public void CoreLayer_ShouldNotDependOn_Npgsql()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot().HaveDependencyOn("Npgsql")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "GSS.Core must be database-agnostic - Npgsql is a PostgreSQL-specific driver that belongs only in GSS.Infrastructure");
    }

    [Fact]
    public void BusinessLayer_ShouldNotDependOn_Npgsql()
    {
        var result = Types.InAssembly(BusinessAssembly)
            .ShouldNot().HaveDependencyOn("Npgsql")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "GSS.Business must be database-agnostic - Npgsql is a PostgreSQL-specific driver that belongs only in GSS.Infrastructure");
    }

    #endregion

    #region DbUp (Database migrations)

    [Fact]
    public void CoreLayer_ShouldNotDependOn_DbUp()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot().HaveDependencyOn("DbUp")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "GSS.Core must not reference migration tooling - DbUp belongs only in GSS.Infrastructure");
    }

    [Fact]
    public void BusinessLayer_ShouldNotDependOn_DbUp()
    {
        var result = Types.InAssembly(BusinessAssembly)
            .ShouldNot().HaveDependencyOn("DbUp")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "GSS.Business must not reference migration tooling - DbUp belongs only in GSS.Infrastructure");
    }

    #endregion

    #region StackExchange.Redis (Cache provider)

    [Fact]
    public void CoreLayer_ShouldNotDependOn_StackExchangeRedis()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot().HaveDependencyOn("StackExchange.Redis")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "GSS.Core must be cache-provider-agnostic - Redis implementation belongs only in GSS.Infrastructure");
    }

    [Fact]
    public void BusinessLayer_ShouldNotDependOn_StackExchangeRedis()
    {
        var result = Types.InAssembly(BusinessAssembly)
            .ShouldNot().HaveDependencyOn("StackExchange.Redis")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: "GSS.Business must be cache-provider-agnostic - Redis implementation belongs only in GSS.Infrastructure");
    }

    #endregion

    #region Microsoft.AspNetCore (Web framework)

    // Infrastructure is not guarded: it uses ASP.NET Core on purpose (IStartupFilter, IHttpContextAccessor).
    // NetArchTest checks emitted type references, so an unused FrameworkReference does not fail these rules.

    [Fact]
    public void CoreLayer_ShouldNotDependOn_AspNetCore()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot().HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: $"GSS.Core must be transport-agnostic - ASP.NET Core types belong only in GSS.Api and GSS.Infrastructure. Leaked in: {Offenders(result)}. Move the HTTP concern out to GSS.Api, or express it as an interface in Core that GSS.Infrastructure implements; do not add a Microsoft.AspNetCore.App FrameworkReference to GSS.Core");
    }

    [Fact]
    public void BusinessLayer_ShouldNotDependOn_AspNetCore()
    {
        var result = Types.InAssembly(BusinessAssembly)
            .ShouldNot().HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: $"GSS.Business must be transport-agnostic - ASP.NET Core types belong only in GSS.Api and GSS.Infrastructure. Leaked in: {Offenders(result)}. Services should take plain DTOs and return Result<T>; if a service needs request context (user, correlation id), inject an abstraction such as IUserContext instead of HttpContext or IHttpContextAccessor");
    }

    [Fact]
    public void UtilLayer_ShouldNotDependOn_AspNetCore()
    {
        var result = Types.InAssembly(UtilAssembly)
            .ShouldNot().HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            because: $"GSS.Util must stay a dependency-light helper library shared by every layer - ASP.NET Core types belong only in GSS.Api and GSS.Infrastructure. Leaked in: {Offenders(result)}. Put the helper in the layer that needs the web framework rather than adding a Microsoft.AspNetCore.App FrameworkReference to GSS.Util");
    }

    #endregion

    /// <summary>Lists offending types for the failure message.</summary>
    private static string Offenders(TestResult result) =>
        result.FailingTypeNames is { Count: > 0 } failing
            ? string.Join(", ", failing)
            : "(no failing types reported)";
}
