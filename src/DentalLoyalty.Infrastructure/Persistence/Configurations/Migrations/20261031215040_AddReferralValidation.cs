using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalLoyalty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No code has ever written a referral, so the table is empty. A row
            // could only have been inserted by hand; it would have no recording
            // Clinic Admin to link to, and its status would not say whether its
            // rewards were applied. Stop rather than invent those values.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [Referrals])
                    THROW 50000, 'Referrals contains rows; they must be reviewed before this migration can record who created and validated referrals.', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Referrals_ReferredPatientId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_LoyaltyTransactions_ReferralId",
                table: "LoyaltyTransactions");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                table: "Referrals",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "ExaminedAt",
                table: "Referrals",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RewardsForfeited",
                table: "Referrals",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Referrals",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "ValidatedBy",
                table: "Referrals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_CreatedBy",
                table: "Referrals",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ReferredPatientId",
                table: "Referrals",
                column: "ReferredPatientId",
                unique: true,
                filter: "[Status] <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ValidatedBy",
                table: "Referrals",
                column: "ValidatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_ReferralId_PatientId",
                table: "LoyaltyTransactions",
                columns: new[] { "ReferralId", "PatientId" },
                unique: true,
                filter: "[ReferralId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_Users_CreatedBy",
                table: "Referrals",
                column: "CreatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_Users_ValidatedBy",
                table: "Referrals",
                column: "ValidatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_Users_CreatedBy",
                table: "Referrals");

            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_Users_ValidatedBy",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_CreatedBy",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_ReferredPatientId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_ValidatedBy",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_LoyaltyTransactions_ReferralId_PatientId",
                table: "LoyaltyTransactions");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Referrals");

            migrationBuilder.DropColumn(
                name: "ExaminedAt",
                table: "Referrals");

            migrationBuilder.DropColumn(
                name: "RewardsForfeited",
                table: "Referrals");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Referrals");

            migrationBuilder.DropColumn(
                name: "ValidatedBy",
                table: "Referrals");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ReferredPatientId",
                table: "Referrals",
                column: "ReferredPatientId");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_ReferralId",
                table: "LoyaltyTransactions",
                column: "ReferralId");
        }
    }
}
