using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalLoyalty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoyaltyCycleLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LoyaltyCycleId",
                table: "LoyaltyTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndedAt",
                table: "LoyaltyCycles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_LoyaltyCycleId",
                table: "LoyaltyTransactions",
                column: "LoyaltyCycleId",
                unique: true,
                filter: "[LoyaltyCycleId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_LoyaltyTransactions_LoyaltyCycles_LoyaltyCycleId",
                table: "LoyaltyTransactions",
                column: "LoyaltyCycleId",
                principalTable: "LoyaltyCycles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LoyaltyTransactions_LoyaltyCycles_LoyaltyCycleId",
                table: "LoyaltyTransactions");

            migrationBuilder.DropIndex(
                name: "IX_LoyaltyTransactions_LoyaltyCycleId",
                table: "LoyaltyTransactions");

            migrationBuilder.DropColumn(
                name: "LoyaltyCycleId",
                table: "LoyaltyTransactions");

            migrationBuilder.DropColumn(
                name: "EndedAt",
                table: "LoyaltyCycles");
        }
    }
}
