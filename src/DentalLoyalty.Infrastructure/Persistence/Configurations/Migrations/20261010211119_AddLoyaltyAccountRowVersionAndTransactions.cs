using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalLoyalty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoyaltyAccountRowVersionAndTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "LoyaltyAccounts",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "LoyaltyTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    PointsChange = table.Column<int>(type: "int", nullable: false),
                    ReferralChange = table.Column<int>(type: "int", nullable: false),
                    RewardId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferralId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoyaltyTransactions_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LoyaltyTransactions_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LoyaltyTransactions_Referrals_ReferralId",
                        column: x => x.ReferralId,
                        principalTable: "Referrals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LoyaltyTransactions_Rewards_RewardId",
                        column: x => x.RewardId,
                        principalTable: "Rewards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LoyaltyTransactions_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LoyaltyTransactions_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_ClinicId_CreatedAt",
                table: "LoyaltyTransactions",
                columns: new[] { "ClinicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_CreatedBy",
                table: "LoyaltyTransactions",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_PatientId_CreatedAt",
                table: "LoyaltyTransactions",
                columns: new[] { "PatientId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_ReferralId",
                table: "LoyaltyTransactions",
                column: "ReferralId");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_RewardId",
                table: "LoyaltyTransactions",
                column: "RewardId");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_ServiceId",
                table: "LoyaltyTransactions",
                column: "ServiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoyaltyTransactions");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "LoyaltyAccounts");
        }
    }
}
