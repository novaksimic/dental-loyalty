using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalLoyalty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoyaltyConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoyaltyConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SpendAmountRsd = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PointsAwarded = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyConfigurations", x => x.Id);
                    table.CheckConstraint("CK_LoyaltyConfigurations_PointsAwarded_Positive", "[PointsAwarded] > 0");
                    table.CheckConstraint("CK_LoyaltyConfigurations_SpendAmountRsd_Positive", "[SpendAmountRsd] > 0");
                    table.ForeignKey(
                        name: "FK_LoyaltyConfigurations_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyConfigurations_ClinicId",
                table: "LoyaltyConfigurations",
                column: "ClinicId",
                unique: true);

            // Clinics created before this migration get the platform default
            // scoring configuration (BUSINESS.md §3.1: 100 RSD = 1,000 points), so
            // every clinic has exactly one. Literal values on purpose: a migration
            // must keep producing the same data even if the code default changes.
            migrationBuilder.Sql(
                """
                INSERT INTO [LoyaltyConfigurations] ([Id], [ClinicId], [SpendAmountRsd], [PointsAwarded], [CreatedAt], [UpdatedAt])
                SELECT NEWID(), [c].[Id], 100.00, 1000, SYSUTCDATETIME(), NULL
                FROM [Clinics] AS [c]
                WHERE NOT EXISTS (
                    SELECT 1 FROM [LoyaltyConfigurations] AS [lc] WHERE [lc].[ClinicId] = [c].[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoyaltyConfigurations");
        }
    }
}
