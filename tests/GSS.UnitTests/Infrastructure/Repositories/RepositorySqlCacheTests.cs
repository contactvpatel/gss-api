using FluentAssertions;
using GSS.Core.Interfaces.Base;
using GSS.Infrastructure.Interfaces;
using GSS.Infrastructure.Repositories.Base;
using GSS.UnitTests.Common;
using GSS.UnitTests.Common.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GSS.UnitTests.Infrastructure.Repositories;

[Trait("Category", TestCategories.Unit)]
public class RepositorySqlCacheTests
{
    [Fact]
    public void Sql_IsBuiltOncePerConcreteRepositoryType()
    {
        CountingRepository first = new();
        CountingRepository second = new();

        object firstSql = first.SharedSql;
        object secondSql = second.SharedSql;

        secondSql.Should().BeSameAs(firstSql);
        CountingRepository.SelectSqlReads.Should().Be(1);
        CountingRepository.BatchColumnsReads.Should().Be(1);
    }

    private sealed class CountingRepository()
        : DapperRepositoryBase<TestEntity>(
            new Mock<IDapperConnectionFactory>().Object,
            new Mock<IDapperTransactionManager>().Object,
            NullLoggerFactory.Instance,
            TimeProvider.System,
            new Mock<IUserContext>().Object)
    {
        public static int SelectSqlReads;
        public static int BatchColumnsReads;

        public object SharedSql => Sql;

        protected override string TableName => "counting_entities";

        protected override string SelectSql
        {
            get
            {
                Interlocked.Increment(ref SelectSqlReads);
                return "SELECT id FROM gss.counting_entities";
            }
        }

        protected override IReadOnlyList<BatchColumn> BatchColumns
        {
            get
            {
                Interlocked.Increment(ref BatchColumnsReads);
                return [Column("name", entity => entity.Name)];
            }
        }

        protected override string BuildInsertSql() => "INSERT";

        protected override string BuildUpdateSql() => "UPDATE";
    }
}
