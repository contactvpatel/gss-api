using GSS.Core.Entities;
using GSS.Core.Interfaces.Base;
using GSS.Core.Interfaces.Services;
using GSS.Infrastructure.Helpers;

namespace GSS.ArchitectureTests.Fixtures.ApiLayer;

// Negative fixtures for ApiLayerDependencyTests: each type takes one dependency Api code must not use.

internal sealed class GenericRepositoryController(IRepository<FixtureEntity> repository)
{
    public object Dependency => repository;
}

internal sealed class UnitOfWorkController(IUnitOfWork unitOfWork)
{
    public object Dependency => unitOfWork;
}

internal sealed class CoreExternalServiceController(ISsoService ssoService)
{
    public object Dependency => ssoService;
}

internal sealed class InfrastructureController(DapperTransactionManager transactionManager)
{
    public object Dependency => transactionManager;
}

internal sealed class UserContextController(IUserContext userContext)
{
    public object Dependency => userContext;
}

internal sealed class FixtureEntity : EntityBase;
