using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalLoyalty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillLoyaltyAccountsForExistingPatients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-006 §7: Patients created before accounts were created together
            // with the Patient get a zero-balance account, so every Patient has
            // exactly one. The account's ClinicId is the Patient's own (§3); no
            // Patient data is changed and no LoyaltyTransaction is recorded (§4).
            // No loyalty cycle is started here: a cycle starts only on
            // registration and account linking (BUSINESS.md §5.1). Literal values
            // on purpose, as in AddLoyaltyConfiguration. Re-running inserts nothing,
            // and the unique PatientId index rejects any duplicate.
            migrationBuilder.Sql(
                """
                INSERT INTO [LoyaltyAccounts] ([Id], [ClinicId], [PatientId], [Points], [CreatedAt], [UpdatedAt])
                SELECT NEWID(), [p].[ClinicId], [p].[Id], 0, SYSUTCDATETIME(), NULL
                FROM [Patients] AS [p]
                WHERE NOT EXISTS (
                    SELECT 1 FROM [LoyaltyAccounts] AS [la] WHERE [la].[PatientId] = [p].[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty. Once Up has run, backfilled accounts cannot be
            // told apart from accounts created with their Patient, and deleting
            // either would leave Patients without the account ADR-006 requires.
        }
    }
}
