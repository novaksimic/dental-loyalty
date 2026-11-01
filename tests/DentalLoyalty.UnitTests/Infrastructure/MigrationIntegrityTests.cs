using DentalLoyalty.Infrastructure.Migrations;
using DentalLoyalty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace DentalLoyalty.UnitTests.Infrastructure;

// Guards the schema history the security invariants depend on. None of these
// open a database connection - they inspect the SQL Server model and the
// compiled migrations directly.
public class MigrationIntegrityTests
{
    private static DentalLoyaltyDbContext CreateSqlServerContext()
    {
        var options = new DbContextOptionsBuilder<DentalLoyaltyDbContext>()
            .UseSqlServer("Server=unused;Database=unused")
            .Options;

        return new DentalLoyaltyDbContext(options);
    }

    [Fact]
    public void MigrationHistory_IsExactlyTheKnownSequence()
    {
        // Fails if an applied migration is removed, renamed or recreated (new
        // id), or if one is inserted out of order. Append new migrations here.
        using var context = CreateSqlServerContext();

        Assert.Equal(
            new[]
            {
                "20260911172821_InitialCreate",
                "20260921142459_EfCorePersistenceHardening",
                "20260921202042_AddUserAndPatientUserLink",
                "20261006210645_EnforceUniquePatientUserId",
                "20261010201504_AddLoyaltyConfiguration",
                "20261010211119_AddLoyaltyAccountRowVersionAndTransactions",
                "20261018115403_AddLoyaltyCycles",
                "20261018115429_BackfillLoyaltyAccountsForExistingPatients",
                "20261018133238_AddPatientServiceRecordingAndTransactionLink",
                "20261028215934_AddRewardRedemptionCycleLinkAndReferralBalance",
                "20261031215040_AddReferralValidation",
                "20261031222038_AddLoyaltyCycleLifecycle",
            },
            context.Database.GetMigrations());
    }

    [Fact]
    public void AddLoyaltyCycles_Up_CreatesTableWithRestrictedForeignKeys_AndOneActiveCyclePerPatient()
    {
        var operations = new AddLoyaltyCycles().UpOperations;

        var create = Assert.IsType<CreateTableOperation>(operations[0]);
        Assert.Equal("LoyaltyCycles", create.Name);
        Assert.Equal(
            new[] { "ClinicId", "Id", "PatientId", "StartedAt", "Status" },
            create.Columns.Select(c => c.Name).OrderBy(n => n));
        Assert.All(create.Columns, c => Assert.False(c.IsNullable));

        // Cycles are history: no cascade can remove them (BUSINESS.md §5.4).
        Assert.Equal(
            new[] { ("ClinicId", "Clinics"), ("PatientId", "Patients") },
            create.ForeignKeys.Select(fk => (fk.Columns.Single(), fk.PrincipalTable)).OrderBy(fk => fk.Item1));
        Assert.All(create.ForeignKeys, fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));

        var indexes = operations.OfType<CreateIndexOperation>().ToList();
        var activePerPatient = Assert.Single(indexes, i => i.Name == "IX_LoyaltyCycles_PatientId");
        Assert.Equal(new[] { "PatientId" }, activePerPatient.Columns);
        Assert.True(activePerPatient.IsUnique);
        Assert.Equal("[Status] = 1", activePerPatient.Filter);
        Assert.Contains(indexes, i => i.Name == "IX_LoyaltyCycles_ClinicId" && !i.IsUnique);

        // Purely additive: no existing table, column or data is changed, and no
        // cycle is created for existing patients.
        Assert.DoesNotContain(operations, op => op is DropTableOperation or DropColumnOperation or AlterColumnOperation or SqlOperation);
    }

    [Fact]
    public void AddLoyaltyCycles_Down_DropsOnlyTheTable()
    {
        var operations = new AddLoyaltyCycles().DownOperations;

        var drop = Assert.IsType<DropTableOperation>(Assert.Single(operations));
        Assert.Equal("LoyaltyCycles", drop.Name);
    }

    [Fact]
    public void BackfillLoyaltyAccountsForExistingPatients_Up_InsertsZeroBalanceAccountOnlyForPatientsWithout()
    {
        var operations = new BackfillLoyaltyAccountsForExistingPatients().UpOperations;

        // ADR-006 §7: data only - one zero-balance account per Patient that has
        // none, in the Patient's own clinic, without changing any Patient data.
        var sql = Assert.IsType<SqlOperation>(Assert.Single(operations)).Sql;
        Assert.Contains("INSERT INTO [LoyaltyAccounts]", sql);
        Assert.Contains("SELECT NEWID(), [p].[ClinicId], [p].[Id], 0, SYSUTCDATETIME(), NULL", sql);
        Assert.Contains("FROM [Patients] AS [p]", sql);
        Assert.Contains("WHERE NOT EXISTS", sql);
        Assert.Contains("[la].[PatientId] = [p].[Id]", sql);
        Assert.DoesNotMatch(@"(?i)\b(UPDATE|DELETE|MERGE)\s", sql);
        Assert.DoesNotContain("LoyaltyTransactions", sql);
        Assert.DoesNotContain("LoyaltyCycles", sql);
    }

    [Fact]
    public void AddPatientServiceRecordingAndTransactionLink_Up_LinksTransactionsToOneServiceOccurrence()
    {
        var operations = new AddPatientServiceRecordingAndTransactionLink().UpOperations;

        // One earning transaction per service occurrence at most: a unique index,
        // filtered so transactions that are not about a service stay allowed.
        var linkColumn = Assert.Single(operations.OfType<AddColumnOperation>(), c => c.Name == "PatientServiceId");
        Assert.Equal("LoyaltyTransactions", linkColumn.Table);
        Assert.True(linkColumn.IsNullable);

        var unique = Assert.Single(operations.OfType<CreateIndexOperation>(), i => i.Name == "IX_LoyaltyTransactions_PatientServiceId");
        Assert.Equal(new[] { "PatientServiceId" }, unique.Columns);
        Assert.True(unique.IsUnique);
        Assert.Equal("[PatientServiceId] IS NOT NULL", unique.Filter);

        var foreignKeys = operations.OfType<AddForeignKeyOperation>().ToList();
        Assert.Contains(foreignKeys, fk => fk.Table == "LoyaltyTransactions" && fk.Columns.Single() == "PatientServiceId" && fk.PrincipalTable == "PatientServices");
        Assert.Contains(foreignKeys, fk => fk.Table == "PatientServices" && fk.Columns.Single() == "RecordedBy" && fk.PrincipalTable == "Users");
        Assert.All(foreignKeys, fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));

        // When and by whom a service was recorded, separately from when it was performed.
        var recordedAt = Assert.Single(operations.OfType<AddColumnOperation>(), c => c.Name == "RecordedAt");
        Assert.Equal("PatientServices", recordedAt.Table);
        Assert.False(recordedAt.IsNullable);
        Assert.True(Assert.Single(operations.OfType<AddColumnOperation>(), c => c.Name == "RecordedBy").IsNullable);

        // Existing services were recorded when they were stored as performed.
        var sql = Assert.IsType<SqlOperation>(Assert.Single(operations.OfType<SqlOperation>())).Sql;
        Assert.Contains("UPDATE [PatientServices] SET [RecordedAt] = [UsedAt];", sql);

        // Purely additive otherwise: nothing is dropped or altered.
        Assert.DoesNotContain(operations, op => op is DropTableOperation or DropColumnOperation or AlterColumnOperation);
    }

    [Fact]
    public void AddRewardRedemptionCycleLinkAndReferralBalance_Up_LinksRedemptionsToOneCyclePerReward_AndStartsReferralBalancesAtZero()
    {
        var operations = new AddRewardRedemptionCycleLinkAndReferralBalance().UpOperations;

        // Refuses to run over existing redemptions rather than inventing their
        // cycle and confirming admin - before anything is changed.
        var guard = Assert.IsType<SqlOperation>(operations[0]);
        Assert.Contains("IF EXISTS (SELECT 1 FROM [RewardRedemptions])", guard.Sql);
        Assert.Contains("THROW", guard.Sql);

        // ADR-004: every existing account starts with a referral balance of 0.
        var referralBalance = Assert.Single(operations.OfType<AddColumnOperation>(), c => c.Name == "ReferralBalance");
        Assert.Equal("LoyaltyAccounts", referralBalance.Table);
        Assert.False(referralBalance.IsNullable);
        Assert.Equal(0, referralBalance.DefaultValue);

        // A reward is redeemed at most once per cycle.
        var unique = Assert.Single(operations.OfType<CreateIndexOperation>(), i => i.Name == "IX_RewardRedemptions_LoyaltyCycleId_RewardId");
        Assert.Equal(new[] { "LoyaltyCycleId", "RewardId" }, unique.Columns);
        Assert.True(unique.IsUnique);
        Assert.Null(unique.Filter);

        var foreignKeys = operations.OfType<AddForeignKeyOperation>().ToList();
        Assert.Contains(foreignKeys, fk => fk.Columns.Single() == "LoyaltyCycleId" && fk.PrincipalTable == "LoyaltyCycles");
        Assert.Contains(foreignKeys, fk => fk.Columns.Single() == "RedeemedBy" && fk.PrincipalTable == "Users");
        Assert.All(foreignKeys, fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));

        // BUSINESS.md §13: a redemption spends nothing, so these columns go.
        Assert.Equal(
            new[] { "PointsSpent", "ReferralsUsed" },
            operations.OfType<DropColumnOperation>().Select(c => c.Name).Order());
        Assert.All(operations.OfType<DropColumnOperation>(), c => Assert.Equal("RewardRedemptions", c.Table));
        Assert.DoesNotContain(operations, op => op is DropTableOperation or AlterColumnOperation);
    }

    [Fact]
    public void AddReferralValidation_Up_RefusesExistingReferrals_AndMakesEachReferralAndRewardUnique()
    {
        var operations = new AddReferralValidation().UpOperations;

        // Refuses to run over existing referrals rather than inventing who
        // recorded them - before anything is changed.
        var guard = Assert.IsType<SqlOperation>(operations[0]);
        Assert.Contains("IF EXISTS (SELECT 1 FROM [Referrals])", guard.Sql);
        Assert.Contains("THROW", guard.Sql);

        // A patient is referred at most once; a cancelled referral does not count.
        var referred = Assert.Single(operations.OfType<CreateIndexOperation>(), i => i.Name == "IX_Referrals_ReferredPatientId");
        Assert.True(referred.IsUnique);
        Assert.Equal("[Status] <> 3", referred.Filter);

        // A referral rewards each of its patients at most once.
        var reward = Assert.Single(operations.OfType<CreateIndexOperation>(), i => i.Name == "IX_LoyaltyTransactions_ReferralId_PatientId");
        Assert.Equal(new[] { "ReferralId", "PatientId" }, reward.Columns);
        Assert.True(reward.IsUnique);
        Assert.Equal("[ReferralId] IS NOT NULL", reward.Filter);

        var rowVersion = Assert.Single(operations.OfType<AddColumnOperation>(), c => c.Name == "RowVersion");
        Assert.Equal("Referrals", rowVersion.Table);
        Assert.True(rowVersion.IsRowVersion);

        var foreignKeys = operations.OfType<AddForeignKeyOperation>().ToList();
        Assert.Equal(new[] { "CreatedBy", "ValidatedBy" }, foreignKeys.Select(fk => fk.Columns.Single()).Order());
        Assert.All(foreignKeys, fk =>
        {
            Assert.Equal("Users", fk.PrincipalTable);
            Assert.Equal(ReferentialAction.Restrict, fk.OnDelete);
        });

        // Additive apart from replacing the two non-unique indexes.
        Assert.Equal(
            new[] { "IX_LoyaltyTransactions_ReferralId", "IX_Referrals_ReferredPatientId" },
            operations.OfType<DropIndexOperation>().Select(i => i.Name).Order());
        Assert.DoesNotContain(operations, op => op is DropTableOperation or DropColumnOperation or AlterColumnOperation);
    }

    [Fact]
    public void AddLoyaltyCycleLifecycle_Up_IsAdditive_AndAllowsOneResetPerCycle()
    {
        var operations = new AddLoyaltyCycleLifecycle().UpOperations;

        var endedAt = Assert.Single(operations.OfType<AddColumnOperation>(), c => c.Name == "EndedAt");
        Assert.Equal("LoyaltyCycles", endedAt.Table);
        Assert.True(endedAt.IsNullable);

        // A cycle is ended - and its progress reset - at most once.
        var reset = Assert.Single(operations.OfType<CreateIndexOperation>(), i => i.Name == "IX_LoyaltyTransactions_LoyaltyCycleId");
        Assert.True(reset.IsUnique);
        Assert.Equal("[LoyaltyCycleId] IS NOT NULL", reset.Filter);

        var foreignKey = Assert.Single(operations.OfType<AddForeignKeyOperation>());
        Assert.Equal(("LoyaltyCycleId", "LoyaltyCycles", ReferentialAction.Restrict), (foreignKey.Columns.Single(), foreignKey.PrincipalTable, foreignKey.OnDelete));

        // Existing cycles stay active with no end; nothing is dropped or altered.
        Assert.DoesNotContain(operations, op => op is DropTableOperation or DropColumnOperation or AlterColumnOperation or DropIndexOperation or SqlOperation);
    }

    [Fact]
    public void BackfillLoyaltyAccountsForExistingPatients_Down_RemovesNothing()
    {
        Assert.Empty(new BackfillLoyaltyAccountsForExistingPatients().DownOperations);
    }

    [Fact]
    public void Model_HasNoPendingChanges_AgainstLatestMigrationSnapshot()
    {
        // A configuration change (e.g. dropping the UserId unique index) without
        // a matching migration would make the database diverge from the model.
        using var context = CreateSqlServerContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void EnforceUniquePatientUserId_Up_ReplacesNonUniqueIndexWithUniqueFilteredIndex()
    {
        var operations = new EnforceUniquePatientUserId().UpOperations;

        Assert.Collection(
            operations,
            op =>
            {
                var drop = Assert.IsType<DropIndexOperation>(op);
                Assert.Equal("IX_Patients_UserId", drop.Name);
                Assert.Equal("Patients", drop.Table);
            },
            op =>
            {
                var create = Assert.IsType<CreateIndexOperation>(op);
                Assert.Equal("IX_Patients_UserId", create.Name);
                Assert.Equal("Patients", create.Table);
                Assert.Equal(new[] { "UserId" }, create.Columns);
                Assert.True(create.IsUnique);
                Assert.Equal("[UserId] IS NOT NULL", create.Filter);
            });
    }

    [Fact]
    public void AddLoyaltyConfiguration_Up_CreatesConstrainedTable_UniqueClinicIndex_AndBackfillsDefaults()
    {
        var operations = new AddLoyaltyConfiguration().UpOperations;

        Assert.Collection(
            operations,
            op =>
            {
                var create = Assert.IsType<CreateTableOperation>(op);
                Assert.Equal("LoyaltyConfigurations", create.Name);

                var spendAmount = Assert.Single(create.Columns, c => c.Name == "SpendAmountRsd");
                Assert.Equal("decimal(18,2)", spendAmount.ColumnType);
                Assert.False(spendAmount.IsNullable);
                Assert.False(Assert.Single(create.Columns, c => c.Name == "PointsAwarded").IsNullable);
                Assert.False(Assert.Single(create.Columns, c => c.Name == "ClinicId").IsNullable);

                Assert.Equal(
                    new[]
                    {
                        ("CK_LoyaltyConfigurations_PointsAwarded_Positive", "[PointsAwarded] > 0"),
                        ("CK_LoyaltyConfigurations_SpendAmountRsd_Positive", "[SpendAmountRsd] > 0"),
                    },
                    create.CheckConstraints.Select(c => (c.Name, c.Sql)).OrderBy(c => c.Name));

                var foreignKey = Assert.Single(create.ForeignKeys);
                Assert.Equal("Clinics", foreignKey.PrincipalTable);
                Assert.Equal(new[] { "ClinicId" }, foreignKey.Columns);
                Assert.Equal(ReferentialAction.Restrict, foreignKey.OnDelete);
            },
            op =>
            {
                var index = Assert.IsType<CreateIndexOperation>(op);
                Assert.Equal("IX_LoyaltyConfigurations_ClinicId", index.Name);
                Assert.Equal(new[] { "ClinicId" }, index.Columns);
                Assert.True(index.IsUnique);
                Assert.Null(index.Filter);
            },
            op =>
            {
                // Existing clinics get the platform default, 100 RSD = 1,000 points.
                var sql = Assert.IsType<SqlOperation>(op).Sql;
                Assert.Contains("INSERT INTO [LoyaltyConfigurations]", sql);
                Assert.Contains("100.00, 1000", sql);
                Assert.Contains("FROM [Clinics]", sql);
                Assert.Contains("WHERE NOT EXISTS", sql);
            });
    }

    [Fact]
    public void AddLoyaltyConfiguration_Down_DropsTheTable()
    {
        var operations = new AddLoyaltyConfiguration().DownOperations;

        var drop = Assert.IsType<DropTableOperation>(Assert.Single(operations));
        Assert.Equal("LoyaltyConfigurations", drop.Name);
    }

    [Fact]
    public void AddLoyaltyAccountRowVersionAndTransactions_Up_AddsRowVersionAndAppendOnlyHistoryTable()
    {
        var operations = new AddLoyaltyAccountRowVersionAndTransactions().UpOperations;

        // ADR-005: rowversion concurrency token on the existing accounts table.
        var addColumn = Assert.IsType<AddColumnOperation>(operations[0]);
        Assert.Equal("LoyaltyAccounts", addColumn.Table);
        Assert.Equal("RowVersion", addColumn.Name);
        Assert.Equal("rowversion", addColumn.ColumnType);
        Assert.True(addColumn.IsRowVersion);
        Assert.False(addColumn.IsNullable);

        var create = Assert.IsType<CreateTableOperation>(operations[1]);
        Assert.Equal("LoyaltyTransactions", create.Name);

        // Every relationship is Restrict, so history never disappears through a cascade.
        Assert.Equal(
            new[]
            {
                ("ClinicId", "Clinics"),
                ("CreatedBy", "Users"),
                ("PatientId", "Patients"),
                ("ReferralId", "Referrals"),
                ("RewardId", "Rewards"),
                ("ServiceId", "Services"),
            },
            create.ForeignKeys.Select(fk => (fk.Columns.Single(), fk.PrincipalTable)).OrderBy(fk => fk.Item1));
        Assert.All(create.ForeignKeys, fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));

        Assert.False(create.Columns.Single(c => c.Name == "ClinicId").IsNullable);
        Assert.False(create.Columns.Single(c => c.Name == "PatientId").IsNullable);
        Assert.True(create.Columns.Single(c => c.Name == "ServiceId").IsNullable);
        Assert.True(create.Columns.Single(c => c.Name == "RewardId").IsNullable);
        Assert.True(create.Columns.Single(c => c.Name == "ReferralId").IsNullable);
        Assert.True(create.Columns.Single(c => c.Name == "CreatedBy").IsNullable);

        var indexes = operations.OfType<CreateIndexOperation>().ToList();
        Assert.Contains(indexes, i => i.Name == "IX_LoyaltyTransactions_PatientId_CreatedAt" && i.Columns.SequenceEqual(new[] { "PatientId", "CreatedAt" }));
        Assert.Contains(indexes, i => i.Name == "IX_LoyaltyTransactions_ClinicId_CreatedAt" && i.Columns.SequenceEqual(new[] { "ClinicId", "CreatedAt" }));
        Assert.All(indexes, i => Assert.False(i.IsUnique));

        // Purely additive: no existing table, column or data is dropped or changed.
        Assert.DoesNotContain(operations, op => op is DropTableOperation or DropColumnOperation or AlterColumnOperation or SqlOperation);
    }

    [Fact]
    public void AddLoyaltyAccountRowVersionAndTransactions_Down_RemovesOnlyWhatUpAdded()
    {
        var operations = new AddLoyaltyAccountRowVersionAndTransactions().DownOperations;

        Assert.Collection(
            operations,
            op => Assert.Equal("LoyaltyTransactions", Assert.IsType<DropTableOperation>(op).Name),
            op =>
            {
                var drop = Assert.IsType<DropColumnOperation>(op);
                Assert.Equal("LoyaltyAccounts", drop.Table);
                Assert.Equal("RowVersion", drop.Name);
            });
    }

    [Fact]
    public void SqlServerModel_LoyaltyAccountRowVersion_IsDatabaseGeneratedConcurrencyToken()
    {
        using var context = CreateSqlServerContext();

        var rowVersion = context.Model
            .FindEntityType(typeof(DentalLoyalty.Domain.Entities.LoyaltyAccount))!
            .FindProperty(nameof(DentalLoyalty.Domain.Entities.LoyaltyAccount.RowVersion))!;

        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
        Assert.Equal("rowversion", rowVersion.GetColumnType());

        var script = context.Database.GenerateCreateScript();
        Assert.Contains("[RowVersion] rowversion NOT NULL", script);
        Assert.Contains("CREATE INDEX [IX_LoyaltyTransactions_PatientId_CreatedAt] ON [LoyaltyTransactions] ([PatientId], [CreatedAt]);", script);
        Assert.Contains("CREATE INDEX [IX_LoyaltyTransactions_ClinicId_CreatedAt] ON [LoyaltyTransactions] ([ClinicId], [CreatedAt]);", script);
    }

    [Fact]
    public void EnforceUniquePatientUserId_Down_RestoresOriginalNonUniqueIndex()
    {
        var operations = new EnforceUniquePatientUserId().DownOperations;

        Assert.Collection(
            operations,
            op => Assert.Equal("IX_Patients_UserId", Assert.IsType<DropIndexOperation>(op).Name),
            op =>
            {
                var create = Assert.IsType<CreateIndexOperation>(op);
                Assert.Equal("IX_Patients_UserId", create.Name);
                Assert.Equal(new[] { "UserId" }, create.Columns);
                Assert.False(create.IsUnique);
                Assert.Null(create.Filter);
            });
    }
}
