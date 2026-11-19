using Xunit;

namespace GSS.IntegrationTests.Fixtures;

/// <summary>
/// Collection for tests sharing the integration database; runs them sequentially.
/// </summary>
[CollectionDefinition("IntegrationDatabase")]
public class IntegrationDatabaseCollection : ICollectionFixture<TestDatabaseFixture>, ICollectionFixture<WebAppFactory>
{
}
