using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalLoyalty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRewardRedemptionCycleLinkAndReferralBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No code has ever written a reward redemption, so the table is empty.
            // A row could only have been inserted by hand; it would have no cycle or
            // confirming admin to link to, and its points/referrals "spent" would be
            // discarded. Stop rather than invent those values or lose data.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [RewardRedemptions])
                    THROW 50000, 'RewardRedemptions contains rows; they must be reviewed before this migration can link redemptions to loyalty cycles.', 1;
                """);

            migrationBuilder.DropColumn(
                name: "PointsSpent",
                table: "RewardRedemptions");

            migrationBuilder.DropColumn(
                name: "ReferralsUsed",
                table: "RewardRedemptions");

            migrationBuilder.AddColumn<Guid>(
                name: "LoyaltyCycleId",
                table: "RewardRedemptions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "RedeemedBy",
                table: "RewardRedemptions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // ADR-004 / ADR-006: the referral balance every existing account starts
            // with. Nothing has ever credited a referral (no transaction has a
            // non-zero ReferralChange), so 0 is every account's actual balance.
            migrationBuilder.AddColumn<int>(
                name: "ReferralBalance",
                table: "LoyaltyAccounts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_RewardRedemptions_LoyaltyCycleId_RewardId",
                table: "RewardRedemptions",
                columns: new[] { "LoyaltyCycleId", "RewardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RewardRedemptions_RedeemedBy",
                table: "RewardRedemptions",
                column: "RedeemedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_RewardRedemptions_LoyaltyCycles_LoyaltyCycleId",
                table: "RewardRedemptions",
                column: "LoyaltyCycleId",
                principalTable: "LoyaltyCycles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RewardRedemptions_Users_RedeemedBy",
                table: "RewardRedemptions",
                column: "RedeemedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RewardRedemptions_LoyaltyCycles_LoyaltyCycleId",
                table: "RewardRedemptions");

            migrationBuilder.DropForeignKey(
                name: "FK_RewardRedemptions_Users_RedeemedBy",
                table: "RewardRedemptions");

            migrationBuilder.DropIndex(
                name: "IX_RewardRedemptions_LoyaltyCycleId_RewardId",
                table: "RewardRedemptions");

            migrationBuilder.DropIndex(
                name: "IX_RewardRedemptions_RedeemedBy",
                table: "RewardRedemptions");

            migrationBuilder.DropColumn(
                name: "LoyaltyCycleId",
                table: "RewardRedemptions");

            migrationBuilder.DropColumn(
                name: "RedeemedBy",
                table: "RewardRedemptions");

            migrationBuilder.DropColumn(
                name: "ReferralBalance",
                table: "LoyaltyAccounts");

            migrationBuilder.AddColumn<int>(
                name: "PointsSpent",
                table: "RewardRedemptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReferralsUsed",
                table: "RewardRedemptions",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
