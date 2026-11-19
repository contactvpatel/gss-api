using FluentAssertions;
using GSS.Infrastructure.DbUp.Core;
using GSS.UnitTests.Common;
using Xunit;

namespace GSS.UnitTests.Infrastructure.DbUp;

/// <summary>
/// Pending scripts keep their order; a <c>-NoTransaction</c> script (e.g. CREATE INDEX CONCURRENTLY) runs outside a
/// transaction, every other script in its own.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class MigrationBatchesTests
{
    [Fact]
    public void Plan_GroupsConsecutiveScriptsByMode_InOrder()
    {
        string[] pending =
        [
            "20270101-100000-GSS-005-Add-Column.sql",
            "20270101-110000-GSS-006-Backfill.sql",
            "20270101-120000-GSS-007-Orders-Index-NoTransaction.sql",
            "20270101-130000-GSS-008-Products-Index-NoTransaction.sql",
            "20270101-140000-GSS-009-Not-Null.sql"
        ];

        var batches = MigrationBatches.Plan(pending);

        batches.Select(b => b.Transactional).Should().Equal(true, false, true);
        batches.SelectMany(b => b.Scripts).Should().Equal(pending, "batches never reorder scripts");
        batches[1].Scripts.Should().Equal(pending[2], pending[3]);
    }

    [Fact]
    public void Plan_NothingPending_IsEmpty()
    {
        MigrationBatches.Plan([]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("20270101-120000-GSS-007-Orders-Index-NoTransaction.sql", true)]
    [InlineData("20270101-120000-GSS-007-Orders-Index.sql", false)]
    [InlineData("20270101-120000-GSS-007-NoTransaction-Notes.sql", false)]
    [InlineData("20270101-120000-GSS-007-Orders-Index-notransaction.sql", false)]
    public void IsNoTransaction_MatchesTheExactSuffixOnly(string name, bool expected)
    {
        MigrationBatches.IsNoTransaction(name).Should().Be(expected);
    }
}
