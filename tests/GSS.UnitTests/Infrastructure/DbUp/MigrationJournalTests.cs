using FluentAssertions;
using GSS.Infrastructure.DbUp.Core;
using GSS.UnitTests.Common;
using Xunit;

namespace GSS.UnitTests.Infrastructure.DbUp;

[Trait("Category", TestCategories.Unit)]
public class MigrationJournalTests
{
    [Fact]
    public void Journal_IsInTheServiceSchema()
    {
        MigrationJournal.QualifiedName.Should().Be("gss.schemaversions");
    }

    /// <summary>Pinned: a changed key lets an old and a new version migrate the same database at once.</summary>
    [Fact]
    public void AdvisoryLockKey_IsStable()
    {
        MigrationJournal.AdvisoryLockKey.Should().Be(5134506130432089607);
        MigrationJournal.DeriveLockKey("gss.schemaversions").Should().Be(MigrationJournal.AdvisoryLockKey);
    }

    [Fact]
    public void AdvisoryLockKey_DiffersPerJournal()
    {
        MigrationJournal.DeriveLockKey("orders.schemaversions").Should().NotBe(MigrationJournal.AdvisoryLockKey);
    }
}
