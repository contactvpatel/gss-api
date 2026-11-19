using System.Reflection;
using FluentAssertions;
using GSS.Core.Interfaces.Base;
using GSS.Infrastructure;
using GSS.Infrastructure.Helpers;
using GSS.Infrastructure.Interfaces;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>Transactions are async-only: no blocking Begin/Commit/Rollback, and disposal is <see cref="IAsyncDisposable"/>.</summary>
[Trait("Category", TestCategories.Unit)]
public class TransactionApiTests
{
    public static TheoryData<Type> TransactionTypes => new() { typeof(IUnitOfWork), typeof(IDapperTransactionManager) };

    [Theory]
    [MemberData(nameof(TransactionTypes))]
    public void TransactionInterfaces_ExposeOnlyAsyncOperations(Type type)
    {
        string[] blocking = [.. type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            // GetCurrentTransaction and EnterCommand do no I/O.
            .Where(m => m.Name is not ("GetCurrentTransaction" or "EnterCommand") && !typeof(Task).IsAssignableFrom(m.ReturnType))
            .Select(m => m.Name)];

        blocking.Should().BeEmpty(because: "transaction I/O must not block a request thread");
    }

    public static TheoryData<Type> DisposableTypes => new()
    {
        typeof(IUnitOfWork), typeof(IDapperTransactionManager), typeof(UnitOfWork), typeof(DapperTransactionManager)
    };

    [Theory]
    [MemberData(nameof(DisposableTypes))]
    public void TransactionTypes_AreAsyncDisposableOnly(Type type)
    {
        type.Should().Implement<IAsyncDisposable>();
        type.Should().NotImplement<IDisposable>(because: "a sync Dispose would roll back with blocking I/O");
    }
}
